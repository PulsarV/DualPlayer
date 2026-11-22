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
        var rig = new SessionRig([new RigDevice(48_000), new RigDevice(48_000)], null, 600);
        using var _ = rig;
        var time = rig.Engine.Time;
        var commandBegan = time.GetTimestamp();

        var workerSaw = rig.Engine.Call(() =>
        {
            var worker = Task.Run(() =>
            {
                Thread.Sleep(400); // the pool was busy: the worker starts late
                return time.GetTimestamp();
            });
            return worker.GetAwaiter().GetResult();
        });

        workerSaw.ShouldBe(commandBegan, "fake time ran on while the worker waited for the pool, so its limit could be cut before it began");
    }
}
