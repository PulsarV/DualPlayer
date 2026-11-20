---
id: T42
title: "Publish start timings without lost outputs, time a file change from its key and print the next-file line first"
layer: "app"
deps: ["T41"]
blocks: []
acs: ["AC-01", "AC-07", "AC-17"]
files_hint: ["src/DualPlayer.Core/Playback/PlaybackSession.cs", "src/DualPlayer.App/Startup/TerminalPrinter.cs", "tests/DualPlayer.Core.Tests/Playback/", "tests/DualPlayer.App.Tests/E2E/PlayEndToEndTests.cs"]
owner: "Volodymyr Kravchuk"
estimate: "M"
context_budget: "M"
origin: "review-2026-11-21 (R5, R8, R9)"
status: "todo"
step: "—"
---

<!-- Follow-up task from review-2026-11-21. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-21.md) · repo `CLAUDE.md`. Do not guess. -->

# T42 — Publish start timings without lost outputs, time a file change from its key and print the next-file line first

## Why

R5 (stage-1, AC-01/AC-17, cli.md §1.1/§3): `_startTimings` holds every output connected at `StartRun` (`PlaybackSession.cs:344`); `PublishStartTimings` (`:1044-1063`) returns early while any output's `FirstAudioTimestamp` is null. An output lost in the pre-roll never gets one, so no `StartTimings` record, the printer prints `started` without timings at the summary, and every 5 ms sync tick makes a blocking `_loop.Execute` round trip to the engine for the rest of the film. `OnDeviceLost` never prunes `_startTimings`.

R8 (stage-1, AC-07, cli.md §2.2): `LatencyEstimated` is published inside `StartCore` (`:239`), `FileChanged` only after the whole start incl. pre-roll (`:495`); the terminal shows `output …`, `picture delay …`, then `next <file>` seconds late. E2E test uses `ShouldContain`, so order is untested.

R9 (stage-1, AC-07/AC-17, §6 start budget): `ChangeFile` calls `StartCore(request with { File = target }, …)` (`:478`) keeping the original `CommandTimestamp` from `Program.Main`; its own `commandAt` (`:400`) is used only for `_resync`. Later files' `StartTimings` are measured from process start.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-01, AC-07, AC-17.

## Definition of Done

- [ ] Outputs lost before their first audio are skipped when publishing `StartTimings` (or pruned in `OnDeviceLost`); timings publish once with the remaining outputs and the per-tick engine round trip stops.
- [ ] A file change's `StartTimings.CommandTimestamp` is the key time (`commandAt`).
- [ ] `FileChanged` is published once the target is chosen, before `StartCore`; on failure the existing note/log still apply.
- [ ] Tests: lose an output in the pre-roll → exactly one `StartTimings` with the remaining outputs; file change → `CommandTimestamp` equals the key time; `PlayEndToEndTests` asserts the line order `next <file>` before the new `output`/`picture delay` lines.
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
