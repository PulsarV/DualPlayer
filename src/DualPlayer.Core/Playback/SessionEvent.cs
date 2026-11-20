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
public sealed record FileSkipped(string File, string Reason, int? OutputNumber, int? Track) : SessionEvent;

/// <summary>The session moved to another file.</summary>
public sealed record FileChanged(string File, string? PreviousFile) : SessionEvent;

/// <summary>No playable file in the pressed direction.</summary>
public sealed record NoNeighbour(bool Next) : SessionEvent;

/// <summary>An output's device was lost; <paramref name="OthersContinue"/> is false when it was the last one.</summary>
public sealed record OutputLost(int OutputNumber, string DeviceId, TimeSpan Clock, bool OthersContinue) : SessionEvent;

/// <summary>The picture could not continue (decode failure or hardware decoding inactive).</summary>
public sealed record PictureFailed(TimeSpan Clock, string Reason) : SessionEvent;

/// <summary>A warning about the remembered offsets (<c>state.*</c>, <c>offsets.clamped</c>); the film starts anyway.</summary>
public sealed record OffsetsWarning(Error Warning) : SessionEvent;
