---
id: T56
title: "Tighten the click-and-flash tolerance and map EPERM to access denied"
layer: "integration"
deps: []
blocks: []
acs: ["AC-03", "AC-16"]
files_hint: ["tests/DualPlayer.Media.Tests/Video/ClickAndFlashVideoTests.cs", "src/DualPlayer.Media/Video/FfmpegPictureProbe.cs", "tests/DualPlayer.Media.Tests/Video/FfmpegPictureProbeTests.cs"]
owner: "Volodymyr Kravchuk"
estimate: "S"
context_budget: "S"
origin: "review-2026-11-21b (Q15, Q16)"
status: "done"
step: "COMMIT"
---

<!-- Follow-up task from review-2026-11-21b. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-21b.md) · repo `CLAUDE.md`. Do not guess. -->

# T56 — Tighten the click-and-flash tolerance and map EPERM to access denied

## Why

Q15: `ClickAndFlashVideoTests.cs:52-53` allows each click up to one frame (41.7 ms) from its flash — almost the whole 45 ms budget AC-16 measures against. The generated clicks are sample-exact, so ≤ 2 ms is achievable.

Q16: `FfmpegPictureProbe.cs:20, 152` maps only EACCES (13) to access denied though the comment claims EPERM (1) too; on the Mac a file in a privacy-protected folder is reported as `playback.file_unreadable` (exit 2) instead of access denied (exit 4).

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-03, AC-16.

## Definition of Done

- [x] The click-to-flash tolerance is about 2 ms (or one audio sample block), and the test still passes on the generated clip.
- [x] `AVERROR(EPERM)` maps to `playback.file_access_denied`; a unit test covers the mapping of both error codes.
- [x] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
