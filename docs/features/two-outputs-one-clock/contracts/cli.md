---
status: Draft
owner: "Volodymyr Kravchuk"
updated_at: "2026-11-11"
feature_size: "L"
---

# CLI contract — two-outputs-one-clock

The Developer console (`DualPlayer.Console`, ADR-0001) is the only interface this feature exposes. It exists so the Developer can list inputs, start a run of the engine on several devices, control it while it plays, and close the step on recorded evidence (spec §1, §7). Derived from `spec.md` §4/§5, `sad.md` §6 flows 1–9 and §7/§8; there is no `data-model.md` (no schema change — every persisted item is a log record).

Conventions:
- Executable: `DualPlayer.Console` (`dotnet run --project src/DualPlayer.Console -- <command> …` during development).
- Output goes to standard output; errors go to standard error with a stable error code. Every line printed during a run is also written to the log (spec AC-11, AC-13).
- Times are master-clock positions formatted `hh:mm:ss.fff`. Drift is in milliseconds with a sign (`+` = the output is ahead of the master clock). Rates are in parts per million (ppm).
- Placeholder values only in the examples below.

## 1. Commands

### 1.1 `devices` — list playback devices (US-02, AC-02)

```
DualPlayer.Console devices
```

Prints every playback device the OS offers right now, numbered from 1, with its name, device type when known, and full OS endpoint id. Nothing plays. Exit code `0`.

```
#  name                    type        id
1  Test Headset A          bluetooth   {0.0.0.00000000}.{00000000-0000-0000-0000-000000000001}
2  Test Headset B          bluetooth   {0.0.0.00000000}.{00000000-0000-0000-0000-000000000002}
3  Test Speakers           speakers    {0.0.0.00000000}.{00000000-0000-0000-0000-000000000003}
```

Devices are sorted by OS endpoint id, so the same set of connected devices always gets the same numbers in `devices` and in a later `play`; if a device is connected or removed in between, numbers may shift — the full id is always accepted, and `play` echoes the id each number resolved to (AC-02).

### 1.2 `tracks <file>` — list the audio tracks of a file (US-02, AC-02, AC-03)

```
DualPlayer.Console tracks <file>
```

Prints every audio track of the file, numbered from 1 among audio tracks only, with language (or `und`) and channel layout. Nothing plays. Exit code `0`.

```
#  language  layout   rate
1  ukr       5.1      48000
2  eng       stereo   44100
```

Errors: `playback.file_unreadable` (exit `2`).

### 1.3 `play <file> --out <track>=<device> [--out …] [options]` — start a run (US-01, AC-01, AC-03, AC-04, AC-05, AC-12)

```
DualPlayer.Console play <file> --out 1=1 --out 2=2
DualPlayer.Console play <file> --out 1=1 --out 1={0.0.0.00000000}.{00000000-0000-0000-0000-000000000002}
```

| Argument / option | Meaning | Constraint | Source |
|---|---|---|---|
| `<file>` | Media file to play | must open as media | AC-01, AC-03 |
| `--out <track>=<device>` | One audio output: a track number and a device | repeatable, ≥ 1; track ∈ the file's tracks (from 1); device = number from `devices` or full OS id; a device may appear once only; the same track may appear on several outputs | AC-01, AC-02, AC-05 |
| `--ring-ms <n>` | Ring buffer per output | integer, default `2000` | `EngineOptions`, sad §7 |
| `--start-level-ms <n>` | Audio every ring must hold before the master clock starts | integer, default `500`, ≤ `--ring-ms` | `EngineOptions`, sad §7 |
| `--device-buffer-ms <n>` | Requested shared-mode device buffer | integer, default `40` | `EngineOptions`, sad §7 |

All inputs are validated before any device opens; on the first invalid input nothing plays (AC-03, AC-05). Devices are then opened in shared mode; if the OS denies one, every device already opened is closed and nothing plays (AC-04).

On start the console prints one line per output and the clock position (AC-01, AC-02 echo of the resolved id):

```
output 1  track 1 (ukr 5.1 -> stereo, LFE dropped)  device Test Headset A  id {…0001}
output 2  track 2 (eng stereo 44100 -> 48000)        device Test Headset B  id {…0002}
started   clock 00:00:00.000  start-to-audio 0.8 s
```

The process then stays in the run and reads interactive commands (§1.4) from standard input until the run ends.

Errors (exit `2` unless stated, nothing plays):
- `playback.file_unreadable` — the file cannot be opened as media.
- `playback.track_not_found` — e.g. `track 3 does not exist, the file has tracks 1 and 2`.
- `outputs.device_not_found` — the device number or id is not a connected playback device.
- `outputs.device_assigned_twice` — `one audio output per device`: the same device appears in two `--out` options.
- `outputs.device_access_denied` — names the device; the OS refused access (held exclusively or otherwise denied). Exit `4`.
- `cli.usage` — missing or malformed arguments.

### 1.4 Interactive commands during a run (US-04, US-08)

| Command | Effect | Source |
|---|---|---|
| `pause` | All outputs fall silent at one master-clock instant (within 10 ms of each other); the clock stops. Prints `paused at <clock>` | AC-08, flow 5 |
| `resume` | All outputs continue from the paused (or newly sought) position. Prints `resumed at <clock>` | AC-08, AC-09b |
| `seek <±seconds>` | Relative seek, e.g. `seek +60`, `seek -10` | AC-09, AC-10 |
| `seek <hh:mm:ss>` | Absolute seek | AC-09, AC-10 |
| `stop` | Ends the run: outputs fall silent, the run summary prints, the process exits `0` | AC-13, flow 9 |

Seek behaviour:
- While playing: prints `seek confirmed at <clock>` only after every output has discarded its old-position audio, including what its device had queued (AC-09, ADR-0006).
- While paused: prints `position moved to <clock>, still paused` (AC-09b).
- Before the start: plays from the beginning and prints `warning: target was outside the file, playing from 00:00:00.000` (AC-10).
- Past the end: prints `warning: target was outside the file`, moves to the end, outputs fall silent and the run ends with the summary, exit `0` (AC-10).

Unknown or malformed commands print `cli.unknown_command` to standard error; the run continues.

Ctrl+C and closing the console window behave like `stop` (AC-13). A crash or killed process prints no summary; the records up to that point stay in the log.

## 2. Output during a run

### 2.1 Sync record — once per second per device (US-06, AC-11)

```
record  clock 00:12:34.000  device {…0001}  drift_ms +1.4  correction_ppm -35  underruns 0
```

| Field | Meaning | Source |
|---|---|---|
| `clock` | master-clock position of the record | AC-11 |
| `device` | OS endpoint id | AC-11 |
| `drift_ms` | average drift over the record's 1 s interval | AC-11, spec §6 |
| `correction_ppm` | current continuous rate adjustment | AC-11, AC-13 |
| `underruns` | cumulative underruns of this device in the run | AC-11 |

The same record is raised as an engine event (`SyncRecord`, ADR-0007) and written to the log as a structured event.

### 2.2 Excursion — once per excursion (US-03, AC-07)

```
excursion  device {…0002}  start 01:02:03.000  duration_ms 850  peak_ms +14.2
```

Printed when the output is back within the 10 ms tolerance; covers the whole excursion once.

### 2.3 Device lost (US-05, AC-06, AC-06b)

```
device-lost  output 2  device {…0002}  clock 00:40:12.300  others continue
```

When it was the last playing output: `device-lost … last output, run ends`, followed by the run summary; exit `3`.

### 2.4 Run summary — at the end of every run (US-08, AC-13)

```
run summary  ended file-end  clock 01:52:10.000
output  device           played     max_drift_ms  clock_mismatch_ppm  corrections  underruns  device_lost
1       {…0001}  01:52:10.0  8.7           +41.2               0            0          -
2       {…0002}  00:40:12.3  9.1           -18.5               0            0          00:40:12.300
```

`ended` is one of `file-end`, `stop`, `interrupt` (Ctrl+C or window closed), `seek-past-end`, `all-devices-lost`. `max_drift_ms` is the largest steady-state drift outside the recovery windows; `corrections` counts one-step re-alignments only.

## 3. Exit codes

| Code | Meaning | Source |
|---|---|---|
| `0` | Listing finished, or a run ended with a summary (file end, `stop`, Ctrl+C, window closed, seek past the end) | AC-02, AC-10, AC-13 |
| `1` | Unexpected failure (a bug); logged with its exception | CLAUDE.md §Errors |
| `2` | Refused before start: invalid file, track, device, duplicate device, or usage | AC-03, AC-05 |
| `3` | Run ended because every output's device was lost; summary printed | AC-06b |
| `4` | Refused before start: the OS denied access to a device | AC-04 |

## 4. Error codes

All errors print to standard error as `error <code>: <plain-language message>`; the same `code` travels in the Core `Error(Code, Message)` record (`src/DualPlayer.Core/Result.cs`).

| Code | When | Exit |
|---|---|---|
| `cli.usage` | Missing or malformed command-line arguments | 2 |
| `cli.unknown_command` | Unknown interactive command during a run | — (run continues) |
| `playback.file_unreadable` | File cannot be opened as media | 2 |
| `playback.track_not_found` | Track number not among the file's audio tracks | 2 |
| `outputs.device_not_found` | Device number or id not a connected playback device | 2 |
| `outputs.device_assigned_twice` | Same device on two outputs — "one audio output per device" | 2 |
| `outputs.device_access_denied` | OS refused access to a device | 4 |
| `playback.seek_out_of_range` | Seek target before the start or past the end (warning) | — (run continues or ends per §1.4) |
