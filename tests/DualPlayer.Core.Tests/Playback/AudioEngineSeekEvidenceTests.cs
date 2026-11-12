using DualPlayer.Core.Diagnostics;
using DualPlayer.Core.Outputs;
using DualPlayer.Core.Playback;
using DualPlayer.Core.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace DualPlayer.Core.Tests.Playback;

/// <summary>
/// What confirms a seek: audio at the target on every output whose track has audio there (an output whose track ended
/// earlier is not waited for), and only audio of the seek's own epoch (a late block decoded before the seek is not
/// evidence). The decoder fakes decide, deterministically, what the pump delivers and when.
/// </summary>
public sealed class AudioEngineSeekEvidenceTests
{
    private const int Rate = 48_000;
    private static readonly MixFormat Stereo = new(Rate, 2, ChannelLayout.Stereo);
    private static readonly TimeSpan Step = TimeSpan.FromMilliseconds(10);

    private static readonly IReadOnlyList<AudioTrackInfo> Tracks =
    [
        new AudioTrackInfo(1, "eng", ChannelLayout.Stereo, Rate),
        new AudioTrackInfo(2, "ukr", ChannelLayout.Stereo, Rate),
    ];

    private static OutputAssignment Assign(int track, int deviceNumber) => new(track, DeviceRef.ByNumber(deviceNumber));

    private sealed class Rig : IDisposable
    {
        private readonly List<FakeAudioDevice> _devices = [];
        private readonly List<EngineEvent> _events = [];
        private readonly Lock _gate = new();
        private readonly IDisposable _subscription;

        public Rig(int devices, IAudioDecoder decoder)
        {
            Time = new FakeTimeProvider();
            var backend = new FakeAudioBackend(Time, Stereo);
            for (var i = 1; i <= devices; i++)
            {
                backend.AddDevice(new AudioDeviceInfo($"Device {i}", null, $"dev-{i}"));
            }

            Engine = new AudioEngine(new CapturingBackend(backend, _devices), decoder, Time, NullLogger<AudioEngine>.Instance);
            _subscription = Engine.Events.Subscribe(new Collector(this));
            var assignments = Enumerable.Range(1, devices).Select(i => Assign(i, i)).ToArray();
            Engine.StartRun("film.mkv", assignments, new EngineOptions()).IsSuccess.ShouldBeTrue();
            Fill();
        }

        public FakeTimeProvider Time { get; }

        public AudioEngine Engine { get; }

        public IReadOnlyList<FakeAudioDevice> Devices => _devices;

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

        /// <summary>Advances 10 ms per step and keeps every device two periods ahead of its position.</summary>
        public void Render(int steps)
        {
            for (var i = 0; i < steps; i++)
            {
                Time.Advance(Step);
                Fill();
                Thread.Sleep(1);
            }
        }

        public bool WaitFor(Func<bool> condition)
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
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

        public Task<T> Start<T>(Func<T> command) =>
            Task.Factory.StartNew(command, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

        /// <summary>Renders on until <paramref name="task"/> completes (at most 30 s of fake time).</summary>
        public T Finish<T>(Task<T> task)
        {
            for (var i = 0; i < 3_000 && !task.IsCompleted; i++)
            {
                Render(1);
            }

            task.IsCompleted.ShouldBeTrue("the command never completed");
            return task.GetAwaiter().GetResult();
        }

        public void Dispose()
        {
            _subscription.Dispose();
            Engine.Dispose();
        }

        private void Fill()
        {
            foreach (var device in _devices.Where(d => !d.IsClosed))
            {
                RigLoops.FillQueue(device, 2 * 480, 480);
            }
        }

        private sealed class CapturingBackend(IAudioBackend inner, List<FakeAudioDevice> devices) : IAudioBackend
        {
            public IReadOnlyList<AudioDeviceInfo> ListDevices() => inner.ListDevices();

            public Result<IAudioDevice> Open(string deviceId, TimeSpan requestedBuffer, IRenderSource source)
            {
                var result = inner.Open(deviceId, requestedBuffer, source);
                if (result.IsSuccess)
                {
                    devices.Add((FakeAudioDevice)result.Value);
                }

                return result;
            }
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

    /// <summary>Content seconds of the last non-silent frame the device was handed; -1 when it has only been handed silence.</summary>
    private static double LastHeardContentSeconds(FakeAudioDevice device)
    {
        for (var i = device.Rendered.Count - 1; i >= 0; i--)
        {
            if (device.Rendered[i] != 0f)
            {
                return Math.Round(device.Rendered[i] * 1000.0) / Rate;
            }
        }

        return -1;
    }

    [Fact]
    public void Seeking_past_the_end_of_the_shorter_track_but_inside_the_file_succeeds_and_the_longer_track_plays_the_new_position()
    {
        var decoder = new PerTrackLengthDecoder(Tracks, new Dictionary<int, TimeSpan>
        {
            [1] = TimeSpan.FromSeconds(10),
            [2] = TimeSpan.FromSeconds(60),
        });
        using var rig = new Rig(2, decoder);
        rig.Render(100);

        var seek = rig.Finish(rig.Start(() => rig.Engine.Seek(TimeSpan.FromSeconds(20))));
        rig.Render(100);

        seek.IsSuccess.ShouldBeTrue(seek.Error?.Message);
        seek.Value.OutsideFile.ShouldBeFalse("20 s is inside the 60 s file; only the 10 s track has nothing there");
        seek.Value.Position.TotalSeconds.ShouldBeInRange(20, 23);
        rig.Events.OfType<SeekTargetOutsideFile>().ShouldBeEmpty();
        rig.Events.OfType<RunSummary>().ShouldBeEmpty("the run goes on");
        LastHeardContentSeconds(rig.Devices[1]).ShouldBeGreaterThan(20.5, "the longer track plays the new position");
    }

    [Fact]
    public async Task A_late_block_decoded_before_a_past_end_seek_does_not_confirm_it_and_the_run_ends_seek_past_end()
    {
        var decoder = new LateBlockDecoder(new FakeAudioDecoder(Tracks, Rate * 60));
        using var rig = new Rig(1, decoder);
        rig.Render(20);

        decoder.Arm(); // the pump decodes one more block of the old epoch and holds it
        rig.WaitFor(() =>
        {
            rig.Render(1);
            return decoder.Holding;
        }).ShouldBeTrue("the decoder never reached the held read");
        var seek = rig.Start(() => rig.Engine.Seek(TimeSpan.FromMinutes(10)));

        // The device applies the new epoch and plays out everything written before it, while the block is still held.
        var until = DateTime.UtcNow + TimeSpan.FromMilliseconds(400);
        for (var i = 0; i < 60 || DateTime.UtcNow < until; i++)
        {
            rig.Render(1);
        }

        seek.IsCompleted.ShouldBeFalse("nothing has been decoded at the target yet");

        // The old-epoch block lands in the ring, then the decoder reports the end of the file at the target.
        // The devices stay still, so that block is in the ring when the engine looks.
        decoder.Release();
        rig.WaitFor(() => decoder.EndedAfterSeek).ShouldBeTrue("the decoder never reached the end of the file after the seek");
        await Task.Delay(300, TestContext.Current.CancellationToken);

        var result = rig.Finish(seek);

        result.IsSuccess.ShouldBeTrue(result.Error?.Message);
        result.Value.OutsideFile.ShouldBeTrue("the late pre-seek block is not audio at the target");
        rig.Events.OfType<SeekTargetOutsideFile>().Count().ShouldBe(1);
        rig.Events.OfType<RunSummary>().Single().EndReason.ShouldBe(RunEndReason.SeekPastEnd);
    }
}
