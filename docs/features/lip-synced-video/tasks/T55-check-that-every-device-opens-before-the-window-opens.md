---
id: T55
title: "Check that every device opens before the window opens"
layer: "wiring"
deps: []
blocks: []
acs: ["AC-01", "AC-12"]
files_hint: ["src/DualPlayer.App/Startup/StartCommand.cs", "src/DualPlayer.App/App.axaml.cs", "src/DualPlayer.Core/Outputs/", "src/DualPlayer.Platform.Windows/", "src/DualPlayer.Platform.MacOS/", "tests/DualPlayer.App.Tests/Startup/"]
owner: "Volodymyr Kravchuk"
estimate: "M"
context_budget: "M"
origin: "review-2026-11-21b (Q14)"
status: "todo"
step: ""
---

<!-- Follow-up task from review-2026-11-21b. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-21b.md) · repo `CLAUDE.md`. Do not guess. -->

# T55 — Check that every device opens before the window opens

## Why

Q14: `outputs.device_access_denied` is found only inside `session.Start`, after `window.Show()` (`App.axaml.cs:105-161`); `StartCommand.Validate` (`StartCommand.cs:90`) checks only that a device exists and is not used twice. A black fullscreen window flashes before exit 4, while `contracts/cli.md:34` says every check runs before any window opens and `screens.md:35` lists device access denied as "no window".

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-01, AC-12.

## Definition of Done

- [ ] `Validate` tries to open each chosen device (through a Core port the platform adapters implement; open and release, no playback) and maps a refusal to `outputs.device_access_denied`, exit 4, before any window opens.
- [ ] Tests: a device that refuses to open → exit 4, the cli.md error line, and the window factory is never called.
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
