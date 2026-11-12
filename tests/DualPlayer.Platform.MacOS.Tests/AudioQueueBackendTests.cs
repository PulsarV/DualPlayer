using System.Runtime.Versioning;
using DualPlayer.Core.Outputs;
using DualPlayer.Platform.MacOS.Audio;
using DualPlayer.Tests;
using Microsoft.Extensions.DependencyInjection;

namespace DualPlayer.Platform.MacOS.Tests;

[SupportedOSPlatform("macos")]
public sealed class AudioQueueBackendTests
{
    private const double ToneHz = 440;

    private static IAudioBackend ResolveBackend()
    {
        var services = new ServiceCollection();
        services.AddSingleton(TimeProvider.System);
        services.AddMacOSPlatform();
        return services.BuildServiceProvider().GetRequiredService<IAudioBackend>();
    }

    public static bool IsMacOS => OperatingSystem.IsMacOS();

    [Fact(SkipUnless = nameof(IsMacOS), SkipType = typeof(AudioQueueBackendTests), Skip = "Needs macOS.")]
    public void AddMacOSPlatform_registers_the_audio_backend_port()
    {
        ResolveBackend().ShouldNotBeNull();
    }

    [Theory]
    [InlineData("livn", nameof(CoreAudioInterop.PropIsAlive))]
    [InlineData("dOut", nameof(CoreAudioInterop.PropDefaultOutputDevice))]
    [InlineData("uid ", nameof(CoreAudioInterop.PropDeviceUid))]
    public void CoreAudio_property_selectors_match_their_SDK_four_char_codes(string fourCc, string constant)
    {
        var expected = ((uint)fourCc[0] << 24) | ((uint)fourCc[1] << 16) | ((uint)fourCc[2] << 8) | fourCc[3];
        var actual = constant switch
        {
            nameof(CoreAudioInterop.PropIsAlive) => CoreAudioInterop.PropIsAlive,
            nameof(CoreAudioInterop.PropDefaultOutputDevice) => CoreAudioInterop.PropDefaultOutputDevice,
            _ => CoreAudioInterop.PropDeviceUid,
        };

        actual.ShouldBe(expected);
    }

    [HardwareFact(HardwareOs.MacOS)]
    public void The_default_output_device_is_among_the_listed_devices()
    {
        var defaultUid = CoreAudioInterop.GetDefaultOutputDeviceUid();

        defaultUid.ShouldNotBeNullOrWhiteSpace();
        ResolveBackend().ListDevices().Select(d => d.Id).ShouldContain(defaultUid);
    }

    [HardwareFact(HardwareOs.MacOS)]
    public void ListDevices_returns_output_devices_with_names_and_ids()
    {
        var devices = ResolveBackend().ListDevices();

        devices.ShouldNotBeEmpty();
        devices.ShouldAllBe(d => !string.IsNullOrWhiteSpace(d.Name) && !string.IsNullOrWhiteSpace(d.Id));
        devices.Select(d => d.Id).Distinct().Count().ShouldBe(devices.Count);
    }

    [HardwareFact(HardwareOs.MacOS)]
    public void Open_with_an_unknown_device_id_fails_with_device_not_found()
    {
        var result = ResolveBackend().Open("no-such-device-uid", TimeSpan.FromMilliseconds(50), new ToneSource());

        result.IsSuccess.ShouldBeFalse();
        result.Error!.Code.ShouldBe("outputs.device_not_found");
    }

    [HardwareFact(HardwareOs.MacOS)]
    public void Playing_for_two_seconds_on_the_default_device_pulls_frames_with_an_advancing_device_position()
    {
        var backend = ResolveBackend();
        var defaultUid = CoreAudioInterop.GetDefaultOutputDeviceUid();
        var source = new ToneSource();

        var opened = backend.Open(defaultUid, TimeSpan.FromMilliseconds(50), source);

        opened.IsSuccess.ShouldBeTrue(opened.Error?.Message);
        var device = opened.Value;
        device.MixFormat.SampleRate.ShouldBeGreaterThan(0);
        device.MixFormat.Channels.ShouldBeGreaterThan(0);
        device.GrantedBuffer.ShouldBeGreaterThan(TimeSpan.Zero);
        source.Channels = device.MixFormat.Channels;
        source.SampleRate = device.MixFormat.SampleRate;

        device.Start();
        Thread.Sleep(TimeSpan.FromSeconds(2));
        device.Close();

        // About two seconds of audio must have been requested (allow generous slack; dev backend has no sync targets).
        var expectedFrames = 2L * device.MixFormat.SampleRate;
        Interlocked.Read(ref source.FramesRendered).ShouldBeInRange(expectedFrames / 2, expectedFrames * 2);
        Interlocked.Read(ref source.LastFramesPlayed).ShouldBeGreaterThan(0);
        Interlocked.Read(ref source.LastTimestamp).ShouldBeGreaterThan(0);
        source.PositionWentBackwards.ShouldBeFalse();
    }

    private sealed class ToneSource : IRenderSource
    {
        public long FramesRendered;
        public long LastFramesPlayed;
        public long LastTimestamp;
        public volatile bool PositionWentBackwards;
        public volatile int Channels = 2;
        public volatile int SampleRate = 48000;
        private long _phase;

        public void Render(Span<float> buffer, DevicePosition position)
        {
            var channels = Channels;
            var frames = buffer.Length / channels;
            for (var i = 0; i < frames; i++)
            {
                var s = (float)(0.1 * Math.Sin(2 * Math.PI * ToneHz * _phase++ / SampleRate));
                for (var c = 0; c < channels; c++)
                {
                    buffer[(i * channels) + c] = s;
                }
            }

            if (position.FramesPlayed < Interlocked.Read(ref LastFramesPlayed))
            {
                PositionWentBackwards = true;
            }

            Interlocked.Exchange(ref LastFramesPlayed, position.FramesPlayed);
            Interlocked.Exchange(ref LastTimestamp, position.Timestamp);
            Interlocked.Add(ref FramesRendered, frames);
        }
    }
}

/// <summary>F17: the registration fact must be skipped, not silently pass, on an OS it cannot exercise.</summary>
public sealed class AudioQueueRegistrationFactGateTests
{
    [Fact]
    public void The_macOS_registration_fact_is_skipped_rather_than_returning_early_on_other_systems()
    {
        var method = typeof(AudioQueueBackendTests).GetMethod(nameof(AudioQueueBackendTests.AddMacOSPlatform_registers_the_audio_backend_port));
        method.ShouldNotBeNull();
        var fact = (FactAttribute)Attribute.GetCustomAttribute(method, typeof(FactAttribute))!;

        fact.SkipUnless.ShouldNotBeNull();
    }
}
