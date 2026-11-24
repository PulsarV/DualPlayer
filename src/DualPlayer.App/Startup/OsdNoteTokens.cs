using Avalonia.Controls;
using Avalonia.Media;
using DualPlayer.Media.Video;

namespace DualPlayer.App.Startup;

/// <summary>Reads the <c>OsdNote*</c> design tokens (<c>Themes/Tokens.axaml</c>) into the renderer's note style.</summary>
internal static class OsdNoteTokens
{
    /// <summary>The note style from <paramref name="resources"/>; a token that is missing keeps its default.</summary>
    public static OsdNoteStyle From(IResourceNode resources)
    {
        ArgumentNullException.ThrowIfNull(resources);
        var d = OsdNoteStyle.Default;
        return new OsdNoteStyle(
            Number(resources, "OsdNoteFontSize") ?? d.FontSizePx,
            Argb(resources, "OsdNoteForegroundColor") ?? d.ForegroundArgb,
            Argb(resources, "OsdNoteWarningColor") ?? d.WarningArgb,
            Argb(resources, "OsdNoteBackingColor") ?? d.BackingArgb,
            Number(resources, "OsdNoteBottomMarginRatio") ?? d.BottomMarginRatio);
    }

    private static double? Number(IResourceNode resources, string key) =>
        resources.TryGetResource(key, null, out var value) && value is double number ? number : null;

    private static uint? Argb(IResourceNode resources, string key) =>
        resources.TryGetResource(key, null, out var value) && value is Color color ? color.ToUInt32() : null;
}
