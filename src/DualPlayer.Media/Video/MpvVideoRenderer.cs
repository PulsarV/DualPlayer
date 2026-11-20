using System.Collections.Concurrent;
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
/// mpv reports positions but no timestamps for them, and its <c>time-pos</c> is the frame handed to the video output
/// ahead of its display, not the frame on screen. <see cref="Presented"/> is therefore the frame on mpv's display
/// schedule, anchored at the moments mpv applied a resume, speed change or restart (stamped with
/// <see cref="TimeProvider.GetTimestamp"/> when the event thread receives them, the same monotonic base Core uses) and
/// corrected by mpv's presentation feedback; <see cref="MpvPictureTimeline"/> documents the mpv semantics relied on.
/// Readings are frame- and event-latency granular (ADR-0002, negative consequences). A renderer owns one native window:
/// it is handed over with the first <see cref="Load"/>, and a later load with a different handle fails with
/// <c>mpv.load_failed</c>.
/// </remarks>
public sealed partial class MpvVideoRenderer : IVideoRenderer
{
    private const int EventNone = 0;
    private const int EventShutdown = 1;
    private const int EventLogMessage = 2;
    private const int EventCommandReply = 5;
    private const int EventStartFile = 6;
    private const int EventEndFile = 7;
    private const int EventFileLoaded = 8;
    private const int EventSeek = 20;
    private const int EventPlaybackRestart = 21;
    private const int EventPropertyChange = 22;

    private const int FormatNone = 0;
    private const int FormatString = 1;
    private const int FormatFlag = 3;
    private const int FormatInt64 = 4;
    private const int FormatDouble = 5;

    private const int EndReasonEof = 0;
    private const int EndReasonError = 4;

    private const ulong PropTimePos = 1;
    private const ulong PropHwdecCurrent = 2;
    private const ulong PropFrameDrop = 3;
    private const ulong PropDecoderDrop = 4;
    private const ulong PropPresentationDelay = 6;
    private const ulong PropEofReached = 7;

    // Reply tags of the commands whose moment of effect anchors the display schedule (0 = reply ignored).
    private const ulong ReplyPause = 1;
    private const ulong ReplyResume = 2;
    private const ulong ReplySpeed = 3;
    private const ulong ReplyStartSeek = 4;
    private const ulong ReplyKindMask = 0xFF;
    private const int ReplyIdShift = 8;

    private const int NoLoad = -1;

    private static readonly TimeSpan LoadTimeout = TimeSpan.FromSeconds(15);

    // A scheduled start wakes this much early and waits out the rest itself: a timer may fire a system tick (15.6 ms on
    // Windows by default) early or late.
    private static readonly TimeSpan StartWaitMargin = TimeSpan.FromMilliseconds(20);

    private readonly TimeProvider _time;
    private readonly IReadOnlyDictionary<string, string> _overrides;
    private readonly ILogger? _logger;
    private readonly Lock _gate = new();
    private readonly ManualResetEventSlim _loadDone = new();
    private readonly ConcurrentDictionary<long, double> _speedRequests = new();
    private readonly MpvPictureTimeline _timeline;

    // Guards the timeline's presentation-feedback log, the one part of it read off the event thread.
    private readonly Lock _delayGate = new();

    private nint _handle;
    private Thread? _eventThread;
    private ITimer? _startTimer;

    // Under _gate: the start being played (PlayAt) until its unpause is issued, and what it is planned with.
    private StartPlan? _plan;
    private long _planIds;
    private long _speedIds;
    private bool _playing;
    private double _requestedSpeed = 1.0;
    private TimeSpan? _nextSeekBudgetOverride;
    private readonly List<TimeSpan> _seekTimes = [];

    // The start whose picture is held paused until its unpause is applied (0 = none); read by diagnostics.
    private long _heldPlan;
    private volatile StartRecord? _lastStart;

    // Event thread: the plan the timeline holds, the start whose unpause is scheduled, and whether this file's
    // presentation feedback has been logged.
    private long _timelinePlanId;
    private StartRecord? _scheduledStart;
    private bool _delayLogged;
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
        _timeline = new MpvPictureTimeline(time.TimestampFrequency);
    }

    /// <summary>How long an on-screen note is held after its last change (sad §8); fixed adapter policy.</summary>
    public static TimeSpan NoteHold => TimeSpan.FromSeconds(2);

    public PresentedPicture? Presented => _presented;

    /// <summary>
    /// Per-file frame counters, reset for each load: dropped and skipped-damaged. <c>Late</c> is always 0 here — mpv's
    /// <c>vo-delayed-frame-count</c> counts only in display-sync mode — and the playback session counts it against due
    /// time (spec §6).
    /// </summary>
    public FrameCounters Counters => _counters;

    /// <summary>
    /// True from a start (<see cref="PlayAt"/>, a seek while playing, a playing load) until mpv applied its unpause: the
    /// screen still shows a paused frame, and <see cref="Presented"/> is a prediction stamped with the planned moment.
    /// </summary>
    internal bool HoldingStart => Interlocked.Read(ref _heldPlan) != 0;

    /// <summary>The last start mpv unpaused: when it was due, when the unpause was applied, and the seek behind it.</summary>
    internal StartRecord? LastStart => _lastStart;

    /// <summary>Tests only: the budget of the next planned seek, replacing the policy once (forces an overrun).</summary>
    internal TimeSpan? NextSeekBudgetOverride
    {
        get
        {
            lock (_gate)
            {
                return _nextSeekBudgetOverride;
            }
        }

        set
        {
            lock (_gate)
            {
                _nextSeekBudgetOverride = value;
            }
        }
    }

    /// <summary>mpv's presentation feedback for the current file (<see cref="MpvPictureTimeline.PresentationDelays"/>).</summary>
    internal PresentationDelayStats PresentationDelays
    {
        get
        {
            lock (_delayGate)
            {
                return _timeline.PresentationDelays;
            }
        }
    }

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

        // At the end, and after a seek past it, mpv pauses on the last frame instead of tearing the video output down,
        // so the picture and any note stay on screen (SCR-02); the end is reported through eof-reached.
        new("keep-open", "yes"),
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
            _playing = !startPaused;
            Interlocked.Exchange(ref _heldPlan, 0);

            // Always opened paused: a playing load is started by a planned start once it is open, so the frame the display
            // schedule starts from is read while nothing is queued behind it.
            CancelScheduledStart();
            if (!SetProperty("pause", "yes", out var pauseError))
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

        if (_loadError is { } error)
        {
            return Result.Failure(error);
        }

        lock (_gate)
        {
            if (_handle != 0 && _playing && _plan is null && _currentLoad == generation)
            {
                // Position zero from the moment a seek to it is expected to be done.
                var now = _time.GetTimestamp();
                PlanStartLocked(TimeSpan.Zero, now + ToTimestamp(PolicyBudgetLocked()));
            }
        }

        return Result.Success();
    }

    /// <summary>
    /// Holds the picture paused, seeks, and unpauses when the start reaches the frame the seek landed on (an exact seek
    /// lands on the first frame at or after its target). A start nearer than the seek takes (measured per renderer) is
    /// moved: the seek goes to the content the start reaches once the seek is done, so the picture is held a little
    /// longer and then shows every frame at its moment (<see cref="MpvStartPlan"/>). A seek that overruns its budget is
    /// not followed by a late unpause: the start is re-planned with a fresh seek ahead (up to
    /// <see cref="MpvStartPlan.MaxReplans"/> times, then it unpauses late with a warning and Core corrects the rest).
    /// </summary>
    public void PlayAt(TimeSpan position, long startTimestamp)
    {
        lock (_gate)
        {
            if (_handle == 0)
            {
                return;
            }

            _playing = true;
            PlanStartLocked(position, startTimestamp);
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

            _playing = false;
            CancelScheduledStart();
            Interlocked.Exchange(ref _heldPlan, 0);
            RunCommandAsync(_handle, ReplyPause, "set", "pause", "yes");
        }
    }

    /// <summary>
    /// Paused, the picture moves to <paramref name="position"/> and stays. Playing, <paramref name="position"/> is due now:
    /// the seek is played as a start (<see cref="PlayAt"/>), so the picture resumes on that schedule. During a scheduled
    /// start, the start stays scheduled and resumes from the new position at the same moment.
    /// </summary>

    public void Seek(TimeSpan position)
    {
        lock (_gate)
        {
            if (_handle == 0)
            {
                return;
            }

            if (_plan is { } plan)
            {
                PlanStartLocked(position, plan.Start);
            }
            else if (_playing)
            {
                PlanStartLocked(position, _time.GetTimestamp());
            }
            else
            {
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
                // The reply carries the request's id, so it is paired with its speed even when a submission fails.
                _requestedSpeed = speed;
                var id = ++_speedIds;
                _speedRequests[id] = speed;
                if (RunCommandAsync(_handle, Tag(ReplySpeed, id), "set", "speed", speed.ToString("R", CultureInfo.InvariantCulture)) < 0)
                {
                    _ = _speedRequests.TryRemove(id, out _);
                }
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

    private static ulong Tag(ulong kind, long id) => kind | ((ulong)id << ReplyIdShift);

    private long ToTimestamp(TimeSpan span) => (long)Math.Round(span.TotalSeconds * _time.TimestampFrequency);

    private static string ErrorText(int status) => Marshal.PtrToStringUTF8(MpvNative.ErrorString(status)) ?? $"error {status}";

    private static int RunCommand(nint ctx, params string[] args) => Run(ctx, async: false, 0, args);

    // Queued by mpv and executed in order on its side; the caller never waits for the command. Replies are ignored events.
    private static int RunCommandAsync(nint ctx, params string[] args) => Run(ctx, async: true, 0, args);

    // As above, but the reply carries replyTag, so the event thread learns when mpv applied the command.
    private static int RunCommandAsync(nint ctx, ulong replyTag, params string[] args) => Run(ctx, async: true, replyTag, args);

    private static int Run(nint ctx, bool async, ulong replyTag, string[] args)
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
                    return async ? MpvNative.CommandAsync(ctx, replyTag, (nint)array) : MpvNative.Command(ctx, (nint)array);
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
        MpvNative.ObserveProperty(handle, PropPresentationDelay, "vo-presentation-delay", FormatDouble);
        MpvNative.ObserveProperty(handle, PropEofReached, "eof-reached", FormatFlag);

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
        _plan = null;
    }

    // Caller holds _gate.
    private TimeSpan PolicyBudgetLocked() => MpvStartPlan.SeekBudget(MpvStartPlan.SeekTimeTail(_seekTimes));

    // Caller holds _gate. Pause, then seek; the seek's reply hands the plan to the event thread, whose restart schedules
    // the unpause (ScheduleUnpause). A re-plan carries the overruns of the seeks before it.
    private void PlanStartLocked(TimeSpan position, long startTimestamp, IReadOnlyList<SeekOverrun>? overruns = null)
    {
        CancelScheduledStart();
        var now = _time.GetTimestamp();
        var budget = _nextSeekBudgetOverride ?? PolicyBudgetLocked();
        _nextSeekBudgetOverride = null;
        var target = MpvStartPlan.SeekTarget(position, startTimestamp, now, budget, _requestedSpeed, _time.TimestampFrequency);
        var plan = new StartPlan(++_planIds, position, startTimestamp, now, budget, overruns ?? []);
        _plan = plan;
        Interlocked.Exchange(ref _heldPlan, plan.Id);
        RunCommandAsync(_handle, ReplyPause, "set", "pause", "yes");
        RunCommandAsync(_handle, Tag(ReplyStartSeek, plan.Id), "seek", Seconds(target), "absolute+exact");
    }

    // Event thread, after a restart under the plan the timeline was given (restartedAt null: no seek was done, so there
    // is no seek time to learn from).
    private void ScheduleUnpause(long planId, long due, long? restartedAt)
    {
        lock (_gate)
        {
            if (_handle == 0 || _plan is not { } plan || plan.Id != planId)
            {
                return;
            }

            TimeSpan? seekTime = restartedAt is { } restarted ? _time.GetElapsedTime(plan.IssuedAt, restarted) : null;
            if (seekTime is { } measured)
            {
                _seekTimes.Add(measured);
                if (_seekTimes.Count > MpvStartPlan.SeekHistory)
                {
                    _seekTimes.RemoveAt(0);
                }

                // A seek that overran its budget landed on a frame already past due: unpausing now would resume late by
                // the overrun, more than Core's speed nudge closes in a second. Seek again ahead instead, with a budget
                // that has learnt this seek, so the picture resumes on its due frame whatever the seek took.
                var now = _time.GetTimestamp();
                var action = MpvStartPlan.AtRestart(due, now, plan.Overruns.Count, _time.TimestampFrequency);
                if (action != StartAction.Unpause)
                {
                    var overruns = plan.Overruns.Append(new SeekOverrun(plan.Budget, measured)).ToList();
                    var lateMs = _time.GetElapsedTime(due, now).TotalMilliseconds;
                    if (action == StartAction.Replan)
                    {
                        if (_logger is not null)
                        {
                            LogSeekOverrun(_logger, measured.TotalMilliseconds, plan.Budget.TotalMilliseconds, lateMs, overruns.Count, MpvStartPlan.MaxReplans);
                        }

                        PlanStartLocked(plan.Position, plan.Start, overruns);
                        return;
                    }

                    if (_logger is not null)
                    {
                        LogStartUnpausedLate(_logger, plan.Overruns.Count, lateMs);
                    }

                    plan = plan with { Overruns = overruns };
                }
            }

            _scheduledStart = new StartRecord(planId, due, 0, plan.Budget, seekTime, plan.Overruns);
            var delay = _time.GetElapsedTime(_time.GetTimestamp(), due);
            if (delay <= TimeSpan.Zero)
            {
                _plan = null;
                RunCommandAsync(_handle, Tag(ReplyResume, planId), "set", "pause", "no");
                return;
            }

            var wake = delay > StartWaitMargin ? delay - StartWaitMargin : TimeSpan.Zero;
            _startTimer?.Dispose();
            _startTimer = _time.CreateTimer(_ => Resume(planId, due), null, wake, Timeout.InfiniteTimeSpan);
        }
    }

    private void Resume(long planId, long startTimestamp)
    {
        ITimer? timer;
        lock (_gate)
        {
            timer = _startTimer;
            if (_handle == 0 || timer is null)
            {
                return;
            }
        }

        WaitUntil(startTimestamp);

        lock (_gate)
        {
            if (_handle != 0 && _startTimer is not null && _startTimer == timer)
            {
                CancelScheduledStart();
                RunCommandAsync(_handle, Tag(ReplyResume, planId), "set", "pause", "no");
            }
        }
    }

    // Spins out the last stretch of a scheduled start (the margin plus at most a timer tick) on the injected clock; the
    // real-time cap keeps a clock that does not move (a fake) from holding the thread.
    private void WaitUntil(long timestamp)
    {
        var cap = Environment.TickCount64 + 100;
        while (_time.GetTimestamp() < timestamp && Environment.TickCount64 < cap)
        {
            _ = Thread.Yield();
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
                    Handle(handle, id, ev);
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
            if (_fileLoaded)
            {
                LogPresentationDelays();
            }

            MpvNative.TerminateDestroy(handle);
        }
    }

    private void Handle(nint handle, int id, nint ev)
    {
        var data = Marshal.ReadIntPtr(ev, 16);
        switch (id)
        {
            case EventStartFile:
                OnStartFile();
                break;
            case EventCommandReply:
                OnCommandReply((ulong)Marshal.ReadInt64(ev, 8), Marshal.ReadInt32(ev, 4));
                break;
            case EventSeek:
                if (_fileLoaded && IsCurrentFile)
                {
                    _timeline.SeekStarted();
                }

                break;
            case EventFileLoaded:
                if (_fileSequence == _currentLoad)
                {
                    _fileLoaded = true;
                    _delayLogged = false;
                    _firstFrameShown = false;
                    _loadError = null;
                    _loadDone.Set();
                }

                break;
            case EventPlaybackRestart:
                OnPlaybackRestart(handle);
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
        _skippedDamaged = 0;
        lock (_delayGate)
        {
            _timeline.Reset(paused: true);
        }

        _presented = null;
        _counters = FrameCounters.None;
    }

    private void OnPlaybackRestart(nint handle)
    {
        if (!_fileLoaded || !IsCurrentFile)
        {
            return;
        }

        // The restart frame is up (mpv shows it before it reports the restart) and is the frame mpv reports now: position
        // zero for the first frame of a file, unless a seek came first. mpv's display schedule starts from it. This read
        // is exact only while paused (playing, mpv may already have queued the next frame), which is why every start and
        // every seek while playing is played paused first (PlanStartLocked). A failed read leaves it to the timeline.
        var at = _time.GetTimestamp();
        TimeSpan? shown = MpvNative.GetPropertyDouble(handle, "time-pos", FormatDouble, out var seconds) >= 0
            ? TimeSpan.FromSeconds(seconds)
            : null;
        _presented = _timeline.Restarted(shown, at);
        if (_timeline.UnpauseDue is { } due)
        {
            ScheduleUnpause(_timelinePlanId, due, at);
        }

        _firstFrameShown = true;
        CheckHardware();
    }

    private void OnEndFile(int reason, int error)
    {
        var wasLoaded = _fileLoaded;
        if (wasLoaded)
        {
            LogPresentationDelays();
        }

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
                    Publish(_timeline.Queued(TimeSpan.FromSeconds(seconds), _time.GetTimestamp()));
                }

                break;
            case PropEofReached when format == FormatFlag && value != 0:
                if (Marshal.ReadInt32(value) != 0 && _fileLoaded && IsCurrentFile)
                {
                    Raise(() => EndOfFile?.Invoke());
                }

                break;
            case PropPresentationDelay:
                TimeSpan? delay = format == FormatDouble && value != 0
                    ? TimeSpan.FromSeconds(BitConverter.Int64BitsToDouble(Marshal.ReadInt64(value)))
                    : null;
                lock (_delayGate)
                {
                    _timeline.PresentationDelay(delay);
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
            default:
                break;
        }
    }

    private void OnCommandReply(ulong tag, int error)
    {
        var at = _time.GetTimestamp();
        switch (tag & ReplyKindMask)
        {
            case ReplyStartSeek:
                OnStartSeekQueued((long)(tag >> ReplyIdShift), error, at);
                break;
            case ReplySpeed:
                if (_speedRequests.TryRemove((long)(tag >> ReplyIdShift), out var speed) && error >= 0)
                {
                    _timeline.SpeedApplied(speed, at);
                }

                break;
            case ReplyResume:
                OnResumed((long)(tag >> ReplyIdShift), error, at);
                break;
            case ReplyPause when error >= 0 && _fileLoaded && IsCurrentFile:
                Publish(_timeline.Paused(at));
                break;
            default:
                break;
        }
    }

    // mpv took the seek of a start (its reply comes before the seek runs): from here its restart is the start's.
    private void OnStartSeekQueued(long planId, int error, long at)
    {
        StartPlan? plan;
        lock (_gate)
        {
            plan = _plan is { } current && current.Id == planId ? current : null;
        }

        if (plan is null || !_fileLoaded || !IsCurrentFile)
        {
            return;
        }

        if (error < 0)
        {
            // No seek, no restart to wait for: start from where the picture is.
            ScheduleUnpause(planId, at, restartedAt: null);
            return;
        }

        _timelinePlanId = planId;
        _timeline.PlanStart(plan.Position, plan.Start);
    }

    // mpv applied the unpause of a start: the held picture moves from here.
    private void OnResumed(long planId, int error, long at)
    {
        _ = Interlocked.CompareExchange(ref _heldPlan, 0, planId);
        if (error < 0 || !_fileLoaded || !IsCurrentFile)
        {
            return;
        }

        Publish(_timeline.Resumed(at));
        if (_scheduledStart is { } scheduled && scheduled.Id == planId)
        {
            _lastStart = scheduled with { ResumedAt = at };
            _scheduledStart = null;
        }
    }

    // One structured event per file: how much presentation feedback mpv gave (0 samples = the property is missing or
    // the output gives none, so the display schedule runs uncorrected).
    private void LogPresentationDelays()
    {
        if (_delayLogged || _logger is null)
        {
            return;
        }

        _delayLogged = true;
        var stats = PresentationDelays;
        LogPresentationFeedback(_logger, stats.Samples, stats.Used, stats.MedianMs);
    }

    private void Publish(PresentedPicture? picture)
    {
        if (picture is not null)
        {
            _presented = picture;
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
        _counters = new FrameCounters(0, _frameDrop + _decoderDrop, _skippedDamaged);

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

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "mpv presentation feedback for the file: {PresentationDelaySamples} samples, {PresentationDelayUsed} used, median {PresentationDelayMedianMs} ms")]
    private static partial void LogPresentationFeedback(ILogger logger, int presentationDelaySamples, int presentationDelayUsed, double? presentationDelayMedianMs);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "A start's seek took {SeekMs} ms of a {SeekBudgetMs} ms budget and landed {LateMs} ms past due; re-planned ({Replan} of {MaxReplans})")]
    private static partial void LogSeekOverrun(ILogger logger, double seekMs, double seekBudgetMs, double lateMs, int replan, int maxReplans);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "A start's seek overran its budget after {Replans} re-plans; unpaused {LateMs} ms late, the picture loop corrects the rest")]
    private static partial void LogStartUnpausedLate(ILogger logger, int replans, double lateMs);

    /// <summary>
    /// A start being played: content <c>Position</c> due at <c>Start</c>; its seek was issued at <c>IssuedAt</c> with
    /// <c>Budget</c> allowed for it, after the <c>Overruns</c> of the seeks this start was re-planned from.
    /// </summary>
    private sealed record StartPlan(long Id, TimeSpan Position, long Start, long IssuedAt, TimeSpan Budget, IReadOnlyList<SeekOverrun> Overruns);
}

/// <summary>A start's seek that took longer (<c>Took</c>) than its <c>Budget</c> allowed for.</summary>
internal sealed record SeekOverrun(TimeSpan Budget, TimeSpan Took);

/// <summary>
/// A start the renderer played (diagnostics): <c>UnpauseDue</c> is when the held frame was due, <c>ResumedAt</c> when mpv
/// applied the unpause (the screen moves from there), with the seek budget allowed and the seek time measured (null when
/// no seek was done) of its last seek, and the <c>Overruns</c>: one per re-plan, plus the last seek when it overran too
/// after the re-plans were used up (then the unpause was late).
/// </summary>
internal sealed record StartRecord(long Id, long UnpauseDue, long ResumedAt, TimeSpan SeekBudget, TimeSpan? SeekTime, IReadOnlyList<SeekOverrun> Overruns)
{
    /// <summary>How many times the start was re-planned (at most <see cref="MpvStartPlan.MaxReplans"/>).</summary>
    public int Replans => Math.Min(Overruns.Count, MpvStartPlan.MaxReplans);
}
