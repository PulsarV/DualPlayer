using DualPlayer.Core;
using DualPlayer.Core.Playback;

namespace DualPlayer.App.Startup;

/// <summary>The native window the picture is drawn into; set once the playback window's video child exists.</summary>
internal sealed class VideoSurface
{
    public nint Handle { get; set; }
}

/// <summary>
/// Hands the playback window's native handle to the renderer: the session loads with no handle, so a zero handle is replaced
/// by the surface's. Everything else is passed through.
/// </summary>
internal sealed class WindowBoundRenderer : IVideoRenderer
{
    private readonly IVideoRenderer _inner;
    private readonly VideoSurface _surface;

    public WindowBoundRenderer(IVideoRenderer inner, VideoSurface surface)
    {
        _inner = inner;
        _surface = surface;
    }

    public PresentedPicture? Presented => _inner.Presented;

    public FrameCounters Counters => _inner.Counters;

    public event Action<Error>? DecodeFailed
    {
        add => _inner.DecodeFailed += value;
        remove => _inner.DecodeFailed -= value;
    }

    public event Action? HardwareDecodingInactive
    {
        add => _inner.HardwareDecodingInactive += value;
        remove => _inner.HardwareDecodingInactive -= value;
    }

    public event Action? EndOfFile
    {
        add => _inner.EndOfFile += value;
        remove => _inner.EndOfFile -= value;
    }

    public Result Load(string file, nint nativeWindowHandle, bool startPaused) =>
        _inner.Load(file, nativeWindowHandle == 0 ? _surface.Handle : nativeWindowHandle, startPaused);

    public void PlayAt(TimeSpan position, long startTimestamp) => _inner.PlayAt(position, startTimestamp);

    public void Pause() => _inner.Pause();

    public void Seek(TimeSpan position) => _inner.Seek(position);

    public void SetSpeed(double speed) => _inner.SetSpeed(speed);

    public double Speed => _inner.Speed;

    public void ShowNote(string text, NoteStyle style) => _inner.ShowNote(text, style);

    public void Dispose() => _inner.Dispose();
}
