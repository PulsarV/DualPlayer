using System.Diagnostics;
using DualPlayer.Core.Playback;
using Shouldly;

namespace DualPlayer.Core.Tests.Playback;

/// <summary>AC-07 / AC-08: folder neighbours in natural name order.</summary>
public sealed class FolderNeighboursTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("neighbours").FullName;

    public void Dispose() => Directory.Delete(_dir, true);

    private string Touch(string name)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllBytes(path, []);
        return path;
    }

    private string[] Names(string current, NeighbourDirection d) =>
        [.. FolderNeighbours.Candidates(current, d).Select(Path.GetFileName)!];

    [Fact]
    public void Next_lists_following_files_in_natural_order_so_Episode_2_precedes_Episode_10()
    {
        Touch("Episode 10.mkv");
        var e2 = Touch("Episode 2.mkv");
        Touch("Episode 1.mp4");
        Touch("Episode 3.mkv");

        Names(e2, NeighbourDirection.Next).ShouldBe(["Episode 3.mkv", "Episode 10.mkv"]);
    }

    [Fact]
    public void Previous_lists_preceding_files_nearest_first()
    {
        Touch("Episode 1.mkv");
        Touch("Episode 2.mkv");
        var e10 = Touch("Episode 10.mkv");

        Names(e10, NeighbourDirection.Previous).ShouldBe(["Episode 2.mkv", "Episode 1.mkv"]);
    }

    [Fact]
    public void Extensions_are_matched_in_any_letter_case_and_other_files_and_folders_are_ignored()
    {
        var a = Touch("a.mkv");
        Touch("b.MP4");
        Touch("c.Mkv");
        Touch("d.avi");
        Touch("e.srt");
        Directory.CreateDirectory(Path.Combine(_dir, "f.mkv.d"));
        Directory.CreateDirectory(Path.Combine(_dir, "g.mp4"));

        Names(a, NeighbourDirection.Next).ShouldBe(["b.MP4", "c.Mkv"]);
    }

    [Fact]
    public void Names_equal_apart_from_letter_case_are_both_kept_and_ordered_by_exact_comparison()
    {
        // Windows (NTFS) and macOS (APFS) folders cannot hold two such names, so the rule is checked on the comparer itself.
        List<string> names = ["Episode 2.mkv", "Episode 1.mkv", "EPISODE 1.mkv"];
        List<string> reversed = ["EPISODE 1.mkv", "Episode 1.mkv", "Episode 2.mkv"];

        names.Sort(FolderNeighbours.NaturalNameComparer.Instance);
        reversed.Reverse();
        reversed.Sort(FolderNeighbours.NaturalNameComparer.Instance);

        names.ShouldBe(["EPISODE 1.mkv", "Episode 1.mkv", "Episode 2.mkv"]);
        reversed.ShouldBe(["EPISODE 1.mkv", "Episode 1.mkv", "Episode 2.mkv"]);
    }

    [Fact]
    public void Without_a_file_after_the_current_one_the_sequence_is_empty()
    {
        Touch("a.mkv");
        var last = Touch("b.mkv");

        Names(last, NeighbourDirection.Next).ShouldBeEmpty();
    }

    [Fact]
    public void Without_a_file_before_the_current_one_the_sequence_is_empty()
    {
        var first = Touch("a.mkv");
        Touch("b.mkv");

        Names(first, NeighbourDirection.Previous).ShouldBeEmpty();
    }

    [Fact]
    public void The_folder_is_read_again_on_every_call()
    {
        var a = Touch("a.mkv");
        Names(a, NeighbourDirection.Next).ShouldBeEmpty();

        Touch("b.mkv");

        Names(a, NeighbourDirection.Next).ShouldBe(["b.mkv"]);
    }

    [Fact]
    public void Ten_thousand_files_are_listed_and_sorted_well_within_the_start_budget()
    {
        for (var i = 0; i < 10_000; i++)
        {
            Touch($"Episode {i}.mkv");
        }

        var current = Path.Combine(_dir, "Episode 5000.mkv");
        var sw = Stopwatch.StartNew();
        var first = FolderNeighbours.Candidates(current, NeighbourDirection.Next).First();
        sw.Stop();

        Path.GetFileName(first).ShouldBe("Episode 5001.mkv");
        sw.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(1));
    }
}
