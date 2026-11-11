---
status: Accepted
owner: "Architect (project owner)"
reviewers: []
updated_at: "2026-11-11"
feature_size: "XL"
ticket: ""
---

# 0003 — Organize code as an OS-free Core with Media, Platform and App adapter projects

- **Status:** Accepted
- **Date:** 2026-11-11
- **Deciders:** project owner (survey foundation session)

## Context

The brief asks that UI and engine be separable and that the number of outputs be a list (`docs/idea-brief.md §1`, 8. Architecture and stack). The engine mixes pure logic (clocks, drift correction, playlists) with native code (WASAPI, FFmpeg, libmpv).

## Decision drivers

- Engine replaceable independently of the UI (`docs/idea-brief.md §1`).
- Test sync logic on macOS and in CI without audio hardware.
- Parallel roadmap lanes need disjoint code zones (`docs/roadmap.md` Execution path).

## Considered options

1. **Ports and adapters: `DualPlayer.Core` + `.Media` + `.Platform.Windows` + `.Platform.MacOS` + `.App`** — boundaries enforced by project references.
2. **One project with folders** — boundaries kept by discipline only.

## Decision outcome

**Chosen:** option 1. Project references make the "Core knows no OS/UI" rule a compile error rather than a review comment, and give roadmap waves separate zones.

## Consequences

**Positive**
- Core tests run anywhere; adapters swap independently.
- Disjoint folders for parallel worktree lanes.

**Negative**
- More projects and DI wiring up front.

**Neutral**
- Merging projects later is cheap; splitting a monolith later is not.

## Links

- `docs/architecture-map.md` §Module inventory, §Conventions
