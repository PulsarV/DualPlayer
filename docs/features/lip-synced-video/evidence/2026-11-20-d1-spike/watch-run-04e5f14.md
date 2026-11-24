# D1 spike report - 2026-11-20 19:13

- Machine: Microsoft Windows 10.0.26200 (X64); .NET 10.0.15
- File: `D:\Video\Predator. Collection\[01] Predator (1987) BDRip 1080p H.265 [3xUKR_ENG] [Hurtom].mkv`
- Steady play: 30 s; cycles: 10; largest picture-delay move: 100 ms


| Kill-criterion line | Measured | Verdict | Owner observation |
|---|---|---|---|
| Steady play: picture presented more than 20 ms from due | p50 4.4 / p95 7.6 / p99 8.0 / max 8.7 ms over 11809 ticks | PASS |  |
| Pause/resume: not back within 20 ms of due within 1 s | 10 events; worst recovery 525 ms | PASS |  |
| Seek (+-10 s, +-60 s): not back within 20 ms of due within 1 s | 40 events; worst recovery 505 ms | PASS |  |
| 0.2 % speed move shows a visible jump or repeated frame | error during moves p99 9.2 / max 11.1 ms; backward steps 0, forward jumps 0 | OWNER |  |
| More than 1 frame per minute late or dropped | 0 in 12.1 min = 0.00 / min (steady play alone: 0 in 0.5 min) | PASS |  |
| Hardware decoding stayed on (no HardwareDecodingInactive) | not raised | PASS |  |

## Other measurements

- mpv load time (Load until first frame): 135 ms (budget context: start <= 3 s, sad.md section 11)
- first start (and any correcting seek after it): unpause command latency 0.2 ms, seek budget 770 ms, seek took 368 ms, re-plans 0
- within 20 ms of due 505 ms after the scheduled first start (held samples not counted)
- presentation-delay samples: 15570 (used 15342), median -1.0 ms
- ticks while the picture was held paused for a start (excluded from error figures): 1771
- counters at the end: late 0, dropped 0, skipped damaged 0
- EndOfFile raised: False; DecodeFailed raised: no

## Scheduled starts and timer granularity

- default timer: 10/10 starts measured, unpause command latency (absolute) p50 0.2 / max 0.2 ms
- default timer start: unpause command latency 0.1 ms, seek budget 187 ms, seek took 4 ms, re-plans 0
- default timer start: unpause command latency 0.2 ms, seek budget 187 ms, seek took 4 ms, re-plans 0
- default timer start: unpause command latency 0.2 ms, seek budget 178 ms, seek took 4 ms, re-plans 0
- default timer start: unpause command latency 0.2 ms, seek budget 178 ms, seek took 4 ms, re-plans 0
- default timer start: unpause command latency 0.2 ms, seek budget 178 ms, seek took 4 ms, re-plans 0
- default timer start: unpause command latency 0.1 ms, seek budget 178 ms, seek took 5 ms, re-plans 0
- default timer start: unpause command latency 0.2 ms, seek budget 178 ms, seek took 4 ms, re-plans 0
- default timer start: unpause command latency 0.1 ms, seek budget 50 ms, seek took 4 ms, re-plans 0
- default timer start: unpause command latency 0.2 ms, seek budget 50 ms, seek took 5 ms, re-plans 0
- default timer start: unpause command latency 0.1 ms, seek budget 50 ms, seek took 4 ms, re-plans 0
- timeBeginPeriod(1): 10/10 starts measured, unpause command latency (absolute) p50 0.1 / max 0.2 ms
- timeBeginPeriod(1) start: unpause command latency 0.1 ms, seek budget 50 ms, seek took 4 ms, re-plans 0
- timeBeginPeriod(1) start: unpause command latency 0.2 ms, seek budget 50 ms, seek took 4 ms, re-plans 0
- timeBeginPeriod(1) start: unpause command latency 0.2 ms, seek budget 50 ms, seek took 5 ms, re-plans 0
- timeBeginPeriod(1) start: unpause command latency 0.1 ms, seek budget 50 ms, seek took 4 ms, re-plans 0
- timeBeginPeriod(1) start: unpause command latency 0.1 ms, seek budget 50 ms, seek took 4 ms, re-plans 0
- timeBeginPeriod(1) start: unpause command latency 0.2 ms, seek budget 50 ms, seek took 5 ms, re-plans 0
- timeBeginPeriod(1) start: unpause command latency 0.1 ms, seek budget 50 ms, seek took 5 ms, re-plans 0
- timeBeginPeriod(1) start: unpause command latency 0.1 ms, seek budget 50 ms, seek took 4 ms, re-plans 0
- timeBeginPeriod(1) start: unpause command latency 0.2 ms, seek budget 50 ms, seek took 4 ms, re-plans 0
- timeBeginPeriod(1) start: unpause command latency 0.1 ms, seek budget 50 ms, seek took 4 ms, re-plans 0

## Picture-delay moves

- 0 ms -> 10 ms: 5 s move, error p99 8.3 ms, max 8.5 ms
- 10 ms -> 0 ms: 5 s move, error p99 7.9 ms, max 8.0 ms
- 0 ms -> 20 ms: 10 s move, error p99 7.8 ms, max 7.9 ms
- 20 ms -> 0 ms: 10 s move, error p99 8.8 ms, max 9.2 ms
- 0 ms -> 30 ms: 15 s move, error p99 8.9 ms, max 9.0 ms
- 30 ms -> 0 ms: 15 s move, error p99 9.5 ms, max 9.8 ms
- 0 ms -> 40 ms: 20 s move, error p99 8.7 ms, max 8.9 ms
- 40 ms -> 0 ms: 20 s move, error p99 9.4 ms, max 9.5 ms
- 0 ms -> 50 ms: 25 s move, error p99 9.1 ms, max 9.2 ms
- 50 ms -> 0 ms: 25 s move, error p99 9.4 ms, max 9.7 ms
- 0 ms -> 60 ms: 30 s move, error p99 9.2 ms, max 9.4 ms
- 60 ms -> 0 ms: 30 s move, error p99 7.3 ms, max 7.4 ms
- 0 ms -> 70 ms: 35 s move, error p99 9.0 ms, max 9.2 ms
- 70 ms -> 0 ms: 35 s move, error p99 8.0 ms, max 8.2 ms
- 0 ms -> 80 ms: 40 s move, error p99 9.6 ms, max 9.8 ms
- 80 ms -> 0 ms: 40 s move, error p99 8.5 ms, max 8.8 ms
- 0 ms -> 90 ms: 45 s move, error p99 8.3 ms, max 8.5 ms
- 90 ms -> 0 ms: 45 s move, error p99 10.4 ms, max 10.5 ms
- 0 ms -> 100 ms: 50 s move, error p99 9.4 ms, max 10.0 ms
- 100 ms -> 0 ms: 50 s move, error p99 11.0 ms, max 11.1 ms

## Recoveries (ms from the align to back within 20 ms after the actual resume, null = never)

- pause/resume: 513, 520, 514, 511, 513, 525, 513, 517, 151, 142
- seek: 120, 142, 70, 116, 131, 102, 127, 103, 330, 279, 239, 279, 260, 284, 247, 242, 256, 165, 505, 286, 308, 282, 289, 289, 307, 288, 297, 284, 314, 292, 301, 295, 228, 253, 257, 252, 232, 208, 219, 214

## Starts behind the recoveries (unpause command latency = mpv applied the unpause minus the moment the held frame was due)

- resume 1: unpause command latency 0.2 ms, seek budget 481 ms, seek took 11 ms, re-plans 0
- resume 2: unpause command latency 0.1 ms, seek budget 481 ms, seek took 17 ms, re-plans 0
- resume 3: unpause command latency 0.1 ms, seek budget 481 ms, seek took 42 ms, re-plans 0
- resume 4: unpause command latency 0.2 ms, seek budget 481 ms, seek took 67 ms, re-plans 0
- resume 5: unpause command latency 0.2 ms, seek budget 481 ms, seek took 7 ms, re-plans 0
- resume 6: unpause command latency 0.1 ms, seek budget 481 ms, seek took 11 ms, re-plans 0
- resume 7: unpause command latency 0.1 ms, seek budget 481 ms, seek took 13 ms, re-plans 0
- resume 8: unpause command latency 0.2 ms, seek budget 481 ms, seek took 22 ms, re-plans 0
- resume 9: unpause command latency 0.1 ms, seek budget 103 ms, seek took 37 ms, re-plans 0
- resume 10: unpause command latency 0.1 ms, seek budget 103 ms, seek took 6 ms, re-plans 0
- seek 1: unpause command latency 0.2 ms, seek budget 103 ms, seek took 37 ms, re-plans 0
- seek 2: unpause command latency 0.2 ms, seek budget 103 ms, seek took 15 ms, re-plans 0
- seek 3: unpause command latency 0.2 ms, seek budget 67 ms, seek took 58 ms, re-plans 0
- seek 4: unpause command latency 0.2 ms, seek budget 92 ms, seek took 30 ms, re-plans 0
- seek 5: unpause command latency 0.2 ms, seek budget 92 ms, seek took 32 ms, re-plans 0
- seek 6: unpause command latency 0.2 ms, seek budget 92 ms, seek took 12 ms, re-plans 0
- seek 7: unpause command latency 0.2 ms, seek budget 92 ms, seek took 55 ms, re-plans 0
- seek 8: unpause command latency 0.2 ms, seek budget 92 ms, seek took 58 ms, re-plans 0
- seek 9: unpause command latency 0.2 ms, seek budget 190 ms, seek took 174 ms, re-plans 1 (overran: 136 ms of 93 ms budget)
- seek 10: unpause command latency 0.2 ms, seek budget 237 ms, seek took 8 ms, re-plans 0
- seek 11: unpause command latency 0.2 ms, seek budget 237 ms, seek took 40 ms, re-plans 0
- seek 12: unpause command latency 0.1 ms, seek budget 237 ms, seek took 39 ms, re-plans 0
- seek 13: unpause command latency 0.2 ms, seek budget 237 ms, seek took 79 ms, re-plans 0
- seek 14: unpause command latency 0.2 ms, seek budget 237 ms, seek took 104 ms, re-plans 0
- seek 15: unpause command latency 0.2 ms, seek budget 237 ms, seek took 9 ms, re-plans 0
- seek 16: unpause command latency 0.1 ms, seek budget 237 ms, seek took 19 ms, re-plans 0
- seek 17: unpause command latency 0.1 ms, seek budget 237 ms, seek took 43 ms, re-plans 0
- seek 18: unpause command latency 0.2 ms, seek budget 150 ms, seek took 31 ms, re-plans 0
- seek 19: unpause command latency 0.2 ms, seek budget 265 ms, seek took 201 ms, re-plans 1 (overran: 196 ms of 150 ms budget)
- seek 20: unpause command latency 0.1 ms, seek budget 271 ms, seek took 12 ms, re-plans 0
- seek 21: unpause command latency 0.2 ms, seek budget 271 ms, seek took 20 ms, re-plans 0
- seek 22: unpause command latency 0.2 ms, seek budget 271 ms, seek took 91 ms, re-plans 0
- seek 23: unpause command latency 0.2 ms, seek budget 271 ms, seek took 148 ms, re-plans 0
- seek 24: unpause command latency 0.2 ms, seek budget 271 ms, seek took 210 ms, re-plans 0
- seek 25: unpause command latency 0.2 ms, seek budget 282 ms, seek took 8 ms, re-plans 0
- seek 26: unpause command latency 0.2 ms, seek budget 282 ms, seek took 54 ms, re-plans 0
- seek 27: unpause command latency 0.2 ms, seek budget 282 ms, seek took 68 ms, re-plans 0
- seek 28: unpause command latency 0.2 ms, seek budget 282 ms, seek took 159 ms, re-plans 0
- seek 29: unpause command latency 0.2 ms, seek budget 282 ms, seek took 76 ms, re-plans 0
- seek 30: unpause command latency 0.1 ms, seek budget 282 ms, seek took 30 ms, re-plans 0
- seek 31: unpause command latency 0.1 ms, seek budget 282 ms, seek took 14 ms, re-plans 0
- seek 32: unpause command latency 0.1 ms, seek budget 282 ms, seek took 71 ms, re-plans 0
- seek 33: unpause command latency 0.2 ms, seek budget 219 ms, seek took 118 ms, re-plans 0
- seek 34: unpause command latency 0.2 ms, seek budget 219 ms, seek took 133 ms, re-plans 0
- seek 35: unpause command latency 0.2 ms, seek budget 219 ms, seek took 8 ms, re-plans 0
- seek 36: unpause command latency 0.2 ms, seek budget 219 ms, seek took 20 ms, re-plans 0
- seek 37: unpause command latency 0.2 ms, seek budget 187 ms, seek took 57 ms, re-plans 0
- seek 38: unpause command latency 0.2 ms, seek budget 187 ms, seek took 110 ms, re-plans 0
- seek 39: unpause command latency 0.2 ms, seek budget 187 ms, seek took 126 ms, re-plans 0
- seek 40: unpause command latency 0.2 ms, seek budget 187 ms, seek took 17 ms, re-plans 0
