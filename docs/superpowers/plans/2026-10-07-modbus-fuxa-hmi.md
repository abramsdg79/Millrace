# Modbus TCP Server and FUXA HMI Implementation Plan (plan 8)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give DSE its first external consumer. A new `Dse.Modbus` project
maps a plant's tag directory to Modbus registers and serves them over Modbus
TCP; `dse serve` runs a plant in real time behind that server and
`dse modbus-map` prints the map; `hmi/fuxa/` starts the mine-conveyor sample
in the FUXA web SCADA — an overview mimic, alarms and trends — with one
`docker compose up`.

**Architecture:** `Dse.Modbus` references `Dse.Io.Abstractions` and
`Dse.Realtime` only, and no package (R195). `RegisterMap` assigns addresses
from an `ITagDirectory` in directory order; `RegisterCodec` holds the
big-endian Float32 and saturating Int32 encodings; `ModbusProtocol` turns one
request PDU into one response PDU, reading a published image through a
`Func<ReadOnlyMemory<TagValue>>` once per request and writing through
`CommandBus`; `ModbusServer` frames MBAP over `TcpListener`, one task per
connection. `Dse.Scenarios` gains `ScenarioRunner.Bind` — the load-and-schedule
half of `Run` — so `dse serve --scenario` replays a timeline without a second
copy of the binding rules. `dse serve` ticks the `Simulation` on the CLI
thread with `SimulationRunner` (real time, or `--speed` times it) while the
server answers on the thread pool from `Simulation.IO.Snapshot`. The FUXA
project is generated, not drawn: a scratch generator (in this plan, never
committed) turns `dse modbus-map --format fuxa` into the device, three views,
the alarms, two charts and one server script; the committed JSON is its
output, verified against a running FUXA 1.3.4 (R205).

**Tech Stack:** .NET 10 (`net10.0`, SDK 10.0.401), C#, xUnit 2.9.3. No package
added in `src/` or `tests/`. Docker (Engine 29.7.2, Compose 5.5.1 measured):
`mcr.microsoft.com/dotnet/sdk:10.0.401-noble`,
`mcr.microsoft.com/dotnet/runtime:10.0.12-noble`, `frangoteam/fuxa:1.3.4`
with npm `modbus-serial@8.0.19`, `curlimages/curl:8.22.0` — every image pinned
by tag and digest. Python 3 runs the scratch generator only.

**Spec:** `docs/superpowers/specs/2026-10-07-modbus-fuxa-hmi-design.md` (all
of it, as amended by this plan — R210 gives the amendment note the controller
adds to the spec with the plan commit), on top of the main spec
`docs/superpowers/specs/2026-09-02-industrial-process-simulation-engine-design.md`
§9 (the I/O image), §10 (the real-time layer) and §18.

**Plan sequence:** Plan 8, the first after v1.0.0. It starts from `aca64d9`
(the commit that added the plan 8 spec). Measured on that commit with
`dotnet test Dse.sln`: **1581 tests, 1580 passing, 1 failing** — 37
`Dse.Io.Abstractions` / 498 `Dse.Core` / 189 `Dse.Components` / 57
`Dse.Realtime` / 239 `Dse.Configuration` / 167 `Dse.Scenarios` / 78
`Dse.Cli` / 153 `Dse.Control` / 27 `Dse.Control.Catalogue` / 136
`Dse.Samples`. The failure is
`ReleaseTests.TheVersionTheChangelogAndTheReadmeAgree`: it requires the
changelog to link every spec and plan dated before 2026-10-08, and the plan 8
spec (2026-10-07) is not linked — `master` has been red since `aca64d9`, and
the plan commit adds a second unlinked file. Task 1 makes it green (R194).
Release build `0 Warning(s)`, `0 Error(s)`.

**Task shape.** Six tasks, sequential, each leaving the whole suite green from
Task 1 on:

- **Task 1 — the changelog's Unreleased entry.** An `## Unreleased` heading
  that links the plan 8 spec and plan; `ReleaseTests` accepts it.
- **Task 2 — the register map.** `Dse.Modbus` and `Dse.Modbus.Tests` join the
  solution: `ModbusArea`, `RegisterEntry`, `RegisterMap`, `RegisterCodec`.
- **Task 3 — the Modbus TCP server.** `ModbusProtocol`, `ModbusServer`,
  `ModbusExceptionCode`; the test-side `ModbusClient` in `tests/Shared/`.
- **Task 4 — `dse serve` and `dse modbus-map`.** `ScenarioRunner.Bind`, the
  two commands, their tests, and the end-to-end test on the mine plant.
- **Task 5 — the FUXA stack.** Dockerfiles, compose, the generated FUXA
  project, the smoke check, `hmi/fuxa/README.md` and the in-sync tests; the
  stack is run, smoke-checked and screenshotted, then removed.
- **Task 6 — docs.** The root README, `docs/architecture.md`,
  `docs/scenarios.md`, the full changelog entry, and a test that pins them.

## Global Constraints

- **Only the files a task lists change.** Each task's `git status --short -uall`
  before its commit lists exactly the paths its commit step adds. No task
  edits `docs/superpowers/` (the controller amends the spec with the plan
  commit, R210). `git grep -n PackageReference -- 'src/*.csproj'` prints
  nothing: `Dse.Modbus` has no package (spec criterion 2).
- **No computed value changes.** No engine component, block, message or
  result changes (spec §2). After every task `git diff --stat aca64d9 --
  samples/mine-conveyors samples/wheel-line tests/Dse.Control.Tests/Golden
  tests/Dse.Scenarios.Tests/Golden src/Dse.Core src/Dse.Components
  src/Dse.Control src/Dse.Control.Catalogue src/Dse.Configuration
  src/Dse.Io.Abstractions src/Dse.Realtime` prints nothing.
- **Generated files are generated and read, never hand-edited.** The FUXA
  project `hmi/fuxa/mine-conveyors.fuxap.json` is written by the committed
  generator `hmi/fuxa/generate-project.py` from `dse modbus-map … --format
  fuxa` (its source, R205), and Task 5 checks the output's SHA-256 as a
  reproducibility check;
  the register map in `hmi/fuxa/README.md` is the output of `dse modbus-map`,
  pasted by the script in Task 5, and a test compares the two. If a check
  disagrees, report the measured output; never edit the file to fit.
- **Report every measurement.** Where an expected value in this plan (a test
  count, a failing-test list, an output line, a hash) disagrees with what the
  code produces, report the measured value in the task report; never edit an
  assertion to fit without saying so.
- **Determinism.** No `System.Random`, no `string.GetHashCode()`, no
  `Dictionary`/`HashSet` iteration order reaching an assertion or an output.
  String comparisons are ordinal; formatting and parsing use
  `CultureInfo.InvariantCulture`. The register map is a pure function of the
  directory. (`dse serve` is real time and is not replayable tick for tick;
  nothing asserts on its tick count.)
- **No flaky tests.** Every test that talks to a socket listens on port 0 (the
  system picks a free port) on loopback, every wait is bounded (5 s per
  client read, 10 s or 30 s per condition, as written), and nothing sleeps to
  "let things settle" without a bound. Measured: the whole suite passed four
  runs in a row, and `Dse.Modbus.Tests` runs in under a second.
- **Warnings are errors** (`Directory.Build.props`: `TreatWarningsAsErrors`,
  `EnforceCodeStyleInBuild`, `GenerateDocumentationFile`, `Nullable`). The
  Release build prints `0 Warning(s)` and `0 Error(s)` after every task:
  `dotnet build Dse.sln -c Release --nologo`.
- **xUnit analyzers run under warnings-as-errors:** prefer `Assert.Single`,
  `Assert.Contains`, `Assert.DoesNotContain`, `Assert.Empty`, `Assert.All`;
  never `Assert.True(x.Any())` or `Assert.Equal(1, x.Count())`; never block on
  a task in a test (`Task.Wait`, `Task.WaitAll`, `.Result` are xUnit1031
  errors — use `await … .WaitAsync(timeout)`).
- **Test names** are long descriptive PascalCase sentences, like the existing
  ones. No existing test is renamed.
- **Messages and files are verbatim.** Copy every C# file, message, README,
  Dockerfile, compose file, script and changelog line in this plan byte for
  byte, including the em dash (`—`, U+2014), the middle dot (`·`), the
  ellipsis (`…`) and the arrows (`──▶`). LF line endings, a final newline, no
  tabs. Markdown prose wrapped at about 78 columns.
- **Git, for every task.** One git command per `Bash` call. `git add` names
  paths explicitly — never `git add -A`, never `git add .`. **Never `git
  stash`.** Commit messages are conventional (`feat(modbus): …`): a subject
  line, a blank line, a body wrapped at about 78 columns, and the trailer as
  the last line, copied verbatim:

  ```
  Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
  ```

  The trailer identifies the **session** that planned and drives this work,
  not the model that implements a given task: it is the same on every commit,
  whichever implementer model a task names. Never put it on the subject line.
  Write each message with the Write tool to `.superpowers/sdd/8/msg-taskN.txt`
  and commit with `git commit -F .superpowers/sdd/8/msg-taskN.txt`; then run
  `git log -1 --format='%h%n%s%n--%n%b'` and check the subject, the blank line
  and the trailer.
- **Commands**, from the repository root: `dotnet build Dse.sln -c Release --nologo`
  (expect `0 Warning(s)`, `0 Error(s)`) and `dotnet test Dse.sln --nologo`
  (expect the task's total). `.superpowers/` is git-ignored; scratch work,
  the FUXA generator and commit messages go under `.superpowers/sdd/8/` and
  are never added. Inside a worktree the harness refuses Bash text that
  mentions git inside a heredoc, `$(…)`, a variable or a loop, and any complex
  command containing "Github" (the repository path): write files with the
  Write tool and run each command as the plain command this plan shows.
- **Docker (Task 5 only).** Everything Task 5 starts it also removes: after
  the task, `docker ps -a`, `docker volume ls` and `docker network ls` list
  nothing whose name starts with `dse-hmi`, and `docker images` lists no
  `dse-hmi/*`, `curlimages/curl` or `frangoteam/fuxa` image. Never touch a
  container, volume, network or image the task did not create (the machine
  runs others, such as `dockhand`). Pkill/pgrep patterns must not match the
  shell's own command line: write `pgrep -af '[n]et10.0/dse serve'`, not
  `pgrep -f 'dse serve'` (measured: the bare pattern kills the calling shell,
  exit 144).

## Review Focus

The five input classes the spec implies but does not name, most likely to
bite first. Each has its pinning test in the owning task.

1. **Writes that cover part of a value, or several values one of which is
   bad.** A 2-register value written by FC6, by FC16 starting on its second
   register, or by an FC16 that ends half-way into the next value; a
   multi-value FC16 whose second value is NaN, infinite or out of range. The
   author expects exception 02 for the first three and 03 for the last, and
   — the part a reader may miss — **nothing of the request written**
   (R197). Tests: Task 3,
   `AWriteToAnAddressNoReadWriteTagOccupiesOrToHalfAValueIsException02`,
   `AWriteOfPartOfATwoRegisterValueIsException02AndWritesNothing`,
   `ANonFiniteOrOutOfRangeValueIsException03AndNothingOfTheRequestIsWritten`,
   `ACoilValueOtherThanFF00Or0000IsException03`.
2. **Framing at the edges of a read.** A request split across three TCP
   segments, two requests in one segment, a frame with protocol id 1, a
   length of 1 or 256. The author expects one whole answer, two answers in
   order with their own transaction ids, and a closed connection for the
   impossible frames — without taking down the server (R198). Tests: Task 3,
   `ARequestSplitAcrossSeveralReadsIsAnsweredOnceWhole`,
   `TwoRequestsInOneReadAreAnsweredInOrder`,
   `AFrameWithAForeignProtocolIdOrAnImpossibleLengthClosesTheConnection`.
3. **A read while the image is being replaced, and many clients.** A read of
   several registers must not mix two ticks; eight clients reading and
   writing at once must each get their own answers. Tests: Task 3,
   `OneRequestsValuesComeFromOneImageWhileTheImageIsReplaced` (a publisher
   thread rewrites the image continuously; 500 reads each see one image),
   `SeveralClientsAtOnceEachGetTheirOwnAnswers`,
   `DisposingTheServerClosesEveryConnectionAndStopsListening`.
4. **Values the wire cannot carry, and an image not yet primed.** An Int64
   beyond Int32 (it saturates), a double beyond the single's range (it becomes
   an infinity), and the all-`default` image a `TagImage` holds before
   `Simulation.Initialize` (a `Bool` `false` in every slot, so `AsDouble`
   would throw) — the author expects zeros, not a crashed connection (R196).
   Tests: Task 2, `AnInt64IsABigEndianInt32ThatSaturates`,
   `ADoubleIsABigEndianSingleHighWordFirst`,
   `AValueOfAnotherKindEncodesAsZeroRatherThanThrowing`; Task 3,
   `AnImageNotYetPrimedReadsAsZeroesRatherThanFailing`.
5. **The HMI drifting from the plant.** A tag renamed or a block added
   changes the map; a hand edit of the FUXA project could point a button at
   a read-only tag, which FUXA would accept and DSE would refuse with 02. The
   author expects a test to fail rather than the HMI to go quietly dead.
   Tests: Task 5, `TheDeviceTagsAreExactlyTheModbusMapOfTheMinePlant`,
   `EveryTagTheHmiReferencesIsADeviceTag`, `EveryTagTheHmiWritesIsACoil`,
   `TheReadmesRegisterMapIsTheOneModbusMapPrints`.

Also pinned, beyond the five: `dse serve` on a port in use exits 3 and names
the port (`APortInUseExits3AndSaysWhichPort`), a scenario written for another
plant is refused (`AScenarioForAnotherPlantIsAUsageError`), and a cancelled
run stops before its first tick with the exit code 0 that Ctrl+C gives
(`AServedPlantSaysWhatItServesAndWhereAndStopsWhenCancelled`).

## Decisions settled here (rulings R194–R210)

These refine the spec where the code, a measured run or the pinned FUXA forced
a choice. R210 gives the amendment note that records them in the spec.

- **R194 — `master` is red at `aca64d9`; Task 1 turns it green.**
  `ReleaseTests.TheVersionTheChangelogAndTheReadmeAgree` (plan 7) requires
  `CHANGELOG.md` to link every spec and plan file dated before 2026-10-08,
  and allows exactly one `## ` heading. The plan 8 spec (committed in
  `aca64d9`) and this plan (committed with the plan) are dated 2026-10-07 and
  unlinked (measured: `Failed: 1, Passed: 135` in `Dse.Samples.Tests`, "Not
  found: docs/superpowers/specs/2026-10-07-modbus-fuxa-hmi-…"). Task 1 adds
  an `## Unreleased` section above `## 1.0.0 — 2026-10-07` that links both,
  and relaxes the one-heading assertion to "an optional `## Unreleased`, then
  the one release heading, last". The date filter stays: the plan 8 files
  are dated before 2026-10-08, so the test now also proves plan 8 is in the
  changelog. Task 6 fills the section in.
- **R195 — `Dse.Modbus` references `Dse.Io.Abstractions` and `Dse.Realtime`,
  and reads through a delegate.** The server needs the directory and
  `TagValue` (Io.Abstractions) and `CommandBus` (Realtime, spec criterion 2).
  It does not reference `Dse.Core`: the one Core member it needs is
  `TagImage.Snapshot()`, the whole published array swapped with one volatile
  write, which no Io.Abstractions interface exposes (`ITagReader.Read(int)`
  reads one tag at a time, so a multi-register read could straddle two
  ticks). The server takes a `Func<ReadOnlyMemory<TagValue>>` — in `dse
  serve`, the method group `simulation.IO.Snapshot` — and calls it once per
  read request. This keeps the adapter a bolt-on in the sense of
  `docs/architecture.md`'s real-time boundary, and lets its tests run with a
  fake directory, a recording `ITagWriter` and a plain array, without Core.
  `RealtimeHub`/`LiveState` are not used: they need a dispatcher thread and
  would add a frame of lag for nothing a register read needs.
- **R196 — The map's details.** "Directory order" is the directory's index
  order, which `TagDirectory` sorts by ordinal name; addresses are assigned
  per area from 0 with no gaps, one bit per Bool, two registers per Double or
  Int64 (spec criterion 1). An area that would pass 65 536 addresses throws
  `ArgumentException` naming the area (the mine plant uses 36 coils, 59
  discrete inputs, 56 input and 2 holding registers). Encodings: a Double is
  narrowed to an IEEE single — beyond ±3.4 × 10³⁸ it becomes an infinity,
  NaN stays NaN; an Int64 saturates at `int.MinValue`/`int.MaxValue`; both
  high word first, high byte first (FUXA's unsuffixed `Float32`/`Int32`).
  Quality is not carried (Modbus has no field for it). A slot whose value
  is of another kind than its tag — the all-`default` image before
  `Simulation.Initialize` holds a `Bool` in every slot — encodes as 0, so a
  connection never faults on it; `dse serve` calls `Initialize` before it
  opens the port anyway.
- **R197 — Requests, checked in the specification's order.** Function first
  (01 for any code but 1, 2, 3, 4, 5, 6, 15, 16 — including 0, 7, 8, 0x17,
  0x2B); then the shape (03: a PDU of the wrong length, a quantity of 0 or
  above 2000 bits / 125 registers read or 1968 bits / 123 registers written,
  a byte count that does not match, a coil value other than `FF00`/`0000`);
  then the address (02: past the end of the area, or a write that covers
  part of a two-register value); then the value (03: a non-finite single, a
  value outside the tag's declared range, anything the `CommandBus` refuses).
  **Reads may start or end mid-value** (a SCADA reading one register of a
  float gets that half of the one snapshot); **writes may not**. Every
  holding register belongs to a two-register value, so **FC6 always answers
  02** inside the area — it is supported in that it is parsed and answered,
  as the spec's list says, and FUXA never sends it for a `Float32`/`Int32`
  (it uses FC16 for any value longer than one register; measured in FUXA's
  driver, `value.length > 2`). A multi-value FC15/FC16 is validated whole
  before any value reaches the `CommandBus`, so a refused request writes
  nothing; should the bus still refuse one (unreachable for a directory's
  own tags), the request answers 03 rather than echoing a write that did not
  happen. A range is checked on the value the single carries, widened to a
  double: a limit that a single cannot represent exactly is a boundary a
  master may not reach — writing `1.95f` to a tag ranged [0, 1.95] sends
  1.9500000476837158 and is refused 03; the nearest single below the limit
  is accepted. Its values are queued under one lock, contiguously in the write
  queue, but are not promised to land on one tick (each is its own
  command, and the simulation drains the queue at phase 1 of whichever tick
  follows).
- **R198 — MBAP.** A 7-byte header, then `length − 1` PDU bytes, each read
  with `ReadExactlyAsync`, so a request split across segments waits for its
  rest and two requests in one segment are two frames. A frame whose
  protocol id is not 0, or whose length is outside 2–254, **closes its
  connection** (there is no exception response without a trustworthy
  frame); the server and other connections carry on. Any unit id is
  accepted and echoed, as is the transaction id. One task per connection
  answers its requests in order; connections are independent. Disposing the
  server cancels every read, closes every socket and awaits every
  connection task.
- **R199 — `dse serve`.** Options: `--scenario`, `--port`, `--speed` and
  `--assembly` (every plant-loading command takes `--assembly`; there is no
  `--time-step`, which a scenario's `timeStepMs` covers). `--port` is a whole
  number 0–65535, default 5020 (502 needs privileges); **0 asks the system
  for a free port**, which the tests use; the listener binds `0.0.0.0`.
  `--speed` is a finite factor > 0; 1 runs `ExecutionMode.RealTime`, any
  other value `Scaled`. The command loads (and, with a scenario, binds), calls
  `Initialize`, opens the port, prints
  `Serving <plant>: <n> tags as <c> coils, <d> discrete inputs, <i> input registers and <h> holding registers.`
  and `Listening on 0.0.0.0:<port> (Modbus TCP, any unit id) at <x>x real time. Press Ctrl+C to stop.`,
  runs until SIGINT, SIGTERM or the caller's `CancellationToken`, prints
  `Stopped at <yyyy-MM-dd HH:mm:ss.fff> after <ticks> ticks.` and exits 0.
  Exit codes keep their meanings: 1 an invalid plant or scenario, 2 usage, 3
  a file that cannot be read — **and a port that cannot be opened** (the
  general help's exit-code line says so; no new code). `CliApp.Run` gains an
  overload with a `CancellationToken`, which `CliContext.Cancellation`
  carries, so a test stops a run as Ctrl+C would. Signals are taken with
  `PosixSignalRegistration` for SIGINT and SIGTERM, each cancelling the run
  (measured: `docker compose stop dse` returns in 0.4 s with `Stopped at …`
  in the log and exit 0; a SIGINT sent to a process started in the
  background by a non-interactive shell is ignored, because such a shell
  starts it with SIGINT ignored — use SIGTERM there).
- **R200 — `--scenario`.** The scenario must name the plant given (its
  `plant`, resolved against the scenario file, equals the full path of
  `<plant.json>`, ordinal); otherwise exit 2 with
  `'--scenario <s>' runs the plant '<a>', not '<b>'.` — serving one plant's
  map while scheduling another's writes would be wrong in a way nothing
  would report. The scenario's seed, start time and time step apply, its
  timeline is scheduled through the new `ScenarioRunner.Bind` (the first half
  of `Run`, which now calls it; diagnostics identical), and the run goes on
  past the scenario's `duration` until stopped. A scenario that does not
  parse or bind is reported as `dse run` reports it, exit 1.
- **R201 — `dse modbus-map`.** Options `--format text|csv|fuxa` (default
  text), `--out` and `--assembly`. `--format` here is its own `OptionSpec`
  (`MapFormat`, same name, other values); `CliApp`'s value check now looks at
  the options the command actually has, so `validate --format csv` is still
  refused and `modbus-map --format json` is refused with
  `'--format json' is not a map format. Use text, csv or fuxa.`. Text and CSV
  list the entries **by area, then address** (how a map is read); columns
  `area, address (1-based), offset (0-based), type, tag, access, unit,
  description`, a claimed tag's description ending `(claimed by <block>)` in
  the text and a `claimedBy` column in the CSV; the CSV quotes a cell with a
  comma, quote or line break (RFC 4180). The FUXA object lists tags in
  directory order.
- **R202 — The end-to-end test runs `dse serve` in-process.** A child
  process would need a built `dse` at a known path and a kill on failure;
  in-process, `CliApp.Run` takes a `CancellationToken`, stdout is a locked
  writer the test polls for `Listening on 0.0.0.0:<port>`, `--port 0` gives a
  free port, and `--speed 20` brings CV001 to 1.74 m/s in about 0.5 s of wall
  time (measured; the bound is 30 s). The test finds the addresses from
  `dse modbus-map --format csv`, as an integrator would, writes the coil
  `SEQ_START.Start` (one rising edge starts the sequence), and polls
  `CV001.Speed`. The test-side `ModbusClient` is one file,
  `tests/Shared/ModbusClient.cs`, linked into `Dse.Modbus.Tests` and
  `Dse.Samples.Tests` as `Golden.cs` is linked elsewhere; it knows only the
  wire protocol.
- **R203 — FUXA tag ids are `t_` + the DSE tag name.** Stable for a given
  plant, readable in FUXA's editor, and the same string the views, alarms,
  charts and script reference (FUXA treats ids as opaque strings; dots are
  fine — measured, `getTagValue` and `setTagValue` with `t_CV001.Speed`).
  Each tag object is exactly `id`, `name` (the DSE name), `type` (`Bool`,
  `Float32`, `Int32`), `memaddress` (`000000`, `100000`, `300000`,
  `400000`), `address` (1-based, a string, as FUXA stores it) and
  `description` (the DSE description, with ` (<unit>)` when the tag has one).
  These are the fields FUXA's own `Tag` model uses for a Modbus tag that is
  not scaled; the committed project is posted as is.
- **R204 — FUXA facts that differ from the spec.** (a) The newest version tag
  of `frangoteam/fuxa` on Docker Hub is **1.3.4** (2026-08-13); 1.3.5 is the
  version of FUXA's source tree, not an image. (`latest` was rebuilt on
  2026-10-05 with another digest, `sha256:095496b2…` — probably a 1.3.5
  development build; it is not a version and is not used.) (b) **The image has no Modbus
  driver**: FUXA loads `modbus-serial` as a plugin, installed from npm at run
  time; with the plain image, posting the project logs
  `try to create DSE but plugin is missing!` and every value stays `null`
  (measured). So the `fuxa` service is built by `hmi/fuxa/fuxa.Dockerfile`:
  `FROM frangoteam/fuxa:1.3.4@sha256:3778da33…` plus
  `npm install --save-exact modbus-serial@8.0.19` (the version FUXA's plugin
  registry names), so the stack starts with no download after the build
  (npm reports `added 3 packages` — `serialport` is already in the image —
  and an `EBADENGINE` warning for a dependency that wants a newer Node than
  the image's 18; the driver works, measured).
  (c) Everything else the spec relies on holds in 1.3.4: device type
  `ModbusTCP`, `address` `host:port`, `slaveid`, `connectionOption`
  `TcpPort`, device `polling`; `memaddress` areas; 1-based `address`;
  `Float32`/`Int32` big-endian; coils by FC5; `POST /api/project` with auth
  off; no load-at-startup setting.
- **R205 — The FUXA project is generated, then verified in FUXA.** The spec
  says views "are built and verified against a running FUXA … the committed
  JSON is the project FUXA accepted and rendered". Drawing ~50 widgets in
  FUXA's editor by browser automation is neither reproducible nor reviewable,
  so the views were written programmatically from FUXA 1.3.4's own widget
  structures (the editor's SVG templates and the runtime's gauge classes),
  posted to a running FUXA and checked in a browser: values, colours, lamps,
  buttons, the alarm table and both trend charts render and update (R209
  lists the screenshots). The generator is committed as
  `hmi/fuxa/generate-project.py` (Python 3, standard library only, run from
  the repository root) and is the project's source: the README says to
  regenerate with it, and that edits exported from FUXA's editor must be
  folded back into it or the next regeneration loses them. No `dotnet test`
  runs Python; the in-sync tests check the JSON. Its output for the mine
  plant's map has SHA-256
  `a750709058ce53fb71a15380d465d39531b91cc110941507e36c763dbff3a0fc`, 3159
  lines — Task 5 checks it as a reproducibility check — and that file is
  committed beside it. Two FUXA facts the generator
  encodes, both measured: a widget group must not carry the editor's
  transient `style="pointer-events:none"` (with it, clicks fall through to
  the shape beneath and no button works), and a value widget's
  `fractionDigits: 0` is ignored (falsy), so whole-number values use 1.
- **R206 — Push-buttons pulse through a FUXA server script; pull-keys and
  e-stops toggle.** Every command the sample's blocks take is
  rising-edge-sensitive and scanned every 100 or 200 ms, and a FUXA button
  that writes `true` on mouse-down and `false` on mouse-up gives a pulse as
  long as the click (~100 ms), which a 200 ms scan misses about half the
  time. So the four push-buttons run the project's one server script,
  `pulse(tags)`: for each comma-separated tag id in turn, `$setTag(tag,
  true)`, hold 500 ms, `$setTag(tag, false)`. **Start line** pulses
  `SEQ_START.Reset` then `SEQ_START.Start`, because a sequencer that has
  completed or faulted ignores `Start` until reset, and takes one command per
  scan (`Sequencer.Scan` is an `else if` chain); **Stop line** does the same
  for `SEQ_STOP`; **Reset safety relays** pulses the three `SafetyReset`s;
  **Reset interlocks** pulses `INT_CV003`, `INT_CV002`, `INT_CV001`,
  `INT_FEED` `.Reset` downstream first. The spec's "interlock and safety
  resets" are these two plant-wide buttons. Pull-keys (`CVn.PullKey1`) and
  e-stops (`CVn.EStop`) latch, as the devices do: a toggle button, grey when
  restored and red when actuated.
- **R207 — What the screens show.** A belt is grey while its contactor's
  auxiliary contact (`CVn.Contactor`) is open and green while closed; a red
  overlay of the same shape is shown while its interlock is latched
  (`INT_CVn.Tripped`) — at power-up all three are red, as the sample's
  interlocks all trip on their permissives (sample README, "Power-up").
  Lamps: contactor, safety relay (`SafetyOk`), zero-speed switch (`Stopped`),
  the interlock's `Ok`, and for the feeder `Enabled` and `INT_FEED.Ok`.
  Alarms: one FUXA alarm per `ALM_CVn.Hi.Active` (high) and
  `ALM_CVn.HiHi.Active` (high-high) — so the sample's deadband and 3 s
  on-delay decide, not FUXA — and one per interlock's `Tripped` (high), all
  `ackactive`. Trends: two real-time charts (speeds; motor currents), no DAQ
  history (spec §2 excludes history). FUXA's own `ALM_CVn.Ack` is not
  wired: FUXA acknowledges its own alarms.
- **R208 — The stack.** Compose project `dse-hmi`; services `dse` (image
  `dse-hmi/dse:local` from `hmi/fuxa/Dockerfile`, context the repository
  root, `Dockerfile.dockerignore` sending only `Directory.Build.props`,
  `src/` and `samples/`), `fuxa` (`dse-hmi/fuxa:1.3.4-modbus` from
  `fuxa.Dockerfile`, port 1881, named volumes for `_appdata`, `_db`,
  `_logs`) and `fuxa-init` (pinned curl; waits up to 120 s for
  `/api/settings`, then **posts the project only if FUXA does not already
  hold one whose device address is `dse:5020`**, so edits made in FUXA's
  editor survive `docker compose up` — "edits stay in its volume until
  exported", spec criterion 7; `docker compose down -v` returns to the
  committed project). `dse` is published on 5020 too, so a local Modbus tool
  can read it. The smoke check is `hmi/fuxa/smoke-check.sh` (POSIX sh, curl
  and awk): it waits for FUXA to read `t_CV001.Speed`, runs the `pulse`
  script for **Start line** through `POST /api/runscript`, and waits for the
  speed to reach 1.74 m/s.
- **R209 — Docs.** `hmi/fuxa/README.md` (Task 5) quotes the register map as
  `dse modbus-map` prints it, and a test compares the two. The root README
  gains two lines in "Command line" and a section "A SCADA on the sample";
  `docs/architecture.md` a section "Modbus TCP" after "The real-time
  boundary"; `docs/scenarios.md` one line; `CHANGELOG.md`'s Unreleased entry
  gains Modbus, Scenarios, Cli and HMI sections (Task 6). No existing test
  pins the command list (`CommandLineTests.HelpPrintsGeneralHelpAndSucceeds`
  uses `Contains` and keeps passing with the wider help column); a new test
  pins the new lines.
- **R210 — The spec amendment.** The controller adds this note to the plan 8
  spec, under its title, with the plan commit (no task edits it):

  ```markdown
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
  ```

## Measurements

Prototype runs in a throwaway `git worktree` of `aca64d9` (removed after),
with this plan's changes applied; each stage built in Release with
`0 Warning(s)` and tested in full.

**Suite after each task** (stage-by-stage counts from the prototype):

| after | Io | Core | Comp | RT | Conf | Scen | Cli | Ctrl | Cat | Samples | Modbus | total |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| `aca64d9` | 37 | 498 | 189 | 57 | 239 | 167 | 78 | 153 | 27 | 136 (1 fails) | — | 1581 |
| Task 1 | 37 | 498 | 189 | 57 | 239 | 167 | 78 | 153 | 27 | 136 | — | 1581 |
| Task 2 | 37 | 498 | 189 | 57 | 239 | 167 | 78 | 153 | 27 | 136 | 29 | 1610 |
| Task 3 | 37 | 498 | 189 | 57 | 239 | 167 | 78 | 153 | 27 | 136 | 93 | 1674 |
| Task 4 | 37 | 498 | 189 | 57 | 239 | 170 | 106 | 153 | 27 | 137 | 93 | 1706 |
| Task 5 | 37 | 498 | 189 | 57 | 239 | 170 | 106 | 153 | 27 | 143 | 93 | 1712 |
| Task 6 | 37 | 498 | 189 | 57 | 239 | 170 | 106 | 153 | 27 | 144 | 93 | **1713** |

`Dse.Modbus.Tests` runs in under a second (Debug), and passed 120 runs of
the whole project under 8-way parallel load with no failure (before the
consistent first image of R197's read test, its read-consistency test failed
13 of about 200 such runs); `Dse.Samples.Tests` in
about 19 s, as before, of which `ServeTests` takes under 2 s. Four full-suite
runs in a row passed with no failure.

**End to end, `dse serve` in-process at `--speed 20`:** the coil write to
`SEQ_START.Start` (coil offset 30) is answered `05 00 1E FF 00`; CV001's
speed (input registers 6–7) reads 0.0004 m/s before and 1.876 m/s 0.5 s
after. Run by hand with `samples/mine-conveyors/scenarios/chute-blockage.json`
at `--speed 20`, `ALM_CV001.Hi.Active` (discrete input offset 1) reads true
4.9 s of wall time after the port opened — the golden's 06:01:53.4, 113.4 s
of simulation, divided by 20.

**The mine plant's map** (`dse modbus-map samples/mine-conveyors/plant.json`):
124 tags — 36 coils, 59 discrete inputs, 28 input-register values (56
registers), 1 holding-register value (`Feed.Rate`, 2 registers). Selected
addresses: `SEQ_START.Start` coil 31 (offset 30); `CV001.Permit` discrete
input 17, read-only, claimed by `INT_CV001`; `ALM_CV001.Hi.Active` discrete
input 2; `CV001.Current` input register 5; `CV001.Speed` input register 7;
`Feed.Rate` holding register 1.

**The FUXA stack** (Task 5's commands, run twice from a clean Docker state;
the second run with the digest-pinned `Dockerfile` exactly as Task 5 writes
it): `docker compose up -d --build` builds both images (about 1 min the first
time); `fuxa-init` logs `Loaded the mine-conveyors project into FUXA.`, and
on a second `docker compose up -d`
`FUXA already has the DSE project; leaving it as it is.`; `./smoke-check.sh`
prints `FUXA reads CV001.Speed = 0.0016007993835955858 m/s.`,
`Pressed Start line.`, `PASS: CV001.Speed = 1.8446321487426758 m/s after the start sequence.`
and exits 0; `docker compose stop dse` → `Stopped at 2026-03-02 06:00:34.860 after 3486 ticks.`;
`docker compose down -v --rmi all` removes the containers, the network, the
three volumes and the three images (`dse-hmi/dse:local`,
`dse-hmi/fuxa:1.3.4-modbus`, `curlimages/curl`). Docker's build cache is left
(about 1.5 GB; `docker builder prune` clears it).

**In the browser** (Playwright, 1500 × 900): the overview at power-up shows
three red belts, zeros, every interlock lamp red and the zero-speed lamps
amber; **Start line** clicked once brings all three belts green at 1.93, 1.94,
1.94 m/s, the feeder `Enabled` and every lamp green (20 s sufficed on a warm
stack; the independent check on a cold stack saw only CV003 running at 20 s
and all three by 45 s, and a re-run on a fresh stack saw "Start complete"
10.7 s after the click, so the plan waits for "Start complete" or 45 s; the
selectors `role=button[name="Alarms"]` and `role=button[name="Trends"]` work); **CV002
pull-key** clicked turns the button red, CV002 and CV001 red, the feeder
off, CV003 still green — the sample's scenario 2; pressing it again and
**Start line** restores the line; the Alarms view lists `INT_CV001 tripped`,
`INT_CV002 tripped`, `INT_FEED tripped` (High, Interlock, Passive) and the bell
counts them; the Trends view draws both charts live. Screenshots (scratch,
not committed): `overview-idle.png`, `overview-running.png`,
`overview-pull-key.png`, `alarms.png`, `trends.png`.

**Pinned images** (`docker buildx imagetools inspect`, 2026-10-07):

| image | digest |
|---|---|
| `frangoteam/fuxa:1.3.4` | `sha256:3778da3377e7685842495497d13157d1548ccad8708c434dc5d4b99fb5cb25bf` |
| `mcr.microsoft.com/dotnet/sdk:10.0.401-noble` | `sha256:e70cdb7f80b0348f5cb85f19a8f670fca061f033d57eed12fa003d58b0e06317` |
| `mcr.microsoft.com/dotnet/runtime:10.0.12-noble` | `sha256:b89586dc17781f25531909993658aa8161205ae38b8cec8847df4a8221a403d5` |
| `curlimages/curl:8.22.0` | `sha256:58adaa4e8dca9c988bae2aba4ab3434a0bb2da16bbe3f92dec39ec7785166777` |

npm `modbus-serial@8.0.19` installs as `added 3 packages` (its `serialport`
dependency is already in the FUXA image), with an `EBADENGINE` warning on the
image's Node 18 and no build step.

**Red runs** (the new tests against the code before each task's
implementation): Task 1 — `ReleaseTests.TheVersionTheChangelogAndTheReadmeAgree`
fails as at baseline. Tasks 2 and 3 — the test project does not compile
(`RegisterMap`, then `ModbusServer`, do not exist). Task 4 — the CLI and
Samples test projects do not compile (`CliApp.Run` has no `CancellationToken`
overload, `ScenarioRunner.Bind` does not exist). Task 5 — the six
`FuxaProjectTests` fail with `DirectoryNotFoundException`/`FileNotFoundException`
(`hmi/fuxa/` does not exist). Task 6 —
`TheRootReadmeTheArchitectureAndTheScenariosPageNameServeAndTheHmi` fails on
its first `Contains`.

## File structure

```
CHANGELOG.md                                      ## Unreleased (Task 1); its sections (Task 6)
tests/Dse.Samples.Tests/ReleaseTests.cs           accepts ## Unreleased (Task 1)
Dse.sln                                           + Dse.Modbus, Dse.Modbus.Tests (Task 2)
src/Dse.Modbus/Dse.Modbus.csproj                  new (Task 2)
src/Dse.Modbus/ModbusArea.cs                      new (Task 2)
src/Dse.Modbus/RegisterEntry.cs                   new (Task 2)
src/Dse.Modbus/RegisterMap.cs                     new (Task 2)
src/Dse.Modbus/RegisterCodec.cs                   new (Task 2)
tests/Dse.Modbus.Tests/Dse.Modbus.Tests.csproj    new (Task 2); + ModbusClient link (Task 3)
tests/Dse.Modbus.Tests/Fakes/Plant.cs             new (Task 2)
tests/Dse.Modbus.Tests/RegisterMapTests.cs        new, 29 tests (Task 2)
src/Dse.Modbus/ModbusExceptionCode.cs             new (Task 3)
src/Dse.Modbus/ModbusProtocol.cs                  new (Task 3)
src/Dse.Modbus/ModbusServer.cs                    new (Task 3)
tests/Shared/ModbusClient.cs                      new (Task 3)
tests/Dse.Modbus.Tests/Fakes/Rig.cs               new (Task 3)
tests/Dse.Modbus.Tests/FunctionCodeTests.cs       new, 50 tests (Task 3)
tests/Dse.Modbus.Tests/ServerTests.cs             new, 14 tests (Task 3)
src/Dse.Scenarios/ScenarioBinding.cs              new (Task 4)
src/Dse.Scenarios/ScenarioRunner.cs               Run = Bind + RunFor (Task 4)
tests/Dse.Scenarios.Tests/ScenarioBindingTests.cs new, 3 tests (Task 4)
src/Dse.Cli/Dse.Cli.csproj                        + Dse.Modbus (Task 4)
src/Dse.Cli/CliApp.cs                             CancellationToken overload; per-command option checks (Task 4)
src/Dse.Cli/CliContext.cs                         + Cancellation (Task 4)
src/Dse.Cli/CommandTable.cs                       serve, modbus-map, 4 options, exit-code line (Task 4)
src/Dse.Cli/ExitCodes.cs                          Unreadable's summary (Task 4)
src/Dse.Cli/Commands/Serve.cs                     new (Task 4)
src/Dse.Cli/Commands/ModbusMap.cs                 new (Task 4)
tests/Dse.Cli.Tests/Cli.cs                        Run(CancellationToken, …) (Task 4)
tests/Dse.Cli.Tests/ServeCommandTests.cs          new, 19 tests (Task 4)
tests/Dse.Cli.Tests/ModbusMapCommandTests.cs      new, 9 tests (Task 4)
tests/Dse.Samples.Tests/Dse.Samples.Tests.csproj  + ModbusClient link (Task 4)
tests/Dse.Samples.Tests/ServeTests.cs             new, 1 test (Task 4)
hmi/fuxa/Dockerfile                               new (Task 5)
hmi/fuxa/Dockerfile.dockerignore                  new (Task 5)
hmi/fuxa/fuxa.Dockerfile                          new (Task 5)
hmi/fuxa/docker-compose.yml                       new (Task 5)
hmi/fuxa/smoke-check.sh                           new, executable (Task 5)
hmi/fuxa/generate-project.py                      new: the FUXA project's generator (Task 5)
hmi/fuxa/mine-conveyors.fuxap.json                generated by it (Task 5)
hmi/fuxa/README.md                                new (Task 5)
tests/Dse.Samples.Tests/FuxaProjectTests.cs       new, 6 tests (Task 5); + 1 (Task 6)
README.md                                         Command line, A SCADA on the sample (Task 6)
docs/architecture.md                              ## Modbus TCP (Task 6)
docs/scenarios.md                                 one line (Task 6)
```

The plan 8 spec is amended with this plan, before execution (R210); no task
edits it.

## Task map

| # | Task | Implementer | Reviewer | Tests after |
|---|---|---|---|---|
| 1 | The changelog's Unreleased entry; `ReleaseTests` | sonnet | sonnet | 1581 (all passing) |
| 2 | `Dse.Modbus`: the register map and encodings | sonnet | sonnet | 1610 |
| 3 | `Dse.Modbus`: the Modbus TCP server | sonnet | **opus** (protocol, framing, concurrency, shutdown: R197, R198, Review Focus 1–4) | 1674 |
| 4 | `dse serve`, `dse modbus-map`, `ScenarioRunner.Bind`, the end-to-end test | sonnet | **opus** (threading, signals, cancellation: R199, R200, R202) | 1706 |
| 5 | The FUXA stack: Docker, compose, the generated project, smoke check, HMI README, in-sync tests | **opus** (needs Docker and the browser tools) | **opus** (R204–R208, Review Focus 5) | 1712 |
| 6 | Docs: root README, architecture, scenarios, changelog | sonnet | sonnet | **1713** |

Every task's brief contains its complete content. Tasks are sequential:
Task 3 builds on Task 2's map, Task 4 on Task 3's server, Task 5 on Task 4's
`modbus-map`, and Task 6's changelog describes Tasks 2–5. The whole-branch
review at the end is Opus.

---

### Task 1: The changelog's Unreleased entry

**Model:** implementer sonnet; reviewer sonnet.

**Files:**
- Modify: `CHANGELOG.md` (an `## Unreleased` section above `## 1.0.0 — 2026-10-07`)
- Modify: `tests/Dse.Samples.Tests/ReleaseTests.cs` (one assertion becomes three lines)

**Interfaces:**
- Consumes: `ReleaseTests`, the plan 8 spec and this plan (both on `master`
  from the plan commit).
- Produces: the `## Unreleased` section Task 6 fills in.

- [ ] **Step 1: See the baseline failure**

Run: `dotnet test tests/Dse.Samples.Tests --nologo --filter "FullyQualifiedName~ReleaseTests"`
Expected: `Failed: 1, Passed: 1, Total: 2` —
`TheVersionTheChangelogAndTheReadmeAgree`, `Assert.All() Failure` with
`Not found: "docs/superpowers/specs/2026-10-07-modbus-fuxa-hmi-"···` (and the
plan file). This is `master`'s state since `aca64d9` (R194).

- [ ] **Step 2: Let the test accept an Unreleased section**

In `tests/Dse.Samples.Tests/ReleaseTests.cs`, replace

```csharp
        Assert.Single(changelog.Split('\n'), l => l.StartsWith("## ", StringComparison.Ordinal));
```

with

```csharp
        string[] headings = changelog.Split('\n').Where(l => l.StartsWith("## ", StringComparison.Ordinal)).ToArray();
        string[] expected = headings.Contains("## Unreleased") ? ["## Unreleased", headings[^1]] : [headings[^1]];
        Assert.Equal(expected, headings);
        Assert.Matches(ReleaseHeading(), headings[^1]);
```

(The line occurs once. The headings are an optional `## Unreleased` first,
then the one release heading last; the `Assert.Single(ReleaseHeading()…)`
line above it stays.)

- [ ] **Step 3: Add the Unreleased section**

In `CHANGELOG.md`, replace

```markdown
## 1.0.0 — 2026-10-07
```

with

```markdown
## Unreleased

DSE's first external consumer: a SCADA watching and operating a simulated
plant over Modbus TCP
([design](docs/superpowers/specs/2026-10-07-modbus-fuxa-hmi-design.md),
[plan 8](docs/superpowers/plans/2026-10-07-modbus-fuxa-hmi.md)).

## 1.0.0 — 2026-10-07
```

(The heading occurs once. Task 6 adds the section's `###` parts after the
paragraph.)

- [ ] **Step 4: Run the tests and everything**

Run: `dotnet test tests/Dse.Samples.Tests --nologo --filter "FullyQualifiedName~ReleaseTests"` — expect `Passed: 2, Total: 2`.
Run: `dotnet build Dse.sln -c Release --nologo` — expect `0 Warning(s)`, `0 Error(s)`.
Run: `dotnet test Dse.sln --nologo` — expect **1581**, all passing: 37 / 498 / 189 / 57 / 239 / 167 / 78 / 153 / 27 / 136.
Run: `git status --short -uall` — expect exactly the two paths of Step 5.

- [ ] **Step 5: Commit**

```bash
git add CHANGELOG.md tests/Dse.Samples.Tests/ReleaseTests.cs
git commit -F .superpowers/sdd/8/msg-task1.txt
```

with `.superpowers/sdd/8/msg-task1.txt`:

```
docs: open the changelog's Unreleased entry for plan 8

ReleaseTests requires the changelog to link every spec and plan, and the
plan 8 spec and plan were not linked, so master was red. An Unreleased
section above 1.0.0 now links both, and the test accepts that heading
before the release heading.

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
```

Then `git log -1 --format='%h%n%s%n--%n%b'`.

---

### Task 2: The register map

**Model:** implementer sonnet; reviewer sonnet.

**Files:**
- Create: `src/Dse.Modbus/Dse.Modbus.csproj`, `ModbusArea.cs`, `RegisterEntry.cs`, `RegisterMap.cs`, `RegisterCodec.cs`
- Create: `tests/Dse.Modbus.Tests/Dse.Modbus.Tests.csproj`, `Fakes/Plant.cs`, `RegisterMapTests.cs`
- Modify: `Dse.sln` (two projects, by `dotnet sln add`)

**Interfaces:**
- Consumes: `Dse.Io` — `ITagDirectory`, `TagDescriptor` (`Index`, `Name`,
  `Kind`, `Access`, `Unit`, `RangeLow`, `RangeHigh`, `HasRange`,
  `Description`, `ClaimedBy`), `TagKind`, `TagAccess`, `TagValue`.
- Produces: `ModbusArea { Coils, DiscreteInputs, InputRegisters, HoldingRegisters }`;
  `RegisterEntry(ModbusArea Area, int Offset, TagDescriptor Tag)` with
  `Width`, `Number`, `IsBit`, `IsWritable`, `DataType`;
  `RegisterMap.Build(ITagDirectory)`, `.Directory`, `.Entries`,
  `.Size(area)`, `.At(area, offset)`, `.TryFind(name, out entry)`,
  `.Find(name)`, static `.AreaOf(tag)`, `.Name(area)`, `AreaCapacity`;
  `RegisterCodec.Float32(double)`, `.Int32(long)`, `.ToFloat32(hi, lo)`,
  `.ToInt32(hi, lo)`, `.Encode(TagKind, TagValue)`. Tasks 3 and 4 use them.

- [ ] **Step 1: Create the test project and its fake plant**

Create `tests/Dse.Modbus.Tests/Dse.Modbus.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="coverlet.collector" Version="6.0.4" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.4" />
  </ItemGroup>

  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\Dse.Modbus\Dse.Modbus.csproj" />
  </ItemGroup>

</Project>
```

(The same packages and versions as every test project; Task 3 adds the shared
client link.)

Create `tests/Dse.Modbus.Tests/Fakes/Plant.cs`:

```csharp
using Dse.Io;

namespace Dse.Modbus.Tests.Fakes;

/// <summary>
/// A nine-tag directory with every kind and access, a claimed tag among them,
/// listed in the order a plant's directory would sort them, and the map it
/// gives:
/// coils A.Run 0, B.Stop 1; discrete inputs A.Permit 0, A.Running 1;
/// input registers A.Count 0–1, A.Speed 2–3;
/// holding registers A.Batch 0–1, A.Setpoint 2–3, B.Bias 4–5.
/// </summary>
public static class Plant
{
    public static ArrayDirectory Directory() => new(
        new TagDescriptor(0, "A.Batch", TagKind.Int64, TagAccess.ReadWrite, "count", double.NaN, double.NaN, "Batch number"),
        new TagDescriptor(1, "A.Count", TagKind.Int64, TagAccess.ReadOnly, "count", double.NaN, double.NaN, "Items counted"),
        new TagDescriptor(2, "A.Permit", TagKind.Bool, TagAccess.ReadOnly, "", double.NaN, double.NaN, "Run permit") { ClaimedBy = "INT_A" },
        new TagDescriptor(3, "A.Run", TagKind.Bool, TagAccess.ReadWrite, "", double.NaN, double.NaN, "Run command"),
        new TagDescriptor(4, "A.Running", TagKind.Bool, TagAccess.ReadOnly, "", double.NaN, double.NaN, "Running"),
        new TagDescriptor(5, "A.Setpoint", TagKind.Double, TagAccess.ReadWrite, "m/s", 0.0, 2.0, "Speed setpoint"),
        new TagDescriptor(6, "A.Speed", TagKind.Double, TagAccess.ReadOnly, "m/s", 0.0, 2.5, "Belt speed"),
        new TagDescriptor(7, "B.Bias", TagKind.Double, TagAccess.ReadWrite, "kg/s", double.NaN, double.NaN, "Unranged bias"),
        new TagDescriptor(8, "B.Stop", TagKind.Bool, TagAccess.ReadWrite, "", double.NaN, double.NaN, "Stop command"));

    /// <summary>An image in which every tag holds a recognisable value.</summary>
    public static TagValue[] Image() =>
    [
        TagValue.Int64(-7L),            // A.Batch
        TagValue.Int64(123_456L),       // A.Count
        TagValue.Bool(true),            // A.Permit
        TagValue.Bool(true),            // A.Run
        TagValue.Bool(false),           // A.Running
        TagValue.Double(1.5),           // A.Setpoint
        TagValue.Double(1.95),          // A.Speed
        TagValue.Double(-0.25),         // B.Bias
        TagValue.Bool(true),            // B.Stop
    ];
}

/// <summary>An <see cref="ITagDirectory"/> over an array, in the given order.</summary>
public sealed class ArrayDirectory : ITagDirectory
{
    private readonly TagDescriptor[] _tags;
    private readonly Dictionary<string, int> _byName = new(StringComparer.Ordinal);

    public ArrayDirectory(params TagDescriptor[] tags)
    {
        _tags = tags;
        for (int i = 0; i < tags.Length; i++)
        {
            _byName[tags[i].Name] = i;
        }
    }

    public int Count => _tags.Length;

    public IReadOnlyList<TagDescriptor> Tags => _tags;

    public TagDescriptor this[int index] => _tags[index];

    public bool TryFind(string name, out TagDescriptor descriptor)
    {
        if (_byName.TryGetValue(name, out int index))
        {
            descriptor = _tags[index];
            return true;
        }

        descriptor = null!;
        return false;
    }

    public TagDescriptor Find(string name) =>
        TryFind(name, out TagDescriptor d) ? d : throw new KeyNotFoundException($"No tag '{name}'.");
}

/// <summary>An <see cref="ITagWriter"/> that remembers every write, safe to share between connections.</summary>
public sealed class RecordingWriter(ITagDirectory directory) : ITagWriter
{
    private readonly Lock _sync = new();
    private readonly List<(string Tag, TagValue Value)> _writes = [];

    public ITagDirectory Directory { get; } = directory;

    /// <summary>A copy of the writes so far, in arrival order, by tag name.</summary>
    public (string Tag, TagValue Value)[] Writes
    {
        get
        {
            lock (_sync)
            {
                return [.. _writes];
            }
        }
    }

    public void Write(int index, TagValue value)
    {
        lock (_sync)
        {
            _writes.Add((Directory[index].Name, value));
        }
    }

    public void Write(string name, TagValue value) => Write(Directory.Find(name).Index, value);
}
```

- [ ] **Step 2: Write the failing map and codec tests**

Create `tests/Dse.Modbus.Tests/RegisterMapTests.cs`:

```csharp
using Dse.Io;
using Dse.Modbus.Tests.Fakes;

namespace Dse.Modbus.Tests;

public class RegisterMapTests
{
    [Theory]
    [InlineData("A.Run", ModbusArea.Coils, 0, "Bool")]
    [InlineData("B.Stop", ModbusArea.Coils, 1, "Bool")]
    [InlineData("A.Permit", ModbusArea.DiscreteInputs, 0, "Bool")]
    [InlineData("A.Running", ModbusArea.DiscreteInputs, 1, "Bool")]
    [InlineData("A.Count", ModbusArea.InputRegisters, 0, "Int32")]
    [InlineData("A.Speed", ModbusArea.InputRegisters, 2, "Float32")]
    [InlineData("A.Batch", ModbusArea.HoldingRegisters, 0, "Int32")]
    [InlineData("A.Setpoint", ModbusArea.HoldingRegisters, 2, "Float32")]
    [InlineData("B.Bias", ModbusArea.HoldingRegisters, 4, "Float32")]
    public void EveryTagGetsTheAreaItsKindAndAccessGiveInDirectoryOrderFromZero(string tag, ModbusArea area, int offset, string type)
    {
        RegisterEntry entry = RegisterMap.Build(Plant.Directory()).Find(tag);

        Assert.Equal((area, offset, offset + 1, type), (entry.Area, entry.Offset, entry.Number, entry.DataType));
    }

    [Fact]
    public void EachAreaIsPackedWithNoGapsAndTwoRegistersPerValue()
    {
        RegisterMap map = RegisterMap.Build(Plant.Directory());

        Assert.Equal((2, 2, 4, 6), (map.Size(ModbusArea.Coils), map.Size(ModbusArea.DiscreteInputs), map.Size(ModbusArea.InputRegisters), map.Size(ModbusArea.HoldingRegisters)));
        Assert.Same(map.Find("A.Setpoint"), map.At(ModbusArea.HoldingRegisters, 2));
        Assert.Same(map.Find("A.Setpoint"), map.At(ModbusArea.HoldingRegisters, 3));
        Assert.Null(map.At(ModbusArea.HoldingRegisters, 6));
        Assert.Null(map.At(ModbusArea.Coils, -1));
        Assert.Equal(Plant.Directory().Tags.Select(t => t.Name), map.Entries.Select(e => e.Tag.Name));
    }

    [Fact]
    public void TheSameDirectoryAlwaysGivesTheSameMap()
    {
        RegisterMap first = RegisterMap.Build(Plant.Directory());
        RegisterMap second = RegisterMap.Build(Plant.Directory());

        Assert.Equal(
            first.Entries.Select(e => (e.Tag.Name, e.Area, e.Offset)),
            second.Entries.Select(e => (e.Tag.Name, e.Area, e.Offset)));
    }

    [Fact]
    public void AClaimedTagMapsToAReadOnlyArea()
    {
        RegisterEntry permit = RegisterMap.Build(Plant.Directory()).Find("A.Permit");

        Assert.Equal("INT_A", permit.Tag.ClaimedBy);
        Assert.Equal(ModbusArea.DiscreteInputs, permit.Area);
        Assert.False(permit.IsWritable);
    }

    [Fact]
    public void APlantTooLargeForOneAreaIsRefusedByName()
    {
        TagDescriptor[] tags = Enumerable.Range(0, 32_769)
            .Select(i => new TagDescriptor(i, $"T{i:D5}", TagKind.Double, TagAccess.ReadWrite, "", double.NaN, double.NaN, ""))
            .ToArray();

        ArgumentException refused = Assert.Throws<ArgumentException>(() => RegisterMap.Build(new ArrayDirectory(tags)));
        Assert.StartsWith("The plant's holding registers need more than 65536 addresses", refused.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1.0, 0x3F80, 0x0000)]
    [InlineData(-2.5, 0xC020, 0x0000)]
    [InlineData(1.95, 0x3FF9, 0x999A)]
    [InlineData(0.0, 0x0000, 0x0000)]
    [InlineData(1e39, 0x7F80, 0x0000)]
    [InlineData(-1e39, 0xFF80, 0x0000)]
    public void ADoubleIsABigEndianSingleHighWordFirst(double value, int high, int low)
    {
        Assert.Equal(((ushort)high, (ushort)low), RegisterCodec.Float32(value));
        Assert.Equal((float)value, (float)RegisterCodec.ToFloat32((ushort)high, (ushort)low));
    }

    [Theory]
    [InlineData(5L, 0x0000, 0x0005)]
    [InlineData(-1L, 0xFFFF, 0xFFFF)]
    [InlineData(70_000L, 0x0001, 0x1170)]
    [InlineData(2_147_483_647L, 0x7FFF, 0xFFFF)]
    [InlineData(2_147_483_648L, 0x7FFF, 0xFFFF)]
    [InlineData(long.MaxValue, 0x7FFF, 0xFFFF)]
    [InlineData(-2_147_483_648L, 0x8000, 0x0000)]
    [InlineData(-3_000_000_000L, 0x8000, 0x0000)]
    [InlineData(long.MinValue, 0x8000, 0x0000)]
    public void AnInt64IsABigEndianInt32ThatSaturates(long value, int high, int low)
    {
        Assert.Equal(((ushort)high, (ushort)low), RegisterCodec.Int32(value));
    }

    [Fact]
    public void AValueOfAnotherKindEncodesAsZeroRatherThanThrowing()
    {
        Assert.Equal(((ushort)0, (ushort)0), RegisterCodec.Encode(TagKind.Double, default));
        Assert.Equal(((ushort)0, (ushort)0), RegisterCodec.Encode(TagKind.Int64, TagValue.Double(3.0)));
        Assert.Equal(((ushort)0x4040, (ushort)0), RegisterCodec.Encode(TagKind.Double, TagValue.Double(3.0)));
    }
}
```

(29 tests: 9 + 1 + 1 + 1 + 1 + 6 + 9 + 1. The fake directory is listed in
ordinal-name order, as `TagDirectory` sorts a real one; its map is in the
summary of `Plant`.)

Run: `dotnet build tests/Dse.Modbus.Tests --nologo` — expect it to fail: the
referenced `src/Dse.Modbus/Dse.Modbus.csproj` does not exist yet.

- [ ] **Step 3: Create the project**

Create `src/Dse.Modbus/Dse.Modbus.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\Dse.Io.Abstractions\Dse.Io.Abstractions.csproj" />
    <ProjectReference Include="..\Dse.Realtime\Dse.Realtime.csproj" />
  </ItemGroup>

  <ItemGroup>
    <InternalsVisibleTo Include="Dse.Modbus.Tests" />
  </ItemGroup>

</Project>
```

Create `src/Dse.Modbus/ModbusArea.cs`:

```csharp
namespace Dse.Modbus;

/// <summary>The four Modbus data areas, in the order a SCADA numbers them (0xxxxx, 1xxxxx, 3xxxxx, 4xxxxx).</summary>
public enum ModbusArea
{
    /// <summary>Read-write bits: a read-write Bool tag. Read with FC1, written with FC5 and FC15.</summary>
    Coils,

    /// <summary>Read-only bits: a read-only Bool tag. Read with FC2.</summary>
    DiscreteInputs,

    /// <summary>Read-only 16-bit registers: a read-only Double or Int64 tag, two registers each. Read with FC4.</summary>
    InputRegisters,

    /// <summary>Read-write 16-bit registers: a read-write Double or Int64 tag, two registers each. Read with FC3, written with FC16.</summary>
    HoldingRegisters,
}
```

Create `src/Dse.Modbus/RegisterEntry.cs`:

```csharp
using Dse.Io;

namespace Dse.Modbus;

/// <summary>
/// Where one tag lives on the wire: its area, its 0-based offset (the address
/// a request carries) and how many bits or registers it takes.
/// </summary>
/// <param name="Area">The data area.</param>
/// <param name="Offset">The 0-based wire offset of the tag's first bit or register.</param>
/// <param name="Tag">The tag.</param>
public sealed record RegisterEntry(ModbusArea Area, int Offset, TagDescriptor Tag)
{
    /// <summary>One bit, or two registers.</summary>
    public int Width => IsBit ? 1 : 2;

    /// <summary>The 1-based address a SCADA shows: <see cref="Offset"/> + 1.</summary>
    public int Number => Offset + 1;

    /// <summary>True for coils and discrete inputs.</summary>
    public bool IsBit => Area is ModbusArea.Coils or ModbusArea.DiscreteInputs;

    /// <summary>True for coils and holding registers, the areas a master may write.</summary>
    public bool IsWritable => Area is ModbusArea.Coils or ModbusArea.HoldingRegisters;

    /// <summary>How the value is encoded: <c>Bool</c>, <c>Float32</c> (big-endian) or <c>Int32</c> (big-endian, saturating).</summary>
    public string DataType => Tag.Kind switch
    {
        TagKind.Bool => "Bool",
        TagKind.Double => "Float32",
        _ => "Int32",
    };
}
```

Create `src/Dse.Modbus/RegisterMap.cs`:

```csharp
using Dse.Io;

namespace Dse.Modbus;

/// <summary>
/// A plant's Modbus register map (plan 8 spec §1, criterion 1). Built from the
/// tag directory, in directory order: a Bool tag is one bit — a coil when it
/// is read-write, a discrete input when it is read-only; a Double or an Int64
/// tag is two registers — holding when read-write, input when read-only.
/// Within each area addresses are assigned from 0 with no gaps. Access is the
/// directory's published access, so a tag a block claims maps read-only. The
/// same directory always gives the same map.
/// </summary>
public sealed class RegisterMap
{
    /// <summary>The 0-based offsets a Modbus request can address in one area: 0 to 65535.</summary>
    public const int AreaCapacity = 65536;

    private readonly RegisterEntry[] _entries;
    private readonly RegisterEntry?[][] _byAddress;
    private readonly Dictionary<string, RegisterEntry> _byName = new(StringComparer.Ordinal);

    private RegisterMap(ITagDirectory directory, RegisterEntry[] entries, RegisterEntry?[][] byAddress)
    {
        Directory = directory;
        _entries = entries;
        _byAddress = byAddress;
        foreach (RegisterEntry entry in entries)
        {
            _byName.Add(entry.Tag.Name, entry);
        }
    }

    /// <summary>The directory the map was built from.</summary>
    public ITagDirectory Directory { get; }

    /// <summary>Every tag's entry, in directory order.</summary>
    public IReadOnlyList<RegisterEntry> Entries => _entries;

    /// <summary>Builds the map. Throws <see cref="ArgumentException"/> when an area would need more than 65 536 addresses.</summary>
    public static RegisterMap Build(ITagDirectory directory)
    {
        ArgumentNullException.ThrowIfNull(directory);

        int[] next = new int[4];
        var entries = new RegisterEntry[directory.Count];
        for (int i = 0; i < directory.Count; i++)
        {
            TagDescriptor tag = directory[i];
            ModbusArea area = AreaOf(tag);
            entries[i] = new RegisterEntry(area, next[(int)area], tag);
            next[(int)area] += entries[i].Width;
            if (next[(int)area] > AreaCapacity)
            {
                throw new ArgumentException(
                    $"The plant's {Name(area)} need more than {AreaCapacity} addresses; a Modbus area holds no more.",
                    nameof(directory));
            }
        }

        var byAddress = new RegisterEntry?[4][];
        for (int a = 0; a < 4; a++)
        {
            byAddress[a] = new RegisterEntry?[next[a]];
        }

        foreach (RegisterEntry entry in entries)
        {
            for (int w = 0; w < entry.Width; w++)
            {
                byAddress[(int)entry.Area][entry.Offset + w] = entry;
            }
        }

        return new RegisterMap(directory, entries, byAddress);
    }

    /// <summary>The area a tag maps to.</summary>
    public static ModbusArea AreaOf(TagDescriptor tag)
    {
        ArgumentNullException.ThrowIfNull(tag);
        bool writable = tag.Access == TagAccess.ReadWrite;
        return tag.Kind == TagKind.Bool
            ? writable ? ModbusArea.Coils : ModbusArea.DiscreteInputs
            : writable ? ModbusArea.HoldingRegisters : ModbusArea.InputRegisters;
    }

    /// <summary>The plural name of an area, as the text map and messages print it: <c>coils</c>, <c>discrete inputs</c>, <c>input registers</c>, <c>holding registers</c>.</summary>
    public static string Name(ModbusArea area) => area switch
    {
        ModbusArea.Coils => "coils",
        ModbusArea.DiscreteInputs => "discrete inputs",
        ModbusArea.InputRegisters => "input registers",
        _ => "holding registers",
    };

    /// <summary>How many bits or registers of <paramref name="area"/> the plant occupies; every offset below it is mapped.</summary>
    public int Size(ModbusArea area) => _byAddress[(int)area].Length;

    /// <summary>The entry occupying a 0-based offset, or null past the end of the area.</summary>
    public RegisterEntry? At(ModbusArea area, int offset)
    {
        RegisterEntry?[] addresses = _byAddress[(int)area];
        return offset >= 0 && offset < addresses.Length ? addresses[offset] : null;
    }

    /// <summary>A tag's entry by name.</summary>
    public bool TryFind(string name, out RegisterEntry entry)
    {
        ArgumentNullException.ThrowIfNull(name);
        return _byName.TryGetValue(name, out entry!);
    }

    /// <summary>A tag's entry by name; throws <see cref="KeyNotFoundException"/> if the plant has no such tag.</summary>
    public RegisterEntry Find(string name) =>
        TryFind(name, out RegisterEntry entry) ? entry : throw new KeyNotFoundException($"No tag '{name}' in the register map.");
}
```

Create `src/Dse.Modbus/RegisterCodec.cs`:

```csharp
using Dse.Io;

namespace Dse.Modbus;

/// <summary>
/// The two-register encodings (spec criterion 1): a Double as an IEEE 754
/// single, an Int64 as a two's-complement Int32 that saturates at
/// <see cref="int.MinValue"/> and <see cref="int.MaxValue"/>. Both are
/// big-endian: the high word is the first register, the high byte of each
/// register is sent first (FUXA's <c>Float32</c> and <c>Int32</c>, a
/// "ABCD" order).
/// </summary>
public static class RegisterCodec
{
    /// <summary>A double narrowed to a single; a value beyond the single's range becomes an infinity, NaN stays NaN.</summary>
    public static (ushort High, ushort Low) Float32(double value)
    {
        uint bits = BitConverter.SingleToUInt32Bits((float)value);
        return ((ushort)(bits >> 16), (ushort)bits);
    }

    /// <summary>A long clamped to the Int32 range.</summary>
    public static (ushort High, ushort Low) Int32(long value)
    {
        uint bits = unchecked((uint)(int)Math.Clamp(value, int.MinValue, int.MaxValue));
        return ((ushort)(bits >> 16), (ushort)bits);
    }

    /// <summary>The single two registers carry, widened to a double.</summary>
    public static double ToFloat32(ushort high, ushort low) =>
        BitConverter.UInt32BitsToSingle(((uint)high << 16) | low);

    /// <summary>The Int32 two registers carry, widened to a long.</summary>
    public static long ToInt32(ushort high, ushort low) =>
        unchecked((int)(((uint)high << 16) | low));

    /// <summary>
    /// The two registers of a value of <paramref name="kind"/>. A value of
    /// another kind — an image not yet primed holds <c>default</c>, a Bool —
    /// encodes as zero rather than throwing.
    /// </summary>
    public static (ushort High, ushort Low) Encode(TagKind kind, TagValue value)
    {
        if (value.Kind != kind)
        {
            return (0, 0);
        }

        return kind switch
        {
            TagKind.Double => Float32(value.AsDouble),
            TagKind.Int64 => Int32(value.AsInt64),
            _ => (0, 0),
        };
    }
}
```

- [ ] **Step 4: Add both projects to the solution**

Run: `dotnet sln Dse.sln add src/Dse.Modbus/Dse.Modbus.csproj --solution-folder src`
Expected: `Project `src/Dse.Modbus/Dse.Modbus.csproj` added to the solution.`
Run: `dotnet sln Dse.sln add tests/Dse.Modbus.Tests/Dse.Modbus.Tests.csproj --solution-folder tests`
Expected: `Project `tests/Dse.Modbus.Tests/Dse.Modbus.Tests.csproj` added to the solution.`

(Measured: each adds a `Project(…)` entry, twelve configuration lines and a
`NestedProjects` line under the existing `src` or `tests` folder; the GUIDs
are random and need not match anyone's.)

- [ ] **Step 5: Run the tests and everything**

Run: `dotnet test tests/Dse.Modbus.Tests --nologo` — expect PASS, **29**.
Run: `dotnet build Dse.sln -c Release --nologo` — expect `0 Warning(s)`, `0 Error(s)`.
Run: `git grep -n PackageReference -- 'src/*.csproj'` — expect nothing.
Run: `dotnet test Dse.sln --nologo` — expect **1610**: the Task 1 counts plus `Dse.Modbus.Tests` 29.
Run: `git status --short -uall` — expect exactly the paths of Step 6.

- [ ] **Step 6: Commit**

```bash
git add Dse.sln src/Dse.Modbus/Dse.Modbus.csproj src/Dse.Modbus/ModbusArea.cs src/Dse.Modbus/RegisterEntry.cs src/Dse.Modbus/RegisterMap.cs src/Dse.Modbus/RegisterCodec.cs tests/Dse.Modbus.Tests/Dse.Modbus.Tests.csproj tests/Dse.Modbus.Tests/Fakes/Plant.cs tests/Dse.Modbus.Tests/RegisterMapTests.cs
git commit -F .superpowers/sdd/8/msg-task2.txt
```

with `.superpowers/sdd/8/msg-task2.txt`:

```
feat(modbus): map a plant's tags to Modbus registers

Dse.Modbus builds a register map from a tag directory, in directory
order: a read-write Bool is a coil, a read-only one a discrete input; a
Double is a big-endian Float32 and an Int64 a saturating big-endian
Int32, two holding registers when read-write and two input registers when
read-only. A claimed tag is read-only. The project references
Dse.Io.Abstractions and Dse.Realtime and no package.

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
```

Then `git log -1 --format='%h%n%s%n--%n%b'`.

---

### Task 3: The Modbus TCP server

**Model:** implementer sonnet; reviewer **opus** (R197, R198; Review Focus 1–4).

**Files:**
- Create: `src/Dse.Modbus/ModbusExceptionCode.cs`, `ModbusProtocol.cs`, `ModbusServer.cs`
- Create: `tests/Shared/ModbusClient.cs`
- Create: `tests/Dse.Modbus.Tests/Fakes/Rig.cs`, `FunctionCodeTests.cs`, `ServerTests.cs`
- Modify: `tests/Dse.Modbus.Tests/Dse.Modbus.Tests.csproj` (link the shared client)

**Interfaces:**
- Consumes: Task 2's `RegisterMap`, `RegisterEntry`, `RegisterCodec`;
  `Dse.Realtime.CommandBus` (`Write(string, TagValue)`, `WriteBool`,
  `Accepted`, `Rejected`; it refuses an unknown tag, a read-only tag, a kind
  mismatch and a double out of range or not finite, and never throws).
- Produces: `ModbusServer(RegisterMap, Func<ReadOnlyMemory<TagValue>>, CommandBus)`
  with `Start(IPEndPoint) → IPEndPoint` (throws `SocketException` on a port
  in use), `LocalEndPoint`, `OpenConnections`, `Requests`, `Map`,
  `DisposeAsync()`; `ModbusExceptionCode`; internal `ModbusProtocol`. The
  test-side `Dse.Tests.Shared.ModbusClient` (Task 4's end-to-end test uses it).

- [ ] **Step 1: Write the shared test client**

Create `tests/Shared/ModbusClient.cs`:

```csharp
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;

namespace Dse.Tests.Shared;

/// <summary>
/// A minimal, blocking Modbus TCP master for tests: it frames a PDU in an
/// MBAP header, sends it, and reads one response. Nothing here knows the
/// server's code; it speaks the wire protocol only. Every read times out
/// after five seconds, so a server that never answers fails a test instead
/// of hanging it.
/// </summary>
internal sealed class ModbusClient : IDisposable
{
    private readonly TcpClient _tcp;
    private readonly NetworkStream _stream;
    private ushort _transaction;

    public ModbusClient(IPEndPoint endpoint)
    {
        _tcp = new TcpClient { NoDelay = true, ReceiveTimeout = 5000, SendTimeout = 5000 };
        _tcp.Connect(endpoint);
        _stream = _tcp.GetStream();
    }

    /// <summary>The unit id sent with every request.</summary>
    public byte Unit { get; set; } = 1;

    /// <summary>An MBAP header (transaction, protocol 0, length, unit) followed by the PDU.</summary>
    public static byte[] Frame(ushort transaction, byte unit, params byte[] pdu)
    {
        byte[] frame = new byte[7 + pdu.Length];
        BinaryPrimitives.WriteUInt16BigEndian(frame, transaction);
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(4), (ushort)(pdu.Length + 1));
        frame[6] = unit;
        pdu.CopyTo(frame, 7);
        return frame;
    }

    /// <summary>A PDU: the function code, then each value as a big-endian word.</summary>
    public static byte[] Pdu(byte function, params ushort[] words)
    {
        byte[] pdu = new byte[1 + (2 * words.Length)];
        pdu[0] = function;
        for (int i = 0; i < words.Length; i++)
        {
            BinaryPrimitives.WriteUInt16BigEndian(pdu.AsSpan(1 + (2 * i)), words[i]);
        }

        return pdu;
    }

    /// <summary>Writes raw bytes, framed or not.</summary>
    public void Send(ReadOnlySpan<byte> bytes) => _stream.Write(bytes);

    /// <summary>Reads one response frame.</summary>
    public (ushort Transaction, byte Unit, byte[] Pdu) Receive()
    {
        byte[] header = new byte[7];
        _stream.ReadExactly(header);
        ushort length = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(4));
        byte[] pdu = new byte[length - 1];
        _stream.ReadExactly(pdu);
        return (BinaryPrimitives.ReadUInt16BigEndian(header), header[6], pdu);
    }

    /// <summary>True when the server has closed the connection: a read returns end of stream.</summary>
    public bool IsClosedByServer()
    {
        try
        {
            return _stream.Read(new byte[1]) == 0;
        }
        catch (IOException ex) when (ex.InnerException is SocketException { SocketErrorCode: SocketError.TimedOut })
        {
            return false;
        }
        catch (IOException)
        {
            return true;
        }
    }

    /// <summary>Sends a PDU under the next transaction id and returns the response PDU, checking the echo.</summary>
    public byte[] Request(byte[] pdu)
    {
        ushort transaction = ++_transaction;
        Send(Frame(transaction, Unit, pdu));
        (ushort echoed, byte unit, byte[] response) = Receive();
        if (echoed != transaction || unit != Unit)
        {
            throw new InvalidDataException($"Sent transaction {transaction} unit {Unit}; the response carries {echoed} unit {unit}.");
        }

        return response;
    }

    public bool[] ReadCoils(int start, int count) => Bits(Expect(Request(Pdu(0x01, (ushort)start, (ushort)count)), 0x01), count);

    public bool[] ReadDiscreteInputs(int start, int count) => Bits(Expect(Request(Pdu(0x02, (ushort)start, (ushort)count)), 0x02), count);

    public ushort[] ReadHoldingRegisters(int start, int count) => Words(Expect(Request(Pdu(0x03, (ushort)start, (ushort)count)), 0x03));

    public ushort[] ReadInputRegisters(int start, int count) => Words(Expect(Request(Pdu(0x04, (ushort)start, (ushort)count)), 0x04));

    /// <summary>FC5: 0xFF00 for on, 0x0000 for off.</summary>
    public void WriteCoil(int address, bool on) => Expect(Request(Pdu(0x05, (ushort)address, on ? (ushort)0xFF00 : (ushort)0x0000)), 0x05);

    /// <summary>FC16 with one big-endian single in two registers.</summary>
    public void WriteFloat(int address, float value)
    {
        uint bits = BitConverter.SingleToUInt32Bits(value);
        WriteRegisters(address, (ushort)(bits >> 16), (ushort)bits);
    }

    /// <summary>FC16.</summary>
    public void WriteRegisters(int address, params ushort[] words) => Expect(Request(WriteRegistersPdu(address, words)), 0x10);

    /// <summary>The FC16 request PDU: start, quantity, byte count, the words.</summary>
    public static byte[] WriteRegistersPdu(int address, params ushort[] words)
    {
        byte[] pdu = new byte[6 + (2 * words.Length)];
        pdu[0] = 0x10;
        BinaryPrimitives.WriteUInt16BigEndian(pdu.AsSpan(1), (ushort)address);
        BinaryPrimitives.WriteUInt16BigEndian(pdu.AsSpan(3), (ushort)words.Length);
        pdu[5] = (byte)(2 * words.Length);
        for (int i = 0; i < words.Length; i++)
        {
            BinaryPrimitives.WriteUInt16BigEndian(pdu.AsSpan(6 + (2 * i)), words[i]);
        }

        return pdu;
    }

    /// <summary>The single in registers <paramref name="at"/> and <paramref name="at"/> + 1, high word first.</summary>
    public static float Float(ushort[] words, int at) => BitConverter.UInt32BitsToSingle(((uint)words[at] << 16) | words[at + 1]);

    /// <summary>The Int32 in registers <paramref name="at"/> and <paramref name="at"/> + 1, high word first.</summary>
    public static int Int32(ushort[] words, int at) => unchecked((int)(((uint)words[at] << 16) | words[at + 1]));

    public void Dispose()
    {
        _stream.Dispose();
        _tcp.Dispose();
    }

    private static byte[] Expect(byte[] pdu, byte function) =>
        pdu[0] == function
            ? pdu
            : throw new InvalidDataException($"Function {function:X2} answered {pdu[0]:X2} with exception {(pdu.Length > 1 ? pdu[1] : 0):X2}.");

    private static bool[] Bits(byte[] pdu, int count)
    {
        bool[] bits = new bool[count];
        for (int i = 0; i < count; i++)
        {
            bits[i] = (pdu[2 + (i / 8)] & (1 << (i % 8))) != 0;
        }

        return bits;
    }

    private static ushort[] Words(byte[] pdu)
    {
        ushort[] words = new ushort[pdu[1] / 2];
        for (int i = 0; i < words.Length; i++)
        {
            words[i] = BinaryPrimitives.ReadUInt16BigEndian(pdu.AsSpan(2 + (2 * i)));
        }

        return words;
    }
}
```

In `tests/Dse.Modbus.Tests/Dse.Modbus.Tests.csproj`, replace

```xml
  <ItemGroup>
    <ProjectReference Include="..\..\src\Dse.Modbus\Dse.Modbus.csproj" />
  </ItemGroup>
```

with

```xml
  <ItemGroup>
    <Compile Include="..\Shared\ModbusClient.cs" Link="Shared\ModbusClient.cs" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\Dse.Modbus\Dse.Modbus.csproj" />
  </ItemGroup>
```

- [ ] **Step 2: Write the failing server tests**

Create `tests/Dse.Modbus.Tests/Fakes/Rig.cs`:

```csharp
using System.Net;
using Dse.Io;
using Dse.Realtime;
using Dse.Tests.Shared;

namespace Dse.Modbus.Tests.Fakes;

/// <summary>
/// A server over <see cref="Plant"/> on a free loopback port: the image it
/// reads is <see cref="Image"/>, replaced whole as the simulation's front
/// buffer is, and every accepted write lands in <see cref="Writer"/>.
/// </summary>
internal sealed class Rig : IAsyncDisposable
{
    private TagValue[] _image = Plant.Image();

    private Rig()
    {
        Directory = Plant.Directory();
        Map = RegisterMap.Build(Directory);
        Writer = new RecordingWriter(Directory);
        Commands = new CommandBus(Writer);
        Server = new ModbusServer(Map, () => Volatile.Read(ref _image), Commands);
        EndPoint = Server.Start(new IPEndPoint(IPAddress.Loopback, 0));
    }

    public ArrayDirectory Directory { get; }

    public RegisterMap Map { get; }

    public RecordingWriter Writer { get; }

    public CommandBus Commands { get; }

    public ModbusServer Server { get; }

    public IPEndPoint EndPoint { get; }

    /// <summary>The image the server reads; setting it publishes a new one.</summary>
    public TagValue[] Image
    {
        get => Volatile.Read(ref _image);
        set => Volatile.Write(ref _image, value);
    }

    public static Rig Start() => new();

    public ModbusClient Connect() => new(EndPoint);

    public ValueTask DisposeAsync() => Server.DisposeAsync();
}
```

Create `tests/Dse.Modbus.Tests/FunctionCodeTests.cs`:

```csharp
using Dse.Io;
using Dse.Modbus.Tests.Fakes;
using Dse.Tests.Shared;

namespace Dse.Modbus.Tests;

/// <summary>Each function code against <see cref="Plant"/>, over a real loopback connection.</summary>
public class FunctionCodeTests
{
    [Fact]
    public async Task ReadCoilsReturnsTheReadWriteBoolsPackedLowBitFirst()
    {
        await using Rig rig = Rig.Start();
        using ModbusClient client = rig.Connect();

        Assert.Equal(new[] { true, true }, client.ReadCoils(0, 2));
        Assert.Equal(new byte[] { 0x01, 0x01, 0b11 }, client.Request(ModbusClient.Pdu(0x01, 0, 2)));
    }

    [Fact]
    public async Task ReadDiscreteInputsReturnsTheReadOnlyBoolsClaimedOnesIncluded()
    {
        await using Rig rig = Rig.Start();
        using ModbusClient client = rig.Connect();

        Assert.Equal(new[] { true, false }, client.ReadDiscreteInputs(0, 2));
    }

    [Fact]
    public async Task ReadHoldingRegistersReturnsTheReadWriteNumbersTwoRegistersEach()
    {
        await using Rig rig = Rig.Start();
        using ModbusClient client = rig.Connect();

        ushort[] words = client.ReadHoldingRegisters(0, 6);

        Assert.Equal(-7, ModbusClient.Int32(words, 0));
        Assert.Equal(1.5f, ModbusClient.Float(words, 2));
        Assert.Equal(-0.25f, ModbusClient.Float(words, 4));
    }

    [Fact]
    public async Task ReadInputRegistersReturnsTheReadOnlyNumbersAndMayStartMidValue()
    {
        await using Rig rig = Rig.Start();
        using ModbusClient client = rig.Connect();

        ushort[] words = client.ReadInputRegisters(0, 4);
        ushort[] tail = client.ReadInputRegisters(3, 1);

        Assert.Equal(123_456, ModbusClient.Int32(words, 0));
        Assert.Equal(1.95f, ModbusClient.Float(words, 2));
        Assert.Equal(new ushort[] { 0x999A }, tail);
    }

    [Fact]
    public async Task EveryReadSeesTheImagePublishedWhenItArrives()
    {
        await using Rig rig = Rig.Start();
        using ModbusClient client = rig.Connect();
        Assert.Equal(1.95f, ModbusClient.Float(client.ReadInputRegisters(2, 2), 0));

        TagValue[] next = Plant.Image();
        next[6] = TagValue.Double(0.5);
        next[4] = TagValue.Bool(true);
        rig.Image = next;

        Assert.Equal(0.5f, ModbusClient.Float(client.ReadInputRegisters(2, 2), 0));
        Assert.Equal(new[] { true, true }, client.ReadDiscreteInputs(0, 2));
    }

    [Fact]
    public async Task AnImageNotYetPrimedReadsAsZeroesRatherThanFailing()
    {
        await using Rig rig = Rig.Start();
        rig.Image = new TagValue[9];
        using ModbusClient client = rig.Connect();

        Assert.Equal(new ushort[6], client.ReadHoldingRegisters(0, 6));
        Assert.Equal(new[] { false, false }, client.ReadCoils(0, 2));
    }

    [Fact]
    public async Task WriteSingleCoilQueuesTheBoolAndEchoesTheRequest()
    {
        await using Rig rig = Rig.Start();
        using ModbusClient client = rig.Connect();

        byte[] response = client.Request(ModbusClient.Pdu(0x05, 1, 0xFF00));
        client.WriteCoil(0, false);

        Assert.Equal(ModbusClient.Pdu(0x05, 1, 0xFF00), response);
        Assert.Equal(new[] { ("B.Stop", TagValue.Bool(true)), ("A.Run", TagValue.Bool(false)) }, rig.Writer.Writes);
    }

    [Fact]
    public async Task WriteMultipleCoilsQueuesEachBoolInAddressOrder()
    {
        await using Rig rig = Rig.Start();
        using ModbusClient client = rig.Connect();

        byte[] response = client.Request([0x0F, 0x00, 0x00, 0x00, 0x02, 0x01, 0b10]);

        Assert.Equal(new byte[] { 0x0F, 0x00, 0x00, 0x00, 0x02 }, response);
        Assert.Equal(new[] { ("A.Run", TagValue.Bool(false)), ("B.Stop", TagValue.Bool(true)) }, rig.Writer.Writes);
    }

    [Fact]
    public async Task WriteMultipleRegistersQueuesWholeValuesDecodedByKind()
    {
        await using Rig rig = Rig.Start();
        using ModbusClient client = rig.Connect();

        client.WriteRegisters(0, 0xFFFF, 0xFFF9, 0x3FC0, 0x0000);
        client.WriteFloat(4, 1e30f);

        Assert.Equal(
            new[] { ("A.Batch", TagValue.Int64(-7L)), ("A.Setpoint", TagValue.Double(1.5)), ("B.Bias", TagValue.Double(1e30f)) },
            rig.Writer.Writes);
    }

    [Theory]
    [InlineData(0x07)]
    [InlineData(0x08)]
    [InlineData(0x17)]
    [InlineData(0x2B)]
    [InlineData(0x00)]
    public async Task AnUnsupportedFunctionIsException01(byte function)
    {
        await using Rig rig = Rig.Start();
        using ModbusClient client = rig.Connect();

        Assert.Equal(new byte[] { (byte)(function | 0x80), 0x01 }, client.Request(ModbusClient.Pdu(function, 0, 1)));
    }

    [Theory]
    [InlineData(0x01, 2, 1)]
    [InlineData(0x01, 0, 3)]
    [InlineData(0x02, 1, 2)]
    [InlineData(0x03, 6, 1)]
    [InlineData(0x03, 0, 7)]
    [InlineData(0x04, 3, 2)]
    [InlineData(0x04, 65535, 1)]
    public async Task AReadPastTheEndOfItsAreaIsException02(byte function, int start, int count)
    {
        await using Rig rig = Rig.Start();
        using ModbusClient client = rig.Connect();

        Assert.Equal(new byte[] { (byte)(function | 0x80), 0x02 }, client.Request(ModbusClient.Pdu(function, (ushort)start, (ushort)count)));
    }

    [Theory]
    [InlineData(0x01, 0)]
    [InlineData(0x01, 2001)]
    [InlineData(0x02, 0)]
    [InlineData(0x03, 0)]
    [InlineData(0x03, 126)]
    [InlineData(0x04, 126)]
    public async Task AReadOfAnIllegalQuantityIsException03(byte function, int count)
    {
        await using Rig rig = Rig.Start();
        using ModbusClient client = rig.Connect();

        Assert.Equal(new byte[] { (byte)(function | 0x80), 0x03 }, client.Request(ModbusClient.Pdu(function, 0, (ushort)count)));
    }

    [Theory]
    [InlineData(0, 0x0001)]
    [InlineData(0, 0x00FF)]
    [InlineData(5, 0x1234)]
    public async Task ACoilValueOtherThanFF00Or0000IsException03(int address, int value)
    {
        await using Rig rig = Rig.Start();
        using ModbusClient client = rig.Connect();

        Assert.Equal(new byte[] { 0x85, 0x03 }, client.Request(ModbusClient.Pdu(0x05, (ushort)address, (ushort)value)));
        Assert.Empty(rig.Writer.Writes);
    }

    [Theory]
    [InlineData(0x05, 2)]
    [InlineData(0x06, 0)]
    [InlineData(0x06, 3)]
    [InlineData(0x06, 6)]
    public async Task AWriteToAnAddressNoReadWriteTagOccupiesOrToHalfAValueIsException02(byte function, int address)
    {
        await using Rig rig = Rig.Start();
        using ModbusClient client = rig.Connect();

        ushort value = function == 0x05 ? (ushort)0xFF00 : (ushort)0x0001;
        Assert.Equal(new byte[] { (byte)(function | 0x80), 0x02 }, client.Request(ModbusClient.Pdu(function, (ushort)address, value)));
        Assert.Empty(rig.Writer.Writes);
    }

    [Theory]
    [InlineData(1, new ushort[] { 0x3FC0, 0x0000 })]
    [InlineData(0, new ushort[] { 0x0000 })]
    [InlineData(0, new ushort[] { 0x0000, 0x0001, 0x3FC0 })]
    [InlineData(4, new ushort[] { 0x0000, 0x0000, 0x0000, 0x0000 })]
    public async Task AWriteOfPartOfATwoRegisterValueIsException02AndWritesNothing(int start, ushort[] words)
    {
        await using Rig rig = Rig.Start();
        using ModbusClient client = rig.Connect();

        Assert.Equal(new byte[] { 0x90, 0x02 }, client.Request(ModbusClient.WriteRegistersPdu(start, words)));
        Assert.Empty(rig.Writer.Writes);
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    [InlineData(2.5f)]
    [InlineData(-0.5f)]
    public async Task ANonFiniteOrOutOfRangeValueIsException03AndNothingOfTheRequestIsWritten(float setpoint)
    {
        await using Rig rig = Rig.Start();
        using ModbusClient client = rig.Connect();
        uint bits = BitConverter.SingleToUInt32Bits(setpoint);

        byte[] response = client.Request(ModbusClient.WriteRegistersPdu(0, 0x0000, 0x0003, (ushort)(bits >> 16), (ushort)bits));

        Assert.Equal(new byte[] { 0x90, 0x03 }, response);
        Assert.Empty(rig.Writer.Writes);
    }

    [Theory]
    [InlineData(new byte[] { 0x10, 0x00, 0x00, 0x00, 0x02, 0x03, 0, 0, 0, 0 })]
    [InlineData(new byte[] { 0x10, 0x00, 0x00, 0x00, 0x02, 0x04, 0, 0, 0 })]
    [InlineData(new byte[] { 0x10, 0x00, 0x00, 0x00, 0x00, 0x00 })]
    [InlineData(new byte[] { 0x0F, 0x00, 0x00, 0x00, 0x02, 0x02, 0x03, 0x00 })]
    [InlineData(new byte[] { 0x0F, 0x00, 0x00, 0x00, 0x09, 0x01, 0xFF })]
    [InlineData(new byte[] { 0x03, 0x00, 0x00, 0x00 })]
    [InlineData(new byte[] { 0x05, 0x00, 0x00, 0xFF, 0x00, 0x00 })]
    public async Task AMalformedRequestIsException03(byte[] pdu)
    {
        await using Rig rig = Rig.Start();
        using ModbusClient client = rig.Connect();

        Assert.Equal(new byte[] { (byte)(pdu[0] | 0x80), 0x03 }, client.Request(pdu));
        Assert.Empty(rig.Writer.Writes);
    }
}
```

(50 tests: 9 facts, and theories of 5, 7, 6, 3, 4, 4, 5 and 7 rows. The
expected bytes follow from `Plant`'s image: `A.Speed` 1.95 is `3FF9 999A`,
`A.Batch` −7 is `FFFF FFF9`, `A.Setpoint` 1.5 is `3FC0 0000`.)

Create `tests/Dse.Modbus.Tests/ServerTests.cs`:

```csharp
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Dse.Io;
using Dse.Modbus.Tests.Fakes;
using Dse.Realtime;
using Dse.Tests.Shared;

namespace Dse.Modbus.Tests;

/// <summary>MBAP framing, unit ids, several clients at once, and shutdown.</summary>
public class ServerTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private static void WaitUntil(Func<bool> condition, string what)
    {
        var clock = Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(clock.Elapsed < Patience, $"Timed out waiting for {what}.");
            Thread.Sleep(5);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(17)]
    [InlineData(247)]
    [InlineData(255)]
    public async Task AnyUnitIdIsAcceptedAndEchoed(byte unit)
    {
        await using Rig rig = Rig.Start();
        using ModbusClient client = rig.Connect();
        client.Unit = unit;

        Assert.Equal(new[] { true, true }, client.ReadCoils(0, 2));
    }

    [Fact]
    public async Task ARequestSplitAcrossSeveralReadsIsAnsweredOnceWhole()
    {
        await using Rig rig = Rig.Start();
        using ModbusClient client = rig.Connect();
        byte[] frame = ModbusClient.Frame(0x1234, 9, ModbusClient.Pdu(0x04, 2, 2));

        client.Send(frame.AsSpan(0, 3));
        Thread.Sleep(50);
        client.Send(frame.AsSpan(3, 5));
        Thread.Sleep(50);
        client.Send(frame.AsSpan(8));
        (ushort transaction, byte unit, byte[] pdu) = client.Receive();

        Assert.Equal((0x1234, 9), (transaction, unit));
        Assert.Equal(new byte[] { 0x04, 0x04, 0x3F, 0xF9, 0x99, 0x9A }, pdu);
    }

    [Fact]
    public async Task TwoRequestsInOneReadAreAnsweredInOrder()
    {
        await using Rig rig = Rig.Start();
        using ModbusClient client = rig.Connect();
        byte[] first = ModbusClient.Frame(7, 1, ModbusClient.Pdu(0x01, 0, 2));
        byte[] second = ModbusClient.Frame(8, 1, ModbusClient.Pdu(0x05, 1, 0x0000));

        client.Send([.. first, .. second]);

        (ushort t1, _, byte[] r1) = client.Receive();
        (ushort t2, _, byte[] r2) = client.Receive();

        Assert.Equal((7, 8), (t1, t2));
        Assert.Equal(new byte[] { 0x01, 0x01, 0b11 }, r1);
        Assert.Equal(ModbusClient.Pdu(0x05, 1, 0x0000), r2);
        Assert.Equal(new[] { ("B.Stop", TagValue.Bool(false)) }, rig.Writer.Writes);
    }

    [Theory]
    [InlineData(new byte[] { 0x00, 0x01, 0x00, 0x01, 0x00, 0x06, 0x01, 0x01, 0x00, 0x00, 0x00, 0x01 })]
    [InlineData(new byte[] { 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, 0x01 })]
    [InlineData(new byte[] { 0x00, 0x01, 0x00, 0x00, 0x01, 0x00, 0x01 })]
    public async Task AFrameWithAForeignProtocolIdOrAnImpossibleLengthClosesTheConnection(byte[] frame)
    {
        await using Rig rig = Rig.Start();
        using ModbusClient client = rig.Connect();

        client.Send(frame);

        Assert.True(client.IsClosedByServer());
        using ModbusClient next = rig.Connect();
        Assert.Equal(new[] { true, true }, next.ReadCoils(0, 2));
    }

    [Fact]
    public async Task SeveralClientsAtOnceEachGetTheirOwnAnswers()
    {
        await using Rig rig = Rig.Start();
        const int clients = 8;
        const int rounds = 100;

        Task[] work = Enumerable.Range(0, clients).Select(c => Task.Run(() =>
        {
            using ModbusClient client = rig.Connect();
            client.Unit = (byte)(c + 1);
            for (int r = 0; r < rounds; r++)
            {
                Assert.Equal(1.95f, ModbusClient.Float(client.ReadInputRegisters(2, 2), 0));
                client.WriteCoil(c % 2, r % 2 == 0);
            }
        })).ToArray();

        await Task.WhenAll(work).WaitAsync(Patience);
        Assert.Equal(clients * rounds, rig.Writer.Writes.Length);
        Assert.Equal(clients * rounds, rig.Commands.Accepted);
        Assert.Equal(2L * clients * rounds, rig.Server.Requests);
    }

    [Fact]
    public async Task OneRequestsValuesComeFromOneImageWhileTheImageIsReplaced()
    {
        await using Rig rig = Rig.Start();
        rig.Image = Consistent(0);                            // every image the client can see is consistent, the first too
        using var stop = new CancellationTokenSource();
        Task publisher = Task.Run(() =>
        {
            for (long n = 1; !stop.IsCancellationRequested; n++)
            {
                rig.Image = Consistent(n);
            }
        });

        using ModbusClient client = rig.Connect();
        for (int i = 0; i < 500; i++)
        {
            ushort[] words = client.ReadHoldingRegisters(0, 6);
            int batch = ModbusClient.Int32(words, 0);
            Assert.Equal(batch % 2, (int)ModbusClient.Float(words, 2));
            Assert.Equal(batch % 2, (int)ModbusClient.Float(words, 4));
        }

        await stop.CancelAsync();
        await publisher;

        static TagValue[] Consistent(long n)
        {
            TagValue[] image = Plant.Image();
            image[0] = TagValue.Int64(n);                     // A.Batch
            image[5] = TagValue.Double(n % 2);                // A.Setpoint
            image[7] = TagValue.Double(n % 2);                // B.Bias
            return image;
        }
    }

    [Fact]
    public async Task DisposingTheServerClosesEveryConnectionAndStopsListening()
    {
        Rig rig = Rig.Start();
        using ModbusClient first = rig.Connect();
        using ModbusClient second = rig.Connect();
        Assert.Equal(new[] { true, true }, first.ReadCoils(0, 2));
        Assert.Equal(new[] { true, true }, second.ReadCoils(0, 2));
        WaitUntil(() => rig.Server.OpenConnections == 2, "two open connections");

        await rig.DisposeAsync().AsTask().WaitAsync(Patience);

        Assert.Equal(0, rig.Server.OpenConnections);
        Assert.True(first.IsClosedByServer());
        Assert.True(second.IsClosedByServer());
        using var probe = new TcpClient();
        Assert.Throws<SocketException>(() => probe.Connect(rig.EndPoint));
    }

    [Fact]
    public async Task APortInUseFailsToStartWithASocketException()
    {
        await using Rig rig = Rig.Start();
        var map = RegisterMap.Build(Plant.Directory());
        await using var second = new ModbusServer(map, () => Plant.Image(), new CommandBus(new RecordingWriter(Plant.Directory())));

        Assert.Throws<SocketException>(() => second.Start(rig.EndPoint));
        Assert.Throws<SocketException>(() => second.Start(new IPEndPoint(IPAddress.Loopback, rig.EndPoint.Port)));
    }
}
```

(14 tests: theories of 5 and 3 rows, 6 facts.)

Run: `dotnet build tests/Dse.Modbus.Tests --nologo` — expect it not to
compile: `ModbusServer` does not exist.

- [ ] **Step 3: Write the server**

Create `src/Dse.Modbus/ModbusExceptionCode.cs`:

```csharp
namespace Dse.Modbus;

/// <summary>The Modbus exception codes this server answers with (spec criterion 2).</summary>
public enum ModbusExceptionCode : byte
{
    /// <summary>01: the function code is not one of 1, 2, 3, 4, 5, 6, 15, 16.</summary>
    IllegalFunction = 0x01,

    /// <summary>02: an address past the end of the area, or a write that covers part of a two-register value.</summary>
    IllegalDataAddress = 0x02,

    /// <summary>03: a malformed request (quantity, byte count, coil value), a non-finite value, or a value the tag refuses.</summary>
    IllegalDataValue = 0x03,
}
```

Create `src/Dse.Modbus/ModbusProtocol.cs`:

```csharp
using System.Buffers.Binary;
using Dse.Io;
using Dse.Realtime;

namespace Dse.Modbus;

/// <summary>
/// One request PDU in, one response PDU out (Modbus Application Protocol
/// v1.1b3 §6). Reads take one snapshot of the published image per request, so
/// every value in a response belongs to the same tick. Writes are validated
/// whole — every value of a multi-value request — before any is handed to the
/// command bus, so a refused request writes nothing. Checks run in the
/// specification's order: function (01), quantity and framing (03), address
/// (02), value (03). Thread-safe: every connection shares one instance.
/// </summary>
internal sealed class ModbusProtocol
{
    private const int MaxReadBits = 2000;
    private const int MaxReadRegisters = 125;
    private const int MaxWriteBits = 1968;
    private const int MaxWriteRegisters = 123;

    private readonly RegisterMap _map;
    private readonly Func<ReadOnlyMemory<TagValue>> _snapshot;
    private readonly CommandBus _commands;
    private readonly Lock _writeGate = new();

    public ModbusProtocol(RegisterMap map, Func<ReadOnlyMemory<TagValue>> snapshot, CommandBus commands)
    {
        _map = map;
        _snapshot = snapshot;
        _commands = commands;
    }

    /// <summary>Answers one request PDU (function code first). Never throws on a malformed request.</summary>
    public byte[] Handle(ReadOnlySpan<byte> pdu)
    {
        byte function = pdu[0];
        return function switch
        {
            0x01 => ReadBits(pdu, ModbusArea.Coils),
            0x02 => ReadBits(pdu, ModbusArea.DiscreteInputs),
            0x03 => ReadRegisters(pdu, ModbusArea.HoldingRegisters),
            0x04 => ReadRegisters(pdu, ModbusArea.InputRegisters),
            0x05 => WriteSingleCoil(pdu),
            0x06 => WriteSingleRegister(pdu),
            0x0F => WriteMultipleCoils(pdu),
            0x10 => WriteMultipleRegisters(pdu),
            _ => Exception(function, ModbusExceptionCode.IllegalFunction),
        };
    }

    private static byte[] Exception(byte function, ModbusExceptionCode code) => [(byte)(function | 0x80), (byte)code];

    private static ushort Word(ReadOnlySpan<byte> pdu, int at) => BinaryPrimitives.ReadUInt16BigEndian(pdu[at..]);

    private bool InArea(ModbusArea area, int start, int quantity) => start + quantity <= _map.Size(area);

    private byte[] ReadBits(ReadOnlySpan<byte> pdu, ModbusArea area)
    {
        byte function = pdu[0];
        if (pdu.Length != 5)
        {
            return Exception(function, ModbusExceptionCode.IllegalDataValue);
        }

        int start = Word(pdu, 1);
        int quantity = Word(pdu, 3);
        if (quantity is < 1 or > MaxReadBits)
        {
            return Exception(function, ModbusExceptionCode.IllegalDataValue);
        }

        if (!InArea(area, start, quantity))
        {
            return Exception(function, ModbusExceptionCode.IllegalDataAddress);
        }

        ReadOnlySpan<TagValue> image = _snapshot().Span;
        int bytes = (quantity + 7) / 8;
        byte[] response = new byte[2 + bytes];
        response[0] = function;
        response[1] = (byte)bytes;
        for (int i = 0; i < quantity; i++)
        {
            TagValue value = image[_map.At(area, start + i)!.Tag.Index];
            if (value.Kind == TagKind.Bool && value.AsBool)
            {
                response[2 + (i / 8)] |= (byte)(1 << (i % 8));
            }
        }

        return response;
    }

    private byte[] ReadRegisters(ReadOnlySpan<byte> pdu, ModbusArea area)
    {
        byte function = pdu[0];
        if (pdu.Length != 5)
        {
            return Exception(function, ModbusExceptionCode.IllegalDataValue);
        }

        int start = Word(pdu, 1);
        int quantity = Word(pdu, 3);
        if (quantity is < 1 or > MaxReadRegisters)
        {
            return Exception(function, ModbusExceptionCode.IllegalDataValue);
        }

        if (!InArea(area, start, quantity))
        {
            return Exception(function, ModbusExceptionCode.IllegalDataAddress);
        }

        ReadOnlySpan<TagValue> image = _snapshot().Span;
        byte[] response = new byte[2 + (2 * quantity)];
        response[0] = function;
        response[1] = (byte)(2 * quantity);
        for (int i = 0; i < quantity; i++)
        {
            RegisterEntry entry = _map.At(area, start + i)!;
            (ushort high, ushort low) = RegisterCodec.Encode(entry.Tag.Kind, image[entry.Tag.Index]);
            ushort word = start + i == entry.Offset ? high : low;
            BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(2 + (2 * i)), word);
        }

        return response;
    }

    private byte[] WriteSingleCoil(ReadOnlySpan<byte> pdu)
    {
        const byte function = 0x05;
        if (pdu.Length != 5)
        {
            return Exception(function, ModbusExceptionCode.IllegalDataValue);
        }

        int address = Word(pdu, 1);
        ushort value = Word(pdu, 3);
        if (value is not (0xFF00 or 0x0000))
        {
            return Exception(function, ModbusExceptionCode.IllegalDataValue);
        }

        if (!InArea(ModbusArea.Coils, address, 1))
        {
            return Exception(function, ModbusExceptionCode.IllegalDataAddress);
        }

        TagDescriptor tag = _map.At(ModbusArea.Coils, address)!.Tag;
        lock (_writeGate)
        {
            if (_commands.WriteBool(tag.Name, value == 0xFF00) != CommandOutcome.Accepted)
            {
                return Exception(function, ModbusExceptionCode.IllegalDataValue);
            }
        }

        return pdu.ToArray();
    }

    /// <summary>
    /// Every holding register belongs to a two-register value, so FC6 — one
    /// register — can only ever write half of one: within the area it answers
    /// 02, as any partial write does (R197).
    /// </summary>
    private byte[] WriteSingleRegister(ReadOnlySpan<byte> pdu)
    {
        const byte function = 0x06;
        if (pdu.Length != 5)
        {
            return Exception(function, ModbusExceptionCode.IllegalDataValue);
        }

        return Exception(function, ModbusExceptionCode.IllegalDataAddress);
    }

    private byte[] WriteMultipleCoils(ReadOnlySpan<byte> pdu)
    {
        const byte function = 0x0F;
        if (pdu.Length < 6)
        {
            return Exception(function, ModbusExceptionCode.IllegalDataValue);
        }

        int start = Word(pdu, 1);
        int quantity = Word(pdu, 3);
        int bytes = pdu[5];
        if (quantity is < 1 or > MaxWriteBits || bytes != (quantity + 7) / 8 || pdu.Length != 6 + bytes)
        {
            return Exception(function, ModbusExceptionCode.IllegalDataValue);
        }

        if (!InArea(ModbusArea.Coils, start, quantity))
        {
            return Exception(function, ModbusExceptionCode.IllegalDataAddress);
        }

        bool refused = false;
        lock (_writeGate)
        {
            for (int i = 0; i < quantity; i++)
            {
                bool on = (pdu[6 + (i / 8)] & (1 << (i % 8))) != 0;
                refused |= _commands.WriteBool(_map.At(ModbusArea.Coils, start + i)!.Tag.Name, on) != CommandOutcome.Accepted;
            }
        }

        return refused ? Exception(function, ModbusExceptionCode.IllegalDataValue) : pdu[..5].ToArray();
    }

    private byte[] WriteMultipleRegisters(ReadOnlySpan<byte> pdu)
    {
        const byte function = 0x10;
        if (pdu.Length < 6)
        {
            return Exception(function, ModbusExceptionCode.IllegalDataValue);
        }

        int start = Word(pdu, 1);
        int quantity = Word(pdu, 3);
        int bytes = pdu[5];
        if (quantity is < 1 or > MaxWriteRegisters || bytes != 2 * quantity || pdu.Length != 6 + bytes)
        {
            return Exception(function, ModbusExceptionCode.IllegalDataValue);
        }

        if (!InArea(ModbusArea.HoldingRegisters, start, quantity))
        {
            return Exception(function, ModbusExceptionCode.IllegalDataAddress);
        }

        RegisterEntry first = _map.At(ModbusArea.HoldingRegisters, start)!;
        RegisterEntry last = _map.At(ModbusArea.HoldingRegisters, start + quantity - 1)!;
        if (first.Offset != start || last.Offset + last.Width != start + quantity)
        {
            return Exception(function, ModbusExceptionCode.IllegalDataAddress);
        }

        var writes = new (TagDescriptor Tag, TagValue Value)[quantity / 2];
        for (int i = 0; i < writes.Length; i++)
        {
            TagDescriptor tag = _map.At(ModbusArea.HoldingRegisters, start + (2 * i))!.Tag;
            ushort high = Word(pdu, 6 + (4 * i));
            ushort low = Word(pdu, 8 + (4 * i));
            if (tag.Kind == TagKind.Double)
            {
                double value = RegisterCodec.ToFloat32(high, low);
                if (!double.IsFinite(value) || (tag.HasRange && (value < tag.RangeLow || value > tag.RangeHigh)))
                {
                    return Exception(function, ModbusExceptionCode.IllegalDataValue);
                }

                writes[i] = (tag, TagValue.Double(value));
            }
            else
            {
                writes[i] = (tag, TagValue.Int64(RegisterCodec.ToInt32(high, low)));
            }
        }

        bool refused = false;
        lock (_writeGate)
        {
            foreach ((TagDescriptor tag, TagValue value) in writes)
            {
                refused |= _commands.Write(tag.Name, value) != CommandOutcome.Accepted;
            }
        }

        // Pre-validation above makes a refusal here unreachable for this directory's
        // tags; should the bus refuse one anyway, the master is told rather than
        // shown an echo for a write that did not happen.
        return refused ? Exception(function, ModbusExceptionCode.IllegalDataValue) : pdu[..5].ToArray();
    }
}
```

Create `src/Dse.Modbus/ModbusServer.cs`:

```csharp
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using Dse.Io;
using Dse.Realtime;

namespace Dse.Modbus;

/// <summary>
/// A Modbus TCP server over a plant (plan 8 spec criterion 2): MBAP framing,
/// function codes 1, 2, 3, 4, 5, 6, 15 and 16, any unit id accepted and
/// echoed, any number of clients at once. Reads come from
/// the snapshot delegate — the tag image's published array — and writes
/// go through the command bus, so they land at phase 1 of the next
/// tick like any other external write. Each connection answers its requests
/// in order; a request split across reads, or several in one read, is framed
/// by the MBAP length. A frame whose protocol id is not 0 or whose length is
/// outside 2–254 closes its connection.
/// </summary>
public sealed class ModbusServer : IAsyncDisposable
{
    private const int HeaderLength = 7;
    private const int MaxLength = 254;

    private readonly ModbusProtocol _protocol;
    private readonly CancellationTokenSource _stop = new();
    private readonly Lock _gate = new();
    private readonly List<Task> _connections = [];
    private TcpListener? _listener;
    private Task? _accepting;
    private int _open;
    private long _requests;
    private bool _disposed;

    /// <summary>Creates a server; <see cref="Start"/> opens it.</summary>
    /// <param name="map">The register map.</param>
    /// <param name="snapshot">Returns the current published image, indexed by tag; called once per read request.</param>
    /// <param name="commands">Where writes go.</param>
    public ModbusServer(RegisterMap map, Func<ReadOnlyMemory<TagValue>> snapshot, CommandBus commands)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(commands);
        Map = map;
        _protocol = new ModbusProtocol(map, snapshot, commands);
    }

    /// <summary>The register map served.</summary>
    public RegisterMap Map { get; }

    /// <summary>The bound endpoint once started — with the port the system chose when started on port 0.</summary>
    public IPEndPoint? LocalEndPoint { get; private set; }

    /// <summary>Connections currently open.</summary>
    public int OpenConnections => Volatile.Read(ref _open);

    /// <summary>Requests answered so far, exceptions included: counted before the response is sent, so a client that has its answer sees it counted.</summary>
    public long Requests => Interlocked.Read(ref _requests);

    /// <summary>
    /// Binds and starts accepting. Throws <see cref="SocketException"/> when
    /// the endpoint cannot be bound (a port in use), and
    /// <see cref="InvalidOperationException"/> when already started.
    /// </summary>
    public IPEndPoint Start(IPEndPoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_listener is not null)
        {
            throw new InvalidOperationException("The server has already been started.");
        }

        var listener = new TcpListener(endpoint);
        try
        {
            listener.Start();
        }
        catch (SocketException)
        {
            listener.Dispose();
            throw;
        }

        _listener = listener;
        LocalEndPoint = (IPEndPoint)listener.LocalEndpoint;
        _accepting = AcceptAsync(listener, _stop.Token);
        return LocalEndPoint;
    }

    /// <summary>Stops accepting, closes every connection and waits for them to finish.</summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await _stop.CancelAsync().ConfigureAwait(false);
        _listener?.Stop();
        if (_accepting is not null)
        {
            await _accepting.ConfigureAwait(false);
        }

        Task[] connections;
        lock (_gate)
        {
            connections = [.. _connections];
        }

        await Task.WhenAll(connections).ConfigureAwait(false);
        _stop.Dispose();
    }

    private async Task AcceptAsync(TcpListener listener, CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(stop).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException or SocketException or ObjectDisposedException)
            {
                return;
            }

            lock (_gate)
            {
                _connections.RemoveAll(t => t.IsCompleted);
                _connections.Add(ServeAsync(client, stop));
            }
        }
    }

    private async Task ServeAsync(TcpClient client, CancellationToken stop)
    {
        await Task.Yield();
        Interlocked.Increment(ref _open);
        try
        {
            using (client)
            {
                client.NoDelay = true;
                NetworkStream stream = client.GetStream();
                byte[] header = new byte[HeaderLength];
                byte[] pdu = new byte[MaxLength - 1];
                while (true)
                {
                    await stream.ReadExactlyAsync(header, stop).ConfigureAwait(false);
                    ushort protocol = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(2));
                    ushort length = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(4));
                    if (protocol != 0 || length is < 2 or > MaxLength)
                    {
                        return;
                    }

                    await stream.ReadExactlyAsync(pdu.AsMemory(0, length - 1), stop).ConfigureAwait(false);
                    byte[] response = _protocol.Handle(pdu.AsSpan(0, length - 1));

                    byte[] frame = new byte[HeaderLength + response.Length];
                    header.AsSpan(0, 2).CopyTo(frame);
                    BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(4), (ushort)(response.Length + 1));
                    frame[6] = header[6];
                    response.CopyTo(frame, HeaderLength);
                    Interlocked.Increment(ref _requests);
                    await stream.WriteAsync(frame, stop).ConfigureAwait(false);
                }
            }
        }
        catch (Exception ex) when (ex is EndOfStreamException or IOException or SocketException or OperationCanceledException or ObjectDisposedException)
        {
            // The client went away, or the server is stopping: either way this connection is done.
        }
        finally
        {
            Interlocked.Decrement(ref _open);
        }
    }
}
```

- [ ] **Step 4: Run the tests and everything**

Run: `dotnet test tests/Dse.Modbus.Tests --nologo` — expect PASS, **93**; run it three times (measured: under a second each, no failure).
Run: `dotnet build Dse.sln -c Release --nologo` — expect `0 Warning(s)`, `0 Error(s)`.
Run: `dotnet test Dse.sln --nologo` — expect **1674**: the Task 2 counts with `Dse.Modbus.Tests` 93.
Run: `git status --short -uall` — expect exactly the paths of Step 5.

- [ ] **Step 5: Commit**

```bash
git add src/Dse.Modbus/ModbusExceptionCode.cs src/Dse.Modbus/ModbusProtocol.cs src/Dse.Modbus/ModbusServer.cs tests/Shared/ModbusClient.cs tests/Dse.Modbus.Tests/Dse.Modbus.Tests.csproj tests/Dse.Modbus.Tests/Fakes/Rig.cs tests/Dse.Modbus.Tests/FunctionCodeTests.cs tests/Dse.Modbus.Tests/ServerTests.cs
git commit -F .superpowers/sdd/8/msg-task3.txt
```

with `.superpowers/sdd/8/msg-task3.txt`:

```
feat(modbus): serve the register map over Modbus TCP

ModbusServer frames MBAP over TCP and answers function codes 1, 2, 3, 4,
5, 6, 15 and 16 for any unit id and any number of connections. Reads take
one snapshot of the published image per request; writes are validated
whole and go through the CommandBus. A partial write of a two-register
value answers 02, a non-finite or out-of-range value 03, any other
function 01; a frame with a foreign protocol id or an impossible length
closes its connection. The tests drive it through a minimal Modbus client
in tests/Shared.

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
```

Then `git log -1 --format='%h%n%s%n--%n%b'`.

---

### Task 4: `dse serve` and `dse modbus-map`

**Model:** implementer sonnet; reviewer **opus** (R199–R202: threading,
signals, cancellation, the scenario binding).

**Files:**
- Create: `src/Dse.Scenarios/ScenarioBinding.cs`
- Replace: `src/Dse.Scenarios/ScenarioRunner.cs` (whole file shown; `Run` becomes `Bind` + `RunFor`)
- Create: `tests/Dse.Scenarios.Tests/ScenarioBindingTests.cs`
- Replace: `src/Dse.Cli/Dse.Cli.csproj`, `src/Dse.Cli/CliApp.cs`, `src/Dse.Cli/CommandTable.cs` (whole files shown)
- Modify: `src/Dse.Cli/CliContext.cs`, `src/Dse.Cli/ExitCodes.cs`, `tests/Dse.Cli.Tests/Cli.cs`, `tests/Dse.Samples.Tests/Dse.Samples.Tests.csproj`
- Create: `src/Dse.Cli/Commands/Serve.cs`, `src/Dse.Cli/Commands/ModbusMap.cs`
- Create: `tests/Dse.Cli.Tests/ServeCommandTests.cs`, `tests/Dse.Cli.Tests/ModbusMapCommandTests.cs`, `tests/Dse.Samples.Tests/ServeTests.cs`

**Interfaces:**
- Consumes: Tasks 2–3 (`RegisterMap`, `RegisterEntry`, `ModbusServer`,
  `ModbusClient`); `Simulation.Initialize`, `Simulation.IO.Snapshot()`,
  `Simulation.IO.Directory`, `Simulation.Clock`; `SimulationRunner(sim,
  ExecutionMode, speed).RunFor(TimeSpan, CancellationToken)` (it checks the
  token before every tick and waits on the token's handle while pacing);
  `CommandBus(ITagWriter)` over `Simulation.IO`; `PlantFile.TryBuild`,
  `TryRead`, `ReportDiagnostics`; `ScenarioLoader.Parse`,
  `Scenario.ResolvePlantPath`; `CatalogueJson.WriterOptions`, `Finish`.
- Produces: `ScenarioRunner.Bind(Scenario, string, ComponentCatalogue) →
  ScenarioBinding` (`Diagnostics`, `Simulation`, `IsValid`, `ToText()`);
  `CliApp.Run(string[], TextWriter, TextWriter, CancellationToken)`;
  `CommandTable.Scenario`, `Port`, `Speed`, `MapFormat`; the `serve` and
  `modbus-map` commands; `ModbusMap.FuxaId`, `ModbusMap.MemoryAddress`
  (internal; Task 5's project uses the same scheme, R203).

- [ ] **Step 1: Write the failing scenario-binding tests**

Create `tests/Dse.Scenarios.Tests/ScenarioBindingTests.cs`:

```csharp
using Dse.Core;
using Dse.Io;

namespace Dse.Scenarios.Tests;

/// <summary><c>ScenarioRunner.Bind</c> (plan 8): the run without the running, for a host that ticks it itself.</summary>
public class ScenarioBindingTests
{
    private static ScenarioBinding Bind(string plant, string body)
    {
        ScenarioParseResult parsed = ScenarioLoader.Parse($$"""{ "plant": "{{plant}}", {{body}} }""");
        return ScenarioRunner.Bind(parsed.Scenario!, File.ReadAllText(Corpus.PlantPath(plant)), Corpus.Catalogue);
    }

    [Fact]
    public void ABoundScenarioIsATickZeroSimulationWithItsTimelineScheduled()
    {
        ScenarioBinding binding = Bind("minimal.json", """
            "duration": 30,
            "timeline": [ { "at": 10, "write": "FEED.Rate", "value": 5 } ]
            """);

        Simulation simulation = Assert.IsType<Simulation>(binding.Simulation);
        Assert.True(binding.IsValid);
        Assert.Empty(binding.Diagnostics);
        Assert.Equal(0L, simulation.Clock.TickCount);

        simulation.RunFor(TimeSpan.FromSeconds(11));
        Assert.Equal(TagValue.Double(5.0), simulation.IO.Read("FEED.Rate"));
        Assert.Contains(simulation.Events.Records, r => r.Code == "WRITE" && r.Message == "Set to 5.");
    }

    [Fact]
    public void ABoundScenarioRunsOnPastItsDuration()
    {
        Simulation simulation = Bind("minimal.json", "\"duration\": 2").Simulation!;

        simulation.RunFor(TimeSpan.FromSeconds(5));

        Assert.Equal(500L, simulation.Clock.TickCount);
    }

    [Fact]
    public void AScenarioThatDoesNotBindGivesNoSimulationAndRunsDiagnostics()
    {
        const string Body = """
            "duration": 30,
            "timeline": [ { "at": 10, "write": "FEED.Nope", "value": 5 } ]
            """;

        ScenarioBinding binding = Bind("minimal.json", Body);
        ScenarioRunResult run = Corpus.Run($$"""{ "plant": "minimal.json", {{Body}} }""", "minimal.json");

        Assert.Null(binding.Simulation);
        Assert.False(binding.IsValid);
        Assert.Equal(run.ToText(), binding.ToText());
    }
}
```

- [ ] **Step 2: Split `ScenarioRunner.Run` into `Bind` and the run**

Create `src/Dse.Scenarios/ScenarioBinding.cs`:

```csharp
using Dse.Configuration;
using Dse.Core;

namespace Dse.Scenarios;

/// <summary>
/// A scenario bound to its plant and not yet run: the built simulation with
/// every action scheduled, or every reason it could not be. Never both.
/// </summary>
public sealed class ScenarioBinding
{
    internal ScenarioBinding(IReadOnlyList<ConfigDiagnostic> diagnostics, Simulation? simulation)
    {
        Diagnostics = diagnostics;
        Simulation = simulation;
    }

    /// <summary>Every problem found, in the order <see cref="ScenarioRunner.Run"/> reports them.</summary>
    public IReadOnlyList<ConfigDiagnostic> Diagnostics { get; }

    /// <summary>The simulation at tick 0 with the timeline scheduled, or null when the scenario did not bind.</summary>
    public Simulation? Simulation { get; }

    /// <summary>True when nothing of error severity was reported.</summary>
    public bool IsValid => Diagnostics.All(d => d.Severity != DiagnosticSeverity.Error);

    /// <summary>Every diagnostic, blank-line separated, ending in a newline; empty when there are none.</summary>
    public string ToText() =>
        Diagnostics.Count == 0 ? string.Empty : string.Join("\n\n", Diagnostics.Select(d => d.ToText())) + "\n";
}
```

Replace the whole of `src/Dse.Scenarios/ScenarioRunner.cs` with:

```csharp
using System.Globalization;
using Dse.Configuration;
using Dse.Core;
using Dse.Core.Catalogue;
using Dse.Core.Faults;
using Dse.Io;

namespace Dse.Scenarios;

/// <summary>
/// Runs a scenario against a plant. Pure given the two texts — the caller reads
/// the files — so a test runs a whole scenario from strings. Nothing ticks
/// until every action has bound.
/// </summary>
public static class ScenarioRunner
{
    private const string BindFix =
        "Use a component, a fault and arguments the plant declares; `dse catalog export` lists every component's faults.";

    /// <summary>Loads the plant, binds every action, and runs only if nothing is wrong.</summary>
    public static ScenarioRunResult Run(Scenario scenario, string plantJson, ComponentCatalogue catalogue)
    {
        ScenarioBinding binding = Bind(scenario, plantJson, catalogue);
        if (binding.Simulation is not { } simulation)
        {
            return new ScenarioRunResult(binding.Diagnostics, null, null);
        }

        simulation.RunFor(scenario.Duration);
        return new ScenarioRunResult(
            [],
            simulation.Events,
            new RunSummary(simulation.Clock.TickCount, simulation.Events.Records.Count, scenario.Timeline.Count));
    }

    /// <summary>
    /// Loads the plant and schedules every action, without ticking: the
    /// simulation a host runs for as long as it likes (plan 8's
    /// <c>dse serve</c>). Null, with every reason, when anything is wrong —
    /// exactly the diagnostics <see cref="Run"/> reports.
    /// </summary>
    public static ScenarioBinding Bind(Scenario scenario, string plantJson, ComponentCatalogue catalogue)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        ArgumentNullException.ThrowIfNull(plantJson);
        ArgumentNullException.ThrowIfNull(catalogue);

        LoadResult load = PlantLoader.Load(plantJson, catalogue, scenario.ToLoadOptions());
        if (!load.IsValid)
        {
            return new ScenarioBinding(PlantDiagnostics(scenario, load), null);
        }

        TimeSpan step = load.Options!.TimeStep;
        if (step.Ticks <= 0)
        {
            return new ScenarioBinding(
                [ScenarioDiagnostics.Error(
                    ScenarioDiagnostics.BadValue,
                    "$.timeStepMs",
                    "\"timeStepMs\" must be at least one tick (0.0001 ms).",
                    "Use the simulation step in milliseconds, such as 10.")],
                null);
        }

        Simulation simulation = load.Builder!.Build();
        var diagnostics = new List<ConfigDiagnostic>();

        if (scenario.Duration.Ticks % step.Ticks != 0L)
        {
            diagnostics.Add(ScenarioDiagnostics.OffTick("$.duration", "The duration", scenario.Duration, step));
        }

        for (int i = 0; i < scenario.Timeline.Count; i++)
        {
            Schedule(simulation, scenario, i, step, diagnostics);
        }

        return diagnostics.Count > 0 ? new ScenarioBinding(diagnostics, null) : new ScenarioBinding([], simulation);
    }

    /// <summary>One line naming the plant, then the plant's own diagnostics unchanged: their codes, their paths, their fixes.</summary>
    private static List<ConfigDiagnostic> PlantDiagnostics(Scenario scenario, LoadResult load)
    {
        int errors = load.Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error);
        var diagnostics = new List<ConfigDiagnostic>(load.Diagnostics.Count + 1)
        {
            ScenarioDiagnostics.Error(
                ScenarioDiagnostics.PlantInvalid,
                "$.plant",
                string.Create(CultureInfo.InvariantCulture,
                    $"The plant '{scenario.PlantPath}' has {errors} error{(errors == 1 ? string.Empty : "s")} of its own; they follow."),
                "Fix the plant file and run the scenario again; `dse validate` reports exactly these errors."),
        };

        diagnostics.AddRange(load.Diagnostics);
        return diagnostics;
    }

    private static void Schedule(
        Simulation simulation, Scenario scenario, int index, TimeSpan step, List<ConfigDiagnostic> diagnostics)
    {
        ScenarioAction action = scenario.Timeline[index];
        string path = string.Create(CultureInfo.InvariantCulture, $"$.timeline[{index}]");

        // "at >= 0" is DSE202 wherever it is checked (spec 3): the parser rejects
        // a negative number as an out-of-range value, and so does this, for a
        // Scenario built in code rather than parsed.
        if (action.At < TimeSpan.Zero)
        {
            diagnostics.Add(ScenarioDiagnostics.Error(
                ScenarioDiagnostics.BadValue,
                $"{path}.at",
                string.Create(CultureInfo.InvariantCulture,
                    $"An action at {ScenarioDiagnostics.Seconds(action.At)} s is before the start of the run."),
                "Move the action to zero or later; \"at\" is seconds from the start of the run."));
            return;
        }

        if (action.At.Ticks % step.Ticks != 0L)
        {
            diagnostics.Add(ScenarioDiagnostics.OffTick($"{path}.at", "The action time", action.At, step));
        }

        if (action.At >= scenario.Duration)
        {
            diagnostics.Add(ScenarioDiagnostics.NotBeforeTheEnd($"{path}.at", action.At, scenario.Duration));
        }

        switch (action)
        {
            case WriteAction write:
                Bind(simulation, write, path, diagnostics);
                break;

            case FaultAction fault:
                Attempt(diagnostics, path, "fault", () => simulation.InjectFaultAt(
                    fault.At, fault.ComponentId, fault.FaultId, new FaultArguments(fault.Arguments.ToArray())));
                break;

            case ClearAction clear:
                Attempt(diagnostics, path, "clear", () => simulation.ClearFaultAt(clear.At, clear.ComponentId, clear.FaultId));
                break;

            default:
                diagnostics.Add(ScenarioDiagnostics.Error(
                    ScenarioDiagnostics.DoesNotBind,
                    path,
                    $"'{action.GetType().Name}' is not an action this runner knows.",
                    "Use a write, a fault or a clear action."));
                break;
        }
    }

    /// <summary>
    /// A write is checked against the directory here (R57), so the message can
    /// name the tag, its kind and the offending value; <see cref="Attempt"/>
    /// still wraps the call, so an engine exception cannot escape as a crash.
    /// </summary>
    private static void Bind(Simulation simulation, WriteAction write, string path, List<ConfigDiagnostic> diagnostics)
    {
        if (!simulation.IO.Directory.TryFind(write.Tag, out TagDescriptor tag))
        {
            diagnostics.Add(ScenarioDiagnostics.Error(
                ScenarioDiagnostics.DoesNotBind,
                $"{path}.write",
                $"There is no tag '{write.Tag}' in this plant.",
                Suggest.Fix(write.Tag, simulation.IO.Directory.Tags.Select(t => t.Name), "tags")));
            return;
        }

        if (tag.ClaimedBy.Length > 0)
        {
            diagnostics.Add(ScenarioDiagnostics.Error(
                ScenarioDiagnostics.DoesNotBind,
                $"{path}.write",
                $"Tag '{tag.Name}' is claimed by {tag.ClaimedBy}; a scenario cannot write it.",
                "Write the inputs of the block that claims it instead; `dse tags` names it."));
            return;
        }

        if (tag.Access != TagAccess.ReadWrite)
        {
            diagnostics.Add(ScenarioDiagnostics.Error(
                ScenarioDiagnostics.DoesNotBind,
                $"{path}.write",
                $"Tag '{tag.Name}' is read-only; a scenario cannot write it.",
                "Write a tag whose access is ReadWrite; `dse tags <plant>` shows each tag's access."));
            return;
        }

        if (write.Value.ToTagValue(tag.Kind) is not { } value)
        {
            diagnostics.Add(ScenarioDiagnostics.Error(
                ScenarioDiagnostics.DoesNotBind,
                $"{path}.value",
                string.Create(CultureInfo.InvariantCulture, $"Tag '{tag.Name}' is a {tag.Kind} tag; {write.Value} is not a {tag.Kind} value."),
                tag.Kind switch
                {
                    TagKind.Bool => "Write true or false.",
                    TagKind.Double => "Write a number, such as 1.5.",
                    _ => "Write a whole number, such as 3.",
                }));
            return;
        }

        Attempt(diagnostics, path, "write", () => simulation.WriteAt(write.At, write.Tag, value));
    }

    /// <summary>
    /// Schedules one action, turning the engine's own "no such thing" into
    /// DSE206 at the JSON path of the part that was wrong (R58). The engine's
    /// messages already name what exists, so they are the message.
    /// </summary>
    private static void Attempt(List<ConfigDiagnostic> diagnostics, string path, string key, Action schedule)
    {
        try
        {
            schedule();
        }
        catch (Exception ex) when (ex is KeyNotFoundException or ArgumentException or InvalidOperationException)
        {
            string where = ex is ArgumentException argument
                ? argument.ParamName switch
                {
                    "faultId" => $"{path}.id",
                    "given" => $"{path}.args",
                    "arguments" => $"{path}.args",
                    _ => $"{path}.{key}",
                }
                : $"{path}.{key}";

            diagnostics.Add(ScenarioDiagnostics.Error(ScenarioDiagnostics.DoesNotBind, where, Sentence(ex.Message), BindFix));
        }
    }

    /// <summary>The exception's own words, without the " (Parameter 'x')" the base class appends, ending in a full stop.</summary>
    private static string Sentence(string message)
    {
        int cut = message.IndexOf(" (Parameter '", StringComparison.Ordinal);
        string text = (cut < 0 ? message : message[..cut]).TrimEnd();
        return text.EndsWith('.') ? text : text + ".";
    }
}
```

(Only `Run` and the new `Bind` differ from the file on `master`: the old
body of `Run` up to the diagnostics check is `Bind`, returning a
`ScenarioBinding` where it returned a `ScenarioRunResult`; `Schedule`,
`Bind(Simulation, WriteAction, …)`, `Attempt`, `Sentence` and
`PlantDiagnostics` are unchanged. Every existing scenario test and golden
passes unchanged.)

Run: `dotnet test tests/Dse.Scenarios.Tests --nologo` — expect PASS, **170**.

- [ ] **Step 3: Give the CLI a cancellation token and the new options**

Replace the whole of `src/Dse.Cli/Dse.Cli.csproj` with:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <AssemblyName>dse</AssemblyName>
    <RootNamespace>Dse.Cli</RootNamespace>
    <PackAsTool>true</PackAsTool>
    <ToolCommandName>dse</ToolCommandName>
    <PackageId>Dse.Cli</PackageId>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\Dse.Core\Dse.Core.csproj" />
    <ProjectReference Include="..\Dse.Components\Dse.Components.csproj" />
    <ProjectReference Include="..\Dse.Configuration\Dse.Configuration.csproj" />
    <ProjectReference Include="..\Dse.Scenarios\Dse.Scenarios.csproj" />
    <ProjectReference Include="..\Dse.Control.Catalogue\Dse.Control.Catalogue.csproj" />
    <ProjectReference Include="..\Dse.Modbus\Dse.Modbus.csproj" />
  </ItemGroup>
  <ItemGroup>
    <InternalsVisibleTo Include="Dse.Cli.Tests" />
  </ItemGroup>
</Project>
```

Replace the whole of `src/Dse.Cli/CliApp.cs` with:

```csharp
using System.Globalization;
using Dse.Components;
using Dse.Control.Catalogue;
using Dse.Core.Catalogue;

namespace Dse.Cli;

/// <summary>The whole CLI as a function of its arguments and two streams, so tests run it in-process.</summary>
public static class CliApp
{
    public static int Run(string[] args, TextWriter stdout, TextWriter stderr) =>
        Run(args, stdout, stderr, CancellationToken.None);

    /// <summary>
    /// As <see cref="Run(string[], TextWriter, TextWriter)"/>; <paramref name="cancellation"/>
    /// stops a long-running command (<c>dse serve</c>) as Ctrl+C does, so a test can.
    /// </summary>
    public static int Run(string[] args, TextWriter stdout, TextWriter stderr, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(stdout);
        ArgumentNullException.ThrowIfNull(stderr);

        ParsedCommandLine parsed = CommandLine.Parse(args);
        if (parsed.Error is not null)
        {
            stderr.Write(parsed.Error + "\n");
            return ExitCodes.Usage;
        }

        if (parsed.HelpRequested)
        {
            stdout.Write(parsed.Command is null ? CommandTable.GeneralHelp() : CommandTable.HelpFor(parsed.Command));
            return ExitCodes.Ok;
        }

        if (OptionValueProblem(parsed) is { } problem)
        {
            stderr.Write($"{problem}\nRun `dse {string.Join(' ', parsed.Command!.Words)} --help` for its options.\n");
            return ExitCodes.Usage;
        }

        CatalogueBuilder builder = new CatalogueBuilder().Add<ComponentsModule>().Add<ControlModule>();
        if (!ModuleLoader.TryLoad(parsed.All(CommandTable.Assembly), builder, out string loadProblem))
        {
            stderr.Write(loadProblem + "\n");
            return ExitCodes.Unreadable;
        }

        return parsed.Command!.Run(new CliContext(parsed, builder.Build(), stdout, stderr, cancellation));
    }

    private static string? OptionValueProblem(ParsedCommandLine parsed)
    {
        CommandSpec command = parsed.Command!;
        if (command.Options.Contains(CommandTable.Format) && parsed.Single(CommandTable.Format) is { } format && format is not ("text" or "json"))
        {
            return $"'--format {format}' is not a format. Use text or json.";
        }

        if (command.Options.Contains(CommandTable.MapFormat) && parsed.Single(CommandTable.MapFormat) is { } mapFormat && mapFormat is not ("text" or "csv" or "fuxa"))
        {
            return $"'--format {mapFormat}' is not a map format. Use text, csv or fuxa.";
        }

        if (parsed.Single(CommandTable.Port) is { } port
            && !(int.TryParse(port, NumberStyles.None, CultureInfo.InvariantCulture, out int number) && number <= 65535))
        {
            return $"'--port {port}' is not a port. Give a whole number from 0 to 65535, such as 5020.";
        }

        if (parsed.Single(CommandTable.Speed) is { } speed
            && !(double.TryParse(speed, NumberStyles.Float, CultureInfo.InvariantCulture, out double factor) && double.IsFinite(factor) && factor > 0.0))
        {
            return $"'--speed {speed}' is not a speed. Give a factor greater than zero, such as 10 or 0.5.";
        }

        if (parsed.Single(CommandTable.TimeStep) is { } step
            && !(double.TryParse(step, NumberStyles.Float, CultureInfo.InvariantCulture, out double ms) && double.IsFinite(ms) && ms > 0.0))
        {
            return $"'--time-step {step}' is not a step. Give milliseconds greater than zero, such as 10 or 0.5.";
        }

        return null;
    }
}
```

(The format check now applies only to commands that take `--format text|json`;
`--port` and `--speed` are checked like `--time-step`.)

In `src/Dse.Cli/CliContext.cs`, replace

```csharp
internal sealed class CliContext(ParsedCommandLine commandLine, ComponentCatalogue catalogue, TextWriter stdout, TextWriter stderr)
{
```

with

```csharp
internal sealed class CliContext(
    ParsedCommandLine commandLine, ComponentCatalogue catalogue, TextWriter stdout, TextWriter stderr, CancellationToken cancellation = default)
{
```

and replace

```csharp
    public TextWriter Err { get; } = stderr;
```

with

```csharp
    public TextWriter Err { get; } = stderr;

    /// <summary>Cancelled when the caller wants a long-running command to stop, as Ctrl+C does.</summary>
    public CancellationToken Cancellation { get; } = cancellation;
```

In `src/Dse.Cli/ExitCodes.cs`, replace

```csharp
    /// <summary>A file or an assembly could not be read, written or loaded.</summary>
```

with

```csharp
    /// <summary>A file or an assembly could not be read, written or loaded, or <c>dse serve</c> could not open its port.</summary>
```

Replace the whole of `src/Dse.Cli/CommandTable.cs` with:

```csharp
namespace Dse.Cli;

/// <summary>An option a command accepts. Every option takes a value.</summary>
internal sealed record OptionSpec(string Name, string ValueName, string Help, bool Repeatable = false);

/// <summary>One command: how it is invoked, what it accepts, what runs it.</summary>
internal sealed record CommandSpec(
    string[] Words,
    string? Argument,
    string Summary,
    OptionSpec[] Options,
    Func<CliContext, int> Run)
{
    public string Invocation => string.Join(' ', Words) + (Argument is null ? string.Empty : $" <{Argument}>");
}

/// <summary>The single source of commands, options and help text.</summary>
internal static class CommandTable
{
    public static readonly OptionSpec Out = new("--out", "file", "Write the output to this file instead of standard output.");
    public static readonly OptionSpec Format = new("--format", "text|json", "How to print results. Default: text.");
    public static readonly OptionSpec TimeStep = new("--time-step", "ms", "Simulation step in milliseconds, overriding the plant's defaults.");
    public static readonly OptionSpec Assembly = new(
        "--assembly", "path", "Load catalogue modules from this assembly, in addition to the shipped components and control blocks.", Repeatable: true);

    public static readonly OptionSpec Expect = new(
        "--expect", "golden.log", "Compare the event log with this file; exit 4 if they differ.");

    public static readonly OptionSpec Scenario = new(
        "--scenario", "scenario.json", "Schedule this scenario's timeline and use its seed, start time and time step. It must name the same plant.");

    public static readonly OptionSpec Port = new("--port", "n", "Serve Modbus TCP on this port; 0 lets the system choose a free one. Default: 5020.");

    public static readonly OptionSpec Speed = new("--speed", "x", "Run this many times faster than real time, such as 10 or 0.5. Default: 1.");

    public static readonly OptionSpec MapFormat = new(
        "--format", "text|csv|fuxa", "How to print the map: a table, CSV, or the tags object of a FUXA Modbus device. Default: text.");

    public static IReadOnlyList<CommandSpec> All { get; } =
    [
        new(["catalog", "export"], null, "Print every component, block, transform, transition, hold and material type as JSON.", [Out, Assembly], Commands.CatalogExport.Run),
        new(["schema", "export"], null, "Print the JSON Schema for plant files, generated from the catalogue.", [Out, Assembly], Commands.SchemaExport.Run),
        new(["validate"], "plant.json", "Load a plant and report every error, each with its fix.", [Format, TimeStep, Assembly], Commands.Validate.Run),
        new(["tags"], "plant.json", "Load and build a plant, then list its tags: name, kind, access, unit, range, description and claimant.", [Format, TimeStep, Assembly], Commands.Tags.Run),
        new(["run"], "scenario.json", "Run a scenario against its plant and print the event log.", [Expect, Out, Format, Assembly], Commands.RunScenario.Run),
        new(["serve"], "plant.json", "Run a plant in real time and serve its tags over Modbus TCP until Ctrl+C.", [Scenario, Port, Speed, Assembly], Commands.Serve.Run),
        new(["modbus-map"], "plant.json", "Print the plant's Modbus register map: area, address, type and tag of every tag.", [MapFormat, Out, Assembly], Commands.ModbusMap.Run),
    ];

    public static string GeneralHelp()
    {
        var lines = new List<string> { "dse — deterministic industrial process simulation engine", string.Empty, "Commands:" };
        int width = All.Max(c => c.Invocation.Length);
        foreach (CommandSpec command in All)
        {
            lines.Add($"  {command.Invocation.PadRight(width)}  {command.Summary}");
        }

        lines.Add(string.Empty);
        lines.Add("Run `dse <command> --help` for a command's options.");
        lines.Add(string.Empty);
        lines.Add("Exit codes: 0 success; 1 the plant or scenario has errors; 2 usage error; " +
                  "3 a file or assembly could not be read, or the port could not be opened; 4 the event log differs from --expect.");
        return string.Join('\n', lines) + "\n";
    }

    public static string HelpFor(CommandSpec command)
    {
        var lines = new List<string> { $"dse {command.Invocation} [options]", string.Empty, command.Summary, string.Empty, "Options:" };
        int width = command.Options.Max(o => o.Name.Length + o.ValueName.Length + 3);
        foreach (OptionSpec option in command.Options)
        {
            lines.Add($"  {$"{option.Name} <{option.ValueName}>".PadRight(width)}  {option.Help}{(option.Repeatable ? " May be repeated." : string.Empty)}");
        }

        return string.Join('\n', lines) + "\n";
    }
}
```

- [ ] **Step 4: Write the failing command tests**

In `tests/Dse.Cli.Tests/Cli.cs`, replace

```csharp
    public static CliRun Run(params string[] args)
    {
        using var stdout = new StringWriter { NewLine = "\n" };
        using var stderr = new StringWriter { NewLine = "\n" };
        int exit = CliApp.Run(args, stdout, stderr);
        return new CliRun(exit, stdout.ToString(), stderr.ToString());
    }
```

with

```csharp
    public static CliRun Run(params string[] args) => Run(CancellationToken.None, args);

    /// <summary>Runs with a cancellation token, which stops <c>dse serve</c> as Ctrl+C does.</summary>
    public static CliRun Run(CancellationToken cancellation, params string[] args)
    {
        using var stdout = new StringWriter { NewLine = "\n" };
        using var stderr = new StringWriter { NewLine = "\n" };
        int exit = CliApp.Run(args, stdout, stderr, cancellation);
        return new CliRun(exit, stdout.ToString(), stderr.ToString());
    }
```

Create `tests/Dse.Cli.Tests/ServeCommandTests.cs`:

```csharp
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace Dse.Cli.Tests;

/// <summary>
/// <c>dse serve</c>'s command line, loading and stopping. A cancelled token
/// stops the run before its first tick, so these tests never wait on the
/// wall clock; the mine-conveyor run over a real connection is
/// <c>Dse.Samples.Tests.ServeTests</c>.
/// </summary>
public partial class ServeCommandTests
{
    [GeneratedRegex(@"^Listening on 0\.0\.0\.0:(\d+) \(Modbus TCP, any unit id\) at 1x real time\. Press Ctrl\+C to stop\.$", RegexOptions.Multiline)]
    private static partial Regex ListeningLine();

    private static CliRun Stopped(params string[] args)
    {
        using var stop = new CancellationTokenSource();
        stop.Cancel();
        return Cli.Run(stop.Token, args);
    }

    [Theory]
    [InlineData("serve a.json --port x")]
    [InlineData("serve a.json --port 65536")]
    [InlineData("serve a.json --port -1")]
    [InlineData("serve a.json --port 50.5")]
    [InlineData("serve a.json --speed 0")]
    [InlineData("serve a.json --speed -2")]
    [InlineData("serve a.json --speed fast")]
    [InlineData("serve a.json --speed NaN")]
    [InlineData("serve a.json --speed Infinity")]
    [InlineData("serve a.json --format json")]
    [InlineData("serve")]
    public void MalformedServeInvocationsAreUsageErrors(string line)
    {
        CliRun run = Cli.Run(line.Split(' '));

        Assert.Equal(ExitCodes.Usage, run.ExitCode);
        Assert.Empty(run.Out);
        Assert.Contains("dse serve --help", run.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void AServedPlantSaysWhatItServesAndWhereAndStopsWhenCancelled()
    {
        string plant = Cli.Plant("claimed-permit.json");

        CliRun run = Stopped("serve", plant, "--port", "0");

        Assert.Equal(ExitCodes.Ok, run.ExitCode);
        Assert.Empty(run.Err);
        string[] lines = run.Out.Split('\n');
        Assert.Equal($"Serving {plant}: 13 tags as 2 coils, 5 discrete inputs, 10 input registers and 2 holding registers.", lines[0]);
        Match listening = ListeningLine().Match(lines[1]);
        Assert.True(listening.Success, lines[1]);
        Assert.NotEqual("0", listening.Groups[1].Value);
        Assert.Equal("Stopped at 2026-01-01 06:00:00.000 after 0 ticks.", lines[2]);
        Assert.Equal(4, lines.Length);
    }

    [Fact]
    public void AScenarioForThePlantIsBoundBeforeServing()
    {
        CliRun run = Stopped("serve", Cli.Plant("minimal.json"), "--scenario", Cli.Scenario("minimal.json"), "--port", "0", "--speed", "4");

        Assert.Equal(ExitCodes.Ok, run.ExitCode);
        Assert.Empty(run.Err);
        Assert.Contains(") at 4x real time.", run.Out, StringComparison.Ordinal);
    }

    [Fact]
    public void AScenarioForAnotherPlantIsAUsageError()
    {
        CliRun run = Stopped("serve", Cli.Plant("claimed-permit.json"), "--scenario", Cli.Scenario("minimal.json"), "--port", "0");

        Assert.Equal(ExitCodes.Usage, run.ExitCode);
        Assert.Empty(run.Out);
        Assert.Contains($"runs the plant '{Cli.Plant("minimal.json")}', not '{Cli.Plant("claimed-permit.json")}'", run.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void AnInvalidPlantBehavesAsValidateDoes()
    {
        CliRun run = Stopped("serve", Cli.Plant("broken.json"), "--port", "0");

        Assert.Equal(ExitCodes.PlantInvalid, run.ExitCode);
        Assert.Empty(run.Out);
        Assert.Contains("DSE102", run.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void AScenarioThatDoesNotBindBehavesAsRunDoes()
    {
        CliRun run = Stopped("serve", Cli.Plant("broken.json"), "--scenario", Cli.Scenario("broken-plant.json"), "--port", "0");

        Assert.Equal(ExitCodes.PlantInvalid, run.ExitCode);
        Assert.Empty(run.Out);
        Assert.Contains("DSE102", run.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void AMissingScenarioFileIsUnreadable()
    {
        CliRun run = Stopped("serve", Cli.Plant("minimal.json"), "--scenario", Cli.Scenario("no-such.json"), "--port", "0");

        Assert.Equal(ExitCodes.Unreadable, run.ExitCode);
        Assert.Contains("Cannot read", run.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void APortInUseExits3AndSaysWhichPort()
    {
        using var holder = new TcpListener(IPAddress.Any, 0);
        holder.Start();
        int port = ((IPEndPoint)holder.LocalEndpoint).Port;

        CliRun run = Stopped("serve", Cli.Plant("minimal.json"), "--port", port.ToString(System.Globalization.CultureInfo.InvariantCulture));

        Assert.Equal(ExitCodes.Unreadable, run.ExitCode);
        Assert.Empty(run.Out);
        Assert.StartsWith($"Cannot listen on port {port}: ", run.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void HelpListsServeAndModbusMapWithTheirOptions()
    {
        CliRun help = Cli.Run("help");
        CliRun serve = Cli.Run("serve", "--help");
        CliRun map = Cli.Run("modbus-map", "--help");

        Assert.Contains("  serve <plant.json>  ", help.Out, StringComparison.Ordinal);
        Assert.Contains("  modbus-map <plant.json>  ", help.Out, StringComparison.Ordinal);
        Assert.Contains("the port could not be opened", help.Out, StringComparison.Ordinal);
        Assert.All(["--scenario <scenario.json>", "--port <n>", "--speed <x>", "--assembly <path>"], o => Assert.Contains(o, serve.Out, StringComparison.Ordinal));
        Assert.All(["--format <text|csv|fuxa>", "--out <file>", "--assembly <path>"], o => Assert.Contains(o, map.Out, StringComparison.Ordinal));
    }
}
```

(19 tests: an 11-row theory and 8 facts. A token cancelled before the call
stops `RunFor` before its first tick, so these never wait on the clock. The
`claimed-permit.json` plant has 13 tags: 2 coils, 5 discrete inputs, 5
input-register values, 1 holding-register value.)

Create `tests/Dse.Cli.Tests/ModbusMapCommandTests.cs`:

```csharp
using System.Text.Json.Nodes;

namespace Dse.Cli.Tests;

public class ModbusMapCommandTests
{
    [Fact]
    public void TheTextMapListsEveryTagByAreaThenAddressWithBothAddressings()
    {
        CliRun run = Cli.Run("modbus-map", Cli.Plant("claimed-permit.json"));

        Assert.Equal(ExitCodes.Ok, run.ExitCode);
        Assert.Empty(run.Err);
        Assert.Equal(
            """
            area               address  offset  type     tag              access     unit      description
            coils                    1       0  Bool     FEED.Enabled     ReadWrite            Feeder enabled
            coils                    2       1  Bool     INT01.Reset      ReadWrite            Clears the latch on a rising edge when every condition is normal
            discrete inputs          1       0  Bool     CHUTE.Full       ReadOnly             At capacity
            discrete inputs          2       1  Bool     FEED.Permit      ReadOnly             Run permit; false stops the feeder (claimed by INT01)
            discrete inputs          3       2  Bool     INT01.Ok         ReadOnly             Not tripped
            discrete inputs          4       3  Bool     INT01.Tripped    ReadOnly             Latched by an abnormal condition
            discrete inputs          5       4  Bool     PILE.Full        ReadOnly             At capacity
            input registers          1       0  Float32  CHUTE.Level      ReadOnly   fraction  Held mass over capacity
            input registers          3       2  Float32  FEED.HopperMass  ReadOnly   kg        Mass in the hopper
            input registers          5       4  Int32    INT01.FirstOut   ReadOnly   count     Index of the condition that tripped, or -1
            input registers          7       6  Float32  PILE.Rate        ReadOnly   kg/s      Receiving rate
            input registers          9       8  Float32  PILE.Received    ReadOnly   kg        Cumulative mass received
            holding registers        1       0  Float32  FEED.Rate        ReadWrite  kg/s      Feed rate

            """,
            run.Out);
    }

    [Fact]
    public void TheCsvMapHasAHeaderAndOneRowPerTagQuotedWhereItMustBe()
    {
        CliRun run = Cli.Run("modbus-map", Cli.Plant("claimed-permit.json"), "--format", "csv");

        Assert.Equal(ExitCodes.Ok, run.ExitCode);
        string[] lines = run.Out.Split('\n');
        Assert.Equal(15, lines.Length);
        Assert.Equal("area,address,offset,type,tag,access,unit,description,claimedBy", lines[0]);
        Assert.Equal("discrete inputs,2,1,Bool,FEED.Permit,ReadOnly,,Run permit; false stops the feeder,INT01", lines[4]);
        Assert.Equal("input registers,5,4,Int32,INT01.FirstOut,ReadOnly,count,\"Index of the condition that tripped, or -1\",", lines[10]);
        Assert.Equal("holding registers,1,0,Float32,FEED.Rate,ReadWrite,kg/s,Feed rate,", lines[13]);
        Assert.Equal(string.Empty, lines[14]);
    }

    [Fact]
    public void TheFuxaMapIsADeviceTagsObjectInDirectoryOrderKeyedByStableIds()
    {
        CliRun run = Cli.Run("modbus-map", Cli.Plant("claimed-permit.json"), "--format", "fuxa");
        CliRun tags = Cli.Run("tags", Cli.Plant("claimed-permit.json"));

        Assert.Equal(ExitCodes.Ok, run.ExitCode);
        JsonObject map = JsonNode.Parse(run.Out)!.AsObject();
        Assert.Equal(
            tags.Out.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => "t_" + l.Split("  ")[0]),
            map.Select(p => p.Key));
        Assert.True(JsonNode.DeepEquals(
            JsonNode.Parse("""
                { "id": "t_INT01.FirstOut", "name": "INT01.FirstOut", "type": "Int32", "memaddress": "300000",
                  "address": "5", "description": "Index of the condition that tripped, or -1 (count)" }
                """),
            map["t_INT01.FirstOut"]));
        Assert.Equal("000000", (string?)map["t_FEED.Enabled"]!["memaddress"]);
        Assert.Equal("100000", (string?)map["t_FEED.Permit"]!["memaddress"]);
        Assert.Equal("400000", (string?)map["t_FEED.Rate"]!["memaddress"]);
        Assert.Equal("Float32", (string?)map["t_FEED.Rate"]!["type"]);
        Assert.Equal("Bool", (string?)map["t_FEED.Enabled"]!["type"]);
    }

    [Fact]
    public void OutWritesTheMapToAFile()
    {
        string path = Path.Combine(Path.GetTempPath(), $"dse-map-{Guid.NewGuid():N}.csv");
        try
        {
            CliRun run = Cli.Run("modbus-map", Cli.Plant("minimal.json"), "--format", "csv", "--out", path);

            Assert.Equal(ExitCodes.Ok, run.ExitCode);
            Assert.Empty(run.Out);
            Assert.StartsWith("area,address,offset,", File.ReadAllText(path), StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("modbus-map a.json --format json")]
    [InlineData("modbus-map a.json --format xml")]
    [InlineData("modbus-map a.json --port 5020")]
    [InlineData("modbus-map")]
    public void MalformedMapInvocationsAreUsageErrors(string line)
    {
        CliRun run = Cli.Run(line.Split(' '));

        Assert.Equal(ExitCodes.Usage, run.ExitCode);
        Assert.Empty(run.Out);
        Assert.Contains("dse modbus-map --help", run.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void AnInvalidPlantBehavesAsValidateDoes()
    {
        CliRun run = Cli.Run("modbus-map", Cli.Plant("broken.json"));

        Assert.Equal(ExitCodes.PlantInvalid, run.ExitCode);
        Assert.Empty(run.Out);
        Assert.Contains("DSE102", run.Err, StringComparison.Ordinal);
    }
}
```

(9 tests: 5 facts and a 4-row theory. The expected text was generated by the
prototype and checked by hand against the `dse tags` listing of
`claimed-permit.json`: one row per tag, by area then address, numbers
right-aligned, trailing spaces trimmed.)

- [ ] **Step 5: Write the commands**

Create `src/Dse.Cli/Commands/ModbusMap.cs`:

```csharp
using System.Globalization;
using System.Text;
using System.Text.Json;
using Dse.Configuration;
using Dse.Core;
using Dse.Core.Catalogue;
using Dse.Modbus;

namespace Dse.Cli.Commands;

/// <summary>
/// <c>dse modbus-map</c> (plan 8): the register map <c>dse serve</c> serves,
/// as a table, as CSV, or as the <c>tags</c> object of a FUXA Modbus device.
/// The table and the CSV list the entries by area, then address; the FUXA
/// object lists them in directory order, as the map assigns them.
/// </summary>
internal static class ModbusMap
{
    private static readonly ModbusArea[] Areas =
        [ModbusArea.Coils, ModbusArea.DiscreteInputs, ModbusArea.InputRegisters, ModbusArea.HoldingRegisters];

    public static int Run(CliContext context)
    {
        int exit = PlantFile.TryBuild(context, out LoadResult? _, out Simulation? simulation);
        if (exit != ExitCodes.Ok)
        {
            return exit;
        }

        RegisterMap map = RegisterMap.Build(simulation!.IO.Directory);
        string payload = context.CommandLine.Single(CommandTable.MapFormat) switch
        {
            "csv" => Csv(map),
            "fuxa" => Fuxa(map),
            _ => Text(map),
        };
        return context.Emit(payload);
    }

    /// <summary>The entries an area holds, by address.</summary>
    private static IEnumerable<RegisterEntry> ByAddress(RegisterMap map) =>
        Areas.SelectMany(area => map.Entries.Where(e => e.Area == area));

    /// <summary>The FUXA <c>memaddress</c> of an area: the leading digit of its six-digit references.</summary>
    internal static string MemoryAddress(ModbusArea area) => area switch
    {
        ModbusArea.Coils => "000000",
        ModbusArea.DiscreteInputs => "100000",
        ModbusArea.InputRegisters => "300000",
        _ => "400000",
    };

    /// <summary>A FUXA tag id: <c>t_</c> and the DSE tag name, so it is stable for a given plant and readable in FUXA's editor (R203).</summary>
    internal static string FuxaId(RegisterEntry entry) => "t_" + entry.Tag.Name;

    private static string Access(RegisterEntry entry) => entry.IsWritable ? "ReadWrite" : "ReadOnly";

    private static string Text(RegisterMap map)
    {
        string[] header = ["area", "address", "offset", "type", "tag", "access", "unit", "description"];
        List<string[]> rows = [header];
        rows.AddRange(ByAddress(map).Select(e => new[]
        {
            RegisterMap.Name(e.Area),
            e.Number.ToString(CultureInfo.InvariantCulture),
            e.Offset.ToString(CultureInfo.InvariantCulture),
            e.DataType,
            e.Tag.Name,
            Access(e),
            e.Tag.Unit,
            e.Tag.ClaimedBy.Length > 0 ? $"{e.Tag.Description} (claimed by {e.Tag.ClaimedBy})" : e.Tag.Description,
        }));

        int[] widths = Enumerable.Range(0, header.Length).Select(c => rows.Max(r => r[c].Length)).ToArray();
        var builder = new StringBuilder();
        foreach (string[] row in rows)
        {
            for (int c = 0; c < row.Length; c++)
            {
                bool numeric = c is 1 or 2;
                string cell = numeric ? row[c].PadLeft(widths[c]) : row[c].PadRight(widths[c]);
                builder.Append(c == 0 ? cell : "  " + cell);
            }

            builder.Append('\n');
        }

        return string.Join('\n', builder.ToString().Split('\n').Select(line => line.TrimEnd()));
    }

    private static string Csv(RegisterMap map)
    {
        var builder = new StringBuilder("area,address,offset,type,tag,access,unit,description,claimedBy\n");
        foreach (RegisterEntry e in ByAddress(map))
        {
            string[] cells =
            [
                RegisterMap.Name(e.Area),
                e.Number.ToString(CultureInfo.InvariantCulture),
                e.Offset.ToString(CultureInfo.InvariantCulture),
                e.DataType,
                e.Tag.Name,
                Access(e),
                e.Tag.Unit,
                e.Tag.Description,
                e.Tag.ClaimedBy,
            ];
            builder.Append(string.Join(',', cells.Select(Quote))).Append('\n');
        }

        return builder.ToString();
    }

    /// <summary>RFC 4180: a cell with a comma, a quote or a line break is quoted, its quotes doubled.</summary>
    private static string Quote(string cell) =>
        cell.AsSpan().IndexOfAny(",\"\n\r") < 0 ? cell : $"\"{cell.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";

    private static string Fuxa(RegisterMap map)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, CatalogueJson.WriterOptions))
        {
            writer.WriteStartObject();
            foreach (RegisterEntry e in map.Entries)
            {
                writer.WriteStartObject(FuxaId(e));
                writer.WriteString("id", FuxaId(e));
                writer.WriteString("name", e.Tag.Name);
                writer.WriteString("type", e.DataType);
                writer.WriteString("memaddress", MemoryAddress(e.Area));
                writer.WriteString("address", e.Number.ToString(CultureInfo.InvariantCulture));
                writer.WriteString("description", e.Tag.Unit.Length > 0 ? $"{e.Tag.Description} ({e.Tag.Unit})" : e.Tag.Description);
                writer.WriteEndObject();
            }

            writer.WriteEndObject();
        }

        return CatalogueJson.Finish(stream);
    }
}
```

Create `src/Dse.Cli/Commands/Serve.cs`:

```csharp
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Dse.Configuration;
using Dse.Core;
using Dse.Modbus;
using Dse.Realtime;
using Dse.Scenarios;

namespace Dse.Cli.Commands;

/// <summary>
/// <c>dse serve</c> (plan 8): runs a plant — and a scenario's timeline, if one
/// is given — paced to the wall clock, and serves its tags over Modbus TCP
/// until Ctrl+C, SIGTERM or the caller's cancellation. The simulation ticks on
/// this thread; the server answers on the thread pool, reading the image the
/// last tick published and queueing writes for the next tick's phase 1.
/// </summary>
internal static class Serve
{
    /// <summary>The port when <c>--port</c> is not given: 502 needs privileges, so the conventional unprivileged alternative.</summary>
    public const int DefaultPort = 5020;

    public static int Run(CliContext context)
    {
        int exit = Load(context, out Simulation? simulation);
        if (exit != ExitCodes.Ok)
        {
            return exit;
        }

        int port = context.CommandLine.Single(CommandTable.Port) is { } text
            ? int.Parse(text, NumberStyles.None, CultureInfo.InvariantCulture)
            : DefaultPort;
        double speed = context.CommandLine.Single(CommandTable.Speed) is { } factor
            ? double.Parse(factor, NumberStyles.Float, CultureInfo.InvariantCulture)
            : 1.0;

        simulation!.Initialize();
        RegisterMap map = RegisterMap.Build(simulation.IO.Directory);

        // Signals are taken before the port opens, so a Ctrl+C or SIGTERM that
        // arrives as soon as "Listening on" is printed stops the run cleanly.
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(context.Cancellation);
        using PosixSignalRegistration interrupt = PosixSignalRegistration.Create(PosixSignal.SIGINT, Stop(stop));
        using PosixSignalRegistration terminate = PosixSignalRegistration.Create(PosixSignal.SIGTERM, Stop(stop));

        var server = new ModbusServer(map, simulation.IO.Snapshot, new CommandBus(simulation.IO));
        try
        {
            IPEndPoint endpoint;
            try
            {
                endpoint = server.Start(new IPEndPoint(IPAddress.Any, port));
            }
            catch (SocketException ex)
            {
                context.Err.Write(string.Create(CultureInfo.InvariantCulture, $"Cannot listen on port {port}: {ex.Message}\n"));
                return ExitCodes.Unreadable;
            }

            context.Out.Write(string.Create(CultureInfo.InvariantCulture, $"""
                Serving {context.CommandLine.Argument}: {Counts(map)}.
                Listening on {endpoint} (Modbus TCP, any unit id) at {speed:R}x real time. Press Ctrl+C to stop.

                """).ReplaceLineEndings("\n"));
            context.Out.Flush();

            ExecutionMode mode = speed == 1.0 ? ExecutionMode.RealTime : ExecutionMode.Scaled;
            new SimulationRunner(simulation, mode, speed).RunFor(TimeSpan.MaxValue, stop.Token);

            context.Out.Write(string.Create(
                CultureInfo.InvariantCulture,
                $"Stopped at {simulation.Clock.Now:yyyy-MM-dd HH:mm:ss.fff} after {simulation.Clock.TickCount} ticks.\n"));
            return ExitCodes.Ok;
        }
        finally
        {
            server.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    /// <summary>One line, such as "124 tags as 36 coils, 59 discrete inputs, 56 input registers and 2 holding registers".</summary>
    internal static string Counts(RegisterMap map)
    {
        int Tags(ModbusArea area) => map.Entries.Count(e => e.Area == area);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{map.Entries.Count} tags as {Tags(ModbusArea.Coils)} coils, {Tags(ModbusArea.DiscreteInputs)} discrete inputs, " +
            $"{map.Size(ModbusArea.InputRegisters)} input registers and {map.Size(ModbusArea.HoldingRegisters)} holding registers");
    }

    private static Action<PosixSignalContext> Stop(CancellationTokenSource stop) => signal =>
    {
        signal.Cancel = true;
        stop.Cancel();
    };

    /// <summary>The plant alone, or the plant bound to <c>--scenario</c>'s timeline. Reports failure as <c>validate</c> and <c>run</c> do.</summary>
    private static int Load(CliContext context, out Simulation? simulation)
    {
        simulation = null;
        string? scenarioPath = context.CommandLine.Single(CommandTable.Scenario);
        if (scenarioPath is null)
        {
            return PlantFile.TryBuild(context, out LoadResult? _, out simulation);
        }

        string plantPath = context.CommandLine.Argument!;
        if (!PlantFile.TryRead(context, scenarioPath, out string scenarioJson))
        {
            return ExitCodes.Unreadable;
        }

        ScenarioParseResult parsed = ScenarioLoader.Parse(scenarioJson);
        if (parsed.Scenario is null)
        {
            PlantFile.ReportDiagnostics(context, scenarioPath, parsed.ToText(), parsed.Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error));
            return ExitCodes.PlantInvalid;
        }

        string named = parsed.Scenario.ResolvePlantPath(scenarioPath);
        if (!string.Equals(named, Path.GetFullPath(plantPath), StringComparison.Ordinal))
        {
            context.Err.Write(
                $"'--scenario {scenarioPath}' runs the plant '{named}', not '{Path.GetFullPath(plantPath)}'.\n" +
                "Give the plant the scenario names, or a scenario written for this plant.\n");
            return ExitCodes.Usage;
        }

        if (!PlantFile.TryRead(context, plantPath, out string plantJson))
        {
            return ExitCodes.Unreadable;
        }

        ScenarioBinding binding = ScenarioRunner.Bind(parsed.Scenario, plantJson, context.Catalogue);
        if (binding.Simulation is null)
        {
            PlantFile.ReportDiagnostics(context, scenarioPath, binding.ToText(), binding.Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error));
            return ExitCodes.PlantInvalid;
        }

        simulation = binding.Simulation;
        return ExitCodes.Ok;
    }
}
```

Run: `dotnet test tests/Dse.Cli.Tests --nologo` — expect PASS, **106**.

- [ ] **Step 6: Write the end-to-end test**

In `tests/Dse.Samples.Tests/Dse.Samples.Tests.csproj`, replace

```xml
  <ItemGroup>
    <ProjectReference Include="..\..\src\Dse.Cli\Dse.Cli.csproj" />
```

with

```xml
  <ItemGroup>
    <Compile Include="..\Shared\ModbusClient.cs" Link="Shared\ModbusClient.cs" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\Dse.Cli\Dse.Cli.csproj" />
```

Create `tests/Dse.Samples.Tests/ServeTests.cs`:

```csharp
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Dse.Cli;
using Dse.Tests.Shared;

namespace Dse.Samples.Tests;

/// <summary>
/// End to end (plan 8 criterion 5): <c>dse serve</c> runs the mine plant
/// in-process at 20 times real time on a port the system picks; a test-side
/// Modbus client writes the start sequence's coil and reads CV001's speed
/// rise past the 1.74 m/s the sequence proves it at. Every wait is bounded.
/// </summary>
public partial class ServeTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    [GeneratedRegex(@"Listening on 0\.0\.0\.0:(\d+) ")]
    private static partial Regex Listening();

    [Fact]
    public async Task AStartSequenceWrittenOverModbusRunsCv001UpToSpeed()
    {
        Dictionary<string, (string Area, int Offset)> map = Map();
        (string startArea, int startCoil) = map["SEQ_START.Start"];
        (string speedArea, int speedRegister) = map["CV001.Speed"];
        (string runningArea, int running) = map["SEQ_START.Running"];
        Assert.Equal(("coils", "input registers", "discrete inputs"), (startArea, speedArea, runningArea));

        var stdout = new LockedWriter();
        var stderr = new LockedWriter();
        using var stop = new CancellationTokenSource();
        Task<int> serving = Task.Run(() => CliApp.Run(
            ["serve", Sample.Plant, "--port", "0", "--speed", "20"], stdout, stderr, stop.Token));
        try
        {
            int port = await PortOf(stdout, stderr, serving);
            using var client = new ModbusClient(new IPEndPoint(IPAddress.Loopback, port));

            Assert.True(ModbusClient.Float(client.ReadInputRegisters(speedRegister, 2), 0) < 0.1f, "CV001 runs before the start.");
            client.WriteCoil(startCoil, true);

            float speed = 0f;
            var clock = Stopwatch.StartNew();
            bool sawRunning = false;
            while (speed < 1.74f)
            {
                Assert.True(clock.Elapsed < Patience, $"CV001 reached only {speed} m/s in {Patience.TotalSeconds} s.");
                await Task.Delay(50);
                sawRunning |= client.ReadDiscreteInputs(running, 1)[0];
                speed = ModbusClient.Float(client.ReadInputRegisters(speedRegister, 2), 0);
            }

            Assert.True(sawRunning, "SEQ_START never read Running.");
            client.WriteCoil(startCoil, false);
        }
        finally
        {
            await stop.CancelAsync();
        }

        Assert.Equal(ExitCodes.Ok, await serving.WaitAsync(Patience));
        Assert.Empty(stderr.Text);
        Assert.Contains("Stopped at 2026-03-02 06:", stdout.Text, StringComparison.Ordinal);
    }

    /// <summary>Every tag's area and wire offset, from <c>dse modbus-map --format csv</c>, as an integrator would read them.</summary>
    private static Dictionary<string, (string Area, int Offset)> Map()
    {
        CliRun run = Cli.Run("modbus-map", Sample.Plant, "--format", "csv");
        Assert.Equal(ExitCodes.Ok, run.ExitCode);
        return run.Out.Split('\n', StringSplitOptions.RemoveEmptyEntries).Skip(1)
            .Select(line => line.Split(','))
            .ToDictionary(cells => cells[4], cells => (cells[0], int.Parse(cells[2], CultureInfo.InvariantCulture)), StringComparer.Ordinal);
    }

    private static async Task<int> PortOf(LockedWriter stdout, LockedWriter stderr, Task<int> serving)
    {
        var clock = Stopwatch.StartNew();
        while (true)
        {
            Match match = Listening().Match(stdout.Text);
            if (match.Success)
            {
                return int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            }

            Assert.False(serving.IsCompleted, $"dse serve exited before listening: {stderr.Text}");
            Assert.True(clock.Elapsed < Patience, "dse serve did not say where it listens.");
            await Task.Delay(20);
        }
    }

    /// <summary>A writer the serving thread writes and the test thread reads.</summary>
    private sealed class LockedWriter : TextWriter
    {
        private readonly Lock _sync = new();
        private readonly StringBuilder _text = new();

        public override Encoding Encoding => Encoding.UTF8;

        public string Text
        {
            get
            {
                lock (_sync)
                {
                    return _text.ToString();
                }
            }
        }

        public override void Write(char value)
        {
            lock (_sync)
            {
                _text.Append(value);
            }
        }

        public override void Write(string? value)
        {
            lock (_sync)
            {
                _text.Append(value);
            }
        }
    }
}
```

Run: `dotnet test tests/Dse.Samples.Tests --nologo --filter "FullyQualifiedName~ServeTests"` — expect PASS, 1, in under 2 s.

- [ ] **Step 7: Try it by hand**

Run: `dotnet run --project src/Dse.Cli -- modbus-map samples/mine-conveyors/plant.json --format csv`
— expect a header and 124 rows; `coils,31,30,Bool,SEQ_START.Start,ReadWrite,,Enters step 1 from idle on a rising edge,`
and `input registers,7,6,Float32,CV001.Speed,ReadOnly,m/s,Measured value,` among them.
Run: `dotnet run --project src/Dse.Cli -- serve samples/mine-conveyors/plant.json --port 0 --speed 0`
— expect exit 2 and `'--speed 0' is not a speed. Give a factor greater than zero, such as 10 or 0.5.`

- [ ] **Step 8: Run everything**

Run: `dotnet build Dse.sln -c Release --nologo` — expect `0 Warning(s)`, `0 Error(s)`.
Run: `dotnet test Dse.sln --nologo` — expect **1706**: 37 / 498 / 189 / 57 / 239 / 170 / 106 / 153 / 27 / 137, and `Dse.Modbus.Tests` 93.
Run: `git diff --stat aca64d9 -- samples tests/Dse.Control.Tests/Golden tests/Dse.Scenarios.Tests/Golden` — expect nothing.
Run: `git status --short -uall` — expect exactly the paths of Step 9.

- [ ] **Step 9: Commit**

```bash
git add src/Dse.Scenarios/ScenarioBinding.cs src/Dse.Scenarios/ScenarioRunner.cs tests/Dse.Scenarios.Tests/ScenarioBindingTests.cs src/Dse.Cli/Dse.Cli.csproj src/Dse.Cli/CliApp.cs src/Dse.Cli/CliContext.cs src/Dse.Cli/CommandTable.cs src/Dse.Cli/ExitCodes.cs src/Dse.Cli/Commands/Serve.cs src/Dse.Cli/Commands/ModbusMap.cs tests/Dse.Cli.Tests/Cli.cs tests/Dse.Cli.Tests/ServeCommandTests.cs tests/Dse.Cli.Tests/ModbusMapCommandTests.cs tests/Dse.Samples.Tests/Dse.Samples.Tests.csproj tests/Dse.Samples.Tests/ServeTests.cs
git commit -F .superpowers/sdd/8/msg-task4.txt
```

with `.superpowers/sdd/8/msg-task4.txt`:

```
feat(cli): serve a plant over Modbus TCP and print its register map

dse serve runs a plant paced to the wall clock, or --speed times it, and
serves its tags over Modbus TCP until Ctrl+C or SIGTERM; --scenario
replays a timeline written for the same plant, bound through the new
ScenarioRunner.Bind. --port 0 picks a free port; a port that cannot be
opened exits 3. dse modbus-map prints the map as a table, as CSV, or as
the tags of a FUXA Modbus device. An end-to-end test serves the mine plant
at 20x, starts the sequence over Modbus and reads CV001 up to speed.

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
```

Then `git log -1 --format='%h%n%s%n--%n%b'`.

---

### Task 5: The FUXA stack

**Model:** implementer **opus** (it needs Docker and the browser tools:
load the Playwright MCP tools with `ToolSearch`, e.g.
`select:mcp__plugin_playwright_playwright__browser_navigate,mcp__plugin_playwright_playwright__browser_resize,mcp__plugin_playwright_playwright__browser_take_screenshot,mcp__plugin_playwright_playwright__browser_click,mcp__plugin_playwright_playwright__browser_close`);
reviewer **opus** (R203–R208, Review Focus 5).

**Files:**
- Create: `hmi/fuxa/Dockerfile`, `hmi/fuxa/Dockerfile.dockerignore`, `hmi/fuxa/fuxa.Dockerfile`, `hmi/fuxa/docker-compose.yml`, `hmi/fuxa/smoke-check.sh` (mode 755), `hmi/fuxa/README.md`
- Generate: `hmi/fuxa/mine-conveyors.fuxap.json`
- Create: `tests/Dse.Samples.Tests/FuxaProjectTests.cs` (6 facts)
- Create: `hmi/fuxa/generate-project.py` (the project's source, R205)
- Scratch, never added: `.superpowers/sdd/8/fuxa-tags.json`, screenshots

**Interfaces:**
- Consumes: `dse modbus-map … --format fuxa` and the text map (Task 4); the
  tag ids `t_<name>` (R203); the `dse serve` command line (Task 4).
- Produces: the stack; the FUXA project (device id `dse`, views `v_overview`,
  `v_alarms`, `v_trends`, script `s_pulse`, charts `c_speed`, `c_current`,
  10 alarms); the README Task 6 links to.

- [ ] **Step 1: Write the failing in-sync tests**

Create `tests/Dse.Samples.Tests/FuxaProjectTests.cs`:

```csharp
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using Dse.Cli;

namespace Dse.Samples.Tests;

/// <summary>
/// The committed FUXA project (plan 8 criterion 5, "in sync"): its device is
/// the DSE server, its tags are exactly <c>dse modbus-map … --format fuxa</c>,
/// and everything its views, alarms, charts and script reference is one of
/// them — and writable where the HMI writes it.
/// </summary>
public class FuxaProjectTests
{
    private static JsonObject Project() =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(RepositoryRoot(), "hmi", "fuxa", "mine-conveyors.fuxap.json")))!.AsObject();

    private static JsonObject Tags(JsonObject project) => project["devices"]!["dse"]!["tags"]!.AsObject();

    [Fact]
    public void TheDeviceTagsAreExactlyTheModbusMapOfTheMinePlant()
    {
        CliRun map = Cli.Run("modbus-map", Path.Combine(RepositoryRoot(), "samples", "mine-conveyors", "plant.json"), "--format", "fuxa");

        Assert.Equal(ExitCodes.Ok, map.ExitCode);
        JsonObject expected = JsonNode.Parse(map.Out)!.AsObject();
        JsonObject actual = Tags(Project());
        Assert.Equal(expected.Select(p => p.Key), actual.Select(p => p.Key));
        Assert.All(expected, p => Assert.True(JsonNode.DeepEquals(p.Value, actual[p.Key]), $"Tag '{p.Key}' differs from the map: {actual[p.Key]?.ToJsonString()}"));
    }

    [Fact]
    public void TheDeviceIsTheDseServerPolledEvery200Milliseconds()
    {
        JsonNode device = Project()["devices"]!["dse"]!;

        Assert.Equal("ModbusTCP", (string?)device["type"]);
        Assert.True((bool)device["enabled"]!);
        Assert.Equal(200, (int)device["polling"]!);
        Assert.Equal("dse:5020", (string?)device["property"]!["address"]);
        Assert.Equal("1", (string?)device["property"]!["slaveid"]);
        Assert.Equal("TcpPort", (string?)device["property"]!["connectionOption"]);
    }

    [Fact]
    public void EveryTagTheHmiReferencesIsADeviceTag()
    {
        JsonObject project = Project();
        JsonObject tags = Tags(project);
        var referenced = new List<string>();
        foreach (string section in new[] { "hmi", "alarms", "charts", "scripts" })
        {
            Collect(project[section], referenced);
        }

        Assert.NotEmpty(referenced);
        Assert.All(referenced, id => Assert.True(tags.ContainsKey(id), $"The HMI references '{id}', which the device does not have."));
    }

    [Fact]
    public void EveryTagTheHmiWritesIsACoil()
    {
        JsonObject project = Project();
        JsonObject tags = Tags(project);
        var written = new List<string>();
        foreach (JsonNode? item in project["hmi"]!["views"]!.AsArray().SelectMany(v => v!["items"]!.AsObject().Select(p => p.Value)))
        {
            foreach (JsonNode? ev in item!["property"]?["events"]?.AsArray() ?? [])
            {
                Collect(ev!["actoptions"], written);
            }
        }

        Assert.Equal(17, written.Count);
        Assert.All(written, id => Assert.Equal("000000", (string?)tags[id]!["memaddress"]));
    }

    [Fact]
    public void TheProjectHasTheThreeViewsAndEachItemIsAnElementOfItsView()
    {
        JsonArray views = Project()["hmi"]!["views"]!.AsArray();

        Assert.Equal(["Overview", "Alarms", "Trends"], views.Select(v => (string?)v!["name"]));
        Assert.All(views, view =>
        {
            string svg = (string)view!["svgcontent"]!;
            Assert.All(view["items"]!.AsObject(), item => Assert.Contains($"id=\"{item.Key}\"", svg, StringComparison.Ordinal));
        });
    }

    [Fact]
    public void TheReadmesRegisterMapIsTheOneModbusMapPrints()
    {
        string readme = File.ReadAllText(Path.Combine(RepositoryRoot(), "hmi", "fuxa", "README.md")).ReplaceLineEndings("\n");
        int start = readme.IndexOf("```text\n", readme.IndexOf("## Register map\n", StringComparison.Ordinal), StringComparison.Ordinal) + "```text\n".Length;
        string quoted = readme[start..readme.IndexOf("```\n", start, StringComparison.Ordinal)];

        CliRun map = Cli.Run("modbus-map", Path.Combine(RepositoryRoot(), "samples", "mine-conveyors", "plant.json"));

        Assert.Equal(map.Out, quoted);
    }

    /// <summary>Every <c>t_…</c> tag id in a subtree: a string value that is one, or a comma-separated list of them.</summary>
    private static void Collect(JsonNode? node, List<string> ids)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (KeyValuePair<string, JsonNode?> property in obj)
                {
                    Collect(property.Value, ids);
                }

                break;
            case JsonArray array:
                foreach (JsonNode? element in array)
                {
                    Collect(element, ids);
                }

                break;
            case JsonValue value when value.TryGetValue(out string? text) && text.StartsWith("t_", StringComparison.Ordinal):
                ids.AddRange(text.Split(','));
                break;
        }
    }

    /// <summary>Two levels up from this file is the repository root.</summary>
    private static string RepositoryRoot([CallerFilePath] string thisFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", ".."));
}
```

(6 facts. `EveryTagTheHmiWritesIsACoil` counts 17 written tag ids: 2 + 2 + 3
+ 4 pulsed by the four push-buttons, and 6 toggled.)

Run: `dotnet test tests/Dse.Samples.Tests --nologo --filter "FullyQualifiedName~FuxaProjectTests"`
Expected: `Failed: 6` — `DirectoryNotFoundException` (no `hmi/fuxa/`).

- [ ] **Step 2: Write the images and the compose file**

Create `hmi/fuxa/Dockerfile`:

```dockerfile
# The dse command line in a container: built from this repository's source
# with the .NET SDK, run on the .NET runtime, with the samples beside it.
# Build context: the repository root (see docker-compose.yml).

FROM mcr.microsoft.com/dotnet/sdk:10.0.401-noble@sha256:e70cdb7f80b0348f5cb85f19a8f670fca061f033d57eed12fa003d58b0e06317 AS build
WORKDIR /src
COPY Directory.Build.props ./
COPY src/ src/
RUN dotnet publish src/Dse.Cli/Dse.Cli.csproj -c Release -o /app --nologo

FROM mcr.microsoft.com/dotnet/runtime:10.0.12-noble@sha256:b89586dc17781f25531909993658aa8161205ae38b8cec8847df4a8221a403d5
WORKDIR /dse
COPY --from=build /app /app
COPY samples/ samples/
EXPOSE 5020
ENTRYPOINT ["/app/dse"]
CMD ["serve", "samples/mine-conveyors/plant.json", "--port", "5020"]
```

Create `hmi/fuxa/Dockerfile.dockerignore` (BuildKit reads a
`<Dockerfile>.dockerignore` beside the Dockerfile in place of the context's
`.dockerignore`, so the repository root's `bin/`, `obj/` and `.git/` stay
out of the build):

```
# The build context is the repository root; send only what the image needs.
*
!Directory.Build.props
!src/
!samples/
**/bin/
**/obj/
```

Create `hmi/fuxa/fuxa.Dockerfile`:

```dockerfile
# FUXA with its Modbus driver. The frangoteam/fuxa image ships without the
# modbus-serial package: FUXA installs it as a plugin, from npm, at run time.
# Installing it here, at the version FUXA's plugin registry names, makes the
# stack start the same way every time, with no download after the build.

FROM frangoteam/fuxa:1.3.4@sha256:3778da3377e7685842495497d13157d1548ccad8708c434dc5d4b99fb5cb25bf
WORKDIR /usr/src/app/FUXA/server
RUN npm install --no-audit --no-fund --save-exact modbus-serial@8.0.19
```

Create `hmi/fuxa/docker-compose.yml` (in the `fuxa-init` command, `$$` is
Compose's escape for a literal `$`):

```yaml
# The mine-conveyor sample as a SCADA would see it: DSE serves the plant over
# Modbus TCP, FUXA polls it and shows the HMI on http://localhost:1881, and a
# one-shot container loads the HMI project into FUXA on its first start.
# Run from this folder: docker compose up --build

name: dse-hmi

services:
  dse:
    build:
      context: ../..
      dockerfile: hmi/fuxa/Dockerfile
    image: dse-hmi/dse:local
    command: ["serve", "samples/mine-conveyors/plant.json", "--port", "5020"]
    ports:
      - "5020:5020"

  fuxa:
    build:
      context: .
      dockerfile: fuxa.Dockerfile
    image: dse-hmi/fuxa:1.3.4-modbus
    depends_on:
      - dse
    ports:
      - "1881:1881"
    volumes:
      - fuxa-appdata:/usr/src/app/FUXA/server/_appdata
      - fuxa-db:/usr/src/app/FUXA/server/_db
      - fuxa-logs:/usr/src/app/FUXA/server/_logs

  fuxa-init:
    image: curlimages/curl:8.22.0@sha256:58adaa4e8dca9c988bae2aba4ab3434a0bb2da16bbe3f92dec39ec7785166777
    depends_on:
      - fuxa
    volumes:
      - ./mine-conveyors.fuxap.json:/project/mine-conveyors.fuxap.json:ro
    entrypoint: ["/bin/sh", "-c"]
    command:
      - |
        tries=0
        until curl -fs -o /dev/null http://fuxa:1881/api/settings; do
          tries=$$((tries + 1))
          if [ "$$tries" -ge 120 ]; then echo "FUXA did not answer within 120 s." >&2; exit 1; fi
          sleep 1
        done
        if curl -fsS http://fuxa:1881/api/project | grep -q '"address":"dse:5020"'; then
          echo "FUXA already has the DSE project; leaving it as it is."
          exit 0
        fi
        curl -fsS -X POST -H 'Content-Type: application/json' \
          --data-binary @/project/mine-conveyors.fuxap.json http://fuxa:1881/api/project
        echo "Loaded the mine-conveyors project into FUXA."
    restart: "no"

volumes:
  fuxa-appdata:
  fuxa-db:
  fuxa-logs:
```

Create `hmi/fuxa/smoke-check.sh`:

```sh
#!/bin/sh
# Smoke check for the FUXA stack (plan 8). Run from hmi/fuxa with the stack up
# (docker compose up -d --build). Exits 0 when FUXA has loaded the project,
# reads CV001's speed over Modbus, and the start-sequence script runs the
# line up to speed; prints what failed and exits 1 otherwise.
set -eu
FUXA=${FUXA:-http://localhost:1881}

speed() {
  curl -fsS "$FUXA/api/getTagValue?ids=%5B%22t_CV001.Speed%22%5D" |
    sed -n 's/.*"value":\([-0-9.eE+]*\).*/\1/p'
}

i=0
until [ -n "$(speed 2>/dev/null || true)" ]; do
  i=$((i + 1))
  if [ "$i" -ge 90 ]; then echo "FAIL: FUXA has no value for t_CV001.Speed after 90 s." >&2; exit 1; fi
  sleep 1
done
echo "FUXA reads CV001.Speed = $(speed) m/s."

curl -fsS -o /dev/null -X POST -H 'Content-Type: application/json' \
  -d '{"params":{"script":{"id":"s_pulse","name":"pulse","parameters":[{"name":"tags","type":"value","value":"t_SEQ_START.Reset,t_SEQ_START.Start"}]},"toLogEvent":false}}' \
  "$FUXA/api/runscript"
echo "Pressed Start line."

i=0
until awk -v v="$(speed)" 'BEGIN { exit !(v >= 1.74) }'; do
  i=$((i + 1))
  if [ "$i" -ge 60 ]; then echo "FAIL: CV001.Speed is $(speed) m/s 60 s after the start; expected 1.74 or more." >&2; exit 1; fi
  sleep 1
done
echo "PASS: CV001.Speed = $(speed) m/s after the start sequence."
```

Run: `chmod 755 hmi/fuxa/smoke-check.sh`

- [ ] **Step 3: Generate the FUXA project**

Create `hmi/fuxa/generate-project.py` (committed: the project's source,
R205; Python 3, standard library only):

```python
"""Writes the FUXA project hmi/fuxa/mine-conveyors.fuxap.json, its source (plan 8).

From the repository root:
    dotnet run --project src/Dse.Cli -- modbus-map samples/mine-conveyors/plant.json --format fuxa --out <tags.json>
    python3 -I hmi/fuxa/generate-project.py <tags.json> hmi/fuxa/mine-conveyors.fuxap.json
The output is a pure function of the tags: same tags, same bytes. Change the
views, alarms, charts or script here, not in the JSON: regenerating replaces it.
"""
import json
import sys
from xml.sax.saxutils import escape

tags = json.load(open(sys.argv[1]))
out = sys.argv[2]

DEV = "dse"


def t(name):
    tid = "t_" + name
    assert tid in tags, tid
    return tid


# ---------------------------------------------------------------- svg helpers
class View:
    def __init__(self, vid, name, width, height):
        self.vid, self.name, self.w, self.h = vid, name, width, height
        self.svg = []
        self.items = {}

    def raw(self, s):
        self.svg.append(s)

    def text(self, eid, x, y, s, size=14, fill="#263238", anchor="start", weight="normal"):
        self.raw(f'<text id="{eid}" x="{x}" y="{y}" font-family="sans-serif" font-size="{size}" '
                 f'font-weight="{weight}" fill="{fill}" stroke-width="0" text-anchor="{anchor}" '
                 f'xml:space="preserve">{escape(s)}</text>')

    def value(self, key, x, y, tag, unit, digits, size=14, anchor="start", label=None):
        gid = f"VAL_{key}"
        self.raw(f'<g id="{gid}" type="svg-ext-value" fill="#0d47a1" stroke="#0d47a1" font-size="{size}" '
                 f'stroke-width="0" font-family="sans-serif" text-anchor="{anchor}">'
                 f'<text id="VAL_{key}_t" x="{x}" y="{y}" fill="#0d47a1" stroke="#0d47a1" stroke-width="0" '
                 f'font-size="{size}" font-family="sans-serif" text-anchor="{anchor}" xml:space="preserve">##.##</text></g>')
        rng = {"type": 1, "min": 0, "max": 0, "color": "", "stroke": "", "text": unit}
        if digits is not None:
            rng["fractionDigits"] = digits
        self.items[gid] = {"id": gid, "type": "svg-ext-value", "name": label or key,
                           "property": {"events": [], "actions": [], "variableSrc": DEV, "variableId": tag,
                                        "variable": tag, "ranges": [rng]},
                           "label": "Value", "hide": False, "lock": False}

    def shape(self, key, element, tag, ranges, actions=None, label=None):
        """element: an svg element string with id=key."""
        self.raw(element)
        prop = {"events": [], "actions": actions or [], "variableSrc": DEV, "variableId": tag, "variable": tag,
                "ranges": [{"type": 2, "min": lo, "max": hi, "color": c, "stroke": s} for (lo, hi, c, s) in ranges]}
        self.items[key] = {"id": key, "type": "svg-ext-shapes-" + element[1:element.index(" ")], "name": label or key,
                           "property": prop, "label": "Shapes", "hide": False, "lock": False}

    def lamp(self, key, cx, cy, tag, on, off, caption):
        self.shape(key, f'<circle id="{key}" cx="{cx}" cy="{cy}" r="7" fill="{off}" stroke="#37474f" stroke-width="1"/>',
                   tag, [(0, 0, off, ""), (1, 1, on, "")], label=caption)
        self.text(key + "_l", cx + 12, cy + 5, caption, size=12)

    def button(self, key, x, y, w, h, text, events, ranges=None, tag=None, bk="#1565c0", fg="#ffffff"):
        gid = f"HXB_{key}"
        style = (f"width:calc(100% - 6px);height:calc(100% - 6px);text-align:center;background-color:{bk};"
                 f"color:{fg};font-size:13px;font-family:sans-serif;")
        self.raw(f'<g id="{gid}" type="svg-ext-html_button" fill="rgba(0,0,0,0)" '
                 f'stroke="rgba(0,0,0,0)" xml:space="preserve">'
                 f'<rect id="{gid}_r" x="{x}" y="{y}" width="{w}" height="{h}" stroke-width="0"/>'
                 f'<foreignObject id="H-{gid}" x="{x}" y="{y}" width="{w}" height="{h}">'
                 f'<button xmlns="http://www.w3.org/1999/xhtml" id="B-{gid}" class="md-btn  md-btn-raised" style="{style}">{escape(text)}</button>'
                 f'</foreignObject></g>')
        prop = {"events": events, "actions": [], "text": text,
                "ranges": [{"type": 2, "min": lo, "max": hi, "color": c, "stroke": s} for (lo, hi, c, s) in (ranges or [])]}
        if tag:
            prop.update({"variableSrc": DEV, "variableId": tag, "variable": tag})
        self.items[gid] = {"id": gid, "type": "svg-ext-html_button", "name": text, "property": prop,
                           "label": "HtmlButton", "hide": False, "lock": False}

    def control(self, key, ctype, x, y, w, h, prop, label, name):
        prefix = "HXC_" if ctype == "svg-ext-html_chart" else "OXC_"
        gid = f"{prefix}{key}"
        self.raw(f'<g id="{gid}" type="{ctype}" fill="#ffffff" stroke="#000000" '
                 f'stroke-width="1" font-size="14" font-family="sans-serif" text-anchor="right" xml:space="preserve">'
                 f'<rect id="{gid}_r" x="{x}" y="{y}" width="{w}" height="{h}" stroke-width="0" fill="none"/>'
                 f'<foreignObject id="H-{gid}" x="{x}" y="{y}" width="{w}" height="{h}">'
                 f'<div xmlns="http://www.w3.org/1999/xhtml" id="D-{gid}" style="width:100%;height:100%;"></div>'
                 f'</foreignObject></g>')
        self.items[gid] = {"id": gid, "type": ctype, "name": name, "property": prop, "label": label,
                           "hide": False, "lock": False}

    def to_json(self):
        svg = (f'<svg width="{self.w}" height="{self.h}" xmlns="http://www.w3.org/2000/svg" '
               f'xmlns:svg="http://www.w3.org/2000/svg"><g><title>Layer 1</title>' + "".join(self.svg) + "</g></svg>")
        return {"id": self.vid, "name": self.name, "type": "svg",
                "profile": {"width": self.w, "height": self.h, "bkcolor": "#eceff1ff", "margin": 10,
                            "align": "topCenter", "gridType": "fixed", "viewRenderDelay": 0},
                "items": self.items, "variables": {}, "svgcontent": svg, "property": {"events": []}}


PULSE = "s_pulse"


def pulse(tag_list):
    return [{"type": "click", "action": "onRunScript", "actparam": PULSE,
             "actoptions": {"params": [{"name": "tags", "type": "value", "value": ",".join(tag_list)}]}}]


def toggle(tag):
    return [{"type": "click", "action": "onToggleValue", "actparam": "",
             "actoptions": {"variable": {"variableId": tag}}}]


GREY, GREEN, RED, AMBER, DARK = "#9e9e9e", "#2e7d32", "#c62828", "#f9a825", "#37474f"

# ---------------------------------------------------------------- Overview
ov = View("v_overview", "Overview", 1280, 720)
ov.text("ov_title", 24, 40, "Mine conveyors — overview", size=24, weight="bold")
ov.text("ov_sub", 24, 64, "Simulated by DSE over Modbus TCP · belt green running, grey stopped, red tripped", size=13,
        fill="#546e7a")

# Ore source (feeder hopper)
ov.raw('<polygon id="ov_hopper" points="30,100 130,100 110,150 50,150" fill="#8d6e63" stroke="#4e342e" stroke-width="2"/>')
ov.text("ov_hopper_l", 30, 92, "Ore feed", size=14, weight="bold")
ov.lamp("SHE_feed_enabled", 40, 172, t("Feed.Enabled"), GREEN, GREY, "Enabled")
ov.lamp("SHE_feed_ok", 40, 194, t("INT_FEED.Ok"), GREEN, RED, "INT_FEED Ok")
ov.value("feed_mass", 40, 222, t("Feed.HopperMass"), "kg in hopper", 1, size=13)

belts = [
    ("CV001", 140, 160, 380, "CH1", 510),
    ("CV002", 500, 290, 360, "CH2", 850),
    ("CV003", 840, 420, 300, None, None),
]
for (cv, x, y, w, chute, cx) in belts:
    k = cv.lower()
    ov.text(f"ov_{k}_name", x + 60, y - 12, cv, size=16, weight="bold")
    # running/stopped colour by the contactor's auxiliary contact
    ov.shape(f"SHE_{k}_belt",
             f'<rect id="SHE_{k}_belt" x="{x}" y="{y}" width="{w}" height="22" rx="11" ry="11" fill="{GREY}" '
             f'stroke="{DARK}" stroke-width="2"/>',
             t(f"{cv}.Contactor"), [(0, 0, GREY, ""), (1, 1, GREEN, "")], label=f"{cv} belt")
    # tripped overlay: shown while the belt's interlock is latched
    ov.shape(f"SHE_{k}_trip",
             f'<rect id="SHE_{k}_trip" x="{x}" y="{y}" width="{w}" height="22" rx="11" ry="11" fill="{RED}" '
             f'stroke="{DARK}" stroke-width="2"/>',
             t(f"INT_{cv}.Tripped"), [],
             actions=[{"variableId": t(f"INT_{cv}.Tripped"), "variableSrc": DEV, "variable": t(f"INT_{cv}.Tripped"),
                       "bitmask": 0, "range": {"min": 0, "max": 0}, "type": "hide", "options": {}},
                      {"variableId": t(f"INT_{cv}.Tripped"), "variableSrc": DEV, "variable": t(f"INT_{cv}.Tripped"),
                       "bitmask": 0, "range": {"min": 1, "max": 1}, "type": "show", "options": {}}],
             label=f"{cv} tripped")
    ov.raw(f'<circle id="ov_{k}_tail" cx="{x + 11}" cy="{y + 11}" r="5" fill="#eceff1" stroke="{DARK}"/>')
    ov.raw(f'<circle id="ov_{k}_head" cx="{x + w - 11}" cy="{y + 11}" r="5" fill="#eceff1" stroke="{DARK}"/>')
    # values
    ov.value(f"{k}_speed", x, y + 44, t(f"{cv}.Speed"), "m/s", 2, label=f"{cv} speed")
    ov.value(f"{k}_load", x + 110, y + 44, t(f"{cv}.TonnesPerHour"), "t/h", 1, label=f"{cv} load")
    ov.value(f"{k}_current", x + 220, y + 44, t(f"{cv}.Current"), "A", 1, label=f"{cv} current")
    # lamps
    ov.lamp(f"SHE_{k}_contactor", x + 6, y + 64, t(f"{cv}.Contactor"), GREEN, GREY, "Contactor")
    ov.lamp(f"SHE_{k}_safety", x + 106, y + 64, t(f"{cv}.SafetyOk"), GREEN, RED, "Safety relay")
    ov.lamp(f"SHE_{k}_zero", x + 216, y + 64, t(f"{cv}.Stopped"), AMBER, GREY, "Zero speed")
    ov.lamp(f"SHE_{k}_intok", x + 6, y + 86, t(f"INT_{cv}.Ok"), GREEN, RED, f"INT_{cv} Ok")
    if chute:
        ov.raw(f'<polygon id="ov_{chute.lower()}" points="{cx - 20},{y + 4} {cx + 30},{y + 4} {cx + 14},{y + 120} {cx - 4},{y + 120}" '
               f'fill="#b0bec5" stroke="{DARK}" stroke-width="2"/>')
        ov.text(f"ov_{chute.lower()}_l", cx + 36, y + 40, chute, size=13, weight="bold")

# stockpile
ov.raw(f'<polygon id="ov_pile" points="1120,560 1250,560 1185,470" fill="#8d6e63" stroke="#4e342e" stroke-width="2"/>')
ov.text("ov_pile_l", 1120, 584, "Stockpile", size=14, weight="bold")
ov.value("pile_rate", 1120, 604, t("Stockpile.Rate"), "kg/s", 1, size=13, label="Stockpile rate")

# operator panel
px, py = 24, 470
ov.raw(f'<rect id="ov_panel" x="{px - 8}" y="{py - 30}" width="760" height="232" rx="6" fill="#ffffff" stroke="#b0bec5"/>')
ov.text("ov_panel_l", px, py - 10, "Operator", size=15, weight="bold")
ov.button("start", px, py, 170, 40, "Start line",
          pulse([t("SEQ_START.Reset"), t("SEQ_START.Start")]), bk=GREEN)
ov.button("stop", px + 180, py, 170, 40, "Stop line", pulse([t("SEQ_STOP.Reset"), t("SEQ_STOP.Start")]), bk="#ef6c00")
ov.button("safety_reset", px + 360, py, 170, 40, "Reset safety relays",
          pulse([t("CV001.SafetyReset"), t("CV002.SafetyReset"), t("CV003.SafetyReset")]))
ov.button("int_reset", px + 540, py, 190, 40, "Reset interlocks",
          pulse([t("INT_CV003.Reset"), t("INT_CV002.Reset"), t("INT_CV001.Reset"), t("INT_FEED.Reset")]))
ov.value("seq_start_step", px, py + 64, t("SEQ_START.Step"), "", None, size=13, label="SEQ_START step")
ov.text("ov_seq_start_l", px + 20, py + 64, "SEQ_START step", size=13)
ov.value("seq_stop_step", px + 180, py + 64, t("SEQ_STOP.Step"), "", None, size=13, label="SEQ_STOP step")
ov.text("ov_seq_stop_l", px + 200, py + 64, "SEQ_STOP step", size=13)
ov.lamp("SHE_seq_complete", px + 366, py + 60, t("SEQ_START.Complete"), GREEN, GREY, "Start complete")
ov.lamp("SHE_seq_faulted", px + 546, py + 60, t("SEQ_START.Faulted"), RED, GREY, "Start faulted")
for i, cv in enumerate(["CV001", "CV002", "CV003"]):
    k = cv.lower()
    bx = px + i * 245
    ov.button(f"{k}_pullkey", bx, py + 90, 115, 36, f"{cv} pull-key", toggle(t(f"{cv}.PullKey1")),
              ranges=[(0, 0, "#546e7a", "#ffffff"), (1, 1, RED, "#ffffff")], tag=t(f"{cv}.PullKey1"), bk="#546e7a")
    ov.button(f"{k}_estop", bx + 120, py + 90, 115, 36, f"{cv} e-stop", toggle(t(f"{cv}.EStop")),
              ranges=[(0, 0, "#546e7a", "#ffffff"), (1, 1, RED, "#ffffff")], tag=t(f"{cv}.EStop"), bk="#546e7a")
ov.text("ov_toggle_note", px, py + 150, "Pull-key and e-stop buttons latch: red is actuated; press again to restore,",
        size=12, fill="#546e7a")
ov.text("ov_toggle_note2", px, py + 166, "then reset the safety relays and the interlocks, and start the line.", size=12,
        fill="#546e7a")

# ---------------------------------------------------------------- Alarms
al = View("v_alarms", "Alarms", 1280, 720)
al.text("al_title", 24, 40, "Alarms", size=24, weight="bold")
al.text("al_sub", 24, 64, "Current alarms: motor current Hi/HiHi (the sample's ALM_ blocks) and interlock trips", size=13,
        fill="#546e7a")
cols = [("ontime", "Time"), ("text", "Alarm"), ("type", "Priority"), ("group", "Group"), ("status", "Status"),
        ("ack", "Ack")]
widths = {"ontime": 170, "text": 520, "type": 110, "group": 140, "status": 150, "ack": 80}
table = {"id": None, "type": "alarms", "events": [], "options": {
    "paginator": {"show": False}, "filter": {"show": False}, "daterange": {"show": False}, "realtime": False,
    "lastRange": "last1h", "gridColor": "#E0E0E0",
    "header": {"show": True, "height": 32, "fontSize": 13, "background": "#ECEFF1", "color": "#37474F"},
    "row": {"height": 30, "fontSize": 13, "background": "#FFFFFF", "color": "#000000"},
    "selection": {"background": "#3059AF", "color": "#FFFFFF", "fontBold": True},
    "columns": [], "alarmsColumns": [{"id": c, "type": "label", "label": l, "align": "left", "width": widths[c]}
                                     for c, l in cols],
    "alarmFilter": {"filterA": [], "filterB": [], "filterC": []}, "reportsColumns": [],
    "reportFilter": {"filterA": []}, "rows": []}}
al.control("alarms", "svg-ext-own_ctrl-table", 24, 90, 1232, 600, table, "HtmlTable", "Current alarms")

alarms = []
for cv in ["CV001", "CV002", "CV003"]:
    for lvl, key in [("Hi", "high"), ("HiHi", "highhigh")]:
        a = {"name": f"ALM_{cv}.{lvl}", "property": {"variableId": t(f"ALM_{cv}.{lvl}.Active"), "permission": None},
             "highhigh": {"enabled": False}, "high": {"enabled": False}, "low": {"enabled": False},
             "info": {"enabled": False}, "actions": {"enabled": False, "values": []}}
        a[key] = {"enabled": True, "checkdelay": 1, "min": 1, "max": 1, "timedelay": 0,
                  "text": f"{cv} motor current {lvl}", "group": "Current", "ackmode": "ackactive",
                  "bkcolor": "#ffcdd2" if key == "highhigh" else "#ffe0b2", "color": "#000000"}
        alarms.append(a)
for intl in ["INT_CV001", "INT_CV002", "INT_CV003", "INT_FEED"]:
    a = {"name": f"{intl}.Tripped", "property": {"variableId": t(f"{intl}.Tripped"), "permission": None},
         "highhigh": {"enabled": False}, "high": {"enabled": True, "checkdelay": 1, "min": 1, "max": 1, "timedelay": 0,
                                                  "text": f"{intl} tripped", "group": "Interlock",
                                                  "ackmode": "ackactive", "bkcolor": "#ffe0b2", "color": "#000000"},
         "low": {"enabled": False}, "info": {"enabled": False}, "actions": {"enabled": False, "values": []}}
    alarms.append(a)

# ---------------------------------------------------------------- Trends
tr = View("v_trends", "Trends", 1280, 720)
tr.text("tr_title", 24, 40, "Trends", size=24, weight="bold")
tr.text("tr_sub", 24, 64, "The last ten minutes, live", size=13, fill="#546e7a")
colors = {"CV001": "#1565c0", "CV002": "#2e7d32", "CV003": "#ef6c00"}
charts = []
for i, (cid, title, field, unit) in enumerate([("c_speed", "Belt speed (m/s)", "Speed", "m/s"),
                                                ("c_current", "Motor current (A)", "Current", "A")]):
    charts.append({"id": cid, "name": title, "lines": [
        {"device": "DSE", "id": t(f"{cv}.{field}"), "name": f"{cv}.{field}", "label": f"{cv} {unit}",
         "color": colors[cv], "yaxis": 1, "lineInterpolation": 0, "lineWidth": 2, "spanGaps": True}
        for cv in ["CV001", "CV002", "CV003"]]})
    opts = {"title": title, "fontFamily": "sans-serif", "legendFontSize": 12, "colorBackground": "rgba(255,255,255,1)",
            "legendBackground": "rgba(255,255,255,0)", "titleHeight": 20, "axisLabelFontSize": 12,
            "labelsDivWidth": 0, "axisLineColor": "rgba(0,0,0,1)", "axisLabelColor": "rgba(0,0,0,1)",
            "legendMode": "always", "series": [], "width": 1232, "height": 290, "decimalsPrecision": 2,
            "realtime": 10, "dateFormat": "YYYY_MM_DD", "timeFormat": "hh_mm_ss", "lastRange": "last8h",
            "gridLineColor": "rgba(0,0,0,0.2)"}
    tr.control(cid, "svg-ext-html_chart", 24, 90 + i * 310, 1232, 290,
               {"id": cid, "type": "realtime1", "options": opts, "events": []}, "HtmlChart", title)

# ---------------------------------------------------------------- project
device = {"id": DEV, "name": "DSE", "type": "ModbusTCP", "enabled": True, "polling": 200,
          "property": {"address": "dse:5020", "port": None, "slot": None, "rack": None, "slaveid": "1",
                       "baudrate": None, "databits": None, "stopbits": None, "parity": None,
                       "connectionOption": "TcpPort", "delay": 10, "socketReuse": None, "forceFC16": False},
          "tags": tags}
script_code = ("const hold = ms => new Promise(resolve => setTimeout(resolve, ms));\n"
               "for (const tag of tags.split(',')) {\n"
               "    await $setTag(tag, true);\n"
               "    await hold(500);\n"
               "    await $setTag(tag, false);\n"
               "}")
project = {
    "version": "1.01",
    "name": "DSE mine conveyors",
    "server": {"id": "0", "name": "FUXA Server", "type": "FuxaServer", "property": {}, "enabled": True, "tags": {}},
    "devices": {DEV: device},
    "hmi": {"views": [ov.to_json(), al.to_json(), tr.to_json()],
            "layout": {"autoresize": False, "start": "v_overview", "showdev": True, "inputdialog": "false",
                       "hidenavigation": False, "theme": "", "zoom": "enabled",
                       "navigation": {"mode": "fix", "type": "block", "bkcolor": "#263238", "fgcolor": "#ffffff",
                                      "items": [{"text": "Overview", "view": "v_overview", "icon": "home", "link": ""},
                                                {"text": "Alarms", "view": "v_alarms", "icon": "notifications",
                                                 "link": ""},
                                                {"text": "Trends", "view": "v_trends", "icon": "show_chart",
                                                 "link": ""}]},
                       "header": {"title": "DSE mine conveyors", "alarms": "fix", "infos": "", "bkcolor": "#ffffff",
                                  "fgcolor": "#000000", "height": 46, "buttonHeight": 36, "fontSize": 13,
                                  "items": [], "itemsAnchor": "left"}}},
    "charts": charts, "graphs": [], "alarms": alarms, "notifications": [],
    "scripts": [{"id": PULSE, "name": "pulse", "code": script_code, "sync": False, "mode": "SERVER",
                 "parameters": [{"name": "tags", "type": "value"}], "permission": None}],
    "reports": [], "texts": [], "recipes": [],
}
json.dump(project, open(out, "w"), indent=2, ensure_ascii=False)
open(out, "a").write("\n")
print("views", [len(v["items"]) for v in project["hmi"]["views"]])
```

Run: `dotnet run --project src/Dse.Cli -- modbus-map samples/mine-conveyors/plant.json --format fuxa --out .superpowers/sdd/8/fuxa-tags.json`
Run: `python3 -I hmi/fuxa/generate-project.py .superpowers/sdd/8/fuxa-tags.json hmi/fuxa/mine-conveyors.fuxap.json`
Expected: `views [45, 1, 2]` (the overview's 45 widgets, the alarm table,
the two charts).
Run: `sha256sum hmi/fuxa/mine-conveyors.fuxap.json`
Expected: `a750709058ce53fb71a15380d465d39531b91cc110941507e36c763dbff3a0fc  hmi/fuxa/mine-conveyors.fuxap.json`
(3159 lines). A different hash means the script or the map was transcribed
differently: compare before going on, and report it; never edit the JSON.

- [ ] **Step 4: Write the HMI's README**

Create `hmi/fuxa/README.md` (the block under "Register map" is exactly what
`dotnet run --project src/Dse.Cli -- modbus-map samples/mine-conveyors/plant.json`
prints — `TheReadmesRegisterMapIsTheOneModbusMapPrints` compares them; if it
fails, replace the block with the command's output and report the
difference):

````markdown
# The mine conveyors in FUXA

The [mine-conveyor sample](../../samples/mine-conveyors/README.md) as an
operator sees it: DSE runs the plant in real time and serves its tags over
Modbus TCP, and [FUXA](https://github.com/frangoteam/FUXA), an open-source web
SCADA, polls them and shows an overview mimic, the alarms and the trends. You
start the line, stop it, pull a pull-key or press an e-stop from the browser,
and watch the plant's PLC logic answer.

```
dse  ──Modbus TCP :5020──▶  FUXA  ──HTTP :1881──▶  your browser
```

## Run it

You need [Docker](https://docs.docker.com/get-docker/) with Compose v2;
nothing else (the images build DSE from this repository). From this folder:

```bash
docker compose up --build
```

then open <http://localhost:1881>. The first start builds two images and takes
a few minutes; later starts take seconds. Three services start:

| service | what it does |
|---|---|
| `dse` | `dse serve samples/mine-conveyors/plant.json --port 5020`: the plant, in real time, on port 5020 |
| `fuxa` | FUXA 1.3.4 with its Modbus driver, on port 1881 |
| `fuxa-init` | waits for FUXA, loads `mine-conveyors.fuxap.json` into it on the first start, and exits |

Stop with Ctrl+C, or `docker compose down` from another terminal. The plant
starts cold every time `dse` starts: every belt stopped and every interlock
tripped, as at power-up.

To check the stack from a terminal instead of a browser, with it running:

```bash
./smoke-check.sh
```

It waits for FUXA to read CV001's speed, presses **Start line** through
FUXA's API, and prints `PASS: CV001.Speed = … m/s after the start sequence.`
once the belt is up to speed (exit 0), or what failed (exit 1).

## The screens

**Overview** is the line from the ore feed to the stockpile. Each belt is
green while its contactor is closed, grey while it is open, and red while its
interlock is tripped; under it are its speed (m/s), load (t/h) and motor
current (A), and lamps for its contactor, its safety relay, its zero-speed
switch and its interlock's `Ok`. At power-up all three belts are red: every
interlock trips on its permissive until the start sequence resets the safety
relays (see "Power-up" in the sample's README).

**Alarms** lists FUXA's alarms: each belt's motor current `Hi` and `HiHi` (the
sample's `ALM_CVn` blocks, with their deadband and 3 s on-delay; FUXA raises
on the blocks' `Active` tags) and each interlock's trip. The bell in the
header counts them; acknowledge a row with its tick.

**Trends** draws the three belts' speeds and motor currents, live, over the
last ten minutes.

## The buttons

| button | writes | what it mirrors |
|---|---|---|
| Start line | `SEQ_START.Reset`, then `SEQ_START.Start` | every scenario's `SEQ_START.Start` at 1 s; [normal start and stop](../../samples/mine-conveyors/README.md#1-normal-start-and-stop) |
| Stop line | `SEQ_STOP.Reset`, then `SEQ_STOP.Start` | the normal stop at 90 s |
| Reset safety relays | `CV001.SafetyReset`, `CV002.SafetyReset`, `CV003.SafetyReset`, in turn | the sequence's first step, by hand |
| Reset interlocks | `INT_CV003.Reset`, `INT_CV002.Reset`, `INT_CV001.Reset`, `INT_FEED.Reset`, in turn | the manual recovery of [a start while the line is tripped](../../samples/mine-conveyors/README.md#9-a-start-while-the-line-is-tripped) |
| CVn pull-key | `CVn.PullKey1`, toggled | [pull-key](../../samples/mine-conveyors/README.md#2-pull-key) |
| CVn e-stop | `CVn.EStop`, toggled | [emergency stop](../../samples/mine-conveyors/README.md#3-emergency-stop) |

The first four are push-buttons. Every command they write acts on a rising
edge and the blocks scan every 100 or 200 ms, so a button does not write the
tag itself: it runs a FUXA server script, `pulse`, that writes each tag true,
holds it for 0.5 s, and writes it false — long enough for any scan to see the
edge, however briefly the button was pressed. **Start line** resets the start
sequence first, because a sequence that has completed (or faulted) accepts a
new start only after a reset.

The pull-keys and e-stops latch, as the real devices do: a red button is
actuated, and pressing it again restores it. Restoring a pull-key does not
restart anything: reset the safety relays and the interlocks, then start the
line — or just press **Start line**, which resets both on its way.

Faults — an overload, a blocked chute, a welded contactor — are not tags, so
the HMI cannot inject them. To watch one, replay a scenario's timeline: in
`docker-compose.yml`, add `"--scenario",
"samples/mine-conveyors/scenarios/chute-blockage.json"` to the `dse` service's
`command`, then `docker compose up`. The scenario starts the line at 1 s and
blocks CH1 at 80 s; CV001's `Hi` alarm raises at about 1 min 53 s, `HiHi`
about 16 s later, and the overload trips at about 3 min 10 s.

## Register map

`dse serve` maps the plant's tags in their directory order: a read-write Bool
is a coil, a read-only one a discrete input; a Double is a big-endian Float32
in two registers and an Int64 a big-endian Int32 in two registers (clamped to
the Int32 range), holding when read-write, input when read-only. A tag a
block claims, such as `CV001.Permit`, is read-only. Any unit id is answered.
`address` is 1-based, as FUXA and most SCADAs show it; `offset` is what goes
on the wire. This is the output of
`dotnet run --project src/Dse.Cli -- modbus-map samples/mine-conveyors/plant.json`
(`--format csv` for a spreadsheet, `--format fuxa` for the FUXA device's
tags):

```text
area               address  offset  type     tag                    access     unit      description
coils                    1       0  Bool     ALM_CV001.Ack          ReadWrite            Acknowledges every outstanding limit on a rising edge
coils                    2       1  Bool     ALM_CV002.Ack          ReadWrite            Acknowledges every outstanding limit on a rising edge
coils                    3       2  Bool     ALM_CV003.Ack          ReadWrite            Acknowledges every outstanding limit on a rising edge
coils                    4       3  Bool     CV001.EStop            ReadWrite            Actuated by the operator
coils                    5       4  Bool     CV001.PullKey1         ReadWrite            Actuated by the operator
coils                    6       5  Bool     CV001.PullKey2         ReadWrite            Actuated by the operator
coils                    7       6  Bool     CV001.Reset            ReadWrite            Overload reset, rising edge
coils                    8       7  Bool     CV001.SafetyReset      ReadWrite            Safety reset, rising edge
coils                    9       8  Bool     CV001.Start            ReadWrite            Run command
coils                   10       9  Bool     CV002.EStop            ReadWrite            Actuated by the operator
coils                   11      10  Bool     CV002.PullKey1         ReadWrite            Actuated by the operator
coils                   12      11  Bool     CV002.PullKey2         ReadWrite            Actuated by the operator
coils                   13      12  Bool     CV002.Reset            ReadWrite            Overload reset, rising edge
coils                   14      13  Bool     CV002.SafetyReset      ReadWrite            Safety reset, rising edge
coils                   15      14  Bool     CV002.Start            ReadWrite            Run command
coils                   16      15  Bool     CV003.EStop            ReadWrite            Actuated by the operator
coils                   17      16  Bool     CV003.PullKey1         ReadWrite            Actuated by the operator
coils                   18      17  Bool     CV003.PullKey2         ReadWrite            Actuated by the operator
coils                   19      18  Bool     CV003.Reset            ReadWrite            Overload reset, rising edge
coils                   20      19  Bool     CV003.SafetyReset      ReadWrite            Safety reset, rising edge
coils                   21      20  Bool     CV003.Start            ReadWrite            Run command
coils                   22      21  Bool     Feed.Enabled           ReadWrite            Feeder enabled
coils                   23      22  Bool     INT_CV001.Reset        ReadWrite            Clears the latch on a rising edge when every condition is normal
coils                   24      23  Bool     INT_CV002.Reset        ReadWrite            Clears the latch on a rising edge when every condition is normal
coils                   25      24  Bool     INT_CV003.Reset        ReadWrite            Clears the latch on a rising edge when every condition is normal
coils                   26      25  Bool     INT_FEED.Reset         ReadWrite            Clears the latch on a rising edge when every condition is normal
coils                   27      26  Bool     SEQ_START.Abort        ReadWrite            Returns to idle on a rising edge
coils                   28      27  Bool     SEQ_START.Hold         ReadWrite            Freezes the step clock on a rising edge
coils                   29      28  Bool     SEQ_START.Reset        ReadWrite            Returns to idle from faulted or complete on a rising edge
coils                   30      29  Bool     SEQ_START.Resume       ReadWrite            Continues a held step on a rising edge
coils                   31      30  Bool     SEQ_START.Start        ReadWrite            Enters step 1 from idle on a rising edge
coils                   32      31  Bool     SEQ_STOP.Abort         ReadWrite            Returns to idle on a rising edge
coils                   33      32  Bool     SEQ_STOP.Hold          ReadWrite            Freezes the step clock on a rising edge
coils                   34      33  Bool     SEQ_STOP.Reset         ReadWrite            Returns to idle from faulted or complete on a rising edge
coils                   35      34  Bool     SEQ_STOP.Resume        ReadWrite            Continues a held step on a rising edge
coils                   36      35  Bool     SEQ_STOP.Start         ReadWrite            Enters step 1 from idle on a rising edge
discrete inputs          1       0  Bool     ALM_CV001.Hi.Acked     ReadOnly             Nothing is outstanding on the Hi limit
discrete inputs          2       1  Bool     ALM_CV001.Hi.Active    ReadOnly             The Hi limit is in alarm
discrete inputs          3       2  Bool     ALM_CV001.HiHi.Acked   ReadOnly             Nothing is outstanding on the HiHi limit
discrete inputs          4       3  Bool     ALM_CV001.HiHi.Active  ReadOnly             The HiHi limit is in alarm
discrete inputs          5       4  Bool     ALM_CV002.Hi.Acked     ReadOnly             Nothing is outstanding on the Hi limit
discrete inputs          6       5  Bool     ALM_CV002.Hi.Active    ReadOnly             The Hi limit is in alarm
discrete inputs          7       6  Bool     ALM_CV002.HiHi.Acked   ReadOnly             Nothing is outstanding on the HiHi limit
discrete inputs          8       7  Bool     ALM_CV002.HiHi.Active  ReadOnly             The HiHi limit is in alarm
discrete inputs          9       8  Bool     ALM_CV003.Hi.Acked     ReadOnly             Nothing is outstanding on the Hi limit
discrete inputs         10       9  Bool     ALM_CV003.Hi.Active    ReadOnly             The Hi limit is in alarm
discrete inputs         11      10  Bool     ALM_CV003.HiHi.Acked   ReadOnly             Nothing is outstanding on the HiHi limit
discrete inputs         12      11  Bool     ALM_CV003.HiHi.Active  ReadOnly             The HiHi limit is in alarm
discrete inputs         13      12  Bool     CH1.Full               ReadOnly             At capacity
discrete inputs         14      13  Bool     CH2.Full               ReadOnly             At capacity
discrete inputs         15      14  Bool     CV001.Contactor        ReadOnly             Contactor closed
discrete inputs         16      15  Bool     CV001.EStop.Ok         ReadOnly             Safety loop healthy
discrete inputs         17      16  Bool     CV001.Permit           ReadOnly             Run permit; false holds the contactor open (claimed by INT_CV001)
discrete inputs         18      17  Bool     CV001.PullKey1.Ok      ReadOnly             Safety loop healthy
discrete inputs         19      18  Bool     CV001.PullKey2.Ok      ReadOnly             Safety loop healthy
discrete inputs         20      19  Bool     CV001.SafetyOk         ReadOnly             Relay energised
discrete inputs         21      20  Bool     CV001.Stopped          ReadOnly             Below the threshold for the delay
discrete inputs         22      21  Bool     CV001.Tripped          ReadOnly             Overload relay tripped
discrete inputs         23      22  Bool     CV002.Contactor        ReadOnly             Contactor closed
discrete inputs         24      23  Bool     CV002.EStop.Ok         ReadOnly             Safety loop healthy
discrete inputs         25      24  Bool     CV002.Permit           ReadOnly             Run permit; false holds the contactor open (claimed by INT_CV002)
discrete inputs         26      25  Bool     CV002.PullKey1.Ok      ReadOnly             Safety loop healthy
discrete inputs         27      26  Bool     CV002.PullKey2.Ok      ReadOnly             Safety loop healthy
discrete inputs         28      27  Bool     CV002.SafetyOk         ReadOnly             Relay energised
discrete inputs         29      28  Bool     CV002.Stopped          ReadOnly             Below the threshold for the delay
discrete inputs         30      29  Bool     CV002.Tripped          ReadOnly             Overload relay tripped
discrete inputs         31      30  Bool     CV003.Contactor        ReadOnly             Contactor closed
discrete inputs         32      31  Bool     CV003.EStop.Ok         ReadOnly             Safety loop healthy
discrete inputs         33      32  Bool     CV003.Permit           ReadOnly             Run permit; false holds the contactor open (claimed by INT_CV003)
discrete inputs         34      33  Bool     CV003.PullKey1.Ok      ReadOnly             Safety loop healthy
discrete inputs         35      34  Bool     CV003.PullKey2.Ok      ReadOnly             Safety loop healthy
discrete inputs         36      35  Bool     CV003.SafetyOk         ReadOnly             Relay energised
discrete inputs         37      36  Bool     CV003.Stopped          ReadOnly             Below the threshold for the delay
discrete inputs         38      37  Bool     CV003.Tripped          ReadOnly             Overload relay tripped
discrete inputs         39      38  Bool     Feed.Permit            ReadOnly             Run permit; false stops the feeder (claimed by INT_FEED)
discrete inputs         40      39  Bool     INT_CV001.Ok           ReadOnly             Not tripped
discrete inputs         41      40  Bool     INT_CV001.Tripped      ReadOnly             Latched by an abnormal condition
discrete inputs         42      41  Bool     INT_CV002.Ok           ReadOnly             Not tripped
discrete inputs         43      42  Bool     INT_CV002.Tripped      ReadOnly             Latched by an abnormal condition
discrete inputs         44      43  Bool     INT_CV003.Ok           ReadOnly             Not tripped
discrete inputs         45      44  Bool     INT_CV003.Tripped      ReadOnly             Latched by an abnormal condition
discrete inputs         46      45  Bool     INT_FEED.Ok            ReadOnly             Not tripped
discrete inputs         47      46  Bool     INT_FEED.Tripped       ReadOnly             Latched by an abnormal condition
discrete inputs         48      47  Bool     PERM_CV001.Ok          ReadOnly             Every condition is normal
discrete inputs         49      48  Bool     PERM_CV002.Ok          ReadOnly             Every condition is normal
discrete inputs         50      49  Bool     PERM_CV003.Ok          ReadOnly             Every condition is normal
discrete inputs         51      50  Bool     SEQ_START.Complete     ReadOnly             The last step finished
discrete inputs         52      51  Bool     SEQ_START.Faulted      ReadOnly             A step timed out
discrete inputs         53      52  Bool     SEQ_START.Held         ReadOnly             The step clock is frozen
discrete inputs         54      53  Bool     SEQ_START.Running      ReadOnly             A step is running
discrete inputs         55      54  Bool     SEQ_STOP.Complete      ReadOnly             The last step finished
discrete inputs         56      55  Bool     SEQ_STOP.Faulted       ReadOnly             A step timed out
discrete inputs         57      56  Bool     SEQ_STOP.Held          ReadOnly             The step clock is frozen
discrete inputs         58      57  Bool     SEQ_STOP.Running       ReadOnly             A step is running
discrete inputs         59      58  Bool     Stockpile.Full         ReadOnly             At capacity
input registers          1       0  Float32  CH1.Level              ReadOnly   fraction  Held mass over capacity
input registers          3       2  Float32  CH2.Level              ReadOnly   fraction  Held mass over capacity
input registers          5       4  Float32  CV001.Current          ReadOnly   A         Measured value
input registers          7       6  Float32  CV001.Speed            ReadOnly   m/s       Measured value
input registers          9       8  Float32  CV001.TonnesPerHour    ReadOnly   t/h       Measured value
input registers         11      10  Float32  CV001.ZeroSpeed.Value  ReadOnly   m/s       Monitored speed
input registers         13      12  Float32  CV002.Current          ReadOnly   A         Measured value
input registers         15      14  Float32  CV002.Speed            ReadOnly   m/s       Measured value
input registers         17      16  Float32  CV002.TonnesPerHour    ReadOnly   t/h       Measured value
input registers         19      18  Float32  CV002.ZeroSpeed.Value  ReadOnly   m/s       Monitored speed
input registers         21      20  Float32  CV003.Current          ReadOnly   A         Measured value
input registers         23      22  Float32  CV003.Speed            ReadOnly   m/s       Measured value
input registers         25      24  Float32  CV003.TonnesPerHour    ReadOnly   t/h       Measured value
input registers         27      26  Float32  CV003.ZeroSpeed.Value  ReadOnly   m/s       Monitored speed
input registers         29      28  Float32  Feed.HopperMass        ReadOnly   kg        Mass in the hopper
input registers         31      30  Int32    INT_CV001.FirstOut     ReadOnly   count     Index of the condition that tripped, or -1
input registers         33      32  Int32    INT_CV002.FirstOut     ReadOnly   count     Index of the condition that tripped, or -1
input registers         35      34  Int32    INT_CV003.FirstOut     ReadOnly   count     Index of the condition that tripped, or -1
input registers         37      36  Int32    INT_FEED.FirstOut      ReadOnly   count     Index of the condition that tripped, or -1
input registers         39      38  Int32    PERM_CV001.FirstOut    ReadOnly   count     Index of the first condition to leave normal, or -1
input registers         41      40  Int32    PERM_CV002.FirstOut    ReadOnly   count     Index of the first condition to leave normal, or -1
input registers         43      42  Int32    PERM_CV003.FirstOut    ReadOnly   count     Index of the first condition to leave normal, or -1
input registers         45      44  Int32    SEQ_START.Step         ReadOnly   count     The step running, or 0 when idle
input registers         47      46  Float32  SEQ_START.StepTime     ReadOnly   s         Time in the current step
input registers         49      48  Int32    SEQ_STOP.Step          ReadOnly   count     The step running, or 0 when idle
input registers         51      50  Float32  SEQ_STOP.StepTime      ReadOnly   s         Time in the current step
input registers         53      52  Float32  Stockpile.Rate         ReadOnly   kg/s      Receiving rate
input registers         55      54  Float32  Stockpile.Received     ReadOnly   kg        Cumulative mass received
holding registers        1       0  Float32  Feed.Rate              ReadWrite  kg/s      Feed rate
```

A write to an address no read-write tag occupies, or to one register of a
two-register value (FC6 always is one), answers exception 02; a non-finite
value, or one outside the tag's range, answers 03; any other function, 01.

## Limits

- **A local demo.** FUXA runs with authentication off and DSE's Modbus server
  has none either: anyone who can reach ports 1881 and 5020 can operate the
  plant. Do not expose them beyond your machine.
- **Edits stay in FUXA's volume.** Whatever you change in FUXA's editor
  (<http://localhost:1881/editor>) is kept in the `fuxa-appdata` volume, and
  `fuxa-init` does not overwrite a project FUXA already has. The committed
  project is generated by `generate-project.py` (see "Changing the
  project"), so an edit you want to keep must be folded back into the
  generator; a project exported from the editor and committed as is would be
  lost at the next regeneration. To go back to the committed project,
  `docker compose down -v` and start again.
- **Real time, not replay.** Under `dse serve` the plant runs on the wall
  clock and the operator's writes land whenever they arrive, so a session is
  not reproducible tick for tick the way `dse run` is.
- **One plant.** The wheel-line HMI is a follow-up.

## Changing the project

`mine-conveyors.fuxap.json` is generated; do not edit it by hand. Change
`generate-project.py`, then, from the repository root:

```bash
dotnet run --project src/Dse.Cli -- modbus-map samples/mine-conveyors/plant.json --format fuxa --out fuxa-tags.json
python3 -I hmi/fuxa/generate-project.py fuxa-tags.json hmi/fuxa/mine-conveyors.fuxap.json
rm fuxa-tags.json
```

Regenerate the same way when the plant's tags change: the tests fail until
the project's device tags equal `dse modbus-map … --format fuxa` again.

## Cleaning up

```bash
docker compose down -v --rmi all
```

removes the containers, the network, FUXA's volumes, and the images the stack
built or pulled. Docker keeps the build cache of the two image builds; `docker
builder prune` removes it.

## Files

| file | what it is |
|---|---|
| `docker-compose.yml` | the three services |
| `Dockerfile` | the `dse` image: the .NET SDK builds `src/Dse.Cli`, the .NET runtime runs it, with `samples/` |
| `Dockerfile.dockerignore` | sends only `Directory.Build.props`, `src/` and `samples/` to that build |
| `fuxa.Dockerfile` | `frangoteam/fuxa:1.3.4` with the `modbus-serial` 8.0.19 driver installed |
| `generate-project.py` | writes `mine-conveyors.fuxap.json` from the register map: its source |
| `mine-conveyors.fuxap.json` | the FUXA project: the `DSE` Modbus device, the three views, the alarms, the trend charts and the `pulse` script |
| `smoke-check.sh` | the scripted check above |
````

- [ ] **Step 5: Run the tests**

Run: `dotnet test tests/Dse.Samples.Tests --nologo` — expect PASS, **143**.
Run: `dotnet build Dse.sln -c Release --nologo` — expect `0 Warning(s)`, `0 Error(s)`.
Run: `dotnet test Dse.sln --nologo` — expect **1712**: the Task 4 counts with Samples 143.

- [ ] **Step 6: Run the stack and smoke-check it**

Docker must be running. Note what exists before you start:
`docker ps -a`, `docker volume ls`, `docker network ls`, `docker images` —
everything listed now must still exist at the end of this step, and nothing
else.

Run: `docker compose -f hmi/fuxa/docker-compose.yml up -d --build` (about a
minute the first time) — expect the three containers `dse-hmi-dse-1`,
`dse-hmi-fuxa-1`, `dse-hmi-fuxa-init-1` created and started.
Run: `docker compose -f hmi/fuxa/docker-compose.yml logs fuxa-init` (wait
about 15 s first, until it has exited) — expect
`Loaded the mine-conveyors project into FUXA.` (and possibly one
`curl: (7) Failed to connect` line from a try before FUXA listened). Never
pass `-f` to `logs` here: it follows and does not return.

Then, in the browser (Playwright): resize to 1500 × 900; navigate to
`http://localhost:1881/home`; wait 5 s; screenshot the overview to
`.playwright-mcp/overview-idle.png` — check: the title "Mine conveyors —
overview", three **red** belts with `0.00 m/s`, `0.0 t/h`, `0.0 A` under
each, every `INT_… Ok` lamp red, the zero-speed lamps amber, the operator
panel with four coloured push-buttons and six grey toggles. Click the
**Start line** button (selector `#B-HXB_start`), wait until the
"Start complete" lamp is green — `#SHE_seq_complete`'s `fill` attribute is
`#2e7d32` — or 45 s have passed
(measured on a cold stack: CV003 alone at 20 s, all three by 45 s),
screenshot to `.playwright-mcp/overview-running.png` — check: three **green** belts at
about 1.93 m/s, `Enabled` and every lamp but zero speed green, the hopper
holding about 0.8 kg. Click the **Alarms** navigation item (selector
`role=button[name="Alarms"]`), wait 4 s, screenshot to
`.playwright-mcp/alarms.png` — check: a table with rows `INT_CV001 tripped`,
`INT_CV002 tripped`, `INT_CV003 tripped`, `INT_FEED tripped`, priority High,
group Interlock. Click **Trends** (selector `role=button[name="Trends"]`),
wait 20 s, screenshot to `.playwright-mcp/trends.png` —
check: two charts, "Belt speed (m/s)" and "Motor current (A)", three
coloured lines each. Close the browser. (The browser check comes first because it starts from
the cold plant; the smoke check below then finds the line already running
and passes on its first read of the speed.) The Playwright tools may only write
under the main checkout's `.playwright-mcp/`: move that folder to
`.superpowers/sdd/8/screenshots/` in your worktree
(`mv /home/keeper/Work/Github/POCs/DSE/.playwright-mcp .superpowers/sdd/8/screenshots`)
so the main checkout's `git status` shows nothing new; quote the checks in
the task report with the screenshots' paths. If a check fails, report what
the screenshot shows; never edit the project JSON by hand (R205).

Run: `hmi/fuxa/smoke-check.sh` — expect, with your own numbers,
`FUXA reads CV001.Speed = 1.9… m/s.`, `Pressed Start line.`,
`PASS: CV001.Speed = 1.9… m/s after the start sequence.`, exit 0. (On a cold
stack it reads about 0 m/s first and passes within about 15 s:
`FUXA reads CV001.Speed = 0.0016007993835955858 m/s.` …
`PASS: CV001.Speed = 1.8446321487426758 m/s after the start sequence.`.)
Run: `docker compose -f hmi/fuxa/docker-compose.yml up -d`, then the same
`logs fuxa-init` — expect the last line
`FUXA already has the DSE project; leaving it as it is.`
Run: `docker compose -f hmi/fuxa/docker-compose.yml stop dse`, then
`docker compose -f hmi/fuxa/docker-compose.yml logs dse` — expect the last
line `Stopped at 2026-03-02 06:… after … ticks.` (SIGTERM, R199).

Clean up:

Run: `docker compose -f hmi/fuxa/docker-compose.yml down -v --rmi all` —
expect the three containers, the network `dse-hmi_default`, the volumes
`dse-hmi_fuxa-appdata`, `dse-hmi_fuxa-db`, `dse-hmi_fuxa-logs` and the images
`dse-hmi/dse:local`, `dse-hmi/fuxa:1.3.4-modbus` and `curlimages/curl…`
removed.
Run: `docker ps -a`, `docker volume ls`, `docker network ls`,
`docker images` — expect exactly the lists noted before the step. (The build
cache stays; it is not a container, volume, network or image. Report its
size from `docker buildx du | tail -1`; do not prune it — other builds on the
machine share it.)

- [ ] **Step 7: Check and commit**

Run: `git status --short -uall` — expect exactly the paths of the `git add` below,
and nothing under `.playwright-mcp/`.
Run: `git ls-files -s hmi/fuxa/smoke-check.sh` after adding — expect mode `100755`.

```bash
git add hmi/fuxa/Dockerfile hmi/fuxa/Dockerfile.dockerignore hmi/fuxa/fuxa.Dockerfile hmi/fuxa/docker-compose.yml hmi/fuxa/smoke-check.sh hmi/fuxa/generate-project.py hmi/fuxa/mine-conveyors.fuxap.json hmi/fuxa/README.md tests/Dse.Samples.Tests/FuxaProjectTests.cs
git commit -F .superpowers/sdd/8/msg-task5.txt
```

with `.superpowers/sdd/8/msg-task5.txt`:

```
feat(hmi): show the mine conveyors in FUXA with one compose up

hmi/fuxa starts three services: dse serves the mine plant over Modbus TCP,
FUXA 1.3.4 with its Modbus driver polls it every 200 ms, and a one-shot
curl loads the HMI project on FUXA's first start. The project is generated
by generate-project.py from dse modbus-map --format fuxa, whose output its
device tags are exactly; its views are an overview
mimic with states, values, lamps and operator buttons, the alarms and two
trend charts. Push-buttons pulse their rising-edge commands through a
FUXA server script. Tests keep the project, the map and the README's map
in step; smoke-check.sh checks the running stack.

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
```

Then `git log -1 --format='%h%n%s%n--%n%b'`.

---

### Task 6: Docs

**Model:** implementer sonnet; reviewer sonnet.

**Files:**
- Modify: `README.md` (two lines in "Command line"; a section "A SCADA on the sample")
- Modify: `docs/architecture.md` (a section "Modbus TCP")
- Modify: `docs/scenarios.md` (one line)
- Modify: `CHANGELOG.md` (the Unreleased entry's sections)
- Modify: `tests/Dse.Samples.Tests/FuxaProjectTests.cs` (one fact)

**Interfaces:**
- Consumes: everything above; `hmi/fuxa/README.md` (Task 5) for the links.
- Produces: nothing other tasks use.

- [ ] **Step 1: Write the failing docs test**

In `tests/Dse.Samples.Tests/FuxaProjectTests.cs`, replace

```csharp
    /// <summary>Every <c>t_…</c> tag id in a subtree: a string value that is one, or a comma-separated list of them.</summary>
```

with

```csharp
    [Fact]
    public void TheRootReadmeTheArchitectureAndTheScenariosPageNameServeAndTheHmi()
    {
        string readme = File.ReadAllText(Path.Combine(RepositoryRoot(), "README.md")).ReplaceLineEndings("\n");
        string architecture = File.ReadAllText(Path.Combine(RepositoryRoot(), "docs", "architecture.md")).ReplaceLineEndings("\n");
        string scenarios = File.ReadAllText(Path.Combine(RepositoryRoot(), "docs", "scenarios.md")).ReplaceLineEndings("\n");

        Assert.Contains("-- serve samples/mine-conveyors/plant.json", readme, StringComparison.Ordinal);
        Assert.Contains("-- modbus-map plant.json", readme, StringComparison.Ordinal);
        Assert.Contains("[FUXA HMI](hmi/fuxa/README.md)", readme, StringComparison.Ordinal);
        Assert.Contains("\n## Modbus TCP\n", architecture, StringComparison.Ordinal);
        Assert.Contains("`Dse.Modbus`", architecture, StringComparison.Ordinal);
        Assert.Contains("dse serve plant.json --scenario scenario.json", scenarios, StringComparison.Ordinal);
    }

    /// <summary>Every <c>t_…</c> tag id in a subtree: a string value that is one, or a comma-separated list of them.</summary>
```

Run: `dotnet test tests/Dse.Samples.Tests --nologo --filter "FullyQualifiedName~TheRootReadmeTheArchitecture"`
Expected: `Failed: 1` on the first `Assert.Contains` (`-- serve samples/mine-conveyors/plant.json`).

- [ ] **Step 2: The root README**

In `README.md`, replace

```bash
dotnet run --project src/Dse.Cli -- run samples/wheel-line/scenarios/slow-press.json      # the second: a slow press over-soaks a billet and the PLC rejects it
```

with

```bash
dotnet run --project src/Dse.Cli -- run samples/wheel-line/scenarios/slow-press.json      # the second: a slow press over-soaks a billet and the PLC rejects it
dotnet run --project src/Dse.Cli -- modbus-map plant.json     # the Modbus register map dse serve serves
dotnet run --project src/Dse.Cli -- serve samples/mine-conveyors/plant.json  # run it in real time on Modbus TCP port 5020 until Ctrl+C
```

(The old line occurs once, inside the "Command line" block.) Then replace

```markdown
## Licence
```

with

````markdown
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
````

(The new section is after "Command line", so `ReleaseTests`' "Getting
started" slice — from its heading to "## Command line" — is unchanged, and
the pinned `[wheel-line sample](samples/wheel-line/README.md)` link stays.)

- [ ] **Step 3: The architecture and scenarios pages**

In `docs/architecture.md`, replace

```markdown
optional `ICommandRecorder`, which is where a scenario recorder attaches.

## Catalogue, schema and loader
```

with

```markdown
optional `ICommandRecorder`, which is where a scenario recorder attaches.

## Modbus TCP

`Dse.Modbus` is the first protocol adapter built on that boundary. It
references `Dse.Io.Abstractions` and `Dse.Realtime` only, and no package.
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
it is queued. `dse serve` puts the two together: it ticks the simulation on
its own thread with `SimulationRunner` in real time (or `--speed` times it),
while the server answers on the thread pool. `hmi/fuxa/` connects the FUXA
web SCADA to it.

## Catalogue, schema and loader
```

In `docs/scenarios.md`, replace

```bash
dse run scenario.json --format json                # the same run, as records
```

with

```bash
dse run scenario.json --format json                # the same run, as records
dse serve plant.json --scenario scenario.json      # the same timeline in real time, served over Modbus TCP
```

- [ ] **Step 4: The changelog's sections**

In `CHANGELOG.md`, replace

```markdown
[plan 8](docs/superpowers/plans/2026-10-07-modbus-fuxa-hmi.md)).

## 1.0.0 — 2026-10-07
```

with

```markdown
[plan 8](docs/superpowers/plans/2026-10-07-modbus-fuxa-hmi.md)).

### Modbus (`Dse.Modbus`)

- A register map built from a plant's tag directory, in directory order: a
  read-write Bool is a coil, a read-only one a discrete input; a Double is a
  big-endian Float32 and an Int64 a saturating big-endian Int32, two holding
  registers when read-write and two input registers when read-only. A claimed
  tag maps read-only.
- A Modbus TCP server, with no external package: function codes 1, 2, 3, 4,
  5, 6, 15 and 16; exceptions 01, 02 and 03; any unit id; several clients at
  once. Reads come from the published tag image; writes go through
  `Dse.Realtime`'s `CommandBus` and land at phase 1 of the next tick.

### Scenarios (`Dse.Scenarios`)

- `ScenarioRunner.Bind` loads a scenario's plant and schedules its timeline
  without running it, for a host that ticks the simulation itself.

### Cli (`dse`)

- `dse serve <plant.json> [--scenario <file>] [--port <n>] [--speed <x>]`
  runs a plant paced to the wall clock and serves it over Modbus TCP until
  Ctrl+C. Exit 3 now also covers a port that cannot be opened.
- `dse modbus-map <plant.json> [--format text|csv|fuxa]` prints the register
  map, or the tags of a FUXA Modbus device.

### HMI (`hmi/fuxa/`)

- The mine-conveyor sample in the FUXA web SCADA, with one
  `docker compose up`: an overview mimic with the line's states, values,
  lamps and operator buttons, an alarms view and a trends view. See
  [its README](hmi/fuxa/README.md).

## 1.0.0 — 2026-10-07
```

(`ReleaseTests` still sees `## Unreleased` then the one release heading; its
area check finds Core … Samples in the 1.0.0 entry as before, and every link
the new sections make resolves.)

- [ ] **Step 5: Run the tests and everything**

Run: `dotnet test tests/Dse.Samples.Tests --nologo` — expect PASS, **144**.
Run: `dotnet build Dse.sln -c Release --nologo` — expect `0 Warning(s)`, `0 Error(s)`.
Run: `dotnet test Dse.sln --nologo` — expect **1713**: 37 / 498 / 189 / 57 / 239 / 170 / 106 / 153 / 27 / 144, and `Dse.Modbus.Tests` 93.
Run: `git diff --stat aca64d9 -- samples tests/Dse.Control.Tests/Golden tests/Dse.Scenarios.Tests/Golden src/Dse.Core src/Dse.Components src/Dse.Control src/Dse.Control.Catalogue src/Dse.Configuration src/Dse.Io.Abstractions src/Dse.Realtime` — expect nothing.
Run: `git status --short -uall` — expect exactly the five paths of Step 6.

- [ ] **Step 6: Commit**

```bash
git add README.md docs/architecture.md docs/scenarios.md CHANGELOG.md tests/Dse.Samples.Tests/FuxaProjectTests.cs
git commit -F .superpowers/sdd/8/msg-task6.txt
```

with `.superpowers/sdd/8/msg-task6.txt`:

```
docs: describe dse serve, the Modbus adapter and the FUXA HMI

The root README lists dse modbus-map and dse serve and points to the FUXA
HMI; the architecture page explains Dse.Modbus on top of the real-time
boundary; the scenarios page shows serving a scenario's timeline; the
changelog's Unreleased entry records Modbus, Scenarios, Cli and the HMI.
A test pins the new lines.

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
```

Then `git log -1 --format='%h%n%s%n--%n%b'`.

---

## Spec coverage

| Spec criterion / section | Task | Tests |
|---|---|---|
| 1. map from the directory, directory order, deterministic | 2 | `RegisterMapTests.EveryTagGetsTheAreaItsKindAndAccessGiveInDirectoryOrderFromZero` (9 rows), `EachAreaIsPackedWithNoGapsAndTwoRegistersPerValue`, `TheSameDirectoryAlwaysGivesTheSameMap` |
| 1. Bool → coil / discrete input; Double/Int64 → holding / input, 2 registers | 2 | the same theory; `ServeTests` (the mine plant's addresses from the CSV) |
| 1. Float32 big-endian, Int32 big-endian saturating | 2 | `ADoubleIsABigEndianSingleHighWordFirst` (6), `AnInt64IsABigEndianInt32ThatSaturates` (9), `AValueOfAnotherKindEncodesAsZeroRatherThanThrowing` |
| 1. a claimed tag maps read-only | 2, 4 | `AClaimedTagMapsToAReadOnlyArea`; `ModbusMapCommandTests.TheTextMap…` (`FEED.Permit … (claimed by INT01)`) |
| 2. MBAP; any unit id echoed | 3 | `AnyUnitIdIsAcceptedAndEchoed` (5), `ARequestSplitAcrossSeveralReadsIsAnsweredOnceWhole`, `TwoRequestsInOneReadAreAnsweredInOrder`, `AFrameWithAForeignProtocolIdOrAnImpossibleLengthClosesTheConnection` (3) |
| 2. FC 1, 2, 3, 4 from the published snapshot | 3 | `ReadCoils…`, `ReadDiscreteInputs…`, `ReadHoldingRegisters…`, `ReadInputRegisters…`, `EveryReadSeesTheImagePublishedWhenItArrives`, `OneRequestsValuesComeFromOneImageWhileTheImageIsReplaced`, `AnImageNotYetPrimedReadsAsZeroesRatherThanFailing` |
| 2. FC 5, 6, 15, 16 through the `CommandBus` | 3 | `WriteSingleCoilQueuesTheBoolAndEchoesTheRequest`, `WriteMultipleCoilsQueuesEachBoolInAddressOrder`, `WriteMultipleRegistersQueuesWholeValuesDecodedByKind`, `AWriteToAnAddressNoReadWriteTagOccupiesOrToHalfAValueIsException02` (FC6, R197) |
| 2. exceptions 01, 02, 03 | 3 | `AnUnsupportedFunctionIsException01` (5), `AReadPastTheEndOfItsAreaIsException02` (7), `AWriteOfPartOfATwoRegisterValueIsException02AndWritesNothing` (4), `ANonFiniteOrOutOfRangeValueIsException03AndNothingOfTheRequestIsWritten` (5), `AReadOfAnIllegalQuantityIsException03` (6), `ACoilValueOtherThanFF00Or0000IsException03` (3), `AMalformedRequestIsException03` (7) |
| 2. several clients; no external package | 3 | `SeveralClientsAtOnceEachGetTheirOwnAnswers`, `DisposingTheServerClosesEveryConnectionAndStopsListening`, `APortInUseFailsToStartWithASocketException`; Task 2 Step 5's `git grep` |
| 3. `dse serve`: load, scenario, real time / `--speed`, until Ctrl+C, prints the address, exit codes | 4 | `ServeCommandTests` (19), `ScenarioBindingTests` (3), `ServeTests.AStartSequenceWrittenOverModbusRunsCv001UpToSpeed`; Task 5 Step 6 (`stop dse` → `Stopped at`) |
| 3. `dse modbus-map` text / csv (1-based and 0-based, type, tag, access, unit, description) / fuxa | 4 | `ModbusMapCommandTests` (9) |
| 4. compose: `dse`, `fuxa` pinned with 3 volumes, `fuxa-init` | 5 | Task 5 Step 6 (run, logs, smoke, cleanup); R204 (driver), R208 (load once) |
| 4. project: ModbusTCP at `dse:5020`, slave 1, 200 ms, tags = the map | 5 | `TheDeviceTagsAreExactlyTheModbusMapOfTheMinePlant`, `TheDeviceIsTheDseServerPolledEvery200Milliseconds` |
| 4. Overview, Alarms, Trends; built and verified against a running FUXA | 5 | `TheProjectHasTheThreeViewsAndEachItemIsAnElementOfItsView`, `EveryTagTheHmiReferencesIsADeviceTag`, `EveryTagTheHmiWritesIsACoil`; Task 5 Step 6's screenshots (R205) |
| 5. tests: protocol, map, end to end, in sync | 2–5 | as above |
| 6. smoke check: up, init POST, live CV001 speed, screenshot, start button | 5 | `hmi/fuxa/smoke-check.sh`; Task 5 Step 6 |
| 7. `hmi/fuxa/README.md` (prerequisites, run, URL, buttons and scenarios, the generated map, limits) | 5 | `TheReadmesRegisterMapIsTheOneModbusMapPrints` |
| 7. `docs/` and the root README mention `dse serve` and the HMI; changelog Unreleased | 1, 6 | `TheRootReadmeTheArchitectureAndTheScenariosPageNameServeAndTheHmi`; `ReleaseTests.TheVersionTheChangelogAndTheReadmeAgree` |
| §2 no change to components, blocks or results | 1–6 | every task's `git diff --stat aca64d9 -- …` (empty); every golden test unchanged |
| Review Focus 1–5 | 2, 3, 5 | the tests named there |

## Test-count arithmetic

Baseline on `aca64d9` (measured): 1581 = 37 + 498 + 189 + 57 + 239 + 167 + 78 +
153 + 27 + 136, of which 1 fails (`ReleaseTests`, R194).

| Task | Added | Changed (count unchanged) | Project | Suite |
|---|---|---|---|---|
| 1 | none | `ReleaseTests.TheVersionTheChangelogAndTheReadmeAgree` (heading assertion) | Samples 136, all passing | 1581 |
| 2 | `RegisterMapTests`: 9 + 1 + 1 + 1 + 1 + 6 + 9 + 1 = 29 | none | Modbus 29 (new) | 1610 |
| 3 | `FunctionCodeTests` 9 + 41 = 50; `ServerTests` 8 + 6 = 14 | none | Modbus 93 | 1674 |
| 4 | `ScenarioBindingTests` 3; `ServeCommandTests` 11 + 8 = 19; `ModbusMapCommandTests` 5 + 4 = 9; `ServeTests` 1 | `Cli.Run` gains an overload (helper only) | Scenarios 170, Cli 106, Samples 137 | 1706 |
| 5 | `FuxaProjectTests` 6 | none | Samples 143 | 1712 |
| 6 | `FuxaProjectTests` 1 | none | Samples 144 | 1713 |

Final: **1713** = 37 Io.Abstractions / 498 Core / 189 Components / 57 Realtime
/ 239 Configuration / 170 Scenarios / 106 Cli / 153 Control / 27
Control.Catalogue / 144 Samples / 93 Modbus (each stage measured in the
prototype worktree). No golden changes.

## Self-review

- Every file this plan creates is shown in full, or — for the FUXA project —
  generated by a script shown in full, with the output's hash; every in-place
  edit gives the exact old and new text, each old text occurring once in its
  file.
- Every count, address, output line, hash and digest was measured on the
  prototype; the FUXA stack was run twice from a clean Docker state and
  checked in a browser, and every Docker object it made was removed.
- The spec's wrong or underspecified points are ruled on: the red baseline
  (R194), the adapter's references (R195), FC6 and partial writes (R197), bad
  frames (R198), the port, signals and exit codes (R199), a scenario for
  another plant (R200), the FUXA version and its missing driver (R204), how
  the views are built (R205), edge-triggered buttons (R206), and loading the
  project once (R208).
- Names checked across tasks: `t_<name>` ids in `ModbusMap.FuxaId`, the
  generator's `t()`, the smoke check (`t_CV001.Speed`, `t_SEQ_START.Reset`,
  `t_SEQ_START.Start`) and the tests; `s_pulse` in the generator and the smoke
  check; `dse:5020` in the generator, `fuxa-init` and
  `TheDeviceIsTheDseServerPolledEvery200Milliseconds`; the `Listening on …`
  line in `Serve.cs`, `ServeCommandTests` and `ServeTests`.
