---
id: T135
title: "Bring the SCR-01 wireframe, ux-flows, sad flow 1, StartErrors and T20's trace in step with the next-line order and the Esc-during-start path"
layer: "docs"
deps: ["T132", "T133", "T134"]
blocks: ["T23"]
acs: ["AC-01", "AC-02", "AC-06", "AC-07"]
files_hint: ["docs/features/lip-synced-video/screens.md", "docs/features/lip-synced-video/ux-flows.md", "docs/features/lip-synced-video/sad.md", "docs/features/lip-synced-video/test-plan.md", "docs/features/lip-synced-video/tasks.json", "docs/features/lip-synced-video/tasks/T20-playback-window-keys-and-notes.md", "docs/features/lip-synced-video/tasks/tracker.md", "src/DualPlayer.App/Startup/StartErrors.cs", "tests/DualPlayer.App.Tests/E2E/PlayEndToEndTests.cs"]
status: "done"
step: "COMMIT"
owner: "Volodymyr Kravchuk"
estimate: "S"
context_budget: "S"
origin: "review-2026-11-23c (W5, W6, W8, W9, W10)"
---

<!-- Follow-up task from review-2026-11-23c. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-23c.md) · repo `CLAUDE.md`. Do not guess. -->

# T135 — Bring the SCR-01 wireframe, ux-flows, sad flow 1, StartErrors and T20's trace in step with the next-line order and the Esc-during-start path

## Why

- **W5.** After T130 the `next` line comes before the old film's `run-summary … reason file-change` (`PlaybackSession.cs:649`, cli.md:68), but the SCR-01 wireframe (`screens.md:61-64`) shows the old order, and `PlayEndToEndTests.cs:130-138` does not compare `next` with the run-summary line.
- **W6.** Rest of Z8: `ux-flows.md:159` ("Esc during the start closes with no error line (E5)") and the `AfterSessionStart` summary in `src/DualPlayer.App/Startup/StartErrors.cs:18-19` ("(Esc during the start)") still say Esc during the start ends with no error line.
- **W8.** sad flow 1 draws the `picture delay … play from the start` block and the presented-position / picture-timing steps (`sad.md:253-259`) after the `end` that closes `start succeeds / any session-start failure`.
- **W9.** AC-02's window branch (ux-flows E4, SCR-02 `loading — opening`) reaches no `ui` task: T20 does not list AC-02 (`tasks.json:419-427`).
- **W10.** The AC-07 row of the ux-flows AC table (`ux-flows.md:161`) says "C yes → RS → NEW"; the flow (`:101-102`) is C → AN → RS.

Owner decision: fix now.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-01, AC-02, AC-06, AC-07.

## Definition of Done

- [x] SCR-01's wireframe shows `next     Episode-04.mkv` above `run-summary  file Episode-01.mkv  reason file-change`.
- [x] `PlayEndToEndTests` asserts the `next` line comes before the old file's `run-summary … reason file-change` line; test-plan.md cites it for AC-07's order.
- [x] ux-flows.md:159 and the `StartErrors.AfterSessionStart` summary say: the OS closing the window during the start exits 0 with no error line; Esc during the start acts once the start returns (a failed start still reports its error).
- [x] sad flow 1's picture-delay block and the presented-position / picture-timing steps sit inside the `start succeeds` branch.
- [x] AC-02 is in T20's `acs` in tasks.json and in its task file.
- [x] The ux-flows AC-07 row reads "C yes → AN → RS → NEW; RS → F → END3" (or matches the flow's node names exactly).
- [x] `dotnet build DualPlayer.slnx` (warnings are errors), `dotnet test DualPlayer.slnx` and `dotnet format DualPlayer.slnx --verify-no-changes` clean
