using DualPlayer.App.Startup;
using DualPlayer.Core;
using Microsoft.Extensions.Logging;

namespace DualPlayer.App.Tests.Startup;

public sealed class StartErrorsTests
{
    private readonly StringWriter _stderr = new();
    private readonly ListLogger _log = new();

    [Fact]
    public void A_start_error_goes_to_standard_error_and_to_the_log_with_its_code()
    {
        StartErrors.Report(new Error("start.screen_not_found", "screen 3 was not found."), _stderr, _log);

        _stderr.ToString().ShouldBe("error start.screen_not_found: screen 3 was not found." + Environment.NewLine);
        _log.Records.ShouldContain(r => r.Level == LogLevel.Error && r.Message.Contains("start.screen_not_found") && r.Message.Contains("screen 3 was not found."));
    }

    [Fact]
    public void A_session_start_that_failed_while_the_window_was_closing_exits_0_without_an_error_line()
    {
        var code = StartErrors.AfterSessionStart(Result.Failure("mpv.load_failed", "the window is gone"), closing: true, _stderr, _log);

        code.ShouldBe(0);
        _stderr.ToString().ShouldBeEmpty();
    }

    [Fact]
    public void A_session_start_that_failed_on_its_own_exits_with_its_code_and_is_reported()
    {
        var code = StartErrors.AfterSessionStart(Result.Failure("outputs.device_access_denied", "denied"), closing: false, _stderr, _log);

        code.ShouldBe(4);
        _stderr.ToString().ShouldContain("error outputs.device_access_denied: denied");
        _log.Records.ShouldContain(r => r.Message.Contains("outputs.device_access_denied"));
    }

    [Fact]
    public void A_session_that_started_needs_no_exit_code()
    {
        StartErrors.AfterSessionStart(Result.Success(), closing: true, _stderr, _log).ShouldBeNull();
        _stderr.ToString().ShouldBeEmpty();
    }

    private sealed class ListLogger : ILogger
    {
        public List<(LogLevel Level, string Message)> Records { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Records.Add((logLevel, formatter(state, exception)));
    }
}
