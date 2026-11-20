---
id: T24
title: "Run session commands and callbacks serialized on one thread off the UI thread"
layer: "app"
deps: []
blocks: []
acs: ["AC-04", "AC-05", "AC-07", "AC-10", "AC-15"]
files_hint: ["src/DualPlayer.Core/Playback/PlaybackSession.cs", "src/DualPlayer.App/ViewModels/PlaybackViewModel.cs", "src/DualPlayer.App/App.axaml.cs", "tests/DualPlayer.Core.Tests/Playback/", "tests/DualPlayer.App.Tests/ViewModels/"]
owner: "Volodymyr Kravchuk"
estimate: "L"
context_budget: "L"
origin: "review-2026-11-20 (F7)"
status: "todo"
step: ""
---

<!-- Follow-up task from review-2026-11-20. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-20.md) · repo `CLAUDE.md`. Do not guess. -->

# T24 — Run session commands and callbacks serialized on one thread off the UI thread

## Why

F7 (stage-1, spec §6.1 ≤ 3 s per key; sad §5/§8 threading): session commands run synchronously on the UI thread — folder scan, FFmpeg probe (≤ 2 s per file), engine stop/start pre-roll and mpv `Load` (`PlaybackViewModel.cs:24-63`, `PlaybackSession.cs:250-298`). Session state is also mutated from timer, engine and mpv threads instead of being serialized on one control thread (sad.md §5: "transport commands, offset changes, device loss and picture-sync ticks are serialized on one thread and never touch a render thread or the UI thread").

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-04, AC-05, AC-07, AC-10, AC-15.

## Definition of Done

- [ ] Every session command (start, pause/resume, seek, offset, next/previous, exit) and every callback that mutates session state (health/freeze/move timers, engine events, renderer `DecodeFailed`/`HardwareDecodingInactive`) runs on one dedicated thread, one at a time, in posting order. Use the engine control loop as sad.md §5 says if it can host the session's blocking engine calls without deadlock; otherwise a session-owned command loop — record which and why in sad.md §8 (crosscutting, threading row).
- [ ] `PlaybackViewModel.HandleKey` posts the command and returns at once: the UI thread never waits for a probe, a pre-roll or mpv `Load`. A failed `Result` is not dropped (it is logged; T27 adds the note).
- [ ] Tests: a key handled while a slow command (blocking fake probe/engine) runs returns without waiting; two commands posted from different threads never interleave (a fake that detects re-entry); a timer callback and a command never run concurrently.
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
