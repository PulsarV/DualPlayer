using DualPlayer.Core.Playback;
using DualPlayer.Core.Tests.Fakes;

namespace DualPlayer.Core.Tests.Playback;

/// <summary>
/// T88 (review S1, S8, S9): the neighbour scan counts its 3 s budget from when the session starts the command; a file is
/// remembered as timed out only when its own listing or probe ran its full, uncut limit; a remembered file is keyed by
/// path + size + mtime and is retried last, so one that recovers plays; a judged file keeps its verdict and reason.
/// </summary>
public sealed class PlaybackSessionNeighbourMemoryTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("dualplayer-memory-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private string File(string name)
    {
        var path = Path.Combine(_folder, name);
        System.IO.File.WriteAllBytes(path, []);
        return path;
    }

    private (SessionRig Rig, ScriptedProbe Probe) PlayingRig(string first = "Episode-01.mkv")
    {
        var probe = new ScriptedProbe();
        var rig = new SessionRig([new RigDevice(48_000), new RigDevice(48_000)], null, 600, neighbourPictureProbe: probe, barrierEvery: NeighbourRig.BarrierEvery);
        probe.Time = rig.Engine.Time;
        rig.Engine.Call(() => rig.Session.Start(rig.Request(2) with { File = File(first) })).IsSuccess.ShouldBeTrue();
        rig.Engine.Render(300);
        return (rig, probe);
    }

    // A press that relies on the workers of the one before it having ended (a cut check is checked again only once its worker
    // has ended) waits for them first, by the session's worker map, not by a real-time sleep.
    private static void Press(SessionRig rig)
    {
        rig.Engine.Call(() => rig.Session.NextFile());
        RigLoops.WaitForNeighbourWorkers(rig.Session);
    }

    [Fact]
    public void A_healthy_neighbour_whose_check_the_press_budget_cut_is_checked_again_and_plays_on_the_next_press_AC_08()
    {
        var (rig, probe) = PlayingRig();
        using var _ = rig;
        File("Episode-02.mkv");
        var healthy = File("Episode-03.mkv");
        File("Episode-04.mkv");
        probe.Hang("Episode-02.mkv"); // E2 hangs its full 2 s; E3 is left 1 s of the press, but needs 1.5 s
        probe.Slow("Episode-03.mkv", TimeSpan.FromMilliseconds(1500));

        Press(rig);
        rig.Events.OfType<FileChanged>().ShouldBeEmpty("the first press cannot judge E3 in the budget it had left");
        rig.Events.OfType<FileSkipped>().Select(e => e.File).ShouldNotContain("Episode-03.mkv", "a healthy file cut by the budget was reported as skipped");

        rig.Engine.Call(() => rig.Session.NextFile());

        rig.Events.OfType<FileChanged>().ShouldHaveSingleItem("the second press did not play the next file").File.ShouldBe(healthy, "a healthy file the budget cut was remembered as timed out and stepped over");
        rig.Events.OfType<FileSkipped>().Select(e => e.File).ShouldNotContain("Episode-03.mkv");
    }

    [Fact]
    public void A_press_that_waited_behind_a_slow_file_change_still_checks_files_and_plays_the_next_one_AC_07()
    {
        var (rig, _) = PlayingRig();
        using var _ = rig;
        var next = File("Episode-02.mkv");
        var keyAt = rig.Engine.Time.GetTimestamp();
        rig.Engine.Render(400); // the key waits 4 s behind a file change, longer than the whole 3 s scan budget

        rig.Engine.Call(() => rig.Session.NextFile(keyAt)).IsSuccess.ShouldBeTrue();

        rig.Events.OfType<NeighbourSearchTimedOut>().ShouldBeEmpty("the scan budget was counted from the key's arrival, so nothing was checked");
        rig.Events.OfType<NoteShown>().Select(e => e.Text).ShouldNotContain(t => t.StartsWith("Next file not found within 3 s", StringComparison.Ordinal));
        rig.Events.OfType<FileChanged>().ShouldHaveSingleItem().File.ShouldBe(next);
    }

    [Fact]
    public void A_remembered_file_that_answers_in_time_on_a_later_press_plays_AC_07()
    {
        var (rig, probe) = PlayingRig();
        using var _ = rig;
        var only = File("Episode-02.mkv");
        probe.Hang("Episode-02.mkv"); // its own full limit runs out: remembered

        Press(rig);
        rig.Events.OfType<FileChanged>().ShouldBeEmpty();
        probe.Ok("Episode-02.mkv"); // the disk woke up

        rig.Engine.Call(() => rig.Session.NextFile());

        rig.Events.OfType<FileChanged>().ShouldHaveSingleItem("a file that recovered was never given another chance").File.ShouldBe(only);
    }

    [Fact]
    public void A_remembered_file_with_a_new_modification_time_is_checked_in_its_own_place_not_last_AC_07()
    {
        var (rig, probe) = PlayingRig();
        using var _ = rig;
        var second = File("Episode-02.mkv");
        File("Episode-03.mkv");
        probe.Hang("Episode-02.mkv");
        probe.Hang("Episode-03.mkv");

        Press(rig); // E2 times out on its own, E3 is cut by the budget
        rig.Events.OfType<FileChanged>().ShouldBeEmpty();

        System.IO.File.SetLastWriteTimeUtc(second, DateTime.UtcNow.AddHours(1)); // the copy finished: a different file now
        probe.Ok("Episode-02.mkv");
        probe.Probed.Clear();

        rig.Engine.Call(() => rig.Session.NextFile());

        rig.Events.OfType<FileChanged>().ShouldHaveSingleItem().File.ShouldBe(second);
        probe.Probed.Select(Path.GetFileName).ShouldBe(["Episode-02.mkv"], "a changed file was still treated as the remembered one and checked last");
    }

    [Fact]
    public void A_file_skipped_for_its_picture_keeps_that_reason_and_is_not_probed_again_on_the_next_press_AC_08()
    {
        var (rig, probe) = PlayingRig();
        using var _ = rig;
        File("Episode-02.mkv");
        File("Episode-03.mkv");
        probe.NotHardwareDecodableAfterLimit("Episode-02.mkv"); // ran its whole 2 s limit, then judged: not decodable
        probe.Hang("Episode-03.mkv"); // cut by the 1 s left of the press

        Press(rig);
        rig.Events.OfType<FileChanged>().ShouldBeEmpty();
        probe.Probed.Clear();

        rig.Engine.Call(() => rig.Session.NextFile());

        probe.Probed.Select(Path.GetFileName).ShouldNotContain("Episode-02.mkv", "a file already judged was probed again and spent budget");
        var reasons = rig.Events.OfType<FileSkipped>().Where(e => e.File == "Episode-02.mkv").Select(e => e.Reason).ToList();
        reasons.ShouldNotBeEmpty();
        reasons.ShouldAllBe(r => r == "not-hardware-decodable", "a skipped file was later reported with another reason");
    }

    // Both outputs are lost one after the other, which ends the film (AC-15b); the devices are still listed, so a return retries them.
    private static void LoseEveryOutput(SessionRig rig)
    {
        rig.Engine.Devices[1].SimulateLoss();
        rig.Engine.Barrier();
        rig.Engine.Devices[0].SimulateLoss();
        rig.Engine.WaitFor(() => rig.Engine.Summary is not null).ShouldBeTrue();
        rig.Session.State.ShouldBe(PlaybackState.Ended);
    }

    [Fact]
    public void A_file_judged_not_hardware_decodable_is_not_probed_again_after_every_output_was_lost_and_the_return_into_the_film_AC_08()
    {
        var (rig, probe) = PlayingRig();
        using var _ = rig;
        File("Episode-02.mkv");
        File("Episode-03.mkv");
        probe.NotHardwareDecodableAfterLimit("Episode-02.mkv");
        probe.Hang("Episode-03.mkv");
        Press(rig);
        rig.Events.OfType<FileChanged>().ShouldBeEmpty();
        LoseEveryOutput(rig);
        rig.Engine.Call(() => rig.Session.Seek(TimeSpan.FromSeconds(-10))).IsSuccess.ShouldBeTrue();
        probe.Probed.Clear();

        rig.Engine.Call(() => rig.Session.NextFile());

        probe.Probed.Select(Path.GetFileName).ShouldNotContain("Episode-02.mkv", "a return into the same film forgot the verdict and spent budget on the file again");
    }

    [Fact]
    public void A_file_change_whose_load_fails_keeps_the_judged_files_and_they_are_not_probed_again_AC_08()
    {
        var (rig, probe) = PlayingRig();
        using var _ = rig;
        File("Episode-02.mkv");
        File("Episode-03.mkv");
        probe.NotHardwareDecodableAfterLimit("Episode-02.mkv");
        probe.Hang("Episode-03.mkv");
        Press(rig);
        rig.Events.OfType<FileChanged>().ShouldBeEmpty();
        probe.Ok("Episode-03.mkv");
        rig.Renderer.LoadError = new Error("video.load_failed", "mpv could not open the file.");
        rig.Engine.Call(() => rig.Session.NextFile()).IsSuccess.ShouldBeFalse(); // plays E3, which does not load
        rig.Renderer.LoadError = null;
        probe.Probed.Clear();

        rig.Engine.Call(() => rig.Session.PreviousFile()); // from E3: E2 first

        probe.Probed.Select(Path.GetFileName).ShouldNotContain("Episode-02.mkv", "a failed file change forgot the verdict and spent budget on the file again");
    }

    [Fact]
    public void A_only_neighbour_whose_check_runs_out_its_limit_on_every_press_ends_the_search_timed_out_not_with_no_neighbour_AC_08()
    {
        var (rig, probe) = PlayingRig();
        using var _ = rig;
        File("Episode-02.mkv");
        probe.Hang("Episode-02.mkv"); // runs its full 2 s limit and ends on cancel, every time

        Press(rig);
        Press(rig);

        rig.Events.OfType<NeighbourSearchTimedOut>().Count().ShouldBe(2, "a check that ran out its limit was counted as judged");
        rig.Events.OfType<NoNeighbour>().ShouldBeEmpty("a file that was never judged was reported as no neighbour");
    }

    [Fact]
    public void A_file_remembered_as_timed_out_is_still_checked_last_after_a_file_change_whose_load_fails_AC_09b()
    {
        var (rig, probe) = PlayingRig();
        using var _ = rig;
        File("Episode-02.mkv");
        File("Episode-03.mkv");
        probe.Hang("Episode-02.mkv");
        probe.Hang("Episode-03.mkv");
        Press(rig); // E2 runs out its limit: remembered; E3 is cut
        rig.Events.OfType<FileChanged>().ShouldBeEmpty();
        probe.Ok("Episode-03.mkv");
        rig.Renderer.LoadError = new Error("video.load_failed", "mpv could not open the file.");
        rig.Engine.Call(() => rig.Session.NextFile()).IsSuccess.ShouldBeFalse(); // plays E3, which does not load
        rig.Renderer.LoadError = null;
        probe.Probed.Clear();

        rig.Engine.Call(() => rig.Session.PreviousFile()); // from E3: E1 first, E2 last

        probe.Probed.Select(Path.GetFileName).ShouldNotContain("Episode-02.mkv", "a failed file change forgot the timed-out memory");
    }

    [Fact]
    public void A_file_remembered_as_timed_out_is_still_checked_last_after_every_output_was_lost_and_the_return_AC_09b()
    {
        var (rig, probe) = PlayingRig();
        using var _ = rig;
        File("Episode-02.mkv");
        File("Episode-03.mkv");
        probe.Hang("Episode-02.mkv");
        probe.Hang("Episode-03.mkv");
        Press(rig); // E2 runs out its limit: remembered; E3 is cut
        rig.Events.OfType<FileChanged>().ShouldBeEmpty();
        probe.Ok("Episode-03.mkv");
        LoseEveryOutput(rig);
        rig.Engine.Call(() => rig.Session.Seek(TimeSpan.FromSeconds(-10))).IsSuccess.ShouldBeTrue();
        probe.Probed.Clear();

        rig.Engine.Call(() => rig.Session.NextFile());

        probe.Probed.Select(Path.GetFileName).ShouldNotContain("Episode-02.mkv", "a return into the same film forgot the timed-out memory");
    }

    [Fact]
    public void A_cut_healthy_neighbour_whose_probe_ends_on_cancel_plays_on_a_press_queued_right_after_AC_08()
    {
        var (rig, probe) = PlayingRig();
        using var _ = rig;
        File("Episode-02.mkv");
        var healthy = File("Episode-03.mkv");
        File("Episode-04.mkv");
        probe.Hang("Episode-02.mkv"); // E2 hangs its full 2 s; E3 is left 1 s of the press
        probe.SlowPastItsLimit("Episode-03.mkv", TimeSpan.FromMilliseconds(1500)); // needs 1.5 s, ends only once cancelled

        rig.Engine.Call(() => rig.Session.NextFile());
        rig.Engine.Call(() => rig.Session.NextFile()); // queued at once: no wait for the first press's workers

        rig.Events.OfType<FileChanged>().ShouldHaveSingleItem("the second press did not play the next file").File.ShouldBe(healthy, "a cut worker that ended on cancel was kept, so the next press stepped past its healthy file");
        rig.Events.OfType<FileSkipped>().Select(e => e.File).ShouldNotContain("Episode-03.mkv", "a healthy file cut by the budget was reported as skipped");
    }

    [Fact]
    public void A_cut_healthy_neighbour_whose_adapter_probe_has_returned_but_is_still_winding_down_plays_on_a_press_queued_right_after_AC_08()
    {
        var (rig, probe) = PlayingRig();
        using var _ = rig;
        File("Episode-02.mkv");
        var healthy = File("Episode-03.mkv");
        File("Episode-04.mkv");
        probe.Hang("Episode-02.mkv"); // E2 hangs its full 2 s; E3 is left 1 s of the press
        probe.AdapterShaped("Episode-03.mkv", TimeSpan.FromMilliseconds(1500)); // needs 1.5 s: returns video.probe_timeout at its cut limit, but its worker is busy for a moment after

        rig.Engine.Call(() => rig.Session.NextFile());
        rig.Engine.Call(() => rig.Session.NextFile()); // queued at once: no wait for the first press's workers

        rig.Events.OfType<FileChanged>().ShouldHaveSingleItem("the second press did not play the next file").File.ShouldBe(healthy, "the adapter's still-running worker made the session step past its healthy file");
        rig.Events.OfType<FileSkipped>().Select(e => e.File).ShouldNotContain("Episode-03.mkv", "a healthy file cut by the budget was reported as skipped");
    }

    /// <summary>Per-file behaviour on the session's fake time; a file with none probes fine at once.</summary>
    private sealed class ScriptedProbe : IPictureProbe
    {
        private readonly Lock _gate = new();
        private readonly Dictionary<string, (TimeSpan Cost, string? Code)> _behaviours = new(StringComparer.Ordinal);
        private readonly HashSet<string> _pastLimit = new(StringComparer.Ordinal);
        private readonly HashSet<string> _adapterShaped = new(StringComparer.Ordinal);
        private readonly Dictionary<string, long> _busyUntil = new(StringComparer.Ordinal);

        public TimeProvider? Time { get; set; }

        public List<string> Probed { get; } = [];

        /// <summary>
        /// Shaped like the production adapter: it spends its limit on fake time (or until cancelled), then returns
        /// <c>video.probe_timeout</c> at once (when its limit is shorter than its cost), but its own worker is still running (<see cref="IsBusy"/> is true) for a short real
        /// time afterwards. Real time, not fake: fake time stands still once the probe has returned, so the grace must wait on the
        /// adapter's answer, not on the clock.
        /// </summary>
        public void AdapterShaped(string name, TimeSpan cost)
        {
            Set(name, cost, null);
            lock (_gate)
            {
                _adapterShaped.Add(name);
            }
        }

        public bool IsBusy(string path)
        {
            lock (_gate)
            {
                return _busyUntil.TryGetValue(Path.GetFileName(path), out var until) && Environment.TickCount64 < until;
            }
        }

        /// <summary>Takes its whole limit, then fails as a probe that gave up.</summary>
        public void Hang(string name) => Set(name, TimeSpan.MaxValue, "video.probe_timeout");

        /// <summary>Takes its whole limit, then fails as a picture this PC cannot decode.</summary>
        public void NotHardwareDecodableAfterLimit(string name) => Set(name, TimeSpan.MaxValue, "video.not_hardware_decodable");

        /// <summary>Succeeds after <paramref name="cost"/>, but fails after the limit when the limit is shorter.</summary>
        public void Slow(string name, TimeSpan cost) => Set(name, cost, null);

        public void Ok(string name) => Set(name, TimeSpan.Zero, null);

        /// <summary>
        /// Succeeds after <paramref name="cost"/> whatever its limit; a cancel stops it, and it then takes a moment to close the file
        /// (real time, while fake time stands still), so it is still running when the session gives up on it.
        /// </summary>
        public void SlowPastItsLimit(string name, TimeSpan cost)
        {
            Set(name, cost, null);
            lock (_gate)
            {
                _pastLimit.Add(name);
            }
        }

        public Result<PictureInfo> Probe(string path, TimeSpan timeLimit) => Probe(path, timeLimit, CancellationToken.None);

        public Result<PictureInfo> Probe(string path, TimeSpan timeLimit, CancellationToken cancel)
        {
            (TimeSpan Cost, string? Code) behaviour;
            bool pastLimit;
            bool adapterShaped;
            lock (_gate)
            {
                Probed.Add(path);
                adapterShaped = _adapterShaped.Contains(Path.GetFileName(path));
                behaviour = _behaviours.GetValueOrDefault(Path.GetFileName(path), (TimeSpan.Zero, null));
                pastLimit = _pastLimit.Contains(Path.GetFileName(path));
            }

            if (adapterShaped)
            {
                // A limit that covers its cost: the probe succeeds. A shorter one: it spends the limit, answers timeout, and its worker winds down.
                var fits = behaviour.Cost <= timeLimit;
                var answerAfter = fits ? behaviour.Cost : timeLimit;
                var clock = Time!;
                clock.SpinUntil(clock.GetTimestamp() + (long)(answerAfter.TotalSeconds * clock.TimestampFrequency), cancel);
                if (fits && !cancel.IsCancellationRequested)
                {
                    return Result<PictureInfo>.Success(new PictureInfo("h264", "High", 1920, 1080));
                }

                lock (_gate)
                {
                    _busyUntil[Path.GetFileName(path)] = Environment.TickCount64 + 200; // its worker winds down after the answer
                }

                return Result<PictureInfo>.Failure("video.probe_timeout", "The probe ran out its limit.");
            }

            if (pastLimit)
            {
                var time = Time!;
                time.SpinUntil(time.GetTimestamp() + (long)(behaviour.Cost.TotalSeconds * time.TimestampFrequency), cancel);
                if (cancel.IsCancellationRequested)
                {
                    Thread.Sleep(200); // closes the file after the cancel
                    return Result<PictureInfo>.Failure("video.probe_timeout", "The probe was cancelled.");
                }

                return Result<PictureInfo>.Success(new PictureInfo("h264", "High", 1920, 1080));
            }

            var spent = behaviour.Cost < timeLimit ? behaviour.Cost : timeLimit;
            var end = Time!.GetTimestamp() + (long)(spent.TotalSeconds * Time.TimestampFrequency);
            Time.SpinUntil(end, cancel);

            if (behaviour.Cost <= timeLimit && behaviour.Code is null)
            {
                return Result<PictureInfo>.Success(new PictureInfo("h264", "High", 1920, 1080));
            }

            return Result<PictureInfo>.Failure(behaviour.Code ?? "video.probe_timeout", "The probe gave up.");
        }

        private void Set(string name, TimeSpan cost, string? code)
        {
            lock (_gate)
            {
                _behaviours[name] = (cost, code);
            }
        }
    }
}
