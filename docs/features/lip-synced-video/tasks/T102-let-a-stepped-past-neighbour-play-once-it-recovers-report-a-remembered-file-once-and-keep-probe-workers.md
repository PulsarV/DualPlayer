---
id: T102
title: "Let a stepped-past neighbour play once it recovers, judge the whole budget per press, report a remembered file once, and keep probe workers too"
layer: "app"
deps: []
blocks: []
acs: ["AC-07", "AC-08", "AC-09", "AC-09b"]
files_hint: ["src/DualPlayer.Core/Playback/PlaybackSession.cs", "src/DualPlayer.Core/Playback/IPictureProbe.cs", "src/DualPlayer.Media/Video/FfmpegPictureProbe.cs", "tests/DualPlayer.Core.Tests/Playback/PlaybackSessionNeighbourRememberedFilesTests.cs", "tests/DualPlayer.Core.Tests/Playback/PlaybackSessionNeighbourMemoryTests.cs", "tests/DualPlayer.Media.Tests/", "docs/features/lip-synced-video/spec.md", "docs/features/lip-synced-video/sad.md", "docs/features/lip-synced-video/contracts/cli.md"]
status: "done"
step: "COMMIT"
owner: "Volodymyr Kravchuk"
estimate: "M"
context_budget: "M"
origin: "review-2026-11-22d (V1, V2, V3, V4, V10)"
---

<!-- Follow-up task from review-2026-11-22d. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-22d.md) · repo `CLAUDE.md`. Do not guess. -->

# T102 — Let a stepped-past neighbour play once it recovers, judge the whole budget per press, report a remembered file once, and keep probe workers too

## Why

- **V1** (all three reviewers, checked in code). The live-worker step-past (`PlaybackSession.cs:753-759`) returns `FileSkipped("unreadable")` with `Expired=false`. The wrapper (`:726-730`) then removes the file from `_timedOut` and writes `_judged[key]`, and later presses read `_judged` first (`:541-543`). So E2, the only neighbour on a NAS that drops for 20 s, is never played again after the NAS returns. The same applies to a healthy file whose cut listing is still running at the next press. The U4 test (`PlaybackSessionNeighbourRememberedFilesTests.cs:75-87`) only counts `ListTracks` calls.
- **V2** (two reviewers, checked in code). "Whole budget" is `elapsed <= ProbeTimingSlack` since `scanAt` (`:764-772`), and that elapsed time includes the folder enumeration, the sort and the `KeyOf` stats (`:514, :537`): about 120 ms on a 2000-file NAS folder. Separately, the post-listing exit at `:806-811` returns `(null, true)` without setting `timedOut`. In both cases a slow first file is never remembered and every press ends `next-file-timeout checked 0`.
- **V3** (checked in code). The block after the target (`:568-586`) reports every `_timedOut` file before the target, including one already checked and reported in this press's loop that stayed remembered (`:836`). Two `skipped Episode-02.mkv` lines and "Skipped 2 files" for one file.
- **V4** (checked in code). Only the listing worker is kept (`:784`); the probe's LongRunning thread (`FfmpegPictureProbe.cs:38, 58`) is abandoned untracked. A file with a bad sector in its first GOP leaves one blocked thread per press, so spec §6.1 (:249) and sad §7 (:533) "at most one blocked thread per file" are false.
- **V10** (docs, rest of U5). sad.md:533 still states the T84 rule (remembered files "are stepped past on the next press"), and "the 3 s budget counts from the start of the command plus any queue wait" reads as if the queue wait were part of the budget, the opposite of spec :249.

Owner decision: fix now.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-07, AC-08, AC-09, AC-09b.

## Definition of Done

- [x] A file stepped past because its worker (listing or probe) is still running is reported skipped (reason `unreadable`, counted in `checked N`) for that press only: it is written neither to `_judged` nor removed from `_timedOut`, and once its worker has completed the next press checks it again (completed `_workers` entries are pruned). This also holds for a healthy file whose cut listing is still running.
- [x] "Whole budget" is decided by a per-press flag (no earlier file in this press started a check), not by elapsed time since `scanAt`, so the folder enumeration, sort and stats never count against it; the post-listing exit (too little left for the probe) sets `timedOut = wholeBudget`.
- [x] A remembered file is reported skipped at most once per press: the block after the target reports only remembered files that were deferred and not visited in this press.
- [x] The abandoned probe worker is kept per `NeighbourKey` (or per path inside `FfmpegPictureProbe`) like the listing worker: while it runs, no press starts another probe on that file; the file is stepped past as in the first box. At most one blocked thread per file, listing or probe.
- [x] Tests: (1) E2 only neighbour, listing blocked through press 2, released, press 3 plays E2; (2) a healthy E3 whose cut listing is still running at press 2 plays on press 3; (3) a folder listing that costs 200 ms of fake time, then E2 listing 1.2 s + probe 1.9 s → E2 remembered, press 2 reaches E3; (4) E2 listing 2.95 s → remembered; (5) the three-press sequence where E2 hangs again on press 3 and E3 plays → exactly one `FileSkipped(E2)` and `skipped 1`; (6) a probe that never returns + three presses → exactly one `Probe` call on E2. Each fails if its fix is reverted.
- [x] spec §6.1 (:249), sad §7 (:533) and cli.md §2.2 (:85) state the final rule: what is remembered, that the 3 s budget counts from when the session starts the command (a queue wait is not part of it), that remembered files are checked last and reported as skipped once, and that a live listing or probe worker is not restarted and its file plays once it recovers.
- [x] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
