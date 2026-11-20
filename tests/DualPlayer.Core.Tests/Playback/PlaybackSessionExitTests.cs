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

    private static int AudioStartedCount(SessionRig rig) => rig.Engine.Events.OfType<AudioStarted>().Count();

    [Fact]
    public void A_backward_seek_queued_behind_Exit_at_the_end_of_the_film_starts_no_engine_run_AC_06()
    {
        using var rig = PlayingRig(totalSeconds: 30);
        rig.Renderer.Duration = TimeSpan.FromSeconds(30);
        rig.Engine.Call(() => rig.Session.Seek(TimeSpan.FromSeconds(60))).IsSuccess.ShouldBeTrue();
        rig.Engine.Render(50);
        rig.Session.State.ShouldBe(PlaybackState.Ended);
        Exit(rig);
        var startedBefore = AudioStartedCount(rig);

        var seek = rig.Engine.Call(() => rig.Session.Seek(TimeSpan.FromSeconds(-10)));
        rig.Engine.Render(150);

        AudioStartedCount(rig).ShouldBe(startedBefore, "a new audio run started after the session had exited");
        seek.IsSuccess.ShouldBeFalse("a seek after exit returned success");
        seek.Error.Code.ShouldBe("playback.no_run");
        rig.Session.State.ShouldBe(PlaybackState.Ended);
    }

    [Fact]
    public void A_backward_seek_queued_behind_Exit_while_playing_starts_no_engine_run_AC_06()
    {
        using var rig = PlayingRig(totalSeconds: 600);
        Exit(rig);
        var startedBefore = AudioStartedCount(rig);

        var seek = rig.Engine.Call(() => rig.Session.Seek(TimeSpan.FromSeconds(-10)));
        rig.Engine.Render(150);

        AudioStartedCount(rig).ShouldBe(startedBefore, "a new audio run started after the session had exited");
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
            var startedBefore = AudioStartedCount(rig);
            var eventsBefore = rig.Events.Count;

            var next = rig.Engine.Call(() => rig.Session.NextFile());
            rig.Engine.Render(50);

            probed.ShouldBe(0, "the folder was scanned and its files probed after exit");
            rig.Events.Skip(eventsBefore).ShouldBeEmpty("Page Down after exit published events");
            AudioStartedCount(rig).ShouldBe(startedBefore);
            next.IsSuccess.ShouldBeFalse("Page Down after exit returned success");
            next.Error.Code.ShouldBe("playback.no_run");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
