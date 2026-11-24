# D1 spike report - 2026-11-20 16:08

- Machine: Microsoft Windows 10.0.26200 (X64); .NET 10.0.15
- File: `D:\Video\Predator. Collection\[01] Predator (1987) BDRip 1080p H.265 [3xUKR_ENG] [Hurtom].mkv`
- Steady play: 30 s; cycles: 2; largest picture-delay move: 30 ms


| Kill-criterion line | Measured | Verdict | Owner observation |
|---|---|---|---|
| Steady play: picture presented more than 20 ms from due | p50 18.0 / p95 43.5 / p99 47.5 / max 48.4 ms over 11296 ticks | FAIL |  |
| Pause/resume: not back within 20 ms of due within 1 s | 2 events; worst recovery never | FAIL |  |
| Seek (+-10 s, +-60 s): not back within 20 ms of due within 1 s | 8 events; worst recovery never | FAIL |  |
| 0.2 % speed move shows a visible jump or repeated frame | error during moves p99 60.7 / max 60.9 ms; backward steps 0, forward jumps 0 | OWNER |  |
| More than 1 frame per minute late or dropped | 0 in 1.8 min = 0.00 / min (steady play alone: 0 in 0.5 min) | PASS |  |
| Hardware decoding stayed on (no HardwareDecodingInactive) | not raised | PASS |  |

## Other measurements

- mpv load time (Load until first frame): 583 ms (budget context: start <= 3 s, sad.md section 11)
- scheduled-start error of the first start: 103.2 ms (event time minus due start; includes up to one frame interval)
- never within 20 ms of due in the 3 s after the first start
- counters at the end: late 0, dropped 0, skipped damaged 0
- EndOfFile raised: False; DecodeFailed raised: no

## Scheduled starts and timer granularity

- default timer: 10/10 starts measured, start error (absolute) p50 63.7 / max 67.9 ms; signed: -60.3, -66.3, -67.9, -61.5, -64.7, -64.6, -66.5, -33.8, -63.7, -34.9
- timeBeginPeriod(1): 10/10 starts measured, start error (absolute) p50 41.9 / max 42.2 ms; signed: -41.5, -41.8, -42.1, -41.4, -42.2, -41.8, -42.2, -41.9, -42.0, -42.2

## Picture-delay moves

- 0 ms -> 15 ms: 7.5 s move, error p99 60.9 ms, max 60.9 ms
- 15 ms -> 0 ms: 7.5 s move, error p99 56.8 ms, max 56.9 ms
- 0 ms -> 30 ms: 15 s move, error p99 52.8 ms, max 52.9 ms
- 30 ms -> 0 ms: 15 s move, error p99 48.8 ms, max 48.8 ms

## Recoveries (ms, null = never within 20 ms)

- pause/resume: null, null
- seek: null, null, null, null, 120, null, null, null
