---
id: T22
title: "Record the ≥ 3 h evidence run on the target Windows PC and commit its run summary"
layer: "docs"
deps: ["T15", "T17", "T20", "T21"]
blocks: []
acs: ["AC-07", "AC-13"]
files_hint: ["docs/features/two-outputs-one-clock/evidence/"]
owner: "Volodymyr Kravchuk"
estimate: "M"
context_budget: "M"
status: "blocked"
step: "manual — needs the ≥ 3 h run on the target Windows PC"
---

<!-- Self-contained task: work from what is inlined here. Every quoted chunk ends with its provenance
signature (`<file> §<section>, <identifier>, verbatim|abridged`); the source always wins. If a slice is
insufficient, ambiguous, or contradicts the code in front of you, open the named file for the full
text and follow that. Do not invent the missing part. -->

# T22 — Record the ≥ 3 h evidence run on the target Windows PC and commit its run summary

## Place in the sequence

- **Blocked by:** T15 — Convert each output's track to its device mix format with swresample and a fold-down matrix, T17 — Render each Windows device on its own MMCSS thread with the device position and the loss signal, T20 — Console: pause, resume, seek and stop a running run from standard input, T21 — Console: print sync records, excursions, device loss and the run summary, also on Ctrl+C and window close
- **Blocks:** nothing (final task)
- **Wave:** 7 — after its deepest dependency finishes (wave 6).
- **Lane:** own lane.

## Why (user story)

> **As a** Developer
> **I want** a per-output summary of the run when playback stops
> **So that** I can record the target-PC measurements in the repository and close this step on evidence
>
> — `spec.md §4, US-08, verbatim` · full text: [spec.md](../spec.md)

> **As a** Viewer
> **I want** to hear my track without drift building up over a feature-length film
> **So that** my sound never wanders away from the shared playback timeline
>
> — `spec.md §4, US-03, verbatim` · full text: [spec.md](../spec.md)

This task closes the step on evidence from the real hardware, not on fakes.

## Inlined context

> "Done" is only ever exercised on the dev Mac or with fake devices; the Mac backend does not validate Windows timing (repo ADR-0002) — Severity High — Mitigation: Acceptance requires the recorded ≥ 3 h evidence run on the target PC with its run summary committed under `docs/` (spec §7)
>
> — `sad.md §11, Risks row 1, abridged` · full text: [sad.md](../sad.md)

> - **Target-PC evidence runs recorded in the repository** — baseline: 0, target: ≥ 1 run of ≥ 3 h with both Bluetooth headsets, its run summary (AC-13) committed under `docs/` before roadmap step 4 is specified.
> - **Largest steady-state drift measured on the target PC** — baseline: unknown (never measured; measurement plan = the evidence run above), target: ≤ 10 ms in the recorded run.
> - **Underruns per 3 h run on the target PC** — baseline: unknown (crackling heard on 2026-11-08, cause not localized; measurement plan = the evidence run above), target: 0.
> - **Device clock mismatch recorded per device** — baseline: none recorded, target: a parts-per-million figure recorded for both headsets in the evidence run, available to roadmap step 4.
>
> — `spec.md §7, KPIs 1–4, verbatim` · full text: [spec.md](../spec.md)

> | Underruns on the target PC | 0 per 3 h run with two Bluetooth headsets | run summary (AC-13) |
> | Start command to audio on all outputs | ≤ 2 s | timestamps in the log |
> | CPU while playing two tracks | ≤ 5 % of total CPU on the target PC | operating-system performance counter, average over the run |
>
> — `spec.md §6, NFR rows 3, 4, 6, verbatim` · full text: [spec.md](../spec.md)

> Which devices join the evidence run besides the two Bluetooth headsets? Default now: add one wired output if available. — owner: Volodymyr Kravchuk, due: before `/sdd:tasks`
>
> — `spec.md §8, open question 4, verbatim` · full text: [spec.md](../spec.md)

> If roadmap step 1 finds the crackling comes from the Bluetooth adapter or radio interference, does "0 underruns" still define this step as done? Default now: yes — the underrun counter measures the engine only.
>
> — `spec.md §8, open question 2, abridged` · full text: [spec.md](../spec.md)

**Fallback:** insufficient or contradicted by the code → read the named file in full ([spec.md](../spec.md) · [sad.md](../sad.md) · [cli.md](../contracts/cli.md) · [adr/](../adr/) · [CONTEXT.md](../../../../CONTEXT.md)) and follow it. Do not guess.

## Data delta

No DB changes.

## API contract

> `ended` is one of `file-end`, `stop`, `interrupt` (Ctrl+C or window closed), `seek-past-end`, `all-devices-lost`. `max_drift_ms` is the largest steady-state drift outside the recovery windows; `corrections` counts one-step re-alignments only.
> Record fields: `clock` (master-clock position), `device` (OS endpoint id), `drift_ms` (average drift over the record's 1 s interval), `correction_ppm` (current continuous rate adjustment), `underruns` (cumulative underruns of this device in the run). Excursion fields: `device`, `start`, `duration_ms`, `peak_ms`.
>
> — `contracts/cli.md §2.1, §2.2, §2.4, abridged` · full text: [cli.md](../contracts/cli.md)

## Acceptance criteria

### AC-07 — happy path (long run)

> **Given** two audio outputs play on devices whose clocks run at slightly different speeds
> **When** playback runs uninterrupted for at least 3 hours with no action from the Developer
> **Then** each output stays within the steady-state drift tolerance of the master clock (§6), and every excursion beyond that tolerance is recorded once — from the moment it starts until the output is back within tolerance — with its device, peak size, duration and the master-clock position where it started
>
> — `spec.md §5, AC-07, verbatim` · full text: [spec.md](../spec.md)

### AC-13 — happy path

> **Given** a run has been playing
> **When** playback stops because the file ended, the Developer gave the stop command, pressed Ctrl+C, or closed the console window (a crash or a killed process need not produce a summary; the AC-11 records up to that point stay in the log)
> **Then** the console prints and the log records, for every audio output: its device, played duration, largest steady-state drift, estimated device clock mismatch in parts per million, number of corrections, number of underruns and any device-lost events. The clock mismatch is the device clock's rate relative to the master clock; a correction (CONTEXT) is counted only when the output's position is re-aligned in one step, so a steady run is expected to count 0, while the continuous rate adjustment is what AC-11 reports as current correction, in parts per million
>
> — `spec.md §5, AC-13, verbatim` · full text: [spec.md](../spec.md)

## Checklist

- [ ] Build `win-x64`, run `build/fetch-ffmpeg.ps1`; on the target PC run `devices`, then `play` a feature-length file with both Bluetooth headsets (plus one wired output if available, spec §8 default) for ≥ 3 h with no action
- [ ] During the run: one manual disconnect of a third/spare output or a separate short run for AC-06; a pause/resume and ±60 s seek in a separate short run so the long run stays untouched
- [ ] Collect from the log: run summary, largest steady-state drift, underruns, clock mismatch ppm per headset, start-to-audio, CPU average from the OS performance counter
- Start-to-audio evidence is the `started` line value and the per-device "First audio on ..." log lines, both measured to the first audio actually rendered.
- [ ] Write `docs/features/two-outputs-one-clock/evidence/<date>-target-pc-run.md` with the summary table, the measurements vs spec §6 targets, and the hardware list; review device names before committing (spec §6.1)

## Edge cases

| Case | Behaviour |
|---|---|
| A target is missed (drift > 10 ms, underruns > 0, CPU > 5 %) | record the numbers as they are; the step is not accepted — open a `/sdd:fix` instead of editing the evidence |
| Crackling heard but 0 underruns | note it; per spec §8 default the counter measures the engine only |
| Device names contain a person's name | redact or keep consciously before committing |

## Definition of Done

- [ ] the evidence file is committed under `docs/` with a ≥ 3 h run summary for both headsets
- [ ] every spec §6 target is listed with its measured value and pass/fail
- [ ] every Hard Rule inlined above still holds
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
