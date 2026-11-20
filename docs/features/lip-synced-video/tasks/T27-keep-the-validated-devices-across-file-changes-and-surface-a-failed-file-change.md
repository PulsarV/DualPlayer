---
id: T27
title: "Keep the validated devices across file changes and surface a failed file change"
layer: "app"
deps: ["T26"]
blocks: []
acs: ["AC-07", "AC-12", "AC-15b"]
files_hint: ["src/DualPlayer.Core/Playback/PlaybackSession.cs", "src/DualPlayer.Core/Playback/StartRequest.cs", "src/DualPlayer.App/App.axaml.cs", "src/DualPlayer.App/ViewModels/PlaybackViewModel.cs", "tests/DualPlayer.Core.Tests/Playback/"]
owner: "Volodymyr Kravchuk"
estimate: "M"
context_budget: "M"
origin: "review-2026-11-20 (F3, F4)"
status: "todo"
step: ""
---

<!-- Follow-up task from review-2026-11-20. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-20.md) · repo `CLAUDE.md`. Do not guess. -->

# T27 — Keep the validated devices across file changes and surface a failed file change

## Why

F3 (stage-1, AC-07/AC-12): each `Start` (file change, backward seek at the end) looks devices up again by `--out` number instead of using the validated ids (`App.axaml.cs:127`; `PlaybackSession.cs:107-114, 298, 582`). After a hot-plug or device loss, a track can go to the wrong device with the wrong offset.
F4 (stage-1, AC-07/AC-15b/§6.1): a file change that fails (e.g. a lost device no longer in the list) leaves the session Idle; every key but Esc then returns `playback.no_run` silently. The ViewModel discards the `Result`: no note, no log. Next/previous after a loss is untested.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-07, AC-12, AC-15b.

## Definition of Done

- [ ] The App passes the validated device ids (`ValidatedStart.Outputs`) to the session; every later start reuses those ids, never `--out` numbers. A device list that changes between files (renumbering) keeps each track on its device and its offset.
- [ ] Lost outputs stay lost across a file change (not reopened); when every remaining device is lost the film ends as for AC-15b.
- [ ] A file change whose start fails leaves the Viewer with a note and a log entry and a defined state (the run ends with the end note and Esc exits), never a silent Idle.
- [ ] Tests: next/previous after an output is lost; device list renumbered between files; a file change whose start fails.
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
