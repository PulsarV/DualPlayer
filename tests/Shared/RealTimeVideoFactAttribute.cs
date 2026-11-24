using System.Runtime.CompilerServices;

namespace DualPlayer.Tests;

/// <summary>
/// A fact that measures real libmpv playback against real time within milliseconds (seek time, speed, presentation on
/// schedule). Skipped on CI (<c>CI=true</c>, which GitHub Actions sets): a hosted runner is a virtual machine without a GPU
/// that does not decode the samples in real time, so the measurement says nothing about the renderer. Runs on the dev
/// machine; precision on the target PC is the click-and-flash acceptance (lip-synced-video T23).
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class RealTimeVideoFactAttribute : FactAttribute
{
    public RealTimeVideoFactAttribute(
        [CallerFilePath] string? sourceFilePath = null,
        [CallerLineNumber] int sourceLineNumber = -1)
        : base(sourceFilePath, sourceLineNumber)
    {
        if (IsCi)
        {
            Skip = "Measures real-time video playback; a CI runner does not decode video in real time.";
        }
    }

    public static bool IsCi => string.Equals(Environment.GetEnvironmentVariable("CI"), "true", StringComparison.OrdinalIgnoreCase);
}
