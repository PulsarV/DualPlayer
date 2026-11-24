# D1 spike report - 2026-11-20 17:09

- Machine: Microsoft Windows 10.0.26200 (X64); .NET 10.0.15
- File: `D:\Video\Predator. Collection\[01] Predator (1987) BDRip 1080p H.265 [3xUKR_ENG] [Hurtom].mkv`
- Steady play: 600 s; cycles: 10; largest picture-delay move: 100 ms


| Kill-criterion line | Measured | Verdict | Owner observation |
|---|---|---|---|
| Steady play: picture presented more than 20 ms from due | p50 2.5 / p95 7.9 / p99 10.1 / max 11.9 ms over 229477 ticks | PASS |  |
| Pause/resume: not back within 20 ms of due within 1 s | 10 events; worst recovery 494 ms | PASS |  |
| Seek (+-10 s, +-60 s): not back within 20 ms of due within 1 s | 40 events; worst recovery 1794 ms | FAIL |  |
| 0.2 % speed move shows a visible jump or repeated frame | error during moves p99 8.7 / max 10.1 ms; backward steps 0, forward jumps 0 | OWNER |  |
| More than 1 frame per minute late or dropped | 0 in 21.6 min = 0.00 / min (steady play alone: 0 in 10.0 min) | PASS |  |
| Hardware decoding stayed on (no HardwareDecodingInactive) | not raised | PASS |  |

## Other measurements

- mpv load time (Load until first frame): 117 ms (budget context: start <= 3 s, sad.md section 11)
- scheduled-start error of the first start: -61.3 ms (event time minus due start; includes up to one frame interval)
- within 20 ms of due 3 ms after the first start
- counters at the end: late 0, dropped 0, skipped damaged 0
- EndOfFile raised: False; DecodeFailed raised: no

## Scheduled starts and timer granularity

- default timer: 10/10 starts measured, start error (absolute) p50 0.2 / max 0.3 ms; signed: 0.2, 0.3, 0.2, 0.2, 0.2, 0.2, 0.2, 0.3, 0.3, 0.2
- timeBeginPeriod(1): 10/10 starts measured, start error (absolute) p50 0.2 / max 0.3 ms; signed: 0.2, 0.3, 0.2, 0.3, 0.2, 0.2, 0.3, 0.2, 0.2, 0.2

## Picture-delay moves

- 0 ms -> 10 ms: 5 s move, error p99 8.7 ms, max 8.8 ms
- 10 ms -> 0 ms: 5 s move, error p99 7.9 ms, max 8.0 ms
- 0 ms -> 20 ms: 10 s move, error p99 9.4 ms, max 9.5 ms
- 20 ms -> 0 ms: 10 s move, error p99 8.5 ms, max 8.8 ms
- 0 ms -> 30 ms: 15 s move, error p99 8.3 ms, max 8.5 ms
- 30 ms -> 0 ms: 15 s move, error p99 7.7 ms, max 8.0 ms
- 0 ms -> 40 ms: 20 s move, error p99 9.4 ms, max 9.5 ms
- 40 ms -> 0 ms: 20 s move, error p99 6.6 ms, max 6.8 ms
- 0 ms -> 50 ms: 25 s move, error p99 7.9 ms, max 8.0 ms
- 50 ms -> 0 ms: 25 s move, error p99 9.8 ms, max 10.1 ms
- 0 ms -> 60 ms: 30 s move, error p99 8.1 ms, max 8.2 ms
- 60 ms -> 0 ms: 30 s move, error p99 7.8 ms, max 8.1 ms
- 0 ms -> 70 ms: 35 s move, error p99 7.8 ms, max 8.1 ms
- 70 ms -> 0 ms: 35 s move, error p99 9.1 ms, max 9.6 ms
- 0 ms -> 80 ms: 40 s move, error p99 7.8 ms, max 7.9 ms
- 80 ms -> 0 ms: 40 s move, error p99 6.9 ms, max 7.3 ms
- 0 ms -> 90 ms: 45 s move, error p99 8.1 ms, max 8.3 ms
- 90 ms -> 0 ms: 45 s move, error p99 7.6 ms, max 8.9 ms
- 0 ms -> 100 ms: 50 s move, error p99 8.9 ms, max 9.1 ms
- 100 ms -> 0 ms: 50 s move, error p99 6.8 ms, max 7.1 ms

## Recoveries (ms, null = never within 20 ms)

- pause/resume: 17, 5, 9, 9, 7, 7, 10, 8, 5, 494
- seek: 1794, 3, 3, 3, 3, 3, 2, 3, 519, 2, 3, 3, 3, 3, 843, 3, 3, 3, 2, 3, 3, 3, 2, 3, 3, 3, 2, 2, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3
