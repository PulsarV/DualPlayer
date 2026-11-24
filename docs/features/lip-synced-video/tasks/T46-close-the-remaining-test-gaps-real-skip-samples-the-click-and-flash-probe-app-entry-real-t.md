---
id: T46
title: "Close the remaining test gaps: real skip samples, the click-and-flash probe, App entry, real tokens and the Windows access-denied skip"
layer: "test"
deps: ["T45"]
blocks: []
acs: ["AC-01", "AC-03", "AC-07", "AC-09", "AC-09b", "AC-16"]
files_hint: ["tests/DualPlayer.Media.Tests/", "tests/DualPlayer.App.Tests/", "tests/DualPlayer.Core.Tests/Playback/", "src/DualPlayer.App/App.axaml.cs", "docs/features/lip-synced-video/test-plan.md"]
owner: "Volodymyr Kravchuk"
estimate: "L"
context_budget: "L"
origin: "review-2026-11-21 (R11, R13, R15 (F19 remainder))"
status: "done"
step: "COMMIT"
---

<!-- Follow-up task from review-2026-11-21. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-21.md) · repo `CLAUDE.md`. Do not guess. -->

# T46 — Close the remaining test gaps: real skip samples, the click-and-flash probe, App entry, real tokens and the Windows access-denied skip

## Why

R11 (stage-1): test-plan rows with no test and not re-planned — row 73 (AC-09: an integration test skipping a real one-track sample; `tests/DualPlayer.Media.Tests/TestMedia/` has none); row 75 (AC-09b: real unreadable, audio-only and not-media samples in a temp folder); row 102 (AC-16: probe the click-and-flash video for one flash + one click per second at the same timestamp on every track; `build/make-click-flash-video.ps1` was checked by hand); row 67 (AC-07: nothing reaches the engine or renderer before a playable neighbour is found). `App.RunAsync`/`Play` (exit code after a start failure, the closing path) has no test.

R13 (stage-1, AC-03): `tests/DualPlayer.Media.Tests/Video/FfmpegPictureProbeTests.cs:40-43` does `return` on Windows (and as root), so `windows-latest` CI reports Passed without checking anything.

R15 (stage-2, SCR-03): no test reads the real `Tokens.axaml` through `OsdNoteTokens.From` (`src/DualPlayer.App/Startup/OsdNoteTokens.cs:15-27`); `OsdNoteStyle.Default` (`src/DualPlayer.Media/Video/OsdNoteStyle.cs:10`) repeats the token values, so a failed lookup would go unnoticed.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-01, AC-03, AC-07, AC-09, AC-09b, AC-16.

## Definition of Done

- [x] Media integration tests over samples generated at test time (FFmpeg, as existing TestMedia helpers do): a one-track video is skipped; unreadable, audio-only and not-media files are skipped with the right reasons.
- [x] A probe test on a short generated click-and-flash clip (same filter graph as the script) asserts one flash and one click per second at the same timestamp on every audio track — or re-plan row 102 in test-plan.md with a reason.
- [x] A session test asserts nothing reaches the engine or renderer before a playable neighbour is found.
- [x] An App test drives `RunAsync`/`Play` for a start failure (exit code + stderr/log) and the closing path, with seams added only if needed.
- [x] The access-denied test uses a real skip (`Assert.Skip`) on Windows/root (or a Windows ACL variant).
- [x] A headless test loads the real App resources (merged `Tokens.axaml`) and checks `OsdNoteTokens.From` reads the token values (not the defaults).
- [x] Any row not worth automating is re-planned in test-plan.md with a reason.
- [x] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean

## Note (implement)

Test-only items passed at once and each was proven non-vacuous by breaking the sample, expectation or production line and restoring it. The App flake (`Call from invalid thread`) came from tests that build Avalonia objects on xUnit threads; they now run through `HeadlessApp.Run`. Seam added: `App.Play` is `internal static` with an optional window factory; `PlaybackWindow.RaiseVideoHandleCreated`. Findings for re-review: (1) a neighbour the OS refuses to read is reported as `unreadable` (the track listing fails before the probe), not `access-denied`, which still meets AC-09b's "cannot be read"; (2) the generated-sample and click-and-flash tests skip on `windows-latest` CI (no `ffmpeg.exe`; `fetch-ffmpeg.ps1` unpacks only DLLs); (3) the existing hardware test `Probe_decodes_the_first_h264_picture_in_hardware…` fails on the dev Mac with `DUALPLAYER_HARDWARE=1` because VideoToolbox rejects the 64x48 sample. Both (2) and (3) predate T46.
