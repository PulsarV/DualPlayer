---
status: Accepted
owner: "Volodymyr Kravchuk"
reviewers: []
updated_at: "2026-11-17"
feature_size: "L"
ticket: ""
---

# 0008 — Start the master clock when the slowest output hears the first frame, after a silent pre-roll

- **Status:** Accepted
- **Date:** 2026-11-17
- **Deciders:** Volodymyr Kravchuk

## Context

On the target PC, a Bluetooth headset takes audio from the moment its stream starts, but it reports playback position 0 for ~0.6 s (JBL Tune 520BT) to ~1 s (MOTIF II A.N.C.). After that, the position advances at the true rate, with a constant lag equal to that stall: a frame handed over at time T is heard at T + 0.6–1 s. A wired output lags ~11 ms. The engine starts the master clock when every ring holds its start level, hands the film to every device at once, and then aligns each output in one step from a measurement of what it hears (`sad.md` §6 flow 1). While a Bluetooth position still reads 0, that measurement is meaningless, but the device has already queued up to 1 s of film that can no longer be changed. In 2 of 3 runs the MOTIF output stayed about 19 ms off the clock for about 1 s after the recovery window, which breaks QG-1 (within 10 ms ≤ 1 s after start).

## Decision drivers

- QG-1 / spec §6: every output within the 10 ms drift tolerance ≤ 1 s after start; steady-state drift ≤ 10 ms.
- AC-01: both audio outputs start together from the beginning of the file.
- Spec §6: start command to audio on all outputs ≤ 2 s at the time of the decision (relaxed to ≤ 3 s, see Consequences).
- A device's latency is only observable once its reported position moves, and audio already handed to a Bluetooth device cannot be recalled.
- Resume and seek do not have this problem: device streams keep running (ADR-0006), so positions keep moving.

## Considered options

1. **Keep the clock at the start command and measure early** (current). Alignment chases readings taken while the position is still 0. It mostly compensates by accident, then runs out of rounds and leaves a residual offset of tens of ms. Nondeterministic.
2. **Clock at the start command; jump a late output ahead once its position moves.** The late headset's first ~1 s of film is heard late, then skipped in one step. The excursion is recorded at every start.
3. **Film to every output from the start; start the clock at 00:00 when the slowest output hears frame 0, and hold the faster outputs back by their difference.** Audio starts in ~1 s, but a faster device plays its head start (~0.4 s between the two headsets, ~1 s for a wired output) and then falls silent for as long.
4. **Silent pre-roll: every output plays silence until every device's position has moved; then the film is handed to all outputs at once; the clock starts at 00:00 when the slowest output hears film frame 0; faster outputs get silence inserted before frame 0 for their difference.**

## Decision outcome

**Chosen:** Option 4. It is the only option where no Viewer hears film early or late at the start, so AC-01 and QG-1 hold by construction. Each output's latency is measured while nothing audible depends on it, and the alignment works on audio that has not been heard yet. Options 2 and 3 trade a shorter start for an audible jump or gap on every run; option 1 is the bug.

## Consequences

**Positive**
- Every output hears film frame 0 at the same master-clock instant, so no start excursion and no one-step correction at start.
- The start alignment becomes deterministic: one measurement per output, taken after its position moves.
- The measured per-device latency at start is a by-product that roadmap step 4 (latency compensation) can use.

**Negative**
- Start-to-audio grows to about the device lag twice: ~2 s with MOTIF (~1 s of silent pre-roll, then ~1 s for the film to reach the ear). This sat at the edge of spec §6's ≤ 2 s target, so the target was relaxed to ≤ 3 s (owner decision, 2026-11-17).
- The start flow gets a new phase (pre-roll, then release) with its own wait. A device whose position never moves must not hold the others: the existing first-audio deadline (5 s) applies.
- The console's `start-to-audio` now includes the pre-roll. Its meaning stays "until the film is heard on every output", but the number goes up.

**Neutral**
- Resume and seek keep their current alignment (ADR-0006): streams keep running there, so there is no stall.
- `sad.md` §6 flow 1 changes from "start master clock when every ring holds its start level" to pre-roll → release → clock at the slowest output's first heard frame.

## Links

- Spec: [spec.md](../spec.md) §5 AC-01, §6 (drift tolerance, recovery ≤ 1 s, start-to-audio ≤ 3 s)
- SAD: [sad.md](../sad.md) §6 flow 1, §10 QG-1
- Related ADR: [0006](0006-coordinate-pause-and-seek-with-playback-epochs-while-device-streams-keep-running.md) (streams keep running across pause and seek)
- Evidence: target-PC position probe and runs, 2026-11-17 ([_fixes/2026-11-17-clock-mismatch-start-stall.md](../_fixes/2026-11-17-clock-mismatch-start-stall.md))
