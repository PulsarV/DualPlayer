using DualPlayer.Core.Diagnostics;
using DualPlayer.Core.Outputs;
using DualPlayer.Core.Playback;
using DualPlayer.Core.Tests.Fakes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Shouldly;

namespace DualPlayer.Core.Tests.Playback;

/// <summary>The engine publishes sync records, excursions and corrections from its control loop (AC-07, AC-11).</summary>
public sealed class AudioEngineSyncRecordTests
{
    private static readonly MixFormat Stereo = new(48_000, 2, ChannelLayout.Stereo);
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan Step = TimeSpan.FromMilliseconds(10);

    private static readonly IReadOnlyList<AudioTrackInfo> Tracks =
    [
        new AudioTrackInfo(1, "eng", ChannelLayout.Stereo, 48_000),
        new AudioTrackInfo(2, "ukr", ChannelLayout.Stereo, 48_000),
    ];

    private static OutputAssignment Assign(int track, int deviceNumber) => new(track, DeviceRef.ByNumber(deviceNumber));

    /// <summary>Fake devices whose clocks run at the given ppm offsets; device n is "dev-n".</summary>
    private sealed class Rig : IDisposable
    {
        private readonly List<EngineEvent> _events = [];
        private readonly Lock _gate = new();
        private readonly IDisposable _subscription;

        public Rig(params double[] ppms)
        {
            Time = new FakeTimeProvider();
            Backend = new PpmBackend(Time, ppms);
            Engine = new AudioEngine(Backend, new FakeAudioDecoder(Tracks, 48_000 * 120), Time, Log);
            _subscription = Engine.Events.Subscribe(new Collector(this));
        }

        public FakeTimeProvider Time { get; }

        public PpmBackend Backend { get; }

        public CapturingLogger Log { get; } = new();

        public AudioEngine Engine { get; }

        public IReadOnlyList<EngineEvent> Events
        {
            get
            {
                lock (_gate)
                {
                    return [.. _events];
                }
            }
        }

        public IReadOnlyList<SyncRecord> Records => [.. Events.OfType<SyncRecord>()];

        public IReadOnlyList<SyncRecord> RecordsOf(string deviceId) => [.. Records.Where(r => r.DeviceId == deviceId)];

        public RunSummary? Summary => Events.OfType<RunSummary>().FirstOrDefault();

        public void Start(int outputs)
        {
            var assignments = Enumerable.Range(1, outputs).Select(n => Assign(((n - 1) % 2) + 1, n)).ToArray();
            var started = Engine.StartRun("film.mkv", assignments, new EngineOptions());
            started.IsSuccess.ShouldBeTrue(started.Error?.Message);
        }

        /// <summary>Advances the fake clock by <paramref name="steps"/> x 10 ms, asking every open device for 10 ms of audio each step.</summary>
        public void Render(int steps)
        {
            for (var i = 0; i < steps; i++)
            {
                Time.Advance(Step);
                foreach (var device in Backend.Devices.Values)
                {
                    device.PumpRender(480);
                }

                Thread.Sleep(1);
            }
        }

        public bool WaitFor(Func<bool> condition)
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

        public void Dispose()
        {
            _subscription.Dispose();
            Engine.Dispose();
        }

        private sealed class Collector(Rig rig) : IObserver<EngineEvent>
        {
            public void OnCompleted()
            {
            }

            public void OnError(Exception error)
            {
            }

            public void OnNext(EngineEvent value)
            {
                lock (rig._gate)
                {
                    rig._events.Add(value);
                }
            }
        }
    }

    private sealed class PpmBackend(TimeProvider time, double[] ppms) : IAudioBackend
    {
        public Dictionary<string, SteppableDevice> Devices { get; } = [];

        public IReadOnlyList<AudioDeviceInfo> ListDevices() =>
            [.. ppms.Select((_, i) => new AudioDeviceInfo($"Device {i + 1}", null, $"dev-{i + 1}"))];

        public Result<IAudioDevice> Open(string deviceId, TimeSpan requestedBuffer, IRenderSource source)
        {
            var index = int.Parse(deviceId["dev-".Length..], System.Globalization.CultureInfo.InvariantCulture) - 1;
            var device = new SteppableDevice(time, requestedBuffer, source, ppms[index]);
            Devices[deviceId] = device;
            return Result<IAudioDevice>.Success(device);
        }
    }

    /// <summary>A fake device whose clock runs <paramref name="ppm"/> fast and whose reported position can step.</summary>
    private sealed class SteppableDevice(TimeProvider time, TimeSpan buffer, IRenderSource source, double ppm) : IAudioDevice
    {
        private long _startTimestamp;
        private long _positionStep;
        private bool _started;
        private bool _gone;

        public MixFormat MixFormat => Stereo;

        public TimeSpan GrantedBuffer => buffer;

        public event EventHandler? DeviceLost;

        public void Start()
        {
            _startTimestamp = time.GetTimestamp();
            _started = true;
        }

        public void Close() => _gone = true;

        public void SimulateLoss()
        {
            _gone = true;
            DeviceLost?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>From now on the device reports <paramref name="frames"/> more played frames (a step error).</summary>
        public void StepPosition(long frames) => _positionStep += frames;

        public void PumpRender(int frames)
        {
            if (!_started || _gone)
            {
                return;
            }

            var now = time.GetTimestamp();
            var elapsed = time.GetElapsedTime(_startTimestamp, now).TotalSeconds;
            var played = (long)Math.Round(elapsed * Stereo.SampleRate * (1 + (ppm * 1e-6))) + _positionStep;
            source.Render(new float[frames * Stereo.Channels], new DevicePosition(played, now));
        }
    }

    /// <summary>Keeps every log entry with its structured values.</summary>
    private sealed class CapturingLogger : ILogger<AudioEngine>
    {
        private readonly List<IReadOnlyDictionary<string, object?>> _entries = [];
        private readonly Lock _gate = new();

        public IReadOnlyList<IReadOnlyDictionary<string, object?>> With(params string[] keys)
        {
            lock (_gate)
            {
                return [.. _entries.Where(e => keys.All(e.ContainsKey))];
            }
        }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var values = state is IEnumerable<KeyValuePair<string, object?>> pairs
                ? pairs.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal)
                : [];
            lock (_gate)
            {
                _entries.Add(values);
            }
        }
    }

    [Fact]
    public void Every_second_each_device_gets_one_sync_record_with_its_id_and_the_clock_position_also_in_the_log()
    {
        using var rig = new Rig(300, -250, 40);
        rig.Start(3);

        rig.Render(500);

        rig.WaitFor(() => rig.Records.Count == 15).ShouldBeTrue($"records: {rig.Records.Count}");
        foreach (var id in new[] { "dev-1", "dev-2", "dev-3" })
        {
            var positions = rig.RecordsOf(id).Select(r => r.ClockPosition).ToList();
            positions.Count.ShouldBe(5);
            for (var s = 0; s < positions.Count; s++)
            {
                positions[s].ShouldBeGreaterThanOrEqualTo(TimeSpan.FromSeconds(s + 1));
                positions[s].ShouldBeLessThan(TimeSpan.FromSeconds(s + 1.5));
            }
        }

        rig.Log.With("DeviceId", "ClockPosition", "DriftMs", "CorrectionPpm", "Underruns").Count.ShouldBe(15);
        rig.Engine.Stop(RunEndReason.Stop);
    }

    [Fact]
    public void A_lost_device_stops_getting_records_while_the_others_continue_every_second()
    {
        using var rig = new Rig(100, -100, 0);
        rig.Start(3);
        rig.Render(300);

        rig.Backend.Devices["dev-2"].SimulateLoss();
        rig.Render(300);

        rig.WaitFor(() => rig.RecordsOf("dev-1").Count == 6 && rig.RecordsOf("dev-3").Count == 6).ShouldBeTrue();
        rig.RecordsOf("dev-2").Count.ShouldBe(3);
        rig.Engine.Stop(RunEndReason.Stop);
    }

    [Fact]
    public void Outputs_at_different_clock_speeds_stay_within_tolerance_with_no_corrections_and_the_summary_shows_it()
    {
        using var rig = new Rig(300, -250, 40);
        rig.Start(3);

        rig.Render(2000);
        rig.WaitFor(() => rig.Records.Count == 60).ShouldBeTrue($"records: {rig.Records.Count}");
        rig.Engine.Stop(RunEndReason.Stop);

        rig.Records.Where(r => r.ClockPosition > TimeSpan.FromSeconds(2)).Max(r => Math.Abs(r.DriftMs)).ShouldBeLessThanOrEqualTo(10);
        rig.Events.OfType<Excursion>().ShouldBeEmpty();
        rig.RecordsOf("dev-1")[^1].CorrectionPpm.ShouldBeGreaterThan(200); // the rate correction follows the fast clock
        rig.RecordsOf("dev-2")[^1].CorrectionPpm.ShouldBeLessThan(-150);
        var summary = rig.Summary.ShouldNotBeNull();
        summary.Outputs.ShouldAllBe(o => o.Corrections == 0);
        summary.Outputs.ShouldAllBe(o => o.MaxDriftMs <= 10);
        summary.Outputs.Single(o => o.DeviceId == "dev-1").ClockMismatchPpm.ShouldBe(300, 20);
        summary.Outputs.Single(o => o.DeviceId == "dev-2").ClockMismatchPpm.ShouldBe(-250, 20);
    }

    [Fact]
    public void An_injected_step_error_is_re_aligned_in_one_step_counted_once_and_recorded_as_one_excursion()
    {
        using var rig = new Rig(0, 0);
        rig.Start(2);
        rig.Render(450);

        rig.Backend.Devices["dev-1"].StepPosition(2880); // 60 ms
        rig.Render(600);
        rig.WaitFor(() => rig.Records.Count == 2 * 10).ShouldBeTrue($"records: {rig.Records.Count}");
        rig.Engine.Stop(RunEndReason.Stop);

        var summary = rig.Summary.ShouldNotBeNull();
        summary.Outputs.Single(o => o.DeviceId == "dev-1").Corrections.ShouldBe(1);
        summary.Outputs.Single(o => o.DeviceId == "dev-2").Corrections.ShouldBe(0);
        var excursion = rig.Events.OfType<Excursion>().ShouldHaveSingleItem();
        excursion.DeviceId.ShouldBe("dev-1");
        excursion.PeakMs.ShouldBeGreaterThan(10);
        rig.RecordsOf("dev-1")[^1].DriftMs.ShouldBe(0, 2);
    }

#pragma warning disable xUnit1031 // the fake clock is driven from this thread, so the seek has to be awaited synchronously
    [Theory]
    [InlineData(60)]
    [InlineData(1)]
    public void An_excursion_open_when_a_seek_starts_is_closed_at_its_pre_seek_end_with_a_non_negative_duration(int targetSeconds)
    {
        using var rig = new Rig(0, 0);
        rig.Start(2);
        rig.Render(450);
        rig.Backend.Devices["dev-1"].StepPosition(2880); // 60 ms: the next record opens an excursion
        for (var i = 0; i < 300 && !rig.RecordsOf("dev-1").Any(r => Math.Abs(r.DriftMs) > 10); i++)
        {
            rig.Render(1);
        }

        rig.RecordsOf("dev-1").Any(r => Math.Abs(r.DriftMs) > 10).ShouldBeTrue("the step never showed as drift");
        var preSeek = rig.RecordsOf("dev-1")[^1].ClockPosition;

        var seek = Task.Run(() => rig.Engine.Seek(TimeSpan.FromSeconds(targetSeconds)), TestContext.Current.CancellationToken);
        for (var i = 0; i < 3_000 && !seek.IsCompleted; i++)
        {
            rig.Render(1);
        }

        seek.IsCompleted.ShouldBeTrue();
        rig.Render(500);
        rig.Engine.Stop(RunEndReason.Stop);

        var excursion = rig.Events.OfType<Excursion>().First(e => e.DeviceId == "dev-1");
        excursion.Duration.ShouldBeGreaterThanOrEqualTo(TimeSpan.Zero);
        excursion.Start.ShouldBeLessThanOrEqualTo(preSeek);
        (excursion.Start + excursion.Duration).ShouldBeLessThanOrEqualTo(preSeek + TimeSpan.FromSeconds(1), "closed at its pre-seek end, not after the seek");
        excursion.Duration.ShouldBeLessThanOrEqualTo(TimeSpan.FromSeconds(3));
    }

#pragma warning restore xUnit1031

    [Fact]
    public void A_sample_taken_before_the_first_render_does_not_skew_the_clock_mismatch_estimate()
    {
        using var rig = new Rig(200, 0);
        rig.Start(2);
        for (var i = 0; i < 30; i++)
        {
            rig.Time.Advance(Step); // control loop samples while no device has rendered yet
            Thread.Sleep(1);
        }

        rig.Render(2000);
        rig.Engine.Stop(RunEndReason.Stop);

        var summary = rig.Summary.ShouldNotBeNull();
        summary.Outputs.Single(o => o.DeviceId == "dev-1").ClockMismatchPpm.ShouldBe(200, 50);
        summary.Outputs.Single(o => o.DeviceId == "dev-2").ClockMismatchPpm.ShouldBe(0, 50);
    }
}
