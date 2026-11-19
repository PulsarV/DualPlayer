---
status: Draft
owner: "Volodymyr Kravchuk"
reviewers: ["Tech Lead", "Security Lead"]
updated_at: "2026-11-19"
feature_size: "L"
---

# Spec — lip-synced-video

> **Glossary:** [CONTEXT](../../../CONTEXT.md) (repo-root; no feature-level CONTEXT.md)
> **Reference module / docs / channels used:** `docs/idea-brief.md` (§1 PLY-01, PLY-02, PLY-04, AUD-05, AUD-07; §6 risks 5–6; §7 items 1–3 and "Any audio device per output"; §8), `docs/roadmap.md` (step 4, open decisions D1 and D4, adjacent steps 5, 6, 8, 9, 10, 12, 14), `docs/architecture-map.md`, `docs/features/two-outputs-one-clock/` (spec, ADR-0006, ADR-0008, target-PC evidence 2026-11-17 to 2026-11-19); no extra channels.

## 1. Context

Two Viewers watch one video on a TV from a couch 2–4 m away, each hearing their own track on their own device — today a JBL Tune 520BT and a Marshall Motif II ANC, possibly wired or HDMI speakers instead (`idea-brief.md §2`, §7 "Any audio device per output"). Roadmap step 3 delivered the audio half: one engine plays several tracks on several devices under one master clock, keeps each output's drift within 10 ms and was accepted on the target PC (`two-outputs-one-clock/evidence/2026-11-18-target-pc-run.md`). What it does not do is show the picture, and it aligns each output with the master clock rather than with what the Viewer hears: every device adds its own latency, about 150–300 ms for a Bluetooth headset by the brief's assumption (`idea-brief.md §6`, risk 5; not measured for these models). Without compensation the words arrive after the lips move, by a different amount in each headset.

Why now: step 4 is the first step a Viewer can use to actually watch a film, and steps 8, 11 and 12 build on it (`roadmap.md` dependency graph). The open question carried from step 3 — whether the position a Bluetooth device reports matches what the Viewer hears (`two-outputs-one-clock/spec.md §8`) — was due before this step and is taken over here (§8).

Committed approach (owner-accepted direction, `idea-brief.md §7` items 2–3, refined on 2026-11-19): the picture is shown against the audio master clock and held back by the largest latency among the playing outputs, so every output's sound lines up with the lips; faster outputs wait the same amount. Each output's latency is estimated as what the operating system reports for the device plus a default for its device type, and a Viewer can correct it by ear with an offset in 10 ms steps, remembered per device; a positive offset makes that output's sound heard later against the picture. A fact-check on 2026-11-19 (limited search) found that the operating system's documentation describes only its own buffering latency, not a Bluetooth codec's or headset's, and found no reusable per-model latency table — so for Bluetooth the estimate is a starting guess, and the honest promise is "calibration optional, possibly once per new headset", not "never needed" (`idea-brief.md §6`, risk 5; roadmap D4 decided this way). When the picture delay has to change during a film, the picture and the other outputs move to it together at a speed change too small to see or hear, so nobody's lip-sync breaks and nothing jumps. The step ships as a fullscreen window started with a command that names the file, tracks and devices (as in step 3), controlled with a small fixed set of keys from a wireless keyboard, with next and previous file taken from the same folder. It is accepted on the target PC by filming a click-and-flash test video with a phone, per output, so that "lip-synced" is measured, not only asserted by the player's own numbers.

Traceability: requirements PLY-01 (formats, hardware decoding), PLY-02 (play, pause, seek ±10 s and ±60 s, next and previous file), PLY-04 (fullscreen by default, choice of screen), AUD-05 (offset per output, −500 to +500 ms in 10 ms steps, remembered per device), AUD-07 (transport applies to all outputs at once) from `idea-brief.md §1`; latency compensation from `§7` items 2–3. Lip-sync thresholds: ITU-R BT.1359 detectability of about 45 ms sound-early and 125 ms sound-late (confirmed at search-summary level on 2026-11-19; the primary text is still unread, §8).

## 2. Goals

- Each Viewer hears their own track in step with the lips on screen, on any combination of Bluetooth, wired and HDMI devices, measured on the target PC.
- Once the Developer has started a film, a Viewer can pause, seek and move between the files of a folder from the couch, with picture and every output acting together.
- Correcting lip-sync by ear takes a few key presses once per device, not a ritual per film.

## 3. Non-goals

- A couch interface (auto-hiding bar, output tiles, menus, notifications beyond a brief value on screen) — roadmap step 8 owns it; this step needs only keys and a short on-screen note.
- A configurable key map, media keys and remote control — roadmap step 9; this step's keys are fixed.
- A playlist, auto-advance at the end of a file, resume from the last position — roadmap step 10; next and previous here only walk the current folder.
- Choosing devices, tracks by language, volume and mute from a picker — roadmap step 5; devices and tracks are still given at start.
- Measuring a device's latency automatically (microphone loopback, calibration wizard) and a per-model latency table — roadmap steps 12 and 14; this step uses device-type defaults plus the Viewer's offset.
- Detecting the TV's own picture-processing delay — the operating system does not report it; offsets absorb it (§8).
- Software decoding fallback and the minimum-requirements check — roadmap step 6.

## 4. User stories

### US-01: Watch a film lip-synced

**As a** Viewer
**I want** to watch a film the Developer started for us in fullscreen and hear my own track in my own device in step with the lips on screen
**So that** I can watch the same film as the other Viewer in my own language without being distracted by sound that runs ahead of or behind the picture

### US-02: Control playback from the couch

**As a** Viewer
**I want** to pause, resume and seek with a few keys on a wireless keyboard
**So that** the picture and both Viewers' sound stop, start and jump together without anyone getting up

### US-03: Move to the next or previous file

**As a** Viewer
**I want** to switch to the next or previous video file in the same folder with one key
**So that** we can watch episodes of a series one after another

### US-04: Correct lip-sync by ear

**As a** Viewer
**I want** to shift my own output's sound earlier or later in small steps while the film plays, and have the player remember it for my device
**So that** I fix a visible mismatch once and never have to fix it again for the same device

### US-05: Close lip-sync on the first start

**As a** Viewer
**I want** the player to estimate each device's latency from what it knows about the device
**So that** the first film with a new device is already close to lip-synced before I touch anything

### US-06: Keep watching when an output changes

**As a** Viewer
**I want** the picture to follow smoothly when the slowest output changes — its offset is corrected or its device is lost
**So that** neither Viewer sees the picture jump or loses lip-sync for longer than a moment

### US-07: Prove lip-sync on the target PC

**As a** Developer
**I want** to measure, per output, how far the sound is from the picture with a click-and-flash test video filmed by a phone, and see in the log which latency, offset and picture delay the player used
**So that** I can accept this step on measurements of what the Viewer actually hears, not only on the player's own numbers

## 5. Acceptance criteria

### AC-01 (US-01) — happy path

**Given** a video file with a picture and at least two audio tracks, and the Developer's start command names the file, a track and a device for each Viewer, and a screen
**When** the start command is given
**Then** the film opens fullscreen on the named screen (on the main screen when none is named), the picture and every output's sound start together within the start budget (§6), and each Viewer hears their own track; on a device whose offset was set in an earlier run (AC-12) the sound is in step with the lips within the lip-sync budget (§6), on a device's first use the error is only measured (§7)

### AC-02 (US-01) — error

**Given** a start command whose file has no picture, or whose picture the PC cannot decode
**When** the start command is given
**Then** nothing plays, no window stays open, and the Developer is told in plain words that the file has no picture or that its picture format cannot be played on this PC, naming the format

### AC-03 (US-01) — authorization

**Given** a start command naming a file the operating system does not let the player read
**When** the start command is given
**Then** nothing plays and the Developer is told that the operating system denied the player access to that file, so they can fix the permission and retry

### AC-04 (US-02) — happy path

**Given** a film is playing on two outputs
**When** a Viewer presses the pause key, and later the pause key again
**Then** the picture freezes on the frame that matches the last sound each Viewer heard and every output falls silent together; on the second press picture and sound continue together from that point, lip-synced again within the recovery time (§6)

### AC-05 (US-02) — happy path

**Given** a film is playing on two outputs
**When** a Viewer presses a seek key for −10 s, +10 s, −60 s or +60 s
**Then** the picture and every output continue from the new position together, lip-synced again within the recovery time (§6); a seek before the start goes to the start, and a seek past the end ends the film as at its natural end

### AC-06 (US-02) — happy path

**Given** a film is playing
**When** it reaches its end, or a Viewer presses the exit key
**Then** the picture stops on its last frame at the end of the film, or the window closes on exit, every output falls silent together, and the run summary of step 3 is printed and logged for every output

### AC-07 (US-03) — happy path

**Given** a film is playing, paused or has reached its end, and its folder holds other video files
**When** a Viewer presses the next-file or previous-file key
**Then** the player stops the current file, prints and logs its run summary, and plays the neighbouring playable file in natural name order ("Episode 2" before "Episode 10"), on the same outputs with the same track numbers and offsets, lip-synced within the start budget (§6)

### AC-08 (US-03) — domain invariant

**Given** a film is playing, paused or has reached its end, and no playable file follows it in its folder in natural name order (or precedes it, for the previous-file key)
**When** a Viewer presses the next-file key (or the previous-file key)
**Then** the current film stays as it was — playing, paused or at its end — and the window shows briefly that there is no next (or previous) file

### AC-09 (US-03) — cross-context

**Given** the neighbouring file lacks a track number that one of the outputs plays (for example it has one audio track while an output plays track 2)
**When** a Viewer presses the next-file key
**Then** that file is skipped and the next playable file in the same direction plays (AC-07), the window shows briefly which file was skipped and whose track it lacks, and the Developer's log names the file, the output and the missing track

### AC-09b (US-03) — error

**Given** the neighbouring file cannot be read, has no picture, or has a picture this PC cannot decode
**When** a Viewer presses the next-file or previous-file key
**Then** that file is skipped in the same way as in AC-09, the window shows briefly which file was skipped and why in plain words, and the Developer's log names the file and the reason

### AC-10 (US-04) — happy path

**Given** a film is playing on two outputs
**When** a Viewer presses their output's offset key to make the sound later (offset +10 ms) or earlier (offset −10 ms)
**Then** that output's sound moves by 10 ms per press against the picture while every other output stays where it was against the picture (if the change moves the picture delay, the picture follows as in AC-14), the window shows briefly the output and its new offset, and the next start on the same device uses this offset again

### AC-11 (US-04) — error

**Given** an output's offset is already at +500 ms (or −500 ms)
**When** a Viewer presses the key that would make it later (or earlier)
**Then** the offset stays at the limit and the window shows briefly that the offset cannot go beyond ±500 ms

### AC-12 (US-04) — cross-context

**Given** a device had an offset remembered in an earlier run
**When** a film is started with that device on any output
**Then** the output starts with the remembered offset, and the Developer's log shows the device, the remembered offset and the latency used

### AC-13 (US-05) — happy path

**Given** a device that has never been used by the player
**When** a film is started on it
**Then** its output's assumed latency is the latency the operating system reports for the device plus the default for its device type (Bluetooth headphones, Bluetooth speaker, wired, HDMI, other), its offset is 0, and the Developer's log shows the reported part, the type default, the type it was taken as, and the resulting picture delay

### AC-14 (US-06) — domain invariant

**Given** a film is playing and the picture is held back by the picture delay the slowest output needs
**When** the picture delay has to change — an offset is corrected, or the slowest output's device is lost and another output becomes the slowest
**Then** the picture and every output whose offset did not change move to the new delay together, at most 0.2 % faster or slower than normal speed, so that nobody sees a jump, a black frame or a repeated scene or hears a change of pitch, and each of those outputs stays within the lip-sync budget (§6) throughout the move; the corrected output reaches its new offset no later than the move ends

### AC-15 (US-06) — happy path

**Given** a film is playing on two outputs
**When** one output's device is lost
**Then** the picture and the other output keep playing (step 3 AC-06), the picture follows the remaining outputs' largest latency as in AC-14, and the loss is shown briefly in the window

### AC-16 (US-07) — happy path

**Given** a click-and-flash test video (a white flash on screen and a click on every output at the same moment, once per second) is playing on the target PC with the outputs under test
**When** the Developer films the screen with a phone, first recording a hand clap close in front of the phone and then holding each output's device to the phone's microphone in turn
**Then** the clap gives the phone's own offset between sound and picture, which is subtracted from every measurement, and the recording shows, per output, the time between the flash and the click, and the Developer records it in the evidence together with the latency, offset and picture delay the player logged

### AC-17 (US-07) — happy path

**Given** a film is playing
**When** the picture delay or any output's latency or offset changes, or a seek, pause, resume or file change happens
**Then** the log records the master-clock position, the new picture delay, each output's assumed latency split into reported part and type default, and its offset, and how far behind or ahead the picture was presented compared with when it was due

## 6. Non-functional requirements

All targets apply to the target Windows 11 PC; the macOS development path is not held to them. They are guaranteed for the conditions of step 3: two Bluetooth headsets in the same room as the PC, in line of sight, plus optionally one wired or HDMI output (`two-outputs-one-clock/spec.md §6`).

| Aspect | Target | Measurement |
|---|---|---|
| Lip-sync per output, after the Viewer's offsets are set | sound no more than 45 ms ahead of and no more than 125 ms behind its picture (ITU-R BT.1359 detectability) | click-and-flash test video filmed with a phone at ≥ 120 frames per second, the phone's own offset subtracted (AC-16), 10 flashes per output, the largest error counts |
| Start command to picture and sound | ≤ 3 s, picture and every output together (step 3 budget, ADR-0008) | timestamps in the log: start command, first frame presented, first audio heard per output |
| Recovery after pause, resume, seek or file change | picture and every output lip-synced again ≤ 1 s after the command (file change: within the start budget) | log: command time and the first picture presentation within 20 ms of due; unit tests with a fake clock |
| Picture-delay change during playback | picture and unchanged outputs move together at ≤ 0.2 % speed change (about 5 s per 10 ms of change), each within the lip-sync budget throughout | log of picture presentation against due time and of playback speed; unit tests with a fake clock |
| Picture presentation on the target PC | ≤ 1 frame presented late or dropped per minute, averaged over a 2 h film, for 1080p H.264 and HEVC files with hardware decoding | the renderer's late and dropped frame counters in the log |
| CPU while playing a 1080p HEVC film on two outputs | ≤ 15 % of total CPU (`idea-brief.md` NFR) | operating-system performance counter, average over the run |
| Offset range and step | −500 to +500 ms in 10 ms steps per output | unit tests; AC-10, AC-11 |

## 6.1 Security / privacy

- **Data classification:** internal — remembered offsets and run logs stay in the Windows user's own app-data folder.
- **Personal data touched:** offsets are kept per device id; device names reported by the operating system may contain a person's name and appear in local logs (as in step 3). No new personal data.
- **AuthZ/AuthN impact:** none — a local single-user program; the only access checks are the operating system's own file and device access (AC-03, step 3 AC-04).
- **Abuse cases:**
  - A malformed or hostile video file: the player refuses it with a plain explanation (AC-02) and never crashes or hangs.
  - A remembered-offsets file edited by hand with values out of range: the player clamps them to ±500 ms and logs it.
  - A folder with thousands of files: next and previous still answer within the start budget (§6).
- **Security review:** N/A — local program with no network access, no new access boundary and no personal data beyond operating-system device names.

## 7. Metrics / KPIs

- **Lip-sync measurements recorded on the target PC** — baseline: 0, target: one measurement (AC-16) per output for both Bluetooth headsets and one wired or HDMI output, committed under `docs/` before roadmap step 8 is specified.
- **Largest lip-sync error per output after offsets** — baseline: unknown (never measured), target: within the §6 budget in the recorded measurement.
- **Offset each device needs on first use** — baseline: unknown, target: recorded per device type in the evidence; used to set the type defaults (§8) and as input to step 12.
- **Offset corrections per film after the first one per device** — baseline: n/a, target: 0 in the owner's first five films after acceptance.

## 8. Open questions

- [ ] Which numbers are the device-type defaults (Bluetooth headphones, Bluetooth speaker, wired, HDMI, other)? Default now: Bluetooth headphones 200 ms, Bluetooth speaker 200 ms, wired 0 ms, HDMI 0 ms, other 0 ms, revised from the first-use measurements (§7). — owner: Volodymyr Kravchuk, due: before `/sdd:tasks`
- [ ] Does the playback position a Bluetooth device reports already include part of its codec and headset latency (carried from `two-outputs-one-clock/spec.md §8`)? Default now: treat it as not included; the first-use measurement (§6) settles it. — owner: Volodymyr Kravchuk, due: before `/sdd:design`
- [ ] The TV's own picture-processing delay is not known to the PC and shifts every output by the same amount. Default now: not modeled; the Viewers' offsets absorb it, and a single screen offset is left to step 8. — owner: Volodymyr Kravchuk, due: before `/sdd:design`
- [ ] One device id can stand for different physical devices (headphones and speakers on the same jack share one id). Default now: accepted; the offset belongs to the id. — owner: Volodymyr Kravchuk, due: before `/sdd:tasks`
- [ ] Roadmap D1: can the picture be presented against the audio master clock precisely enough for the §6 lip-sync budget and the 0.2 % speed change with the planned video rendering, or is an own decode-and-present path needed? Default now: a prototype during design decides. — owner: Volodymyr Kravchuk (decision), prototype by the agent, due: during `/sdd:design`
- [ ] Do the primary texts of ITU-R BT.1359 and EBU R37 confirm the 45 ms sound-early and 125 ms sound-late limits (the fact-check of 2026-11-19 saw them only in search summaries; BT.1359 also gives an overall tolerance of +90 ms and −185 ms)? Default now: use 45 and 125 ms. — owner: Volodymyr Kravchuk, due: before `/sdd:design`
