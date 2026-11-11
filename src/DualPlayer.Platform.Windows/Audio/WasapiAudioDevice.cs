using DualPlayer.Core;
using DualPlayer.Core.Outputs;
using Microsoft.Extensions.Logging;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace DualPlayer.Platform.Windows.Audio;

/// <summary>An open shared-mode, event-driven WASAPI stream. The render thread lands in T17.</summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
internal sealed class WasapiAudioDevice : IAudioDevice, IDisposable
{
    private static readonly Guid IeeeFloatSubFormat = new("00000003-0000-0010-8000-00aa00389b71");

    private readonly MMDevice _device;
    private readonly AudioClient _client;
    private bool _disposed;

    private WasapiAudioDevice(MMDevice device, AudioClient client, MixFormat mixFormat, TimeSpan grantedBuffer)
    {
        _device = device;
        _client = client;
        MixFormat = mixFormat;
        GrantedBuffer = grantedBuffer;
    }

    public MixFormat MixFormat { get; }

    public TimeSpan GrantedBuffer { get; }

    public event EventHandler? DeviceLost
    {
        add { }
        remove { }
    }

    /// <summary>
    /// Initialises the endpoint in shared mode. On success the device owns <paramref name="device"/>; on failure the
    /// caller still owns it. COM failures propagate to the caller for HRESULT mapping; the audio client is released first.
    /// </summary>
    internal static Result<IAudioDevice> Create(MMDevice device, TimeSpan requestedBuffer, ILogger logger)
    {
        var client = device.AudioClient;
        try
        {
            var format = client.MixFormat;
            if (!IsFloat32(format))
            {
                client.Dispose();
                return Result<IAudioDevice>.Failure(
                    "outputs.device_open_failed",
                    $"Device '{device.FriendlyName}' has a shared mix format of {format}; the player needs 32-bit float.");
            }

            client.Initialize(
                AudioClientShareMode.Shared,
                AudioClientStreamFlags.EventCallback,
                requestedBuffer.Ticks,
                0,
                format,
                Guid.Empty);

            var granted = TimeSpan.FromSeconds((double)client.BufferSize / format.SampleRate);
            var mix = new MixFormat(format.SampleRate, format.Channels, ToLayout(format.Channels));
            logger.LogInformation(
                "Opened '{Device}' shared: {SampleRate} Hz, {Channels} ch, granted buffer {Buffer} ms (requested {Requested} ms)",
                device.FriendlyName,
                mix.SampleRate,
                mix.Channels,
                granted.TotalMilliseconds,
                requestedBuffer.TotalMilliseconds);
            return Result<IAudioDevice>.Success(new WasapiAudioDevice(device, client, mix, granted));
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    /// <summary>True when <paramref name="format"/> is 32-bit IEEE float, plain or extensible.</summary>
    internal static bool IsFloat32(WaveFormat format) =>
        format.BitsPerSample == 32
        && (format.Encoding == WaveFormatEncoding.IeeeFloat
            || (format is WaveFormatExtensible extensible && extensible.SubFormat == IeeeFloatSubFormat));

    public void Start() => _client.Start();

    public void Close()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            _client.Stop();
        }
        catch (Exception ex) when (WasapiErrorMapper.IsDeviceFailure(ex))
        {
            // The device is already gone; there is nothing left to stop.
        }
        finally
        {
            Dispose();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _client.Dispose();
        _device.Dispose();
    }

    private static ChannelLayout ToLayout(int channels) => channels switch
    {
        1 => ChannelLayout.Mono,
        2 => ChannelLayout.Stereo,
        6 => ChannelLayout.Surround51,
        8 => ChannelLayout.Surround71,
        _ => ChannelLayout.Other,
    };
}
