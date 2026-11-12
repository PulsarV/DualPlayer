---
id: T52
title: "Bound the stop wait in polls the test can set"
layer: "test"
deps: []
blocks: []
acs: ["AC-08"]
files_hint: ["src/DualPlayer.Core/Playback/AudioEngine.cs", "tests/DualPlayer.Core.Tests/Playback/AudioEngineStopBoundTests.cs"]
owner: "Volodymyr Kravchuk"
estimate: "S"
context_budget: "S"
origin: "review-2026-11-12-r4 (T1)"
status: "done"
step: "COMMIT"
---

<!-- Follow-up task from re-review 4. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [adr/](../adr/) · [review](../_review/review-2026-11-12-r4.md) · repo `CLAUDE.md`. Do not guess. -->

# T52 — Bound the stop wait in polls the test can set

## Why

T1 (review-2026-11-12-r4): the stop-wait real-time bound is `StopWaitMaxPolls` = 2000 polls of `Thread.Sleep(2)` (`AudioEngine.cs:25,822,827`). On macOS that is ~4 s; on Windows (15.6 ms default timer) ~31 s, above the 20 s `CompletesWithin` limit in `AudioEngineStopBoundTests.cs:30` — the test will likely fail on `windows-latest`. Production is unaffected (the real 2 s timestamp deadline ends first).

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-08.

## Definition of Done

- [ ] The poll bound is an internal engine parameter (e.g. internal `EngineOptions`/constructor value, default unchanged in production); Core still uses no `Stopwatch`/`DateTime.Now`.
- [ ] `AudioEngineStopBoundTests` sets a small bound (~50 polls) and a limit that holds at 15.6 ms ticks (e.g. ≤ 5 s); the test still fails if the bound is removed.
- [ ] The test runs in well under 2 s on macOS.
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
