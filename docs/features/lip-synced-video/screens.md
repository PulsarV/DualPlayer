---
status: draft
feature_size: "L"
tool: "code"
updated_at: "2026-11-20"
---

# Screens — lip-synced-video

> The canonical **screen manifest** — every screen in every state — produced by `screens` (between
> `api` and `tasks`) and read by `tasks` (each `ui` task cites SCR ids + states), `implement`
> (builds the screen to the declared states) and `review` (the built screen must match this).
> Downstream stages reference **only this manifest** — never the raw Figma / `.pen` file.

## Source

- **Tool:** code — `docs/design-system.md` does not exist yet, so the run used code mode; run `/sdd:design-system` before roadmap step 8.
- **File:** inline wireframes below.
- **Inventory used in place of a design system:** Avalonia Fluent (dark) `Window`, and the tokens in `src/DualPlayer.App/Themes/Tokens.axaml` (`FontSizeBody` 32, `FontSizeHeading` 48, `FontSizeMinimum` 24, `SurfaceColor` #000000, `ForegroundColor` #FFFFFF, contrast ≥ 7:1). The terminal (SCR-01) is plain text, specified line by line in `contracts/cli.md`.
- **Inputs:** `ux-flows.md` (SCR inventory, flows), `spec.md` §5 (clarified 2026-11-20), `sad.md` §6 flows 1–9 and ADR-0003 (picture = mpv's own video output in a native child window; brief note = mpv OSD styled from tokens), `contracts/cli.md`.
- **Known divergence:** `ux-flows.md` flow US-02 still says the picture freezes "on the frame shown at the key press"; this manifest follows the clarified AC-04 (freezes on the frame matching the last sound heard). `ux-flows.md` needs that one-line update.

## Screens

### SCR-01 — Developer terminal

Text only; every line's exact format is in `contracts/cli.md` (section in brackets).

| State | Trigger / condition | Components (from the inventory) | Source-ref |
|---|---|---|---|
| default — started | Valid start command; window opened (AC-01, AC-12, AC-13, flow 1) | text lines: one `output …` line per output, `picture …`, `started …` [cli §1.1] | wireframe below |
| loading | N/A: nothing prints between the command and the start lines; the start budget is ≤ 3 s and the window shows the opening state (SCR-02) | — | — |
| empty | N/A: a start command always names a file and ≥ 1 output; usage errors cover a missing one | — | — |
| validation | Malformed arguments, more than three outputs (`cli.usage`, `outputs.too_many`) | `error <code>: <message>` on standard error, exit 2 [cli §5] | wireframe below |
| error | File access denied (AC-03, exit 4), unreadable, no picture or not hardware-decodable (AC-02), track or device not found, device twice, device access denied, screen not found with the screen list (AC-03b), renderer missing — nothing plays, no window [cli §4, §5] | `error <code>: <message>`, exit 2 or 4 | wireframe below |
| warning | Remembered offsets unreadable / invalid / too new, or a value clamped (spec §6.1) — the film still starts | `warning <code>: <message>` before the start lines [cli §1.1] | wireframe below |
| running | During the film: files skipped, next file, device lost, picture failed (AC-07…AC-09b, AC-15, AC-15b, AC-06b) | `skipped`, `next`, `no-next-file`, `device-lost`, `picture-failed` lines [cli §2.2, §2.3] | wireframe below |
| success | Natural end, exit or file change — run summary per output (AC-06, AC-07, AC-15b) | `run-summary …` + step-3 summary block; exit 0 after the window closes [cli §2.1, §4] | wireframe below |

```text
SCR-01 default — started
$ DualPlayer.App play Episode-01.mkv --out 1=1 --out 2=2
output 1  track 1  device Test Headset A  type bluetooth-headphones  reported 0  default 200  offset +40  assumed 160  lag 40
output 2  track 2  device Test Headset B  type bluetooth-headphones  reported 0  default 200  offset 0    assumed 200  lag 0
picture   delay 200  screen 1  1920x1080
started   clock 00:00:00.000  first-frame 1.9 s  first-audio 1.9 s 1.9 s
```

```text
SCR-01 error — screen not found (AC-03b)
$ DualPlayer.App play Episode-01.mkv --out 1=1 --out 2=2 --screen 3
error start.screen_not_found: screen 3 was not found. Connected screens: 1 (1920x1080, main), 2 (3840x2160)
$ echo $?   -> 2
```

```text
SCR-01 warning + running + success
warning state.unreadable: remembered offsets could not be read, every output starts at offset 0
output 1  ...
started   clock 00:00:00.000  first-frame 2.1 s  first-audio 2.0 s 2.1 s
device-lost  output 2  device {…0002}  clock 00:40:12.300  others continue  picture-delay 200 -> 0
skipped  Episode-03.mkv  reason track-missing  output 2  track 2
next     Episode-04.mkv
run-summary  file Episode-01.mkv  reason file-change
<step-3 summary block per output>
```

### SCR-02 — Playback window

One fullscreen Avalonia window on the chosen screen (main screen by default); no title bar, no cursor, no controls. The whole client area is the video child window. Keys are handled by the window (spec §5 key table); there is no focus model.

| State | Trigger / condition | Components (from the inventory) | Source-ref |
|---|---|---|---|
| loading — opening | Window opened after validation, until the first frame is presented (≤ 3 s, flow 1) | `Window` (fullscreen, `SurfaceBrush` black), NEW: `VideoHost` | wireframe below |
| default — playing | Film playing, lip-synced (AC-01); also during a seek re-alignment (AC-05) and a picture-delay move, which look identical — no visible indicator (AC-14) | `Window`, `VideoHost` | wireframe below |
| paused | Pause key: picture runs on until the sound already in the devices has played out (≤ picture delay), then holds the frame matching the last sound heard (AC-04); a seek while paused shows the new frame (AC-05) | `Window`, `VideoHost` | wireframe below |
| ended | Natural end (last frame), seek past the end, last output lost (AC-15b, picture frozen), or picture could not be decoded further (AC-06b, last good frame); pause key ignored; next, previous, backward seek and exit still work | `Window`, `VideoHost`, with SCR-03 note when caused by loss or decode failure | wireframe below |
| closed | Exit key from any state — window closes, terminal shows the run summary (AC-06) | — | — |
| error | N/A: start errors never open the window (AC-02, AC-03, AC-03b → SCR-01); errors during play show as SCR-03 notes and the `ended` state | — | — |
| empty | N/A: the window only exists while a file is open | — | — |

```text
SCR-02 loading — opening            SCR-02 default — playing
+------------------------------+    +------------------------------+
|                              |    |                              |
|        (black, no UI)        |    |      full-frame video        |
|                              |    |      (mpv output, no UI)     |
+------------------------------+    +------------------------------+

SCR-02 paused                        SCR-02 ended
+------------------------------+    +------------------------------+
|                              |    |                              |
|   video frozen on the frame  |    |   video held on last frame   |
|   matching the last sound    |    |   (or last good frame)       |
|   (no pause icon - step 8)   |    |   [SCR-03 note if loss/fail] |
+------------------------------+    +------------------------------+
```

### SCR-03 — Brief note

A single line of text over the picture, drawn by mpv's OSD (ADR-0003), styled from tokens: `FontSizeBody` 32 px at 1080p, `ForegroundColor` white on a `SurfaceColor` black backing box at ≥ 7:1 contrast, bottom-centre, 5 % from the bottom edge. Held 2 s after the last change; a new note replaces the current one and restarts the 2 s (spec §5). English text in this step (Ukrainian UI is step 8).

| State | Trigger / condition | Components (from the inventory) | Source-ref |
|---|---|---|---|
| hidden (default) | No note in the last 2 s | — | — |
| offset changed | Offset key within range, any window state (AC-10, flow 8) | NEW: `OsdNote` — `Output 1: +40 ms` | wireframe below |
| offset limit | Offset key past ±500 ms (AC-11, flow 8) | `OsdNote` — `Output 1: offset cannot go beyond ±500 ms` | wireframe below |
| no next / previous | No playable file in that direction (AC-08, flow 3); when every neighbour in that direction was skipped, one combined note (AC-08 + AC-09) | `OsdNote` — `No next file` / `No previous file`; combined: `No next file (skipped 2; last: Episode-05.mkv - picture cannot be played on this PC)` | wireframe below |
| skipped file | Neighbour lacks a track, unreadable, no picture, not hardware-decodable (AC-09, AC-09b); several on one press → the last note counts them | `OsdNote` — `Skipped Episode-03.mkv: no track 2 for output 2` / `Skipped 3 files; last: Episode-05.mkv - picture cannot be played on this PC` | wireframe below |
| output lost | A device lost while others remain (AC-15, flow 2) | `OsdNote` — `Output 2 lost` | wireframe below |
| last output lost | The only remaining device lost (AC-15b, flow 6) | `OsdNote` — `Output 1 lost - last output, film ended` | wireframe below |
| picture failed | Picture cannot be decoded further (AC-06b, flow 7) | `OsdNote` — `The picture could not be decoded further - film ended` | wireframe below |
| error / empty / loading | N/A: the note is itself the feedback channel; it has no data to load and no empty content | — | — |

```text
SCR-03 note over SCR-02 (bottom-centre, 2 s)
+--------------------------------------------------+
|                                                  |
|                 full-frame video                 |
|                                                  |
|            +---------------------------+         |
|            |  Output 1: +40 ms         |         |
|            +---------------------------+         |
+--------------------------------------------------+
```

## New components

| Component | Why no existing primitive fits | Registered in design-system |
|---|---|---|
| `VideoHost` | Avalonia has no video control; a `NativeControlHost` subclass hands its native window handle to mpv (ADR-0003) | pending — no `docs/design-system.md` yet |
| `OsdNote` | Avalonia cannot draw over the native video child window (airspace, ADR-0003), so the note is mpv OSD text styled from `Tokens.axaml`; no Avalonia primitive can be used there | pending — no `docs/design-system.md` yet |
