# Control Blocks — Design (plan 5c)

Date: 2026-09-22. Addendum to
`2026-09-02-industrial-process-simulation-engine-design.md` (the "main spec"),
refining its sections 4, 11 and 17 for `Dse.Control` and the scan host in
`Dse.Core`. Builds on plans 5a (catalogue, configuration, CLI) and 5b
(scenarios, replay, `dse run`), both merged.

## 1. Scope

**Plan 5c — this document.** The five control blocks the main spec names —
interlock, permissive, sequencer, timer, alarm — as pure scan blocks in a new
`Dse.Control` project; a scan host in `Dse.Core` that schedules each block's
scans through the event queue at its own period; alarm state as tags the alarm
block owns; and the two crash paths parked by plan 5b's final review.

Blocks are **built and attached in code** in 5c. Describing them in the plant
JSON (a `controllers` section, catalogue descriptors for blocks, a loader
stage, schema and diagnostics) is a later plan: the block API gets a shakedown
before it is frozen into a file format. `dse run` therefore cannot attach
blocks in 5c; the worked example's golden is a test golden, not a CLI one, and
the documentation says so.

Out of scope, named: branching sequential function charts; PID (main spec 18
defers it); alarm shelving, priorities and a dedicated `LiveState.Alarms`; CSV
telemetry; `--speed`; scenario JSON Schema.

### Success criteria

1. A block written against `Dse.Io.Abstractions` alone scans at its own period
   through the event queue, reads the plant sampled and stale as a PLC would,
   and its outputs are ordinary tags: visible in `dse tags`, `LiveState`, tick
   frames and the scenario recorder with no change to `Dse.Realtime`.
2. Each block has a pure unit-test suite (records in, records out) that needs
   no `Simulation`, plus a host-level test over a real plant.
3. The worked example — the conveyor plant with a permissive, an interlock, an
   alarm and a sequencer — runs to a committed golden event log that shows the
   sequence stepping, the alarm raising on the start inrush, the interlock
   tripping on the overload and holding the run command low, and the sequencer
   faulting or completing; produced and read, never invented.
4. A plant with no blocks behaves exactly as before: the 947 existing tests and
   the four 5b goldens are unchanged.
5. Zero external package references anywhere under `src/`; `Dse.Control`
   references only `Dse.Io.Abstractions`.

## 2. The contract (`Dse.Io.Abstractions`)

```csharp
public sealed record TagRef(string Name, TagKind Kind);
public sealed record TagSpec(string Name, TagKind Kind, string Unit = "", string Description = "");
public readonly record struct BlockEvent(string Code, string Message);

public interface IScanBlock
{
    string Id { get; }                        // prefixes every owned tag: "INT01" → "INT01.Ok"
    TimeSpan ScanPeriod { get; }
    IReadOnlyList<TagRef>  Inputs   { get; }  // plant tags it reads, full names
    IReadOnlyList<TagRef>  Writes   { get; }  // plant tags it may command, full names
    IReadOnlyList<TagSpec> Outputs  { get; }  // tags it owns, read-only to the plant, relative names
    IReadOnlyList<TagSpec> Commands { get; }  // tags it owns, read-write, relative names
    void Scan(in ScanInputs inputs, ref ScanOutputs outputs);
}
```

Three pin classes: **inputs** are read, **writes** are commanded, **outputs**
and **commands** are owned. The block never sees a directory, a binding, a
clock or a log.

`ScanInputs` (readonly struct): `TagValue Input(int index)` over `Inputs`
order; `TagValue Command(int index)` over `Commands` order; `long Tick`;
`DateTimeOffset Now`; `double DeltaSeconds` (the simulation step);
`double Elapsed` — simulation seconds since this block's previous scan (zero on
the first).

`ScanOutputs` (mutable struct, reference-backed so copies alias, owned and
reused by the host): `Set(int index, TagValue)` over `Outputs` order;
`Write(int index, TagValue)` over `Writes` order; `Raise(string code, string
message)`; `Reset()`, `TryOutput`/`TryWrite`/`Events` for tests. Both structs
are publicly constructible so a block's pure tests can build them. An output
not set in a scan holds its previous value, as a PLC output does. Nothing on
the scan path allocates once the host is initialised except a raised event's
message string.

## 3. The scan host (`Dse.Core`)

**`SimulationBuilder.AddScanBlock(IScanBlock block)`**, at build:

| code | check |
|---|---|
| DSE013 | `ScanPeriod` is a positive whole number of steps. |
| DSE014 | Every `Inputs` and `Writes` entry names a tag the directory holds, with the same kind; every `Writes` entry is read-write. |
| DSE015 | The block id is unique across components and blocks, and no owned tag name collides with an existing tag. |

Messages are sentences with a fix, like the existing `DSE001`–`DSE011`. Owned
tags become directory entries: outputs `ReadOnly`, commands `ReadWrite`, with
the block's unit and description. A command is therefore an ordinary tag write —
from a `CommandBus`, a scenario `write`, `WriteAt` or a bound port — and the
recorder of plan 5b captures it.

**Scheduling.** `Build()` schedules one `ScanEvent` per block at tick 0; each
reschedules itself every period. Scans run in phase 1 during the event drain in
schedule order: blocks added first scan first among equals.

**One scan:** read every input from the image published at the end of the
previous tick; supply the command values the same way; call `Scan`; store the
outputs into the block's own value slots (captured at the next publish, like any
tag); queue every write; log every raised event with `source` = the block id.

**Timing, stated once.** A scan at tick *N* sees inputs as of the end of tick
*N−1*; its outputs are published at the end of tick *N*; anything that reads
them — another block, a bound component input — sees them at tick *N+1*, and a
write it queued lands at phase 1 of tick *N+1*. At a 100 ms period on a 10 ms
step a block reacts to the plant between 10 and 110 ms late. This is the PLC
asymmetry the main spec asks for, and it is what makes scan order among equals
irrelevant to the result: every block in a tick reads the same previous
publish.

A plant with no blocks schedules nothing and adds nothing to the directory.

## 4. The blocks (`Dse.Control`)

Each block is one class, a pure `IScanBlock` with constructor parameters
validated with `ArgumentException` like a component. Owned tag names follow the
plant naming rules; every event message is a sentence ending in a full stop.

| block | inputs | commands | outputs | behaviour | events |
|---|---|---|---|---|---|
| **Timer** (`TimerMode.OnDelay` / `OffDelay` / `Pulse` — IEC 61131-3 TON / TOF / TP) | `In` (Bool) | — | `Q` Bool, `ET` Double s | TON: `Q` true once `In` has held true for `Preset`; TOF: `Q` stays true for `Preset` after `In` falls; TP: a `Preset`-long pulse on a rising edge. `ET` accumulates `Elapsed`, so it is quantised to the scan period. | none |
| **Permissive** | N Bool conditions, each with a normal polarity | — | `Ok` Bool, `FirstOut` Int64 | `Ok` = every condition normal, re-evaluated each scan, never latched. `FirstOut` = index of the first condition to leave normal while `Ok` was true, −1 when none; cleared when `Ok` returns. Conditions required to *start*. | `PERMISSIVE_LOST` "CV001.SafetyOk dropped." / `PERMISSIVE_OK` "All conditions normal." |
| **Interlock** | N Bool conditions with polarity | `Reset` Bool | `Ok`, `Tripped` Bool, `FirstOut` Int64 | Any condition abnormal → `Tripped` latches, `Ok` false, `FirstOut` captured, and the block's `Writes` (typically a run command set false) go out on the trip scan. Clears only on a `Reset` rising edge while every condition is normal. Conditions that *stop* a running thing. | `INTERLOCK_TRIP` "CV001.Tripped abnormal." / `INTERLOCK_RESET` "Reset with all conditions normal." |
| **Alarm** | one Double | `Ack` Bool | per configured limit `HiHi`, `Hi`, `Lo`, `LoLo`: `<Limit>.Active`, `<Limit>.Acked` Bool | A limit raises when the value crosses it and stays across for `OnDelay` seconds (0 allowed); clears when it recrosses by `Deadband`. `Acked` starts true and survives a clear; a raise sets it false; an `Ack` rising edge sets `Acked` on every unacknowledged limit, active or already returned to normal. Return to normal while unacknowledged leaves `Active=false, Acked=false` — the ISA-18.2 "cleared, unacknowledged" state — until acknowledged. Limits must be ordered `LoLo < Lo < Hi < HiHi` among those configured. | `ALARM_RAISED` "Hi: 82.3 above 80." / `ALARM_CLEARED` "Hi: 71.5 back within limits." / `ALARM_ACKED` "Hi acknowledged." |
| **Sequencer** (linear) | the union of every predicate's tags | `Start`, `Hold`, `Resume`, `Abort`, `Reset` Bool | `Step` Int64 (0 = idle), `Running`, `Held`, `Complete`, `Faulted` Bool, `StepTime` Double s | Ordered `Step(name, entryWrites[], transition, timeout?)`; transition is `Predicate(tag, op, value)` with `op` in `==, !=, <, <=, >, >=` or `After(seconds)`; a step's `timeout` elapsing → `Faulted`. `Start` rising edge from idle enters step 1; `Hold` freezes the step clock; `Resume` continues; `Abort` → idle with the optional abort write set; `Reset` from `Faulted` or `Complete` → idle. Entry writes go out on the scan that enters the step. Commands are rising-edge sensitive so a held-true tag does not retrigger. | `STEP_ENTERED` "2: Start the belt." / `SEQUENCE_COMPLETE` "Finished after 6 steps." / `SEQUENCE_FAULTED` "Step 2 timed out after 30 s." / `SEQUENCE_ABORTED` "Aborted at step 3." |

## 5. Composition

Blocks compose through tags only. An interlock may list `PERM01.Ok` or
`CUR01.Hi.Active` as an input and sees it one scan late by the timing
rule — like two rungs in different scan groups. There is no block-to-block
wiring API and no ordering declaration.

Operators are writes: `Ack`, `Reset`, `Start` are read-write tags, so a
`CommandBus` write, a scenario `write` action and a test's `WriteAt` are the
same thing.

### The worked example

The `conveyor-line` plant (`tests/Dse.Configuration.Tests/Plants/valid/`) plus,
in code:

- **PERM01** — permissive on `CV001.SafetyOk` (normal true) and `Pile.Full`
  (normal false).
- **INT01** — interlock on `CV001.Tripped` (normal false) and `PERM01.Ok`
  (normal true), writing `CV001.Start = false` on trip.
- **CUR01** — alarm on `CV001.Current`. Measured (plan 5c, scratch run of the
  conveyor plant): running current 1.38–1.56 A; start inrush peak 11.88 A, above
  8 A for 0.40 s and above 3 A for 1.37 s; during `thermal-bias` the overload
  trips on the injection tick and the current falls to zero — it does not climb.
  Limits: `Hi` 3.0 A (deadband 0.2, on-delay 0.5 s), `HiHi` 8.0 A (deadband
  0.5, on-delay 0.1 s) — so the alarm raises on the start inrush, which is the
  plant's only real current excursion.
- **SEQ01** — sequencer: pulse `CV001.SafetyReset` → pulse `INT01.Reset`
  (at tick 0 the safety relay is de-energised and `PERM01.Ok` reads false, so
  `INT01` trips at once and must be reset once the relay is healthy) → set
  `CV001.Start` true, wait `CV001.Speed >= 1.0` (timeout 15 s) → set
  `Feed.Enabled` true → `After(60)` → set `Feed.Enabled` false → set
  `CV001.Start` false, wait `CV001.Stopped == true` (timeout 60 s) → complete.

A scenario writes `SEQ01.Start` at 1 s and injects `CV001.Motor` `thermal-bias`
at 40 s. The golden must show the steps entered, the alarm raising on the
start inrush, `INT01` tripping on `CV001.Tripped` when the overload trips and
holding `Start` low, and `SEQ01` faulting on a step's timeout or completing —
whichever the run produces, read and reported, never invented.

## 6. Testing

`Dse.Core.Tests`
- `AddScanBlock` validation, one test per code (`DSE013`–`DSE015`).
- A stub block proving the timing rule tick-exactly: input as of *N−1*, output
  visible at *N+1*, a queued write landing at *N+1*, the period honoured, add
  order among equals; outputs held between scans; events logged under the block
  id; commands writable from `WriteAt` and read-only outputs refused; no
  `ScanEvent` when no block is added.
- Two same-seed runs with blocks are byte-identical (`DeterminismTests`
  pattern).

`Dse.Control.Tests`
- Pure suites, one per block, no `Simulation`: timer TON/TOF/TP edges and the
  quantisation of `ET`; permissive first-out and recovery; interlock latch,
  reset only when healthy, first-out, trip writes; alarm raise, deadband,
  on-delay, ack, return-unacknowledged, for each limit and for limit ordering;
  sequencer every command from every state, timeouts, entry writes, all six
  predicate operators, `After`; constructor validation for every block.
- One host-level test per block over a small plant.
- The worked example as a golden via `Golden.Assert`, and its run twice
  byte-identical.

## 7. Documentation

- `docs/control-blocks.md` — the contract, the timing rule with the 10/100 ms
  example, each block's pins, parameters and events, the composition rule, and
  the 5c limit: blocks are attached in code; plant-JSON wiring is a later plan.
- `docs/architecture.md` — "The control layer".
- `README.md` — status; `Dse.Control` in the module list.
- `docs/configuration-diagnostics.md` — regenerated for `DSE013`–`DSE015`; the
  trailer range `DSE001–DSE011` becomes `DSE001–DSE015` (and
  `docs/scenario-diagnostics.md`'s trailer, if it quotes the range).

## 8. Parked crash paths from plan 5b — the plan's first task

- `ScenarioLoader.ReadTimeStep`: `timeStepMs` beyond `TimeSpan.MaxValue`
  (`1e30`) throws `OverflowException` → exit 134. Bound it: `DSE202`.
- `StructureStage.ReadDefaults`: a plant `defaults.timeStepMs` that is positive
  but rounds to zero ticks passes the loader and aborts in `SimulationClock`
  at `Build()`; and `1e30` throws `OverflowException` out of the loader itself —
  both live crashes in `dse validate` and `dse tags` on master. Guard both in
  the loader: `DSE103`, with the "at least one tick (0.0001 ms)" wording plan
  5b used for scenarios, and an upper bound.

Both with tests that reproduce the crash from the shipped binary first.

## 9. Layout

| project | references | notes |
|---|---|---|
| `src/Dse.Io.Abstractions` | unchanged | gains the contract of section 2 |
| `src/Dse.Core` | unchanged | gains `AddScanBlock`, the scan host, `DSE013`–`DSE015` |
| `src/Dse.Control` | `Dse.Io.Abstractions` only | new; five blocks; no packages |
| `tests/Dse.Control.Tests` | `Dse.Control`, `Dse.Core`, `Dse.Components`, `Dse.Configuration`, `tests/Shared` | pure suites, host tests, the worked-example golden (`Dse.Scenarios` is not referenced: the runner cannot attach a block in 5c) |
| `Dse.Configuration`, `Dse.Scenarios`, `Dse.Cli`, `Dse.Realtime` | untouched apart from the section 8 fixes | |

Global constraints from 5a and 5b apply unchanged.
