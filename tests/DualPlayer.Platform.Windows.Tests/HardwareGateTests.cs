using DualPlayer.Tests;

namespace DualPlayer.Platform.Windows.Tests;

public sealed class HardwareGateTests
{
    [HardwareFact(HardwareOs.Windows)]
    public void Runs_only_on_a_windows_machine_with_hardware_enabled() =>
        OperatingSystem.IsWindows().ShouldBeTrue();
}
