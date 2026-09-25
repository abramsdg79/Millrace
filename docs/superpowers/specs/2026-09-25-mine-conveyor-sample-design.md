# Mine Conveyor Sample — Design (plan 6a)

Date: 2026-09-25. Addendum to
`2026-09-02-industrial-process-simulation-engine-design.md` (the "main spec"),
refining its section 15.1. Builds on plans 5a–5d, all merged: the catalogue and
plant JSON (5a), scenarios, replay and `dse run --expect` (5b), the control blocks
(5c) and controllers in the plant file (5d).

## 1. Scope

**Plan 6a — this document.** The mine-conveyor reference sample of main spec 15.1,
as a data folder — a plant file with its controllers, eight scenarios and their
golden logs, and a README — plus a test project that runs every scenario and
asserts its causal chain.

**Plan 6b — a later document.** The wheel-line sample of main spec 15.2. Its
causal chain needs component behaviour that does not exist today: a graded
slow-cycle fault on a process unit, a blocked furnace batch that keeps soaking,
and a reject path for discrete items. 6b designs those first.

Out of scope, named: a variable-speed drive (the motor is direct-on-line); the
wheel line; nested composites (neither sample needs one, so the tag-renaming
reunification stays parked); a C# sample project; the deferred minors of plan 5d.

### Decisions taken in brainstorming

| Question | Decision |
|---|---|
| One plan or two | **Split:** 6a mine conveyors now; 6b wheel line, physics first. |
| "Speed change" (15.1) with an on/off motor | **DOL only.** Speed change is the start, stop and trip transients and speed sag under load. Main spec 15.1 is amended to say so. A VSD is its own plan. |
| What the sample is | **A data folder plus a test project.** No C# under `samples/`; the sample proves that a real plant needs none. |
| Control philosophy | Downstream-first sequenced start with speed proving; upstream-first stop; cascade interlocks on the downstream zero-speed switch; the feed interlocked on CV001; pull-keys and e-stops hardwired through each conveyor's safety relay, seen by the PLC only as a permissive; current alarms per motor. |
| Which signal a cascade interlock reads | **The instrument** — the downstream conveyor's zero-speed switch — as a real PLC is wired, never the true speed. |
| Scenarios | Eight: normal start/stop, pull-key, e-stop, overload, chute blockage, failed zero-speed switch, welded contactor, feed starve. |
| How tests assert | **Goldens plus causal chains:** each golden pins the whole log; a second assertion checks the scenario's chain as an ordered subsequence (and absences), so a regenerated golden cannot silently lose the story. |

### Success criteria

1. `dse validate samples/mine-conveyors/plant.json` reports no diagnostics, and
   the generated plant schema accepts the file (main spec 16).
2. All eight scenarios run through `dse run <scenario> --expect <golden>`,
   in-process, in `tests/Dse.Samples.Tests`, and match.
3. Each scenario's causal chain holds as an ordered subsequence of its event log,
   and each stated absence holds.
4. Each scenario, recorded with `ScenarioRecorder` and replayed, reproduces its
   event log byte for byte.
5. Nothing under `src/` changes unless a scenario exposes a real defect; such a fix
   carries its own test and a ruling in the plan. Zero package references under
   `src/`; every existing test and golden is unchanged.
6. Every log line the sample README quotes appears verbatim in the named golden (a
   test checks it).

## 2. Layout

```
samples/mine-conveyors/
  README.md             the line, the control philosophy, one section per scenario
  plant.json            the plant and its controllers
  scenarios/            one JSON file per scenario (section 5)
  expected/             one golden event log per scenario
tests/Dse.Samples.Tests/
  Dse.Samples.Tests.csproj
  MineConveyorTests.cs  golden, chain, absence and replay per scenario
  CausalChain.cs        the ordered-subsequence and absence helper
  SampleReadmeTests.cs  quoted log lines exist in the goldens
```

`Dse.Samples.Tests` references `Dse.Cli` (to run `dse run` in-process through
`CliApp.Run`), `Dse.Scenarios`, `Dse.Configuration`, `Dse.Control.Catalogue` and
`Dse.Components`, and links `tests/Shared` for `Golden`. It is added to `Dse.sln`.
The sample's files are copied to the test output so the tests read them by
relative path, as the CLI tests read theirs.

## 3. The plant

`ore source → CV001 → CH1 → CV002 → CH2 → CV003 → stockpile`: one `bulk-source`
(`Feed`), three `conveyor` composites, two `transfer-chute`s, one `bulk-sink`,
one plant-local ore material.

**Sizing: realistic in kind, modest in size.** Belts in the tens to low hundreds
of metres, motor ratings, gear ratios and pulley sizes consistent with them, and
cell sizes chosen so that each scenario runs in a few seconds of wall time and no
`DSE006` (CFL) fires. It is a demonstration line, not a sized design; the README
says so. The plan writer settles the numbers on a scratch run and reports the
measured start inrush, running current, time to 90 % speed and overload trip
time for each conveyor.

`defaults`: a fixed `seed`, `timeStepMs` 10, a `startTime` with an explicit
offset.

## 4. The controllers

Blocks scan at 100 ms; the two sequencers at 200 ms (as in the 5c worked
example).

**`SEQ_START`** — downstream first, each conveyor proved at 90 % of its running
speed before the next starts, 15 s timeout per proving step:

1. pulse each conveyor's `SafetyReset`;
2. pulse `INT_CV003.Reset`; `CV003.Start` true; wait `CV003.Speed ≥ 90 %`;
3. pulse `INT_CV002.Reset`; `CV002.Start` true; wait `CV002.Speed ≥ 90 %`;
4. pulse `INT_CV001.Reset`; `CV001.Start` true; wait `CV001.Speed ≥ 90 %`;
5. pulse `INT_FEED.Reset`; `Feed.Enabled` true.

Every zero-speed interlock powers up tripped (5c: a block's first scan reads the
primed image), so each is reset only once its downstream conveyor is proved. The
plan settles the exact step list — a pulse is a set step followed by a clear step,
and a sequencer step's writes land one tick after it is entered — and the abort
writes (every `Start` false, `Feed.Enabled` false).

**`SEQ_STOP`** — upstream first: `Feed.Enabled` false; a run-out delay long enough
to clear CV001 (measured from belt length and speed); `CV001.Start` false, wait
for CV001's zero-speed switch; then CV002; then CV003. A second sequencer, because
the sequencer is linear.

**Interlocks** (trip writes on the trip scan only):

| block | conditions (abnormal when) | trip writes |
|---|---|---|
| `INT_CV003` | `CV003.Tripped` true | `CV003.Start` false |
| `INT_CV002` | `CV002.Tripped` true; CV003's zero-speed switch shows stopped | `CV002.Start` false |
| `INT_CV001` | `CV001.Tripped` true; CV002's zero-speed switch shows stopped | `CV001.Start` false |
| `INT_FEED` | CV001's zero-speed switch shows stopped | `Feed.Enabled` false |

A cascade interlock reads the downstream **zero-speed switch tag** — the
instrument — never the true speed or a value computed from it. The plan confirms
which exposed tag carries the switch's output (`CVn.ZeroSpeed.Value` or
`CVn.Stopped`) and uses the one that follows the instrument, so that the
failed-zero-speed scenario shows a nuisance trip.

**Permissives** `PERM_CV001..3`: `CVn.SafetyOk` normal true. Pull-keys and e-stops
act through each conveyor's hardwired safety relay, inside the composite; the PLC
sees them only through `SafetyOk`, as a real plant wires them.

**Alarms** `ALM_CV001..3` on `CVn.Current`: `Hi` and `HiHi`, with limits,
deadbands and on-delays set from the measured currents. `HiHi` raises on a real
overload or an overloaded belt, not on the normal start inrush; `Hi` may raise on
the inrush. The plan states which, from the measurements.

## 5. The scenarios

Each starts from a cold plant, writes `SEQ_START.Start` at 1 s, reaches steady
running, injects its event, and runs long enough for the consequence to settle.
The chain is asserted as an ordered subsequence; codes, messages and times come
from the generated log. Where the physics does not produce the chain below, the
plan writer reports what it does produce, and the scenario, not the log, is
adjusted — or the discrepancy is ruled on — never invented.

| scenario | injected | chain (in order) | absences |
|---|---|---|---|
| `normal-start-stop` | `SEQ_START.Start`; after steady running, `SEQ_STOP.Start` | CV003, CV002, CV001 prove speed in that order; the feed enables; `SEQ_START` complete; then the feed disables, CV001, CV002, CV003 stop in that order; `SEQ_STOP` complete | no `INTERLOCK_TRIP` after `SEQ_START` completes |
| `pull-key` | `CV002.PullKey1` pulled | CV002's relay de-energises; CV002 stops; `INT_CV001` trips; `CV001.Start` false by `INT_CV001`; `INT_FEED` trips | `INT_CV003` does not trip; CV003 stays at speed |
| `e-stop` | `CV001.EStop` operated | CV001's relay de-energises; CV001 stops; `INT_FEED` trips | `INT_CV002`, `INT_CV003` do not trip |
| `overload` | `CV003.Motor` `thermal-bias` | CV003 overload trip; `INT_CV003` trips; CV003 stops; `INT_CV002` trips; CV002 stops; `INT_CV001` trips; `INT_FEED` trips | — |
| `chute-blockage` | `CH1` `blockage` | CH1 fills; CV001's discharge backs up and the belt loads; `ALM_CV001` raises; CV001 stops by whichever the physics produces first (overload trip or an interlock) — measured and stated in the plan | — |
| `failed-zero-speed` | CV002's zero-speed switch `fail-low` (reads stopped) | `INT_CV001` trips while CV002 is still at speed; `CV001.Start` false by `INT_CV001`; `INT_FEED` trips | CV002 and CV003 do not stop; `INT_CV002`, `INT_CV003` do not trip |
| `welded-contactor` | `CV003.Starter` `contactor-welded`; then `CV003.Start` false (from `SEQ_STOP` or a trip); then `CV003.EStop` | `CV003.Start` false is written, and CV003 keeps running; the e-stop de-energises the relay and CV003 stops | CV003 does not stop between the `Start` false and the e-stop |
| `feed-starve` | `Feed` `starve` | the belt-scale rates fall to zero along the line in transport order | no `INTERLOCK_TRIP`, no `ALARM_RAISED` after start-up; no conveyor stops |

Fault ids and tag names above are the catalogue's as of 5d; the plan confirms each
against `dse tags` and the catalogue export.

## 6. Tests

`Dse.Samples.Tests`:

- **`dse validate`** on the plant: exit 0, no diagnostics; the schema accepts it
  (JsonSchema.Net 8.0.5 stays pinned, test-only — this project may reference it
  or reuse the configuration tests' agreement helper; the plan decides).
- Per scenario, a theory row running `dse run <scenario> --expect <golden>`
  in-process through `CliApp.Run`: exit 0.
- Per scenario, the chain and absences of section 5 over the run's event log,
  through `CausalChain`: an ordered-subsequence matcher over `(source, code,
  message-fragment)` patterns, and an absence check bounded by a start point
  (for example "after `SEQ_START`'s `SEQUENCE_COMPLETE`"). A failure message
  names the first pattern not found and the last one matched.
- Per scenario, record → replay byte-identical (5b's pattern), recording through
  `ScenarioRecorder` — the recording holds only the scenario's actions (5d).
- The README's quoted log lines each appear verbatim in the golden the README
  names for them.

Goldens are generated with `DSE_UPDATE_GOLDEN=1`, **read in full**, and checked
against section 5 before commit; the plan's task report quotes the lines that
carry each chain.

## 7. Documentation

- `samples/mine-conveyors/README.md` — the line (a text diagram), the control
  philosophy (start and stop order and why, the cascade, why safety is hardwired
  and the PLC sees only the relay, why an interlock reads the switch and not the
  true speed), the sizing caveat, and per scenario: what is injected, the log
  lines that show the chain (quoted from the golden), and the command that runs
  it.
- Main spec 15.1 amended: "speed change" is the start, stop and trip transients
  and speed sag under load, the drives being direct-on-line; the sample is a data
  folder under `samples/mine-conveyors/`, not a C# project, and the main spec's
  project list (which names `samples/Dse.Samples.MineConveyors`) is amended to
  match. 15.2 gains a note that the wheel
  line is plan 6b.
- `README.md` — status; the sample in the quick start.
- `docs/control-blocks.md` — points at the sample as the full-size example.

Global constraints from 5a–5d apply unchanged.
