---
id: T71
title: "Time recovery from the key press and budget the return into the film as a start"
layer: "app"
deps: []
blocks: []
acs: ["AC-04", "AC-05", "AC-17"]
files_hint: ["src/DualPlayer.Core/Playback/PlaybackSession.cs", "docs/features/lip-synced-video/spec.md", "docs/features/lip-synced-video/contracts/cli.md", "tests/DualPlayer.Core.Tests/Playback/"]
status: "todo"
step: "—"
owner: "Volodymyr Kravchuk"
estimate: "M"
context_budget: "M"
origin: "review-2026-11-21d (R5)"
---

<!-- Follow-up task from review-2026-11-21d. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-21d.md) · repo `CLAUDE.md`. Do not guess. -->

# T71 — Time recovery from the key press and budget the return into the film as a start

## Why

R5: recovery for seek, resume and return-into-the-film is timed from after the engine command, not from the key. `_resync` for seek and resume is stamped after `_engine.Seek` (`WaitForSeek`) or `_engine.Resume` (`WaitForStartLevel`) returns (`PlaybackSession.cs:919-926, 952` resume; `1019, 1045, 1067` seek); return-into-the-film after the picture reload (`:196, 325, 340-341, 418`). A file change is stamped at the key (`:469`). So `LipSyncedAgain`/`NotLipSynced` "ms since the command", which T23 L2 reads, leaves those waits out. A backward seek at the end runs a full engine start with pre-roll (~2 s with the MOTIF headset, `sad.md:590`) yet is judged as `seek` against 1 s (`:1210, 1278`), so it always logs `NotLipSynced` on the target PC. Owner decision in review-2026-11-21d: stamp at the key, and budget return-into-the-film as a start (3 s) under its own command name.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-04, AC-05, AC-17.

## Definition of Done

- [ ] `SeekCore` and `ResumeCore` take the recovery timestamp at entry (before the engine call); `ReturnIntoFilm` receives the key's timestamp from its caller.
- [ ] Return-into-the-film is judged against the start budget (3 s) and is published with its own command name (e.g. `return`), not `seek`.
- [ ] spec.md §6 recovery row and cli.md §3 (`LipSyncedAgain`/`NotLipSynced` command values) name the new command and its budget.
- [ ] Tests with the fake clock: a seek whose engine call takes 300 ms reports ms-since-command including those 300 ms; a resume likewise; a backward seek from Ended that recovers in 2 s logs `LipSyncedAgain` with the return command, not `NotLipSynced`.
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
