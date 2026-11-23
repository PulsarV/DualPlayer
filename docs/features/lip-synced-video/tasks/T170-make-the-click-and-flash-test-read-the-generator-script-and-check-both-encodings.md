---
id: T170
title: "Make the click-and-flash test read the generator script and check both encodings"
layer: "test"
deps: []
blocks: ["T23", "T173"]
acs: ["AC-16"]
files_hint: ["tests/DualPlayer.Media.Tests/Video/ClickAndFlashVideoTests.cs", "build/make-click-flash-video.ps1"]
status: "done"
step: "COMMIT"
owner: "Volodymyr Kravchuk"
estimate: "S"
context_budget: "S"
origin: "review-2026-11-24 (AD2)"
---

<!-- Follow-up task from review-2026-11-24. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-24.md) · repo `CLAUDE.md`. Do not guess. -->

# T170 — Make the click-and-flash test read the generator script and check both encodings

## Why

- **AD2.** `tests/DualPlayer.Media.Tests/Video/ClickAndFlashVideoTests.cs:22-24` hard-codes a copy of the filter graphs in `build/make-click-flash-video.ps1:26-27`; no test opens the script. Changing the script's click period to `mod(t\,1000/1000)` still passes. The test encodes only H.264, while the T23 L1 scenario uses `click-flash-hevc.mkv`. test-plan.md:105 claims "the script's own filter graph".

Owner decision: fix now.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-16.

## Definition of Done

- [x] The test reads the `$video` and `$click` assignments and each encoding's codec arguments (`$encodings`) from `build/make-click-flash-video.ps1` (found from the repo root), unescaping PowerShell's double-quoted string as needed; no copy of the graphs is left in the test. *(Test reads $video, $click and each $encodings entry from build/make-click-flash-video.ps1 (found by walking up to DualPlayer.slnx); the graph copies are removed from the test.)*
- [x] The check runs for both encodings (H.264 and HEVC) — a theory over the script's encodings; an encoding the local FFmpeg cannot write is skipped with a reason, never silently passed. *(The check is a Theory over the script's encodings (click-flash-h264.mkv and click-flash-hevc.mkv), both ran and passed. An encoding the local FFmpeg cannot write is skipped with a reason through Assert.SkipUnless.)*
- [x] Mutants on a scratch copy, each failing the test: the script's click period `1001/1000` → `1000/1000`; the flash period `mod(n\,24)` → `mod(n\,25)`. *(2 mutants killed, reverted. Click period 1001/1000 -> 1000/1000 failed both encodings (2/2). Flash period mod(n\,24) -> mod(n\,25) failed both encodings (2/2). The extra HEVC codec mutant was killed in the RED step.)*
- [x] The script's behaviour is unchanged (a refactor that makes it easier to read is allowed only if the generated files stay the same). *(git diff -- src build is empty; the script is unchanged and only the test file is modified.)*
- [x] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean *(dotnet build has 0 warnings and 0 errors, dotnet test is all green, and dotnet format --verify-no-changes exits 0.)*
