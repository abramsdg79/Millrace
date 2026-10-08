# Architecture

## The tick

`Simulation.Tick()` runs five phases in a fixed order, and that order is the
determinism guarantee:

1. **Apply queued writes, then drain events** due at or before this tick.
   Writes queued from outside land first, in enqueue order; then scheduled
   events, ordered by `(dueTick, sequence)`. The sequence number is monotonic,
   so events due on the same tick always fire in the order they were scheduled.
   This is what lets `Simulation.WriteAt` and `InjectFaultAt` share one rule:
   the time you name is the tick at which the plant sees it.
2. **Evaluate the signal graph**, in two passes over the resolved topological
   order: first every component evaluates, then every component latches. The
   latch pass is where a component with no direct feedthrough (a `UnitDelay`)
   captures this tick's input for the next tick, independent of where the
   resolver placed it relative to its producer.
3. **Advance flow** — one downstream-first sweep over the material graph: each
   node discharges through its outgoing links into consumers that have already
   advanced and made room, then advances its own contents. The conservation
   audit runs after the sweep.
4. **Publish the I/O image.** Every binding is captured into a fresh array,
   diffed against the previous snapshot into a `DirtyMask`, and published with
   a volatile swap, so a reader on any thread sees a whole tick's values or
   none of them.
5. **Emit the tick frame.** The published array, its dirty mask and this tick's
   event-log records are wrapped in an immutable `TickFrame` and handed to the
   attached `ITickFrameSink`, if there is one, in a single non-blocking call.

The clock advances after phase 5, so a component evaluating on tick N sees tick
N's simulation time.

## The port model

A component reads its inputs, updates its own state, and writes its outputs. It
never touches another component. That single restriction is what makes
topological ordering meaningful and lets every component be unit-tested alone.

An output may drive many inputs. An input has exactly one source — two sources
is an undefined value, so the second connection throws at wiring time rather
than being reported later. An unconnected input reads its declared default, so a
partial plant still runs.

Composites never evaluate. `CompositeComponent` owns children and exposes
aliases of their ports; at build time the tree is flattened to leaves and their
ids are qualified with the composite path. There is no hard-coded conveyor
anywhere in the engine.

## Feedback loops

Some loops are real physics, not mistakes: belt load raises motor torque demand,
which lowers speed, which changes belt load. Validation detects the cycle, names
the components in it, and refuses to run until a `UnitDelay` breaks it.
`UnitDelay` declares `HasDirectFeedthrough => false`, so it creates no ordering
edge. Because it emits in the evaluate pass but captures its input in the
latch pass, it lags its producer by exactly one tick whether or not it sits
inside a loop — the resolver is free to place it before or after its producer
without changing that guarantee. One tick of lag at 10 ms is physically
irrelevant.

## Latched inputs

A component that knows one of its inputs is a *reflection* — the torque a
load pushes back up a shaft — can declare that input `latched: true`. A
latched input reads the value captured in the latch pass of the previous
tick and creates no ordering edge, so a chain of bidirectional mechanical
elements (motor ⇄ gearbox ⇄ drive pulley) resolves without a `UnitDelay`.
`UnitDelay` remains the wiring-time tool for loops between components that
did not anticipate them. Belts declare no direct feedthrough at all: their
outputs (load, item count) come from material that moves only in phase 3.

## Material flow

Mass travels on a second port graph, separate from signals. A `FlowOutlet`
feeds exactly one `FlowInlet` and an inlet has exactly one source — mass cannot
fan out or merge implicitly — and both ends must carry the same `PayloadKind`.
Flow ports never create signal-ordering edges: transport is phase 3, after
every component has evaluated, so a belt whose speed comes from a controller
that reads the belt's load is not an algebraic loop. Within phase 3,
`FlowGraph.Step` visits nodes most-downstream first and, for each, transfers
its outgoing links before calling its own `Advance`. So material deposited
into a node during a tick is not moved by that node until the next tick — one
tick per hand-off, deterministic and identical whether the node is a
`BulkBelt` or a `DiscreteBelt`.

A component that holds material implements `IFlowNode` (usually by deriving
from `FlowComponentBase`) and, per port, one side of the transport protocol:

- **Bulk** — `IBulkProducer.OfferMass` says how much the producer wants to push,
  `IBulkConsumer.AcceptMass` how much the consumer can take, and the engine
  moves the minimum with `Withdraw` then `Deposit`. Back pressure is not a
  feature of any node; it is what a full consumer's `AcceptMass` returns.
  `OfferMass` takes no `dt`, so a rate-based producer must capture the time
  step itself, in `Initialize` (`BulkBelt` does this); a belt that has not
  been initialised offers zero. `Simulation` always initialises every
  component before the first tick, but a unit test that drives a node by hand
  without a `Simulation` must call `Initialize` itself. `Withdraw` may hand
  back less than was asked for — the returned lot is what actually moves — but
  it must remove from the node exactly the mass it returns, or the
  conservation audit trips.
- **Discrete** — `IItemProducer.TryPeekItem` shows the head item,
  `IItemConsumer.CanAcceptItem` says whether it fits, and the engine moves whole
  items until one side says no. `WithdrawItem` must return exactly the item
  last shown, the same contract as `Withdraw` on the bulk side: what is
  removed must equal what is returned.

Bulk is a `BulkLot`: mass, one `MaterialType`, and `MaterialProperties`
(density, moisture, temperature) that blend by mass-weighted average when lots
merge. Discrete is an `ItemInstance` with an id from the simulation's
`ItemIdSequence` (so replays mint the same ids), a mass, properties, and a
state array sized by its material's schema.

`BulkBelt` is an array of cells. Each tick a fraction `v·dt/cellSize` of every
cell moves to its neighbour, resolved from the head backwards so a blocked
discharge builds load along the belt; validation refuses `cellSize <
maxSpeed·dt` (`MR006`). A cell never exceeds `maxLinearDensity·cellSize`, so
the inlet accepts only the room in the first cell. Because of that cap,
`PeakLinearDensity` can never reach or exceed `MaxLinearDensity`; an overload
detector watching it must trigger on `>=`, or better, on `Load` driving
torque and current rather than on a density that will never actually be
exceeded. Speed zero freezes the load profile exactly. `DiscreteBelt` carries
items at continuous positions with no diffusion; items queue behind a blocked
head at the minimum spacing.

A node may have more than one outlet; each still feeds exactly one inlet.
`RejectGate` (`reject-gate`) has `Out` and `RejectOut` and holds one item at a
time. It shows that item through `TryPeekItem` on exactly one outlet — the one
its `Reject` input chooses on that tick — so an item leaves by one outlet or
waits, and the choice is taken again on every tick it waits. The item stays for
`dwellSeconds`, counted from the tick after it arrives, and leaves on the tick
after that: deposited on tick N, it leaves on tick N + ⌈dwell / dt⌉ + 1 at the
earliest, the same one-tick hand-off a belt and a process unit have, and the
next item may enter on that same tick. The dwell is what lets an instrument
read the item and a control block decide before it leaves.

Size the dwell to cover that decision. From the tick an item arrives, its
instrument reads it on the next tick; an alarm raises on its first scan after
that, plus its on-delay rounded up to whole alarm scans; the block that writes
`Reject` writes on its first scan after the alarm publishes; and the write
lands one tick later. With the alarm scanning every *a* ticks and the writer
every *c* ticks, the decision is certain to land in time only when
⌈dwell / dt⌉ ≥ a + c + 1, plus the on-delay's ticks rounded up to a multiple
of *a*, plus any lag the instrument adds: 3 × dt when both scan every tick,
because the write then lands four ticks after the item arrives. A shorter dwell
lets the item leave before the decision, and the same latency decides when
`Reject` falls again for the item that follows.

Transforms (`IMaterialTransform`) run on resident material every tick, before
it moves and whatever the speed, with ambient conditions taken from the node's
signal inputs. A process unit runs them only while it processes, unless it is
built with `heatWhileHeld`: then they run on every tick it holds items —
filling, processing and discharging — so a batch that cannot leave keeps
heating, as it would in a real furnace. Bulk cells pass an empty state span in
this version; items pass their own.

Every tick the engine sums each node's `MassHeld`, `MassCreated` and
`MassDestroyed` and throws `MassConservationException` if
`created − destroyed − held` drifts beyond `SimulationOptions.ConservationTolerance`
(relative to the mass sourced). A node that injects mass reports it in
`MassCreated`; a node that removes it — a sink, a declared loss — reports it in
`MassDestroyed`. Anything else is a bug, and the audit finds it on the tick it
happens. Validation also rejects recirculation loops (`MR005`), inlets fed
from outside the plant (`MR007`) and flow ports whose owner lacks the
producer/consumer contract (`MR008`).

Two more rules for flow-node authors, both consequences of the phase
structure. **Material changes only in `Advance`.** A source creates mass, a
former cuts pieces, a process unit fills, holds and releases — all in phase
3, never in `Evaluate`. `Evaluate` only publishes outputs from the frozen
state. **Instruments read a node through `IMaterialObservable`.** Because
nothing moves in phase 2, a belt scale or a pyrometer may hold a reference
to the node it is mounted on and call `TryObserve` in its `Evaluate`,
whatever the evaluation order. It may call nothing else on the node.

## Faults

A component that can be broken implements `IFaultTarget`: it publishes
`FaultDescriptor`s (id, description, parameters with defaults) and accepts
`ApplyFault` / `ClearFault`. `Simulation.InjectFaultAt` resolves the target,
the fault and the arguments when the fault is *scheduled*, so a mistake fails
at the call site naming what exists, and delivers it through the event queue
in phase 1 of the due tick, logging `FAULT` / `FAULT_CLEARED`. Faults change
state and behaviour only; the graph is immutable, so injection cannot
perturb evaluation order.

Every instrument derives from `InstrumentBase` and gets calibration, noise,
drift, lag, freeze, fail-high and fail-low for free; its `Truth` telemetry
is what it should have read. Physical faults — bearing friction, belt slip,
a welded contactor, a blocked chute, a process unit that runs slow
(`slow-cycle`), a reject kicker that does not fire (`stuck`) — are declared per
component. A component switches on the fault id, so its faults are independent
and may be active together.

## Determinism rules

Rules a component author must follow, all of them easy to break by accident:

- Never use `System.Random`. Its algorithm is not contractually stable across
  .NET versions. Use the `DeterministicRandom` handed to you in `InitContext`.
- Never use `string.GetHashCode()` in anything affecting behaviour. It is
  randomised per process. Use `Hash64.OfString`.
- Never iterate a `Dictionary` or `HashSet` during a tick. Resolve lookups to
  arrays during `Initialize`.
- Never read wall-clock time. `TickContext.SimTime` is the only clock.
- Keep all state in the component. Static mutable state is shared across
  simulations and destroys reproducibility.

Each component's random stream is seeded from `hash(masterSeed, componentId)`,
not drawn from one shared generator. This is deliberate: it means adding a
component to a plant does not shift any other component's randomness, so a saved
scenario survives plant edits. `SeedStabilityTests` guards it.

## Telemetry versus instrumentation

Telemetry is the god view — true internal values, published by components, read
freely by tests. Instrumentation is what the plant can actually measure, and
arrives with the component library: sensors are ordinary components subject to
noise, drift, lag and failure. A test asserting true belt speed uses telemetry.
A controller must go through a sensor, and can be lied to.

## The I/O image

Tags are the plant's front door. A leaf component that has something a plant
would measure or command implements `ITagProvider` and returns `TagBinding`s
with names relative to itself — a belt scale declares `Value` with the unit and
range from its spec and its `Health` port as the quality source; a starter
declares `Command` and `Reset` as writable and `Contactor` and `Tripped` as
read-only. The builder prefixes each name with the component id. A composite's
`Expose(alias, port)` renames the tag to `CompositeId.Alias`, applied
inside-out so the outermost alias wins, which is how `CV001.Start` and
`CV001.TonnesPerHour` arise. `SimulationBuilder.Bind` adds a tag for a port
nothing declared, or replaces a declared one. Every port has at most one
binding; names are unique; the directory is sorted by name and printable with
`ToText()`.

Truth stays telemetry. The motor, gearbox, pulleys and belts declare no tags: a
SCADA that could read true motor current would not need the current sensor.

A writable tag drives an `InputPort` from outside. If the plant already wires
an output into that input, the declared tag degrades to read-only (the tag
observes the command instead of issuing it); an explicit `Bind` of a writable
tag on a driven input is validation error `MR010`.

`Simulation.IO` is the image. Reads (`Read`, `ReadDouble`, `Handle<T>` …) see
the snapshot published at the last tick's phase 4, from any thread, without a
lock: the array is written once, swapped with a volatile write and never
touched again. Writes queue lock-free and land at phase 1 of the next tick, in
enqueue order, before scheduled events; each is logged as `WRITE` from the tag
name, so the write timeline is in the event stream. Every tag value carries a
`TagQuality`: instruments report `Uncertain:OutOfRange` when the unclamped
reading leaves their range and `Bad:SensorFailure` under fail-high or fail-low.

`Simulation.WriteAt(fromStart, tag, value)` is the other way in: it resolves
and checks the tag when the write is *scheduled* — unknown tag, read-only tag,
kind mismatch, all at the call site — and applies it during the phase-1 event
drain, so the value lands on exactly the tick named rather than the one after.
Whichever way a write arrives, it is reported to the simulation's one
`IActionRecorder` with the tick it landed on, as fault injections and
clearances are.

Phase 5 wraps the published array, its dirty mask and the tick's event-log
records in an immutable `TickFrame` and hands it to the attached
`ITickFrameSink` in one non-blocking call. Nothing downstream of that call can
affect the run.

## The real-time boundary

`Millrace.Realtime` references only `Millrace.Io.Abstractions`. It cannot see the model,
which is what makes a protocol adapter a genuine bolt-on.

`RealtimeHub` is the frame sink. `Publish` is a single-producer enqueue into a
ring; when the ring is full the incoming frame is dropped and counted, and the
simulation thread never waits. `Pump` — called by `DispatcherThread` in
production, directly in tests — drains the ring into `LiveState` (current
value, quality and last-change tick per tag, plus recent events) and then
offers each frame to every `Subscription`. `Subscribe` captures the state
snapshot and registers the subscription under the pump lock, so no frame can
fall between them: deltas start at the tick after the snapshot.

A subscription diffs each frame against the values it last delivered, filtered
by tag prefix and deadband (absolute per tag, or a percent of the directory
range), and optionally decimated to one delta per interval of simulation time
with the frames in between merged. Under `Conflate` it keeps the latest value
per tag and never faults; under `Lossless` it queues every delta and faults at
capacity or on a ring gap rather than drop an event. Consumers pull with
`TryRead` or wait on `Available`; a slow consumer stalls nothing.

Inbound writes take their own channel: `CommandBus` validates a command against
the directory — unknown tag, read-only, wrong kind, outside the declared range —
forwards accepted ones to `ITagWriter`, and reports every command to an
optional `ICommandRecorder`, which is where a scenario recorder attaches.

## Modbus TCP

`Millrace.Modbus` is the first protocol adapter built on that boundary. It
references `Millrace.Io.Abstractions` and `Millrace.Realtime` only, and no package.
`RegisterMap.Build` turns a tag directory into a register map, in directory
order: a read-write Bool is a coil and a read-only one a discrete input; a
Double is a big-endian Float32 and an Int64 a saturating big-endian Int32,
each two holding registers when read-write and two input registers when
read-only. Access is the directory's published access, so a claimed tag is
read-only on the wire too.

`ModbusServer` answers function codes 1, 2, 3, 4, 5, 6, 15 and 16 for any
unit id and any number of connections. It is given a function that returns
the published image — `Simulation.IO.Snapshot` — and reads it once per
request, so every value in a response belongs to one tick; it is given a
`CommandBus` for writes, which therefore land at phase 1 of the next tick like
any other external write. A multi-value write is validated whole before any of
it is queued. `millrace serve` puts the two together: it ticks the simulation on
its own thread with `SimulationRunner` in real time (or `--speed` times it),
while the server answers on the thread pool. It listens on 127.0.0.1 unless
`--bind` names another address, because the protocol has no authentication
and the server accepts writes. `hmi/fuxa/` connects the FUXA
web SCADA to it.

## Catalogue, schema and loader

Three things are generated from one source, the descriptors, and therefore agree:

    ComponentDescriptor ──► CatalogueJson.Export   what exists, for a person or an agent
           │            ──► PlantSchema.Generate   what a plant file may say, for an editor or a validator
           └── Factory  ──► PlantLoader.Load       a SimulationBuilder, or diagnostics

A **descriptor** is hand-written beside the constructor it describes and carries
the factory that builds the component from parsed parameters. It cannot drift:
`CatalogueConformance` builds one instance of every type and compares the
descriptor with it, and a sweep fails the build if any concrete node, transform
or hold condition has no descriptor.

A **catalogue** is an immutable value composed from modules
(`new CatalogueBuilder().Add<ComponentsModule>()…`). There is no static registry
and no assembly scanning; the CLI's `--assembly` is the only place a module is
discovered rather than named.

The **loader** runs six stages — parse, structure, references, instantiate,
wire, build — and stops at the end of the first stage that reported an error,
having collected every error of that stage. It resolves ports against the live
instances, not against descriptors, so what it wires is what exists. The last
stage is `SimulationBuilder.Validate()`: a plant loaded from JSON passes exactly
the checks a plant built in code passes, and `MR001`–`MR011` mean the same in
both. The loader returns the builder unbuilt, so a caller can still set a frame
sink or decide not to build.

The **schema** checks structure; the loader checks meaning. That a reference
names a component, a material a material, an address a port — no schema can
know. The boundary is asserted by `SchemaAgreementTests`, which requires the
schema to reject every structural fixture and to *accept* every semantic one.

Determinism is unaffected: the loader adds components in file order, the
catalogue and every export are sorted, and the round-trip test holds a JSON plant
and its hand-built twin to byte-identical event logs.

A plant's **controllers** are read in the structure stage like components and
resolved in the build stage, after the plant alone has passed `Validate()`. The tag table is
`SimulationBuilder.PlantTags()` — the directory `Build()` would publish, R23
downgrades included — plus every controller's declared owned tags, so every tag
and value is checked (`MR113`–`MR115`) before any block is built; the blocks
are then added in file order and pass `Validate()`'s `MR013`–`MR016` like a
block attached in code. Block descriptors live beside component and object
descriptors in the catalogue; `ControlModule`, in `Millrace.Control.Catalogue`,
registers the six shipped ones.

## Scenarios and replay

A scenario is a JSON file: a plant to run, the three engine overrides, a
duration, and a timeline of writes, fault injections and clearances. `Millrace.Scenarios`
sees `Millrace.Core` and `Millrace.Configuration` and nothing else.

    ScenarioLoader.Parse   structural: shape, types, ranges — no plant, no file system
    ScenarioRunner.Run     load the plant, bind every action, then and only then tick
    ScenarioJson.Write     a Scenario back to deterministic JSON
    GoldenLog.Compare      where this run's log left the committed one
    ScenarioRecorder       a live run, as a scenario

The two passes are the point. Everything that can be known without a plant is
`MR200`–`MR204`; everything that needs one is `MR205` (the plant has its own
errors, which follow unchanged) and `MR206` (this action names something the
plant does not have). Both run to completion and collect every problem, and
`RunFor` is not called if there is a single one — **a bad scenario never
produces a partial log**.

The event log is the regression artifact, and `EventLog.ToText()` is its format
by contract. `millrace run --expect golden.log` exits 4, not 1, when the two differ:
"the configuration is broken" and "the behaviour changed" are different
questions, and a script should not have to guess which it got.

Replay works because a scheduled write lands on the tick it names.
`TagImage.Write` queues for phase 1 of the *next* tick, which is right for a
live command arriving from outside; a replayed write must land where the
original landed, so `Simulation.WriteAt` applies through `TagImage.ApplyNow`
during the event drain, logging the identical `WRITE` record. `IActionRecorder`
watches all three landing sites — the queued-write drain,
`ApplyNow` and `FaultEvent.Apply` — for external actions, so a recording
captures actions by where they took effect, not by where they came from. A
control block's write carries its origin: it is logged `by <block id>` and is
not recorded, because a replay re-runs the block. `Millrace.Realtime`'s
`ICommandRecorder` is a different thing and stays: it also sees commands the bus
*rejected*, which is an audit trail, not a replay.

See [scenarios](scenarios.md) for the file format and the golden workflow.

## The control layer

A control block is a PLC rung: `Millrace.Control` holds six of them — a timer, a
permissive, an interlock, an alarm, a sequencer and a coil — and sees
`Millrace.Io.Abstractions` and nothing else. A block is a pure `IScanBlock`: values
and two elapsed times in, values, writes and events out. It never sees a
`Simulation`, a directory, a binding, a clock or a log, which is why its unit
tests need none of them.

`SimulationBuilder.AddScanBlock` checks each block at `Build()` — `MR013` for
the period, `MR014` for the pins, `MR015` for the names, `MR016` for its
claims — and turns its declared outputs and commands into ordinary tags over
ordinary ports: an output is an `OutputPort<T>` behind a read-only binding, a
command an `InputPort<T>` behind a writable one. Nothing in `Millrace.Realtime` or
the scenario recorder had to learn what a block is; `TagImage` learned only a
write's origin, so the log attributes a block's write and the recorder skips
it. A block added with claims (`AddScanBlock(block, claims)`, `MR016`) is its
claimed tags' only writer: the binding stays writable, the directory publishes
the tag `ReadOnly` with `ClaimedBy` set, and `TagImage` refuses a write whose
origin is not the claimant — so every consumer that already honours `ReadOnly`
refuses it too. `Simulation` then schedules one self-rescheduling `ScanEvent`
per block, first due at tick 0, drained in phase 1 in schedule order.

The timing rule is one sentence: **a scan at tick N sees the image published at
the end of tick N−1, publishes its own outputs at the end of tick N, and its
writes land at phase 1 of tick N+1.** At a 100 ms period on a 10 ms step a block
reacts between 10 and 110 ms late — the asymmetry a real PLC has — and it is
what makes scan order among blocks due on the same tick irrelevant: they all
read the same previous publish. A plant with no blocks schedules nothing and
adds nothing to the directory, which is why the four scenario goldens of plan 5b
are byte-identical across this change.

Blocks are declared in a plant file's `controllers` section or attached in
code. `Millrace.Control.Catalogue` — which sees `Millrace.Core` and `Millrace.Control` —
registers them in the catalogue through `ControlModule`, so `Millrace.Control` itself
still sees the I/O contract alone.

See [control blocks](control-blocks.md) for each block's pins, parameters and
events.
