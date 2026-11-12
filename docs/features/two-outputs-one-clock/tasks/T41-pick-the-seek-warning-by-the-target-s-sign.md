---
id: T41
title: "Pick the seek warning by the target's sign"
layer: "app"
deps: ["T40"]
blocks: []
acs: ["AC-10"]
files_hint: ["src/DualPlayer.Console/Rendering/EventPrinter.cs", "src/DualPlayer.Console/Commands/RunCommandLoop.cs", "tests/DualPlayer.Console.Tests/"]
owner: "Volodymyr Kravchuk"
estimate: "S"
context_budget: "S"
origin: "review-2026-11-12-r2 (R8)"
status: "done"
step: "COMMIT"
---

<!-- Follow-up task from the re-review. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [adr/](../adr/) · [review](../_review/review-2026-11-12-r2.md) · repo `CLAUDE.md`. Do not guess. -->

# T41 — Pick the seek warning by the target's sign

## Why

R8 (review-2026-11-12-r2): The printer chooses the warning with `Target > Position`. When the target is past the audio end but inside the container duration, `Position == Target`, so the before-the-start warning prints before `ended seek-past-end`, and the command loop keeps reading stdin.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-10.

## Definition of Done

- [ ] Before the start ⇔ `Target < 0`; everything else is past the end — in `EventPrinter` and `RunCommandLoop.Seek`.
- [ ] Tests: a past-end event with `Position == Target` prints the past-end warning and the loop returns.
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
