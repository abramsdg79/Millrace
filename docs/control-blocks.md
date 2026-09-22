# Control blocks

A control block is a PLC rung: a small, stateful, pure function of the tags it
reads, scanned at its own period, whose outputs are ordinary tags. `Dse.Control`
carries five of them — a timer, a permissive, an interlock, an alarm and a
sequencer — and `Dse.Core` carries the host that scans them.

`Dse.Control` references `Dse.Io.Abstractions` and nothing else. A block cannot
see a `Simulation`, a directory, a binding, a clock or an event log, which is
why a block's unit tests need none of them.

## The contract

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

Three pin classes. **Inputs** are read. **Writes** are commanded. **Outputs**
and **commands** are owned: the host publishes them as `<Id>.<Name>`, outputs
read-only and commands read-write. A command is therefore an ordinary tag write
— from a `CommandBus`, a scenario `write`, `Simulation.WriteAt` or a bound port
— and plan 5b's recorder captures it like any other.

`ScanInputs` gives `Input(int)` over `Inputs` order, `Command(int)` over
`Commands` order, `Tick`, `Now`, `DeltaSeconds` (the simulation step) and
`Elapsed` (simulation seconds since this block's previous scan, zero on the
first). `ScanOutputs` takes `Set(int, TagValue)` over `Outputs` order,
`Write(int, TagValue)` over `Writes` order and `Raise(code, message)`. **An
output not set in a scan holds its previous value**, as a PLC output does. The
host checks a stored output's kind against the kind its `TagSpec` declared: a
mismatch throws `InvalidOperationException` naming the block, the pin and both
kinds, so a block cannot silently publish a different kind than it advertised.

A scan that neither raises an event nor queues a write allocates nothing: the
host allocates every buffer once and clears rather than reallocates them. An
event message is a string, and a queued write goes through a concurrent queue,
so both allocate a little — which is why a block raises and commands on
transitions rather than on every scan.

## Attaching a block

```csharp
builder.AddScanBlock(new Interlock(
    "INT01",
    [new Condition("CV001.Tripped", false), new Condition("PERM01.Ok", true)],
    [new BlockWrite("CV001.Start", TagValue.Bool(false))],
    TimeSpan.FromMilliseconds(100)));
```

Blocks are attached **in code**. Describing them in the plant JSON — a
`controllers` section, catalogue descriptors, a loader stage and schema — is a
later plan: the block API gets a shakedown before it is frozen into a file
format. `dse run` therefore cannot attach blocks yet.

`Build()` checks every block:

| code | check |
|---|---|
| DSE013 | `ScanPeriod` is a positive whole number of time steps. |
| DSE014 | Every `Inputs` and `Writes` entry names a tag the plant has, with the same kind; every `Writes` entry is read-write. |
| DSE015 | The block id is unique across components and blocks, and no owned tag name collides with an existing tag. |

`Inputs` and `Writes` are resolved against the plant's tags **and every block's
owned tags**, whichever order the blocks were added, so an interlock may list
`PERM01.Ok` before `PERM01` is added.

## The timing rule

A scan at tick *N*:

- sees every input and command **as of the end of tick *N−1***;
- publishes its outputs at the end of tick ***N***, so anything that reads them
  — another block, a bound component input, an HMI — sees them at tick ***N+1***;
- queues its writes, which land at **phase 1 of tick *N+1***.

At a 100 ms period on a 10 ms step, a block reacts to the plant between 10 ms
and 110 ms late. This is the PLC asymmetry, and it is what makes scan order
among blocks due on the same tick irrelevant to the result: every block in a
tick reads the same previous publish. Blocks added first scan first, and that
order is fixed for the life of the plant.

Two consequences worth stating:

- **A block's first scan is at tick 0**, before any write scheduled for tick 0
  lands, and it reads the primed image — the plant's initial values, and the
  *default* of every owned output that has not been published yet. An interlock
  watching `PERM01.Ok` therefore trips at tick 0, exactly as a real plant powers
  up latched. Reset it as part of your start-up sequence.
- **A sequencer step takes at least two scans**: its entry writes are queued on
  the scan that enters it and land one tick later, so its transition is first
  tested on the following scan.

A plant with no blocks schedules nothing and adds nothing to the directory.

## Composition

Blocks compose through tags only. There is no block-to-block wiring API and no
ordering declaration: an interlock lists `PERM01.Ok` or `CUR01.Hi.Active` as an
input and sees it one scan late by the timing rule, like two rungs in different
scan groups.

Operators are writes. `Ack`, `Reset`, `Start`, `Hold`, `Resume` and `Abort` are
read-write tags, so a `CommandBus` write, a scenario `write` action and a test's
`WriteAt` are the same thing.

Two blocks may name the same tag in their `Writes`. Both writes are queued and
applied at phase 1 of the following tick in enqueue order, and blocks scan in
the order they were added, so the block added later wins — deterministic, and
exactly what a PLC does with a double coil, but, as on a PLC, it is usually a
mistake worth checking for.

## `Timer`

```csharp
new Timer("TMR01", TimerMode.OnDelay, "CV001.Running", TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(100))
```

| pin | kind | |
|---|---|---|
| input | Bool | the tag to time |
| `Q` | Bool | output |
| `ET` | Double, `s` | elapsed time |

`OnDelay` (IEC TON) raises `Q` once the input has held true for `Preset`;
`OffDelay` (TOF) holds `Q` true for `Preset` after the input falls; `Pulse` (TP)
gives one `Preset`-long pulse on a rising edge and is not retriggerable while it
runs. A preset of zero acts immediately. `ET` accumulates the scan period, so it
is quantised to it: a 100 ms timer measures in tenths of a second. No events.

## `Permissive`

```csharp
new Permissive("PERM01",
    [new Condition("CV001.SafetyOk", true), new Condition("Pile.Full", false)],
    TimeSpan.FromMilliseconds(100))
```

The conditions something needs before it may **start**. `Ok` is every condition
at its normal polarity, re-evaluated every scan and **never latched**.
`FirstOut` is the index of the first condition to leave normal while `Ok` was
true, and −1 when nothing is out.

Events: `PERMISSIVE_LOST` — `CV001.SafetyOk dropped.` — and `PERMISSIVE_OK` —
`All conditions normal.`

## `Interlock`

```csharp
new Interlock("INT01",
    [new Condition("CV001.Tripped", false), new Condition("PERM01.Ok", true)],
    [new BlockWrite("CV001.Start", TagValue.Bool(false))],
    TimeSpan.FromMilliseconds(100))
```

The conditions that **stop** a running thing. Any abnormal condition latches
`Tripped`, drops `Ok`, captures `FirstOut` and sends the trip writes — **on the
trip scan only**, so the event log carries one `WRITE` record, not one every
scan. The latch clears on a rising edge of the `Reset` command while every
condition is normal, and on nothing else.

Events: `INTERLOCK_TRIP` — `CV001.Tripped abnormal.` — and `INTERLOCK_RESET` —
`Reset with all conditions normal.`

## `Alarm`

```csharp
new Alarm("CUR01", "CV001.Current",
    [
        new AlarmLimit(AlarmLimitKind.Hi,   3.0, 0.2, TimeSpan.FromSeconds(0.5)),
        new AlarmLimit(AlarmLimitKind.HiHi, 8.0, 0.5, TimeSpan.FromSeconds(0.1)),
    ],
    TimeSpan.FromMilliseconds(100))
```

One Double tag and up to four limits, which must ascend
`LoLo < Lo < Hi < HiHi` among those configured. Each limit publishes
`<Kind>.Active` and `<Kind>.Acked`, and the pair is the ISA-18.2 state:

| `Active` | `Acked` | state |
|---|---|---|
| false | true | normal |
| true | false | unacknowledged alarm |
| true | true | acknowledged alarm |
| false | false | cleared, unacknowledged |

A limit raises when the value crosses it and stays across for `OnDelay` (zero is
allowed), and clears when it recrosses by `Deadband`. A raise clears `Acked`; a
clear leaves `Acked` alone. A rising edge of the `Ack` command acknowledges
every limit with anything outstanding, still active or not.

Events: `ALARM_RAISED` — `Hi: 82.3 above 80.` — `ALARM_CLEARED` —
`Hi: 71.5 back within limits.` — and `ALARM_ACKED` — `Hi acknowledged.`

## `Sequencer`

```csharp
new Sequencer("SEQ01",
    [
        new SequenceStep("Start the belt",
            [new BlockWrite("CV001.Start", TagValue.Bool(true))],
            StepTransition.When("CV001.Speed", PredicateOperator.GreaterOrEqual, TagValue.Double(1.0)),
            TimeSpan.FromSeconds(15)),
        new SequenceStep("Run the feed",
            [new BlockWrite("Feed.Enabled", TagValue.Bool(true))],
            StepTransition.After(TimeSpan.FromSeconds(60))),
    ],
    TimeSpan.FromMilliseconds(200),
    [new BlockWrite("CV001.Start", TagValue.Bool(false))])
```

A linear sequence — there is no branching, and no parallel step. Each step names
what it commands on entry, what ends it, and how long it may run.

A transition is either `StepTransition.When(tag, op, value)` with `op` in
`Equal`, `NotEqual`, `Less`, `LessOrEqual`, `Greater`, `GreaterOrEqual` (a Bool
tag allows only the first two), or `StepTransition.After(delay)` on the step
clock. A step whose `timeout` elapses first faults the sequence and drives the
abort writes — the same writes an operator `Abort` sends — so a fault leaves the
plant in the same safe state an abort would, not a state a real operator never
sees.

The step clock is an accumulated `double`, and a transition or a timeout fires
once it is `>=` the target, not when it exactly reaches it: it can fire a scan
late. Measured on the worked example's 100 ms scan: an `After(60)` elapsed in
exactly 60.000 s, but an `After(2)` took 2.200 s — one scan later than the naive
2.000 s, because ten accumulations of 0.1 sum to 1.999… s, just under the
target, so an eleventh scan is needed.

Outputs are `Step` (Int64, 0 when idle), `Running`, `Held`, `Complete`,
`Faulted` and `StepTime` (Double, `s`). Commands are `Start`, `Hold`, `Resume`,
`Abort` and `Reset`, all rising-edge sensitive so a tag left high does not
retrigger: `Start` from idle enters step 1, `Hold` freezes the step clock,
`Resume` continues, `Abort` returns to idle with the abort writes, and `Reset`
returns to idle from `Faulted` or `Complete`. A scan that accepts `Start`,
`Hold`, `Resume`, `Abort` or `Reset` does nothing else that scan: in particular,
the step clock does not tick on the scan a `Resume` is accepted, so a
held-and-resumed step costs one extra scan period over one that never held.

Events: `STEP_ENTERED` — `2: Start the belt.` — `SEQUENCE_COMPLETE` —
`Finished after 6 steps.` (pluralised correctly: one step reads `Finished after
1 step.`) — `SEQUENCE_FAULTED` — `Step 2 timed out after 30 s.` — and
`SEQUENCE_ABORTED` — `Aborted at step 3.`

## Writing your own

Implement `IScanBlock`. Validate constructor parameters with `ArgumentException`
as a component does; `TagNameRules` (in `Dse.Io.Abstractions`) is public, so a
block may check an id or a pin name against the same rules `TagRef` and
`TagSpec` already enforce on every pin, rather than duplicating them. Build the
pin lists once, in the constructor, and never change them. Keep state in
fields, set every output on every scan (or deliberately do not, and document
that it holds), and make every event message a sentence ending in a full stop —
the event log is a golden-file format, and its bytes are a contract.

`tests/Dse.Control.Tests/Scan.cs` shows the pattern for a pure test: values in,
published values, writes and events out, no `Simulation`.

## What is not here

Branching sequential function charts; PID; alarm shelving, priorities and a
dedicated `LiveState.Alarms`; blocks described in the plant file. All are later
plans.
