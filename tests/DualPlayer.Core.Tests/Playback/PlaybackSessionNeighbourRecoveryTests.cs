using DualPlayer.Core.Diagnostics;
using DualPlayer.Core.Outputs;
using DualPlayer.Core.Playback;
using DualPlayer.Core.Tests.Fakes;

namespace DualPlayer.Core.Tests.Playback;

/// <summary>
/// T102 (review-2026-11-22d V1-V4): a neighbour stepped past because its worker is still running is not judged for good, so it
/// plays once the worker recovers; the press's whole budget is the press's own, not what the folder work left of it; a
/// remembered file is reported skipped once per press; a probe worker still running is never started twice (AC-07, AC-08, AC-09, AC-09b).
/// </summary>
public sealed class PlaybackSessionNeighbourRecoveryTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("dualplayer-recovery-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private string File(string name)
    {
        var path = Path.Combine(_folder, name);
        System.IO.File.WriteAllBytes(path, []);
        return path;
    }

    private SessionRig Playing(ScriptedDecoder decoder, ScriptedProbe probe)
    {
        var rig = new SessionRig([new RigDevice(48_000), new RigDevice(48_000)], null, 600, neighbourDecoder: decoder, neighbourPictureProbe: probe, barrierEvery: NeighbourRig.BarrierEvery);
        decoder.Time = rig.Engine.Time;
        probe.Time = rig.Engine.Time;
        rig.Engine.Call(() => rig.Session.Start(rig.Request(2) with { File = File("Episode-01.mkv") })).IsSuccess.ShouldBeTrue();
        rig.Engine.Render(300);
        return rig;
    }

    private static void Press(SessionRig rig) => rig.Engine.Call(() => rig.Session.NextFile());

    // A press whose scan spends fake time on the folder work before it checks the first file.
    private static void PressOnSlowFolder(SessionRig rig, TimeSpan folderCost)
    {
        rig.Engine.Time.SlowNextFolderScan(folderCost);
        rig.Engine.Call(() => rig.Session.NextFile(rig.Engine.Time.GetTimestamp()));
    }

    private static IEnumerable<string> Notes(SessionRig rig) => rig.Events.OfType<NoteShown>().Select(e => e.Text);

    // The worker's task completes a moment after the fake call returns: wait until the session holds no live worker. Read
    // between commands, when the session's control thread does not touch its worker map.
    private static void LetWorkerFinish(SessionRig rig, ScriptedDecoder decoder, string name)
    {
        RigLoops.WaitOrFail(() => decoder.Returned(name), TimeSpan.FromSeconds(10), $"the listing of {name} to return");
        RigLoops.WaitForNeighbourWorkers(rig.Session);
    }

    [Fact]
    public void A_file_change_whose_scan_spent_2900_ms_before_the_target_was_chosen_counts_its_3_s_recovery_from_the_choice_AC_07()
    {
        using var decoder = new ScriptedDecoder();
        using var probe = new ScriptedProbe();
        File("Episode-02.mkv");
        using var rig = Playing(decoder, probe);

        PressOnSlowFolder(rig, TimeSpan.FromMilliseconds(2900)); // the scan alone spends 2.9 s; the target is then chosen at once
        rig.Engine.Render(300);

        rig.Events.OfType<FileChanged>().ShouldHaveSingleItem("the target was not chosen");
        rig.Events.OfType<NotLipSynced>().Where(e => e.Command == "file-change").ShouldBeEmpty("the 3 s recovery was counted from the key, so the time spent scanning was held against the new file");
        rig.Events.OfType<LipSyncedAgain>().Where(e => e.Command == "file-change").ShouldHaveSingleItem("the new file never reported lip-synced again");
    }

    [Fact]
    public void A_file_change_chosen_at_once_whose_recovery_runs_past_3_s_is_still_given_up_on_AC_07()
    {
        using var decoder = new ScriptedDecoder();
        using var probe = new ScriptedProbe();
        File("Episode-02.mkv");
        using var rig = Playing(decoder, probe);

        rig.Renderer.AfterLoad = () => rig.Renderer.Frozen = true; // the new file's picture never becomes measurable
        Press(rig);
        rig.Engine.Render(300);
        rig.Engine.Render(100);

        rig.Events.OfType<NotLipSynced>().Where(e => e.Command == "file-change").ShouldHaveSingleItem("a recovery that ran past 3 s from the choice was not given up on");
    }

    [Fact]
    public void A_file_change_whose_old_run_stop_took_1_s_counts_its_3_s_recovery_from_the_choice_so_a_new_film_lip_synced_3_5_s_after_it_is_given_up_on_AC_07()
    {
        using var decoder = new ScriptedDecoder();
        using var probe = new ScriptedProbe();
        File("Episode-02.mkv");
        using var rig = Playing(decoder, probe);
        rig.EngineStopTakes = TimeSpan.FromSeconds(1); // the old run's stop waits 1 s for the devices to play past the stop point
        rig.EngineCommandTakes = TimeSpan.FromMilliseconds(2300); // the new film's engine start (pre-roll): lip-synced about 2.5 s after the stop ended

        Press(rig);
        rig.Engine.Render(300);

        rig.Events.OfType<FileChanged>().ShouldHaveSingleItem("the target was not chosen");
        rig.Events.OfType<LipSyncedAgain>().Where(e => e.Command == "file-change").ShouldBeEmpty("the old run's 1 s stop was left out of the 3 s recovery counted from the choice");
        rig.Events.OfType<NotLipSynced>().Where(e => e.Command == "file-change").ShouldHaveSingleItem("a new film lip-synced 3.5 s after the choice was not given up on");
    }

    [Fact]
    public void A_file_change_whose_old_run_stop_took_500_ms_reports_lip_synced_again_counted_from_the_choice_AC_07()
    {
        using var decoder = new ScriptedDecoder();
        using var probe = new ScriptedProbe();
        File("Episode-02.mkv");
        using var rig = Playing(decoder, probe);
        rig.EngineStopTakes = TimeSpan.FromMilliseconds(500);
        rig.EngineCommandTakes = TimeSpan.FromMilliseconds(1500);

        Press(rig);
        rig.Engine.Render(300);

        rig.Events.OfType<NotLipSynced>().Where(e => e.Command == "file-change").ShouldBeEmpty("a recovery inside 3 s from the choice was given up on");
        var again = rig.Events.OfType<LipSyncedAgain>().Where(e => e.Command == "file-change").ShouldHaveSingleItem("the new film never reported lip-synced again");
        again.AfterCommandMs.ShouldBeGreaterThanOrEqualTo(2000, "the old run's 500 ms stop was left out of the time since the choice");
        again.AfterCommandMs.ShouldBeLessThan(3000);
    }

    [Fact]
    public void A_file_change_is_announced_when_the_target_is_chosen_not_after_the_old_run_stop_that_took_1_s_AC_07()
    {
        using var decoder = new ScriptedDecoder();
        using var probe = new ScriptedProbe();
        File("Episode-02.mkv");
        using var rig = Playing(decoder, probe);
        rig.EngineStopTakes = TimeSpan.FromSeconds(1); // the old run's stop waits 1 s for the devices to play past the stop point
        var announcedAt = new List<DateTimeOffset>();
        using var watch = rig.Session.Events.Subscribe(new AnnouncementClock(rig.Engine.Time, announcedAt));
        var chosenAt = rig.Engine.Time.GetUtcNow(); // the folder work and the target's check spend no fake time: the target is chosen now

        Press(rig);

        announcedAt.ShouldHaveSingleItem("the target was not chosen");
        (announcedAt[0] - chosenAt).ShouldBeLessThan(TimeSpan.FromMilliseconds(100), "the next/previous line waited for the old run's 1 s stop instead of being said when the target was chosen");
    }

    private sealed class AnnouncementClock(TimeProvider time, List<DateTimeOffset> announcedAt) : IObserver<SessionEvent>
    {
        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }

        public void OnNext(SessionEvent value)
        {
            if (value is FileChanged)
            {
                announcedAt.Add(time.GetUtcNow());
            }
        }
    }

    [Fact]
    public void The_only_neighbour_whose_listing_stayed_blocked_through_a_press_plays_on_the_press_after_it_recovered_AC_09b()
    {
        using var decoder = new ScriptedDecoder();
        using var probe = new ScriptedProbe();
        var second = File("Episode-02.mkv");
        using var rig = Playing(decoder, probe);
        decoder.Block("Episode-02.mkv");

        Press(rig); // the listing hangs its full limit: E2 is remembered
        Press(rig); // its worker is still blocked: stepped past, counted, reported for this press only
        rig.Events.OfType<FileSkipped>().ShouldContain(e => e.File == "Episode-02.mkv" && e.Reason == "unreadable", "the live worker's file was not reported as skipped");
        Notes(rig).ShouldContain(t => t.StartsWith("Next file not found within 3 s (checked 1)", StringComparison.Ordinal), "a press that only stepped past a file ended as no next file");
        rig.Events.OfType<NoNeighbour>().ShouldBeEmpty("a press reports no file nobody judged");
        rig.Events.OfType<NeighbourSearchTimedOut>().Count().ShouldBe(2);
        rig.Events.OfType<FileChanged>().ShouldBeEmpty();
        decoder.Release("Episode-02.mkv"); // the NAS is back
        LetWorkerFinish(rig, decoder, "Episode-02.mkv");

        Press(rig);

        rig.Events.OfType<FileChanged>().ShouldHaveSingleItem("a file stepped past for a still-running worker was judged for good and never played").File.ShouldBe(second);
    }

    [Fact]
    public void A_healthy_neighbour_whose_cut_listing_was_still_running_at_the_next_press_plays_on_the_press_after_AC_08()
    {
        using var decoder = new ScriptedDecoder();
        using var probe = new ScriptedProbe();
        File("Episode-02.mkv");
        var third = File("Episode-03.mkv");
        using var rig = Playing(decoder, probe);
        probe.Hang("Episode-02.mkv"); // takes its whole 2 s: E3 is left 1 s of the first press
        decoder.Block("Episode-03.mkv"); // its listing is cut by the budget and keeps running

        Press(rig);
        Press(rig); // E3's worker is still blocked: E3 is stepped past, E2 hangs again
        rig.Events.OfType<FileChanged>().ShouldBeEmpty();
        rig.Events.OfType<FileSkipped>().Select(e => e.File).ShouldContain("Episode-03.mkv", "the file with a live worker was not stepped past");
        decoder.Release("Episode-03.mkv");
        LetWorkerFinish(rig, decoder, "Episode-03.mkv");

        Press(rig);

        rig.Events.OfType<FileChanged>().ShouldHaveSingleItem("a healthy file stepped past for a still-running listing was judged unreadable for good").File.ShouldBe(third);
    }

    [Fact]
    public void A_slow_first_file_is_remembered_however_long_the_folder_work_took_and_the_next_press_reaches_the_file_after_it_AC_09b()
    {
        using var decoder = new ScriptedDecoder();
        using var probe = new ScriptedProbe();
        File("Episode-02.mkv");
        var third = File("Episode-03.mkv");
        using var rig = Playing(decoder, probe);
        decoder.Cost("Episode-02.mkv", TimeSpan.FromMilliseconds(1200));
        probe.Cost("Episode-02.mkv", TimeSpan.FromMilliseconds(1900));

        PressOnSlowFolder(rig, TimeSpan.FromMilliseconds(200)); // E2 has the whole budget: the folder work is not E2's doing
        rig.Events.OfType<FileChanged>().ShouldBeEmpty("the first press cannot judge E2 within its budget");
        RigLoops.WaitForNeighbourWorkers(rig.Session);

        PressOnSlowFolder(rig, TimeSpan.FromMilliseconds(200));

        rig.Events.OfType<FileChanged>().ShouldHaveSingleItem("the folder work counted against E2's whole budget, so it was not remembered and held up every press").File.ShouldBe(third);
    }

    [Fact]
    public void A_first_file_whose_listing_is_cut_is_remembered_after_folder_work_of_1200_ms_and_the_next_press_reaches_the_file_after_it_AC_09b()
    {
        using var decoder = new ScriptedDecoder();
        using var probe = new ScriptedProbe();
        File("Episode-02.mkv");
        var third = File("Episode-03.mkv");
        using var rig = Playing(decoder, probe);
        decoder.Cost("Episode-02.mkv", TimeSpan.FromMilliseconds(2500));

        PressOnSlowFolder(rig, TimeSpan.FromMilliseconds(1200)); // E2 is the first file checked: its listing is cut by the budget
        rig.Events.OfType<FileChanged>().ShouldBeEmpty("the first press cannot judge E2 within its budget");
        rig.Engine.Render(100); // fake time moves on: E2's cut listing completes, so only a memory can keep the next press off it
        LetWorkerFinish(rig, decoder, "Episode-02.mkv");

        PressOnSlowFolder(rig, TimeSpan.FromMilliseconds(1200));

        rig.Events.OfType<FileChanged>().ShouldHaveSingleItem("the first file of the press, cut during its listing, was not remembered and held up every press").File.ShouldBe(third);
    }

    [Fact]
    public void A_listing_that_leaves_too_little_of_the_budget_for_the_probe_is_remembered_and_the_next_press_reaches_the_file_after_it_AC_09b()
    {
        using var decoder = new ScriptedDecoder();
        using var probe = new ScriptedProbe();
        File("Episode-02.mkv");
        var third = File("Episode-03.mkv");
        using var rig = Playing(decoder, probe);
        decoder.Cost("Episode-02.mkv", TimeSpan.FromMilliseconds(2950));

        Press(rig);
        rig.Events.OfType<FileChanged>().ShouldBeEmpty("the first press cannot probe E2 in the 50 ms its listing left");
        RigLoops.WaitForNeighbourWorkers(rig.Session);

        Press(rig);

        rig.Events.OfType<FileChanged>().ShouldHaveSingleItem("a file whose listing took almost the whole budget was not remembered").File.ShouldBe(third);
    }

    [Fact]
    public void A_remembered_file_that_hangs_again_while_the_file_after_it_recovers_is_reported_skipped_once_AC_09b()
    {
        using var decoder = new ScriptedDecoder();
        using var probe = new ScriptedProbe();
        File("Episode-02.mkv");
        var third = File("Episode-03.mkv");
        using var rig = Playing(decoder, probe);
        probe.Hang("Episode-02.mkv");
        probe.Hang("Episode-03.mkv");

        Press(rig); // E2 hangs its full limit: remembered; E3 is cut by the budget
        RigLoops.WaitForNeighbourWorkers(rig.Session);
        Press(rig); // E3 hangs its full limit: remembered; E2 is cut by the budget
        rig.Events.OfType<FileChanged>().ShouldBeEmpty();
        RigLoops.WaitForNeighbourWorkers(rig.Session);
        probe.Ok("Episode-03.mkv"); // E3 recovers; E2 hangs again
        var skipsBefore = rig.Events.OfType<FileSkipped>().Count();
        var notesBefore = Notes(rig).Count();

        Press(rig);

        rig.Events.OfType<FileChanged>().ShouldHaveSingleItem().File.ShouldBe(third);
        rig.Events.OfType<FileSkipped>().Skip(skipsBefore).Select(e => e.File).ShouldBe(["Episode-02.mkv"], "the remembered file was reported once when it was checked and again after the target");
        var notes = Notes(rig).Skip(notesBefore).ToList();
        notes.ShouldContain(t => t.StartsWith("Skipped Episode-02.mkv", StringComparison.Ordinal));
        notes.ShouldNotContain(t => t.StartsWith("Skipped 2 files", StringComparison.Ordinal));
    }

    [Fact]
    public void A_probe_that_never_returns_is_started_once_however_many_presses_find_nothing_else_AC_09b()
    {
        using var decoder = new ScriptedDecoder();
        using var probe = new ScriptedProbe();
        File("Episode-02.mkv");
        using var rig = Playing(decoder, probe);
        using var releaseProbe = new Defer(probe.ReleaseAll); // disposed before the rig: a blocked probe must not hold its loop
        probe.Never("Episode-02.mkv");

        Press(rig);
        Press(rig);
        Press(rig);

        probe.Probed.Count(n => n == "Episode-02.mkv").ShouldBe(1, "a press started another probe on a file whose probe was still blocked");
        rig.Events.OfType<FileChanged>().ShouldBeEmpty();
    }

    [Fact]
    public void A_neighbour_whose_probe_adapter_reports_its_worker_busy_is_stepped_past_not_judged_and_plays_once_it_recovers_AC_09b()
    {
        using var decoder = new ScriptedDecoder();
        using var probe = new ScriptedProbe();
        var second = File("Episode-02.mkv");
        using var rig = Playing(decoder, probe);
        probe.Busy("Episode-02.mkv");

        Press(rig);
        Press(rig);
        rig.Events.OfType<FileChanged>().ShouldBeEmpty();
        rig.Events.OfType<FileSkipped>().ShouldAllBe(e => e.File == "Episode-02.mkv" && e.Reason == "unreadable");
        probe.Ok("Episode-02.mkv"); // the blocked read ended: the adapter probes the file again

        Press(rig);

        rig.Events.OfType<FileChanged>().ShouldHaveSingleItem().File.ShouldBe(second);
    }

    [Fact]
    public void A_file_whose_probe_worker_is_still_running_is_stepped_past_before_a_second_listing_blocks_on_it_AC_09b()
    {
        using var decoder = new ScriptedDecoder();
        using var probe = new ScriptedProbe();
        File("Episode-02.mkv");
        using var rig = Playing(decoder, probe);
        probe.Hang("Episode-02.mkv");
        probe.StayBusyAfterTimeout("Episode-02.mkv"); // the adapter returns probe_timeout and keeps its worker blocked in ReadFile

        Press(rig); // E2's listing returns, its probe runs out of time: the adapter's worker stays
        decoder.Listed("Episode-02.mkv").ShouldBe(1);
        decoder.Block("Episode-02.mkv"); // the share is now fully down: a new listing would block on open

        Press(rig);

        decoder.Listed("Episode-02.mkv").ShouldBe(1, "a second thread was blocked on a file whose probe worker was still running");
        rig.Events.OfType<FileSkipped>().ShouldContain(e => e.File == "Episode-02.mkv" && e.Reason == "unreadable", "the file with a live probe worker was not stepped past");
        rig.Events.OfType<FileChanged>().ShouldBeEmpty();
    }

    [Fact]
    public void A_give_up_on_a_probe_worker_that_stays_busy_forever_costs_a_small_bounded_real_time_AC_08()
    {
        using var decoder = new ScriptedDecoder();
        using var probe = new ScriptedProbe();
        File("Episode-02.mkv");
        using var rig = Playing(decoder, probe);
        probe.Hang("Episode-02.mkv");
        probe.StayBusyAfterTimeout("Episode-02.mkv");

        var wall = System.Diagnostics.Stopwatch.StartNew();
        Press(rig);
        wall.Stop();

        wall.Elapsed.ShouldBeLessThan(TimeSpan.FromMilliseconds(300), "the give-up grace polled a worker that never lets go 500 times on a frozen clock");
    }

    [Fact]
    public void The_probe_wrapper_runs_off_the_shared_thread_pool_and_the_chosen_file_starts_AC_09b()
    {
        using var decoder = new ScriptedDecoder();
        using var probe = new ScriptedProbe();
        var second = File("Episode-02.mkv");
        using var rig = Playing(decoder, probe);
        bool? onPool = null;
        probe.OnProbe = () => onPool = Thread.CurrentThread.IsThreadPoolThread;

        Press(rig);

        onPool.ShouldBe(false, "the probe wrapper ran on a thread-pool thread: a busy pool would delay it past its limit");
        probe.Probed.ShouldContain("Episode-02.mkv");
        rig.Events.OfType<FileChanged>().ShouldHaveSingleItem().File.ShouldBe(second);
        rig.EngineRunFiles.ShouldContain(second, "the new film never started");
        rig.Engine.Events.OfType<AudioStarted>().Count().ShouldBeGreaterThan(1, "the new film never reached audio");
    }

    private sealed class Defer(Action action) : IDisposable
    {
        public void Dispose() => action();
    }

    /// <summary>Listings per file on the session's fake time: instant, costing fake time, or blocked until released.</summary>
    private sealed class ScriptedDecoder : IAudioDecoder, IDisposable
    {
        private readonly Lock _gate = new();
        private readonly Dictionary<string, TimeSpan> _costs = new(StringComparer.Ordinal);
        private readonly Dictionary<string, ManualResetEventSlim> _blocks = new(StringComparer.Ordinal);
        private readonly HashSet<string> _returned = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _listed = new(StringComparer.Ordinal);

        public TimeProvider? Time { get; set; }

        public int Listed(string name)
        {
            lock (_gate)
            {
                return _listed.GetValueOrDefault(name);
            }
        }

        public void Cost(string name, TimeSpan cost)
        {
            lock (_gate)
            {
                _costs[name] = cost;
            }
        }

        public void Block(string name)
        {
            lock (_gate)
            {
                _blocks[name] = new ManualResetEventSlim();
            }
        }

        public void Release(string name)
        {
            lock (_gate)
            {
                _blocks[name].Set();
            }
        }

        public bool Returned(string name)
        {
            lock (_gate)
            {
                return _returned.Contains(name);
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                foreach (var block in _blocks.Values)
                {
                    block.Set();
                }
            }
        }

        public Result<IReadOnlyList<AudioTrackInfo>> ListTracks(string path)
        {
            var name = Path.GetFileName(path);
            ManualResetEventSlim? block;
            TimeSpan cost;
            lock (_gate)
            {
                _blocks.TryGetValue(name, out block);
                cost = _costs.GetValueOrDefault(name);
                _listed[name] = _listed.GetValueOrDefault(name) + 1;
            }

            if (block is not null)
            {
                using var waiting = Time.Waiting();
                block.Wait(TimeSpan.FromSeconds(30)); // ignores the token: an uninterruptible native call
            }
            else if (cost > TimeSpan.Zero)
            {
                var end = Time!.GetTimestamp() + (long)(cost.TotalSeconds * Time.TimestampFrequency);
                Time.SpinUntil(end);
            }

            lock (_gate)
            {
                _returned.Add(name);
            }

            return Result<IReadOnlyList<AudioTrackInfo>>.Success([.. new[] { 1, 2 }.Select(n => new AudioTrackInfo(n, "und", ChannelLayout.Stereo, 48_000))]);
        }

        public Result<IDecodeSession> Open(string path, IReadOnlyList<DecodeRequest> outputs) =>
            Result<IDecodeSession>.Failure("test.not_decoding", "The listing decoder does not decode.");
    }

    /// <summary>Probes per file on the session's fake time: instant, costing fake time, hanging its whole limit, or never returning.</summary>
    private sealed class ScriptedProbe : IPictureProbe, IDisposable
    {
        private readonly Lock _gate = new();
        private readonly Dictionary<string, TimeSpan> _costs = new(StringComparer.Ordinal);
        private readonly ManualResetEventSlim _release = new();
        private readonly HashSet<string> _never = new(StringComparer.Ordinal);
        private readonly HashSet<string> _busy = new(StringComparer.Ordinal);
        private readonly HashSet<string> _stayBusy = new(StringComparer.Ordinal);
        private readonly HashSet<string> _stuck = new(StringComparer.Ordinal);

        public TimeProvider? Time { get; set; }

        public Action? OnProbe { get; set; }

        /// <summary>After a probe of the file returns <c>video.probe_timeout</c>, the adapter's worker is still running: <see cref="IsBusy"/> says so.</summary>
        public void StayBusyAfterTimeout(string name)
        {
            lock (_gate)
            {
                _stayBusy.Add(name);
            }
        }

        public bool IsBusy(string path)
        {
            lock (_gate)
            {
                return _stuck.Contains(Path.GetFileName(path));
            }
        }

        public List<string> Probed { get; } = [];

        public void Hang(string name) => SetCost(name, TimeSpan.MaxValue);

        public void Cost(string name, TimeSpan cost) => SetCost(name, cost);

        public void Ok(string name)
        {
            lock (_gate)
            {
                _busy.Remove(name);
            }

            SetCost(name, TimeSpan.Zero);
        }

        public void Busy(string name)
        {
            lock (_gate)
            {
                _busy.Add(name);
            }
        }

        public void Never(string name)
        {
            lock (_gate)
            {
                _never.Add(name);
            }
        }

        public void ReleaseAll() => _release.Set();

        public void Dispose() => _release.Set();

        public Result<PictureInfo> Probe(string path, TimeSpan timeLimit) => Probe(path, timeLimit, CancellationToken.None);

        public Result<PictureInfo> Probe(string path, TimeSpan timeLimit, CancellationToken cancel)
        {
            OnProbe?.Invoke();
            var name = Path.GetFileName(path);
            TimeSpan cost;
            bool never;
            bool busy;
            lock (_gate)
            {
                Probed.Add(name);
                cost = _costs.GetValueOrDefault(name);
                never = _never.Contains(name);
                busy = _busy.Contains(name);
            }

            if (busy)
            {
                return Result<PictureInfo>.Failure("video.probe_busy", "An earlier probe of the file is still running.");
            }

            if (never)
            {
                using var waiting = Time.Waiting();
                _release.Wait(TimeSpan.FromSeconds(30)); // ignores the limit and the token: a read stuck on a bad sector
                return Result<PictureInfo>.Failure("video.probe_timeout", "The probe gave up.");
            }

            var spent = cost < timeLimit ? cost : timeLimit;
            var end = Time!.GetTimestamp() + (long)(spent.TotalSeconds * Time.TimestampFrequency);
            Time.SpinUntil(end, cancel);

            if (cost > timeLimit)
            {
                lock (_gate)
                {
                    if (_stayBusy.Contains(name))
                    {
                        _stuck.Add(name);
                    }
                }
            }

            return cost > timeLimit
                ? Result<PictureInfo>.Failure("video.probe_timeout", "The probe gave up.")
                : Result<PictureInfo>.Success(new PictureInfo("h264", "High", 1920, 1080));
        }

        private void SetCost(string name, TimeSpan cost)
        {
            lock (_gate)
            {
                _costs[name] = cost;
            }
        }
    }
}
