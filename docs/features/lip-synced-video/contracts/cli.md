---
status: Draft
owner: "Volodymyr Kravchuk"
updated_at: "2026-11-20"
feature_size: "L"
---

# CLI contract — lip-synced-video

`sad.md` declares `target_surfaces: [desktop-app, cli]`. The `desktop-app` surface (the playback window, its fixed keys and brief notes) consumes no contract and is specified by `spec.md` §5 and `screens`. This document is the `cli` surface: the start command the Developer gives in a terminal, what it prints, and how it exits (ADR-0001). Derived from `spec.md` §4/§5/§6/§6.1, `sad.md` §6 flows 1–9 and §8, `data-model.md` (`device-offsets.json`), and the step-3 contract `docs/features/two-outputs-one-clock/contracts/cli.md`, whose conventions it reuses.

Conventions:
- Executable: `DualPlayer` (`dotnet run --project src/DualPlayer.App -- play …` during development). It is a console-subsystem executable in this step, so everything below prints to the terminal that started it (ADR-0001).
- Normal output goes to standard output; errors go to standard error as `error <code>: <plain-language message>`, with the same `code` as the Core `Error(Code, Message)` record (`src/DualPlayer.Core/Result.cs`). Every printed line is also written to the log.
- Times are master-clock positions formatted `hh:mm:ss.fff`; latency, offset and picture delay are integer milliseconds with a sign; a positive offset makes that output's sound heard later (`CONTEXT.md` Offset).
- Device and track arguments follow the step-3 console: a device is a number from `DualPlayer.Console devices` or a full OS endpoint id; a track is a number among the file's audio tracks, from 1 (`DualPlayer.Console tracks <file>`). This step adds no listing command.
- Fields on a printed line are separated by exactly two spaces, with these exceptions where the code pads a fixed-width label or column: the `next` / `previous` file-change line (label padded to 8 characters, then one space, so `previous <file name>` has one space and `next     <file name>` has five), the `picture` line (`picture   delay …`) and the `started` line (`started   clock …`) (labels padded to 10 characters), and the rows of the run-summary table (each column padded to its fixed width, then one space). The alignment of columns in the other examples below is illustrative only; match on the two-space separators, not on the padding.
- Placeholder values only in the examples below.

## 1. Command

### 1.1 `play <file> --out <track>=<device> [--out …] [--screen <n>]` — start a film (US-01, US-05, AC-01, AC-02, AC-03, AC-03b, AC-12, AC-13)

```
DualPlayer play <file> --out 1=1 --out 2=2
DualPlayer play <file> --out 1=1 --out 2=2 --out 1=3 --screen 2
```

| Argument / option | Meaning | Constraint | Source |
|---|---|---|---|
| `<file>` | Video file to play | must be readable, have a picture, and its first picture must decode in hardware within the start budget | AC-01, AC-02, AC-03, spec §6.1 |
| `--out <track>=<device>` | One audio output; outputs are numbered 1…3 in the order given (window keys 1/Q, 2/W, 3/E) | repeatable, 1 to 3; track ∈ the file's audio tracks; a device may appear once only; the same track may appear on several outputs | spec §5 key table, AC-01, CONTEXT invariant "one audio output per device" |
| `--screen <n>` | Screen for the fullscreen window, numbered from 1 as listed in a `start.screen_not_found` error | optional; default = the main screen | AC-01, AC-03b |

Order of checks, all before any window opens or any device plays (flow 1, ADR-0007): arguments → file access → file readable → picture present and hardware-decodable (a first picture not decoded within the 3 s start budget is `video.probe_timeout`) → tracks → devices (present, used once, then opened and released) → screen → video renderer library. The first failure prints its error and exits; nothing plays and no window stays open.

On start the App prints one line per output with its latency estimate, then the start line (AC-12, AC-13, spec §6 start budget):

```
output 1  track 1  device Test Headset A  type bluetooth-headphones  reported 0  default 200  offset +40  assumed 160  lag 40
output 2  track 2  device Test Headset B  type bluetooth-headphones  reported 0  default 200  offset 0    assumed 200  lag 0
picture   delay 200  screen 1  1920x1080
started   clock 00:00:00.000  first-frame 1.9 s  first-audio 1.9 s 1.9 s
```

| Field | Meaning | Source |
|---|---|---|
| `type` | Device type taken: `bluetooth-headphones`, `bluetooth-speaker`, `wired`, `hdmi`, `other` | AC-13, CONTEXT Device type |
| `reported` | Latency the OS reports beyond the device's own playback position (0 when it reports nothing) | AC-13, ADR-0004 |
| `default` | Type default in use (spec §8) | AC-13 |
| `offset` | Remembered offset for this device id, 0 on first use | AC-12, `data-model.md` DeviceOffset |
| `assumed` | `reported + default − offset` | AC-10, AC-13, ADR-0004 |
| `lag` | How far this output waits behind the master clock: picture delay − assumed. A `pause` record taken mid-ramp carries the lag the engine actually applies at that moment, so there lag ≠ picture delay − assumed; `assumed` is never rewritten | ADR-0005 |
| `picture delay` | Largest `assumed` among the outputs still playing; may be negative | AC-10, CONTEXT Picture delay |
| `lost` | An output lost before a file change keeps its number but is printed as `output N  lost`, with no `track`, `assumed` or `lag`: it is left out of the picture delay and the lag arithmetic above, which holds for the playing outputs only | AC-07, AC-12, AC-13 |
| `first-frame`, `first-audio` | Start-command-to-picture and to each output's first heard sound (sound given + assumed latency) | spec §6 start budget |

If the remembered-offsets file cannot be read, the App prints `warning state.unreadable: remembered offsets could not be read, every output starts at offset 0` (or `state.invalid` / `state.too-new` / `state.no-upgrader`) and continues (spec §6.1). Out-of-range or off-grid values print `warning offsets.clamped: device {…} offset 900 -> 500` per value.

The process then stays in the run, controlled from the window (§2), until the window closes.

## 2. During the run

The Viewers control the film from the window with the fixed keys of `spec.md` §5 (Space, ←/→, ↓/↑, Page Up/Page Down, Esc, 1/Q, 2/W, 3/E). The App reads no commands from standard input in this step. Brief notes appear in the window only (SCR-03); the terminal receives the lines below.

### 2.1 Run summary — at every natural end, exit and file change (AC-06, AC-07, AC-15b)

Printed in the step-3 run-summary format (two-outputs-one-clock `contracts/cli.md` §2), one block per output, covering the file's playback since it was opened, preceded by:

```
run-summary  file <file name>  reason end|exit|file-change|last-output-lost|picture-failed
```

Once the film has ended (reasons `end` (also a seek past the end), `last-output-lost`, `picture-failed` and `file-change` when the new file fails to start) its run summary is already printed, so the exit key afterwards closes the window and prints no second run summary: the end's summary covers the file (AC-06).

### 2.2 File change (US-03, AC-07, AC-08, AC-09, AC-09b)

```
skipped  <file name>  reason track-missing  output 2  track 2
skipped  <file name>  reason not-hardware-decodable  codec hevc  profile main-10
next     <file name>
previous <file name>
```

`reason` is one of `track-missing`, `unreadable`, `no-picture`, `not-hardware-decodable`, `access-denied`. The `codec` and `profile` fields are printed with `not-hardware-decodable` and also with an `unreadable` skip whose probe timed out (`video.probe_timeout`), when the probe had read them before it gave up; without them (a file that could not be opened, a probe that read nothing) the line ends at the reason. When no playable file is found in that direction: `no-next-file` (or `no-previous-file`), and the current film is unchanged. When the 3 s key budget runs out first: `next-file-timeout  checked N` (or `previous-file-timeout  checked N`) instead, the current film is unchanged, and a probe cut short by the budget is not printed as `skipped`. The 3 s budget counts from when the session starts the command, not from the key's arrival, so a key that waited behind a slow file change still checks files. Whenever the session gives up on a file its worker is cancelled (its file is closed). A file is remembered as timed out when its own listing or probe ran its full, uncut limit, or when it was the first file checked in the press (the folder work before it does not count) and could not be judged within the budget; a file whose check the key's budget cut is not remembered and not printed as `skipped`. The remembered key is the normalized path plus size and modification time, so a file that changed on disk is a new file. Later presses check remembered files last, only when budget is left (printed as `skipped ... reason unreadable`, counted in `checked N`; also when a healthy file after them plays), each remembered file is reported once per press, and a file whose listing or probe worker is still running (the probe adapter answers `video.probe_busy` without waiting) is stepped past for that press only (reported as `skipped ... reason unreadable`, counted, not judged) without starting a new worker, so once its worker has completed the next press checks it again, a file that recovers plays, and two hanging neighbours in a row do not block the key for ever. A file already judged for the film (for example `not-hardware-decodable`) keeps its verdict and reason on later presses and is not probed again. A press whose skips were all remembered files, files whose worker is still running or files whose check ran out its limit, and so judged none, ends as `next-file-timeout  checked N` (not `no-next-file`); the stepped-past files are still printed as `skipped ... unreadable` and counted. Both memories are cleared only on the next successful file change; a failed file change and a return into the same film keep them. A new file prints the §1.1 output and start lines again.

### 2.3 Device lost and picture failure (AC-06b, AC-15, AC-15b)

```
device-lost      output 2  device {…0002}  clock 00:40:12.300  others continue  picture-delay 200 -> 0
device-lost      output 1  device {…0001}  clock 00:41:00.000  last output, film ended
picture-failed   clock 01:10:05.120  the picture could not be decoded further, film ended
```

The window stays open after `last output` and `picture-failed`; next, previous and exit still work.

## 3. Log records (US-07, AC-13, AC-17)

Structured events in the rolling log (not printed unless stated above), from the session event stream (`sad.md` §5, §8; flow 9):

| Event | Fields | When |
|---|---|---|
| `LatencyEstimated` | per output: device id, type, reported, default, offset, assumed, lag; picture delay. A lost output keeps its output number and is written with `Lost=true` and no type, reported, default, offset, assumed or lag (§1.1) | every start and file change (AC-13, AC-12) |
| `PictureDelayChanged` | clock, cause (`offset`, `device-lost`, `seek`, `pause`, `resume`, `file-change`), old and new picture delay, per output assumed (always `reported + default − offset`) and lag (picture delay − assumed, except a mid-ramp `pause` record, which carries the lag the engine applies) (a lost output: `Lost=true` only, as in `LatencyEstimated`) | AC-17 |
| `PictureDelayMoved` | clock, cause (as `PictureDelayChanged`), from (the in-flight picture delay when a press retargets a running move) and to picture delay (ms), duration (ms, the stretched move duration), picture speed change (`PicturePpm`: the speed last set on the video renderer (mpv `speed`) once the move is applied, as (speed − 1) × 10⁶; signed: positive = the picture runs faster than real time, i.e. the delay shrinks), then one line per output with its device id and the speeds its drift controller applies once the move is applied, not clamped: lag ramp (`RampPpm`), drift correction (`CorrectionPpm`, as `correction=…ppm` in the sync records) and their sum (`TotalPpm`), each signed: positive = the output's audio runs slower than real time, i.e. its lag grows; written for each picture-delay move that ramps (offset change, device lost) | AC-14, AC-17 |
| `OffsetChanged` | output, device id, old and new offset, saved-at | AC-10, AC-11 |
| `PictureTiming` | clock, frame due time, presented time, error ms | every event above and once per second (AC-17) |
| `FrameCounters` | late (> 20 ms after due), dropped, skipped-damaged, per minute | spec §6 picture presentation, AC-06b |
| `StartTimings` | `CommandToFirstFrameMs` (milliseconds from the start command to the first frame presented, a decimal number) and `CommandToFirstAudioMs` (milliseconds from the start command to the first sound heard per output, whole ms, written as `deviceId=ms deviceId=ms`); the unit is milliseconds, never raw clock ticks | spec §6 start budget, AC-01, AC-07 |
| `PictureFailed` | clock (hh:mm:ss.fff), error code (`video.decode_failed`; absent when hardware decoding became inactive) and reason; logged as a warning when the picture could not continue | AC-06b, AC-07 |
| `PictureReload` | clock (the position the last frame was loaded at, hh:mm:ss.fff) and outcome (`reloaded` = the file is loaded again paused on the last frame, `failed` = the renderer refused the load, `timed-out` = the renderer reported `mpv.load_timeout`, the load used the whole 3 s bound, `failed-again` = the reloaded frame failed to decode again and the window is empty); written right after `PictureFailed` | AC-06b |
| `LipSyncedAgain` | clock, command (as `NotLipSynced`), ms since the key press (its wait in the command queue and the engine's own wait included), picture error ms (always present: a recovery counts as succeeded only on a measured frame; with no measurable frame the session logs `NotLipSynced` instead), latest error per output; logged as information when recovery succeeded within its budget; the T23 guide judges L1/L2 on it | spec §6 recovery, AC-17 |
| `DamagedStretchSkipped` | clock (the position where the damaged stretch starts), length (ms), frames skipped | AC-06b, AC-17 |
| `NotLipSynced` | clock, command (`start`, `seek`, `resume`, `file-change`, `return` = a backward seek at the end that returns into the film), ms since the key press (its wait in the command queue and the engine's own wait included), picture error ms (absent/null when no frame was measurable after the command), latest error per output; logged as a warning when recovery gave up after its budget (1 s; 3 s for a start, a file change or a return) | spec §6 recovery, AC-17 |

## 4. Exit codes

| Code | Meaning | Source |
|---|---|---|
| `0` | The window was closed with Esc (or the window was closed) after a film played, whatever ended it | AC-06, AC-15b |
| `1` | Unexpected failure (a bug); logged with its exception | `CLAUDE.md` Errors |
| `2` | Refused before start: usage, unreadable file, no picture, not hardware-decodable, track, device, too many outputs, screen not found, video renderer or FFmpeg library missing, a device that cannot be opened, first picture, file open or output start timed out | AC-02, AC-03b, step-3 §3 |
| `4` | Refused before start: the OS denied access to the file or a device | AC-03, step-3 AC-04 |

## 5. Error codes

| Code | When | Exit | Status in repo |
|---|---|---|---|
| `cli.usage` | Missing or malformed arguments, including `--screen` not a positive integer | 2 | exists |
| `playback.file_access_denied` | The OS denied the App read access to the file (AC-03) | 4 | new |
| `playback.file_unreadable` | The file cannot be opened as media | 2 | exists |
| `video.no_picture` | The file has no video stream (AC-02) | 2 | new |
| `video.not_hardware_decodable` | The picture's format (codec + profile named in the message) cannot be decoded in hardware on this PC (AC-02, spec §6.1) | 2 | new |
| `video.probe_timeout` | The first picture did not decode within the 3 s start budget; printed at start by the probe check, before tracks, devices and screen. The message names codec and profile when the probe had read them (AC-02, spec §6.1). On next or previous the same code is not printed as an error: the file is skipped with `reason unreadable` (§2.2) | 2 | new |
| `playback.track_not_found` | Track number not among the file's audio tracks | 2 | exists |
| `outputs.device_not_found` | Device number or id not a connected playback device | 2 | exists |
| `outputs.device_assigned_twice` | Same device on two outputs | 2 | exists |
| `outputs.too_many` | More than three `--out` options (the window has keys for three outputs) | 2 | new |
| `outputs.device_access_denied` | The OS refused access to a device | 4 | exists |
| `start.screen_not_found` | `--screen` names a screen that is not connected; the message lists the connected screens with their numbers and resolutions (AC-03b) | 2 | new |
| `mpv.not_found` / `mpv.load_failed` | The video renderer library is missing or the wrong version; `mpv.load_failed` is also returned by the session start when mpv refuses the file or fails to initialise | 2 | new |
| `ffmpeg.not_found` / `ffmpeg.load_failed` | The FFmpeg libraries are missing or cannot be loaded; printed by the very first check (the picture probe), before any other check, so also for a file that would otherwise be refused | 2 | exists |
| `outputs.device_open_failed` | A device that is present and not refused by the OS could not be opened: printed by the device check at start (opened and released), or by the session start when the output is opened for play | 2 | exists |
| `playback.start_timeout` | The outputs did not start, or the decoder did not fill the output buffers, in time when the session started; printed with the window already open, which then closes | 2 | exists |
| `playback.decoder_fault` | The audio decoder raised an exception while filling the output buffers before the start; printed by the session start with the window already open, which then closes | 2 | exists |
| `mpv.load_timeout` | The renderer library loaded and every check passed, but mpv did not open the file within its wait (15 s by default) when the session started the film; distinct from `mpv.load_failed`, where mpv refused the file (AC-02, spec §6.1) | 2 | new |
| `state.unreadable` / `state.invalid` / `state.too-new` | Remembered offsets could not be read — printed as a warning, the film starts with offsets 0 | — | exists (`state.too-new` is returned by `JsonFileStateStore`) |
| `state.no-upgrader` | The remembered-offsets file has no `schemaVersion`, so no upgrader applies — printed as a warning, the film starts with offsets 0 | — | exists |
| `offsets.clamped` | A remembered offset was out of range or off the 10 ms grid and was corrected — warning | — | new |

Example (AC-03b):

```
error start.screen_not_found: screen 3 was not found. Connected screens: 1 (1920x1080, main), 2 (3840x2160)
```
