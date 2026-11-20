using DualPlayer.Core.Playback;
using DualPlayer.Core.Sync;
using Microsoft.Extensions.Logging;

namespace DualPlayer.App.Startup;

/// <summary>
/// Writes the session events of contracts/cli.md section 3 to the log as structured events carrying the contract's field names
/// (LatencyEstimated, PictureDelayChanged, OffsetChanged, PictureTiming, FrameCounters, StartTimings). Independent of the terminal printer.
/// </summary>
internal sealed class SessionEventLogWriter : IDisposable
{
    private readonly ILogger _log;
    private readonly IDisposable _subscription;

    public SessionEventLogWriter(IObservable<SessionEvent> session, ILogger log)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(log);
        _log = log;
        _subscription = session.Subscribe(new Observer(OnEvent));
    }

    public void Dispose() => _subscription.Dispose();

    private static string Clock(TimeSpan clock) => clock.ToString("hh\\:mm\\:ss\\.fff", System.Globalization.CultureInfo.InvariantCulture);

    private void OnEvent(SessionEvent value)
    {
        switch (value)
        {
            case LatencyEstimated l:
                LogOutputs("LatencyEstimated", l.Estimate.Outputs, l.Estimate.PictureDelayMs);
                break;
            case PictureDelayChanged p:
                _log.LogInformation(
                    "PictureDelayChanged Clock={Clock} Cause={Cause} OldPictureDelayMs={OldPictureDelayMs} NewPictureDelayMs={NewPictureDelayMs}",
                    Clock(p.Clock),
                    p.Cause.ToString(),
                    p.OldPictureDelayMs,
                    p.NewPictureDelayMs);
                LogOutputs("PictureDelayChanged.Output", p.Outputs, p.NewPictureDelayMs);
                break;
            case OffsetChanged o:
                _log.LogInformation(
                    "OffsetChanged Output={Output} DeviceId={DeviceId} OldOffsetMs={OldOffsetMs} NewOffsetMs={NewOffsetMs} SavedAt={SavedAt}",
                    o.OutputNumber,
                    o.DeviceId,
                    o.OldOffsetMs,
                    o.NewOffsetMs,
                    o.SavedAt);
                break;
            case PictureTimingReported t:
                _log.LogInformation(
                    "PictureTiming Clock={Clock} FrameDue={FrameDue} Presented={Presented} ErrorMs={ErrorMs}",
                    Clock(t.Clock),
                    Clock(t.FrameDue),
                    Clock(t.Presented),
                    t.ErrorMs);
                break;
            case FrameCountersReported f:
                _log.LogInformation(
                    "FrameCounters Clock={Clock} Late={Late} Dropped={Dropped} SkippedDamaged={SkippedDamaged}",
                    Clock(f.Clock),
                    f.Counters.Late,
                    f.Counters.Dropped,
                    f.Counters.SkippedDamaged);
                break;
            case StartTimings s:
                _log.LogInformation(
                    "StartTimings CommandTimestamp={CommandTimestamp} FirstFramePresented={FirstFramePresented} FirstAudioHeard={FirstAudioHeard}",
                    s.CommandTimestamp,
                    s.FirstFramePresented,
                    string.Join(' ', s.FirstAudioHeard.Select(p => $"{p.Key}={p.Value}")));
                break;
            default:
                break;
        }
    }

    private void LogOutputs(string name, IReadOnlyList<OutputLatency> outputs, int pictureDelayMs)
    {
        for (var i = 0; i < outputs.Count; i++)
        {
            var o = outputs[i];
            _log.LogInformation(
                "{Event} Output={Output} DeviceId={DeviceId} Type={Type} ReportedMs={ReportedMs} DefaultMs={DefaultMs} OffsetMs={OffsetMs} AssumedMs={AssumedMs} LagMs={LagMs} PictureDelayMs={PictureDelayMs}",
                name,
                i + 1,
                o.DeviceId,
                o.Type.ToString(),
                o.ReportedMs,
                o.TypeDefaultMs,
                o.OffsetMs,
                o.AssumedMs,
                o.LagMs,
                pictureDelayMs);
        }
    }

    private sealed class Observer(Action<SessionEvent> onNext) : IObserver<SessionEvent>
    {
        public void OnNext(SessionEvent value) => onNext(value);

        public void OnError(Exception error)
        {
        }

        public void OnCompleted()
        {
        }
    }
}
