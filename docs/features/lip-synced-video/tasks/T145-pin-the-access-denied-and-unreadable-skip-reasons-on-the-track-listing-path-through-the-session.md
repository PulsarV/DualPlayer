---
id: T145
title: "Pin the access-denied and unreadable skip reasons on the track-listing path through the session"
layer: "test"
deps: []
blocks: ["T146", "T23"]
acs: ["AC-09b"]
files_hint: ["tests/DualPlayer.Core.Tests/Fakes/SessionRig.cs", "tests/DualPlayer.Core.Tests/Playback/PlaybackSessionFileChangeTests.cs"]
status: "done"
step: "COMMIT"
owner: "Volodymyr Kravchuk"
estimate: "S"
context_budget: "S"
origin: "review-2026-11-23g (X1)"
---

<!-- Follow-up task from review-2026-11-23g. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-23g.md) · repo `CLAUDE.md`. Do not guess. -->

# T145 — Pin the access-denied and unreadable skip reasons on the track-listing path through the session

## Why

- **X1.** For a neighbour the OS refuses to read, the audio decoder's `ListTracks` reports `playback.file_access_denied` first (test-plan.md:75, T66), so the skip reason comes from the listing-failure branch `PlaybackSession.cs:893-896`, not from the picture probe (:1013) that the T142 theory drives. `TrackListingDecoder` (`SessionRig.cs:343-358`) can only succeed. Mutant: :895 hard-coded to `"unreadable"` (the T66 regression on the real path) passes the whole suite. AC-09b; cli.md §2.2 `reason access-denied`; screens.md:110 SCR-03.

Owner decision: fix now.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-09b.

## Definition of Done

- [x] `TrackListingDecoder` gets `Fail(fileName, code, message)` so its `ListTracks` returns that failure.
- [x] New theory in `PlaybackSessionFileChangeTests` where the listing of Episode-02 fails: `playback.file_access_denied` → reason `access-denied` and note `"Skipped Episode-02.mkv: access denied"`; `playback.file_unreadable` → `unreadable` and `"Skipped Episode-02.mkv: file cannot be read"`; each asserts `FileSkipped.Reason`, the exact `NoteShown` text with `NoteStyle.Warning`, and that Episode-03 plays next. *(`A_file_whose_track_listing_fails_is_skipped_with_the_decoders_reason_and_a_warning_note_AC_09b`.)*
- [x] With `PlaybackSession.cs:895` hard-coded to `"unreadable"` (scratch copy), the access-denied row fails. *(1 of 2 cases fails — the access-denied row; reverted.)*
- [x] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean *(Build 0 warnings; tests Core 626, Media 174, Console 151, App 166, Windows 41, MacOS 12 passed; format clean.)*
