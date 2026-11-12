using System.Runtime.InteropServices;
using DualPlayer.Core;

namespace DualPlayer.Platform.Windows.Audio;

/// <summary>Maps WASAPI HRESULTs to the domain's device errors.</summary>
internal static class WasapiErrorMapper
{
    private const int EAccessDenied = unchecked((int)0x80070005);
    private const int DeviceInUse = unchecked((int)0x8889000A);
    private const int ExclusiveModeNotAllowed = unchecked((int)0x8889000E);
    private const int ENotFound = unchecked((int)0x80070490);
    private const int DeviceInvalidated = unchecked((int)0x88890004);

    /// <summary>Returns the domain error for <paramref name="hresult"/>, or null when it is not a device access/presence failure.</summary>
    public static Error? Map(int hresult, string deviceName) => hresult switch
    {
        EAccessDenied or DeviceInUse or ExclusiveModeNotAllowed => new Error(
            "outputs.device_access_denied",
            $"The operating system denied the player access to device '{deviceName}'. Free the device and retry."),
        ENotFound or DeviceInvalidated => new Error(
            "outputs.device_not_found",
            $"Device '{deviceName}' was not found; it may have been unplugged."),
        _ => null,
    };

    /// <summary>
    /// True for the exceptions NAudio raises (via <see cref="Marshal.ThrowExceptionForHR(int)"/>) when a device call fails:
    /// .NET turns E_ACCESSDENIED into <see cref="UnauthorizedAccessException"/>, E_INVALIDARG into
    /// <see cref="ArgumentException"/>, and other HRESULTs into <see cref="COMException"/>.
    /// </summary>
    public static bool IsDeviceFailure(Exception ex) =>
        ex is COMException or UnauthorizedAccessException or ArgumentException;

    /// <summary>
    /// The narrower rule for inside the render loop: only COM/HRESULT errors and access denial are a device loss.
    /// An <see cref="ArgumentException"/> there (e.g. a Span bug in Render) is a defect, not a device problem.
    /// </summary>
    public static bool IsRenderDeviceFailure(Exception ex) =>
        ex is not ArgumentException && ex is COMException or UnauthorizedAccessException;

    /// <summary>Maps a device failure to its domain error, or to <paramref name="fallbackCode"/> naming the device.</summary>
    public static Error Map(Exception ex, string deviceName, string fallbackCode) =>
        Map(ex.HResult, deviceName)
        ?? new Error(fallbackCode, $"Could not use device '{deviceName}': {ex.Message}");
}
