# Modbus TCP Server and FUXA HMI — Design (plan 8)

Date: 2026-10-07. Addendum to
`2026-09-02-industrial-process-simulation-engine-design.md` (the "main spec"),
taking up §18's deferred Modbus TCP adapter on top of the real-time layer of
§10. Builds on v1.0.0.

**Amended 2026-10-07 by the plan**
(`docs/superpowers/plans/2026-10-07-modbus-fuxa-hmi.md`, rulings
R194–R210), where the code, a measured run or the pinned FUXA forced a
choice:

- **FUXA 1.3.4, with its Modbus driver added (R204)**: the newest image tag
  is 1.3.4, and the image ships without `modbus-serial`, which FUXA
  installs as a plugin at run time. The `fuxa` service is built from
  `frangoteam/fuxa:1.3.4` (pinned by digest) plus `modbus-serial@8.0.19`.
- **The views are generated, then verified in FUXA (R205)**: written from
  FUXA's widget structures by a committed generator,
  `hmi/fuxa/generate-project.py`, posted to a running FUXA and checked in a
  browser; the committed JSON is the generator's output, and the generator
  is its source.
- **Criterion 6's screenshot and start-button press are a manual
  Playwright step in Task 5**: `smoke-check.sh` presses Start through
  `POST /api/runscript` (the button's script) and takes no screenshot.
- **FC6 always answers 02 (R197)**: every holding register is half of a
  two-register value. Reads may start mid-value; writes may not. A refused
  multi-value write writes nothing.
- **A bad frame closes its connection (R198)**: protocol id other than 0,
  or a length outside 2–254.
- **Push-buttons pulse (R206)**: the sample's commands are rising edges
  scanned every 100–200 ms, so buttons run a FUXA server script that holds
  each tag true for 0.5 s; Start and Stop reset their sequencer first.
  Pull-keys and e-stops are toggles. "Interlock and safety resets" are two
  plant-wide buttons.
- **`dse serve` (R199, R200)**: `--port 0` picks a free port; a port that
  cannot be opened exits 3; a scenario must name the plant served; SIGINT
  and SIGTERM stop it with exit 0. `dse serve` also takes `--assembly`.
  `ScenarioRunner.Bind` is added to `Dse.Scenarios` for it.
- **`fuxa-init` loads the project only into a FUXA that does not have it
  (R208)**, so editor changes survive a restart; `docker compose down -v`
  resets.
- **The changelog's "Unreleased" entry also fixes `master` (R194)**:
  `ReleaseTests` requires every spec and plan to be linked, and the plan 8
  spec was not.
## 1. Scope

The first external consumer of DSE: a real SCADA watching and operating a
simulated plant. DSE gains a Modbus TCP server and a real-time host command;
the open-source web SCADA **FUXA** connects to it as a Modbus master and shows
the mine-conveyor sample as an operator HMI — a mimic, alarms and trends —
started with one `docker compose up`.

### Decisions taken in brainstorming

| Question | Decision |
|---|---|
| UI | **FUXA** (github.com/frangoteam/FUXA, MIT, web SCADA/HMI, Docker image `frangoteam/fuxa`), rather than an own web HMI or a terminal UI: the quickest path to a real SCADA screen, connected the way a plant SCADA would be, and editable afterwards in FUXA's editor. |
| Protocol | **Modbus TCP**, rather than OPC UA: a server is small and dependency-free in .NET, FUXA's client supports it natively, and OPC UA would bring the OPC Foundation stack, certificates and its licensing. |
| Placement | **This repository**: `src/Dse.Modbus` (+ tests) in `Dse.sln`, `dse serve` and `dse modbus-map` in `Dse.Cli`, and `hmi/fuxa/` for the compose stack and the FUXA project. |
| Plant | **The mine conveyors first**; a wheel-line HMI is a follow-up. |

### Facts about FUXA this design relies on (researched 2026-10-07, FUXA 1.3.5)

- Image `frangoteam/fuxa`, web UI on port 1881; project and settings in
  `/usr/src/app/FUXA/server/_appdata`, history in `.../_db`, logs in
  `.../_logs`.
- Device type `ModbusTCP`; property `address` (`host:port`), `slaveid`,
  `connectionOption` `TcpPort`; device-level `polling` in ms.
- Tags: `memaddress` `000000` coils, `100000` discrete inputs, `300000`
  input registers, `400000` holding registers; `address` **1-based**
  (`address 1` = wire offset 0); types include `Bool`, `Int32`, `Float32`
  (unsuffixed names are big-endian; `…MLE` word-swapped, `…LE`
  byte-swapped). Coils written with FC5; holding registers with FC6, or FC16
  for values longer than one register.
- A whole project is loaded with `POST /api/project` (the export format of
  "Save project"); auth is off by default; there is no load-at-startup
  setting.
- Each view's items must also exist as elements in the view's `svgcontent`,
  so views are not practical to hand-write; they are built and verified
  against a running FUXA (§4).

### Success criteria

1. **Register map (`Dse.Modbus`).** Built from a plant's tag directory, in
   directory order, deterministic for a given plant:

   | Tag kind and access | Modbus area | Encoding |
   |---|---|---|
   | Bool, read-write | coils | one bit |
   | Bool, read-only | discrete inputs | one bit |
   | Double, read-write | holding registers | Float32, big-endian, 2 registers |
   | Double, read-only | input registers | Float32, big-endian, 2 registers |
   | Int64, read-write | holding registers | Int32, big-endian, 2 registers, saturating |
   | Int64, read-only | input registers | Int32, big-endian, 2 registers, saturating |

   Access is the tag directory's published access, so a claimed tag (6d)
   maps to a read-only area. Within each area, addresses are assigned from 0
   in directory order, 2 registers per register-mapped tag.
2. **Server (`Dse.Modbus`).** A Modbus TCP server (MBAP framing; any unit
   id is accepted and echoed in the response, as one plant is one slave) with function
   codes 1, 2, 3, 4, 5, 6, 15 and 16. Reads come from the tag image's
   published snapshot (thread-safe). Writes go through `Dse.Realtime`'s
   `CommandBus`; a write to an address no read-write tag occupies, or a
   partial write of a 2-register value, answers exception 02 (illegal data
   address); a non-finite or otherwise invalid value answers 03 (illegal data
   value); an unsupported function answers 01. Several clients may connect at
   once. No external package.
3. **Host (`Dse.Cli`).**
   - `dse serve <plant.json> [--scenario <file>] [--port <n>] [--speed <x>]`
     loads the plant (and the scenario's timeline, if given), runs it with
     `SimulationRunner` in real time (or `--speed` times real time) until
     Ctrl+C, serves Modbus TCP on the port (default 5020), and prints the
     address it listens on. Exit codes follow the CLI's existing conventions.
   - `dse modbus-map <plant.json> [--format text|csv|fuxa]` prints the
     register map: text and CSV list area, address (1-based as a SCADA shows
     it, and 0-based wire offset), type, tag, access, unit and description;
     `fuxa` prints the FUXA device `tags` object for the plant.
4. **FUXA stack (`hmi/fuxa/`).**
   - `docker-compose.yml` with three services: `dse` (built from a Dockerfile
     in the repo, running `dse serve samples/mine-conveyors/plant.json
     --port 5020`), `fuxa` (`frangoteam/fuxa` pinned to a version tag, port
     1881, named volumes for `_appdata`, `_db`, `_logs`), and `fuxa-init` (a
     one-shot curl container that waits for FUXA's API and POSTs the project).
   - `mine-conveyors.fuxap.json`: a `ModbusTCP` device at `dse:5020`, slave
     1, polling 200 ms, whose `tags` are exactly `dse modbus-map … --format
     fuxa`; and three views:
     - **Overview** — a mimic of ore source → CV001 → chute → CV002 → chute →
       CV003 showing, per belt, a running/stopped/tripped state colour, speed
       (m/s), load (t/h) and motor current (A); lamps for contactor, safety
       relay, zero speed and its interlock's `Ok`; buttons for the start and
       stop sequences, interlock and safety resets, and pull-key and e-stop
       actuation.
     - **Alarms** — FUXA alarms for the sample's alarm blocks (Hi/HiHi) and
       the interlocks' trip states.
     - **Trends** — the three belts' speeds and motor currents.
   - The views are built and verified against a running FUXA (§4); the
     committed JSON is the project FUXA accepted and rendered.
5. **Tests (`dotnet test`).**
   - Protocol: each function code, each exception, MBAP framing (including a
     request split across reads and two requests in one read), several
     concurrent clients.
   - Register map: deterministic addresses; claimed tags read-only; Float32 and
     Int32 encodings, Int64 saturation.
   - End to end: `dse serve` on the mine plant at a high `--speed`, a minimal
     test-side Modbus client writes the start-sequence coil and reads CV001's
     speed rising past its at-speed value.
   - In sync: the committed FUXA project's device `tags` equal
     `dse modbus-map … --format fuxa`.
6. **Smoke check (scripted, documented, run by the plan, not in
   `dotnet test`).** `docker compose up`, the init POST succeeds, FUXA's REST
   API returns live values for CV001's speed, and a headless-browser
   screenshot of the overview shows the mimic with live values; pressing the
   start-sequence button starts the belts.
7. **Docs.** `hmi/fuxa/README.md`: prerequisites (Docker), run, open
   http://localhost:1881, what each button does and which sample scenario it
   mirrors, the register map (generated by `dse modbus-map`), and the limits
   (local demo, no auth, edits in FUXA's editor stay in its volume until
   exported). `docs/` and the root README mention `dse serve` and the HMI;
   `CHANGELOG.md` gains an "Unreleased" entry.

## 2. Out of scope

- OPC UA, MQTT, authentication, TLS.
- The wheel-line HMI (follow-up).
- FUXA history and reporting beyond the trends view.
- Any change to the engine's components, blocks or results.
