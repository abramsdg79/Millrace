# Architecture

## The tick

`Simulation.Tick()` runs five phases in a fixed order, and that order is the
determinism guarantee:

1. **Drain events** due at or before this tick, ordered by `(dueTick, sequence)`.
   The sequence number is monotonic, so events due on the same tick always fire
   in the order they were scheduled.
2. **Evaluate the signal graph**, in two passes over the resolved topological
   order: first every component evaluates, then every component latches. The
   latch pass is where a component with no direct feedthrough (a `UnitDelay`)
   captures this tick's input for the next tick, independent of where the
   resolver placed it relative to its producer.
3. **Advance flow** — one downstream-first sweep over the material graph: each
   node discharges through its outgoing links into consumers that have already
   advanced and made room, then advances its own contents. The conservation
   audit runs after the sweep.
4. **Publish the I/O image** — the snapshot external readers see. Not yet
   implemented.
5. **Emit the tick frame.** Not yet implemented; the event log is already
   appended during evaluation.

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

## Material flow

Mass travels on a second port graph, separate from signals. A `FlowOutlet`
feeds exactly one `FlowInlet` and an inlet has exactly one source — mass cannot
fan out or merge implicitly — and both ends must carry the same `PayloadKind`.
Flow ports never create signal-ordering edges: transport is phase 3, after
every component has evaluated, so a belt whose speed comes from a controller
that reads the belt's load is not an algebraic loop.

A component that holds material implements `IFlowNode` (usually by deriving
from `FlowComponentBase`) and, per port, one side of the transport protocol:

- **Bulk** — `IBulkProducer.OfferMass` says how much the producer wants to push,
  `IBulkConsumer.AcceptMass` how much the consumer can take, and the engine
  moves the minimum with `Withdraw` then `Deposit`. Back pressure is not a
  feature of any node; it is what a full consumer's `AcceptMass` returns.
- **Discrete** — `IItemProducer.TryPeekItem` shows the head item,
  `IItemConsumer.CanAcceptItem` says whether it fits, and the engine moves whole
  items until one side says no. `WithdrawItem` must return exactly the item
  last shown.

Bulk is a `BulkLot`: mass, one `MaterialType`, and `MaterialProperties`
(density, moisture, temperature) that blend by mass-weighted average when lots
merge. Discrete is an `ItemInstance` with an id from the simulation's
`ItemIdSequence` (so replays mint the same ids), a mass, properties, and a
state array sized by its material's schema.

`BulkBelt` is an array of cells. Each tick a fraction `v·dt/cellSize` of every
cell moves to its neighbour, resolved from the head backwards so a blocked
discharge builds load along the belt; validation refuses `cellSize <
maxSpeed·dt` (`DSE006`). A cell never exceeds `maxLinearDensity·cellSize`, so
the inlet accepts only the room in the first cell. Speed zero freezes the load
profile exactly. `DiscreteBelt` carries items at continuous positions with no
diffusion; items queue behind a blocked head at the minimum spacing.

Transforms (`IMaterialTransform`) run on resident material every tick, before
it moves and whatever the speed, with ambient conditions taken from the node's
signal inputs. Bulk cells pass an empty state span in this version; items pass
their own.

Every tick the engine sums each node's `MassHeld`, `MassCreated` and
`MassDestroyed` and throws `MassConservationException` if
`created − destroyed − held` drifts beyond `SimulationOptions.ConservationTolerance`
(relative to the mass sourced). A node that injects mass reports it in
`MassCreated`; a node that removes it — a sink, a declared loss — reports it in
`MassDestroyed`. Anything else is a bug, and the audit finds it on the tick it
happens. Validation also rejects recirculation loops (`DSE005`), inlets fed
from outside the plant (`DSE007`) and flow ports whose owner lacks the
producer/consumer contract (`DSE008`).

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
