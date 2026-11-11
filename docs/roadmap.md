---
status: living
updated_at: "2026-11-11"
---

# Roadmap — MediaPlayer (dual-audio-player)

> **A decomposition, not a promise.** The overall idea broken into incremental steps: what each
> step is, where it comes from, how big it is — or that nobody has looked at it yet — and in which
> order, and parallel lanes, we walk them. **No dates** (except shipped history), **no scores** —
> order is the prioritization. The *solution* for any step lives in its `docs/features/<slug>/`
> spec, not here.

## Destination

Two people watch one video on any Windows 11 PC that meets the minimum requirements, each hearing their own audio track on their own audio device lip-synced with the picture without manual calibration, and control it from the couch without a mouse.

## Steps

| # | Step | Source | Size | Status |
|---|---|---|:---:|---|
| 1 | Crackling localized and minimum hardware requirements fixed (experiment on the target PC, no code) | `idea-brief.md §8 Open questions`; `§7 Recommendation` (crackling checks → first roadmap step) | XS | idea |
| 2 | Project skeleton: .NET 8 + Avalonia solution, test harness, smoke test, CI — builds on macOS, runs on Windows | `idea-brief.md §1 Raw idea` (8. Architecture and stack) | S | idea |
| 3 | Two outputs, one clock (console): one decoder fans two tracks out to N audio endpoints, per-device drift correction against the device clock, underrun/drift log; audio-backend abstraction (WASAPI target, CoreAudio for dev) | `idea-brief.md §7 Recommendation` (architecture direction items 1, 4) | L | idea |
| 4 | Watch a file lip-synced: fullscreen Avalonia window with libmpv video slaved to the audio clock, video delayed by the largest output latency, automatic latency compensation (Windows-reported + device-type defaults) with per-device manual offset; play/pause/seek/next/prev on all outputs | `idea-brief.md §7 Recommendation` (items 1–3, any audio device); `§1 Raw idea` (PLY-01, PLY-02, PLY-04, AUD-05, AUD-07) | L | idea |
| 5 | Audio outputs: per-output track/device/volume/mute, language auto-select, same-track mode, downmix to stereo; device list with friendly names and type, hotplug, continue on device loss, auto-reconnect | `idea-brief.md §1 Raw idea` (AUD-01–04, AUD-06, AUD-09, AUD-10, AUD-12); `§7 Recommendation` (any audio device) | L | idea |
| 6 | Self-diagnostics and minimum-requirements check: Hands-Free detection, BT streams per adapter, underrun/drift counters with hints, startup requirements check with software-decoding fallback | `idea-brief.md §7 Recommendation` (items 5, 6) | M | idea |
| 7 | Anti-crackling settings → see [Not yet specified](#not-yet-specified) | `idea-brief.md §7 Recommendation` (anti-crackling player settings) | fog | idea |
| 8 | Couch UI shell: auto-hiding bar over video, output tiles and output menu, notifications, pause screen, settings screen, focus navigation, UI scale, Ukrainian UI | `idea-brief.md §1 Raw idea` (6. UI for distance viewing: UI-01, UI-02, UI-03, UI-05) | L | idea |
| 9 | Controls: keyboard map, media/remote keys, per-output volume hotkeys | `idea-brief.md §1 Raw idea` (7. Controls: CTL-01–03) | S | idea |
| 10 | Playlist and resume: open files/folder, drag-and-drop, natural sort, side panel, auto-advance/repeat, per-file position and tracks, crash resume | `idea-brief.md §1 Raw idea` (5. Playlist: PL-01–05; PLY-03) | M | idea |
| 11 | Subtitles: embedded and external discovery, encoding detection, language auto-select, style settings with live preview, delay | `idea-brief.md §1 Raw idea` (4. Subtitles: SUB-01–05, SUB-07, SUB-08, SUB-10, SUB-11) | L | idea |
| 12 | Developer sync-verification rig: automated click/flash test recorded via microphone or loopback on several machines | `idea-brief.md §7 Recommendation` (item 7) | M | idea |
| 13 | Packaging: self-contained win-x64 build + libmpv, installer | `idea-brief.md §1 Raw idea` (8. Architecture and stack — Delivery) | S | idea |
| 14 | Later extras (S/C requirements incl. phone remote) → see [Not yet specified](#not-yet-specified) | `idea-brief.md §1 Raw idea` (CTL-05, S/C rows); `§5 Out of scope` (Not agreed) | fog | idea |

## Not yet specified

| Area | What we'd have to learn | Blocks | How it gets sharpened |
|---|---|:---:|---|
| Anti-crackling settings | Where the crackling comes from (source track / adapter bandwidth / buffer underruns / interference) — only a software cause makes these settings worth building, and the cause picks which ones | 7 | Step 1 result, then `/sdd:specify` |
| Later extras | Which S/C requirements the owner actually keeps (phone remote, dual subtitles, thumbnails, seek preview, speed, key remap, English UI, .m3u8, Recent page, OpenSubtitles, gamepad, headphone buttons, calibration wizard, >2 outputs in UI, speaker-only mode) | 14 | D5 conversation with the owner |

## Out of scope

- Media-center / cover-art library (Kodi/Plex style) — excluded in the brief.
- Copying graphics and themes of other players — licenses and trademarks.
- Extra hardware beyond a second USB Bluetooth adapter — owner constraint.
- Mandatory user calibration before first use — contradicts the portability goal; manual offset stays only as fine-tuning.

## Open decisions

| # | Question | Type | Owner | Blocks |
|---|---|:---:|:---:|:---:|
| D1 | Is libmpv video slaved to our audio clock via `speed`/`seek` correction precise enough for lip-sync, or is an own FFmpeg video decode/render path needed? (mpv has no external-clock option — [mpv options.rst `--video-sync`](https://github.com/mpv-player/mpv/blob/master/DOCS/man/options.rst)) | prototype | agent | 4 |
| D2 | How to detect Hands-Free (HFP) vs stereo on Windows 11's merged BT endpoint — no documented property; infer from mix-format drop / mic use? ([MS Learn: Bluetooth classic audio](https://learn.microsoft.com/en-us/windows-hardware/drivers/bluetooth/bluetooth-classic-audio)) | prototype | agent | 6 |
| D3 | Can an audio endpoint be mapped to its BT adapter by walking the CfgMgr/SetupAPI parent chain, to count streams per adapter? | prototype | agent | 6 |
| D4 | Where do per-model latency defaults come from — no open dataset exists ([RTINGS latency test](https://www.rtings.com/headphones/tests/connectivity/bluetooth-connection) is not reusable): own table from step 12 measurements, hand-picked per-type defaults, or both? | grilling | human | 4 |
| D5 | Which S/C requirements stay in scope and which become explicit "won't do" (incl. speaker-only mode)? | grilling | human | 14 |
| D6 | Is speaker bleed acceptable for the headphones listener when the other output is speakers? | prototype | human | 5 |

## Decisions so far

- One engine, one master clock, audio fanned out to N endpoints with per-device drift correction (architecture B primary; libmpv may still render video) → [`docs/idea-brief.md §7`](idea-brief.md)
- Any Windows audio endpoint per output (BT, wired, HDMI, USB), aligned by delaying video by the largest latency → [`docs/idea-brief.md §7`](idea-brief.md)
- Sync target is each output lip-synced with the video, not ≤20 ms between outputs → [`docs/idea-brief.md §7`](idea-brief.md)
- Crackling checks are nonblocking and form the first roadmap step → [`docs/idea-brief.md §7`](idea-brief.md)
- Engine is built on existing libs (NAudio WASAPI + FFmpeg.AutoGen) with an own N-device render loop and MMCSS "Pro Audio" P/Invoke → [NAudio WasapiOut.cs](https://github.com/naudio/NAudio/blob/master/NAudio.Wasapi/WasapiOut.cs)
- macOS development via an audio-backend abstraction: CoreAudio implementation for dev, WASAPI as the target (owner answer in roadmap review; to be recorded as an ADR in `/sdd:design`) → this file

## Dependency graph

```mermaid
flowchart LR
  s1["1 · crackling + min reqs"]
  s2["2 · skeleton"]
  s3["3 · two outputs, one clock"]
  s4["4 · watch lip-synced"]
  s5["5 · audio outputs"]
  s6["6 · self-diagnostics"]
  s7["7 · anti-crackling (fog)"]
  s8["8 · couch UI shell"]
  s9["9 · controls"]
  s10["10 · playlist + resume"]
  s11["11 · subtitles"]
  s12["12 · sync rig"]
  s13["13 · packaging"]
  s14["14 · later extras (fog)"]

  s2 -->|solution, test harness| s3
  s3 -->|audio master clock video slaves to| s4
  s3 -->|output model lives in the engine| s5
  s3 -->|underrun/drift counters come from the engine| s6
  s1 -->|minimum requirements defined by the experiment| s6
  s1 -->|crackling cause decides which settings exist| s7
  s4 -->|UI overlays the playback window| s8
  s5 -->|output tiles show output config| s8
  s4 -->|transport commands to bind| s9
  s5 -->|per-output volume hotkeys need outputs| s9
  s4 -->|open/next file on the transport| s10
  s8 -->|side panel lives in the UI shell focus nav| s10
  s4 -->|subtitles render over the video| s11
  s8 -->|subtitle menu lives in the UI shell| s11
  s4 -->|needs A/V playback to measure| s12
  s4 -->|bundles libmpv used for video| s13
```

## Execution path

Zones are a first cut: there is no `docs/architecture-map.md` yet, so every code zone is `(new)`.

| Wave | Steps | Zone per step (why parallel-safe) | Unlocks |
|:---:|---|---|---|
| 1 | 1 ∥ 2 | 1: `docs/` (experiment notes, target PC, no code) · 2: `src/`, `tests/` (new) | 3, 6, 7 |
| 2 | 3 | 3: `src/Engine/Audio` (new) | 4, 5, 6 |
| 3 | 4 ∥ 5 ∥ 6 | 4: `src/Engine/Video` + `src/App` window (new) · 5: `src/Engine/Outputs` (new) · 6: `src/Diagnostics` (new) — disjoint modules | 8, 9, 10, 11, 12, 13 |
| 4 | 8 ∥ 9 ∥ 12 ∥ 13 | 8: `src/App/UI` (new) · 9: `src/App/Input` (new) · 12: `tools/sync-rig` (new) · 13: `build/installer` (new) — disjoint | 10, 11 |
| 5 | 10 ∥ 11 | 10: `src/Playlist` + its side panel (new) · 11: `src/Subtitles` + its menu (new) — separate modules and UI zones | — |

## Shipped

| Step | Shipped | Link |
|---|---|---|
| — | — | — |
