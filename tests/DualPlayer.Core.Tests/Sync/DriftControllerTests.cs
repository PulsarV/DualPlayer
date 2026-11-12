using DualPlayer.Core.Sync;
using Microsoft.Extensions.Time.Testing;
using Shouldly;

namespace DualPlayer.Core.Tests.Sync;

public sealed class DriftControllerTests
{
    private static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(10);
    private const double ToleranceMs = 10;

    // Steady-state rate authority must stay within the ADR-0003 "few hundred ppm" range around the needed correction.
    private const double MaxSteadyRatioExcursionPpm = 50;

    /// <summary>
    /// A device whose clock runs (1 + ppm) fast, whose output is resampled by the controller's ratio and whose
    /// position readings carry deterministic pseudo-random jitter of up to ±<c>jitterMs</c>.
    /// Like ADR-0006, the device stream keeps running (playing silence) while paused.
    /// </summary>
    private sealed class Harness
    {
        private readonly FakeTimeProvider _time = new();
        private readonly MasterClock _clock;
        private readonly DriftMeter _meter;
        private readonly DriftController _controller = new();
        private readonly ClockMismatchEstimator _estimator;
        private readonly Random _random;
        private readonly double _deviceRate;
        private readonly double _devicePpm;
        private readonly double _jitterSeconds;
        private double _contentSeconds;
        private double _deviceSeconds;
        private bool _paused;

        public Harness(double devicePpm, double jitterMs = 1, int seed = 7)
        {
            _devicePpm = devicePpm;
            _deviceRate = 1 + (devicePpm * 1e-6);
            _jitterSeconds = jitterMs / 1000;
            _random = new Random(seed);
            _clock = new MasterClock(_time);
            _meter = new DriftMeter(_clock);
            _estimator = new ClockMismatchEstimator(_time);
            _clock.Start();
        }

        public DriftController Controller => _controller;

        public ClockMismatchEstimator Estimator => _estimator;

        public double DriftMs { get; private set; }

        public double MaxRatioExcursionPpm { get; private set; }

        public void Seek(TimeSpan position)
        {
            _clock.JumpTo(position);
            _contentSeconds = position.TotalSeconds;
            _controller.Reset();
        }

        public void Pause()
        {
            _clock.StopAt(_time.GetTimestamp());
            _paused = true;
        }

        public void Resume()
        {
            _clock.Start();
            _controller.Reset();
            _paused = false;
        }

        public double Run(TimeSpan duration, out double maxAbsMs)
        {
            maxAbsMs = 0;
            MaxRatioExcursionPpm = 0;
            for (var t = TimeSpan.Zero; t < duration; t += Tick)
            {
                _time.Advance(Tick);
                var deviceDt = Tick.TotalSeconds * _deviceRate;
                _deviceSeconds += deviceDt;
                var jitter = ((_random.NextDouble() * 2) - 1) * _jitterSeconds;
                var now = _time.GetTimestamp();
                _estimator.AddReading(now, TimeSpan.FromSeconds(_deviceSeconds + jitter));
                if (_paused)
                {
                    continue;
                }

                _contentSeconds += deviceDt * _controller.RateRatio;
                var drift = _meter.Record(TimeSpan.FromSeconds(_contentSeconds + jitter), now);
                _controller.Update(drift, Tick);
                DriftMs = (_contentSeconds + jitter - _clock.PositionAt(now).TotalSeconds) * 1000;
                maxAbsMs = Math.Max(maxAbsMs, Math.Abs(DriftMs));
                MaxRatioExcursionPpm = Math.Max(MaxRatioExcursionPpm, Math.Abs(_controller.CorrectionPpm - _devicePpm));
            }

            return DriftMs;
        }
    }

    private sealed class CountingReader : IFrameReader
    {
        public long Consumed { get; private set; }

        public int Channels => 1;

        public int Read(Span<float> destination)
        {
            destination.Clear();
            Consumed += destination.Length;
            return destination.Length;
        }
    }

    [Theory]
    [InlineData(300)]
    [InlineData(-300)]
    [InlineData(0)]
    public void A_device_running_fast_or_slow_stays_within_tolerance_in_steady_state(double ppm)
    {
        var h = new Harness(ppm);
        h.Run(TimeSpan.FromSeconds(60), out _); // settle

        h.Run(TimeSpan.FromMinutes(10), out var maxAbsMs);

        maxAbsMs.ShouldBeLessThanOrEqualTo(ToleranceMs);
    }

    [Theory]
    [InlineData(300)]
    [InlineData(-300)]
    [InlineData(0)]
    public void Measurement_jitter_keeps_the_steady_state_ratio_near_the_needed_correction(double ppm)
    {
        var h = new Harness(ppm, jitterMs: 1);
        h.Run(TimeSpan.FromSeconds(60), out _); // settle

        h.Run(TimeSpan.FromMinutes(10), out _);

        h.MaxRatioExcursionPpm.ShouldBeLessThanOrEqualTo(MaxSteadyRatioExcursionPpm);
    }

    [Fact]
    public void The_controller_output_is_a_near_unity_clamped_ratio()
    {
        var controller = new DriftController();

        for (var i = 0; i < 1000; i++)
        {
            controller.Update(TimeSpan.FromSeconds(5), Tick);
        }

        controller.RateRatio.ShouldBeLessThan(1.0);
        controller.RateRatio.ShouldBeGreaterThan(0.98);
        Math.Abs(controller.CorrectionPpm).ShouldBeLessThanOrEqualTo(DriftController.MaxCorrectionPpm);
        controller.CorrectionPpm.ShouldBe(DriftController.MaxCorrectionPpm);
        DriftController.MaxCorrectionPpm.ShouldBeLessThanOrEqualTo(1_000);
    }

    [Fact]
    public void A_fast_device_is_slowed_down_and_a_slow_device_is_sped_up()
    {
        var fast = new DriftController();
        var slow = new DriftController();

        fast.Update(TimeSpan.FromMilliseconds(5), Tick);
        slow.Update(TimeSpan.FromMilliseconds(-5), Tick);

        fast.RateRatio.ShouldBeLessThan(1.0);
        slow.RateRatio.ShouldBeGreaterThan(1.0);
    }

    [Fact]
    public void The_resampler_ratio_makes_the_resampler_consume_content_slower_when_content_is_ahead()
    {
        var controller = new DriftController();
        controller.Update(TimeSpan.FromMilliseconds(2), Tick);
        var resampler = new AdaptiveResampler(1);
        var reader = new CountingReader();
        var output = new float[480_000];

        var produced = resampler.Process(reader, output, controller.ResamplerRatio);

        controller.ResamplerRatio.ShouldBeGreaterThan(1.0);
        controller.ResamplerRatio.ShouldBe(1 / controller.RateRatio, 1e-12);
        produced.ShouldBe(output.Length);
        reader.Consumed.ShouldBeLessThan(produced);
        ((double)reader.Consumed / produced).ShouldBe(controller.RateRatio, 1e-5);
    }

    [Fact]
    public void The_resampler_realign_frames_move_content_back_when_content_is_ahead()
    {
        const int sampleRate = 48_000;
        var frames = DriftController.ResamplerRealignFrames(TimeSpan.FromMilliseconds(10), sampleRate);
        var plain = new AdaptiveResampler(1);
        var realigned = new AdaptiveResampler(1);
        var plainReader = new CountingReader();
        var realignedReader = new CountingReader();
        realigned.Realign(frames);

        plain.Process(plainReader, new float[sampleRate], 1.0);
        realigned.Process(realignedReader, new float[sampleRate], 1.0);

        frames.ShouldBe(-480);
        (plainReader.Consumed - realignedReader.Consumed).ShouldBe(480);
    }

    [Fact]
    public void Reset_returns_the_controller_to_unity()
    {
        var controller = new DriftController();
        controller.Update(TimeSpan.FromMilliseconds(8), Tick);

        controller.Reset();

        controller.RateRatio.ShouldBe(1.0);
        controller.CorrectionPpm.ShouldBe(0);
    }

    [Theory]
    [InlineData(300)]
    [InlineData(-300)]
    public void The_mismatch_estimate_matches_the_configured_device_ppm(double ppm)
    {
        var h = new Harness(ppm);

        h.Run(TimeSpan.FromMinutes(5), out _);

        h.Estimator.EstimatedPpm.ShouldBe(ppm, 20);
    }

    [Theory]
    [InlineData(300)]
    [InlineData(-300)]
    public void The_mismatch_estimate_survives_pause_and_seek(double ppm)
    {
        var h = new Harness(ppm);
        h.Run(TimeSpan.FromMinutes(2), out _);
        h.Pause();
        h.Run(TimeSpan.FromSeconds(20), out _);
        h.Resume();
        h.Run(TimeSpan.FromMinutes(1), out _);
        h.Seek(TimeSpan.FromMinutes(42));
        h.Run(TimeSpan.FromMinutes(1), out _);
        h.Seek(TimeSpan.FromSeconds(5));

        h.Run(TimeSpan.FromMinutes(1), out _);

        h.Estimator.EstimatedPpm.ShouldBe(ppm, 20);
    }
}
