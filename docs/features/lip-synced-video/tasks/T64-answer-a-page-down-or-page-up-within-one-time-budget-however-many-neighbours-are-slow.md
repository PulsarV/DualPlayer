---
id: T64
title: "Answer a Page Down or Page Up within one time budget however many neighbours are slow"
layer: "app"
deps: ["T63"]
blocks: []
acs: ["AC-09b"]
files_hint: ["src/DualPlayer.Core/Playback/PlaybackSession.cs", "tests/DualPlayer.Core.Tests/Playback/", "tests/DualPlayer.Core.Tests/Fakes/"]
status: "done"
step: "COMMIT"
owner: "Volodymyr Kravchuk"
estimate: "M"
context_budget: "M"
origin: "review-2026-11-21c (S14)"
---

<!-- Follow-up task from review-2026-11-21c. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-21c.md) · repo `CLAUDE.md`. Do not guess. -->

# T64 — Answer a Page Down or Page Up within one time budget however many neighbours are slow

## Why

S14: one press checks neighbours one after another (`PlaybackSession.cs:477-491`) with a 2 s picture-probe limit each (`:55`) and no limit on `ListTracks` (`:582`); the note appears only after the loop (`:500-516`). Two slow files give ≥ 4 s with no answer and Space/Esc queue behind the scan. `spec.md:247` says no key goes unanswered for longer than 3 s; `test-plan.md:134` lists the case with no test.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-09b.

## Definition of Done

- [x] Each Page Down/Up press has one deadline inside the 3 s budget; each candidate's probe is limited to the time left, and when the deadline passes the scan stops with the combined note ("No next file (skipped N; last: …)" per screens.md) and log entries.
- [x] Test with a fake probe that advances fake time: three slow neighbours → the press answers within the budget and names the skipped count.
- [x] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
