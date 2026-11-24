---
id: T167
title: "Read the captured stdout under the writer's lock in AppEntryTests"
layer: "test"
deps: []
blocks: ["T23"]
acs: []
files_hint: ["tests/DualPlayer.App.Tests/Startup/AppEntryTests.cs"]
status: "done"
step: "COMMIT"
owner: "Volodymyr Kravchuk"
estimate: "XS"
context_budget: "S"
origin: "review-2026-11-23j (AB7)"
---

<!-- Follow-up task from review-2026-11-23j. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-23j.md) · repo `CLAUDE.md`. Do not guess. -->

# T167 — Read the captured stdout under the writer's lock in AppEntryTests

## Why

- **AB7.** `PrintedSoFar` (`tests/DualPlayer.App.Tests/Startup/AppEntryTests.cs:598`, polled at :516) calls `StringWriter.ToString()` on the inner writer without the `TextWriter.Synchronized` lock while the session's start worker writes through the wrapper (:605); `StringBuilder.ToString` during a concurrent append can throw, so the test can fail rarely at random.

Owner decision: fix now.

## Acceptance criteria

See [spec.md](../spec.md) §5 none (test hardening).

## Definition of Done

- [x] `CaptureConsole` keeps the synchronised writer it installs; `PrintedSoFar` takes its snapshot under `lock` on that writer (the lock `TextWriter.Synchronized` itself uses). *(CaptureConsole keeps the TextWriter.Synchronized wrapper; PrintedSoFar snapshots under lock on it (SyncTextWriter methods are MethodImplOptions.Synchronized, i.e. lock(this)))*
- [x] No RED is possible for a rare race: the change is test hardening, verified by reading and by the App suite staying green. *(test hardening, no RED possible; App 193/193)*
- [x] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean *(build 0 warnings, all suites pass, format clean)*
