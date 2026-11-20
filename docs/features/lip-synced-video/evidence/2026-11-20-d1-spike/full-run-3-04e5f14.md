# D1 spike report - 2026-11-20 18:41

- Machine: Microsoft Windows 10.0.26200 (X64); .NET 10.0.15
- File: `D:\Video\Predator. Collection\[01] Predator (1987) BDRip 1080p H.265 [3xUKR_ENG] [Hurtom].mkv`
- Steady play: 600 s; cycles: 10; largest picture-delay move: 100 ms


| Kill-criterion line | Measured | Verdict | Owner observation |
|---|---|---|---|
| Steady play: picture presented more than 20 ms from due | p50 2.5 / p95 7.8 / p99 10.1 / max 11.6 ms over 229302 ticks | PASS |  |
| Pause/resume: not back within 20 ms of due within 1 s | 10 events; worst recovery 527 ms | PASS |  |
| Seek (+-10 s, +-60 s): not back within 20 ms of due within 1 s | 40 events; worst recovery 598 ms | PASS |  |
| 0.2 % speed move shows a visible jump or repeated frame | error during moves p99 8.5 / max 9.8 ms; backward steps 0, forward jumps 0 | OWNER |  |
| More than 1 frame per minute late or dropped | 0 in 21.6 min = 0.00 / min (steady play alone: 0 in 10.0 min) | PASS |  |
| Hardware decoding stayed on (no HardwareDecodingInactive) | not raised | PASS |  |

## Other measurements

- mpv load time (Load until first frame): 127 ms (budget context: start <= 3 s, sad.md section 11)
- first start (and any correcting seek after it): unpause command latency 0.2 ms, seek budget 770 ms, seek took 380 ms, re-plans 0
- within 20 ms of due 513 ms after the scheduled first start (held samples not counted)
- presentation-delay samples: 28897 (used 28664), median -1.4 ms
- ticks while the picture was held paused for a start (excluded from error figures): 1832
- counters at the end: late 0, dropped 0, skipped damaged 0
- EndOfFile raised: False; DecodeFailed raised: no

## Scheduled starts and timer granularity

- default timer: 10/10 starts measured, unpause command latency (absolute) p50 0.2 / max 0.2 ms
- default timer start: unpause command latency 0.2 ms, seek budget 264 ms, seek took 4 ms, re-plans 0
- default timer start: unpause command latency 0.2 ms, seek budget 264 ms, seek took 5 ms, re-plans 0
- default timer start: unpause command latency 0.1 ms, seek budget 264 ms, seek took 5 ms, re-plans 0
- default timer start: unpause command latency 0.2 ms, seek budget 264 ms, seek took 5 ms, re-plans 0
- default timer start: unpause command latency 0.2 ms, seek budget 264 ms, seek took 5 ms, re-plans 0
- default timer start: unpause command latency 0.2 ms, seek budget 127 ms, seek took 4 ms, re-plans 0
- default timer start: unpause command latency 0.1 ms, seek budget 127 ms, seek took 4 ms, re-plans 0
- default timer start: unpause command latency 0.2 ms, seek budget 127 ms, seek took 4 ms, re-plans 0
- default timer start: unpause command latency 0.1 ms, seek budget 50 ms, seek took 4 ms, re-plans 0
- default timer start: unpause command latency 0.2 ms, seek budget 50 ms, seek took 4 ms, re-plans 0
- timeBeginPeriod(1): 10/10 starts measured, unpause command latency (absolute) p50 0.2 / max 0.2 ms
- timeBeginPeriod(1) start: unpause command latency 0.2 ms, seek budget 50 ms, seek took 5 ms, re-plans 0
- timeBeginPeriod(1) start: unpause command latency 0.1 ms, seek budget 50 ms, seek took 4 ms, re-plans 0
- timeBeginPeriod(1) start: unpause command latency 0.2 ms, seek budget 50 ms, seek took 4 ms, re-plans 0
- timeBeginPeriod(1) start: unpause command latency 0.2 ms, seek budget 50 ms, seek took 4 ms, re-plans 0
- timeBeginPeriod(1) start: unpause command latency 0.2 ms, seek budget 50 ms, seek took 4 ms, re-plans 0
- timeBeginPeriod(1) start: unpause command latency 0.1 ms, seek budget 50 ms, seek took 4 ms, re-plans 0
- timeBeginPeriod(1) start: unpause command latency 0.2 ms, seek budget 50 ms, seek took 4 ms, re-plans 0
- timeBeginPeriod(1) start: unpause command latency 0.2 ms, seek budget 50 ms, seek took 4 ms, re-plans 0
- timeBeginPeriod(1) start: unpause command latency 0.2 ms, seek budget 50 ms, seek took 4 ms, re-plans 0
- timeBeginPeriod(1) start: unpause command latency 0.2 ms, seek budget 50 ms, seek took 4 ms, re-plans 0

## Picture-delay moves

- 0 ms -> 10 ms: 5 s move, error p99 8.5 ms, max 8.6 ms
- 10 ms -> 0 ms: 5 s move, error p99 7.9 ms, max 8.0 ms
- 0 ms -> 20 ms: 10 s move, error p99 8.1 ms, max 8.2 ms
- 20 ms -> 0 ms: 10 s move, error p99 8.5 ms, max 8.8 ms
- 0 ms -> 30 ms: 15 s move, error p99 8.7 ms, max 8.9 ms
- 30 ms -> 0 ms: 15 s move, error p99 7.6 ms, max 7.8 ms
- 0 ms -> 40 ms: 20 s move, error p99 9.7 ms, max 9.8 ms
- 40 ms -> 0 ms: 20 s move, error p99 6.4 ms, max 6.7 ms
- 0 ms -> 50 ms: 25 s move, error p99 8.9 ms, max 9.0 ms
- 50 ms -> 0 ms: 25 s move, error p99 8.6 ms, max 9.0 ms
- 0 ms -> 60 ms: 30 s move, error p99 8.3 ms, max 8.5 ms
- 60 ms -> 0 ms: 30 s move, error p99 7.4 ms, max 7.8 ms
- 0 ms -> 70 ms: 35 s move, error p99 8.0 ms, max 8.3 ms
- 70 ms -> 0 ms: 35 s move, error p99 8.6 ms, max 9.1 ms
- 0 ms -> 80 ms: 40 s move, error p99 8.0 ms, max 8.1 ms
- 80 ms -> 0 ms: 40 s move, error p99 7.6 ms, max 8.1 ms
- 0 ms -> 90 ms: 45 s move, error p99 8.0 ms, max 8.2 ms
- 90 ms -> 0 ms: 45 s move, error p99 8.2 ms, max 8.5 ms
- 0 ms -> 100 ms: 50 s move, error p99 8.9 ms, max 9.1 ms
- 100 ms -> 0 ms: 50 s move, error p99 6.7 ms, max 6.9 ms

## Recoveries (ms from the align to back within 20 ms after the actual resume, null = never)

- pause/resume: 521, 524, 521, 513, 527, 519, 523, 526, 195, 193
- seek: 598, 229, 238, 226, 212, 234, 239, 228, 254, 228, 247, 227, 252, 276, 279, 273, 248, 282, 284, 280, 251, 240, 253, 231, 252, 190, 210, 237, 217, 235, 254, 235, 210, 233, 204, 232, 212, 283, 289, 291

## Starts behind the recoveries (unpause command latency = mpv applied the unpause minus the moment the held frame was due)

- resume 1: unpause command latency 0.2 ms, seek budget 495 ms, seek took 124 ms, re-plans 0
- resume 2: unpause command latency 0.2 ms, seek budget 495 ms, seek took 5 ms, re-plans 0
- resume 3: unpause command latency 0.2 ms, seek budget 495 ms, seek took 11 ms, re-plans 0
- resume 4: unpause command latency 0.2 ms, seek budget 495 ms, seek took 37 ms, re-plans 0
- resume 5: unpause command latency 0.2 ms, seek budget 495 ms, seek took 78 ms, re-plans 0
- resume 6: unpause command latency 0.1 ms, seek budget 495 ms, seek took 114 ms, re-plans 0
- resume 7: unpause command latency 0.1 ms, seek budget 495 ms, seek took 5 ms, re-plans 0
- resume 8: unpause command latency 0.2 ms, seek budget 495 ms, seek took 11 ms, re-plans 0
- resume 9: unpause command latency 0.1 ms, seek budget 175 ms, seek took 16 ms, re-plans 0
- resume 10: unpause command latency 0.2 ms, seek budget 162 ms, seek took 62 ms, re-plans 0
- seek 1: unpause command latency 0.3 ms, seek budget 162 ms, seek took 146 ms, re-plans 0
- seek 2: unpause command latency 0.3 ms, seek budget 202 ms, seek took 112 ms, re-plans 0
- seek 3: unpause command latency 0.1 ms, seek budget 202 ms, seek took 25 ms, re-plans 0
- seek 4: unpause command latency 0.1 ms, seek budget 202 ms, seek took 39 ms, re-plans 0
- seek 5: unpause command latency 0.2 ms, seek budget 202 ms, seek took 100 ms, re-plans 0
- seek 6: unpause command latency 0.1 ms, seek budget 202 ms, seek took 131 ms, re-plans 0
- seek 7: unpause command latency 0.2 ms, seek budget 202 ms, seek took 156 ms, re-plans 0
- seek 8: unpause command latency 0.2 ms, seek budget 215 ms, seek took 28 ms, re-plans 0
- seek 9: unpause command latency 0.1 ms, seek budget 215 ms, seek took 45 ms, re-plans 0
- seek 10: unpause command latency 0.1 ms, seek budget 215 ms, seek took 103 ms, re-plans 0
- seek 11: unpause command latency 0.2 ms, seek budget 215 ms, seek took 135 ms, re-plans 0
- seek 12: unpause command latency 0.2 ms, seek budget 215 ms, seek took 180 ms, re-plans 0
- seek 13: unpause command latency 0.2 ms, seek budget 245 ms, seek took 20 ms, re-plans 0
- seek 14: unpause command latency 0.2 ms, seek budget 245 ms, seek took 44 ms, re-plans 0
- seek 15: unpause command latency 0.2 ms, seek budget 245 ms, seek took 106 ms, re-plans 0
- seek 16: unpause command latency 0.2 ms, seek budget 245 ms, seek took 145 ms, re-plans 0
- seek 17: unpause command latency 0.1 ms, seek budget 245 ms, seek took 157 ms, re-plans 0
- seek 18: unpause command latency 0.2 ms, seek budget 245 ms, seek took 12 ms, re-plans 0
- seek 19: unpause command latency 0.2 ms, seek budget 245 ms, seek took 60 ms, re-plans 0
- seek 20: unpause command latency 0.2 ms, seek budget 245 ms, seek took 117 ms, re-plans 0
- seek 21: unpause command latency 0.1 ms, seek budget 216 ms, seek took 119 ms, re-plans 0
- seek 22: unpause command latency 0.2 ms, seek budget 216 ms, seek took 135 ms, re-plans 0
- seek 23: unpause command latency 0.1 ms, seek budget 216 ms, seek took 40 ms, re-plans 0
- seek 24: unpause command latency 0.2 ms, seek budget 216 ms, seek took 50 ms, re-plans 0
- seek 25: unpause command latency 0.1 ms, seek budget 216 ms, seek took 109 ms, re-plans 0
- seek 26: unpause command latency 0.2 ms, seek budget 189 ms, seek took 147 ms, re-plans 0
- seek 27: unpause command latency 0.4 ms, seek budget 204 ms, seek took 140 ms, re-plans 0
- seek 28: unpause command latency 0.2 ms, seek budget 204 ms, seek took 46 ms, re-plans 0
- seek 29: unpause command latency 0.2 ms, seek budget 204 ms, seek took 40 ms, re-plans 0
- seek 30: unpause command latency 0.2 ms, seek budget 204 ms, seek took 81 ms, re-plans 0
- seek 31: unpause command latency 0.2 ms, seek budget 204 ms, seek took 125 ms, re-plans 0
- seek 32: unpause command latency 0.1 ms, seek budget 204 ms, seek took 144 ms, re-plans 0
- seek 33: unpause command latency 0.2 ms, seek budget 204 ms, seek took 37 ms, re-plans 0
- seek 34: unpause command latency 0.2 ms, seek budget 204 ms, seek took 51 ms, re-plans 0
- seek 35: unpause command latency 0.2 ms, seek budget 200 ms, seek took 106 ms, re-plans 0
- seek 36: unpause command latency 0.1 ms, seek budget 200 ms, seek took 112 ms, re-plans 0
- seek 37: unpause command latency 0.2 ms, seek budget 200 ms, seek took 195 ms, re-plans 0
- seek 38: unpause command latency 0.2 ms, seek budget 264 ms, seek took 13 ms, re-plans 0
- seek 39: unpause command latency 0.2 ms, seek budget 264 ms, seek took 48 ms, re-plans 0
- seek 40: unpause command latency 0.2 ms, seek budget 264 ms, seek took 86 ms, re-plans 0
