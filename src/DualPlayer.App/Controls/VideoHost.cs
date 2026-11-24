using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia.Controls;
using Avalonia.Platform;

namespace DualPlayer.App.Controls;

/// <summary>
/// Avalonia has no video control: this host creates a native child window and exposes its handle so mpv can render into
/// it (ADR-0003). On Windows the child is disabled and never activated, so it cannot take keyboard focus from the
/// playback window; the Viewer keys always reach Avalonia.
/// </summary>
public sealed partial class VideoHost : NativeControlHost
{
    /// <summary>The native window handle mpv renders into; zero until the host is attached to a window.</summary>
    public nint NativeHandle { get; private set; }

    /// <summary>Raised once the native child exists and its handle can be given to the renderer.</summary>
    public event Action<nint>? NativeHandleCreated;

    internal void RaiseNativeHandleCreated(nint handle) => NativeHandleCreated?.Invoke(handle);

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        var child = OperatingSystem.IsWindows()
            ? CreateWindowsChild(parent)
            : base.CreateNativeControlCore(parent);

        NativeHandle = child.Handle;
        RaiseNativeHandleCreated(child.Handle);
        return child;
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        NativeHandle = 0;

        if (OperatingSystem.IsWindows())
        {
            _ = NativeMethods.DestroyWindow(control.Handle);
        }
        else
        {
            base.DestroyNativeControlCore(control);
        }
    }

    [SupportedOSPlatform("windows")]
    private static PlatformHandle CreateWindowsChild(IPlatformHandle parent)
    {
        var hwnd = NativeMethods.CreateWindowEx(
            NativeMethods.WsExNoActivate,
            "Static",
            null,
            NativeMethods.WsChild | NativeMethods.WsVisible | NativeMethods.WsClipSiblings | NativeMethods.WsClipChildren,
            0,
            0,
            0,
            0,
            parent.Handle,
            0,
            0,
            0);

        if (hwnd != 0)
        {
            // A disabled window can never become the focus window; mouse and keys fall through to the parent.
            _ = NativeMethods.EnableWindow(hwnd, false);
        }

        return new PlatformHandle(hwnd, "HWND");
    }

    [SupportedOSPlatform("windows")]
    private static partial class NativeMethods
    {
        public const uint WsChild = 0x40000000;
        public const uint WsVisible = 0x10000000;
        public const uint WsClipSiblings = 0x04000000;
        public const uint WsClipChildren = 0x02000000;
        public const uint WsExNoActivate = 0x08000000;

        [LibraryImport("user32.dll", EntryPoint = "CreateWindowExW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
        public static partial nint CreateWindowEx(
            uint exStyle,
            string className,
            string? windowName,
            uint style,
            int x,
            int y,
            int width,
            int height,
            nint parent,
            nint menu,
            nint instance,
            nint param);

        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool EnableWindow(nint hwnd, [MarshalAs(UnmanagedType.Bool)] bool enable);

        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool DestroyWindow(nint hwnd);
    }
}
