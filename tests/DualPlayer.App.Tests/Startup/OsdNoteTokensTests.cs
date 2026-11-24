using System.Globalization;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using DualPlayer.App.Startup;
using DualPlayer.App.Tests.Views;

namespace DualPlayer.App.Tests.Startup;

public sealed class OsdNoteTokensTests
{
    [Fact]
    public void The_note_style_is_read_from_the_osd_note_tokens() => HeadlessApp.Run(() =>
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
    });

    [Fact]
    public void A_missing_token_falls_back_to_the_default_note_style() => HeadlessApp.Run(() =>
    {
        var style = OsdNoteTokens.From(new ResourceDictionary());

        style.ShouldBe(DualPlayer.Media.Video.OsdNoteStyle.Default);
    });

    [Fact]
    public void Tokens_define_the_warning_note_colour_from_the_focus_colour()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "DualPlayer.App", "Themes", "Tokens.axaml");
        File.ReadAllText(path).ShouldContain("x:Key=\"OsdNoteWarningColor\" ResourceKey=\"FocusColor\"");
    }

    // Themes/Tokens.axaml is read as text: the values the note must show, independent of OsdNoteStyle.Default (which repeats them).
    private static string TokensSource() =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "DualPlayer.App", "Themes", "Tokens.axaml"));

    private static string Token(string source, string tag, string key) =>
        Regex.Match(source, $"""<{tag} x:Key="{key}">([^<]+)</{tag}>""").Groups[1].Value.Trim();

    private static uint ArgbOf(string source, string key) =>
        uint.Parse(Token(source, "Color", key).TrimStart('#'), NumberStyles.HexNumber, CultureInfo.InvariantCulture);

    [Fact]
    public void The_real_app_resources_define_every_osd_note_token_with_the_value_of_Tokens_axaml_AC_01() => HeadlessApp.Run(() =>
    {
        var source = TokensSource();
        var resources = Application.Current.ShouldNotBeNull().Resources;

        // Every token resolves in the merged resources with its real type: a key that is missing or does not resolve would
        // make OsdNoteTokens.From fall back to OsdNoteStyle.Default without a word, and the default repeats these values.
        Lookup<double>(resources, "OsdNoteFontSize").ShouldBe(double.Parse(Token(source, "x:Double", "FontSizeBody"), CultureInfo.InvariantCulture));
        Lookup<Color>(resources, "OsdNoteForegroundColor").ToUInt32().ShouldBe(ArgbOf(source, "ForegroundColor"));
        Lookup<Color>(resources, "OsdNoteWarningColor").ToUInt32().ShouldBe(ArgbOf(source, "FocusColor"));
        Lookup<Color>(resources, "OsdNoteBackingColor").ToUInt32().ShouldBe(ArgbOf(source, "SurfaceColor"));
        Lookup<double>(resources, "OsdNoteBottomMarginRatio").ShouldBe(double.Parse(Token(source, "x:Double", "OsdNoteBottomMarginRatio"), CultureInfo.InvariantCulture));
    });

    [Fact]
    public void The_note_style_read_from_the_real_app_resources_has_the_token_values_AC_01() => HeadlessApp.Run(() =>
    {
        var source = TokensSource();

        var style = OsdNoteTokens.From(Application.Current.ShouldNotBeNull().Resources);

        style.FontSizePx.ShouldBe(double.Parse(Token(source, "x:Double", "FontSizeBody"), CultureInfo.InvariantCulture));
        style.ForegroundArgb.ShouldBe(ArgbOf(source, "ForegroundColor"));
        style.WarningArgb.ShouldBe(ArgbOf(source, "FocusColor"));
        style.BackingArgb.ShouldBe(ArgbOf(source, "SurfaceColor"));
        style.BottomMarginRatio.ShouldBe(double.Parse(Token(source, "x:Double", "OsdNoteBottomMarginRatio"), CultureInfo.InvariantCulture));
        style.ShouldBe(DualPlayer.Media.Video.OsdNoteStyle.Default, "the renderer's built-in default must repeat the token values");
    });

    private static T Lookup<T>(IResourceNode resources, string key)
    {
        resources.TryGetResource(key, ThemeVariant.Dark, out var value).ShouldBeTrue($"token {key} is not defined in the App resources");
        return value.ShouldBeOfType<T>($"token {key}");
    }
}
