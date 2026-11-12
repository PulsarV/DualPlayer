using DualPlayer.Core.Outputs;

namespace DualPlayer.Core.Tests.Outputs;

public sealed class AudioRingBufferReclaimRaceTests
{
    private const int Channels = 2;
    private const int Capacity = 4_096;
    private const int Piece = 1_024;
    private const long TotalFrames = 8_000_000; // below 2^24, so every content frame is exact as a float sample
    private static readonly TimeSpan RunFor = TimeSpan.FromSeconds(3);

    [Fact]
    public void Reclaiming_a_full_ring_while_the_reader_reads_never_counts_a_dropped_or_skipped_frame_twice()
    {
        // Every sample holds its content frame, so for each frame read: sample == frames read before it + skipped
        // frames passed so far. The writer behaves like the pump for a stalled output (reclaim whenever full, plus an
        // occasional decoder hole reported with NoteSkipped); the reader like the render path (BeginRead per period,
        // then one-frame reads as the resampler pulls them).
        var ring = new AudioRingBuffer(Capacity, Channels);
        var writerDone = false;
        long reclaims = 0, readsChecked = 0, violations = 0, torn = 0, maxError = 0, shortWrites = 0;
        Exception? writerFault = null;
        var deadline = DateTime.UtcNow + RunFor;

        var writer = new Thread(() =>
        {
            try
            {
                var block = new float[Piece * Channels];
                long next = 0;
                var blocks = 0;
                while (next + Piece < TotalFrames && DateTime.UtcNow < deadline)
                {
                    if (ring.FreeFrames < Piece)
                    {
                        if (ring.TryReclaimOldest(Piece, epoch: 0))
                        {
                            reclaims++;
                        }

                        continue;
                    }

                    if (++blocks % 7 == 0)
                    {
                        ring.NoteSkipped(100, epoch: 0); // a hole in the decoded content
                        next += 100;
                    }

                    for (var i = 0; i < Piece; i++)
                    {
                        block[2 * i] = next + i;
                        block[(2 * i) + 1] = next + i;
                    }

                    if (ring.Write(block, epoch: 0) != Piece)
                    {
                        shortWrites++;
                    }

                    next += Piece;
                }
            }
            catch (Exception ex)
            {
                writerFault = ex;
            }

            Volatile.Write(ref writerDone, true);
        })
        { IsBackground = true, Name = "ring-race-writer" };

        var reader = new Thread(() =>
        {
            var one = new float[Channels];
            long reads = 0;
            long timestamp = 0;
            while (true)
            {
                ring.BeginRead(currentEpoch: 0, ++timestamp);
                var gotAny = false;
                for (var f = 0; f < 24; f++)
                {
                    if (ring.Read(one) == 0)
                    {
                        break;
                    }

                    gotAny = true;
                    var value = (long)one[0];
                    if (one[0] != one[1])
                    {
                        torn++;
                    }

                    var expected = reads + ring.SkippedFramesConsumed;
                    if (value != expected)
                    {
                        violations++;
                        maxError = Math.Max(maxError, Math.Abs(expected - value));
                        reads = value - ring.SkippedFramesConsumed; // resync, so each later violation is a new one
                    }

                    reads++;
                    readsChecked++;
                }

                if (!gotAny && Volatile.Read(ref writerDone) && ring.FramesAvailable == 0)
                {
                    return;
                }
            }
        })
        { IsBackground = true, Name = "ring-race-reader" };

        writer.Start();
        reader.Start();
        writer.Join(RunFor + TimeSpan.FromSeconds(10)).ShouldBeTrue("the writer must finish");
        reader.Join(TimeSpan.FromSeconds(10)).ShouldBeTrue("the reader must drain and finish");

        writerFault.ShouldBeNull();
        shortWrites.ShouldBe(0, "every write after a reclaim must be accepted in full");
        reclaims.ShouldBeGreaterThan(0, "the run must exercise reclaim");
        readsChecked.ShouldBeGreaterThan(0);
        torn.ShouldBe(0);
        violations.ShouldBe(0, $"max content-position error {maxError} frames over {readsChecked} reads and {reclaims} reclaims");
    }
}
