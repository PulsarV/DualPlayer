---
id: T4
title: "Implement the hardened libmpv video renderer with presented-position snapshots and OSD notes"
layer: "infra"
deps: ["T1", "T2"]
blocks: ["T5", "T20"]
acs: ["AC-01", "AC-06b", "AC-17"]
files_hint: ["src/DualPlayer.Media/Video/MpvVideoRenderer.cs", "src/DualPlayer.Media/MediaServiceCollectionExtensions.cs", "tests/DualPlayer.Media.Tests/Video/MpvVideoRendererTests.cs"]
owner: "Volodymyr Kravchuk"
estimate: "L"
context_budget: "M"
status: "done"
step: "COMMIT"
---

<!-- Self-contained task: work from what is inlined here. Every quoted chunk ends with its provenance
signature (`<file> §<section>, <identifier>, verbatim|abridged`); the source always wins. If a slice is
insufficient, ambiguous, or contradicts the code in front of you, open the named file for the full
text and follow that. Do not invent the missing part. -->

# T4 — Implement the hardened libmpv video renderer with presented-position snapshots and OSD notes

## Place in the sequence

- **Blocked by:** T1 — Define the IVideoRenderer port and a scripted fake renderer in Core, T2 — Fetch a pinned libmpv, copy it beside the executables, and load it with a version check
- **Blocks:** T5 — Spike libmpv precision on the target PC against the ADR-0002 kill criterion and record the D1 verdict, T20 — Build the fullscreen playback window hosting mpv's video, mapping the fixed keys and styling notes from tokens
- **Wave:** 2 — after its deepest dependency finishes (wave 1).
- **Lane:** shares `src/DualPlayer.Media/MediaServiceCollectionExtensions.cs` with T9 — serialized.

## Why (user story)

> **As a** Viewer
> **I want** to watch a film the Developer started for us in fullscreen and hear my own track in my own device in step with the lips on screen
> **So that** I can watch the same film as the other Viewer in my own language without being distracted by sound that runs ahead of or behind the picture
>
> — `spec.md §4, US-01, verbatim` · full text: [spec.md](../spec.md)

This task is the adapter that actually decodes in hardware and presents the picture, reporting back the position and counters the Core loop steers by.

## Inlined context

> **mpv's own video output in a native child window; notes via mpv's OSD.** Avalonia `NativeControlHost` provides a child window handle passed to mpv as `wid`. mpv decodes and presents with zero-copy hardware decoding and its own vsync timing. The note is an mpv OSD overlay styled from `Themes/Tokens.axaml`.
>
> — `adr/0003 §Considered options, option 1 (chosen), verbatim` · full text: [ADR-0003](../adr/0003-embed-mpv-video-output-in-a-native-child-window-and-draw-notes-with-its-osd.md)

> `MpvVideoRenderer.cs` — IVideoRenderer: audio off, hwdec only, own video output into the given window handle, input off, OSD notes, property observation on mpv's event thread
>
> — `sad.md §5, internal decomposition, Media/Video/MpvVideoRenderer.cs, verbatim` · full text: [sad.md](../sad.md)

> **Hard rule:** mpv starts with no user config, no scripts, no on-screen controller, no default key bindings, no keyboard on its video output, no network helpers, audio off, and hardware decoding only (no copy-back, no software fallback): `config=no`, `load-scripts=no`, `osc=no`, `input-default-bindings=no`, `input-vo-keyboard=no`, `ytdl=no`, `audio=no`, `hwdec=d3d11va` on Windows
>
> — `sad.md §8, mpv hardening, verbatim` · full text: [sad.md](../sad.md)

> Precision is bounded by what mpv reports: presented-position readings are frame- and vsync-granular, and the "presented vs due" log (AC-17) is an estimate, not a swapchain statistic.
>
> — `adr/0002 §Consequences, negative, verbatim` · full text: [ADR-0002](../adr/0002-render-video-with-libmpv-slaved-to-the-master-clock-behind-a-renderer-port.md)

> If mpv later reports that hardware decoding is not active (`hwdec-current` = no), that is treated as a picture decode failure (AC-06b path, or a skip during next/previous).
>
> — `adr/0007 §Considered options, option 1, abridged` · full text: [ADR-0007](../adr/0007-probe-the-picture-with-ffmpeg-before-the-window-opens.md)

> The note's font size, colours and margins come from `Themes/Tokens.axaml` (body ≥ 32 px, contrast ≥ 7:1) and are passed to mpv's OSD; held 2 s after the last change, a new note replaces the current one
>
> — `sad.md §8, UI and on-screen note, verbatim` · full text: [sad.md](../sad.md)

> All lip-sync arithmetic is in Core on the master clock and `TimeProvider`; no `DateTime.Now` / `Stopwatch` in Core. The mpv adapter converts mpv's timestamps to the same monotonic time base the step-3 backend uses (QPC on Windows) before handing them to Core
>
> — `sad.md §8, Time and clocks, verbatim` · full text: [sad.md](../sad.md)

> **Hard rule:** The playback session, picture sync and lag ramps run on the step-3 engine control loop. mpv property and end events arrive on mpv's event thread; the adapter publishes immutable snapshots without locks the control loop could wait on. Render threads are never blocked. UI updates marshal through `Dispatcher.UIThread`. The picture probe runs off the UI thread with a time limit
>
> — `sad.md §8, Threading, verbatim` · full text: [sad.md](../sad.md)

> **Hard rule:** FFmpeg 8 through FFmpeg.AutoGen 8.1.0 and libmpv (`libmpv-2.dll` beside the exe) — both LGPL: dynamic linking only, LGPL builds only. On the dev Mac both come from Homebrew, outside the repo; libmpv is not installed there yet.
>
> — `sad.md §2, Technical, native libraries, verbatim` · full text: [sad.md](../sad.md)

**Fallback:** insufficient or contradicted by the code → read the named file in full ([spec.md](../spec.md) · [sad.md](../sad.md) · [data-model.md](../data-model.md) · [cli.md](../contracts/cli.md) · [screens.md](../screens.md) · [adr/](../adr/)) and follow it. Do not guess.

## Data delta

No DB changes.

## API contract

Internal — no API surface.

## Acceptance criteria

### AC-01 — happy path

> **Given** a video file with a picture and at least two audio tracks, and the Developer's start command names the file, a track and a device for each Viewer, and a screen
> **When** the start command is given
> **Then** the film opens fullscreen on the named screen (on the main screen when none is named), the picture and every output's sound start together within the start budget (§6), and each Viewer hears their own track; on a device whose offset was set in an earlier run (AC-12) the sound is in step with the lips within the lip-sync budget (§6), on a device's first use the error is only measured (§7)
>
> — `spec.md §5, AC-01, verbatim` · full text: [spec.md](../spec.md)

### AC-06b — error

> **Given** a film is playing
> **When** part of its picture cannot be decoded (a damaged stretch of the file)
> **Then** the damaged frames are skipped while the sound plays on and the log counts them; if the picture cannot be decoded again at all, the film stops as at its natural end (AC-06) and the window and the Developer's log say in plain words that the picture could not be decoded further
>
> — `spec.md §5, AC-06b, verbatim` · full text: [spec.md](../spec.md)

### AC-17 — happy path

> **Given** a film is playing
> **When** the picture delay or any output's latency or offset changes, or a seek, pause, resume or file change happens
> **Then** the log records the master-clock position, the new picture delay, each output's assumed latency split into reported part and type default, and its offset, and how far behind or ahead the picture was presented compared with when it was due
>
> — `spec.md §5, AC-17, verbatim` · full text: [spec.md](../spec.md)

## Checklist

- [ ] Expose the hardening options as a pure, testable list and set them before `mpv_initialize`; `hwdec=d3d11va` on Windows, the macOS hardware decoder on the dev Mac — `src/DualPlayer.Media/Video/MpvVideoRenderer.cs`
- [ ] `Load`: set `wid` to the handed-over native handle, `loadfile` paused at the start position; map failures to `Result` errors
- [ ] Commands: play/pause (`pause`), absolute exact seek, `speed`; `ShowNote` via OSD with the style values the caller passes
- [ ] Observe the properties for presented position, late/dropped frame counts, `hwdec-current` and end of file on mpv's event thread; convert mpv timestamps to the step-3 monotonic time base; publish immutable snapshots (no locks the control loop could wait on); raise decode-failure / hardware-inactive / end events
- [ ] Dispose: terminate mpv and its event thread cleanly
- [ ] Register `IVideoRenderer` in `AddMedia` — `src/DualPlayer.Media/MediaServiceCollectionExtensions.cs`
- [ ] Tests: option list (pure), and a `[HardwareFact]`/library-present test over a short sample generated with FFmpeg — `tests/DualPlayer.Media.Tests/Video/MpvVideoRendererTests.cs`

## Edge cases

| Case | Behaviour |
|---|---|
| `hwdec-current` reports no hardware decoding after load | raise the hardware-inactive event; never fall back to software (AC-02, AC-06b) |
| Damaged frames skipped by the decoder | skipped-damaged counter increases; playback continues |
| Native child window would take keyboard focus | mpv input off (`input-default-bindings=no`, `input-vo-keyboard=no`) — `sad.md §11` focus risk |
| Event thread raises while Core is mid-tick | Core sees the previous or next whole snapshot, never a torn one |

## Definition of Done

- [x] `MpvVideoRendererTests` pass (pure option list everywhere; playback test where libmpv is present)
- [x] the hardening option list matches `sad.md §8` exactly
- [x] every Hard Rule inlined above still holds
- [x] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` are clean
