namespace DualPlayer.Console.Tests.E2E;

/// <summary>The E2E rig itself must never spin forever when a device stops taking audio while it is being fed.</summary>
public sealed class ConsoleRigTests
{
    [Fact]
    public async Task Moving_time_on_after_a_device_stopped_rendering_returns_instead_of_spinning()
    {
        using var rig = new ConsoleRig();
        rig.StartPlay("play", ConsoleRig.Media("two-tracks.mkv"), "--out", "1=1", "--out", "2=2");
        rig.PumpUntilLine("started");

        // A lost device is not closed until the engine gets to it; until then it renders nothing while time moves on.
        rig.Devices[1].SimulateLoss();
        var pump = Task.Run(() => rig.PumpUntilLine("device-lost"), TestContext.Current.CancellationToken);

        var finished = await Task.WhenAny(pump, Task.Delay(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
        finished.ShouldBe(pump, "the rig kept feeding a device that renders nothing");
        await pump;

        rig.Send("stop");
        rig.Finish().ShouldBe(0);
    }
}
