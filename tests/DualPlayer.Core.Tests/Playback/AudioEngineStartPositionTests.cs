using DualPlayer.Core.Diagnostics;
using DualPlayer.Core.Playback;
using DualPlayer.Core.Tests.Fakes;

namespace DualPlayer.Core.Tests.Playback;

/// <summary>
/// T41: a run can start at a given film position (<c>EngineOptions.StartPosition</c>, a <see cref="TimeSpan"/>, default zero), so a
/// return into the film after its end pre-rolls and starts at the target and no audio from position 0 is ever rendered.
/// The fake decoder gives frame n the value n / 1000 on every channel, silence is 0: a sample's value names its film position.
/// </summary>
public sealed class AudioEngineStartPositionTests
{
    private static readonly TimeSpan Position = TimeSpan.FromSeconds(20);

    private static EngineOptions StartingAt(TimeSpan position) => new() { StartPosition = position };

    [Fact]
    public void A_run_started_at_a_position_renders_the_film_from_there_as_its_first_audio_AC_05()
    {
        using var rig = new EngineRig([new RigDevice(48_000), new RigDevice(48_000)]);

        rig.Start(StartingAt(Position));
        rig.Render(200);

        foreach (var device in rig.Devices)
        {
            var audible = device.Rendered.Where(v => v > 0).ToArray();
            audible.ShouldNotBeEmpty("the run played nothing");
            // 20 s = value 960; the first audio of the run is the film at the start position (not 0), within 50 ms.
            audible[0].ShouldBeInRange(957.6f, 962.4f, "the first audio rendered is not the film at the start position");
            audible.Min().ShouldBeGreaterThan(950f, "audio from before the start position was rendered");
        }
    }

    [Fact]
    public void A_run_started_at_a_position_starts_its_master_clock_at_that_position_AC_05()
    {
        using var rig = new EngineRig([new RigDevice(48_000), new RigDevice(48_000)]);

        rig.Start(StartingAt(Position));

        var started = rig.Events.OfType<AudioStarted>().Single();
        started.ClockPosition.TotalSeconds.ShouldBe(Position.TotalSeconds, 0.05, "the clock did not start at the start position");
    }
}
