# Deterministic Industrial Process Simulation Engine — Design

**Date:** 2026-09-02
**Status:** Approved for implementation planning

## 1. Purpose

A deterministic, composable simulation engine for real-world industrial
processes, written in .NET. Developers build virtual plants from reusable
machines, instrumentation and process components, then run, test, break and
replay them deterministically.

The engine simulates *behaviour*, not tags. Values such as belt speed, motor
current and tonnes per hour are derived from an underlying physical model, so a
change anywhere propagates causally through the plant. A stopped downstream
conveyor causes upstream accumulation, which raises belt loading, which raises
motor torque demand, which raises current, which trips an overload — with no
code anywhere that says "if downstream stops, trip upstream".

### Primary goal

The engine and component model are the product. Success for v1 is a clean,
composable, well-tested library that others — including AI agents — can extend
to new industrial domains.

### Design principles

- Deterministic by default.
- Composable objects; no domain special cases in the engine.
- Behaviour over fake data. If nothing real is being measured, no instrument
  ships for it.
- Process, instrumentation and I/O are separate layers.
- Everything is testable.
- Start small; build a component ecosystem.

## 2. Non-goals for v1

Not a SCADA system, an HMI, or a replacement for commercial process simulators.
No GUI, and no protocol adapters — no OPC UA, MQTT or Modbus. What v1 does ship
is the real-time distribution layer those adapters attach to (section 10), so
adding one later is additive rather than surgical. Excluded components are
listed in section 18.

## 3. Key decisions

| Decision | Choice | Rationale |
|---|---|---|
| Consumer of v1 | Open-source foundation | Abstractions and extensibility carry the weight, not a specific integration. |
| Belt material fidelity | Spatially discretised | A surge must physically travel and reach the scale late; stopping a belt must freeze the load profile in place. |
| Control logic location | Layered — plant-only engine, optional control package over the same I/O contract | Keeps the plant/controller boundary honest while still shipping usable control blocks. |
| Determinism strictness | Same build, same machine | Covers regression testing and failure reproduction. Bit-identical cross-platform results would constrain the entire numeric layer forever for benefit not currently needed. |
| Component wiring | Two-layer typed port graph (signal + material) | Signals fan out freely; mass cannot. Separating them lets the framework *guarantee* conservation instead of asking every contributor to remember it. |
| Payload generality | Flow layer carries bulk *or* discrete items | Required for bakery- and production-line-shaped processes. The offer/accept protocol is already payload-agnostic. |
| External data exposure | Real-time event engine and live state engine; protocols bolt on downstream | Adapters reference the distribution layer and never the model, which makes "bolt on a protocol" structurally true rather than aspirational. |
| Tag quality | Every tag value carries a quality code | Sensor faults are meaningless to a SCADA without one, and quality handling is a large part of why anyone points a SCADA at a simulator. |
| Serialization | JSON via `System.Text.Json` only | Zero external runtime dependencies. YAML deferred. |
| Target framework | `net10.0` | Current SDK on the development machine. |
| Test framework | xUnit, plain assertions | Minimal dependencies; avoids libraries with shifting licences. |
| Licence | MIT | Maximum reuse for a foundation library. |

Root namespace `Dse` is provisional and may be renamed before first release.

## 4. Solution layout

Eight shipping projects, two samples, and a test project per shipping project.

| Project | Contains | Depends on |
|---|---|---|
| `Dse.Io.Abstractions` | The I/O contract only: tag read/write interface, tag metadata, `TagValue`, `Quality`, `TickFrame`. Deliberately tiny so an application under test, or a protocol adapter, can reference it without the engine. | — |
| `Dse.Core` | Clock, tick loop, event queue, RNG, component and port model, graph resolver, flow graph and transport, transforms, fault channel, telemetry, event log, component catalogue. No industrial concepts. | `Io.Abstractions` |
| `Dse.Components` | The industrial object library: mechanical, instrumentation, safety, process units, material types. | `Core` |
| `Dse.Control` | Optional control blocks written against the I/O contract: interlock, permissive, sequencer, timer, alarm. | `Io.Abstractions` |
| `Dse.Scenarios` | Scenario definition, fault schedules, recording, replay, golden event logs. | `Core` |
| `Dse.Realtime` | Real-time event engine, live state engine, subscription API, command bus. The attachment point for every downstream consumer. | `Io.Abstractions` |
| `Dse.Configuration` | Declarative plant definition (JSON), loader, and JSON Schema generated from the catalogue. | `Core`, `Components` |
| `Dse.Cli` | Thin CLI: `catalog export`, `validate`, `run`. | all |

Samples: `samples/Dse.Samples.MineConveyors`, `samples/Dse.Samples.WheelLine`.

`Dse.Control` deliberately does *not* reference `Dse.Components`. A control
block that reaches into a component has broken the layering.

`Dse.Realtime` deliberately does *not* reference `Dse.Core`. It consumes frames
and produces commands, and has no access to the model. This is what makes a
future protocol adapter a genuine bolt-on: it references `Dse.Realtime` only.

## 5. Simulation core

### 5.1 Time

The clock holds `long TickCount` and a fixed `TimeSpan` step (default 10 ms).
Simulation time is **derived by multiplication**, never by accumulating a
double — accumulated `+= dt` drifts, and drift breaks replay.

Components receive a `TickContext` exposing `dt` in seconds, the current tick
index, and the current simulation timestamp.

### 5.2 Tick phases

Evaluation order *is* the determinism guarantee, so the phases are fixed and
documented:

1. **Drain events** due at or before this tick, ordered by `(dueTick, sequence)`.
   The monotonic sequence number breaks ties so simultaneous events can never
   reorder between runs. This is where queued external I/O writes, injected
   faults, and scheduled control scans are applied.
2. **Evaluate the signal graph** in resolved topological order.
3. **Advance flow** (material transport and residence transforms).
4. **Publish the I/O image** — a consistent snapshot for external readers.
5. **Emit the tick frame** — telemetry, the tag image with its dirty mask, and
   the tick's discrete events, packaged as one immutable `TickFrame` and handed
   to the real-time layer (section 10) by a single non-blocking enqueue.

### 5.3 Randomness

One master seed. Each component receives its own stream seeded from
`hash(masterSeed, componentId)`.

This matters more than it looks: with a single shared stream, adding a component
anywhere shifts the random sequence of every other component, so any saved
scenario breaks the moment the plant is edited. Per-component seeding keeps
scenarios stable across plant edits, which is what makes replay useful in
practice rather than in theory.

### 5.4 Lifecycle

`Build → Validate → Initialize → Tick* → Dispose`.

Validation resolves evaluation order and rejects:

- unbroken algebraic loops (naming the components in the cycle);
- unconnected required ports;
- inputs driven by more than one output;
- CFL violations on bulk belts (`cellSize < maxBeltSpeed · dt`);
- flow links joining incompatible payload kinds.

**The graph is immutable after validation.** Faults change component behaviour
through the fault channel; they never add or rewire components. This makes
resolution a one-time cost and removes a class of nondeterminism.

### 5.5 Pacing

`Simulation.Tick()` is pure and unpaced. A separate runner provides real-time,
scaled (2x, 10x, 100x), as-fast-as-possible, single-step and paused execution.
Execution mode therefore cannot affect results.

## 6. Component and port model

### 6.1 Components

`ISimComponent` exposes an `Id`, its ports, `Initialize(ctx)` and
`Evaluate(in TickContext ctx)`. `Evaluate` reads its inputs, updates its own
state, and writes its outputs. **A component never touches a sibling.** That
restriction is what makes topological ordering meaningful and components
independently testable.

### 6.2 Ports

`InputPort<T>` and `OutputPort<T>`, with `T` constrained to unmanaged structs.
v1 wires `double` and `bool` — analog and discrete, the vocabulary the domain
already uses. The generic shape leaves room for richer types later without
redesign.

- An output may fan out to many inputs.
- An input has exactly one source; two sources is an undefined value and a
  validation error.
- An unconnected input holds a declared default, so partial plants still run.

### 6.3 Composites are flattened

A `Conveyor` is a component that *contains* a motor, gearbox, pulleys, belt and
instruments, and exposes ports that alias its children's ports. At validation
the whole tree collapses into one flat, ordered array of leaf components.

Two consequences: a conveyor really is just a composition — there is no
hard-coded conveyor anywhere in the engine — and evaluation walks a flat array
in fixed order, with no dictionary iteration in the hot path.

### 6.4 Feedback loops are explicit

Belt load raises motor torque demand, which lowers motor speed, which changes
belt speed, which changes belt load. This is a genuine algebraic loop, not a
modelling mistake. Validation detects the cycle, names the components in it, and
refuses to run until a `UnitDelay` is inserted to break it. One tick of lag at
10 ms is physically irrelevant and makes the solve order unambiguous. This is a
documented modelling pattern.

### 6.5 Telemetry versus instrumentation

Two distinct things, deliberately not merged:

- **Telemetry** is the god view. Any component may publish named internal
  values; tests read them freely.
- **Instrumentation** is what the plant can actually *measure*. Sensors are
  ordinary components with signal ports, subject to noise, drift, lag and
  failure.

A test asserting true belt speed uses telemetry. A controller wanting belt speed
must go through a speed sensor, and can be lied to. This separation is what
makes sensor-failure faults meaningful rather than cosmetic.

## 7. Material and flow model

### 7.1 Payloads

The flow graph is nodes (belt cells, chutes, transfer points, process units,
sources, sinks) joined by links. Node contents are one of:

- **Bulk** — mass in kilograms plus blended intensive properties.
- **Discrete** — a set of `ItemInstance` values.

A node declares which kind it handles. Validation rejects incompatible links.

Crossing between kinds is an explicit component, never an implicit cast:

- **Former** — bulk to discrete (a dough divider).
- **Disintegrator** — discrete to bulk (a shredder, a melt). Deferred past v1;
  neither reference sample needs one, and the former proves the crossing works.

Real plants contain exactly these machines, so making them explicit is more
honest than a silent conversion.

### 7.2 Material properties

```
MaterialProperties { Density, Moisture, Temperature }   // intensive
```

A fixed struct, not a property bag: a dictionary per cell would put allocation
and non-deterministic iteration into the hottest loop in the engine. Adding a
field later is a source change that costs nothing at runtime, and new fields
blend by the same rule.

When streams meet, intensive properties blend by mass-weighted average.

### 7.3 Items

```
ItemInstance { Id, MaterialType, Mass, MaterialProperties, double[] State }
```

`Id` comes from a per-simulation monotonic counter, so it is deterministic. The
`State` array is sized by a schema declared on the `MaterialType`: an ordered
list of named doubles such as `BakeTimeAbove200C` or `CoreTemperature`. An
ordered array rather than a dictionary keeps iteration deterministic and avoids
per-item allocation. A material type with an empty schema costs nothing, which
is why bulk cells may carry the same array when a process needs it.

### 7.4 Transport

The transport phase resolves **downstream first**, in reverse topological order.
Each node offers what it wants to push and declares what it can accept; flow is
the minimum of the two.

Back-pressure is not a feature written into any conveyor — it is what falls out
of that minimum when a downstream node is full.

For discrete payloads the offer and accept are denominated in whole items; a
partial item cannot move.

### 7.5 Belts

**Bulk mode:** the belt is an array of cells of configured size. Each tick a
fraction `v·dt / cellSize` of each cell's mass moves to its neighbour
(partial-transfer Eulerian advection). Validation enforces
`cellSize ≥ maxBeltSpeed · dt` and refuses to run otherwise.

The trade, stated plainly: this smears a sharp surge slightly as it travels —
numerical diffusion proportional to the timestep. What it gets right is what
matters: a surge takes real time to reach the scale, stopping the belt freezes
the load profile exactly where it is, and restarting resumes it. Diffusion is
tunable by cell size. Exactness would cost a Lagrangian scheme and its
variable-length bookkeeping, which is not worth it for bulk.

**Discrete mode:** items carry a continuous position advanced by `v·dt`. No
cells, no diffusion, exact transport. Discrete is the easier case.

A belt is configured for one payload kind; validation enforces it.

### 7.6 Belt loading and the scale

Cell mass over cell length gives linear density in kg/m. The belt scale reads
linear density at its position multiplied by belt speed to give t/h, through its
own integration window and noise model.

Belt width and material angle of repose give a maximum linear density.
Exceeding it is what *defines* overload and spillage, rather than being asserted
separately.

### 7.7 Conservation

Transport is expressed only as transfers between nodes, so the engine asserts
each tick that `sourced − sunk − inSystem` is zero within epsilon. Declared
process losses are routed to an explicit loss sink so the invariant survives.
The check runs in debug builds and in tests; a contributor who breaks
conservation finds out immediately.

### 7.8 The causal chain this buys

Conveyor 3 stops → its cells stop advancing → the transfer chute from CV002
cannot discharge → the chute fills → CV002's last cell cannot discharge → load
builds along CV002 → linear density rises → motor torque demand rises → motor
current rises → the overload trips CV002.

Every arrow is a consequence of the transport rule. None of it is
conveyor-specific code.

## 8. Process units and transforms

### 8.1 Residence transforms

A node may hold transforms applied each tick to whatever material is resident:

```
IMaterialTransform.Apply(ref MaterialProperties props, Span<double> state, double dt, in TransformContext ctx)
```

`TransformContext` exposes ambient conditions taken from the node's signal
inputs. The `state` span is the accumulated-state array described in section
7.3, empty when the material type declares no state schema. One interface
therefore serves bulk cells and discrete items alike.

Three ship in v1:

- `ThermalTransfer` — lumped capacitance toward ambient with a time constant.
- `MoistureLoss` — rate as a function of temperature.
- `ResidenceAccumulator` — time spent above a threshold.

This is the piece that turns a belt into a band oven: apply a thermal transform
per cell with zone temperature varying along the length, and a bake profile
*emerges* rather than being scripted.

### 8.2 ProcessUnit

A state machine — `Idle → Filling → Processing → Discharging` — configured by:

- an input recipe (quantities by material type);
- a hold condition: a fixed cycle time, *or* a predicate such as core
  temperature ≥ 1150 °C;
- transforms applied during the hold;
- a discharge transformation that may change material type.

Mixer, prover, furnace and forging press are configurations of `ProcessUnit`,
not separate classes.

Type changes declare their mass balance. Yield loss is routed to a declared loss
sink, so section 7.7's conservation invariant remains intact and still catches
real bugs.

### 8.3 Motor overload is a model, not a flag

Torque demand from belt load gives current; current drives an I²t thermal state;
an overload relay trips on a curve.

"Inject motor overload" is therefore not a special code path — it is a bias on
the thermal state or the load, and the trip that follows is the same trip that
material accumulation would cause unaided. This component is the project's
thesis in miniature and is worth getting right in v1.

## 9. Instrumentation and I/O

### 9.1 Tag binding

An `IoBinding` maps a tag name to a port. Composites contribute their id as a
prefix, so a belt scale on `CV001` offers `CV001.Scale.TonnesPerHour`.

No reflection and no attribute magic — the set of tags is a list that can be
printed, and it carries metadata: direction, engineering unit, range,
description. That directory is what makes HMI generation, tooling and generated
documentation possible later.

### 9.2 Two access styles

The string API is the front door:

```csharp
simulation.IO.ReadBool("CV001.PullKey01");
simulation.IO.ReadFloat("CV001.Scale.TonnesPerHour");
simulation.IO.WriteBool("CV001.Start", true);
```

Underneath, a tag resolves once to a typed `IoHandle<double>`. Anything reading
every scan uses the handle and never pays for a dictionary lookup. Same data, no
duplicated semantics.

### 9.3 Snapshot in, queued out

Reads see the consistent snapshot published at tick phase 4 — never a
half-updated plant. Writes from outside are queued and applied at phase 1 of the
*next* tick. Nothing mutates mid-evaluation.

**Thread-safety contract**, stated as a guarantee so that adapter authors never
reach for a lock: the published snapshot is immutable and double-buffered, and
the inbound write queue is concurrent and lock-free for producers. The
simulation thread is the only writer of the model, and it never blocks on a
consumer.

**Stated limitation:** this makes the simulation deterministic with respect to
*when writes land*, not with respect to wall-clock. A live external application
poking tags in real-time mode is not reproducible by itself. Reproducibility
comes from recording the write timeline into a scenario and replaying it against
tick numbers.

### 9.4 Safety bypasses the controller

E-stops and pull-keys are physical components producing discrete signals, and a
`SafetyRelay` in `Dse.Components` de-energises the motor contactor directly. A
pull-key therefore stops the belt with no controller connected at all — as in a
real plant, where the safety circuit is hardwired around the PLC. Routing safety
through the control layer would be both wrong and a weaker demonstration.

### 9.5 Value quality

Every tag value carries a `Quality` code alongside its value and simulation
timestamp — `Good`, `Uncertain`, `Bad`, with a sub-status.

This is not decoration. Every industrial protocol carries quality, and sensor
faults map onto it directly: a disconnected transmitter reads `Bad`, a drifting
one failing a plausibility check reads `Uncertain`. Without quality in the I/O
image, the simulator cannot exercise a SCADA's quality or alarm handling at all,
which is a large fraction of the reason to point a SCADA at a simulator.

## 10. Real-time distribution layer

### 10.1 The determinism boundary

The `TickFrame` emitted at phase 5 is the only thing that crosses out of the
engine:

```
TickFrame { Tick, SimTime, TagValues[], DirtyMask, DiscreteEvents[] }
```

Everything downstream is observation and distribution. It cannot affect the
simulation, and the plant runs identically whether nothing or fifty consumers
are attached. The simulation thread's entire obligation is one non-blocking
enqueue into a ring buffer; a dispatcher thread performs fan-out.

### 10.2 Real-time event engine

Ordered distribution. Owns the ring buffer, the dispatcher, per-subscriber
filtering and deadbands, and backpressure policy.

Deadbands live here rather than in `Dse.Core` because they are
subscriber-specific: an HMI wants half a percent on a belt speed, a historian
wants every sample.

### 10.3 Live state engine

Current truth. Applies the frame stream to maintain, per tag, the current value,
quality, simulation timestamp and last-change tick, plus active alarm state. It
answers "what is true right now" for late joiners: a client connecting at hour
six receives a full snapshot, then deltas.

### 10.4 Both engines are subscribable

The event engine fans out to consumers *and* to the state engine, rather than
placing all consumers strictly downstream of state:

```
Simulator ─→ Event engine ─┬→ Live state engine ─→ snapshot, API, HMI paint
                            └→ stream consumers ──→ rules, historian, event log
```

The reason is transients. An alarm that raises and self-clears within one
publish interval is correctly invisible in state — but it must never be
invisible in the stream. Reducing everything through state before distribution
silently swallows exactly the short-lived events that matter most in fault
testing.

### 10.5 Backpressure

A declared policy per subscriber, because there is no single right answer:

- `Conflate` — bounded buffer, keeping only the latest value per tag. Correct
  for analog values feeding an HMI, where a stale intermediate reading has no
  value.
- `Lossless` — bounded buffer, disconnecting the subscriber on overflow.
  Mandatory for discrete events and alarms, where silently dropping a trip is
  worse than dropping the connection.

Under either policy a slow consumer never stalls the simulation. This is
non-negotiable: the moment a consumer can block a tick, both determinism and the
timing model are gone.

### 10.6 Commands flow on their own channel

The event stream is strictly one-way out. Inbound writes go to a command bus
that validates against the tag directory, enqueues into the simulation's write
queue for phase 1 of the next tick, and records into the active scenario by
default. Treating commands as the reverse of the event stream is how these
architectures become tangled; separate channels keep both simple.

### 10.7 Subscription has a no-gap guarantee

`Subscribe` atomically captures the state snapshot at tick N and delivers deltas
from N+1 onward. Without that atomicity a subscriber can miss a change landing
between snapshot and subscription — a race that surfaces rarely and is miserable
to diagnose.

### 10.8 Time and rate

Frames carry simulation time, never wall-clock, so a run at 10x produces
coherent history. At a 10 ms tick in real time that is 100 frames per second;
running as fast as possible it is far more, so a subscriber may declare a
decimation interval and receive one frame per simulated 100 ms regardless of
engine speed.

### 10.9 The attachment point for protocols

A protocol adapter subscribes to these engines and pushes to the command bus.
It never touches the model, so no adapter can perturb the simulation.

The tag directory already carries direction, engineering unit, range and
description, which maps closely onto OPC UA's `DisplayName`, `EngineeringUnits`
and `EURange`. The catalogue work of section 14 pays for itself here.

Suggested adapter order when they are built: OPC UA first, as the protocol
modern SCADA systems speak natively and the only one carrying units, ranges and
quality without invention; then MQTT with Sparkplug B, whose birth-and-data
split is precisely the state and stream division above; then Modbus TCP. Modbus
is where the deferred raw-count to engineering-unit scaling stops being
optional, since 16-bit registers force it.

Each adapter ships as its own package. Beyond keeping dependencies out of the
core, this contains licensing: the mainstream OPC UA .NET stack carries terms
that need checking against an MIT core, and a separate package prevents that
question from reaching the engine.

## 11. Control layer

`Dse.Control` blocks are written against `ISimulationIo` and know nothing about
components: interlock, permissive, sequencer, timer, alarm.

A controller declares a **scan period** — say 100 ms against a 10 ms simulation
step — and the engine schedules its scans through the event queue. The
controller therefore sees the plant the way a PLC does: sampled, slightly stale,
at its own rate. A surprising number of real control bugs live in that
asymmetry, and modelling it is nearly free.

Alarm *evaluation* lives here, since it is logic over signals. The resulting
alarm events go into the same ordered event log as everything else in
`Dse.Core`.

## 12. Faults

A component implements `IFaultTarget` and publishes the fault ids it supports
plus their parameter schema, so tooling can ask "what can break here?" and get a
real answer.

Faults are delivered through the event queue at a specific tick and change
component **state and behaviour only**. The graph stays immutable, so fault
injection cannot perturb evaluation order.

**Sensor faults come from a shared base.** Every instrument derives from
`InstrumentBase`, which provides calibration, noise, drift, lag, freeze, and
fail-high/fail-low. A new sensor arrives with the whole fault vocabulary already
working.

**Physical faults are declared per component** — bearing friction, belt slip,
contactor failure — because they are genuinely specific.

## 13. Scenarios and replay

A scenario contains:

- plant configuration reference;
- master seed;
- simulation start time, duration, timestep;
- an ordered timeline of actions keyed to simulation time: fault injections,
  control writes, setpoint changes.

Serialized as JSON in `Dse.Scenarios`. A recorder captures external writes and
injections from a live run into the same format, so "reproduce the failure I
just saw" is a save rather than a reconstruction.

**The event log is the regression artifact.** The ordered sequence of discrete
events — started, reached speed, overload, tripped — is written out and diffed.
It is stable, human-readable, and exactly the artifact implied by a reproducible
incident timeline. Analog telemetry samples to CSV alongside it for when the
curve is needed.

## 14. Discoverability

An AI agent is a first-class consumer of this framework, and for a domain like a
bakery it will be *writing* components, not only wiring existing ones. That
imposes requirements beyond capability.

- **Component catalogue.** Every component type publishes a descriptor:
  category, ports with units and ranges, parameters with defaults and
  descriptions, supported faults, telemetry keys, flow node kinds. Exports as
  JSON.
- **Declarative plant configuration** in JSON, with its JSON Schema generated
  from the catalogue, so a generated plant can be validated mechanically.
- **Validation errors that name the fix**, not just the symptom.
- **A written recipe** for authoring a new component: which interfaces to
  implement, where state lives, how to declare ports, faults and descriptors,
  and how to avoid breaking determinism.
- **`Dse.Cli`**: `dse catalog export`, `dse validate <plant>`, `dse run <scenario>`.

The framework's job is to make the plumbing free so that domain physics is the
only real work left.

## 15. Reference samples

Two samples, deliberately in different domains. A framework validated against a
single domain reliably turns out to fit only that domain.

### 15.1 Mine conveyors — bulk

Three interconnected conveyors: ore source → CV001 → transfer → CV002 →
transfer → CV003 → sink. Each conveyor composes motor, gearbox, drive and tail
pulley, belt, speed sensor, belt scale, current sensor, zero-speed switch,
pull-keys, e-stops, safety relay and starter. A demo controller in `Dse.Control`
provides sequenced start, interlocks and permissives.

Demonstrates: plant start, sequenced conveyor start, material flow, speed
change, belt scale measurement, pull-key activation, emergency stop,
upstream/downstream interlocks, motor overload, material accumulation, fault
injection, deterministic replay.

### 15.2 Wheel line — discrete

Billet source → furnace → conveyor → press → sink. Roughly five components
beyond the conveyor sample.

Exercises what the conveyor sample cannot: discrete items, per-item state, a
pyrometer reading item temperature, a transform cooling blanks in transit, a
type change at the press (blank → wheel), and cycle-time-limited throughput.

Its own causal chain, in a different domain: the press runs slow → blanks queue
on the conveyor → the furnace cannot discharge → billets over-soak → a
temperature interlock rejects them. None of it is conveyor-specific code.

## 16. Testing strategy

- **Per-component unit tests** — cheap, because components cannot touch
  siblings.
- **Mass-conservation property test** over randomly generated plants.
- **Determinism test** — the same scenario run twice, event logs compared
  exactly.
- **Seed-stability test** — add a component to a plant, assert the other
  components' random streams are unchanged. This guards the section 5.3
  decision, which is easy to regress and expensive to discover late.
- **Validation tests** — unbroken algebraic loops, CFL violations, unconnected
  required ports, double-driven inputs, incompatible flow links.
- **Catalogue and schema tests** — every shipped component has a descriptor;
  the generated schema accepts both samples' plant files.
- **Integration tests** — both samples asserting their causal chains end to end,
  with the same outcome on replay.
- **Golden-file scenario tests** — committed scenario plus expected event log.
- **Real-time layer tests** — a deliberately stalled subscriber affects neither
  tick timing nor results; `Conflate` drops only intermediate analog values;
  `Lossless` disconnects rather than dropping an event; subscribing under load
  delivers a snapshot and deltas with no gap and no duplicate.

## 17. v1 scope

**Core** — clock, tick loop, event queue, per-component RNG, port and component
model, composites and flattening, graph resolver and validation, flow graph and
transport, transforms, fault channel, telemetry, event log, catalogue.

**Components** — bulk material source and sink, discrete item source, motor with
thermal model, gearbox, drive and tail pulley, belt (bulk and discrete modes),
transfer chute, former, `ProcessUnit`, the three transforms, speed sensor, belt
scale, current sensor, temperature sensor/pyrometer, part counter, zero-speed
switch, e-stop, pull-key, safety relay, motor starter.

**Io** — bindings, image, string and handle APIs, tag directory, per-tag
quality, dirty mask, tick frames.

**Realtime** — event engine, live state engine, subscription API with deadbands
and decimation, backpressure policies, command bus. In-process only, with no
external dependencies.

**Control** — interlock, permissive, sequencer, timer, alarm.

**Scenarios** — definition, JSON, record, replay, golden logs.

**Configuration** — declarative JSON plants, loader, generated JSON Schema.

**Cli** — `catalog export`, `validate`, `run`.

**Samples** — mine conveyors and wheel line.

## 18. Deferred

Pumps, valves, tanks, pipes, fans, crushers, feeders, hoppers, splitters,
mergers, disintegrators; pressure, level, flow and vibration sensors; guard
switches; overspeed detectors; PID control; raw-count to engineering-unit
scaling; YAML configuration; mid-run state snapshot and restore; any GUI.

Deferred consumers of the real-time layer, every one of them additive because
section 10 exists: `Dse.Realtime.Http` (WebSocket streaming and REST), OPC UA,
MQTT/Sparkplug B and Modbus TCP adapters, an external rules engine, and a
historian. `Dse.Control` already covers logic running inside the plant; an
external rules engine is a different thing, and needs only the stream
subscription API.

**Belt drift switches and belt tracking are excluded, with reasoning.** Belt
tracking is not modelled, so a drift switch would have nothing real to measure —
it could only be a tag that flips when told to. Shipping it would be precisely
the fake-data behaviour this project exists to avoid. It returns when belt
tracking is modelled, and not before.

## 19. Repository standards

The library is the product, so repository hygiene is part of the deliverable:

- Nullable reference types enabled; warnings as errors; analyzers on.
- Deterministic builds.
- MIT licence.
- CI running the full suite, including golden scenarios, on every push.
- An architecture document explaining tick phases, the port model and the
  determinism rules. A contributor who does not understand evaluation order will
  write a component that breaks determinism.
