---
id: T160
title: "Add the run-out listing exception to sad.md and name SCR-03 in T120"
layer: "docs"
deps: []
blocks: ["T23"]
acs: ["AC-08", "AC-09b", "AC-11"]
files_hint: ["docs/features/lip-synced-video/sad.md", "docs/features/lip-synced-video/tasks/T120-drive-the-real-session-through-the-view-model-to-the-renderers-notes-in-an-app-end-to-end-test.md"]
status: "done"
step: "COMMIT"
owner: "Volodymyr Kravchuk"
estimate: "XS"
context_budget: "S"
origin: "review-2026-11-23i (AA7, AA9)"
---

<!-- Follow-up task from review-2026-11-23i. Work from what is inlined here; the source always wins.
Fallback: [spec.md](../spec.md) · [sad.md](../sad.md) · [contracts/cli.md](../contracts/cli.md) · [adr/](../adr/) · [review](../_review/review-2026-11-23i.md) · repo `CLAUDE.md`. Do not guess. -->

# T160 — Add the run-out listing exception to sad.md and name SCR-03 in T120

## Why

- **AA7.** sad.md:543 says remembered files are checked last "reporting each as skipped once per press", contradicting the listing exception T151 added to spec.md:249 and cli.md:85 and the code (`PlaybackSession.cs:878-887` returns `(null, true)`; the scan breaks at :567-571 before `skipped++` at :578).
- **AA9.** T120 (layer `ui`) is the one `ui` task that names no screen id.

Owner decision: fix now.

## Acceptance criteria

See [spec.md](../spec.md) §5 AC-08, AC-09b, AC-11.

## Definition of Done

- [x] sad.md:543 adds the clause: a file whose track listing runs out the budget ends the press, stays remembered, and is neither reported as skipped nor counted in that press, including a remembered file checked last whose new listing hangs. Wording agrees with spec.md:249 and cli.md:85 (read check). *(sad.md §7 scaling line: added the clause after 'reporting each as skipped once per press;' (run-out listing ends the press at once, stays remembered as timed out, not reported as skipped nor counted); matches spec.md:249 and cli.md:85)
- [x] T120's task file names SCR-03 (states: no next file; offset limit) in its trace. *(T120 task file: added a '## Trace' section naming SCR-03 (states: no next file, AC-08; offset limit, AC-11))
- [x] No code or test changes. *(Only the two docs files changed; no code or test changes)
