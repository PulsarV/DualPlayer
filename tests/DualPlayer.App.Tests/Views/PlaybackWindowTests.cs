using Avalonia.Controls;
using DualPlayer.App.Controls;
using DualPlayer.App.Views;

namespace DualPlayer.App.Tests.Views;

public sealed class PlaybackWindowTests
{
    private static string AppSource(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "DualPlayer.App")))
        {
            dir = dir.Parent;
        }

        dir.ShouldNotBeNull();
        return File.ReadAllText(Path.Combine([dir.FullName, "src", "DualPlayer.App", .. parts]));
    }

    [Fact]
    public void Video_host_is_a_native_control_host_exposing_the_child_handle()
    {
        typeof(VideoHost).BaseType.ShouldBe(typeof(NativeControlHost));
        typeof(VideoHost).GetProperty(nameof(VideoHost.NativeHandle))!.PropertyType.ShouldBe(typeof(nint));
    }

    [Fact]
    public void Playback_window_places_itself_on_a_screen_and_exposes_the_video_handle()
    {
        typeof(PlaybackWindow).BaseType.ShouldBe(typeof(Window));
        typeof(PlaybackWindow).GetMethod(nameof(PlaybackWindow.PlaceOnScreen), [typeof(int?)]).ShouldNotBeNull();
        typeof(PlaybackWindow).GetProperty(nameof(PlaybackWindow.VideoHandle))!.PropertyType.ShouldBe(typeof(nint));
    }

    [Fact]
    public void Playback_window_markup_is_fullscreen_chromeless_cursorless_and_styled_from_tokens()
    {
        var xaml = AppSource("Views", "PlaybackWindow.axaml");

        xaml.ShouldContain("x:DataType=\"vm:PlaybackViewModel\"");
        xaml.ShouldContain("WindowState=\"FullScreen\"");
        xaml.ShouldContain("SystemDecorations=\"None\"");
        xaml.ShouldContain("Cursor=\"None\"");
        xaml.ShouldContain("{StaticResource SurfaceBrush}");
        xaml.ShouldContain("controls:VideoHost");
    }

    [Fact]
    public void Tokens_define_the_brief_note_style()
    {
        var tokens = AppSource("Themes", "Tokens.axaml");

        tokens.ShouldContain("OsdNoteBottomMarginRatio\">0.05<");
        tokens.ShouldContain("x:Key=\"OsdNoteBackingColor\" ResourceKey=\"SurfaceColor\"");
        tokens.ShouldContain("x:Key=\"OsdNoteFontSize\" ResourceKey=\"FontSizeBody\"");
    }
}
