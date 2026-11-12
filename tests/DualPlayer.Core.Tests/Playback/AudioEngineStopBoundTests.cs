using DualPlayer.Core.Diagnostics;
using DualPlayer.Core.Outputs;
using DualPlayer.Core.Playback;
using DualPlayer.Core.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace DualPlayer.Core.Tests.Playback;

public sealed class AudioEngineStopBoundTests
{
    [Fact]
    public async Task Stop_returns_in_bounded_real_time_when_a_device_stops_rendering_under_a_frozen_fake_clock()
    {
        var time = new FakeTimeProvider();
        var format = new MixFormat(48_000, 2, ChannelLayout.Stereo);
        var backend = new FakeAudioBackend(time, format);
        backend.AddDevice(new AudioDeviceInfo("Device 1", null, "dev-1"));
        var devices = new List<FakeAudioDevice>();
        var tracks = new List<AudioTrackInfo> { new(1, "eng", ChannelLayout.Stereo, 48_000) };
        using var engine = new AudioEngine(new Capture(backend, devices), new FakeAudioDecoder(tracks, 48_000L * 600), time, NullLogger<AudioEngine>.Instance);
        engine.StartRun("film.mkv", [new OutputAssignment(1, DeviceRef.ByNumber(1))], new EngineOptions { StopWaitMaxPolls = 50 }).IsSuccess.ShouldBeTrue();
        var device = devices.Single();
        time.Advance(TimeSpan.FromMilliseconds(10));
        RigLoops.FillQueue(device, 960, 480);
        device.Hang(); // nothing renders from here and fake time never moves: only a real-time bound can end the wait

        var stop = Task.Run(() => engine.Stop(RunEndReason.Stop), TestContext.Current.CancellationToken);

        (await RigLoops.CompletesWithin(stop, TimeSpan.FromSeconds(5))).ShouldBeTrue("Stop never returned although the device stopped rendering");
    }

    private sealed class Capture(IAudioBackend inner, List<FakeAudioDevice> devices) : IAudioBackend
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
}
