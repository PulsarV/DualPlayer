---
id: T33
title: "Reject an offsets file without the offsets key and clamp huge stored offsets to the right bound"
layer: "infra"
deps: ["T32"]
blocks: []
acs: ["AC-12"]
files_hint: ["src/DualPlayer.Core/Settings/DeviceOffsetsFile.cs", "src/DualPlayer.Core/Sync/OffsetBook.cs", "tests/DualPlayer.Core.Tests/Settings/", "tests/DualPlayer.Core.Tests/Sync/"]
owner: "Volodymyr Kravchuk"
estimate: "S"
context_budget: "S"
origin: "review-2026-11-20 (F18, F26)"
status: "done"
step: "COMMIT"
---

<!-- Follow-up task from review-2026-11-20. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-20.md) · repo `CLAUDE.md`. Do not guess. -->

# T33 — Reject an offsets file without the offsets key and clamp huge stored offsets to the right bound

## Why

F18 (data-model.md, `offsets` required): a missing `offsets` key loads silently as an empty map; only `null` triggers `state.invalid` (`DeviceOffsetsFile.cs:10`; `OffsetBook.cs:51-57`).
F26 (stage-2): normalizing a stored offset near `int.MaxValue`/`int.MinValue` overflows when multiplied by 10 and flips sign, so it clamps to the wrong bound (`OffsetBook.cs:116-120`).

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-12.

## Definition of Done

- [x] A file without `offsets` gives the same `state.invalid` warning and handling as `offsets: null` (per data-model.md).
- [x] Stored offsets near `int.MaxValue` / `int.MinValue` clamp to +limit / −limit (no overflow; checked arithmetic or long).
- [x] Tests for both; they fail on the current code.
- [x] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean

## Note (implement)

F18 did not reproduce through the real store: System.Text.Json's source-generated reader leaves the init-only `Offsets` null when the key is missing (the property initializer is not applied), so `OffsetBook` already warned `state.invalid`. The new test (`A_file_without_the_offsets_key_warns_state_invalid_and_starts_at_zero_like_offsets_null`) passed on the old code and stays as a regression guard over the real store. F26 reproduced and is fixed.
