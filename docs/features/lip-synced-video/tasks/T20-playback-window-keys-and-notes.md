---
id: T20
title: "Build the fullscreen playback window hosting mpv's video, mapping the fixed keys and styling notes from tokens"
layer: "ui"
deps: ["T4", "T15", "T17"]
blocks: ["T22"]
acs: ["AC-01", "AC-04", "AC-05", "AC-06", "AC-07", "AC-10", "AC-11"]
files_hint: ["src/DualPlayer.App/Views/PlaybackWindow.axaml", "src/DualPlayer.App/Views/PlaybackWindow.axaml.cs", "src/DualPlayer.App/ViewModels/PlaybackViewModel.cs", "src/DualPlayer.App/Controls/VideoHost.cs", "src/DualPlayer.App/Themes/Tokens.axaml", "tests/DualPlayer.App.Tests/ViewModels/PlaybackViewModelTests.cs"]
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

# T20 — Build the fullscreen playback window hosting mpv's video, mapping the fixed keys and styling notes from tokens

## Place in the sequence

- **Blocked by:** T4 — Implement the hardened libmpv video renderer with presented-position snapshots and OSD notes, T15 — Apply offset keys per output and move picture and unchanged outputs to a new picture delay together, T17 — Move to the next or previous playable file in the folder, skipping files that cannot play
- **Blocks:** T22 — Wire the App composition root and host: validate, open the window on the screen, run the session, exit with the right code
- **Wave:** 9 — after its deepest dependency finishes (wave 8).
- **Lane:** own lane.

## Why (user story)

> **As a** Viewer
> **I want** to pause, resume and seek with a few keys on a wireless keyboard
> **So that** the picture and both Viewers' sound stop, start and jump together without anyone getting up
>
> — `spec.md §4, US-02, verbatim` · full text: [spec.md](../spec.md)

This task is the Viewers' whole surface: one black fullscreen window showing the picture, answering a fixed set of keys, with brief notes over the video.

## Inlined context

> An Avalonia fullscreen window (Fluent dark, MVVM with CommunityToolkit.Mvvm, compiled bindings) hosts mpv's own video output in a native child window. Viewer keys are handled by the Avalonia window, and mpv's own input is switched off. The brief note (SCR-03) is drawn by mpv's on-screen display, styled from `Themes/Tokens.axaml` ([ADR-0003]). The window has no navigation, menus or focus model: one view, three states (playing, paused, ended), per `ux-flows.md`.
>
> — `sad.md §4, UI architecture, abridged` · full text: [sad.md](../sad.md)

| Key | Action |
|---|---|
| Space | pause / resume |
| ← / → | seek −10 s / +10 s |
| ↓ / ↑ | seek −60 s / +60 s |
| Page Up / Page Down | previous / next file |
| Esc | exit |
| 1 / Q | output 1: offset +10 ms (later) / −10 ms (earlier) |
| 2 / W | output 2: offset +10 ms / −10 ms |
| 3 / E | output 3: offset +10 ms / −10 ms |

"Shows briefly" in the AC below means an on-screen note held for 2 s after the last change; a new note replaces the current one and restarts the 2 s.

— `spec.md §5, fixed keys + shows briefly, verbatim` · full text: [spec.md](../spec.md)

**SCR-02 — Playback window.** One fullscreen Avalonia window on the chosen screen (main screen by default); no title bar, no cursor, no controls. The whole client area is the video child window. Keys are handled by the window (spec §5 key table); there is no focus model.

| State | Trigger / condition | Components |
|---|---|---|
| loading — opening | Window opened after validation, until the first frame is presented (≤ 3 s, flow 1) | `Window` (fullscreen, `SurfaceBrush` black), NEW: `VideoHost` |
| default — playing | Film playing, lip-synced (AC-01); also during a seek re-alignment (AC-05) and a picture-delay move, which look identical — no visible indicator (AC-14) | `Window`, `VideoHost` |
| paused | Pause key: picture runs on until the sound already in the devices has played out (≤ picture delay), then holds the frame matching the last sound heard (AC-04) | `Window`, `VideoHost` |
| ended | Natural end, seek past the end, last output lost, or picture could not be decoded further; pause key ignored; next, previous, backward seek and exit still work | `Window`, `VideoHost`, with SCR-03 note when caused by loss or decode failure |
| closed | Exit key from any state — window closes, terminal shows the run summary (AC-06) | — |

**SCR-03 — Brief note.** A single line of text over the picture, drawn by mpv's OSD (ADR-0003), styled from tokens: `FontSizeBody` 32 px at 1080p, `ForegroundColor` white on a `SurfaceColor` black backing box at ≥ 7:1 contrast, bottom-centre, 5 % from the bottom edge. Held 2 s after the last change; a new note replaces the current one and restarts the 2 s (spec §5). English text in this step (Ukrainian UI is step 8).

— `screens.md SCR-02 + SCR-03, abridged (source-ref column and wireframes dropped)` · full text: [screens.md](../screens.md)

> | `VideoHost` | Avalonia has no video control; a `NativeControlHost` subclass hands its native window handle to mpv (ADR-0003) | pending — no `docs/design-system.md` yet |
> | `OsdNote` | Avalonia cannot draw over the native video child window (airspace, ADR-0003), so the note is mpv OSD text styled from `Tokens.axaml`; no Avalonia primitive can be used there | pending — no `docs/design-system.md` yet |
>
> — `screens.md §New components, verbatim` · full text: [screens.md](../screens.md)

> **Hard rule:** **UI:** Avalonia Fluent, dark variant; MVVM with CommunityToolkit.Mvvm (`ObservableObject`, `[ObservableProperty]`, `[RelayCommand]`); compiled bindings (`x:DataType` on every view). Sizes and colours come from `Themes/Tokens.axaml` (1080p baselines: body ≥ 32 px, headings ≥ 48 px, min 24 px, buttons ≥ 96×96, focus border 4 px, contrast ≥ 7:1) — no inline styling in views.
>
> — `CLAUDE.md §Conventions, UI, verbatim` · full text: [CLAUDE.md](../../../../CLAUDE.md)

> **Hard rule:** The playback session, picture sync and lag ramps run on the step-3 engine control loop. mpv property and end events arrive on mpv's event thread; the adapter publishes immutable snapshots without locks the control loop could wait on. Render threads are never blocked. UI updates marshal through `Dispatcher.UIThread`. The picture probe runs off the UI thread with a time limit
>
> — `sad.md §8, Threading, verbatim` · full text: [sad.md](../sad.md)

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

## Checklist

- [ ] `VideoHost : NativeControlHost` creating a non-focusable child window and exposing its handle — `src/DualPlayer.App/Controls/VideoHost.cs`
- [ ] `PlaybackWindow` fullscreen on the requested screen, no chrome, cursor hidden, `x:DataType`, colours/sizes from tokens — `src/DualPlayer.App/Views/PlaybackWindow.axaml(.cs)`
- [ ] `PlaybackViewModel`: key table → session commands (pause/resume, seek ±10/±60 s, previous/next, exit, offset ±10 for outputs 1–3); forwards `NoteShown` to `IVideoRenderer.ShowNote` with the token style; UI updates via `Dispatcher.UIThread` — `src/DualPlayer.App/ViewModels/PlaybackViewModel.cs`
- [ ] Add the OSD note tokens (bottom margin 5 %, backing box) if missing — `src/DualPlayer.App/Themes/Tokens.axaml`
- [ ] Tests for the key mapping and note forwarding — `tests/DualPlayer.App.Tests/ViewModels/PlaybackViewModelTests.cs`

## Edge cases

| Case | Behaviour |
|---|---|
| Key for output 3 with two outputs started | ignored |
| Native child window takes focus | must not: non-focusable child, mpv input off (sad §11) |
| Note while another is showing | replaces it, restarts 2 s (spec §5) |
| Pause key while ended | forwarded; the session ignores it (AC-06) |

## Definition of Done

- [x] `PlaybackViewModelTests` pass for every key row
- [x] no inline sizes or colours in `PlaybackWindow.axaml`
- [x] every Hard Rule inlined above still holds
- [x] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` are clean
