---
status: Draft
owner: "Volodymyr Kravchuk"
reviewers: ["Tech Lead", "Security Lead"]
updated_at: "2026-11-11"
feature_size: "L"
---

# Spec — two-outputs-one-clock

> **Glossary:** [CONTEXT](../../../CONTEXT.md) (repo-root; no feature-level CONTEXT.md)
> **Reference module / docs / channels used:** `docs/idea-brief.md` (§2, §4, §6, §7, §8), `docs/roadmap.md` (step 3, graph edges 3→4/5/6), `docs/architecture-map.md`, `docs/adr/0002-put-audio-output-behind-a-backend-port.md`; no extra channels.

## 1. Context

Two Viewers watch one video, each hearing their own track on their own device, lip-synced with the picture, on any Windows 11 PC that meets the minimum requirements and without a calibration ritual (`idea-brief.md §2`). Before any video or couch UI is built, the player needs an engine that decodes a file once and plays several tracks on several devices under one master clock. Independent players cannot share a clock: the owner's experiment with two separate player processes on the target PC showed no noticeable desync over one evening, but crackling on quiet passages with no localized cause (`idea-brief.md §6`, Experiment results). The Developer is the only person who works with this step's console; the Viewers are the people whose listening experience it must protect.

Why now: the skeleton is in place (roadmap step 2), and roadmap steps 4 (lip-synced playback), 5 (audio outputs) and 6 (self-diagnostics) all depend on this engine (`roadmap.md` dependency graph). There is no external deadline (`idea-brief.md §4`); the trigger is that this step is on the critical path.

Committed approach (owner-accepted direction, `idea-brief.md §7` items 1 and 4): one engine decodes the file once and fans the chosen tracks out to N audio outputs; a single master clock, independent of any one device, drives them; each output's drift against that clock is measured from the device's own reported playback position and corrected continuously, without the Developer's help; generous buffering keeps underruns away; drift, correction and underrun counters are recorded per device. The step ships as a Developer console with play, pause and seek, so start/stop alignment is proven before video arrives. Fact-check (2026-11-11): the target OS does expose a per-device playback position suitable for drift measurement, but its accuracy for Bluetooth devices is unknown, and the lip-sync perception budget (about +45 ms audio-early / −125 ms audio-late, ITU-R BT.1359) is only partly verified — so drift gets a tight share of it (≤ 10 ms, §6) and the rest is left to step 4's latency compensation. The sharpest failure mode is a "done" that was only ever exercised on the dev Mac or with fake devices (ADR-0002: the Mac path does not validate Windows timing); therefore the step is accepted only after a recorded long run on the target Windows PC.

Traceability: requirements AUD-01 (≥2 outputs, own track and device), AUD-07 (transport on all outputs), AUD-08 (recovery after seek ≤ 1 s; the "≤ 20 ms between outputs" part is superseded by per-output alignment with the master clock, `idea-brief.md §7`), AUD-09 (only the "others keep playing" part), AUD-10 (only a basic fold-down) from `idea-brief.md §1`; architecture direction items 1 and 4 and the self-diagnostics counters (item 5) from `idea-brief.md §7`.

## 2. Goals

- A single decode drives two or more audio outputs on different devices that stay aligned with one master clock for a feature-length run without the Developer intervening.
- Pause, resume and seek act on all outputs at once, and every output re-aligns with the master clock shortly after.
- Losing one device never stops or disturbs the remaining outputs.
- Per-device evidence (drift, corrections, underruns, device clock mismatch) measured on the target Windows PC is recorded so steps 4–6 build on numbers, not assumptions.

## 3. Non-goals

- Video and lip-sync latency compensation (delaying video by the slowest device, per-device latency offsets) — roadmap step 4 owns them; this step aligns outputs with the master clock, not with what the listener hears after the device's own latency.
- Device hotplug, automatic reconnect, friendly device picker, per-output volume/mute, language auto-select, same-track mode and dialogue-normalized downmix (AUD-02/04/06/09-reconnect/10/12) — roadmap step 5 owns the output model; here only a basic fold-down of multichannel tracks is in scope.
- Diagnostic hints, Hands-Free detection, per-adapter stream count and the minimum-requirements check — roadmap step 6 consumes this step's counters but is built separately.
- Fixing crackling caused outside the player (adapter bandwidth, radio interference) and anti-crackling settings — roadmap steps 1 and 7; this step only makes underruns visible so the cause can be told apart.
- External audio files next to the video (AUD-03 "S" part) — not needed to prove the engine.
- Sync targets on the macOS development backend — it exists for hearing the engine during development and is never shipped (ADR-0002).

## 4. User stories

### US-01: Play a file on several outputs

**As a** Developer
**I want** to start a file with each audio output assigned its own track and its own device
**So that** each Viewer hears their own track from one shared start

### US-02: List devices and tracks

**As a** Developer
**I want** to list the connected devices and the tracks inside a file
**So that** I can choose the inputs for a run without guessing device ids or track numbers

### US-03: Hear my track without drift

**As a** Viewer
**I want** to hear my track without drift building up over a feature-length film
**So that** my sound never wanders away from the shared playback timeline

### US-04: Pause, resume and seek together

**As a** Developer
**I want** to pause, resume and seek all audio outputs at once
**So that** start and stop alignment is proven before video is added

### US-05: Keep listening when another device drops

**As a** Viewer
**I want** to keep hearing my track when the other Viewer's device disappears
**So that** someone else's device problem never interrupts my viewing

### US-06: Per-device sync counters

**As a** Developer
**I want** drift, correction and underrun counters recorded separately for every device
**So that** roadmap steps 4–6 and the crackling investigation work from data instead of guesses

### US-07: Play multichannel tracks on stereo devices

**As a** Developer
**I want** to play a 5.1 or 7.1 track on a stereo device
**So that** I can test the engine with real films, not only specially prepared files

### US-08: Run summary per output

**As a** Developer
**I want** a per-output summary of the run when playback stops
**So that** I can record the target-PC measurements in the repository and close this step on evidence

## 5. Acceptance criteria

<!-- Coverage: happy AC-01/02/07/08/09/13 · error AC-03/10 · authorization AC-04 · domain invariant AC-05/06 · cross-context AC-11/12. Every US has ≥1 AC. -->

### AC-01 (US-01) — happy path

**Given** the Developer has a media file with at least two tracks and two connected devices
**When** the Developer starts playback with one track assigned to the first device and another track assigned to the second device
**Then** both audio outputs start together from the beginning of the file, each Viewer hears the assigned track, and the console shows for every output its track, its device and the current master-clock position

### AC-02 (US-02) — happy path

**Given** devices are connected and the Developer has a media file
**When** the Developer asks for the device list and the track list
**Then** the console shows every playback device the operating system currently offers (name, device type when known, device id) and every audio track of the file (number, language, channel layout), and nothing starts playing

### AC-03 (US-01) — error

**Given** the Developer names a file that cannot be opened as media, a track number the file does not have, or a device that is not connected
**When** the Developer starts playback
**Then** no audio output starts and the console names the invalid input and the reason in plain language (for example: track 3 does not exist, the file has tracks 1 and 2)

### AC-04 (US-01) — authorization

**Given** one of the chosen devices is held exclusively by another application, or the operating system otherwise refuses the player access to it
**When** the Developer starts playback
**Then** no audio output starts and the console names that device and says the operating system denied the player access to it, so the Developer can free the device and retry

### AC-05 (US-01) — domain invariant

**Given** the Developer assigns the same device to two audio outputs
**When** the Developer starts playback
**Then** the system refuses to start and names the rule "one audio output per device"

### AC-06 (US-05) — domain invariant

**Given** two audio outputs are playing
**When** the second output's device disappears (switched off, unplugged, out of range)
**Then** the first output keeps playing without a gap, the master clock keeps running, the second output stops, and the console and the log record which device was lost and at which master-clock position; the lost output is not reconnected automatically

### AC-07 (US-03) — happy path (long run)

**Given** two audio outputs play on devices whose clocks run at slightly different speeds
**When** playback runs uninterrupted for at least 3 hours with no action from the Developer
**Then** each output stays within the steady-state drift tolerance of the master clock (§6), and every moment an output exceeds that tolerance is recorded with its device, size and master-clock position

### AC-08 (US-04) — happy path

**Given** two audio outputs are playing
**When** the Developer pauses and later resumes playback
**Then** on pause all outputs fall silent together and the master clock stops; on resume all outputs continue from the same master-clock position and are back within the drift tolerance within the recovery time (§6)

### AC-09 (US-04) — happy path

**Given** two audio outputs are playing
**When** the Developer seeks 10 or 60 seconds forward or backward
**Then** all outputs continue from the new position, each is back within the drift tolerance within the recovery time (§6), and no output plays audio from the old position once the seek is confirmed in the console

### AC-10 (US-04) — error

**Given** two audio outputs are playing
**When** the Developer seeks to a point before the start or past the end of the file
**Then** a seek before the start plays from the beginning, a seek past the end plays to the end and stops with the run summary (AC-13), and in both cases the console tells the Developer the target was outside the file

### AC-11 (US-06) — cross-context

**Given** a run is in progress on one or more devices
**When** playback continues
**Then** at a fixed interval the system records, for every device, its current drift, current correction and cumulative underruns, tied to that device's operating-system id and the master-clock position, so roadmap step 6 (self-diagnostics) can read these records without any change to the engine

### AC-12 (US-07) — cross-context

**Given** a track's channel layout or sample rate differs from what its device plays natively (for example a 5.1 track on stereo headphones, or a 44.1 kHz track on a 48 kHz device)
**When** the Developer plays that track on that device
**Then** the Viewer hears all channels folded into the device's layout at the correct pitch and speed, and the output still meets the drift tolerance (§6)

### AC-13 (US-08) — happy path

**Given** a run has been playing
**When** playback stops because the file ended, the Developer stopped it, or the console was closed normally
**Then** the console prints and the log records, for every audio output: its device, played duration, largest steady-state drift, estimated device clock mismatch in parts per million, number of corrections, number of underruns and any device-lost events

## 6. Non-functional requirements

All targets apply to the target Windows 11 PC; the macOS development backend is not held to them (ADR-0002).

| Aspect | Target | Measurement |
|---|---|---|
| Steady-state drift of each audio output from the master clock | ≤ 10 ms (absolute) | per-device records (AC-11) over a ≥ 3 h run on the target PC with both Bluetooth headsets; largest value outside the recovery windows. Measured against the device-reported playback position only; acoustic verification is roadmap step 12 |
| Recovery after start, resume or seek | every output within the drift tolerance ≤ 1 s after the command | per-device records (AC-11); unit tests with a fake clock and fake devices |
| Underruns on the target PC | 0 per 3 h run with two Bluetooth headsets | run summary (AC-13) |
| Start command to audio on all outputs | ≤ 2 s | timestamps in the log |
| Surviving output during another device's loss | 0 underruns within ±5 s of the device-lost event | fake-device test + manual disconnect on the target PC |
| CPU while playing two tracks | ≤ 5 % of total CPU on the target PC | operating-system performance counter, average over the run |
| Number of outputs | ≥ 3 outputs simultaneously within the drift tolerance | test with fake devices (no third physical device is required) |

## 6.1 Security / privacy

- **Data classification:** internal — run logs and summaries stay on the Developer's machine unless the Developer commits a summary to the repository.
- **Personal data touched:** none collected; device names reported by the operating system may contain a person's name (for example "Volodymyr's headphones") and appear in local logs and run summaries.
- **AuthZ/AuthN impact:** none — a local single-user console; the only access check is the operating system's own device access (AC-04).
- **Abuse cases:**
  - Malformed or hostile media file: the player refuses it with a plain explanation (AC-03) and never crashes or hangs.
  - Unbounded log growth during multi-hour runs: logs roll over with a size cap, so a long run cannot fill the disk.
  - Device names leaking through committed run summaries: the Developer reviews a summary before committing it to the repository.
- **Security review:** N/A — local console with no network access, no new access boundary and no personal data beyond operating-system device names.

## 7. Metrics / KPIs

- **Target-PC evidence runs recorded in the repository** — baseline: 0, target: ≥ 1 run of ≥ 3 h with both Bluetooth headsets, its run summary (AC-13) committed under `docs/` before roadmap step 4 is specified.
- **Largest steady-state drift measured on the target PC** — baseline: unknown (never measured; measurement plan = the evidence run above), target: ≤ 10 ms in the recorded run.
- **Underruns per 3 h run on the target PC** — baseline: unknown (crackling heard on 2026-11-08, cause not localized; measurement plan = the evidence run above), target: 0.
- **Device clock mismatch recorded per device** — baseline: none recorded, target: a parts-per-million figure recorded for both headsets in the evidence run, available to roadmap step 4.
- **Engine changes roadmap step 6 needs to read the counters** — baseline: n/a (step 6 not started), target: 0 changes when step 6 is built.

## 8. Open questions

- [ ] Does the playback position a Bluetooth device reports match what the Viewer actually hears (codec and headset buffering after that point)? Default now: treat it as accurate for drift; the remaining latency belongs to roadmap step 4. — owner: Volodymyr Kravchuk, due: before `/sdd:specify` of roadmap step 4
- [ ] If roadmap step 1 finds the crackling comes from the Bluetooth adapter or radio interference, does "0 underruns" still define this step as done? Default now: yes — the underrun counter measures the engine only. — owner: Volodymyr Kravchuk, due: before the target-PC evidence run
- [ ] Do the primary texts of ITU-R BT.1359 and EBU R37 confirm the lip-sync budget (about +45 ms audio-early / −125 ms audio-late; only partly verified on 2026-11-11)? Default now: drift keeps a 10 ms share of that budget. — owner: Volodymyr Kravchuk, due: before `/sdd:specify` of roadmap step 4
- [ ] Which devices join the evidence run besides the two Bluetooth headsets? Default now: add one wired output if available. — owner: Volodymyr Kravchuk, due: before `/sdd:tasks`
