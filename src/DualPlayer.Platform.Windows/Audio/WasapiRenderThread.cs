using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using DualPlayer.Core.Outputs;
using Microsoft.Extensions.Logging;
using NAudio.CoreAudioApi;

namespace DualPlayer.Platform.Windows.Audio;

/// <summary>
/// One dedicated MMCSS "Pro Audio" thread per device: waits on the WASAPI event, reads the device position and
/// pulls frames from the <see cref="IRenderSource"/>. A device failure or a wait timeout ends this thread only.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed partial class WasapiRenderThread : IDisposable
{
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan JoinTimeout = WaitTimeout * 2;

    private readonly AudioClient _client;
    private readonly IRenderSource _source;
    private readonly string _deviceName;
    private readonly long _timestampFrequency;
    private readonly ILogger _logger;
    private readonly int _channels;
    private readonly int _sampleRate;
    private readonly float[] _scratch;
    private readonly EventWaitHandle _bufferReady = new(false, EventResetMode.AutoReset);
    private readonly ManualResetEventSlim _stop = new(false);
    private readonly Thread _thread;
    private readonly Action _onLost;
    private bool _abandoned;

    public WasapiRenderThread(
        AudioClient client,
        IRenderSource source,
        string deviceName,
        int sampleRate,
        int channels,
        TimeProvider time,
        Action onLost,
        ILogger logger)
    {
        _client = client;
        _source = source;
        _deviceName = deviceName;
        _sampleRate = sampleRate;
        _channels = channels;
        _timestampFrequency = time.TimestampFrequency;
        _onLost = onLost;
        _logger = logger;
        _scratch = new float[client.BufferSize * channels];
        _client.SetEventHandle(_bufferReady.SafeWaitHandle.DangerousGetHandle());
        _thread = new Thread(Run) { Name = $"WASAPI render: {deviceName}", IsBackground = true };
    }

    public void Start() => _thread.Start();

    /// <summary>
    /// Stops the loop and waits a bounded time for the thread to end. Returns false when the thread is stuck in a
    /// driver call and was abandoned; the caller must then leave the audio client alone.
    /// </summary>
    public bool Stop()
    {
        if (_abandoned)
        {
            return false;
        }

        _stop.Set();
        _bufferReady.Set();
        if (_thread.IsAlive && Thread.CurrentThread != _thread && !_thread.Join(JoinTimeout))
        {
            _logger.LogWarning(
                "Render thread for {Device} did not stop within {Timeout} ms; abandoning it",
                _deviceName,
                JoinTimeout.TotalMilliseconds);
            _abandoned = true;
        }

        return !_abandoned;
    }

    public void Dispose()
    {
        if (!Stop())
        {
            // The stuck thread may still touch these handles; leak them rather than fault it.
            return;
        }

        _bufferReady.Dispose();
        _stop.Dispose();
    }

    [LibraryImport("avrt.dll", EntryPoint = "AvSetMmThreadCharacteristicsW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial IntPtr AvSetMmThreadCharacteristics(string taskName, ref uint taskIndex);

    [LibraryImport("avrt.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AvRevertMmThreadCharacteristics(IntPtr handle);

    private void Run()
    {
        uint taskIndex = 0;
        var mmcss = AvSetMmThreadCharacteristics("Pro Audio", ref taskIndex);
        MmcssLog.WarnIfNotRaised(mmcss, Marshal.GetLastPInvokeError(), _logger, _deviceName);
        var lost = false;
        try
        {
            FillAvailable();
            _client.Start();
            while (!_stop.IsSet)
            {
                if (!_bufferReady.WaitOne(WaitTimeout))
                {
                    if (!_stop.IsSet)
                    {
                        _logger.LogWarning("Render event wait timed out on {Device}; treating the device as lost", _deviceName);
                        lost = true;
                    }

                    break;
                }

                if (_stop.IsSet)
                {
                    break;
                }

                FillAvailable();
            }
        }
        catch (Exception ex) when (WasapiErrorMapper.IsRenderDeviceFailure(ex))
        {
            _logger.LogWarning(ex, "Render device {Device} failed; treating it as lost", _deviceName);
            lost = true;
        }
        catch (Exception ex)
        {
            // A defect, not a device problem: the output still stops (and is reported through the loss signal, the
            // only stop signal the port has), so the log is the place that tells the two apart.
            _logger.LogError(
                ex,
                "Render loop bug on {Device}; this output stops and is reported as lost, but the device itself did not fail",
                _deviceName);
            lost = true;
        }
        finally
        {
            if (mmcss != IntPtr.Zero)
            {
                AvRevertMmThreadCharacteristics(mmcss);
            }
        }

        // A failure while the owner is closing the device is not a loss: nobody is listening for it any more.
        if (lost && !_stop.IsSet)
        {
            _onLost();
        }
    }

    private void FillAvailable()
    {
        var frames = _client.BufferSize - _client.CurrentPadding;
        if (frames <= 0)
        {
            return;
        }

        var ptr = _client.AudioRenderClient.GetBuffer(frames);
        var clock = _client.AudioClockClient;
        clock.GetPosition(out var position, out var qpc100Ns);
        var framesPlayed = (long)((double)position * _sampleRate / clock.Frequency);
        var timestamp = QpcTimeBase.ToTimestamp((long)qpc100Ns, _timestampFrequency);

        var span = _scratch.AsSpan(0, frames * _channels);
        _source.Render(span, new DevicePosition(framesPlayed, timestamp));
        Marshal.Copy(_scratch, 0, ptr, span.Length);
        _client.AudioRenderClient.ReleaseBuffer(frames, AudioClientBufferFlags.None);
    }
}
