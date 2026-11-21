---
id: T61
title: "Return or change file after every output was lost on whichever devices are back"
layer: "app"
deps: ["T60"]
blocks: []
acs: ["AC-15b", "AC-05", "AC-07"]
files_hint: ["src/DualPlayer.Core/Playback/PlaybackSession.cs", "tests/DualPlayer.Core.Tests/Playback/", "tests/DualPlayer.Core.Tests/Fakes/"]
status: "todo"
step: "SELECT"
owner: "Volodymyr Kravchuk"
estimate: "M"
context_budget: "M"
origin: "review-2026-11-21c (S2)"
---

<!-- Follow-up task from review-2026-11-21c. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-21c.md) · repo `CLAUDE.md`. Do not guess. -->

# T61 — Return or change file after every output was lost on whichever devices are back

## Why

S2: after AC-15b the retry runs `StartCore(keepLost: false)` (`PlaybackSession.cs:326-328`, Page Down/Up at `:547`), and `StartCore` fails with `outputs.device_not_found` on the first device missing from the list (`:216-219`). Headset A died earlier, B dropped and the film ended, B is back → "Could not go back: … is not a connected playback device"; Page Down fails the same way. Contradicts T49's DoD ("plays on every device that is back") and `screens.md:114` (note only when every device is still gone).

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-15b, AC-05, AC-07.

## Definition of Done

- [ ] In the retry after every output was lost (← and Page Down/Up), a device missing from the list is treated as lost — it keeps its output number and estimate, as `keepLost` does — instead of failing the start.
- [ ] The retry fails with the existing note and log entry only when no device resolves.
- [ ] Tests: two outputs lost, one relisted and one still unlisted, then ← → the film plays from the end position on the relisted device, the other stays lost; the same for Page Down (the next file plays on the relisted device); none relisted → note, Ended.
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
