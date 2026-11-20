using DualPlayer.Core;
using Microsoft.Extensions.Logging;

namespace DualPlayer.App.Startup;

/// <summary>Reports a start error: the contract line on standard error and the same error in the log (cli.md conventions).</summary>
internal static partial class StartErrors
{
    public static void Report(Error error, TextWriter stderr, ILogger log)
    {
        ArgumentNullException.ThrowIfNull(error);
        ArgumentNullException.ThrowIfNull(stderr);
        LogRefused(log, error.Code, error.Message);
        stderr.WriteLine(StartExitCodes.FormatError(error));
    }

    /// <summary>
    /// The exit code after the session's start returned <paramref name="started"/>: null when it started; 0 without an
    /// error line when the window was already closing (Esc during the start); otherwise the error's code, reported.
    /// </summary>
    public static int? AfterSessionStart(Result started, bool closing, TextWriter stderr, ILogger log)
    {
        if (started.IsSuccess)
        {
            return null;
        }

        if (closing)
        {
            LogStartAbandoned(log, started.Error.Code, started.Error.Message);
            return StartExitCodes.Ok;
        }

        Report(started.Error, stderr, log);
        return StartExitCodes.For(started.Error);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Start refused: {Code} {Message}")]
    private static partial void LogRefused(ILogger logger, string code, string message);

    [LoggerMessage(Level = LogLevel.Information, Message = "The window closed during the start; the start ended with {Code} {Message}")]
    private static partial void LogStartAbandoned(ILogger logger, string code, string message);
}
