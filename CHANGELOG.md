# Changelog

All notable changes to Millrace are recorded here. Versions follow
[Semantic Versioning](https://semver.org/).

## Unreleased

Millrace's first external consumer: a SCADA watching and operating a simulated
plant over Modbus TCP
([design](docs/superpowers/specs/2026-10-07-modbus-fuxa-hmi-design.md),
[plan 8](docs/superpowers/plans/2026-10-07-modbus-fuxa-hmi.md)).

### Modbus (`Millrace.Modbus`)

- A register map built from a plant's tag directory, in directory order: a
  read-write Bool is a coil, a read-only one a discrete input; a Double is a
  big-endian Float32 and an Int64 a saturating big-endian Int32, two holding
  registers when read-write and two input registers when read-only. A claimed
  tag maps read-only.
- A Modbus TCP server, with no external package: function codes 1, 2, 3, 4,
  5, 6, 15 and 16; exceptions 01, 02 and 03; any unit id; several clients at
  once. Reads come from the published tag image; writes go through
  `Millrace.Realtime`'s `CommandBus` and land at phase 1 of the next tick.

### Scenarios (`Millrace.Scenarios`)

- `ScenarioRunner.Bind` loads a scenario's plant and schedules its timeline
  without running it, for a host that ticks the simulation itself.

### Cli (`millrace`)

- `millrace serve <plant.json> [--scenario <file>] [--port <n>] [--bind <address>] [--speed <x>]`
  runs a plant paced to the wall clock and serves it over Modbus TCP until
  Ctrl+C. It listens on 127.0.0.1 unless `--bind` says otherwise (Modbus has
  no authentication); `--speed` must be at least 0.001; `--scenario` runs its
  timeline but ignores the scenario's duration. Exit 3 now also covers a port that cannot be opened.
- `millrace modbus-map <plant.json> [--format text|csv|fuxa]` prints the register
  map, or the tags of a FUXA Modbus device.

### HMI (`hmi/fuxa/`)

- The mine-conveyor sample in the FUXA web SCADA, with one
  `docker compose up`: an overview mimic with the line's states, values,
  lamps and operator buttons, an alarms view and a trends view. See
  [its README](hmi/fuxa/README.md).

## 1.0.0 — 2026-10-07

The first release: every item of the v1 scope (§17 of the
[design](docs/superpowers/specs/2026-09-02-industrial-process-simulation-engine-design.md)),
with both reference samples.

### Core (`Millrace.Core`)

- A deterministic clock and a five-phase tick: queued writes and scheduled
  events, the signal graph, material flow, the published I/O image, the tick
  frame. Events due on one tick fire in the order they were scheduled.
- Per-component random streams from one seed; the same seed gives the same run,
  byte for byte.
- Typed signal ports, latched inputs, composites flattened to leaves at build
  time, topological resolution with algebraic-loop detection, and `UnitDelay`
  to break a real loop.
- Bulk and discrete payloads, typed flow ports, offer/accept transport and a
  per-tick mass-conservation audit.
- The transform and hold contracts, the fault channel, telemetry, an ordered
  event log, the component catalogue, and a host that scans control blocks at
  their own period and publishes their outputs as tags.

### Components (`Millrace.Components`)

- Material: `bulk-source`, `bulk-sink`, `item-source`, `item-sink`,
  `transfer-chute`, `former`, `bulk-belt`, `discrete-belt`, `bulk-process-unit`,
  `item-process-unit` (with `heatWhileHeld` and the `slow-cycle` fault) and
  `reject-gate`.
- Mechanical: `motor` with an I²t thermal model, `gearbox`, `drive-pulley`,
  `tail-pulley`, `belt-friction` and the `conveyor` composite, which trips its
  own overload when the belt downstream of it blocks.
- Instruments, with the full sensor-fault vocabulary: `speed-sensor`,
  `belt-scale`, `current-sensor`, `temperature-sensor`, `pyrometer`,
  `part-counter` and `zero-speed-switch`.
- Safety: `e-stop`, `pull-key`, `safety-relay` and `motor-starter`, whose run
  permit is an interlock contact in series with the run command.
- Signal: `unit-delay-bool` and `unit-delay-double`, to break a feedback loop
  in a plant file.
- Transforms `thermal-transfer`, `moisture-loss` and `residence-accumulator`;
  holds `for-seconds`, `temperature-at-least`, `temperature-at-most`,
  `state-at-least` and `all`.

### Io (`Millrace.Io.Abstractions`; the `Millrace.Core.Io` namespace)

- A declared, printable tag directory with units, ranges and per-tag quality.
- A lock-free, double-buffered image any thread may read; writes queued from
  any thread land at phase 1 of the next tick.
- One immutable `TickFrame` per tick, with its dirty mask; a tag a control
  block claims can be written by that block only.

### Realtime (`Millrace.Realtime`)

- A ring-buffered hub, a live state engine for late joiners, subscriptions
  with prefix filters, deadbands, decimation and declared backpressure, and a
  validated command bus. It references the I/O contract only.

### Control (`Millrace.Control`, `Millrace.Control.Catalogue`)

- `timer`, `permissive`, `interlock` (trip and reset writes, the run-permit
  pattern), `alarm` (limits with deadbands, on-delays and acknowledgement),
  `sequencer` (steps that end `after` a time or `when` a tag compares with a
  value) and `coil`, written against the I/O contract alone, declared in a
  plant file's `controllers` section or attached in code.
- An interlock logs `RESET_REFUSED`, naming the first condition that is still
  not normal, when it refuses a reset.

### Scenarios (`Millrace.Scenarios`)

- A scenario file: a plant, engine overrides, a duration and a timeline of
  writes, fault injections and clears, each landing on the tick it names.
- Golden event logs, compared line for line, and a recorder that turns a live
  run into a scenario that replays it byte for byte.

### Configuration (`Millrace.Configuration`)

- Declarative JSON plants — materials, components, flow links, a tags
  envelope and controllers — loaded through the catalogue, with a JSON Schema
  generated from it.
- Every mistake reported at once, each with a code — `MR0xx` from the
  engine, `MR1xx` from the loader, `MR2xx` from a scenario — a JSON path and
  its fix.

### Cli (`millrace`)

- `catalog export`, `schema export`, `validate`, `tags` and `run` (with
  `--expect`, `--out`, `--format json` and `--assembly`), and exit codes a
  script can rely on: 4 when a run's log differs from its golden.

### Event messages

- Measured values print to fixed decimals. An alarm, a stalled motor and a
  tripped overload print the value on the right side of the limit it crossed.
  A process unit's hold message gives the wall time too when a slowed cycle
  made it differ from the hold time.

### Samples

- [`samples/mine-conveyors/`](samples/mine-conveyors/README.md): three
  conveyors, a feeder and a stockpile with a sequenced start and stop, cascade
  interlocks, permissives and alarms; nine scenarios.
- [`samples/wheel-line/`](samples/wheel-line/README.md): a forging cell of
  discrete items whose PLC rejects an over-soaked billet; six scenarios.
- Both are data only, and every scenario is checked against its golden log.

### Design and plans

Specs, in `docs/superpowers/specs/`:
[engine design](docs/superpowers/specs/2026-09-02-industrial-process-simulation-engine-design.md),
[catalogue, configuration and CLI](docs/superpowers/specs/2026-09-20-catalogue-configuration-cli-design.md),
[control blocks](docs/superpowers/specs/2026-09-22-control-blocks-design.md),
[scenarios and run](docs/superpowers/specs/2026-09-22-scenarios-and-run-design.md),
[controllers in plant JSON](docs/superpowers/specs/2026-09-25-controllers-in-plant-json-design.md),
[interlock start inhibit](docs/superpowers/specs/2026-09-25-interlock-start-inhibit-design.md),
[mine-conveyor sample](docs/superpowers/specs/2026-09-25-mine-conveyor-sample-design.md),
[block-claimed tags](docs/superpowers/specs/2026-09-30-block-claimed-tags-design.md),
[discrete reject physics](docs/superpowers/specs/2026-09-30-discrete-reject-physics-design.md),
[HMI message polish](docs/superpowers/specs/2026-09-30-hmi-message-polish-design.md),
[wheel-line sample](docs/superpowers/specs/2026-10-02-wheel-line-sample-design.md),
[v1 close-out](docs/superpowers/specs/2026-10-07-v1-close-out-design.md).

Plans, in `docs/superpowers/plans/`:
[1 simulation core](docs/superpowers/plans/2026-09-02-simulation-core.md),
[2 material flow](docs/superpowers/plans/2026-09-11-material-flow.md),
[3 component library](docs/superpowers/plans/2026-09-12-component-library.md),
[4 I/O and real time](docs/superpowers/plans/2026-09-13-io-and-realtime.md),
[5a catalogue, configuration and CLI](docs/superpowers/plans/2026-09-20-catalogue-configuration-cli.md),
[5b scenarios and run](docs/superpowers/plans/2026-09-22-scenarios-and-run.md),
[5c control blocks](docs/superpowers/plans/2026-09-22-control-blocks.md),
[5d controllers in plant JSON](docs/superpowers/plans/2026-09-25-controllers-in-plant-json.md),
[6a mine-conveyor sample](docs/superpowers/plans/2026-09-25-mine-conveyor-sample.md),
[6c interlock start inhibit](docs/superpowers/plans/2026-09-25-interlock-start-inhibit.md),
[6d block-claimed tags](docs/superpowers/plans/2026-09-30-block-claimed-tags.md),
[6e HMI message polish](docs/superpowers/plans/2026-09-30-hmi-message-polish.md),
[6b.1 discrete reject physics](docs/superpowers/plans/2026-09-30-discrete-reject-physics.md),
[6b.2 wheel-line sample](docs/superpowers/plans/2026-10-02-wheel-line-sample.md),
[7 v1 close-out](docs/superpowers/plans/2026-10-07-v1-close-out.md).
