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
A scan that throws — including that kind-check `InvalidOperationException` —
aborts the tick before the clock advances and the block is not rescheduled; a
`Simulation` that has thrown out of `Tick()` must be rebuilt.

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

That is the code form. A plant file declares the same block under
`controllers` — see *In the plant file* below — and `dse validate`, `dse tags`
and `dse run` handle it with no C#.

`Build()` checks every block:

| code | check |
|---|---|
| DSE013 | `ScanPeriod` is a positive whole number of time steps. |
| DSE014 | Every `Inputs` and `Writes` entry names a tag the plant has, with the same kind; every `Writes` entry is read-write. |
| DSE015 | The block id is unique across components and blocks, and no owned tag name collides with an existing tag. |
| DSE016 | Every claim names a read-write tag in the block's `Writes`, once; no two blocks claim one tag; no other block's `Writes` names a claimed tag. |

`Inputs` and `Writes` are resolved against the plant's tags **and every block's
owned tags**, whichever order the blocks were added, so an interlock may list
`PERM01.Ok` before `PERM01` is added.

### In the plant file

A plant file lists its blocks under `controllers`, a sibling of `components`.
Each entry has the component envelope — `id`, `type`, `parameters` — plus
`scanPeriodMs`, which is required: a PLC task period has no sensible default;
it may also list `claims` (*Claiming a tag*, below).

```json
"controllers": [
  { "id": "INT01", "type": "interlock", "scanPeriodMs": 100,
    "parameters": {
      "conditions": [
        { "tag": "CV001.Tripped", "normal": false },
        { "tag": "PERM01.Ok",     "normal": true } ],
      "trip": [ { "tag": "CV001.Start", "value": false } ] } }
]
```

Durations are seconds and end in `S` — `presetS`, `timeoutS`, `delayS`,
`onDelayS` — and are at most a year. Only the scan period is in milliseconds,
like `timeStepMs` and a PLC task: a whole number of time steps, at most a day. A
controller id follows the component id rule — no dot, no whitespace — and is
unique across components and controllers.

A tag parameter is a tag's full name, a plant tag or one a block owns, in any
file order: an interlock may list `PERM01.Ok` before `PERM01` is declared. A
value is `true`, `false` or a number, converted to the kind of the tag it is for:
an integer-valued number fits an Int64 or a Double tag, a fraction only a Double,
and only `true` and `false` fit a Bool. The loader resolves every tag and every
value before it builds any block, and reports every mistake at its path:

| code | check |
|---|---|
| DSE113 | The tag exists; the fix names the nearest one. |
| DSE114 | The tag is of a kind the block can use, and the value fits the tag. |
| DSE115 | A tag the block commands is read-write — not a measured value, another block's output, or an input a signal link drives. |

Block types come from the catalogue: `ControlModule`, in
`Dse.Control.Catalogue`, registers the five below, and `dse catalog export`
lists them under `"blocks"`. A module loaded with `--assembly` may register
more.

**File order is scan order.** Blocks due on the same tick scan in the order the
`controllers` array lists them, and when two write one tag on one tick the later
one wins.

### Claiming a tag

A block may **claim** a plant tag it writes: a claimed tag is written by that
block and by nothing else, as a PLC program's permit bit is written by its own
rung and never by the HMI. A controller entry lists its claims in an optional
`"claims"` array of full tag names; in code, `AddScanBlock` takes the same list.

```json
{ "id": "INT01", "type": "interlock", "scanPeriodMs": 100,
  "claims": [ "CV001.Permit" ],
  "parameters": {
    "conditions": [ { "tag": "CV001.Tripped", "normal": false } ],
    "trip":  [ { "tag": "CV001.Start", "value": false }, { "tag": "CV001.Permit", "value": false } ],
    "reset": [ { "tag": "CV001.Permit", "value": true }, { "tag": "CV001.Start", "value": false } ] } }
```

```csharp
builder.AddScanBlock(interlock, ["CV001.Permit"]);
```

The claimant's own writes land and log exactly as before — `Set to true by
INT01.` Every other writer is refused before anything is queued. The directory
publishes the tag `ReadOnly` with `ClaimedBy` set to the block's id, and
`dse tags` prints it as `CV001.Permit  Bool  ReadOnly  …  claimed by INT01`, so an
OPC UA server or an HMI generator sees an ordinary read-only tag. `TagImage.Write`,
`Simulation.WriteAt` and `WriteIn` throw
`Tag 'CV001.Permit' is claimed by INT01; only that block writes it.`; the
realtime `CommandBus` answers `ReadOnly`; a scenario that writes it does not bind
(`DSE206`, naming the claimant). Another block whose writes name a claimed tag
fails validation, like a PLC's duplicate-coil check. There is no force or
override. A claim must name a read-write tag the block commands — not a measured
value, another block's output, or an input a signal link drives — and each tag
has one claimant; every breach is `DSE016`, reported in a plant file at the
`claims` entry it is about. A block may claim another block's command (a
sequence that alone resets an interlock), or its own.

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

### The worked example

`tests/Dse.Control.Tests/Golden/conveyor-control.log` is a permissive, an
interlock, a current alarm and a start-up sequencer running the conveyor plant
for two simulated minutes, with a thermal-overload fault injected at 40 s. Its
tick at 40.000-40.110 s is the clearest illustration of the timing rule end to
end — a plant-driven trip, a block that reacts one scan later, and a write
that lands the tick after that:

```
06:00:40.000  CV001.Starter  OVERLOAD_TRIP  Thermal state 1.1796472378913028 reached the trip level 1.1.
06:00:40.100  INT01  INTERLOCK_TRIP  CV001.Tripped abnormal.
06:00:40.110  CV001.Start  WRITE  Set to false by INT01.
```

A write a block issues is logged with its origin — `Set to false by INT01.` —
and every other write keeps the plain `Set to false.`, so the log tells a
block's command from an operator's. The example is also a plant file,
`tests/Dse.Configuration.Tests/Plants/valid/conveyor-control.json`; with the
scenario `tests/Dse.Cli.Tests/Scenarios/conveyor-control.json`, `dse run`
reproduces this golden byte for byte.

The example deliberately has no interlock between the feed and the belt: once
`INT01` trips the belt, `Feed.Enabled` stays true for a further 24 s onto a
stopped belt, and the sequence runs to `SEQUENCE_COMPLETE` while the plant is
still tripped. The mine-conveyor sample interlocks the feed on the belt
instead, so this gap is not carried forward as a pattern to copy.

### The full-size example

`samples/mine-conveyors/` is the control layer at plant scale: twelve blocks
over three conveyors — a permissive per belt on its safety relay, cascade
interlocks that read the downstream belt's contactor auxiliary contact and
zero-speed switch, current alarms, and a start and a stop sequencer — with
nine scenarios and their goldens. Its
README explains each design choice; its `plant.json` is the file to copy from.
Its interlocks hold their devices off with the run permit described under
`Interlock` below; its scenario 9, `start-while-tripped`, writes a start while
the cascade is tripped, and nothing moves — not then, and not after the reset.

## `Timer`

```csharp
new Timer("TMR01", TimerMode.OnDelay, "CV001.Running", TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(100))
```

| pin | kind | |
|---|---|---|
| input | Bool | the tag to time |
| `Q` | Bool | output |
| `ET` | Double, `s` | elapsed time |

In a plant file (`mode` is `on-delay`, `off-delay` or `pulse`):

```json
{ "id": "TMR01", "type": "timer", "scanPeriodMs": 100,
  "parameters": { "mode": "on-delay", "input": "CV001.Running", "presetS": 5 } }
```

`OnDelay` (IEC TON) raises `Q` once the input has held true for `Preset`;
`OffDelay` (TOF) holds `Q` true for `Preset` after the input falls; `Pulse` (TP)
gives one `Preset`-long pulse on a rising edge and is not retriggerable while it
runs. A preset of zero acts immediately. `ET` accumulates the scan period, so it
is quantised to it: a 100 ms timer measures in tenths of a second. No events.

Spec gap: on `Pulse`, once the pulse ends `ET` holds at `Preset` rather than
resetting to 0 when the input falls, as IEC 61131-3 specifies. This has stood
unchanged through review; no test or caller depends on the IEC-exact behaviour.

## `Permissive`

```csharp
new Permissive("PERM01",
    [new Condition("CV001.SafetyOk", true), new Condition("Pile.Full", false)],
    TimeSpan.FromMilliseconds(100))
```

In a plant file:

```json
{ "id": "PERM01", "type": "permissive", "scanPeriodMs": 100,
  "parameters": { "conditions": [
    { "tag": "CV001.SafetyOk", "normal": true },
    { "tag": "Pile.Full",      "normal": false } ] } }
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
    [new BlockWrite("CV001.Start", TagValue.Bool(false)), new BlockWrite("CV001.Permit", TagValue.Bool(false))],
    TimeSpan.FromMilliseconds(100),
    [new BlockWrite("CV001.Permit", TagValue.Bool(true)), new BlockWrite("CV001.Start", TagValue.Bool(false))])
```

The last argument, the reset writes, may be left out: the four-argument
constructor is an interlock with none.

In a plant file (`trip` and `reset` may be left out):

```json
{ "id": "INT01", "type": "interlock", "scanPeriodMs": 100,
  "parameters": {
    "conditions": [ { "tag": "CV001.Tripped", "normal": false }, { "tag": "PERM01.Ok", "normal": true } ],
    "trip": [ { "tag": "CV001.Start", "value": false }, { "tag": "CV001.Permit", "value": false } ],
    "reset": [ { "tag": "CV001.Permit", "value": true }, { "tag": "CV001.Start", "value": false } ] } }
```

The conditions that **stop** a running thing. Any abnormal condition latches
`Tripped`, drops `Ok`, captures `FirstOut` and sends the trip writes — **on the
trip scan only**, so the event log carries one `WRITE` record, not one every
scan. The latch clears on a rising edge of the `Reset` command while every
condition is normal, and on nothing else. The scan that clears it sends the
reset writes, once; a refused reset sends nothing. Within each list a tag may
appear once. A tag may be in both lists, with the same kind; the block then has
one write pin for it. `Writes` lists the trip tags first, in order, then any tag
only the reset writes — and the host sends one scan's writes in that pin order,
so the reset above logs `CV001.Start` before `CV001.Permit`.

Events: `INTERLOCK_TRIP` — `CV001.Tripped abnormal.` — and `INTERLOCK_RESET` —
`Reset with all conditions normal.`

### Holding a device off: the run permit

A trip write is sent once. On its own it drops a run command, but anything that
writes the command true again while the interlock is still tripped starts the
device. To make an interlock a true start inhibit — an interlock contact in
series in the run circuit — give the device a run permit and drive it from both
lists: on trip, the command false and `Permit` false; on reset, `Permit` true
and the command false again. `motor-starter` (and so `conveyor`, as
`CVn.Permit`) and `bulk-source` have a `Permit` input, true when unwired, ANDed
into their run logic; a welded contactor still defeats it. The reset's `false`
is the seal-in: a command written during the trip is refused while the permit
is false and forgotten when it comes back, so a device runs again only on a
fresh command after the reset. A sequence that resets an interlock and starts
its device must therefore do it in two steps — reset, wait for the interlock's
`Ok`, then command — or the reset's `false` lands after its start. In this
pattern each trip logs two writes, `Set to false by INT01.` for the command and
again for `Permit`; each reset also logs two, `Set to true by INT01.` for
`Permit` and `Set to false by INT01.` for the command. Claim the permit for the
interlock (*Claiming a tag*, above): unclaimed, it is an ordinary writable tag,
and anything that writes it true while the interlock is still tripped defeats
the inhibit until the interlock trips again. The mine-conveyor sample uses this
pattern on every interlock, and each interlock claims its device's permit.

## `Alarm`

```csharp
new Alarm("CUR01", "CV001.Current",
    [
        new AlarmLimit(AlarmLimitKind.Hi,   3.0, 0.2, TimeSpan.FromSeconds(0.5)),
        new AlarmLimit(AlarmLimitKind.HiHi, 8.0, 0.5, TimeSpan.FromSeconds(0.1)),
    ],
    TimeSpan.FromMilliseconds(100))
```

In a plant file (`kind` is `lo-lo`, `lo`, `hi` or `hi-hi`; `deadband` and
`onDelayS` default to 0):

```json
{ "id": "CUR01", "type": "alarm", "scanPeriodMs": 100,
  "parameters": {
    "input": "CV001.Current",
    "limits": [
      { "kind": "hi",    "value": 3.0, "deadband": 0.2, "onDelayS": 0.5 },
      { "kind": "hi-hi", "value": 8.0, "deadband": 0.5, "onDelayS": 0.1 } ] } }
```

One Double tag and up to four limits, which must ascend
`LoLo < Lo < Hi < HiHi` among those configured. Each limit publishes
`<Kind>.Active` and `<Kind>.Acked`, and the pair is the ISA-18.2 state.
`Acked` starts true on power-up: with nothing yet outstanding, a limit powers
up normal, not as an unacknowledged alarm.

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

Events: `ALARM_RAISED` — `Hi: 82.5 above 80.` — `ALARM_CLEARED` —
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

In a plant file (`op` is `==`, `!=`, `<`, `<=`, `>` or `>=`; `writes`, `abort`
and `timeoutS` may be left out):

```json
{ "id": "SEQ01", "type": "sequencer", "scanPeriodMs": 200,
  "parameters": {
    "steps": [
      { "name": "Start the belt",
        "writes": [ { "tag": "CV001.Start", "value": true } ],
        "transition": { "type": "when", "tag": "CV001.Speed", "op": ">=", "value": 1.0 },
        "timeoutS": 15 },
      { "name": "Run the feed",
        "writes": [ { "tag": "Feed.Enabled", "value": true } ],
        "transition": { "type": "after", "delayS": 60 } } ],
    "abort": [ { "tag": "CV001.Start", "value": false } ] } }
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
late. Measured on the worked example's 200 ms scan: an `After(60)` elapsed in
exactly 60.000 s (300 accumulations of 0.2 sum to 60.00000000000031, at or past
the target on the 300th scan), but an `After(2)` took 2.200 s — one scan later
than the naive 2.000 s, because ten accumulations of 0.2 sum to
1.9999999999999998, just under the target, so an eleventh scan is needed.

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
dedicated `LiveState.Alarms`. All are later plans.
