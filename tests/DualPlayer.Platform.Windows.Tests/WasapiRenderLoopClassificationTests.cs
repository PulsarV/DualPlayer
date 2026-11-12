using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using DualPlayer.Platform.Windows.Audio;

namespace DualPlayer.Platform.Windows.Tests;

/// <summary>
/// F18: inside the render loop only COM/HRESULT device errors are a device loss; an ArgumentException from Render
/// (e.g. ArgumentOutOfRangeException from a Span bug) is a render-loop bug, logged as an error.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WasapiRenderLoopClassificationTests
{
    [Fact]
    public void ArgumentOutOfRangeException_from_render_is_a_render_loop_bug_not_a_device_loss() =>
        WasapiErrorMapper.IsRenderDeviceFailure(new ArgumentOutOfRangeException("length")).ShouldBeFalse();

    [Fact]
    public void A_plain_ArgumentException_from_render_is_a_render_loop_bug_not_a_device_loss() =>
        WasapiErrorMapper.IsRenderDeviceFailure(new ArgumentException("bad span")).ShouldBeFalse();

    [Theory]
    [InlineData(unchecked((int)0x88890004))] // AUDCLNT_E_DEVICE_INVALIDATED
    [InlineData(unchecked((int)0x88890003))] // AUDCLNT_E_WRONG_ENDPOINT_TYPE: any COMException counts, not only the HRESULTs Map knows
    [InlineData(unchecked((int)0x8889000A))] // AUDCLNT_E_DEVICE_IN_USE
    [InlineData(unchecked((int)0x88890026))] // AUDCLNT_E_RESOURCES_INVALIDATED
    public void A_COMException_with_a_device_HRESULT_from_render_is_a_device_loss(int hresult) =>
        WasapiErrorMapper.IsRenderDeviceFailure(new COMException("device", hresult)).ShouldBeTrue();

    [Fact]
    public void UnauthorizedAccessException_from_a_device_call_is_still_a_device_loss() =>
        WasapiErrorMapper.IsRenderDeviceFailure(new UnauthorizedAccessException()).ShouldBeTrue();

    [Fact]
    public void ArgumentException_is_still_a_device_failure_around_device_lookup() =>
        WasapiErrorMapper.IsDeviceFailure(new ArgumentException("E_INVALIDARG from GetDevice")).ShouldBeTrue();
}
