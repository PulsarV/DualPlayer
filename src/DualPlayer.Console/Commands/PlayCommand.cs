using DualPlayer.Console.Rendering;
using DualPlayer.Core;
using DualPlayer.Core.Diagnostics;
using DualPlayer.Core.Outputs;
using DualPlayer.Core.Playback;
using Microsoft.Extensions.Logging;

namespace DualPlayer.Console.Commands;

/// <summary>Starts a run and prints each output with the device id it resolved to.</summary>
internal sealed class PlayCommand(IAudioEngine engine, TimeProvider? time = null, ILogger<PlayCommand>? log = null)
{
    public int Run(PlayRequest request, TextWriter output, TextWriter error, TextReader? input = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        using var lines = new ConsoleLineQueue(output, error, log);
        var console = lines.Writer(toError: false);
        var errors = lines.Writer(toError: true);
        using var printer = new EventPrinter(engine.Events, lines);
        printer.HoldStartLine();
        using var signals = new ShutdownSignals(engine, () =>
        {
            WaitForSummary(printer);
            lines.Flush();
            Environment.Exit(printer.ExitCode);
        });
        signals.DeferUntilRunStarts();
        signals.Register();
        var started = engine.StartRun(request.File, request.Outputs, request.Options);
        if (!started.IsSuccess)
        {
            return ExitCodes.Report(started.Error, errors);
        }

        var index = 1;
        foreach (var o in started.Value.Outputs)
        {
            console.WriteLine($"output {index++}  track {o.TrackNumber} ({ConsoleFormat.TrackConversion(o)})  device {o.DeviceName}  id {o.DeviceId}");
        }

        printer.ReleaseStartLine();
        signals.RunStarted();
        if (input is null)
        {
            return 0;
        }

        var tracker = new PositionTracker(engine, time ?? TimeProvider.System, started.Value.ClockPosition);
        var loopExit = new RunCommandLoop(tracker, tracker.Current).Run(new UntilRunEnds(input, printer.RunEnded), console, errors);

        // End of input while the run is still going: stop it as an interrupt and keep its summary (contracts/cli.md §1.3).
        if (!tracker.Stopped && !printer.RunEnded.IsCancellationRequested)
        {
            tracker.Stop(RunEndReason.Interrupt);
        }

        WaitForSummary(printer);
        return loopExit != 0 ? loopExit : printer.ExitCode;
    }

    private static void WaitForSummary(EventPrinter printer)
    {
        try
        {
            printer.RunEnded.WaitHandle.WaitOne(TimeSpan.FromSeconds(5));
        }
        catch (ObjectDisposedException)
        {
            // The printer is already disposed: the run is over.
        }
    }

    /// <summary>Reads standard input until the run ends on its own (file end, last device lost), then reports end of input.</summary>
    private sealed class UntilRunEnds(TextReader inner, CancellationToken runEnded) : TextReader
    {
        public override string? ReadLine()
        {
            if (runEnded.IsCancellationRequested)
            {
                return null;
            }

            var read = Task.Run(inner.ReadLine, CancellationToken.None);
            try
            {
                Task.WaitAny([read], runEnded);
            }
            catch (OperationCanceledException)
            {
                return null;
            }

            return read.GetAwaiter().GetResult();
        }
    }

    /// <summary>Wraps the engine and follows the master-clock position from the positions its replies report.</summary>
    private sealed class PositionTracker(IAudioEngine inner, TimeProvider time, TimeSpan start) : IAudioEngine
    {
        private readonly Lock _gate = new();
        private TimeSpan _anchor = start;
        private long _anchorStamp = time.GetTimestamp();
        private bool _paused;
        private volatile bool _stopped;

        public bool Stopped => _stopped;

        public IObservable<EngineEvent> Events => inner.Events;

        public TimeSpan Current()
        {
            lock (_gate)
            {
                return _paused ? _anchor : _anchor + time.GetElapsedTime(_anchorStamp);
            }
        }

        public Result<RunStarted> StartRun(string file, IReadOnlyList<OutputAssignment> assignments, EngineOptions options) =>
            inner.StartRun(file, assignments, options);

        public Result<TimeSpan> Pause() => Track(inner.Pause(), paused: true);

        public Result<TimeSpan> Resume() => Track(inner.Resume(), paused: false);

        public Result<SeekResult> Seek(TimeSpan target)
        {
            var result = inner.Seek(target);
            if (result.IsSuccess)
            {
                Set(result.Value.Position, result.Value.Paused);
            }

            return result;
        }

        public void Stop(RunEndReason reason)
        {
            _stopped = true;
            inner.Stop(reason);
        }

        private Result<TimeSpan> Track(Result<TimeSpan> result, bool paused)
        {
            if (result.IsSuccess)
            {
                Set(result.Value, paused);
            }

            return result;
        }

        private void Set(TimeSpan position, bool paused)
        {
            lock (_gate)
            {
                _anchor = position;
                _anchorStamp = time.GetTimestamp();
                _paused = paused;
            }
        }
    }
}
