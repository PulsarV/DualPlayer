using Microsoft.Extensions.Logging;

namespace DualPlayer.Core.Diagnostics;

/// <summary>
/// One 100 ms sync sample of an output as the control loop saw it: where the master clock was, how old the device's
/// latest position report was, the raw device position, how much audio the device held ahead of it, the drift that
/// follows from these (zero when <paramref name="Measured"/> is false), the audio waiting in the ring, the current rate
/// correction and the underrun count.
/// </summary>
public readonly record struct DriftReading(
    TimeSpan ClockPosition,
    bool Measured,
    TimeSpan PositionAge,
    long PlayedFrames,
    TimeSpan Queued,
    TimeSpan Drift,
    TimeSpan Ring,
    double CorrectionPpm,
    long Underruns);

/// <summary>
/// Keeps an output's latest sync samples and writes them to the log on demand, each one once, so the log shows the raw
/// readings around a drift excursion without growing by ten lines per second and device in a steady run. Driven by its
/// single owner (the engine-control loop); not thread-safe.
/// </summary>
public sealed partial class DriftTrace(string deviceId, ILogger logger)
{
    /// <summary>Two record intervals: the one an excursion shows up in and the one before it.</summary>
    public const int Capacity = 20;

    private readonly DriftReading[] _readings = new DriftReading[Capacity];
    private int _next;
    private int _unlogged;

    public void Add(in DriftReading reading)
    {
        _readings[_next] = reading;
        _next = (_next + 1) % Capacity;
        _unlogged = Math.Min(_unlogged + 1, Capacity);
    }

    /// <summary>Logs every reading added since the last flush that is still kept, oldest first.</summary>
    public void Flush()
    {
        for (var i = _unlogged; i > 0; i--)
        {
            var r = _readings[(_next - i + Capacity) % Capacity];
            LogReading(
                deviceId,
                r.ClockPosition,
                r.Measured,
                r.PositionAge.TotalMilliseconds,
                r.PlayedFrames,
                r.Queued.TotalMilliseconds,
                r.Drift.TotalMilliseconds,
                r.Ring.TotalMilliseconds,
                r.CorrectionPpm,
                r.Underruns);
        }

        _unlogged = 0;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "drift-trace {DeviceId} clock={ClockPosition} measured={Measured} age={PositionAgeMs:F1}ms played={PlayedFrames} queued={QueuedMs:F2}ms drift={DriftMs:F2}ms ring={RingMs:F0}ms correction={CorrectionPpm:F1}ppm underruns={Underruns}")]
    private partial void LogReading(string deviceId, TimeSpan clockPosition, bool measured, double positionAgeMs, long playedFrames, double queuedMs, double driftMs, double ringMs, double correctionPpm, long underruns);
}
