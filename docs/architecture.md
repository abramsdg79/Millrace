# Architecture

## The tick

`Simulation.Tick()` runs five phases in a fixed order, and that order is the
determinism guarantee:

1. **Drain events** due at or before this tick, ordered by `(dueTick, sequence)`.
   The sequence number is monotonic, so events due on the same tick always fire
   in the order they were scheduled.
2. **Evaluate the signal graph** in resolved topological order.
3. **Advance flow** — material transport. Not yet implemented.
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
edge. One tick of lag at 10 ms is physically irrelevant.

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
