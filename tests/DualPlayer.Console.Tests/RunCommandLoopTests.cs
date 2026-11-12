using DualPlayer.Console.Commands;
using DualPlayer.Core;
using DualPlayer.Core.Diagnostics;
using DualPlayer.Core.Outputs;
using DualPlayer.Core.Playback;

namespace DualPlayer.Console.Tests;

public sealed class RunCommandLoopTests
{
    private static readonly TimeSpan Duration = TimeSpan.FromMinutes(10);

    private readonly StringWriter _out = new();
    private readonly StringWriter _err = new();
    private readonly FakeEngine _engine;

    public RunCommandLoopTests() => _engine = new FakeEngine(_out);

    private int Run(params string[] lines)
    {
        var input = new StringReader(string.Join('\n', lines) + "\n");
        return new RunCommandLoop(_engine, () => _engine.Position).Run(input, _out, _err);
    }

    private string[] OutLines() =>
        _out.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    [Fact]
    public void Pause_prints_paused_at_the_paused_clock_position()
    {
        _engine.Position = TimeSpan.FromSeconds(30.5);

        Run("pause", "stop");

        _engine.Calls.ShouldContain("Pause");
        OutLines().ShouldContain("paused at 00:00:30.500");
    }

    [Fact]
    public void Resume_after_pause_prints_resumed_at_the_paused_position()
    {
        _engine.Position = TimeSpan.FromSeconds(12);

        Run("pause", "resume", "stop");

        _engine.Calls.ShouldBe(["Pause", "Resume", "Stop:Stop"]);
        OutLines().ShouldContain("resumed at 00:00:12.000");
    }

    [Fact]
    public void Resume_while_playing_replies_with_the_current_position_and_changes_nothing()
    {
        _engine.Position = TimeSpan.FromSeconds(5);

        Run("resume", "stop");

        OutLines().ShouldContain("resumed at 00:00:05.000");
        _engine.Paused.ShouldBeFalse();
    }

    [Theory]
    [InlineData("seek +60", 100, 160)]
    [InlineData("seek -10", 100, 90)]
    [InlineData("seek +10", 100, 110)]
    [InlineData("seek -60", 100, 40)]
    public void Relative_seek_while_playing_targets_the_current_position_plus_the_offset_and_confirms(string command, double from, double expected)
    {
        _engine.Position = TimeSpan.FromSeconds(from);

        Run(command, "stop");

        _engine.SeekTargets.ShouldBe([TimeSpan.FromSeconds(expected)]);
        OutLines().ShouldContain($"seek confirmed at {TimeSpan.FromSeconds(expected):hh\\:mm\\:ss\\.fff}");
    }

    [Fact]
    public void Absolute_seek_uses_the_hh_mm_ss_target()
    {
        Run("seek 00:01:30", "stop");

        _engine.SeekTargets.ShouldBe([TimeSpan.FromSeconds(90)]);
        OutLines().ShouldContain("seek confirmed at 00:01:30.000");
    }

    [Fact]
    public void Seek_confirmation_is_printed_only_after_the_engine_replied()
    {
        Run("seek +10", "stop");

        _engine.OutputWhenSeekCalled.ShouldNotContain("seek confirmed");
        _out.ToString().ShouldContain("seek confirmed");
    }

    [Fact]
    public void Seek_while_paused_prints_position_moved_and_still_paused_without_a_confirmation()
    {
        _engine.Position = TimeSpan.FromSeconds(20);

        Run("pause", "seek +30", "stop");

        var lines = OutLines();
        lines.ShouldContain("position moved to 00:00:50.000, still paused");
        lines.ShouldNotContain(l => l.StartsWith("seek confirmed"));
        _engine.Paused.ShouldBeTrue();
    }

    [Fact]
    public void Resume_after_a_seek_while_paused_continues_from_the_new_position()
    {
        _engine.Position = TimeSpan.FromSeconds(20);

        Run("pause", "seek +30", "resume", "stop");

        OutLines().ShouldContain("resumed at 00:00:50.000");
    }

    [Fact]
    public void Seek_before_the_start_warns_and_plays_from_the_beginning()
    {
        _engine.Position = TimeSpan.FromSeconds(5);

        Run("seek -60", "stop");

        OutLines().ShouldContain("warning: target was outside the file, playing from 00:00:00.000");
        _engine.Position.ShouldBe(TimeSpan.Zero);
        _engine.Paused.ShouldBeFalse();
    }

    [Fact]
    public void Seek_past_the_end_warns_and_the_loop_returns_with_exit_0_without_reading_further_commands()
    {
        _engine.Position = TimeSpan.FromMinutes(9);

        var exit = Run("seek +600", "pause", "seek +1", "stop");

        exit.ShouldBe(0);
        OutLines().ShouldContain("warning: target was outside the file");
        _engine.Position.ShouldBe(Duration);
        _engine.Calls.ShouldBe(["Seek"]);
    }

    [Fact]
    public void Stop_ends_the_run_and_returns_exit_0()
    {
        var exit = Run("stop", "pause");

        exit.ShouldBe(0);
        _engine.Calls.ShouldBe(["Stop:Stop"]);
    }

    [Theory]
    [InlineData("seek abc")]
    [InlineData("seek")]
    [InlineData("seek 1:2:3:4")]
    [InlineData("dance")]
    [InlineData("pause now")]
    public void Unknown_or_malformed_commands_print_cli_unknown_command_on_stderr_and_the_run_continues(string command)
    {
        var exit = Run(command, "pause", "stop");

        _err.ToString().ShouldContain("cli.unknown_command");
        _engine.Calls.ShouldBe(["Pause", "Stop:Stop"]);
        OutLines().ShouldContain(l => l.StartsWith("paused at"));
        exit.ShouldBe(0);
    }

    [Fact]
    public void Blank_lines_are_ignored()
    {
        Run("", "  ", "stop");

        _err.ToString().ShouldBeEmpty();
        _engine.Calls.ShouldBe(["Stop:Stop"]);
    }

    private sealed class FakeEngine(StringWriter console) : IAudioEngine
    {
        public TimeSpan Position { get; set; }

        public bool Paused { get; private set; }

        public List<string> Calls { get; } = [];

        public List<TimeSpan> SeekTargets { get; } = [];

        public string OutputWhenSeekCalled { get; private set; } = string.Empty;

        public IObservable<EngineEvent> Events => throw new NotSupportedException();

        public Result<RunStarted> StartRun(string file, IReadOnlyList<OutputAssignment> assignments, EngineOptions options) =>
            throw new NotSupportedException();

        public Result<TimeSpan> Pause()
        {
            Calls.Add("Pause");
            Paused = true;
            return Result<TimeSpan>.Success(Position);
        }

        public Result<TimeSpan> Resume()
        {
            Calls.Add("Resume");
            Paused = false;
            return Result<TimeSpan>.Success(Position);
        }

        public Result<SeekResult> Seek(TimeSpan target)
        {
            Calls.Add("Seek");
            SeekTargets.Add(target);
            OutputWhenSeekCalled = console.ToString();
            var outside = target < TimeSpan.Zero || target > Duration;
            Position = target < TimeSpan.Zero ? TimeSpan.Zero : target > Duration ? Duration : target;
            return Result<SeekResult>.Success(new SeekResult(Position, outside, Paused));
        }

        public void Stop(RunEndReason reason) => Calls.Add($"Stop:{reason}");
    }
}
