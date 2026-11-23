---
id: T147
title: "Pin the mpv renderer's end-file outcomes: decode failure, load failure and end of file"
layer: "test"
deps: []
blocks: ["T23", "T152"]
acs: ["AC-06b"]
files_hint: ["tests/DualPlayer.Media.Tests/Video/MpvEndFileTests.cs"]
status: "todo"
step: "—"
owner: "Volodymyr Kravchuk"
estimate: "S"
context_budget: "S"
origin: "review-2026-11-23h (Y1)"
---

<!-- Follow-up task from review-2026-11-23h. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-23h.md) · repo `CLAUDE.md`. Do not guess. -->

# T147 — Pin the mpv renderer's end-file outcomes: decode failure, load failure and end of file

## Why

- **Y1.** `MpvVideoRenderer.OnEndFile` (`src/DualPlayer.Media/Video/MpvVideoRenderer.cs:896-925`) is what turns mpv's error end-file into AC-06b's `DecodeFailed(video.decode_failed)`. Every AC-06b session test raises it on `FakeVideoRenderer`; the only real-renderer test (`MpvVideoRendererTests.cs:593-597`, hardware) accepts either event. Mutant: an error end-file after load raises `EndOfFile` instead; Media passes 174/174. AC-06b; cli.md §2.3 `picture-failed`, §3 `PictureFailed`/`PictureReload`.

Owner decision: fix now.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-06b.

## Definition of Done

- [ ] New `MpvEndFileTests` drives `OnEndFile` through reflection without native mpv, in the style of `MpvSpeedReplyTests` (renderer state set through its private fields; `IsCurrentFile` true).
- [ ] An error end-file after the file loaded raises exactly one `DecodeFailed` with code `video.decode_failed` and no `EndOfFile`.
- [ ] An error end-file while loading (not yet loaded) raises no event, sets the load error `mpv.load_failed` and releases the load wait.
- [ ] An EOF end-file raises `EndOfFile` and no `DecodeFailed`; an end-file for a file that is no longer current raises nothing.
- [ ] Mutants on a scratch copy, each failing at least one new test: the after-load error branch raises `EndOfFile`; the `video.decode_failed` code is changed; the EOF branch raises nothing.
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
