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
}
