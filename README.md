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
trips its own overload when the belt downstream of it blocks — plus the I/O
and real-time layers. A component catalogue, declarative JSON plants with a
generated JSON Schema, and a `dse` command line sit on top.

It also contains the I/O layer (a declared, printable tag directory with
units, ranges and per-tag quality; a lock-free double-buffered image any thread
may read; queued writes that land at phase 1 of the next tick; one immutable
`TickFrame` per tick) and the in-process real-time layer (`Dse.Realtime`: a
ring-buffered hub, a live state engine for late joiners, subscriptions with
prefix filters, deadbands, decimation and declared backpressure, and a
validated command bus), which references the I/O contract only.

Scenarios are files too: a plant, the engine overrides, a duration and a
timeline of writes and fault injections, replayed by `dse run` against a
committed golden event log, and recordable from a live run.

On top of that sits the control layer: a scan-block contract in
`Dse.Io.Abstractions`, a host in `Dse.Core` that scans each block at its own
period through the event queue and publishes its outputs as ordinary tags, and
`Dse.Control` — a timer, a permissive, an interlock, an alarm and a sequencer,
which reference the I/O contract alone. Blocks are declared in a plant file's
`controllers` section — `Dse.Control.Catalogue` registers them — or attached in
code.

The first reference sample, `samples/mine-conveyors/`, is three conveyors, a
feeder and a stockpile with a sequenced start and stop, cascade interlocks,
permissives and alarms, and eight scenarios — a normal start and stop, a
pull-key, an e-stop, an overload, a blocked chute, a failed zero-speed switch, a
welded contactor and a starved feed — each with its golden log. It is data only: no C#. The second
sample, a wheel line of discrete items, is planned (plan 6b).

See `docs/superpowers/specs/` for the design, `docs/superpowers/plans/` for the
implementation plans, `docs/architecture.md` for how the engine works, and
`docs/control-blocks.md` for the control layer.

## Command line

```bash
dotnet run --project src/Dse.Cli -- catalog export            # every component, block, transform, transition, hold and material, as JSON
dotnet run --project src/Dse.Cli -- schema export --out dse-plant.schema.json
dotnet run --project src/Dse.Cli -- validate plant.json       # every error, each with its fix; exit 1 if any
dotnet run --project src/Dse.Cli -- tags plant.json           # the tag directory a SCADA would see
dotnet run --project src/Dse.Cli -- run scenario.json --expect golden.log  # replay a scenario; exit 4 if the log changed
dotnet run --project src/Dse.Cli -- run samples/mine-conveyors/scenarios/pull-key.json  # the sample: a pull-key stops the line
```

Add `--assembly path/to/YourModule.dll` to any command to include your own
components; add `--format json` to `validate` and `tags` for machine-readable
output. A plant file looks like this:

```json
{
  "defaults": { "seed": 1, "timeStepMs": 10 },
  "materials": [ { "name": "ore", "kind": "bulk", "properties": { "density": 2000, "moisture": 0.03, "temperature": 15 } } ],
  "components": [
    { "id": "FEED",  "type": "bulk-source", "parameters": { "material": "ore", "rateKgPerS": 20 } },
    { "id": "CV001", "type": "conveyor",    "parameters": { "lengthM": 10, "cellSizeM": 0.5, "beltWidthM": 0.8,
        "angleOfReposeDeg": 20, "materialDensityKgM3": 2000, "emptyBeltMassKg": 250, "frictionCoefficient": 0.04,
        "pulleyDiameterM": 0.5, "gearRatio": 20, "motor": { "ratedPowerW": 750, "ratedSpeedRadPerS": 150, "ratedCurrentA": 2 } } },
    { "id": "PILE",  "type": "bulk-sink" }
  ],
  "flows": [ { "from": "FEED.Out", "to": "CV001.In" }, { "from": "CV001.Out", "to": "PILE.In" } ]
}
```

A scenario over that plant looks like this:

```json
{
  "plant": "plant.json",
  "seed": 42,
  "duration": 120,
  "timeline": [
    { "at": 1,  "write": "CV001.SafetyReset", "value": true },
    { "at": 2,  "write": "CV001.SafetyReset", "value": false },
    { "at": 5,  "write": "CV001.Start", "value": true },
    { "at": 30, "fault": "CV001.Motor", "id": "thermal-bias", "args": { "amount": 0.8 } }
  ]
}
```

A plant adds control blocks under `controllers`; a scenario may write their
commands (`SEQ01.Start`, `INT01.Reset`) like any other tag:

```json
"controllers": [
  { "id": "INT01", "type": "interlock", "scanPeriodMs": 100,
    "parameters": {
      "conditions": [ { "tag": "CV001.Tripped", "normal": false } ],
      "trip": [ { "tag": "CV001.Start", "value": false } ] } }
]
```

See the [mine-conveyor sample](samples/mine-conveyors/README.md),
[scenarios](docs/scenarios.md), [control blocks](docs/control-blocks.md),
[authoring a component](docs/authoring-a-component.md), the
[configuration diagnostics](docs/configuration-diagnostics.md) and the
[scenario diagnostics](docs/scenario-diagnostics.md).

## Build and test

```bash
dotnet build
dotnet test
```

## Licence

MIT.
