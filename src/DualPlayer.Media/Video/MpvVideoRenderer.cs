using System.Globalization;
using System.Runtime.InteropServices;
using DualPlayer.Core;
using DualPlayer.Core.Playback;
using Microsoft.Extensions.Logging;

namespace DualPlayer.Media.Video;

/// <summary>
/// <see cref="IVideoRenderer"/> over libmpv: mpv decodes (hardware only) and presents into the handed-over native window;
/// Core steers it. Commands come from the caller's thread; mpv's property and end events are read on one dedicated event
/// thread, which publishes immutable snapshots (<see cref="Presented"/>, <see cref="Counters"/>) through plain
/// reference writes, so a reader never waits on a lock and never sees a torn value.
/// </summary>
/// <remarks>
/// mpv reports positions but no timestamps for them: a position is stamped with <see cref="TimeProvider.GetTimestamp"/>
/// when the event thread receives it, the same monotonic base Core uses. Readings are therefore frame- and
/// event-latency granular (ADR-0002, negative consequences). A renderer owns one native window: it is handed over with the
/// first <see cref="Load"/>, and a later load with a different handle fails with <c>mpv.load_failed</c>.
/// </remarks>
public sealed partial class MpvVideoRenderer : IVideoRenderer
{
    private const int EventNone = 0;
    private const int EventShutdown = 1;
    private const int EventLogMessage = 2;
    private const int EventStartFile = 6;
    private const int EventEndFile = 7;
    private const int EventFileLoaded = 8;
    private const int EventPlaybackRestart = 21;
    private const int EventPropertyChange = 22;

    private const int FormatNone = 0;
    private const int FormatString = 1;
    private const int FormatInt64 = 4;
    private const int FormatDouble = 5;

    private const int EndReasonEof = 0;
    private const int EndReasonError = 4;

    private const ulong PropTimePos = 1;
    private const ulong PropHwdecCurrent = 2;
    private const ulong PropFrameDrop = 3;
    private const ulong PropDecoderDrop = 4;
    private const ulong PropDelayed = 5;

    private const int NoLoad = -1;

    private static readonly TimeSpan LoadTimeout = TimeSpan.FromSeconds(15);

    private readonly TimeProvider _time;
    private readonly IReadOnlyDictionary<string, string> _overrides;
    private readonly ILogger? _logger;
    private readonly Lock _gate = new();
    private readonly ManualResetEventSlim _loadDone = new();

    private nint _handle;
    private Thread? _eventThread;
    private ITimer? _startTimer;
    private volatile bool _disposed;
    private nint _windowHandle;
    private NoteStyle? _appliedNoteStyle;
    private bool _noteLayoutApplied;

    // Written by the event thread only (reset there when a file starts); read lock-free by the control loop.
    private volatile PresentedPicture? _presented;
    private volatile FrameCounters _counters = FrameCounters.None;

    // Load generations: every accepted loadfile is file number N for the event thread (its START_FILE count), so events of
    // a replaced or abandoned load are told apart from the current one. NoLoad means no file is current.
    private int _issuedLoads;
    private volatile int _currentLoad;

    // Event-thread state.
    private int _fileSequence;
    private volatile bool _stop;
    private volatile bool _loading;
    private volatile Error? _loadError;
    private string _loadingFile = string.Empty;
    private bool _fileLoaded;
    private bool _firstFrameShown;
    private bool _hardwareInactiveRaised;
    private string _hwdecCurrent = string.Empty;
    private long _frameDrop;
    private long _decoderDrop;
    private long _delayed;
    private long _skippedDamaged;

    public MpvVideoRenderer(TimeProvider time, ILogger<MpvVideoRenderer>? logger = null)
        : this(time, new Dictionary<string, string>(), logger)
    {
    }

    // Option overrides exist for tests only (a build machine has no window and no GPU); a public way to override the
    // hardening list would let configuration undo it.
    internal MpvVideoRenderer(
        TimeProvider time,
        IReadOnlyDictionary<string, string> optionOverrides,
        ILogger<MpvVideoRenderer>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(optionOverrides);
        _time = time;
        _overrides = optionOverrides;
        _logger = logger;
    }

    /// <summary>How long an on-screen note is held after its last change (sad §8); fixed adapter policy.</summary>
    public static TimeSpan NoteHold => TimeSpan.FromSeconds(2);

    public PresentedPicture? Presented => _presented;

    /// <summary>
    /// Per-file frame counters, reset for each load. <c>Late</c> is mpv's <c>vo-delayed-frame-count</c>, which mpv fills
    /// only in display-sync mode; outside it, it stays zero.
    /// </summary>
    public FrameCounters Counters => _counters;

    public event Action<Error>? DecodeFailed;

    public event Action? HardwareDecodingInactive;

    public event Action? EndOfFile;

    /// <summary>The options mpv is started with, before <c>mpv_initialize</c> (sad §8, mpv hardening).</summary>
    public static IReadOnlyList<KeyValuePair<string, string>> HardeningOptions(OSPlatform platform) =>
    [
        new("config", "no"),
        new("load-scripts", "no"),
        new("osc", "no"),
        new("input-default-bindings", "no"),
        new("input-vo-keyboard", "no"),
        new("ytdl", "no"),
        new("audio", "no"),
        new("hwdec", platform == OSPlatform.OSX ? "videotoolbox" : "d3d11va"),
    ];

    public Result Load(string file, nint nativeWindowHandle, bool startPaused)
    {
        ArgumentException.ThrowIfNullOrEmpty(file);

        int generation;
        var library = MpvLibrary.Load();
        if (!library.IsSuccess)
        {
            return Result.Failure(library.Error);
        }

        lock (_gate)
        {
            if (_disposed)
            {
                return Result.Failure("mpv.load_failed", "The video renderer has been disposed.");
            }

            if (_handle == 0)
            {
                var created = CreateContext(nativeWindowHandle);
                if (!created.IsSuccess)
                {
                    return created;
                }

                _windowHandle = nativeWindowHandle;
            }
            else if (nativeWindowHandle != _windowHandle)
            {
                return Result.Failure("mpv.load_failed", "The video renderer is already bound to another native window; use one renderer per window.");
            }

            // Per-file state is reset by the event thread when the new file starts; here the new load only becomes current.
            var previousLoad = _currentLoad;
            generation = _issuedLoads + 1;
            _currentLoad = generation;
            _loadError = null;
            _loadingFile = file;
            _loadDone.Reset();
            _loading = true;

            CancelScheduledStart();
            if (!SetProperty("pause", startPaused ? "yes" : "no", out var pauseError))
            {
                _loading = false;
                _currentLoad = previousLoad;
                return Result.Failure("mpv.load_failed", pauseError);
            }

            var status = RunCommand(_handle, "loadfile", file, "replace");
            if (status < 0)
            {
                _loading = false;
                _currentLoad = previousLoad;
                return Result.Failure("mpv.load_failed", $"mpv rejected '{file}': {ErrorText(status)}.");
            }

            _issuedLoads = generation;
        }

        var completed = _loadDone.Wait(LoadTimeout);
        _loading = false;
        if (_disposed)
        {
            return Result.Failure("mpv.load_failed", "The video renderer was disposed while the file was opening.");
        }

        if (!completed)
        {
            // Whatever this file does later is no longer anyone's business: its events are ignored.
            Interlocked.CompareExchange(ref _currentLoad, NoLoad, generation);
            return Result.Failure("mpv.load_failed", $"mpv did not open '{file}' within {LoadTimeout.TotalSeconds:0} s.");
        }

        return _loadError is { } error ? Result.Failure(error) : Result.Success();
    }

    public void PlayAt(TimeSpan position, long startTimestamp)
    {
        lock (_gate)
        {
            if (_handle == 0)
            {
                return;
            }

            CancelScheduledStart();
            RunCommandAsync(_handle, "seek", Seconds(position), "absolute+exact");

            var delay = _time.GetElapsedTime(_time.GetTimestamp(), startTimestamp);
            if (delay <= TimeSpan.Zero)
            {
                RunCommandAsync(_handle, "set", "pause", "no");
            }
            else
            {
                _startTimer = _time.CreateTimer(_ => Resume(), null, delay, Timeout.InfiniteTimeSpan);
            }
        }
    }

    public void Pause()
    {
        lock (_gate)
        {
            if (_handle == 0)
            {
                return;
            }

            CancelScheduledStart();
            RunCommandAsync(_handle, "set", "pause", "yes");
        }
    }

    public void Seek(TimeSpan position)
    {
        lock (_gate)
        {
            if (_handle != 0)
            {
                // A scheduled start stays scheduled: the picture resumes from the new position at the same moment.
                RunCommandAsync(_handle, "seek", Seconds(position), "absolute+exact");
            }
        }
    }

    public void SetSpeed(double speed)
    {
        lock (_gate)
        {
            if (_handle != 0)
            {
                RunCommandAsync(_handle, "set", "speed", speed.ToString("R", CultureInfo.InvariantCulture));
            }
        }
    }

    public void ShowNote(string text, NoteStyle style)
    {
        ArgumentNullException.ThrowIfNull(text);

        lock (_gate)
        {
            if (_handle == 0)
            {
                return;
            }

            // Style properties are set when they change, not per note; every call is a queued (non-blocking) one.
            if (!_noteLayoutApplied)
            {
                RunCommandAsync(_handle, "set", "osd-font-size", "32");
                RunCommandAsync(_handle, "set", "osd-back-color", "#CC000000");
                RunCommandAsync(_handle, "set", "osd-border-style", "background-box");
                RunCommandAsync(_handle, "set", "osd-margin-x", "48");
                RunCommandAsync(_handle, "set", "osd-margin-y", "48");
                _noteLayoutApplied = true;
            }

            if (_appliedNoteStyle != style)
            {
                RunCommandAsync(_handle, "set", "osd-color", style == NoteStyle.Warning ? "#FFFFD60A" : "#FFFFFFFF");
                _appliedNoteStyle = style;
            }

            // A new note replaces the current one; "$" would start a property expansion, so it is doubled.
            RunCommandAsync(_handle, "show-text", text.Replace("$", "$$", StringComparison.Ordinal), ((int)NoteHold.TotalMilliseconds).ToString(CultureInfo.InvariantCulture));
        }
    }

    public void Dispose()
    {
        Thread? thread;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            CancelScheduledStart();
            _stop = true;
            thread = _eventThread;
            if (_handle != 0)
            {
                MpvNative.Wakeup(_handle);
            }

            _handle = 0;
        }

        // The event thread terminates and destroys the mpv context when it leaves its loop.
        if (thread is not null && thread != Thread.CurrentThread)
        {
            thread.Join();
        }

        _loadDone.Set();
    }

    private static string Seconds(TimeSpan position) =>
        position.TotalSeconds.ToString("0.######", CultureInfo.InvariantCulture);

    private static string ErrorText(int status) => Marshal.PtrToStringUTF8(MpvNative.ErrorString(status)) ?? $"error {status}";

    private static int RunCommand(nint ctx, params string[] args) => Run(ctx, async: false, args);

    // Queued by mpv and executed in order on its side; the caller never waits for the command. Replies are ignored events.
    private static int RunCommandAsync(nint ctx, params string[] args) => Run(ctx, async: true, args);

    private static int Run(nint ctx, bool async, string[] args)
    {
        var pointers = new nint[args.Length + 1];
        try
        {
            for (var i = 0; i < args.Length; i++)
            {
                pointers[i] = Marshal.StringToCoTaskMemUTF8(args[i]);
            }

            unsafe
            {
                fixed (nint* array = pointers)
                {
                    return async ? MpvNative.CommandAsync(ctx, 0, (nint)array) : MpvNative.Command(ctx, (nint)array);
                }
            }
        }
        finally
        {
            foreach (var pointer in pointers)
            {
                if (pointer != 0)
                {
                    Marshal.FreeCoTaskMem(pointer);
                }
            }
        }
    }

    private Result CreateContext(nint nativeWindowHandle)
    {
        var handle = MpvNative.Create();
        if (handle == 0)
        {
            return Result.Failure("mpv.load_failed", "libmpv could not create a player context.");
        }

        var options = HardeningOptions(OperatingSystem.IsMacOS() ? OSPlatform.OSX : OSPlatform.Windows)
            .Where(o => !_overrides.ContainsKey(o.Key))
            .Concat(_overrides)
            .ToList();
        if (nativeWindowHandle != 0)
        {
            options.Add(new("wid", ((long)nativeWindowHandle).ToString(CultureInfo.InvariantCulture)));
        }

        foreach (var (name, value) in options)
        {
            var status = MpvNative.SetOptionString(handle, name, value);
            if (status < 0)
            {
                MpvNative.TerminateDestroy(handle);
                return Result.Failure("mpv.load_failed", $"mpv rejected option {name}={value}: {ErrorText(status)}.");
            }
        }

        var init = MpvNative.Initialize(handle);
        if (init < 0)
        {
            MpvNative.TerminateDestroy(handle);
            return Result.Failure("mpv.load_failed", $"mpv failed to initialize: {ErrorText(init)}.");
        }

        MpvNative.RequestLogMessages(handle, "warn");
        MpvNative.ObserveProperty(handle, PropTimePos, "time-pos", FormatDouble);
        MpvNative.ObserveProperty(handle, PropHwdecCurrent, "hwdec-current", FormatString);
        MpvNative.ObserveProperty(handle, PropFrameDrop, "frame-drop-count", FormatInt64);
        MpvNative.ObserveProperty(handle, PropDecoderDrop, "decoder-frame-drop-count", FormatInt64);
        MpvNative.ObserveProperty(handle, PropDelayed, "vo-delayed-frame-count", FormatInt64);

        _handle = handle;
        _eventThread = new Thread(() => EventLoop(handle)) { IsBackground = true, Name = "mpv events" };
        _eventThread.Start();
        return Result.Success();
    }

    private bool SetProperty(string name, string value, out string error)
    {
        var status = MpvNative.SetPropertyString(_handle, name, value);
        error = status < 0 ? $"mpv rejected {name}={value}: {ErrorText(status)}." : string.Empty;
        return status >= 0;
    }

    private void CancelScheduledStart()
    {
        _startTimer?.Dispose();
        _startTimer = null;
    }

    private void Resume()
    {
        lock (_gate)
        {
            if (_handle != 0 && _startTimer is not null)
            {
                _startTimer.Dispose();
                _startTimer = null;
                RunCommandAsync(_handle, "set", "pause", "no");
            }
        }
    }

    // ---- event thread ----

    private void EventLoop(nint handle)
    {
        try
        {
            while (!_stop)
            {
                var ev = MpvNative.WaitEvent(handle, 0.1);
                var id = Marshal.ReadInt32(ev);
                if (id == EventShutdown)
                {
                    break;
                }

                if (id == EventNone || _stop)
                {
                    continue;
                }

                try
                {
                    Handle(id, ev);
                }
                catch (Exception ex)
                {
                    LogEventFailure(ex);
                }
            }
        }
        catch (Exception ex)
        {
            LogEventFailure(ex);
        }
        finally
        {
            MpvNative.TerminateDestroy(handle);
        }
    }

    private void Handle(int id, nint ev)
    {
        var data = Marshal.ReadIntPtr(ev, 16);
        switch (id)
        {
            case EventStartFile:
                OnStartFile();
                break;
            case EventFileLoaded:
                if (_fileSequence == _currentLoad)
                {
                    _fileLoaded = true;
                    _firstFrameShown = false;
                    _loadError = null;
                    _loadDone.Set();
                }

                break;
            case EventPlaybackRestart:
                OnPlaybackRestart();
                break;
            case EventEndFile:
                OnEndFile(Marshal.ReadInt32(data, 0), Marshal.ReadInt32(data, 4));
                break;
            case EventPropertyChange:
                OnPropertyChange(Marshal.ReadInt64(ev, 8), data);
                break;
            case EventLogMessage:
                OnLogMessage(Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(data, 0)), Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(data, 16)));
                break;
            default:
                break;
        }
    }

    // True while events belong to the file the latest accepted Load asked for (not a replaced or abandoned one).
    private bool IsCurrentFile => _fileSequence == _currentLoad;

    // A new file starts: everything per-file begins from zero, on this thread, so no stale event can write after it.
    private void OnStartFile()
    {
        _fileSequence++;
        _fileLoaded = false;
        if (!IsCurrentFile)
        {
            return;
        }

        _firstFrameShown = false;
        _hardwareInactiveRaised = false;
        _hwdecCurrent = string.Empty;
        _frameDrop = 0;
        _decoderDrop = 0;
        _delayed = 0;
        _skippedDamaged = 0;
        _presented = null;
        _counters = FrameCounters.None;
    }

    private void OnPlaybackRestart()
    {
        if (!_fileLoaded || !IsCurrentFile)
        {
            return;
        }

        if (!_firstFrameShown)
        {
            // The first frame of a file is up: position zero, stamped now (a paused load holds it; a playing one moves on).
            _presented = new PresentedPicture(TimeSpan.Zero, _time.GetTimestamp());
        }

        _firstFrameShown = true;
        CheckHardware();
    }

    private void OnEndFile(int reason, int error)
    {
        var wasLoaded = _fileLoaded;
        _fileLoaded = false;
        if (!IsCurrentFile)
        {
            return;
        }

        if (reason == EndReasonError)
        {
            if (_loading && !wasLoaded)
            {
                _loadError = new Error("mpv.load_failed", $"mpv could not open '{_loadingFile}': {ErrorText(error)}.");
                _loadDone.Set();
            }
            else
            {
                Raise(() => DecodeFailed?.Invoke(new Error("video.decode_failed", $"The picture could not be decoded further: {ErrorText(error)}.")));
            }
        }
        else if (reason == EndReasonEof)
        {
            Raise(() => EndOfFile?.Invoke());
        }
    }

    private void OnPropertyChange(long property, nint data)
    {
        var format = Marshal.ReadInt32(data, 8);
        var value = Marshal.ReadIntPtr(data, 16);
        switch ((ulong)property)
        {
            case PropTimePos when format == FormatDouble && value != 0:
                var seconds = BitConverter.Int64BitsToDouble(Marshal.ReadInt64(value));
                if (_fileLoaded && IsCurrentFile)
                {
                    _presented = new PresentedPicture(TimeSpan.FromSeconds(seconds), _time.GetTimestamp());
                }

                break;
            case PropHwdecCurrent when format == FormatString && value != 0:
                _hwdecCurrent = Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(value)) ?? string.Empty;
                if (_firstFrameShown)
                {
                    CheckHardware();
                }

                break;
            case PropHwdecCurrent when format == FormatNone:
                // Unknown (no decoder yet or any more), never "software": a stale "no" must not fire for the next file.
                _hwdecCurrent = string.Empty;
                break;
            case PropFrameDrop when format == FormatInt64 && value != 0:
                _frameDrop = Marshal.ReadInt64(value);
                PublishCounters();
                break;
            case PropDecoderDrop when format == FormatInt64 && value != 0:
                _decoderDrop = Marshal.ReadInt64(value);
                PublishCounters();
                break;
            case PropDelayed when format == FormatInt64 && value != 0:
                _delayed = Marshal.ReadInt64(value);
                PublishCounters();
                break;
            default:
                break;
        }
    }

    // Damaged frames are not counted by an mpv property; the decoder reports each as a log line.
    private void OnLogMessage(string? prefix, string? text)
    {
        if (prefix is null || text is null || !_fileLoaded || !IsCurrentFile)
        {
            return;
        }

        var fromDecoder = prefix.StartsWith("ffmpeg/video", StringComparison.Ordinal) || prefix == "vd";
        if (fromDecoder && text.Contains("error while decoding", StringComparison.OrdinalIgnoreCase))
        {
            _skippedDamaged++;
            PublishCounters();
        }
    }

    private void PublishCounters() =>
        _counters = new FrameCounters(_delayed, _frameDrop + _decoderDrop, _skippedDamaged);

    // Hardware decoding only: a software decode is reported, never silently accepted.
    private void CheckHardware()
    {
        if (_hwdecCurrent == "no" && !_hardwareInactiveRaised)
        {
            _hardwareInactiveRaised = true;
            Raise(() => HardwareDecodingInactive?.Invoke());
        }
    }

    private void Raise(Action raise)
    {
        if (_stop)
        {
            return;
        }

        try
        {
            raise();
        }
        catch (Exception ex)
        {
            LogEventFailure(ex);
        }
    }

    private void LogEventFailure(Exception ex)
    {
        if (_logger is not null)
        {
            LogEventThreadFailure(_logger, ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "The mpv event thread failed on an event.")]
    private static partial void LogEventThreadFailure(ILogger logger, Exception exception);
}
