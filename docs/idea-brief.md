---
status: Draft
owner: "Volodymyr Kravchuk"
updated_at: "2026-11-11"
depth: "medium"
---

# Idea brief — dual-audio-player

## 1. Raw idea

> Translated from the owner's original Ukrainian text at the owner's request (2026-11-07).
> The verbatim original is preserved in git history (commit `d78154f`).

Here is the initial, unrefined spec:

Spec: a video player with two audio outputs for Windows 11

Draft, version 0.1, 07.11.2026. Every requirement has an ID so it can be referenced during clarification. Priorities: M — mandatory in the first version, S — desirable, C — can come later.

1. Goal and scenario

A minimalist video player for Windows 11 that is comfortable to control from a couch 2–4 m from the screen. Two people watch the same video. Each listens to their own audio track in their own Bluetooth headphones: JBL Tune 520BT and Marshall Motif II ANC. The player can be controlled from a wireless keyboard, a remote, or a phone.

Constraints
- Target system — Windows 11 x64. Development happens on macOS, so the code must be cross-platform.
- Formats: MKV and MP4 mandatory, preferably everything FFmpeg can open.
- No extra hardware. Exception — a second USB Bluetooth adapter if the built-in one cannot handle two pairs of headphones.

Glossary
- Audio output — a player channel: audio track + device + volume + delay.
- Device — a Windows audio device, i.e. a WASAPI endpoint (e.g. "JBL Tune 520BT").
- Master instance — the mpv instance that shows the video and sets the playback time.
- Slave instance — an mpv instance without video. It plays the second track and follows the master.

2. Review of similar products and design conclusions

| Product | What to take | What to avoid |
|---|---|---|
| Light Alloy (Windows) | Minimalism: video takes almost the whole screen, the bottom bar auto-hides, playlist as a side panel, frame thumbnails on the seek bar | Small mouse-oriented elements; overloaded settings menu |
| IINA (macOS, mpv-based) | Modern translucent bar over the video, neat track and subtitle menus | Designed for use close to the screen, not from a distance |
| uosc (open-source mpv UI) | Minimalist seek bar, large track and subtitle menus, command menu | Thin timeline that is hard to see from 3 m |
| Infuse / Apple TV | Benchmark for distance viewing: large fonts, clear focus highlight, simple arrow navigation, info bar at the top | Focus on a cover-art library, unnecessary for this project |
| Kodi (Estuary skin), Jellyfin, Plex | Remote-friendly UI, track and subtitle selection screen, subtitle settings | Heavy media-center UI, requires library setup |
| PotPlayer | Flexible subtitle settings, two subtitles at once | Small and cluttered UI |

Conclusion. No existing design fits as a whole. Ideas can be borrowed, but graphics and themes of other players must not be copied due to licenses and trademarks. Proposed combination:
- Light Alloy minimalism: only the video and an auto-hiding bar;
- Apple TV / Infuse approach to large fonts and focus highlight;
- uosc / IINA-style track and subtitle menus.

Size references:
- Microsoft recommends for TV UI at least 15 epx for body text, 12 epx for secondary text and 32 epx for control height (Designing for Xbox and TV (https://learn.microsoft.com/pl-pl/windows/apps/design/devices/designing-for-tv)).
- The Amazon Fire TV guide names a minimum of 28 px on a 1080p screen (Fire TV UX guidelines (https://developer.amazon.com/docs/fire-tv/design-and-user-experience-guidelines.html)).
- Hence the spec's base values for 1080p: body text ≥ 32 px, headings ≥ 48 px, nothing smaller than 24 px.

3. Playback and audio outputs

| ID | Requirement | Pr. |
|---|---|---|
| PLY-01 | Play MKV/MP4 (H.264, H.265/HEVC, AV1, VP9; AAC, AC3, E-AC3, DTS, Opus, FLAC) with hardware decoding (hwdec=auto-safe) | M |
| PLY-02 | Play, pause, seek ±10 s / ±60 s, go to next and previous file | M |
| PLY-03 | Resume playback from where it stopped, per file | M |
| PLY-04 | Fullscreen by default, monitor selection | M |
| PLY-05 | Playback speed 0.5–2.0× (both outputs together) | C |
| AUD-01 | At least 2 audio outputs. Each with its own track, device, volume and delay | M |
| AUD-02 | Device list comes from Windows (WASAPI), updates when headphones connect/disconnect and shows friendly names | M |
| AUD-03 | Track — internal from the file or an external audio file (.mka, .ac3, .aac, .flac) next to the video | M / S |
| AUD-04 | Separate volume 0–150 % and mute button per output | M |
| AUD-05 | Separate delay per output, from −500 to +500 ms, 10 ms step. The value is remembered per device | M |
| AUD-06 | Auto-select track by language per output (e.g. output 1 — ukr,uk, output 2 — eng,en) | M |
| AUD-07 | Pause, resume, seek and switching files apply to all outputs at once | M |
| AUD-08 | Divergence between outputs in steady state ≤ 20 ms. After a seek, sync recovers within ≤ 1 s | M |
| AUD-09 | If output 2's device disappears, playback continues on output 1 and a notification appears. When the headphones return, the output reconnects automatically | M |
| AUD-10 | Downmix multichannel audio to stereo (5.1/7.1 → 2.0) with dialogue loudness normalization | M |
| AUD-11 | Delay calibration wizard: test video with a click and a flash, adjust delay with ± buttons | S |
| AUD-12 | "Both outputs — same track" mode (for when there is no translation) | M |
| AUD-13 | More than 2 outputs (third pair of headphones, speakers) | C |

How sync works (architecture A, primary)
1. Both libmpv instances run in one process and open the same file.
2. The master instance shows the video and plays output 1's track. The slave runs without video (vid=no) and plays output 2's track.
3. Every command (pause, seek, file change) is sent to both. Seeking is precise (hr-seek).
4. Every ~100 ms the program compares the playback positions of the two instances, accounting for user-set delays:
   - divergence under 20 ms — do nothing;
   - 20 to 150 ms — smoothly change the slave's speed by ±0.5–3 % (inaudible);
   - over 150 ms — seek the slave to the required position.

Risks
- Bluetooth adds 150–300 ms latency, and each model has its own. Manual delay (AUD-05) and the calibration wizard (AUD-11) compensate for it.
- The built-in adapter may not handle two A2DP streams. Then a second USB Bluetooth adapter is needed.
- The "Headset" (Hands-Free) mode in Windows sharply degrades audio quality. The app must warn if a device is in this mode.
- mpv scripts controlling separate processes achieve only ~100 ms precision (mpv #13989 (https://github.com/mpv-player/mpv/issues/13989)). That is why both libmpv instances run in one process.

Fallback (architecture B). One FFmpeg decoder and a custom WASAPI audio pipeline with a shared clock for both devices. More precise, but 3–4× more work. Switch to it only if architecture A cannot meet AUD-08.

4. Subtitles

| ID | Requirement | Pr. |
|---|---|---|
| SUB-01 | Embedded subtitles: SRT, ASS/SSA, WebVTT, PGS, VobSub | M |
| SUB-02 | External subtitles: .srt, .ass, .ssa, .vtt, .sub/.idx, .sup. Found automatically by file name (incl. film.uk.srt, film.eng.srt) and in Subs/ and Subtitles/ subfolders | M |
| SUB-03 | Manually open a subtitle file via a large file dialog | M |
| SUB-04 | Encoding auto-detection (UTF-8, Windows-1251, KOI8-U) and manual encoding choice | M |
| SUB-05 | Auto-select subtitles by language, or "off by default" | M |
| SUB-06 | Two subtitles at once: primary at the bottom, secondary at the top (via mpv secondary-sid). Useful when two people watch in different languages | S |
| SUB-07 | Settings: size (5 presets + exact value), font, text color, outline and its thickness, shadow, translucent background box, bottom margin, line spacing | M |
| SUB-08 | Subtitle delay ±100 ms / ±1 s, remembered per file | M |
| SUB-09 | "Apply my styles to ASS" toggle (overrides original ASS styles) | S |
| SUB-10 | Live style preview over the video right in the settings menu | M |
| SUB-11 | Default subtitle size ~5.5 % of frame height (≈ 60 px at 1080p), white text with a 3 px black outline | M |
| SUB-12 | Online subtitle search (OpenSubtitles API) | C |

5. Playlist

| ID | Requirement | Pr. |
|---|---|---|
| PL-01 | Open a file, several files or a folder (recursively, with natural sorting: "Episode 2" before "Episode 10") | M |
| PL-02 | Drag and drop files and folders into the window | M |
| PL-03 | Playlist — side panel ~35 % of screen width, items ≥ 72 px tall, title ≥ 32 px, duration and watch progress | M |
| PL-04 | Auto-advance to the next file, repeat file or list | M |
| PL-05 | Save the last playlist, position and selected tracks per file | M |
| PL-06 | Frame thumbnails in playlist items | S |
| PL-07 | Save and open playlists (.m3u8) | C |
| PL-08 | "Recent" page on the start screen | S |

6. UI for distance viewing

Principles
1. During playback only the video is on screen. The control bar appears on any key or mouse movement and hides after 4 s.
2. Everything works with arrows, Enter and Esc/Back. A mouse is optional.
3. The focused element is clearly visible from 3 m: 8 % scale-up, bright 4 px border, highlighted background.
4. Dark theme, text contrast ≥ 7:1, no small grey text.
5. The UI scales with window height: sizes are defined for 1080p and scale proportionally for 720p / 1440p / 4K.

Typography (base sizes for 1080p)

| Element | Size |
|---|---|
| File name in the bar | 48 px, semibold |
| Body text, menu items | 32–36 px |
| Secondary text (duration, track language) | 26–28 px |
| Minimum anywhere | 24 px |
| Time on the seek bar | 40 px, monospaced digits |
| Buttons | ≥ 96×96 px, icon 56 px |
| Seek bar | 12 px, 20 px while navigating |

Font — Inter or Segoe UI Variable with full Cyrillic.

Screens
1. Start screen: recent files as large cards, "Open file" and "Open folder" buttons.
2. Playback, bar hidden: only video and subtitles.
3. Playback, bar shown:
   - top: file name and clock;
   - bottom: seek bar with current and total time, buttons (previous, −10 s, pause, +10 s, next), tiles for both audio outputs, subtitle and playlist buttons.
4. Audio output tiles: each shows a headphones icon, device name, track language and volume bar.
   - Example: "🎧 JBL · Ukrainian · 70 %".
   - Enter on a tile opens the output menu: track, device, volume, delay.
5. Subtitle menu: track selection, load file, style, delay.
6. Playlist: side panel on the right.
7. Settings: audio outputs and language priority, subtitles, UI, keys.
8. Notifications (e.g. "Marshall disconnected"): large banner at the top, disappears after 5 s.

| ID | Requirement | Pr. |
|---|---|---|
| UI-01 | All described screens with mouse-free navigation | M |
| UI-02 | UI scale 75–150 % in settings | M |
| UI-03 | UI language Ukrainian, then English | M / S |
| UI-04 | Frame preview above the seek bar while seeking | S |
| UI-05 | On pause, show a large pause icon and the file name | M |

7. Controls

| ID | Requirement | Pr. |
|---|---|---|
| CTL-01 | Keyboard: Space/Enter — pause, ←/→ — ±10 s, Shift+←/→ — ±60 s, ↑/↓ — navigation or volume, PgUp/PgDn — previous/next file, S — subtitles, A — audio, P — playlist, F — fullscreen, Esc — back | M |
| CTL-02 | Media keys and volume buttons on remotes and keyboards | M |
| CTL-03 | Separate volume hotkeys for output 1 and output 2 (e.g. 1/Q and 2/W) | M |
| CTL-04 | Key remapping in settings | S |
| CTL-05 | Phone remote: a web page on the home network (QR code on screen) with pause, seek, two volume sliders and file selection. PIN protection | S |
| CTL-06 | Gamepad support (XInput) | C |
| CTL-07 | Headphone buttons (pause, volume) control the player | C |

8. Architecture and stack

- Language and UI: C# / .NET 8 + Avalonia 11 (cross-platform: develop on macOS, build for Windows).
- Playback: libmpv via the C API, two instances in one process. Video is rendered into an Avalonia window.
- Modules:
  a. PlaybackEngine — manages the two mpv instances;
  b. SyncController — output synchronization;
  c. AudioDeviceService — device list and connect/disconnect handling;
  d. SubtitleService — subtitle discovery and settings;
  e. PlaylistService — playlist and saved positions;
  f. SettingsStore — settings in a JSON file in %APPDATA%;
  g. RemoteServer (S) — embedded web server for the phone remote.
- Extensibility: the number of outputs is a list, not hard-coded to two. UI and playback engine are separated, so the engine can be replaced (architecture B).
- Delivery: a single .exe (self-contained, win-x64) + libmpv-2.dll. An installer (MSIX or Inno Setup) is desirable.
- Licenses: libmpv — LGPL (dynamically linked), Avalonia — MIT.

9. Non-functional requirements, testing, stages

Non-functional
- From opening a file to the first frame ≤ 2 s.
- CPU load while playing 1080p HEVC with hardware decoding ≤ 15 %.
- 3+ hours of playback without desync over 20 ms.
- After a crash, playback resumes from the last position.
- Event log for sync diagnostics.

Testing
- Sync is verified with test files containing clicks and flashes in both tracks, recorded by a microphone from both headphones at once.
- Check Bluetooth stability: both pairs of headphones for 2 hours.
- Check distance usability: read all UI text from 3 m on a 43–55″ TV.

Stages
1. Sync validation (1–2 weeks): a console prototype with two tracks on two devices. Measure divergence and decide whether to stay on architecture A.
2. MVP: playback, audio outputs, basic UI, playlist (all M requirements except subtitles).
3. Subtitles: SUB-* requirements.
4. Polishing the distance-viewing UI: settings, calibration wizard.
5. Phone remote and extensions: S and C requirements.

Open questions
1. What is the screen resolution and diagonal, and from what distance do you watch?
2. Is a separate "audio to speakers" mode without headphones needed?
3. Is there a wireless keyboard or remote? If so, which model?
4. Is a UI language other than Ukrainian needed?
5. Does your PC support two Bluetooth audio devices at once? Worth checking before stage 1 starts.

## 2. Problem

- **Desired outcome (owner-reported):** two people watch the same video on a TV, each hearing their own
  audio track (e.g. Ukrainian and English) in their own Bluetooth headphones, in sync with the picture,
  with comfortable control from the couch without a mouse.
- **Portability goal (owner-reported, 2026-11-11):** the player must work equally well on any computer
  that meets the minimum requirements, without the user having to run a series of calibration tests first.
- **Observed problem:** not established. The scenario has not been tried in any form
  (owner-reported, 2026-11-07): today they watch with a single audio output and have never run two outputs
  at once. Desync, awkward controls and Bluetooth limits are therefore expected, not observed, problems.
- **Proposed solution (not the problem):** a custom player on C#/.NET 8 + Avalonia + two libmpv instances
  (architecture A) — the solution from the spec, not yet confirmed by measurements.

## 3. Users

- **Primary users (owner-reported):** two people at home watching one video from a couch 2–4 m from the
  screen. Each listens to their own audio track in their own Bluetooth headphones (JBL Tune 520BT, Marshall Motif II ANC).
- **Audience scope (owner-reported, decision 2026-11-07):** first a tool for the owner's own scenario,
  later published for other people with a similar scenario. Broader segments are not defined yet.
- **Developer/operator (owner-reported):** the owner; development on macOS, target machine Windows 11 x64.

## 4. Why now

No specific trigger or deadline: a personal project without a deadline (owner-reported, 2026-11-07).

## 5. Out of scope

- **Exclusions from the spec (owner-reported):** a cover-art library / Kodi/Plex-style media center;
  copying graphics and themes of other players; extra hardware beyond a second USB Bluetooth adapter if needed.
- **Constraints to preserve (owner-required):** Windows 11 x64 as the target; development on macOS;
  MKV and MP4 mandatory; LGPL-compatible dynamic linking of libmpv.
- **Not agreed:** C-priority requirements (OpenSubtitles, gamepad, >2 outputs, etc.) are marked
  "can come later", but there is no explicit "won't do" decision for them.

## 6. Risks

1. **Weakest assumption: the target PC can drive two pairs of BT headphones in stereo (A2DP) simultaneously.**
   Not verified (owner-reported, 2026-11-07). If not, a second USB adapter is needed or the scenario is
   impossible without other hardware. The check is cheap and fully reversible.
2. **Achievability of AUD-08 (≤ 20 ms between outputs) with architecture A** — assumption. Nobody has
   measured the drift of two BT devices or the difference in their latencies. If A cannot meet it, B is
   estimated at 3–4× the cost (estimate from the spec, assumption).
3. **Whether 20 ms precision is needed at all** — unknown. Two people in separate headphones do not hear
   each other; what matters is each output's sync with the video (lip-sync), not the outputs with each other.
   If so, AUD-08 could be substantially relaxed and a simpler approach could be acceptable.
4. **Scope:** ~45 M-priority requirements for a home tool before the basic scenario is validated; risk of
   spending weeks on UI before confirming that the audio setup works at all.

5. **Zero-calibration is only partly achievable** — assumption. Windows reports buffer/driver latency,
   but not the Bluetooth codec latency of a given headset model. Without a measurement, the exact
   per-model latency stays unknown; the realistic goal is "calibration optional", not "never needed".
6. **Cost of the portability direction** — assumption. It makes architecture B (estimated 3–4× the cost
   of A in the spec) the primary engine and adds new requirements (auto latency compensation,
   self-diagnostics, minimum requirements check).

Experiment results (owner-reported, 2026-11-08): two independent mpv processes on the target PC, JBL and
Marshall both as WASAPI "Headphones" endpoints (stereo, 48000 Hz, no Hands-Free endpoint listed).
No noticeable desync. Crackling, like a vinyl record, was heard on quiet passages; cause not yet localized.

Evidence ledger:

| Claim / question | Class | Source / origin | Finding / limit |
|---|---|---|---|
| Scenario not tried yet | owner-reported | owner answer, 2026-11-07 | — |
| Two BT audio devices at once on the target PC | unknown | owner answer, 2026-11-07 | not verified |
| Syncing separate mpv processes via IPC is limited to ~100 ms | source-verified (claim exists) | https://github.com/mpv-player/mpv/issues/13989, inspected 2026-11-07 | Open feature request; the author states "100ms resolution of ipc calls" about the audio-dupe script. Author's opinion, not a measurement; does not apply to two libmpv instances in one process |
| Bluetooth adds 150–300 ms latency | assumption | spec §3 "Risks" | no source given; not measured for these models |
| Architecture B costs 3–4× more | assumption | spec §3 | owner estimate without breakdown |

Critique done inline (no separate devils-advocate): the change does not touch access, data or public
contracts and is fully reversible.

## 7. Recommendation

- **Direction (owner-accepted, 2026-11-07):** before spec and code — a cheap one-evening experiment on the
  target PC. Connect JBL and Marshall simultaneously, run two plain mpv processes
  (`--audio-device` per pair, different `--aid`, `--vid=no` on the second), watch for 30–60 min.
  Record: whether both pairs are in stereo (not Hands-Free), whether there are dropouts, whether desync of
  each output with the video and between outputs is noticeable, whether manual delay is needed.
- **Observable result of the experiment:** a yes/no answer on two BT streams and a qualitative desync
  assessment, based on which AUD-08 is confirmed, relaxed or revised.
- **Main tradeoff:** a day or two of delay before development starts versus the risk of building a large
  UI on top of an unvalidated hardware scenario.
- **Rejected alternatives:** go straight to roadmap with validation as the first step (rejected by the owner).
  The simpler "two mpv + script" option is dismissed by the spec due to ~100 ms precision; the experiment
  will show whether that is really insufficient for two people in separate headphones.
- **Architecture direction change (owner-accepted, 2026-11-11)** — driven by the portability goal in §2:
  1. One engine and one master clock instead of two independent mpv instances: decode once, fan audio
     out to N WASAPI endpoints, correct each device's drift automatically by resampling against its
     reported position (`IAudioClock`). Architecture B becomes primary; libmpv may still render video.
  2. Delay the video by the largest output latency and pad faster outputs, so each pair is lip-synced
     without manual tuning; manual offset remains as fine-tuning only.
  3. Automatic latency discovery: latency Windows reports + a built-in per-model/codec latency table,
     with a sensible default for unknown models.
  4. Generous buffers (free once video is delayed) and an event-driven WASAPI thread with MMCSS
     "Pro Audio" priority, to prevent underruns (a candidate cause of the observed crackling).
  5. Self-diagnostics instead of user tests: Hands-Free detection, number of BT streams per adapter,
     underrun and drift counters, with concrete hints ("add a second adapter", "use 5 GHz Wi-Fi") and logging.
  6. Explicit minimum requirements (Windows version, BT adapter version, CPU/GPU with HW decoding)
     checked at startup, with automatic fallback to software decoding.
  7. Sync verification on the developer side: an automated click/flash test recorded via microphone or
     loopback on several test machines, so users get pre-validated behavior.
  Impact on the spec: AUD-05/AUD-11 become optional fine-tuning; new requirements for auto latency
  compensation, self-diagnostics and minimum requirements; larger scope. The direction is accepted; the
  concrete design still has to be recorded as an ADR in `/sdd:design`.
- **Anti-crackling player settings (owner-proposed, 2026-11-11; exact set to be shaped in specify):**
  - audio buffer size: `Auto` (adaptive) by default, manual value in ms as override;
  - automatic buffer growth when underruns are detected (ties into self-diagnostics, item 5 above);
  - WASAPI mode: shared (default) / exclusive;
  - output sample rate: match the device's native rate (e.g. 48 kHz) to avoid extra resampling;
  - resampler quality level;
  - a single "Stability ↔ Low latency" preset that sets the above together;
  - settings remembered per device.
  Limit: these settings only help if the cause is in software (underruns, resampling). If the crackling
  comes from adapter bandwidth or radio interference, no setting fixes it; self-diagnostics should then
  show a hint (second BT adapter, 5 GHz Wi-Fi). The cause is not established yet (§8).
- **Resolved by the experiment and the accepted direction (2026-11-11):** the target PC drives both pairs
  in stereo at once (owner-reported); no noticeable desync between two independent mpv processes
  (owner-reported); the sync requirement is each output lip-synced with the video under one master
  clock (accepted direction, item 1–2).
- **Crackling checks made nonblocking (owner decision, 2026-11-11):** they become the first roadmap step.
- **Outcome: ready-for-roadmap.**
- **Next action:** `/sdd:roadmap` — the first step localizes the crackling (§8) and fixes the minimum
  hardware requirements; the accepted architecture is then designed and recorded as an ADR in `/sdd:design`.

## 8. Open questions

| Question | Consequence | Blocking? | Verification | Owner |
|---|---|---|---|---|
| TV resolution, diagonal and viewing distance (spec, question 1) | Affects UI sizes | nonblocking | measure at home | Volodymyr Kravchuk |
| Wireless keyboard/remote model (spec, question 3) | Affects CTL-02 | nonblocking | check existing hardware | Volodymyr Kravchuk |
| Speaker-only mode and other UI languages (spec, questions 2, 4) | MVP scope | nonblocking | decide during roadmap | Volodymyr Kravchuk |
| Is the crackling present in the source track itself? | Rules out / in a Bluetooth cause | nonblocking (first roadmap step) | play the same quiet passage on the Realtek output | Volodymyr Kravchuk |
| Does each pair crackle when it plays alone? | Alone clean + together crackling → built-in adapter bandwidth → second USB BT adapter | nonblocking (first roadmap step) | play JBL alone, then Marshall alone | Volodymyr Kravchuk |
| Does a larger audio buffer remove the crackling? | Yes → buffer underruns, fixable in software | nonblocking (first roadmap step) | add `--audio-buffer=0.5` to both mpv commands | Volodymyr Kravchuk |
| Is it 2.4 GHz Wi-Fi interference or distance? | Environment issue, not the player | nonblocking | switch PC to 5 GHz / cable; compare near vs far from the PC | Volodymyr Kravchuk |
| Which headphones crackled: one pair or both? | Narrows down device vs adapter | nonblocking (first roadmap step) | owner recollection / repeat test | Volodymyr Kravchuk |
| What are the minimum requirements (Windows version, BT adapter, CPU/GPU)? | Defines what "works on any computer" means | nonblocking | decide during roadmap/specify | Volodymyr Kravchuk |
| Where do per-model Bluetooth latency defaults come from? | Without data, auto-compensation falls back to a generic default | nonblocking | research measurements / build own table from tests | unassigned |
