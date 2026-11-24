using DualPlayer.Core.Playback;
using DualPlayer.Core.Sync;
using DualPlayer.Core.Tests.Fakes;

namespace DualPlayer.Core.Tests.Playback;

/// <summary>T24: session commands, timer callbacks and renderer events run one at a time on one session thread.</summary>
public sealed class PlaybackSessionSerializationTests
{
    private static readonly RigDevice[] TwoDevices = [new RigDevice(48_000), new RigDevice(48_000)];

    private static SessionRig PlayingRig()
    {
        var rig = new SessionRig(TwoDevices, null, 600);
        rig.Probe.Set("dev-1", new DeviceLatencyReport(TimeSpan.FromMilliseconds(40), "test", ConnectionKind.Bluetooth, FormFactor.Headphones));
        rig.Probe.Set("dev-2", new DeviceLatencyReport(TimeSpan.Zero, "test", ConnectionKind.Usb, FormFactor.Speaker));
        rig.Engine.Call(() => rig.Session.Start(rig.Request(2))).IsSuccess.ShouldBeTrue();
        rig.Engine.Render(300);
        return rig;
    }

    /// <summary>Starts an offset command that blocks inside the renderer's speed change until <paramref name="release"/> is set.</summary>
    private static Task<Result> BlockedOffsetCommand(SessionRig rig, ManualResetEventSlim entered, ManualResetEventSlim release)
    {
        rig.Renderer.BeforeSetSpeed = () =>
        {
            entered.Set();
            release.Wait(TimeSpan.FromSeconds(10));
        };
        var command = Task.Factory.StartNew(
            () => rig.Session.ChangeOffset(1, 10),
            TestContext.Current.CancellationToken,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
        entered.Wait(TimeSpan.FromSeconds(10)).ShouldBeTrue("the offset command never reached the renderer");
        return command;
    }

    [Fact]
    public async Task A_picture_failure_raised_while_a_command_runs_waits_for_the_command_and_never_blocks_the_raising_thread()
    {
        using var rig = PlayingRig();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var command = BlockedOffsetCommand(rig, entered, release);

        var raised = Task.Run(() => rig.Renderer.InjectDecodeFailure(new Error("video.decode_failed", "broken")), TestContext.Current.CancellationToken);

        (await RigLoops.CompletesWithin(raised, TimeSpan.FromSeconds(2))).ShouldBeTrue("the renderer's event thread waited for the session");
        rig.Session.State.ShouldBe(PlaybackState.Playing, "the failure ran while the offset command was still inside the renderer");

        rig.Renderer.BeforeSetSpeed = null;
        release.Set();
        (await RigLoops.CompletesWithin(command, TimeSpan.FromSeconds(10))).ShouldBeTrue();
        rig.Engine.WaitFor(() => rig.Session.State == PlaybackState.Ended).ShouldBeTrue("the failure was lost instead of running after the command");
    }

    [Fact]
    public async Task A_health_tick_that_falls_due_while_a_command_runs_waits_for_the_command()
    {
        using var rig = PlayingRig();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var command = BlockedOffsetCommand(rig, entered, release);
        var timingsBefore = rig.Events.OfType<PictureTimingReported>().Count();

        // A second of fake time: the health timer falls due on this (test) thread.
        var ticked = Task.Run(() => rig.Engine.Time.Advance(TimeSpan.FromSeconds(1)), TestContext.Current.CancellationToken);

        (await RigLoops.CompletesWithin(ticked, TimeSpan.FromSeconds(2))).ShouldBeTrue("the timer thread waited for the session");
        rig.Events.OfType<PictureTimingReported>().Count().ShouldBe(timingsBefore, "the health tick ran while the offset command was still inside the renderer");

        rig.Renderer.BeforeSetSpeed = null;
        release.Set();
        (await RigLoops.CompletesWithin(command, TimeSpan.FromSeconds(10))).ShouldBeTrue();
        rig.Engine.WaitFor(() => rig.Events.OfType<PictureTimingReported>().Count() > timingsBefore).ShouldBeTrue("the health tick was lost");
    }

    [Fact]
    public async Task A_command_from_a_second_thread_waits_until_the_first_command_has_finished()
    {
        var folder = Directory.CreateTempSubdirectory("dualplayer-serial-").FullName;
        try
        {
            var current = Path.Combine(folder, "a.mkv");
            File.WriteAllBytes(current, []);
            File.WriteAllBytes(Path.Combine(folder, "b.mkv"), []);
            using var rig = new SessionRig(TwoDevices, null, 600);
            rig.Decoder.Script("b.mkv", 1); // no track 2: skipped, so the next-file command never starts a new film
            rig.Engine.Call(() => rig.Session.Start(rig.Request(2) with { File = current })).IsSuccess.ShouldBeTrue();
            rig.Engine.Render(300);

            using var entered = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            rig.Decoder.BeforeList = () =>
            {
                entered.Set();
                release.Wait(TimeSpan.FromSeconds(10));
            };
            var first = Task.Factory.StartNew(rig.Session.NextFile, TestContext.Current.CancellationToken, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            entered.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken).ShouldBeTrue("the next-file command never reached the folder scan");

            var second = Task.Factory.StartNew(() => rig.Session.ChangeOffset(2, 10), TestContext.Current.CancellationToken, TaskCreationOptions.LongRunning, TaskScheduler.Default);

            (await RigLoops.CompletesWithin(second, TimeSpan.FromMilliseconds(500))).ShouldBeFalse("the offset command ran while the next-file command was still scanning the folder");

            release.Set();
            (await RigLoops.CompletesWithin(first, TimeSpan.FromSeconds(10))).ShouldBeTrue();
            (await RigLoops.CompletesWithin(second, TimeSpan.FromSeconds(10))).ShouldBeTrue();
            rig.Events.OfType<NoNeighbour>().ShouldHaveSingleItem();
            rig.Events.OfType<OffsetChanged>().ShouldHaveSingleItem();
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task Exit_during_the_start_waits_for_the_start_and_only_then_disposes_the_renderer_and_stops_the_sound()
    {
        using var rig = new SessionRig(TwoDevices, null, 600);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        rig.Renderer.AfterLoad = () =>
        {
            entered.Set();
            release.Wait(TimeSpan.FromSeconds(10));
        };
        var start = Task.Factory.StartNew(() => rig.Engine.Call(() => rig.Session.Start(rig.Request(2))), TestContext.Current.CancellationToken, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        entered.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken).ShouldBeTrue();

        var exit = Task.Factory.StartNew(rig.Session.Exit, TestContext.Current.CancellationToken, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        (await RigLoops.CompletesWithin(exit, TimeSpan.FromMilliseconds(300))).ShouldBeFalse("Esc ran inside the start");
        rig.Renderer.IsDisposed.ShouldBeFalse("the renderer was disposed under the start");

        release.Set();
        (await RigLoops.CompletesWithin(start, TimeSpan.FromSeconds(30))).ShouldBeTrue();
        (await RigLoops.CompletesWithin(exit, TimeSpan.FromSeconds(10))).ShouldBeTrue();
        rig.Renderer.IsDisposed.ShouldBeTrue();
        rig.Session.State.ShouldBe(PlaybackState.Ended);
        rig.Engine.WaitFor(() => rig.Engine.Summary is not null).ShouldBeTrue("the sound was not stopped after the start");
    }
}
