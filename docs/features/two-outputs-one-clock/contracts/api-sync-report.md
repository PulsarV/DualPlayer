---
status: Draft
updated_at: "2026-11-11"
feature_size: "L"
---

# API sync report — two-outputs-one-clock

- **Contract form:** `contracts/cli.md` — `sad.md` frontmatter `target_surfaces: [cli]` (read, not re-derived).
- **Schema source:** `data-model.md` absent — legal fast-lane skip: no schema change. The feature persists nothing but log records (sad §6 persist notes, §8); `IStateStore` / state files are untouched (`EngineOptions` is not persisted, sad §8). Field origins therefore point to `spec.md`, `sad.md` and ADRs, not to a schema.
- **Inputs found:** `spec.md` ✓, `sad.md` §6 flows 1–9 ✓, ADR-0001…0007 ✓, `CONTEXT.md` ✓.
- **events.md:** not produced — no async transport (no message bus, webhook or queue). The in-process engine-event stream that roadmap step 6 subscribes to is defined by ADR-0007 and `Core/Diagnostics/`, not by this CLI contract.

## A. Field origins

| schema_path | origin | confidence |
|---|---|---|
| devices.# | spec AC-02 (number in the list) | high |
| devices.name | spec AC-02 | high |
| devices.type | spec AC-02 ("device type when known") | high |
| devices.id | spec AC-02; CONTEXT `Device` (OS endpoint id) | high |
| tracks.# | spec AC-02 (numbered from 1 among audio tracks) | high |
| tracks.language | spec AC-02 | high |
| tracks.layout | spec AC-02 | high |
| tracks.rate | spec AC-12 (sample rate may differ from the device) | medium |
| play.file | spec AC-01, AC-03 | high |
| play.--out track | spec AC-01, AC-02 | high |
| play.--out device | spec AC-02 (number or full id), AC-05 | high |
| play.--ring-ms | sad §7 `EngineOptions` (2 s) | high |
| play.--start-level-ms | sad §7 `EngineOptions` (0.5 s) | high |
| play.--device-buffer-ms | sad §7 `EngineOptions` (40 ms) | high |
| play.start line resolved id | spec AC-02 (echo resolved id) | high |
| play.start line clock | spec AC-01 | high |
| play.start line start-to-audio | spec §6 (≤ 2 s, log timestamps) | medium |
| pause / resume | spec AC-08, AC-09b; sad flow 5 | high |
| seek ±seconds / hh:mm:ss | spec AC-09, AC-10; sad flows 2, 6 | high |
| stop | spec AC-13; sad flow 9 | high |
| record.clock / device / drift_ms / correction_ppm / underruns | spec AC-11; sad flow 7; ADR-0007 | high |
| excursion.device / start / duration_ms / peak_ms | spec AC-07; sad flow 7 | high |
| device-lost.output / device / clock | spec AC-06, AC-06b; sad flow 3 | high |
| summary.device / played / max_drift_ms / clock_mismatch_ppm / corrections / underruns / device_lost | spec AC-13; CONTEXT `Run summary`; sad flow 9 | high |
| summary.ended | sad flows 3, 6, 9 (end reasons) | medium |
| exit codes 0–4 | spec AC-03…AC-06b, AC-13; CLAUDE.md §Errors | medium |

## B. Drift checklist

1. **Command ↔ model** *(core)* — ✓ Legal skip: every command maps to a §4 user story (devices/tracks → US-02, play → US-01/US-07, pause/resume/seek → US-04, stop/summary → US-08, records → US-03/US-06, device-lost → US-05); the only persisted items are log records, matching the sad §6 persist notes.
2. **Error code ↔ repo** *(core)* — ✓ with note: no error registry found — `src/DualPlayer.Core/Result.cs` defines only `Error(Code, Message)`. The eight codes in cli.md §4 are the contract's proposal in the neutral `module.error_name` form; reconcile when Core defines them.
3. **Validation ↔ constraint** *(core)* — ✓ No schema constraints apply; argument constraints come from spec (track from 1, device once, AC-05) and sad §7 defaults.
4. **Contract ↔ sequence** *(supporting)* — ✓ Every `alt`/`else`/`opt` branch of flows 1–9 has a printed outcome or exit code: invalid input / access denied (flow 1), seek confirm gate (flow 2), others continue / last output (flow 3), file unreadable (flow 4), seek while paused (flow 5), before start / past end (flow 6), underrun / excursion / one-step correction (flow 7), fold-down / fewer channels / match (flow 8, shown in the start line), file end / stop / interrupt (flow 9).

**Back-feed (coverage):** every §5 AC (AC-01…AC-13, incl. AC-06b, AC-09b) maps to ≥ 1 command or output line; every command maps to a §4 user story. No sequence gaps.

**Fixed during derivation:** AC-02 lets the Developer name a device by its list number, but `devices` and `play` are separate invocations; the contract now sorts devices by OS id so numbers stay stable while the connected set is unchanged, and `play` echoes the resolved id.

**Follow-up notes (supporting, not blocking):**
- sad §8 lists "tolerance" inside `EngineOptions`; the contract does not expose it as a flag, because the 10 ms tolerance is a spec §6 target, not a tuning knob. Either drop it from `EngineOptions` or keep it internal — settle in `tasks`.
- `spectral lint` does not apply (no OpenAPI document).
