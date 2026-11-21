---
id: T75
title: "Keep Page Down and Page Up honest when the time budget runs out"
layer: "app"
deps: ["T74"]
blocks: []
acs: ["AC-07", "AC-08", "AC-09b"]
files_hint: ["src/DualPlayer.Core/Playback/PlaybackSession.cs", "src/DualPlayer.Core/Playback/", "src/DualPlayer.Media/Video/FfmpegPictureProbe.cs", "src/DualPlayer.App/Startup/TerminalPrinter.cs", "src/DualPlayer.App/Startup/SessionEventLogWriter.cs", "docs/features/lip-synced-video/spec.md", "docs/features/lip-synced-video/contracts/cli.md", "docs/features/lip-synced-video/screens.md", "tests/"]
status: "done"
step: "COMMIT"
owner: "Volodymyr Kravchuk"
estimate: "M"
context_budget: "M"
origin: "review-2026-11-21d (R3)"
---

<!-- Follow-up task from review-2026-11-21d. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-21d.md) · repo `CLAUDE.md`. Do not guess. -->

# T75 — Keep Page Down and Page Up honest when the time budget runs out

## Why

R3 (rest of S14): when the per-press budget runs out, the scan breaks into the AC-08 branch ("No next file", `no-next-file`) while files remain unchecked (`PlaybackSession.cs:490-498, 519-527`; `TerminalPrinter.cs:151-152`; the test `PlaybackSessionNeighbourBudgetTests.cs:36-42` enshrines this). A probe cut to the time left that times out is reported as `video.not_hardware_decodable` (`PlaybackSession.cs:626-648`; `FfmpegPictureProbe.cs:32-56`), so a playable file is skipped as "cannot be played on this PC" — AC-09b allows skipping only files the PC really cannot decode. `_decoder.ListTracks` (`PlaybackSession.cs:605`) runs after the budget check with no limit (reproduced: 0.8 s ListTracks + 2 s probe → one press took 3.8 s; Space/Esc wait behind it). Timed-out probe workers keep the file open.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-07, AC-08, AC-09b.

## Definition of Done

- [x] When the budget runs out before a playable neighbour is found, the Viewer gets its own note (e.g. "Next file not found within 3 s (checked N)") and the terminal its own line; it is not the AC-08 `no-next-file` outcome.
- [x] A probe that timed out because the budget cut its limit below the normal 2 s is not reported as a skipped file; the scan stops before that candidate.
- [x] `ListTracks` runs under the same per-press deadline (worker + wait, like the probe) and the probe limit is re-derived after it; one press never exceeds the budget by more than one scheduling tick.
- [x] spec.md §6.1 / AC-08 wording, cli.md §2.2 and screens.md SCR-01/SCR-02 name the budget-expiry outcome.
- [x] Tests with fake time: slow `ListTracks` (`rig.Decoder.BeforeList`) keeps the press within budget; a budget-cut probe timeout is not a skip; the budget-expiry note/line appear; `PlaybackSessionNeighbourBudgetTests` updated. Pinned by T83: the `NeighbourSearchTimedOut` event (`Next`, `Checked`), the `next-file-timeout  checked N` / `previous-file-timeout  checked N` lines and a listing that blocks past the budget.
- [x] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
