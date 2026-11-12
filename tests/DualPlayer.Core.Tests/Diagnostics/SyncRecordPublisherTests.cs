using DualPlayer.Core.Diagnostics;
using Microsoft.Extensions.Logging;
using Shouldly;

namespace DualPlayer.Core.Tests.Diagnostics;

public sealed class SyncRecordPublisherTests
{
    private static TimeSpan At(int s) => TimeSpan.FromSeconds(s);

    private static OutputSample Sample(string id, double driftMs = 0, double ppm = 0, long underruns = 0, bool recovery = false) =>
        new(id, TimeSpan.FromMilliseconds(driftMs), ppm, underruns, recovery);

    private sealed class Rig
    {
        public List<EngineEvent> Events { get; } = [];

        public CapturingLogger Log { get; } = new();

        public SyncRecordPublisher Publisher { get; }

        public Rig() => Publisher = new SyncRecordPublisher(Events.Add, Log);

        public List<SyncRecord> Records => [.. Events.OfType<SyncRecord>()];
    }

    [Fact]
    public void Each_second_publishes_one_record_per_device_with_device_id_and_clock_position()
    {
        var rig = new Rig();

        rig.Publisher.Publish(At(1), [Sample("dev-A", 2.5, 120, 3), Sample("dev-B", -1.0, -80, 0)]);

        rig.Records.ShouldBe(
        [
            new SyncRecord("dev-A", At(1), 2.5, 120, 3),
            new SyncRecord("dev-B", At(1), -1.0, -80, 0),
        ]);
    }

    [Fact]
    public void Every_record_is_also_written_to_the_log_as_a_structured_entry()
    {
        var rig = new Rig();

        rig.Publisher.Publish(At(1), [Sample("dev-A", 2.5, 120, 3), Sample("dev-B")]);

        var entries = rig.Log.Entries.Where(e => e.ContainsKey("DeviceId") && e.ContainsKey("DriftMs")).ToList();
        entries.Count.ShouldBe(2);
        entries[0]["DeviceId"].ShouldBe("dev-A");
        entries[0]["DriftMs"].ShouldBe(2.5);
        entries[0]["CorrectionPpm"].ShouldBe(120.0);
        entries[0]["Underruns"].ShouldBe(3L);
        entries[0].ContainsKey("ClockPosition").ShouldBeTrue();
    }

    [Fact]
    public void A_lost_device_stops_getting_records_while_the_others_continue()
    {
        var rig = new Rig();
        rig.Publisher.Publish(At(1), [Sample("dev-A"), Sample("dev-B")]);

        rig.Publisher.OutputLost("dev-A", At(1));
        rig.Publisher.Publish(At(2), [Sample("dev-B")]);

        rig.Records.Where(r => r.DeviceId == "dev-A").Select(r => r.ClockPosition).ShouldBe([At(1)]);
        rig.Records.Where(r => r.DeviceId == "dev-B").Select(r => r.ClockPosition).ShouldBe([At(1), At(2)]);
    }

    [Fact]
    public void An_excursion_is_published_once_when_the_output_is_back_within_tolerance()
    {
        var rig = new Rig();

        rig.Publisher.Publish(At(1), [Sample("dev-A", 1)]);
        rig.Publisher.Publish(At(2), [Sample("dev-A", 15)]);
        rig.Publisher.Publish(At(3), [Sample("dev-A", 22)]);
        rig.Events.OfType<Excursion>().ShouldBeEmpty();
        rig.Publisher.Publish(At(4), [Sample("dev-A", 2)]);
        rig.Publisher.Publish(At(5), [Sample("dev-A", 1)]);

        rig.Events.OfType<Excursion>().ShouldBe([new Excursion("dev-A", At(2), At(2), 22)]);
    }

    [Fact]
    public void Drift_inside_a_recovery_window_is_not_an_excursion()
    {
        var rig = new Rig();

        rig.Publisher.Publish(At(1), [Sample("dev-A", 80, recovery: true)]);
        rig.Publisher.Publish(At(2), [Sample("dev-A", 3)]);

        rig.Events.OfType<Excursion>().ShouldBeEmpty();
    }

    [Fact]
    public void Finishing_the_run_closes_an_open_excursion()
    {
        var rig = new Rig();
        rig.Publisher.Publish(At(1), [Sample("dev-A", 30)]);

        rig.Publisher.Finish(At(3));

        rig.Events.OfType<Excursion>().ShouldBe([new Excursion("dev-A", At(1), At(2), 30)]);
    }

    private sealed class CapturingLogger : ILogger<SyncRecordPublisher>
    {
        private readonly List<IReadOnlyDictionary<string, object?>> _entries = [];

        public IReadOnlyList<IReadOnlyDictionary<string, object?>> Entries => _entries;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            _entries.Add(state is IEnumerable<KeyValuePair<string, object?>> pairs
                ? pairs.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal)
                : []);
    }
}
