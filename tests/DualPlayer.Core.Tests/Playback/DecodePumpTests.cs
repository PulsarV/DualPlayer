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

    [Fact]
    public void A_full_ring_drops_only_its_own_frames_while_another_ring_has_room()
    {
        var time = new FakeTimeProvider();
        var small = new AudioRingBuffer(Block, 2);
        var large = new AudioRingBuffer(10_000, 2);
        var ended = new ManualResetEventSlim();
        using var pump = NewPump(OpenSession(4_800), time, small, large);
        pump.Ended += ended.Set;

        pump.Start(epoch: 1);

        ended.Wait(Timeout, TestContext.Current.CancellationToken).ShouldBeTrue();
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

            // Before the threshold the idle output still paces: its free room lets the decoder run ahead,
            // so the third block for the full healthy ring is dropped.
            session.Release(6);
            WaitFor(() => session.Reads == 7).ShouldBeTrue();
            idle.FramesAvailable.ShouldBe(1_440);
            healthy.FramesAvailable.ShouldBe(960);

            // The idle output has not read for over a second; the healthy one keeps reading.
            time.Advance(DecodePump.StallThreshold + TimeSpan.FromMilliseconds(100));
            var dest = new float[960 * 2];
            healthy.Read(dest, 1, out _, time.GetTimestamp()).ShouldBe(960);
            pump.IsStalled(0).ShouldBeTrue();
            pump.IsStalled(1).ShouldBeFalse();

            // Now the stalled ring's room no longer lets the decoder run ahead: the pump waits for the healthy ring.
            session.Release(6);
            WaitFor(() => session.Reads == 12).ShouldBeTrue();
            Thread.Sleep(100);
            session.Reads.ShouldBe(12);
            healthy.FramesAvailable.ShouldBe(960);

            healthy.Read(dest.AsSpan(0, Block * 2), 1, out _, time.GetTimestamp()).ShouldBe(Block);
            time.Advance(TimeSpan.FromMilliseconds(10));
            WaitFor(() => session.Reads == 13).ShouldBeTrue();

            healthy.Read(dest, 1, out var discarded, time.GetTimestamp()).ShouldBe(960);
            discarded.ShouldBe(0);
            dest[0].ShouldBe(1.92f, 0.0001f);
            dest[959 * 2].ShouldBe(2.879f, 0.0001f);
        }
        finally
        {
            session.Open();
        }
    }

    [Fact]
    public void A_stalled_output_that_reads_again_is_flagged_once_for_re_alignment()
    {
        var time = new FakeTimeProvider();
        var idle = new AudioRingBuffer(960, 2);
        var other = new AudioRingBuffer(960, 2);
        using var pump = NewPump(OpenSession(480_000), time, idle, other);
        pump.Start(epoch: 1);
        WaitFor(() => idle.FreeFrames == 0 && other.FreeFrames == 0).ShouldBeTrue();

        for (var i = 0; i < 110; i++)
        {
            time.Advance(TimeSpan.FromMilliseconds(10));
            Thread.Sleep(1);
        }

        Thread.Sleep(20);
        time.Advance(TimeSpan.FromMilliseconds(10));
        Thread.Sleep(20);
        pump.TryConsumeResumedFromStall(0).ShouldBeFalse();

        var dest = new float[Block * 2];
        idle.Read(dest, 1, out _, time.GetTimestamp());

        pump.TryConsumeResumedFromStall(0).ShouldBeTrue();
        pump.TryConsumeResumedFromStall(0).ShouldBeFalse();
        pump.TryConsumeResumedFromStall(1).ShouldBeFalse();
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
