namespace DualPlayer.Media.Video;

/// <summary>
/// How a note over the picture looks (SCR-03), from the App's design tokens: text size in px at 1080 lines, ARGB colours
/// of the text (info and warning) and of its backing box, and the gap to the bottom edge as a share of the height.
/// </summary>
public sealed record OsdNoteStyle(double FontSizePx, uint ForegroundArgb, uint WarningArgb, uint BackingArgb, double BottomMarginRatio)
{
    /// <summary>The token values of <c>Themes/Tokens.axaml</c>, for a renderer built without the App.</summary>
    public static OsdNoteStyle Default { get; } = new(32, 0xFFFFFFFF, 0xFFFFD400, 0xFF000000, 0.05);
}
