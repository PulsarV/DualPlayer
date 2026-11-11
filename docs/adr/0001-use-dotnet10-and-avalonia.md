---
status: Accepted
owner: "Architect (project owner)"
reviewers: []
updated_at: "2026-11-11"
feature_size: "XL"
ticket: ""
---

# 0001 — Use C# on .NET 10 LTS with Avalonia and MVVM Toolkit

- **Status:** Accepted
- **Date:** 2026-11-11
- **Deciders:** project owner (survey foundation session)

## Context

The brief fixes C# + Avalonia for a Windows 11 player developed on macOS, naming .NET 8. .NET 8 support ended on 2026-11-10, so a new project would start unsupported.

## Decision drivers

- Target Windows 11 x64, development on macOS (`docs/idea-brief.md §1`, Constraints).
- Single self-contained exe delivery (`docs/idea-brief.md §1`, 8. Architecture and stack).
- A supported runtime for a project meant to be published later (`docs/idea-brief.md §3`).

## Considered options

1. **.NET 10 LTS + Avalonia + CommunityToolkit.Mvvm** — current LTS, simplest MVVM.
2. **.NET 8 + Avalonia** — as in the brief; out of support.
3. **.NET 10 + Avalonia + ReactiveUI** — reactive MVVM, steeper learning curve.

## Decision outcome

**Chosen:** option 1. It keeps the brief's cross-platform UI choice on a supported runtime, and the MVVM Toolkit keeps ViewModels plain and testable.

## Consequences

**Positive**
- Supported runtime; NAudio, FFmpeg.AutoGen and Avalonia all run on it.
- ViewModels testable without a window.

**Negative**
- Diverges from the brief's stated .NET 8.

**Neutral**
- Moving to the next LTS later is a TFM bump.

## Links

- `docs/idea-brief.md` §1, §7 · `docs/architecture-map.md` §Stack
