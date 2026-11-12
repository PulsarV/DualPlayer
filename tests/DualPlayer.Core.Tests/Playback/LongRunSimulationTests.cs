using DualPlayer.Core.Diagnostics;
using DualPlayer.Core.Sync;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Shouldly;

namespace DualPlayer.Core.Tests.Playback;

public sealed class LongRunSimulationTests
{
    private static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(10);
    private const double ToleranceMs = 10;

    /// <summary>
    /// A fake output whose clock runs (1 + ppm) fast, kept on the master clock by the engine's per-output sync
    /// logic: a drift reading every render period, the record and the one-step re-alignment decision once per second.
    /// </summary>
    private sealed class FakeOutput(string id, double ppm, MasterClock clock)
    {
        private readonly OutputSync _sync = new(clock, TimeSpan.FromMilliseconds(ToleranceMs));
        private double _contentSeconds;

        public string Id { get; } = id;

        public int Corrections { get; private set; }

        public void Step(FakeTimeProvider time)
        {
            _contentSeconds += Tick.TotalSeconds * (1 + (ppm * 1e-6)) / _sync.ResamplerRatio;
            _sync.AddReading(TimeSpan.FromSeconds(_contentSeconds), time.GetTimestamp(), Tick, correct: true);
        }

        public void InjectStep(double ms) => _contentSeconds += ms / 1000;

        public OutputSample Snapshot(bool recovery)
        {
            var interval = _sync.CloseInterval(mayRealign: !recovery);
            if (interval.Realignment != TimeSpan.Zero)
            {
                _contentSeconds -= interval.Realignment.TotalSeconds;
                Corrections++;
            }

            return new(Id, interval.AverageDrift, _sync.CorrectionPpm, 0, recovery);
        }
    }

    private static (List<EngineEvent> Events, List<FakeOutput> Outputs, int Seconds) Run(
        TimeSpan duration, double[] ppms, Action<int, List<FakeOutput>>? atSecond = null)
    {
        var time = new FakeTimeProvider();
        var clock = new MasterClock(time);
        clock.Start();
        var events = new List<EngineEvent>();
        var publisher = new SyncRecordPublisher(events.Add, NullLogger<SyncRecordPublisher>.Instance);
        var outputs = ppms.Select((p, i) => new FakeOutput($"dev-{i}", p, clock)).ToList();
        var seconds = (int)duration.TotalSeconds;
        for (var s = 1; s <= seconds; s++)
        {
            for (var t = 0; t < 100; t++)
            {
                time.Advance(Tick);
                outputs.ForEach(o => o.Step(time));
            }

            atSecond?.Invoke(s, outputs);
            publisher.Publish(clock.PositionAt(time.GetTimestamp()), [.. outputs.Select(o => o.Snapshot(recovery: s == 1))]);
        }

        publisher.Finish(clock.PositionAt(time.GetTimestamp()));
        return (events, outputs, seconds);
    }

    [Fact]
    public void Three_outputs_at_different_clock_speeds_stay_within_tolerance_for_three_hours_with_no_corrections()
    {
        var (events, outputs, seconds) = Run(TimeSpan.FromHours(3), [300, -250, 40]);

        var records = events.OfType<SyncRecord>().ToList();
        records.Count.ShouldBe(3 * seconds);
        records.Where(r => r.ClockPosition > TimeSpan.FromSeconds(1))
            .Max(r => Math.Abs(r.DriftMs)).ShouldBeLessThanOrEqualTo(ToleranceMs);
        events.OfType<Excursion>().ShouldBeEmpty();
        outputs.Sum(o => o.Corrections).ShouldBe(0);
        records.Select(r => r.DeviceId).Distinct().Count().ShouldBe(3);
    }

    [Fact]
    public void An_injected_step_error_is_re_aligned_in_one_step_counted_once_and_recorded_as_one_excursion()
    {
        var (events, outputs, _) = Run(
            TimeSpan.FromMinutes(5),
            [100, -100],
            (s, outs) =>
            {
                if (s == 120)
                {
                    outs[0].InjectStep(60);
                }
            });

        outputs[0].Corrections.ShouldBe(1);
        outputs[1].Corrections.ShouldBe(0);
        var excursions = events.OfType<Excursion>().ToList();
        excursions.Count.ShouldBe(1);
        excursions[0].DeviceId.ShouldBe("dev-0");
        excursions[0].PeakMs.ShouldBeGreaterThan(ToleranceMs);
        excursions[0].Start.ShouldBeGreaterThanOrEqualTo(TimeSpan.FromSeconds(119));
        excursions[0].Start.ShouldBeLessThanOrEqualTo(TimeSpan.FromSeconds(121));
    }
}
