using System.Runtime.Versioning;
using DualPlayer.Core.Sync;
using DualPlayer.Platform.MacOS.Audio;
using DualPlayer.Tests;

namespace DualPlayer.Platform.MacOS.Tests;

/// <summary>AC-13: device type comes from the Core Audio transport type, never the name.</summary>
[SupportedOSPlatform("macos")]
public sealed class CoreAudioLatencyProbeTests
{
    private static uint Fourcc(string s) => (uint)((s[0] << 24) | (s[1] << 16) | (s[2] << 8) | s[3]);

    [Theory]
    [InlineData("blue", ConnectionKind.Bluetooth, DeviceType.BluetoothHeadphones)]
    [InlineData("blea", ConnectionKind.Bluetooth, DeviceType.BluetoothHeadphones)]
    [InlineData("usb ", ConnectionKind.Usb, DeviceType.Wired)]
    [InlineData("hdmi", ConnectionKind.DisplayHdmi, DeviceType.Hdmi)]
    [InlineData("dprt", ConnectionKind.DisplayPort, DeviceType.Hdmi)]
    [InlineData("bltn", ConnectionKind.Unknown, DeviceType.Other)]
    [InlineData("zzzz", ConnectionKind.Unknown, DeviceType.Other)]
    public void Transport_type_maps_to_connection_kind_and_device_type(string code, ConnectionKind connection, DeviceType type)
    {
        var (c, f) = CoreAudioLatencyProbe.MapTransport(Fourcc(code));

        c.ShouldBe(connection);
        DeviceTypeClassifier.Classify(c, f).ShouldBe(type);
    }

    [HardwareFact(HardwareOs.MacOS)]
    public void Every_listed_output_device_is_probed_with_a_kind_and_a_non_negative_latency()
    {
        var uids = new CoreAudioBackendLister().Uids();
        uids.ShouldNotBeEmpty();
        var probe = new CoreAudioLatencyProbe();

        foreach (var uid in uids)
        {
            var result = probe.Probe(uid);

            result.IsSuccess.ShouldBeTrue(result.Error?.Message);
            result.Value.ReportedLatency.ShouldBeGreaterThanOrEqualTo(TimeSpan.Zero);
            result.Value.Source.ShouldNotBeNullOrWhiteSpace();
        }
    }

    [HardwareFact(HardwareOs.MacOS)]
    public void The_default_output_device_reports_a_known_connection_kind_or_a_built_in_transport()
    {
        var result = new CoreAudioLatencyProbe().Probe(CoreAudioInterop.GetDefaultOutputDeviceUid());

        result.IsSuccess.ShouldBeTrue(result.Error?.Message);
        DeviceTypeClassifier.Classify(result.Value.Connection, result.Value.FormFactor).ShouldBeOneOf(Enum.GetValues<DeviceType>());
    }

    [HardwareFact(HardwareOs.MacOS)]
    public void Probing_an_unknown_device_id_fails_with_device_not_found()
    {
        var result = new CoreAudioLatencyProbe().Probe("no-such-device-uid");

        result.IsSuccess.ShouldBeFalse();
        result.Error!.Code.ShouldBe("outputs.device_not_found");
    }

    private sealed class CoreAudioBackendLister
    {
        public IReadOnlyList<string> Uids() =>
        [
            .. CoreAudioInterop.GetDeviceIds()
                .Where(CoreAudioInterop.HasOutput)
                .Select(id => CoreAudioInterop.GetStringProperty(id, CoreAudioInterop.PropDeviceUid)),
        ];
    }
}
