namespace DualPlayer.Media.Tests.Video;

public sealed class SpikeReportTests
{
    [Fact]
    public void Render_puts_each_kill_criterion_line_in_a_table_row_with_its_verdict()
    {
        var report = new SpikeReport("Test header");
        report.AddCriterion("Steady play within 20 ms", "max 12.0 ms", SpikeVerdict.Pass);
        report.AddCriterion("Back within 1 s", "max 1400 ms", SpikeVerdict.Fail);
        report.AddCriterion("No visible jump", "0 backward steps", null);

        var text = report.Render();

        text.ShouldContain("Test header");
        text.ShouldContain("| Kill-criterion line | Measured | Verdict | Owner observation |");
        text.ShouldContain("| Steady play within 20 ms | max 12.0 ms | PASS |  |");
        text.ShouldContain("| Back within 1 s | max 1400 ms | FAIL |  |");
        text.ShouldContain("| No visible jump | 0 backward steps | OWNER |  |");
    }

    [Fact]
    public void Render_escapes_pipes_and_lists_notes_and_sections()
    {
        var report = new SpikeReport("H");
        report.AddCriterion("a|b", "c", SpikeVerdict.NotMeasured);
        report.AddSection("Timer granularity", ["default: 3 ms", "1 ms timer: 1 ms"]);

        var text = report.Render();

        text.ShouldContain("| a\\|b | c | NOT MEASURED |  |");
        text.ShouldContain("## Timer granularity");
        text.ShouldContain("- default: 3 ms");
        text.ShouldContain("- 1 ms timer: 1 ms");
    }

    [Fact]
    public void Overall_is_fail_when_any_line_fails_pass_when_all_numeric_lines_pass()
    {
        var failing = new SpikeReport("H");
        failing.AddCriterion("a", "1", SpikeVerdict.Pass);
        failing.AddCriterion("b", "2", SpikeVerdict.Fail);
        failing.Overall.ShouldBe(SpikeVerdict.Fail);

        var passing = new SpikeReport("H");
        passing.AddCriterion("a", "1", SpikeVerdict.Pass);
        passing.AddCriterion("b", "2", null);
        passing.Overall.ShouldBe(SpikeVerdict.Pass);

        new SpikeReport("H").Overall.ShouldBe(SpikeVerdict.NotMeasured);
    }
}
