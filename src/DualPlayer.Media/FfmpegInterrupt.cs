using System.Runtime.InteropServices;
using FFmpeg.AutoGen;

namespace DualPlayer.Media;

/// <summary>
/// An FFmpeg interrupt callback (<c>AVIOInterruptCB</c>) that trips when a token is cancelled, so a blocked open, probe or
/// read returns <c>AVERROR_EXIT</c> promptly and the caller can close the file. Dispose it only after the format context
/// is closed.
/// </summary>
internal sealed unsafe class FfmpegInterrupt : IDisposable
{
    private readonly GCHandle _token;

    public FfmpegInterrupt(CancellationToken cancel) => _token = GCHandle.Alloc(new Box(cancel));

    /// <summary>Allocates <paramref name="fmt"/> with the callback installed, ready for <c>avformat_open_input</c>.</summary>
    public bool Attach(AVFormatContext** fmt)
    {
        *fmt = ffmpeg.avformat_alloc_context();
        if (*fmt == null)
        {
            return false;
        }

        (*fmt)->interrupt_callback.callback = new AVIOInterruptCB_callback_func { Pointer = (nint)(delegate* unmanaged[Cdecl]<void*, int>)&Check };
        (*fmt)->interrupt_callback.opaque = (void*)GCHandle.ToIntPtr(_token);
        return true;
    }

    public void Dispose() => _token.Free();

    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    private static int Check(void* opaque) =>
        ((Box)GCHandle.FromIntPtr((nint)opaque).Target!).Cancel.IsCancellationRequested ? 1 : 0;

    private sealed record Box(CancellationToken Cancel);
}
