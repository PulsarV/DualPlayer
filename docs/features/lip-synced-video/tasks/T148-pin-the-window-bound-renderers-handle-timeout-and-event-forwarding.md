---
id: T148
title: "Pin the window-bound renderer's handle, timeout and event forwarding"
layer: "test"
deps: []
blocks: ["T23", "T152"]
acs: ["AC-06b", "AC-01"]
files_hint: ["tests/DualPlayer.App.Tests/Startup/WindowBoundRendererTests.cs"]
status: "todo"
step: "—"
owner: "Volodymyr Kravchuk"
estimate: "S"
context_budget: "S"
origin: "review-2026-11-23h (Y1, Y2)"
---

<!-- Follow-up task from review-2026-11-23h. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-23h.md) · repo `CLAUDE.md`. Do not guess. -->

# T148 — Pin the window-bound renderer's handle, timeout and event forwarding

## Why

- **Y1.** `WindowBoundRenderer` (`src/DualPlayer.App/Startup/VideoSurface.cs:16-63`) is the `IVideoRenderer` every production session gets (`CompositionRoot.cs:46-48`), but only its type is checked (`CompositionRootSmokeTests.cs:51`). Mutants `DecodeFailed { add { } remove { } }` (an undecodable picture ends with no note and no `picture-failed` line) and `Load` dropping `timeout` (the 3 s reload bound becomes mpv's 15 s) each pass App 166/166. AC-06b.
- **Y2.** A `Load` that ignores the surface handle makes mpv open its own window instead of drawing in the fullscreen one; also passes. AC-01.

Owner decision: fix now.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-06b, AC-01.

## Definition of Done

- [ ] New `WindowBoundRendererTests` over an NSubstitute `IVideoRenderer` inner and a `VideoSurface` with a known handle.
- [ ] `Load` with handle 0 passes the surface's handle to the inner renderer; `Load` with a non-zero handle passes that handle through; `file`, `startPaused` and `timeout` are forwarded unchanged (assert a 3 s timeout arrives as 3 s).
- [ ] Each event (`DecodeFailed`, `EndOfFile`, `HardwareDecodingInactive`) raised on the inner reaches a handler subscribed on the wrapper, and an unsubscribed handler no longer receives it; `DecodeFailed` carries the inner's `Error` unchanged.
- [ ] Mutants on a scratch copy, each failing at least one new test: `DecodeFailed { add { } remove { } }`; `Load` dropping `timeout`; `Load` ignoring the surface handle.
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
