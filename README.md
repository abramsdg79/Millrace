# Dse — Deterministic Industrial Process Simulation Engine

A deterministic, composable simulation engine for real-world industrial
processes, written in .NET. Build virtual plants from reusable machines,
instrumentation and process components, then run, test, break and replay them
deterministically.

The engine simulates behaviour, not tags: belt speed, motor current and tonnes
per hour are derived from a physical model, so a change anywhere propagates
causally through the plant.

## Status

Under construction. This repository contains the simulation core
(deterministic clock, per-component random streams, typed signal ports with
latched inputs, composites, topological resolution with algebraic-loop
detection, validation, telemetry, an ordered event log, a runner), the
material layer (bulk and discrete payloads, typed flow ports, offer/accept
transport, cell-based and position-based belts, residence transforms, a
per-tick mass conservation audit), the fault channel, and the first
component library: sources, sinks, a transfer chute, a former, bulk and
item process units, three transforms, an instrument base with the full
sensor-fault vocabulary, seven instruments, a motor with an I²t thermal model,
a drivetrain, a safety circuit, a starter, and a `Conveyor` composite that
trips its own overload when the belt downstream of it blocks.

The I/O and real-time layers, declarative configuration and the reference
samples are planned.

See `docs/superpowers/specs/` for the design, `docs/superpowers/plans/` for the
implementation plans, and `docs/architecture.md` for how the engine works.

## Build and test

```bash
dotnet build
dotnet test
```

## Licence

MIT.
