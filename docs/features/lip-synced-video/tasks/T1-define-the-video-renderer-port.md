---
id: T1
title: "Define the IVideoRenderer port and a scripted fake renderer in Core"
layer: "domain"
deps: []
blocks: ["T3", "T4"]
acs: ["AC-01", "AC-17"]
files_hint: ["src/DualPlayer.Core/Playback/IVideoRenderer.cs", "tests/DualPlayer.Core.Tests/Fakes/FakeVideoRenderer.cs", "tests/DualPlayer.Core.Tests/Fakes/FakeVideoRendererTests.cs"]
owner: "Volodymyr Kravchuk"
estimate: "S"
context_budget: "M"
status: "todo"
step: ""
---

<!-- Self-contained task: work from what is inlined here. Every quoted chunk ends with its provenance
signature (`<file> §<section>, <identifier>, verbatim|abridged`); the source always wins. If a slice is
insufficient, ambiguous, or contradicts the code in front of you, open the named file for the full
text and follow that. Do not invent the missing part. -->

# T1 — Define the IVideoRenderer port and a scripted fake renderer in Core

## Place in the sequence

- **Blocked by:** — (wave-1 root)
- **Blocks:** T3 — Steer the picture to clock minus picture delay with a Core picture-sync loop, T4 — Implement the hardened libmpv video renderer with presented-position snapshots and OSD notes
- **Wave:** 1 — starts at once — no dependencies.
- **Lane:** own lane.

## Why (user story)

> **As a** Viewer
> **I want** to watch a film the Developer started for us in fullscreen and hear my own track in my own device in step with the lips on screen
> **So that** I can watch the same film as the other Viewer in my own language without being distracted by sound that runs ahead of or behind the picture
>
> — `spec.md §4, US-01, verbatim` · full text: [spec.md](../spec.md)

This task gives Core the seam through which the picture is shown and timed, so every picture-sync rule after it can be tested with a fake renderer and a fake clock.

## Inlined context

> Core gains a playback session use case and three ports: the video renderer, the device latency probe and the picture probe. The latency model, the offset book and the picture-sync loop live in Core so they are tested with a fake clock, fake devices and a fake renderer.
>
> — `sad.md §5, intro, abridged` · full text: [sad.md](../sad.md)

> `IVideoRenderer.cs` — port: load, play at a timestamp, pause, seek, set speed, presented position, show note, frame counters, decode-failure and end events
>
> — `sad.md §5, internal decomposition, Playback/IVideoRenderer.cs, verbatim` · full text: [sad.md](../sad.md)

> Core owns timing decisions; the adapter only reports positions and applies commands, so the picture-sync rules are unit-tested with a fake renderer and a fake clock.
>
> — `adr/0002 §Consequences, positive, verbatim` · full text: [ADR-0002](../adr/0002-render-video-with-libmpv-slaved-to-the-master-clock-behind-a-renderer-port.md)

> All lip-sync arithmetic is in Core on the master clock and `TimeProvider`; no `DateTime.Now` / `Stopwatch` in Core. The mpv adapter converts mpv's timestamps to the same monotonic time base the step-3 backend uses (QPC on Windows) before handing them to Core
>
> — `sad.md §8, Time and clocks, verbatim` · full text: [sad.md](../sad.md)

> **Hard rule:** The playback session, picture sync and lag ramps run on the step-3 engine control loop. mpv property and end events arrive on mpv's event thread; the adapter publishes immutable snapshots without locks the control loop could wait on. Render threads are never blocked. UI updates marshal through `Dispatcher.UIThread`. The picture probe runs off the UI thread with a time limit
>
> — `sad.md §8, Threading, verbatim` · full text: [sad.md](../sad.md)

> xUnit v3 + Shouldly + NSubstitute. Core logic with a fake clock, fake devices (`EngineRig`), and new `FakeVideoRenderer`, `FakeDeviceLatencyProbe`, `FakePictureProbe`. The libmpv adapter and the FFmpeg probe with sample files; tests needing a GPU, a screen or audio hardware use `[HardwareFact]`
>
> — `sad.md §8, Testing, verbatim` · full text: [sad.md](../sad.md)

> **Hard rule:** Ports and adapters: `DualPlayer.Core` references only the BCL and `Microsoft.Extensions.*.Abstractions` — no OS, UI or native library; OS code lives in `DualPlayer.Platform.Windows` / `.MacOS`, codec and video code in `DualPlayer.Media`.
>
> — `sad.md §2, Technical, ports and adapters, abridged` · full text: [sad.md](../sad.md)

> **Hard rule:** Expected runtime failures (file unreadable, no picture, device lost, decode error) travel as `Result` / `Result<T>` (`src/DualPlayer.Core/Result.cs`, dotted lowercase error codes) or a domain event — never thrown across module boundaries; exceptions mean bugs and are logged at the top of each background loop.
>
> — `sad.md §2, Conventions, errors, verbatim` · full text: [sad.md](../sad.md)

**Fallback:** insufficient or contradicted by the code → read the named file in full ([spec.md](../spec.md) · [sad.md](../sad.md) · [data-model.md](../data-model.md) · [cli.md](../contracts/cli.md) · [screens.md](../screens.md) · [adr/](../adr/)) and follow it. Do not guess.

## Data delta

No DB changes.

## API contract

Internal — no API surface.

## Acceptance criteria

### AC-01 — happy path

> **Given** a video file with a picture and at least two audio tracks, and the Developer's start command names the file, a track and a device for each Viewer, and a screen
> **When** the start command is given
> **Then** the film opens fullscreen on the named screen (on the main screen when none is named), the picture and every output's sound start together within the start budget (§6), and each Viewer hears their own track; on a device whose offset was set in an earlier run (AC-12) the sound is in step with the lips within the lip-sync budget (§6), on a device's first use the error is only measured (§7)
>
> — `spec.md §5, AC-01, verbatim` · full text: [spec.md](../spec.md)

### AC-17 — happy path

> **Given** a film is playing
> **When** the picture delay or any output's latency or offset changes, or a seek, pause, resume or file change happens
> **Then** the log records the master-clock position, the new picture delay, each output's assumed latency split into reported part and type default, and its offset, and how far behind or ahead the picture was presented compared with when it was due
>
> — `spec.md §5, AC-17, verbatim` · full text: [spec.md](../spec.md)

## Checklist

- [ ] Declare `IVideoRenderer` with: `Load(file, nativeWindowHandle, startPaused)` → `Result`, `PlayAt(...)` / `Pause()` / `Seek(position)` / `SetSpeed(double)`, a `PresentedPicture` snapshot (content position + monotonic timestamp it was read at), `ShowNote(text, style)`, `FrameCounters` (late, dropped, skipped-damaged) and events for decode failure, hardware decoding inactive and end of file — `src/DualPlayer.Core/Playback/IVideoRenderer.cs`
- [ ] Keep every value type immutable (records) so the adapter can publish snapshots without locks — same file
- [ ] Write `FakeVideoRenderer`: scripted presentation driven by a `FakeTimeProvider`, honours speed, pause and seek, can inject decode failure, inactive hardware decoding, end of file and late/dropped frames — `tests/DualPlayer.Core.Tests/Fakes/FakeVideoRenderer.cs`
- [ ] Test the fake itself — `tests/DualPlayer.Core.Tests/Fakes/FakeVideoRendererTests.cs`

## Edge cases

| Case | Behaviour |
|---|---|
| `Load` fails (library missing, file rejected) | returns a `Result` error (`mpv.not_found`, `mpv.load_failed`, `video.decode_failed`), never throws |
| Presented position read before the first frame | snapshot says «no frame yet»; no default zero that a sync loop could mistake for a frame |
| Events raised from another thread | the port documents that handlers may run on the renderer's event thread; snapshots are immutable |

## Definition of Done

- [ ] `FakeVideoRendererTests` pass (position follows clock × speed, pause holds, seek jumps, each injected event raised once)
- [ ] no package added to `DualPlayer.Core.csproj`
- [ ] every Hard Rule inlined above still holds
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` are clean
