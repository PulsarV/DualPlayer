---
id: T64
title: "Start the master clock when the slowest output hears the first frame, after a silent pre-roll"
layer: "app"
deps: []
blocks: []
acs: ["AC-01", "AC-07"]
files_hint: ["src/DualPlayer.Core/Playback/AudioEngine.cs", "src/DualPlayer.Core/Outputs/OutputRenderSource.cs", "tests/DualPlayer.Core.Tests/Playback/AudioEngineStartAndHangTests.cs", "tests/DualPlayer.Core.Tests/Fakes/EngineRig.cs"]
owner: "Volodymyr Kravchuk"
estimate: "L"
context_budget: "L"
origin: "target-PC diagnosis 2026-11-17; ADR-0008"
status: "todo"
step: ""
---

<!-- Follow-up task from the target-PC diagnosis. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) §6 flow 1 · [ADR-0008](../adr/0008-start-the-master-clock-when-the-slowest-output-hears-the-first-frame.md) · [ADR-0006](../adr/0006-coordinate-pause-and-seek-with-playback-epochs-while-device-streams-keep-running.md) · repo `CLAUDE.md`. Do not guess. -->

# T64 — Start the master clock when the slowest output hears the first frame, after a silent pre-roll

## Why

On the target PC a Bluetooth headset takes audio from the moment its stream starts but reports playback position 0 for ~0.6 s (JBL Tune 520BT) to ~1 s (MOTIF II A.N.C.). After that `elapsed − position` stays constant (978.4 ms MOTIF, 607.0 ms JBL, 11.1 ms wired Realtek). A frame handed over at T is heard at T + that lag.

Today the clock starts when every ring holds its start level, film goes to every device at once, and `AlignOutputs` (`AudioEngine.cs` ~548) aligns each output from `TryMeasureHeardContent` (~606). While the position reads 0, `heardAt = timestamp + (bufferFrame − played)/rate` runs ahead of real time, so alignment chases a moving target, spends `MaxAlignRounds` (16) and leaves a residual. Observed in 2 of 3 runs: MOTIF ~−19 ms for ~1 s after the 1 s recovery window, correction pinned at −500 ppm, then one one-step correction (`max_drift_ms 18.9`, `corrections 1`). This breaks QG-1 (within 10 ms ≤ 1 s after start).

Measuring later does not help: the first ~1 s of film is already queued in the headset (tried: gating the measurement on `played > 0` leaves the headset ~1 s behind until a jump).

## Decision (ADR-0008, Accepted)

1. Start every device on **silence** (pre-roll) — e.g. `SetSilenceFrom(0)` before `Device.Start()`.
2. Wait until every non-lost output's reported position has **moved** (`played > 0`). An output that never moves must not hold the others: reuse `FirstAudioDeadline` (5 s).
3. Per output, read its queue lag `L_i = (framesWritten − played) / rate` from one consistent position reading (constant once the position moves).
4. **Release**: give each output a silence insert of `(L_max − L_i)` frames before film frame 0 (negative `RequestRealign` before any film is consumed), then `ClearSilence()` on all outputs together.
5. Start the master clock at 00:00 when the slowest output hears film frame 0 (≈ release + `L_max`); then `BeginRecovery` and the existing one-step `BeginAlignment` as today (measurements are valid now, so it only trims a residual of about one device period).
6. Resume and seek are unchanged (ADR-0006: streams keep running, no stall).
7. Commands during the pre-roll (pause, seek) must not play film while paused or break the start. Simplest acceptable rule: refuse them with a plain-language "the run is still starting" error, or queue them. Pick one and test it.

## Pinning test (already written, RED on the current engine)

Adds a rig mode where the fake device takes audio at its rate while its position still reads 0, and a test that fails on the current engine with `at 1,50 s heard 0,810 s, master clock 1,500 s`. Under ADR-0008, the expected times shift: the clock starts later, so adapt the assertion to "every output hears content = master clock within 10 ms from 1 s after the clock started" (read the clock start from the engine, e.g. the `AudioStarted` event or the run-started clock position), not from the start command.

```diff
diff --git a/tests/DualPlayer.Core.Tests/Playback/AudioEngineStartAndHangTests.cs b/tests/DualPlayer.Core.Tests/Playback/AudioEngineStartAndHangTests.cs
index 73c4208..bd185fe 100644
--- a/tests/DualPlayer.Core.Tests/Playback/AudioEngineStartAndHangTests.cs
+++ b/tests/DualPlayer.Core.Tests/Playback/AudioEngineStartAndHangTests.cs
@@ -35,11 +35,17 @@ public sealed class AudioEngineStartAndHangTests
         private readonly DateTimeOffset _start;
         private readonly int _depth;
         private readonly int _chunk;
+        private readonly bool _pullsDuringStartLatency;
 
-        public Rig(TimeSpan[] latencies, double ppm = 0, long totalFrames = (long)Rate * 600, int depth = 120, int chunk = 120)
+        /// <param name="pullsDuringStartLatency">
+        /// The device takes audio at its rate from the start while its position still reads 0, as a Bluetooth headset
+        /// does (measured on the target PC: position 0 for ~0.6–1 s, then <c>elapsed − position</c> stays constant).
+        /// </param>
+        public Rig(TimeSpan[] latencies, double ppm = 0, long totalFrames = (long)Rate * 600, int depth = 120, int chunk = 120, bool pullsDuringStartLatency = false)
         {
             _depth = depth;
             _chunk = chunk;
+            _pullsDuringStartLatency = pullsDuringStartLatency;
             Time = new FakeTimeProvider();
             var backend = new FakeAudioBackend(Time, Stereo) { PpmOffset = ppm };
             for (var i = 1; i <= latencies.Length; i++)
@@ -128,7 +134,15 @@ public sealed class AudioEngineStartAndHangTests
                     continue;
                 }
 
-                while (device.WrittenFrames < device.PlayedFramesNow + _depth)
+                var target = _pullsDuringStartLatency
+                    ? device.PlayedFramesNow + (long)(device.StartLatency.TotalSeconds * Rate) + _depth
+                    : device.PlayedFramesNow + _depth;
+                if (_pullsDuringStartLatency && device.PlayedFramesNow == 0)
+                {
+                    target = (long)(Elapsed.TotalSeconds * Rate) + _depth; // still silent: the device takes audio at its rate
+                }
+
+                while (device.WrittenFrames < target)
                 {
                     device.PumpRender(_chunk);
                 }
@@ -201,6 +215,17 @@ public sealed class AudioEngineStartAndHangTests
         }
     }
 
+    [Fact]
+    public void A_bluetooth_output_that_takes_audio_while_its_position_reads_zero_for_a_second_is_aligned_once_it_plays()
+    {
+        using var rig = new Rig([TimeSpan.Zero, TimeSpan.FromMilliseconds(980)], depth: 1_920, chunk: 480, pullsDuringStartLatency: true);
+
+        rig.Render(300);
+
+        AssertInSync(rig.Devices[1], TimeSpan.Zero, TimeSpan.Zero, 1.5, 2.9);
+        AssertInSync(rig.Devices[0], TimeSpan.Zero, TimeSpan.Zero, 1.0, 2.9);
+    }
+
     [Fact]
     public void A_seek_with_one_hung_output_is_confirmed_on_the_rendering_output_long_before_the_ten_second_timeout()
     {
```

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-01 (outputs start together from the beginning) and AC-07, and §6: drift tolerance ≤ 10 ms, recovery ≤ 1 s after start, start command to audio on all outputs ≤ 3 s (changed 2026-11-17).

## Definition of Done

- [ ] With one zero-latency output and one output that takes audio while reading position 0 for 980 ms, both hear content = master clock within 10 ms from 1 s after the clock starts; no one-step correction is counted at start; no film is rendered on any output before the release.
- [ ] Three or more outputs with different start lags (0, 600, 980 ms) align the same way.
- [ ] An output whose position never moves does not hold the clock past `FirstAudioDeadline`; the others start.
- [ ] Pause / seek during the pre-roll follow the rule chosen in Decision step 7, with a test.
- [ ] Existing test rigs (`EngineRig`, `AudioEngineStartAndHangTests.Rig`, Console tests) take the clock start from the engine instead of assuming it is the start command; no test weakened.
- [ ] Console `start-to-audio` still means "until film is heard on every output" and includes the pre-roll.
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean.
