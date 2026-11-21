using DualPlayer.Core.Playback;
using DualPlayer.Core.Sync;
using DualPlayer.Core.Tests.Fakes;

namespace DualPlayer.Core.Tests.Playback;

/// <summary>T50 (Q5, Q6): output errors are polled once per engine reading, recovery gives up after its budget, and picture timing is a hold while an align is pending.</summary>
public sealed class PlaybackSessionRecoveryBudgetTests
{
    private static readonly RigDevice[] TwoDevices = [new RigDevice(48_000), new RigDevice(48_000)];

    private static SessionRig PlayingRig(Func<IOutputLagControl, IOutputLagControl>? wrap = null)
    {
        var rig = new SessionRig(TwoDevices, null, 600, wrapLags: wrap);
        rig.Probe.Set("dev-1", new DeviceLatencyReport(TimeSpan.FromMilliseconds(200), "test", ConnectionKind.Usb, FormFactor.Headphones));
        rig.Probe.Set("dev-2", new DeviceLatencyReport(TimeSpan.Zero, "test", ConnectionKind.Usb, FormFactor.Speaker));
        rig.Engine.Call(() => rig.Session.Start(rig.Request(2))).IsSuccess.ShouldBeTrue();
        rig.Engine.Render(300);
        return rig;
    }

    [Fact]
    public void An_output_that_never_recovers_gives_up_once_after_the_budget_with_a_bounded_number_of_engine_queries_AC_17()
    {
        var queries = new QueryCount();
        using var rig = PlayingRig(inner => new CountingLags(inner, queries));
        rig.ForceOutputError("dev-2", 30);
        var before = queries.Value;

        rig.Engine.Call(() => rig.Session.Seek(TimeSpan.FromSeconds(10))).IsSuccess.ShouldBeTrue();
        rig.Engine.Render(200); // two seconds on the fake clock, past the 1 s budget
        var atGiveUp = queries.Value;
        rig.Engine.Render(300); // three more seconds: polling must have stopped
        var later = queries.Value;

        var gaveUp = rig.Events.OfType<NotLipSynced>().ShouldHaveSingleItem("expected exactly one give-up record");
        gaveUp.Command.ShouldBe("seek");
        gaveUp.AfterCommandMs.ShouldBeInRange(1000, 1500);
        gaveUp.OutputErrorsMs.Keys.ShouldBe(["dev-1", "dev-2"], ignoreOrder: true);
        gaveUp.OutputErrorsMs["dev-2"].ShouldBe(30, 1e-9);
        rig.Events.OfType<LipSyncedAgain>().Any(e => e.Command == "seek").ShouldBeFalse();
        (atGiveUp - before).ShouldBeLessThanOrEqualTo(25, "the engine was asked on every 5 ms tick, not once per 100 ms reading");
        (later - atGiveUp).ShouldBe(0, "polling went on after giving up");
    }

    [Fact]
    public void A_start_lip_synced_1_5_s_after_its_command_is_inside_the_3_s_start_budget_AC_17()
    {
        using var rig = new SessionRig(TwoDevices, null, 600);
        var commandAt = rig.Engine.Time.GetTimestamp() - (rig.Engine.Time.TimestampFrequency * 3 / 2);
        rig.Engine.Call(() => rig.Session.Start(rig.Request(2) with { CommandTimestamp = commandAt })).IsSuccess.ShouldBeTrue();
        rig.Engine.Render(300);

        var again = rig.Events.OfType<LipSyncedAgain>().Where(e => e.Command == "start").ShouldHaveSingleItem("the start's lip-synced record was never published");
        again.AfterCommandMs.ShouldBeInRange(1500, 3000);
        rig.Events.OfType<NotLipSynced>().ShouldBeEmpty("a start lip-synced inside its 3 s budget was logged as a give-up");
    }

    [Theory]
    [InlineData(3)] // resume 30 ms after the pause
    [InlineData(15)] // resume 150 ms after the pause
    public void Resume_before_the_picture_froze_publishes_no_picture_timing_off_by_more_than_20_ms_AC_04(int stepsBeforeResume)
    {
        using var rig = PlayingRig();
        rig.Engine.Call(() => rig.Session.Pause()).IsSuccess.ShouldBeTrue();
        rig.Engine.Render(stepsBeforeResume);
        var seen = rig.Events.Count;

        rig.Engine.Call(() => rig.Session.Resume()).IsSuccess.ShouldBeTrue();
        rig.Engine.Render(60);

        var timings = rig.Events.Skip(seen).OfType<PictureTimingReported>().ToList();
        foreach (var t in timings)
        {
            Math.Abs(t.ErrorMs).ShouldBeLessThanOrEqualTo(20, "picture timing was reported against a stale hold while an align was pending");
        }
    }

    private sealed class QueryCount
    {
        private int _value;

        public int Value => Volatile.Read(ref _value);

        public void Add() => Interlocked.Increment(ref _value);
    }

    private sealed class CountingLags(IOutputLagControl inner, QueryCount count) : IOutputLagControl
    {
        public long? FrameZeroTimestamp => inner.FrameZeroTimestamp;

        public long? ClockStartTimestamp => inner.ClockStartTimestamp;

        public bool IsRamping => inner.IsRamping;

        public Result SetLagTargets(IReadOnlyDictionary<string, TimeSpan> targets, LagChangeMode mode, TimeSpan moveDuration = default) => inner.SetLagTargets(targets, mode, moveDuration);

        public TimeSpan? CurrentLag(string deviceId) => inner.CurrentLag(deviceId);

        public long? FirstAudioTimestamp(string deviceId) => inner.FirstAudioTimestamp(deviceId);

        public IReadOnlyDictionary<string, OutputPositionError> PositionErrors()
        {
            count.Add();
            return inner.PositionErrors();
        }
    }
}
