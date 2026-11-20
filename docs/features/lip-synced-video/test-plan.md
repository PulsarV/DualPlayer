---
status: Draft
owner: "Volodymyr Kravchuk"
reviewers: ["implementing engineer", "Tech Lead"]
updated_at: "2026-11-20"
feature_size: "L"
---

# Test plan — lip-synced-video

The picture is shown fullscreen against the audio master clock, held back by the largest assumed latency among the playing outputs, so each Viewer hears their own track in step with the lips; a fixed key set drives pause, seek, next/previous file and per-device offsets, with picture and every output acting together.

## Levels

| Level | Scope | Strategy (generic — no tool names) |
|---|---|---|
| Unit | Pure Core logic: latency model, offset book, picture sync, lag ramp, folder neighbours ordering, playback session state machine, start-command validation. | In-memory, fake clock, fake audio devices, fake video renderer, fake latency and picture probes; no I/O, no native library. |
| Integration | An adapter against the real dependency it owns: the offsets state file on the real file system, the picture probe and the video renderer against real sample media, the folder listing on a real directory, the device latency probe against real audio devices. | Ephemeral real dependency: a throwaway temp directory per test, real sample media from the test-media folder, the real native libraries installed on the machine; tests needing a GPU, a screen or audio hardware are hardware-gated. |
| Contract | The agreed shapes between participants: the start command's terminal lines, exit codes and log records (`contracts/cli.md`); the `device-offsets.json` v1 shape; the video-renderer port (the fake and the real adapter honour the same behaviour). | Assert the real output against the agreed contract text and shape; one shared behaviour suite runs against both renderer implementations. |
| E2E | One full flow from the App entry point: start command → session → run summary and exit code. | The App host with the real decoder on sample media, the audio backend and time replaced by fakes; hardware-gated variants run the same flow on the target PC. |
| Load | NFRs with numbers: start budget, frame lateness over a 2 h film, CPU, folder size. | Soak and timing runs on the target Windows PC, hardware-gated; see the load section. |
| Component *(UI surface only)* | The playback view model (fixed key table → session commands, note text) and the playback window's states (opening, playing, paused, ended). | Render in a headless UI harness with a fake session; assert commands issued and state shown, no full app boot. |
| Visual-regression *(web UI only)* | <!-- N/A: desktop-app surface, not web; the picture and notes are drawn by the video renderer's own output --> | — |
| E2E-through-UI *(UI surface only)* | The user-story flows of `ux-flows.md` driven by key presses on the real playback window. | Headless window with the real view model and session over fake devices, fake clock and the fake renderer; keys sent as a Viewer would press them. |

## AC coverage

| AC (spec.md §5) | Test name (intent-based) | Level | Expected outcome |
|---|---|---|---|
| AC-01 happy path — film starts lip-synced | picture delay is the largest assumed latency and each lag puts heard time on the picture's due time | unit | for every output, heard time of content position p equals p + picture delay |
| AC-01 | session start releases every output with its lag and plays the picture at its due time | unit | first frame presented within 20 ms of due, every output within 10 ms of its due position, all inside the 3 s start budget on the fake clock |
| AC-01 | negative picture delay starts the picture ahead of the master clock | unit | picture starts the size of the picture delay before the slowest output hears frame 0 |
| AC-01 | video renderer plays a sample file from a given timestamp and reports its presented position | integration | presented position advances from the requested start; hardware-gated where a window or GPU is needed |
| AC-01 | play command starts the film and prints start lines, then a run summary on exit | e2e | start lines per output, picture and sound started, run summary printed, success exit code |
| AC-01 | window opens fullscreen on the named screen, or on the main screen when none is named | e2e-through-UI | window fullscreen on the expected screen in the opening state, then playing |
| AC-02 error — no picture / not hardware-decodable | start refuses a file whose picture probe says no picture or not hardware-decodable | unit | nothing plays, no window opened, refusal names the reason and the format |
| AC-02 | picture probe recognises an audio-only file and a picture that cannot be decoded in hardware | integration | audio-only sample → no picture; unsupported or software-only picture → not hardware-decodable with codec and profile |
| AC-02 | refusal message and exit code match the CLI contract | contract | terminal lines and exit code equal those in `contracts/cli.md` for both reasons |
| AC-02 | play command with an audio-only file ends without a window | e2e | nothing plays, no window, plain-language refusal, refusal exit code |
| AC-03 authorization — OS denies file access | start refuses a file the access check reports as denied | unit | nothing plays, Developer told the OS denied access to that file |
| AC-03 | a real file without read permission is reported as access denied, not as a damaged file | integration | temp file with read permission removed → access-denied result |
| AC-03 | access-denied message and exit code match the CLI contract | contract | terminal lines and exit code equal those in `contracts/cli.md` |
| AC-03 | play command on an unreadable file ends without a window | e2e | nothing plays, no window, access-denied message and exit code |
| AC-03b error — screen not connected | start refuses a screen number missing from the connected screens | unit | nothing plays, no window, message lists the connected screens |
| AC-03b | screen-not-found message and exit code match the CLI contract | contract | terminal lines and exit code equal those in `contracts/cli.md` |
| AC-03b | play command naming a missing screen ends without a window | e2e | no window opened, screen list printed, screen-not-found exit code |
| AC-04 happy path — pause and resume | pause stops giving sound to every output at the same master-clock instant | unit | every output's last given sample is at one master-clock instant |
| AC-04 | picture runs on until the in-device sound has played out and freezes on the frame of the last sound heard | unit | picture paused at the freeze point, no later than the picture delay after the key |
| AC-04 | resume continues picture and sound from the freeze point with nothing repeated or skipped | unit | no content position heard twice, no frame skipped, lip-synced again ≤ 1 s after the key |
| AC-04 | pause key twice in the window pauses and resumes the film | e2e-through-UI | window state paused, then playing |
| AC-05 happy path — seek | seek while playing re-aligns picture and every output at the target | unit | lip-synced again ≤ 1 s after the key |
| AC-05 | seek while paused shows the frame at the target and stays paused | unit | renderer shows the target frame, state stays paused, freeze point = target |
| AC-05 | backward seek at the end returns into the film and plays | unit | state playing from the target |
| AC-05 | seek before the start goes to the start; seek past the end ends the film as at its natural end | unit | target clamped to 0; past the end → ended state with run summary |
| AC-05 | arrow keys in the window seek by 10 s and 60 s | e2e-through-UI | session receives −10/+10/−60/+60 s seeks, position moves accordingly |
| AC-06 happy path — end and exit | natural end keeps the last frame and silences every output together | unit | ended state, every output silent at one instant, run summary emitted for every output |
| AC-06 | pause key at the end does nothing | unit | state stays ended, no command reaches the renderer or the engine |
| AC-06 | a run summary is emitted at every natural end, exit and file change covering the file since it was opened | unit | exactly one summary per end, exit or file change, counters reset per file |
| AC-06 | run summary lines match the CLI contract | contract | lines equal `contracts/cli.md` §2.1 for every output |
| AC-06 | exit key closes the window and prints the run summary | e2e | window closed, summary printed, success exit code |
| AC-06b error — damaged picture | skipped damaged frames are counted while the sound plays on | unit | sound uninterrupted, skipped-frame count in the log records |
| AC-06b | picture that cannot be decoded again ends the film as at its natural end | unit | ended state on the last good frame, note shown, plain-language reason and run summary |
| AC-06b | video renderer reports skipped frames on a deliberately damaged sample | integration | skipped-frame counter rises over the damaged stretch, playback continues after it |
| AC-06b | picture-failure terminal line matches the CLI contract | contract | line equals `contracts/cli.md` §2.3 |
| AC-07 happy path — next / previous file | folder neighbours are `.mkv`/`.mp4` in any letter case, in natural name order | unit | "Episode 2" before "Episode 10"; names equal apart from case ordered by exact comparison; other extensions ignored |
| AC-07 | file change keeps outputs, track numbers and offsets and prints the old file's summary first | unit | summary of the old file, then the new file starts on the same outputs, tracks and offsets within the start budget |
| AC-07 | the current film is untouched while the neighbour is being looked for | unit | no stop or pause reaches the renderer or engine before a playable neighbour is found |
| AC-07 | folder listing on a real directory is read again on every press | integration | a file added to the temp directory between presses is found on the next press |
| AC-07 | Page Down and Page Up in the window move to the next and previous file | e2e-through-UI | neighbouring file plays in the window |
| AC-08 domain invariant — no neighbour | no next (or previous) file leaves the film exactly as it was | unit | playing, paused or ended state unchanged; note "no next file" / "no previous file" |
| AC-09 cross-context — missing track | a neighbour lacking a track an output plays is skipped for the next playable one | unit | skipped file not played, next playable file plays, note names the file and whose track it lacks |
| AC-09 | several files skipped on one press produce one final note with the last name and the count | unit | last note names the last skipped file and how many were skipped; log names file, output and track for each |
| AC-09 | a real one-track sample is skipped when an output plays track 2 | integration | sample with one audio track is skipped in a temp folder of real samples |
| AC-09b error — unreadable / no picture / not hardware-decodable neighbour | an unreadable, picture-less or not-hardware-decodable neighbour is skipped with its reason | unit | file skipped, note gives the reason in plain words, log names file and reason |
| AC-09b | real unreadable, audio-only and not-media samples are skipped | integration | each sample in a temp folder is skipped with its own reason, the next playable sample plays |
| AC-10 happy path — offset key | an offset press moves that output by 10 ms at no more than 0.2 % speed change | unit | lag ramp ≤ 2000 ppm, about 5 s per 10 ms, target reached |
| AC-10 | other outputs stay within 10 ms of where they were against the picture during the move | unit | each unchanged output's error ≤ 10 ms throughout |
| AC-10 | further presses during a move change its target instead of queueing | unit | one ramp toward the latest target |
| AC-10 | picture delay has no lower limit and "later" presses take effect up to +500 ms | unit | picture delay negative when every assumed latency is below zero; every "later" press shifts the output |
| AC-10 | offset change while paused or ended is shown and remembered at once and applied on continue | unit | note shown, offset saved, applied in the resume or seek re-alignment |
| AC-10 | offset is saved no later than 1 s after the last press | unit | one save after the debounce on the fake clock, none before |
| AC-10 | offset survives an abandoned session and is read back by a fresh store | integration | press, advance 1 s, drop the session without shutdown, reload from the temp directory → offset present |
| AC-10 | offset keys 1/Q, 2/W, 3/E map to later/earlier commands for outputs 1–3 and show the note | component | right command per key; note text names output and new offset |
| AC-10 | pressing an offset key in the window shows the output and its new offset | e2e-through-UI | note shown for 2 s, replaced by the next note |
| AC-11 error — offset at the limit | offset at +500 ms (or −500 ms) does not move further | unit | offset stays at the limit, note says it cannot go beyond ±500 ms, nothing saved |
| AC-12 cross-context — remembered offset | a remembered offset is applied at start and the latency is worked out again | unit | output starts with the remembered offset; log shows device, offset and the freshly computed latency |
| AC-12 | a revised type default shifts devices that already have a remembered offset | unit | assumed latency = reported part + new type default − remembered offset |
| AC-12 | offsets file on disk is clamped, rounded and tolerated when unreadable | integration | 900 → 500 and 15 → 20, each logged; malformed file → every offset 0, warning, file not overwritten until an offset key is pressed |
| AC-12 | `device-offsets.json` keeps the v1 shape | contract | `schemaVersion` 1 and `offsets` map of device id → integer ms, camelCase |
| AC-13 happy path — first-use latency | device type is taken from connection kind and form factor, never from the name | unit | Bluetooth → headphones unless reported as speaker; USB → wired; HDMI/DisplayPort display → HDMI; anything else → other |
| AC-13 | a never-used device starts at reported part + type default with offset 0 | unit | assumed latency equals reported part + default from the one defaults table; reported part 0 ms when nothing is reported |
| AC-13 | start log shows reported part, type default, type taken and picture delay | unit | one latency-estimated event per output with all four values |
| AC-13 | device latency probe reports connection kind, form factor and hidden latency for real devices | integration | values returned for each connected device; hardware-gated |
| AC-14 domain invariant — picture delay moves smoothly | picture and unchanged outputs move to a new picture delay at no more than 0.2 % speed change | unit | renderer speed and every unchanged output's ratio within 2000 ppm of normal until the new delay is reached |
| AC-14 | each unchanged output stays within 10 ms of where it was against the picture throughout the move | unit | per-output error against the picture ≤ 10 ms at every fake-clock step |
| AC-14 | the corrected output reaches its new offset no later than the move ends | unit | corrected output's lag at target when the picture move completes |
| AC-14 | seek, file change or resume during a move sets the target delay and offsets at once | unit | no ramp after the command; picture delay and lags equal their targets in the re-alignment |
| AC-14 | real renderer follows the master clock within budget and moves at 0.2 % on the target PC | e2e | ADR-0002 kill criterion passes: presentation within 20 ms of due, 0.2 % move with no visible jump; hardware-gated, verdict recorded in `evidence/` |
| AC-15 happy path — one device lost | losing one output's device keeps the picture and the other output playing | unit | remaining output keeps playing, picture delay follows the remaining largest assumed latency as in AC-14, note shows the loss |
| AC-15b error — last device lost | losing the only remaining output ends the film as at its natural end | unit | picture frozen, ended state, window stays open, note shown, run summary for every output |
| AC-15b | next, previous and exit keys still work after the last output is lost | e2e-through-UI | Page Down plays the next file, Esc closes the window with a summary |
| AC-16 happy path — measured lip-sync | click-and-flash test video has the flash and the click at the same position on every track | integration | probe of the test video: one flash frame and one click per second, aligned to the same timestamp on every audio track |
| AC-16 | phone measurement of lip-sync per output on the target PC | e2e | manual, hardware: clap gives the phone's own offset; 10 flashes per output, phone offset subtracted, largest error within −125/+45 ms; recorded in `evidence/` with the logged latency, offset and picture delay |
| AC-17 happy path — lip-sync log | every change of picture delay, latency or offset and every seek, pause, resume or file change emits a lip-sync record | unit | record with master-clock position, picture delay, per output reported part, type default, offset, and picture presented-vs-due error |
| AC-17 | lip-sync log records carry the fields of the CLI contract | contract | fields and names equal `contracts/cli.md` §3 |
| AC-17 | renderer presentation timestamps are converted to the audio clock's monotonic time base | integration | presented-vs-due error of a sample playback is computed on the same time base as the audio master clock; hardware-gated |

## Edge cases / error paths

Each error and authorization AC (AC-02, AC-03, AC-03b, AC-06b, AC-09b, AC-11, AC-15b) has its own rows above. Further boundary and failure cases the spec implies:

- Start command with an unknown device id, a track number the file lacks, or more than three outputs → expected: refused before any window opens, plain-language message per `contracts/cli.md`.
- Picture probe exceeds its time limit on a hostile file → expected: treated as cannot decode, refused at start (AC-02) or skipped on next/previous (AC-09b); the player answers within 3 s and does not crash (spec §6.1).
- Renderer reports hardware decoding inactive after load → expected: handled as picture cannot be decoded further, ended as in AC-06b.
- Offsets file with an unknown future `schemaVersion` → expected: every offset 0, warning in terminal and log, file not overwritten until an offset key is pressed.
- Offsets file missing the `offsets` map → expected: same as an unreadable file.
- Offsets save fails (folder read-only) → expected: failure logged, offset kept for this run, playback unaffected.
- Offset at +490 ms pressed twice "later" → expected: first press reaches +500, second shows the limit note.
- Seek of −60 s at 30 s into the film → expected: lands at the start; +60 s with 20 s left → ended as at the natural end.
- Next file in a folder where every other file is skipped → expected: one note with the last skipped name and the count, current film unchanged (AC-08 + AC-09).
- Folder containing only the current file, or the current file deleted from the folder between presses → expected: "no next file" note, film unchanged.
- Folder with thousands of files → expected: next/previous answers within the start budget (load scenario below).
- Offset key for output 3 when only two outputs were started → expected: ignored, no note, nothing saved.
- Device lost during a picture-delay move → expected: the move retargets to the new largest assumed latency, still at ≤ 0.2 %.
- Pause pressed during a picture-delay move → expected: resume re-aligns at the target delay at once (AC-14).
- Pause key at the end, then backward seek → expected: pause ignored, seek returns into the film and plays.

## Test data

- Seed strategy: `DeviceOffsetsBuilder.With(deviceId, offsetMs)` for in-memory offsets (ids like `test-device-1`); raw JSON fixtures for the load edge cases from `data-model.md` (out-of-range `900`, off-grid `15`, `schemaVersion` `2`, malformed JSON, missing `offsets`); `EngineRig` with fake devices and fake clock; scripted `FakeVideoRenderer`, `FakeDeviceLatencyProbe` and `FakePictureProbe` builders (connection kind, form factor, reported latency; has picture, hardware-decodable, codec and profile).
- Sample media: existing test-media files plus new small generated samples — a short video with two audio tracks, a video with one audio track, an audio-only `.mkv`, a not-media file named `.mkv`, a picture in a codec the probe refuses, a deliberately damaged video stretch, and the click-and-flash test video. Each sample is a few seconds long, kept small and described in the test-media README with the command that generated it.
- Folder fixtures: built per test in a throwaway temp directory from copies or links of the samples, with names chosen for natural-order and letter-case cases (`Episode 2.mkv`, `Episode 10.MKV`, `episode 2.mkv`, `notes.txt`).
- Integration dependency: the real file system (temp directory + `JsonFileStateStore`), the real native picture probe and video renderer, real audio devices for hardware-gated tests — never a mocked store.
- Cleanup boundary: per test — each test creates and deletes its own temp directory; renderer and session instances are disposed per test; hardware-gated e2e runs write their logs to a per-run folder that is kept only as evidence.

## NFR validation (load)

All scenarios run on the target Windows 11 PC with both Bluetooth headsets plus one wired or HDMI output (spec §6 conditions), hardware-gated; results go to `docs/features/lip-synced-video/evidence/`. The load tool already in your repo, or e.g. k6 or Locust, is not used — the scenarios drive the player itself and read its log.

- Lip-sync per output (sound ≤ 45 ms ahead, ≤ 125 ms behind) → scenario: the AC-16 phone measurement, 10 flashes per output at ≥ 120 frames per second, phone offset subtracted; assert the largest error per output within −125/+45 ms.
- Start command to picture and sound ≤ 3 s → scenario: 20 consecutive starts of a 1080p HEVC sample; assert every start's first frame presented and first audio heard per output ≤ 3 s after the command, first frame within 20 ms of due and every output within 10 ms of due.
- Recovery ≤ 1 s → scenario: 10 minutes of playback with 60 scripted commands (pause, resume, seek ±10/±60 s) and 5 file changes; assert every command lip-synced again ≤ 1 s (file change ≤ 3 s), measured as picture within 20 ms of due and every output within 10 ms.
- Picture-delay change ≤ 0.2 % → scenario: 20 offset presses spread over 10 minutes, including moves that change the slowest output; assert logged playback speed within 2000 ppm and every unchanged output within 10 ms against the picture throughout.
- Picture presentation ≤ 1 late or dropped frame per minute → scenario: a 2 h 1080p H.264 film and a 2 h 1080p HEVC film, hardware decoding, two outputs; assert (late + dropped) / minutes ≤ 1 for each film, a frame being late when presented > 20 ms after due.
- CPU ≤ 15 % → scenario: the 2 h 1080p HEVC run on two outputs; assert the operating system's total-CPU counter averaged over the run ≤ 15 %.
- Folder of thousands of files (spec §6.1) → scenario: a folder of 5,000 `.mkv`/`.mp4` names, 20 next/previous presses; assert each answers within 3 s.
- Offset range and step (−500…+500 ms in 10 ms) → covered by the AC-10 and AC-11 unit rows, no load run.

## CI placement

- On every PR (both CI operating systems): unit, component, contract, the e2e and e2e-through-UI suites over fakes, and the integration tests that only read files — offsets state file, folder listing, picture probe on samples (no-picture, codec and profile cases), libmpv loading with its version check.
- Hardware-gated (`DUALPLAYER_HARDWARE=1`, run by hand on the target PC before acceptance and after a native-library update): renderer integration tests needing a window or GPU, hardware-decodable probe cases, device latency probe, the ADR-0002 spike e2e, the AC-16 phone measurement, and every load scenario.
