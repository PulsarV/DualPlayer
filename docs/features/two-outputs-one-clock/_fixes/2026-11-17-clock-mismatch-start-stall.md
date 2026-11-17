---
slug: two-outputs-one-clock
date: 2026-11-17
triage: regression
acs: [AC-13]
commit: bed6724
recurrence_of: none
---

# Fix: the run summary's clock mismatch reports the Bluetooth start-up stall instead of the clock rate

## Symptom

On the target PC (Windows 11, `TV`), a 3-minute run on two Bluetooth headsets: the continuous correction stayed at ±1–2 ppm, so the device clocks match the master clock to about 1 ppm. The expected `clock_mismatch_ppm` was a few ppm. The summary reported −380 ppm (JBL Tune 520BT) and −4904 ppm (MOTIF II A.N.C.). The value shrinks as 1/duration: MOTIF showed −41 149 ppm on a 7 s run and −223 206 ppm on a 4 s run. It hits every Bluetooth output, on every run since the estimator was added (T-series of `two-outputs-one-clock`).

## Root cause

`ClockMismatchEstimator` (`src/DualPlayer.Core/Sync/ClockMismatchEstimator.cs`) fits a two-point rate from the first device-position reading to the last one. The first reading is taken right after the device starts. A position probe on the target PC showed that a Bluetooth device holds its position at 0 for a while after start (MOTIF ~980 ms, JBL ~600 ms, wired Realtek ~11 ms), then advances at the true rate (`t − pos` constant to 0.1 ms). The stall was counted as lost device time, giving −stall / duration. Existing tests fed positions that move from the first reading, so the stall was never modelled. A device that never moved produced −1 000 000 ppm.

## The pinning test

`ClockMismatchEstimatorTests.A_device_whose_position_stalls_at_start_reports_its_true_rate_not_the_stall` (unit, theory with 0 / +200 / −150 ppm, 980 ms stall, 3 min run) and `A_device_that_never_moves_reports_no_estimate`. RED run before the fix:

```
estimator.EstimatedPpm should be within 1d of 0d but was -5422,704240376941d
estimator.EstimatedPpm should be 0d but was -1000000d
```

The fix anchors the estimate at the first reading after the device position has moved off its initial value. The tests pass, and the existing estimator, sync-record, long-run and summary tests stay green.

## Spec patch

None. The spec was right: AC-13 defines the clock mismatch as "the device clock's rate relative to the master clock", and the code violated it. AC-13 was re-verified by the pinning test.

## Follow-ups

- Re-check on the target PC: a short run should now report single-digit ppm for both headsets.
- Bluetooth start-up stall (0.6–1 s) is a measured fact that roadmap step 4 (latency compensation) should know about; record it with the T22 evidence.
