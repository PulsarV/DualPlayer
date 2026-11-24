---
status: Accepted
owner: "Volodymyr Kravchuk"
reviewers: []
updated_at: "2026-11-20"
feature_size: "L"
ticket: ""
---

# 0001 — Host the start command in the App executable

- **Status:** Accepted
- **Date:** 2026-11-20
- **Deciders:** Volodymyr Kravchuk

## Context

The Developer starts a film from a terminal with a command that names the file, a track and a device per output, and optionally a screen. Start errors, run summaries and the end of the run go back to that terminal (SCR-01), and the Viewers see a fullscreen window (SCR-02). The step-3 Developer console (`DualPlayer.Console`) has no Avalonia dependency by design (two-outputs-one-clock ADR-0001). A GUI-subsystem Windows executable cannot write to the terminal it was started from. This decision also sets `target_surfaces` in `sad.md`, which `api`, `tasks` and `plan-tests` read.

## Decision drivers

- Spec AC-01, AC-02, AC-03, AC-03b: errors and the list of connected screens are told to the Developer in plain words; AC-06: the run summary is printed and logged.
- `CLAUDE.md` module table: `DualPlayer.Console` may reference everything but App; App is the composition root for the UI.
- One process must hold the audio engine and the video renderer together (one master clock, spec §1).

## Considered options

1. **App executable takes the start command** — `DualPlayer.App` parses the start arguments, is built as a console-subsystem executable for this step, prints errors, summaries and exit codes to its terminal, and opens the fullscreen window. Surfaces `[desktop-app, cli]`.
2. **App executable only, start arguments as an internal detail** — same host, but no separate command contract. Surface `[desktop-app]`.
3. **Console launches an App process** — `DualPlayer.Console play … --video` starts App as a child process and relays its messages; App stays a pure GUI executable.

## Decision outcome

**Chosen:** Option 1. It keeps the engine, the renderer and the start contract in one process and one composition root. It satisfies the terminal ACs without inter-process plumbing, and it gives the start command a written contract (`contracts/cli.md` from the `api` stage), so the error ACs are testable as command behaviour.

## Consequences

**Positive**
- One process: audio engine, master clock, renderer and window share one lifetime and one shutdown path.
- The start command, its errors and exit codes become an explicit contract that `api` writes and end-to-end command tests exercise.
- `DualPlayer.Console` and its smoke test stay untouched.

**Negative**
- As a console-subsystem executable, App opens a console window when started by double-click. This is acceptable while only the Developer starts films, and is revisited when step 8/13 turn App into the Viewers' entry point (WinExe).
- App now has two jobs: parse and report like a command-line tool, and run a window. Startup validation must finish before the window opens (AC-02, AC-03b: no window stays open on error).

**Neutral**
- Run-summary printing reuses the step-3 summary builder; the console renderer for it is shared or copied into App, a choice left to `tasks`.

## Links

- Spec: [[../spec.md]] US-01, AC-01, AC-02, AC-03, AC-03b, AC-06
- SAD: [[../sad.md]] §4
- Related ADR: [[0003-embed-mpv-video-output-in-a-native-child-window-and-draw-notes-with-its-osd]]; two-outputs-one-clock ADR-0001
