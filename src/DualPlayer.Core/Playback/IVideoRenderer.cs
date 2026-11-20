namespace DualPlayer.Core.Playback;

/// <summary>
/// The picture on screen: the content position of a frame and the monotonic timestamp
/// (<see cref="TimeProvider.GetTimestamp"/> time base) at which it was shown.
/// </summary>
/// <remarks>
/// While a start (<see cref="IVideoRenderer.PlayAt"/>, or a seek while playing) is being prepared, the snapshot is a
/// prediction, not a frame on screen: the seek target, then the frame the seek landed on, stamped with the moment the
/// start reaches it (which can be ahead of now), while the screen still shows the paused picture until the renderer
/// resumes. Measured against the schedule it was planned on, such a snapshot has no error by construction.
/// </remarks>
public sealed record PresentedPicture(TimeSpan Position, long Timestamp);

/// <summary>Frames the renderer reports as late, dropped or skipped because they were damaged, since load.</summary>
public sealed record FrameCounters(long Late, long Dropped, long SkippedDamaged)
{
    public static FrameCounters None { get; } = new(0, 0, 0);
}

/// <summary>How an on-screen note is drawn.</summary>
public enum NoteStyle
{
    Info,
    Warning,
}

/// <summary>
/// Shows the picture and reports where it is; Core owns every timing decision. The adapter applies commands and
/// publishes immutable snapshots. Events may be raised on the renderer's own event thread, not the caller's.
/// Expected failures (library missing, file rejected, decode error) are returned as <see cref="Result"/>
/// (<c>mpv.not_found</c>, <c>mpv.load_failed</c>, <c>video.decode_failed</c>) and never thrown.
/// </summary>
public interface IVideoRenderer : IDisposable
{
    /// <summary>
    /// Opens <paramref name="file"/> into the window <paramref name="nativeWindowHandle"/>, optionally held paused on the
    /// first frame. Once the first frame is up, <see cref="Presented"/> is position zero stamped with that moment; a playing
    /// load may hold it briefly and start from it as a scheduled start.
    /// </summary>
    Result Load(string file, nint nativeWindowHandle, bool startPaused);

    /// <summary>Starts playing from content <paramref name="position"/> so that it is shown at monotonic time <paramref name="startTimestamp"/>.</summary>
    void PlayAt(TimeSpan position, long startTimestamp);

    void Pause();

    /// <summary>
    /// Moves the picture to content <paramref name="position"/>. Paused, it stays paused there. Playing,
    /// <paramref name="position"/> is due at the moment of the call and the picture continues on that schedule. During a
    /// scheduled start, the start keeps its moment.
    /// </summary>
    void Seek(TimeSpan position);

    /// <summary>Playback speed factor, 1.0 = natural.</summary>
    void SetSpeed(double speed);

    /// <summary>The latest presented picture, or <c>null</c> until a load has put its first frame up (never a default zero position).</summary>
    PresentedPicture? Presented { get; }

    FrameCounters Counters { get; }

    /// <summary>
    /// Draws an on-screen note over the picture. A note is held for a fixed 2 s after its last change (sad §8, adapter
    /// policy, no duration parameter); a new note replaces the current one.
    /// </summary>
    void ShowNote(string text, NoteStyle style);

    /// <summary>The file cannot be decoded (further).</summary>
    event Action<Error>? DecodeFailed;

    /// <summary>The picture is being decoded in software instead of on the GPU.</summary>
    event Action? HardwareDecodingInactive;

    /// <summary>The picture reached the end of the file.</summary>
    event Action? EndOfFile;
}
