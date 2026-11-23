using DualPlayer.App.Startup;
using Shouldly;

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
}
