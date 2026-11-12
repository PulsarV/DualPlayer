using System.Globalization;
using DualPlayer.Console.Rendering;
using DualPlayer.Core;
using DualPlayer.Core.Diagnostics;
using DualPlayer.Core.Playback;

namespace DualPlayer.Console.Commands;

/// <summary>Reads transport commands (contracts/cli.md §1.4) from standard input during a run.</summary>
internal sealed class RunCommandLoop(IAudioEngine engine, Func<TimeSpan> position)
{
    public int Run(TextReader input, TextWriter output, TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        while (input.ReadLine() is { } line)
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                continue;
            }

            switch (parts)
            {
                case ["pause"]:
                    Report(engine.Pause(), t => output.WriteLine($"paused at {ConsoleFormat.Clock(t)}"), error);
                    break;
                case ["resume"]:
                    Report(engine.Resume(), t => output.WriteLine($"resumed at {ConsoleFormat.Clock(t)}"), error);
                    break;
                case ["stop"]:
                    engine.Stop(RunEndReason.Stop);
                    return 0;
                case ["seek", var arg] when TryParseTarget(arg, position, out var target):
                    if (Seek(target, output, error))
                    {
                        return 0;
                    }

                    break;
                default:
                    error.WriteLine($"error cli.unknown_command: '{line.Trim()}' (use pause, resume, seek <+-seconds|hh:mm:ss>, stop)");
                    break;
            }
        }

        return 0;
    }

    private static bool TryParseTarget(string arg, Func<TimeSpan> position, out TimeSpan target)
    {
        target = default;
        if (arg.Length > 0 && (arg[0] is '+' or '-'))
        {
            if (!double.TryParse(arg, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
                || !double.IsFinite(seconds) || Math.Abs(seconds) > TimeSpan.MaxValue.TotalSeconds / 2)
            {
                return false;
            }

            target = position() + TimeSpan.FromSeconds(seconds);
            return true;
        }

        return arg.Count(c => c == ':') == 2
            && TimeSpan.TryParse(arg, CultureInfo.InvariantCulture, out target)
            && target >= TimeSpan.Zero;
    }

    /// <summary>Returns true when the seek ended the run (past the end).</summary>
    private bool Seek(TimeSpan target, TextWriter output, TextWriter error)
    {
        var result = engine.Seek(target);
        if (!result.IsSuccess)
        {
            ExitCodes.Report(result.Error, error);
            return false;
        }

        // The out-of-file warning is an engine event the EventPrinter prints, before the run summary.
        var seek = result.Value;
        if (seek.OutsideFile && target > seek.Position)
        {
            return true;
        }

        if (seek.OutsideFile)
        {
            return false;
        }

        output.WriteLine(seek.Paused
            ? $"position moved to {ConsoleFormat.Clock(seek.Position)}, still paused"
            : $"seek confirmed at {ConsoleFormat.Clock(seek.Position)}");
        return false;
    }

    private static void Report(Result<TimeSpan> result, Action<TimeSpan> onSuccess, TextWriter error)
    {
        if (result.IsSuccess)
        {
            onSuccess(result.Value);
        }
        else
        {
            ExitCodes.Report(result.Error, error);
        }
    }
}
