using DualPlayer.Console.Commands;
using DualPlayer.Core.Outputs;
using DualPlayer.Core.Tests.Fakes;

namespace DualPlayer.Console.Tests.E2E;

/// <summary>
/// Test-plan E2E rows for AC-01/02/03/05/08/09/10/13: the console's commands over the real engine and the real FFmpeg
/// decoder on real test media, audio through the fake backend.
/// </summary>
public sealed class ConsoleEndToEndTests
{
    private static readonly string TwoTracks = ConsoleRig.Media("two-tracks.mkv");
    private static readonly string Ramp = ConsoleRig.Media("long-gop-ramp.mkv");

    private static TimeSpan ClockOf(string line)
    {
        var at = line.Split(' ', StringSplitOptions.RemoveEmptyEntries)[^1];
        return TimeSpan.Parse(at, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static double Density(FakeAudioDevice device)
    {
        var audible = device.Rendered.Where(s => Math.Abs(s) > 1e-4f).ToArray();
        return Crossings(audible) / (double)Math.Max(1, audible.Length);
    }

    private static int Crossings(IEnumerable<float> samples)
    {
        var count = 0;
        var previous = 0f;
        foreach (var s in samples.Where(s => Math.Abs(s) > 1e-4f))
        {
            if (previous != 0 && Math.Sign(s) != Math.Sign(previous))
            {
                count++;
            }

            previous = s;
        }

        return count;
    }

    // ---- AC-01 -------------------------------------------------------------------------------------------------

    [Fact]
    public void Playing_two_tracks_on_two_devices_prints_each_output_with_its_resolved_id_and_the_clock_and_feeds_each_device_its_own_track()
    {
        using var rig = new ConsoleRig();

        rig.StartPlay("play", TwoTracks, "--out", "1=1", "--out", "2=2");
        rig.PumpUntilLine("started");
        rig.PumpUntil(() => rig.Devices.All(d => d.WrittenFrames > 12_000), "audio on both devices");

        var lines = rig.OutLines;
        lines.ShouldContain("output 1  track 1 (ukr 5.1 -> stereo, LFE dropped)  device Test Device 1  id dev-1", rig.Out.Snapshot());
        lines.ShouldContain("output 2  track 2 (eng stereo 44100 -> 48000)  device Test Device 2  id dev-2", rig.Out.Snapshot());
        lines.ShouldContain(l => l.StartsWith("started   clock 00:00:00.0", StringComparison.Ordinal), rig.Out.Snapshot());

        rig.Devices.Count.ShouldBe(2);
        var first = rig.Devices[0].Rendered.Where(s => Math.Abs(s) > 1e-4f).ToArray();
        var second = rig.Devices[1].Rendered.Where(s => Math.Abs(s) > 1e-4f).ToArray();
        first.Length.ShouldBeGreaterThan(1_000);
        second.Length.ShouldBeGreaterThan(1_000);
        // Track 1 (ukr) is a lower tone than track 2 (eng): about 0.018 zero crossings per sample against 0.037.
        Density(rig.Devices[0]).ShouldBeLessThan(0.027, "device 1 must hear track 1, the lower tone");
        Density(rig.Devices[1]).ShouldBeGreaterThan(0.027, "device 2 must hear track 2, the higher tone");

        rig.Send("stop");
        rig.Finish().ShouldBe(0);
    }

    [Fact]
    public void Swapping_the_track_numbers_swaps_what_each_device_receives()
    {
        using var rig = new ConsoleRig();

        rig.StartPlay("play", TwoTracks, "--out", "2=1", "--out", "1=2");
        rig.PumpUntil(() => rig.Devices.Count == 2 && rig.Devices.All(d => d.WrittenFrames > 12_000), "audio on both devices");

        Density(rig.Devices[0]).ShouldBeGreaterThan(0.027, "device 1 must hear track 2, the higher tone");
        Density(rig.Devices[1]).ShouldBeLessThan(0.027, "device 2 must hear track 1, the lower tone");
        rig.Send("stop");
        rig.Finish().ShouldBe(0);
    }

    // ---- AC-02 -------------------------------------------------------------------------------------------------

    [Fact]
    public void The_device_and_track_listings_show_every_device_and_track_and_start_nothing()
    {
        using var rig = new ConsoleRig();
        using var output = new StringWriter();
        using var error = new StringWriter();

        new DevicesCommand(rig.Backend).Run(output, error).ShouldBe(0);
        new TracksCommand(rig.Decoder).Run(new TracksRequest(TwoTracks), output, error).ShouldBe(0);

        var lines = ConsoleRig.Lines(output.ToString());
        lines.ShouldBe(
        [
            "#  name  type  id",
            "1  Test Device 1  bluetooth  dev-1",
            "2  Test Device 2  unknown  dev-2",
            "3  Test Device 3  unknown  dev-3",
            "#  language  layout  rate",
            "1  ukr  5.1  48000",
            "2  eng  stereo  44100",
        ]);
        error.ToString().ShouldBeEmpty();
        rig.Devices.ShouldBeEmpty("nothing may be opened by a listing");
    }

    [Fact]
    public void A_device_named_by_its_full_id_resolves_to_that_device_and_the_start_line_echoes_the_id()
    {
        using var rig = new ConsoleRig();

        rig.StartPlay("play", TwoTracks, "--out", "1=dev-3");
        rig.PumpUntilLine("started");

        rig.OutLines.ShouldContain("output 1  track 1 (ukr 5.1 -> stereo, LFE dropped)  device Test Device 3  id dev-3", rig.Out.Snapshot());
        rig.Send("stop");
        rig.Finish().ShouldBe(0);
    }

    // ---- AC-03 -------------------------------------------------------------------------------------------------

    [Fact]
    public void A_file_that_is_not_media_is_refused_with_its_reason_exit_2_and_no_device_opens()
    {
        using var rig = new ConsoleRig();

        var exit = rig.Play("play", ConsoleRig.Media("not-media.txt"), "--out", "1=1");

        exit.ShouldBe(2);
        rig.ErrLines.ShouldContain(l => l.StartsWith("error playback.file_unreadable: ", StringComparison.Ordinal), rig.Err.Snapshot());
        rig.Devices.ShouldBeEmpty();
        rig.OutLines.ShouldNotContain(l => l.StartsWith("started", StringComparison.Ordinal));
    }

    [Fact]
    public void A_file_that_does_not_exist_is_refused_with_exit_2()
    {
        using var rig = new ConsoleRig();

        rig.Play("play", ConsoleRig.Media("missing.mkv"), "--out", "1=1").ShouldBe(2);

        rig.ErrLines.ShouldContain(l => l.StartsWith("error playback.file_unreadable: ", StringComparison.Ordinal));
        rig.Devices.ShouldBeEmpty();
    }

    [Fact]
    public void A_track_the_file_does_not_have_is_refused_naming_the_track_and_the_tracks_it_has()
    {
        using var rig = new ConsoleRig();

        var exit = rig.Play("play", TwoTracks, "--out", "3=1");

        exit.ShouldBe(2);
        var error = rig.ErrLines.Single(l => l.StartsWith("error playback.track_not_found: ", StringComparison.Ordinal));
        error.ShouldContain("track 3");
        error.ShouldContain("tracks 1 and 2");
        rig.Devices.ShouldBeEmpty();
    }

    [Fact]
    public void A_device_that_is_not_connected_is_refused_with_exit_2_and_no_device_opens()
    {
        using var rig = new ConsoleRig();

        var exit = rig.Play("play", TwoTracks, "--out", "1=1", "--out", "2=9");

        exit.ShouldBe(2);
        rig.ErrLines.ShouldContain(l => l.StartsWith("error outputs.device_not_found: ", StringComparison.Ordinal), rig.Err.Snapshot());
        rig.Devices.ShouldBeEmpty("validation happens before any device opens");
    }

    // ---- AC-05 -------------------------------------------------------------------------------------------------

    [Fact]
    public void The_same_device_twice_is_refused_naming_one_audio_output_per_device_and_nothing_starts()
    {
        using var rig = new ConsoleRig();

        var exit = rig.Play("play", TwoTracks, "--out", "1=1", "--out", "2=1");

        exit.ShouldBe(2);
        var error = rig.ErrLines.Single(l => l.StartsWith("error outputs.device_assigned_twice: ", StringComparison.Ordinal));
        error.ShouldContain("one audio output per device");
        rig.Devices.ShouldBeEmpty();
    }

    [Fact]
    public void The_same_device_named_once_by_number_and_once_by_id_is_still_refused()
    {
        using var rig = new ConsoleRig();

        rig.Play("play", TwoTracks, "--out", "1=1", "--out", "1=dev-1").ShouldBe(2);

        rig.ErrLines.ShouldContain(l => l.StartsWith("error outputs.device_assigned_twice: ", StringComparison.Ordinal), rig.Err.Snapshot());
        rig.Devices.ShouldBeEmpty();
    }

    // ---- AC-08 -------------------------------------------------------------------------------------------------

    [Fact]
    public void Pause_prints_the_clock_and_resume_continues_from_the_same_position_even_after_time_has_passed()
    {
        using var rig = new ConsoleRig();
        rig.StartPlay("play", Ramp, "--out", "1=1", "--out", "2=2");
        rig.PumpUntilLine("started");
        rig.PumpUntil(() => rig.Devices.All(d => d.WrittenFrames > 48_000), "one second of audio");

        var paused = rig.SendAndWaitFor("pause", "paused at ");
        for (var i = 0; i < 300; i++)
        {
            rig.Time.Advance(TimeSpan.FromMilliseconds(10));
            Thread.Sleep(1);
        }

        var resumed = rig.SendAndWaitFor("resume", "resumed at ");

        ClockOf(paused).ShouldBeGreaterThan(TimeSpan.Zero);
        ClockOf(resumed).ShouldBe(ClockOf(paused), "the master clock stopped for the three seconds of fake time between pause and resume");

        rig.Send("stop");
        rig.Finish().ShouldBe(0);
    }

    // ---- AC-09 -------------------------------------------------------------------------------------------------

    [Fact]
    public void Seeking_ten_seconds_forward_and_back_prints_the_confirmation_at_the_new_position()
    {
        using var rig = new ConsoleRig();
        rig.StartPlay("play", Ramp, "--out", "1=1", "--out", "2=2");
        rig.PumpUntilLine("started");
        rig.PumpUntil(() => rig.Devices.All(d => d.WrittenFrames > 24_000), "audio on both devices");

        var forward = ClockOf(rig.SendAndWaitFor("seek +10", "seek confirmed at "));
        forward.ShouldBeInRange(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(11.5));

        var back = ClockOf(rig.SendAndWaitFor("seek -10", "seek confirmed at "));
        back.ShouldBeInRange(TimeSpan.Zero, TimeSpan.FromSeconds(1.5));

        rig.Send("stop");
        rig.Finish().ShouldBe(0);
        rig.ErrLines.ShouldBeEmpty();
    }

    [Fact]
    public void A_seek_confirmation_is_printed_only_once_the_command_has_been_given()
    {
        using var rig = new ConsoleRig();
        rig.StartPlay("play", Ramp, "--out", "1=1");
        rig.PumpUntilLine("started");

        rig.OutLines.ShouldNotContain(l => l.StartsWith("seek confirmed", StringComparison.Ordinal));
        rig.SendAndWaitFor("seek +5", "seek confirmed at ");
        rig.OutLines.Count(l => l.StartsWith("seek confirmed", StringComparison.Ordinal)).ShouldBe(1);

        rig.Send("stop");
        rig.Finish();
    }

    // ---- AC-10 -------------------------------------------------------------------------------------------------

    [Fact]
    public void A_seek_before_the_start_plays_from_the_beginning_and_says_the_target_was_outside_the_file()
    {
        using var rig = new ConsoleRig();
        rig.StartPlay("play", Ramp, "--out", "1=1", "--out", "2=2");
        rig.PumpUntilLine("started");

        var warning = rig.SendAndWaitFor("seek -9999", "warning: ");

        warning.ShouldBe("warning: target was outside the file, playing from 00:00:00.000");
        rig.Send("stop");
        rig.Finish().ShouldBe(0);
    }

    [Fact]
    public void A_seek_past_the_end_prints_the_warning_then_the_run_summary_and_the_command_returns_with_exit_0()
    {
        using var rig = new ConsoleRig();
        rig.StartPlay("play", Ramp, "--out", "1=1", "--out", "2=2");
        rig.PumpUntilLine("started");

        rig.Send("seek +9999");
        var exit = rig.Finish();

        exit.ShouldBe(0);
        var lines = rig.OutLines;
        var warning = lines.ToList().FindIndex(l => l == "warning: target was outside the file");
        var summary = lines.ToList().FindIndex(l => l.StartsWith("run summary  ended seek-past-end", StringComparison.Ordinal));
        warning.ShouldBeGreaterThanOrEqualTo(0, rig.Out.Snapshot());
        summary.ShouldBeGreaterThan(warning, rig.Out.Snapshot());
    }

    // ---- AC-13 -------------------------------------------------------------------------------------------------

    [Fact]
    public void The_stop_command_ends_the_run_with_a_summary_row_per_output_and_exit_0()
    {
        using var rig = new ConsoleRig();
        rig.StartPlay("play", Ramp, "--out", "1=1", "--out", "2=2");
        rig.PumpUntilLine("started");
        rig.PumpUntil(() => rig.Devices.All(d => d.WrittenFrames > 48_000), "one second of audio");

        rig.Send("stop");
        var exit = rig.Finish();

        exit.ShouldBe(0);
        var lines = rig.OutLines;
        lines.ShouldContain(l => l.StartsWith("run summary  ended stop  clock ", StringComparison.Ordinal), rig.Out.Snapshot());
        var rows = lines.SkipWhile(l => !l.StartsWith("run summary", StringComparison.Ordinal)).ToList();
        rows.ShouldContain(l => l.StartsWith("output  device", StringComparison.Ordinal));
        rows.ShouldContain(l => l.StartsWith("1 ", StringComparison.Ordinal) && l.Contains("dev-1", StringComparison.Ordinal));
        rows.ShouldContain(l => l.StartsWith("2 ", StringComparison.Ordinal) && l.Contains("dev-2", StringComparison.Ordinal));
        rig.Devices.ShouldAllBe(d => d.IsClosed);
    }

    [Fact]
    public void End_of_input_ends_the_run_as_an_interrupt_with_the_summary_and_exit_0()
    {
        using var rig = new ConsoleRig();
        rig.StartPlay("play", Ramp, "--out", "1=1", "--out", "2=2");
        rig.PumpUntilLine("started");

        rig.CloseInput();
        var exit = rig.Finish();

        exit.ShouldBe(0);
        rig.OutLines.ShouldContain(l => l.StartsWith("run summary  ended interrupt", StringComparison.Ordinal), rig.Out.Snapshot());
    }

    [Fact]
    public void Playing_to_the_end_of_the_file_ends_the_run_by_itself_with_the_file_end_summary_and_exit_0()
    {
        using var rig = new ConsoleRig();
        rig.StartPlay("play", TwoTracks, "--out", "1=1", "--out", "2=2");

        var exit = rig.Finish();

        exit.ShouldBe(0);
        rig.OutLines.ShouldContain(l => l.StartsWith("run summary  ended file-end", StringComparison.Ordinal), rig.Out.Snapshot());
        rig.OutLines.Count(l => l.StartsWith("run summary", StringComparison.Ordinal)).ShouldBe(1);
    }
}
