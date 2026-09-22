# Scenarios, Replay and `dse run` — Design (plan 5b)

Date: 2026-09-22. Addendum to
`2026-09-02-industrial-process-simulation-engine-design.md` (the "main spec"),
refining its sections 4, 13, 14 and 17 for `Dse.Scenarios`, two small seams in
`Dse.Core`, and the `dse run` command. Builds on plan 5a
(`2026-09-20-catalogue-configuration-cli-design.md`), which is merged.

## 1. Scope

Plan 5b, as left by 5a, held three things. It is split again.

**Plan 5b — this document.** Scenario definition as JSON, replay of a scenario
against a plant, golden event logs as the regression artifact, an in-process
recorder proven by a record-and-replay round trip, and `dse run`.

**Plan 5c — a later document.** `Dse.Control` (interlock, permissive,
sequencer, timer, alarm; scan periods scheduled through the event queue) and
the questions that belong with it: alarm state in `LiveState`, and whether
`Dse.Core` grows a periodic-event helper or the blocks self-reschedule.
Scenarios do not need the control blocks; the golden logs this plan produces
are the harness the blocks will be tested against.

Decided here, from 5a's open list: the `EventLog.ToText()` format is kept as
shipped (it is already the golden format by contract, pinned by
`EventLogTests`); scenario recording hooks a new `IActionRecorder` seam on the
simulation rather than `ICommandRecorder` on the command bus (section 6).

Still parked with no consumer: dispatcher failure observability, frame pooling,
CSV telemetry export (main spec 13; arrives with the reference samples that
need a curve), paced execution of `dse run` (`SimulationRunner` already has the
modes; a `--speed` option is added when something live watches the run), a
JSON Schema for scenario files (the format is fixed, not catalogue-driven; a
hand-written schema can ship with the samples).

### Success criteria

1. A scenario file over an existing valid plant runs from the command line and
   prints the event log; running it twice gives byte-identical logs.
2. `dse run --expect` distinguishes "the configuration is broken" (exit 1)
   from "the behaviour changed" (exit 4), and says where the log diverged.
3. Every way a scenario can be wrong is reported before tick 0, with a code, a
   JSON path and a fix; a bad scenario never produces a partial log.
4. A live run driven by `CommandBus` writes and direct fault injections is
   recorded, written as JSON, parsed back and replayed, and the two event logs
   are byte-identical.
5. Zero external package references anywhere under `src/`.

## 2. The scenario file

```json
{
  "plant": "conveyor-line.json",
  "seed": 42,
  "startTime": "2026-01-01T06:00:00Z",
  "timeStepMs": 10,
  "duration": 120,
  "timeline": [
    { "at": 5,    "write": "CV001.Start", "value": true },
    { "at": 30,   "fault": "CV001.Motor", "id": "thermal-bias", "args": { "amount": 0.8 } },
    { "at": 60.5, "clear": "CV001.Motor", "id": "thermal-bias" }
  ]
}
```

| key | required | meaning |
|---|---|---|
| `plant` | yes | Path of the plant file, relative to the directory holding the scenario file. A scenario is a sibling of its plant; a moved directory keeps working. |
| `seed`, `startTime`, `timeStepMs` | no | Same names, types and rules as the plant's `defaults` block (whole number ≥ 0; ISO 8601 with an explicit offset; number > 0). They become `LoadOptions`, so precedence is the existing chain: scenario > plant `defaults` > engine default. |
| `duration` | yes | Seconds from start, > 0, a whole number of ticks. The only field with no plant counterpart; it drives `Simulation.RunFor`. |
| `timeline` | no (default empty) | Ordered list of actions. |

An action has `at` (seconds from start) and exactly one of:

| shape | keys | meaning |
|---|---|---|
| write | `write` (tag name), `value` | The plant sees `value` on the tag at `at`. `value` is a JSON `true`/`false` for a `Bool` tag, a number for a `Double` tag, a whole number for an `Int64` tag. |
| fault | `fault` (component id), `id` (fault id), `args` (object, optional) | Inject the fault. Every arg is a number; an arg the descriptor declares but the file omits takes the descriptor's default. |
| clear | `clear` (component id), `id` | Clear the fault. |

Rules for `at`:

- It must fall exactly on a tick: `60.5` is fine at 10 ms, `60.005` is
  `DSE203` naming the step.
- It must satisfy `at < duration`. `RunFor(120 s)` at 10 ms runs ticks
  0…11999; an action at 120 s would land on tick 12000, which never runs.
- **`at` is the tick at which the plant sees it**, for writes and faults
  alike. Section 6 makes that one rule true in the engine.
- Actions on the same tick fire in file order; the event queue's sequence
  number already guarantees that.

Component ids are the flattened leaf ids the fault API takes today
(`CV001.Motor`); tag names are the directory's names (`CV001.Start`), as
`dse tags` lists them.

Deliberately absent: a `$schema` key, assertions or expectations inside the
file (the golden log is the assertion), an inline plant, a `comment` field
(JSON has none; the file name and the golden log say what a scenario is for).

## 3. Loading and diagnostics

Two pure steps, then one that touches the engine. Nothing ticks until every
check has passed.

**`ScenarioLoader.Parse(string json) → ScenarioParseResult`** — `Scenario?`
plus diagnostics. Structural only: well-formed JSON, known keys, types and
ranges, `duration > 0`, each action has `at ≥ 0` and exactly one shape with
its required companions. No file I/O, no catalogue.

`Scenario` is an immutable record: `PlantPath`, `Seed?`, `StartTime?`,
`TimeStep?`, `Duration`, `Timeline` — a list of `ScenarioAction` records
(`WriteAction(At, Tag, ScenarioValue)`, `FaultAction(At, ComponentId, FaultId,
Args)`, `ClearAction(At, ComponentId, FaultId)`). A write's value stays as
parsed (`ScenarioValue`: bool, double, or whole number) until the tag's kind is
known at scheduling. `Scenario.ResolvePlantPath(scenarioFilePath)` returns the
absolute plant path.

**Plant load** — the caller reads the plant text; `ScenarioRunner` calls
`PlantLoader.Load(plantJson, catalogue, options)` with `LoadOptions` built from
the overrides. If the plant is invalid, the run reports one `DSE205` line
naming the plant path and then the plant's own diagnostics unchanged — their
codes, their paths, their fixes.

**Scheduling with checks** — for each action in order: `at` is on a tick (the
real step is known now), `at < duration`; then `Simulation.WriteAt`,
`InjectFaultAt` or `ClearFaultAt`, each of which resolves its names at schedule
time and throws on a bad one. The runner turns each throw into a `DSE206` with
the action's JSON path (`timeline[1].fault`), collects every diagnostic rather
than stopping at the first, and refuses to run if there are any.

### Codes

Same `ConfigDiagnostic` record, same Fix contract (an imperative first
sentence, an optional second, every sentence ending in a full stop).

| code | title | when |
|---|---|---|
| DSE200 | Scenario is not valid JSON | Parse failure; message carries the parser's position. |
| DSE201 | Unknown key | A key the format does not define; the fix names the nearest known key when one is close. |
| DSE202 | Value missing, of the wrong type, or out of range | `plant` absent, `duration ≤ 0`, `seed` negative, `startTime` without an offset, a non-numeric `arg`, a `value` that is not a JSON bool or number. |
| DSE203 | Time is not on a tick, or not before the end | `at` or `duration` not a multiple of the step; `at ≥ duration`. The message states the step and the offending time. |
| DSE204 | Action is malformed | None or more than one of `write`/`fault`/`clear`; `value` missing on a write; `id` missing on a fault or clear; `args` on a clear. |
| DSE205 | Plant file is invalid | Wrapper line with the plant path; the plant's diagnostics follow. (An unreadable plant file is the CLI's exit 3, not a diagnostic.) |
| DSE206 | Action does not bind to the plant | Unknown tag, read-only tag, value kind does not match the tag, unknown component, unknown fault id, undeclared fault argument. The message says which; the fix points at `dse tags` or the catalogue. |

`ScenarioDiagnostics.All` lists them. `DiagnosticsReference.Render` becomes
`Render(string title, IReadOnlyList<DiagnosticInfo> codes)`; the existing
configuration page keeps its content, and `docs/scenario-diagnostics.md` is
generated beside it and pinned by a golden test. `Dse.Configuration` cannot
see `Dse.Scenarios`, so two pages are cleaner than one page that knows both.

## 4. Running

**`ScenarioRunner.Run(Scenario scenario, string plantJson, ComponentCatalogue
catalogue) → ScenarioRunResult`.** Pure given the two texts: the caller reads
the files, so a test runs a whole scenario from strings. It performs the plant
load and the scheduling-with-checks of section 3, then
`Simulation.RunFor(scenario.Duration)`. The result carries:

- `Diagnostics` and `IsValid` (no diagnostics);
- `Events` — the `EventLog` (structured records, and `ToText()`);
- `Summary` — `RunSummary(long Ticks, int Events, int ActionsScheduled)`.

Execution is as fast as possible. `SimulationRunner`'s paced modes are not
used by 5b.

**Golden comparison** lives beside the runner so the CLI and the tests judge a
mismatch the same way: `GoldenLog.Compare(string expected, string actual) →
LogComparison`. Both texts are normalised (`\r\n` → `\n`, exactly one trailing
newline) before comparison. On mismatch the comparison reports the first
differing line number, three lines of context either side from both texts, and
the two line counts. No unified diff in v1: "here is where it diverged, and
the full actual log is in a file" is the honest, reviewable version, and it is
exactly what `tests/Shared/Golden.cs` already does.

## 5. `dse run`

`dse run <scenario.json> [--expect <golden.log>] [--out <file>]
[--format text|json] [--assembly <path>]...`

| case | stdout | stderr | exit |
|---|---|---|---|
| runs, no `--expect` | the event log text (nothing with `--out`) | | 0 |
| runs, `--expect` matches | `Matched <golden> (<N> events).` | | 0 |
| runs, `--expect` differs | | first divergence with context, the line counts, and the path of the `.actual` file written beside the golden | 4 |
| scenario or plant invalid | (`--format json`: the JSON with `ok: false`) | diagnostics then a count line (`--format text`) | 1 |
| scenario, plant or golden file unreadable; `--out` or `.actual` unwritable | | `Cannot read '…': …` / `Cannot write '…': …` | 3 |
| usage error | | the problem, then a pointer to `--help` | 2 |

`ExitCodes.LogMismatch = 4` is new and deliberate: a script can tell "the
configuration is broken" from "the behaviour changed".

`--format json` emits `{ "ok", "scenario", "plant", "ticks", "events": [
{ "tick", "time", "source", "code", "message" } ], "match" (only with
`--expect`), "diagnostics" }` — structured records rather than the text, for
tooling. Text is the default.

There is no `--update`. `dse run s.json --out golden.log` is how a golden is
made or remade, on purpose, and the change is reviewed in version control.

The command reuses `PlantFile`'s reading and diagnostic rendering; `CliContext`
gains nothing. `--assembly` works as for every other command, so a scenario
over a plant that uses a plugin's component type runs.

## 6. Engine seams

The only changes to `Dse.Core`.

### 6.1 `Simulation.WriteAt`

```csharp
public long WriteAt(TimeSpan fromStart, string tag, TagValue value);
public long WriteIn(TimeSpan delay, string tag, TagValue value);
```

Mirrors `InjectFaultAt/In`. At call time it resolves the tag through
`TagDirectory` and runs exactly the checks `TagImage.Write` runs — unknown
tag, read-only tag, kind mismatch — so a bad action fails at schedule time.
The private `WriteEvent` fires in phase 1 during the event drain and applies
the value immediately through a new `internal TagImage.ApplyNow(int index,
TagValue value, in TickContext ctx)`, which logs `WRITE` exactly as a queued
write does (`source` = tag name, message `Set to {value}.`).

Order within a tick: queued (external) writes land first, in enqueue order;
then scheduled events in sequence order, writes and faults interleaved as
scheduled. This is what makes "`at` is the tick at which the plant sees it"
one rule for both action kinds — a write replayed through `IO.Write` would
land a tick late.

### 6.2 `IActionRecorder`

```csharp
public interface IActionRecorder
{
    void Wrote(long tick, string tag, TagValue value);
    void Faulted(long tick, string componentId, string faultId, FaultArguments arguments);
    void Cleared(long tick, string componentId, string faultId);
}
public void Simulation.AttachActionRecorder(IActionRecorder recorder);
```

One recorder, attached any time before or during a run; a second attach
throws, as `AttachFrameSink` does. It is called from the three places an
action lands — `TagImage.ApplyPendingWrites` (per applied write),
`TagImage.ApplyNow`, `FaultEvent.Apply` (inject and clear) — so it sees every
action that took effect, stamped with the tick it took effect, whichever path
it came by: scenario, `CommandBus`, or test code. `arguments` is the resolved
set (defaults filled in), so a recording is explicit and does not depend on a
descriptor's defaults staying put.

`ICommandRecorder` on the command bus is unchanged. It also sees rejected
commands, which is an audit concern, not a replay one.

## 7. Recording

`ScenarioRecorder : IActionRecorder` accumulates actions in landing order.
`ToScenario(string plantPath, SimulationOptions options, TimeSpan duration)`
returns a `Scenario` whose overrides are the run's actual seed, start time and
step, and whose timeline is the recording with `at = tick × step`.

`ScenarioJson.Write(Scenario) → string` renders deterministically:
`Utf8JsonWriter`, indented, properties in the order of section 2's example,
timeline in order, `at` as seconds in invariant culture, values as JSON bool /
number / integer by kind, `\n` line endings, one trailing newline. Parsing what
it writes yields an equal `Scenario`.

Recording has no CLI surface in 5b. Nothing external drives a run yet; the
seam is proven by the round trip in section 8 and is ready for the first
adapter.

## 8. Testing

`Dse.Core.Tests`
- `WriteAt` rejects an unknown, a read-only and a kind-mismatched tag at
  schedule time; lands on the named tick with a `WRITE` record; lands after a
  queued write on the same tick; interleaves with a fault in sequence order.
- Two same-seed runs with the same `WriteAt` calls give byte-identical
  `Events.ToText()` (the `DeterminismTests` pattern).
- `IActionRecorder` receives a queued write, a `WriteAt`, a fault and a clear,
  each with its landing tick; a second attach throws.

`Dse.Scenarios.Tests`
- A scenario corpus like the plant corpus: `Scenarios/valid/*.json` over the
  existing valid plants (`minimal`, `conveyor-line`, `instrumented-belt`,
  `item-line`), each with a committed `Golden/<name>.log` pinned by
  `Golden.Assert`; `Scenarios/invalid/DSE20x-*.json`, one per code, asserting
  code and JSON path.
- Runner rules: `at == duration`, off-tick `at`, off-tick `duration`, an
  invalid plant passing its diagnostics through behind `DSE205`, several
  diagnostics collected in one run.
- `GoldenLog.Compare`: identical, first divergence with context, `\r\n` and
  trailing-newline normalisation, a shorter and a longer actual.
- `ScenarioJson`: write → parse round trip equals the original.
- **Record and replay:** a plant is built, a `CommandBus` issues writes and
  `InjectFaultAt`/`ClearFaultAt` are called on a live run with a
  `ScenarioRecorder` attached; the recording is written, parsed, and replayed
  by `ScenarioRunner` against the same plant text; the two logs are
  byte-identical.
- `docs/scenario-diagnostics.md` golden.

`Dse.Cli.Tests`
- Every row of section 5's table; exit 4 writes the `.actual` file; the
  `--format json` shape; `--assembly` reaching a scenario over the sample
  plugin's plant.

## 9. Documentation

- `docs/scenarios.md` — the format reference, the `at` rule, the golden
  workflow: run, `--out`, review the diff, commit.
- `docs/architecture.md` — a "Scenarios and replay" section; and the stale
  "not yet implemented" text for tick phases 4 and 5 is corrected (both have
  shipped since plan 4).
- `README.md` — `dse run` in the quick start; status paragraph.
- `docs/scenario-diagnostics.md` — generated.

## 10. Layout

| project | references | notes |
|---|---|---|
| `src/Dse.Core` | unchanged | gains `WriteAt`/`WriteIn`, `IActionRecorder`, `AttachActionRecorder`, `TagImage.ApplyNow` |
| `src/Dse.Scenarios` | `Dse.Core`, `Dse.Configuration` | new; no `Dse.Components`, no packages. The main spec's layout table said `Dse.Core` only — it predates the loader. |
| `src/Dse.Cli` | + `Dse.Scenarios` | `run` command, exit code 4 |
| `tests/Dse.Scenarios.Tests` | `Dse.Scenarios`, `Dse.Components`, `tests/Shared` | corpus, goldens, round trip |

Global constraints from 5a apply unchanged: net10.0, warnings as errors,
`InvariantCulture`, no Dictionary/HashSet order reaching an output,
deterministic JSON via `Utf8JsonWriter`, diagnostics as sentences with a fix.
