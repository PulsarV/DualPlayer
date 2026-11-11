# Tracker — two-outputs-one-clock

> Status of every task in the epic. `implement` updates **Status** and **Step** after every
> subagent it runs (and commits them with the task), so this table shows where the run is right now.
> Status: `todo` · `in_progress` · `blocked` · `review` · `done`.
> Step: `SELECT` · `RED` · `GREEN` · `REFACTOR` · `GATE` · `REVIEW` · `COMMIT` (`—` before the task starts).
> A `blocked` row carries its reason in Step, e.g. `GATE — lint red after 3 retries`.

| # | Task | Layer | Owner | Estimate | Blocked by | Status | Step |
|---|---|---|---|---|---|---|---|
| T1 | [Define the audio engine ports, value types and test fakes in Core](./T1-define-audio-engine-ports.md) | domain | Volodymyr Kravchuk | M | — | done | COMMIT |
| T2 | [Create the DualPlayer.Console executable with its own composition root and capped logging](./T2-create-console-project.md) | wiring | Volodymyr Kravchuk | M | — | done | COMMIT |
| T3 | [Provision LGPL FFmpeg for the dev Mac, CI and the Windows build](./T3-provision-lgpl-ffmpeg.md) | wiring | Volodymyr Kravchuk | M | — | done | COMMIT |
| T4 | [Validate output assignments and resolve devices by list number or OS id](./T4-validate-output-assignments.md) | domain | Volodymyr Kravchuk | S | T1 | done | COMMIT |
| T5 | [Build the epoch-tagged single-producer/single-consumer ring buffer](./T5-build-epoch-ring-buffer.md) | domain | Volodymyr Kravchuk | M | — | done | COMMIT |
| T6 | [Write the managed adaptive resampler with one-step re-alignment](./T6-write-adaptive-resampler.md) | domain | Volodymyr Kravchuk | M | — | done | COMMIT |
| T7 | [Implement the master clock, drift measurement, PI drift controller and clock-mismatch estimate](./T7-implement-master-clock-and-drift-controller.md) | domain | Volodymyr Kravchuk | M | T1 | blocked | GATE - red after 3 attempts: RED test harness Seek never moves content position; needs test fix |
| T8 | [Define engine events, the excursion tracker and the run-summary builder](./T8-define-engine-events-and-run-summary.md) | domain | Volodymyr Kravchuk | M | — | done | COMMIT |
| T9 | [Assemble the per-output render source: epoch discard, drift-corrected read, silence instant and underrun counting](./T9-assemble-output-render-source.md) | app | Volodymyr Kravchuk | L | T1, T5, T6, T7 | todo | — |
| T10 | [Fan one decoder out to the output rings with epochs, full-ring dropping and stall detection](./T10-fan-out-decode-pump.md) | app | Volodymyr Kravchuk | M | T1, T5 | done | COMMIT |
| T11 | [Run the engine-control loop: start a run, stop it, and end it on file end or device loss](./T11-run-engine-control-loop.md) | app | Volodymyr Kravchuk | L | T4, T7, T8, T9, T10 | todo | — |
| T12 | [Pause, resume and seek every output together with playback epochs](./T12-coordinate-pause-resume-seek.md) | app | Volodymyr Kravchuk | L | T11 | todo | — |
| T13 | [Publish per-device sync records, excursions and one-step corrections once per second](./T13-publish-sync-records.md) | app | Volodymyr Kravchuk | M | T11 | todo | — |
| T14 | [List a file's audio tracks and decode the chosen ones once with FFmpeg](./T14-decode-tracks-with-ffmpeg.md) | infra | Volodymyr Kravchuk | L | T1, T3 | todo | — |
| T15 | [Convert each output's track to its device mix format with swresample and a fold-down matrix](./T15-convert-tracks-to-mix-format.md) | infra | Volodymyr Kravchuk | M | T14 | todo | — |
| T16 | [List Windows playback devices and open them in shared mode, reporting access denial](./T16-list-and-open-wasapi-devices.md) | infra | Volodymyr Kravchuk | M | T1 | todo | — |
| T17 | [Render each Windows device on its own MMCSS thread with the device position and the loss signal](./T17-render-wasapi-devices.md) | infra | Volodymyr Kravchuk | L | T16 | todo | — |
| T18 | [Provide the macOS AudioQueue development backend behind the same port](./T18-macos-dev-backend.md) | infra | Volodymyr Kravchuk | M | T1 | todo | — |
| T19 | [Console: list devices and tracks, and start a run from command-line arguments](./T19-console-list-and-play.md) | ports | Volodymyr Kravchuk | L | T2, T11 | todo | — |
| T20 | [Console: pause, resume, seek and stop a running run from standard input](./T20-console-transport-commands.md) | ports | Volodymyr Kravchuk | M | T19, T12 | todo | — |
| T21 | [Console: print sync records, excursions, device loss and the run summary, also on Ctrl+C and window close](./T21-console-live-output-and-shutdown.md) | ports | Volodymyr Kravchuk | M | T19, T13 | todo | — |
| T22 | [Record the ≥ 3 h evidence run on the target Windows PC and commit its run summary](./T22-record-target-pc-evidence-run.md) | docs | Volodymyr Kravchuk | M | T15, T17, T20, T21 | todo | — |

**Total:** 22 tasks, ~21.5 person-days (S = ½ day, M/L = 1 day; L tasks are full days with a split risk).
