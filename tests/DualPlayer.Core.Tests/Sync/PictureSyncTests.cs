using DualPlayer.Core.Playback;
using DualPlayer.Core.Sync;
using DualPlayer.Core.Tests.Fakes;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Shouldly;

namespace DualPlayer.Core.Tests.Sync;

public sealed class PictureSyncTests
{
    private static readonly TimeSpan OneMs = TimeSpan.FromMilliseconds(1);
    private static readonly TimeSpan Step = TimeSpan.FromMilliseconds(100);

    private readonly FakeTimeProvider _time = new();
    private readonly MasterClock _clock;
    private readonly FakeVideoRenderer _renderer;

    public PictureSyncTests()
    {
        _clock = new MasterClock(_time);
        _renderer = new FakeVideoRenderer(_time);
        _renderer.Load("film.mkv", 0, startPaused: true).IsSuccess.ShouldBeTrue();
    }

    // Presented minus the due position against a fixed picture delay.
    private TimeSpan ErrorAgainst(TimeSpan pictureDelay) =>
        _renderer.Presented!.Position - (_clock.PositionAt(_time.GetTimestamp()) - pictureDelay);

    private PictureSync StartedAt(TimeSpan pictureDelay)
    {
        var sync = new PictureSync(_clock, _renderer, _time, pictureDelay);
        _clock.Start();
        sync.AlignAt(TimeSpan.Zero, _time.GetTimestamp());
        return sync;
    }

    private void Run(PictureSync sync, TimeSpan duration, Action<PictureTiming?>? afterTick = null)
    {
        for (var elapsed = TimeSpan.Zero; elapsed < duration; elapsed += Step)
        {
            _time.Advance(Step);
            afterTick?.Invoke(sync.Tick());
        }
    }

    // ---- AC-01: start ----

    [Fact]
    public void A_picture_started_with_zero_picture_delay_is_presented_on_the_master_clock()
    {
        var sync = StartedAt(TimeSpan.Zero);

        _time.Advance(TimeSpan.FromSeconds(2));
        var timing = sync.Tick().ShouldNotBeNull();

        timing.Due.ShouldBe(TimeSpan.FromSeconds(2));
        timing.Presented.ShouldBe(TimeSpan.FromSeconds(2));
        timing.ErrorMs.ShouldBe(0, 0.5);
        _renderer.Speed.ShouldBe(1.0);
    }

    [Fact]
    public void A_positive_picture_delay_holds_the_first_frame_until_the_clock_reaches_the_delay()
    {
        var pd = TimeSpan.FromMilliseconds(200);
        StartedAt(pd);

        _time.Advance(TimeSpan.FromMilliseconds(100));
        _renderer.Presented!.Position.ShouldBe(TimeSpan.Zero);

        _time.Advance(TimeSpan.FromMilliseconds(900));
        _renderer.Presented!.Position.ShouldBe(TimeSpan.FromMilliseconds(800));
        ErrorAgainst(pd).ShouldBe(TimeSpan.Zero, OneMs);
    }

    [Theory]
    [InlineData(200)]
    [InlineData(250)]
    [InlineData(1000)]
    public void Ticking_through_the_hold_of_a_positive_picture_delay_never_seeks_or_steers(int delayMs)
    {
        var pd = TimeSpan.FromMilliseconds(delayMs);
        var sync = StartedAt(pd);
        var held = TimeSpan.Zero;

        Run(sync, pd + TimeSpan.FromSeconds(2), timing =>
        {
            held += Step;
            _renderer.Speed.ShouldBe(1.0);
            if (held < pd)
            {
                // A seek would move the held frame off position zero.
                _renderer.Presented!.Position.ShouldBe(TimeSpan.Zero);
                timing.ShouldBeNull();
            }
            else if (held > pd + Step)
            {
                _renderer.Presented!.Position.ShouldBeGreaterThanOrEqualTo(TimeSpan.Zero);
                timing.ShouldNotBeNull().ErrorMs.ShouldBe(0, 1);
            }
        });

        ErrorAgainst(pd).ShouldBe(TimeSpan.Zero, OneMs);
    }

    [Fact]
    public void Resuming_at_a_content_position_below_the_picture_delay_holds_without_correcting()
    {
        var pd = TimeSpan.FromMilliseconds(500);
        var sync = StartedAt(pd);
        _time.Advance(TimeSpan.FromSeconds(3));
        _clock.JumpTo(TimeSpan.FromMilliseconds(100));
        sync.AlignAt(TimeSpan.FromMilliseconds(100), _time.GetTimestamp());

        Run(sync, TimeSpan.FromMilliseconds(300), timing =>
        {
            timing.ShouldBeNull();
            _renderer.Speed.ShouldBe(1.0);
            _renderer.Presented!.Position.ShouldBe(TimeSpan.Zero);
        });

        Run(sync, TimeSpan.FromSeconds(1));
        ErrorAgainst(pd).ShouldBe(TimeSpan.Zero, OneMs);
    }

    [Fact]
    public void A_negative_picture_delay_starts_the_picture_ahead_of_the_clock_by_the_delay()
    {
        var pd = TimeSpan.FromMilliseconds(-150);
        var sync = new PictureSync(_clock, _renderer, _time, pd);
        var clockStart = _time.GetTimestamp() + (long)(0.150 * _time.TimestampFrequency);

        // The caller issues the picture start 150 ms before the master clock starts.
        sync.AlignAt(TimeSpan.Zero, clockStart);
        _time.Advance(TimeSpan.FromMilliseconds(150));
        _clock.Start();

        _renderer.Presented!.Position.ShouldBe(TimeSpan.FromMilliseconds(150), OneMs);

        _time.Advance(TimeSpan.FromSeconds(1));

        _renderer.Presented!.Position.ShouldBe(TimeSpan.FromMilliseconds(1150), OneMs);
        ErrorAgainst(pd).ShouldBe(TimeSpan.Zero, OneMs);
        sync.Tick().ShouldNotBeNull().ErrorMs.ShouldBe(0, 1);
    }

    [Theory]
    [InlineData(-50)]
    [InlineData(-150)]
    [InlineData(-400)]
    public void Ticking_through_the_pre_roll_of_a_negative_picture_delay_never_seeks_or_steers(int delayMs)
    {
        var pd = TimeSpan.FromMilliseconds(delayMs);
        var preRoll = -pd;
        var sync = new PictureSync(_clock, _renderer, _time, pd);
        sync.AlignAt(TimeSpan.Zero, _time.GetTimestamp() + (long)(preRoll.TotalSeconds * _time.TimestampFrequency));

        for (var elapsed = TimeSpan.Zero; elapsed < preRoll; elapsed += TimeSpan.FromMilliseconds(10))
        {
            _time.Advance(TimeSpan.FromMilliseconds(10));
            sync.Tick();
            _renderer.Speed.ShouldBe(1.0);
        }

        _clock.Start();
        for (var i = 0; i < 100; i++)
        {
            _time.Advance(TimeSpan.FromMilliseconds(10));
            sync.Tick();
            _renderer.Speed.ShouldBe(1.0);
        }

        // No seek happened: the picture still runs on its original schedule, ahead of the clock by the delay.
        _renderer.Presented!.Position.ShouldBe(preRoll + TimeSpan.FromSeconds(1), OneMs);
        ErrorAgainst(pd).ShouldBe(TimeSpan.Zero, OneMs);
        sync.Tick().ShouldNotBeNull().ErrorMs.ShouldBe(0, 1);
    }

    [Fact]
    public void Tracking_resumes_after_a_negative_pre_roll_and_corrects_a_later_error()
    {
        var pd = TimeSpan.FromMilliseconds(-150);
        var sync = new PictureSync(_clock, _renderer, _time, pd);
        sync.AlignAt(TimeSpan.Zero, _time.GetTimestamp() + (long)(0.150 * _time.TimestampFrequency));
        _time.Advance(TimeSpan.FromMilliseconds(150));
        _clock.Start();
        _time.Advance(TimeSpan.FromSeconds(1));
        _renderer.Seek(_renderer.Presented!.Position + TimeSpan.FromMilliseconds(5));

        sync.Tick().ShouldNotBeNull().ErrorMs.ShouldBe(5, 1);
        _renderer.Speed.ShouldBeLessThan(1.0);
    }

    [Fact]
    public void Setting_the_delay_at_once_while_the_clock_is_stopped_seeks_without_starting_the_picture()
    {
        var sync = StartedAt(TimeSpan.Zero);
        _time.Advance(TimeSpan.FromSeconds(3));
        _clock.StopAt(_time.GetTimestamp());
        _renderer.Pause();

        sync.SetAtOnce(TimeSpan.FromMilliseconds(200));

        _renderer.IsPlaying.ShouldBeFalse();
        _renderer.Presented!.Position.ShouldBe(TimeSpan.FromMilliseconds(2800), OneMs);
        _time.Advance(TimeSpan.FromSeconds(1));
        _renderer.Presented!.Position.ShouldBe(TimeSpan.FromMilliseconds(2800), OneMs);
        sync.Tick().ShouldBeNull();
    }

    // ---- resume and seek ----

    [Fact]
    public void Resume_after_a_pause_presents_the_picture_on_clock_minus_picture_delay_again()
    {
        var pd = TimeSpan.FromMilliseconds(200);
        var sync = StartedAt(pd);
        _time.Advance(TimeSpan.FromSeconds(3));
        _clock.StopAt(_time.GetTimestamp());
        _renderer.Pause();
        _time.Advance(TimeSpan.FromSeconds(10));

        _clock.Start();
        sync.AlignAt(_clock.PositionAt(_time.GetTimestamp()), _time.GetTimestamp());
        _time.Advance(TimeSpan.FromSeconds(1));

        _renderer.Presented!.Position.ShouldBe(TimeSpan.FromMilliseconds(3800), OneMs);
        ErrorAgainst(pd).ShouldBe(TimeSpan.Zero, OneMs);
    }

    [Fact]
    public void A_seek_re_aligns_the_picture_to_the_new_clock_position_minus_the_picture_delay()
    {
        var pd = TimeSpan.FromMilliseconds(100);
        var sync = StartedAt(pd);
        _time.Advance(TimeSpan.FromSeconds(5));

        _clock.JumpTo(TimeSpan.FromSeconds(60));
        sync.AlignAt(TimeSpan.FromSeconds(60), _time.GetTimestamp());
        _time.Advance(TimeSpan.FromSeconds(1));

        _renderer.Presented!.Position.ShouldBe(TimeSpan.FromMilliseconds(60900), OneMs);
        ErrorAgainst(pd).ShouldBe(TimeSpan.Zero, OneMs);
    }

    [Fact]
    public void Aligning_resets_the_speed_to_normal()
    {
        var sync = StartedAt(TimeSpan.Zero);
        _renderer.SetSpeed(0.999);

        sync.AlignAt(TimeSpan.Zero, _time.GetTimestamp());

        _renderer.Speed.ShouldBe(1.0);
    }

    // ---- Tick: steer, seek, report ----

    [Fact]
    public void Tick_with_no_error_keeps_the_speed_at_exactly_normal()
    {
        var sync = StartedAt(TimeSpan.Zero);
        _time.Advance(TimeSpan.FromSeconds(1));

        sync.Tick().ShouldNotBeNull().ErrorMs.ShouldBe(0, 0.5);

        _renderer.Speed.ShouldBe(1.0);
    }

    [Fact]
    public void Tick_slows_the_picture_within_0_2_percent_when_it_is_a_little_ahead()
    {
        var sync = StartedAt(TimeSpan.Zero);
        _time.Advance(TimeSpan.FromSeconds(1));
        _renderer.Seek(_renderer.Presented!.Position + TimeSpan.FromMilliseconds(5));

        var timing = sync.Tick().ShouldNotBeNull();

        timing.ErrorMs.ShouldBe(5, 0.5);
        _renderer.Speed.ShouldBeLessThan(1.0);
        _renderer.Speed.ShouldBeGreaterThanOrEqualTo(1 - PictureSync.MaxSpeedDeviation);
    }

    [Fact]
    public void Tick_speeds_the_picture_up_within_0_2_percent_when_it_is_a_little_behind()
    {
        var sync = StartedAt(TimeSpan.Zero);
        _time.Advance(TimeSpan.FromSeconds(1));
        _renderer.Seek(_renderer.Presented!.Position - TimeSpan.FromMilliseconds(5));

        var timing = sync.Tick().ShouldNotBeNull();

        timing.ErrorMs.ShouldBe(-5, 0.5);
        _renderer.Speed.ShouldBeGreaterThan(1.0);
        _renderer.Speed.ShouldBeLessThanOrEqualTo(1 + PictureSync.MaxSpeedDeviation);
    }

    [Fact]
    public void A_small_error_is_steered_out_smoothly_without_a_jump_or_leaving_the_speed_band()
    {
        var sync = StartedAt(TimeSpan.Zero);
        _time.Advance(TimeSpan.FromSeconds(1));
        _renderer.Seek(_renderer.Presented!.Position + TimeSpan.FromMilliseconds(30));
        var previous = _renderer.Presented!;

        Run(sync, TimeSpan.FromSeconds(40), _ =>
        {
            var current = _renderer.Presented!;
            var advanced = _time.GetElapsedTime(previous.Timestamp, current.Timestamp);
            var moved = current.Position - previous.Position;
            _renderer.Speed.ShouldBeInRange(1 - PictureSync.MaxSpeedDeviation, 1 + PictureSync.MaxSpeedDeviation);
            moved.ShouldBe(advanced, advanced * (PictureSync.MaxSpeedDeviation * 1.01) + TimeSpan.FromTicks(10));
            previous = current;
        });

        Math.Abs(ErrorAgainst(TimeSpan.Zero).TotalMilliseconds).ShouldBeLessThan(2);
    }

    [Theory]
    [InlineData(2000)]
    [InlineData(-3000)]
    public void A_large_error_is_removed_by_a_seek_and_reported_as_measured(int errorMs)
    {
        var sync = StartedAt(TimeSpan.Zero);
        _time.Advance(TimeSpan.FromSeconds(10));
        _renderer.Seek(_renderer.Presented!.Position + TimeSpan.FromMilliseconds(errorMs));

        var timing = sync.Tick().ShouldNotBeNull();

        timing.ErrorMs.ShouldBe(errorMs, 0.5);
        Math.Abs(ErrorAgainst(TimeSpan.Zero).TotalMilliseconds).ShouldBeLessThan(20);
        _renderer.Speed.ShouldBe(1.0);
    }

    [Fact]
    public void Tick_does_nothing_and_reports_nothing_while_the_renderer_has_no_frame()
    {
        var renderer = Substitute.For<IVideoRenderer>();
        renderer.Presented.Returns((PresentedPicture?)null);
        var sync = new PictureSync(_clock, renderer, _time, TimeSpan.Zero);

        sync.Tick().ShouldBeNull();

        renderer.DidNotReceiveWithAnyArgs().SetSpeed(default);
        renderer.DidNotReceiveWithAnyArgs().Seek(default);
        renderer.DidNotReceiveWithAnyArgs().PlayAt(default, default);
    }

    // ---- AC-14: picture delay moves ----

    [Fact]
    public void A_10_ms_increase_runs_the_picture_at_0_2_percent_slower_for_5_seconds_then_back_to_normal()
    {
        var sync = StartedAt(TimeSpan.Zero);
        var target = TimeSpan.FromMilliseconds(10);

        sync.MoveTo(target);

        sync.IsMoving.ShouldBeTrue();
        sync.TargetPictureDelay.ShouldBe(target);
        _renderer.Speed.ShouldBe(1 - PictureSync.MaxSpeedDeviation, 1e-9);

        Run(sync, TimeSpan.FromSeconds(4.5), timing =>
        {
            _renderer.Speed.ShouldBeInRange(1 - PictureSync.MaxSpeedDeviation - 1e-9, 1 + PictureSync.MaxSpeedDeviation + 1e-9);
            timing.ShouldNotBeNull().ErrorMs.ShouldBe(0, 1);

            // The picture never strays from where it was against the clock by more than the move itself (10 ms).
            Math.Abs(ErrorAgainst(TimeSpan.Zero).TotalMilliseconds).ShouldBeLessThanOrEqualTo(10.5);
        });
        sync.IsMoving.ShouldBeTrue();

        Run(sync, TimeSpan.FromSeconds(0.7));

        sync.IsMoving.ShouldBeFalse();
        sync.PictureDelay.ShouldBe(target);
        _renderer.Speed.ShouldBe(1.0);
        ErrorAgainst(target).ShouldBe(TimeSpan.Zero, OneMs);
    }

    [Fact]
    public void A_decrease_runs_the_picture_at_0_2_percent_faster_until_the_new_delay_is_reached()
    {
        var sync = StartedAt(TimeSpan.FromMilliseconds(20));
        var target = TimeSpan.FromMilliseconds(10);

        sync.MoveTo(target);

        _renderer.Speed.ShouldBe(1 + PictureSync.MaxSpeedDeviation, 1e-9);

        Run(sync, TimeSpan.FromSeconds(5.2));

        sync.IsMoving.ShouldBeFalse();
        sync.PictureDelay.ShouldBe(target);
        _renderer.Speed.ShouldBe(1.0);
        ErrorAgainst(target).ShouldBe(TimeSpan.Zero, OneMs);
    }

    [Fact]
    public void A_move_to_the_delay_already_in_effect_changes_nothing()
    {
        var pd = TimeSpan.FromMilliseconds(40);
        var sync = StartedAt(pd);

        sync.MoveTo(pd);

        sync.IsMoving.ShouldBeFalse();
        _renderer.Speed.ShouldBe(1.0);
    }

    [Fact]
    public void A_new_target_during_a_move_retargets_from_the_delay_reached_so_far()
    {
        var sync = StartedAt(TimeSpan.Zero);
        sync.MoveTo(TimeSpan.FromMilliseconds(10));
        Run(sync, TimeSpan.FromSeconds(2));
        sync.PictureDelay.ShouldBe(TimeSpan.FromMilliseconds(4), OneMs / 2);

        // From +4 ms to -10 ms is 14 ms: the direction reverses and the move takes 7 s.
        var target = TimeSpan.FromMilliseconds(-10);
        sync.MoveTo(target);

        sync.TargetPictureDelay.ShouldBe(target);
        _renderer.Speed.ShouldBe(1 + PictureSync.MaxSpeedDeviation, 1e-9);

        Run(sync, TimeSpan.FromSeconds(6.5));
        sync.IsMoving.ShouldBeTrue();

        Run(sync, TimeSpan.FromSeconds(0.7));

        sync.IsMoving.ShouldBeFalse();
        sync.PictureDelay.ShouldBe(target);
        _renderer.Speed.ShouldBe(1.0);
        ErrorAgainst(target).ShouldBe(TimeSpan.Zero, OneMs);
    }

    [Fact]
    public void Setting_the_delay_at_once_re_aligns_the_picture_without_a_ramp()
    {
        var sync = StartedAt(TimeSpan.Zero);
        _time.Advance(TimeSpan.FromSeconds(3));
        var target = TimeSpan.FromMilliseconds(300);

        sync.SetAtOnce(target);

        sync.IsMoving.ShouldBeFalse();
        sync.PictureDelay.ShouldBe(target);
        _renderer.Speed.ShouldBe(1.0);
        ErrorAgainst(target).ShouldBe(TimeSpan.Zero, OneMs);

        _time.Advance(TimeSpan.FromSeconds(1));

        ErrorAgainst(target).ShouldBe(TimeSpan.Zero, OneMs);
    }

    [Fact]
    public void Setting_the_delay_at_once_during_a_move_cancels_the_ramp()
    {
        var sync = StartedAt(TimeSpan.Zero);
        sync.MoveTo(TimeSpan.FromMilliseconds(10));
        Run(sync, TimeSpan.FromSeconds(2));
        var target = TimeSpan.FromMilliseconds(-50);

        sync.SetAtOnce(target);

        sync.IsMoving.ShouldBeFalse();
        sync.PictureDelay.ShouldBe(target);
        _renderer.Speed.ShouldBe(1.0);
        ErrorAgainst(target).ShouldBe(TimeSpan.Zero, OneMs);
    }

    [Fact]
    public void A_seek_during_a_move_sets_the_target_delay_at_once()
    {
        var sync = StartedAt(TimeSpan.Zero);
        var target = TimeSpan.FromMilliseconds(10);
        sync.MoveTo(target);
        Run(sync, TimeSpan.FromSeconds(2));

        _clock.JumpTo(TimeSpan.FromSeconds(30));
        sync.AlignAt(TimeSpan.FromSeconds(30), _time.GetTimestamp());

        sync.IsMoving.ShouldBeFalse();
        sync.PictureDelay.ShouldBe(target);
        _renderer.Speed.ShouldBe(1.0);

        _time.Advance(TimeSpan.FromSeconds(1));

        ErrorAgainst(target).ShouldBe(TimeSpan.Zero, OneMs);
    }

    // ---- AC-17: the timing record ----

    [Fact]
    public void The_timing_record_carries_due_presented_and_a_signed_error_for_the_log()
    {
        var sync = StartedAt(TimeSpan.FromMilliseconds(200));
        _time.Advance(TimeSpan.FromSeconds(2));
        _renderer.Seek(_renderer.Presented!.Position + TimeSpan.FromMilliseconds(12));

        var timing = sync.Tick().ShouldNotBeNull();

        timing.Due.ShouldBe(TimeSpan.FromMilliseconds(1800), OneMs / 2);
        timing.Presented.ShouldBe(TimeSpan.FromMilliseconds(1812), OneMs / 2);
        timing.ErrorMs.ShouldBe(12, 0.5);
    }
}
