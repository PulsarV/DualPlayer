# API sync report — lip-synced-video — 2026-11-20

Contract form: `contracts/cli.md` — `target_surfaces: [desktop-app, cli]` read from `sad.md`; `desktop-app` consumes no contract. No async flows, so no `events.md`. Inputs found: `spec.md` ✓, `sad.md` §6 flows 1–9 ✓, `data-model.md` ✓ (one JSON file, no database).

## A. Field origins

| schema_path | origin | confidence |
|---|---|---|
| play.file | spec AC-01, AC-02, AC-03; flow 1 | high |
| play.--out (track, device, 1…3) | spec §5 key table, AC-01; step-3 `cli.md` §1.3; CONTEXT invariant | high |
| play.--screen | spec AC-01, AC-03b; flow 1 screen branch | high |
| start-line.type | spec AC-13; CONTEXT Device type | high |
| start-line.reported | spec AC-13 (clarified 2026-11-20); ADR-0004 | high |
| start-line.default | spec §8 type defaults | high |
| start-line.offset | `data-model.md` → DeviceOffset.value (−500…+500, step 10) | high |
| start-line.assumed | spec AC-10 (latency − offset); ADR-0004 | high |
| start-line.lag | ADR-0005; `sad.md` §8 Units and sign | medium (derived value, no AC names it) |
| start-line.picture delay | spec AC-10, AC-13; CONTEXT Picture delay | high |
| start-line.first-frame / first-audio | spec §6 start budget measurement | high |
| run-summary.reason | spec AC-06, AC-07, AC-15b, AC-06b; flows 3, 6, 7 | high |
| skipped.reason | spec AC-09, AC-09b; flow 3 | high |
| device-lost line | spec AC-15, AC-15b; flows 2, 6; step-3 `cli.md` §2.3 | high |
| picture-failed line | spec AC-06b; flow 7 | high |
| log.LatencyEstimated | spec AC-13, AC-12; flow 1 | high |
| log.PictureDelayChanged | spec AC-17; flow 9 | high |
| log.OffsetChanged | spec AC-10, AC-11; flow 8 | high |
| log.PictureTiming | spec AC-17; flow 9 | high |
| log.FrameCounters | spec §6 picture presentation (late > 20 ms), AC-06b | high |
| log.StartTimings | spec §6 start budget | high |
| exit codes 0/1/2/4 | step-3 `cli.md` §3; spec AC-02, AC-03, AC-03b | high |

## B. Drift checklist

1. **Command ↔ data-model** *(core)* ✓ — the start command reads `device-offsets.json` (DeviceOffset) for every output; the offset values and their range match `data-model.md`. The unreadable / out-of-range handling matches spec §6.1 and `data-model.md` access patterns.
2. **Error code ↔ repo** *(core)* ✓ with proposals — existing codes reused verbatim (`cli.usage`, `playback.file_unreadable`, `playback.track_not_found`, `outputs.device_not_found`, `outputs.device_assigned_twice`, `outputs.device_access_denied`, `state.unreadable`, `state.invalid`, `state.too-new`). New codes are proposals the implementation adds: `playback.file_access_denied`, `video.no_picture`, `video.not_hardware_decodable`, `outputs.too_many`, `start.screen_not_found`, `mpv.not_found`, `mpv.load_failed`, `offsets.clamped`.
3. **Validation ↔ constraint** *(core)* ✓ — outputs 1…3 (spec §5 key table), offsets −500…+500 step 10 (`data-model.md`), one device per output (CONTEXT invariant).
4. **Contract ↔ sequence** *(supporting)* ✓ — every `alt` branch of flow 1 (access, screen, no picture / not hardware-decodable) has an error code; flows 3, 6, 7 map to §2.2–§2.3 lines; flow 9 maps to §3. Flows 2, 4, 5, 8 act only in the window (no terminal contract).

**Flag (supporting, resolved 2026-11-20 — owner chose `playback.file_access_denied`; `sad.md` §8 updated):** `sad.md` §8 lists `start.access_denied` as an example code for AC-03; this contract uses `playback.file_access_denied` to sit beside the repo's existing `playback.file_unreadable`.

## C. Coverage back-feed

| AC | Contract element |
|---|---|
| AC-01 | §1.1 command + start lines |
| AC-02 | `video.no_picture`, `video.not_hardware_decodable` |
| AC-03 | `playback.file_access_denied` (exit 4) |
| AC-03b | `start.screen_not_found` with screen list |
| AC-04, AC-05, AC-10, AC-11, AC-14 | window only (desktop-app); logged via §3 |
| AC-06 | §2.1 run summary |
| AC-06b | §2.3 `picture-failed`, §3 FrameCounters |
| AC-07, AC-08, AC-09, AC-09b | §2.2 |
| AC-12, AC-13 | §1.1 start lines, §3 LatencyEstimated |
| AC-15, AC-15b | §2.3 `device-lost` |
| AC-16 | non-runtime (phone); evidence pairs with §1.1 and §3 |
| AC-17 | §3 |

Every §4 user story maps to the command or the window; no sequence gap found.
