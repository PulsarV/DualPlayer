using DualPlayer.Core.Playback;
using DualPlayer.Core.Sync;
using DualPlayer.Core.Tests.Fakes;

namespace DualPlayer.Core.Tests.Playback;

/// <summary>
/// T85 (P8, P9): a key's recovery is timed from the instant the key arrived (stamped by the caller, as a start's
/// <c>CommandTimestamp</c> is), however long it waited behind earlier commands; output readings count as fresh only when
/// taken after the engine command returned.
/// </summary>
public sealed class PlaybackSessionKeyArrivalTests
{
    private static readonly RigDevice[] TwoDevices = [new RigDevice(48_000), new RigDevice(48_000)];

    private static SessionRig PlayingRig(Func<IOutputLagControl, IOutputLagControl>? wrapLags = null)
    {
        var rig = new SessionRig(TwoDevices, null, 600, wrapLags: wrapLags);
        rig.Probe.Set("dev-1", new DeviceLatencyReport(TimeSpan.FromMilliseconds(40), "test", ConnectionKind.Bluetooth, FormFactor.Headphones));
        rig.Probe.Set("dev-2", new DeviceLatencyReport(TimeSpan.Zero, "test", ConnectionKind.Usb, FormFactor.Speaker));
        rig.Engine.Call(() => rig.Session.Start(rig.Request(2))).IsSuccess.ShouldBeTrue();
        rig.Engine.Render(300);
        return rig;
    }

    [Fact]
    public void A_seek_that_waited_300_ms_behind_another_command_reports_the_time_since_the_key_press_AC_04()
    {
        using var rig = PlayingRig();
        var keyPressed = rig.Engine.Time.GetTimestamp();
        rig.Engine.Render(30); // the key waits 300 ms in the queue

        rig.Engine.Call(() => rig.Session.Seek(TimeSpan.FromSeconds(10), keyPressed)).IsSuccess.ShouldBeTrue();
        rig.Engine.Render(200);

        var again = rig.Events.OfType<LipSyncedAgain>().Where(e => e.Command == "seek").ShouldHaveSingleItem("the seek's lip-synced record was never published");
        again.AfterCommandMs.ShouldBeGreaterThanOrEqualTo(300, "the key's queue wait was left out of the time since the key press");
    }

    [Fact]
    public void A_seek_that_waited_1500_ms_behind_another_command_is_given_up_on_its_1_s_budget_counted_from_the_key_AC_04()
    {
        using var rig = PlayingRig();
        var keyPressed = rig.Engine.Time.GetTimestamp();
        rig.Engine.Render(150); // the key waits 1.5 s in the queue, past the 1 s recovery budget

        rig.Engine.Call(() => rig.Session.Seek(TimeSpan.FromSeconds(10), keyPressed)).IsSuccess.ShouldBeTrue();
        rig.Engine.Render(200);

        rig.Events.OfType<LipSyncedAgain>().Where(e => e.Command == "seek").ShouldBeEmpty("the seek passed a 1 s budget although the key was pressed 1.5 s before");
        var gaveUp = rig.Events.OfType<NotLipSynced>().Where(e => e.Command == "seek").ShouldHaveSingleItem("the seek was not given up on though its budget from the key had run out");
        gaveUp.AfterCommandMs.ShouldBeGreaterThanOrEqualTo(1500, "the key's queue wait was left out of the time since the key press");
    }

    [Fact]
    public void A_resume_that_waited_300_ms_behind_another_command_reports_the_time_since_the_key_press_AC_04()
    {
        using var rig = PlayingRig();
        rig.Engine.Call(() => rig.Session.Pause()).IsSuccess.ShouldBeTrue();
        rig.Engine.Render(20);
        var keyPressed = rig.Engine.Time.GetTimestamp();
        rig.Engine.Render(30);

        rig.Engine.Call(() => rig.Session.Resume(keyPressed)).IsSuccess.ShouldBeTrue();
        rig.Engine.Render(200);

        var again = rig.Events.OfType<LipSyncedAgain>().Where(e => e.Command == "resume").ShouldHaveSingleItem("the resume's lip-synced record was never published");
        again.AfterCommandMs.ShouldBeGreaterThanOrEqualTo(300, "the key's queue wait was left out of the time since the key press");
    }

    [Fact]
    public void An_output_reading_taken_between_the_key_and_the_engine_seek_does_not_let_lip_synced_again_fire_AC_17()
    {
        var stale = new StaleReadings();
        using var rig = PlayingRig(inner => new StaleReadingLags(inner, stale));
        var keyPressed = rig.Engine.Time.GetTimestamp();
        rig.Engine.Render(30); // the key waits 300 ms; the readings below are stamped 100 ms after the key, before the engine seeks
        stale.At = keyPressed + (rig.Engine.Time.TimestampFrequency / 10);

        rig.Engine.Call(() => rig.Session.Seek(TimeSpan.FromSeconds(10), keyPressed)).IsSuccess.ShouldBeTrue();
        rig.Engine.Render(200);

        rig.Events.OfType<LipSyncedAgain>().Where(e => e.Command == "seek").ShouldBeEmpty("the outputs were judged on readings taken before the engine's seek");
        rig.Events.OfType<NotLipSynced>().Where(e => e.Command == "seek").ShouldHaveSingleItem("no fresh reading arrived, so the seek should have been given up on its budget");
    }

    private sealed class StaleReadings
    {
        public long At { get; set; }
    }

    /// <summary>Every output reports a perfect reading that was taken at a fixed instant and never refreshed.</summary>
    private sealed class StaleReadingLags(IOutputLagControl inner, StaleReadings stale) : IOutputLagControl
    {
        public long? FrameZeroTimestamp => inner.FrameZeroTimestamp;

        public long? ClockStartTimestamp => inner.ClockStartTimestamp;

        public bool IsRamping => inner.IsRamping;

        public Result SetLagTargets(IReadOnlyDictionary<string, TimeSpan> targets, LagChangeMode mode, TimeSpan moveDuration = default) => inner.SetLagTargets(targets, mode, moveDuration);

        public TimeSpan? CurrentLag(string deviceId) => inner.CurrentLag(deviceId);

        public IReadOnlyDictionary<string, OutputSpeed> AppliedSpeeds() => inner.AppliedSpeeds();

        public long? FirstAudioTimestamp(string deviceId) => inner.FirstAudioTimestamp(deviceId);

        public IReadOnlyDictionary<string, OutputPositionError> PositionErrors() =>
            stale.At == 0
                ? inner.PositionErrors()
                : inner.PositionErrors().ToDictionary(r => r.Key, _ => new OutputPositionError(0, stale.At), StringComparer.Ordinal);
    }
}
