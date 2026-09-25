# Mine Conveyor Sample — Design (plan 6a)

Date: 2026-09-25. Addendum to
`2026-09-02-industrial-process-simulation-engine-design.md` (the "main spec"),
refining its section 15.1. Builds on plans 5a–5d, all merged: the catalogue and
plant JSON (5a), scenarios, replay and `dse run --expect` (5b), the control blocks
(5c) and controllers in the plant file (5d).

**Amended 2026-09-25 by the plan**
(`docs/superpowers/plans/2026-09-25-mine-conveyor-sample.md`, rulings
R103–R121), where the code, a measured run or the owner's ruling forced a
choice: interlocks read `CVn.Stopped`, which follows the switch's own reading
(R103); each belt interlock also reads its permissive, so a safety reset never
restarts a belt, and that — not the zero-speed switch — is why they power up
tripped (R104); feed starve is asserted by sampled belt-scale values, and there
are no low-flow alarms (R105); `SEQ_STOP` runs every belt out (R106); a normal
stop trips `INT_FEED`, `INT_CV001` and `INT_CV002` by design, so the normal
scenario's no-trip window ends when `SEQ_STOP` starts (R107); a welded contactor
defeats both the run command and the e-stop, and the scenario shows it with
nothing clearing it (R108); the overload scenario's `thermal-bias` is 1.0
(R109); the blocked chute stops CV001 through its own overload relay (R110); no
current alarm raises on the start inrush (R111); `bulk-source` gains an optional
`enabled` parameter so the feeder starts off, as a PLC output does — the one
`src/` change (R112); goldens are compared and remade through `dse run`, not
`tests/Shared/Golden.cs` (R113); the samples test project pins JsonSchema.Net
8.0.5 itself (R114); an absence may carry an end point (R115). The sections
below read as amended.

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
| The welded contactor (owner, after the plan's first draft) | **Show the hazard honestly.** A single welded contactor defeats both the PLC's `Start` false and the e-stop; nothing clears it, and CV003 runs to the end of the scenario. The README explains two monitored contactors in series (Category 3 / PL d); a redundant safety-contactor starter is a later component. |
| The feeder at power-up (owner) | **Off, as a PLC output is.** `bulk-source` gains an optional `enabled` parameter, default true (every existing plant unchanged); the sample declares it false and `SEQ_START` enables the feeder. No ore moves before that. |
| Feed starve (owner) | **Asserted by state.** No low-flow alarms (no standing alarms at power-up); the belt-scale values are sampled and must fall to near zero in transport order. |
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
5. Nothing under `src/` changes unless the sample needs it — a defect a scenario
   exposes, or (R112) the `bulk-source` `enabled` parameter; such a change carries
   its own test and a ruling in the plan. Zero package references under `src/`;
   every existing test and golden is unchanged except as that change requires
   (R112: the catalogue export and plant schema goldens gain the parameter).
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
`Dse.Components`, and JsonSchema.Net 8.0.5 (pinned, test-only); it also holds
`Cli.cs`, `Sample.cs`, `Stories.cs`, `StateChain.cs` and the helpers' own tests.
Goldens are compared through `dse run --expect` and remade through `dse run
--out` when `DSE_UPDATE_GOLDEN=1`, so `tests/Shared` is not linked. It is added
to `Dse.sln`.
The sample's files are copied to the test output so the tests read them by
relative path, as the CLI tests read theirs.

## 3. The plant

`ore source → CV001 → CH1 → CV002 → CH2 → CV003 → stockpile`: one `bulk-source`
(`Feed`, declared `"enabled": false`, R112), three `conveyor` composites, two `transfer-chute`s, one `bulk-sink`,
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

Every belt interlock powers up tripped, because it reads its permissive, whose
`Ok` primes false (5c: a block's first scan reads the primed image); `INT_FEED`
trips at 1 s, when CV001's zero-speed switch first reports stopped. So each is
reset only once its downstream conveyor is proved. A pulse is a write set in one
step and cleared in the next, and a step's writes land one tick after it is
entered; the abort writes are every `Start` false and `Feed.Enabled` false. The
feeder is off from tick 0 (`"enabled": false`), so step 5 is the first time ore
moves.

**`SEQ_STOP`** — upstream first, every belt run out: `Feed.Enabled` false and a
40 s run-out for CV001; `CV001.Start` false, wait for CV001's zero-speed switch;
a 20 s run-out for CV002; `CV002.Start` false, wait; a 15 s run-out for CV003;
`CV003.Start` false, wait. Run-outs measured on the plant. A second sequencer,
because the sequencer is linear. Each stop trips the cascade interlock above it
(`INT_FEED`, `INT_CV001`, `INT_CV002`) by design: an interlock does not know a
stop was planned, and its trip writes a command that is already off.

**Interlocks** (trip writes on the trip scan only):

| block | conditions (abnormal when) | trip writes |
|---|---|---|
| `INT_CV003` | `CV003.Tripped` true; `PERM_CV003.Ok` false | `CV003.Start` false |
| `INT_CV002` | `CV002.Tripped` true; `PERM_CV002.Ok` false; `CV003.Stopped` true | `CV002.Start` false |
| `INT_CV001` | `CV001.Tripped` true; `PERM_CV001.Ok` false; `CV002.Stopped` true | `CV001.Start` false |
| `INT_FEED` | `CV001.Stopped` true | `Feed.Enabled` false |

A cascade interlock reads the downstream **zero-speed switch tag** — the
instrument — never the true speed or a value computed from it. That tag is
`CVn.Stopped`, which the switch derives from its own reading, so a failed switch
changes it; `CVn.ZeroSpeed.Value` is the reading itself, a Double. A belt
interlock also reads its own permissive, so that when a relay drops the PLC drops
the run command too, and a safety reset can never restart the belt by itself.

**Permissives** `PERM_CV001..3`: `CVn.SafetyOk` normal true. Pull-keys and e-stops
act through each conveyor's hardwired safety relay, inside the composite; the PLC
sees them only through `SafetyOk`, as a real plant wires them.

**Alarms** `ALM_CV001..3` on `CVn.Current`: `Hi` and `HiHi`, with limits,
deadbands and on-delays set from the measured currents. `HiHi` raises on a real
overload or an overloaded belt, not on the normal start inrush; `Hi` may raise on
the inrush. Measured: with a 3 s on-delay neither raises on the inrush, so a
healthy run logs no alarm at all. There are no other alarms.

## 5. The scenarios

Each starts from a cold plant, writes `SEQ_START.Start` at 1 s, reaches steady
running, injects its event, and runs long enough for the consequence to settle.
The chain is asserted as an ordered subsequence; codes, messages and times come
from the generated log. Where the physics does not produce the chain below, the
plan writer reports what it does produce, and the scenario, not the log, is
adjusted — or the discrepancy is ruled on — never invented.

| scenario | injected | chain (in order) | absences |
|---|---|---|---|
| `normal-start-stop` | `SEQ_START.Start`; after steady running, `SEQ_STOP.Start` | CV003, CV002, CV001 prove speed in that order; the feed enables; `SEQ_START` complete; then the feed disables, CV001, CV002, CV003 stop in that order, each stop tripping the interlock above it (`INT_FEED`, `INT_CV001`, `INT_CV002`); `SEQ_STOP` complete | no `INTERLOCK_TRIP` between `SEQ_START` completing and `SEQ_STOP` starting; no `ALARM_RAISED` in the run |
| `pull-key` | `CV002.PullKey1` pulled | CV002's relay de-energises; CV002 stops; `INT_CV001` trips; `CV001.Start` false by `INT_CV001`; `INT_FEED` trips | `INT_CV003` does not trip; CV003 stays at speed |
| `e-stop` | `CV001.EStop` operated | CV001's relay de-energises; CV001 stops; `INT_FEED` trips | `INT_CV002`, `INT_CV003` do not trip |
| `overload` | `CV003.Motor` `thermal-bias`, `amount` 1.0 | CV003 overload trip; `INT_CV003` trips; CV003 stops; `INT_CV002` trips; CV002 stops; `INT_CV001` trips; `INT_FEED` trips | — |
| `chute-blockage` | `CH1` `blockage` | CH1 fills; CV001's discharge backs up and the belt loads; `ALM_CV001` `Hi` then `HiHi`; CV001's overload relay trips (measured: 109.7 s after the blockage); `INT_CV001` trips; `INT_FEED` trips | no `INTERLOCK_TRIP` before the overload trip; `INT_CV002`, `INT_CV003` do not trip |
| `failed-zero-speed` | CV002's zero-speed switch `fail-low` (reads stopped) | `INT_CV001` trips while CV002 is still at speed; `CV001.Start` false by `INT_CV001`; `INT_FEED` trips | CV002 and CV003 do not stop; `INT_CV002`, `INT_CV003` do not trip |
| `welded-contactor` | `CV003.Starter` `contactor-welded`; then `SEQ_STOP` (its step 6 writes `CV003.Start` false); then `CV003.EStop` | `CV003.Start` false is written, and CV003 keeps running; `SEQ_STOP` faults on its timeout; the e-stop de-energises the relay (`SAFETY_TRIP`, `INT_CV003` trips) and CV003 **still** keeps running — one welded contactor defeats both | CV003 never stops within the run: no `CONTACTOR_OPENED`, `DE_ENERGISED`, `STOPPED` or `ZERO_SPEED` for CV003 after the weld; `CV003.Speed`, sampled, stays at speed to the end |
| `feed-starve` | `Feed` `starve` | by state: the belt-scale values (`CVn.TonnesPerHour`, sampled every 100 ms) each above 200 t/h at the fault (CV003's is still rising, 268 t/h), then fall to near zero (≤ 5 t/h, and stay there) in transport order CV001 → CV002 → CV003 | no `INTERLOCK_TRIP`, no `ALARM_RAISED`, no conveyor stops after start-up |

Fault ids and tag names above are the catalogue's as of 5d; the plan confirms each
against `dse tags` and the catalogue export.

## 6. Tests

`Dse.Samples.Tests`:

- **`dse validate`** on the plant: exit 0, no diagnostics; the schema accepts it
  (JsonSchema.Net 8.0.5 stays pinned, test-only; this project references it).
- Per scenario, a theory row running `dse run <scenario> --expect <golden>`
  in-process through `CliApp.Run`: exit 0.
- Per scenario, the chain and absences of section 5 over the run's event log,
  through `CausalChain`: an ordered-subsequence matcher over `(source, code,
  message-fragment)` patterns, and an absence check bounded by a start point
  (for example "after `SEQ_START`'s `SEQUENCE_COMPLETE`"), and optionally by an
  end point. A failure message names the first pattern not found and the last
  one matched. Where a chain is told by state rather than events (feed starve,
  and CV003 still running in welded-contactor), a sibling helper, `StateChain`,
  checks tag values sampled from a live run: each above a floor at the start
  point, then settling below a ceiling, in order (feed starve). CV003 still at
  speed is not a fall: it is a direct assertion over the sampled speeds.
- Per scenario, record → replay byte-identical (5b's pattern), recording through
  `ScenarioRecorder` — the recording holds only the scenario's actions (5d).
- The README's quoted log lines each appear verbatim in the golden the README
  names for them.

Goldens are generated with `DSE_UPDATE_GOLDEN=1` (through `dse run --out`), **read in full**, and checked
against section 5 before commit; the plan's task report quotes the lines that
carry each chain.

## 7. Documentation

- `samples/mine-conveyors/README.md` — the line (a text diagram), the control
  philosophy (start and stop order and why, the cascade, why safety is hardwired
  and the PLC sees only the relay, why an interlock reads the switch and not the
  true speed, why a welded contactor defeats a single-contactor safety circuit
  and what a Category 3 / PL d circuit does instead), the sizing caveat, and per
  scenario: what is injected, the log lines that show the chain (quoted from the
  golden), and the command that runs it.
- Main spec 15.1 amended: "speed change" is the start, stop and trip transients
  and speed sag under load, the drives being direct-on-line; the sample is a data
  folder under `samples/mine-conveyors/`, not a C# project, and the main spec's
  project list (which names `samples/Dse.Samples.MineConveyors`) is amended to
  match. 15.2 gains a note that the wheel
  line is plan 6b.
- `README.md` — status; the sample in the quick start.
- `docs/control-blocks.md` — points at the sample as the full-size example.

Global constraints from 5a–5d apply unchanged.
