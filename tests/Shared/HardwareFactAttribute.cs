using System.Runtime.CompilerServices;

namespace DualPlayer.Tests;

/// <summary>The operating system a hardware test was written for.</summary>
public enum HardwareOs
{
    Windows,
    MacOS,
}

/// <summary>
/// A fact that needs real audio hardware of one operating system. Skipped unless the environment variable
/// <c>DUALPLAYER_HARDWARE=1</c> is set, and skipped on any other operating system even then.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class HardwareFactAttribute : FactAttribute
{
    public const string EnvironmentVariable = "DUALPLAYER_HARDWARE";

    public HardwareFactAttribute(
        HardwareOs os,
        [CallerFilePath] string? sourceFilePath = null,
        [CallerLineNumber] int sourceLineNumber = -1)
        : base(sourceFilePath, sourceLineNumber)
    {
        if (!IsEnabled)
        {
            Skip = $"Needs real audio hardware; set {EnvironmentVariable}=1 to run.";
        }
        else if (!IsCurrentOs(os))
        {
            Skip = $"Needs {os} audio hardware; this test does not run on the current operating system.";
        }
    }

    public static bool IsEnabled => Environment.GetEnvironmentVariable(EnvironmentVariable) == "1";

    private static bool IsCurrentOs(HardwareOs os) => os switch
    {
        HardwareOs.Windows => OperatingSystem.IsWindows(),
        HardwareOs.MacOS => OperatingSystem.IsMacOS(),
        _ => false,
    };
}
