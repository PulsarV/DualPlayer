using DualPlayer.Core.Outputs;
using DualPlayer.Core.Playback;
using Microsoft.Extensions.Time.Testing;
using Shouldly;

namespace DualPlayer.Core.Tests.Fakes;

public class FakeAudioDeviceTests
{
    private static readonly MixFormat Stereo48 = new(48_000, 2, ChannelLayout.Stereo);

    private sealed class RecordingSource : IRenderSource
    {
        public List<DevicePosition> Positions { get; } = [];

        public void Render(Span<float> buffer, DevicePosition position) => Positions.Add(position);
    }

    [Fact]
    public void Fake_device_frames_played_advance_at_the_configured_ppm()
    {
        var time = new FakeTimeProvider();
        var source = new RecordingSource();
        var backend = new FakeAudioBackend(time, Stereo48) { PpmOffset = 1000 };
        backend.AddDevice(new AudioDeviceInfo("Speakers", "Speakers", "dev-1"));

        var opened = backend.Open("dev-1", TimeSpan.FromMilliseconds(40), source);
        opened.IsSuccess.ShouldBeTrue();
        var device = (FakeAudioDevice)opened.Value;
        device.Start();

        time.Advance(TimeSpan.FromSeconds(10));
        device.PumpRender(frames: 480);

        // 10 s at 48 kHz, +1000 ppm => 480_000 * 1.001 = 480_480 frames
        ((double)source.Positions[^1].FramesPlayed).ShouldBe(480_480d, tolerance: 2d);
        source.Positions[^1].Timestamp.ShouldBe(time.GetTimestamp());
    }

    [Fact]
    public void Fake_device_raises_loss_exactly_once_and_stops_rendering()
    {
        var time = new FakeTimeProvider();
        var source = new RecordingSource();
        var backend = new FakeAudioBackend(time, Stereo48);
        backend.AddDevice(new AudioDeviceInfo("Headset", null, "dev-2"));
        var device = (FakeAudioDevice)backend.Open("dev-2", TimeSpan.FromMilliseconds(40), source).Value!;
        var losses = 0;
        device.DeviceLost += (_, _) => losses++;
        device.Start();

        device.SimulateLoss();
        device.SimulateLoss();
        var rendersBefore = source.Positions.Count;
        device.PumpRender(frames: 480);

        losses.ShouldBe(1);
        source.Positions.Count.ShouldBe(rendersBefore);
    }

    [Fact]
    public void Open_reports_access_denied_naming_the_device_without_throwing()
    {
        var backend = new FakeAudioBackend(new FakeTimeProvider(), Stereo48);
        backend.AddDevice(new AudioDeviceInfo("Busy DAC", null, "dev-3"));
        backend.DenyAccess("dev-3");

        var result = backend.Open("dev-3", TimeSpan.FromMilliseconds(40), new RecordingSource());

        result.IsSuccess.ShouldBeFalse();
        result.Error.Code.ShouldBe("outputs.device_access_denied");
        result.Error.Message.ShouldContain("Busy DAC");
    }

    [Fact]
    public void Open_reports_device_not_found_for_an_unknown_id()
    {
        var backend = new FakeAudioBackend(new FakeTimeProvider(), Stereo48);

        var result = backend.Open("nope", TimeSpan.FromMilliseconds(40), new RecordingSource());

        result.Error!.Code.ShouldBe("outputs.device_not_found");
    }

    [Fact]
    public void List_devices_returns_every_device_with_name_type_and_id()
    {
        var backend = new FakeAudioBackend(new FakeTimeProvider(), Stereo48);
        backend.AddDevice(new AudioDeviceInfo("A", "Speakers", "id-a"));
        backend.AddDevice(new AudioDeviceInfo("B", null, "id-b"));

        backend.ListDevices().Select(d => d.Id).ShouldBe(["id-a", "id-b"]);
    }

    [Fact]
    public void Engine_options_default_to_the_documented_buffer_sizes()
    {
        var options = new EngineOptions();

        options.RingBuffer.ShouldBe(TimeSpan.FromSeconds(2));
        options.StartLevel.ShouldBe(TimeSpan.FromSeconds(0.5));
        options.DeviceBuffer.ShouldBe(TimeSpan.FromMilliseconds(40));
    }
}
