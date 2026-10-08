# Scenarios

A scenario is a JSON file that says which plant to run, how the engine is set
up, how long to run, and what happens while it runs. It is the unit of
regression testing: run one twice and the event log is byte-identical; commit
that log and `millrace run --expect` tells you the day the behaviour changes.

```bash
millrace run scenario.json                              # print the event log
millrace run scenario.json --out golden.log             # make or remake a golden
millrace run scenario.json --expect golden.log          # exit 0 if unchanged, 4 if not
millrace run scenario.json --format json                # the same run, as records
millrace serve plant.json --scenario scenario.json      # the same timeline in real time, served over Modbus TCP
```

`millrace serve --scenario` schedules the timeline and uses the seed, start time and
time step, but ignores `duration`: it runs until stopped.

## The file

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
| `plant` | yes | The plant file's path, relative to the directory holding the scenario. A scenario is a sibling of its plant, so moving the pair keeps them working. |
| `seed` | no | A whole number, zero or greater. Overrides the plant's `defaults.seed`. |
| `startTime` | no | ISO 8601 **with an offset** — `2026-01-01T06:00:00Z` or `2026-03-01T08:00:00+02:00`. Overrides `defaults.startTime`. |
| `timeStepMs` | no | A number greater than zero; fractions are allowed, down to one tick (0.0001 ms). Overrides `defaults.timeStepMs`. |
| `duration` | yes | Seconds from the start, greater than zero and a whole number of ticks. This is what drives the run; there is no plant counterpart. |
| `timeline` | no | Actions, in order. Defaults to none. |

Precedence for the three overrides is the existing chain: scenario, then the
plant's `defaults`, then the engine's own (seed 0, `2026-01-01T00:00:00Z`,
10 ms).

There is deliberately no `$schema`, no inline plant, no assertion inside the
file — the golden log is the assertion — and no `comment` key, because JSON has
none: the file's name and its golden log say what a scenario is for.

## Actions

An action has an `at` and exactly one of three shapes.

| shape | keys | meaning |
|---|---|---|
| write | `write` (tag name), `value` | The plant sees `value` on the tag at `at`. |
| fault | `fault` (component id), `id` (fault id), `args` (optional object of numbers) | Inject the fault. An argument the descriptor declares and the file omits takes the descriptor's default. |
| clear | `clear` (component id), `id` | Clear the fault. |

Tag names are the directory's names, exactly as `millrace tags <plant>` prints them
(`CV001.Start`, `FEED.Rate`). Component ids for faults are the flattened leaf
ids (`CV001.Motor`), and `millrace catalog export` lists each type's fault ids and
arguments.

A value's JSON type must fit the tag's kind:

| tag kind | accepts | rejects |
|---|---|---|
| `Bool` | `true`, `false` | any number |
| `Double` | any number: `1.5`, `5`, `1e2` | `true`, `false` |
| `Int64` | an integer literal: `5` | `5.0`, `5.5`, `true` |

`5.0` is not an integer literal, so it does not fit an `Int64` tag. Write `5`.

## The `at` rule

**`at` is the tick at which the plant sees it**, for a write and for a fault
alike. That is one rule rather than two, and it is what makes a recording
replay exactly: a write replayed through the ordinary queued-write API would
land a tick late, so scenarios use `Simulation.WriteAt`, whose value is applied
during the phase-1 event drain of the tick it named.

Two constraints follow, both checked before tick 0:

- **`at` must fall exactly on a tick.** At a 10 ms step, `60.5` is fine and
  `60.005` is `MR203`, which names the step and the offending time.
- **`at` must be less than `duration`.** `duration: 120` at 10 ms runs ticks
  0 to 11999; an action at 120 s would land on tick 12000, which never runs.

Actions on the same tick fire in file order. Within a tick, anything an
external writer queued lands first, then the scenario's actions in order.

## The golden workflow

1. Write the scenario and run it: `millrace run s.json`. Look at the log.
2. When it says what you meant, save it: `millrace run s.json --out s.log`.
3. Commit both files. The log is the assertion.
4. Later, `millrace run s.json --expect s.log`. Exit 0 means nothing changed. Exit 4
   means the behaviour changed: standard error names the first differing line
   with three lines of context from both logs, and the whole new log is written
   beside the golden as `s.log.actual`.
5. If the change was intended, regenerate with `--out` and review the diff in
   version control. There is no `--update`: remaking a golden is a decision, and
   a decision belongs in a commit.

Exit 1 is different and means the configuration is broken — the scenario or the
plant has errors, and nothing ran. A script can tell the two apart.

## Diagnostics

Every way a scenario can be wrong is reported before tick 0, with a code, a
JSON path and a fix, and a wrong scenario never produces a partial log. See
[scenario diagnostics](scenario-diagnostics.md) for the codes and
[configuration diagnostics](configuration-diagnostics.md) for the plant's own.

## Recording a live run

`ScenarioRecorder` implements `IActionRecorder`, the seam every external action
that takes effect passes through — a command bus write, a scenario write, a
direct `InjectFaultAt`. Attach it, drive the plant however you like, and ask for
the file:

```csharp
var recorder = new ScenarioRecorder();
simulation.AttachActionRecorder(recorder);
// … drive the plant …
Scenario recorded = recorder.ToScenario("conveyor-line.json", options, TimeSpan.FromSeconds(120));
File.WriteAllText("recorded.json", ScenarioJson.Write(recorded));
```

The overrides written out are the run's *actual* seed, start time and step, and
each action's `at` is the tick it landed on times the step, so replaying the
file reproduces the run's event log byte for byte.

A recording holds external actions only. A write a control block issues is not
one: it is logged `Set to … by <block id>.` and never reaches the recorder,
because replaying the external actions re-runs the block, which issues it again.
A recorded run of a plant with `controllers` therefore replays byte for byte.
