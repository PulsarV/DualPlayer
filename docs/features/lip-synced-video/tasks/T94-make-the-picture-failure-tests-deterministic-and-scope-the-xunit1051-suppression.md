---
id: T94
title: "Make the picture-failure tests deterministic and scope the xUnit1051 suppression"
layer: "test"
deps: ["T90"]
blocks: []
acs: ["AC-06b"]
files_hint: ["tests/DualPlayer.Core.Tests/Playback/PlaybackSessionPictureFailureReloadTests.cs", "tests/DualPlayer.Core.Tests/Playback/PlaybackSessionPictureHealthTests.cs", "tests/DualPlayer.Core.Tests/Fakes/", "tests/DualPlayer.Media.Tests/DualPlayer.Media.Tests.csproj", "tests/DualPlayer.Media.Tests/"]
status: "done"
step: "COMMIT"
owner: "Volodymyr Kravchuk"
estimate: "S"
context_budget: "S"
origin: "review-2026-11-22b (S11, S12)"
---

<!-- Follow-up task from review-2026-11-22b. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-22b.md) · repo `CLAUDE.md`. Do not guess. -->

# T94 — Make the picture-failure tests deterministic and scope the xUnit1051 suppression

## Why

**S12.** Under a heavily loaded full-solution run, 3 picture-failure tests failed on a real-time `WaitFor(Summary is not null)` — e.g. `A_failed_reload_after_a_decode_failure_still_shows_the_note_and_throws_nothing_AC_06b` and `The_reload_wait_is_bounded_to_three_seconds_…_AC_06b` (`tests/DualPlayer.Core.Tests/Playback/PlaybackSessionPictureFailureReloadTests.cs:30, 70`; `PlaybackSessionPictureHealthTests.cs:24, 62`; `tests/DualPlayer.Core.Tests/Fakes/EngineRig.cs:284-305`). They pass alone. Same class of problem as T59; a slow CI runner will hit it.

**S11.** The `xUnit1051` analyzer rule is switched off for the whole Media test project (`tests/DualPlayer.Media.Tests/DualPlayer.Media.Tests.csproj:4`), while the Console tests scope the same suppression with local `#pragma` (`tests/DualPlayer.Console.Tests/ListAndPlayCommandTests.cs:52, 77`).

Owner decision: fix both.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-06b.

## Definition of Done

- [x] The picture-failure tests no longer depend on real-time waits racing the session thread: they advance fake time / the fake renderer until the summary appears (or wait on a signal), with no weakened assertion.
- [x] The full `dotnet test DualPlayer.slnx` passes 3 times in a row with the Core tests under parallel load (e.g. run alongside a second test process).
- [x] The project-wide `NoWarn` for xUnit1051 is gone; `TestContext.Current.CancellationToken` is passed where an overload exists, local `#pragma` elsewhere.
- [x] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
