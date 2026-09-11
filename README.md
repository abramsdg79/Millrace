# Dse — Deterministic Industrial Process Simulation Engine

A deterministic, composable simulation engine for real-world industrial
processes, written in .NET. Build virtual plants from reusable machines,
instrumentation and process components, then run, test, break and replay them
deterministically.

The engine simulates behaviour, not tags: belt speed, motor current and tonnes
per hour are derived from a physical model, so a change anywhere propagates
causally through the plant.

## Status

Under construction. This repository currently contains the simulation core —
deterministic clock, per-component random streams, typed signal ports,
composite components, topological resolution with algebraic-loop detection,
validation, telemetry, an ordered event log, a runner with real-time and scaled
execution — and the material layer: bulk and discrete payloads, typed flow
ports, offer/accept transport resolved downstream-first, cell-based bulk belts
and position-based discrete belts, residence transforms, and a per-tick mass
conservation audit.

The industrial component library, the I/O and real-time layers, declarative
configuration and the reference samples are planned. See
`docs/superpowers/specs/` for the design, `docs/superpowers/plans/` for the
implementation plans, and `docs/architecture.md` for how the engine works.

## Build and test

```bash
dotnet build
dotnet test
```

## Licence

MIT.
