using System.Text.RegularExpressions;
using Avalonia.Input;
using DualPlayer.App.Startup;
using DualPlayer.App.ViewModels;
using DualPlayer.Core.Playback;
using DualPlayer.Core.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace DualPlayer.App.Tests.E2E;

/// <summary>
/// The test-plan's play-command rows end to end in one process: a real PlaybackSession over the real AudioEngine (fake
/// devices and fake time), the App's terminal printer and its queued window controls. Only the window and mpv are fakes.
/// </summary>
public sealed partial class PlayEndToEndTests : IDisposable
{
    private static readonly RigDevice[] TwoDevices = [new RigDevice(48_000), new RigDevice(48_000)];
    private readonly string _folder = Directory.CreateTempSubdirectory("dualplayer-app-e2e-").FullName;
    private readonly StringWriter _out = new();
    private readonly StringWriter _err = new();

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private string File(string name)
    {
        var path = Path.Combine(_folder, name);
        System.IO.File.WriteAllBytes(path, []);
        return path;
    }

    private (SessionRig Rig, TerminalPrinter Printer, PlaybackControls Controls) Play(string file, double totalSeconds = 600)
    {
        var rig = new SessionRig(TwoDevices, null, totalSeconds);
        var printer = new TerminalPrinter(
            rig.Engine.Engine.Events,
            rig.Session.Events,
            _out,
            _err,
            NullLogger.Instance,
            new TerminalContext([new OutputLabel(1, "Device 1"), new OutputLabel(2, "Device 2")], new ScreenInfo(1, 1920, 1080, true), file, rig.Engine.Time));
        var controls = new PlaybackControls(rig.Session, NullLogger<PlaybackControls>.Instance, rig.Engine.Time);
        rig.Engine.Call(() => rig.Session.Start(rig.Request(2) with { File = file })).IsSuccess.ShouldBeTrue();
        rig.Engine.Render(300);
        return (rig, printer, controls);
    }

    // Esc in the window: the command queued like a key, fake time moving until the session has ended and said so.
    private static void PressEscAndWait(SessionRig rig, PlaybackControls controls)
    {
        var exited = false;
        controls.Exited += () => exited = true;
        controls.Exit();
        rig.Engine.AdvanceUntil(() => exited).ShouldBeTrue("Esc never ended the session");
    }

    private List<string> Lines() => [.. _out.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Select(l => Spaces().Replace(l.Trim(), " "))];

    [Fact]
    public void Play_prints_the_start_lines_and_Esc_prints_the_run_summary_for_every_output_AC_01_AC_06()
    {
        var (rig, printer, controls) = Play(File("Film.mkv"));
        using (rig)
        using (controls)
        {
            PressEscAndWait(rig, controls);
            printer.Dispose();
        }

        var lines = Lines();
        lines.ShouldContain(l => l.StartsWith("output 1 track 1 device Device 1", StringComparison.Ordinal));
        lines.ShouldContain(l => l.StartsWith("output 2 track 2 device Device 2", StringComparison.Ordinal));
        lines.ShouldContain(l => l.StartsWith("picture delay", StringComparison.Ordinal));
        lines.ShouldContain(l => l.StartsWith("started clock", StringComparison.Ordinal));
        var header = lines.FindIndex(l => l == "run-summary file Film.mkv reason exit");
        header.ShouldBeGreaterThan(0, string.Join(Environment.NewLine, lines));
        lines.Skip(header).ShouldContain(l => l.Contains("dev-1", StringComparison.Ordinal));
        lines.Skip(header).ShouldContain(l => l.Contains("dev-2", StringComparison.Ordinal));
        _err.ToString().ShouldBeEmpty();
        rig.Renderer.IsDisposed.ShouldBeTrue();
    }

    [Fact]
    public void After_the_last_output_is_lost_Page_Down_plays_the_next_file_on_the_devices_that_are_back_and_Esc_still_exits_AC_15b()
    {
        var (rig, printer, controls) = Play(File("Episode 1.mkv"));
        File("Episode 2.mkv");
        using (rig)
        using (controls)
        {
            rig.Engine.Devices[1].SimulateLoss();
            rig.Engine.Barrier();
            rig.Engine.Devices[0].SimulateLoss();
            rig.Engine.WaitFor(() => rig.Session.State == DualPlayer.Core.Playback.PlaybackState.Ended).ShouldBeTrue();

            // The devices are still in the list (plugged back in): the next film plays on them.
            controls.NextFile();
            rig.Engine.AdvanceUntil(() => rig.Session.State == DualPlayer.Core.Playback.PlaybackState.Playing)
                .ShouldBeTrue("Page Down after the last loss did not play the next file");
            rig.Engine.AdvanceUntil(() => false, maxSteps: 100); // a second of the new film

            PressEscAndWait(rig, controls);
            printer.Dispose();
        }

        var lines = Lines();
        lines.ShouldContain("run-summary file Episode 1.mkv reason last-output-lost");
        lines.ShouldContain("next Episode 2.mkv");
        lines.ShouldContain("run-summary file Episode 2.mkv reason exit");
    }

    [Fact]
    public void Page_Down_prints_the_next_file_line_before_the_new_files_output_and_picture_delay_lines_AC_07()
    {
        var (rig, printer, controls) = Play(File("Episode 1.mkv"));
        File("Episode 2.mkv");
        using (rig)
        using (controls)
        {
            controls.NextFile();
            rig.Engine.AdvanceUntil(() => rig.Events.OfType<DualPlayer.Core.Playback.StartTimings>().Count() >= 2)
                .ShouldBeTrue("Page Down did not start the next file");

            PressEscAndWait(rig, controls);
            printer.Dispose();
        }

        var lines = Lines();
        var text = string.Join(Environment.NewLine, lines);
        var next = lines.FindIndex(l => l == "next Episode 2.mkv");
        next.ShouldBeGreaterThan(0, text);
        var outputs = Enumerable.Range(0, lines.Count).Where(i => lines[i].StartsWith("output 1 track", StringComparison.Ordinal)).ToList();
        var delays = Enumerable.Range(0, lines.Count).Where(i => lines[i].StartsWith("picture delay", StringComparison.Ordinal)).ToList();
        outputs.Count.ShouldBe(2, text);
        delays.Count.ShouldBe(2, text);
        next.ShouldBeLessThan(outputs[1], "the new file's output lines were printed before the next-file line:" + Environment.NewLine + text);
        next.ShouldBeLessThan(delays[1], "the new file's picture delay line was printed before the next-file line:" + Environment.NewLine + text);
        next.ShouldBeGreaterThan(outputs[0], text);
        var oldSummary = lines.FindIndex(l => l == "run-summary file Episode 1.mkv reason file-change");
        oldSummary.ShouldBeGreaterThan(-1, text);
        next.ShouldBeLessThan(oldSummary, "the old file's run-summary was printed before the next-file line:" + Environment.NewLine + text);
    }

    [Fact]
    public void Seeking_past_the_end_and_back_prints_one_start_line_and_the_exit_summary_covers_the_whole_playback_AC_05_AC_06()
    {
        var (rig, printer, controls) = Play(File("Film.mkv"), totalSeconds: 30);
        using (rig)
        using (controls)
        {
            rig.Engine.Render(200); // five seconds of the film in all
            controls.Seek(TimeSpan.FromSeconds(60));
            rig.Engine.AdvanceUntil(() => rig.Session.State == DualPlayer.Core.Playback.PlaybackState.Ended)
                .ShouldBeTrue("the seek past the end did not end the film");
            controls.Seek(TimeSpan.FromSeconds(-10));
            rig.Engine.AdvanceUntil(() => rig.Session.State == DualPlayer.Core.Playback.PlaybackState.Playing)
                .ShouldBeTrue("the backward seek at the end did not return into the film");
            rig.Engine.AdvanceUntil(() => false, maxSteps: 200); // two seconds from the target

            PressEscAndWait(rig, controls);
            printer.Dispose();
        }

        var lines = Lines();
        var text = string.Join(Environment.NewLine, lines);
        lines.Count(l => l.StartsWith("started clock", StringComparison.Ordinal)).ShouldBe(1, "a return into the film must not print a start line again:" + Environment.NewLine + text);
        lines.Count(l => l.StartsWith("run-summary", StringComparison.Ordinal)).ShouldBe(2, "one summary at the end, one at the exit:" + Environment.NewLine + text);
        var atEnd = lines.FindIndex(l => l == "run-summary file Film.mkv reason end");
        var atExit = lines.FindIndex(l => l == "run-summary file Film.mkv reason exit");
        atEnd.ShouldBeGreaterThan(0, text);
        atExit.ShouldBeGreaterThan(atEnd, text);

        // The exit summary covers the file since it was opened: the five seconds before the end plus the two after the return.
        var playedAtEnd = PlayedOf(lines, atEnd);
        var playedAtExit = PlayedOf(lines, atExit);
        playedAtEnd.Count.ShouldBe(2);
        playedAtExit.Count.ShouldBe(2);
        for (var i = 0; i < 2; i++)
        {
            playedAtExit[i].ShouldBeGreaterThan(playedAtEnd[i] + TimeSpan.FromSeconds(1.5), $"output {i + 1}: the exit summary covers only the stretch after the return");
        }
    }

    [Fact]
    public void Space_through_the_real_toggle_pauses_then_resumes_and_after_the_natural_end_stays_Ended_AC_04_AC_06()
    {
        var (rig, _, controls) = Play(File("Film.mkv"), totalSeconds: 30);
        using (rig)
        using (controls)
        {
            controls.TogglePause();
            rig.Engine.AdvanceUntil(() => rig.Session.State == DualPlayer.Core.Playback.PlaybackState.Paused)
                .ShouldBeTrue("the first Space did not pause");
            controls.TogglePause();
            rig.Engine.AdvanceUntil(() => rig.Session.State == DualPlayer.Core.Playback.PlaybackState.Playing)
                .ShouldBeTrue("the second Space did not resume");

            rig.Engine.AdvanceUntil(() => rig.Session.State == DualPlayer.Core.Playback.PlaybackState.Ended, maxSteps: 5000)
                .ShouldBeTrue("the film did not end");
            controls.TogglePause();
            rig.Engine.AdvanceUntil(() => false, maxSteps: 50);
            rig.Session.State.ShouldBe(DualPlayer.Core.Playback.PlaybackState.Ended, "Space after the natural end changed the state");
        }
    }

    [Fact]
    public void Pressing_the_offset_key_past_the_limit_reaches_the_renderer_as_the_limit_note_AC_11()
    {
        var (rig, _, controls) = Play(File("Film.mkv"));
        var window = Substitute.For<IVideoRenderer>();
        var vm = new PlaybackViewModel(controls, window);
        using (rig)
        using (controls)
        {
            // From 0 to +500 ms is fifty presses; the fifty-first cannot go further.
            for (var i = 0; i < 51; i++)
            {
                vm.HandleKey(Key.D1).ShouldBeTrue();
            }

            rig.Engine.AdvanceUntil(() => window.ReceivedCalls().Any(c => c.GetArguments().Any(a => a is string t && t.Contains("beyond", StringComparison.Ordinal))), maxSteps: 2000)
                .ShouldBeTrue("the offset limit note never reached the renderer");
        }

        window.Received().ShowNote("Output 1: offset cannot go beyond \u00b1500 ms", NoteStyle.Warning);
    }

    [Fact]
    public void Pressing_Page_Down_on_the_last_file_reaches_the_renderer_as_the_No_next_file_note_AC_08()
    {
        var (rig, _, controls) = Play(File("Film.mkv"));
        var window = Substitute.For<IVideoRenderer>();
        var vm = new PlaybackViewModel(controls, window);
        using (rig)
        using (controls)
        {
            vm.HandleKey(Key.PageDown).ShouldBeTrue();

            rig.Engine.AdvanceUntil(() => window.ReceivedCalls().Any(c => c.GetArguments().Any(a => a is string t && t.StartsWith("No next", StringComparison.Ordinal))), maxSteps: 2000)
                .ShouldBeTrue("the no-next-file note never reached the renderer");
        }

        window.Received().ShowNote("No next file", NoteStyle.Info);
    }

    // The "played" column of the output rows under a run-summary header: index, device, played, ...
    private static List<TimeSpan> PlayedOf(List<string> lines, int header) =>
        [.. lines.Skip(header + 2).Take(2).Select(l => TimeSpan.ParseExact(l.Split(' ')[2], @"hh\:mm\:ss\.f", System.Globalization.CultureInfo.InvariantCulture))];

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
