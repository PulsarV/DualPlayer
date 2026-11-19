# Changelog — two-outputs-one-clock

## two-outputs-one-clock — one decode, several audio devices, one master clock (Developer console)

**What:** the Developer console (`DualPlayer.Console`) can list playback devices and a file's audio tracks, and play a file with several tracks on several devices at once. Each output gets its own track and device (the same track may go to several devices; a device only to one). All outputs follow one master clock. Each output's drift is measured from the device's reported playback position and corrected continuously. Pause, resume, seek and stop act on all outputs together. Losing one device leaves the others playing. The console prints a sync record per device once per second, each drift excursion, device-lost events and a run summary at the end (drift, clock mismatch in ppm, corrections, underruns).

**Why:** independent players cannot share a clock, and the owner's two-process experiment crackled with no localized cause ([spec](./spec.md) §1). Roadmap steps 4–6 need an engine that decodes once and keeps N outputs aligned, plus measured per-device numbers. Key decisions:
- [ADR-0001](./adr/0001-host-the-developer-console-as-a-separate-executable.md) — the console is a separate executable.
- [ADR-0002](./adr/0002-fan-out-one-decoder-to-per-device-render-threads-through-ring-buffers.md) — one decoder fans out to per-device render threads through ring buffers.
- [ADR-0003](./adr/0003-correct-drift-with-a-managed-adaptive-resampler-after-the-ring-buffer.md) — drift is corrected by a managed adaptive resampler after the ring buffer.
- [ADR-0004](./adr/0004-let-the-audio-backend-pull-frames-from-core-with-the-device-position.md) — the audio backend pulls frames from Core together with the device position.
- [ADR-0005](./adr/0005-convert-tracks-to-the-device-mix-format-with-ffmpeg-swresample-in-media.md) — tracks are converted to the device mix format with FFmpeg swresample (basic fold-down, LFE dropped).
- [ADR-0006](./adr/0006-coordinate-pause-and-seek-with-playback-epochs-while-device-streams-keep-running.md) — pause and seek use playback epochs while device streams keep running.
- [ADR-0007](./adr/0007-publish-engine-events-as-an-observable-stream-from-the-engine-control-loop.md) — engine events are an observable stream, so step 6 can subscribe without engine changes.
- [ADR-0008](./adr/0008-start-the-master-clock-when-the-slowest-output-hears-the-first-frame.md) — the master clock starts when the slowest output hears the first film frame, after a silent pre-roll; start-to-audio relaxed to ≤ 3 s.

**How to use** (full contract: [cli.md](./contracts/cli.md)):

```sh
dotnet run --project src/DualPlayer.Console -- devices
dotnet run --project src/DualPlayer.Console -- tracks movie.mkv
dotnet run --project src/DualPlayer.Console -- play movie.mkv --out 1=1 --out 2=2
# during the run: pause | resume | seek +60 | seek -10 | seek 00:40:00 | stop   (Ctrl+C = stop)
```

**Operational notes:**
- Migration: none (no persisted schema change; everything new is a log record).
- Feature flag / config: none. Tuning options on `play`: `--ring-ms` (default 2000), `--start-level-ms` (500), `--device-buffer-ms` (40).
- Native libraries: FFmpeg (LGPL build, dynamically linked) must be present — Homebrew on the dev Mac; shipped beside the exe on Windows. Its version and license are written to the log at start.
- Logs: Serilog rolling file in `<app-data>/DualPlayer/logs/` with a size cap that holds at least one ≥ 3 h run.
- Rollback: revert the merge; nothing persisted needs cleaning.
- **Acceptance on the target PC:** the ≥ 3 h evidence run with both Bluetooth headsets is recorded (task T22). The repeat run of 2026-11-18 met every §6 target: max drift 0.0 ms, 0 underruns, start-to-audio 2.7 s, CPU 2.69 % on average ([evidence/2026-11-18-target-pc-run.md](evidence/2026-11-18-target-pc-run.md)). The first run of 2026-11-17 missed the drift and underrun targets ([evidence/2026-11-17-target-pc-run.md](evidence/2026-11-17-target-pc-run.md)); short checks, incl. AC-04, AC-06 with a wired output and AC-12 by ear, are in [evidence/2026-11-19-target-pc-short-checks.md](evidence/2026-11-19-target-pc-short-checks.md). The macOS backend is dev-only and not held to the targets.
- **Known problem, fix deferred:** a Bluetooth output's drift can step 12–154 ms within one second and is re-aligned in the next; rare with two headsets, frequent with three Bluetooth devices, clearly worse through a wall (spec §8).

**Acceptance criteria delivered:** AC-01 – AC-13 (incl. AC-06b, AC-09b) are implemented and covered by tests with a fake clock and fake devices. AC-07 (≥ 3 h drift tolerance) and the §6 targets were measured on the target PC in T22.
