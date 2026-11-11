using System.Runtime.InteropServices;
using System.Text;

namespace DualPlayer.Platform.MacOS.Audio;

/// <summary>P/Invoke surface for CoreAudio device enumeration and AudioQueue output (dev-only).</summary>
internal static unsafe partial class CoreAudioInterop
{
    private const string CoreAudio = "/System/Library/Frameworks/CoreAudio.framework/CoreAudio";
    private const string AudioToolbox = "/System/Library/Frameworks/AudioToolbox.framework/AudioToolbox";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const string LibSystem = "/usr/lib/libSystem.dylib";
    private const uint Utf8 = 0x08000100;

    public const uint SystemObject = 1;
    public const uint PropDevices = 0x64657623; // 'dev#'
    public const uint PropDeviceUid = 0x75696420; // 'uid '
    public const uint PropName = 0x6C6E616D; // 'lnam'
    public const uint PropStreams = 0x73746D23; // 'stm#'
    public const uint PropNominalRate = 0x6E737274; // 'nsrt'
    public const uint PropIsAlive = 0x6C69766E; // 'livn' (kAudioDevicePropertyDeviceIsAlive)
    public const uint PropDefaultOutputDevice = 0x644F7574; // 'dOut' (kAudioHardwarePropertyDefaultOutputDevice)
    public const uint ScopeGlobal = 0x676C6F62; // 'glob'
    public const uint ScopeOutput = 0x6F757470; // 'outp'
    public const uint QueuePropCurrentDevice = 0x61716364; // 'aqcd'
    public const uint FormatLinearPcm = 0x6C70636D; // 'lpcm'
    public const uint FormatFlagFloatPacked = 0x1 | 0x8; // IsFloat | IsPacked

    [StructLayout(LayoutKind.Sequential)]
    public struct PropertyAddress
    {
        public uint Selector;
        public uint Scope;
        public uint Element;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct StreamDescription
    {
        public double SampleRate;
        public uint FormatId;
        public uint FormatFlags;
        public uint BytesPerPacket;
        public uint FramesPerPacket;
        public uint BytesPerFrame;
        public uint ChannelsPerFrame;
        public uint BitsPerChannel;
        public uint Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct QueueBuffer
    {
        public uint Capacity;
        public void* Data;
        public uint ByteSize;
        public void* UserData;
        public uint PacketDescCapacity;
        public void* PacketDescs;
        public uint PacketDescCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct TimeStamp
    {
        public double SampleTime;
        public ulong HostTime;
        public double RateScalar;
        public ulong WordClockTime;
        public fixed byte Smpte[24];
        public uint Flags;
        public uint Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct TimebaseInfo
    {
        public uint Numer;
        public uint Denom;
    }

    [LibraryImport(CoreAudio)]
    public static partial int AudioObjectGetPropertyDataSize(uint objectId, in PropertyAddress address, uint qualifierSize, void* qualifier, out uint size);

    [LibraryImport(CoreAudio)]
    public static partial int AudioObjectGetPropertyData(uint objectId, in PropertyAddress address, uint qualifierSize, void* qualifier, ref uint size, void* data);

    [LibraryImport(CoreAudio)]
    public static partial int AudioObjectAddPropertyListener(uint objectId, in PropertyAddress address, delegate* unmanaged<uint, uint, PropertyAddress*, void*, int> listener, void* clientData);

    [LibraryImport(CoreAudio)]
    public static partial int AudioObjectRemovePropertyListener(uint objectId, in PropertyAddress address, delegate* unmanaged<uint, uint, PropertyAddress*, void*, int> listener, void* clientData);

    [LibraryImport(AudioToolbox)]
    public static partial int AudioQueueNewOutput(in StreamDescription format, delegate* unmanaged<void*, void*, QueueBuffer*, void> callback, void* userData, void* runLoop, void* runLoopMode, uint flags, out void* queue);

    [LibraryImport(AudioToolbox)]
    public static partial int AudioQueueDispose(void* queue, [MarshalAs(UnmanagedType.U1)] bool immediate);

    [LibraryImport(AudioToolbox)]
    public static partial int AudioQueueAllocateBuffer(void* queue, uint byteSize, out QueueBuffer* buffer);

    [LibraryImport(AudioToolbox)]
    public static partial int AudioQueueEnqueueBuffer(void* queue, QueueBuffer* buffer, uint packetDescCount, void* packetDescs);

    [LibraryImport(AudioToolbox)]
    public static partial int AudioQueueStart(void* queue, void* startTime);

    [LibraryImport(AudioToolbox)]
    public static partial int AudioQueueStop(void* queue, [MarshalAs(UnmanagedType.U1)] bool immediate);

    [LibraryImport(AudioToolbox)]
    public static partial int AudioQueueSetProperty(void* queue, uint propertyId, void* data, uint size);

    [LibraryImport(AudioToolbox)]
    public static partial int AudioQueueGetCurrentTime(void* queue, void* timeline, out TimeStamp time, [MarshalAs(UnmanagedType.U1)] out bool discontinuity);

    [LibraryImport(CoreFoundation)]
    public static partial void CFRelease(void* cf);

    [LibraryImport(CoreFoundation)]
    private static partial nint CFStringGetLength(void* s);

    [LibraryImport(CoreFoundation)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static partial bool CFStringGetCString(void* s, byte* buffer, nint size, uint encoding);

    [LibraryImport(CoreFoundation, StringMarshalling = StringMarshalling.Utf8)]
    public static partial void* CFStringCreateWithCString(void* allocator, string value, uint encoding);

    [LibraryImport(LibSystem)]
    public static partial ulong mach_absolute_time();

    [LibraryImport(LibSystem)]
    public static partial int mach_timebase_info(out TimebaseInfo info);

    public static void* CreateCfString(string value) => CFStringCreateWithCString(null, value, Utf8);

    /// <summary>Copies a CFString into a managed string and releases it.</summary>
    public static string TakeCfString(void* cf)
    {
        if (cf is null)
        {
            return string.Empty;
        }

        try
        {
            var capacity = (int)(CFStringGetLength(cf) * 4) + 1;
            var buffer = new byte[capacity];
            fixed (byte* p = buffer)
            {
                return CFStringGetCString(cf, p, capacity, Utf8) ? Encoding.UTF8.GetString(buffer, 0, Array.IndexOf(buffer, (byte)0)) : string.Empty;
            }
        }
        finally
        {
            CFRelease(cf);
        }
    }

    public static PropertyAddress Address(uint selector, uint scope = ScopeGlobal) => new() { Selector = selector, Scope = scope, Element = 0 };

    public static uint[] GetDeviceIds()
    {
        var address = Address(PropDevices);
        if (AudioObjectGetPropertyDataSize(SystemObject, address, 0, null, out var size) != 0 || size == 0)
        {
            return [];
        }

        var ids = new uint[size / sizeof(uint)];
        fixed (uint* p = ids)
        {
            return AudioObjectGetPropertyData(SystemObject, address, 0, null, ref size, p) == 0 ? ids : [];
        }
    }

    public static bool HasOutput(uint deviceId) =>
        AudioObjectGetPropertyDataSize(deviceId, Address(PropStreams, ScopeOutput), 0, null, out var size) == 0 && size > 0;

    public static string GetStringProperty(uint deviceId, uint selector)
    {
        void* cf = null;
        var size = (uint)sizeof(void*);
        return AudioObjectGetPropertyData(deviceId, Address(selector), 0, null, ref size, &cf) == 0 ? TakeCfString(cf) : string.Empty;
    }

    /// <summary>The UID of the system default output device, or an empty string when there is none.</summary>
    public static string GetDefaultOutputDeviceUid()
    {
        uint deviceId = 0;
        var size = (uint)sizeof(uint);
        return AudioObjectGetPropertyData(SystemObject, Address(PropDefaultOutputDevice), 0, null, ref size, &deviceId) == 0 && deviceId != 0
            ? GetStringProperty(deviceId, PropDeviceUid)
            : string.Empty;
    }

    public static double GetNominalRate(uint deviceId)
    {
        double rate = 0;
        var size = (uint)sizeof(double);
        return AudioObjectGetPropertyData(deviceId, Address(PropNominalRate), 0, null, ref size, &rate) == 0 ? rate : 0;
    }

    /// <summary>Converts a mach host time to ticks of the given timestamp base, anchored at "now".</summary>
    public static long HostTimeToTimestamp(ulong hostTime, TimeProvider time)
    {
        mach_timebase_info(out var tb);
        var nowHost = mach_absolute_time();
        var deltaNs = ((double)hostTime - nowHost) * tb.Numer / tb.Denom;
        return time.GetTimestamp() + (long)(deltaNs * time.TimestampFrequency / 1_000_000_000d);
    }
}
