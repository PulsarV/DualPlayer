using DualPlayer.Core.Diagnostics;
using DualPlayer.Core.Playback;
using DualPlayer.Core.Sync;
using DualPlayer.Core.Tests.Fakes;

namespace DualPlayer.Core.Tests.Playback;

/// <summary>T39: exit is terminal; a command that was queued behind Esc (or behind Alt+F4) does nothing once the session has exited.</summary>
public sealed class PlaybackSessionExitTests
{
    private static readonly RigDevice[] TwoDevices = [new RigDevice(48_000), new RigDevice(48_000)];

    private static SessionRig PlayingRig(double totalSeconds, string? file = null)
    {
        var rig = new SessionRig(TwoDevices, null, totalSeconds);
        rig.Probe.Set("dev-1", new DeviceLatencyReport(TimeSpan.FromMilliseconds(40), "test", ConnectionKind.Bluetooth, FormFactor.Headphones));
        rig.Probe.Set("dev-2", new DeviceLatencyReport(TimeSpan.Zero, "test", ConnectionKind.Usb, FormFactor.Speaker));
        var request = rig.Request(2);
        rig.Engine.Call(() => rig.Session.Start(file is null ? request : request with { File = file })).IsSuccess.ShouldBeTrue();
        rig.Engine.Render(300);
        return rig;
    }

    private static void Exit(SessionRig rig) =>
        rig.Engine.Call(() =>
        {
            rig.Session.Exit();
            return true;
        });

    private static int EngineRunCount(SessionRig rig) => rig.EngineRunFiles.Count;

    [Fact]
    public void A_backward_seek_queued_behind_Exit_at_the_end_of_the_film_starts_no_engine_run_AC_06()
    {
        using var rig = PlayingRig(totalSeconds: 30);
        rig.Renderer.Duration = TimeSpan.FromSeconds(30);
        rig.Engine.Call(() => rig.Session.Seek(TimeSpan.FromSeconds(60))).IsSuccess.ShouldBeTrue();
        rig.Engine.Render(50);
        rig.Session.State.ShouldBe(PlaybackState.Ended);
        Exit(rig);
        var startedBefore = EngineRunCount(rig);

        var seek = rig.Engine.Call(() => rig.Session.Seek(TimeSpan.FromSeconds(-10)));
        rig.Engine.Render(150);

        EngineRunCount(rig).ShouldBe(startedBefore, "a new engine run started after the session had exited");
        seek.IsSuccess.ShouldBeFalse("a seek after exit returned success");
        seek.Error.Code.ShouldBe("playback.no_run");
        rig.Session.State.ShouldBe(PlaybackState.Ended);
    }

    [Theory]
    [InlineData("natural end")]
    [InlineData("seek past end")]
    [InlineData("last output lost")]
    [InlineData("picture failure")]
    [InlineData("failed file change")]
    public void Esc_after_the_film_has_ended_publishes_no_second_run_summary_AC_06(string endCause)
    {
        var folder = Directory.CreateTempSubdirectory("dualplayer-exit-end-").FullName;
        try
        {
            var current = Path.Combine(folder, "a.mkv");
            File.WriteAllBytes(current, []);
            File.WriteAllBytes(Path.Combine(folder, "b.mkv"), []);
            using var rig = PlayingRig(totalSeconds: 30, file: current);
            rig.Renderer.Duration = TimeSpan.FromSeconds(30);

            EndTheFilm(rig, endCause);

            rig.Session.State.ShouldBe(PlaybackState.Ended);
            rig.Flush();
            var summariesAtTheEnd = rig.Engine.Events.OfType<RunSummary>().Count();
            summariesAtTheEnd.ShouldBe(1, $"the end ({endCause}) prints the one summary that covers the file");

            Exit(rig);
            rig.Engine.Render(50);
            rig.Flush();

            rig.Engine.Events.OfType<RunSummary>().Count().ShouldBe(summariesAtTheEnd, $"Esc after the end ({endCause}) printed a second run summary");
            rig.Session.State.ShouldBe(PlaybackState.Ended);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private static void EndTheFilm(SessionRig rig, string endCause)
    {
        switch (endCause)
        {
            case "natural end":
                rig.Engine.Render(3_200); // past the 30 s of the film
                break;
            case "seek past end":
                rig.Engine.Call(() => rig.Session.Seek(TimeSpan.FromSeconds(60))).IsSuccess.ShouldBeTrue();
                rig.Engine.Render(50);
                break;
            case "last output lost":
                rig.Engine.Devices[1].SimulateLoss();
                rig.Engine.Barrier();
                rig.Engine.Devices[0].SimulateLoss();
                rig.Engine.WaitFor(() => rig.Engine.Summary is not null).ShouldBeTrue();
                break;
            case "picture failure":
                rig.Renderer.InjectDecodeFailure(new Error("video.decode_failed", "The picture cannot be decoded."));
                rig.Flush();
                break;
            case "failed file change":
                rig.Renderer.LoadError = new Error("video.load_failed", "mpv could not open the file.");
                rig.Engine.Call(() => rig.Session.NextFile()).IsSuccess.ShouldBeFalse();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(endCause), endCause, null);
        }
    }

    [Fact]
    public void A_backward_seek_queued_behind_Exit_while_playing_starts_no_engine_run_AC_06()
    {
        using var rig = PlayingRig(totalSeconds: 600);
        Exit(rig);
        var startedBefore = EngineRunCount(rig);

        var seek = rig.Engine.Call(() => rig.Session.Seek(TimeSpan.FromSeconds(-10)));
        rig.Engine.Render(150);

        EngineRunCount(rig).ShouldBe(startedBefore, "a new engine run started after the session had exited");
        seek.IsSuccess.ShouldBeFalse("a seek after exit returned success");
        seek.Error.Code.ShouldBe("playback.no_run");
        rig.Session.State.ShouldBe(PlaybackState.Ended);
    }

    [Fact]
    public void Page_Down_queued_behind_Exit_scans_no_folder_and_starts_nothing_AC_06()
    {
        var folder = Directory.CreateTempSubdirectory("dualplayer-exit-").FullName;
        try
        {
            var current = Path.Combine(folder, "a.mkv");
            File.WriteAllBytes(current, []);
            File.WriteAllBytes(Path.Combine(folder, "b.mkv"), []);
            using var rig = PlayingRig(totalSeconds: 600, file: current);
            rig.Decoder.Script("b.mkv", 1); // no track 2: were the scan to run, the file is skipped, not started
            var probed = 0;
            rig.Decoder.BeforeList = () => probed++;
            Exit(rig);
            var startedBefore = EngineRunCount(rig);
            var eventsBefore = rig.Events.Count;

            var next = rig.Engine.Call(() => rig.Session.NextFile());
            rig.Engine.Render(50);

            probed.ShouldBe(0, "the folder was scanned and its files probed after exit");
            rig.Events.Skip(eventsBefore).ShouldBeEmpty("Page Down after exit published events");
            EngineRunCount(rig).ShouldBe(startedBefore);
            next.IsSuccess.ShouldBeFalse("Page Down after exit returned success");
            next.Error.Code.ShouldBe("playback.no_run");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
