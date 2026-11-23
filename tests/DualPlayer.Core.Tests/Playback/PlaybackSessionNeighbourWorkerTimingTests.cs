using DualPlayer.Core.Outputs;
using DualPlayer.Core.Playback;
using DualPlayer.Core.Tests.Fakes;

namespace DualPlayer.Core.Tests.Playback;

/// <summary>
/// T100 (review U11): a neighbour listing or probe worker that the thread pool starts late must see the same fake time the
/// command had when it began, so the scan budget and the probe limit never depend on pool scheduling (AC-07, AC-08).
/// </summary>
public sealed class PlaybackSessionNeighbourWorkerTimingTests
{
    [Fact]
    public void A_worker_the_thread_pool_starts_late_sees_the_fake_time_the_command_began_at_AC_08()
    {
        var rig = new SessionRig([new RigDevice(48_000), new RigDevice(48_000)], null, 600, barrierEvery: NeighbourRig.BarrierEvery);
        using var _ = rig;
        var time = rig.Engine.Time;
        var commandBegan = time.GetTimestamp();

        var workerSaw = rig.Engine.Call(() =>
        {
            var worker = Task.Run(() =>
            {
                // The pool was busy: the worker starts late, only once the rig's gate has looked at the command and kept time still.
                time.WaitForGateDeclined(1, TimeSpan.FromSeconds(10)).ShouldBeTrue("the rig's gate never looked at the command");
                return time.GetTimestamp();
            });
            return worker.GetAwaiter().GetResult();
        });

        workerSaw.ShouldBe(commandBegan, "fake time ran on while the worker waited for the pool, so its limit could be cut before it began");
    }

    [Fact]
    public void A_timer_another_thread_creates_during_a_command_does_not_move_fake_time_AC_08()
    {
        var rig = new SessionRig([new RigDevice(48_000), new RigDevice(48_000)], null, 600, barrierEvery: NeighbourRig.BarrierEvery);
        using var _ = rig;
        var time = rig.Engine.Time;
        var commandBegan = time.GetTimestamp();

        var sawAfter = rig.Engine.Call(() =>
        {
            // The playing film's DecodePump re-arming its wait: a timer from an unrelated thread, not a signal from a fake.
            time.WaitForGateDeclined(1, TimeSpan.FromSeconds(10)).ShouldBeTrue("the rig's gate never looked at the command");
            var declined = time.GateDeclined;
            Task.Run(() => time.CreateTimer(_ => { }, null, TimeSpan.FromHours(1), Timeout.InfiniteTimeSpan)).Wait(TimeSpan.FromSeconds(5));
            time.WaitForGateDeclined(declined + 1, TimeSpan.FromSeconds(10)).ShouldBeTrue("the gate did not look at the timer");
            return time.GetTimestamp();
        });

        sawAfter.ShouldBe(commandBegan, "an unrelated timer opened the gate and fake time ran on during a command that waits on no fake");
    }

    [Fact]
    public void A_neighbour_press_that_waits_on_no_fake_time_returns_without_a_real_time_wait_AC_09b()
    {
        var folder = Directory.CreateTempSubdirectory("dualplayer-nowait-").FullName;
        try
        {
            File.WriteAllBytes(Path.Combine(folder, "Episode-01.mkv"), []);
            File.WriteAllBytes(Path.Combine(folder, "Episode-02.mkv"), []);
            var rig = new SessionRig([new RigDevice(48_000), new RigDevice(48_000)], null, 600, barrierEvery: NeighbourRig.BarrierEvery);
            using var _ = rig;
            rig.Engine.Call(() => rig.Session.Start(rig.Request(2) with { File = Path.Combine(folder, "Episode-01.mkv") })).IsSuccess.ShouldBeTrue();
            rig.Engine.Render(300);

            var watch = System.Diagnostics.Stopwatch.StartNew();
            rig.Engine.Call(() => rig.Session.NextFile());
            watch.Stop();

            watch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(1), "the press waited on a real-time cap before fake time moved");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void A_neighbour_scan_with_a_film_playing_does_not_move_fake_time_when_an_unrelated_timer_appears_AC_08()
    {
        var folder = Directory.CreateTempSubdirectory("dualplayer-scan-").FullName;
        try
        {
            var first = Path.Combine(folder, "Episode-01.mkv");
            var second = Path.Combine(folder, "Episode-02.mkv");
            File.WriteAllBytes(first, []);
            File.WriteAllBytes(second, []);
            var rig = new SessionRig([new RigDevice(48_000), new RigDevice(48_000)], null, 600, barrierEvery: NeighbourRig.BarrierEvery);
            using var _ = rig;
            var time = rig.Engine.Time;
            rig.Pictures.Fail(second, "video.not_hardware_decodable", "No hardware decoder.");
            rig.Engine.Call(() => rig.Session.Start(rig.Request(2) with { File = first })).IsSuccess.ShouldBeTrue();
            rig.Engine.Render(300);

            // The scan lists the neighbour's tracks while the film's DecodePump re-arms its wait: a timer from an unrelated thread.
            var listed = 0;
            rig.Decoder.BeforeList = () =>
            {
                Interlocked.Increment(ref listed);
                time.WaitForGateDeclined(1, TimeSpan.FromSeconds(10)).ShouldBeTrue("the rig's gate never looked at the command");
                var declined = time.GateDeclined;
                Task.Run(() => time.CreateTimer(_ => { }, null, TimeSpan.FromHours(1), Timeout.InfiniteTimeSpan)).Wait(TimeSpan.FromSeconds(5));
                time.WaitForGateDeclined(declined + 1, TimeSpan.FromSeconds(10)).ShouldBeTrue("the gate did not look at the timer");
            };

            var before = time.GetTimestamp();
            rig.Engine.Call(() => rig.Session.NextFile());

            listed.ShouldBeGreaterThan(0, "the scan never listed a neighbour, so the timer was not created during it");
            rig.Events.OfType<FileSkipped>().ShouldHaveSingleItem();
            time.GetTimestamp().ShouldBe(before, "an unrelated timer opened the gate and fake time ran on during a neighbour scan that waits on no fake");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void A_neighbour_track_listing_runs_off_the_shared_thread_pool_AC_08()
    {
        var folder = Directory.CreateTempSubdirectory("dualplayer-listpool-").FullName;
        try
        {
            var first = Path.Combine(folder, "Episode-01.mkv");
            File.WriteAllBytes(first, []);
            File.WriteAllBytes(Path.Combine(folder, "Episode-02.mkv"), []);
            var rig = new SessionRig([new RigDevice(48_000), new RigDevice(48_000)], null, 600, barrierEvery: NeighbourRig.BarrierEvery);
            using var _ = rig;
            rig.Engine.Call(() => rig.Session.Start(rig.Request(2) with { File = first })).IsSuccess.ShouldBeTrue();
            rig.Engine.Render(300);

            var listed = 0;
            var onPool = 0;
            rig.Decoder.BeforeList = () =>
            {
                Interlocked.Increment(ref listed);
                if (Thread.CurrentThread.IsThreadPoolThread)
                {
                    Interlocked.Increment(ref onPool);
                }
            };

            rig.Engine.Call(() => rig.Session.NextFile());

            listed.ShouldBeGreaterThan(0, "the scan never listed a neighbour");
            onPool.ShouldBe(0, "the track listing ran on the shared thread pool");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void A_probe_wrapper_whose_thread_starts_after_the_budget_does_not_hold_the_press_past_budget_and_slack_AC_09b()
    {
        var folder = Directory.CreateTempSubdirectory("dualplayer-latewrapper-").FullName;
        using var release = new CancellationTokenSource();
        try
        {
            var first = Path.Combine(folder, "Episode-01.mkv");
            File.WriteAllBytes(first, []);
            File.WriteAllBytes(Path.Combine(folder, "Episode-02.mkv"), []);
            var rig = new SessionRig([new RigDevice(48_000), new RigDevice(48_000)], null, 600, barrierEvery: NeighbourRig.BarrierEvery);
            using var _ = rig;
            var time = rig.Engine.Time;
            rig.Engine.Call(() => rig.Session.Start(rig.Request(2) with { File = first })).IsSuccess.ShouldBeTrue();
            rig.Engine.Render(300);

            // The OS starts the probe wrapper's thread late: its first line (the read of the time it enters at) is held while fake
            // time runs well past the press budget, until the test lets it go.
            var held = 0;
            var gateTimedOut = 0;
            time.BeforeRead = () =>
            {
                if (!IsProbeWrapper() || Interlocked.Exchange(ref held, 1) == 1)
                {
                    return;
                }

                // Fake time runs only to just past the press's bound (budget + slack) and no further, so a session thread that wakes
                // late cannot find the clock far beyond it; the thread then stays held on a real-time gate until the test lets it go.
                time.SpinUntil(time.GetTimestamp() + (long)((Budget + Slack + TimeSpan.FromMilliseconds(20)).TotalSeconds * time.TimestampFrequency), release.Token);
                if (!release.Token.WaitHandle.WaitOne(TimeSpan.FromSeconds(5)))
                {
                    Volatile.Write(ref gateTimedOut, 1); // the press was still waiting for this thread after 5 s of real time
                }
            };

            var key = time.GetUtcNow();
            rig.Engine.Call(() => rig.Session.NextFile());
            var elapsed = time.GetUtcNow() - key;

            held.ShouldBe(1, "the probe wrapper was never held back");
            Volatile.Read(ref gateTimedOut).ShouldBe(0, "the press waited for a probe wrapper that had not started yet, until it was let go");
            elapsed.ShouldBeLessThanOrEqualTo(Budget + Slack + Tolerance, "the press waited for a probe wrapper that had not started yet");
        }
        finally
        {
            release.Cancel();
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void A_probe_wrapper_that_enters_late_on_a_cut_limit_is_given_up_on_not_judged_and_probed_again_on_the_next_press_AC_09b()
    {
        var folder = Directory.CreateTempSubdirectory("dualplayer-latecut-").FullName;
        using var release = new CancellationTokenSource();
        try
        {
            var first = Path.Combine(folder, "Episode-01.mkv");
            File.WriteAllBytes(first, []);
            File.WriteAllBytes(Path.Combine(folder, "Episode-02.mkv"), []);
            var decoder = new SlowListingDecoder(TimeSpan.FromMilliseconds(1200));
            var probe = new NeverAnsweringProbe(release.Token);
            using var rig = new SessionRig([new RigDevice(48_000), new RigDevice(48_000)], null, 600, neighbourDecoder: decoder, neighbourPictureProbe: probe, barrierEvery: NeighbourRig.BarrierEvery);
            using var releaseFirst = new Defer(release.Cancel); // disposed before the rig: a probe that was never cancelled must not hold its loop
            var time = rig.Engine.Time;
            decoder.Time = time;
            probe.Time = time;
            rig.Engine.Call(() => rig.Session.Start(rig.Request(2) with { File = first })).IsSuccess.ShouldBeTrue();
            rig.Engine.Render(300);

            // E2's listing takes 1.2 s of the 3 s budget, so its probe limit is cut to 1.8 s; the wrapper then enters 200 ms late and
            // the adapter never answers. The limit would end at 3.2 s, after the press's hard bound (3.05 s).
            var held = 0;
            time.BeforeRead = () =>
            {
                if (!decoder.Done || !IsProbeWrapper() || Interlocked.Exchange(ref held, 1) == 1)
                {
                    return;
                }

                time.SpinUntil(time.GetTimestamp() + (long)(0.2 * time.TimestampFrequency), release.Token);
            };

            rig.Engine.Call(() => rig.Session.NextFile());

            held.ShouldBe(1, "the probe wrapper was never held back");
            probe.Probed.ShouldBe(["Episode-02.mkv"]);
            rig.Events.OfType<FileSkipped>().ShouldBeEmpty("a file the session gave up on while its probe was still running was judged and reported skipped");
            rig.Events.OfType<NoNeighbour>().ShouldBeEmpty("a file nobody could judge ended the press as no neighbour");
            rig.Events.OfType<NeighbourSearchTimedOut>().ShouldHaveSingleItem("the press did not end as a search that ran out of time");
            probe.TokenOf("Episode-02.mkv").IsCancellationRequested.ShouldBeTrue("the session gave up on the probe but never cancelled its worker");

            RigLoops.WaitForNeighbourWorkers(rig.Session);
            rig.Engine.Call(() => rig.Session.NextFile());

            probe.Probed.ShouldBe(["Episode-02.mkv", "Episode-02.mkv"], "the file was judged on the first press and never probed again");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void A_probe_wrapper_that_ran_its_full_limit_without_an_answer_gets_its_file_reported_skipped_and_cancelled_AC_09b()
    {
        var folder = Directory.CreateTempSubdirectory("dualplayer-fulllimit-").FullName;
        using var release = new CancellationTokenSource();
        try
        {
            var first = Path.Combine(folder, "Episode-01.mkv");
            File.WriteAllBytes(first, []);
            File.WriteAllBytes(Path.Combine(folder, "Episode-02.mkv"), []);
            var decoder = new SlowListingDecoder(TimeSpan.Zero);
            var probe = new NeverAnsweringProbe(release.Token);
            using var rig = new SessionRig([new RigDevice(48_000), new RigDevice(48_000)], null, 600, neighbourDecoder: decoder, neighbourPictureProbe: probe, barrierEvery: NeighbourRig.BarrierEvery);
            using var releaseFirst = new Defer(release.Cancel); // disposed before the rig: a probe that was never cancelled must not hold its loop
            decoder.Time = rig.Engine.Time;
            probe.Time = rig.Engine.Time;
            rig.Engine.Call(() => rig.Session.Start(rig.Request(2) with { File = first })).IsSuccess.ShouldBeTrue();
            rig.Engine.Render(300);

            // E2's listing is instant, so its probe gets its full, uncut 2 s limit; the wrapper enters at once and the adapter never
            // answers: the file is slow, so it is reported skipped (and remembered), like a probe that answered at its limit.
            rig.Engine.Call(() => rig.Session.NextFile());

            probe.Probed.ShouldBe(["Episode-02.mkv"]);
            rig.Events.OfType<FileSkipped>().ShouldHaveSingleItem("a file whose probe ran its full limit was not reported skipped").File.ShouldBe("Episode-02.mkv");
            rig.Events.OfType<NoNeighbour>().ShouldBeEmpty("a file that was not judged ended the press as no neighbour");
            rig.Events.OfType<NeighbourSearchTimedOut>().ShouldHaveSingleItem("the press did not end as a search that ran out of time");
            probe.TokenOf("Episode-02.mkv").IsCancellationRequested.ShouldBeTrue("the session gave up on the probe but never cancelled its worker");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void A_probe_wrapper_that_never_enters_before_the_press_bound_does_not_get_its_file_remembered_as_timed_out_AC_09b()
    {
        var folder = Directory.CreateTempSubdirectory("dualplayer-neverentered-").FullName;
        using var release = new CancellationTokenSource();
        try
        {
            var first = Path.Combine(folder, "Episode-01.mkv");
            var second = Path.Combine(folder, "Episode-02.mkv");
            File.WriteAllBytes(first, []);
            File.WriteAllBytes(second, []);
            File.WriteAllBytes(Path.Combine(folder, "Episode-03.mkv"), []);
            var rig = new SessionRig([new RigDevice(48_000), new RigDevice(48_000)], null, 600, barrierEvery: NeighbourRig.BarrierEvery);
            using var _ = rig;
            using var releaseFirst = new Defer(release.Cancel);
            var time = rig.Engine.Time;
            rig.Engine.Call(() => rig.Session.Start(rig.Request(2) with { File = first })).IsSuccess.ShouldBeTrue();
            rig.Engine.Render(300);

            // The OS starts the probe wrapper's thread so late that the press gives up before it has entered: the adapter never ran.
            var held = 0;
            time.BeforeRead = () =>
            {
                if (!IsProbeWrapper() || Interlocked.Exchange(ref held, 1) == 1)
                {
                    return;
                }

                time.SpinUntil(time.GetTimestamp() + (long)(5 * time.TimestampFrequency), release.Token);
            };

            rig.Engine.Call(() => rig.Session.NextFile());
            held.ShouldBe(1, "the probe wrapper was never held back");
            rig.Events.OfType<FileChanged>().ShouldBeEmpty("the press chose a file although its probe wrapper had not entered");
            release.Cancel(); // the thread starts at last, sees the press gave up and returns without calling the adapter (Z5)
            RigLoops.WaitForNeighbourWorkers(rig.Session);
            rig.Pictures.Probed.ShouldBeEmpty("the late wrapper called the adapter for a file the press had already given up on");

            rig.Engine.Call(() => rig.Session.NextFile());

            rig.Events.OfType<FileChanged>().ShouldHaveSingleItem("the file whose wrapper never entered was remembered as timed out, so the press went past it").File.ShouldBe(second);
            rig.Pictures.Probed.ShouldBe([second]);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void A_first_file_whose_listing_cut_the_probe_limit_and_whose_wrapper_never_answers_is_not_reported_skipped_AC_08()
    {
        var folder = Directory.CreateTempSubdirectory("dualplayer-cutfirst-").FullName;
        using var release = new CancellationTokenSource();
        try
        {
            var first = Path.Combine(folder, "Episode-01.mkv");
            File.WriteAllBytes(first, []);
            File.WriteAllBytes(Path.Combine(folder, "Episode-02.mkv"), []);
            var decoder = new SlowListingDecoder(TimeSpan.FromMilliseconds(1200));
            var probe = new NeverAnsweringProbe(release.Token);
            using var rig = new SessionRig([new RigDevice(48_000), new RigDevice(48_000)], null, 600, neighbourDecoder: decoder, neighbourPictureProbe: probe, barrierEvery: NeighbourRig.BarrierEvery);
            using var releaseFirst = new Defer(release.Cancel);
            decoder.Time = rig.Engine.Time;
            probe.Time = rig.Engine.Time;
            rig.Engine.Call(() => rig.Session.Start(rig.Request(2) with { File = first })).IsSuccess.ShouldBeTrue();
            rig.Engine.Render(300);

            // E2 is the first file of the press and has the whole budget; its 1.2 s listing cuts the probe limit to 1.8 s, and the
            // wrapper enters at once and never answers: the probe was cut short by the budget, so the file is not reported skipped.
            rig.Engine.Call(() => rig.Session.NextFile());

            probe.Probed.ShouldBe(["Episode-02.mkv"]);
            rig.Events.OfType<FileSkipped>().ShouldBeEmpty("a file whose probe was cut short by the budget was reported skipped");
            rig.Events.OfType<NoNeighbour>().ShouldBeEmpty("a file nobody could judge ended the press as no neighbour");
            rig.Events.OfType<NeighbourSearchTimedOut>().ShouldHaveSingleItem("the press did not end as a search that ran out of time");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void A_probe_wrapper_whose_thread_starts_after_the_press_gave_up_never_calls_the_adapter_AC_09b()
    {
        var folder = Directory.CreateTempSubdirectory("dualplayer-lateadapter-").FullName;
        using var release = new CancellationTokenSource();
        try
        {
            var first = Path.Combine(folder, "Episode-01.mkv");
            File.WriteAllBytes(first, []);
            File.WriteAllBytes(Path.Combine(folder, "Episode-02.mkv"), []);
            var rig = new SessionRig([new RigDevice(48_000), new RigDevice(48_000)], null, 600, barrierEvery: NeighbourRig.BarrierEvery);
            using var _ = rig;
            using var releaseFirst = new Defer(release.Cancel);
            var time = rig.Engine.Time;
            rig.Engine.Call(() => rig.Session.Start(rig.Request(2) with { File = first })).IsSuccess.ShouldBeTrue();
            rig.Engine.Render(300);

            var held = 0;
            time.BeforeRead = () =>
            {
                if (!IsProbeWrapper() || Interlocked.Exchange(ref held, 1) == 1)
                {
                    return;
                }

                time.SpinUntil(time.GetTimestamp() + (long)(5 * time.TimestampFrequency), release.Token);
            };

            rig.Engine.Call(() => rig.Session.NextFile());
            held.ShouldBe(1, "the probe wrapper was never held back");

            release.Cancel(); // the wrapper's thread starts at last, after the press gave up and cancelled it
            RigLoops.WaitForNeighbourWorkers(rig.Session);

            rig.Pictures.Probed.ShouldBeEmpty("the wrapper called the adapter for a file the press had already given up on");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan Slack = TimeSpan.FromMilliseconds(50);

    // How far past a give-up the rig's racing fake time may run before the session's next look at the clock.
    private static readonly TimeSpan Tolerance = TimeSpan.FromMilliseconds(250);

    // The probe wrapper reads the time first thing, directly from its lambda inside CheckPlayable: the first frame below the
    // clock and this test is that lambda (the session's own reads come from CheckPlayable itself, not a lambda).
    private static bool IsProbeWrapper() =>
        new System.Diagnostics.StackTrace().GetFrames()
            .Select(f => f.GetMethod())
            .FirstOrDefault(m => m?.DeclaringType is { } t
                && !typeof(TimeProvider).IsAssignableFrom(t)
                && t != typeof(PlaybackSessionNeighbourWorkerTimingTests)
                && t.DeclaringType != typeof(PlaybackSessionNeighbourWorkerTimingTests))
            ?.Name.StartsWith("<CheckPlayable>", StringComparison.Ordinal) == true;

    private sealed class Defer(Action action) : IDisposable
    {
        public void Dispose() => action();
    }

    /// <summary>Lists tracks 1 and 2 for every file, each listing costing <paramref name="cost"/> of the session's fake time.</summary>
    private sealed class SlowListingDecoder(TimeSpan cost) : IAudioDecoder
    {
        private int _done;

        public TimeProvider? Time { get; set; }

        /// <summary>True once a listing has returned (the probe wrapper starts after it).</summary>
        public bool Done => Volatile.Read(ref _done) == 1;

        public Result<IReadOnlyList<AudioTrackInfo>> ListTracks(string path)
        {
            var time = Time!;
            time.SpinUntil(time.GetTimestamp() + (long)(cost.TotalSeconds * time.TimestampFrequency));
            Volatile.Write(ref _done, 1);
            return Result<IReadOnlyList<AudioTrackInfo>>.Success([.. new[] { 1, 2 }.Select(n => new AudioTrackInfo(n, "und", ChannelLayout.Stereo, 48_000))]);
        }

        public Result<IDecodeSession> Open(string path, IReadOnlyList<DecodeRequest> outputs) =>
            Result<IDecodeSession>.Failure("test.not_decoding", "The listing decoder does not decode.");
    }

    /// <summary>A probe whose adapter never answers on its own: it keeps running until the session cancels it (or the test lets it go).</summary>
    private sealed class NeverAnsweringProbe(CancellationToken release) : IPictureProbe
    {
        private readonly Lock _gate = new();
        private readonly Dictionary<string, CancellationToken> _tokens = new(StringComparer.Ordinal);
        private readonly List<string> _probed = [];

        public TimeProvider? Time { get; set; }

        public List<string> Probed
        {
            get
            {
                lock (_gate)
                {
                    return [.. _probed];
                }
            }
        }

        public CancellationToken TokenOf(string fileName)
        {
            lock (_gate)
            {
                return _tokens[fileName];
            }
        }

        public Result<PictureInfo> Probe(string path, TimeSpan timeLimit) => Probe(path, timeLimit, CancellationToken.None);

        public Result<PictureInfo> Probe(string path, TimeSpan timeLimit, CancellationToken cancel)
        {
            lock (_gate)
            {
                _probed.Add(Path.GetFileName(path));
                _tokens[Path.GetFileName(path)] = cancel;
            }

            using var either = CancellationTokenSource.CreateLinkedTokenSource(cancel, release);
            Time!.SpinUntil(long.MaxValue, either.Token);
            return Result<PictureInfo>.Failure("video.probe_timeout", "The picture probe never answered.");
        }
    }
}
