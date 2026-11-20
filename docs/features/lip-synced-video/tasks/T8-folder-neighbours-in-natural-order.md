---
id: T8
title: "List a file's folder neighbours (.mkv/.mp4) in natural name order for next and previous"
layer: "domain"
deps: ["T5"]
blocks: ["T17"]
acs: ["AC-07", "AC-08"]
files_hint: ["src/DualPlayer.Core/Playback/FolderNeighbours.cs", "tests/DualPlayer.Core.Tests/Playback/FolderNeighboursTests.cs"]
owner: "Volodymyr Kravchuk"
estimate: "S"
context_budget: "S"
status: "todo"
step: ""
---

<!-- Self-contained task: work from what is inlined here. Every quoted chunk ends with its provenance
signature (`<file> §<section>, <identifier>, verbatim|abridged`); the source always wins. If a slice is
insufficient, ambiguous, or contradicts the code in front of you, open the named file for the full
text and follow that. Do not invent the missing part. -->

# T8 — List a file's folder neighbours (.mkv/.mp4) in natural name order for next and previous

## Place in the sequence

- **Blocked by:** T5 — Spike libmpv precision on the target PC against the ADR-0002 kill criterion and record the D1 verdict
- **Blocks:** T17 — Move to the next or previous playable file in the folder, skipping files that cannot play
- **Wave:** 4 — after its deepest dependency finishes (wave 3).
- **Lane:** own lane.

## Why (user story)

> **As a** Viewer
> **I want** to switch to the next or previous video file in the same folder with one key
> **So that** we can watch episodes of a series one after another
>
> — `spec.md §4, US-03, verbatim` · full text: [spec.md](../spec.md)

This task finds, in the right order, the files a next or previous press may move to; whether each is playable is decided by the session (T17).

## Inlined context

> `FolderNeighbours.cs` — .mkv/.mp4 in the file's folder, natural name order, skip rules (AC-07…AC-09b)
>
> — `sad.md §5, internal decomposition, Playback/FolderNeighbours.cs, verbatim` · full text: [sad.md](../sad.md)

> Folder size: next and previous list and naturally sort the folder on each key press. Thousands of files must still answer within the start budget (spec §6.1). The probe runs only on the file being moved to, plus each skipped file on the way.
>
> — `sad.md §7, scaling thresholds, folder size, verbatim` · full text: [sad.md](../sad.md)

> **Hard rule:** Ports and adapters: `DualPlayer.Core` references only the BCL and `Microsoft.Extensions.*.Abstractions` — no OS, UI or native library; OS code lives in `DualPlayer.Platform.Windows` / `.MacOS`, codec and video code in `DualPlayer.Media`.
>
> — `sad.md §2, Technical, ports and adapters, abridged` · full text: [sad.md](../sad.md)

**Fallback:** insufficient or contradicted by the code → read the named file in full ([spec.md](../spec.md) · [sad.md](../sad.md) · [data-model.md](../data-model.md) · [cli.md](../contracts/cli.md) · [screens.md](../screens.md) · [adr/](../adr/)) and follow it. Do not guess.

## Data delta

No DB changes.

## API contract

Internal — no API surface.

## Acceptance criteria

### AC-07 — happy path

> **Given** a film is playing, paused or has reached its end, and its folder holds other video files (files with the PLY-01 container extensions `.mkv` or `.mp4`, in any letter case; whether a file is playable is checked only for the file being moved to, as in AC-09 and AC-09b)
> **When** a Viewer presses the next-file or previous-file key
> **Then** the player finds the neighbouring playable file first, leaving the current film untouched while it looks (the folder is read again on every press), then stops the current file, prints and logs its run summary, and plays the neighbouring playable file in natural name order ("Episode 2" before "Episode 10"; names equal apart from letter case are ordered by exact comparison), on the same outputs with the same track numbers and offsets, lip-synced within the start budget (§6)
>
> — `spec.md §5, AC-07, verbatim` · full text: [spec.md](../spec.md)

### AC-08 — domain invariant

> **Given** a film is playing, paused or has reached its end, and no playable file follows it in its folder in natural name order (or precedes it, for the previous-file key)
> **When** a Viewer presses the next-file key (or the previous-file key)
> **Then** the current film stays as it was — playing, paused or at its end — and the window shows briefly that there is no next (or previous) file
>
> — `spec.md §5, AC-08, verbatim` · full text: [spec.md](../spec.md)

## Checklist

- [ ] `FolderNeighbours.Candidates(currentFile, direction)`: list the folder (BCL `System.IO`), keep `.mkv`/`.mp4` case-insensitively, natural-sort, yield the files after (next) or before (previous) the current one lazily — `src/DualPlayer.Core/Playback/FolderNeighbours.cs`
- [ ] Natural comparer: digit runs compare numerically; names equal apart from letter case fall back to exact ordinal comparison
- [ ] Tests in a temp directory, incl. a 10 000-file timing test — `tests/DualPlayer.Core.Tests/Playback/FolderNeighboursTests.cs`

## Edge cases

| Case | Behaviour |
|---|---|
| No file after (before) the current one | empty sequence → the session shows «no next/previous file» (AC-08) |
| Other extensions (`.avi`, `.srt`) and folders | ignored (AC-07 lists `.mkv` / `.mp4` only) |
| `EPISODE 1.mkv` and `Episode 1.mkv` | both kept, ordered by exact comparison (AC-07) |
| Folder changed between presses | re-read on every call (AC-07) |

## Definition of Done

- [ ] `FolderNeighboursTests` pass incl. the 10 000-file timing test
- [ ] every Hard Rule inlined above still holds
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` are clean
