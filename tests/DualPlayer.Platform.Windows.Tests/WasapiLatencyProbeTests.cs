using System.Runtime.Versioning;
using DualPlayer.Core.Sync;
using DualPlayer.Platform.Windows.Audio;
using DualPlayer.Tests;
using NAudio.CoreAudioApi;

namespace DualPlayer.Platform.Windows.Tests;

/// <summary>AC-13: device type comes from the enumerator/bus and form factor the OS reports, never the name.</summary>
[SupportedOSPlatform("windows")]
public sealed class WasapiLatencyProbeTests
{
    // EndpointFormFactor: 1 Speakers, 3 Headphones, 5 Headset, 9 DigitalAudioDisplayDevice
    [Theory]
    [InlineData("BTHENUM", 3, ConnectionKind.Bluetooth, FormFactor.Headphones, DeviceType.BluetoothHeadphones)]
    [InlineData("BTHENUM", 1, ConnectionKind.Bluetooth, FormFactor.Speaker, DeviceType.BluetoothSpeaker)]
    [InlineData("BTHHFENUM", 5, ConnectionKind.Bluetooth, FormFactor.Headset, DeviceType.BluetoothHeadphones)]
    [InlineData("BTHENUM", null, ConnectionKind.Bluetooth, FormFactor.Unknown, DeviceType.BluetoothHeadphones)]
    [InlineData("USB", 1, ConnectionKind.Usb, FormFactor.Speaker, DeviceType.Wired)]
    [InlineData("HDAUDIO", 9, ConnectionKind.DisplayHdmi, FormFactor.Display, DeviceType.Hdmi)]
    [InlineData("HDAUDIO", 1, ConnectionKind.Unknown, FormFactor.Speaker, DeviceType.Other)]
    [InlineData(null, null, ConnectionKind.Unknown, FormFactor.Unknown, DeviceType.Other)]
    public void Enumerator_and_form_factor_map_to_connection_kind_and_device_type(
        string? enumerator, int? formFactor, ConnectionKind connection, FormFactor expectedForm, DeviceType type)
    {
        var (c, f) = WasapiLatencyProbe.Map(enumerator, formFactor);

        c.ShouldBe(connection);
        f.ShouldBe(expectedForm);
        DeviceTypeClassifier.Classify(c, f).ShouldBe(type);
    }

    [HardwareFact(HardwareOs.Windows)]
    public void Every_active_render_endpoint_is_probed_with_no_reported_latency()
    {
        var ids = ActiveRenderEndpointIds();
        ids.ShouldNotBeEmpty();
        var probe = new WasapiLatencyProbe();

        foreach (var id in ids)
        {
            var result = probe.Probe(id);

            result.IsSuccess.ShouldBeTrue(result.Error?.Message);
            result.Value.ReportedLatency.ShouldBe(TimeSpan.Zero);
            result.Value.Source.ShouldBe("none");
        }
    }

    [HardwareFact(HardwareOs.Windows)]
    public void The_default_endpoint_reports_a_form_factor_or_connection_the_property_store_holds()
    {
        using var enumerator = new MMDeviceEnumerator();
        string defaultId;
        using (var endpoint = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia))
        {
            defaultId = endpoint.ID;
        }

        var result = new WasapiLatencyProbe().Probe(defaultId);

        result.IsSuccess.ShouldBeTrue(result.Error?.Message);
        (result.Value.Connection != ConnectionKind.Unknown || result.Value.FormFactor != FormFactor.Unknown).ShouldBeTrue(
            "the OS reports an enumerator or a form factor for a real render endpoint");
    }

    [HardwareFact(HardwareOs.Windows)]
    public void Probing_an_unknown_endpoint_id_reports_device_not_found()
    {
        var result = new WasapiLatencyProbe().Probe("{0.0.0.00000000}.{no-such-device}");

        result.IsSuccess.ShouldBeFalse();
        result.Error!.Code.ShouldBe("outputs.device_not_found");
    }

    private static List<string> ActiveRenderEndpointIds()
    {
        using var enumerator = new MMDeviceEnumerator();
        var ids = new List<string>();
        foreach (var endpoint in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        {
            ids.Add(endpoint.ID);
            endpoint.Dispose();
        }

        return ids;
    }
}
