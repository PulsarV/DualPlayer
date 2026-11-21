---
id: T67
title: "Keep the window and last frame up after a picture failure so its note is seen"
layer: "integration"
deps: []
blocks: []
acs: ["AC-06b", "AC-07"]
files_hint: ["src/DualPlayer.Media/Video/MpvVideoRenderer.cs", "tests/DualPlayer.Media.Tests/Video/MpvVideoRendererTests.cs", "docs/features/lip-synced-video/evidence/t23-acceptance-run-guide.md"]
status: "todo"
step: "SELECT"
owner: "Volodymyr Kravchuk"
estimate: "S"
context_budget: "S"
origin: "review-2026-11-21c (S13)"
---

<!-- Follow-up task from review-2026-11-21c. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-21c.md) · repo `CLAUDE.md`. Do not guess. -->

# T67 — Keep the window and last frame up after a picture failure so its note is seen

## Why

S13: `DecodeFailed` comes from an error end-file (`MpvVideoRenderer.cs:880-903`); the hardening options have no `force-window` (`:219-233`, `keep-open` covers only end-of-file), so mpv tears down its video output when idle and `show-text` has nothing to draw on — the "picture failed" note (screens.md:76) and "Could not play X" (:113, :115) are likely never seen. The test only checks that `ShowNote` does not throw (`MpvVideoRendererTests.cs:547-548`).

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-06b, AC-07.

## Definition of Done

- [ ] mpv keeps its window after an error end-file (`force-window=yes`, or a paused reload at the failure position before the note), and a note shown after it is drawn.
- [ ] A test checks the option is set (unit) and a `[HardwareFact]` test shows a note after an error end-file on a truncated sample and reads `osd`/window state back.
- [ ] The T23 run guide has a checklist item: a truncated file shows the picture-failed note over the last good frame.
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
