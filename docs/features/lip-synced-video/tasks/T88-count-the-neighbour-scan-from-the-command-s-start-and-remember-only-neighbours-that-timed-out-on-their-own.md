---
id: T88
title: "Count the neighbour scan from the command's start and remember only neighbours that timed out on their own"
layer: "app"
deps: []
blocks: []
acs: ["AC-07", "AC-08", "AC-09b"]
files_hint: ["src/DualPlayer.Core/Playback/PlaybackSession.cs", "src/DualPlayer.Core/Playback/", "src/DualPlayer.App/ViewModels/PlaybackControls.cs", "tests/DualPlayer.Core.Tests/Playback/", "tests/DualPlayer.App.Tests/ViewModels/", "docs/features/lip-synced-video/spec.md", "docs/features/lip-synced-video/contracts/cli.md"]
status: "todo"
step: "—"
owner: "Volodymyr Kravchuk"
estimate: "M"
context_budget: "M"
origin: "review-2026-11-22b (S1, S8, S9)"
---

<!-- Follow-up task from review-2026-11-22b. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-22b.md) · repo `CLAUDE.md`. Do not guess. -->

# T88 — Count the neighbour scan from the command's start and remember only neighbours that timed out on their own

## Why

**S1** (all three reviewers). When a press runs out of time, T84 remembers every file it gave up on in `_timedOut`, including a healthy file whose check was cut short by the 3 s budget of the whole press. That happens because `gaveUp = usedUp` is set even when `cut` (`PlaybackSession.cs:731-740`), and a listing started with little budget left is given up at the shared deadline (`:689-694`). On the next press the file is stepped over (`:530-532`) and reported as `skipped … reason unreadable` / "file cannot be read". Example: E2 hangs its full 2 s, healthy E3 gets 0.9 s, and press 2 plays E4. A file first skipped as `not-hardware-decodable` also turns into `unreadable`. This breaks AC-08 ("a file whose probe was cut short by the budget is not reported as skipped") and AC-07 (the next playable file plays).

**S8.** T85 passes the key-arrival stamp into `ChangeFile` (`PlaybackControls.cs:67`), where it also starts the 3 s neighbour-scan budget (`PlaybackSession.cs:506, 681-685`). A file change holds the session for about 3 s (scan + load + BT pre-roll), so Page Downs queued behind it report "Next file not found within 3 s (checked 0)" without checking anything. With S1, a queued press that keeps a few hundred ms also remembers a healthy file as timed out.

**S9.** `_timedOut` is keyed by path only (CLAUDE.md IDs: media files are keyed by normalized absolute path + size + mtime) and is cleared only on a successful file change (`:65, :634`). If the only next neighbour sits on a sleeping disk or is still being copied, one timeout makes it "file cannot be read" for the rest of the session.

Owner decision (all three): fix now in one task.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-07, AC-08, AC-09b.

## Definition of Done

- [ ] A file enters the timed-out memory only when its own listing or probe ran its full, uncut `PictureProbeLimit`; a file whose check the press budget cut is neither remembered nor reported as skipped (the worker is still cancelled on every give-up).
- [ ] Files already judged for the current film keep their verdict and original reason on later presses (a `not-hardware-decodable` file stays so; nothing is reported `unreadable` unless that was its verdict) and spend no budget again.
- [ ] The timed-out memory is keyed by (normalized path, size, mtime); remembered files are checked last when the press still has budget (or forgotten after one step-past), so a file that recovers plays on a later press; a press that only stepped past remembered files never reports a file nobody judged as the reason for `no-next-file`.
- [ ] The neighbour-scan budget counts from when the session starts the command; the key-arrival stamp is still used for `_resync.At` (T85 recovery timing unchanged).
- [ ] Tests: hanging E2 + slow but healthy E3 → press 2 plays E3; a press queued behind a slow file change still checks files; a remembered file that recovers (new mtime or answers in time) plays; a skipped file keeps its original reason on the next press.
- [ ] spec §6.1 and cli.md §2.2 state the rule (what is remembered, the key, when it is retried, and that the scan budget counts from the command's start).
- [ ] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
