---
id: T37
title: "Decorate the video renderer independent of registration order"
layer: "wiring"
deps: ["T34"]
blocks: []
acs: ["AC-01"]
files_hint: ["src/DualPlayer.App/CompositionRoot.cs", "src/DualPlayer.Media/MediaServiceCollectionExtensions.cs", "tests/DualPlayer.App.Tests/CompositionRootSmokeTests.cs"]
owner: "Volodymyr Kravchuk"
estimate: "S"
context_budget: "S"
origin: "review-2026-11-20 (F23)"
status: "done"
step: "COMMIT"
---

<!-- Follow-up task from review-2026-11-20. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-20.md) · repo `CLAUDE.md`. Do not guess. -->

# T37 — Decorate the video renderer independent of registration order

## Why

F23 (stage-2): the composition root registers `MpvVideoRenderer` and `WindowBoundRenderer` before `AddMedia()`, so the decorator depends on registration order (`CompositionRoot.cs:37-42`; `MediaServiceCollectionExtensions.cs:15-16`).

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-01.

## Definition of Done

- [x] `AddMedia()` registers the renderer; the App replaces/decorates `IVideoRenderer` explicitly after it (no reliance on TryAdd order). The smoke test resolves `IVideoRenderer` as `WindowBoundRenderer` regardless of order.
- [x] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
