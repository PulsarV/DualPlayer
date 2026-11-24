using System.Globalization;
using System.Text;

namespace DualPlayer.Media.Tests.Video;

/// <summary>Markdown report of the libmpv spike: the kill-criterion table plus free sections of measured facts.</summary>
public sealed class SpikeReport(string header)
{
    private readonly List<(string Line, string Measured, SpikeVerdict? Verdict)> _criteria = [];
    private readonly List<(string Title, IReadOnlyList<string> Items)> _sections = [];

    /// <summary>Gets the worst verdict over the numeric lines; owner-observation lines do not count.</summary>
    public SpikeVerdict Overall
    {
        get
        {
            var verdicts = _criteria.Where(c => c.Verdict is not null).Select(c => c.Verdict!.Value).ToList();
            if (verdicts.Contains(SpikeVerdict.Fail))
            {
                return SpikeVerdict.Fail;
            }

            return verdicts.Contains(SpikeVerdict.Pass) ? SpikeVerdict.Pass : SpikeVerdict.NotMeasured;
        }
    }

    /// <summary>Adds a kill-criterion line; a <c>null</c> verdict leaves it to the owner's observation.</summary>
    public void AddCriterion(string line, string measured, SpikeVerdict? verdict) =>
        _criteria.Add((line, measured, verdict));

    public void AddSection(string title, IReadOnlyList<string> items) => _sections.Add((title, items));

    public string Render()
    {
        var text = new StringBuilder();
        text.AppendLine(header).AppendLine();
        text.AppendLine("| Kill-criterion line | Measured | Verdict | Owner observation |");
        text.AppendLine("|---|---|---|---|");
        foreach (var (line, measured, verdict) in _criteria)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"| {Cell(line)} | {Cell(measured)} | {VerdictText(verdict)} |  |");
        }

        foreach (var (title, items) in _sections)
        {
            text.AppendLine().AppendLine(CultureInfo.InvariantCulture, $"## {title}").AppendLine();
            foreach (var item in items)
            {
                text.AppendLine(CultureInfo.InvariantCulture, $"- {item}");
            }
        }

        return text.ToString();
    }

    private static string Cell(string value) => value.Replace("|", "\\|", StringComparison.Ordinal);

    private static string VerdictText(SpikeVerdict? verdict) => verdict switch
    {
        SpikeVerdict.Pass => "PASS",
        SpikeVerdict.Fail => "FAIL",
        SpikeVerdict.NotMeasured => "NOT MEASURED",
        _ => "OWNER",
    };
}
