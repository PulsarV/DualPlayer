---
id: T66
title: "Map access denied on the audio open and name the format when the picture probe times out"
layer: "integration"
deps: []
blocks: []
acs: ["AC-09b", "AC-02"]
files_hint: ["src/DualPlayer.Media/Audio/FfmpegAudioDecoder.cs", "src/DualPlayer.Media/Video/FfmpegPictureProbe.cs", "tests/DualPlayer.Media.Tests/"]
status: "todo"
step: "SELECT"
owner: "Volodymyr Kravchuk"
estimate: "S"
context_budget: "S"
origin: "review-2026-11-21c (S9, S17)"
---

<!-- Follow-up task from review-2026-11-21c. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-21c.md) · repo `CLAUDE.md`. Do not guess. -->

# T66 — Map access denied on the audio open and name the format when the picture probe times out

## Why

S9: `CheckPlayable` calls `_decoder.ListTracks` before the picture probe (`PlaybackSession.cs:582-586`), and `FfmpegAudioDecoder.OpenInput` maps every `avformat_open_input` failure to `playback.file_unreadable` (`FfmpegAudioDecoder.cs:76-80`), so a neighbour without read permission always gives `reason unreadable`, never `access-denied` (cli.md §2.2).

S17: when the first picture misses the 3 s limit, the error has no codec or profile (`FfmpegPictureProbe.cs:44-46`) though `ProbeCore` has read them (`:80-81`); AC-02 and `cli.md:123` want the format named.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-09b, AC-02.

## Definition of Done

- [ ] `FfmpegAudioDecoder.OpenInput` maps AVERROR(EACCES) and AVERROR(EPERM) to `playback.file_access_denied` (shared mapping with the picture probe).
- [ ] The probe worker publishes codec and profile as soon as they are read; the timeout error names them in its message and `Fields`.
- [ ] Tests: the decoder mapping for 13 and 1; a timeout after the codec is read names it.
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
