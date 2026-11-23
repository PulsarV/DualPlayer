using DualPlayer.Core.Playback;
using Microsoft.Extensions.Time.Testing;
using Shouldly;

namespace DualPlayer.Core.Tests.Fakes;

public class FakeVideoRendererTests
{
    private static (FakeTimeProvider Time, FakeVideoRenderer Renderer) Loaded(bool startPaused = true)
    {
        var time = new FakeTimeProvider();
        var renderer = new FakeVideoRenderer(time);
        renderer.Load("film.mkv", 0, startPaused).IsSuccess.ShouldBeTrue();
        return (time, renderer);
    }

    [Fact]
    public void A_paused_load_holds_the_first_frame_and_publishes_position_zero_at_the_load_timestamp()
    {
        var (time, renderer) = Loaded();
        time.Advance(TimeSpan.FromSeconds(1));

        var presented = renderer.Presented.ShouldNotBeNull();
        presented.Position.ShouldBe(TimeSpan.Zero);
        presented.Timestamp.ShouldBe(time.GetTimestamp());
    }

    [Fact]
    public void A_seek_before_a_scheduled_play_at_start_moves_the_position_but_keeps_the_future_start()
    {
        var (time, renderer) = Loaded();
        renderer.PlayAt(TimeSpan.FromSeconds(5), time.GetTimestamp() + time.TimestampFrequency);

        renderer.Seek(TimeSpan.FromSeconds(20));
        time.Advance(TimeSpan.FromMilliseconds(500));

        renderer.Presented!.Position.ShouldBe(TimeSpan.FromSeconds(20));

        time.Advance(TimeSpan.FromSeconds(1));

        renderer.Presented!.Position.ShouldBe(TimeSpan.FromSeconds(20.5));
    }

    [Fact]
    public void Presented_position_follows_the_clock_after_play_at()
    {
        var (time, renderer) = Loaded();

        renderer.PlayAt(TimeSpan.FromSeconds(5), time.GetTimestamp());
        time.Advance(TimeSpan.FromSeconds(2));

        var presented = renderer.Presented.ShouldNotBeNull();
        presented.Position.ShouldBe(TimeSpan.FromSeconds(7));
        presented.Timestamp.ShouldBe(time.GetTimestamp());
    }

    [Fact]
    public void Presented_position_does_not_move_before_the_scheduled_start_timestamp()
    {
        var (time, renderer) = Loaded();

        renderer.PlayAt(TimeSpan.FromSeconds(5), time.GetTimestamp() + time.TimestampFrequency);
        time.Advance(TimeSpan.FromMilliseconds(500));

        renderer.Presented!.Position.ShouldBe(TimeSpan.FromSeconds(5));

        time.Advance(TimeSpan.FromSeconds(1));

        renderer.Presented!.Position.ShouldBe(TimeSpan.FromSeconds(5.5));
    }

    [Fact]
    public void In_held_start_mode_a_frame_held_for_a_scheduled_start_is_stamped_with_that_start()
    {
        var time = new FakeTimeProvider();
        var renderer = new FakeVideoRenderer(time) { StampsHeldFrameWithItsStart = true };
        renderer.Load("film.mkv", 0, startPaused: true);
        var start = time.GetTimestamp() + time.TimestampFrequency;

        renderer.PlayAt(TimeSpan.FromSeconds(5), start);

        renderer.Presented.ShouldBe(new PresentedPicture(TimeSpan.FromSeconds(5), start));
        time.Advance(TimeSpan.FromSeconds(2));
        renderer.Presented.ShouldBe(new PresentedPicture(TimeSpan.FromSeconds(6), time.GetTimestamp()));
    }

    [Fact]
    public void Presented_position_advances_at_clock_times_speed()
    {
        var (time, renderer) = Loaded();
        renderer.PlayAt(TimeSpan.Zero, time.GetTimestamp());

        time.Advance(TimeSpan.FromSeconds(1));
        renderer.SetSpeed(1.002);
        time.Advance(TimeSpan.FromSeconds(10));

        renderer.Speed.ShouldBe(1.002);
        renderer.Presented!.Position.ShouldBe(TimeSpan.FromSeconds(1 + 10.02), TimeSpan.FromMilliseconds(0.1));
    }

    [Fact]
    public void Pause_holds_the_presented_position()
    {
        var (time, renderer) = Loaded();
        renderer.PlayAt(TimeSpan.Zero, time.GetTimestamp());
        time.Advance(TimeSpan.FromSeconds(3));

        renderer.Pause();
        time.Advance(TimeSpan.FromSeconds(10));

        renderer.IsPlaying.ShouldBeFalse();
        renderer.Presented!.Position.ShouldBe(TimeSpan.FromSeconds(3));
        renderer.Presented!.Timestamp.ShouldBe(time.GetTimestamp());
    }

    [Fact]
    public void Seek_jumps_the_position_and_keeps_playing()
    {
        var (time, renderer) = Loaded();
        renderer.PlayAt(TimeSpan.Zero, time.GetTimestamp());
        time.Advance(TimeSpan.FromSeconds(3));

        renderer.Seek(TimeSpan.FromSeconds(60));
        time.Advance(TimeSpan.FromSeconds(1));

        renderer.Presented!.Position.ShouldBe(TimeSpan.FromSeconds(61));
    }

    [Fact]
    public void Seek_while_paused_jumps_and_stays_paused()
    {
        var (time, renderer) = Loaded();
        renderer.PlayAt(TimeSpan.Zero, time.GetTimestamp());
        time.Advance(TimeSpan.FromSeconds(3));
        renderer.Pause();

        renderer.Seek(TimeSpan.FromSeconds(60));
        time.Advance(TimeSpan.FromSeconds(5));

        renderer.Presented!.Position.ShouldBe(TimeSpan.FromSeconds(60));
    }

    [Fact]
    public void Load_failure_is_returned_as_a_result_and_never_thrown()
    {
        var renderer = new FakeVideoRenderer(new FakeTimeProvider())
        {
            LoadError = new Error("mpv.not_found", "libmpv is missing."),
        };

        var result = renderer.Load("film.mkv", 0, startPaused: false);

        result.IsSuccess.ShouldBeFalse();
        result.Error!.Code.ShouldBe("mpv.not_found");
        renderer.Presented.ShouldBeNull();
    }

    [Fact]
    public void Each_injected_event_is_raised_once()
    {
        var (_, renderer) = Loaded();
        var failures = new List<Error>();
        var inactive = 0;
        var ended = 0;
        renderer.DecodeFailed += failures.Add;
        renderer.HardwareDecodingInactive += () => inactive++;
        renderer.EndOfFile += () => ended++;

        renderer.InjectDecodeFailure(new Error("video.decode_failed", "Bad frame."));
        renderer.InjectHardwareDecodingInactive();
        renderer.InjectEndOfFile();

        failures.Count.ShouldBe(1);
        failures[0].Code.ShouldBe("video.decode_failed");
        inactive.ShouldBe(1);
        ended.ShouldBe(1);
    }

    [Fact]
    public void A_re_anchored_picture_read_in_the_same_instant_shows_the_grid_frame_not_a_frame_of_its_own_AC_17()
    {
        var (time, renderer) = Loaded(startPaused: false);
        renderer.FrameDuration = TimeSpan.FromMilliseconds(40);
        time.Advance(TimeSpan.FromMilliseconds(100));
        renderer.Seek(TimeSpan.FromMilliseconds(55)); // re-anchors off the 40 ms grid, at this very instant

        var first = renderer.Presented.ShouldNotBeNull();
        var second = renderer.Presented.ShouldNotBeNull();
        renderer.SnapshotAge = TimeSpan.FromMilliseconds(10);
        var aged = renderer.Presented.ShouldNotBeNull();

        first.Position.ShouldBe(TimeSpan.FromMilliseconds(40), "a read at the anchor's own instant named a position off the frame grid");
        second.ShouldBe(first);
        aged.Position.ShouldBe(TimeSpan.FromMilliseconds(40), "a read older than the anchor named a position off the frame grid");
    }

    [Fact]
    public void Injected_late_and_dropped_frames_accumulate_in_the_counters()
    {
        var (_, renderer) = Loaded();

        renderer.Counters.ShouldBe(FrameCounters.None);

        renderer.InjectFrames(late: 2, dropped: 1);
        renderer.InjectFrames(late: 1, skippedDamaged: 4);

        renderer.Counters.ShouldBe(new FrameCounters(Late: 3, Dropped: 1, SkippedDamaged: 4));
    }

    [Fact]
    public void Shown_notes_are_recorded_with_their_style()
    {
        var (_, renderer) = Loaded();

        renderer.ShowNote("Picture is late", NoteStyle.Warning);

        renderer.Notes.ShouldBe([("Picture is late", NoteStyle.Warning)]);
    }
}
