using DualPlayer.Core.Outputs;
using DualPlayer.Core.Playback;
using Microsoft.Extensions.Time.Testing;

namespace DualPlayer.Core.Tests.Fakes;

public sealed class RigLoopsTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(5);

    private sealed class Silent : IRenderSource
    {
        public void Render(Span<float> buffer, DevicePosition position) => buffer.Clear();
    }

    private static FakeAudioDevice StartedDevice(FakeTimeProvider time)
    {
        var device = new FakeAudioDevice(time, new MixFormat(48_000, 2, ChannelLayout.Stereo), TimeSpan.FromMilliseconds(100), new Silent(), 0);
        device.Start();
        return device;
    }

    [Fact]
    public async Task Filling_a_device_that_was_lost_returns_instead_of_spinning()
    {
        var time = new FakeTimeProvider();
        var device = StartedDevice(time);
        device.SimulateLoss();
        time.Advance(TimeSpan.FromMilliseconds(10));

        var fill = Task.Run(() => RigLoops.FillQueue(device, 960, 480), TestContext.Current.CancellationToken);

        (await RigLoops.CompletesWithin(fill, Bound)).ShouldBeTrue("FillQueue spun on a lost device");
    }

    [Fact]
    public async Task Filling_a_device_that_was_closed_returns_instead_of_spinning()
    {
        var time = new FakeTimeProvider();
        var device = StartedDevice(time);
        device.Close();
        time.Advance(TimeSpan.FromMilliseconds(10));

        var fill = Task.Run(() => RigLoops.FillQueue(device, 960, 480), TestContext.Current.CancellationToken);

        (await RigLoops.CompletesWithin(fill, Bound)).ShouldBeTrue("FillQueue spun on a closed device");
    }

    [Fact]
    public void Filling_an_open_device_queues_the_requested_depth()
    {
        var device = StartedDevice(new FakeTimeProvider());

        RigLoops.FillQueue(device, 960, 480);

        device.WrittenFrames.ShouldBe(960);
    }

    [Fact]
    public void Waiting_for_something_that_never_happens_fails_the_test()
    {
        Should.Throw<ShouldAssertException>(() => RigLoops.WaitOrFail(() => false, TimeSpan.FromMilliseconds(50), "a thing"));
    }

    [Fact]
    public void Waiting_for_something_that_holds_returns()
    {
        RigLoops.WaitOrFail(() => true, TimeSpan.FromMilliseconds(50), "a thing");
    }

    [Fact]
    public void The_engine_rig_barrier_fails_once_the_run_has_ended_so_engine_tests_cannot_pass_on_a_finished_run()
    {
        using var rig = new EngineRig([new RigDevice(48_000)], totalSeconds: 1);
        rig.Start();

        // Render past the film's end: the barrier of the first step after the end fails instead of passing silently.
        var failed = false;
        for (var i = 0; i < 1_000 && !failed; i++)
        {
            try
            {
                rig.Render(1);
            }
            catch (ShouldAssertException)
            {
                failed = true;
            }
        }

        failed.ShouldBeTrue("rendering went on past the end of the run");
        rig.Summary.ShouldNotBeNull().EndReason.ShouldBe(DualPlayer.Core.Diagnostics.RunEndReason.FileEnd);
        Should.Throw<ShouldAssertException>(rig.Barrier);
    }

    [Fact]
    public void A_session_rig_barrier_after_the_film_ended_waits_for_the_engine_without_starting_a_run()
    {
        using var rig = new SessionRig([new RigDevice(48_000), new RigDevice(48_000)], null, totalSeconds: 1);
        rig.Engine.Call(() => rig.Session.Start(rig.Request(2))).IsSuccess.ShouldBeTrue();
        rig.Engine.WaitFor(() =>
        {
            rig.Engine.Render(10);
            return rig.Engine.Summary is not null;
        }).ShouldBeTrue();

        rig.Engine.Barrier();
        rig.Engine.Render(10);

        rig.Engine.Engine.StartRun("film.mkv", rig.Request(2).Outputs, new EngineOptions()).Error?.Code.ShouldNotBe("playback.run_active", "a barrier left a phantom run behind");
    }

    [Fact]
    public void A_waiter_abandoned_in_an_earlier_call_does_not_move_fake_time_in_the_next_call()
    {
        using var rig = new EngineRig([new RigDevice(48_000)], totalSeconds: 1) { StrictGate = true };

        // Press 1: a fake enters its Waiting() scope and never leaves it (a Block/Never fake the session has given up on).
        rig.Call(() =>
        {
            rig.Time.Waiting();
            return 0;
        });

        // Press 2: no fake waits; the folder work takes real time only, so fake time must stand still.
        var moved = rig.Call(() =>
        {
            var before = rig.Time.GetTimestamp();
            Thread.Sleep(300);
            return rig.Time.GetTimestamp() - before;
        });

        moved.ShouldBe(0L, "an abandoned waiter of an earlier call kept moving fake time");
    }

    [Fact]
    public void Disposing_a_session_rig_with_a_stuck_command_returns_within_a_bounded_time()
    {
        var rig = new SessionRig([new RigDevice(48_000), new RigDevice(48_000)], null, 600);

        // A start nobody drives: the engine waits on fake time that never moves, so the session thread is stuck in the command.
        var stuck = new Thread(() => rig.Session.Start(rig.Request(2))) { IsBackground = true };
        stuck.Start();
        Thread.Sleep(500);

        var dispose = new Thread(rig.Dispose) { IsBackground = true };
        dispose.Start();

        dispose.Join(TimeSpan.FromSeconds(15)).ShouldBeTrue("SessionRig.Dispose hung on a stuck command: the engine must be disposed before the session");
    }
}
