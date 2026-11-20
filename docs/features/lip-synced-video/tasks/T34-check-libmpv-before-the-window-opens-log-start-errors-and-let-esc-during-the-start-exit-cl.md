---
id: T34
title: "Check libmpv before the window opens, log start errors, and let Esc during the start exit cleanly"
layer: "wiring"
deps: ["T24"]
blocks: []
acs: ["AC-01", "AC-02"]
files_hint: ["src/DualPlayer.App/Startup/StartCommand.cs", "src/DualPlayer.App/App.axaml.cs", "src/DualPlayer.App/Program.cs", "src/DualPlayer.Media/Video/MpvVideoRenderer.cs", "tests/DualPlayer.App.Tests/Startup/"]
owner: "Volodymyr Kravchuk"
estimate: "M"
context_budget: "M"
origin: "review-2026-11-20 (F8, F17, F25)"
status: "done"
step: "COMMIT"
---

<!-- Follow-up task from review-2026-11-20. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-20.md) · repo `CLAUDE.md`. Do not guess. -->

# T34 — Check libmpv before the window opens, log start errors, and let Esc during the start exit cleanly

## Why

F8 (cli.md §4 exit 2 no window; SCR-01 error; ADR-0001): a missing or wrong libmpv is detected only inside `session.Start`, after the fullscreen window has opened (`StartCommand.cs:68-108`; `App.axaml.cs:115-139`; `MpvVideoRenderer.cs:228`).
F17 (cli.md conventions; sad §7): start errors go to stderr but are not written to the log (`Program.cs:27`; `App.axaml.cs:54, 130`).
F25 (stage-2): Esc during start disposes the renderer under `session.Start`, which gives exit 2 and a stray error; audio can start after the window has closed (`App.axaml.cs:105-133`).

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-01, AC-02.

## Definition of Done

- [x] Validation checks that libmpv loads with the expected version (the T2 loader) before any window; a failure prints the contract error and exits with the cli.md code, no window shown.
- [x] Every start error printed to stderr is also logged (parse, validation, session start).
- [x] Esc/close while the session is still starting cancels the start: exit code 0, no error line, no audio after the window is gone (the start finishes or aborts before the renderer is disposed).
- [x] Tests for each (a fake libmpv loader, a slow fake start with Esc during it).
- [x] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean

## Note (implement)

Since T24 the session runs Exit only after a running Start has finished, so the renderer is never disposed under the start; `Exit_during_the_start_waits_for_the_start_and_only_then_disposes_the_renderer_and_stops_the_sound` pins it (it passed once T24 was in). Audio that the start released is stopped by that Exit right after the start returns; the engine has no way to cancel a start in its pre-roll, so a few hundred ms of sound after Esc during the start remain possible on real hardware — check in T23.
