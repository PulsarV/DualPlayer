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
| T17 | [Move to the next or previous playable file in the folder, skipping files that cannot play](./T17-session-next-and-previous-file.md) | app | Volodymyr Kravchuk | M | T8, T9, T13 | done | COMMIT |
| T18 | [Skip damaged frames, end on an unrecoverable picture, and log picture timing and frame counters](./T18-damaged-picture-and-lip-sync-log.md) | app | Volodymyr Kravchuk | M | T13 | done | COMMIT |
| T19 | [Parse and validate the play start command in the App executable before any window opens](./T19-start-command-parse-and-validate.md) | ports | Volodymyr Kravchuk | M | T9 | done | COMMIT |
| T20 | [Build the fullscreen playback window hosting mpv's video, mapping the fixed keys and styling notes from tokens](./T20-playback-window-keys-and-notes.md) | ui | Volodymyr Kravchuk | M | T4, T15, T17 | done | COMMIT |
| T21 | [Print start lines, warnings, running lines and run summaries to the terminal and the log](./T21-terminal-lines-and-run-summaries.md) | ports | Volodymyr Kravchuk | M | T13, T19 | done | COMMIT |
| T22 | [Wire the App composition root and host: validate, open the window on the screen, run the session, exit with the right code](./T22-wire-the-app-composition-root-and-host.md) | wiring | Volodymyr Kravchuk | M | T10, T16, T18, T20, T21 | done | COMMIT |
| T23 | [Measure lip-sync per output on the target PC with a click-and-flash video and record the acceptance evidence](./T23-measure-lip-sync-on-the-target-pc.md) | tests | Volodymyr Kravchuk | M | T22, T24–T58 | blocked | owner run on the target PC (script + run guide ready) |
| T24 | [Run session commands and callbacks serialized on one thread off the UI thread](./T24-run-session-commands-and-callbacks-serialized-on-one-thread-off-the-ui-thread.md) | app | Volodymyr Kravchuk | L | — | done | COMMIT |
| T25 | [Tick the picture-sync loop in production and measure picture error at the snapshot's own time](./T25-tick-the-picture-sync-loop-in-production-and-measure-picture-error-at-the-snapshot-s-own-t.md) | app | Volodymyr Kravchuk | M | T24 | done | COMMIT |
| T26 | [Re-align the picture on a resume after a pause, resume and pause before the freeze](./T26-re-align-the-picture-on-a-resume-after-a-pause-resume-and-pause-before-the-freeze.md) | app | Volodymyr Kravchuk | S | T25 | done | COMMIT |
| T27 | [Keep the validated devices across file changes and surface a failed file change](./T27-keep-the-validated-devices-across-file-changes-and-surface-a-failed-file-change.md) | app | Volodymyr Kravchuk | M | T26 | done | COMMIT |
| T28 | [Hold the last frame at the end and on a seek past it, and return into the film without a restart](./T28-hold-the-last-frame-at-the-end-and-on-a-seek-past-it-and-return-into-the-film-without-a-re.md) | integration | Volodymyr Kravchuk | M | T27 | done | COMMIT |
| T29 | [Log the right end reason, the start timings as measured, and the missing session records](./T29-log-the-right-end-reason-the-start-timings-as-measured-and-the-missing-session-records.md) | app | Volodymyr Kravchuk | M | T28 | done | COMMIT |
| T30 | [Count a frame late when it is presented more than 20 ms after its due time](./T30-count-a-frame-late-when-it-is-presented-more-than-20-ms-after-its-due-time.md) | integration | Volodymyr Kravchuk | M | T29 | done | COMMIT |
| T31 | [Draw OSD notes from the OsdNote tokens at the bottom centre](./T31-draw-osd-notes-from-the-osdnote-tokens-at-the-bottom-centre.md) | ui | Volodymyr Kravchuk | S | T30 | done | COMMIT |
| T32 | [Print the terminal lines exactly as cli.md specifies](./T32-print-the-terminal-lines-exactly-as-cli-md-specifies.md) | ports | Volodymyr Kravchuk | M | T29 | done | COMMIT |
| T33 | [Reject an offsets file without the offsets key and clamp huge stored offsets to the right bound](./T33-reject-an-offsets-file-without-the-offsets-key-and-clamp-huge-stored-offsets-to-the-right.md) | infra | Volodymyr Kravchuk | S | T32 | done | COMMIT |
| T34 | [Check libmpv before the window opens, log start errors, and let Esc during the start exit cleanly](./T34-check-libmpv-before-the-window-opens-log-start-errors-and-let-esc-during-the-start-exit-cl.md) | wiring | Volodymyr Kravchuk | M | T24 | done | COMMIT |
| T35 | [Guard the session's timer callbacks and start the clock from the outputs still connected](./T35-guard-the-session-s-timer-callbacks-and-start-the-clock-from-the-outputs-still-connected.md) | app | Volodymyr Kravchuk | S | T27 | done | COMMIT |
| T36 | [Restore the strict EngineRig barrier for the engine tests](./T36-restore-the-strict-enginerig-barrier-for-the-engine-tests.md) | test | Volodymyr Kravchuk | S | — | done | COMMIT |
| T37 | [Decorate the video renderer independent of registration order](./T37-decorate-the-video-renderer-independent-of-registration-order.md) | wiring | Volodymyr Kravchuk | S | T34 | done | COMMIT |
| T38 | [Add the App-entry, end-to-end and window key tests the test plan lists](./T38-add-the-app-entry-end-to-end-and-window-key-tests-the-test-plan-lists.md) | test | Volodymyr Kravchuk | L | T27, T28, T31, T33, T35, T37 | done | COMMIT |
| T39 | [Make exit terminal so no queued command runs after it](./T39-make-exit-terminal-so-no-queued-command-runs-after-it.md) | app | Volodymyr Kravchuk | S | — | done | COMMIT |
| T40 | [Keep a usable ended session when a file change or a return into the film fails](./T40-keep-a-usable-ended-session-when-a-file-change-or-a-return-into-the-film-fails.md) | app | Volodymyr Kravchuk | M | T39 | done | COMMIT |
| T41 | [Return into the film as a seek with an engine start position, one start line and a carried-over summary](./T41-return-into-the-film-as-a-seek-with-an-engine-start-position-one-start-line-and-a-carried.md) | app | Volodymyr Kravchuk | L | T40 | done | COMMIT |
| T42 | [Publish start timings without lost outputs, time a file change from its key and print the next-file line first](./T42-publish-start-timings-without-lost-outputs-time-a-file-change-from-its-key-and-print-the-n.md) | app | Volodymyr Kravchuk | M | T41 | done | COMMIT |
| T43 | [Hold the sync loop until a pending align and drop stale renderer events](./T43-hold-the-sync-loop-until-a-pending-align-and-drop-stale-renderer-events.md) | app | Volodymyr Kravchuk | M | T42 | done | COMMIT |
| T44 | [Judge lip-synced again on the picture and every output and log the output errors](./T44-judge-lip-synced-again-on-the-picture-and-every-output-and-log-the-output-errors.md) | app | Volodymyr Kravchuk | M | T43 | done | COMMIT |
| T45 | [Show one combined note when every neighbour is skipped](./T45-show-one-combined-note-when-every-neighbour-is-skipped.md) | app | Volodymyr Kravchuk | S | T44 | done | COMMIT |
| T46 | [Close the remaining test gaps: real skip samples, the click-and-flash probe, App entry, real tokens and the Windows access-denied skip](./T46-close-the-remaining-test-gaps-real-skip-samples-the-click-and-flash-probe-app-entry-real-t.md) | test | Volodymyr Kravchuk | L | T45 | done | COMMIT |
| T47 | [Add the target-PC load scenarios to the T23 run guide](./T47-add-the-target-pc-load-scenarios-to-the-t23-run-guide.md) | docs | Volodymyr Kravchuk | S | — | done | COMMIT |
| T48 | [Return into the film only after a run of that file started, carry a summary only for its own file, and bump the film token before Load](./T48-return-into-the-film-only-after-a-run-of-that-file-started-carry-a-summary-only-for-its-ow.md) | app | Volodymyr Kravchuk | M | — | blocked | REVIEW — unresolved blocking review issues and gate red (flaky Core test) |
| T49 | [Let a return into the film retry every device after all outputs are lost and reload the picture after a picture failure](./T49-let-a-return-into-the-film-retry-every-device-after-all-outputs-are-lost-and-reload-the-pi.md) | app | Volodymyr Kravchuk | M | T48 | blocked | dropped — dep T48 blocked |
| T50 | [Poll output errors once per engine reading, give up after the recovery budget with a not-lip-synced record, and hold picture timing until a pending align](./T50-poll-output-errors-once-per-engine-reading-give-up-after-the-recovery-budget-with-a-not-li.md) | app | Volodymyr Kravchuk | M | T49 | blocked | dropped — dep T49 blocked |
| T51 | [Record a seek past the end and log the target picture delay for a seek or resume during a move](./T51-record-a-seek-past-the-end-and-log-the-target-picture-delay-for-a-seek-or-resume-during-a.md) | app | Volodymyr Kravchuk | S | T50 | blocked | DROP — dep T50 blocked |
| T52 | [Stretch a picture-delay move so the corrected output arrives by its end](./T52-stretch-a-picture-delay-move-so-the-corrected-output-arrives-by-its-end.md) | app | Volodymyr Kravchuk | M | T51 | blocked | DROP — dep T51 blocked |
| T53 | [Write log record values with the contract's names](./T53-write-log-record-values-with-the-contract-s-names.md) | ports | Volodymyr Kravchuk | S | — | done | COMMIT |
| T54 | [Flush a saved state file to disk before the rename](./T54-flush-a-saved-state-file-to-disk-before-the-rename.md) | infra | Volodymyr Kravchuk | S | — | done | COMMIT |
| T55 | [Check that every device opens before the window opens](./T55-check-that-every-device-opens-before-the-window-opens.md) | wiring | Volodymyr Kravchuk | M | — | done | COMMIT |
| T56 | [Tighten the click-and-flash tolerance and map EPERM to access denied](./T56-tighten-the-click-and-flash-tolerance-and-map-eperm-to-access-denied.md) | integration | Volodymyr Kravchuk | S | — | done | COMMIT |
| T57 | [Make the exit and pending-align assertions bite and test Space through the real toggle](./T57-make-the-exit-and-pending-align-assertions-bite-and-test-space-through-the-real-toggle.md) | test | Volodymyr Kravchuk | S | T50 | blocked | DROP — dep T50 blocked |
| T58 | [Bring screens.md, test-plan.md and sad.md in step with the code](./T58-bring-screens-md-test-plan-md-and-sad-md-in-step-with-the-code.md) | docs | Volodymyr Kravchuk | S | T49, T50, T55 | todo |  |

**Total:** 58 tasks (T24–T38 are follow-ups from review-2026-11-20, T39–T47 from review-2026-11-21, T48–T58 from review-2026-11-21b; T23 runs last, on the fixed code), ~33 person-days (S = ½ day, M/L = 1 day; T5 and T23 also need the owner's time on the target PC).
