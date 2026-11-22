---
id: T121
title: "Fix the start-failure branch in sad flow 1, list decoder_fault and no-upgrader in cli.md, correct the start-budget mitigation and the executable name, and bring test-plan and the tracker in step"
layer: "docs"
deps: ["T115", "T116", "T117", "T118", "T119", "T120"]
blocks: []
acs: ["AC-01", "AC-02", "AC-12"]
files_hint: ["docs/features/lip-synced-video/sad.md", "docs/features/lip-synced-video/contracts/cli.md", "docs/features/lip-synced-video/screens.md", "docs/features/lip-synced-video/test-plan.md", "docs/features/lip-synced-video/tasks/tracker.md", "docs/features/lip-synced-video/tasks/T23-measure-lip-sync-on-the-target-pc.md", "docs/features/lip-synced-video/tasks.json", "tests/DualPlayer.App.Tests/Startup/StartCommandTests.cs"]
status: "todo"
step: ""
owner: "Volodymyr Kravchuk"
estimate: "S"
context_budget: "S"
origin: "review-2026-11-22f (X2, X3, X6, X7)"
---

<!-- Follow-up task from review-2026-11-22f. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-22f.md) · repo `CLAUDE.md`. Do not guess. -->

# T121 — Fix the start-failure branch in sad flow 1, list decoder_fault and no-upgrader in cli.md, correct the start-budget mitigation and the executable name, and bring test-plan and the tracker in step

## Why

- **X2**. sad flow 1 (`sad.md:245-249`) draws the start-failure branch between "load the file paused" and the pre-roll, but `outputs.device_open_failed` and `playback.start_timeout` come from `_engine.StartRun` (`PlaybackSession.cs:428`), after the load; the settle step (`:440-445`) can also fail.
- **X3**. cli.md §5 misses `playback.decoder_fault` (a decoder exception during pre-fill, `AudioEngine.cs:202-209` → `StartErrors.AfterSessionStart`, exit 2) and `state.no-upgrader` (a `device-offsets.json` with no `schemaVersion`, `JsonFileStateStore.cs:63, 119-120`). The `mpv.load_failed` row (cli.md:141) says only "library missing or wrong version", but the session start also returns it when mpv refuses the file or fails to initialise (`MpvVideoRenderer.cs:296, 304, 577, 595, 603`). Owner decision: `playback.decoder_fault` is exit 2.
- **X6**. sad §11 (`sad.md:606`) credits "load the file in mpv while the pre-roll runs", but `_renderer.Load` completes before `_engine.StartRun` (`PlaybackSession.cs:410-428`). Owner decision: fix the text, not the code; overlap only if T23 measures more than 3 s.
- **X7**. cli.md (:13, :25-26) writes `DualPlayer.App play …`, but `DualPlayer.App.csproj:4` sets `<AssemblyName>DualPlayer</AssemblyName>`.

Owner decision: fix now.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-01, AC-02, AC-12.

## Definition of Done

- [ ] sad flow 1 wraps the load, the pre-roll and the clock start in one `alt start succeeds / else start fails (mpv.load_timeout, mpv.load_failed, outputs.device_not_found, outputs.device_open_failed, playback.decoder_fault, playback.start_timeout)` block.
- [ ] cli.md §5 has a `playback.decoder_fault` row (exit 2, when it is printed); the warning list has `state.no-upgrader`; the `mpv.load_failed` row's "When" covers mpv refusing the file or failing to initialise at the session start; sad.md's code list matches. `StartExitCodes.For("playback.decoder_fault")` is 2, pinned in `Start_exit_code_for_a_refusal_before_or_during_start_is_2`.
- [ ] sad §11's start-budget mitigation describes the sequential start (load, then pre-roll) and says the overlap is the fallback if T23 measures more than 3 s.
- [ ] cli.md and screens.md SCR-01 write the command as `DualPlayer play …`.
- [ ] test-plan.md has a row for every test T115–T120 added; every DoD box of T115–T120 that holds is ticked; the tracker's T23 row and T23's deps include T115–T121.
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
