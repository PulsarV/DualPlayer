---
id: T15
title: "Convert each output's track to its device mix format with swresample and a fold-down matrix"
layer: "infra"
deps: ["T14"]
blocks: ["T22"]
acs: ["AC-12"]
files_hint: ["src/DualPlayer.Media/Audio/MixFormatConverter.cs", "src/DualPlayer.Media/Audio/FoldDownMatrix.cs", "src/DualPlayer.Media/Audio/FfmpegDecodeSession.cs", "tests/DualPlayer.Media.Tests/MixFormatConverterTests.cs"]
owner: "Volodymyr Kravchuk"
estimate: "M"
context_budget: "M"
status: "todo"
step: ""
---

<!-- Self-contained task: work from what is inlined here. Every quoted chunk ends with its provenance
signature (`<file> §<section>, <identifier>, verbatim|abridged`); the source always wins. If a slice is
insufficient, ambiguous, or contradicts the code in front of you, open the named file for the full
text and follow that. Do not invent the missing part. -->

# T15 — Convert each output's track to its device mix format with swresample and a fold-down matrix

## Place in the sequence

- **Blocked by:** T14 — List a file's audio tracks and decode the chosen ones once with FFmpeg
- **Blocks:** T22 — Record the ≥ 3 h evidence run on the target Windows PC and commit its run summary
- **Wave:** 3 — after its deepest dependency finishes (wave 2).
- **Lane:** shares `tests/DualPlayer.Media.Tests/` with T3 — serialized; shares `src/DualPlayer.Media/Audio/FfmpegDecodeSession.cs` with T14 — serialized.

## Why (user story)

> **As a** Developer
> **I want** to play a 5.1 or 7.1 track on a stereo device
> **So that** I can test the engine with real films, not only specially prepared files
>
> — `spec.md §4, US-07, verbatim` · full text: [spec.md](../spec.md)

This task lets real films with 5.1/7.1 or 44.1 kHz tracks play correctly on stereo 48 kHz devices.

## Inlined context

> **Chosen:** FFmpeg swresample in `DualPlayer.Media` — Core gives the decoder each output's target format (from the backend, ADR-0004); Media converts rate and layout in one pass with a custom rematrix (LFE weight 0, mono to both fronts, no upmix) and hands Core frames already in the device format. … the fold-down rules are expressed as a matrix, so roadmap step 5's dialogue-normalized downmix can replace the matrix without moving code.
>
> — `adr/0005 §Considered options option 1 + §Decision outcome, abridged` · full text: [ADR-0005](../adr/0005-convert-tracks-to-the-device-mix-format-with-ffmpeg-swresample-in-media.md)

> - One converter per output, so a track on two devices is converted twice.
> - Fold-down correctness is verified by tests in `DualPlayer.Media` that need FFmpeg installed (Homebrew on the dev Mac, shipped DLLs on Windows).
>
> — `adr/0005 §Consequences, Negative, verbatim` · full text: [ADR-0005](../adr/0005-convert-tracks-to-the-device-mix-format-with-ffmpeg-swresample-in-media.md)

> - Own DSP code in Core: a fractional-rate resampler (cubic interpolation is sufficient for ratios within a few hundred ppm) must be written and checked for audible artefacts.
> - Render-thread CPU grows with the resampler; counted against the ≤ 5 % budget.
> - Sample-rate and channel-layout conversion (e.g. 44.1 → 48 kHz, 5.1 → stereo) stays in `DualPlayer.Media` before the ring (§5); only the near-unity drift ratio is applied after it.
>
> — `adr/0003 §Consequences, Negative + Neutral, verbatim` · full text: [ADR-0003](../adr/0003-correct-drift-with-a-managed-adaptive-resampler-after-the-ring-buffer.md)

```text
X-->>S: device mix format - sample rate and channel layout
S->>S: set up this output's converter from track format to mix format
loop while decoding
    S->>S: decode and resample to the device rate
    alt track has more channels than the device
        S->>S: fold every channel except LFE into the device layout
    else track has fewer channels than the device
        S->>S: route to the front channels only, mono to both, no upmix
    else layouts match
        S->>S: pass the channels through
    S->>S: write the frames into the output's ring buffer
```

— `sad.md §6, Flow 8 «convert a track to its device format», sequence lines, abridged` · full text: [sad.md](../sad.md)

**Fallback:** insufficient or contradicted by the code → read the named file in full ([spec.md](../spec.md) · [sad.md](../sad.md) · [cli.md](../contracts/cli.md) · [adr/](../adr/) · [CONTEXT.md](../../../../CONTEXT.md)) and follow it. Do not guess.

## Data delta

No DB changes.

## API contract

> ```
> output 1  track 1 (ukr 5.1 -> stereo, LFE dropped)  device Test Headset A  id {…0001}
> output 2  track 2 (eng stereo 44100 -> 48000)        device Test Headset B  id {…0002}
> ```
>
> — `contracts/cli.md §1.3, start lines, abridged` · full text: [cli.md](../contracts/cli.md)

The converter exposes the conversion description shown in parentheses; the console prints it (T19).

## Acceptance criteria

### AC-12 — cross-context

> **Given** a track's channel layout or sample rate differs from what its device plays natively (for example a 5.1 track on stereo headphones, or a 44.1 kHz track on a 48 kHz device)
> **When** the Developer plays that track on that device
> **Then** the Viewer hears all channels except the low-frequency effects (LFE, ".1") channel folded into the device's layout at the correct pitch and speed, and the output still meets the drift tolerance (§6); a track with fewer channels than the device plays on the device's front channels only (a mono track on both front channels), with no upmix
>
> — `spec.md §5, AC-12, verbatim` · full text: [spec.md](../spec.md)

## Checklist

- [ ] `FoldDownMatrix` — builds the rematrix from source to device layout: LFE weight 0, every other channel folded in, mono to both fronts, fewer channels to front channels only, no upmix — `src/DualPlayer.Media/Audio/FoldDownMatrix.cs`
- [ ] `MixFormatConverter` — one `SwrContext` per output (rate + layout + float), custom matrix via `swr_set_matrix`, conversion description string — `MixFormatConverter.cs`
- [ ] Wire one converter per output into `FfmpegDecodeSession` so frames leave Media in device format — `FfmpegDecodeSession.cs`
- [ ] Tests with per-channel distinct tones — `tests/DualPlayer.Media.Tests/MixFormatConverterTests.cs`

## Edge cases

| Case | Behaviour |
|---|---|
| 5.1 / 7.1 on stereo | all channels except LFE audible in L/R; LFE tone absent |
| 44.1 kHz on 48 kHz | tone frequency unchanged (correct pitch and speed) |
| Mono on stereo | same signal on both fronts |
| Stereo or mono on a 5.1 device | front channels only; centre, surround, LFE silent |
| Same track on two devices | two independent converters |

## Definition of Done

- [ ] `MixFormatConverterTests` pass for every Edge-case row on the dev Mac and in CI
- [ ] every Hard Rule inlined above still holds
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
