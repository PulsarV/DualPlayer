using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using DualPlayer.Core;
using DualPlayer.Core.Playback;
using DualPlayer.Core.Sync;
using DualPlayer.Media.Video;
using DualPlayer.Tests;

namespace DualPlayer.Media.Tests.Video;

/// <summary>
/// The D1 spike (T5, ADR-0002 verification gate): libmpv steered by <see cref="PictureSync"/> on the real clock, measured
/// against the kill criterion and written to a markdown report. Needs a 1080p HEVC file (<c>DUALPLAYER_SPIKE_FILE</c>), a
/// display and <c>DUALPLAYER_HARDWARE=1</c>; see <c>docs/features/lip-synced-video/evidence/d1-spike-run-guide.md</c>.
/// The master clock is a plain <see cref="MasterClock"/> on <see cref="TimeProvider.System"/>: no audio backend is involved,
/// so the spike isolates the picture path.
/// </summary>
public sealed class MpvSpikeTests
{
    public const string FileVariable = "DUALPLAYER_SPIKE_FILE";
    public const string ReportVariable = "DUALPLAYER_SPIKE_REPORT";
    public const string DamagedFileVariable = "DUALPLAYER_SPIKE_DAMAGED_FILE";

    private const string SteadySecondsVariable = "DUALPLAYER_SPIKE_STEADY_SECONDS";
    private const string CyclesVariable = "DUALPLAYER_SPIKE_CYCLES";
    private const string MoveMaxMsVariable = "DUALPLAYER_SPIKE_MOVE_MAX_MS";
    private const string MpvOptionsVariable = "DUALPLAYER_SPIKE_MPV_OPTIONS";
    private const string DamagedSecondsVariable = "DUALPLAYER_SPIKE_DAMAGED_SECONDS";

    private static readonly TimeSpan Settle = TimeSpan.FromSeconds(2);

    [HardwareFact(HardwareOs.Windows)]
    public void Libmpv_steered_by_the_core_loop_meets_the_adr_0002_kill_criterion_on_windows() => RunSpike();

    [HardwareFact(HardwareOs.MacOS)]
    public void Libmpv_spike_harness_runs_on_the_macos_dev_machine_as_a_smoke_not_as_evidence() => RunSpike();

    [HardwareFact(HardwareOs.Windows)]
    public void A_damaged_file_with_d3d11va_reports_skipped_frames_and_how_it_ends_on_windows() => RunDamaged();

    [HardwareFact(HardwareOs.MacOS)]
    public void A_damaged_file_is_observed_on_the_macos_dev_machine_as_a_smoke_not_as_evidence() => RunDamaged();

    private static void RunSpike()
    {
        var file = RequiredFile(FileVariable);
        var steady = TimeSpan.FromSeconds(EnvDouble(SteadySecondsVariable, 600));
        var cycles = (int)EnvDouble(CyclesVariable, 10);
        var moveMaxMs = EnvDouble(MoveMaxMsVariable, 100);

        var time = TimeProvider.System;
        using var renderer = new MpvVideoRenderer(time, MpvOptions());
        var events = new EventLog(renderer);
        var clock = new MasterClock(time);
        using var sync = new PictureSync(clock, renderer, time, TimeSpan.Zero);
        var run = new Run(time, renderer, clock, sync);

        var loadStart = time.GetTimestamp();
        var loaded = renderer.Load(file, nativeWindowHandle: 0, startPaused: true);
        var loadMs = time.GetElapsedTime(loadStart).TotalMilliseconds;
        loaded.IsSuccess.ShouldBeTrue(loaded.Error?.Message);

        var report = new SpikeReport(
            $"# D1 spike report - {DateTimeOffset.Now:yyyy-MM-dd HH:mm}\n\n" +
            $"- Machine: {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture}); .NET {Environment.Version}\n" +
            $"- File: `{file}`\n" +
            (MpvOptions().Count == 0 ? string.Empty : $"- mpv option overrides ({MpvOptionsVariable}): `{Environment.GetEnvironmentVariable(MpvOptionsVariable)}`\n") +
            $"- Steady play: {steady.TotalSeconds:0} s; cycles: {cycles}; largest picture-delay move: {moveMaxMs:0} ms\n" +
            (OperatingSystem.IsWindows() ? string.Empty : "- **Not the target PC: numbers from this machine are a smoke run, not evidence.**\n"));

        // Phase 0: scheduled start, then settle into steady play. Recovery counts from the scheduled start; samples taken
        // while the picture is still held paused for the start never count as back (their error is ~0 by construction).
        var startTimestamp = time.GetTimestamp() + (long)(0.3 * time.TimestampFrequency);
        clock.StartAt(startTimestamp);
        sync.AlignAt(TimeSpan.Zero, startTimestamp);
        var baseline = Total(renderer.Counters);
        var settle = run.Sample(TimeSpan.FromSeconds(3));
        var startRecovery = SpikeMath.HeldAwareRecoveryTimeMs(settle.Samples, SpikeMath.RecoveryToleranceMs) -
            time.GetElapsedTime(settle.Started, startTimestamp).TotalMilliseconds;

        // Phase 1: steady play.
        var steadyBefore = Total(renderer.Counters);
        var steadyRun = run.Sample(steady);
        var steadyLateDropped = Total(renderer.Counters) - steadyBefore;
        var steadySummary = SpikeMath.Summarize(steadyRun.Errors);

        // Phase 2: pause / resume.
        var pauseRecoveries = new List<double?>();
        var pauseStarts = new List<IReadOnlyList<StartRecord>>();
        for (var i = 0; i < cycles; i++)
        {
            run.Pause();
            Thread.Sleep(1000);
            run.Resume();
            var sampled = run.Sample(Settle);
            pauseRecoveries.Add(SpikeMath.HeldAwareRecoveryTimeMs(sampled.Samples, SpikeMath.RecoveryToleranceMs));
            pauseStarts.Add(sampled.Starts);
        }

        // Phase 3: seeks.
        var seekRecoveries = new List<double?>();
        var seekStarts = new List<IReadOnlyList<StartRecord>>();
        double[] seekSeconds = [60, -60, 10, -10];
        for (var i = 0; i < cycles; i++)
        {
            foreach (var delta in seekSeconds)
            {
                run.SeekBy(TimeSpan.FromSeconds(delta));
                var sampled = run.Sample(Settle);
                seekRecoveries.Add(SpikeMath.HeldAwareRecoveryTimeMs(sampled.Samples, SpikeMath.RecoveryToleranceMs));
                seekStarts.Add(sampled.Starts);
            }
        }

        // Phase 4: picture-delay moves at 0.2 % (out to +m ms and back to 0).
        var moveErrors = new List<double>();
        var moveJumps = new JumpProxy();
        var moveLines = new List<string>();
        for (var i = 1; i <= cycles; i++)
        {
            var magnitude = TimeSpan.FromMilliseconds(moveMaxMs * i / cycles);
            foreach (var target in new[] { magnitude, TimeSpan.Zero })
            {
                var from = sync.TargetPictureDelay;
                sync.MoveTo(target);
                var move = run.SampleWhile(() => sync.IsMoving, TimeSpan.FromMinutes(5), moveJumps);
                var after = run.Sample(Settle, moveJumps);
                var errors = move.Errors.Concat(after.Errors).ToList();
                moveErrors.AddRange(errors);
                var summary = SpikeMath.Summarize(errors);
                moveLines.Add(FormattableString.Invariant(
                    $"{from.TotalMilliseconds:0.#} ms -> {target.TotalMilliseconds:0.#} ms: {move.Duration.TotalSeconds:0.#} s move, error p99 {summary.P99:0.0} ms, max {summary.Max:0.0} ms"));
            }
        }

        var playing = run.SampledTime;
        var lateDropped = Total(renderer.Counters) - baseline;
        var moveSummary = SpikeMath.Summarize(moveErrors);
        var feedback = renderer.PresentationDelays;

        // Mpv's own window and the picture are watched by the owner during the moves; the numbers below are the proxy.
        var scheduled = OperatingSystem.IsWindows() ? MeasureTimers(renderer, time, 10) : MeasureStarts(renderer, time, 10);

        report.AddCriterion(
            "Steady play: picture presented more than 20 ms from due",
            Invariant($"p50 {steadySummary.P50:0.0} / p95 {steadySummary.P95:0.0} / p99 {steadySummary.P99:0.0} / max {steadySummary.Max:0.0} ms over {steadySummary.Count} ticks"),
            steadySummary.Count == 0 ? SpikeVerdict.NotMeasured : SpikeMath.SteadyVerdict(steadySummary.Max));
        report.AddCriterion(
            "Pause/resume: not back within 20 ms of due within 1 s",
            RecoveryText(pauseRecoveries),
            SpikeMath.RecoveryVerdict(pauseRecoveries));
        report.AddCriterion(
            "Seek (+-10 s, +-60 s): not back within 20 ms of due within 1 s",
            RecoveryText(seekRecoveries),
            SpikeMath.RecoveryVerdict(seekRecoveries));
        report.AddCriterion(
            "0.2 % speed move shows a visible jump or repeated frame",
            Invariant($"error during moves p99 {moveSummary.P99:0.0} / max {moveSummary.Max:0.0} ms; backward steps {moveJumps.Backward}, forward jumps {moveJumps.Forward}"),
            null);
        report.AddCriterion(
            "More than 1 frame per minute late or dropped",
            Invariant($"{lateDropped} in {playing.TotalMinutes:0.0} min = {SpikeMath.PerMinute(lateDropped, playing):0.00} / min (steady play alone: {steadyLateDropped} in {steady.TotalMinutes:0.0} min)"),
            SpikeMath.LateDroppedVerdict(lateDropped, playing));
        report.AddCriterion(
            "Hardware decoding stayed on (no HardwareDecodingInactive)",
            events.HardwareInactive ? "event raised" : "not raised",
            events.HardwareInactive ? SpikeVerdict.Fail : SpikeVerdict.Pass);

        report.AddSection("Other measurements", [
            Invariant($"mpv load time (Load until first frame): {loadMs:0} ms (budget context: start <= 3 s, sad.md section 11)"),
            "first start (and any correcting seek after it): " + StartsText(settle.Starts, time),
            startRecovery is { } r ? Invariant($"within 20 ms of due {r:0} ms after the scheduled first start (held samples not counted)") : "never within 20 ms of due in the 3 s after the first start",
            SpikeMath.PresentationDelayText(feedback.Samples, feedback.Used, feedback.MedianMs),
            Invariant($"ticks while the picture was held paused for a start (excluded from error figures): {run.HeldTicks}"),
            Invariant($"counters at the end: late {renderer.Counters.Late}, dropped {renderer.Counters.Dropped}, skipped damaged {renderer.Counters.SkippedDamaged}"),
            $"EndOfFile raised: {events.EndOfFile}; DecodeFailed raised: {events.DecodeFailed ?? "no"}",
        ]);
        report.AddSection("Scheduled starts and timer granularity", scheduled);
        report.AddSection("Picture-delay moves", moveLines);
        report.AddSection("Recoveries (ms from the align to back within 20 ms after the actual resume, null = never)", [
            "pause/resume: " + string.Join(", ", pauseRecoveries.Select(Format)),
            "seek: " + string.Join(", ", seekRecoveries.Select(Format)),
        ]);
        report.AddSection(
            "Starts behind the recoveries (unpause command latency = mpv applied the unpause minus the moment the held frame was due)",
            [
                .. pauseStarts.Select((starts, i) => Invariant($"resume {i + 1}: ") + StartsText(starts, time)),
                .. seekStarts.Select((starts, i) => Invariant($"seek {i + 1}: ") + StartsText(starts, time)),
            ]);

        var path = WriteReport(report, "d1-spike-report");
        TestContext.Current.SendDiagnosticMessage($"D1 spike report written to {path}; overall: {report.Overall}");

        report.Overall.ShouldNotBe(SpikeVerdict.Fail, $"A kill-criterion line failed - see {path}.");
    }

    private static void RunDamaged()
    {
        var damaged = Environment.GetEnvironmentVariable(DamagedFileVariable);
        if (string.IsNullOrEmpty(damaged))
        {
            Assert.Skip($"Set {DamagedFileVariable} to a damaged 1080p HEVC file to observe SkippedDamaged and the end event.");
            return;
        }

        var limit = TimeSpan.FromSeconds(EnvDouble(DamagedSecondsVariable, 120));
        var time = TimeProvider.System;
        using var renderer = new MpvVideoRenderer(time, MpvOptions());
        var events = new EventLog(renderer);

        var loaded = renderer.Load(damaged, nativeWindowHandle: 0, startPaused: false);
        loaded.IsSuccess.ShouldBeTrue(loaded.Error?.Message);

        var started = time.GetTimestamp();
        events.Ended.Wait(limit);
        var elapsed = time.GetElapsedTime(started);

        var report = new SpikeReport(
            $"# D1 spike - damaged file observation - {DateTimeOffset.Now:yyyy-MM-dd HH:mm}\n\n" +
            $"- Machine: {RuntimeInformation.OSDescription}\n- File: `{damaged}`\n" +
            (OperatingSystem.IsWindows() ? string.Empty : "- **Not the target PC: smoke run, not evidence.**\n"));
        report.AddCriterion("SkippedDamaged counts damaged frames", Invariant($"{renderer.Counters.SkippedDamaged}"), null);
        report.AddCriterion("HardwareDecodingInactive on a damaged file", events.HardwareInactive ? "raised" : "not raised", null);
        report.AddCriterion(
            "How playback ended",
            events.DecodeFailed is { } failed ? $"DecodeFailed ({failed})" : events.EndOfFile ? "EndOfFile" : Invariant($"neither within {limit.TotalSeconds:0} s"),
            null);
        report.AddSection("Detail", [
            Invariant($"observed for {elapsed.TotalSeconds:0.#} s; counters late {renderer.Counters.Late}, dropped {renderer.Counters.Dropped}"),
            "Expectation to confirm: damaged frames are counted and playback goes on; an undecodable stretch in mid-file ends with DecodeFailed (not a silent EndOfFile).",
        ]);

        var path = WriteReport(report, "d1-spike-damaged");
        TestContext.Current.SendDiagnosticMessage($"Damaged-file report written to {path}");
    }

    /// <summary>Ten scheduled starts with the default timer resolution, then (Windows) ten with a 1 ms timer period.</summary>
    [SupportedOSPlatform("windows")]
    private static List<string> MeasureTimers(MpvVideoRenderer renderer, TimeProvider time, int count)
    {
        var lines = MeasureStarts(renderer, time, count);
        _ = TimeBeginPeriod(1);
        try
        {
            lines.AddRange(MeasureStarts(renderer, time, count).Select(l => l.Replace("default timer", "timeBeginPeriod(1)", StringComparison.Ordinal)));
        }
        finally
        {
            _ = TimeEndPeriod(1);
        }

        return lines;
    }

    /// <summary>
    /// Scheduled starts 250 ms ahead: the unpause command latency (mpv applied the unpause minus the moment the held frame
    /// was due) with the seek budget and the measured seek time of each start. Presented is not used: a held frame is
    /// stamped with its planned moment, so it would show ~0 by construction.
    /// </summary>
    private static List<string> MeasureStarts(MpvVideoRenderer renderer, TimeProvider time, int count)
    {
        var latencies = new List<double>();
        var perStart = new List<string>();
        for (var i = 0; i < count; i++)
        {
            renderer.Pause();
            renderer.Seek(TimeSpan.Zero);
            Thread.Sleep(500);

            var before = renderer.LastStart?.Id;
            var due = time.GetTimestamp() + (long)(0.25 * time.TimestampFrequency);
            renderer.PlayAt(TimeSpan.Zero, due);

            var waitStart = time.GetTimestamp();
            while (time.GetElapsedTime(waitStart) < TimeSpan.FromSeconds(3))
            {
                if (renderer.LastStart is { } start && start.Id != before)
                {
                    latencies.Add(time.GetElapsedTime(start.UnpauseDue, start.ResumedAt).TotalMilliseconds);
                    perStart.Add(StartText(start, time));
                    break;
                }

                Thread.Sleep(1);
            }
        }

        renderer.Pause();
        var summary = SpikeMath.Summarize(latencies);
        return
        [
            Invariant($"default timer: {latencies.Count}/{count} starts measured, unpause command latency (absolute) p50 {summary.P50:0.0} / max {summary.Max:0.0} ms"),
            .. perStart.Select(line => "default timer start: " + line),
        ];
    }

    // Every start unpaused in a sampled window: more than one means the loop had to correct with another seek.
    private static string StartsText(IReadOnlyList<StartRecord> starts, TimeProvider time) =>
        starts.Count == 0 ? "no start was unpaused" : string.Join("; then ", starts.Select(start => StartText(start, time)));

    private static string StartText(StartRecord? start, TimeProvider time) =>
        start is null
            ? "no start was unpaused"
            : Invariant($"unpause command latency {time.GetElapsedTime(start.UnpauseDue, start.ResumedAt).TotalMilliseconds:0.0} ms, seek budget {start.SeekBudget.TotalMilliseconds:0} ms, seek took {(start.SeekTime is { } seek ? seek.TotalMilliseconds.ToString("0", CultureInfo.InvariantCulture) + " ms" : "n/a (no seek)")}");

    private static string RecoveryText(List<double?> recoveries)
    {
        var worst = recoveries.Contains(null) ? "never" : recoveries.Count == 0 ? "none" : Invariant($"{recoveries.Max(r => r!.Value):0} ms");
        return Invariant($"{recoveries.Count} events; worst recovery {worst}");
    }

    private static string Format(double? value) => value is { } v ? v.ToString("0", CultureInfo.InvariantCulture) : "null";

    private static string Invariant(FormattableString text) => FormattableString.Invariant(text);

    private static long Total(FrameCounters counters) => counters.Late + counters.Dropped;

    /// <summary>Optional mpv option overrides, <c>key=value;key=value</c> (for example <c>vo=null</c> on a machine without a usable display).</summary>
    private static Dictionary<string, string> MpvOptions() =>
        (Environment.GetEnvironmentVariable(MpvOptionsVariable) ?? string.Empty)
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(pair => pair.Split('=', 2))
            .Where(parts => parts.Length == 2)
            .ToDictionary(parts => parts[0], parts => parts[1]);

    private static string RequiredFile(string variable)
    {
        var file = Environment.GetEnvironmentVariable(variable);
        if (string.IsNullOrEmpty(file) || !File.Exists(file))
        {
            throw new InvalidOperationException($"Set {variable} to an existing 1080p HEVC file (current: '{file}').");
        }

        return file;
    }

    private static double EnvDouble(string variable, double fallback) =>
        double.TryParse(Environment.GetEnvironmentVariable(variable), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : fallback;

    private static string WriteReport(SpikeReport report, string stem)
    {
        var path = Environment.GetEnvironmentVariable(ReportVariable);
        path = string.IsNullOrEmpty(path)
            ? Path.Combine(AppContext.BaseDirectory, $"{stem}-{DateTime.Now:yyyyMMdd-HHmmss}.md")
            : stem == "d1-spike-report" ? path : Path.ChangeExtension(path, null) + "-damaged.md";
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, report.Render());
        return path;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate uint TimePeriod(uint milliseconds);

    // winmm is bound by hand: a source-generated import would need AllowUnsafeBlocks in this project.
    [SupportedOSPlatform("windows")]
    private static uint TimeBeginPeriod(uint milliseconds) => WinMm("timeBeginPeriod")(milliseconds);

    [SupportedOSPlatform("windows")]
    private static uint TimeEndPeriod(uint milliseconds) => WinMm("timeEndPeriod")(milliseconds);

    private static TimePeriod WinMm(string export) =>
        Marshal.GetDelegateForFunctionPointer<TimePeriod>(NativeLibrary.GetExport(NativeLibrary.Load("winmm.dll"), export));

    /// <summary>Counts jumps in the presented position between ticks - a numeric stand-in for the owner's eyes.</summary>
    private sealed class JumpProxy
    {
        private double? _lastPresentedMs;
        private double _lastAtMs;

        public int Backward { get; private set; }

        public int Forward { get; private set; }

        public void Observe(double presentedMs, double atMs)
        {
            if (_lastPresentedMs is { } last)
            {
                var step = presentedMs - last;
                if (step < -1)
                {
                    Backward++;
                }
                else if (step > (atMs - _lastAtMs) + 150)
                {
                    Forward++;
                }
            }

            _lastPresentedMs = presentedMs;
            _lastAtMs = atMs;
        }

        public void Reset() => _lastPresentedMs = null;
    }

    private sealed record Sampled(
        List<(double AtMs, double ErrorMs, bool Held)> Samples,
        TimeSpan Duration,
        long Started,
        IReadOnlyList<StartRecord> Starts)
    {
        /// <summary>Errors of the samples taken while the picture was moving (a held frame's error is ~0 by construction).</summary>
        public IReadOnlyList<double> Errors => Samples.Where(s => !s.Held).Select(s => s.ErrorMs).ToList();
    }

    private sealed class EventLog
    {
        public EventLog(MpvVideoRenderer renderer)
        {
            renderer.HardwareDecodingInactive += () => HardwareInactive = true;
            renderer.DecodeFailed += error =>
            {
                DecodeFailed = error.Message;
                Ended.Set();
            };
            renderer.EndOfFile += () =>
            {
                EndOfFile = true;
                Ended.Set();
            };
        }

        public ManualResetEventSlim Ended { get; } = new();

        public volatile bool HardwareInactive;
        public volatile bool EndOfFile;
        public volatile string? DecodeFailed;
    }

    /// <summary>The ticking loop and the control actions the engine would take (clock stop/start, align, seek).</summary>
    private sealed class Run(TimeProvider time, MpvVideoRenderer renderer, MasterClock clock, PictureSync sync)
    {
        /// <summary>Gets the total time the picture was being sampled while playing (pauses excluded).</summary>
        public TimeSpan SampledTime { get; private set; }

        /// <summary>Gets how many ticks were taken while the picture was held paused for a start.</summary>
        public int HeldTicks { get; private set; }

        public void Pause()
        {
            clock.StopAt(time.GetTimestamp());
            renderer.Pause();
        }

        public void Resume()
        {
            var now = time.GetTimestamp();
            clock.StartAt(now);
            sync.AlignAt(clock.PositionAt(now), now);
        }

        public void SeekBy(TimeSpan delta)
        {
            var now = time.GetTimestamp();
            var target = TimeSpan.FromTicks(Math.Max(0, (clock.PositionAt(now) + delta).Ticks));
            clock.JumpTo(target);
            now = time.GetTimestamp();
            sync.AlignAt(clock.PositionAt(now), now);
        }

        public Sampled Sample(TimeSpan duration, JumpProxy? jumps = null) =>
            SampleWhile(() => true, duration, jumps, runForFullDuration: true);

        public Sampled SampleWhile(Func<bool> keepGoing, TimeSpan limit, JumpProxy? jumps = null, bool runForFullDuration = false)
        {
            var samples = new List<(double, double, bool)>();
            var starts = new List<StartRecord>();
            var lastStartId = renderer.LastStart?.Id;
            var start = time.GetTimestamp();
            jumps?.Reset();
            while (time.GetElapsedTime(start) < limit && (runForFullDuration || keepGoing()))
            {
                if (renderer.LastStart is { } unpaused && unpaused.Id != lastStartId)
                {
                    starts.Add(unpaused);
                    lastStartId = unpaused.Id;
                }

                var held = renderer.HoldingStart;
                if (sync.Tick() is { } timing)
                {
                    var at = time.GetElapsedTime(start).TotalMilliseconds;
                    held = held || renderer.HoldingStart;
                    samples.Add((at, timing.ErrorMs, held));
                    if (held)
                    {
                        HeldTicks++;
                    }
                    else
                    {
                        jumps?.Observe(timing.Presented.TotalMilliseconds, at);
                    }
                }

                Thread.Sleep(2);
            }

            var duration = time.GetElapsedTime(start);
            SampledTime += duration;
            return new Sampled(samples, duration, start, starts);
        }
    }
}
