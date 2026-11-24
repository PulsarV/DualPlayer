---
id: T17
title: "Move to the next or previous playable file in the folder, skipping files that cannot play"
layer: "app"
deps: ["T8", "T9", "T13"]
blocks: ["T20"]
acs: ["AC-07", "AC-08", "AC-09", "AC-09b"]
files_hint: ["src/DualPlayer.Core/Playback/PlaybackSession.cs", "tests/DualPlayer.Core.Tests/Playback/PlaybackSessionFileChangeTests.cs"]
owner: "Volodymyr Kravchuk"
estimate: "M"
context_budget: "M"
status: "done"
step: "COMMIT"
---

<!-- Self-contained task: work from what is inlined here. Every quoted chunk ends with its provenance
signature (`<file> §<section>, <identifier>, verbatim|abridged`); the source always wins. If a slice is
insufficient, ambiguous, or contradicts the code in front of you, open the named file for the full
text and follow that. Do not invent the missing part. -->

# T17 — Move to the next or previous playable file in the folder, skipping files that cannot play

## Place in the sequence

- **Blocked by:** T8 — List a file's folder neighbours (.mkv/.mp4) in natural name order for next and previous, T9 — Probe a file's picture with FFmpeg in hardware, off the caller's thread, with a time limit, T13 — Start a lip-synced playback session, end it at the natural end or on exit, and emit session events
- **Blocks:** T20 — Build the fullscreen playback window hosting mpv's video, mapping the fixed keys and styling notes from tokens
- **Wave:** 7 — after its deepest dependency finishes (wave 6).
- **Lane:** shares `src/DualPlayer.Core/Playback/PlaybackSession.cs` with T13, T14, T15, T16, T18 — serialized.

## Why (user story)

> **As a** Viewer
> **I want** to switch to the next or previous video file in the same folder with one key
> **So that** we can watch episodes of a series one after another
>
> — `spec.md §4, US-03, verbatim` · full text: [spec.md](../spec.md)

This task lets the Viewers watch episodes back to back with one key, never stopping a film for a file that would not play.

## Inlined context

> Flow 3: next-file key → loop each following file in natural name order: list its tracks and probe its picture; missing a track an output plays, unreadable or not hardware-decodable → note naming the skipped file and why, log file, output and reason; playable → stop the current file, run summary logged and printed, start the new file on the same outputs, tracks and offsets, load the new file and play at clock time 0 plus picture delay. No playable file left → note no next file, current film unchanged.
>
> — `sad.md §6, flow 3, abridged` · full text: [sad.md](../sad.md)

| State | Trigger / condition | Components (from the inventory) | Source-ref |
|---|---|---|---|
| no next / previous | No playable file in that direction (AC-08, flow 3) | `OsdNote` — `No next file` / `No previous file` | wireframe below |
| skipped file | Neighbour lacks a track, unreadable, no picture, not hardware-decodable (AC-09, AC-09b); several on one press → the last note counts them | `OsdNote` — `Skipped Episode-03.mkv: no track 2 for output 2` / `Skipped 3 files; last: Episode-05.mkv - picture cannot be played on this PC` | wireframe below |

— `screens.md SCR-03, rows no next/previous + skipped file, verbatim` · full text: [screens.md](../screens.md)

> The session runs on the step-3 engine control loop (two-outputs-one-clock ADR-0007), so transport commands, offset changes, device loss and picture-sync ticks are serialized on one thread and never touch a render thread or the UI thread. New session events travel on their own `SessionEvent` stream in `Core/Playback/`, merged with the engine events by the host, so `Core/Diagnostics/` (step 6) is not edited.
>
> — `sad.md §5, intro, abridged` · full text: [sad.md](../sad.md)

**Fallback:** insufficient or contradicted by the code → read the named file in full ([spec.md](../spec.md) · [sad.md](../sad.md) · [data-model.md](../data-model.md) · [cli.md](../contracts/cli.md) · [screens.md](../screens.md) · [adr/](../adr/)) and follow it. Do not guess.

## Data delta

No DB changes.

## API contract

```
skipped  <file name>  reason track-missing  output 2  track 2
skipped  <file name>  reason not-hardware-decodable  codec hevc  profile main-10
next     <file name>
```

`reason` is one of `track-missing`, `unreadable`, `no-picture`, `not-hardware-decodable`, `access-denied`. When no playable file is found in that direction: `no-next-file` (or `no-previous-file`), and the current film is unchanged. A new file prints the §1.1 output and start lines again.

— `contracts/cli.md §2.2, file change, verbatim` · full text: [cli.md](../contracts/cli.md)

This task raises the events carrying these fields; printing is T21.

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

### AC-09 — cross-context

> **Given** the neighbouring file lacks a track number that one of the outputs plays (for example it has one audio track while an output plays track 2)
> **When** a Viewer presses the next-file or previous-file key
> **Then** that file is skipped and the next playable file in the same direction plays (AC-07), the window shows briefly which file was skipped and whose track it lacks (when several files are skipped on one press, the last note names the last skipped file and how many were skipped), and the Developer's log names the file, the output and the missing track
>
> — `spec.md §5, AC-09, verbatim` · full text: [spec.md](../spec.md)

### AC-09b — error

> **Given** the neighbouring file cannot be read, has no picture, or has a picture this PC cannot decode in hardware (as in AC-02)
> **When** a Viewer presses the next-file or previous-file key
> **Then** that file is skipped in the same way as in AC-09, the window shows briefly which file was skipped and why in plain words, and the Developer's log names the file and the reason
>
> — `spec.md §5, AC-09b, verbatim` · full text: [spec.md](../spec.md)

## Checklist

- [ ] `NextFile()` / `PreviousFile()`: walk `FolderNeighbours.Candidates`; per candidate list audio tracks (step-3 decoder) and `IPictureProbe.Probe`, off the control loop with the time limit — `src/DualPlayer.Core/Playback/PlaybackSession.cs`
- [ ] Skip → `FileSkipped` (file, reason, output/track) + note; several skips → one final note naming the last file and the count
- [ ] Playable → current run summary (reason `file-change`), start the new file with the same outputs/tracks/offsets as in T13 (`LatencyEstimated` again), `FileChanged`
- [ ] None → `NoNeighbour` + note; film state untouched
- [ ] Tests with `FakePictureProbe` and a temp folder — `tests/DualPlayer.Core.Tests/Playback/PlaybackSessionFileChangeTests.cs`

## Edge cases

| Case | Behaviour |
|---|---|
| Pressed while paused or ended | same search; the new file plays (AC-07) |
| Neighbour with one audio track while an output plays track 2 | skipped, note names file and output (AC-09) |
| Thousands of files | answer within the start budget (§6.1); probe only the file moved to plus skipped ones |
| Seek/resume/file change during a PD move | targets set at once (AC-14) |

## Definition of Done

- [x] `PlaybackSessionFileChangeTests` pass for AC-07, AC-08, AC-09, AC-09b
- [x] every Hard Rule inlined above still holds
- [x] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` are clean
