---
id: T81
title: "Prove on hardware that the picture-failed note sits on the last good frame"
layer: "integration"
deps: ["T80"]
blocks: []
acs: ["AC-06b"]
files_hint: ["tests/DualPlayer.Media.Tests/Video/MpvVideoRendererTests.cs", "tests/Shared/", "docs/features/lip-synced-video/tasks/T76-show-the-picture-failed-note-on-the-last-good-frame.md", "docs/features/lip-synced-video/evidence/t23-acceptance-run-guide.md", "docs/features/lip-synced-video/test-plan.md"]
status: "todo"
step: "—"
owner: "Volodymyr Kravchuk"
estimate: "S"
context_budget: "S"
origin: "review-2026-11-22 (P2)"
---

<!-- Follow-up task from review-2026-11-22. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-22.md) · repo `CLAUDE.md`. Do not guess. -->

# T81 — Prove on hardware that the picture-failed note sits on the last good frame

## Why

P2 (rest of R2): T76 is `done` with DoD box :34 unticked. Its new hardware test (`MpvVideoRendererTests.cs:604-620`) reloads a healthy `long-gop-ramp.mkv` and seeks to a hard-coded 1 s; it never raises an error end-file and passes without the session's reload. The truncated-sample test (`:564-602`) notes that this Mac's mpv ends the sample with a plain end of file. "The note sits on the last good frame" is proven only by the fake-renderer unit test.

Owner decision: hardware test on the truncated sample (or a sample/stream that really raises an error end-file) — wait for `DecodeFailed`, reload + seek, assert `video-params` non-null and `time-pos` near the last presented position; if no sample on the dev Mac raises an error end-file, move the T76 :34 item into the T23 checklist explicitly.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-06b.

## Definition of Done

- [ ] Either a `[HardwareFact]` test raises a real error end-file, waits for `DecodeFailed`, runs the reload + seek and asserts `video-params` non-null and `time-pos` within one frame interval of the last presented position — and it fails without the reload; or, if no sample on the dev Mac raises an error end-file, the T23 checklist carries the item explicitly (with what to look at) and T76 :34 points there.
- [ ] The misleading healthy-file test at `MpvVideoRendererTests.cs:604-620` is removed or renamed to what it actually checks; test-plan.md rows match.
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
