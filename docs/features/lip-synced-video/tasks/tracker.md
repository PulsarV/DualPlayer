# Tracker — lip-synced-video

> Status of every task in the epic. `implement` updates **Status** and **Step** after every
> subagent it runs (and commits them with the task), so this table shows where the run is right now.
> Status: `todo` · `in_progress` · `blocked` · `review` · `done`.
> Step: `SELECT` · `RED` · `GREEN` · `REFACTOR` · `GATE` · `REVIEW` · `COMMIT` (`—` before the task starts).
> A `blocked` row carries its reason in Step, e.g. `GATE — lint red after 3 retries`.

| # | Task | Layer | Owner | Estimate | Blocked by | Status | Step |
|---|---|---|---|---|---|---|---|
| T1 | [Define the IVideoRenderer port and a scripted fake renderer in Core](./T1-define-the-video-renderer-port.md) | domain | Volodymyr Kravchuk | S | — | done | COMMIT |
| T2 | [Fetch a pinned libmpv, copy it beside the executables, and load it with a version check](./T2-fetch-and-load-libmpv.md) | infra | Volodymyr Kravchuk | M | — | done | COMMIT |
| T3 | [Steer the picture to clock minus picture delay with a Core picture-sync loop](./T3-picture-sync-loop.md) | domain | Volodymyr Kravchuk | M | T1 | done | COMMIT |
| T4 | [Implement the hardened libmpv video renderer with presented-position snapshots and OSD notes](./T4-libmpv-video-renderer.md) | infra | Volodymyr Kravchuk | L | T1, T2 | done | COMMIT |
| T5 | [Spike libmpv precision on the target PC against the ADR-0002 kill criterion and record the D1 verdict](./T5-spike-libmpv-precision-on-the-target-pc.md) | tests | Volodymyr Kravchuk | M | T3, T4 | done | COMMIT |
| T6 | [Model device types, type defaults, assumed latency, picture delay and lags behind a latency-probe port](./T6-latency-model-and-device-types.md) | domain | Volodymyr Kravchuk | M | T5 | done | COMMIT |
| T7 | [Persist per-device offsets in device-offsets.json and keep them in an offset book with clamp, step and debounced save](./T7-device-offsets-file-and-offset-book.md) | infra | Volodymyr Kravchuk | M | T5 | done | COMMIT |
| T8 | [List a file's folder neighbours (.mkv/.mp4) in natural name order for next and previous](./T8-folder-neighbours-in-natural-order.md) | domain | Volodymyr Kravchuk | S | T5 | done | COMMIT |
| T9 | [Probe a file's picture with FFmpeg in hardware, off the caller's thread, with a time limit](./T9-ffmpeg-picture-probe.md) | infra | Volodymyr Kravchuk | M | T5 | done | COMMIT |
| T10 | [Report each device's connection kind, form factor and hidden latency on Windows and the dev Mac](./T10-device-latency-probes.md) | infra | Volodymyr Kravchuk | M | T6 | done | COMMIT |
| T11 | [Measure each output's drift against clock minus its lag and ramp lag changes at up to 2000 ppm](./T11-lag-ramp-in-the-drift-controller.md) | domain | Volodymyr Kravchuk | M | T5 | done | COMMIT |
| T12 | [Give the audio engine per-output start lags in the pre-roll and lag targets that ramp or jump](./T12-engine-lag-hooks.md) | app | Volodymyr Kravchuk | M | T11 | done | COMMIT |
| T13 | [Start a lip-synced playback session, end it at the natural end or on exit, and emit session events](./T13-playback-session-start-end-exit.md) | app | Volodymyr Kravchuk | L | T3, T6, T7, T12 | done | COMMIT |
| T14 | [Pause, resume and seek picture and every output together in the session](./T14-session-pause-resume-seek.md) | app | Volodymyr Kravchuk | M | T13 | done | COMMIT |
| T15 | [Apply offset keys per output and move picture and unchanged outputs to a new picture delay together](./T15-session-offset-keys-and-picture-delay-moves.md) | app | Volodymyr Kravchuk | M | T14 | done | COMMIT |
| T16 | [Keep playing when an output's device is lost, and end the film when the last one is lost](./T16-session-device-loss.md) | app | Volodymyr Kravchuk | S | T15 | done | COMMIT |
| T17 | [Move to the next or previous playable file in the folder, skipping files that cannot play](./T17-session-next-and-previous-file.md) | app | Volodymyr Kravchuk | M | T8, T9, T13 | todo | — |
| T18 | [Skip damaged frames, end on an unrecoverable picture, and log picture timing and frame counters](./T18-damaged-picture-and-lip-sync-log.md) | app | Volodymyr Kravchuk | M | T13 | todo | — |
| T19 | [Parse and validate the play start command in the App executable before any window opens](./T19-start-command-parse-and-validate.md) | ports | Volodymyr Kravchuk | M | T9 | done | COMMIT |
| T20 | [Build the fullscreen playback window hosting mpv's video, mapping the fixed keys and styling notes from tokens](./T20-playback-window-keys-and-notes.md) | ui | Volodymyr Kravchuk | M | T4, T15, T17 | todo | — |
| T21 | [Print start lines, warnings, running lines and run summaries to the terminal and the log](./T21-terminal-lines-and-run-summaries.md) | ports | Volodymyr Kravchuk | M | T13, T19 | done | COMMIT |
| T22 | [Wire the App composition root and host: validate, open the window on the screen, run the session, exit with the right code](./T22-wire-the-app-composition-root-and-host.md) | wiring | Volodymyr Kravchuk | M | T10, T16, T18, T20, T21 | todo | — |
| T23 | [Measure lip-sync per output on the target PC with a click-and-flash video and record the acceptance evidence](./T23-measure-lip-sync-on-the-target-pc.md) | tests | Volodymyr Kravchuk | M | T22 | todo | — |

**Total:** 23 tasks, ~21.5 person-days (S = ½ day, M/L = 1 day; T5 and T23 also need the owner's time on the target PC).
