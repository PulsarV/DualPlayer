---
slug: two-outputs-one-clock
date: 2026-11-19
triage: gap
acs: [AC-07]
commit: 225de25
recurrence_of: _fixes/2026-11-19-bluetooth-drift-step-diagnostics.md
---

# Fix: after a drift step an output stays out of sync until the end of the 1 s record interval

## Symptom

Playing on Bluetooth devices on the target PC, a device stops taking audio for 10–180 ms and its drift steps by that much at once; expected the output back in sync quickly, got it out of sync until the record interval closed, up to 1 s later (about 2 s when the step came just before a close).

Scope: the target PC with a third Bluetooth device on the adapter or devices behind a wall: 129 steps on the speaker and 1 on the MOTIF II in a 30 min run ([evidence](../evidence/2026-11-19-target-pc-drift-trace.md)). Two headsets in the room with the PC showed no step in the 3 h run of 2026-11-18.

## Root cause

The step itself comes from the Bluetooth output, not from the engine: the device position advances less than the elapsed time in one 100 ms sample while the ring holds 1.5 s or more and no underrun is counted. It cannot be removed in code.

What the engine controls is how long the error stays. `OutputSync.CloseInterval` decided the one-step re-alignment only once per second, on the interval's average, and the clamped rate correction (±500 ppm) removes about 0.5 ms per second. Spec §6 had a recovery target only for start, resume and seek, so nothing required a faster reaction and no test covered it.

The fix: `OutputSync` counts consecutive rate-corrected readings beyond the re-alignment threshold on the same side; after three (`OutputSync.StepReadings`, 300 ms at one reading per 100 ms) `AudioEngine.RealignStep` removes the latest drift in one step, counts a correction and logs it. Recovery windows and a pending earlier re-alignment still block it. The once-per-second decision stays as a fallback.

## The pinning test

`AudioEngineSyncRecordTests.A_step_error_is_re_aligned_within_half_a_second_without_waiting_for_the_end_of_the_record_interval` — engine level, fake clock and fake devices: a 60 ms step injected at master clock 00:00:04.1.

RED run before the change:

```
    should be less than or equal to
00:00:04.6000000
    but was
00:00:05
Additional Info:
    spec §6: back within the tolerance 0.5 s after the step
```

The diagnostics test of the previous record now expects the step re-alignment line (`Readings` instead of `AverageDriftMs`).

## Spec patch

Gap, confirmed by the owner on 2026-11-19. Spec §6:

- added after the first sentence: "The targets are guaranteed for two Bluetooth headsets in the same room as the PC, in line of sight. A third Bluetooth device on the same adapter, or devices behind a wall, are outside the guaranteed conditions: there a Bluetooth output can stop taking audio for 10–180 ms (evidence 2026-11-19); the engine re-aligns it in one step and counts a correction (AC-13)."
- new table row: "Recovery after a drift step of a device (its position falls behind or ahead by more than the tolerance at once) | the output is back within the drift tolerance ≤ 0.5 s after the step | unit tests with a fake clock and fake devices; re-alignment lines in the log".

Spec §8: the drift-step question is marked resolved. `sad.md` §6 flow 7 shows the 100 ms step check.

## Follow-ups

- Not yet measured on the target PC: run three Bluetooth devices behind the wall with this build and compare how long a step stays in the `drift-trace` lines (expected about 0.3 s instead of about 1 s).
- The correction reaches the listener only after the audio already queued in the device has played (0.05–0.45 s on these devices); the 0.5 s target is measured on the engine's drift readings, like every other drift figure.
- A 1 s record averages its ten readings, so a step removed after three readings can leave the record within 10 ms and open no excursion; the correction count and the re-alignment log lines still show it.
- What the listener hears at the instant of a step (a pause or smoothed audio) is not visible from the PC.
