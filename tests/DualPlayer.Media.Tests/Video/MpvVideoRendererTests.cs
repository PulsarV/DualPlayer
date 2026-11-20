using System.Diagnostics;
using System.Runtime.InteropServices;
using DualPlayer.Core;
using DualPlayer.Core.Playback;
using DualPlayer.Media.Video;
using Microsoft.Extensions.DependencyInjection;

namespace DualPlayer.Media.Tests.Video;

public sealed class MpvVideoRendererTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(8);

    // Tests run without a window or GPU: no video output, software decoding. The hardening list itself is
    // asserted separately; these overrides only make the renderer drivable on a build machine.
    private static readonly IReadOnlyDictionary<string, string> Headless = new Dictionary<string, string>
    {
        ["vo"] = "null",
        ["hwdec"] = "no",
    };

    private static string Media(string name) => Path.Combine(AppContext.BaseDirectory, "TestMedia", name);

    private static bool IsLibmpvInstalled() =>
        OperatingSystem.IsMacOS()
            ? File.Exists("/opt/homebrew/lib/libmpv.dylib") || File.Exists("/usr/local/lib/libmpv.dylib")
            : File.Exists(Path.Combine(AppContext.BaseDirectory, "libmpv-2.dll"));

    private static void RequireLibmpv() =>
        Assert.SkipUnless(IsLibmpvInstalled(), "libmpv is not installed (brew install mpv / build/fetch-libmpv.ps1).");

    private static MpvVideoRenderer NewRenderer(IReadOnlyDictionary<string, string>? overrides = null) =>
        new(TimeProvider.System, overrides ?? Headless);

    private static MpvVideoRenderer LoadedPaused(string sample = "long-gop-ramp.mkv")
    {
        var renderer = NewRenderer();
        var result = renderer.Load(Media(sample), 0, startPaused: true);
        result.IsSuccess.ShouldBeTrue(result.Error?.Message);
        return renderer;
    }

    private static T? WaitFor<T>(Func<T?> probe, Func<T, bool> accept)
        where T : class
    {
        var deadline = Stopwatch.GetTimestamp() + (long)(Patience.TotalSeconds * Stopwatch.Frequency);
        while (Stopwatch.GetTimestamp() < deadline)
        {
            var value = probe();
            if (value is not null && accept(value))
            {
                return value;
            }

            Thread.Sleep(10);
        }

        return probe();
    }

    // ---- hardening option list (pure; runs everywhere) ----

    [Fact]
    public void Hardening_options_on_Windows_are_exactly_the_sad_list_with_hardware_decoding_d3d11va()
    {
        var options = MpvVideoRenderer.HardeningOptions(OSPlatform.Windows);

        options.Select(o => $"{o.Key}={o.Value}").Order().ShouldBe(
            [
                "audio=no",
                "config=no",
                "hwdec=d3d11va",
                "input-default-bindings=no",
                "input-vo-keyboard=no",
                "load-scripts=no",
                "osc=no",
                "ytdl=no",
            ]);
    }

    [Fact]
    public void Hardening_options_on_the_dev_Mac_differ_only_in_the_hardware_decoder_and_never_allow_copy_back_or_software()
    {
        var windows = MpvVideoRenderer.HardeningOptions(OSPlatform.Windows).Where(o => o.Key != "hwdec").ToList();
        var mac = MpvVideoRenderer.HardeningOptions(OSPlatform.OSX);

        mac.Where(o => o.Key != "hwdec").ShouldBe(windows);
        var hwdec = mac.Single(o => o.Key == "hwdec").Value;
        hwdec.ShouldBe("videotoolbox");
        hwdec.ShouldNotContain("copy");
        hwdec.ShouldNotBe("no");
        hwdec.ShouldNotBe("auto");
    }

    [Fact]
    public void Hardening_options_name_each_option_once()
    {
        var keys = MpvVideoRenderer.HardeningOptions(OSPlatform.Windows).Select(o => o.Key).ToList();

        keys.Distinct().Count().ShouldBe(keys.Count);
    }

    [Fact]
    public void A_note_is_held_for_two_seconds()
    {
        MpvVideoRenderer.NoteHold.ShouldBe(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void AddMedia_registers_the_libmpv_renderer_as_the_video_renderer_as_a_singleton()
    {
        var services = new ServiceCollection().AddMedia();

        var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IVideoRenderer)).ShouldNotBeNull();
        descriptor.ImplementationType.ShouldBe(typeof(MpvVideoRenderer));
        descriptor.Lifetime.ShouldBe(ServiceLifetime.Singleton);
    }

    // ---- Load failures are Results, never exceptions (library-present) ----

    [Fact]
    public void Load_of_a_missing_file_returns_mpv_load_failed_naming_the_file()
    {
        RequireLibmpv();
        using var renderer = NewRenderer();
        var missing = Path.Combine(Path.GetTempPath(), "DualPlayer.Tests", Guid.NewGuid().ToString("N"), "film.mkv");

        var result = renderer.Load(missing, 0, startPaused: true);

        result.IsSuccess.ShouldBeFalse();
        result.Error.ShouldNotBeNull();
        result.Error.Code.ShouldBe("mpv.load_failed");
        result.Error.Message.ShouldContain("film.mkv");
        renderer.Presented.ShouldBeNull();
    }

    [Fact]
    public void Load_of_a_file_that_is_not_media_returns_mpv_load_failed()
    {
        RequireLibmpv();
        using var renderer = NewRenderer();

        var result = renderer.Load(Media("not-media.txt"), 0, startPaused: true);

        result.IsSuccess.ShouldBeFalse();
        result.Error.ShouldNotBeNull();
        result.Error.Code.ShouldBe("mpv.load_failed");
    }

    // ---- presented position (AC-01, AC-17) ----

    [Fact]
    public void A_paused_load_holds_the_first_frame_and_publishes_position_zero()
    {
        RequireLibmpv();
        using var renderer = LoadedPaused();

        var presented = WaitFor(() => renderer.Presented, _ => true).ShouldNotBeNull();

        presented.Position.ShouldBe(TimeSpan.Zero, TimeSpan.FromMilliseconds(100));
    }

    [Fact]
    public void Play_at_advances_the_presented_position_from_the_requested_start()
    {
        RequireLibmpv();
        using var renderer = LoadedPaused();

        renderer.PlayAt(TimeSpan.FromSeconds(2), TimeProvider.System.GetTimestamp());

        var first = WaitFor(() => renderer.Presented, p => p.Position >= TimeSpan.FromSeconds(1.9)).ShouldNotBeNull();
        first.Position.ShouldBeGreaterThanOrEqualTo(TimeSpan.FromSeconds(1.9));
        first.Position.ShouldBeLessThan(TimeSpan.FromSeconds(4));
        var later = WaitFor(() => renderer.Presented, p => p.Position >= first.Position + TimeSpan.FromSeconds(0.5)).ShouldNotBeNull();
        later.Position.ShouldBeGreaterThanOrEqualTo(first.Position + TimeSpan.FromSeconds(0.5));
    }

    [Fact]
    public void A_presented_position_is_never_published_before_the_moment_its_frame_is_due()
    {
        // mpv hands a frame to its video output ahead of the frame's display time (render-ahead) and reports it as
        // time-pos at that moment. The presented picture must be the one due, not the one queued: while playing from a
        // scheduled start, no snapshot may claim a position later than the time elapsed since that start.
        RequireLibmpv();
        using var renderer = LoadedPaused();
        WaitFor(() => renderer.Presented, _ => true).ShouldNotBeNull();
        var time = TimeProvider.System;
        var start = time.GetTimestamp() + (long)(0.3 * time.TimestampFrequency);

        renderer.PlayAt(TimeSpan.Zero, start);

        WaitFor(() => renderer.Presented, p => p.Position >= TimeSpan.FromSeconds(1)).ShouldNotBeNull();
        var ahead = TimeSpan.MinValue;
        var until = time.GetTimestamp() + time.TimestampFrequency;
        while (time.GetTimestamp() < until)
        {
            var presented = renderer.Presented.ShouldNotBeNull();
            var lead = presented.Position - time.GetElapsedTime(start, presented.Timestamp);
            ahead = lead > ahead ? lead : ahead;
            Thread.Sleep(2);
        }

        // Scheduling and event latency only ever make a snapshot later; a render-ahead lead is a frame or more (40 ms here).
        ahead.ShouldBeLessThan(TimeSpan.FromMilliseconds(10));
    }

    [Fact]
    public void Presented_timestamps_are_on_the_time_provider_monotonic_time_base()
    {
        RequireLibmpv();
        using var renderer = LoadedPaused();
        renderer.PlayAt(TimeSpan.Zero, TimeProvider.System.GetTimestamp());
        var started = WaitFor(() => renderer.Presented, p => p.Position > TimeSpan.FromSeconds(0.3)).ShouldNotBeNull();
        WaitFor(() => renderer.Presented, p => p.Position > started.Position + TimeSpan.FromSeconds(0.3)).ShouldNotBeNull();

        var presented = renderer.Presented.ShouldNotBeNull();
        var now = TimeProvider.System.GetTimestamp();

        // While playing, a picture is published once it is up: never stamped ahead of now, and recent.
        presented.Timestamp.ShouldBeLessThanOrEqualTo(now);
        TimeProvider.System.GetElapsedTime(presented.Timestamp, now).ShouldBeLessThan(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void A_frame_held_for_a_scheduled_start_is_stamped_with_the_moment_the_start_reaches_it()
    {
        RequireLibmpv();
        using var renderer = LoadedPaused();
        WaitFor(() => renderer.Presented, _ => true).ShouldNotBeNull();
        var time = TimeProvider.System;
        var start = time.GetTimestamp() + (long)(1.5 * time.TimestampFrequency);

        renderer.PlayAt(TimeSpan.FromSeconds(2.01), start);

        // 2.01 s is between frames (25 fps here): the seek target stands in as 2.01 s, while the exact seek lands on the
        // restart frame 2.04 s. Held, that frame is due when the start reaches it, 30 ms after the start.
        var held = WaitFor(() => renderer.Presented, p => p.Position >= TimeSpan.FromSeconds(2.03)).ShouldNotBeNull();
        held.Position.ShouldBe(TimeSpan.FromSeconds(2.04), TimeSpan.FromMilliseconds(1));
        held.Timestamp.ShouldBeGreaterThan(time.GetTimestamp());
        time.GetElapsedTime(start, held.Timestamp).ShouldBe(TimeSpan.FromMilliseconds(30), TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public void A_seek_while_playing_presents_the_target_as_due_at_the_moment_of_the_seek_and_never_ahead_of_it()
    {
        // A seek while playing is a start "now" from the target: the renderer pauses, seeks ahead by its seek time and
        // resumes on time, so the frame it anchors on is the paused restart frame, not a frame mpv queued after it.
        RequireLibmpv();
        using var renderer = LoadedPaused();
        var time = TimeProvider.System;
        renderer.PlayAt(TimeSpan.Zero, time.GetTimestamp());
        WaitFor(() => renderer.Presented, p => p.Position >= TimeSpan.FromSeconds(0.5)).ShouldNotBeNull();

        var target = TimeSpan.FromSeconds(5);
        var seekAt = time.GetTimestamp();
        renderer.Seek(target);

        WaitFor(() => renderer.Presented, p => p.Position >= target + TimeSpan.FromSeconds(1.2)).ShouldNotBeNull();
        var ahead = TimeSpan.MinValue;
        var behind = TimeSpan.MaxValue;
        var until = time.GetTimestamp() + time.TimestampFrequency;
        while (time.GetTimestamp() < until)
        {
            var presented = renderer.Presented.ShouldNotBeNull();
            var lead = presented.Position - target - time.GetElapsedTime(seekAt, presented.Timestamp);
            ahead = lead > ahead ? lead : ahead;
            behind = lead < behind ? lead : behind;
            Thread.Sleep(2);
        }

        ahead.ShouldBeLessThan(TimeSpan.FromMilliseconds(10));
        behind.ShouldBeGreaterThan(TimeSpan.FromMilliseconds(-10), "the picture should resume on the seek's schedule, not a seek time late");
    }

    [Fact]
    public void A_playing_load_publishes_position_zero_first_and_plays_on_from_it()
    {
        RequireLibmpv();
        using var renderer = NewRenderer();
        var time = TimeProvider.System;

        renderer.Load(Media("long-gop-ramp.mkv"), 0, startPaused: false).IsSuccess.ShouldBeTrue();

        PresentedPicture? first = null;
        var until = time.GetTimestamp() + (Patience.Ticks * time.TimestampFrequency / TimeSpan.TicksPerSecond);
        while (first is null && time.GetTimestamp() < until)
        {
            first = renderer.Presented;
        }

        first.ShouldNotBeNull().Position.ShouldBe(TimeSpan.Zero, TimeSpan.FromMilliseconds(1));
        WaitFor(() => renderer.Presented, p => p.Position >= TimeSpan.FromSeconds(0.5)).ShouldNotBeNull();
    }

    [Fact]
    public void Successive_snapshots_never_go_backwards_in_position_or_timestamp_while_playing()
    {
        RequireLibmpv();
        using var renderer = LoadedPaused();
        renderer.PlayAt(TimeSpan.Zero, TimeProvider.System.GetTimestamp());
        WaitFor(() => renderer.Presented, p => p.Position > TimeSpan.FromSeconds(0.2)).ShouldNotBeNull();

        PresentedPicture? previous = null;
        var until = Stopwatch.GetTimestamp() + (Stopwatch.Frequency / 2);
        while (Stopwatch.GetTimestamp() < until)
        {
            var current = renderer.Presented.ShouldNotBeNull();
            if (previous is not null)
            {
                current.Position.ShouldBeGreaterThanOrEqualTo(previous.Position);
                current.Timestamp.ShouldBeGreaterThanOrEqualTo(previous.Timestamp);
            }

            previous = current;
        }
    }

    [Fact]
    public void Pause_freezes_the_presented_position()
    {
        RequireLibmpv();
        using var renderer = LoadedPaused();
        renderer.PlayAt(TimeSpan.Zero, TimeProvider.System.GetTimestamp());
        WaitFor(() => renderer.Presented, p => p.Position > TimeSpan.FromSeconds(0.3)).ShouldNotBeNull();

        renderer.Pause();
        Thread.Sleep(300);
        var frozen = renderer.Presented.ShouldNotBeNull().Position;
        Thread.Sleep(400);

        renderer.Presented.ShouldNotBeNull().Position.ShouldBe(frozen, TimeSpan.FromMilliseconds(50));
    }

    [Fact]
    public void Seek_while_paused_shows_the_exact_target_and_stays_paused()
    {
        RequireLibmpv();
        using var renderer = LoadedPaused();
        WaitFor(() => renderer.Presented, _ => true).ShouldNotBeNull();

        renderer.Seek(TimeSpan.FromSeconds(7));

        var shown = WaitFor(() => renderer.Presented, p => Math.Abs((p.Position - TimeSpan.FromSeconds(7)).TotalMilliseconds) < 50);
        shown.ShouldNotBeNull();
        shown.Position.ShouldBe(TimeSpan.FromSeconds(7), TimeSpan.FromMilliseconds(50));
        Thread.Sleep(500);
        renderer.Presented.ShouldNotBeNull().Position.ShouldBe(TimeSpan.FromSeconds(7), TimeSpan.FromMilliseconds(50));
    }

    [Fact]
    public void Set_speed_two_moves_the_position_about_twice_as_fast_as_real_time()
    {
        RequireLibmpv();
        using var renderer = LoadedPaused();
        renderer.SetSpeed(2.0);
        renderer.PlayAt(TimeSpan.Zero, TimeProvider.System.GetTimestamp());
        var start = WaitFor(() => renderer.Presented, p => p.Position > TimeSpan.FromSeconds(0.3)).ShouldNotBeNull();

        Thread.Sleep(1000);
        var end = renderer.Presented.ShouldNotBeNull();

        var contentSeconds = (end.Position - start.Position).TotalSeconds;
        var realSeconds = TimeProvider.System.GetElapsedTime(start.Timestamp, end.Timestamp).TotalSeconds;
        (contentSeconds / realSeconds).ShouldBe(2.0, 0.4);
    }

    // ---- events and counters (AC-06b) ----

    [Fact]
    public void Playing_to_the_end_of_the_file_raises_end_of_file()
    {
        RequireLibmpv();
        using var renderer = LoadedPaused();
        using var ended = new ManualResetEventSlim();
        renderer.EndOfFile += ended.Set;

        renderer.PlayAt(TimeSpan.FromSeconds(11), TimeProvider.System.GetTimestamp());

        ended.Wait(Patience, TestContext.Current.CancellationToken).ShouldBeTrue("EndOfFile was not raised");
    }

    [Fact]
    public void Software_decoding_after_load_raises_hardware_decoding_inactive_instead_of_falling_back_silently()
    {
        RequireLibmpv();
        using var renderer = NewRenderer(new Dictionary<string, string> { ["vo"] = "null", ["hwdec"] = "no" });
        using var inactive = new ManualResetEventSlim();
        renderer.HardwareDecodingInactive += inactive.Set;
        renderer.Load(Media("long-gop-ramp.mkv"), 0, startPaused: true).IsSuccess.ShouldBeTrue();

        renderer.PlayAt(TimeSpan.Zero, TimeProvider.System.GetTimestamp());

        inactive.Wait(Patience, TestContext.Current.CancellationToken).ShouldBeTrue("HardwareDecodingInactive was not raised");
    }

    [Fact]
    public void A_damaged_stretch_of_the_picture_is_counted_as_skipped_while_playback_continues_to_the_end()
    {
        RequireLibmpv();
        using var renderer = LoadedPaused("video-damaged.mkv");
        using var ended = new ManualResetEventSlim();
        renderer.EndOfFile += ended.Set;

        renderer.PlayAt(TimeSpan.Zero, TimeProvider.System.GetTimestamp());

        ended.Wait(Patience, TestContext.Current.CancellationToken).ShouldBeTrue("playback did not continue to the end of the damaged file");
        renderer.Counters.SkippedDamaged.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void Counters_are_zero_for_an_undamaged_file_played_to_the_end()
    {
        RequireLibmpv();
        using var renderer = LoadedPaused();
        using var ended = new ManualResetEventSlim();
        renderer.EndOfFile += ended.Set;
        renderer.PlayAt(TimeSpan.FromSeconds(11), TimeProvider.System.GetTimestamp());
        ended.Wait(Patience, TestContext.Current.CancellationToken).ShouldBeTrue();

        renderer.Counters.SkippedDamaged.ShouldBe(0);
    }

    // ---- a second Load starts from a clean slate ----

    [Fact]
    public void A_second_load_shows_the_new_file_at_position_zero_with_zeroed_counters_never_the_old_files_state()
    {
        RequireLibmpv();
        using var renderer = LoadedPaused("video-damaged.mkv");
        renderer.PlayAt(TimeSpan.Zero, TimeProvider.System.GetTimestamp());
        WaitFor(() => renderer.Presented, p => p.Position >= TimeSpan.FromSeconds(1.5)).ShouldNotBeNull();
        WaitFor(() => renderer.Counters, c => c.SkippedDamaged > 0).ShouldNotBeNull().SkippedDamaged.ShouldBeGreaterThan(0);

        renderer.Load(Media("long-gop-ramp.mkv"), 0, startPaused: true).IsSuccess.ShouldBeTrue();

        var until = Stopwatch.GetTimestamp() + Stopwatch.Frequency;
        PresentedPicture? last = null;
        while (Stopwatch.GetTimestamp() < until)
        {
            last = renderer.Presented;
            last?.Position.ShouldBeLessThan(TimeSpan.FromSeconds(0.5), "the old file's position leaked into the new load");
            renderer.Counters.ShouldBe(FrameCounters.None);
            Thread.Sleep(5);
        }

        last.ShouldNotBeNull().Position.ShouldBe(TimeSpan.Zero, TimeSpan.FromMilliseconds(100));
    }

    [Fact]
    public void A_later_load_into_a_different_native_window_is_refused_because_a_renderer_owns_one_window()
    {
        RequireLibmpv();
        using var renderer = NewRenderer();
        renderer.Load(Media("long-gop-ramp.mkv"), 0, startPaused: true).IsSuccess.ShouldBeTrue();

        var result = renderer.Load(Media("long-gop-ramp.mkv"), 12345, startPaused: true);

        result.IsSuccess.ShouldBeFalse();
        result.Error.ShouldNotBeNull().Code.ShouldBe("mpv.load_failed");
    }

    [Fact]
    public void A_load_after_dispose_fails_instead_of_succeeding()
    {
        RequireLibmpv();
        var renderer = LoadedPaused();
        renderer.Dispose();

        var result = renderer.Load(Media("long-gop-ramp.mkv"), 0, startPaused: true);

        result.IsSuccess.ShouldBeFalse();
        result.Error.ShouldNotBeNull().Code.ShouldBe("mpv.load_failed");
    }

    // ---- notes and lifetime ----

    [Fact]
    public void Show_note_on_a_loaded_renderer_does_not_throw_for_either_style()
    {
        RequireLibmpv();
        using var renderer = LoadedPaused();

        Should.NotThrow(() => renderer.ShowNote("Picture delay 120 ms", NoteStyle.Info));
        Should.NotThrow(() => renderer.ShowNote("Headphones disconnected", NoteStyle.Warning));
    }

    [Fact]
    public void Dispose_terminates_the_renderer_cleanly_stops_events_and_is_idempotent()
    {
        RequireLibmpv();
        var renderer = LoadedPaused();
        var raised = 0;
        renderer.EndOfFile += () => Interlocked.Increment(ref raised);
        renderer.PlayAt(TimeSpan.Zero, TimeProvider.System.GetTimestamp());
        WaitFor(() => renderer.Presented, p => p.Position > TimeSpan.FromSeconds(0.2)).ShouldNotBeNull();

        var stopwatch = Stopwatch.StartNew();
        renderer.Dispose();
        renderer.Dispose();

        stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(5));
        Should.NotThrow(() => _ = renderer.Presented);
        Should.NotThrow(() => _ = renderer.Counters);
        Thread.Sleep(300);
        raised.ShouldBe(0);
    }
}
