---
id: T165
title: "Pin the Avalonia screen adapter and the window position with two screens"
layer: "ui"
deps: []
blocks: ["T23", "T168"]
acs: ["AC-01", "AC-03b"]
files_hint: ["src/DualPlayer.App/Views/PlaybackWindow.axaml.cs", "src/DualPlayer.App/Startup/AvaloniaScreenCatalog.cs", "tests/DualPlayer.App.Tests/Startup/ScreenChoiceTests.cs"]
status: "done"
step: "COMMIT"
owner: "Volodymyr Kravchuk"
estimate: "S"
context_budget: "S"
origin: "review-2026-11-23j (AB5)"
---

<!-- Follow-up task from review-2026-11-23j. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-23j.md) · repo `CLAUDE.md`. Do not guess. -->

# T165 — Pin the Avalonia screen adapter and the window position with two screens

## Why

- **AB5** (AA6 residual). The headless screen sits at (0,0) and is the primary, so `AvaloniaScreenCatalog.ToBounds` (`src/DualPlayer.App/Startup/AvaloniaScreenCatalog.cs:16`) and the window position (`src/DualPlayer.App/Views/PlaybackWindow.axaml.cs:46`) are unpinned. Mutants that pass App 193/193: `ToBounds` `s.IsPrimary` → `true` (no `--screen` opens on screen 1 when the main screen is listed second); X↔Y at :16; `new PixelPoint(chosen.Y, chosen.X)` and `new PixelPoint(0, 0)` at :46 (`--screen 2` lands on screen 1). Screens: SCR-02 (opening, fullscreen on the chosen screen).

Owner decision: fix now.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-01, AC-03b.

## Definition of Done

- [x] The window's target point is computed by an internal static helper over Avalonia screens (`PlaybackWindow.PlacementFor(IReadOnlyList<Screen>, int?)` → `PixelPoint?`) that `PlaceOnScreen` uses; no position is copied by hand outside it. *(PlaybackWindow.PlacementFor(IReadOnlyList<Screen>, int?) builds the PixelPoint from ScreenChoice.Pick over ToBounds; PlaceOnScreen assigns it unchanged)*
- [x] New tests with two built `Avalonia.Platform.Screen` objects, the primary listed second, both at non-zero, unequal X/Y: `ToBounds` keeps each screen's X, Y, width, height and only the second is primary; `PlacementFor(null)` and `PlacementFor(9)` give the primary's top-left; `PlacementFor(1)` the first's; `PlacementFor(2)` the second's. *(5 new ScreenChoiceTests over two built Avalonia screens (primary second, at (-1920,120) and (300,40)): ToBounds, no number, unknown number, numbers 1 and 2, no screens)*
- [x] Mutants on a scratch copy, each failing at least one new test: `ToBounds` `IsPrimary` → `true`; X↔Y in `ToBounds`; X↔Y in the placement point; placement point `(0, 0)`. *(ToBounds IsPrimary->true, ToBounds X<->Y, placement X<->Y, placement (0,0): each fails at least one new test; reverted)*
- [x] Existing headless and `ScreenChoiceTests` stay green. *(ScreenChoiceTests 13/13 and headless tests green)*
- [x] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean *(build 0 warnings, App 202 and all suites pass, format clean; one full run showed 3 unnamed App failures, not reproduced in an App-only run and 4 full re-runs)*
