---
id: T14
title: "List a file's audio tracks and decode the chosen ones once with FFmpeg"
layer: "infra"
deps: ["T1", "T3"]
blocks: ["T15"]
acs: ["AC-02", "AC-03"]
files_hint: ["src/DualPlayer.Media/Audio/FfmpegAudioDecoder.cs", "src/DualPlayer.Media/Audio/FfmpegDecodeSession.cs", "src/DualPlayer.Media/MediaServiceCollectionExtensions.cs", "tests/DualPlayer.Media.Tests/FfmpegAudioDecoderTests.cs", "tests/DualPlayer.Media.Tests/TestMedia/"]
owner: "Volodymyr Kravchuk"
estimate: "L"
context_budget: "M"
status: "todo"
step: ""
---

<!-- Self-contained task: work from what is inlined here. Every quoted chunk ends with its provenance
signature (`<file> §<section>, <identifier>, verbatim|abridged`); the source always wins. If a slice is
insufficient, ambiguous, or contradicts the code in front of you, open the named file for the full
text and follow that. Do not invent the missing part. -->

# T14 — List a file's audio tracks and decode the chosen ones once with FFmpeg

## Place in the sequence

- **Blocked by:** T1 — Define the audio engine ports, value types and test fakes in Core, T3 — Provision LGPL FFmpeg for the dev Mac, CI and the Windows build
- **Blocks:** T15 — Convert each output's track to its device mix format with swresample and a fold-down matrix
- **Wave:** 2 — after its deepest dependency finishes (wave 1).
- **Lane:** shares `tests/DualPlayer.Media.Tests/` with T3 — serialized; shares `src/DualPlayer.Media/Audio/FfmpegDecodeSession.cs` with T15 — serialized.

## Why (user story)

> **As a** Developer
> **I want** to list the connected devices and the tracks inside a file
> **So that** I can choose the inputs for a run without guessing device ids or track numbers
>
> — `spec.md §4, US-02, verbatim` · full text: [spec.md](../spec.md)

This task implements the decoder port: the track list the Developer chooses from and the single decode that feeds every output.

## Inlined context

> - `IAudioBackend` lists the playback devices the OS offers right now (`ListDevices()` — name, type when known, endpoint id; AC-02, AC-03) and opens one by id (`Open(deviceId)`), returning an `IAudioDevice`: the open shared-mode stream with its mix format, start/close and loss/denial events. Hotplug (`IDeviceWatcher`) stays with roadmap step 5; this feature does not create it.
> - The device stream is pull-based: the backend opens a device in shared mode, reports its mix format, owns that device's render thread and calls a Core-provided source with a buffer to fill and the device's playback position (frames played + monotonic timestamp); device loss and access denial arrive as backend events.
> - `IAudioDecoder` lists a file's audio tracks and decodes the chosen ones once, converting each output's track to that output's device mix format with FFmpeg swresample — fold-down drops LFE, fewer-channel tracks go to the front channels, no upmix.
>
> — `sad.md §5, Port contracts, abridged (ADR links dropped)` · full text: [sad.md](../sad.md)

> **Chosen:** One decode thread, one ring buffer per output, one render thread per device — the decode thread demuxes once, decodes each chosen track, converts it to its device's format and writes into each output's single-producer/single-consumer ring buffer holding several seconds of audio; each device's event-driven render thread reads only its own ring.
>
> — `adr/0002 §Considered options, option 1 (chosen), verbatim` · full text: [ADR-0002](../adr/0002-fan-out-one-decoder-to-per-device-render-threads-through-ring-buffers.md)

> Malformed or hostile media file: the player refuses it with a plain explanation (AC-03) and never crashes or hangs.
>
> — `spec.md §6.1, Abuse cases, verbatim` · full text: [spec.md](../spec.md)

> **Hard rule — Error handling.** Expected failures (file unreadable, track or device invalid, access denied, device lost) are `Result` / `Result<T>` or engine events with a plain-language message for the console; exceptions are bugs, logged at the top of each background loop (decode, control, console); a bug in one render loop stops that output only
>
> — `sad.md §8, Error handling, verbatim` · full text: [sad.md](../sad.md)

```text
S->>X: enumerate playback devices the OS offers now
X-->>S: name, device type when known, endpoint id
S-->>C: numbered device list
S->>X: open the file and read its audio streams
alt file opens as media
    S-->>C: tracks numbered from 1 with language and channel layout
else file cannot be opened as media
    S-->>C: the file cannot be opened, with the reason in plain language
Note: Postcondition: nothing plays and no run starts
```

— `sad.md §6, Flow 4 «list devices and tracks», sequence lines, abridged` · full text: [sad.md](../sad.md)

**Fallback:** insufficient or contradicted by the code → read the named file in full ([spec.md](../spec.md) · [sad.md](../sad.md) · [cli.md](../contracts/cli.md) · [adr/](../adr/) · [CONTEXT.md](../../../../CONTEXT.md)) and follow it. Do not guess.

## Data delta

No DB changes.

## API contract

> Prints every audio track of the file, numbered from 1 among audio tracks only, with language (or `und`) and channel layout. Nothing plays. Exit code `0`.
> ```
> #  language  layout   rate
> 1  ukr       5.1      48000
> 2  eng       stereo   44100
> ```
> Errors: `playback.file_unreadable` (exit `2`).
>
> — `contracts/cli.md §1.2 `tracks <file>`, abridged` · full text: [cli.md](../contracts/cli.md)

## Acceptance criteria

### AC-02 — happy path

> **Given** devices are connected and the Developer has a media file
> **When** the Developer asks for the device list and the track list
> **Then** the console shows every playback device the operating system currently offers (name, device type when known, device id) and every audio track of the file (number, language, channel layout), and nothing starts playing. Tracks are numbered from 1 among the file's audio tracks only; the Developer names a device either by its number in this list or by its full operating-system id, and when playback starts the console echoes the id each output resolved to
>
> — `spec.md §5, AC-02, verbatim` · full text: [spec.md](../spec.md)

### AC-03 — error

> **Given** the Developer names a file that cannot be opened as media, a track number the file does not have, or a device that is not connected
> **When** the Developer starts playback
> **Then** no audio output starts and the console names the invalid input and the reason in plain language (for example: track 3 does not exist, the file has tracks 1 and 2)
>
> — `spec.md §5, AC-03, verbatim` · full text: [spec.md](../spec.md)

## Checklist

- [ ] `FfmpegAudioDecoder : IAudioDecoder` — `ListTracks(path)` numbering audio streams from 1 with language (`und` if none), channel layout, sample rate — `src/DualPlayer.Media/Audio/FfmpegAudioDecoder.cs`
- [ ] `FfmpegDecodeSession` — one demux pass, decodes every chosen track, returns float frames per output tagged with the epoch; `Seek(target, epoch)`; end of file signalled — `FfmpegDecodeSession.cs` (conversion to mix format lands in T15)
- [ ] Every FFmpeg error becomes `playback.file_unreadable` with a plain-language reason; no exception crosses the port
- [ ] Register in `AddMedia` — `src/DualPlayer.Media/MediaServiceCollectionExtensions.cs`
- [ ] Small committed test files (seconds long: 5.1 ukr 48 kHz + stereo eng 44.1 kHz; mono; a non-media file) — `tests/DualPlayer.Media.Tests/TestMedia/`

## Edge cases

| Case | Behaviour |
|---|---|
| Not a media file / truncated / zero bytes | `playback.file_unreadable`, no crash, no hang |
| File with video + subtitle streams | only audio streams numbered, from 1 |
| Track without a language tag | `und` |
| Seek target past the end | session reports end of file (the engine decides AC-10 behaviour) |

## Definition of Done

- [ ] `FfmpegAudioDecoderTests` pass on the dev Mac and in CI: track listing for the test files, `file_unreadable` for a non-media file, decode of two tracks in one pass, seek tags frames with the new epoch
- [ ] every Hard Rule inlined above still holds
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
