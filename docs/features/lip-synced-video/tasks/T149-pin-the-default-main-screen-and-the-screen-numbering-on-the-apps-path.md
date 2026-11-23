---
id: T149
title: "Pin the default main screen and the screen numbering on the App's path"
layer: "test"
deps: []
blocks: ["T23", "T152"]
acs: ["AC-01", "AC-03b"]
files_hint: ["tests/DualPlayer.App.Tests/Startup/StartCommandTests.cs", "tests/DualPlayer.App.Tests/Views/PlaybackWindowHeadlessTests.cs", "tests/DualPlayer.App.Tests/Startup/AvaloniaScreenCatalogTests.cs"]
status: "todo"
step: "—"
owner: "Volodymyr Kravchuk"
estimate: "S"
context_budget: "S"
origin: "review-2026-11-23h (Y2)"
---

<!-- Follow-up task from review-2026-11-23h. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-23h.md) · repo `CLAUDE.md`. Do not guess. -->

# T149 — Pin the default main screen and the screen numbering on the App's path

## Why

- **Y2.** `App.Play` always calls `PlaceOnScreen(validated.Screen.Number)` (`App.axaml.cs:108`), so the default screen comes from the `IsMain` lookup in `StartCommand.Validate` (`StartCommand.cs:111-113`) and the number from `AvaloniaScreenCatalog` (`AvaloniaScreenCatalog.cs:19`). The headless test covers only `PlaceOnScreen(null)`. Mutants that pass App 166/166: `:113` → `list.FirstOrDefault()` (`FakeScreens` lists main first, `StartCommandTests.cs:336`); catalog numbering `i + 1` → `i`. AC-01 (main screen by default); AC-03b and cli.md §1.1 (`--screen` numbered from 1 as listed in `start.screen_not_found`).

Owner decision: fix now.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-01, AC-03b.

## Definition of Done

- [ ] A `StartCommandTests` case with the main screen listed second: an omitted `--screen` resolves to screen number 2 (the main one).
- [ ] A headless test that `AvaloniaScreenCatalog.Screens` numbers from 1 in Avalonia's order and marks `Screens.Primary` as main, and that `PlaybackWindow.PlaceOnScreen(n)` places the window on the screen the catalog calls n.
- [ ] Mutants on a scratch copy, each failing at least one new test: `StartCommand.cs:113` → `list.FirstOrDefault()`; catalog numbering `i + 1` → `i`.
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
