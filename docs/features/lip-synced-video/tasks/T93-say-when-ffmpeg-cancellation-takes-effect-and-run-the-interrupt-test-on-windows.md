---
id: T93
title: "Say when FFmpeg cancellation takes effect and run the interrupt test on Windows"
layer: "integration"
deps: ["T88"]
blocks: []
acs: ["AC-09b"]
files_hint: ["src/DualPlayer.Media/FfmpegInterrupt.cs", "src/DualPlayer.Media/", "tests/DualPlayer.Media.Tests/", "docs/features/lip-synced-video/spec.md", "docs/features/lip-synced-video/sad.md"]
status: "done"
step: "COMMIT"
owner: "Volodymyr Kravchuk"
estimate: "M"
context_budget: "M"
origin: "review-2026-11-22b (S10)"
---

<!-- Follow-up task from review-2026-11-22b. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-22b.md) · repo `CLAUDE.md`. Do not guess. -->

# T93 — Say when FFmpeg cancellation takes effect and run the interrupt test on Windows

## Why

**S10.** FFmpeg calls the AVIO `interrupt_callback` only between I/O calls (`src/DualPlayer.Media/FfmpegInterrupt.cs:26-35`), so it cannot break a read blocked in the kernel (a dead SMB share or a failing disk). The thread stays busy and the file stays open until the OS times out, yet spec §6.1 (`spec.md:249`) still promises "the file is closed and no thread is left busy on it". The FIFO test trickles a byte every 20 ms (`tests/DualPlayer.Media.Tests/StalledPipe.cs:34`) and is skipped on Windows, so windows-latest CI never runs the interrupt.

Owner decision: fix — honest docs plus a test that runs on Windows.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-09b.

## Definition of Done

- [x] spec §6.1 and sad.md §7 say cancellation takes effect at the next I/O boundary, that a read blocked inside the OS holds its thread until the OS gives up, and that the step-past memory bounds how many such threads can pile up.
- [x] A Media test that runs on Windows (and macOS) proves an interrupted open/probe returns promptly: e.g. a custom `AVIOContext` whose `read_packet` trickles or blocks between calls, or a Windows named-pipe writer; it is not skipped on windows-latest CI.
- [x] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
