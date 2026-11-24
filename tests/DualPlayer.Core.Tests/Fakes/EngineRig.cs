using DualPlayer.Core.Diagnostics;
using DualPlayer.Core.Outputs;
using DualPlayer.Core.Playback;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Shouldly;

namespace DualPlayer.Core.Tests.Fakes;

/// <summary>One fake output device: its sample rate, how fast its clock runs and how late its position starts moving.</summary>
internal readonly record struct RigDevice(int Rate = 48_000, double Ppm = 0, TimeSpan StartLatency = default);

/// <summary>
/// Drives a real <see cref="AudioEngine"/> on a <see cref="FakeTimeProvider"/> with realistic fakes: devices of different
/// rates, clock speeds and start latencies, a decoder with mismatched block sizes and early-landing seeks. Fake time moves
/// in 10 ms steps; after each step the devices are asked for audio until they hold a little more than they have played,
/// the decoder is waited for until it is a second ahead (so a slow test machine cannot cause an underrun the real engine
/// would not have) and, every <c>barrierEvery</c> steps, the engine-control loop is waited for until it has handled
/// everything queued (so the 100 ms sync samples are taken at the instant the timer fired). Device n is "dev-n" and
/// plays track ((n - 1) % 2) + 1.
/// </summary>
internal sealed class EngineRig : IDisposable
{
    public static readonly TimeSpan Step = TimeSpan.FromMilliseconds(10);

    private static readonly IReadOnlyList<AudioTrackInfo> Tracks =
    [
        new AudioTrackInfo(1, "eng", ChannelLayout.Stereo, 48_000),
        new AudioTrackInfo(2, "ukr", ChannelLayout.Stereo, 48_000),
        new AudioTrackInfo(3, "deu", ChannelLayout.Stereo, 48_000),
    ];

    private readonly DeviceRegistry _devices = new();
    private readonly List<EngineEvent> _events = [];
    private readonly Lock _gate = new();
    private readonly RigDevice[] _specs;
    private readonly RealisticAudioDecoder _decoder;
    private readonly DateTimeOffset _origin;
    private readonly int _barrierEvery;
    private readonly List<(TimeSpan At, long[] Played)> _observations = [];
    private readonly bool _observe;
    private readonly FakeAudioBackend _fakeBackend;
    private TimeSpan _positionBase;
    private TimeSpan _positionSince;
    private bool _paused;
    private int _steps;

    public EngineRig(
        RigDevice[] devices,
        double totalSeconds = 600,
        int[]? blockFrames = null,
        TimeSpan[]? seekLandsEarly = null,
        TimeSpan[]? seekLandsLate = null,
        TimeSpan[]? startLandsLate = null,
        bool keepRendered = true,
        bool observe = false,
        int barrierEvery = 1)
    {
        _specs = devices;
        _observe = observe;
        _barrierEvery = barrierEvery;
        Time = new CountingTimeProvider();
        var backend = new FakeAudioBackend(Time, new MixFormat(48_000, 2, ChannelLayout.Stereo)) { KeepRendered = keepRendered };
        for (var i = 0; i < devices.Length; i++)
        {
            var id = $"dev-{i + 1}";
            backend.AddDevice(new AudioDeviceInfo($"Device {i + 1}", null, id));
            backend.SetFormat(id, new MixFormat(devices[i].Rate, 2, ChannelLayout.Stereo));
            backend.SetPpm(id, devices[i].Ppm);
            backend.SetStartLatency(id, devices[i].StartLatency);
        }

        _decoder = new RealisticAudioDecoder(Tracks, totalSeconds, blockFrames ?? [1536, 1024], seekLandsEarly, seekLandsLate, startLandsLate);
        Backend = backend;
        _fakeBackend = backend;
        Engine = new AudioEngine(new CapturingBackend(backend, _devices), _decoder, Time, NullLogger<AudioEngine>.Instance);
        Engine.Events.Subscribe(new Collector(this));
        _origin = Time.GetUtcNow();
    }

    public CountingTimeProvider Time { get; }

    /// <summary>
    /// When set (a session rig), fake time stands still during a command until a fake signals that it waits on it, with no real-time
    /// cap. When clear (a bare engine rig), the engine's own gate applies: any new timer, or 2 s of real time.
    /// </summary>
    public bool StrictGate { get; set; }

    public IAudioBackend Backend { get; }

    /// <summary>Device <paramref name="number"/> (dev-n) is lost the moment the engine starts it: in the pre-roll, before its first audio.</summary>
    public void LoseOutputInPreRoll(int number) => _fakeBackend.LoseOnStart($"dev-{number}");

    /// <summary>Device <paramref name="number"/> (dev-n) refuses to open (access denied) until <see cref="AllowAccess"/>.</summary>
    public void DenyAccess(int number) => _fakeBackend.DenyAccess($"dev-{number}");

    public void AllowAccess(int number) => _fakeBackend.AllowAccess($"dev-{number}");

    /// <summary>Device <paramref name="number"/> (dev-n) leaves the device list (a run already playing on it keeps it open).</summary>
    public void Unlist(int number) => _fakeBackend.RemoveDevice($"dev-{number}");

    /// <summary>Device <paramref name="number"/> (dev-n) is listed again, at the end of the list.</summary>
    public void Relist(int number) => _fakeBackend.AddDevice(new AudioDeviceInfo($"Device {number}", null, $"dev-{number}"));

    public AudioEngine Engine { get; }

    /// <summary>A snapshot of the current run's outputs: the engine loop may re-open them while a reader walks the list.</summary>
    public IReadOnlyList<FakeAudioDevice> Devices => _devices.Snapshot();

    public TimeSpan Elapsed => Time.GetUtcNow() - _origin;

    /// <summary>When the master clock read 00:00 (rig time): the start command plus the engine's start-to-audio (ADR-0008).</summary>
    public TimeSpan ClockStart { get; private set; }

    /// <summary>Where the master clock should read now, as the test knows it from the commands it gave.</summary>
    public TimeSpan Position => _paused ? _positionBase : _positionBase + (Elapsed - _positionSince);

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

    public IReadOnlyList<SyncRecord> Records => [.. Events.OfType<SyncRecord>()];

    public IReadOnlyList<SyncRecord> RecordsOf(string deviceId) => [.. Records.Where(r => r.DeviceId == deviceId)];

    public RunSummary? Summary => Events.OfType<RunSummary>().FirstOrDefault();

    public void Start(EngineOptions? options = null)
    {
        var assignments = Enumerable.Range(0, _specs.Length)
            .Select(i => new OutputAssignment((i % 2) + 1, DeviceRef.ByNumber(i + 1)))
            .ToArray();
        var commandAt = Elapsed;
        Engine.StartRun("film.mkv", assignments, options ?? new EngineOptions()).IsSuccess.ShouldBeTrue();
        _positionBase = TimeSpan.Zero;
        _paused = true; // the clock holds 00:00 through the silent pre-roll
        Fill();

        // The engine starts the clock when the slowest output hears film frame 0; the rig takes that instant from it.
        for (var i = 0; i < 700 && !Events.OfType<AudioStarted>().Any(); i++)
        {
            Advance(barrier: true);
        }

        var started = Events.OfType<AudioStarted>().FirstOrDefault();
        started.ShouldNotBeNull("the master clock never started");
        ClockStart = commandAt + started.StartToAudio;
        _positionSince = ClockStart;
        _paused = false;
    }

    /// <summary>Advances fake time by <paramref name="steps"/> x 10 ms.</summary>
    public void Render(int steps)
    {
        for (var i = 0; i < steps; i++)
        {
            Advance(barrier: true);
        }
    }

    /// <summary>Runs <paramref name="command"/> on its own thread while fake time moves on until it has finished.</summary>
    public T Call<T>(Func<T> command)
    {
        using var started = new ManualResetEventSlim();
        var signals = Time.Signals;
        Time.BeginCall();
        var task = Task.Factory.StartNew(
            () =>
            {
                Time.WatchCommandThread(Environment.CurrentManagedThreadId);
                started.Set();
                try
                {
                    return command();
                }
                finally
                {
                    Time.WatchCommandThread(0);
                }
            },
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
        started.Wait(TimeSpan.FromSeconds(10)).ShouldBeTrue("the command thread never started");

        // Fake time stands still until the command waits on it, or finishes: a worker the pool starts late must see the time the
        // command began at. A session rig waits for an explicit signal from a fake (a scripted decoder or probe spending fake time,
        // an engine command) or a timer the command waits on; real time and an unrelated timer never open the gate. It sleeps until
        // something it looks at changes; a command that neither finishes nor waits fails the test after 60 s, it never moves time.
        // A bare engine rig keeps the engine's own gate: any new timer, or 2 s.
        if (StrictGate)
        {
            _ = task.ContinueWith(_ => Time.Pulse(), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            while (!task.IsCompleted && Time.Signals == signals)
            {
                if (Time.Waiters > 0)
                {
                    // A fake is spending fake time: it moves only while one is, and never past the earliest deadline of a fake that
                    // spins to one: that fake, however late its thread is scheduled, sees its deadline and leaves its scope first.
                    var room = Time.RoomBeforeDeadline();
                    if (room is null || room > TimeSpan.Zero)
                    {
                        Advance(barrier: false, room is { } r && r < Step ? r : Step);
                    }

                    PaceToDecoder();
                }
                else
                {
                    Time.WaitForPulse(TimeSpan.FromSeconds(60)).ShouldBeTrue("the command neither finished nor waited on fake time within 60 s");
                }
            }
        }
        else
        {
            var timers = Time.TimersCreated;
            var gate = DateTime.UtcNow + TimeSpan.FromSeconds(2);
            while (!task.IsCompleted && Time.TimersCreated == timers && DateTime.UtcNow < gate)
            {
                task.Wait(5);
            }
        }

        for (var i = 0; i < 3_000 && !task.IsCompleted; i++)
        {
            Thread.Sleep(1); // the command, woken by its own signal, reaches its read of the time before the clock moves
            Advance(barrier: false);
        }

        task.IsCompleted.ShouldBeTrue("the command never completed");
        return task.GetAwaiter().GetResult();
    }

    /// <summary>
    /// Moves fake time on, without barriers, until <paramref name="condition"/> holds — for a command that runs on another
    /// thread (a queued window key) and waits for time itself, as <see cref="Call{T}"/> does for its own command.
    /// </summary>
    public bool AdvanceUntil(Func<bool> condition, int maxSteps = 3_000)
    {
        for (var i = 0; i < maxSteps; i++)
        {
            if (condition())
            {
                return true;
            }

            Advance(barrier: false);
            Thread.Sleep(1);
        }

        return condition();
    }

    /// <summary>Seeks and tells the rig where the master clock now is (it jumps when the command starts).</summary>
    public SeekResult Seek(TimeSpan target)
    {
        _positionBase = target;
        _positionSince = Elapsed;
        _paused = false;
        var result = Call(() => Engine.Seek(target));
        result.IsSuccess.ShouldBeTrue(result.Error?.Message);
        return result.Value;
    }

    public void Pause()
    {
        var result = Call(Engine.Pause);
        result.IsSuccess.ShouldBeTrue(result.Error?.Message);
        _positionBase = result.Value;
        _paused = true;
    }

    public void Resume()
    {
        var result = Call(Engine.Resume);
        result.IsSuccess.ShouldBeTrue(result.Error?.Message);
        _positionBase = result.Value;
        _positionSince = Elapsed;
        _paused = false;
    }

    /// <summary>Tells the rig a transport command is starting: where the master clock now reads (the paused state stays).</summary>
    public void CommandStarting(TimeSpan position)
    {
        _positionBase = position;
        _positionSince = Elapsed;
    }

    /// <summary>Tells the rig where the master clock stands after a transport command finished.</summary>
    public void CommandDone(TimeSpan position, bool paused)
    {
        _positionBase = position;
        _positionSince = Elapsed;
        _paused = paused;
    }

    public void Stop(RunEndReason reason)
    {
        Call(() =>
        {
            Engine.Stop(reason);
            return 0;
        });
    }

    /// <summary>
    /// Waits until the engine has handled everything queued so far (a round trip through the engine's loop; strict: the run
    /// must still be active), then runs <see cref="AfterStep"/>.
    /// </summary>
    public void Barrier()
    {
        EngineBarrier();
        AfterStep?.Invoke();
    }

    private void EngineBarrier()
    {
        if (EndsAllowed)
        {
            // A round trip through the engine's loop: everything queued before it has run. No run is started, so a film
            // that ended meanwhile leaves no phantom run behind.
            _ = Engine.FrameZeroTimestamp;
            return;
        }

        // Strict (step 3): the run must still be active. The same round trip through the engine's loop, which answers with the
        // outputs of the current run (none once the run has ended, pre-roll included while it runs). No start is attempted: a probe
        // start of an ended run would succeed on the fake decoder and leave a phantom run behind.
        Engine.PositionErrors().ShouldNotBeEmpty($"the run is no longer active; it ended with {Summary?.EndReason.ToString() ?? "no summary"} at {Summary?.ClockPosition}");
    }

    /// <summary>Waits for <paramref name="condition"/>; once it holds, also for <see cref="AfterStep"/> (what the session still has to handle).</summary>
    public bool WaitFor(Func<bool> condition, int milliseconds = 10_000)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromMilliseconds(milliseconds);
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                AfterStep?.Invoke();
                return true;
            }

            Thread.Sleep(2);
        }

        var held = condition();
        if (held)
        {
            AfterStep?.Invoke();
        }

        return held;
    }

    /// <summary>
    /// The largest distance, in ms, between what the device played and the master clock, over every observed step whose time
    /// lies in [<paramref name="from"/>, <paramref name="to"/>] after <paramref name="commandAt"/> (the master clock read
    /// <paramref name="positionAtCommand"/> then). Needs <c>observe</c>; silence where audio belongs counts as a huge error.
    /// </summary>
    public double MaxDriftMs(int deviceIndex, TimeSpan commandAt, TimeSpan positionAtCommand, TimeSpan from, TimeSpan to)
    {
        var device = _devices.Snapshot()[deviceIndex];
        var rate = _specs[deviceIndex].Rate;
        var max = 0.0;
        var seen = 0;
        foreach (var (at, played) in _observations)
        {
            if (at < commandAt + from || at > commandAt + to)
            {
                continue;
            }

            seen++;
            var frame = played[deviceIndex];
            var heard = frame < device.Rendered.Count ? Math.Round(device.Rendered[(int)frame] * 1000.0) / rate : double.NaN;
            var expected = (positionAtCommand + (at - commandAt)).TotalSeconds;
            max = Math.Max(max, double.IsNaN(heard) ? double.PositiveInfinity : Math.Abs(heard - expected) * 1000);
        }

        seen.ShouldBeGreaterThan(0, "no observations in the window");
        return max;
    }

    /// <summary>
    /// Set by a session rig: the film may end on its own or by a session command while a test renders, so the barrier only
    /// waits for the engine's loop. Engine tests keep the strict barrier, which fails once the run has ended.
    /// </summary>
    public bool EndsAllowed { get; set; }

    /// <summary>Runs after every <see cref="Render"/> step (a session rig waits there for what the step posted to the session).</summary>
    public Action? AfterStep { get; set; }

    public void Dispose() => Engine.Dispose();

    private void Advance(bool barrier, TimeSpan? step = null)
    {
        Time.Advance(step ?? Step);
        Fill();
        if (barrier)
        {
            WaitForDecoder();
        }

        if (_observe)
        {
            _observations.Add((Elapsed, [.. _devices.Snapshot().Select(d => d.PlayedFramesNow)]));
        }

        if (barrier && ++_steps % _barrierEvery == 0)
        {
            EngineBarrier();
        }

        if (barrier)
        {
            AfterStep?.Invoke();
        }
    }

    private void Fill()
    {
        foreach (var device in _devices.Snapshot())
        {
            if (device.IsClosed || device.IsHung || device.IsLost)
            {
                continue;
            }

            var chunk = device.MixFormat.SampleRate / 100;
            RigLoops.FillQueue(device, 2 * chunk, chunk);
        }
    }

    /// <summary>
    /// Gives the playing film's decoder the real time it needs to stay ahead of fake time, as <see cref="WaitForDecoder"/> does between
    /// rendered steps, but never fails and never waits longer than a few ms (a stopped or seeking run has no decoder to wait for).
    /// </summary>
    private void PaceToDecoder()
    {
        var wanted = Math.Min(Position.TotalSeconds + 1.0, _decoder.TotalSeconds);
        var devices = _devices.Snapshot();
        for (var i = 0; i < devices.Count; i++)
        {
            if (devices[i].IsClosed || devices[i].IsLost || devices[i].IsHung)
            {
                continue;
            }

            var index = i;
            SpinWait.SpinUntil(() => _decoder.DecodedSeconds(index) >= wanted, TimeSpan.FromMilliseconds(5));
        }

        Thread.Yield();
    }

    private void WaitForDecoder()
    {
        var wanted = Math.Min(Position.TotalSeconds + 1.0, _decoder.TotalSeconds);
        var devices = _devices.Snapshot();
        for (var i = 0; i < devices.Count; i++)
        {
            if (devices[i].IsClosed || devices[i].IsLost || devices[i].IsHung)
            {
                continue;
            }

            var index = i;
            RigLoops.WaitOrFail(() => _decoder.DecodedSeconds(index) >= wanted, TimeSpan.FromSeconds(10), $"the decoder to reach {wanted:F2} s on output {index}");
        }
    }

    /// <summary>
    /// The current run's outputs, copy-on-write: the engine loop re-opens them (<see cref="Add"/>) while the test thread walks
    /// a <see cref="Snapshot"/>, which never changes after it is taken.
    /// </summary>
    private sealed class DeviceRegistry
    {
        private readonly Lock _lock = new();
        private FakeAudioDevice[] _devices = [];

        public IReadOnlyList<FakeAudioDevice> Snapshot()
        {
            lock (_lock)
            {
                return _devices;
            }
        }

        public void Add(FakeAudioDevice device)
        {
            lock (_lock)
            {
                // A new run after the previous one ended: the rig follows the new run's outputs.
                var keep = _devices.Length > 0 && _devices.All(d => d.IsClosed) ? [] : _devices;
                _devices = [.. keep, device];
            }
        }
    }

    private sealed class CapturingBackend(IAudioBackend inner, DeviceRegistry devices) : IAudioBackend
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

    private sealed class Collector(EngineRig rig) : IObserver<EngineEvent>
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

/// <summary>Loops shared by the Core test rigs; each ends on its own instead of spinning or waiting without a limit.</summary>
internal static class RigLoops
{
    /// <summary>Keeps <paramref name="depth"/> frames queued ahead of the device's position, written in <paramref name="chunk"/> frames.</summary>
    public static void FillQueue(FakeAudioDevice device, long depth, int chunk)
    {
        while (device.WrittenFrames < device.PlayedFramesNow + depth)
        {
            // A closed, lost or hung device renders nothing; the engine may close it between two pumps.
            var before = device.WrittenFrames;
            device.PumpRender(chunk);
            if (device.WrittenFrames == before)
            {
                return;
            }
        }
    }

    /// <summary>Waits until <paramref name="condition"/> holds; fails the test when it does not within <paramref name="timeout"/>.</summary>
    public static void WaitOrFail(Func<bool> condition, TimeSpan timeout, string what)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition() && DateTime.UtcNow < deadline)
        {
            Thread.Yield();
        }

        condition().ShouldBeTrue($"timed out after {timeout.TotalSeconds:F0} s waiting for {what}");
    }

    /// <summary>
    /// Waits until every neighbour worker the session keeps (a listing or probe it gave up on) has ended: for a press that relies
    /// on an earlier worker having ended. Read between commands, when the session's control thread does not touch its worker map.
    /// </summary>
    public static void WaitForNeighbourWorkers(PlaybackSession session)
    {
        var workers = typeof(PlaybackSession).GetField("_workers", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.GetValue(session) as System.Collections.IDictionary
            ?? throw new InvalidOperationException("PlaybackSession no longer keeps its neighbour workers in _workers.");
        WaitOrFail(() => workers.Values.Cast<Task>().All(t => t.IsCompleted), TimeSpan.FromSeconds(10), "the session's neighbour workers to end");
    }

    /// <summary>True when <paramref name="task"/> finished within <paramref name="timeout"/>; never blocks the calling thread.</summary>
    public static async Task<bool> CompletesWithin(Task task, TimeSpan timeout) =>
        await Task.WhenAny(task, Task.Delay(timeout, TestContext.Current.CancellationToken)) == task;
}
