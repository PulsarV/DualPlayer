namespace DualPlayer.App.Startup;

/// <summary>A screen's position and size in pixels, and whether the OS marks it as the main one.</summary>
internal readonly record struct ScreenBounds(int X, int Y, int Width, int Height, bool IsPrimary);

/// <summary>Pure screen logic, kept apart from Avalonia so it can be tested with several screens.</summary>
internal static class ScreenChoice
{
    /// <summary>Numbers the screens from 1 in list order; the primary one is the main screen.</summary>
    public static IReadOnlyList<ScreenInfo> Map(IReadOnlyList<ScreenBounds> screens) =>
        [.. screens.Select((s, i) => new ScreenInfo(i + 1, s.Width, s.Height, s.IsPrimary))];

    /// <summary>The screen with the given number (from 1); with no number, or an unknown one, the primary screen.</summary>
    public static ScreenBounds? Pick(IReadOnlyList<ScreenBounds> screens, int? number)
    {
        if (number is >= 1 && number <= screens.Count)
        {
            return screens[number.Value - 1];
        }

        foreach (var screen in screens)
        {
            if (screen.IsPrimary)
            {
                return screen;
            }
        }

        return null;
    }
}
