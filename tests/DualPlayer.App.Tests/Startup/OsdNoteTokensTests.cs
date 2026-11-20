using Avalonia.Controls;
using Avalonia.Media;
using DualPlayer.App.Startup;

namespace DualPlayer.App.Tests.Startup;

public sealed class OsdNoteTokensTests
{
    [Fact]
    public void The_note_style_is_read_from_the_osd_note_tokens()
    {
        var resources = new ResourceDictionary
        {
            ["OsdNoteFontSize"] = 40.0,
            ["OsdNoteForegroundColor"] = Color.FromArgb(0xFF, 0xEE, 0xEE, 0xEE),
            ["OsdNoteWarningColor"] = Color.FromArgb(0xFF, 0xFF, 0xD4, 0x00),
            ["OsdNoteBackingColor"] = Color.FromArgb(0xFF, 0x10, 0x10, 0x10),
            ["OsdNoteBottomMarginRatio"] = 0.08,
        };

        var style = OsdNoteTokens.From(resources);

        style.FontSizePx.ShouldBe(40.0);
        style.ForegroundArgb.ShouldBe(0xFFEEEEEEu);
        style.WarningArgb.ShouldBe(0xFFFFD400u);
        style.BackingArgb.ShouldBe(0xFF101010u);
        style.BottomMarginRatio.ShouldBe(0.08);
    }

    [Fact]
    public void A_missing_token_falls_back_to_the_default_note_style()
    {
        var style = OsdNoteTokens.From(new ResourceDictionary());

        style.ShouldBe(DualPlayer.Media.Video.OsdNoteStyle.Default);
    }

    [Fact]
    public void Tokens_define_the_warning_note_colour_from_the_focus_colour()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "DualPlayer.App", "Themes", "Tokens.axaml");
        File.ReadAllText(path).ShouldContain("x:Key=\"OsdNoteWarningColor\" ResourceKey=\"FocusColor\"");
    }
}
