---
status: Accepted
owner: "Volodymyr Kravchuk"
reviewers: []
updated_at: "2026-11-11"
feature_size: "L"
ticket: ""
---

# 0007 — Publish per-device records and other engine events as one observable stream from the engine-control loop

- **Status:** Accepted
- **Date:** 2026-11-11
- **Deciders:** Volodymyr Kravchuk (project owner), during the design Socratic walk

## Context

AC-11 asks for a once-per-second record per device (drift, current correction, cumulative underruns, device id, master-clock position), raised as an engine event other parts of the player can subscribe to and written to the log. Spec §7 sets "0 engine changes" for roadmap step 6 to read these records. The same channel also needs to carry excursions (AC-07), device-lost events (AC-06) and the run summary (AC-13). The values originate on the real-time render threads.

## Decision drivers

- Spec AC-11 and §7 KPI: step 6 subscribes without engine changes.
- QG-3: nothing on a render thread may block or allocate (§8 real-time rule).
- Repo convention: Core raises domain events (C# events / `IObservable`) that consumers subscribe to.

## Considered options

1. **`IObservable<EngineEvent>` published by the engine-control loop** — render threads only update lock-free counters; once per second, timed by `TimeProvider`, the control loop snapshots every output, publishes one `SyncRecord` per device and writes the structured log entry; excursions, device-lost events and the run summary go through the same stream.
2. **Separate typed C# events raised by the same engine-control loop** — `SyncRecordPublished`, `ExcursionRecorded`, `DeviceLost`, `RunEnded`, each its own event on the engine facade.

## Decision outcome

**Chosen:** option 1. Both options keep subscribers off the real-time threads; one stream wins because step 6 subscribes once and receives every kind of event, including kinds added later, whereas typed events need a new subscription per event kind — at odds with the "0 engine changes for step 6" KPI.

## Consequences

**Positive**
- A slow subscriber cannot cause an underrun.
- One subscription point for every engine event; step 6 adds a subscriber, not engine code.

**Negative**
- Events arrive on the engine-control thread; UI subscribers (roadmap steps 4 and 8) marshal through `Dispatcher.UIThread`, as the repo convention already requires.
- Counter snapshots are up to one second old by design.

**Neutral**
- `EngineEvent` is a closed set of records in `Core/Diagnostics/`; adding an event kind later is additive for subscribers that ignore unknown kinds.

## Links

- Spec: [[../spec.md]] AC-06, AC-07, AC-11, AC-13, §7
- SAD: [[../sad.md]] §8
- Related ADR: [[0006-coordinate-pause-and-seek-with-playback-epochs-while-device-streams-keep-running]]
