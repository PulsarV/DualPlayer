# Changelog — lip-synced-video

## lip-synced-video — watch a film fullscreen, each Viewer's own track lip-synced with the picture

**What:** `DualPlayer play <file> --out <track>=<device> …` opens the film fullscreen (on the main screen or on `--screen <n>`) and plays the picture together with up to three audio outputs, each with its own track and device. The picture is held back by the largest assumed latency among the playing outputs, so every output's sound lines up with the lips; faster outputs wait the same amount. Each output's latency is estimated at every start as what the OS reports beyond the device's playback position plus a default for its device type (Bluetooth headphones / speaker, wired, HDMI, other). A Viewer corrects it by ear with keys 1/Q, 2/W, 3/E in 10 ms steps within ±500 ms; the offset is remembered per device id and saved within 1 s. Space pauses and resumes, arrows seek ±10 s / ±60 s, Page Up / Page Down move to the previous / next playable file in the folder (natural name order, files lacking a track or a playable picture are skipped with a note), Esc exits. When the picture delay has to change (an offset correction, a lost slowest device) the picture and the other outputs move to it at ≤ 0.2 % speed change, so nothing jumps. Start errors (no picture, picture not hardware-decodable, access denied, screen not found) are refused before any window opens, in plain words. Every run ends with the step-3 run summary; the log records latency, offset and picture delay at every change.

**Why:** step 3 aligned outputs with the master clock but showed no picture and ignored what each device adds before the Viewer hears it — about 150–300 ms for a Bluetooth headset — so words would arrive after the lips, by a different amount per headset ([spec](./spec.md) §1, §2). Key decisions:
- [ADR-0001](./adr/0001-host-the-start-command-in-the-app-executable.md) — the start command lives in the App executable.
- [ADR-0002](./adr/0002-render-video-with-libmpv-slaved-to-the-master-clock-behind-a-renderer-port.md) — libmpv renders the picture, steered by a Core loop against the audio master clock (roadmap D1, spike [evidence](./evidence/2026-11-20-d1-spike.md)).
- [ADR-0003](./adr/0003-embed-mpv-video-output-in-a-native-child-window-and-draw-notes-with-its-osd.md) — mpv draws into a native child window; brief notes use its OSD.
- [ADR-0004](./adr/0004-estimate-output-latency-as-the-hidden-latency-beyond-the-device-position.md) — latency = the hidden part beyond the reported device position + a device-type default.
- [ADR-0005](./adr/0005-hold-faster-outputs-back-with-a-target-lag-moved-by-the-drift-resampler.md) — faster outputs are held back by a target lag moved through the drift resampler.
- [ADR-0006](./adr/0006-keep-device-offsets-in-their-own-versioned-state-file.md) — offsets live in their own versioned `device-offsets.json`.
- [ADR-0007](./adr/0007-probe-the-picture-with-ffmpeg-before-the-window-opens.md) — the picture is probed with FFmpeg before the window opens.

**How to use** (full contract: [cli.md](./contracts/cli.md)):

```sh
dotnet run --project src/DualPlayer.Console -- devices          # device numbers (step 3)
dotnet run --project src/DualPlayer.App -- play movie.mkv --out 1=1 --out 2=2 [--screen 2]
# in the window: Space · ←/→ ±10 s · ↓/↑ ±60 s · PgUp/PgDn file · 1/Q 2/W 3/E offset · Esc
```

**Operational notes:**
- Migration: none in a database. New state file `device-offsets.json` (`schemaVersion` 1) in the per-user app-data folder ([data-model.md](./data-model.md)); unreadable or too-new files start every offset at 0 with a warning and are not overwritten until an offset key is pressed.
- Feature flag / config: none. Device-type defaults are code constants (Bluetooth 200 ms, wired / HDMI / other 0 ms) until the acceptance measurement revises them (spec §8).
- Native libraries: libmpv (`libmpv-2.dll` beside the exe on Windows, Homebrew on the dev Mac) and FFmpeg, both LGPL, dynamically linked.
- Rollback: revert the merge; delete `device-offsets.json` from app-data if wanted (nothing else reads it).
- **Acceptance on the target PC is pending (T23):** the click-and-flash measurement per output (AC-16) with both Bluetooth headsets plus a wired or HDMI output, filmed at ≥ 120 fps — script `build/make-click-flash-video.ps1`, guide [t23-acceptance-run-guide.md](./evidence/t23-acceptance-run-guide.md). The evidence lands in a follow-up PR, as in step 3. The macOS path is dev-only and not held to the §6 targets.
- **Open, carried in spec §8:** the device-type default numbers and whether a Bluetooth device's reported position already includes part of its latency (both settled by T23); durability of a correction saved seconds before a power loss; the review's test-strength residuals R2–R4, incl. an Alt+F4 check during a slow start in the T23 run.

**Acceptance criteria delivered:** AC-01 – AC-15b and AC-17 (incl. AC-03b, AC-06b, AC-09b, AC-15b) are implemented and covered by tests with a fake clock and fake devices (final review PASS, [review-2026-11-24b](./_review/review-2026-11-24b.md)). AC-16 — and the photon-level lip-sync, start, presentation and CPU targets of §6 — await the T23 run on the target PC.
