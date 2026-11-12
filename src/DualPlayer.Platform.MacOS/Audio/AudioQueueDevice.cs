using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using DualPlayer.Core;
using DualPlayer.Core.Outputs;
using static DualPlayer.Platform.MacOS.Audio.CoreAudioInterop;

namespace DualPlayer.Platform.MacOS.Audio;

/// <summary>An AudioQueue output stream pulling float frames from an <see cref="IRenderSource"/>.</summary>
[SupportedOSPlatform("macos")]
internal sealed unsafe class AudioQueueDevice : IAudioDevice
{
    private const int BufferCount = 3;
    private const int Channels = 2;

    // Native callbacks receive a registry key, not a GCHandle: a HAL listener callback already in flight
    // when Close() runs then finds no entry instead of dereferencing a freed handle.
    private static readonly ConcurrentDictionary<nint, AudioQueueDevice> Registry = new();
    private static long _nextKey;

    private readonly IRenderSource _source;
    private readonly TimeProvider _time;
    private readonly uint _deviceId;
    private readonly nint _key = (nint)Interlocked.Increment(ref _nextKey);
    private void* _queue;
    private bool _listening;
    private volatile bool _closed;
    private int _lost;

    private AudioQueueDevice(IRenderSource source, TimeProvider time, uint deviceId, MixFormat format, TimeSpan granted)
    {
        _source = source;
        _time = time;
        _deviceId = deviceId;
        MixFormat = format;
        GrantedBuffer = granted;
    }

    public MixFormat MixFormat { get; }

    public TimeSpan GrantedBuffer { get; }

    public event EventHandler? DeviceLost;

    internal static Result<IAudioDevice> Create(uint deviceId, string uid, TimeSpan requestedBuffer, IRenderSource source, TimeProvider time)
    {
        var rate = GetNominalRate(deviceId);
        if (rate <= 0)
        {
            rate = 48000;
        }

        var frames = Math.Max(64, (int)(requestedBuffer.TotalSeconds * rate));

        // Deliberately fixed to float32 stereo at the device's nominal rate rather than the device's own
        // channel layout: AudioQueue converts to the hardware format, and this dev-only backend is not
        // held to ADR-0004's "report the device mix format" (never shipped, no sync targets).
        var format = new MixFormat((int)rate, Channels, ChannelLayout.Stereo);
        var device = new AudioQueueDevice(source, time, deviceId, format, TimeSpan.FromSeconds(frames / rate));
        var error = device.Open(uid, rate, frames);
        if (error is not null)
        {
            device.Close();
            return Result<IAudioDevice>.Failure(error);
        }

        return Result<IAudioDevice>.Success(device);
    }

    public void Start()
    {
        if (!_closed && _queue is not null)
        {
            AudioQueueStart(_queue, null);
        }
    }

    public void Close()
    {
        if (_closed)
        {
            return;
        }

        _closed = true;
        if (_listening)
        {
            AudioObjectRemovePropertyListener(_deviceId, Address(PropIsAlive), &OnAliveChanged, (void*)_key);
            _listening = false;
        }

        if (_queue is not null)
        {
            AudioQueueStop(_queue, true);
            AudioQueueDispose(_queue, true);
            _queue = null;
        }

        Registry.TryRemove(_key, out _);
    }

    [UnmanagedCallersOnly]
    private static void OnBuffer(void* userData, void* queue, QueueBuffer* buffer)
    {
        if (Registry.TryGetValue((nint)userData, out var device))
        {
            device.Fill(buffer);
        }
    }

    [UnmanagedCallersOnly]
    private static int OnAliveChanged(uint objectId, uint count, PropertyAddress* addresses, void* clientData)
    {
        if (Registry.TryGetValue((nint)clientData, out var device))
        {
            device.CheckAlive();
        }

        return 0;
    }

    private Error? Open(string uid, double rate, int frames)
    {
        Registry[_key] = this;
        var description = new StreamDescription
        {
            SampleRate = rate,
            FormatId = FormatLinearPcm,
            FormatFlags = FormatFlagFloatPacked,
            BytesPerPacket = Channels * sizeof(float),
            FramesPerPacket = 1,
            BytesPerFrame = Channels * sizeof(float),
            ChannelsPerFrame = Channels,
            BitsPerChannel = 32,
        };

        void* queue;
        if (AudioQueueNewOutput(description, &OnBuffer, (void*)_key, null, null, 0, out queue) != 0)
        {
            return new Error("outputs.device_access_denied", "Could not create an AudioQueue output.");
        }

        _queue = queue;
        var cfUid = CreateCfString(uid);
        var status = AudioQueueSetProperty(queue, QueuePropCurrentDevice, &cfUid, (uint)sizeof(void*));
        CFRelease(cfUid);
        if (status != 0)
        {
            return new Error("outputs.device_access_denied", $"Could not bind the AudioQueue to device '{uid}'.");
        }

        if (AudioObjectAddPropertyListener(_deviceId, Address(PropIsAlive), &OnAliveChanged, (void*)_key) != 0)
        {
            return new Error("outputs.device_access_denied", $"Could not watch device '{uid}' for removal.");
        }

        _listening = true;

        var bytes = (uint)(frames * Channels * sizeof(float));
        for (var i = 0; i < BufferCount; i++)
        {
            if (AudioQueueAllocateBuffer(queue, bytes, out var buffer) != 0)
            {
                return new Error("outputs.device_access_denied", "Could not allocate AudioQueue buffers.");
            }

            Fill(buffer);
        }

        return null;
    }

    private void Fill(QueueBuffer* buffer)
    {
        if (_closed || _queue is null)
        {
            return;
        }

        var floats = (int)(buffer->Capacity / sizeof(float));
        _source.Render(new Span<float>(buffer->Data, floats), ReadPosition());
        buffer->ByteSize = buffer->Capacity;
        AudioQueueEnqueueBuffer(_queue, buffer, 0, null);
    }

    private DevicePosition ReadPosition()
    {
        if (AudioQueueGetCurrentTime(_queue, null, out var ts, out _) != 0)
        {
            return new DevicePosition(0, _time.GetTimestamp());
        }

        return new DevicePosition(Math.Max(0, (long)ts.SampleTime), HostTimeToTimestamp(ts.HostTime, _time));
    }

    private void CheckAlive()
    {
        uint value = 1;
        var usize = (uint)sizeof(uint);
        if (!_closed
            && AudioObjectGetPropertyData(_deviceId, Address(PropIsAlive), 0, null, ref usize, &value) == 0
            && value == 0
            && Interlocked.Exchange(ref _lost, 1) == 0)
        {
            ThreadPool.QueueUserWorkItem(_ => DeviceLost?.Invoke(this, EventArgs.Empty));
        }
    }
}
