---
status: Draft
owner: "Volodymyr Kravchuk"
reviewers: ["Volodymyr Kravchuk", "Tech Lead"]
updated_at: "2026-11-11"
feature_size: "L"
---

# Test plan — two-outputs-one-clock

One decode fans two or more tracks out to audio outputs on different devices, all held within ≤ 10 ms of one master clock for a ≥ 3 h run; transport acts on every output at once, one lost device never disturbs the others, and per-device sync records and a run summary are published.

## Levels

| Level | Scope | Strategy (generic — no tool names) |
|---|---|---|
| Unit | Core sync and transport logic: master clock, drift controller, adaptive resampler, ring buffer, output validation, engine-control loop, excursion tracker, run summary, record publisher. | In-memory; a fake `TimeProvider` and fake audio devices whose clocks run fast or slow by a set ppm and report a playback position; no OS audio, no native library. |
| Integration | `DualPlayer.Media` against the real native decoding library; `DualPlayer.Platform.Windows` against the real OS audio subsystem and real devices. | Real dependency, never mocked: the real native decoding library loaded dynamically with small test media files copied into a per-test temp folder; Windows device tests run on real hardware and only with `DUALPLAYER_HARDWARE=1`. |
| Contract | Boundaries other participants rely on: the audio backend port (every adapter behaves as Core expects) and the per-device sync record that roadmap step 6 subscribes to. | One shared port contract suite run against every adapter (fake, Windows, macOS where the OS allows); the sync record shape asserted against its agreed field list. |
| E2E | One full Developer flow through the console's real entry point (command line + standard input) down to the audio backend port. | Real Core + real Media + real test media, audio through a fake backend in CI; a hardware-gated variant drives real devices on the target PC. |
| Load | Spec §6 numeric targets that only a long run on the target PC can prove (drift, underruns, CPU, start latency). | A soak run (a ≥ 3 h evidence run) with the console itself on the target PC (see NFR validation). |

## AC coverage

E2E rows run with a fake audio backend in CI and again, unchanged, as a hardware-gated variant on the target PC.

| AC (spec.md §5) | Test name (intent-based) | Level | Expected outcome |
|---|---|---|---|
| AC-01 play on several outputs | `two outputs start together from the beginning with their assigned tracks` | unit | Both fake devices receive frames of their own track from position 0; neither starts before the other is ready |
| AC-01 | `one track may be assigned to two outputs` | unit | Run starts; both outputs receive the same track |
| AC-01 | `play command starts every output and echoes track, device and master clock position` | e2e | Console shows one line per output with track, resolved device id and a running master-clock position |
| AC-02 list devices and tracks | `device can be named by list number or by full os id` | unit | Both forms resolve to the same device; an unknown number or id is reported, not guessed |
| AC-02 | `track list numbers audio tracks from one with language and channel layout` | integration | A test file with a video stream and three audio tracks lists exactly tracks 1–3 with their language and layout |
| AC-02 | `device list shows every playback device the os offers` | integration | On the target PC the list holds every active playback device with name, type when known and id (hardware-gated) |
| AC-02 | `devices and tracks commands print lists and play nothing` | e2e | Both lists are printed; no device is opened |
| AC-03 invalid input | `missing track number is refused and the existing tracks are named` | unit | Run refused; message names the missing track and the tracks the file has |
| AC-03 | `disconnected device is refused by name` | unit | Run refused; message names the device that is not connected |
| AC-03 | `file that is not media is refused with a plain reason` | integration | A text file, an empty file and a truncated media file are each refused as unreadable media; no crash, no hang |
| AC-03 | `invalid play command starts no output` | e2e | Console prints the invalid input and reason; no device is opened |
| AC-04 access denied by the OS | `access denied on one device closes the devices already opened` | unit | Run refused; every device opened before the denial is closed; no output plays; the denied device is named |
| AC-04 | `backend reports access denial as a result, never as an exception` | contract | Every adapter returns a denial result naming the device when the OS refuses it |
| AC-04 | `device held exclusively by another process is reported as denied` | integration | On the target PC the Windows adapter returns the denial for a device another process holds exclusively (hardware-gated) |
| AC-05 one output per device | `same device assigned twice is refused with the rule name` | unit | Run refused with "one audio output per device"; nothing opened |
| AC-05 | `play command with a reused device starts nothing` | e2e | Console prints the rule; no device is opened |
| AC-06 device lost, others keep playing | `surviving output has no underrun around another device's loss` | unit | Surviving output's underrun count does not change within ±5 s of the loss; master clock keeps running; lost output stops, is not reopened, and the loss is recorded with device and master-clock position |
| AC-06 | `each further loss with three outputs behaves the same` | unit | With three outputs, losing two in turn leaves the third playing without underruns |
| AC-06 | `backend raises device lost and ends that device's render thread` | contract | Every adapter raises one device-lost event and stops calling the render source for that device only |
| AC-06 | `unplugged headset during a run leaves the other output playing` | integration | Manual disconnect on the target PC during the evidence run; records around the event show 0 underruns on the survivor (hardware-gated, recorded in the evidence) |
| AC-06b last device lost | `losing the last output ends the run with a summary` | unit | Master clock stops; run summary is produced and lists every device-lost event of the run |
| AC-07 3 h without drift | `skewed device clocks stay within tolerance for a simulated three hours` | unit | With fake devices at +200 ppm and −200 ppm on a fake clock, every 1 s drift average outside recovery windows is ≤ 10 ms |
| AC-07 | `drift excursion is recorded once from start to return` | unit | An injected position jump produces exactly one excursion record with device, peak, duration and start position |
| AC-07 | `three hour soak on the target PC stays within tolerance` | load | See NFR validation — largest steady-state drift ≤ 10 ms, 0 underruns |
| AC-08 pause and resume | `pause silences all outputs within ten milliseconds and stops the clock` | unit | Fake-device positions at the silence instant differ by ≤ 10 ms; master clock does not advance while paused; same on stop |
| AC-08 | `resume continues every output from the paused position and recovers within one second` | unit | Each output is within ≤ 10 ms of the master clock ≤ 1 s after resume |
| AC-08 | `pause and resume commands act on every output` | e2e | Console confirms pause and resume; all outputs stop and continue together |
| AC-09 seek | `seek ten and sixty seconds both ways recovers within one second` | unit | For ±10 s and ±60 s each output is within tolerance ≤ 1 s after the command |
| AC-09 | `no old-position audio is played after the seek is confirmed` | unit | Frames from the previous epoch are discarded before confirmation; no device receives them afterwards |
| AC-09 | `seek is confirmed only after every output has discarded its queue` | unit | Confirmation is withheld while any output still holds frames from the old position |
| AC-09 | `seek command moves every output to the new position` | e2e | Console prints the confirmation and the new position; status lines continue from it |
| AC-09b seek while paused | `seek while paused moves the position and stays silent` | unit | Position moves; all outputs stay silent until resume, then continue from the new position |
| AC-10 seek outside the file | `seek before the start plays from the beginning` | unit | Position becomes 0; caller is told the target was outside the file |
| AC-10 | `seek past the end ends the run with a summary` | unit | Position jumps to the end; all outputs fall silent; run ends with its summary; caller is told the target was outside the file |
| AC-10 | `out of range seek commands are reported in the console` | e2e | Console prints the outside-the-file notice for both directions; past-the-end prints the run summary |
| AC-11 per-device records | `one record per device per second with drift, correction and underruns` | unit | Over 10 s on the fake clock each device gets 10 records tied to its OS id and the master-clock position |
| AC-11 | `sync record shape stays stable for subscribers` | contract | The record carries exactly the agreed fields (device id, master-clock position, drift, current correction in ppm, cumulative underruns); a renamed or removed field fails |
| AC-11 | `records reach the log file and survive rollover within a run` | integration | The real rolling log in a temp folder holds every record of the run; rollover with the size cap removes only files of older runs |
| AC-12 format conversion | `fold-down keeps every channel except LFE` | unit | 5.1 and 7.1 to stereo matrices give LFE weight 0 and a non-zero weight to every other channel |
| AC-12 | `five point one track plays on stereo without LFE` | integration | Real decoding of a 5.1 test file with a distinct tone per channel: the stereo output holds every tone except the LFE one |
| AC-12 | `forty-four kilohertz track plays at correct pitch on a forty-eight kilohertz device` | integration | A 1 kHz tone at 44.1 kHz comes out as 1 kHz at 48 kHz, with duration preserved |
| AC-12 | `mono track goes to both front channels with no upmix` | integration | Mono input appears on front-left and front-right only; other device channels stay silent |
| AC-13 run summary | `summary lists every output with its counters` | unit | Summary holds device, played duration, largest steady-state drift, clock mismatch in ppm, correction count, underrun count and device-lost events per output |
| AC-13 | `steady run counts zero one-step corrections` | unit | A run with only continuous rate adjustment reports 0 corrections |
| AC-13 | `summary is printed on file end, stop and Ctrl+C` | e2e | Console prints the summary and the log holds it for each of the three endings |

## Edge cases / error paths

Error and authorization ACs (AC-03, AC-04, AC-05, AC-10) each have their own rows above. Further boundary and failure cases the spec implies:

- Console window closed during a run (AC-13) → expected: the run summary is printed and logged before exit (Windows, hardware-gated e2e).
- Decoder falls behind (stall) while outputs play → expected: affected outputs count underruns and play silence; the engine keeps running and does not crash (unit).
- An output's buffer is full because its device is slow → expected: that output drops its own oldest frames; other outputs are not delayed (unit).
- A second seek arrives before the first is confirmed → expected: only the latest target wins; no audio from either earlier position plays after the final confirmation (unit).
- A transport command (pause, seek, stop) arrives while the run is still starting → expected: it is applied after start completes, in order, with no race in transport state (unit).
- A device disappears after opening but before its first frame → expected: treated as device lost (AC-06 / AC-06b), not as a start failure of the other outputs (unit).
- Log volume over a ≥ 3 h run → expected: every record of the current run fits under the size cap; nothing of the current run is removed by rollover (integration).
- Malformed or hostile media file (spec §6.1) → expected: refused with a plain reason; no crash, no hang beyond a bounded open time (integration).
- Device whose mix format has more than two channels or a high sample rate (e.g. 7.1 at 96 kHz) → expected: the track is converted to that layout and rate and still meets the drift tolerance (unit + integration).

## Test data

- Seed strategy: small generated test media committed under `tests/DualPlayer.Media.Tests/TestMedia/` — a distinct tone per channel and per track so a test can tell channels and tracks apart; variants: several audio tracks with languages plus a video stream, 5.1, 7.1, mono, 44.1 kHz, a non-media file and a truncated file. Fake audio devices are built through one test builder (clock skew in ppm, reported position, buffer size, scheduled loss or access denial); the fake clock starts at a fixed instant.
- Integration dependency: the real native decoding library and, on the target PC, the real OS audio subsystem and devices — never a mocked decoder or device in an integration test. Each test copies the media it needs into its own temp folder and points the log and app-data folder there.
- Cleanup boundary: per-test — the temp folder (media copies, logs, app data) is deleted after each test, every opened device and run is closed in teardown, so tests run independently and in any order. Hardware tests also release every device so the next hardware test can open it.

## NFR validation (load)

The load level here is a soak run with the console on the target Windows PC (spec §6, measured in shared mode). Tool: the console and its per-device records already in the repo; a generic load tool (e.g. k6 or Locust) does not apply.

- Steady-state drift ≤ 10 ms → scenario: two Bluetooth headsets (plus one wired output if available, spec §8) play two tracks for ≥ 3 h with no Developer action; assert the largest 1 s drift average outside the 1 s recovery windows ≤ 10 ms per device.
- Recovery ≤ 1 s → scenario: during the same run, 5 pauses/resumes and 5 seeks of ±10 s and ±60 s; assert every output within 10 ms ≤ 1 s after each command.
- Underruns 0 per 3 h → scenario: the same ≥ 3 h run; assert the run summary's underrun count = 0 for every output.
- Start to audio ≤ 2 s → scenario: 10 starts; assert log time from the play command to each device's first rendered frame ≤ 2 s every time.
- Survivor underruns 0 within ±5 s of a loss → scenario: one manual headset disconnect during the run; assert 0 new underruns on the other output within ±5 s of the device-lost event.
- CPU ≤ 5 % → scenario: the same ≥ 3 h run; assert the OS performance counter for the console process, averaged over the run, ≤ 5 % of total CPU.
- ≥ 3 outputs within tolerance → covered by the unit test with three fake devices (`each further loss with three outputs behaves the same` and a three-output variant of the simulated three-hour run); no third physical device is required.

The run summary of this soak is committed under `docs/features/two-outputs-one-clock/evidence/` (T22) and closes AC-07 and AC-13 on evidence.

## CI placement

- On every PR (macOS + Windows): unit, contract (against the fake adapter, plus the macOS adapter where the runner allows), Media integration with the real decoding library, and e2e with the fake audio backend.
- Hardware-gated (`DUALPLAYER_HARDWARE=1`, run by hand on the target PC before merging changes to `Platform.Windows` or the engine): Windows integration, the contract suite against the Windows adapter, and the hardware variant of each e2e row.
- Pre-release, once per step: the ≥ 3 h soak run; its summary is committed as evidence.
