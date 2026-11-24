using Avalonia;
using DualPlayer.App.Startup;
using DualPlayer.App.Views;
using Shouldly;
using AvaloniaScreen = Avalonia.Platform.Screen;

namespace DualPlayer.App.Tests.Startup;

public class ScreenChoiceTests
{
    // Two screens, the primary listed second (AC-01 default, AC-03b number choice).
    private static readonly ScreenBounds First = new(0, 0, 1280, 720, IsPrimary: false);
    private static readonly ScreenBounds Second = new(1280, 0, 1920, 1080, IsPrimary: true);
    private static readonly ScreenBounds[] Screens = [First, Second];

    [Fact]
    public void Map_numbers_the_screens_from_1_in_list_order()
    {
        var mapped = ScreenChoice.Map(Screens);

        mapped.Select(s => s.Number).ShouldBe([1, 2]);
        mapped.Select(s => (s.Width, s.Height)).ShouldBe([(1280, 720), (1920, 1080)]);
    }

    [Fact]
    public void Map_marks_only_the_primary_screen_as_main()
    {
        ScreenChoice.Map(Screens).Select(s => s.IsMain).ShouldBe([false, true]);
    }

    [Fact]
    public void Pick_without_a_number_chooses_the_primary_screen_even_when_listed_second()
    {
        ScreenChoice.Pick(Screens, null).ShouldBe(Second);
    }

    [Fact]
    public void Pick_number_2_chooses_the_second_screen()
    {
        ScreenChoice.Pick(Screens, 2).ShouldBe(Second);
    }

    [Fact]
    public void Pick_number_1_chooses_the_first_screen()
    {
        ScreenChoice.Pick(Screens, 1).ShouldBe(First);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(-1)]
    public void Pick_an_unknown_number_falls_back_to_the_primary_screen(int number)
    {
        ScreenChoice.Pick(Screens, number).ShouldBe(Second);
    }

    // ---- the Avalonia side: reading screens and the window's target point (AA6/AB5) ----

    // Two Avalonia screens, the primary listed second, both away from the origin and with X != Y, so a dropped or
    // swapped coordinate shows.
    private static readonly AvaloniaScreen[] AvaloniaScreens =
    [
        new(1.0, new PixelRect(-1920, 120, 1920, 1080), new PixelRect(-1920, 120, 1920, 1040), false),
        new(1.0, new PixelRect(300, 40, 2560, 1440), new PixelRect(300, 40, 2560, 1400), true),
    ];

    [Fact]
    public void ToBounds_keeps_each_avalonia_screens_position_size_and_primary_flag_AC_01_AC_03b()
    {
        AvaloniaScreenCatalog.ToBounds(AvaloniaScreens).ShouldBe(
        [
            new ScreenBounds(-1920, 120, 1920, 1080, IsPrimary: false),
            new ScreenBounds(300, 40, 2560, 1440, IsPrimary: true),
        ]);
    }

    [Fact]
    public void Without_a_number_the_window_goes_to_the_primary_screens_top_left_AC_01()
    {
        PlaybackWindow.PlacementFor(AvaloniaScreens, null).ShouldBe(new PixelPoint(300, 40));
    }

    [Fact]
    public void An_unknown_number_sends_the_window_to_the_primary_screens_top_left()
    {
        PlaybackWindow.PlacementFor(AvaloniaScreens, 9).ShouldBe(new PixelPoint(300, 40));
    }

    [Fact]
    public void Number_1_and_number_2_send_the_window_to_those_screens_top_left_AC_03b()
    {
        PlaybackWindow.PlacementFor(AvaloniaScreens, 1).ShouldBe(new PixelPoint(-1920, 120));
        PlaybackWindow.PlacementFor(AvaloniaScreens, 2).ShouldBe(new PixelPoint(300, 40));
    }

    [Fact]
    public void With_no_screens_there_is_no_placement()
    {
        PlaybackWindow.PlacementFor([], 1).ShouldBeNull();
    }
}
