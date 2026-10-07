# Dse — Deterministic Industrial Process Simulation Engine

A deterministic, composable simulation engine for real-world industrial
processes, written in .NET. Build virtual plants from reusable machines,
instrumentation and process components, then run, test, break and replay them
deterministically.

The engine simulates behaviour, not tags: belt speed, motor current and tonnes
per hour are derived from a physical model, so a change anywhere propagates
causally through the plant.

## Status

**Version 1.0.0.** Everything in the v1 scope of the
[design](docs/superpowers/specs/2026-09-02-industrial-process-simulation-engine-design.md)
is in: the deterministic core and material layer, a library of thirty
component types, the I/O and real-time layers, six control blocks, scenarios
replayed against golden event logs, JSON plants with a generated schema, the
`dse` command line, and two reference samples. The [changelog](CHANGELOG.md)
lists what each project contains and links every spec and plan.

The first reference sample, `samples/mine-conveyors/`, is three conveyors, a
feeder and a stockpile with a sequenced start and stop, cascade interlocks,
permissives and alarms, and nine scenarios. The second sample,
`samples/wheel-line/`, is a forging cell of discrete items — a billet saw, a
furnace, a measuring station with a pyrometer and a reject kicker, a belt and a
press — whose PLC rejects an over-soaked billet, with six scenarios. Both are
data only: no C#.

## Getting started

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0);
nothing else. From the repository root:

```bash
dotnet build Dse.sln
dotnet test Dse.sln
```

The tests run every sample scenario against its golden log. To run one
yourself, through the `dse` command line, and check it against its golden:

```bash
dotnet run --project src/Dse.Cli -- run samples/mine-conveyors/scenarios/pull-key.json --expect samples/mine-conveyors/expected/pull-key.log
dotnet run --project src/Dse.Cli -- run samples/wheel-line/scenarios/slow-press.json --expect samples/wheel-line/expected/slow-press.log
```

Each prints `Matched <golden> (<n> events).` and exits 0; it exits 4 if the
log has changed. Leave out `--expect …` to print the event log itself.

Where to read next: the [mine-conveyor sample](samples/mine-conveyors/README.md)
and the [wheel-line sample](samples/wheel-line/README.md) tell each scenario's
story; [architecture](docs/architecture.md) explains the tick, the port model
and the determinism rules; [scenarios](docs/scenarios.md),
[control blocks](docs/control-blocks.md) and
[authoring a component](docs/authoring-a-component.md) show how to write your
own. The design specs are in `docs/superpowers/specs/` and the implementation
plans in `docs/superpowers/plans/`.

## Command line

```bash
dotnet run --project src/Dse.Cli -- catalog export            # every component, block, transform, transition, hold and material, as JSON
dotnet run --project src/Dse.Cli -- schema export --out dse-plant.schema.json
dotnet run --project src/Dse.Cli -- validate plant.json       # every error, each with its fix; exit 1 if any
dotnet run --project src/Dse.Cli -- tags plant.json           # the tag directory a SCADA would see
dotnet run --project src/Dse.Cli -- run scenario.json --expect golden.log  # replay a scenario; exit 4 if the log changed
dotnet run --project src/Dse.Cli -- run samples/mine-conveyors/scenarios/pull-key.json  # the sample: a pull-key stops the line
dotnet run --project src/Dse.Cli -- run samples/wheel-line/scenarios/slow-press.json      # the second: a slow press over-soaks a billet and the PLC rejects it
dotnet run --project src/Dse.Cli -- modbus-map plant.json     # the Modbus register map dse serve serves
dotnet run --project src/Dse.Cli -- serve samples/mine-conveyors/plant.json  # run it in real time on Modbus TCP port 5020 until Ctrl+C (localhost only; --bind 0.0.0.0 opens it to the network)
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

A block may claim a tag so that only it writes it; see *Claiming a tag* in
[control blocks](docs/control-blocks.md).

See the [mine-conveyor sample](samples/mine-conveyors/README.md), the
[wheel-line sample](samples/wheel-line/README.md),
[scenarios](docs/scenarios.md), [control blocks](docs/control-blocks.md),
[authoring a component](docs/authoring-a-component.md), the
[configuration diagnostics](docs/configuration-diagnostics.md) and the
[scenario diagnostics](docs/scenario-diagnostics.md).

## A SCADA on the sample

`dse serve` runs a plant in real time and serves its tags over Modbus TCP, so a
real SCADA can watch and operate it. `hmi/fuxa/` puts the mine-conveyor sample
in the open-source web SCADA FUXA — an overview mimic, alarms and trends, with
start, stop, reset, pull-key and e-stop buttons — with one command:

```bash
cd hmi/fuxa
docker compose up --build
```

then open <http://localhost:1881>. See the [FUXA HMI](hmi/fuxa/README.md) for
what each screen and button does, and the register map.

## Licence

MIT.
