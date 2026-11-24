using DualPlayer.Core.Playback;

namespace DualPlayer.Core.Tests.Fakes;

/// <summary>
/// A stop the rig does not drive (a renderer event, an exit outside <see cref="EngineRig.Call{T}"/>) leaves fake time standing,
/// so the engine's stop wait can end only on its poll bound. That bound is counted in real sleeps, whose length depends on the
/// machine: the production 2000 polls are about 4 s on the dev Mac and about 31 s on a Windows CI runner (15.6 ms per sleep),
/// past the 10 s every test waits for the summary. The session rig keeps the bound at 50 polls, as AudioEngineStopBoundTests does.
/// </summary>
public sealed class SessionRigStopWaitTests : IDisposable
{
    private static readonly RigDevice[] TwoDevices = [new RigDevice(48_000), new RigDevice(48_000)];
    private readonly string _folder = Directory.CreateTempSubdirectory("dualplayer-stopwait-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void Every_engine_run_the_session_rig_starts_bounds_its_stop_wait_far_below_the_production_poll_bound()
    {
        var file = Path.Combine(_folder, "Episode 1.mkv");
        File.WriteAllBytes(file, []);
        using var rig = new SessionRig(TwoDevices, null, 600);

        rig.Engine.Call(() => rig.Session.Start(rig.Request(2) with { File = file })).IsSuccess.ShouldBeTrue();

        rig.EngineStopWaitMaxPolls.ShouldNotBeEmpty();
        rig.EngineStopWaitMaxPolls.ShouldAllBe(polls => polls <= 50, $"the production bound is {new EngineOptions().StopWaitMaxPolls} polls");
    }
}
