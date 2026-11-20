---
status: Accepted
owner: "Volodymyr Kravchuk"
reviewers: []
updated_at: "2026-11-20"
feature_size: "L"
ticket: ""
---

# 0006 — Keep device offsets in their own versioned state file

- **Status:** Accepted
- **Date:** 2026-11-20
- **Deciders:** Volodymyr Kravchuk

## Context

A Viewer's offset is remembered per device id and must be saved no later than 1 s after the last offset key press, so that a crash of the player or the PC does not lose it (AC-10). The next start on the same device uses it (AC-12). State is kept as versioned JSON files through `IStateStore` ([ADR-0004](../../../adr/0004-store-state-as-versioned-json-files.md)). Today there is one file, `settings.json`, with an empty `AppSettings` record at version 1. Roadmap steps 5 (device choice, volume) and 8 (UI settings) run in the same wave or later and will add fields to settings as well.

## Decision drivers

- Spec AC-10, AC-12: saved ≤ 1 s after the last press; only the offset is remembered.
- Spec §6.1 abuse case: a hand-edited file with out-of-range values is clamped to ±500 ms and logged.
- `CLAUDE.md` Persistence + Migrations: atomic writes, `schemaVersion`, `IStateUpgrader` on every bump.
- §2 constraint: parallel wave 4 ∥ 5 ∥ 6 without file conflicts.

## Considered options

1. **Own file `device-offsets.json`** — `{ "schemaVersion": 1, "offsets": { "<device id>": <milliseconds> } }`, its own `StateFileDefinition` and source-generated JSON context in `Core/Settings/`.
2. **A field in `settings.json`** — `AppSettings` gains the offsets map; `CurrentVersion` 1 → 2 with a `V1ToV2` upgrader.

## Decision outcome

**Chosen:** Option 1. Offsets are written often (up to once a second while a Viewer adjusts). A file of their own keeps those writes away from the rest of the settings. It avoids a version-number race with steps 5 and 8 on `settings.json`, and a damaged offsets file cannot cost any other setting.

## Consequences

**Positive**
- Frequent atomic writes touch only offsets; a corrupt or too-new offsets file falls back to "no remembered offset" and is logged, while every other setting survives.
- Steps 5 and 8 bump `settings.json` independently.

**Negative**
- One more state file and one more upgrader chain to maintain.
- Reading "everything about a device" (step 5's picker, step 12's calibration) means reading two files.

**Neutral**
- Values are clamped to ±500 ms and rounded to the 10 ms step on load (spec §6.1); a clamp is logged with the device id.
- Device ids may name hardware shared by two physical devices (spec §8); the offset belongs to the id.

## Links

- Spec: [[../spec.md]] AC-10, AC-11, AC-12, §6.1, §8
- SAD: [[../sad.md]] §5, §8
- Related ADR: [[0004-estimate-output-latency-as-the-hidden-latency-beyond-the-device-position]]; project ADR-0004
