using DualPlayer.Core.Diagnostics;
using DualPlayer.Core.Playback;
using DualPlayer.Core.Sync;
using DualPlayer.Core.Tests.Fakes;

namespace DualPlayer.Core.Tests.Playback;

public sealed class PlaybackSessionTransportTests
{
    private static readonly RigDevice[] TwoDevices = [new RigDevice(48_000), new RigDevice(48_000)];

    private static SessionRig PlayingRig(double totalSeconds = 600)
    {
        var rig = new SessionRig(TwoDevices, null, totalSeconds);
        rig.Probe.Set("dev-1", new DeviceLatencyReport(TimeSpan.FromMilliseconds(40), "test", ConnectionKind.Bluetooth, FormFactor.Headphones));
        rig.Probe.Set("dev-2", new DeviceLatencyReport(TimeSpan.Zero, "test", ConnectionKind.Usb, FormFactor.Speaker));
        rig.Engine.Call(() => rig.Session.Start(rig.Request(2))).IsSuccess.ShouldBeTrue();
        rig.Engine.Render(300);
        return rig;
    }

    private static TimeSpan Shown(SessionRig rig) => rig.Renderer.Presented!.Position;

    [Fact]
    public void Pause_freezes_the_picture_within_the_picture_delay_after_the_key_and_stops_every_output_AC_04()
    {
        using var rig = PlayingRig();
        var atKey = Shown(rig);

        rig.Engine.Call(() => rig.Session.Pause()).IsSuccess.ShouldBeTrue();
        rig.Engine.Render(100);

        rig.Session.State.ShouldBe(PlaybackState.Paused);
        rig.Renderer.IsPlaying.ShouldBeFalse();
        var frozen = Shown(rig);
        (frozen - atKey).ShouldBeGreaterThanOrEqualTo(TimeSpan.Zero);
        (frozen - atKey).TotalMilliseconds.ShouldBeLessThanOrEqualTo(240 + 20);
        rig.Engine.Render(100);
        Shown(rig).ShouldBe(frozen);
        rig.Events.OfType<PictureDelayChanged>().ShouldContain(e => e.Cause == PictureDelayCause.Pause);
    }

    [Fact]
    public void Resume_continues_picture_from_the_freeze_point_without_skipping_or_repeating_AC_04()
    {
        using var rig = PlayingRig();
        rig.Engine.Call(() => rig.Session.Pause()).IsSuccess.ShouldBeTrue();
        rig.Engine.Render(100);
        var frozen = Shown(rig);

        rig.Engine.Call(() => rig.Session.Resume()).IsSuccess.ShouldBeTrue();
        rig.Engine.Render(150);

        rig.Session.State.ShouldBe(PlaybackState.Playing);
        rig.Renderer.IsPlaying.ShouldBeTrue();
        var resumed = Shown(rig);
        resumed.ShouldBeGreaterThan(frozen);
        (resumed - frozen).TotalSeconds.ShouldBeLessThan(1.5 + 1.0);
        rig.Events.OfType<PictureDelayChanged>().ShouldContain(e => e.Cause == PictureDelayCause.Resume);
    }

    [Fact]
    public void Resume_before_the_picture_has_frozen_still_realigns_picture_with_sound_AC_04()
    {
        using var rig = PlayingRig();
        rig.Engine.Call(() => rig.Session.Pause()).IsSuccess.ShouldBeTrue();
        var freeze = rig.Events.OfType<PictureDelayChanged>().Last(e => e.Cause == PictureDelayCause.Pause).Clock;
        var pictureAtPause = Shown(rig);
        var delay = TimeSpan.FromMilliseconds(rig.Events.OfType<PictureDelayChanged>().Last().NewPictureDelayMs);
        (freeze - pictureAtPause).ShouldBeGreaterThan(TimeSpan.FromMilliseconds(60));

        // Resume while the picture is still running up to the freeze point.
        rig.Engine.Render(2);
        rig.Renderer.IsPlaying.ShouldBeTrue();
        rig.Engine.Call(() => rig.Session.Resume()).IsSuccess.ShouldBeTrue();
        var resumedAt = rig.Engine.Time.GetTimestamp();
        rig.Engine.Render(150);
        rig.Engine.Time.GetElapsedTime(resumedAt).ShouldBeGreaterThan(delay);

        rig.Session.State.ShouldBe(PlaybackState.Playing);
        rig.Renderer.IsPlaying.ShouldBeTrue();

        // The frame at the freeze point is shown one picture delay after the resume, then the picture runs with the sound.
        var sinceShown = rig.Engine.Time.GetElapsedTime(resumedAt) - delay;
        Math.Abs((Shown(rig) - (freeze + sinceShown)).TotalMilliseconds).ShouldBeLessThan(20);
    }

    [Fact]
    public void Seeking_forward_while_playing_moves_the_picture_and_keeps_playing_AC_05()
    {
        using var rig = PlayingRig();
        var before = Shown(rig);

        rig.Engine.Call(() => rig.Session.Seek(TimeSpan.FromSeconds(10))).IsSuccess.ShouldBeTrue();
        rig.Engine.Render(150);

        rig.Session.State.ShouldBe(PlaybackState.Playing);
        rig.Renderer.IsPlaying.ShouldBeTrue();
        var shown = Shown(rig);
        shown.ShouldBeGreaterThanOrEqualTo(before + TimeSpan.FromSeconds(10));
        (shown - before).TotalSeconds.ShouldBeLessThan(10 + 3);
        rig.Events.OfType<PictureDelayChanged>().ShouldContain(e => e.Cause == PictureDelayCause.Seek);
    }

    [Fact]
    public void Seeking_while_paused_shows_the_frame_at_the_new_position_and_stays_paused_AC_05()
    {
        using var rig = PlayingRig();
        rig.Engine.Call(() => rig.Session.Pause()).IsSuccess.ShouldBeTrue();
        rig.Engine.Render(100);
        var frozen = Shown(rig);

        rig.Engine.Call(() => rig.Session.Seek(TimeSpan.FromSeconds(-1))).IsSuccess.ShouldBeTrue();
        rig.Engine.Render(100);

        rig.Session.State.ShouldBe(PlaybackState.Paused);
        rig.Renderer.IsPlaying.ShouldBeFalse();
        Math.Abs((Shown(rig) - (frozen - TimeSpan.FromSeconds(1))).TotalMilliseconds).ShouldBeLessThan(20);
    }

    [Fact]
    public void Seeking_before_the_start_goes_to_the_start_AC_05()
    {
        using var rig = PlayingRig();

        rig.Engine.Call(() => rig.Session.Seek(TimeSpan.FromSeconds(-60))).IsSuccess.ShouldBeTrue();
        rig.Engine.Render(50);

        rig.Session.State.ShouldBe(PlaybackState.Playing);
        Shown(rig).ShouldBeLessThan(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void Seeking_past_the_end_ends_the_film_as_at_its_natural_end_AC_05()
    {
        using var rig = PlayingRig(totalSeconds: 30);

        rig.Engine.Call(() => rig.Session.Seek(TimeSpan.FromSeconds(60))).IsSuccess.ShouldBeTrue();
        rig.Engine.WaitFor(() =>
        {
            rig.Engine.Render(10);
            return rig.Session.State == PlaybackState.Ended;
        }).ShouldBeTrue();

        rig.Renderer.IsPlaying.ShouldBeFalse();
    }

    [Fact]
    public void A_backward_seek_at_the_end_returns_into_the_film_and_plays_AC_05()
    {
        using var rig = PlayingRig(totalSeconds: 30);
        rig.Engine.Call(() => rig.Session.Seek(TimeSpan.FromSeconds(60))).IsSuccess.ShouldBeTrue();
        rig.Engine.WaitFor(() =>
        {
            rig.Engine.Render(10);
            return rig.Session.State == PlaybackState.Ended;
        }).ShouldBeTrue();

        rig.Engine.Call(() => rig.Session.Seek(TimeSpan.FromSeconds(-10))).IsSuccess.ShouldBeTrue();
        rig.Engine.Render(100);

        rig.Session.State.ShouldBe(PlaybackState.Playing);
        rig.Renderer.IsPlaying.ShouldBeTrue();
    }
}
