using DualPlayer.Core.Sync;
using Microsoft.Extensions.Time.Testing;
using Shouldly;

namespace DualPlayer.Core.Tests.Sync;

public sealed class ClockMismatchEstimatorTests
{
    private static readonly TimeSpan Sample = TimeSpan.FromMilliseconds(16);

    private readonly FakeTimeProvider _time = new();

    // Measured on the target PC: a Bluetooth headset reports position 0 for ~0.6–1 s after start, then advances at
    // the true rate. The stall is start-up latency, not a clock-rate difference.
    [Theory]
    [InlineData(0)]
    [InlineData(200)]
    [InlineData(-150)]
    public void A_device_whose_position_stalls_at_start_reports_its_true_rate_not_the_stall(double devicePpm)
    {
        var estimator = new ClockMismatchEstimator(_time);
        var stall = TimeSpan.FromMilliseconds(980);
        var run = TimeSpan.FromMinutes(3);
        var elapsed = TimeSpan.Zero;
        var device = TimeSpan.Zero;

        while (elapsed < run)
        {
            estimator.AddReading(_time.GetTimestamp(), device);
            _time.Advance(Sample);
            elapsed += Sample;
            if (elapsed > stall)
            {
                device += Sample * (1 + (devicePpm / 1e6));
            }
        }

        estimator.EstimatedPpm.ShouldBe(devicePpm, 1);
    }

    [Fact]
    public void A_device_that_never_moves_reports_no_estimate()
    {
        var estimator = new ClockMismatchEstimator(_time);

        for (var i = 0; i < 100; i++)
        {
            estimator.AddReading(_time.GetTimestamp(), TimeSpan.Zero);
            _time.Advance(Sample);
        }

        estimator.EstimatedPpm.ShouldBe(0);
    }
}
