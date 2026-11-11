---
status: Accepted
owner: "Architect (project owner)"
reviewers: []
updated_at: "2026-11-11"
feature_size: "XL"
ticket: ""
---

# 0004 — Store app state as versioned JSON files with in-code upgraders

- **Status:** Accepted
- **Date:** 2026-11-11
- **Deciders:** project owner (survey foundation session)

## Context

The player persists settings, per-device delays, per-file positions and tracks, and the last playlist (`docs/idea-brief.md §1`: AUD-05, PLY-03, PL-05, SettingsStore). It must resume after a crash (`docs/idea-brief.md §1`, 9. Non-functional).

## Decision drivers

- Small, per-user state; no queries across records.
- Crash-safe writes (resume after crash).
- No extra native dependency.

## Considered options

1. **JSON files with `schemaVersion` + in-code upgraders, atomic temp+rename writes.**
2. **SQLite with real migrations.**

## Decision outcome

**Chosen:** option 1. The state is small and document-shaped; System.Text.Json is built in, and atomic rename covers crash safety.

## Consequences

**Positive**
- Zero dependencies; human-readable files help diagnostics.

**Negative**
- Per-file history for very large libraries rewrites a whole file on each save.

**Neutral**
- Moving to SQLite later means one importer from the JSON files.

## Links

- `docs/architecture-map.md` §Datastores, §Conventions
