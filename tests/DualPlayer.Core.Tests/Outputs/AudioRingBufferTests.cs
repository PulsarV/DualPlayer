using DualPlayer.Core.Outputs;

namespace DualPlayer.Core.Tests.Outputs;

public sealed class AudioRingBufferTests
{
    private static float[] Ramp(int start, int frames, int channels)
    {
        var data = new float[frames * channels];
        for (var i = 0; i < frames; i++)
        {
            for (var c = 0; c < channels; c++)
            {
                data[(i * channels) + c] = start + i + (c * 0.5f);
            }
        }

        return data;
    }

    [Fact]
    public void New_buffer_is_empty_with_all_frames_free()
    {
        var ring = new AudioRingBuffer(capacityFrames: 8, channels: 2);

        ring.FramesAvailable.ShouldBe(0);
        ring.FreeFrames.ShouldBe(8);
    }

    [Fact]
    public void Last_written_epoch_is_the_epoch_of_the_newest_frame_written_even_after_it_was_read()
    {
        var ring = new AudioRingBuffer(8, 2);
        ring.LastWrittenEpoch.ShouldBe(-1);

        ring.Write(Ramp(0, 2, 2), epoch: 3);
        ring.Read(new float[4], 3, out _).ShouldBe(2);
        ring.LastWrittenEpoch.ShouldBe(3);

        ring.Write(Ramp(0, 8, 2), epoch: 4);
        ring.Write(Ramp(0, 1, 2), epoch: 5).ShouldBe(0, "the ring is full");
        ring.LastWrittenEpoch.ShouldBe(4, "nothing of epoch 5 was written");
    }

    [Fact]
    public void Read_when_empty_returns_zero()
    {
        var ring = new AudioRingBuffer(8, 2);
        var dest = new float[8];

        ring.Read(dest, currentEpoch: 0, out var discarded).ShouldBe(0);
        discarded.ShouldBe(0);
    }

    [Fact]
    public void Write_then_read_returns_the_same_frames_in_order()
    {
        var ring = new AudioRingBuffer(8, 2);
        var src = Ramp(0, 5, 2);
        var dest = new float[10];

        ring.Write(src, epoch: 0).ShouldBe(5);
        ring.FramesAvailable.ShouldBe(5);
        ring.Read(dest, 0, out _).ShouldBe(5);

        dest.ShouldBe(src);
        ring.FramesAvailable.ShouldBe(0);
    }

    [Fact]
    public void Write_larger_than_free_space_is_partial_and_returns_accepted_count()
    {
        var ring = new AudioRingBuffer(4, 2);

        ring.Write(Ramp(0, 6, 2), 0).ShouldBe(4);
        ring.FreeFrames.ShouldBe(0);
        ring.Write(Ramp(0, 1, 2), 0).ShouldBe(0);
    }

    [Fact]
    public void Frames_survive_wrap_around_in_order()
    {
        var ring = new AudioRingBuffer(4, 2);
        var dest = new float[8];

        ring.Write(Ramp(0, 3, 2), 0).ShouldBe(3);
        ring.Read(dest.AsSpan(0, 6), 0, out _).ShouldBe(3);

        var src = Ramp(10, 4, 2);
        ring.Write(src, 0).ShouldBe(4);
        ring.Read(dest, 0, out _).ShouldBe(4);

        dest.ShouldBe(src);
    }

    [Fact]
    public void Read_skips_frames_from_an_older_epoch_and_reports_them_discarded()
    {
        var ring = new AudioRingBuffer(16, 2);
        var fresh = Ramp(100, 3, 2);
        var dest = new float[16];

        ring.Write(Ramp(0, 4, 2), epoch: 1).ShouldBe(4);
        ring.Write(fresh, epoch: 2).ShouldBe(3);

        var read = ring.Read(dest, currentEpoch: 2, out var discarded);

        read.ShouldBe(3);
        discarded.ShouldBe(4);
        dest.AsSpan(0, 6).ToArray().ShouldBe(fresh);
        ring.FramesAvailable.ShouldBe(0);
    }

    [Fact]
    public void Read_never_copies_old_epoch_frames_when_only_old_audio_is_queued()
    {
        var ring = new AudioRingBuffer(8, 2);
        var dest = new float[8];
        dest.AsSpan().Fill(-1f);

        ring.Write(Ramp(0, 5, 2), epoch: 3);

        ring.Read(dest, currentEpoch: 4, out var discarded).ShouldBe(0);
        discarded.ShouldBe(5);
        dest.ShouldAllBe(x => x == -1f);
        ring.FreeFrames.ShouldBe(8);
    }

    [Fact]
    public void Read_records_the_timestamp_passed_in_by_the_reader()
    {
        var ring = new AudioRingBuffer(8, 2);
        var dest = new float[4];

        ring.Read(dest, 0, out _, readTimestamp: 12345);

        ring.LastReadTimestamp.ShouldBe(12345);
    }

    [Fact]
    public void Read_allocates_zero_bytes()
    {
        var ring = new AudioRingBuffer(256, 2);
        var dest = new float[512];
        ring.Write(Ramp(0, 128, 2), 1);
        ring.Read(dest, 1, out _); // warm up
        ring.Write(Ramp(0, 128, 2), 1);
        ring.Write(Ramp(0, 10, 2), 0);

        var before = GC.GetAllocatedBytesForCurrentThread();
        ring.Read(dest, 1, out _);
        var after = GC.GetAllocatedBytesForCurrentThread();

        (after - before).ShouldBe(0);
    }

    [Fact]
    public async Task One_writer_and_one_reader_transfer_ten_million_frames_intact()
    {
        const int total = 10_000_000;
        const int channels = 2;
        var ring = new AudioRingBuffer(4096, channels);
        double writerSum = 0, readerSum = 0;
        long readFrames = 0;
        var inOrder = true;

        var writer = Task.Run(() =>
        {
            var chunk = new float[333 * channels];
            var next = 0;
            while (next < total)
            {
                var n = Math.Min(333, total - next);
                for (var i = 0; i < n; i++)
                {
                    var v = (next + i) % 100_000;
                    chunk[i * channels] = v;
                    chunk[(i * channels) + 1] = -v;
                    writerSum += v;
                }

                var offset = 0;
                while (offset < n)
                {
                    var wrote = ring.Write(chunk.AsSpan(offset * channels, (n - offset) * channels), 0);
                    if (wrote == 0)
                    {
                        Thread.Yield();
                    }

                    offset += wrote;
                }

                next += n;
            }
        }, TestContext.Current.CancellationToken);

        var reader = Task.Run(() =>
        {
            var buf = new float[257 * channels];
            while (readFrames < total)
            {
                var n = ring.Read(buf, 0, out _);
                if (n == 0)
                {
                    Thread.Yield();
                    continue;
                }

                for (var i = 0; i < n; i++)
                {
                    var expected = (readFrames + i) % 100_000;
                    if (buf[i * channels] != expected || buf[(i * channels) + 1] != -expected)
                    {
                        inOrder = false;
                    }

                    readerSum += buf[i * channels];
                }

                readFrames += n;
            }
        }, TestContext.Current.CancellationToken);

        await Task.WhenAll(writer, reader).WaitAsync(TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken);

        inOrder.ShouldBeTrue();
        readFrames.ShouldBe(total);
        readerSum.ShouldBe(writerSum);
    }
}
