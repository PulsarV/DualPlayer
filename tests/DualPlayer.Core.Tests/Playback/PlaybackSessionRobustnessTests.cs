using DualPlayer.Core.Playback;
using DualPlayer.Core.Sync;
using DualPlayer.Core.Tests.Fakes;

namespace DualPlayer.Core.Tests.Playback;

/// <summary>T35: background callbacks never escape, and the picture starts from the clock the engine really started.</summary>
public sealed class PlaybackSessionRobustnessTests
{
    private static readonly RigDevice[] TwoDevices = [new RigDevice(48_000), new RigDevice(48_000)];

    private static void Arrange(SessionRig rig)
    {
        rig.Probe.Set("dev-1", new DeviceLatencyReport(TimeSpan.FromMilliseconds(40), "test", ConnectionKind.Bluetooth, FormFactor.Headphones));
        rig.Probe.Set("dev-2", new DeviceLatencyReport(TimeSpan.Zero, "test", ConnectionKind.Usb, FormFactor.Speaker));
    }

    [Fact]
    public void A_renderer_that_throws_when_a_picture_delay_move_ends_is_logged_on_the_session_thread_and_the_film_goes_on()
    {
        using var rig = new SessionRig(TwoDevices, null, 600);
        Arrange(rig);
        rig.Engine.Call(() => rig.Session.Start(rig.Request(2))).IsSuccess.ShouldBeTrue();
        rig.Engine.Render(300);
        rig.Engine.Call(() => rig.Session.ChangeOffset(1, 10)).IsSuccess.ShouldBeTrue(); // a 10 ms move: about 5 s

        // Paused, nothing ticks the picture: the move ends on its own timer.
        rig.Engine.Call(() => rig.Session.Pause()).IsSuccess.ShouldBeTrue();

        var fail = true;
        rig.Renderer.BeforeSetSpeed = () =>
        {
            // Paused, the only speed change is the move timer's own, when the move ends.
            if (fail)
            {
                fail = false;
                throw new InvalidOperationException("mpv went away");
            }
        };

        Should.NotThrow(() => rig.Engine.Render(700), "a timer callback threw into the thread that fired it");
        fail.ShouldBeFalse("the move never ended on its timer");
        rig.Engine.Call(() => rig.Session.Resume()).IsSuccess.ShouldBeTrue("the session stopped answering after the fault");
    }

    [Fact]
    public void The_picture_starts_from_the_clock_the_engine_started_when_an_output_dropped_out_of_the_pre_roll()
    {
        // As if dev-2 (lag 240 ms) was lost in the pre-roll: the engine's frame zero then follows dev-1's lag (0), so it
        // equals the clock start, not the clock start plus the session's largest lag.
        using var rig = new SessionRig(TwoDevices, null, 600, wrapLags: inner => new FrameZeroWithoutTheSlowestLag(inner, (long)(0.240 * new Microsoft.Extensions.Time.Testing.FakeTimeProvider().TimestampFrequency)));
        Arrange(rig);
        rig.Engine.Call(() => rig.Session.Start(rig.Request(2))).IsSuccess.ShouldBeTrue();
        rig.Engine.Render(300);

        // The clock really started 240 ms before the engine's own frame zero; the picture is due at clock - 240 ms.
        var clockStart = rig.Engine.Engine.FrameZeroTimestamp!.Value - (long)(0.240 * rig.Engine.Time.TimestampFrequency);
        var due = rig.Engine.Time.GetElapsedTime(clockStart) - TimeSpan.FromMilliseconds(240);
        Math.Abs((rig.Renderer.ShownNow - due).TotalMilliseconds).ShouldBeLessThanOrEqualTo(20, "the picture started a lag early");
    }

    private sealed class FrameZeroWithoutTheSlowestLag(IOutputLagControl inner, long slowestLagTicks) : IOutputLagControl
    {
        public long? FrameZeroTimestamp => inner.FrameZeroTimestamp is { } t ? t - slowestLagTicks : null;

        public long? ClockStartTimestamp => inner.FrameZeroTimestamp is { } t ? t - slowestLagTicks : null;

        public bool IsRamping => inner.IsRamping;

        public IReadOnlyDictionary<string, OutputPositionError> PositionErrors() => inner.PositionErrors();

        public Result SetLagTargets(IReadOnlyDictionary<string, TimeSpan> targets, LagChangeMode mode, TimeSpan moveDuration = default) => inner.SetLagTargets(targets, mode, moveDuration);

        public TimeSpan? CurrentLag(string deviceId) => inner.CurrentLag(deviceId);

        public long? FirstAudioTimestamp(string deviceId) => inner.FirstAudioTimestamp(deviceId);
    }
}
