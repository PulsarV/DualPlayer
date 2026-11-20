# D1 spike report - 2026-11-20 17:49

- Machine: Microsoft Windows 10.0.26200 (X64); .NET 10.0.15
- File: `D:\Video\Predator. Collection\[01] Predator (1987) BDRip 1080p H.265 [3xUKR_ENG] [Hurtom].mkv`
- Steady play: 600 s; cycles: 10; largest picture-delay move: 100 ms


| Kill-criterion line | Measured | Verdict | Owner observation |
|---|---|---|---|
| Steady play: picture presented more than 20 ms from due | p50 2.3 / p95 7.9 / p99 10.4 / max 11.6 ms over 237706 ticks | PASS |  |
| Pause/resume: not back within 20 ms of due within 1 s | 10 events; worst recovery 470 ms | PASS |  |
| Seek (+-10 s, +-60 s): not back within 20 ms of due within 1 s | 40 events; worst recovery never | FAIL |  |
| 0.2 % speed move shows a visible jump or repeated frame | error during moves p99 8.7 / max 10.4 ms; backward steps 0, forward jumps 0 | OWNER |  |
| More than 1 frame per minute late or dropped | 0 in 21.6 min = 0.00 / min (steady play alone: 0 in 10.0 min) | PASS |  |
| Hardware decoding stayed on (no HardwareDecodingInactive) | not raised | PASS |  |

## Other measurements

- mpv load time (Load until first frame): 115 ms (budget context: start <= 3 s, sad.md section 11)
- first start (and any correcting seek after it): unpause command latency 0.1 ms, seek budget 395 ms, seek took 371 ms
- within 20 ms of due 135 ms after the scheduled first start (held samples not counted)
- presentation-delay samples: 28620 (used 28385), median -1.3 ms
- ticks while the picture was held paused for a start (excluded from error figures): 1324
- counters at the end: late 0, dropped 0, skipped damaged 0
- EndOfFile raised: False; DecodeFailed raised: no

## Scheduled starts and timer granularity

- default timer: 10/10 starts measured, unpause command latency (absolute) p50 0.2 / max 0.2 ms
- default timer start: unpause command latency 0.2 ms, seek budget 123 ms, seek took 4 ms
- default timer start: unpause command latency 0.1 ms, seek budget 94 ms, seek took 4 ms
- default timer start: unpause command latency 0.1 ms, seek budget 74 ms, seek took 5 ms
- default timer start: unpause command latency 0.2 ms, seek budget 60 ms, seek took 4 ms
- default timer start: unpause command latency 0.2 ms, seek budget 50 ms, seek took 4 ms
- default timer start: unpause command latency 0.2 ms, seek budget 50 ms, seek took 4 ms
- default timer start: unpause command latency 0.1 ms, seek budget 50 ms, seek took 5 ms
- default timer start: unpause command latency 0.2 ms, seek budget 50 ms, seek took 6 ms
- default timer start: unpause command latency 0.2 ms, seek budget 50 ms, seek took 4 ms
- default timer start: unpause command latency 0.2 ms, seek budget 50 ms, seek took 4 ms
- timeBeginPeriod(1): 10/10 starts measured, unpause command latency (absolute) p50 0.2 / max 0.2 ms
- timeBeginPeriod(1) start: unpause command latency 0.2 ms, seek budget 50 ms, seek took 5 ms
- timeBeginPeriod(1) start: unpause command latency 0.2 ms, seek budget 50 ms, seek took 4 ms
- timeBeginPeriod(1) start: unpause command latency 0.1 ms, seek budget 50 ms, seek took 4 ms
- timeBeginPeriod(1) start: unpause command latency 0.1 ms, seek budget 50 ms, seek took 4 ms
- timeBeginPeriod(1) start: unpause command latency 0.2 ms, seek budget 50 ms, seek took 4 ms
- timeBeginPeriod(1) start: unpause command latency 0.1 ms, seek budget 50 ms, seek took 4 ms
- timeBeginPeriod(1) start: unpause command latency 0.1 ms, seek budget 50 ms, seek took 4 ms
- timeBeginPeriod(1) start: unpause command latency 0.2 ms, seek budget 50 ms, seek took 4 ms
- timeBeginPeriod(1) start: unpause command latency 0.2 ms, seek budget 50 ms, seek took 4 ms
- timeBeginPeriod(1) start: unpause command latency 0.2 ms, seek budget 50 ms, seek took 4 ms

## Picture-delay moves

- 0 ms -> 10 ms: 5 s move, error p99 8.9 ms, max 9.1 ms
- 10 ms -> 0 ms: 5 s move, error p99 7.5 ms, max 8.8 ms
- 0 ms -> 20 ms: 10 s move, error p99 8.0 ms, max 8.1 ms
- 20 ms -> 0 ms: 10 s move, error p99 8.2 ms, max 8.4 ms
- 0 ms -> 30 ms: 15 s move, error p99 8.1 ms, max 8.3 ms
- 30 ms -> 0 ms: 15 s move, error p99 7.8 ms, max 8.1 ms
- 0 ms -> 40 ms: 20 s move, error p99 8.3 ms, max 8.4 ms
- 40 ms -> 0 ms: 20 s move, error p99 8.8 ms, max 8.9 ms
- 0 ms -> 50 ms: 25 s move, error p99 8.9 ms, max 9.0 ms
- 50 ms -> 0 ms: 25 s move, error p99 9.1 ms, max 9.2 ms
- 0 ms -> 60 ms: 30 s move, error p99 9.2 ms, max 9.3 ms
- 60 ms -> 0 ms: 30 s move, error p99 6.7 ms, max 7.4 ms
- 0 ms -> 70 ms: 35 s move, error p99 9.3 ms, max 9.5 ms
- 70 ms -> 0 ms: 35 s move, error p99 8.1 ms, max 8.3 ms
- 0 ms -> 80 ms: 40 s move, error p99 8.9 ms, max 9.0 ms
- 80 ms -> 0 ms: 40 s move, error p99 7.8 ms, max 8.0 ms
- 0 ms -> 90 ms: 45 s move, error p99 8.3 ms, max 8.5 ms
- 90 ms -> 0 ms: 45 s move, error p99 7.9 ms, max 10.1 ms
- 0 ms -> 100 ms: 50 s move, error p99 8.6 ms, max 8.7 ms
- 100 ms -> 0 ms: 50 s move, error p99 9.3 ms, max 10.4 ms

## Recoveries (ms from the align to back within 20 ms after the actual resume, null = never)

- pause/resume: 470, 384, 310, 234, 187, 186, 184, 189, 141, 146
- seek: 563, 141, 158, 147, 121, 144, null, 191, 166, 156, 156, 200, 214, 154, null, 243, 215, 242, 159, 152, 166, 192, 207, 157, 524, 153, 165, 200, 182, 152, 170, 167, 223, 169, 167, 159, 176, 213, 178, 118

## Starts behind the recoveries (unpause command latency = mpv applied the unpause minus the moment the held frame was due)

- resume 1: unpause command latency 0.2 ms, seek budget 450 ms, seek took 125 ms
- resume 2: unpause command latency 0.1 ms, seek budget 377 ms, seek took 5 ms
- resume 3: unpause command latency 0.1 ms, seek budget 272 ms, seek took 11 ms
- resume 4: unpause command latency 0.2 ms, seek budget 201 ms, seek took 34 ms
- resume 5: unpause command latency 0.2 ms, seek budget 162 ms, seek took 74 ms
- resume 6: unpause command latency 0.2 ms, seek budget 153 ms, seek took 110 ms
- resume 7: unpause command latency 0.1 ms, seek budget 163 ms, seek took 127 ms
- resume 8: unpause command latency 0.1 ms, seek budget 177 ms, seek took 11 ms
- resume 9: unpause command latency 0.2 ms, seek budget 135 ms, seek took 17 ms
- resume 10: unpause command latency 0.2 ms, seek budget 108 ms, seek took 63 ms
- seek 1: unpause command latency 12.4 ms, seek budget 110 ms, seek took 128 ms
- seek 2: unpause command latency 0.2 ms, seek budget 140 ms, seek took 102 ms
- seek 3: unpause command latency 0.2 ms, seek budget 150 ms, seek took 30 ms
- seek 4: unpause command latency 0.2 ms, seek budget 125 ms, seek took 24 ms
- seek 5: unpause command latency 0.1 ms, seek budget 104 ms, seek took 87 ms
- seek 6: unpause command latency 0.2 ms, seek budget 118 ms, seek took 108 ms
- seek 7: unpause command latency 35.3 ms, seek budget 137 ms, seek took 187 ms
- seek 8: unpause command latency 0.1 ms, seek budget 186 ms, seek took 13 ms
- seek 9: unpause command latency 0.2 ms, seek budget 142 ms, seek took 51 ms
- seek 10: unpause command latency 0.2 ms, seek budget 129 ms, seek took 88 ms
- seek 11: unpause command latency 0.2 ms, seek budget 136 ms, seek took 136 ms
- seek 12: unpause command latency 0.4 ms, seek budget 162 ms, seek took 136 ms
- seek 13: unpause command latency 0.2 ms, seek budget 181 ms, seek took 37 ms
- seek 14: unpause command latency 0.2 ms, seek budget 149 ms, seek took 37 ms
- seek 15: unpause command latency 86.2 ms, seek budget 127 ms, seek took 240 ms
- seek 16: unpause command latency 0.2 ms, seek budget 203 ms, seek took 95 ms
- seek 17: unpause command latency 0.2 ms, seek budget 191 ms, seek took 143 ms
- seek 18: unpause command latency 0.2 ms, seek budget 204 ms, seek took 14 ms
- seek 19: unpause command latency 0.1 ms, seek budget 155 ms, seek took 46 ms
- seek 20: unpause command latency 0.2 ms, seek budget 136 ms, seek took 82 ms
- seek 21: unpause command latency 0.1 ms, seek budget 138 ms, seek took 120 ms
- seek 22: unpause command latency 0.2 ms, seek budget 156 ms, seek took 146 ms
- seek 23: unpause command latency 0.2 ms, seek budget 181 ms, seek took 37 ms
- seek 24: unpause command latency 0.2 ms, seek budget 150 ms, seek took 48 ms
- seek 25: unpause command latency 0.2 ms, seek budget 132 ms, seek took 92 ms
- seek 26: unpause command latency 0.2 ms, seek budget 140 ms, seek took 127 ms
- seek 27: unpause command latency 0.2 ms, seek budget 161 ms, seek took 140 ms
- seek 28: unpause command latency 0.2 ms, seek budget 182 ms, seek took 13 ms
- seek 29: unpause command latency 0.2 ms, seek budget 139 ms, seek took 38 ms
- seek 30: unpause command latency 0.1 ms, seek budget 121 ms, seek took 102 ms
- seek 31: unpause command latency 0.2 ms, seek budget 136 ms, seek took 112 ms
- seek 32: unpause command latency 8.1 ms, seek budget 152 ms, seek took 161 ms
- seek 33: unpause command latency 0.2 ms, seek budget 185 ms, seek took 23 ms
- seek 34: unpause command latency 0.2 ms, seek budget 146 ms, seek took 50 ms
- seek 35: unpause command latency 0.2 ms, seek budget 130 ms, seek took 89 ms
- seek 36: unpause command latency 0.2 ms, seek budget 137 ms, seek took 109 ms
- seek 37: unpause command latency 0.2 ms, seek budget 151 ms, seek took 157 ms
- seek 38: unpause command latency 0.2 ms, seek budget 182 ms, seek took 25 ms
- seek 39: unpause command latency 0.1 ms, seek budget 145 ms, seek took 24 ms
- seek 40: unpause command latency 0.1 ms, seek budget 118 ms, seek took 76 ms
