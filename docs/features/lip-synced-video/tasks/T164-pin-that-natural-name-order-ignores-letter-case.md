---
id: T164
title: "Pin that natural name order ignores letter case"
layer: "test"
deps: []
blocks: ["T23", "T168"]
acs: ["AC-07", "AC-08"]
files_hint: ["tests/DualPlayer.Core.Tests/Playback/FolderNeighboursTests.cs"]
status: "todo"
step: "—"
owner: "Volodymyr Kravchuk"
estimate: "XS"
context_budget: "S"
origin: "review-2026-11-23j (AB3)"
---

<!-- Follow-up task from review-2026-11-23j. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-23j.md) · repo `CLAUDE.md`. Do not guess. -->

# T164 — Pin that natural name order ignores letter case

## Why

- **AB3.** A case-sensitive comparison at `src/DualPlayer.Core/Playback/FolderNeighbours.cs:89` passes Core 645/645: the only case test (`FolderNeighboursTests.cs:59-73`) uses names whose case-sensitive and -insensitive orders agree. With the mutant, `episode 3.mkv` (playing) beside `Episode 10.mkv` answers Page Down with "No next file". test-plan.md:261 planned a mixed-case fixture.

Owner decision: fix now.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-07, AC-08.

## Definition of Done

- [ ] New `FolderNeighboursTests` case: a folder with `A.mkv`, `b.mkv`, `episode 3.mkv`, `Episode 10.mkv`; Next from `episode 3.mkv` lists `Episode 10.mkv`; Next from `A.mkv` lists `b.mkv`, `episode 3.mkv`, `Episode 10.mkv`.
- [ ] Mutant on a scratch copy fails the new test: `char.ToUpperInvariant(x[i]).CompareTo(char.ToUpperInvariant(y[j]))` → `x[i].CompareTo(y[j])`.
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
