---
id: T50
title: "Test the played duration of a seek past the end on both branches within 100 ms"
layer: "test"
deps: ["T46"]
blocks: []
acs: ["AC-10", "AC-13"]
files_hint: ["tests/DualPlayer.Core.Tests/Playback/AudioEngineTransportTests.cs", "tests/DualPlayer.Core.Tests/Fakes/"]
owner: "Volodymyr Kravchuk"
estimate: "S"
context_budget: "S"
origin: "review-2026-11-12-r3 (S10)"
status: "todo"
step: "SELECT"
---

<!-- Follow-up task from re-review 3. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [adr/](../adr/) · [review](../_review/review-2026-11-12-r3.md) · repo `CLAUDE.md`. Do not guess. -->

# T50 — Test the played duration of a seek past the end on both branches within 100 ms

## Why

S10 (review-2026-11-12-r3): The past-end played test covers only the duration-known branch at ±1.5 s; the heard-before branch (`Duration` null) and ±100 ms from the T35 DoD are untested.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-10, AC-13.

## Definition of Done

- [ ] A decoder fake with `Duration => null`.
- [ ] Both branches assert |Played − pre-seek play time| ≤ 0.1 s.
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
