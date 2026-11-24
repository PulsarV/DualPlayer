---
id: T154
title: "Pin the App.Play hand-off to the start lines and the window's screen number"
layer: "test"
deps: ["T153"]
blocks: ["T23", "T161"]
acs: ["AC-01", "AC-03b", "AC-12", "AC-13"]
files_hint: ["tests/DualPlayer.App.Tests/Startup/AppEntryTests.cs", "src/DualPlayer.App/Views/PlaybackWindow.axaml.cs", "src/DualPlayer.App/App.axaml.cs"]
status: "done"
step: "COMMIT"
owner: "Volodymyr Kravchuk"
estimate: "S"
context_budget: "S"
origin: "review-2026-11-23i (AA1)"
---

<!-- Follow-up task from review-2026-11-23i. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-23i.md) · repo `CLAUDE.md`. Do not guess. -->

# T154 — Pin the App.Play hand-off to the start lines and the window's screen number

## Why

- **AA1.** `App.Play` builds the output labels, the terminal context and the window placement (`App.axaml.cs:94-96, 104, 108`), but no test reads what it prints or where it places the window. Mutants that pass App 181/181: `new OutputLabel(1, …)` (every output line prints `track 1`); `?.Id` instead of `?.Name` (the line shows the endpoint id); `validated.Screen with { Number = 9 }` in `TerminalContext`; `window.PlaceOnScreen(null)` (`--screen 2` opens on the main screen). The T150 test runs a real start through `App.Play` but discards stdout (`AppEntryTests.cs:441`).

Owner decision: fix now.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-01, AC-03b, AC-12, AC-13.

## Definition of Done

- [x] The T150 `AppEntryTests` start (or a sibling case) captures stdout and asserts the lines `output 1  track 1  device <rig device name>`, `output 2  track 2  device <rig device name>` and `picture   delay …  screen 1  1920x1080` per cli.md §1.1. *(Test App_Play_prints_... pins output lines, picture line and PlacedScreenNumber; passes both theory cases)*
- [x] `PlaybackWindow` records the screen number it was last placed on (or the test observes it through a seam), and a case validated on screen 2 asserts `App.Play` passes number 2. *(internal PlacedScreenNumber seam set at top of PlaceOnScreen; asserted for screen 2)*
- [x] Mutants on a scratch copy, each failing at least one new test: `new OutputLabel(1, …)`; `?.Id` for `?.Name`; `Screen with { Number = 9 }` in `TerminalContext`; `PlaceOnScreen(null)`. *(4 mutants each fail both theory cases; reverted)*
- [x] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean *(build 0 warnings, all tests pass, format clean)*
