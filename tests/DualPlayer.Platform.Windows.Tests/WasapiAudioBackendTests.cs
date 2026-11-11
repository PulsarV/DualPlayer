using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using DualPlayer.Core.Outputs;
using DualPlayer.Platform.Windows.Audio;
using DualPlayer.Tests;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NSubstitute;

namespace DualPlayer.Platform.Windows.Tests;

[SupportedOSPlatform("windows")]
public sealed class WasapiAudioBackendTests
{
    [Theory]
    [InlineData(unchecked((int)0x80070005))] // E_ACCESSDENIED
    [InlineData(unchecked((int)0x8889000A))] // AUDCLNT_E_DEVICE_IN_USE
    [InlineData(unchecked((int)0x8889000E))] // AUDCLNT_E_EXCLUSIVE_MODE_NOT_ALLOWED
    public void Access_refusal_hresults_map_to_device_access_denied_naming_the_device(int hresult)
    {
        var error = WasapiErrorMapper.Map(hresult, "Test Headset A");

        error.ShouldNotBeNull();
        error.Code.ShouldBe("outputs.device_access_denied");
        error.Message.ShouldContain("Test Headset A");
    }

    [Theory]
    [InlineData(unchecked((int)0x80070490))] // E_NOTFOUND
    [InlineData(unchecked((int)0x88890004))] // AUDCLNT_E_DEVICE_INVALIDATED
    public void Missing_endpoint_hresults_map_to_device_not_found(int hresult)
    {
        var error = WasapiErrorMapper.Map(hresult, "Test Headset A");

        error.ShouldNotBeNull();
        error.Code.ShouldBe("outputs.device_not_found");
    }

    [Fact]
    public void Unrelated_hresult_is_not_mapped_to_a_device_error() =>
        WasapiErrorMapper.Map(unchecked((int)0x80004005), "Test Headset A").ShouldBeNull();

    [Theory]
    [InlineData(unchecked((int)0x80070005), "outputs.device_access_denied")] // E_ACCESSDENIED -> UnauthorizedAccessException
    [InlineData(unchecked((int)0x8889000A), "outputs.device_access_denied")] // AUDCLNT_E_DEVICE_IN_USE -> COMException
    [InlineData(unchecked((int)0x80070490), "outputs.device_not_found")] // E_NOTFOUND -> COMException
    [InlineData(unchecked((int)0x80070057), "outputs.device_not_found")] // E_INVALIDARG -> ArgumentException
    public void Exceptions_naudio_raises_for_device_hresults_are_caught_and_mapped(int hresult, string expectedCode)
    {
        var raised = Marshal.GetExceptionForHR(hresult)!;

        WasapiErrorMapper.IsDeviceFailure(raised).ShouldBeTrue();
        WasapiErrorMapper.Map(raised, "Test Headset A", "outputs.device_not_found").Code.ShouldBe(expectedCode);
    }

    [Fact]
    public void A_null_reference_bug_is_not_treated_as_a_device_failure() =>
        WasapiErrorMapper.IsDeviceFailure(new NullReferenceException()).ShouldBeFalse();

    [Fact]
    public void Unmapped_device_failure_falls_back_to_the_given_code_naming_the_device()
    {
        var error = WasapiErrorMapper.Map(new COMException("boom", unchecked((int)0x80004005)), "Test Headset A", "outputs.device_open_failed");

        error.Code.ShouldBe("outputs.device_open_failed");
        error.Message.ShouldContain("Test Headset A");
    }

    [Fact]
    public void A_32_bit_float_mix_format_is_accepted()
    {
        WasapiAudioDevice.IsFloat32(WaveFormat.CreateIeeeFloatWaveFormat(48000, 2)).ShouldBeTrue();
        WasapiAudioDevice.IsFloat32(new WaveFormatExtensible(48000, 32, 2)).ShouldBeTrue();
    }

    [Fact]
    public void A_16_bit_pcm_mix_format_is_rejected()
    {
        WasapiAudioDevice.IsFloat32(new WaveFormat(48000, 16, 2)).ShouldBeFalse();
        WasapiAudioDevice.IsFloat32(new WaveFormatExtensible(48000, 16, 2)).ShouldBeFalse();
    }

    [HardwareFact]
    public void Listing_returns_devices_sorted_by_endpoint_id_with_names_and_ids()
    {
        var devices = new WasapiAudioBackend().ListDevices();

        devices.ShouldNotBeEmpty();
        devices.ShouldAllBe(d => d.Name.Length > 0 && d.Id.Length > 0);
        devices.Select(d => d.Id).ShouldBe(devices.Select(d => d.Id).OrderBy(i => i, StringComparer.Ordinal));
    }

    [HardwareFact]
    public void Opening_an_unknown_endpoint_id_reports_device_not_found()
    {
        var result = new WasapiAudioBackend().Open("{0.0.0.00000000}.{no-such-device}", TimeSpan.FromMilliseconds(40), null!);

        result.IsSuccess.ShouldBeFalse();
        result.Error!.Code.ShouldBe("outputs.device_not_found");
    }

    [HardwareFact]
    public void Opening_the_default_device_in_shared_mode_reports_its_mix_format_and_granted_buffer()
    {
        using var enumerator = new MMDeviceEnumerator();
        string defaultId;
        using (var endpoint = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia))
        {
            defaultId = endpoint.ID;
        }

        var requested = TimeSpan.FromMilliseconds(40);
        var result = new WasapiAudioBackend().Open(defaultId, requested, Substitute.For<IRenderSource>());

        result.IsSuccess.ShouldBeTrue(result.Error?.Message);
        var device = result.Value!;
        device.MixFormat.SampleRate.ShouldBeGreaterThan(0);
        device.MixFormat.Channels.ShouldBeGreaterThan(0);
        device.GrantedBuffer.ShouldBeGreaterThanOrEqualTo(requested - TimeSpan.FromMilliseconds(1));

        device.Close();
        device.Close();
    }
}
