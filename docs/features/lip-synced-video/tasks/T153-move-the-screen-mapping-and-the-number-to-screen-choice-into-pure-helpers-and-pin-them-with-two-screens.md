---
id: T153
title: "Move the screen mapping and the number-to-screen choice into pure helpers and pin them with two screens"
layer: "ui"
deps: []
blocks: ["T23", "T154", "T161"]
acs: ["AC-01", "AC-03b"]
files_hint: ["src/DualPlayer.App/Startup/AvaloniaScreenCatalog.cs", "src/DualPlayer.App/Views/PlaybackWindow.axaml.cs", "src/DualPlayer.App/Startup/ScreenChoice.cs", "tests/DualPlayer.App.Tests/Startup/ScreenChoiceTests.cs"]
status: "done"
step: "COMMIT"
owner: "Volodymyr Kravchuk"
estimate: "S"
context_budget: "S"
origin: "review-2026-11-23i (AA6)"
---

<!-- Follow-up task from review-2026-11-23i. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-23i.md) · repo `CLAUDE.md`. Do not guess. -->

# T153 — Move the screen mapping and the number-to-screen choice into pure helpers and pin them with two screens

## Why

- **AA6.** Avalonia headless has one screen and it is the primary, so the T149 headless tests (`PlaybackWindowHeadlessTests.cs:122-153`) only run with n = 1. Mutants that pass App 181/181: `AvaloniaScreenCatalog.cs:19` `s.IsPrimary` → `true` (with no `--screen`, a PC whose main screen is listed second opens on screen 1 — the AC-01 default); `PlaybackWindow.axaml.cs:34` `screens[screenNumber.Value - 1]` → `screens[0]` (`--screen 2` lands on screen 1, AC-03b).

Owner decision: fix now.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-01, AC-03b.

## Definition of Done

- [x] A pure helper (in `src/DualPlayer.App/Startup/`, no Avalonia types in its signature beyond plain values) maps a list of (bounds, isPrimary) to `ScreenInfo` numbered from 1 in list order with `IsMain` from isPrimary; `AvaloniaScreenCatalog` uses it. *(ScreenChoice.Map in Startup/ScreenChoice.cs (plain ScreenBounds, numbered from 1, IsMain=IsPrimary); AvaloniaScreenCatalog uses it)
- [x] A pure helper picks the screen for a number (1-based index into the same list; `null` → the primary); `PlaybackWindow.PlaceOnScreen` uses it. *(ScreenChoice.Pick (1-based, null/unknown gives primary); PlaybackWindow.PlaceOnScreen uses it)
- [x] New `ScreenChoiceTests` with two screens, the primary listed second: the mapping numbers 1 and 2 and marks screen 2 main; no number picks screen 2; number 2 picks the second bounds; number 1 picks the first. *(ScreenChoiceTests: 8 cases with two screens, primary second, all pass)
- [x] Mutants on a scratch copy, each failing at least one new test: `IsPrimary` → `true`; `screens[n - 1]` → `screens[0]`; numbering `i + 1` → `i`. *(3 mutants (IsPrimary->true, screens[0], i+1->i) each fail 1 new test; all reverted)
- [x] Existing headless tests stay green; no behaviour change. *(App tests 189/189 incl. headless, no behaviour change)
- [x] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean *(build 0 warnings, all tests pass, dotnet format clean)
