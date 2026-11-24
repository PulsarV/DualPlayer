namespace DualPlayer.Media.Tests;

/// <summary>
/// FFmpeg checks its interrupt callback only between I/O calls, so cancellation takes effect at the next I/O boundary
/// (AC-09b). <see cref="AbandonedWorkerTests"/> proves that boundary is reached promptly on a trickling pipe on Windows and
/// macOS; a read blocked inside the OS never reaches one, so the feature docs must say what such a read costs.
/// </summary>
public sealed class InterruptBoundaryTests
{
    [Theory]
    [InlineData("spec.md")]
    [InlineData("sad.md")]
    public void The_feature_docs_say_when_cancellation_takes_effect_and_what_a_blocked_read_costs_AC_09b(string doc)
    {
        var text = File.ReadAllText(FeatureDoc(doc));

        text.ShouldContain("next I/O boundary", Case.Insensitive, $"{doc} must say cancellation takes effect at the next I/O boundary");
        text.ShouldContain("blocked inside the OS", Case.Insensitive, $"{doc} must say a read blocked inside the OS holds its thread until the OS gives up");
        text.ShouldContain("until the OS gives up", Case.Insensitive, $"{doc} must say how long a blocked read holds its thread");
    }

    private static string FeatureDoc(string name)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "docs", "features", "lip-synced-video", name);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException($"docs/features/lip-synced-video/{name} not found above {AppContext.BaseDirectory}");
    }
}
