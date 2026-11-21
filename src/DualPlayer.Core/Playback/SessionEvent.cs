using DualPlayer.Core.Sync;

namespace DualPlayer.Core.Playback;

/// <summary>Closed set of session events (cli.md section 3), merged with the engine events by the host.</summary>
public abstract record SessionEvent;

/// <summary>Why the picture delay moved (cli.md section 3, PictureDelayChanged).</summary>
public enum PictureDelayCause
{
    Offset,
    DeviceLost,
    Seek,
    Pause,
    Resume,
    FileChange,
}

/// <summary>Per output: device id, type, reported, default, offset, assumed latency and lag; plus the picture delay.</summary>
public sealed record LatencyEstimated(LatencyEstimate Estimate) : SessionEvent;

/// <summary>The picture delay moved; carries the new per-output assumed latency and lag.</summary>
public sealed record PictureDelayChanged(
    TimeSpan Clock,
    PictureDelayCause Cause,
    int OldPictureDelayMs,
    int NewPictureDelayMs,
    IReadOnlyList<OutputLatency> Outputs) : SessionEvent;

/// <summary>An output's lag ramp during a picture-delay move: signed speed change in ppm (positive = lag growing).</summary>
public sealed record OutputRamp(string DeviceId, double RampPpm);

/// <summary>A picture-delay move that ramps: its duration and the speeds the picture and each output ran at (AC-14, AC-17).</summary>
public sealed record PictureDelayMoved(
    TimeSpan Clock,
    PictureDelayCause Cause,
    int FromMs,
    int ToMs,
    TimeSpan Duration,
    double PicturePpm,
    IReadOnlyList<OutputRamp> Ramps) : SessionEvent;

/// <summary>An output's offset moved; <paramref name="SavedAt"/> is null until the debounced save has run.</summary>
public sealed record OffsetChanged(
    int OutputNumber,
    string DeviceId,
    int OldOffsetMs,
    int NewOffsetMs,
    DateTimeOffset? SavedAt) : SessionEvent;

/// <summary>The picture's presented time against its due time.</summary>
public sealed record PictureTimingReported(TimeSpan Clock, TimeSpan FrameDue, TimeSpan Presented, double ErrorMs) : SessionEvent;

/// <summary>Late (more than 20 ms after due), dropped and skipped-damaged frames, once per minute.</summary>
public sealed record FrameCountersReported(TimeSpan Clock, FrameCounters Counters) : SessionEvent;

/// <summary>Start command, first frame presented and first audio heard per output, all in the session's timestamp base.</summary>
public sealed record StartTimings(long CommandTimestamp, long FirstFramePresented, IReadOnlyDictionary<string, long> FirstAudioHeard) : SessionEvent;

/// <summary>A note shown briefly on the picture.</summary>
public sealed record NoteShown(string Text, NoteStyle Style) : SessionEvent;

/// <summary>A neighbouring file was skipped; names the file, why and, when a track is missing, the output and track.</summary>
public sealed record FileSkipped(string File, string Reason, int? OutputNumber, int? Track) : SessionEvent
{
    /// <summary>Gets the picture's codec when the picture is the reason (<c>not-hardware-decodable</c>).</summary>
    public string? Codec { get; init; }

    /// <summary>Gets the picture's profile when the picture is the reason (<c>not-hardware-decodable</c>).</summary>
    public string? Profile { get; init; }
}

/// <summary>The session moved to another file.</summary>
public sealed record FileChanged(string File, string? PreviousFile) : SessionEvent
{
    /// <summary>Gets a value indicating whether the Viewer moved to the previous file (false = the next one).</summary>
    public bool ToPrevious { get; init; }
}

/// <summary>No playable file in the pressed direction.</summary>
public sealed record NoNeighbour(bool Next) : SessionEvent;

/// <summary>An output's device was lost; <paramref name="OthersContinue"/> is false when it was the last one.</summary>
public sealed record OutputLost(int OutputNumber, string DeviceId, TimeSpan Clock, bool OthersContinue) : SessionEvent
{
    /// <summary>Gets the picture delay before the loss (when others continue).</summary>
    public int? OldPictureDelayMs { get; init; }

    /// <summary>Gets the picture delay the outputs still playing move to (when others continue).</summary>
    public int? NewPictureDelayMs { get; init; }
}

/// <summary>The picture could not continue (decode failure or hardware decoding inactive).</summary>
public sealed record PictureFailed(TimeSpan Clock, string Reason) : SessionEvent;

/// <summary>A warning about the remembered offsets (<c>state.*</c>, <c>offsets.clamped</c>); the film starts anyway.</summary>
public sealed record OffsetsWarning(Error Warning) : SessionEvent;

/// <summary>
/// After a command, the first picture presented within 20 ms of due (sad.md flow 4, "lip-synced again"):
/// <paramref name="Command"/> names it (<c>start</c>, <c>seek</c>, <c>resume</c>, <c>file-change</c>); every output still
/// playing is then within 10 ms too (AC-17), each <see cref="OutputErrorsMs"/> entry its position error in ms keyed by device id.
/// </summary>
public sealed record LipSyncedAgain(TimeSpan Clock, string Command, double AfterCommandMs, double ErrorMs) : SessionEvent
{
    public IReadOnlyDictionary<string, double> OutputErrorsMs { get; init; } = new Dictionary<string, double>();
}

/// <summary>
/// Recovery gave up (T50): <paramref name="Command"/> was not followed by a lip-synced picture and outputs within the
/// recovery budget (1 s; 3 s for a start or a file change); <see cref="OutputErrorsMs"/> are the latest per-output errors by device id.
/// <see cref="ErrorMs"/> is null when no frame was measurable after the command.
/// </summary>
public sealed record NotLipSynced(TimeSpan Clock, string Command, double AfterCommandMs, double? ErrorMs) : SessionEvent
{
    public IReadOnlyDictionary<string, double> OutputErrorsMs { get; init; } = new Dictionary<string, double>();
}

/// <summary>A stretch of damaged frames was skipped: where it began and how long it lasted (AC-06b).</summary>
public sealed record DamagedStretchSkipped(TimeSpan Position, TimeSpan Length, long Frames) : SessionEvent;
