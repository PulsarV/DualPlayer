using System.Text.RegularExpressions;
using DualPlayer.App.Startup;
using DualPlayer.App.ViewModels;
using DualPlayer.Core.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

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

    private (SessionRig Rig, TerminalPrinter Printer, PlaybackControls Controls) Play(string file)
    {
        var rig = new SessionRig(TwoDevices, null, 600);
        var printer = new TerminalPrinter(
            rig.Engine.Engine.Events,
            rig.Session.Events,
            _out,
            _err,
            NullLogger.Instance,
            new TerminalContext([new OutputLabel(1, "Device 1"), new OutputLabel(2, "Device 2")], new ScreenInfo(1, 1920, 1080, true), file, rig.Engine.Time));
        var controls = new PlaybackControls(rig.Session, NullLogger<PlaybackControls>.Instance);
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

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
