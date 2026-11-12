---
id: T32
title: "Platform: OS-gated hardware tests, device-loss mapping only for device errors, and a logged MMCSS failure"
layer: "infra"
deps: []
blocks: []
acs: ["AC-06", "AC-07"]
files_hint: ["tests/Shared/HardwareFactAttribute.cs", "tests/DualPlayer.Platform.Windows.Tests/", "tests/DualPlayer.Platform.MacOS.Tests/", "src/DualPlayer.Platform.Windows/Audio/WasapiErrorMapper.cs", "src/DualPlayer.Platform.Windows/Audio/WasapiRenderThread.cs", "src/DualPlayer.Platform.Windows/Audio/WasapiAudioDevice.cs"]
owner: "Volodymyr Kravchuk"
estimate: "M"
context_budget: "M"
origin: "review-2026-11-12 (F17, F18, F23)"
status: "done"
step: "COMMIT"
---

<!-- Follow-up task from the independent review. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [cli.md](../contracts/cli.md) · [adr/](../adr/) ·
[test-plan.md](../test-plan.md) · [review](../_review/review-2026-11-12.md) · repo `CLAUDE.md`. Do not guess. -->

# T32 — Platform: OS-gated hardware tests, device-loss mapping only for device errors, and a logged MMCSS failure

## Why (review findings)

### F17 — stage-2, medium

> Hardware tests are not gated by OS: with `DUALPLAYER_HARDWARE=1` AudioQueue tests fail on Windows and WASAPI tests fail on macOS; `HardwareGateTests` asserts Windows; a macOS registration fact silently passes on Windows.
>
> **Where:** `tests/DualPlayer.Platform.MacOS.Tests/AudioQueueBackendTests.cs:25-28,50-105`, `tests/DualPlayer.Platform.Windows.Tests/WasapiAudioBackendTests.cs:83-123`, `tests/DualPlayer.Platform.Windows.Tests/WasapiRenderTests.cs:38-125`, `tests/DualPlayer.Platform.Windows.Tests/HardwareGateTests.cs:7-9`
>
> **Suggested:** Add an OS parameter to `HardwareFact` (or `Assert.SkipUnless`); fix the vacuous fact and `HardwareGateTests`.
>
> — `_review/review-2026-11-12.md, F17, verbatim`

### F18 — stage-2, medium

> The render loop maps every `ArgumentException` (incl. `ArgumentOutOfRangeException` from a Core Span bug) to a device failure, logged at Warning as device loss.
>
> **Where:** `src/DualPlayer.Platform.Windows/Audio/WasapiErrorMapper.cs:32-33`, `src/DualPlayer.Platform.Windows/Audio/WasapiRenderThread.cs:132-146`
>
> **Suggested:** In the render loop treat only COM/HRESULT device errors as device loss; keep the ArgumentException mapping around `GetDevice` only.
>
> — `_review/review-2026-11-12.md, F18, verbatim`

### F23 — stage-2, low

> A failed `AvSetMmThreadCharacteristics` is not logged.
>
> **Where:** `src/DualPlayer.Platform.Windows/Audio/WasapiRenderThread.cs:105,149`
>
> **Suggested:** Log a warning with the last P/Invoke error (`SetLastError = true`).
>
> — `_review/review-2026-11-12.md, F23, verbatim`

## Acceptance criteria

> ### AC-06 (US-05) — domain invariant
> 
> **Given** two audio outputs are playing
> **When** the second output's device disappears (switched off, unplugged, out of range)
> **Then** the first output keeps playing without a gap, the master clock keeps running, the second output stops, and the console and the log record which device was lost and at which master-clock position; the lost output is not reconnected automatically. With three or more outputs, each further loss behaves the same way
>
> — `spec.md §5, AC-06, verbatim`

> ### AC-07 (US-03) — happy path (long run)
> 
> **Given** two audio outputs play on devices whose clocks run at slightly different speeds
> **When** playback runs uninterrupted for at least 3 hours with no action from the Developer
> **Then** each output stays within the steady-state drift tolerance of the master clock (§6), and every excursion beyond that tolerance is recorded once — from the moment it starts until the output is back within tolerance — with its device, peak size, duration and the master-clock position where it started
>
> — `spec.md §5, AC-07, verbatim`

## Hard rules (unchanged)

- Real-time code (`Render`, resampler, ring read) never allocates, locks, logs, awaits or throws — `sad.md §8`.
- No `DateTime.Now` / `Stopwatch` in Core; time from `TimeProvider` or the master clock. Expected failures return `Result` / domain events, never thrown across module boundaries — repo `CLAUDE.md`.
- Never weaken an existing test to make it pass; update a test only where it encoded the bug named above.

## Definition of Done

- [ ] with `DUALPLAYER_HARDWARE=1` Windows-only hardware tests are skipped on macOS and macOS-only ones on Windows (`HardwareFact` takes an OS); the vacuous macOS registration fact skips instead of passing on Windows; an `ArgumentOutOfRangeException` from `Render` is logged as a render-loop bug (Error), not a device loss, while COM/HRESULT device errors still raise device-lost (unit test on the mapper); a failed `AvSetMmThreadCharacteristics` logs a warning with the Win32 error
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
