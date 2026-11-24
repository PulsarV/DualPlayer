---
id: T114
title: "Redraw sad flow 1 after the checks as the code runs it, list every start error code in cli.md, and bring test-plan and the tracker in step"
layer: "docs"
deps: ["T108", "T109", "T110", "T111", "T112", "T113"]
blocks: []
acs: ["AC-01", "AC-02", "AC-12", "AC-13"]
files_hint: ["docs/features/lip-synced-video/sad.md", "docs/features/lip-synced-video/contracts/cli.md", "docs/features/lip-synced-video/test-plan.md", "docs/features/lip-synced-video/tasks/tracker.md", "docs/features/lip-synced-video/tasks/T23-measure-lip-sync-on-the-target-pc.md", "docs/features/lip-synced-video/tasks.json"]
status: "done"
step: "COMMIT"
owner: "Volodymyr Kravchuk"
estimate: "S"
context_budget: "S"
origin: "review-2026-11-22e (W9, W10)"
---

<!-- Follow-up task from review-2026-11-22e. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-22e.md) · repo `CLAUDE.md`. Do not guess. -->

# T114 — Redraw sad flow 1 after the checks as the code runs it, list every start error code in cli.md, and bring test-plan and the tracker in step

## Why

- **W9**. After the checks, sad flow 1 (`sad.md:237-244`) draws "Cli→Core start the session → latency probe → load offsets → assumed latency logged → Core-->Window open fullscreen". In the code (`App.axaml.cs:106-162`) the App creates and shows the window first; `session.Start` runs only from `VideoHandleCreated`, where offsets are loaded, latency is probed and logged, and mpv loads the file. So a `state.*` warning, the latency lines and an `mpv.load_timeout` start failure happen with the window already open.
- **W10**. Start can print codes with exit 2 that cli.md §5 does not list: `ffmpeg.not_found` / `ffmpeg.load_failed` (`FfmpegPictureProbe.cs:34-37`, `FfmpegLibrary.cs:26, 55`; the very first check when the FFmpeg DLLs are missing), `outputs.device_open_failed` (`BackendDeviceAccessProbe.cs:10-13`, `WasapiAudioDevice.cs:68`, `WasapiAudioBackend.cs:72`), and `playback.start_timeout` (`AudioEngine.cs:476, 739`, via `StartErrors.AfterSessionStart`).

Owner decision: fix now.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-01, AC-02, AC-12, AC-13.

## Definition of Done

- [x] sad flow 1, after "checks pass": Cli→Window open on the screen, Window→Cli handle, Cli→Core start session (offsets, latency probe + log, load, pre-roll), with a failure branch that closes the window and exits with the code.
- [x] cli.md §5 has rows (exit 2, with when each is printed) for `ffmpeg.not_found`, `ffmpeg.load_failed`, `outputs.device_open_failed` and `playback.start_timeout`; §4's exit-2 meaning mentions a missing FFmpeg and a device that cannot be opened; the code list in sad.md (:547) includes them; `StartExitCodes.For` maps each to 2 (add a test case if one is missing).
- [x] test-plan.md has a row for every test T108–T113 added, and the T107 start-order row (:170) matches what T113 pins; every DoD box of T108–T113 that holds is ticked; the tracker's T23 row and T23's deps include T108–T114.
- [x] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
