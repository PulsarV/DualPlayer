---
id: T142
title: "Pin the AC-09b skip reasons and note texts for no picture, access denied and unreadable through the session"
layer: "test"
deps: []
blocks: ["T144", "T23"]
acs: ["AC-09b"]
files_hint: ["tests/DualPlayer.Core.Tests/Playback/PlaybackSessionFileChangeTests.cs", "tests/DualPlayer.App.Tests/Startup/TerminalPrinterRawLineTests.cs"]
status: "todo"
step: "—"
owner: "Volodymyr Kravchuk"
estimate: "S"
context_budget: "S"
origin: "review-2026-11-23f (W1)"
---

<!-- Follow-up task from review-2026-11-23f. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-23f.md) · repo `CLAUDE.md`. Do not guess. -->

# T142 — Pin the AC-09b skip reasons and note texts for no picture, access denied and unreadable through the session

## Why

- **W1.** No test checks the skip-note texts "no picture" and "access denied" (`SkipText`, `PlaybackSession.cs:1038-1045`), or the `access-denied` reason through the session (`ReasonOf`, `PlaybackSession.cs:1030-1036`). Mutants (texts changed; `playback.file_access_denied` → `unreadable`) pass the whole suite. AC-09b: "which file was skipped and why in plain words"; cli.md §2.2 lists `reason access-denied`; screens.md:110 SCR-03 "skipped file".

Owner decision: fix now.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-09b.

## Definition of Done

- [ ] New theory in `PlaybackSessionFileChangeTests` with `rig.Pictures.Fail(<Episode-02>, code, …)`: `video.no_picture` → reason `no-picture` and note `"Skipped Episode-02.mkv: no picture"`; `playback.file_access_denied` → `access-denied` and `"Skipped Episode-02.mkv: access denied"`; `playback.file_unreadable` → `unreadable` and `"Skipped Episode-02.mkv: file cannot be read"`; each asserts `FileSkipped.Reason` and the exact `NoteShown` text with `NoteStyle.Warning`.
- [ ] With `SkipText` "no picture"/"access denied" changed, or `ReasonOf` mapping `playback.file_access_denied` to `unreadable` (scratch copy), the theory fails.
- [ ] `TerminalPrinterRawLineTests` pins `skipped  X.mkv  reason access-denied`.
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
