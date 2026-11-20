using DualPlayer.Core.Sync;
using DualPlayer.Core.Tests.Fakes;
using Shouldly;

namespace DualPlayer.Core.Tests.Sync;

public sealed class LatencyModelTests
{
    [Theory]
    [InlineData(ConnectionKind.Bluetooth, FormFactor.Headphones, DeviceType.BluetoothHeadphones)]
    [InlineData(ConnectionKind.Bluetooth, FormFactor.Headset, DeviceType.BluetoothHeadphones)]
    [InlineData(ConnectionKind.Bluetooth, FormFactor.Unknown, DeviceType.BluetoothHeadphones)]
    [InlineData(ConnectionKind.Bluetooth, FormFactor.Speaker, DeviceType.BluetoothSpeaker)]
    [InlineData(ConnectionKind.Usb, FormFactor.Headphones, DeviceType.Wired)]
    [InlineData(ConnectionKind.Usb, FormFactor.Speaker, DeviceType.Wired)]
    [InlineData(ConnectionKind.Analog, FormFactor.Speaker, DeviceType.Other)]
    [InlineData(ConnectionKind.DisplayHdmi, FormFactor.Display, DeviceType.Hdmi)]
    [InlineData(ConnectionKind.DisplayPort, FormFactor.Display, DeviceType.Hdmi)]
    [InlineData(ConnectionKind.Unknown, FormFactor.Unknown, DeviceType.Other)]
    public void Device_type_is_taken_from_connection_kind_and_form_factor(ConnectionKind c, FormFactor f, DeviceType expected) =>
        DeviceTypeClassifier.Classify(c, f).ShouldBe(expected);

    [Theory]
    [InlineData(DeviceType.BluetoothHeadphones, 200)]
    [InlineData(DeviceType.BluetoothSpeaker, 200)]
    [InlineData(DeviceType.Wired, 0)]
    [InlineData(DeviceType.Hdmi, 0)]
    [InlineData(DeviceType.Other, 0)]
    public void Type_defaults_follow_the_spec_table(DeviceType type, int expectedMs) =>
        LatencyModel.TypeDefaultMs(type).ShouldBe(expectedMs);

    [Fact]
    public void Fake_probe_reports_what_it_was_given_and_fails_for_unknown_devices()
    {
        var probe = new FakeDeviceLatencyProbe();
        probe.Set("a", new DeviceLatencyReport(TimeSpan.Zero, "none", ConnectionKind.Usb, FormFactor.Headphones));
        probe.Probe("a").IsSuccess.ShouldBeTrue();
        probe.Probe("b").IsSuccess.ShouldBeFalse();
    }

    [Fact]
    public void New_bluetooth_device_assumes_reported_plus_type_default_with_offset_zero()
    {
        var e = LatencyModel.Estimate([("bt", DeviceType.BluetoothHeadphones, 30, 0)]);

        var o = e.Outputs.Single();
        o.ReportedMs.ShouldBe(30);
        o.TypeDefaultMs.ShouldBe(200);
        o.AssumedMs.ShouldBe(230);
        o.LagMs.ShouldBe(0);
        e.PictureDelayMs.ShouldBe(230);
    }

    [Fact]
    public void Reported_part_is_zero_when_os_reports_nothing_and_wired_default_is_zero()
    {
        var e = LatencyModel.Estimate([("w", DeviceType.Wired, 0, 0)]);

        e.Outputs.Single().AssumedMs.ShouldBe(0);
        e.PictureDelayMs.ShouldBe(0);
    }

    [Fact]
    public void Remembered_offset_is_subtracted_and_latency_uses_the_current_default()
    {
        var e = LatencyModel.Estimate([("bt", DeviceType.BluetoothSpeaker, 0, 50)]);

        e.Outputs.Single().OffsetMs.ShouldBe(50);
        e.Outputs.Single().AssumedMs.ShouldBe(150);
    }

    [Fact]
    public void Two_outputs_picture_delay_is_the_max_and_lags_are_the_gaps()
    {
        var e = LatencyModel.Estimate([
            ("bt", DeviceType.BluetoothHeadphones, 0, 0),
            ("w", DeviceType.Wired, 0, 0)]);

        e.PictureDelayMs.ShouldBe(200);
        e.Outputs.Single(x => x.DeviceId == "bt").LagMs.ShouldBe(0);
        e.Outputs.Single(x => x.DeviceId == "w").LagMs.ShouldBe(200);
    }

    [Fact]
    public void Three_outputs_have_non_negative_lags_summing_to_the_gaps()
    {
        var e = LatencyModel.Estimate([
            ("a", DeviceType.BluetoothHeadphones, 40, 10),
            ("b", DeviceType.Hdmi, 0, -20),
            ("c", DeviceType.Wired, 0, 0)]);

        e.PictureDelayMs.ShouldBe(230);
        e.Outputs.Select(x => x.LagMs).ShouldBe([0, 210, 230]);
    }

    [Fact]
    public void All_negative_assumed_latencies_give_negative_picture_delay_and_non_negative_lags()
    {
        var e = LatencyModel.Estimate([
            ("a", DeviceType.Wired, 0, 300),
            ("b", DeviceType.Hdmi, 0, 100)]);

        e.PictureDelayMs.ShouldBe(-100);
        e.Outputs.Select(x => x.AssumedMs).ShouldBe([-300, -100]);
        e.Outputs.Select(x => x.LagMs).ShouldBe([200, 0]);
    }

    [Fact]
    public void Dropping_an_output_recomputes_the_picture_delay()
    {
        var e = LatencyModel.Estimate([("w", DeviceType.Wired, 0, 0)]);

        e.PictureDelayMs.ShouldBe(0);
        e.Outputs.Single().LagMs.ShouldBe(0);
    }
}
