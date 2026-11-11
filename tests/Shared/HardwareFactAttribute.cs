using System.Runtime.CompilerServices;

namespace DualPlayer.Tests;

/// <summary>
/// A fact that needs real audio hardware. Skipped unless the environment variable
/// <c>DUALPLAYER_HARDWARE=1</c> is set.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class HardwareFactAttribute : FactAttribute
{
    public const string EnvironmentVariable = "DUALPLAYER_HARDWARE";

    public HardwareFactAttribute(
        [CallerFilePath] string? sourceFilePath = null,
        [CallerLineNumber] int sourceLineNumber = -1)
        : base(sourceFilePath, sourceLineNumber)
    {
        if (!IsEnabled)
        {
            Skip = $"Needs real audio hardware; set {EnvironmentVariable}=1 to run.";
        }
    }

    public static bool IsEnabled => Environment.GetEnvironmentVariable(EnvironmentVariable) == "1";
}
