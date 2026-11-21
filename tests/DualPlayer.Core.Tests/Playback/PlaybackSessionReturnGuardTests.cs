using DualPlayer.Core.Diagnostics;
using DualPlayer.Core.Playback;
using DualPlayer.Core.Sync;
using DualPlayer.Core.Tests.Fakes;

namespace DualPlayer.Core.Tests.Playback;

/// <summary>T48: a return into the film needs a run of that file; a summary is carried only for its own file; the old film's late errors are stale.</summary>
public sealed class PlaybackSessionReturnGuardTests : IDisposable
{
    private static readonly RigDevice[] TwoDevices = [new RigDevice(48_000), new RigDevice(48_000)];
    private readonly string _folder = Directory.CreateTempSubdirectory("dualplayer-returnguard-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private string File(string name)
    {
        var path = Path.Combine(_folder, name);
        System.IO.File.WriteAllBytes(path, []);
        return path;
    }

    private SessionRig PlayingRig(int steps)
    {
        File("Episode 1.mkv");
        File("Episode 3.mkv");
        var rig = new SessionRig(TwoDevices, null, 600);
        rig.Probe.Set("dev-1", new DeviceLatencyReport(TimeSpan.FromMilliseconds(40), "test", ConnectionKind.Bluetooth, FormFactor.Headphones));
        rig.Probe.Set("dev-2", new DeviceLatencyReport(TimeSpan.Zero, "test", ConnectionKind.Usb, FormFactor.Speaker));
        rig.Engine.Call(() => rig.Session.Start(rig.Request(2) with { File = File("Episode 2.mkv") })).IsSuccess.ShouldBeTrue();
        rig.Engine.Render(steps);
        return rig;
    }

    [Fact]
    public void After_the_engine_failed_to_start_the_next_file_a_backward_seek_and_Esc_carry_no_summary_of_the_old_file_AC_05_AC_06_AC_07()
    {
        using var rig = PlayingRig(2_000);
        rig.Engine.DenyAccess(2);
        rig.Engine.Call(() => rig.Session.NextFile()).IsSuccess.ShouldBeFalse("the engine could not open the device");
        rig.Engine.AllowAccess(2); // the device is back, so a return run that is wrongly started would succeed
        rig.Engine.Barrier();
        var runsBefore = rig.EngineRunFiles.Count;

        rig.Engine.Call(() => rig.Session.Seek(TimeSpan.FromSeconds(-10)));
        rig.Session.State.ShouldBe(PlaybackState.Ended);
        rig.EngineRunFiles.Count.ShouldBe(runsBefore, "the failed start left no film to return into");
        rig.Engine.Render(100);
        rig.Engine.Call(() =>
        {
            rig.Session.Exit();
            return true;
        });
        rig.Engine.Barrier();

        var summaries = rig.Engine.Events.OfType<RunSummary>().ToList();
        summaries.Count.ShouldBe(1, "only Episode 2's run ever played");
        summaries[0].EndReason.ShouldBe(RunEndReason.FileChange);
        foreach (var later in summaries.Skip(1))
        {
            later.Outputs.ShouldAllBe(o => o.Played < TimeSpan.FromSeconds(1), "no run of Episode 3 may carry the 20 s Episode 2 played");
        }
    }

    [Fact]
    public void An_old_film_decode_failure_raised_before_the_new_picture_loads_does_not_end_the_new_film_AC_06b()
    {
        using var rig = PlayingRig(300);
        rig.Probe.BeforeProbe = () =>
        {
            rig.Probe.BeforeProbe = null;
            rig.Renderer.InjectDecodeFailure(new Error("video.decode_failed", "The old film cannot be decoded."));
        };

        rig.Engine.Call(() => rig.Session.NextFile()).IsSuccess.ShouldBeTrue();
        rig.Engine.Render(100);
        rig.Flush();

        rig.Session.State.ShouldBe(PlaybackState.Playing, "the failure belonged to the old film");
        rig.Events.OfType<NoteShown>().ShouldNotContain(n => n.Text.Contains("decode", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void After_a_file_change_failed_before_the_picture_loaded_a_backward_seek_starts_no_run_and_the_session_stays_ended_AC_05_AC_06()
    {
        using var rig = PlayingRig(2_000);
        rig.Engine.Unlist(2);
        rig.Engine.Call(() => rig.Session.NextFile()).Error!.Code.ShouldBe("outputs.device_not_found");
        rig.Engine.Relist(2); // the device is back, so a return run that is wrongly started would succeed
        rig.Engine.Barrier();
        var runsBefore = rig.EngineRunFiles.Count;

        rig.Engine.Call(() => rig.Session.Seek(TimeSpan.FromSeconds(-10)));

        rig.EngineRunFiles.Count.ShouldBe(runsBefore, "the old film was given up when the change began");
        rig.Session.State.ShouldBe(PlaybackState.Ended);
    }
}
