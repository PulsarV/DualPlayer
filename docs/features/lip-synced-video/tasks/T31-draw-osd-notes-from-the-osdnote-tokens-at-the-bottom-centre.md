---
id: T31
title: "Draw OSD notes from the OsdNote tokens at the bottom centre"
layer: "ui"
deps: ["T30"]
blocks: []
acs: ["AC-08", "AC-15"]
files_hint: ["src/DualPlayer.Media/Video/MpvVideoRenderer.cs", "src/DualPlayer.App/Themes/Tokens.axaml", "src/DualPlayer.App/Startup/", "tests/DualPlayer.Media.Tests/Video/", "tests/DualPlayer.App.Tests/"]
owner: "Volodymyr Kravchuk"
estimate: "S"
context_budget: "S"
origin: "review-2026-11-20 (F10)"
status: "todo"
step: ""
---

<!-- Follow-up task from review-2026-11-20. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-20.md) · repo `CLAUDE.md`. Do not guess. -->

# T31 — Draw OSD notes from the OsdNote tokens at the bottom centre

## Why

F10 (SCR-03, ADR-0003, CLAUDE.md UI): the OSD note ignores the `OsdNote*` tokens (`MpvVideoRenderer.cs:418-431`; `Tokens.axaml:19-24`). It hard-codes size 32 (which mpv scales on a 720-line canvas), a translucent `#CC000000` box, a 48 margin and a non-token warning colour. With no `osd-align-x/y` set it sits top-left instead of bottom-centre. The test only checks that the tokens exist.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-08, AC-15.

## Definition of Done

- [ ] The renderer takes the note style (size, colours, box, margin) from the `OsdNote*` tokens via an options object the App fills from `Tokens.axaml`; size is converted to mpv's 720-line OSD scale so it renders at the token px at 1080p.
- [ ] `osd-align-x=center`, `osd-align-y=bottom` set; the box is opaque per the token (contrast ≥ 7:1).
- [ ] Test asserts the mpv option values derived from given tokens (not just that tokens exist).
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
