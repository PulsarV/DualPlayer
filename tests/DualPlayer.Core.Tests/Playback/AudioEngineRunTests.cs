using DualPlayer.Core.Diagnostics;
using DualPlayer.Core.Outputs;
using DualPlayer.Core.Playback;
using DualPlayer.Core.Tests.Fakes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace DualPlayer.Core.Tests.Playback;

public sealed class AudioEngineRunTests
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

    private sealed class Rig : IDisposable
    {
        private readonly FakeAudioBackend _inner;
        private readonly List<EngineEvent> _events = [];
        private readonly Lock _gate = new();
        private readonly IDisposable _subscription;

        public Rig(int devices, long totalFrames = 48_000 * 30, string? denied = null, IAudioDecoder? decoder = null)
        {
            Time = new FakeTimeProvider();
            _inner = new FakeAudioBackend(Time, Stereo);
            for (var i = 1; i <= devices; i++)
            {
                _inner.AddDevice(new AudioDeviceInfo($"Device {i}", null, $"dev-{i}"));
            }

            if (denied is not null)
            {
                _inner.DenyAccess(denied);
            }

            Backend = new RecordingBackend(_inner);
            Engine = new AudioEngine(Backend, decoder ?? new FakeAudioDecoder(Tracks, totalFrames), Time, Log);
            _subscription = Engine.Events.Subscribe(new Collector(this));
        }

        public FakeTimeProvider Time { get; }

        public RecordingBackend Backend { get; }

        public CapturingLogger Log { get; } = new();

        public IAudioEngine Engine { get; }

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

        public RunSummary? Summary => Events.OfType<RunSummary>().FirstOrDefault();

        /// <summary>Advances the fake clock by <paramref name="steps"/> x 10 ms, asking every open device for 10 ms of audio each step.</summary>
        public void Render(int steps)
        {
            for (var i = 0; i < steps; i++)
            {
                Time.Advance(Step);
                foreach (var d in Backend.Devices.Values)
                {
                    d.PumpRender(480);
                }

                Thread.Sleep(1);
            }
        }

        /// <summary>
        /// Stops the run from its own thread while rendering on, since a stop waits until every device has played past the
        /// stop instant and the fake devices only advance when rendered.
        /// </summary>
        public void Stop(RunEndReason reason)
        {
            var task = Task.Factory.StartNew(
                () => Engine.Stop(reason),
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
            for (var i = 0; i < 3_000 && !task.IsCompleted; i++)
            {
                Render(1);
            }

            task.Wait(TimeSpan.FromSeconds(10)).ShouldBeTrue("the stop did not complete");
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
            (Engine as IDisposable)?.Dispose();
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

    /// <summary>Keeps every log entry with its structured values.</summary>
    private sealed class CapturingLogger : ILogger<AudioEngine>
    {
        private readonly List<IReadOnlyDictionary<string, object?>> _entries = [];
        private readonly Lock _gate = new();

        public IReadOnlyList<IReadOnlyDictionary<string, object?>> Entries
        {
            get
            {
                lock (_gate)
                {
                    return [.. _entries];
                }
            }
        }

        /// <summary>Entries that carry every one of <paramref name="keys"/>.</summary>
        public IReadOnlyList<IReadOnlyDictionary<string, object?>> With(params string[] keys) =>
            [.. Entries.Where(e => keys.All(e.ContainsKey))];

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

    /// <summary>Decoder whose session fails on its first read with <paramref name="error"/>.</summary>
    private static IAudioDecoder FailingDecoder(Error error)
    {
        var session = Substitute.For<IDecodeSession>();
        session.Read().Returns(Result<DecodedBlock>.Failure(error));
        return DecoderWith(session);
    }

    /// <summary>Decoder whose session never delivers a frame, so no ring ever reaches its start level.</summary>
    private static IAudioDecoder StalledDecoder()
    {
        var session = Substitute.For<IDecodeSession>();
        session.Read().Returns(_ =>
        {
            Thread.Sleep(1);
            return Result<DecodedBlock>.Success(new DecodedBlock(0, 0, 0, []));
        });
        return DecoderWith(session);
    }

    private static IAudioDecoder DecoderWith(IDecodeSession session)
    {
        var decoder = Substitute.For<IAudioDecoder>();
        decoder.ListTracks(Arg.Any<string>()).Returns(Result<IReadOnlyList<AudioTrackInfo>>.Success(Tracks));
        decoder.Open(Arg.Any<string>(), Arg.Any<IReadOnlyList<DecodeRequest>>()).Returns(Result<IDecodeSession>.Success(session));
        return decoder;
    }

    private sealed class RecordingBackend(FakeAudioBackend inner) : IAudioBackend
    {
        public Dictionary<string, FakeAudioDevice> Devices { get; } = [];

        public int OpenCalls { get; private set; }

        public IReadOnlyList<AudioDeviceInfo> ListDevices() => inner.ListDevices();

        public Result<IAudioDevice> Open(string deviceId, TimeSpan requestedBuffer, IRenderSource source)
        {
            OpenCalls++;
            var result = inner.Open(deviceId, requestedBuffer, source);
            if (result.IsSuccess)
            {
                Devices[deviceId] = (FakeAudioDevice)result.Value;
            }

            return result;
        }
    }

    [Fact]
    public void Starting_two_outputs_opens_both_devices_and_starts_one_clock_from_the_file_beginning()
    {
        using var rig = new Rig(2);

        var result = rig.Engine.StartRun("film.mkv", [Assign(1, 1), Assign(2, 2)], new EngineOptions());

        result.IsSuccess.ShouldBeTrue(result.Error?.Message);
        result.Value.Outputs.Select(o => o.DeviceId).ShouldBe(["dev-1", "dev-2"]);
        result.Value.Outputs.Select(o => o.TrackNumber).ShouldBe([1, 2]);
        result.Value.ClockPosition.ShouldBe(TimeSpan.Zero);
        rig.Backend.Devices.Values.ShouldAllBe(d => d.IsStarted);
        rig.Stop(RunEndReason.Stop);
    }

    [Fact]
    public void Starting_three_outputs_starts_all_of_them_from_the_same_clock_position()
    {
        using var rig = new Rig(3);

        var result = rig.Engine.StartRun("film.mkv", [Assign(1, 1), Assign(2, 2), Assign(1, 3)], new EngineOptions());

        result.IsSuccess.ShouldBeTrue(result.Error?.Message);
        result.Value.Outputs.Count.ShouldBe(3);
        result.Value.ClockPosition.ShouldBe(TimeSpan.Zero);
        rig.Backend.Devices.Values.ShouldAllBe(d => d.IsStarted);
        rig.Stop(RunEndReason.Stop);
    }

    [Fact]
    public void An_invalid_assignment_is_refused_before_any_device_is_opened()
    {
        using var rig = new Rig(2);

        var result = rig.Engine.StartRun("film.mkv", [Assign(1, 1), Assign(9, 2)], new EngineOptions());

        result.IsSuccess.ShouldBeFalse();
        result.Error.Code.ShouldBe("playback.track_not_found");
        rig.Backend.OpenCalls.ShouldBe(0);
    }

    [Fact]
    public void A_device_that_denies_access_stops_the_start_closes_the_others_and_is_named()
    {
        using var rig = new Rig(2, denied: "dev-2");

        var result = rig.Engine.StartRun("film.mkv", [Assign(1, 1), Assign(2, 2)], new EngineOptions());

        result.IsSuccess.ShouldBeFalse();
        result.Error.Code.ShouldBe("outputs.device_access_denied");
        result.Error.Message.ShouldContain("Device 2");
        rig.Backend.Devices["dev-1"].IsClosed.ShouldBeTrue();
        rig.Backend.Devices.Values.ShouldAllBe(d => !d.IsStarted);
    }

    [Fact]
    public void A_lost_device_stops_only_its_output_and_the_survivor_keeps_playing_without_underruns()
    {
        using var rig = new Rig(2);
        rig.Engine.StartRun("film.mkv", [Assign(1, 1), Assign(2, 2)], new EngineOptions()).IsSuccess.ShouldBeTrue();
        rig.Render(500);

        rig.Backend.Devices["dev-2"].SimulateLoss();

        rig.WaitFor(() => rig.Events.OfType<DeviceLost>().Any()).ShouldBeTrue();
        var lost = rig.Events.OfType<DeviceLost>().Single();
        lost.DeviceId.ShouldBe("dev-2");
        lost.OthersContinue.ShouldBeTrue();
        lost.ClockPosition.ShouldBeGreaterThanOrEqualTo(TimeSpan.FromSeconds(5));
        rig.Render(500);
        rig.Summary.ShouldBeNull();

        rig.Stop(RunEndReason.Stop);
        var survivor = rig.Summary!.Outputs.Single(o => o.DeviceId == "dev-1");
        survivor.Underruns.ShouldBe(0);
        survivor.Played.ShouldBeGreaterThanOrEqualTo(TimeSpan.FromSeconds(10));
        rig.Summary!.Outputs.Single(o => o.DeviceId == "dev-2").DeviceLostEvents.Count.ShouldBe(1);
        rig.Summary!.Outputs.Single(o => o.DeviceId == "dev-2").Played.ShouldBeLessThan(survivor.Played);
    }

    [Fact]
    public void Losing_the_last_playing_device_ends_the_run_with_every_device_lost_event_in_the_summary()
    {
        using var rig = new Rig(2);
        rig.Engine.StartRun("film.mkv", [Assign(1, 1), Assign(2, 2)], new EngineOptions()).IsSuccess.ShouldBeTrue();
        rig.Render(100);

        rig.Backend.Devices["dev-2"].SimulateLoss();
        rig.WaitFor(() => rig.Events.OfType<DeviceLost>().Any()).ShouldBeTrue();
        rig.Render(100);
        rig.Backend.Devices["dev-1"].SimulateLoss();

        rig.WaitFor(() => rig.Summary is not null).ShouldBeTrue();
        rig.Summary!.EndReason.ShouldBe(RunEndReason.AllDevicesLost);
        rig.Summary.Outputs.SelectMany(o => o.DeviceLostEvents).Select(e => e.DeviceId).ShouldBe(["dev-2", "dev-1"], ignoreOrder: true);
    }

    [Fact]
    public void A_stop_command_ends_the_run_with_a_summary_for_every_output_and_closes_every_device()
    {
        using var rig = new Rig(2);
        rig.Engine.StartRun("film.mkv", [Assign(1, 1), Assign(2, 2)], new EngineOptions()).IsSuccess.ShouldBeTrue();
        rig.Render(100);

        rig.Stop(RunEndReason.Stop);

        var summary = rig.Summary;
        summary.ShouldNotBeNull();
        summary.EndReason.ShouldBe(RunEndReason.Stop);
        summary.Outputs.Select(o => o.DeviceId).ShouldBe(["dev-1", "dev-2"], ignoreOrder: true);
        summary.Outputs.ShouldAllBe(o => o.Played >= TimeSpan.FromSeconds(1) && o.Corrections == 0 && o.Underruns == 0);
        rig.Backend.Devices.Values.ShouldAllBe(d => d.IsClosed);
    }

    [Fact]
    public void Reaching_the_end_of_the_file_ends_the_run_once_the_outputs_have_drained()
    {
        using var rig = new Rig(2, totalFrames: 48_000 * 2);
        rig.Engine.StartRun("film.mkv", [Assign(1, 1), Assign(2, 2)], new EngineOptions()).IsSuccess.ShouldBeTrue();

        for (var i = 0; i < 100 && rig.Summary is null; i++)
        {
            rig.Render(10);
        }

        rig.WaitFor(() => rig.Summary is not null).ShouldBeTrue();
        rig.Summary!.EndReason.ShouldBe(RunEndReason.FileEnd);
        rig.Summary.Outputs.Count.ShouldBe(2);
        rig.Backend.Devices.Values.ShouldAllBe(d => d.IsClosed);
    }

    [Fact]
    public void A_run_played_to_the_end_of_the_file_counts_no_underrun_on_any_output_even_when_the_render_buffer_does_not_divide_the_rest()
    {
        using var rig = new Rig(2, totalFrames: (48_000 * 2) + 137);
        rig.Engine.StartRun("film.mkv", [Assign(1, 1), Assign(2, 2)], new EngineOptions()).IsSuccess.ShouldBeTrue();

        for (var i = 0; i < 100 && rig.Summary is null; i++)
        {
            rig.Render(10);
        }

        rig.WaitFor(() => rig.Summary is not null).ShouldBeTrue();
        rig.Summary!.EndReason.ShouldBe(RunEndReason.FileEnd);
        rig.Summary.Outputs.Count.ShouldBe(2);
        foreach (var output in rig.Summary.Outputs)
        {
            output.Underruns.ShouldBe(0, $"{output.DeviceId} counted an underrun at the end of the file");
        }
    }

    [Fact]
    public void Start_to_audio_is_logged_for_every_device_and_is_at_most_two_seconds_on_the_fake_clock()
    {
        using var rig = new Rig(2);
        rig.Engine.StartRun("film.mkv", [Assign(1, 1), Assign(2, 2)], new EngineOptions()).IsSuccess.ShouldBeTrue();

        rig.Render(1);

        rig.WaitFor(() => rig.Log.With("DeviceId", "StartToAudioMs").Count == 2).ShouldBeTrue();
        var entries = rig.Log.With("DeviceId", "StartToAudioMs");
        entries.Select(e => (string)e["DeviceId"]!).ShouldBe(["dev-1", "dev-2"], ignoreOrder: true);
        entries.ShouldAllBe(e => (double)e["StartToAudioMs"]! > 0 && (double)e["StartToAudioMs"]! <= 2_000);
        rig.Stop(RunEndReason.Stop);
    }

    [Fact]
    public void A_decoder_failure_during_start_is_returned_at_once_and_no_device_starts()
    {
        var error = new Error("media.decode_error", "The audio stream is corrupt.");
        using var rig = new Rig(2, decoder: FailingDecoder(error));

        var result = rig.Engine.StartRun("film.mkv", [Assign(1, 1), Assign(2, 2)], new EngineOptions());

        result.IsSuccess.ShouldBeFalse();
        result.Error.ShouldBe(error);
        rig.Backend.Devices.Values.ShouldAllBe(d => d.IsClosed && !d.IsStarted);
    }

    [Fact]
    public async Task A_start_that_never_reaches_the_start_level_times_out_on_the_injected_clock()
    {
        using var rig = new Rig(2, decoder: StalledDecoder());

        var start = Task.Run(() => rig.Engine.StartRun("film.mkv", [Assign(1, 1), Assign(2, 2)], new EngineOptions()), TestContext.Current.CancellationToken);
        rig.WaitFor(() =>
        {
            rig.Time.Advance(TimeSpan.FromSeconds(1));
            return start.IsCompleted;
        }).ShouldBeTrue();

        var result = await start;
        result.IsSuccess.ShouldBeFalse();
        result.Error.Code.ShouldBe("playback.start_timeout");
        rig.Backend.Devices.Values.ShouldAllBe(d => d.IsClosed && !d.IsStarted);
    }

    [Fact]
    public void Stop_after_the_engine_is_disposed_does_nothing()
    {
        var rig = new Rig(1);
        rig.Dispose();

        Should.NotThrow(() => rig.Engine.Stop(RunEndReason.Stop));
    }
}
