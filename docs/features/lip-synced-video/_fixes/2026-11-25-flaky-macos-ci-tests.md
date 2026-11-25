---
slug: lip-synced-video
date: 2026-11-25
triage: regression
acs: [AC-02, AC-09b]
commit: 59454a3
recurrence_of: none
---

# Fix: two tests fail now and then on the macOS CI runner

## Symptom

On `macos-latest`, expected the suite green as on the dev Mac; got, on some runs only:

- `FfmpegPictureProbeTests.Probe_returns_within_the_time_limit_with_probe_timeout_when_the_first_picture_is_not_in_time` — expected `video.probe_timeout`, got `video.not_hardware_decodable` (PR #6, run 45507690489 attempt 1; green on the rerun).
- `AudioEngineRecoveryTests.After_an_output_that_hung_for_three_seconds_recovers_without_a_seek_it_is_aligned_once_with_no_underruns` — dev-2 had 1 underrun after recovery, expected 0 (same run with `ppm: 0`; master run 45511307291 after the PR #6 merge with `ppm: 300`).

Neither reproduces on the dev Mac: the recovery test passed 12 times in isolation, under `taskpolicy -b` and under a `yes` load on every core, and in three full Core runs under that load.

## Root cause

**Probe test (race in the test).** A 1-tick limit is `Task.Wait(0)`: the probe answers whatever the worker has by then. A runner VM can deschedule the calling thread long enough for the worker to finish, and with no hardware decoding there the worker fails fast with `not_hardware_decodable` (`FfmpegPictureProbe.cs:175`, reached only after `AfterFormatRead`). On the dev Mac hardware decoding is slow enough that the worker never wins.

**Recovery test — not found.** It could not be reproduced, so no fix is guessed at.

## The pinning test

- Probe: the test itself is made deterministic — `AfterFormatRead` holds the worker until the probe has answered, so no verdict on the picture can come first. No RED could be produced on the dev Mac (the race needs a machine without hardware decoding); the evidence is the CI failure above.
- Recovery: no fix. The assertion now prints when dev-2 recovered, its underruns before that, and every dev-2 sync record (clock, underruns, drift, correction), so the next CI failure shows whether the underrun came at the recovery or later.

## Spec patch

None — AC-02 / AC-09b (probe verdicts) and step 3's recovery behaviour are unchanged; only tests changed.

## Follow-ups

- Recovery test: read the timeline from the next macOS CI failure and fix from there.
- Found while trying to reproduce, never seen on CI: under a `yes` load on every core, `PlaybackSessionTransportTests.Resume_before_the_picture_has_frozen_still_realigns_picture_with_sound_AC_04` and `PlaybackSessionPendingAlignTests.Resume_before_the_picture_froze_…(stepsBeforeResume: 3, 15)` find the picture already frozen right after `Pause` ("the picture should still be running up to the freeze point"). Likely cause: the tail loop of `EngineRig.Call` advances fake time 10 ms per 1 ms real sleep until the command thread returns, so a descheduled command thread lets fake time run past the freeze point. A fix touches the rig's time gate for every session test — a task of its own.
- `PlaybackSessionBoundEdgeTests.A_seek_whose_output_qualifies_at_0_9_s_is_lip_synced_again_AC_17` failed once under the same load; probably the same cause.
