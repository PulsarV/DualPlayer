---
id: T72
title: "Record the pre-roll stop close assumption and the start recovery wording in sad.md"
layer: "app"
deps: ["T71"]
blocks: []
acs: ["AC-08", "AC-13", "AC-07"]
files_hint: ["src/DualPlayer.Core/Playback/AudioEngine.cs", "docs/features/two-outputs-one-clock/sad.md", "docs/features/two-outputs-one-clock/test-plan.md"]
owner: "Volodymyr Kravchuk"
estimate: "S"
context_budget: "S"
origin: "review-2026-11-17-r2: R1, R6"
status: "todo"
step: "—"
---

<!-- Follow-up task from review-2026-11-17-r2. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [adr/](../adr/) · [review](../_review/review-2026-11-17-r2.md) · repo `CLAUDE.md`. Do not guess. -->

# T72 — Record the pre-roll stop close assumption and the start recovery wording in sad.md

## Why

- R1: a stop between the release and the clock start relies on WASAPI Stop + release discarding film already queued (`AudioEngine.cs:943-945`); ADR-0008:25 says audio handed to a Bluetooth device cannot be recalled, ADR-0006:28 and flow 9 (`sad.md:421`) stop beyond the queue; T66 DoD 3 asked for a sad.md note. The comment also says the faster outputs hold only silence — an output with lag L_i gets film from clockStart − L_i.
- R6: `sad.md:18` and `sad.md:508` say "≤ 1 s after start" without the qualifier that the start window opens at the master-clock start (QG-1 Then, spec §6).

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-08, AC-13, AC-07; [ADR-0008](../adr/0008-start-the-master-clock-when-the-slowest-output-hears-the-first-frame.md).

## Definition of Done

- [ ] sad.md §11 has a risk row for the pre-roll stop (film already queued on a device may still be heard after the close; mitigation: target-PC check) and flow 9 shows the pre-roll branch.
- [ ] The `EndRun` comment states the assumption correctly.
- [ ] test-plan.md NFR validation has a target-PC scenario: stop within ~1 s of `play` on the Bluetooth pair, every output falls silent together.
- [ ] `sad.md:18` and `:508` say the start window opens at the master-clock start.
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
