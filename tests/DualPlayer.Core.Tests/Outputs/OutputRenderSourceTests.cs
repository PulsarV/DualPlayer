using DualPlayer.Core.Outputs;
using DualPlayer.Core.Playback;
using DualPlayer.Core.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace DualPlayer.Core.Tests.Outputs;

public sealed class OutputRenderSourceTests
{
    private const int Base = 1000;

    private static float[] Ramp(int start, int frames)
    {
        var data = new float[frames];
        for (var i = 0; i < frames; i++)
        {
            data[i] = start + i;
        }

        return data;
    }

    private static (OutputRenderSource Source, AudioRingBuffer Ring) Create(int filledFrames = 600)
    {
        var ring = new AudioRingBuffer(2048, channels: 1);
        ring.Write(Ramp(Base, filledFrames), epoch: 0);
        var source = new OutputRenderSource(ring);
        source.BeginEpoch(0, startContentFrame: 0);
        return (source, ring);
    }

    [Fact]
    public void Render_hands_ring_frames_through_at_unity_ratio()
    {
        var (source, _) = Create();
        var buffer = new float[100];

        source.Render(buffer, new DevicePosition(0, 1));

        buffer.ShouldBe(Ramp(Base, 100));
        source.UnderrunCount.ShouldBe(0);
    }

    [Fact]
    public void Render_publishes_the_latest_device_position()
    {
        var (source, _) = Create();

        source.Render(new float[10], new DevicePosition(480, 12345));

        source.LatestFramesPlayed.ShouldBe(480);
        source.LatestTimestamp.ShouldBe(12345);
    }

    [Fact]
    public void Dry_ring_during_playback_fills_silence_and_counts_one_underrun_per_call()
    {
        var (source, _) = Create(filledFrames: 30);
        var buffer = new float[100];
        Array.Fill(buffer, -1f);

        source.Render(buffer, new DevicePosition(0, 1));

        buffer[0].ShouldBe(Base);
        buffer[^1].ShouldBe(0f);
        source.UnderrunCount.ShouldBe(1);

        source.Render(new float[100], new DevicePosition(100, 2));
        source.UnderrunCount.ShouldBe(2);
    }

    [Fact]
    public void New_epoch_discards_old_frames_and_plays_silence_without_an_underrun()
    {
        var (source, ring) = Create(filledFrames: 200);
        source.BeginEpoch(1, startContentFrame: 5000);
        var buffer = new float[100];
        Array.Fill(buffer, -1f);

        source.Render(buffer, new DevicePosition(0, 1));

        buffer.ShouldAllBe(s => s == 0f);
        source.UnderrunCount.ShouldBe(0);
        ring.FramesAvailable.ShouldBe(0);
    }

    [Fact]
    public void New_epoch_frames_play_once_they_arrive_and_later_dryness_is_an_underrun()
    {
        var (source, ring) = Create(filledFrames: 200);
        source.BeginEpoch(1, startContentFrame: 5000);
        source.Render(new float[100], new DevicePosition(0, 1));

        ring.Write(Ramp(9000, 50), epoch: 1);
        var buffer = new float[100];
        source.Render(buffer, new DevicePosition(100, 2));

        buffer[0].ShouldBe(9000f);
        source.UnderrunCount.ShouldBe(1);
    }

    [Fact]
    public void Silence_instant_inside_the_buffer_gives_audio_up_to_that_frame_then_zeros()
    {
        var (source, _) = Create();
        source.SetSilenceFrom(40);
        var buffer = new float[100];

        source.Render(buffer, new DevicePosition(0, 1));

        buffer[..40].ShouldBe(Ramp(Base, 40));
        buffer[40..].ShouldAllBe(s => s == 0f);
        source.UnderrunCount.ShouldBe(0);
    }

    [Fact]
    public void Silence_after_the_instant_is_not_an_underrun_even_when_the_ring_is_empty()
    {
        var (source, _) = Create(filledFrames: 40);
        source.SetSilenceFrom(40);

        source.Render(new float[100], new DevicePosition(0, 1));
        source.Render(new float[100], new DevicePosition(100, 2));

        source.UnderrunCount.ShouldBe(0);
    }

    [Fact]
    public void Clearing_the_silence_instant_resumes_audio()
    {
        var (source, _) = Create();
        source.SetSilenceFrom(0);
        source.Render(new float[50], new DevicePosition(0, 1));

        source.ClearSilence();
        var buffer = new float[10];
        source.Render(buffer, new DevicePosition(50, 2));

        buffer.ShouldNotBeEmpty();
        buffer.ShouldContain(s => s >= Base);
    }

    [Fact]
    public void Queued_frames_are_frames_written_to_the_device_minus_its_reported_position()
    {
        var (source, _) = Create();

        source.Render(new float[100], new DevicePosition(0, 1));
        source.QueuedFrames.ShouldBe(100);

        source.Render(new float[100], new DevicePosition(60, 2));
        source.QueuedFrames.ShouldBe(140);
    }

    [Fact]
    public void Last_content_frame_handed_out_tracks_the_epoch_start_plus_frames_played()
    {
        var (source, _) = Create();
        source.BeginEpoch(0, startContentFrame: 1000);

        source.Render(new float[50], new DevicePosition(0, 1));

        source.LastContentFrameHandedOut.ShouldBe(1049);
    }

    [Fact]
    public void Requested_realignment_drops_frames_once_at_the_next_call()
    {
        var (source, _) = Create();
        source.RequestRealign(5);
        var first = new float[20];
        var second = new float[20];

        source.Render(first, new DevicePosition(0, 1));
        source.Render(second, new DevicePosition(20, 2));

        first.ShouldBe(Ramp(Base + 5, 20));
        second.ShouldBe(Ramp(Base + 25, 20));
    }

    [Fact]
    public void Resampler_ratio_set_by_the_control_loop_changes_how_many_frames_are_consumed()
    {
        var (source, ring) = Create(filledFrames: 1000);
        source.SetResamplerRatio(2.0);

        source.Render(new float[200], new DevicePosition(0, 1));

        ring.FramesAvailable.ShouldBeGreaterThan(850);
        ring.FramesAvailable.ShouldBeLessThan(1000 - 90);
    }

    [Fact]
    public void Render_allocates_zero_bytes()
    {
        var (source, _) = Create(filledFrames: 2000);
        var buffer = new float[100];
        source.Render(buffer, new DevicePosition(0, 1));

        var before = GC.GetAllocatedBytesForCurrentThread();
        source.Render(buffer, new DevicePosition(100, 2));
        source.Render(buffer, new DevicePosition(200, 3));

        (GC.GetAllocatedBytesForCurrentThread() - before).ShouldBe(0);
    }

    [Fact]
    public void Render_stamps_the_ring_read_with_the_device_position_timestamp()
    {
        var (source, ring) = Create();

        source.Render(new float[100], new DevicePosition(0, 777));

        ring.LastReadTimestamp.ShouldBe(777);
    }

    [Fact]
    public void Silent_render_while_paused_still_refreshes_the_ring_read_timestamp()
    {
        var (source, ring) = Create();
        source.SetSilenceFrom(0);

        source.Render(new float[100], new DevicePosition(0, 555));

        ring.LastReadTimestamp.ShouldBe(555);
        ring.FramesAvailable.ShouldBe(600);
    }

    [Fact]
    public void Fake_device_render_publishes_its_position_and_stamps_the_ring_read()
    {
        var time = new FakeTimeProvider();
        var ring = new AudioRingBuffer(4096, channels: 2);
        ring.Write(new float[2 * 2000], epoch: 0);
        var source = new OutputRenderSource(ring);
        source.BeginEpoch(0, startContentFrame: 0);
        var backend = new FakeAudioBackend(time, new MixFormat(48_000, 2, ChannelLayout.Stereo));
        backend.AddDevice(new AudioDeviceInfo("Speakers", null, "dev-1"));
        var device = (FakeAudioDevice)backend.Open("dev-1", TimeSpan.FromMilliseconds(40), source).Value!;
        device.Start();

        time.Advance(TimeSpan.FromMilliseconds(10));
        device.PumpRender(frames: 480);

        source.LatestFramesPlayed.ShouldBe(480);
        source.LatestTimestamp.ShouldBe(time.GetTimestamp());
        ring.LastReadTimestamp.ShouldBe(time.GetTimestamp());
        source.QueuedFrames.ShouldBe(0);
        source.UnderrunCount.ShouldBe(0);
    }

    [Fact]
    public void Silence_instant_counts_content_frames_when_the_ratio_stretches()
    {
        var (source, _) = Create();
        source.SetResamplerRatio(2.0);
        source.SetSilenceFrom(40);
        var buffer = new float[200];
        Array.Fill(buffer, -1f);

        source.Render(buffer, new DevicePosition(0, 1));

        buffer[79].ShouldBe(Base + 39.5f);
        buffer[80..].ShouldAllBe(s => s == 0f);
        source.LastContentFrameHandedOut.ShouldBe(39);
        source.UnderrunCount.ShouldBe(0);
    }

    [Fact]
    public void Silence_instant_counts_content_frames_when_the_ratio_compresses()
    {
        var (source, _) = Create();
        source.SetResamplerRatio(0.5);
        source.SetSilenceFrom(40);
        var buffer = new float[100];
        Array.Fill(buffer, -1f);

        source.Render(buffer, new DevicePosition(0, 1));

        buffer[..20].ShouldBe([.. Enumerable.Range(0, 20).Select(i => (float)(Base + (2 * i)))]);
        buffer[20..].ShouldAllBe(s => s == 0f);
        source.LastContentFrameHandedOut.ShouldBe(38);
        source.UnderrunCount.ShouldBe(0);
    }

    [Fact]
    public void Last_content_frame_handed_out_follows_content_frames_at_a_non_unity_ratio()
    {
        var (source, _) = Create();
        source.SetResamplerRatio(2.0);

        source.Render(new float[100], new DevicePosition(0, 1));

        source.LastContentFrameHandedOut.ShouldBe(49);
    }

    [Fact]
    public void Last_content_frame_handed_out_includes_frames_dropped_by_a_realign()
    {
        var (source, _) = Create();
        source.RequestRealign(5);

        source.Render(new float[20], new DevicePosition(0, 1));

        source.LastContentFrameHandedOut.ShouldBe(24);
    }

    [Fact]
    public void Silence_instant_after_a_realign_stops_at_the_content_frame()
    {
        var (source, _) = Create();
        source.RequestRealign(5);
        source.SetSilenceFrom(30);
        var buffer = new float[100];

        source.Render(buffer, new DevicePosition(0, 1));

        buffer[..25].ShouldBe(Ramp(Base + 5, 25));
        buffer[25..].ShouldAllBe(s => s == 0f);
        source.LastContentFrameHandedOut.ShouldBe(29);
    }

    [Fact]
    public void Epoch_switch_publishes_the_applied_epoch_and_the_old_epoch_boundary()
    {
        var (source, _) = Create();
        source.Render(new float[100], new DevicePosition(0, 1));
        source.AppliedEpoch.ShouldBe(0);

        source.BeginEpoch(1, startContentFrame: 5000);
        source.Render(new float[100], new DevicePosition(40, 2));

        source.AppliedEpoch.ShouldBe(1);
        source.PreviousEpochLastContentFrame.ShouldBe(99);
        source.OldEpochEndFramesWritten.ShouldBe(100);
        source.HasPlayedOutOldEpoch(1).ShouldBeFalse();

        source.Render(new float[100], new DevicePosition(100, 3));
        source.HasPlayedOutOldEpoch(1).ShouldBeTrue();
        source.HasPlayedOutOldEpoch(2).ShouldBeFalse();
    }

    [Fact]
    public void Seek_while_paused_discards_the_old_epoch_frames_from_the_ring()
    {
        var (source, ring) = Create(filledFrames: 200);
        source.SetSilenceFrom(0);
        source.Render(new float[100], new DevicePosition(0, 1));
        ring.FramesAvailable.ShouldBe(200);

        source.BeginEpoch(1, startContentFrame: 5000);
        source.Render(new float[100], new DevicePosition(100, 2));

        ring.FramesAvailable.ShouldBe(0);
        source.AppliedEpoch.ShouldBe(1);
        source.UnderrunCount.ShouldBe(0);
    }

    [Fact]
    public void Render_allocates_zero_bytes_on_underrun_epoch_switch_and_silence_paths()
    {
        var (source, ring) = Create(filledFrames: 250);
        var buffer = new float[100];
        source.Render(buffer, new DevicePosition(0, 1));

        var before = GC.GetAllocatedBytesForCurrentThread();
        source.Render(buffer, new DevicePosition(100, 2));
        source.Render(buffer, new DevicePosition(200, 3));
        source.SetSilenceFrom(250);
        source.Render(buffer, new DevicePosition(300, 4));
        source.Render(buffer, new DevicePosition(400, 5));
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        source.ClearSilence();
        source.BeginEpoch(1, startContentFrame: 5000);
        ring.Write(Ramp(Base, 300), epoch: 1);
        var beforeSwitch = GC.GetAllocatedBytesForCurrentThread();
        source.Render(buffer, new DevicePosition(500, 6));
        allocated += GC.GetAllocatedBytesForCurrentThread() - beforeSwitch;

        allocated.ShouldBe(0);
        source.UnderrunCount.ShouldBe(1);
        source.AppliedEpoch.ShouldBe(1);
    }

    [Fact]
    public void Frames_the_pump_skipped_for_a_stalled_output_advance_the_content_position_once_it_reads_again()
    {
        var time = new FakeTimeProvider();
        var stalled = new AudioRingBuffer(2_000, 1);
        var healthy = new AudioRingBuffer(100_000, 1);
        var session = new GatedSession(new MismatchedBlockSession([1536, 1536], 100_000));
        var source = new OutputRenderSource(stalled);
        source.BeginEpoch(1, startContentFrame: 0);
        using var pump = new DecodePump(session, [stalled, healthy], time, NullLogger<DecodePump>.Instance);
        try
        {
            // The device is open but silent for now; block 0 lands in the ring.
            source.Render(new float[1], new DevicePosition(0, time.GetTimestamp()));
            pump.Start(epoch: 1);
            session.Release(2);
            WaitFor(() => stalled.FramesAvailable >= 1_536).ShouldBeTrue();

            // The reader stops for over a second: the pump skips this output's next two blocks (1536 frames each).
            session.Release(4);
            var scratch = new float[2_000];
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
            while (session.Reads < 7 && DateTime.UtcNow < deadline)
            {
                healthy.Read(scratch, 1, out _, time.GetTimestamp()); // the healthy output keeps reading
                time.Advance(TimeSpan.FromMilliseconds(100));
                Thread.Sleep(1);
            }

            session.Reads.ShouldBe(7);
            stalled.FramesAvailable.ShouldBe(1_536);

            // It reads again, draining block 0; the pump then delivers block 3 (content frames 4608..6143).
            var drain = new float[1_500];
            source.Render(drain, new DevicePosition(0, time.GetTimestamp()));
            session.Release(2);
            WaitFor(() => session.Reads == 9).ShouldBeTrue();
            WaitFor(() => stalled.FramesAvailable >= 1_536).ShouldBeTrue();

            var buffer = new float[1_000];
            source.Render(buffer, new DevicePosition(1_500, time.GetTimestamp()));

            var lastSample = (long)Math.Round(buffer[^1]);
            lastSample.ShouldBeGreaterThan(4_608, "the last frame handed out must come from the block after the skipped ones");
            source.LastContentFrameHandedOut.ShouldBe(lastSample);
        }
        finally
        {
            session.Open();
        }
    }

    private static bool WaitFor(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return true;
            }

            Thread.Sleep(2);
        }

        return condition();
    }

    /// <summary>Hands the pump one decoded block per <see cref="Release"/> permit.</summary>
    private sealed class GatedSession(IDecodeSession inner) : IDecodeSession
    {
        private readonly SemaphoreSlim _gate = new(0);
        private int _reads;
        private bool _open;

        public int Reads => Volatile.Read(ref _reads);

        public void Release(int blocks) => _gate.Release(blocks);

        public void Open()
        {
            Volatile.Write(ref _open, true);
            _gate.Release();
        }

        public Result Seek(TimeSpan position, int epoch) => inner.Seek(position, epoch);

        public Result<DecodedBlock> Read()
        {
            Interlocked.Increment(ref _reads);
            if (!Volatile.Read(ref _open))
            {
                _gate.Wait();
            }

            return inner.Read();
        }

        public void Dispose() => inner.Dispose();
    }
}
