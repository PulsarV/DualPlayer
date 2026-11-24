---
id: T105
title: "Pin the real renderer's mpv.load_timeout with a Media test on a stalled file"
layer: "integration"
deps: []
blocks: []
acs: ["AC-06b"]
files_hint: ["tests/DualPlayer.Media.Tests/Video/MpvVideoRendererTests.cs", "tests/DualPlayer.Media.Tests/AbandonedWorkerTests.cs", "src/DualPlayer.Media/Video/MpvVideoRenderer.cs"]
status: "done"
step: "COMMIT"
owner: "Volodymyr Kravchuk"
estimate: "S"
context_budget: "S"
origin: "review-2026-11-22d (V8)"
---

<!-- Follow-up task from review-2026-11-22d. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-22d.md) · repo `CLAUDE.md`. Do not guess. -->

# T105 — Pin the real renderer's mpv.load_timeout with a Media test on a stalled file

## Why

- **V8** (two reviewers). Only `FakeVideoRenderer` (`tests/DualPlayer.Core.Tests/Fakes/FakeVideoRenderer.cs:209`) returns `mpv.load_timeout`. Reverting `MpvVideoRenderer.cs:321` to `mpv.load_failed` leaves every test green (`MpvVideoRendererTests.cs:158-183` covers only `load_failed`), and every real reload timeout would again be logged `Outcome=failed`. This is the U1 pattern: the fake is right, the adapter is unchecked.

Owner decision: fix now.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-06b.

## Definition of Done

- [x] A Media test loads a stalled source (FIFO on macOS / named pipe on Windows, reusing the `AbandonedWorkerTests` helper) through `MpvVideoRenderer.Load(..., timeout: short)` and asserts `mpv.load_timeout`; it fails if `:321` returns `mpv.load_failed`. It skips like the other mpv tests when libmpv is absent.
- [x] The test does not depend on wall-clock slack beyond the timeout it passes (no flaky comparisons).
- [x] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
