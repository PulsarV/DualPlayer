using DualPlayer.Core.Outputs;
using DualPlayer.Core.Playback;
using DualPlayer.Core.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace DualPlayer.Core.Tests.Playback;

public sealed class DecodePumpTests
{
    private const int Block = 480;
    private static readonly MixFormat Stereo = new(48_000, 2, ChannelLayout.Stereo);
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private static IReadOnlyList<AudioTrackInfo> Tracks() =>
        [new AudioTrackInfo(1, "eng", ChannelLayout.Stereo, 48_000), new AudioTrackInfo(2, "ukr", ChannelLayout.Stereo, 48_000)];

    private static IDecodeSession OpenSession(long totalFrames, int outputs = 2)
    {
        var decoder = new FakeAudioDecoder(Tracks(), totalFrames);
        var requests = Enumerable.Range(0, outputs).Select(i => new DecodeRequest((i % 2) + 1, Stereo)).ToList();
        return decoder.Open("file.mkv", requests).Value!;
    }

    private static DecodePump NewPump(IDecodeSession session, FakeTimeProvider time, params AudioRingBuffer[] rings) =>
        new(session, rings, time, NullLogger<DecodePump>.Instance);

    private static bool WaitFor(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Timeout;
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

    [Fact]
    public void One_decode_feeds_every_ring_with_its_own_track_frames_in_order()
    {
        var time = new FakeTimeProvider();
        var ring0 = new AudioRingBuffer(10_000, 2);
        var ring1 = new AudioRingBuffer(10_000, 2);
        var ended = new ManualResetEventSlim();
        using var pump = NewPump(OpenSession(4_800), time, ring0, ring1);
        pump.Ended += ended.Set;

        pump.Start(epoch: 1);

        ended.Wait(Timeout, TestContext.Current.CancellationToken).ShouldBeTrue();
        ring0.FramesAvailable.ShouldBe(4_800);
        ring1.FramesAvailable.ShouldBe(4_800);
        var dest = new float[4_800 * 2];
        ring1.Read(dest, 1, out var discarded).ShouldBe(4_800);
        discarded.ShouldBe(0);
        dest[0].ShouldBe(0f);
        dest[(4_799 * 2)].ShouldBe(4.799f, 0.0001f);
    }

    private static (AudioRingBuffer Ring, long Skipped) RunScripted(params long[] startFrames)
    {
        var ring = new AudioRingBuffer(10_000, 2);
        var ended = new ManualResetEventSlim();
        using var pump = NewPump(new ScriptedSession(startFrames), new FakeTimeProvider(), ring);
        pump.Ended += ended.Set;
        pump.Start(epoch: 1);
        ended.Wait(Timeout, TestContext.Current.CancellationToken).ShouldBeTrue();
        return (ring, 0);
    }

    [Fact]
    public void A_gap_between_blocks_from_the_decoder_advances_the_content_position_by_the_gap()
    {
        // Blocks at frames 0 and 960 with a 480-frame hole between them (skipped corrupt packets).
        var (ring, _) = RunScripted(0, 960, 1_440);

        var dest = new float[1_440 * 2];
        ring.Read(dest, 1, out _).ShouldBe(1_440);

        ring.SkippedFramesConsumed.ShouldBe(480, "the reader must be told that 480 content frames are missing");
    }

    [Fact]
    public void Blocks_that_overlap_the_previous_block_have_the_overlapping_frames_dropped()
    {
        // Second block claims to start at frame 240, i.e. its first 240 frames were already delivered.
        var (ring, _) = RunScripted(0, 240, 720);

        ring.FramesAvailable.ShouldBe(1_200, "480 + (480 - 240 overlap) + 480");
        var dest = new float[1_200 * 2];
        ring.Read(dest, 1, out _).ShouldBe(1_200);
        for (var frame = 0; frame < 1_200; frame++)
        {
            dest[frame * 2].ShouldBe(frame / 1000f, 0.0001f, $"content frame {frame} must appear exactly once, in order");
        }

        ring.SkippedFramesConsumed.ShouldBe(0);
    }

    /// <summary>
    /// Runs a pump that starts with an empty script, seeks to <paramref name="target"/> (epoch 2) and then delivers blocks at
    /// <paramref name="afterSeek"/>; returns the ring once the decoder reached the end of the file in epoch 2.
    /// </summary>
    private static AudioRingBuffer RunScriptedAfterSeek(TimeSpan target, params long[] afterSeek)
    {
        var ring = new AudioRingBuffer(10_000, 2);
        using var pump = NewPump(new ScriptedSession([], afterSeek), new FakeTimeProvider(), ring);
        pump.Start(epoch: 1);
        WaitFor(() => pump.EndedEpoch == 1).ShouldBeTrue();
        pump.Seek(target, epoch: 2);
        WaitFor(() => pump.EndedEpoch == 2).ShouldBeTrue();
        return ring;
    }

    [Fact]
    public void A_first_block_after_a_seek_that_starts_later_than_the_target_records_the_gap_as_skipped_frames()
    {
        // Target 5 s = frame 240 000 at 48 kHz; the decoder's first block starts 480 frames (10 ms) late.
        var ring = RunScriptedAfterSeek(TimeSpan.FromSeconds(5), 240_480, 240_960);

        var dest = new float[960 * 2];
        ring.Read(dest, 2, out _).ShouldBe(960);

        ring.SkippedFramesConsumed.ShouldBe(480, "the first block starts 480 frames after the seek target");
    }

    [Fact]
    public void A_first_block_after_a_seek_that_starts_before_the_target_has_its_leading_frames_trimmed()
    {
        // The first block covers frames 239 760..240 239: its first 240 frames are before the 240 000 target.
        var ring = RunScriptedAfterSeek(TimeSpan.FromSeconds(5), 239_760, 240_240);

        ring.FramesAvailable.ShouldBe(240 + 480, "240 frames of the first block are before the target");
        var dest = new float[720 * 2];
        ring.Read(dest, 2, out _).ShouldBe(720);
        dest[0].ShouldBe(240f, 0.0001f);
        ring.SkippedFramesConsumed.ShouldBe(0);
    }

    [Fact]
    public void A_first_block_at_start_that_starts_later_than_frame_zero_records_the_gap_as_skipped_frames()
    {
        var (ring, _) = RunScripted(480, 960);

        var dest = new float[960 * 2];
        ring.Read(dest, 1, out _).ShouldBe(960);

        ring.SkippedFramesConsumed.ShouldBe(480, "the first block starts 480 frames after the start of the file");
    }

    /// <summary>Emits 480-frame blocks of the ramp (frame / 1000) at the given content start frames, then end of stream; a seek switches to <paramref name="afterSeek"/>.</summary>
    private sealed class ScriptedSession(long[] startFrames, long[]? afterSeek = null) : IDecodeSession
    {
        private long[] _script = startFrames;
        private int _epoch = 1;
        private int _next;

        public Result Seek(TimeSpan position, int epoch)
        {
            _script = afterSeek ?? _script;
            _epoch = epoch;
            _next = 0;
            return Result.Success();
        }

        public Result<DecodedBlock> Read()
        {
            if (_next >= _script.Length)
            {
                return Result<DecodedBlock>.Failure("playback.end_of_stream", "End of file.");
            }

            var start = _script[_next++];
            var samples = new float[Block * 2];
            for (var i = 0; i < Block; i++)
            {
                samples[i * 2] = samples[(i * 2) + 1] = (start + i) / 1000f;
            }

            return Result<DecodedBlock>.Success(new DecodedBlock(0, _epoch, start, samples));
        }

        public void Dispose()
        {
        }
    }

    [Fact]
    public void A_full_ring_whose_output_has_stalled_drops_only_its_own_frames_while_another_ring_has_room()
    {
        var time = new FakeTimeProvider();
        var small = new AudioRingBuffer(Block, 2);
        var large = new AudioRingBuffer(10_000, 2);
        var ended = new ManualResetEventSlim();
        using var pump = NewPump(OpenSession(4_800), time, small, large);
        pump.Ended += ended.Set;

        pump.Start(epoch: 1);

        // The small ring's reader never reads: once it has been silent past the threshold the pump skips its blocks.
        var deadline = DateTime.UtcNow + Timeout;
        while (!ended.IsSet && DateTime.UtcNow < deadline)
        {
            time.Advance(TimeSpan.FromMilliseconds(100));
            Thread.Sleep(1);
        }

        ended.IsSet.ShouldBeTrue();
        large.FramesAvailable.ShouldBe(4_800);
        small.FramesAvailable.ShouldBe(Block);
    }

    [Fact]
    public void When_every_ring_is_full_the_pump_waits_and_drops_nothing()
    {
        var time = new FakeTimeProvider();
        var ring0 = new AudioRingBuffer(960, 2);
        var ring1 = new AudioRingBuffer(960, 2);
        var ended = new ManualResetEventSlim();
        using var pump = NewPump(OpenSession(9_600), time, ring0, ring1);
        pump.Ended += ended.Set;
        var total = new long[2];
        var dest = new float[Block * 2];

        pump.Start(epoch: 1);
        WaitFor(() => ring0.FreeFrames == 0 && ring1.FreeFrames == 0).ShouldBeTrue();
        ended.IsSet.ShouldBeFalse();

        var deadline = DateTime.UtcNow + Timeout;
        while (!(ended.IsSet && ring0.FramesAvailable == 0 && ring1.FramesAvailable == 0) && DateTime.UtcNow < deadline)
        {
            total[0] += ring0.Read(dest, 1, out _, time.GetTimestamp());
            total[1] += ring1.Read(dest, 1, out _, time.GetTimestamp());
            time.Advance(TimeSpan.FromMilliseconds(10));
            Thread.Sleep(1);
        }

        total[0].ShouldBe(9_600);
        total[1].ShouldBe(9_600);
    }

    [Fact]
    public void An_output_that_stops_reading_for_over_a_second_is_stalled_and_no_longer_paces_the_decoder()
    {
        var time = new FakeTimeProvider();
        var idle = new AudioRingBuffer(960, 2);
        var active = new AudioRingBuffer(960, 2);
        using var pump = NewPump(OpenSession(480_000), time, idle, active);
        var dest = new float[Block * 2];
        idle.Read(dest, 1, out _, time.GetTimestamp());
        active.Read(dest, 1, out _, time.GetTimestamp());

        pump.Start(epoch: 1);
        WaitFor(() => idle.FreeFrames == 0 && active.FreeFrames == 0).ShouldBeTrue();
        pump.IsStalled(0).ShouldBeFalse();

        long activeRead = 0;
        var deadline = DateTime.UtcNow + Timeout;
        while (activeRead < 4_800 && DateTime.UtcNow < deadline)
        {
            activeRead += active.Read(dest, 1, out _, time.GetTimestamp());
            time.Advance(TimeSpan.FromMilliseconds(100));
            Thread.Sleep(1);
        }

        activeRead.ShouldBeGreaterThanOrEqualTo(4_800);
        pump.IsStalled(0).ShouldBeTrue();
        pump.IsStalled(1).ShouldBeFalse();
        idle.FramesAvailable.ShouldBe(960);
    }

    [Fact]
    public void Past_the_stall_threshold_an_idle_output_no_longer_paces_the_decoder_and_the_healthy_output_loses_nothing()
    {
        var time = new FakeTimeProvider();
        var idle = new AudioRingBuffer(48_000, 2);
        var healthy = new AudioRingBuffer(960, 2);
        var session = new SteppedSession(OpenSession(480_000));
        using var pump = NewPump(session, time, idle, healthy);
        try
        {
            pump.Start(epoch: 1);

            // Before the threshold the idle output is alive: the decoder waits on the full healthy ring for its
            // third block instead of dropping it, while the idle ring (with room) keeps taking its own blocks.
            session.Release(6);
            WaitFor(() => session.Reads == 6).ShouldBeTrue();
            Thread.Sleep(100);
            session.Reads.ShouldBe(6);
            idle.FramesAvailable.ShouldBe(1_440);
            healthy.FramesAvailable.ShouldBe(960);

            // The idle output never reads; the healthy one keeps reading and loses nothing.
            session.Release(6);
            var received = new List<float>();
            var dest = new float[960 * 2];
            var deadline = DateTime.UtcNow + Timeout;
            while ((received.Count < 6 * Block || !pump.IsStalled(0)) && DateTime.UtcNow < deadline)
            {
                var read = healthy.Read(dest, 1, out var discarded, time.GetTimestamp());
                discarded.ShouldBe(0);
                received.AddRange(Enumerable.Range(0, read).Select(n => dest[n * 2]));
                time.Advance(TimeSpan.FromMilliseconds(100));
                Thread.Sleep(1);
            }

            pump.IsStalled(0).ShouldBeTrue();
            pump.IsStalled(1).ShouldBeFalse();
            session.Reads.ShouldBe(13); // the 13th read is entered and waits at the gate
            received.Count.ShouldBe(6 * Block);
            for (var n = 0; n < received.Count; n++)
            {
                received[n].ShouldBe(n / 1000f, 0.0001f);
            }
        }
        finally
        {
            session.Open();
        }
    }

    [Fact]
    public void Mismatched_block_sizes_and_readers_at_different_rates_deliver_every_frame_to_every_non_stalled_output()
    {
        const int total = 120_000;
        var time = new FakeTimeProvider();
        var rings = new[] { new AudioRingBuffer(24_000, 1), new AudioRingBuffer(24_000, 1) };
        var readPerTick = new[] { 480, 441 };
        var ended = new ManualResetEventSlim();
        using var pump = new DecodePump(new MismatchedBlockSession([1536, 1024], total), rings, time, NullLogger<DecodePump>.Instance);
        pump.Ended += ended.Set;
        var received = new List<float>[] { [], [] };
        var dest = new float[480];
        foreach (var ring in rings)
        {
            ring.Read(dest, 1, out _, time.GetTimestamp());
        }

        pump.Start(epoch: 1);
        var deadline = DateTime.UtcNow + Timeout;
        while (!(ended.IsSet && rings.All(r => r.FramesAvailable == 0)) && DateTime.UtcNow < deadline)
        {
            for (var i = 0; i < rings.Length; i++)
            {
                var read = rings[i].Read(dest.AsSpan(0, readPerTick[i]), 1, out _, time.GetTimestamp());
                received[i].AddRange(dest.Take(read));
            }

            time.Advance(TimeSpan.FromMilliseconds(10));
            Thread.Sleep(1);
        }

        for (var i = 0; i < rings.Length; i++)
        {
            pump.IsStalled(i).ShouldBeFalse();
            received[i].Count.ShouldBe(total, $"output {i} lost {total - received[i].Count} frames");
            var firstGap = received[i].Select((v, n) => (v, n)).FirstOrDefault(x => x.v != x.n);
            firstGap.v.ShouldBe(firstGap.n, $"output {i} content is not contiguous at frame {firstGap.n}");
        }
    }

    [Fact]
    public void A_full_ring_whose_output_is_not_stalled_makes_the_pump_wait_instead_of_dropping_and_a_stalled_one_is_skipped()
    {
        var time = new FakeTimeProvider();
        var idle = new AudioRingBuffer(960, 2);
        var other = new AudioRingBuffer(48_000, 2);
        var session = new SteppedSession(OpenSession(480_000));
        using var pump = NewPump(session, time, idle, other);
        try
        {
            pump.Start(epoch: 1);

            // Reads 1-4 fill both rings' first two blocks; read 5 is the third block for the full, not stalled output 0.
            session.Release(6);
            WaitFor(() => session.Reads >= 5).ShouldBeTrue();
            Thread.Sleep(100);
            session.Reads.ShouldBe(5, "the pump must wait for output 0 instead of dropping its block");
            idle.FramesAvailable.ShouldBe(960);

            // Once output 0 has not read for over a second it is stalled: its block is skipped, the pump moves on.
            var scratch = new float[Block * 2];
            var otherRead = 0;
            var deadline = DateTime.UtcNow + Timeout;
            while (session.Reads < 7 && DateTime.UtcNow < deadline)
            {
                otherRead += other.Read(scratch, 1, out _, time.GetTimestamp());
                time.Advance(TimeSpan.FromMilliseconds(100));
                Thread.Sleep(1);
            }

            session.Reads.ShouldBe(7);
            otherRead += other.Read(new float[6 * Block * 2], 1, out _, time.GetTimestamp());
            idle.FramesAvailable.ShouldBe(960);
            otherRead.ShouldBe(3 * Block);
        }
        finally
        {
            session.Open();
        }
    }

    [Fact]
    public void A_block_larger_than_the_ring_is_delivered_in_pieces()
    {
        var time = new FakeTimeProvider();
        var ring = new AudioRingBuffer(300, 2);
        var ended = new ManualResetEventSlim();
        using var pump = NewPump(OpenSession(4_800, outputs: 1), time, ring);
        pump.Ended += ended.Set;
        var dest = new float[300 * 2];
        long total = 0;
        var last = -1f;

        pump.Start(epoch: 1);
        var deadline = DateTime.UtcNow + Timeout;
        while (!(ended.IsSet && ring.FramesAvailable == 0) && DateTime.UtcNow < deadline)
        {
            var read = ring.Read(dest, 1, out _, time.GetTimestamp());
            if (read > 0)
            {
                last = dest[(read - 1) * 2];
            }

            total += read;
            time.Advance(TimeSpan.FromMilliseconds(10));
            Thread.Sleep(1);
        }

        total.ShouldBe(4_800);
        last.ShouldBe(4.799f, 0.0001f);
    }

    private static IDecodeSession OpenPerTrackSession(TimeSpan track1, TimeSpan track2) =>
        new PerTrackLengthDecoder(Tracks(), new Dictionary<int, TimeSpan> { [1] = track1, [2] = track2 })
            .Open("file.mkv", [new DecodeRequest(1, Stereo), new DecodeRequest(2, Stereo)]).Value!;

    [Fact]
    public void After_a_seek_past_one_tracks_end_the_pump_reports_no_more_blocks_for_that_output_once_another_track_is_ahead()
    {
        var ring0 = new AudioRingBuffer(96_000, 2);
        var ring1 = new AudioRingBuffer(96_000, 2);
        using var pump = NewPump(OpenPerTrackSession(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(60)), new FakeTimeProvider(), ring0, ring1);
        pump.Seek(TimeSpan.FromSeconds(20), epoch: 2); // applied before the first read

        pump.Start(epoch: 1);

        WaitFor(() => pump.HasNoBlocksInEpoch(0, 2)).ShouldBeTrue("track 1 ends at 10 s and track 2 is decoded on past 20 s");
        pump.HasNoBlocksInEpoch(1, 2).ShouldBeFalse();
        pump.HasNoBlocksInEpoch(0, 1).ShouldBeFalse("only the epoch it was decided in");
        pump.EndedEpoch.ShouldBe(-1, "the file goes on for the longer track");
        ring0.LastWrittenEpoch.ShouldBe(-1);
        ring1.LastWrittenEpoch.ShouldBe(2);
    }

    [Fact]
    public void At_the_end_of_the_file_an_output_that_got_no_block_in_the_epoch_has_no_more_blocks_and_the_others_do_not()
    {
        var ring0 = new AudioRingBuffer(96_000, 2);
        var ring1 = new AudioRingBuffer(96_000, 2);
        using var pump = NewPump(OpenPerTrackSession(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(21)), new FakeTimeProvider(), ring0, ring1);
        pump.Seek(TimeSpan.FromSeconds(20.5), epoch: 2); // track 2 has only 0.5 s left: less than the lead

        pump.Start(epoch: 1);

        WaitFor(() => pump.EndedEpoch == 2).ShouldBeTrue();
        pump.HasNoBlocksInEpoch(0, 2).ShouldBeTrue();
        pump.HasNoBlocksInEpoch(1, 2).ShouldBeFalse();
        ring1.FramesAvailable.ShouldBe(24_000);
    }

    [Fact]
    public void Disposing_the_pump_from_its_ended_handler_does_not_deadlock()
    {
        var time = new FakeTimeProvider();
        var done = new ManualResetEventSlim();
        using var pump = NewPump(OpenSession(960), time, new AudioRingBuffer(10_000, 2), new AudioRingBuffer(10_000, 2));
        pump.Ended += () =>
        {
            pump.Dispose();
            done.Set();
        };

        pump.Start(epoch: 1);

        done.Wait(Timeout, TestContext.Current.CancellationToken).ShouldBeTrue();
    }

    [Fact]
    public void An_expected_decoder_failure_is_reported_once_with_its_error_and_not_as_a_fault()
    {
        var time = new FakeTimeProvider();
        var session = Substitute.For<IDecodeSession>();
        session.Read().Returns(Result<DecodedBlock>.Failure("playback.decode_failed", "The audio track could not be decoded."));
        var errors = new List<Error>();
        var failed = new ManualResetEventSlim();
        var faulted = false;
        using var pump = NewPump(session, time, new AudioRingBuffer(960, 2));
        pump.DecodeFailed += e =>
        {
            errors.Add(e);
            failed.Set();
        };
        pump.Faulted += _ => faulted = true;

        pump.Start(epoch: 1);

        failed.Wait(Timeout, TestContext.Current.CancellationToken).ShouldBeTrue();
        Thread.Sleep(50);
        errors.ShouldHaveSingleItem().Code.ShouldBe("playback.decode_failed");
        faulted.ShouldBeFalse();
    }

    [Fact]
    public void A_detached_output_is_never_written_again()
    {
        var time = new FakeTimeProvider();
        var ring0 = new AudioRingBuffer(20_000, 2);
        var ring1 = new AudioRingBuffer(20_000, 2);
        var ended = new ManualResetEventSlim();
        using var pump = NewPump(OpenSession(9_600), time, ring0, ring1);
        pump.Ended += ended.Set;

        pump.Detach(1);
        pump.Start(epoch: 1);

        ended.Wait(Timeout, TestContext.Current.CancellationToken).ShouldBeTrue();
        ring0.FramesAvailable.ShouldBe(9_600);
        ring1.FramesAvailable.ShouldBe(0);
    }

    [Fact]
    public void After_a_seek_new_frames_carry_the_new_epoch_and_start_at_the_target()
    {
        var time = new FakeTimeProvider();
        var ring0 = new AudioRingBuffer(4_800, 2);
        var ring1 = new AudioRingBuffer(4_800, 2);
        using var pump = NewPump(OpenSession(480_000), time, ring0, ring1);
        pump.Start(epoch: 1);
        WaitFor(() => ring0.FreeFrames == 0 && ring1.FreeFrames == 0).ShouldBeTrue();

        pump.Seek(TimeSpan.FromSeconds(5), epoch: 2);

        var dest = new float[Block * 2];
        var read = 0;
        var discardedTotal = 0;
        var deadline = DateTime.UtcNow + Timeout;
        while (read == 0 && DateTime.UtcNow < deadline)
        {
            read = ring0.Read(dest, 2, out var discarded, time.GetTimestamp());
            discardedTotal += discarded;
            time.Advance(TimeSpan.FromMilliseconds(10));
            Thread.Sleep(1);
        }

        read.ShouldBeGreaterThan(0);
        discardedTotal.ShouldBeGreaterThan(0);
        dest[0].ShouldBe(240f, 0.0001f);
    }

    [Fact]
    public void A_seek_after_the_end_of_the_file_decodes_again_from_the_target_with_the_new_epoch()
    {
        var time = new FakeTimeProvider();
        var ring = new AudioRingBuffer(10_000, 2);
        var ended = new ManualResetEventSlim();
        using var pump = NewPump(OpenSession(4_800, outputs: 1), time, ring);
        pump.Ended += ended.Set;
        pump.Start(epoch: 1);
        ended.Wait(Timeout, TestContext.Current.CancellationToken).ShouldBeTrue();
        pump.EndedEpoch.ShouldBe(1);
        ended.Reset();

        pump.Seek(TimeSpan.FromSeconds(0.05), epoch: 2);

        ended.Wait(Timeout, TestContext.Current.CancellationToken).ShouldBeTrue();
        pump.EndedEpoch.ShouldBe(2);
        var dest = new float[Block * 2];
        ring.Read(dest, 2, out var discarded, time.GetTimestamp()).ShouldBe(Block);
        discarded.ShouldBe(4_800);
        dest[0].ShouldBe(2.4f, 0.0001f);
    }

    [Fact]
    public void A_decoder_exception_is_reported_once_as_a_fault_and_the_thread_ends_cleanly()
    {
        var time = new FakeTimeProvider();
        var session = Substitute.For<IDecodeSession>();
        session.Read().Returns(_ => throw new InvalidOperationException("boom"));
        var faults = new List<Exception>();
        var faulted = new ManualResetEventSlim();
        var ended = false;
        using var pump = NewPump(session, time, new AudioRingBuffer(960, 2));
        pump.Faulted += e =>
        {
            faults.Add(e);
            faulted.Set();
        };
        pump.Ended += () => ended = true;

        pump.Start(epoch: 1);

        faulted.Wait(Timeout, TestContext.Current.CancellationToken).ShouldBeTrue();
        Thread.Sleep(50);
        faults.Count.ShouldBe(1);
        ended.ShouldBeFalse();
    }

    /// <summary>Hands the pump one decoded block per <see cref="Release"/> permit, so a test can step the decoder.</summary>
    private sealed class SteppedSession(IDecodeSession inner) : IDecodeSession
    {
        private readonly SemaphoreSlim _gate = new(0);
        private int _reads;
        private bool _open;

        /// <summary>Read calls the pump has entered so far.</summary>
        public int Reads => Volatile.Read(ref _reads);

        public void Release(int blocks) => _gate.Release(blocks);

        /// <summary>Stops gating, so the pump can run to the end and shut down.</summary>
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
