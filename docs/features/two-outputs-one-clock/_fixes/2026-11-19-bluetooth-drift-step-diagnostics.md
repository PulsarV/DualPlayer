---
slug: two-outputs-one-clock
date: 2026-11-19
triage: regression
acs: [AC-07]
commit: fd5cfbb
recurrence_of: none
---

# Fix: a Bluetooth output's drift steps past the tolerance and the log does not show why (diagnostics step)

## Symptom

Playing on Bluetooth devices on the target PC, expected every output to stay within the 10 ms drift tolerance (AC-07, spec §6), got a drift step of 12–154 ms within one 1 s record with 0 underruns; a one-step re-alignment removes it in the next second.

Scope: the target PC, since the first evidence run of 2026-11-17. Rare with two headsets (twice in one night, both behind a wall), frequent with three Bluetooth devices, clearly worse through a wall (8 excursions in 2 min behind the wall against 2 in line of sight, [evidence 2026-11-19](../evidence/2026-11-19-target-pc-short-checks.md)).

## Root cause

Not established. The log held only the 1 s average drift per device, so it could not tell whether the device really delayed the sound or only its position report stepped, nor whether several devices stepped at the same instant. One-step re-alignments were counted in the run summary but not logged one by one.

This change does not remove the steps. It adds the data needed to find the cause, as the owner decided on 2026-11-18 (spec §8):

- `AudioEngine.PublishSyncRecords` logs every one-step re-alignment with the device, the master-clock position, the drift removed and the interval's average drift. The alignment steps after a start, seek or resume are logged as well.
- `DriftTrace` (new, `Core/Diagnostics`) keeps the last 20 sync samples of every output. When a record interval has an output beyond the tolerance outside a recovery window, the engine logs the samples of every output for that interval, the one before it and the one after it as `drift-trace` lines: master-clock position, age of the device's latest position report, raw played frames, audio queued in the device, drift, ring fill, rate correction and underruns.

## The pinning test

`AudioEngineSyncRecordTests.An_injected_step_error_logs_the_one_step_re_alignment_and_the_raw_readings_of_every_output_around_it` — engine level, fake clock and fake devices.

RED run before the change:

```
Shouldly.ShouldAssertException : rig.Log.With("DeviceId", "ClockPosition", "RealignMs", "AverageDriftMs")
    should have single item but had
Additional Info:
    one-step re-alignments in the log
```

## Spec patch

None to an acceptance criterion: AC-07 is right and stays violated on the target PC in the conditions above. The open question in spec §8 stays open; its note now says the diagnostics exist and data collection is next.

## Follow-ups

- Collect `drift-trace` data on the target PC with three Bluetooth devices behind the wall and in line of sight, then decide between an engine fix and a placement requirement (spec §8).
- A device that stays beyond the tolerance for long (a hung output) now adds 10 log lines per second and output for as long as it lasts; cap it if a long run shows the log growing too much.
- `contracts/cli.md` and `sad.md` do not list log-only lines; add the two new ones there if a later step starts to depend on them.
- `AudioEngineRecoveryTests…recovers_without_a_seek…(ppm: 0)` failed once on the macOS CI runner on 2026-11-19 (29.6 ms against 10 ms) and passed on the rerun; not touched here.
