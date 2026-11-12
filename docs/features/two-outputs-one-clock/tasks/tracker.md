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
| T7 | [Implement the master clock, drift measurement, PI drift controller and clock-mismatch estimate](./T7-implement-master-clock-and-drift-controller.md) | domain | Volodymyr Kravchuk | M | T1 | done | COMMIT |
| T8 | [Define engine events, the excursion tracker and the run-summary builder](./T8-define-engine-events-and-run-summary.md) | domain | Volodymyr Kravchuk | M | — | done | COMMIT |
| T9 | [Assemble the per-output render source: epoch discard, drift-corrected read, silence instant and underrun counting](./T9-assemble-output-render-source.md) | app | Volodymyr Kravchuk | L | T1, T5, T6, T7 | done | COMMIT |
| T10 | [Fan one decoder out to the output rings with epochs, full-ring dropping and stall detection](./T10-fan-out-decode-pump.md) | app | Volodymyr Kravchuk | M | T1, T5 | done | COMMIT |
| T11 | [Run the engine-control loop: start a run, stop it, and end it on file end or device loss](./T11-run-engine-control-loop.md) | app | Volodymyr Kravchuk | L | T4, T7, T8, T9, T10 | done | COMMIT |
| T12 | [Pause, resume and seek every output together with playback epochs](./T12-coordinate-pause-resume-seek.md) | app | Volodymyr Kravchuk | L | T11 | done | COMMIT |
| T13 | [Publish per-device sync records, excursions and one-step corrections once per second](./T13-publish-sync-records.md) | app | Volodymyr Kravchuk | M | T11 | done | COMMIT |
| T14 | [List a file's audio tracks and decode the chosen ones once with FFmpeg](./T14-decode-tracks-with-ffmpeg.md) | infra | Volodymyr Kravchuk | L | T1, T3 | done | COMMIT |
| T15 | [Convert each output's track to its device mix format with swresample and a fold-down matrix](./T15-convert-tracks-to-mix-format.md) | infra | Volodymyr Kravchuk | M | T14 | done | COMMIT |
| T16 | [List Windows playback devices and open them in shared mode, reporting access denial](./T16-list-and-open-wasapi-devices.md) | infra | Volodymyr Kravchuk | M | T1 | done | COMMIT |
| T17 | [Render each Windows device on its own MMCSS thread with the device position and the loss signal](./T17-render-wasapi-devices.md) | infra | Volodymyr Kravchuk | L | T16 | done | COMMIT |
| T18 | [Provide the macOS AudioQueue development backend behind the same port](./T18-macos-dev-backend.md) | infra | Volodymyr Kravchuk | M | T1 | done | COMMIT |
| T19 | [Console: list devices and tracks, and start a run from command-line arguments](./T19-console-list-and-play.md) | ports | Volodymyr Kravchuk | L | T2, T11 | done | COMMIT |
| T20 | [Console: pause, resume, seek and stop a running run from standard input](./T20-console-transport-commands.md) | ports | Volodymyr Kravchuk | M | T19, T12 | done | COMMIT |
| T21 | [Console: print sync records, excursions, device loss and the run summary, also on Ctrl+C and window close](./T21-console-live-output-and-shutdown.md) | ports | Volodymyr Kravchuk | M | T19, T13 | done | COMMIT |
| T22 | [Record the ≥ 3 h evidence run on the target Windows PC and commit its run summary](./T22-record-target-pc-evidence-run.md) | docs | Volodymyr Kravchuk | M | T15, T17, T20, T21 | blocked | manual — needs the ≥ 3 h run on the target Windows PC |
| T23 | [Stop the decode pump from dropping blocks for healthy outputs and keep content position true](./T23-stop-the-decode-pump-from-dropping-blocks-for-heal.md) | app | Volodymyr Kravchuk | M | — | done | COMMIT |
| T24 | [Start audio exactly at the seek target and honour block start positions from the decoder](./T24-start-audio-exactly-at-the-seek-target-and-honour.md) | infra | Volodymyr Kravchuk | M | T23, T27 | done | COMMIT |
| T25 | [Align every output in one step at start, and keep seek confirmation and correction alive when a device hangs](./T25-align-every-output-in-one-step-at-start-and-keep-s.md) | app | Volodymyr Kravchuk | M | T23 | done | COMMIT |
| T26 | [Silence all outputs at the stop instant, count no underrun at file end, and warn before the summary on a seek past the end](./T26-silence-all-outputs-at-the-stop-instant-count-no-u.md) | app | Volodymyr Kravchuk | M | T25, T23, T24 | done | COMMIT |
| T27 | [Keep the excursion peak signed, measure played and excursion durations on running time, and ignore positions before the first render](./T27-keep-the-excursion-peak-signed-measure-played-and.md) | domain | Volodymyr Kravchuk | M | T23 | done | COMMIT |
| T28 | [Make the long-run, recovery and device-loss tests drive the real engine with realistic fakes](./T28-make-the-long-run-recovery-and-device-loss-tests-d.md) | app | Volodymyr Kravchuk | M | T23, T24, T25, T26, T27 | done | COMMIT |
| T29 | [Console: one output writer off the control loop that logs every line, keeps the summary on end of input and Ctrl+C, and numbers outputs from 1](./T29-console-one-output-writer-off-the-control-loop-tha.md) | ports | Volodymyr Kravchuk | M | T26, T27 | done | COMMIT |
| T30 | [Console: full start lines with source track, conversion and start-to-audio, and the FFmpeg version and license in the log](./T30-console-full-start-lines-with-source-track-convers.md) | ports | Volodymyr Kravchuk | M | T24, T29 | done | COMMIT |
| T31 | [Console end-to-end tests over real Core and Media with a fake backend, and a smoke test that resolves the play path](./T31-console-end-to-end-tests-over-real-core-and-media.md) | ports | Volodymyr Kravchuk | M | T29, T30 | done | COMMIT |
| T32 | [Platform: OS-gated hardware tests, device-loss mapping only for device errors, and a logged MMCSS failure](./T32-platform-os-gated-hardware-tests-device-loss-mappi.md) | infra | Volodymyr Kravchuk | M | — | done | COMMIT |
| T33 | [Compare the first block of each epoch with the seek target so a late-landing seek stays aligned](./T33-align-a-late-landing-first-block-after-seek-or-start.md) | app | Volodymyr Kravchuk | S | T28 | done | COMMIT |
| T34 | [Subtract the stream start time so files whose timestamps start above 0 play and seek correctly](./T34-subtract-the-stream-start-time-so-files-whose-timestamps-start-above-0.md) | integration | Volodymyr Kravchuk | M | — | done | COMMIT |
| T35 | [Record the clamped seek-past-end jump so the played duration stays true](./T35-record-the-clamped-seek-past-end-jump-so-the-played-duration-stays-tru.md) | app | Volodymyr Kravchuk | S | T34 | done | COMMIT |
| T36 | [Keep an output that hung during a seek measured and corrected after it recovers](./T36-keep-an-output-that-hung-during-a-seek-measured-and-corrected-after-it.md) | app | Volodymyr Kravchuk | M | T35 | done | COMMIT |
| T37 | [Recover a stalled output without seek in one correction and no underruns](./T37-recover-a-stalled-output-without-seek-in-one-correction-and-no-underru.md) | app | Volodymyr Kravchuk | M | T36 | done | COMMIT |
| T38 | [Measure start-to-audio to the first audio on every output](./T38-measure-start-to-audio-to-the-first-audio-on-every-output.md) | app | Volodymyr Kravchuk | S | T37 | done | COMMIT |
| T39 | [Stop ConsoleRig from spinning when the engine closes a device mid-pump](./T39-stop-consolerig-from-spinning-when-the-engine-closes-a-device-mid-pump.md) | test | Volodymyr Kravchuk | S | — | done | COMMIT |
| T40 | [Log console lines independently of the console write and always signal the run end](./T40-log-console-lines-independently-of-the-console-write-and-always-signal.md) | app | Volodymyr Kravchuk | S | T39 | done | COMMIT |
| T41 | [Pick the seek warning by the target's sign](./T41-pick-the-seek-warning-by-the-target-s-sign.md) | app | Volodymyr Kravchuk | S | T40 | done | COMMIT |
| T42 | [Take the LFE-dropped note from Media's conversion](./T42-take-the-lfe-dropped-note-from-media-s-conversion.md) | app | Volodymyr Kravchuk | S | T34, T41, T38 | done | COMMIT |
| T43 | [Make Core test rigs fail instead of spinning and bound the stop wait in real time](./T43-make-core-test-rigs-fail-instead-of-spinning-and-bound-the-stop-wait-i.md) | test | Volodymyr Kravchuk | S | — | done | COMMIT |
| T44 | [Make reclaiming a stalled ring exclusive so skipped frames are never counted twice](./T44-make-reclaiming-a-stalled-ring-exclusive-so-skipped-frames-are-never-c.md) | app | Volodymyr Kravchuk | M | T43 | done | COMMIT |
| T45 | [Keep the recovery window to start, resume and seek](./T45-keep-the-recovery-window-to-start-resume-and-seek.md) | app | Volodymyr Kravchuk | S | T44 | done | COMMIT |
| T46 | [Publish AudioStarted without waiting on stalled outputs and only for real audio](./T46-publish-audiostarted-without-waiting-on-stalled-outputs-and-only-for-r.md) | app | Volodymyr Kravchuk | S | T45 | done | COMMIT |
| T47 | [Print the started line after the output lines and list the new events in the SAD](./T47-print-the-started-line-after-the-output-lines-and-list-the-new-events.md) | app | Volodymyr Kravchuk | S | T46 | done | COMMIT |
| T48 | [Decide seek past the end from the decoder, not from the Matroska duration](./T48-decide-seek-past-the-end-from-the-decoder-not-from-the-matroska-durati.md) | integration | Volodymyr Kravchuk | S | — | done | COMMIT |
| T49 | [Carry Media's LFE-dropped fact to the start line](./T49-carry-media-s-lfe-dropped-fact-to-the-start-line.md) | app | Volodymyr Kravchuk | S | T47, T48 | done | COMMIT |
| T50 | [Test the played duration of a seek past the end on both branches within 100 ms](./T50-test-the-played-duration-of-a-seek-past-the-end-on-both-branches-withi.md) | test | Volodymyr Kravchuk | S | T46 | done | COMMIT |
| T51 | [Shorten the stalled-console test and rename the play test](./T51-shorten-the-stalled-console-test-and-rename-the-play-test.md) | test | Volodymyr Kravchuk | S | T47 | done | COMMIT |
| T52 | [Bound the stop wait in polls the test can set](./T52-bound-the-stop-wait-in-polls-the-test-can-set.md) | test | Volodymyr Kravchuk | S | — | done | COMMIT |
| T53 | [Confirm a seek without waiting on outputs whose track has no audio at the target, counting only new-epoch frames](./T53-confirm-a-seek-without-waiting-on-outputs-whose-track-has-no-audio-at-th.md) | app | Volodymyr Kravchuk | M | T52 | todo | SELECT |
| T54 | [Log first audio for every output that starts late and fix the handed-out doc comments](./T54-log-first-audio-for-every-output-that-starts-late-and-fix-the-handed-out.md) | app | Volodymyr Kravchuk | S | T53 | todo | SELECT |
| T55 | [Assert the race test writes after Join instead of on the writer thread](./T55-assert-the-race-test-writes-after-join-instead-of-on-the-writer-thread.md) | test | Volodymyr Kravchuk | S | — | todo | SELECT |
| T56 | [Label a source 5.1 or 7.1 only when it has an LFE channel](./T56-label-a-source-5-1-or-7-1-only-when-it-has-an-lfe-channel.md) | integration | Volodymyr Kravchuk | S | — | todo | SELECT |
| T57 | [Check the restore seek after the Matroska duration probe](./T57-check-the-restore-seek-after-the-matroska-duration-probe.md) | integration | Volodymyr Kravchuk | S | T56 | todo | SELECT |

**Total:** 57 tasks (T23–T33 are follow-ups from review-2026-11-12, T34–T42 from review-2026-11-12-r2, T43–T51 from review-2026-11-12-r3, T52–T57 from review-2026-11-12-r4), ~46 person-days (S = ½ day, M/L = 1 day; L tasks are full days with a split risk).
