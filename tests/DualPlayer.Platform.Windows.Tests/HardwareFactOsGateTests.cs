using DualPlayer.Tests;

namespace DualPlayer.Platform.Windows.Tests;

/// <summary>
/// F17: with DUALPLAYER_HARDWARE=1 a hardware fact must still be skipped on an operating system it was not written for.
/// These run on every OS (no hardware needed); they only construct the attribute.
/// </summary>
[Collection("HardwareEnvironmentVariable")]
public sealed class HardwareFactOsGateTests
{
    private static string? SkipFor(HardwareOs required, string? environmentValue)
    {
        var previous = Environment.GetEnvironmentVariable(HardwareFactAttribute.EnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(HardwareFactAttribute.EnvironmentVariable, environmentValue);
            return new HardwareFactAttribute(required).Skip;
        }
        finally
        {
            Environment.SetEnvironmentVariable(HardwareFactAttribute.EnvironmentVariable, previous);
        }
    }

    [Fact]
    public void A_fact_for_another_operating_system_is_skipped_even_when_hardware_is_enabled()
    {
        var other = OperatingSystem.IsWindows() ? HardwareOs.MacOS : HardwareOs.Windows;

        var skip = SkipFor(other, "1");

        skip.ShouldNotBeNull();
        skip.ShouldContain(other.ToString());
    }

    [Fact]
    public void A_fact_for_the_current_operating_system_runs_when_hardware_is_enabled()
    {
        var current = OperatingSystem.IsWindows() ? HardwareOs.Windows : HardwareOs.MacOS;

        SkipFor(current, "1").ShouldBeNull();
    }

    [Fact]
    public void A_fact_for_the_current_operating_system_is_skipped_when_hardware_is_not_enabled()
    {
        var current = OperatingSystem.IsWindows() ? HardwareOs.Windows : HardwareOs.MacOS;

        SkipFor(current, null).ShouldNotBeNull();
    }
}
