---
id: T159
title: "Let the stale-file end-file theory run without libmpv"
layer: "test"
deps: []
blocks: ["T23", "T161"]
acs: ["AC-06b"]
files_hint: ["tests/DualPlayer.Media.Tests/Video/MpvEndFileTests.cs"]
status: "todo"
step: "—"
owner: "Volodymyr Kravchuk"
estimate: "XS"
context_budget: "S"
origin: "review-2026-11-23i (AA8)"
---

<!-- Follow-up task from review-2026-11-23i. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-23i.md) · repo `CLAUDE.md`. Do not guess. -->

# T159 — Let the stale-file end-file theory run without libmpv

## Why

- **AA8.** Neither row of the "no longer current" theory in `MpvEndFileTests.cs:71` calls `mpv_error_string`: `OnEndFile` returns at the `IsCurrentFile` guard (`MpvVideoRenderer.cs:905-908`) first. Its `RequireLibmpv()` skip means the guard loses its only test on a machine without libmpv.

Owner decision: fix now.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-06b.

## Definition of Done

- [ ] `RequireLibmpv()` is removed from the stale-file theory; both rows run and pass without libmpv.
- [ ] Mutant on a scratch copy: removing the `IsCurrentFile` guard fails both rows.
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
