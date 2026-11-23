using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using StartRequest = DualPlayer.App.Startup.StartRequest;

namespace DualPlayer.App.Tests.Startup;

/// <summary>
/// AC-01 and cli.md §4/§5: the process entry takes the start command's time before anything else and refuses a bad
/// command line with exit 2. <see cref="App.CommandTimestamp"/> is static, so these run alone (the <see cref="ConsoleCollection"/>).
/// </summary>
[Collection(ConsoleCollection.Name)]
public sealed class ProgramRunTests
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 24, 10, 0, 0, TimeSpan.Zero));

    private int Run(string[] args, TextWriter stderr, Func<StartRequest, ILoggerFactory, int> startApp, Action? whileLogging = null)
    {
        var previous = App.CommandTimestamp;
        try
        {
            return Program.Run(
                args,
                _time,
                stderr,
                () =>
                {
                    whileLogging?.Invoke();
                    return NullLoggerFactory.Instance;
                },
                startApp);
        }
        finally
        {
            App.CommandTimestamp = previous;
        }
    }

    private static int NotStarted(StartRequest request, ILoggerFactory logging) =>
        throw new InvalidOperationException("the App must not start on a refused command line");

    [Fact]
    public void Play_without_a_file_exits_2_with_the_usage_error_and_does_not_start_the_app()
    {
        var stderr = new StringWriter();

        var code = Run(["play"], stderr, NotStarted);

        code.ShouldBe(2);
        stderr.ToString().ShouldStartWith("error cli.usage: ");
    }

    [Fact]
    public void Four_outputs_exit_2_with_the_too_many_outputs_error_and_do_not_start_the_app()
    {
        var stderr = new StringWriter();
        string[] args = ["play", "film.mkv", "--out", "1=a", "--out", "2=b", "--out", "3=c", "--out", "4=d"];

        var code = Run(args, stderr, NotStarted);

        code.ShouldBe(2);
        stderr.ToString().ShouldStartWith("error outputs.too_many: ");
    }

    [Fact]
    public void A_valid_command_starts_the_app_with_the_time_taken_before_logging_and_parsing_AC_01()
    {
        var first = _time.GetTimestamp();
        long? seen = null;
        StartRequest? request = null;

        var code = Run(
            ["play", "film.mkv", "--out", "1=a"],
            new StringWriter(),
            (r, _) =>
            {
                (seen, request) = (App.CommandTimestamp, r);
                return 0;
            },
            whileLogging: () => _time.Advance(TimeSpan.FromMilliseconds(250)));

        code.ShouldBe(0);
        seen.ShouldBe(first, "the start budget must count from before logging was set up");
        request.ShouldNotBeNull().File.ShouldBe("film.mkv");
    }

    [Fact]
    public void An_exception_from_the_app_exits_1()
    {
        var code = Run(["play", "film.mkv", "--out", "1=a"], new StringWriter(), (_, _) => throw new InvalidOperationException("boom"));

        code.ShouldBe(1);
    }
}
