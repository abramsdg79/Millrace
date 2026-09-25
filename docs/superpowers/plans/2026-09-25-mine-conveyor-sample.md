# Mine Conveyor Sample Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship the mine-conveyor reference sample of main spec §15.1 as a data
folder — `samples/mine-conveyors/` holding a plant file with twelve controllers,
eight scenarios, their golden event logs and a README — plus
`tests/Dse.Samples.Tests`, which runs every scenario through `dse run --expect`
in-process, asserts its causal chain (by events, and by sampled state where the
log cannot show it) and its absences, checks it settles before it ends, and
replays it byte for byte from a recording; with one small engine change first —
a `bulk-source` that can be declared disabled at power-up.

**Architecture:** One change under `src/` (Task 1, R112): `BulkSource` gains an
optional `enabled` parameter, default true, so a plant can declare its feeder
off at power-up as a real PLC output is; every existing plant and golden is
unchanged except the two catalogue-derived goldens that list parameters. The
sample itself is JSON the `dse` command line of plans 5a–5d already runs. The
test project drives the CLI in-process through `CliApp.Run` for `validate` and
the goldens (`run --expect`, and `run --out` when `DSE_UPDATE_GOLDEN=1`), reads
each scenario's event log through `ScenarioRunner` (one cached run per scenario)
for the stories, samples tag values from a live `Simulation` where a story is
told by state, and builds a live `Simulation` with a `ScenarioRecorder` for
record → replay. Two small test-side helpers, `CausalChain` (an ordered
subsequence of event patterns, and bounded absences) and `StateChain` (sampled
tags falling in a given order), name what is missing when they fail.

**Tech Stack:** .NET 10 (`net10.0`, SDK 10.0.401), C#, xUnit 2.9.3. No package
under `src/`. JsonSchema.Net 8.0.5, test-only and pinned.

**Spec:** `docs/superpowers/specs/2026-09-25-mine-conveyor-sample-design.md`
(all of it, as amended by this plan — see its header note), refining §15.1 of the
main spec `docs/superpowers/specs/2026-09-02-industrial-process-simulation-engine-design.md`
(§4's project list, §15, §16), on top of plans 5a–5d.

**Plan sequence:** This is plan 6a. Plans 1–5d are merged on `master`; this plan
starts from `51980e2` (the commit that added the 6a spec). Measured on that
commit with `dotnet test Dse.sln`: **1255 tests**, all passing — 37
`Dse.Io.Abstractions` / 461 `Dse.Core` / 126 `Dse.Components` / 56
`Dse.Realtime` / 209 `Dse.Configuration` / 163 `Dse.Scenarios` / 76 `Dse.Cli` /
105 `Dse.Control` / 22 `Dse.Control.Catalogue`.

**Task shape.** Eight tasks, in the spec's suggested order, with three changes
the code argued for:

- **The engine change comes first (Task 1).** The plant of Task 3 declares
  `"enabled": false` on its feeder, which the loader rejects until `bulk-source`
  has the parameter (R112). It is its own task with its own tests and its two
  regenerated catalogue goldens, as spec criterion 5 requires of any `src/`
  change.
- **The CLI helper and the plant tests share Task 3.** Task 2 is the project and
  the two helpers alone, with their own tests; `Cli.cs` has no test of its own
  and is first used by Task 3's `validate` test.
- **The scenarios come in three groups sized by their chains** (Tasks 4–6): the
  harness and the three operator stories (normal, pull-key, e-stop); the three
  fault cascades (overload, chute blockage, failed switch); and the two told
  partly by state (welded contactor, feed starve). Every golden is generated and
  read in the task that adds its scenario.

## Global Constraints

- **`src/` changes only in Task 1** (R112; spec criterion 5 allows a change
  with its own test and ruling). After Task 1, `git diff --stat 51980e2 -- src/`
  lists only `src/Dse.Components/Flow/BulkSource.cs`. If a scenario exposes any
  other engine defect during execution, stop and report it rather than fixing
  `src/` inside a sample task. `git grep -n PackageReference -- 'src/*.csproj'`
  prints nothing (a plain `grep -r` over `src/` also hits `obj/` after a build).
- **Determinism.** No `System.Random`, no `string.GetHashCode()`, no
  `Dictionary`/`HashSet` iteration order reaching an assertion or an output: sort
  with `StringComparer.Ordinal` or keep file order. String comparisons are
  ordinal. All formatting uses `CultureInfo.InvariantCulture`.
- **Test packages.** The new project uses the versions of
  `tests/Dse.Core.Tests/Dse.Core.Tests.csproj` (`coverlet.collector` 6.0.4,
  `Microsoft.NET.Test.Sdk` 17.14.1, `xunit` 2.9.3, `xunit.runner.visualstudio`
  3.1.4) and references **JsonSchema.Net 8.0.5**, pinned, with the same comment
  `tests/Dse.Configuration.Tests` carries (R114). Those two test projects are the
  only ones that reference it.
- **Warnings are errors** (`Directory.Build.props`: `TreatWarningsAsErrors`,
  `EnforceCodeStyleInBuild`, `GenerateDocumentationFile`, `Nullable`). The
  Release build prints `0 Warning(s)` and `0 Error(s)`. A csproj repeats only
  `TargetFramework`, `ImplicitUsings`, `Nullable` and `IsPackable`.
- **xUnit analyzers run under warnings-as-errors:** prefer `Assert.Single`,
  `Assert.Contains`, `Assert.DoesNotContain`, `Assert.Empty`, `Assert.All`;
  never `Assert.True(x.Any())` or `Assert.Equal(1, x.Count())`. Theories take
  their rows from `Sample.Scenarios` (a `TheoryData<string>`).
- **Tests spell fault arguments `new FaultArgument("n", v)`**; the replay test
  builds `new FaultArguments(fault.Arguments.ToArray())` from a parsed scenario.
- **Goldens are generated and read, never invented or hand-edited** (R113).
  Task 1's two catalogue goldens use `tests/Shared/Golden.cs`: regenerate each
  with `DSE_UPDATE_GOLDEN=1` and a `--filter` naming its one test, then read the
  whole `git diff`. A sample golden is written by the golden theory itself when `DSE_UPDATE_GOLDEN=1`:
  it runs `dse run <scenario> --out samples/mine-conveyors/expected/<name>.log`
  in-process, at the source path. Run the update **for this project and this
  theory only** —
  `DSE_UPDATE_GOLDEN=1 dotnet test tests/Dse.Samples.Tests --nologo --filter "FullyQualifiedName~EveryScenarioMatchesItsGolden"`
  — never over the whole solution, which would rewrite every other project's
  goldens. Then **read each new golden in full** with a file-reading tool, check
  it against the task's checklist, quote the checked lines in the task report,
  and run the project again *without* the variable: the rebuild copies the new
  goldens to the test output, where `--expect` reads them. If a golden disagrees
  with a checklist, report the measured line and say which changed and why;
  never adjust a checklist, a story or a duration to fit without saying so.
- **Report every measurement.** Where an expected value in this plan (a test
  count, a line count, a time) disagrees with what the code produces, report the
  measured value in the task report.
- **Timing rule** (it shapes every golden here): **a scan at tick N sees the
  image published at the end of tick N−1; its outputs are visible from the end of
  tick N (read at N+1); its queued writes land at phase 1 of tick N+1.** A
  scenario `write` at `at` lands at phase 1 of tick `at / 10 ms`. Blocks scan at
  100 ms, the sequencers at 200 ms, so an interlock reacts 10–110 ms after its
  condition changes and its trip write is logged 10 ms after its
  `INTERLOCK_TRIP`; a sequencer step's writes land 10 ms after its
  `STEP_ENTERED`.
- **Names.** Tag names are exactly what `dse tags samples/mine-conveyors/plant.json`
  prints and match ordinally. A scenario's file name, its golden's file name and
  its `Sample.Names` entry are the same kebab-case word. Component ids for faults
  are the flattened leaf ids (`CV003.Motor`, `CV002.ZeroSpeed`,
  `CV003.Starter`); `CH1` and `Feed` are leaves already.
- **JSON and Markdown files** are written exactly as this plan shows them: LF
  line endings, two-space indentation, a final newline, no tabs.
- **Git, for every task.** One git command per `Bash` call. `git add` names
  paths explicitly — never `git add -A`, never `git add .`. **Never `git
  stash`.** Commit messages: a subject line, a blank line, an optional body, and
  the trailer as the last line, copied verbatim:

  ```
  Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
  ```

  The trailer identifies the **session** that planned and drives this work, not
  the model that implements a given task: it is the same on every commit,
  whichever implementer model a task names. Never put it on the subject line.
- **Commands**, from the repository root:
  `dotnet build Dse.sln -c Release --nologo` (expect `0 Warning(s)`,
  `0 Error(s)`) and `dotnet test Dse.sln --nologo` (expect the task's total).
  `.superpowers/` **is** git-ignored (since `68c61e9`); scratch work goes under
  `.superpowers/sdd/6a/` and is never added.


## Review Focus

The five failure modes the spec implies but its test list does not pin, most
likely to bite first. Each has its test in the owning task, named here.

1. **An existing plant that never mentions `enabled`** — every plant file,
   scenario and golden written before this plan expects its feeder to run from
   tick 0 exactly as before; the author expects the new parameter to change
   nothing unless it is written. Tests: Task 1,
   `FlowFactoryTests.ASourceIsEnabledAtPowerUpUnlessTheFileSaysOtherwise`, and
   the unchanged goldens of `Dse.Scenarios.Tests` and `Dse.Control.Tests`
   (`conveyor-control.log` feeds from tick 0 and must not move).
2. **A scenario added, renamed or deleted without its golden, its story or its
   README section** — the author expects a red test, not a theory that quietly
   runs over a stale list, and not a bin copy that still holds a deleted file.
   Tests: Task 4,
   `MineConveyorTests.TheScenarioFolderHoldsExactlyTheEightScenariosEachWithAGoldenAndAStory`
   (reads the *source* folder); Task 7,
   `SampleReadmeTests.EveryScenarioHasOneQuotedBlockAndItsCommand`.
3. **A check that passes on nothing** — an absence whose start or end point
   never occurs (a renamed step, a reworded message), a pattern whose source is
   a prefix of another (`CV001` against `CV001.Starter`), one event satisfying
   two patterns, or a state fall on a tag that was never running. The author
   expects each to fail and say why. Tests: Task 2,
   `CausalChainTests.AnAbsenceWhoseBoundsNeverOccurFailsRatherThanPassingEmpty`,
   `APatternMatchesExactSourceExactCodeAndAMessageFragment` (three rows),
   `OneEventCannotSatisfyTwoPatterns`, and
   `StateChainTests.ATagThatWasNotRunningAtTheStartProvesNothing`.
4. **A scenario whose duration cuts its consequence off** — a later physics or
   tuning change that slows a trip or a run-out by a few seconds would leave a
   golden that ends mid-story; the author expects a quiet tail and a state fall
   that completes. Tests: Task 4,
   `MineConveyorTests.EveryScenarioSettlesAtLeastFiveSecondsBeforeItEnds`; Task 6,
   `AStarvedFeedEmptiesTheBeltsInTransportOrder` (fails with "never settles" if
   CV003's scale has not reached 5 t/h by the end).
5. **A recording that captures a block's writes** (R77 regressing) — replaying
   it would issue every interlock and sequencer write twice; the author expects
   the recording to hold exactly the scenario's actions. Test: Task 4,
   `MineConveyorTests.EveryScenarioReplaysByteForByteFromARecording`, which
   asserts `recorder.Count == scenario.Timeline.Count` before it compares logs.

## Decisions settled here (rulings R103–R121)

These refine the spec where the code, or a measured run, forced a choice, and
record the three the owner ruled on after the first draft (R105, R108, R112).
The 6a spec is amended in place to agree (its header note lists them), as plan
5d did.

- **R103 — Interlocks read `CVn.Stopped`; it is the instrument.** The conveyor
  exposes `Stopped` from `ZeroSpeedSwitch.Stopped`, which `OnEvaluated` derives
  from the switch's own *reading* after calibration, drift, noise, lag, freeze and
  failure (`InstrumentBase.Evaluate`), so a failed switch changes it.
  `CVn.ZeroSpeed.Value` is the switch's reading (Double, m/s), not a Bool; a
  condition on it would be `DSE114`. Measured (`fail-low` on `CV002.ZeroSpeed` at
  80 s): the reading pins at 0 m/s; `CV002.ZeroSpeed ZERO_SPEED` is logged at
  80.990 s (the switch's fixed 1 s delay) while CV002 runs at 1.92 m/s, and
  `INT_CV001` trips at 81.000 s.
- **R104 — Each belt interlock also reads its own permissive.** Without it a
  pulled key or an e-stop drops the contactor through the relay but leaves
  `CVn.Start` true, so the next `SafetyReset` edge restarts the belt — the
  restart a reset must never cause (EN 60204-1 §9.2.5.4). So `INT_CVn` has three
  conditions: `CVn.Tripped` normal false, `PERM_CVn.Ok` normal true, and (CV001,
  CV002) the downstream `Stopped` normal false. Consequences, measured:
  `INT_CV001..3` power up tripped at 0.000 s because `PERM_CVn.Ok` primes false
  (`Stopped` primes **false**; the switches assert only at 0.990 s); `INT_FEED`,
  which reads only `CV001.Stopped`, trips at 1.000 s. The pull-key chain gains
  `INT_CV002` tripping on `PERM_CV002.Ok`, the e-stop chain `INT_CV001` on
  `PERM_CV001.Ok`; neither is one of the spec's absences.
- **R105 — Feed starve is asserted by state; there are no low-flow alarms**
  (owner's ruling, replacing the draft's `FLOW_CVn` alarms, which stood raised at
  power-up). The event log records no belt-scale value, so the chain "the rates
  fall to zero in transport order" is checked on sampled tags:
  `Sample.Trace` runs the scenario live and samples `CV001..3.TonnesPerHour`
  every 100 ms; `StateChain.FindFallInOrder` requires each to be above 200 t/h at
  the fault (80 s) and then to settle at or below 5 t/h (the scale's noise is σ
  0.3 t/h) strictly after the one before. Measured settle times: CV001 104.2 s,
  CV002 133.7 s, CV003 153.7 s (at 80 s: 288.0, 287.3, 267.7 t/h — the floor is
  200, not 250, because CV003 is still filling at 80 s). The
  feed-starve golden's own chain is the fault line; its absences are no
  `INTERLOCK_TRIP`, no `ALARM_RAISED`, no `CONTACTOR_OPENED` after start-up.
  Duration 170 s so CV003 settles 16 s before the end.
- **R106 — `SEQ_STOP` gives every belt a run-out.** Measured with CV001's alone:
  281 kg stays on CV002 and 544 kg on CV003. Measured run-outs after the feeder
  stops (belts running): CV001 ≤ 3 kg at 40 s, CV002 ≤ 1 kg at 65 s, CV003 ≤ 4 kg
  at 80 s. Steps: stop the feed, 40 s; stop CV001, wait for its switch (timeout
  20 s); run out CV002, 20 s; stop CV002, wait (20 s); run out CV003, 15 s; stop
  CV003, wait (20 s). The run-out steps have no writes.
- **R107 — A normal stop trips the cascade interlocks, by design; the no-trip
  window ends at `SEQ_STOP`.** An interlock has no "running" qualifier, so when
  CV001 stops, `INT_FEED` trips; CV002 → `INT_CV001`; CV003 → `INT_CV002` (each
  writes a `Start` or `Enabled` already false). The absence runs from
  `SEQ_START`'s `SEQUENCE_COMPLETE` to `SEQ_STOP.Start` set true, and the three
  stop-time trips join the chain in order. The next `SEQ_START` resets them.
- **R108 — A welded contactor defeats both the PLC and the e-stop; the scenario
  shows it and nothing clears it** (owner's ruling, replacing the draft's
  weld-clearing). `MotorStarter.Evaluate` computes `closed = _welded ||
  (Command && SafetyOk && !_tripped && !_open)`: neither the run command nor the
  safety input opens welded contacts, true of a single real contactor. The
  scenario welds CV003's contactor at 80 s, starts `SEQ_STOP` at 90 s (its step 6
  writes `CV003.Start` false at 181.610 s and times out: `SEQUENCE_FAULTED Step 6
  timed out after 20 s.` at 201.8 s), and operates `CV003.EStop` at 210 s
  (`SAFETY_TRIP`, `PERM_CV003` lost, `INT_CV003` trips). CV003 never stops: the
  absences, from the weld to the end of the run, are no `CV003.Starter
  CONTACTOR_OPENED`, `CV003.Motor DE_ENERGISED`/`STOPPED`, `CV003.ZeroSpeed
  ZERO_SPEED`; and `CV003.Speed`, sampled every 100 ms from 80 s to 240 s, never
  falls below 1.7 m/s (measured minimum 1.910 m/s). The README explains two
  monitored contactors in series (Category 3 / PL d, auxiliary-contact feedback
  to the safety relay) and names a redundant safety-contactor starter as a later
  component.
- **R109 — The overload scenario's `thermal-bias` is `amount` 1.0.** CV003's
  thermal state at 80 s is 0.288 (τ = 60 s, loaded only since about 70 s); the
  5c worked example's 0.8 gives 1.088 < 1.1 and does not trip. 1.0 gives
  1.2884784008371077 and trips on the injection tick. The current does not rise —
  the trip opens the contactor — so the story pins "no `ALM_CV003` raise".
- **R110 — The chute blockage stops CV001 on its own overload relay.** Measured
  (blockage at 80 s): `CH1 FULL` at +12.5 s; `ALM_CV001` `Hi` at +33.4 s and `HiHi`
  at +49.3 s; the belt fully loaded (6 311 kg, 9.56 A = 1.17 × rated) by about
  +60 s; `OVERLOAD_TRIP` at +109.70 s. No interlock acts first, so the absence is
  "no `INTERLOCK_TRIP` between the blockage and the overload trip". A real chute
  has a blocked-chute probe interlocked to the feeding belt, tripping it within
  seconds; the sample deliberately omits one so the overload chain can play out,
  and must not interlock on `CH1.Full`, which is model truth rather than an
  instrument (R103's reasoning).
- **R111 — Neither `Hi` nor `HiHi` raises on the start inrush.** Both limits have a
  3 s on-delay; measured, the inrush stays above `Hi` for at most 1.87 s and
  above `HiHi` for at most 1.73 s (all three belts). Spec §4 allowed `Hi` to
  raise; the plan chooses not, so a normal run logs no alarm at all.
- **R112 — The feeder starts disabled: `bulk-source` gains `enabled`** (owner's
  ruling, replacing the draft's "the feeder runs for the first second"). No
  existing mechanism sets an initial value: `bulk-source` has no such parameter,
  a plant `tags` bind has only `name`, `port`, `access`, `unit`, `rangeLow`,
  `rangeHigh` and `description`, and driving `Feed.Enabled` from a signal link
  would make the tag read-only (R23), so `SEQ_START` could not write it. The
  smallest change: `Param.Bool("enabled", …, @default: true)` on the descriptor,
  a `bool enabled = true` constructor parameter, and `AddInput<bool>("Enabled",
  defaultValue: enabled)` — the tag image primes from the port's default, so
  `Feed.Enabled` reads false from tick 0. Default true keeps every existing plant
  and golden byte-identical (measured: all 1255 existing tests pass, and only the
  two catalogue-derived goldens change: `components-catalogue.json` +7 lines for
  the parameter and 1 line for the `Enabled` port's description, and
  `plant.schema.json` +5 lines). Spec criterion 5 admits a `src/` change with its
  own test and ruling; this is Task 1. The sample declares `"enabled": false`;
  measured, no ore is created before `Feed.Enabled` is written true at 9.410 s,
  and `INT_FEED`'s power-up trip at 1.000 s writes a false that is already there.
- **R113 — Goldens go through `dse run`, not `tests/Shared/Golden.cs`.** The
  golden theory runs `--expect` against the copy in the test output and, when
  `DSE_UPDATE_GOLDEN=1`, runs `--out` to the source path found from `Sample.cs` by
  `[CallerFilePath]`. `tests/Shared` is not linked. A mismatch leaves
  `<name>.log.actual` beside the *output* copy (and `*.actual` is git-ignored).
  Task 1's two catalogue goldens are ordinary `Golden.Assert` goldens.
- **R114 — `Dse.Samples.Tests` references JsonSchema.Net 8.0.5 itself.** The
  configuration tests' helper is `private` to `SchemaAgreementTests`. 5d's
  constraint "pinned in `tests/Dse.Configuration.Tests` only" widens to these two
  test projects.
- **R115 — `CausalChain`'s shape.** `EventPattern(string? Source, string Code,
  string MessageFragment = "")`: source and code match exactly (ordinal), a null
  source matches any source, the fragment is an ordinal substring. `Absence(
  EventPattern? After, EventPattern Forbidden, EventPattern? Until = null)`: the
  spec asks for a start point; normal-start-stop (R107) needs an end point too. A
  bound that never occurs is a failure, not an empty window. Both checks return
  `null` or a message, so a test reads `Assert.Null(...)` and a failure prints
  the reason.
- **R116 — Two checks beyond spec §6.** The folder test (Review Focus 2) and the
  settle test (the last event at least 500 ticks = 5 s before the end). Measured
  quiet tails: 6.29 s (overload) to 90 s (feed-starve, whose last event is the
  fault).
- **R117 — Scenario timing.** Every scenario writes `SEQ_START.Start` true at 1 s
  and false at 2 s (rising-edge commands, as in the 5c worked example), and
  injects its event at 80 s, once ore is on every belt (CV003's scale reads
  268 t/h at 80 s, still rising; 282 t/h — 98 % of 288 — at about 86 s);
  normal-start-stop pulses
  `SEQ_STOP.Start` at 90–91 s, welded-contactor at 90–91 s and presses the e-stop
  at 210 s. Durations: 100 s (e-stop, failed-zero-speed), 110 s (pull-key,
  overload), 170 s (feed-starve), 200 s (normal-start-stop), 210 s
  (chute-blockage), 240 s (welded-contactor).
- **R118 — Ids and scan order.** `Feed`, `CV001`–`CV003`, `CH1`, `CH2`,
  `Stockpile`; controllers `PERM_CV001..3`, `INT_CV003`, `INT_CV002`,
  `INT_CV001`, `INT_FEED`, `ALM_CV001..3`, `SEQ_START`, `SEQ_STOP`, in that file
  order (= scan order): permissives before the interlocks that read them,
  interlocks downstream first. Seed 7; start time `2026-03-02T06:00:00+08:00`.
- **R119 — Speed proving at 1.74 m/s.** No-load belt speed is 155 rad/s ÷ 25 ×
  0.315 m = 1.953 m/s; measured running speed with an empty belt 1.935 m/s
  (1.913 m/s at 288 t/h); 90 % of 1.935 is 1.74. `CVn.Speed` is the speed
  sensor (noise σ 0.002 m/s, no lag).
- **R120 — Main spec amendments.** §15.1 says "A demo controller in
  `Dse.Control`"; the controllers are in the plant file, so the sentence is
  amended with the rest of §15.1 and §4's sample list (Task 8). §16's "the
  generated schema accepts both samples' plant files" holds and is left as
  written.
- **R121 — `StateChain` and `Sample.Trace`.** `TagSample(TimeSpan Time, double
  Value)`; `StateChain.SettlesAtOrBelow(series, ceiling, from)` returns the first
  time at or after `from` from which the series stays at or below the ceiling to
  its end, or null; `StateChain.FindFallInOrder(traces, order, from, floor,
  ceiling)` returns null or a message naming the first tag that was not above
  the floor at `from`, never settles, or settles no later than the tag before it.
  `Sample.Trace(name, tags, every)` loads the plant, schedules the scenario's
  actions with `Sample.Schedule` (shared with the replay test), ticks to the end
  and samples the tags every `every`, which must be a whole number of time steps
  of at least one (`ArgumentOutOfRangeException` otherwise). It is a second live
  run, not a replacement for the golden: the golden pins the events, the trace
  the values. Welded-contactor's "CV003 still at speed" is not a fall, so it uses
  `Sample.Trace` with a direct `Assert.All` over the sampled speeds (every sample
  from 80 s ≥ 1.7 m/s), not `StateChain`.

## Measurements

Scratch runs on `51980e2` plus Task 1's change (a copy of the repository under
`.superpowers/sdd/6a/scratch/`, deleted after), with the plant and scenarios of
this plan.

**The line.** Belts 1.0 m wide, horizontal, 1 m cells, 20° surcharge, ore at
1 600 kg/m³: capacity 105.2 kg/m, or 738 t/h at 1.95 m/s. Feed 80 kg/s
(288 t/h, 41.8 kg/m at 1.913 m/s, 40 % of capacity), enabled at 9.410 s. CFL:
max speed 2.148 m/s × 10 ms = 0.021 m ≪ 1 m cells; no `DSE006`. Motors
155 rad/s (1 480 rpm), gear 25:1, pulley 0.63 m, coast time constant 1.5 s (a
loaded belt's switch asserts 7.8 s after its contactor opens), other constants
the catalogue defaults (locked-rotor 6×, acceleration τ 1 s, thermal τ 60 s,
starter trip 1.1 / reset 0.9). `dse validate`: 7 components, 52 leaves, 6 flow
links, 120 tags, 12 controllers.

| | CV001 | CV002 | CV003 |
|---|---|---|---|
| length, empty belt mass, tail drag | 60 m, 1 200 kg, 200 N | 40 m, 800 kg, 150 N | 30 m, 600 kg, 100 N |
| motor | 4 kW, 8.2 A | 3 kW, 6.3 A | 2.2 kW, 4.7 A |
| inrush peak (sensor) | 48.73 A | 37.42 A | 27.92 A |
| current, empty belt | 4.20 A | 3.05 A | 2.26 A |
| current, 288 t/h | 6.26 A | 4.54 A | 3.40 A |
| energised → 1.74 m/s | 2.27 s | 2.28 s | 2.26 s |
| inrush above `Hi` / above `HiHi` | 1.87 / 1.72 s | 1.86 / 1.72 s | 1.86 / 1.73 s |
| `Hi` / `HiHi` (deadband 0.3 A, on-delay 3 s) | 7.5 / 8.6 A | 5.7 / 6.6 A | 4.3 / 4.9 A |
| thermal state at 80 s | 0.46 | 0.36 | 0.29 |
| overload trip | full belt: +109.7 s after `CH1` blocks (9.56 A) | `thermal-bias` 1.0 at 80 s: on the injection tick (thermal state 0.353 → 1.353; measured, not a shipped scenario) | `thermal-bias` 1.0: on the injection tick |

`Hi` is about 1.2 × the loaded running current; `HiHi` about 1.05 × rated, just
under the 1.049 × rated at which the thermal state settles at the trip level.

**Wall time** per scenario, `dse run` from a shell (Release, process start
included): normal-start-stop 0.52 s, pull-key 0.50 s, e-stop 0.49 s, overload
0.50 s, chute-blockage 0.63 s, failed-zero-speed 0.49 s, welded-contactor
0.72 s, feed-starve 0.61 s. Under `dotnet test` (Debug) the whole new project
runs in about 16 s.

**What each scenario produced** (all eight measured end to end; the first 63
lines — power-up and start-up, to `SEQ_START`'s `SEQUENCE_COMPLETE` — are
byte-identical in every golden):

| scenario | spec chain | measured |
|---|---|---|
| normal-start-stop | as spec | as spec, plus three stop-time interlock trips (R107); no alarm; 94 events |
| pull-key | as spec | as spec, plus `INT_CV002` on its permissive (R104); CV003 runs on, empty; 81 events |
| e-stop | as spec | as spec, plus `INT_CV001` on its permissive (R104); 75 events |
| overload | as spec | as spec with `amount` 1.0 (R109); 85 events |
| chute-blockage | "whichever first" | overload relay, +109.7 s; no interlock first (R110); 78 events |
| failed-zero-speed | as spec | as spec; `INT_CV001` 1.000 s after the fault; 73 events |
| welded-contactor | e-stop stops CV003 | neither `Start` false nor the e-stop stops it; it runs to the end at ≥ 1.91 m/s (R108); 99 events |
| feed-starve | rates fall in order | scales settle ≤ 5 t/h at 104.2, 133.7, 153.7 s; the log holds only the fault (R105); 64 events |

**Record → replay:** all eight byte-identical; each recording holds exactly the
scenario's actions (3 each, 4 for normal-start-stop, 6 for welded-contactor).

**Names confirmed** against `dse tags` (120 tags) and `dse catalog export`:
fault ids `thermal-bias` (`motor`), `blockage` (`transfer-chute`), `fail-low`
(every instrument, including `zero-speed-switch`), `contactor-welded`
(`motor-starter`), `starve` (`bulk-source`); tags `CVn.Start`, `CVn.SafetyReset`,
`CVn.EStop`, `CVn.PullKey1`, `CVn.Speed`, `CVn.Current`, `CVn.TonnesPerHour`,
`CVn.Stopped`, `CVn.Tripped`, `CVn.SafetyOk`, `Feed.Enabled`. The e-stop is the
safety relay's last channel: `Channel3` with the default two pull-keys.

## File structure

```
src/Dse.Components/Flow/BulkSource.cs          + optional `enabled` parameter (Task 1)
tests/Dse.Components.Tests/SourceSinkTests.cs  + 1 test (Task 1)
tests/Dse.Components.Tests/Catalogue/FlowFactoryTests.cs              + 1 test (Task 1)
tests/Dse.Components.Tests/Catalogue/Golden/components-catalogue.json regenerated (Task 1)
tests/Dse.Configuration.Tests/Golden/plant.schema.json                regenerated (Task 1)
samples/mine-conveyors/
  plant.json                      the line and its twelve controllers (Task 3)
  README.md                       the line, the philosophy, one section per scenario (Task 7)
  scenarios/<name>.json           eight scenarios (Tasks 4–6)
  expected/<name>.log             eight goldens, generated (Tasks 4–6)
tests/Dse.Samples.Tests/
  Dse.Samples.Tests.csproj        new; copies samples/mine-conveyors/** to the output (Task 2)
  CausalChain.cs                  EventPattern, Absence, CausalChain (Task 2)
  CausalChainTests.cs             11 tests (Task 2)
  StateChain.cs                   TagSample, StateChain (Task 2)
  StateChainTests.cs              5 tests (Task 2)
  Cli.cs                          CliRun, Cli.Run — the CLI in-process (Task 3)
  Sample.cs                       paths, names, catalogue, cached runs, Schedule, Trace (Task 3; names grow in 4–6)
  MineConveyorTests.cs            plant tests (Task 3); scenario theories (Task 4); state facts (Task 6)
  Stories.cs                      Story and the eight stories (Task 4; grows in 5–6)
  SampleReadmeTests.cs            README quotes (Task 7)
Dse.sln                           + Dse.Samples.Tests (Task 2)
README.md                         status and quick start (Task 8)
docs/control-blocks.md            points at the sample (Task 8)
docs/superpowers/specs/2026-09-02-industrial-process-simulation-engine-design.md   §4, §15.1, §15.2 (Task 8)
```

The 6a spec (`docs/superpowers/specs/2026-09-25-mine-conveyor-sample-design.md`)
was amended with this plan, before execution; no task edits it.

## Task map

| # | Task | Model | Tests after |
|---|---|---|---|
| 1 | `bulk-source` `enabled` parameter; two catalogue goldens | sonnet | 1257 |
| 2 | Test project, `CausalChain` and `StateChain` | sonnet | 1273 |
| 3 | The plant: `plant.json`, validate, loader, schema | sonnet | 1276 |
| 4 | Scenario harness; normal-start-stop, pull-key, e-stop | opus | 1289 |
| 5 | Overload, chute-blockage, failed-zero-speed | opus | 1301 |
| 6 | Welded-contactor, feed-starve; the two state facts and the trace guard | opus | 1312 |
| 7 | The sample README and its quote test | sonnet | 1321 |
| 8 | Documentation and main-spec amendments | sonnet | 1321 |

Tasks are sequential. Reviewers use the larger model for Tasks 1, 4, 5 and 6
(Task 1 touches a shipped component and two goldens; 4–6 each golden must be
read against its story).

---

### Task 1: `bulk-source` can start disabled

**Model:** sonnet.

**Files:**
- Modify: `src/Dse.Components/Flow/BulkSource.cs` (descriptor, constructor, one XML summary)
- Test: `tests/Dse.Components.Tests/SourceSinkTests.cs` (+1 fact, + `using Dse.Io;`)
- Test: `tests/Dse.Components.Tests/Catalogue/FlowFactoryTests.cs` (+1 fact)
- Regenerate: `tests/Dse.Components.Tests/Catalogue/Golden/components-catalogue.json`
- Regenerate: `tests/Dse.Configuration.Tests/Golden/plant.schema.json`

**Interfaces:**
- Consumes: `Param.Bool(string name, string description, bool? @default = null)`,
  `ParameterValues.Bool(string)` (existing).
- Produces: `BulkSource(string id, MaterialType type, double rateKgPerSecond,
  MaterialProperties properties = default, double hopperCapacityKg =
  double.PositiveInfinity, bool enabled = true)`; the `bulk-source` parameter
  `enabled` (bool, default true) that Task 3's plant sets to false.

- [ ] **Step 1: Write the failing tests**

In `tests/Dse.Components.Tests/SourceSinkTests.cs`, add `using Dse.Io;` after
`using Dse.Core.Time;`, and insert before
`    [Fact]\n    public void ASinkWithCapacityFillsOnceAndSaysSo()`:

```csharp
    [Fact]
    public void ASourceBuiltDisabledCreatesNothingUntilItsEnabledTagIsWritten()
    {
        var source = new BulkSource("Feed", Ore, rateKgPerSecond: 2.0, enabled: false);
        var sink = new BulkSink("Pile");
        source.Out.ConnectTo(sink.In);
        Simulation sim = new SimulationBuilder(Options()).Add(sink).Add(source).Build();

        sim.RunFor(TimeSpan.FromSeconds(2));
        Assert.False(sim.IO.Read("Feed.Enabled").AsBool);
        Assert.Equal(0.0, sim.MassBalance.Created, 9);

        sim.WriteIn(TimeSpan.Zero, "Feed.Enabled", TagValue.Bool(true));
        sim.RunFor(TimeSpan.FromSeconds(1));
        Assert.Equal(2.0, sim.MassBalance.Created, 9);
    }
```

In `tests/Dse.Components.Tests/Catalogue/FlowFactoryTests.cs`, insert before
`    [Fact]\n    public void AProcessUnitGetsOneInletPerRecipeLineAndANestedHold()`:

```csharp
    [Fact]
    public void ASourceIsEnabledAtPowerUpUnlessTheFileSaysOtherwise()
    {
        BulkSource running = MechanicalFactoryTests.Build<BulkSource>(BulkSource.Descriptor, """{ "material": "flour", "rateKgPerS": 2 }""", Context());
        BulkSource held = MechanicalFactoryTests.Build<BulkSource>(
            BulkSource.Descriptor, """{ "material": "flour", "rateKgPerS": 2, "enabled": false }""", Context());

        Assert.True(running.Enabled.Value);
        Assert.False(held.Enabled.Value);
    }
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/Dse.Components.Tests --nologo`
Expected: the build fails with `error CS1739: The best overload for 'BulkSource' does not have a parameter named 'enabled'`.

- [ ] **Step 3: Add the parameter**

In `src/Dse.Components/Flow/BulkSource.cs` make these five replacements.

The factory:

```csharp
            p.DoubleOr("hopperCapacityKg", double.PositiveInfinity)))
```

becomes

```csharp
            p.DoubleOr("hopperCapacityKg", double.PositiveInfinity), p.Bool("enabled")))
```

The parameter list — after the `hopperCapacityKg` line
(`            Param.Double("hopperCapacityKg", "Hopper size. Omit for unlimited.", "kg", min: 0.0, exclusiveMin: true, optional: true),`)
add:

```csharp
            Param.Bool("enabled", "Whether the feeder runs before anything writes Enabled. A PLC output is off at power-up; set false to match.", @default: true),
```

The port:

```csharp
            PortSpec.In<bool>("Enabled", description: "Defaults to true."),
```

becomes

```csharp
            PortSpec.In<bool>("Enabled", description: "Defaults to the enabled parameter."),
```

The constructor signature:

```csharp
        double hopperCapacityKg = double.PositiveInfinity)
        : base(id)
```

becomes

```csharp
        double hopperCapacityKg = double.PositiveInfinity,
        bool enabled = true)
        : base(id)
```

and its input:

```csharp
        Enabled = AddInput<bool>("Enabled", defaultValue: true);
```

becomes

```csharp
        Enabled = AddInput<bool>("Enabled", defaultValue: enabled);
```

Finally the property's summary,

```csharp
    /// <summary>False stops the feeder. Unconnected reads true.</summary>
```

becomes

```csharp
    /// <summary>False stops the feeder. Unconnected, it reads the <c>enabled</c> given at construction: true unless told otherwise.</summary>
```

- [ ] **Step 4: Run the component tests**

Run: `dotnet test tests/Dse.Components.Tests --nologo`
Expected: 127 passed, 1 failed —
`ComponentsExportTests.TheShippedCatalogueExportsExactlyTheGoldenFile` (the
catalogue golden lists parameters). The two new facts pass, and so does the
catalogue conformance sweep: the new parameter has a default, so
`ComponentsFixtures`' `bulk-source` fixture needs no change.

- [ ] **Step 5: Regenerate the two catalogue goldens and read their diffs**

Run: `DSE_UPDATE_GOLDEN=1 dotnet test tests/Dse.Components.Tests --nologo --filter "FullyQualifiedName~TheShippedCatalogueExportsExactlyTheGoldenFile"`
Run: `DSE_UPDATE_GOLDEN=1 dotnet test tests/Dse.Configuration.Tests --nologo --filter "FullyQualifiedName~PlantSchemaTests.MatchesTheGoldenFile"`
Then read `git diff tests/Dse.Components.Tests/Catalogue/Golden/components-catalogue.json tests/Dse.Configuration.Tests/Golden/plant.schema.json`
in full. It must be exactly this, and nothing else (measured):

```diff
--- a/tests/Dse.Components.Tests/Catalogue/Golden/components-catalogue.json
+++ b/tests/Dse.Components.Tests/Catalogue/Golden/components-catalogue.json
@@ -601,6 +601,13 @@
           "optional": true,
           "minimum": 0,
           "exclusiveMinimum": true
+        },
+        {
+          "name": "enabled",
+          "kind": "bool",
+          "required": false,
+          "description": "Whether the feeder runs before anything writes Enabled. A PLC output is off at power-up; set false to match.",
+          "default": true
         }
       ],
       "ports": [
@@ -615,7 +622,7 @@
           "name": "Enabled",
           "direction": "in",
           "valueType": "bool",
-          "description": "Defaults to true."
+          "description": "Defaults to the enabled parameter."
         },
         {
           "name": "HopperMass",
--- a/tests/Dse.Configuration.Tests/Golden/plant.schema.json
+++ b/tests/Dse.Configuration.Tests/Golden/plant.schema.json
@@ -701,6 +701,11 @@
               "description": "Hopper size. Omit for unlimited. Unit: kg.",
               "type": "number",
               "exclusiveMinimum": 0
+            },
+            "enabled": {
+              "description": "Whether the feeder runs before anything writes Enabled. A PLC output is off at power-up; set false to match.",
+              "type": "boolean",
+              "default": true
             }
           }
         }
```

- [ ] **Step 6: Build and test the solution**

Run: `dotnet build Dse.sln -c Release --nologo` — expect `0 Warning(s)`, `0 Error(s)`.
Run: `dotnet test Dse.sln --nologo` — expect **1257** (Components 128). Every
other golden — the scenario goldens, `conveyor-control.log`, the control
catalogue — is unchanged: `git status --short` lists only the five files of
this task.

- [ ] **Step 7: Commit**

```bash
git add src/Dse.Components/Flow/BulkSource.cs tests/Dse.Components.Tests/SourceSinkTests.cs tests/Dse.Components.Tests/Catalogue/FlowFactoryTests.cs tests/Dse.Components.Tests/Catalogue/Golden/components-catalogue.json tests/Dse.Configuration.Tests/Golden/plant.schema.json
```

```bash
git commit -m "feat(components): let a bulk source start disabled, as a PLC output does" -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>"
```

---

### Task 2: The test project, `CausalChain` and `StateChain`

**Model:** sonnet.

**Files:**
- Create: `tests/Dse.Samples.Tests/Dse.Samples.Tests.csproj`
- Create: `tests/Dse.Samples.Tests/CausalChain.cs`, `tests/Dse.Samples.Tests/StateChain.cs`
- Test: `tests/Dse.Samples.Tests/CausalChainTests.cs`, `tests/Dse.Samples.Tests/StateChainTests.cs`
- Modify: `Dse.sln` (through `dotnet sln add`)

**Interfaces:**
- Consumes: `Dse.Core.Logging.SimEventRecord(long Tick, DateTimeOffset SimTime,
  string Source, string Code, string Message)` (existing).
- Produces (namespace `Dse.Samples.Tests`):
  - `public sealed record EventPattern(string? Source, string Code, string MessageFragment = "")`
    with `bool Matches(SimEventRecord record)` and a `ToString()` of the form
    `CV001.Starter CONTACTOR_OPENED` / `any source INTERLOCK_TRIP "fragment"`.
  - `public sealed record Absence(EventPattern? After, EventPattern Forbidden, EventPattern? Until = null)`.
  - `public static class CausalChain` with
    `static string? FindChain(IReadOnlyList<SimEventRecord> events, IReadOnlyList<EventPattern> chain)`,
    `static string? FindAbsence(IReadOnlyList<SimEventRecord> events, Absence absence)` and
    `static string Line(SimEventRecord record)` (the `EventLog.ToText()` line, no newline).
  - `public sealed record TagSample(TimeSpan Time, double Value)`.
  - `public static class StateChain` with
    `static TimeSpan? SettlesAtOrBelow(IReadOnlyList<TagSample> series, double ceiling, TimeSpan from)` and
    `static string? FindFallInOrder(IReadOnlyDictionary<string, IReadOnlyList<TagSample>> traces, IReadOnlyList<string> order, TimeSpan from, double floor, double ceiling)`.

- [ ] **Step 1: Create the project file**

`tests/Dse.Samples.Tests/Dse.Samples.Tests.csproj`:

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
    <!-- Test-only. Pinned to the last MIT-licensed major: 9.x ships under a maintenance-fee EULA. Do not upgrade without the owner's decision. -->
    <PackageReference Include="JsonSchema.Net" Version="8.0.5" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.4" />
  </ItemGroup>

  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>

  <ItemGroup>
    <None Include="..\..\samples\mine-conveyors\**\*"
          Link="mine-conveyors\%(RecursiveDir)%(Filename)%(Extension)" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\Dse.Cli\Dse.Cli.csproj" />
    <ProjectReference Include="..\..\src\Dse.Scenarios\Dse.Scenarios.csproj" />
    <ProjectReference Include="..\..\src\Dse.Configuration\Dse.Configuration.csproj" />
    <ProjectReference Include="..\..\src\Dse.Control.Catalogue\Dse.Control.Catalogue.csproj" />
    <ProjectReference Include="..\..\src\Dse.Components\Dse.Components.csproj" />
  </ItemGroup>

</Project>
```

The `None` item copies the whole sample folder to `bin/.../mine-conveyors/`,
keeping its layout, so a scenario's `"plant": "../plant.json"` resolves there.
The folder does not exist until Task 3; an empty glob is not an error.

- [ ] **Step 2: Add the project to the solution**

Run: `dotnet sln Dse.sln add tests/Dse.Samples.Tests/Dse.Samples.Tests.csproj`
Expected: `Project `tests/Dse.Samples.Tests/Dse.Samples.Tests.csproj` added to the solution.`
Then `git diff --stat Dse.sln` shows 15 insertions: one `Project(...)` entry,
twelve configuration lines, and a nesting line placing it under the existing
`tests` folder `{0AB3BF05-4346-4AA6-1389-037BE0695223}`.

- [ ] **Step 3: Write the failing tests**

`tests/Dse.Samples.Tests/CausalChainTests.cs`:

```csharp
using Dse.Core.Logging;

namespace Dse.Samples.Tests;

public class CausalChainTests
{
    private static readonly DateTimeOffset Start = new(2026, 3, 2, 6, 0, 0, TimeSpan.FromHours(8));

    private static SimEventRecord At(long tick, string source, string code, string message) =>
        new(tick, Start.AddMilliseconds(tick * 10), source, code, message);

    private static readonly SimEventRecord[] Log =
    [
        At(0, "PERM_CV001", "PERMISSIVE_LOST", "CV001.SafetyOk dropped."),
        At(100, "CV001.Starter", "CONTACTOR_CLOSED", "Motor energised."),
        At(200, "INT_CV001", "INTERLOCK_TRIP", "CV002.Stopped abnormal."),
        At(201, "CV001.Start", "WRITE", "Set to false by INT_CV001."),
        At(300, "CV001.Starter", "CONTACTOR_OPENED", "Motor de-energised."),
    ];

    [Fact]
    public void AChainInLogOrderHoldsWithOtherEventsBetween()
    {
        EventPattern[] chain =
        [
            new("CV001.Starter", "CONTACTOR_CLOSED"),
            new("CV001.Start", "WRITE", "by INT_CV001."),
            new("CV001.Starter", "CONTACTOR_OPENED"),
        ];

        Assert.Null(CausalChain.FindChain(Log, chain));
    }

    [Fact]
    public void AChainOutOfOrderNamesTheFirstPatternNotFoundAndTheLastMatched()
    {
        EventPattern[] chain =
        [
            new("INT_CV001", "INTERLOCK_TRIP"),
            new("CV001.Starter", "CONTACTOR_CLOSED"),
        ];

        Assert.Equal(
            "The chain breaks at CV001.Starter CONTACTOR_CLOSED: no such event after INT_CV001 INTERLOCK_TRIP, " +
            "matched by '06:00:02.000  INT_CV001  INTERLOCK_TRIP  CV002.Stopped abnormal.'.",
            CausalChain.FindChain(Log, chain));
    }

    [Fact]
    public void AChainWhoseFirstPatternIsMissingSaysSoFromTheStart()
    {
        Assert.Equal(
            "The chain breaks at CV002.Starter CONTACTOR_OPENED: no such event after the start of the log.",
            CausalChain.FindChain(Log, [new EventPattern("CV002.Starter", "CONTACTOR_OPENED")]));
    }

    [Fact]
    public void OneEventCannotSatisfyTwoPatterns()
    {
        EventPattern twice = new("CV001.Starter", "CONTACTOR_OPENED");

        Assert.NotNull(CausalChain.FindChain(Log, [twice, twice]));
    }

    [Theory]
    [InlineData("CV001", "CONTACTOR_CLOSED", "")]                      // a source is exact, not a prefix
    [InlineData("CV001.Starter", "CONTACTOR", "")]                     // so is a code
    [InlineData("CV001.Starter", "CONTACTOR_CLOSED", "de-energised")]  // the fragment must be in the message
    public void APatternMatchesExactSourceExactCodeAndAMessageFragment(string source, string code, string fragment)
    {
        Assert.NotNull(CausalChain.FindChain(Log, [new EventPattern(source, code, fragment)]));
    }

    [Fact]
    public void ANullSourceMatchesAnySource()
    {
        Assert.Null(CausalChain.FindChain(Log, [new EventPattern(null, "INTERLOCK_TRIP", "CV002.Stopped")]));
    }

    [Fact]
    public void AnAbsenceHoldsWhenTheForbiddenEventIsOutsideItsWindow()
    {
        var absence = new Absence(
            new EventPattern("CV001.Starter", "CONTACTOR_CLOSED"),
            new EventPattern("CV001.Starter", "CONTACTOR_OPENED"),
            new EventPattern("CV001.Start", "WRITE"));

        Assert.Null(CausalChain.FindAbsence(Log, absence));
    }

    [Fact]
    public void AnAbsenceThatFailsQuotesTheOffendingLine()
    {
        var absence = new Absence(new EventPattern("CV001.Starter", "CONTACTOR_CLOSED"), new EventPattern(null, "INTERLOCK_TRIP"));

        Assert.Equal(
            "any source INTERLOCK_TRIP must not occur here, but '06:00:02.000  INT_CV001  INTERLOCK_TRIP  CV002.Stopped abnormal.' does.",
            CausalChain.FindAbsence(Log, absence));
    }

    [Fact]
    public void AnAbsenceWhoseBoundsNeverOccurFailsRatherThanPassingEmpty()
    {
        EventPattern forbidden = new(null, "INTERLOCK_TRIP");

        Assert.Equal(
            "The absence of any source INTERLOCK_TRIP starts at SEQ_STOP SEQUENCE_COMPLETE, which never occurs.",
            CausalChain.FindAbsence(Log, new Absence(new EventPattern("SEQ_STOP", "SEQUENCE_COMPLETE"), forbidden)));
        Assert.Equal(
            "The absence of any source INTERLOCK_TRIP ends at SEQ_STOP SEQUENCE_COMPLETE, which never occurs after its start.",
            CausalChain.FindAbsence(Log, new Absence(null, forbidden, new EventPattern("SEQ_STOP", "SEQUENCE_COMPLETE"))));
    }
}
```

`tests/Dse.Samples.Tests/StateChainTests.cs`:

```csharp
namespace Dse.Samples.Tests;

public class StateChainTests
{
    private static List<TagSample> Series(params (double Seconds, double Value)[] points) =>
        points.Select(p => new TagSample(TimeSpan.FromSeconds(p.Seconds), p.Value)).ToList();

    private static readonly TimeSpan Start = TimeSpan.FromSeconds(10);

    [Fact]
    public void ASeriesSettlesAtTheStartOfItsLastQuietRun()
    {
        List<TagSample> series = Series((9, 0), (10, 300), (11, 2), (12, 40), (13, 3), (14, 1), (15, 0));

        Assert.Equal(TimeSpan.FromSeconds(13), StateChain.SettlesAtOrBelow(series, 5, Start));
    }

    [Fact]
    public void ASeriesThatEndsAboveTheCeilingNeverSettles()
    {
        Assert.Null(StateChain.SettlesAtOrBelow(Series((10, 300), (11, 0), (12, 6)), 5, Start));
    }

    [Fact]
    public void TagsThatFallOneAfterAnotherHoldInThatOrder()
    {
        var traces = new Dictionary<string, IReadOnlyList<TagSample>>(StringComparer.Ordinal)
        {
            ["A"] = Series((10, 300), (11, 0), (12, 0), (13, 0)),
            ["B"] = Series((10, 300), (11, 300), (12, 0), (13, 0)),
        };

        Assert.Null(StateChain.FindFallInOrder(traces, ["A", "B"], Start, 250, 5));
    }

    [Fact]
    public void ATagThatFallsNoLaterThanTheOneBeforeItIsNamed()
    {
        var traces = new Dictionary<string, IReadOnlyList<TagSample>>(StringComparer.Ordinal)
        {
            ["A"] = Series((10, 300), (11, 300), (12, 0), (13, 0)),
            ["B"] = Series((10, 300), (11, 0), (12, 0), (13, 0)),
        };

        Assert.Equal(
            "B settles at or below 5 at 11 s, not after A at 12 s.",
            StateChain.FindFallInOrder(traces, ["A", "B"], Start, 250, 5));
    }

    [Fact]
    public void ATagThatWasNotRunningAtTheStartProvesNothing()
    {
        var traces = new Dictionary<string, IReadOnlyList<TagSample>>(StringComparer.Ordinal)
        {
            ["A"] = Series((10, 0), (11, 0)),
        };

        Assert.Equal(
            "A was not above 250 at 10 s, so its fall proves nothing.",
            StateChain.FindFallInOrder(traces, ["A"], Start, 250, 5));
    }
}
```

- [ ] **Step 4: Run them to see them fail**

Run: `dotnet test tests/Dse.Samples.Tests --nologo`
Expected: the build fails with `error CS0246: The type or namespace name 'EventPattern' could not be found` (and the same for `Absence`, `CausalChain`, `TagSample` and `StateChain`).

- [ ] **Step 5: Write the helpers**

`tests/Dse.Samples.Tests/CausalChain.cs`:

```csharp
using System.Globalization;
using Dse.Core.Logging;

namespace Dse.Samples.Tests;

/// <summary>
/// One event a story expects: an exact source (or any source, when null), an
/// exact code, and a fragment the message must contain. Exact, not prefix: a
/// pattern on <c>CV001</c> never matches <c>CV001.Starter</c>.
/// </summary>
public sealed record EventPattern(string? Source, string Code, string MessageFragment = "")
{
    public bool Matches(SimEventRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return (Source is null || string.Equals(record.Source, Source, StringComparison.Ordinal))
            && string.Equals(record.Code, Code, StringComparison.Ordinal)
            && record.Message.Contains(MessageFragment, StringComparison.Ordinal);
    }

    public override string ToString()
    {
        string source = Source ?? "any source";
        return MessageFragment.Length == 0 ? $"{source} {Code}" : $"{source} {Code} \"{MessageFragment}\"";
    }
}

/// <summary>
/// Something that must not happen: <see cref="Forbidden"/> never occurs after
/// the first match of <see cref="After"/> (the start of the log when null) and
/// before the first match of <see cref="Until"/> that follows it (the end of the
/// log when null).
/// </summary>
public sealed record Absence(EventPattern? After, EventPattern Forbidden, EventPattern? Until = null);

/// <summary>
/// Asserts a scenario's story over its event log. A chain is an ordered
/// subsequence: each pattern must match an event strictly after the event the
/// previous pattern matched, and other events may come between. Both checks
/// return null when they hold and a message naming what was missing when they
/// do not, so a test reads <c>Assert.Null(CausalChain.FindChain(...))</c> and a
/// failure prints the reason.
/// </summary>
public static class CausalChain
{
    public static string? FindChain(IReadOnlyList<SimEventRecord> events, IReadOnlyList<EventPattern> chain)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(chain);

        int next = 0;
        string lastMatched = "the start of the log";
        foreach (EventPattern pattern in chain)
        {
            int found = IndexOf(events, pattern, next);
            if (found < 0)
            {
                return $"The chain breaks at {pattern}: no such event after {lastMatched}.";
            }

            lastMatched = $"{pattern}, matched by '{Line(events[found])}'";
            next = found + 1;
        }

        return null;
    }

    public static string? FindAbsence(IReadOnlyList<SimEventRecord> events, Absence absence)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(absence);

        int start = 0;
        if (absence.After is { } after)
        {
            int found = IndexOf(events, after, 0);
            if (found < 0)
            {
                return $"The absence of {absence.Forbidden} starts at {after}, which never occurs.";
            }

            start = found + 1;
        }

        int end = events.Count;
        if (absence.Until is { } until)
        {
            end = IndexOf(events, until, start);
            if (end < 0)
            {
                return $"The absence of {absence.Forbidden} ends at {until}, which never occurs after its start.";
            }
        }

        for (int i = start; i < end; i++)
        {
            if (absence.Forbidden.Matches(events[i]))
            {
                return $"{absence.Forbidden} must not occur here, but '{Line(events[i])}' does.";
            }
        }

        return null;
    }

    /// <summary>The record as <c>EventLog.ToText()</c> prints it.</summary>
    public static string Line(SimEventRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{record.SimTime:HH:mm:ss.fff}  {record.Source}  {record.Code}  {record.Message}");
    }

    private static int IndexOf(IReadOnlyList<SimEventRecord> events, EventPattern pattern, int from)
    {
        for (int i = from; i < events.Count; i++)
        {
            if (pattern.Matches(events[i]))
            {
                return i;
            }
        }

        return -1;
    }
}
```

`tests/Dse.Samples.Tests/StateChain.cs`:

```csharp
using System.Globalization;

namespace Dse.Samples.Tests;

/// <summary>One sampled value of a tag, at a time from the start of the run.</summary>
public sealed record TagSample(TimeSpan Time, double Value);

/// <summary>
/// Asserts a story told by state rather than by events: a set of sampled tags
/// that fall, one after another, in a given order. Like <see cref="CausalChain"/>,
/// a check returns null when it holds and a message when it does not.
/// </summary>
public static class StateChain
{
    /// <summary>
    /// The first sample time at or after <paramref name="from"/> from which the
    /// series stays at or below <paramref name="ceiling"/> to its end; null if it
    /// never settles there.
    /// </summary>
    public static TimeSpan? SettlesAtOrBelow(IReadOnlyList<TagSample> series, double ceiling, TimeSpan from)
    {
        ArgumentNullException.ThrowIfNull(series);

        TimeSpan? since = null;
        foreach (TagSample sample in series)
        {
            if (sample.Time < from)
            {
                continue;
            }

            since = sample.Value <= ceiling ? since ?? sample.Time : null;
        }

        return since;
    }

    /// <summary>
    /// Null when every tag in <paramref name="order"/> was above
    /// <paramref name="floor"/> at <paramref name="from"/> — so its fall means
    /// something — and then settles at or below <paramref name="ceiling"/>, each
    /// strictly later than the tag before it. Otherwise a message naming the first
    /// tag that does not.
    /// </summary>
    public static string? FindFallInOrder(
        IReadOnlyDictionary<string, IReadOnlyList<TagSample>> traces,
        IReadOnlyList<string> order,
        TimeSpan from,
        double floor,
        double ceiling)
    {
        ArgumentNullException.ThrowIfNull(traces);
        ArgumentNullException.ThrowIfNull(order);

        TimeSpan? previous = null;
        string previousTag = string.Empty;
        foreach (string tag in order)
        {
            if (!traces.TryGetValue(tag, out IReadOnlyList<TagSample>? series))
            {
                return $"There is no trace of {tag}.";
            }

            TagSample? start = series.FirstOrDefault(s => s.Time >= from);
            if (start is null || start.Value <= floor)
            {
                return string.Create(CultureInfo.InvariantCulture,
                    $"{tag} was not above {floor} at {from.TotalSeconds} s, so its fall proves nothing.");
            }

            TimeSpan? settled = SettlesAtOrBelow(series, ceiling, from);
            if (settled is not { } at)
            {
                return string.Create(CultureInfo.InvariantCulture, $"{tag} never settles at or below {ceiling}.");
            }

            if (previous is { } before && at <= before)
            {
                return string.Create(CultureInfo.InvariantCulture,
                    $"{tag} settles at or below {ceiling} at {at.TotalSeconds} s, not after {previousTag} at {before.TotalSeconds} s.");
            }

            previous = at;
            previousTag = tag;
        }

        return null;
    }
}
```

- [ ] **Step 6: Run the tests to see them pass**

Run: `dotnet test tests/Dse.Samples.Tests --nologo`
Expected: `Passed!  - Failed:     0, Passed:    16, Skipped:     0, Total:    16`
(`CausalChainTests`: eight facts and one theory of three rows; `StateChainTests`:
five facts).

- [ ] **Step 7: Build and test the solution**

Run: `dotnet build Dse.sln -c Release --nologo` — expect `0 Warning(s)`, `0 Error(s)`.
Run: `dotnet test Dse.sln --nologo` — expect ten `Passed!` lines; the total is
**1273** (1257 + 16).

- [ ] **Step 8: Commit**

```bash
git add tests/Dse.Samples.Tests/Dse.Samples.Tests.csproj tests/Dse.Samples.Tests/CausalChain.cs tests/Dse.Samples.Tests/CausalChainTests.cs tests/Dse.Samples.Tests/StateChain.cs tests/Dse.Samples.Tests/StateChainTests.cs Dse.sln
```

```bash
git commit -m "test(samples): add the samples test project and the event and state chain matchers" -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>"
```

---

### Task 3: The plant

**Model:** sonnet.

**Files:**
- Create: `samples/mine-conveyors/plant.json`
- Create: `tests/Dse.Samples.Tests/Cli.cs`
- Create: `tests/Dse.Samples.Tests/Sample.cs`
- Test: `tests/Dse.Samples.Tests/MineConveyorTests.cs`

**Interfaces:**
- Consumes: Task 1's `bulk-source` `enabled`; Task 2's `TagSample`;
  `Dse.Cli.CliApp.Run(string[] args, TextWriter stdout, TextWriter stderr)` and
  `Dse.Cli.ExitCodes` (public); `PlantLoader.Load(string json,
  ComponentCatalogue catalogue, LoadOptions? options = null)`;
  `PlantSchema.Generate(ComponentCatalogue)`; `JsonSchema.FromText` /
  `Evaluate(JsonElement)`; `ScenarioLoader.Parse`, `ScenarioRunner.Run(Scenario,
  string, ComponentCatalogue)`; `Simulation.WriteAt`, `InjectFaultAt`,
  `ClearFaultAt`, `Tick`, `IO.Read(string)`, `IO.Directory.Find(string)`;
  `ScenarioValue.ToTagValue(TagKind)`.
- Produces:
  - `internal sealed record CliRun(int ExitCode, string Out, string Err)` and
    `internal static class Cli { static CliRun Run(params string[] args) }`.
  - `public static class Sample` with `Names` (`IReadOnlyList<string>`, empty in
    this task), `Scenarios` (`TheoryData<string>`), `Catalogue`, `Root`, `Plant`,
    `Readme`, `SourceRoot`, `Updating`, `Scenario(name)`, `Golden(name)`,
    `SourceGolden(name)`, `Run(name)` (a cached `ScenarioRunResult`),
    `Schedule(Simulation, ScenarioAction)` and
    `Trace(string name, IReadOnlyList<string> tags, TimeSpan every)` returning
    `IReadOnlyDictionary<string, IReadOnlyList<TagSample>>`.
  - The plant's tag and controller names, listed in R118.

- [ ] **Step 1: Write the CLI helper**

`tests/Dse.Samples.Tests/Cli.cs`:

```csharp
using Dse.Cli;

namespace Dse.Samples.Tests;

/// <summary>What one in-process run of the CLI printed, and how it exited.</summary>
internal sealed record CliRun(int ExitCode, string Out, string Err);

/// <summary>Runs the CLI in-process, as <c>Dse.Cli.Tests</c> does, and captures both streams.</summary>
internal static class Cli
{
    public static CliRun Run(params string[] args)
    {
        using var stdout = new StringWriter { NewLine = "\n" };
        using var stderr = new StringWriter { NewLine = "\n" };
        int exit = CliApp.Run(args, stdout, stderr);
        return new CliRun(exit, stdout.ToString(), stderr.ToString());
    }
}
```

- [ ] **Step 2: Write `Sample`**

`tests/Dse.Samples.Tests/Sample.cs` — `Names` is empty here; Tasks 4–6 fill it.
`Schedule` and `Trace` are first used in Tasks 4 and 6:

```csharp
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Dse.Components;
using Dse.Control.Catalogue;
using Dse.Configuration;
using Dse.Core;
using Dse.Core.Catalogue;
using Dse.Core.Faults;
using Dse.Io;
using Dse.Scenarios;

namespace Dse.Samples.Tests;

/// <summary>
/// The mine-conveyor sample's files: read from the copy in the test output, and
/// written — only when DSE_UPDATE_GOLDEN=1 — at their source under
/// <c>samples/mine-conveyors/</c>.
/// </summary>
public static class Sample
{
    /// <summary>The eight scenarios, in the order the README tells them.</summary>
    public static IReadOnlyList<string> Names { get; } = [];

    public static TheoryData<string> Scenarios => new(Names);

    /// <summary>The CLI's default catalogue: the components and the control blocks.</summary>
    public static ComponentCatalogue Catalogue { get; } =
        new CatalogueBuilder().Add<ComponentsModule>().Add<ControlModule>().Build();

    public static string Root { get; } = Path.Combine(AppContext.BaseDirectory, "mine-conveyors");

    public static string Plant => Path.Combine(Root, "plant.json");

    public static string Readme => Path.Combine(Root, "README.md");

    /// <summary>The sample's folder in the repository, found from this source file.</summary>
    public static string SourceRoot { get; } = FindSourceRoot();

    public static bool Updating => Environment.GetEnvironmentVariable("DSE_UPDATE_GOLDEN") == "1";

    public static string Scenario(string name) => Path.Combine(Root, "scenarios", name + ".json");

    public static string Golden(string name) => Path.Combine(Root, "expected", name + ".log");

    public static string SourceGolden(string name) => Path.Combine(SourceRoot, "expected", name + ".log");

    private static readonly ConcurrentDictionary<string, Lazy<ScenarioRunResult>> Runs = new(StringComparer.Ordinal);

    /// <summary>
    /// A scenario's run through the same runner <c>dse run</c> uses, once per test
    /// process: a run is deterministic, so every test that reads it shares it.
    /// </summary>
    public static ScenarioRunResult Run(string name) =>
        Runs.GetOrAdd(name, n => new Lazy<ScenarioRunResult>(() => RunOnce(n))).Value;

    private static ScenarioRunResult RunOnce(string name)
    {
        string path = Scenario(name);
        ScenarioParseResult parsed = ScenarioLoader.Parse(File.ReadAllText(path));
        Scenario scenario = parsed.Scenario ?? throw new InvalidOperationException($"'{name}' does not parse: {parsed.ToText()}");
        ScenarioRunResult result = ScenarioRunner.Run(scenario, File.ReadAllText(scenario.ResolvePlantPath(path)), Catalogue);
        return result.IsValid ? result : throw new InvalidOperationException($"'{name}' does not run: {result.ToText()}");
    }

    /// <summary>
    /// Runs a scenario live and samples the named tags every <paramref name="every"/>,
    /// from the first sample to the end of the run. The event log holds events
    /// only; a story told by a measured value needs this.
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<TagSample>> Trace(string name, IReadOnlyList<string> tags, TimeSpan every)
    {
        ArgumentNullException.ThrowIfNull(tags);
        string path = Scenario(name);
        Scenario scenario = ScenarioLoader.Parse(File.ReadAllText(path)).Scenario
            ?? throw new InvalidOperationException($"'{name}' does not parse.");
        LoadResult load = PlantLoader.Load(File.ReadAllText(scenario.ResolvePlantPath(path)), Catalogue, scenario.ToLoadOptions());
        Simulation simulation = load.Builder?.Build() ?? throw new InvalidOperationException(load.ToText());
        foreach (ScenarioAction action in scenario.Timeline)
        {
            Schedule(simulation, action);
        }

        TimeSpan step = load.Options!.TimeStep;
        long ticks = scenario.Duration.Ticks / step.Ticks;
        if (every < step || every.Ticks % step.Ticks != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(every), every, $"Sample every whole number of {step.TotalMilliseconds} ms steps, at least one.");
        }

        long stride = every.Ticks / step.Ticks;
        var traces = tags.ToDictionary(t => t, _ => new List<TagSample>(), StringComparer.Ordinal);
        for (long tick = 1; tick <= ticks; tick++)
        {
            simulation.Tick();
            if (tick % stride == 0)
            {
                TimeSpan time = TimeSpan.FromTicks(step.Ticks * tick);
                foreach (string tag in tags)
                {
                    traces[tag].Add(new TagSample(time, simulation.IO.Read(tag).AsDouble));
                }
            }
        }

        return traces.ToDictionary(p => p.Key, p => (IReadOnlyList<TagSample>)p.Value, StringComparer.Ordinal);
    }

    /// <summary>Schedules one scenario action on a live simulation, the way <c>ScenarioRunner</c> does, so a recorder sees it.</summary>
    public static void Schedule(Simulation simulation, ScenarioAction action)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        switch (action)
        {
            case WriteAction write:
                TagKind kind = simulation.IO.Directory.Find(write.Tag).Kind;
                TagValue value = write.Value.ToTagValue(kind)
                    ?? throw new InvalidOperationException($"'{write.Tag}' cannot take {write.Value}.");
                simulation.WriteAt(write.At, write.Tag, value);
                break;
            case FaultAction fault:
                simulation.InjectFaultAt(fault.At, fault.ComponentId, fault.FaultId, new FaultArguments(fault.Arguments.ToArray()));
                break;
            case ClearAction clear:
                simulation.ClearFaultAt(clear.At, clear.ComponentId, clear.FaultId);
                break;
            default:
                throw new InvalidOperationException($"'{action?.GetType().Name}' is not a scenario action.");
        }
    }

    private static string FindSourceRoot([CallerFilePath] string thisFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", "..", "samples", "mine-conveyors"));
}
```

- [ ] **Step 3: Write the failing plant tests**

`tests/Dse.Samples.Tests/MineConveyorTests.cs` (Task 4 replaces this file with its
full form, keeping these three facts unchanged):

```csharp
using System.Text.Json;
using Dse.Cli;
using Dse.Configuration;
using Json.Schema;

namespace Dse.Samples.Tests;

/// <summary>The mine-conveyor sample's plant: it validates, and the schema accepts it.</summary>
public class MineConveyorTests
{
    [Fact]
    public void ThePlantValidatesWithTwelveControllers()
    {
        CliRun run = Cli.Run("validate", Sample.Plant);

        Assert.Equal(ExitCodes.Ok, run.ExitCode);
        Assert.Empty(run.Err);
        Assert.StartsWith($"OK  {Sample.Plant}\n", run.Out, StringComparison.Ordinal);
        Assert.Contains("  controllers   12\n", run.Out, StringComparison.Ordinal);
    }

    [Fact]
    public void TheLoaderReportsNothingAtAllNotEvenAWarning()
    {
        LoadResult load = PlantLoader.Load(File.ReadAllText(Sample.Plant), Sample.Catalogue);

        Assert.True(load.IsValid, load.ToText());
        Assert.Empty(load.Diagnostics);
    }

    [Fact]
    public void TheGeneratedSchemaAcceptsThePlantAndRejectsAMistakeInIt()
    {
        JsonSchema schema = JsonSchema.FromText(PlantSchema.Generate(Sample.Catalogue));
        string plant = File.ReadAllText(Sample.Plant);
        string broken = plant.Replace("\"lengthM\": 60", "\"lengthM\": \"60\"", StringComparison.Ordinal);

        Assert.NotEqual(plant, broken);
        Assert.True(schema.Evaluate(JsonDocument.Parse(plant).RootElement).IsValid);
        Assert.False(schema.Evaluate(JsonDocument.Parse(broken).RootElement).IsValid);
    }
}
```

- [ ] **Step 4: Run them to see them fail**

Run: `dotnet test tests/Dse.Samples.Tests --nologo --filter "FullyQualifiedName~MineConveyorTests"`
Expected: 3 failed. `ThePlantValidatesWithTwelveControllers` exits 3 (`Cannot
read`), the other two throw `FileNotFoundException` or
`DirectoryNotFoundException` for `mine-conveyors/plant.json`.

- [ ] **Step 5: Write the plant**

`samples/mine-conveyors/plant.json`:

```json
{
  "defaults": { "seed": 7, "timeStepMs": 10, "startTime": "2026-03-02T06:00:00+08:00" },
  "materials": [
    { "name": "Ore", "kind": "bulk", "properties": { "density": 1600, "moisture": 0.04, "temperature": 25 } }
  ],
  "components": [
    { "id": "Feed", "type": "bulk-source",
      "parameters": { "material": "Ore", "rateKgPerS": 80, "hopperCapacityKg": 2000, "enabled": false } },
    { "id": "CV001", "type": "conveyor", "parameters": {
        "lengthM": 60, "cellSizeM": 1, "beltWidthM": 1.0, "angleOfReposeDeg": 20, "materialDensityKgM3": 1600,
        "emptyBeltMassKg": 1200, "frictionCoefficient": 0.03, "pulleyDiameterM": 0.63, "gearRatio": 25,
        "motor": { "ratedPowerW": 4000, "ratedSpeedRadPerS": 155, "ratedCurrentA": 8.2, "coastTimeConstantS": 1.5 },
        "tailDragN": 200 } },
    { "id": "CH1", "type": "transfer-chute", "parameters": { "capacityKg": 1000 } },
    { "id": "CV002", "type": "conveyor", "parameters": {
        "lengthM": 40, "cellSizeM": 1, "beltWidthM": 1.0, "angleOfReposeDeg": 20, "materialDensityKgM3": 1600,
        "emptyBeltMassKg": 800, "frictionCoefficient": 0.03, "pulleyDiameterM": 0.63, "gearRatio": 25,
        "motor": { "ratedPowerW": 3000, "ratedSpeedRadPerS": 155, "ratedCurrentA": 6.3, "coastTimeConstantS": 1.5 },
        "tailDragN": 150 } },
    { "id": "CH2", "type": "transfer-chute", "parameters": { "capacityKg": 1000 } },
    { "id": "CV003", "type": "conveyor", "parameters": {
        "lengthM": 30, "cellSizeM": 1, "beltWidthM": 1.0, "angleOfReposeDeg": 20, "materialDensityKgM3": 1600,
        "emptyBeltMassKg": 600, "frictionCoefficient": 0.03, "pulleyDiameterM": 0.63, "gearRatio": 25,
        "motor": { "ratedPowerW": 2200, "ratedSpeedRadPerS": 155, "ratedCurrentA": 4.7, "coastTimeConstantS": 1.5 },
        "tailDragN": 100 } },
    { "id": "Stockpile", "type": "bulk-sink" }
  ],
  "flows": [
    { "from": "Feed.Out",  "to": "CV001.In" },
    { "from": "CV001.Out", "to": "CH1.In" },
    { "from": "CH1.Out",   "to": "CV002.In" },
    { "from": "CV002.Out", "to": "CH2.In" },
    { "from": "CH2.Out",   "to": "CV003.In" },
    { "from": "CV003.Out", "to": "Stockpile.In" }
  ],
  "controllers": [
    { "id": "PERM_CV001", "type": "permissive", "scanPeriodMs": 100,
      "parameters": { "conditions": [ { "tag": "CV001.SafetyOk", "normal": true } ] } },
    { "id": "PERM_CV002", "type": "permissive", "scanPeriodMs": 100,
      "parameters": { "conditions": [ { "tag": "CV002.SafetyOk", "normal": true } ] } },
    { "id": "PERM_CV003", "type": "permissive", "scanPeriodMs": 100,
      "parameters": { "conditions": [ { "tag": "CV003.SafetyOk", "normal": true } ] } },

    { "id": "INT_CV003", "type": "interlock", "scanPeriodMs": 100,
      "parameters": {
        "conditions": [
          { "tag": "CV003.Tripped",  "normal": false },
          { "tag": "PERM_CV003.Ok",  "normal": true } ],
        "trip": [ { "tag": "CV003.Start", "value": false } ] } },
    { "id": "INT_CV002", "type": "interlock", "scanPeriodMs": 100,
      "parameters": {
        "conditions": [
          { "tag": "CV002.Tripped",  "normal": false },
          { "tag": "PERM_CV002.Ok",  "normal": true },
          { "tag": "CV003.Stopped",  "normal": false } ],
        "trip": [ { "tag": "CV002.Start", "value": false } ] } },
    { "id": "INT_CV001", "type": "interlock", "scanPeriodMs": 100,
      "parameters": {
        "conditions": [
          { "tag": "CV001.Tripped",  "normal": false },
          { "tag": "PERM_CV001.Ok",  "normal": true },
          { "tag": "CV002.Stopped",  "normal": false } ],
        "trip": [ { "tag": "CV001.Start", "value": false } ] } },
    { "id": "INT_FEED", "type": "interlock", "scanPeriodMs": 100,
      "parameters": {
        "conditions": [ { "tag": "CV001.Stopped", "normal": false } ],
        "trip": [ { "tag": "Feed.Enabled", "value": false } ] } },

    { "id": "ALM_CV001", "type": "alarm", "scanPeriodMs": 100,
      "parameters": {
        "input": "CV001.Current",
        "limits": [
          { "kind": "hi",    "value": 7.5, "deadband": 0.3, "onDelayS": 3 },
          { "kind": "hi-hi", "value": 8.6, "deadband": 0.3, "onDelayS": 3 } ] } },
    { "id": "ALM_CV002", "type": "alarm", "scanPeriodMs": 100,
      "parameters": {
        "input": "CV002.Current",
        "limits": [
          { "kind": "hi",    "value": 5.7, "deadband": 0.3, "onDelayS": 3 },
          { "kind": "hi-hi", "value": 6.6, "deadband": 0.3, "onDelayS": 3 } ] } },
    { "id": "ALM_CV003", "type": "alarm", "scanPeriodMs": 100,
      "parameters": {
        "input": "CV003.Current",
        "limits": [
          { "kind": "hi",    "value": 4.3, "deadband": 0.3, "onDelayS": 3 },
          { "kind": "hi-hi", "value": 4.9, "deadband": 0.3, "onDelayS": 3 } ] } },

    { "id": "SEQ_START", "type": "sequencer", "scanPeriodMs": 200,
      "parameters": {
        "steps": [
          { "name": "Reset the safety relays",
            "writes": [
              { "tag": "CV001.SafetyReset", "value": true },
              { "tag": "CV002.SafetyReset", "value": true },
              { "tag": "CV003.SafetyReset", "value": true } ],
            "transition": { "type": "after", "delayS": 1 } },
          { "name": "Start CV003",
            "writes": [
              { "tag": "CV001.SafetyReset", "value": false },
              { "tag": "CV002.SafetyReset", "value": false },
              { "tag": "CV003.SafetyReset", "value": false },
              { "tag": "INT_CV003.Reset",   "value": true },
              { "tag": "CV003.Start",       "value": true } ],
            "transition": { "type": "when", "tag": "CV003.Speed", "op": ">=", "value": 1.74 },
            "timeoutS": 15 },
          { "name": "Start CV002",
            "writes": [
              { "tag": "INT_CV003.Reset", "value": false },
              { "tag": "INT_CV002.Reset", "value": true },
              { "tag": "CV002.Start",     "value": true } ],
            "transition": { "type": "when", "tag": "CV002.Speed", "op": ">=", "value": 1.74 },
            "timeoutS": 15 },
          { "name": "Start CV001",
            "writes": [
              { "tag": "INT_CV002.Reset", "value": false },
              { "tag": "INT_CV001.Reset", "value": true },
              { "tag": "CV001.Start",     "value": true } ],
            "transition": { "type": "when", "tag": "CV001.Speed", "op": ">=", "value": 1.74 },
            "timeoutS": 15 },
          { "name": "Start the feed",
            "writes": [
              { "tag": "INT_CV001.Reset", "value": false },
              { "tag": "INT_FEED.Reset",  "value": true },
              { "tag": "Feed.Enabled",    "value": true } ],
            "transition": { "type": "after", "delayS": 1 } },
          { "name": "Release the feed reset",
            "writes": [ { "tag": "INT_FEED.Reset", "value": false } ],
            "transition": { "type": "after", "delayS": 1 } } ],
        "abort": [
          { "tag": "Feed.Enabled", "value": false },
          { "tag": "CV001.Start",  "value": false },
          { "tag": "CV002.Start",  "value": false },
          { "tag": "CV003.Start",  "value": false } ] } },

    { "id": "SEQ_STOP", "type": "sequencer", "scanPeriodMs": 200,
      "parameters": {
        "steps": [
          { "name": "Stop the feed",
            "writes": [ { "tag": "Feed.Enabled", "value": false } ],
            "transition": { "type": "after", "delayS": 40 } },
          { "name": "Stop CV001",
            "writes": [ { "tag": "CV001.Start", "value": false } ],
            "transition": { "type": "when", "tag": "CV001.Stopped", "op": "==", "value": true },
            "timeoutS": 20 },
          { "name": "Run out CV002",
            "transition": { "type": "after", "delayS": 20 } },
          { "name": "Stop CV002",
            "writes": [ { "tag": "CV002.Start", "value": false } ],
            "transition": { "type": "when", "tag": "CV002.Stopped", "op": "==", "value": true },
            "timeoutS": 20 },
          { "name": "Run out CV003",
            "transition": { "type": "after", "delayS": 15 } },
          { "name": "Stop CV003",
            "writes": [ { "tag": "CV003.Start", "value": false } ],
            "transition": { "type": "when", "tag": "CV003.Stopped", "op": "==", "value": true },
            "timeoutS": 20 } ],
        "abort": [
          { "tag": "Feed.Enabled", "value": false },
          { "tag": "CV001.Start",  "value": false },
          { "tag": "CV002.Start",  "value": false },
          { "tag": "CV003.Start",  "value": false } ] } }
  ]
}
```

- [ ] **Step 6: Run the plant tests to see them pass**

Run: `dotnet test tests/Dse.Samples.Tests --nologo`
Expected: `Passed:    19` (16 + 3).

- [ ] **Step 7: Check the plant through the CLI**

Run: `dotnet run --project src/Dse.Cli -- validate samples/mine-conveyors/plant.json`
Expected, exactly:

```
OK  samples/mine-conveyors/plant.json
  components    7
  leaves        52
  signal links  0
  flow links    6
  tags          120 (0 explicit)
  controllers   12
  time step     10 ms
```

Run: `dotnet run --project src/Dse.Cli -- tags samples/mine-conveyors/plant.json`
Expected: 120 lines. Among them, exactly these (spot checks of tags a controller
or a scenario names):

```
CV001.Stopped  Bool  ReadOnly  Below the threshold for the delay
CV001.TonnesPerHour  Double  ReadOnly  t/h  [0, 813.5067113467554]  Measured value
CV002.PullKey1  Bool  ReadWrite  Actuated by the operator
CV003.EStop  Bool  ReadWrite  Actuated by the operator
Feed.Enabled  Bool  ReadWrite  Feeder enabled
INT_FEED.Reset  Bool  ReadWrite  Clears the latch on a rising edge when every condition is normal
SEQ_START.Start  Bool  ReadWrite  Enters step 1 from idle on a rising edge
SEQ_STOP.Start  Bool  ReadWrite  Enters step 1 from idle on a rising edge
```

- [ ] **Step 8: Build and test the solution**

Run: `dotnet build Dse.sln -c Release --nologo` — expect `0 Warning(s)`, `0 Error(s)`.
Run: `dotnet test Dse.sln --nologo` — expect **1276**.
Run: `git diff --stat 51980e2 -- src/` — expect only `src/Dse.Components/Flow/BulkSource.cs`.

- [ ] **Step 9: Commit**

```bash
git add samples/mine-conveyors/plant.json tests/Dse.Samples.Tests/Cli.cs tests/Dse.Samples.Tests/Sample.cs tests/Dse.Samples.Tests/MineConveyorTests.cs
```

```bash
git commit -m "feat(samples): add the mine-conveyor plant and its controllers" -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>"
```

---

### Task 4: The scenario harness; normal start and stop, pull-key, e-stop

**Model:** opus (three goldens to read against their stories).

**Files:**
- Create: `samples/mine-conveyors/scenarios/normal-start-stop.json`, `pull-key.json`, `e-stop.json`
- Create (generated): `samples/mine-conveyors/expected/normal-start-stop.log`, `pull-key.log`, `e-stop.log`
- Create: `tests/Dse.Samples.Tests/Stories.cs`
- Modify: `tests/Dse.Samples.Tests/Sample.cs` (`Names`)
- Test: `tests/Dse.Samples.Tests/MineConveyorTests.cs` (replaced in full)

**Interfaces:**
- Consumes: Task 2's `EventPattern`, `Absence`, `CausalChain`; Task 3's `Sample`
  (including `Schedule`), `Cli`; `ScenarioRecorder`, `ScenarioJson.Write`,
  `Simulation.AttachActionRecorder`, `RunFor`.
- Produces: `public sealed record Story(IReadOnlyList<EventPattern> Chain,
  IReadOnlyList<Absence> Absences)`; `public static class Stories { static
  IReadOnlyDictionary<string, Story> All }`, keyed by scenario name; the four
  scenario theories and the folder fact, which Tasks 5–6 extend by adding names,
  stories, scenarios and goldens (Task 6 also adds two facts).

**Timing rule** (restated): a scan at tick N sees the image of N−1; its writes
land at phase 1 of N+1. The goldens below were measured with it: e.g. `INT_CV001`
trips at 87.900 s on the switch that asserted at 87.820 s, and its write is
logged at 87.910 s.

- [ ] **Step 1: Write the three scenarios**

`samples/mine-conveyors/scenarios/normal-start-stop.json`:

```json
{
  "plant": "../plant.json",
  "duration": 200,
  "timeline": [
    { "at": 1,  "write": "SEQ_START.Start", "value": true },
    { "at": 2,  "write": "SEQ_START.Start", "value": false },
    { "at": 90, "write": "SEQ_STOP.Start",  "value": true },
    { "at": 91, "write": "SEQ_STOP.Start",  "value": false }
  ]
}
```

`samples/mine-conveyors/scenarios/pull-key.json`:

```json
{
  "plant": "../plant.json",
  "duration": 110,
  "timeline": [
    { "at": 1,  "write": "SEQ_START.Start", "value": true },
    { "at": 2,  "write": "SEQ_START.Start", "value": false },
    { "at": 80, "write": "CV002.PullKey1",  "value": true }
  ]
}
```

`samples/mine-conveyors/scenarios/e-stop.json`:

```json
{
  "plant": "../plant.json",
  "duration": 100,
  "timeline": [
    { "at": 1,  "write": "SEQ_START.Start", "value": true },
    { "at": 2,  "write": "SEQ_START.Start", "value": false },
    { "at": 80, "write": "CV001.EStop",     "value": true }
  ]
}
```

- [ ] **Step 2: Name them in `Sample`**

In `tests/Dse.Samples.Tests/Sample.cs`, replace

```csharp
    public static IReadOnlyList<string> Names { get; } = [];
```

with

```csharp
    public static IReadOnlyList<string> Names { get; } =
    [
        "normal-start-stop",
        "pull-key",
        "e-stop",
    ];
```

- [ ] **Step 3: Write their stories**

`tests/Dse.Samples.Tests/Stories.cs`:

```csharp
namespace Dse.Samples.Tests;

/// <summary>A scenario's causal chain, in order, and what must not happen.</summary>
public sealed record Story(IReadOnlyList<EventPattern> Chain, IReadOnlyList<Absence> Absences);

/// <summary>
/// The eight stories of the design (spec section 5), as the measured event logs
/// tell them. Each chain is an ordered subsequence of the scenario's log; each
/// absence is bounded by a start point that must itself occur.
/// </summary>
public static class Stories
{
    private static readonly EventPattern StartComplete = E("SEQ_START", "SEQUENCE_COMPLETE");

    public static IReadOnlyDictionary<string, Story> All { get; } = new Dictionary<string, Story>(StringComparer.Ordinal)
    {
        ["normal-start-stop"] = new(
            [
                E("SEQ_START", "STEP_ENTERED", "2: Start CV003."),
                E("CV003.Starter", "CONTACTOR_CLOSED"),
                E("SEQ_START", "STEP_ENTERED", "3: Start CV002."),
                E("CV002.Starter", "CONTACTOR_CLOSED"),
                E("SEQ_START", "STEP_ENTERED", "4: Start CV001."),
                E("CV001.Starter", "CONTACTOR_CLOSED"),
                E("SEQ_START", "STEP_ENTERED", "5: Start the feed."),
                E("Feed.Enabled", "WRITE", "Set to true by SEQ_START."),
                StartComplete,
                E("SEQ_STOP", "STEP_ENTERED", "1: Stop the feed."),
                E("Feed.Enabled", "WRITE", "Set to false by SEQ_STOP."),
                E("CV001.Starter", "CONTACTOR_OPENED"),
                E("CV001.ZeroSpeed", "ZERO_SPEED"),
                E("INT_FEED", "INTERLOCK_TRIP", "CV001.Stopped abnormal."),
                E("CV002.Starter", "CONTACTOR_OPENED"),
                E("CV002.ZeroSpeed", "ZERO_SPEED"),
                E("INT_CV001", "INTERLOCK_TRIP", "CV002.Stopped abnormal."),
                E("CV003.Starter", "CONTACTOR_OPENED"),
                E("CV003.ZeroSpeed", "ZERO_SPEED"),
                E("INT_CV002", "INTERLOCK_TRIP", "CV003.Stopped abnormal."),
                E("SEQ_STOP", "SEQUENCE_COMPLETE"),
            ],
            [
                new(StartComplete, E(null, "INTERLOCK_TRIP"), E("SEQ_STOP.Start", "WRITE", "Set to true.")),
                new(null, E(null, "ALARM_RAISED")),
                new(null, E(null, "SEQUENCE_FAULTED")),
            ]),

        ["pull-key"] = new(
            [
                StartComplete,
                E("CV002.PullKey1", "PULLKEY_PULLED"),
                E("CV002.Safety", "SAFETY_TRIP", "Channel1 open"),
                E("CV002.Starter", "CONTACTOR_OPENED"),
                E("PERM_CV002", "PERMISSIVE_LOST", "CV002.SafetyOk dropped."),
                E("INT_CV002", "INTERLOCK_TRIP", "PERM_CV002.Ok abnormal."),
                E("CV002.ZeroSpeed", "ZERO_SPEED"),
                E("INT_CV001", "INTERLOCK_TRIP", "CV002.Stopped abnormal."),
                E("CV001.Start", "WRITE", "Set to false by INT_CV001."),
                E("CV001.ZeroSpeed", "ZERO_SPEED"),
                E("INT_FEED", "INTERLOCK_TRIP", "CV001.Stopped abnormal."),
                E("Feed.Enabled", "WRITE", "Set to false by INT_FEED."),
            ],
            [
                new(E("CV002.PullKey1", "PULLKEY_PULLED"), E("INT_CV003", "INTERLOCK_TRIP")),
                new(E("CV002.PullKey1", "PULLKEY_PULLED"), E("CV003.Starter", "CONTACTOR_OPENED")),
                new(E("CV002.PullKey1", "PULLKEY_PULLED"), E("CV003.ZeroSpeed", "ZERO_SPEED")),
            ]),

        ["e-stop"] = new(
            [
                StartComplete,
                E("CV001.EStop", "ESTOP_PRESSED"),
                E("CV001.Safety", "SAFETY_TRIP", "Channel3 open"),
                E("CV001.Starter", "CONTACTOR_OPENED"),
                E("PERM_CV001", "PERMISSIVE_LOST", "CV001.SafetyOk dropped."),
                E("INT_CV001", "INTERLOCK_TRIP", "PERM_CV001.Ok abnormal."),
                E("CV001.ZeroSpeed", "ZERO_SPEED"),
                E("INT_FEED", "INTERLOCK_TRIP", "CV001.Stopped abnormal."),
                E("Feed.Enabled", "WRITE", "Set to false by INT_FEED."),
            ],
            [
                new(E("CV001.EStop", "ESTOP_PRESSED"), E("INT_CV002", "INTERLOCK_TRIP")),
                new(E("CV001.EStop", "ESTOP_PRESSED"), E("INT_CV003", "INTERLOCK_TRIP")),
                new(E("CV001.EStop", "ESTOP_PRESSED"), E("CV002.Starter", "CONTACTOR_OPENED")),
                new(E("CV001.EStop", "ESTOP_PRESSED"), E("CV003.Starter", "CONTACTOR_OPENED")),
            ]),
    };

    private static EventPattern E(string? source, string code, string fragment = "") => new(source, code, fragment);
}
```

- [ ] **Step 4: Write the scenario tests**

Replace `tests/Dse.Samples.Tests/MineConveyorTests.cs` with:

```csharp
using System.Text.Json;
using Dse.Cli;
using Dse.Configuration;
using Dse.Core;
using Dse.Core.Logging;
using Dse.Scenarios;
using Json.Schema;

namespace Dse.Samples.Tests;

/// <summary>
/// The mine-conveyor sample, end to end: the plant validates and the schema
/// accepts it; every scenario matches its golden through <c>dse run --expect</c>,
/// tells its story, settles before it ends, and replays byte for byte from a
/// recording.
/// </summary>
public class MineConveyorTests
{
    [Fact]
    public void ThePlantValidatesWithTwelveControllers()
    {
        CliRun run = Cli.Run("validate", Sample.Plant);

        Assert.Equal(ExitCodes.Ok, run.ExitCode);
        Assert.Empty(run.Err);
        Assert.StartsWith($"OK  {Sample.Plant}\n", run.Out, StringComparison.Ordinal);
        Assert.Contains("  controllers   12\n", run.Out, StringComparison.Ordinal);
    }

    [Fact]
    public void TheLoaderReportsNothingAtAllNotEvenAWarning()
    {
        LoadResult load = PlantLoader.Load(File.ReadAllText(Sample.Plant), Sample.Catalogue);

        Assert.True(load.IsValid, load.ToText());
        Assert.Empty(load.Diagnostics);
    }

    [Fact]
    public void TheGeneratedSchemaAcceptsThePlantAndRejectsAMistakeInIt()
    {
        JsonSchema schema = JsonSchema.FromText(PlantSchema.Generate(Sample.Catalogue));
        string plant = File.ReadAllText(Sample.Plant);
        string broken = plant.Replace("\"lengthM\": 60", "\"lengthM\": \"60\"", StringComparison.Ordinal);

        Assert.NotEqual(plant, broken);
        Assert.True(schema.Evaluate(JsonDocument.Parse(plant).RootElement).IsValid);
        Assert.False(schema.Evaluate(JsonDocument.Parse(broken).RootElement).IsValid);
    }

    [Fact]
    public void TheScenarioFolderHoldsExactlyTheEightScenariosEachWithAGoldenAndAStory()
    {
        string[] onDisk = Directory.GetFiles(Path.Combine(Sample.SourceRoot, "scenarios"), "*.json")
            .Select(f => Path.GetFileNameWithoutExtension(f))
            .Order(StringComparer.Ordinal)
            .ToArray();
        string[] expected = Sample.Names.Order(StringComparer.Ordinal).ToArray();

        Assert.Equal(expected, onDisk);
        Assert.Equal(expected, Stories.All.Keys.Order(StringComparer.Ordinal).ToArray());
        Assert.All(Sample.Names, name => Assert.True(File.Exists(Sample.SourceGolden(name)), $"'{name}' has no golden."));
    }

    [Theory]
    [MemberData(nameof(Sample.Scenarios), MemberType = typeof(Sample))]
    public void EveryScenarioMatchesItsGolden(string name)
    {
        if (Sample.Updating)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Sample.SourceGolden(name))!);
            CliRun made = Cli.Run("run", Sample.Scenario(name), "--out", Sample.SourceGolden(name));
            Assert.Equal(ExitCodes.Ok, made.ExitCode);
            return;
        }

        string golden = Sample.Golden(name);

        CliRun run = Cli.Run("run", Sample.Scenario(name), "--expect", golden);

        Assert.True(run.ExitCode == ExitCodes.Ok, run.Err);
        Assert.Empty(run.Err);
        Assert.StartsWith($"Matched {golden} (", run.Out, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Sample.Scenarios), MemberType = typeof(Sample))]
    public void EveryScenarioTellsItsStory(string name)
    {
        IReadOnlyList<SimEventRecord> events = Sample.Run(name).Events!.Records;
        Story story = Stories.All[name];

        Assert.Null(CausalChain.FindChain(events, story.Chain));
        Assert.All(story.Absences, absence => Assert.Null(CausalChain.FindAbsence(events, absence)));
    }

    [Theory]
    [MemberData(nameof(Sample.Scenarios), MemberType = typeof(Sample))]
    public void EveryScenarioSettlesAtLeastFiveSecondsBeforeItEnds(string name)
    {
        ScenarioRunResult result = Sample.Run(name);

        long quietTicks = result.Summary!.Ticks - result.Events!.Records[^1].Tick;

        Assert.True(
            quietTicks >= 500,
            $"'{name}' logs its last event {quietTicks} ticks before the end; lengthen its duration so the consequence settles.");
    }

    [Theory]
    [MemberData(nameof(Sample.Scenarios), MemberType = typeof(Sample))]
    public void EveryScenarioReplaysByteForByteFromARecording(string name)
    {
        string path = Sample.Scenario(name);
        Scenario scenario = ScenarioLoader.Parse(File.ReadAllText(path)).Scenario!;
        string plantJson = File.ReadAllText(scenario.ResolvePlantPath(path));
        LoadResult load = PlantLoader.Load(plantJson, Sample.Catalogue, scenario.ToLoadOptions());
        Assert.True(load.IsValid, load.ToText());

        Simulation live = load.Builder!.Build();
        var recorder = new ScenarioRecorder();
        live.AttachActionRecorder(recorder);
        foreach (ScenarioAction action in scenario.Timeline)
        {
            Sample.Schedule(live, action);
        }

        live.RunFor(scenario.Duration);

        Assert.Equal(scenario.Timeline.Count, recorder.Count);
        Scenario recorded = recorder.ToScenario("../plant.json", load.Options!, scenario.Duration);
        ScenarioParseResult reparsed = ScenarioLoader.Parse(ScenarioJson.Write(recorded));
        Assert.Empty(reparsed.Diagnostics);

        ScenarioRunResult replay = ScenarioRunner.Run(reparsed.Scenario!, plantJson, Sample.Catalogue);

        Assert.True(replay.IsValid, replay.ToText());
        Assert.Equal(live.Events.ToText(), replay.Events!.ToText());
    }
}
```

- [ ] **Step 5: Run them to see the goldens missing**

Run: `dotnet test tests/Dse.Samples.Tests --nologo`
Expected: 4 failed, 28 passed (32 total). The failures are the three
`EveryScenarioMatchesItsGolden` rows (exit 3; standard error `Cannot read
'…/expected/<name>.log'`) and
`TheScenarioFolderHoldsExactlyTheEightScenariosEachWithAGoldenAndAStory`
(`'normal-start-stop' has no golden.`). Every story, settle and replay row
already passes — they do not read goldens. If a story row fails, stop: its
message names the first pattern not found and the last matched; report it.

- [ ] **Step 6: Generate the three goldens**

Run: `DSE_UPDATE_GOLDEN=1 dotnet test tests/Dse.Samples.Tests --nologo --filter "FullyQualifiedName~EveryScenarioMatchesItsGolden"`
Expected: `Passed:     3`. `git status --short -uall samples/` lists the three new
`expected/*.log` files and the three scenarios, nothing else.

- [ ] **Step 7: Read each golden in full and check it**

Read all three files with a file-reading tool. Check, and quote in the report:

Common to all three (and to every later golden): **lines 1–63 are identical**
(`cmp <(head -63 samples/mine-conveyors/expected/pull-key.log) <(head -63 samples/mine-conveyors/expected/normal-start-stop.log)`
prints nothing; the same for `e-stop.log`), and they contain, in order:

```
06:00:00.000  INT_CV003  INTERLOCK_TRIP  PERM_CV003.Ok abnormal.
06:00:00.000  INT_CV002  INTERLOCK_TRIP  PERM_CV002.Ok abnormal.
06:00:00.000  INT_CV001  INTERLOCK_TRIP  PERM_CV001.Ok abnormal.
06:00:00.990  CV001.ZeroSpeed  ZERO_SPEED  Speed below 0.02 m/s for 1 s.
06:00:01.000  SEQ_START.Start  WRITE  Set to true.
06:00:01.000  INT_FEED  INTERLOCK_TRIP  CV001.Stopped abnormal.
06:00:01.010  Feed.Enabled  WRITE  Set to false by INT_FEED.
06:00:01.210  CV001.Safety  SAFETY_RESET  All channels healthy; relay energised.
06:00:02.210  CV003.Start  WRITE  Set to true by SEQ_START.
06:00:02.300  INT_CV003  INTERLOCK_RESET  Reset with all conditions normal.
06:00:04.600  SEQ_START  STEP_ENTERED  3: Start CV002.
06:00:07.000  SEQ_START  STEP_ENTERED  4: Start CV001.
06:00:09.400  SEQ_START  STEP_ENTERED  5: Start the feed.
06:00:09.410  Feed.Enabled  WRITE  Set to true by SEQ_START.
06:00:09.990  CV001.Motor  AT_SPEED  Reached 145.98148219625233 rad/s.
06:00:11.400  SEQ_START  SEQUENCE_COMPLETE  Finished after 6 steps.
```

(the last is line 63). No line in any of the three contains `ALARM_` (R111):
`grep -c ALARM_ samples/mine-conveyors/expected/*.log` prints `:0` for each (it
exits 1 — expected).

`normal-start-stop.log` — **94 lines**; lines 64–94 include, in order, and the
last line is the last one here:

```
06:01:30.000  SEQ_STOP.Start  WRITE  Set to true.
06:01:30.210  Feed.Enabled  WRITE  Set to false by SEQ_STOP.
06:02:10.210  CV001.Start  WRITE  Set to false by SEQ_STOP.
06:02:18.040  CV001.ZeroSpeed  ZERO_SPEED  Speed below 0.02 m/s for 1 s.
06:02:18.100  INT_FEED  INTERLOCK_TRIP  CV001.Stopped abnormal.
06:02:18.200  SEQ_STOP  STEP_ENTERED  3: Run out CV002.
06:02:38.410  CV002.Start  WRITE  Set to false by SEQ_STOP.
06:02:46.240  CV002.ZeroSpeed  ZERO_SPEED  Speed below 0.02 m/s for 1 s.
06:02:46.300  INT_CV001  INTERLOCK_TRIP  CV002.Stopped abnormal.
06:03:01.610  CV003.Start  WRITE  Set to false by SEQ_STOP.
06:03:09.440  CV003.ZeroSpeed  ZERO_SPEED  Speed below 0.02 m/s for 1 s.
06:03:09.500  INT_CV002  INTERLOCK_TRIP  CV003.Stopped abnormal.
06:03:09.600  SEQ_STOP  SEQUENCE_COMPLETE  Finished after 6 steps.
```

and exactly seven `INTERLOCK_TRIP` lines in the file: four at power-up, the three
above (R107).

`pull-key.log` — **81 lines**; lines 64–81 are:

```
06:01:20.000  CV002.PullKey1  WRITE  Set to true.
06:01:20.000  CV002.PullKey1  PULLKEY_PULLED  Actuated by the operator.
06:01:20.000  CV002.Safety  SAFETY_TRIP  Channel1 open; relay de-energised.
06:01:20.000  CV002.Starter  CONTACTOR_OPENED  Motor de-energised.
06:01:20.010  CV002.Motor  DE_ENERGISED  Contactor open; coasting.
06:01:20.100  PERM_CV002  PERMISSIVE_LOST  CV002.SafetyOk dropped.
06:01:20.200  INT_CV002  INTERLOCK_TRIP  PERM_CV002.Ok abnormal.
06:01:20.210  CV002.Start  WRITE  Set to false by INT_CV002.
06:01:27.820  CV002.ZeroSpeed  ZERO_SPEED  Speed below 0.02 m/s for 1 s.
06:01:27.900  INT_CV001  INTERLOCK_TRIP  CV002.Stopped abnormal.
06:01:27.900  CV002.Motor  STOPPED  Shaft at rest.
06:01:27.910  CV001.Start  WRITE  Set to false by INT_CV001.
06:01:27.910  CV001.Starter  CONTACTOR_OPENED  Motor de-energised.
06:01:27.920  CV001.Motor  DE_ENERGISED  Contactor open; coasting.
06:01:35.720  CV001.ZeroSpeed  ZERO_SPEED  Speed below 0.02 m/s for 1 s.
06:01:35.800  INT_FEED  INTERLOCK_TRIP  CV001.Stopped abnormal.
06:01:35.810  Feed.Enabled  WRITE  Set to false by INT_FEED.
06:01:35.810  CV001.Motor  STOPPED  Shaft at rest.
```

— no `CV003.` line after line 63: CV003 runs on.

`e-stop.log` — **75 lines**; lines 64–75 are:

```
06:01:20.000  CV001.EStop  WRITE  Set to true.
06:01:20.000  CV001.EStop  ESTOP_PRESSED  Actuated by the operator.
06:01:20.000  CV001.Safety  SAFETY_TRIP  Channel3 open; relay de-energised.
06:01:20.000  CV001.Starter  CONTACTOR_OPENED  Motor de-energised.
06:01:20.010  CV001.Motor  DE_ENERGISED  Contactor open; coasting.
06:01:20.100  PERM_CV001  PERMISSIVE_LOST  CV001.SafetyOk dropped.
06:01:20.200  INT_CV001  INTERLOCK_TRIP  PERM_CV001.Ok abnormal.
06:01:20.210  CV001.Start  WRITE  Set to false by INT_CV001.
06:01:27.810  CV001.ZeroSpeed  ZERO_SPEED  Speed below 0.02 m/s for 1 s.
06:01:27.900  INT_FEED  INTERLOCK_TRIP  CV001.Stopped abnormal.
06:01:27.900  CV001.Motor  STOPPED  Shaft at rest.
06:01:27.910  Feed.Enabled  WRITE  Set to false by INT_FEED.
```

— no `CV002.` or `CV003.` line after line 63.

- [ ] **Step 8: Run the project again**

Run: `dotnet test tests/Dse.Samples.Tests --nologo`
Expected: `Passed:    32` (19 + 13). On success each golden row's `run.Out` is
`Matched <path> (94 events).`, `(81 events)` and `(75 events)` respectively.

- [ ] **Step 9: Build and test the solution**

Run: `dotnet build Dse.sln -c Release --nologo` — expect `0 Warning(s)`, `0 Error(s)`.
Run: `dotnet test Dse.sln --nologo` — expect **1289**.
Run: `git diff --stat 51980e2 -- src/` — expect only `src/Dse.Components/Flow/BulkSource.cs`.

- [ ] **Step 10: Commit**

```bash
git add samples/mine-conveyors/scenarios/normal-start-stop.json samples/mine-conveyors/scenarios/pull-key.json samples/mine-conveyors/scenarios/e-stop.json samples/mine-conveyors/expected/normal-start-stop.log samples/mine-conveyors/expected/pull-key.log samples/mine-conveyors/expected/e-stop.log tests/Dse.Samples.Tests/Stories.cs tests/Dse.Samples.Tests/Sample.cs tests/Dse.Samples.Tests/MineConveyorTests.cs
```

```bash
git commit -m "feat(samples): run, tell and replay the start-stop, pull-key and e-stop scenarios" -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>"
```

---

### Task 5: Overload, chute blockage, failed zero-speed switch

**Model:** opus (three goldens to read against their stories).

**Files:**
- Create: `samples/mine-conveyors/scenarios/overload.json`, `chute-blockage.json`, `failed-zero-speed.json`
- Create (generated): `samples/mine-conveyors/expected/overload.log`, `chute-blockage.log`, `failed-zero-speed.log`
- Modify: `tests/Dse.Samples.Tests/Sample.cs` (`Names`), `tests/Dse.Samples.Tests/Stories.cs` (three entries)

**Interfaces:**
- Consumes: Task 4's `Stories.All`, `Sample.Names` and the theories over them.
- Produces: three more names, stories, scenarios and goldens.

**Timing rule** (restated): a scan at tick N sees the image of N−1; its writes
land at phase 1 of N+1. A fault's `at` is the tick the plant sees it: the
overload trip is logged on the injection tick, 80.000 s.

- [ ] **Step 1: Write the three scenarios**

`samples/mine-conveyors/scenarios/overload.json`:

```json
{
  "plant": "../plant.json",
  "duration": 110,
  "timeline": [
    { "at": 1,  "write": "SEQ_START.Start", "value": true },
    { "at": 2,  "write": "SEQ_START.Start", "value": false },
    { "at": 80, "fault": "CV003.Motor", "id": "thermal-bias", "args": { "amount": 1.0 } }
  ]
}
```

`samples/mine-conveyors/scenarios/chute-blockage.json`:

```json
{
  "plant": "../plant.json",
  "duration": 210,
  "timeline": [
    { "at": 1,  "write": "SEQ_START.Start", "value": true },
    { "at": 2,  "write": "SEQ_START.Start", "value": false },
    { "at": 80, "fault": "CH1", "id": "blockage" }
  ]
}
```

`samples/mine-conveyors/scenarios/failed-zero-speed.json`:

```json
{
  "plant": "../plant.json",
  "duration": 100,
  "timeline": [
    { "at": 1,  "write": "SEQ_START.Start", "value": true },
    { "at": 2,  "write": "SEQ_START.Start", "value": false },
    { "at": 80, "fault": "CV002.ZeroSpeed", "id": "fail-low" }
  ]
}
```

- [ ] **Step 2: Name them in `Sample`**

In `tests/Dse.Samples.Tests/Sample.cs`, replace

```csharp
        "e-stop",
    ];
```

with

```csharp
        "e-stop",
        "overload",
        "chute-blockage",
        "failed-zero-speed",
    ];
```

- [ ] **Step 3: Add their stories**

In `tests/Dse.Samples.Tests/Stories.cs`, the dictionary ends with the only
occurrence of

```csharp
            ]),
    };
```

Replace it with:

```csharp
            ]),

        ["overload"] = new(
            [
                StartComplete,
                E("CV003.Motor", "FAULT", "thermal-bias injected"),
                E("CV003.Starter", "OVERLOAD_TRIP"),
                E("CV003.Starter", "CONTACTOR_OPENED"),
                E("INT_CV003", "INTERLOCK_TRIP", "CV003.Tripped abnormal."),
                E("CV003.ZeroSpeed", "ZERO_SPEED"),
                E("INT_CV002", "INTERLOCK_TRIP", "CV003.Stopped abnormal."),
                E("CV002.Start", "WRITE", "Set to false by INT_CV002."),
                E("CV002.ZeroSpeed", "ZERO_SPEED"),
                E("INT_CV001", "INTERLOCK_TRIP", "CV002.Stopped abnormal."),
                E("CV001.Start", "WRITE", "Set to false by INT_CV001."),
                E("CV001.ZeroSpeed", "ZERO_SPEED"),
                E("INT_FEED", "INTERLOCK_TRIP", "CV001.Stopped abnormal."),
            ],
            [
                new(E("CV003.Motor", "FAULT"), E("ALM_CV003", "ALARM_RAISED")),
            ]),

        ["chute-blockage"] = new(
            [
                StartComplete,
                E("CH1", "FAULT", "blockage injected."),
                E("CH1", "FULL"),
                E("ALM_CV001", "ALARM_RAISED", "above 7.5."),
                E("ALM_CV001", "ALARM_RAISED", "above 8.6."),
                E("CV001.Starter", "OVERLOAD_TRIP"),
                E("INT_CV001", "INTERLOCK_TRIP", "CV001.Tripped abnormal."),
                E("CV001.Start", "WRITE", "Set to false by INT_CV001."),
                E("CV001.ZeroSpeed", "ZERO_SPEED"),
                E("INT_FEED", "INTERLOCK_TRIP", "CV001.Stopped abnormal."),
            ],
            [
                new(E("CH1", "FAULT"), E(null, "INTERLOCK_TRIP"), E("CV001.Starter", "OVERLOAD_TRIP")),
                new(E("CH1", "FAULT"), E("INT_CV002", "INTERLOCK_TRIP")),
                new(E("CH1", "FAULT"), E("INT_CV003", "INTERLOCK_TRIP")),
            ]),

        ["failed-zero-speed"] = new(
            [
                StartComplete,
                E("CV002.ZeroSpeed", "FAULT", "fail-low injected."),
                E("CV002.ZeroSpeed", "ZERO_SPEED"),
                E("INT_CV001", "INTERLOCK_TRIP", "CV002.Stopped abnormal."),
                E("CV001.Start", "WRITE", "Set to false by INT_CV001."),
                E("CV001.ZeroSpeed", "ZERO_SPEED"),
                E("INT_FEED", "INTERLOCK_TRIP", "CV001.Stopped abnormal."),
            ],
            [
                new(E("CV002.ZeroSpeed", "FAULT"), E("CV002.Starter", "CONTACTOR_OPENED")),
                new(E("CV002.ZeroSpeed", "FAULT"), E("CV003.Starter", "CONTACTOR_OPENED")),
                new(E("CV002.ZeroSpeed", "FAULT"), E("INT_CV002", "INTERLOCK_TRIP")),
                new(E("CV002.ZeroSpeed", "FAULT"), E("INT_CV003", "INTERLOCK_TRIP")),
            ]),
    };
```

- [ ] **Step 4: Run to see the goldens missing**

Run: `dotnet test tests/Dse.Samples.Tests --nologo`
Expected: 4 failed, 40 passed (44 total): the three new golden rows (exit 3) and
the folder fact (`'overload' has no golden.`). Every story, settle and replay row
passes.

- [ ] **Step 5: Generate the three goldens**

Run: `DSE_UPDATE_GOLDEN=1 dotnet test tests/Dse.Samples.Tests --nologo --filter "FullyQualifiedName~EveryScenarioMatchesItsGolden"`
Expected: `Passed:     6`. `git status --short -uall samples/` lists the three new
goldens and three new scenarios only — the update rewrote Task 4's goldens with
identical bytes, so they do not appear.

- [ ] **Step 6: Read each golden in full and check it**

For each: lines 1–63 are identical to `normal-start-stop.log`'s (the `cmp`
command of Task 4). Then:

`overload.log` — **85 lines**; lines 64–85 are:

```
06:01:20.000  CV003.Motor  FAULT  thermal-bias injected: amount=1.
06:01:20.000  CV003.Starter  OVERLOAD_TRIP  Thermal state 1.2884784008371077 reached the trip level 1.1.
06:01:20.000  CV003.Starter  CONTACTOR_OPENED  Motor de-energised.
06:01:20.010  CV003.Motor  DE_ENERGISED  Contactor open; coasting.
06:01:20.100  INT_CV003  INTERLOCK_TRIP  CV003.Tripped abnormal.
06:01:20.110  CV003.Start  WRITE  Set to false by INT_CV003.
06:01:27.820  CV003.ZeroSpeed  ZERO_SPEED  Speed below 0.02 m/s for 1 s.
06:01:27.900  INT_CV002  INTERLOCK_TRIP  CV003.Stopped abnormal.
06:01:27.900  CV003.Motor  STOPPED  Shaft at rest.
06:01:27.910  CV002.Start  WRITE  Set to false by INT_CV002.
06:01:27.910  CV002.Starter  CONTACTOR_OPENED  Motor de-energised.
06:01:27.920  CV002.Motor  DE_ENERGISED  Contactor open; coasting.
06:01:35.730  CV002.ZeroSpeed  ZERO_SPEED  Speed below 0.02 m/s for 1 s.
06:01:35.800  INT_CV001  INTERLOCK_TRIP  CV002.Stopped abnormal.
06:01:35.810  CV001.Start  WRITE  Set to false by INT_CV001.
06:01:35.810  CV001.Starter  CONTACTOR_OPENED  Motor de-energised.
06:01:35.810  CV002.Motor  STOPPED  Shaft at rest.
06:01:35.820  CV001.Motor  DE_ENERGISED  Contactor open; coasting.
06:01:43.620  CV001.ZeroSpeed  ZERO_SPEED  Speed below 0.02 m/s for 1 s.
06:01:43.700  INT_FEED  INTERLOCK_TRIP  CV001.Stopped abnormal.
06:01:43.710  Feed.Enabled  WRITE  Set to false by INT_FEED.
06:01:43.710  CV001.Motor  STOPPED  Shaft at rest.
```

The trip is on the injection tick (R109); no `ALARM_` line.

`chute-blockage.log` — **78 lines**; lines 64–78 are:

```
06:01:20.000  CH1  FAULT  blockage injected.
06:01:32.500  CH1  FULL  Chute is full.
06:01:53.400  ALM_CV001  ALARM_RAISED  Hi: 7.705095441377381 above 7.5.
06:02:09.300  ALM_CV001  ALARM_RAISED  HiHi: 8.806547735328072 above 8.6.
06:03:09.700  CV001.Starter  OVERLOAD_TRIP  Thermal state 1.1000144443933113 reached the trip level 1.1.
06:03:09.700  CV001.Starter  CONTACTOR_OPENED  Motor de-energised.
06:03:09.710  CV001.Motor  DE_ENERGISED  Contactor open; coasting.
06:03:09.800  INT_CV001  INTERLOCK_TRIP  CV001.Tripped abnormal.
06:03:09.800  ALM_CV001  ALARM_CLEARED  Hi: 0 back within limits.
06:03:09.800  ALM_CV001  ALARM_CLEARED  HiHi: 0 back within limits.
06:03:09.810  CV001.Start  WRITE  Set to false by INT_CV001.
06:03:17.490  CV001.ZeroSpeed  ZERO_SPEED  Speed below 0.02 m/s for 1 s.
06:03:17.500  INT_FEED  INTERLOCK_TRIP  CV001.Stopped abnormal.
06:03:17.510  Feed.Enabled  WRITE  Set to false by INT_FEED.
06:03:17.570  CV001.Motor  STOPPED  Shaft at rest.
```

The overload relay stops CV001; no `INTERLOCK_TRIP` between the fault and it
(R110).

`failed-zero-speed.log` — **73 lines**; lines 64–73 are:

```
06:01:20.000  CV002.ZeroSpeed  FAULT  fail-low injected.
06:01:20.990  CV002.ZeroSpeed  ZERO_SPEED  Speed below 0.02 m/s for 1 s.
06:01:21.000  INT_CV001  INTERLOCK_TRIP  CV002.Stopped abnormal.
06:01:21.010  CV001.Start  WRITE  Set to false by INT_CV001.
06:01:21.010  CV001.Starter  CONTACTOR_OPENED  Motor de-energised.
06:01:21.020  CV001.Motor  DE_ENERGISED  Contactor open; coasting.
06:01:28.820  CV001.ZeroSpeed  ZERO_SPEED  Speed below 0.02 m/s for 1 s.
06:01:28.900  INT_FEED  INTERLOCK_TRIP  CV001.Stopped abnormal.
06:01:28.910  Feed.Enabled  WRITE  Set to false by INT_FEED.
06:01:28.910  CV001.Motor  STOPPED  Shaft at rest.
```

No `CV002.Starter`, `CV002.Motor`, `CV003.Starter` or `CV003.Motor` line after
line 63: CV002 and CV003 never stop (R103).

- [ ] **Step 7: Run the project again**

Run: `dotnet test tests/Dse.Samples.Tests --nologo`
Expected: `Passed:    44` (32 + 12).

- [ ] **Step 8: Build and test the solution**

Run: `dotnet build Dse.sln -c Release --nologo` — expect `0 Warning(s)`, `0 Error(s)`.
Run: `dotnet test Dse.sln --nologo` — expect **1301**.
Run: `git diff --stat 51980e2 -- src/` — expect only `src/Dse.Components/Flow/BulkSource.cs`.

- [ ] **Step 9: Commit**

```bash
git add samples/mine-conveyors/scenarios/overload.json samples/mine-conveyors/scenarios/chute-blockage.json samples/mine-conveyors/scenarios/failed-zero-speed.json samples/mine-conveyors/expected/overload.log samples/mine-conveyors/expected/chute-blockage.log samples/mine-conveyors/expected/failed-zero-speed.log tests/Dse.Samples.Tests/Sample.cs tests/Dse.Samples.Tests/Stories.cs
```

```bash
git commit -m "feat(samples): add the overload, chute-blockage and failed-switch scenarios" -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>"
```

---

### Task 6: Welded contactor and feed starve, told partly by state

**Model:** opus (both stories rest on rulings R105 and R108, and on sampled state).

**Files:**
- Create: `samples/mine-conveyors/scenarios/welded-contactor.json`, `feed-starve.json`
- Create (generated): `samples/mine-conveyors/expected/welded-contactor.log`, `feed-starve.log`
- Modify: `tests/Dse.Samples.Tests/Sample.cs` (`Names`), `tests/Dse.Samples.Tests/Stories.cs` (two entries)
- Test: `tests/Dse.Samples.Tests/MineConveyorTests.cs` (+3 facts)

**Interfaces:**
- Consumes: Task 5's state; Task 2's `StateChain`, `TagSample`; Task 3's `Sample.Trace`.
- Produces: the last two names, stories, scenarios and goldens — eight in all —
  and the two state facts, plus a fact that `Sample.Trace` refuses a sampling
  interval shorter than one time step.

**Timing rule** (restated): a scan at tick N sees the image of N−1; its writes
land at phase 1 of N+1. `Sample.Trace` samples after a tick completes, so a
sample at 80.0 s shows the plant as of the end of tick 8000.

- [ ] **Step 1: Write the two scenarios**

`samples/mine-conveyors/scenarios/welded-contactor.json`:

```json
{
  "plant": "../plant.json",
  "duration": 240,
  "timeline": [
    { "at": 1,   "write": "SEQ_START.Start", "value": true },
    { "at": 2,   "write": "SEQ_START.Start", "value": false },
    { "at": 80,  "fault": "CV003.Starter", "id": "contactor-welded" },
    { "at": 90,  "write": "SEQ_STOP.Start",  "value": true },
    { "at": 91,  "write": "SEQ_STOP.Start",  "value": false },
    { "at": 210, "write": "CV003.EStop",     "value": true }
  ]
}
```

`samples/mine-conveyors/scenarios/feed-starve.json`:

```json
{
  "plant": "../plant.json",
  "duration": 170,
  "timeline": [
    { "at": 1,  "write": "SEQ_START.Start", "value": true },
    { "at": 2,  "write": "SEQ_START.Start", "value": false },
    { "at": 80, "fault": "Feed", "id": "starve" }
  ]
}
```

- [ ] **Step 2: Name them in `Sample`**

In `tests/Dse.Samples.Tests/Sample.cs`, replace

```csharp
        "failed-zero-speed",
    ];
```

with

```csharp
        "failed-zero-speed",
        "welded-contactor",
        "feed-starve",
    ];
```

- [ ] **Step 3: Add their stories**

In `tests/Dse.Samples.Tests/Stories.cs`, replace the only occurrence of

```csharp
            ]),
    };
```

with:

```csharp
            ]),

        ["welded-contactor"] = new(
            [
                StartComplete,
                E("CV003.Starter", "FAULT", "contactor-welded injected."),
                E("SEQ_STOP", "STEP_ENTERED", "6: Stop CV003."),
                E("CV003.Start", "WRITE", "Set to false by SEQ_STOP."),
                E("SEQ_STOP", "SEQUENCE_FAULTED", "Step 6 timed out after 20 s."),
                E("CV003.EStop", "ESTOP_PRESSED"),
                E("CV003.Safety", "SAFETY_TRIP", "Channel3 open"),
                E("INT_CV003", "INTERLOCK_TRIP", "PERM_CV003.Ok abnormal."),
                E("CV003.Start", "WRITE", "Set to false by INT_CV003."),
            ],
            [
                new(E("CV003.Starter", "FAULT"), E("CV003.Starter", "CONTACTOR_OPENED")),
                new(E("CV003.Starter", "FAULT"), E("CV003.Motor", "DE_ENERGISED")),
                new(E("CV003.Starter", "FAULT"), E("CV003.ZeroSpeed", "ZERO_SPEED")),
                new(E("CV003.Starter", "FAULT"), E("CV003.Motor", "STOPPED")),
            ]),

        ["feed-starve"] = new(
            [
                StartComplete,
                E("Feed", "FAULT", "starve injected."),
            ],
            [
                new(StartComplete, E(null, "INTERLOCK_TRIP")),
                new(StartComplete, E(null, "ALARM_RAISED")),
                new(StartComplete, E(null, "CONTACTOR_OPENED")),
            ]),
    };
```

and in the same file replace the class summary's last sentence,

```csharp
/// absence is bounded by a start point that must itself occur.
/// </summary>
```

with

```csharp
/// absence is bounded by a start point that must itself occur. Feed-starve's
/// fall in transport order is state, not events: see
/// <c>MineConveyorTests.AStarvedFeedEmptiesTheBeltsInTransportOrder</c>.
/// </summary>
```

- [ ] **Step 4: Add the two state facts and the trace guard**

In `tests/Dse.Samples.Tests/MineConveyorTests.cs`, insert immediately before

```csharp
    [Theory]
    [MemberData(nameof(Sample.Scenarios), MemberType = typeof(Sample))]
    public void EveryScenarioReplaysByteForByteFromARecording(string name)
```

these three facts:

```csharp
    [Fact]
    public void AStarvedFeedEmptiesTheBeltsInTransportOrder()
    {
        string[] scales = ["CV001.TonnesPerHour", "CV002.TonnesPerHour", "CV003.TonnesPerHour"];

        IReadOnlyDictionary<string, IReadOnlyList<TagSample>> traces =
            Sample.Trace("feed-starve", scales, TimeSpan.FromMilliseconds(100));

        Assert.Null(StateChain.FindFallInOrder(traces, scales, TimeSpan.FromSeconds(80), floor: 200, ceiling: 5));
    }

    [Fact]
    public void AWeldedContactorKeepsCV003AtSpeedThroughTheStopAndTheEStop()
    {
        IReadOnlyDictionary<string, IReadOnlyList<TagSample>> traces =
            Sample.Trace("welded-contactor", ["CV003.Speed"], TimeSpan.FromMilliseconds(100));

        Assert.All(
            traces["CV003.Speed"].Where(s => s.Time >= TimeSpan.FromSeconds(80)),
            s => Assert.True(s.Value >= 1.7, $"CV003 slowed to {s.Value} m/s at {s.Time}."));
    }

    [Fact]
    public void ATraceCannotSampleFasterThanTheTimeStep()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Sample.Trace("feed-starve", ["CV001.Speed"], TimeSpan.FromMilliseconds(5)));
    }
```

- [ ] **Step 5: Run to see the goldens missing**

Run: `dotnet test tests/Dse.Samples.Tests --nologo`
Expected: 3 failed, 52 passed (55 total): the two new golden rows and the folder
fact. The two state facts and the trace guard already pass — they read no golden. If
`AStarvedFeedEmptiesTheBeltsInTransportOrder` fails, its message names the scale
that was not running, never settled, or settled out of order; report it.

- [ ] **Step 6: Generate the two goldens**

Run: `DSE_UPDATE_GOLDEN=1 dotnet test tests/Dse.Samples.Tests --nologo --filter "FullyQualifiedName~EveryScenarioMatchesItsGolden"`
Expected: `Passed:     8`; `git status --short -uall samples/` lists only the two new
goldens and two new scenarios.

- [ ] **Step 7: Read each golden in full and check it**

Lines 1–63 are identical to `normal-start-stop.log`'s. Then:

`welded-contactor.log` — **99 lines**; lines 64–99 are:

```
06:01:20.000  CV003.Starter  FAULT  contactor-welded injected.
06:01:30.000  SEQ_STOP.Start  WRITE  Set to true.
06:01:30.200  SEQ_STOP  STEP_ENTERED  1: Stop the feed.
06:01:30.210  Feed.Enabled  WRITE  Set to false by SEQ_STOP.
06:01:31.000  SEQ_STOP.Start  WRITE  Set to false.
06:02:10.200  SEQ_STOP  STEP_ENTERED  2: Stop CV001.
06:02:10.210  CV001.Start  WRITE  Set to false by SEQ_STOP.
06:02:10.210  CV001.Starter  CONTACTOR_OPENED  Motor de-energised.
06:02:10.220  CV001.Motor  DE_ENERGISED  Contactor open; coasting.
06:02:18.040  CV001.ZeroSpeed  ZERO_SPEED  Speed below 0.02 m/s for 1 s.
06:02:18.100  INT_FEED  INTERLOCK_TRIP  CV001.Stopped abnormal.
06:02:18.110  Feed.Enabled  WRITE  Set to false by INT_FEED.
06:02:18.120  CV001.Motor  STOPPED  Shaft at rest.
06:02:18.200  SEQ_STOP  STEP_ENTERED  3: Run out CV002.
06:02:38.400  SEQ_STOP  STEP_ENTERED  4: Stop CV002.
06:02:38.410  CV002.Start  WRITE  Set to false by SEQ_STOP.
06:02:38.410  CV002.Starter  CONTACTOR_OPENED  Motor de-energised.
06:02:38.420  CV002.Motor  DE_ENERGISED  Contactor open; coasting.
06:02:46.240  CV002.ZeroSpeed  ZERO_SPEED  Speed below 0.02 m/s for 1 s.
06:02:46.300  INT_CV001  INTERLOCK_TRIP  CV002.Stopped abnormal.
06:02:46.310  CV001.Start  WRITE  Set to false by INT_CV001.
06:02:46.320  CV002.Motor  STOPPED  Shaft at rest.
06:02:46.400  SEQ_STOP  STEP_ENTERED  5: Run out CV003.
06:03:01.600  SEQ_STOP  STEP_ENTERED  6: Stop CV003.
06:03:01.610  CV003.Start  WRITE  Set to false by SEQ_STOP.
06:03:21.800  SEQ_STOP  SEQUENCE_FAULTED  Step 6 timed out after 20 s.
06:03:21.810  Feed.Enabled  WRITE  Set to false by SEQ_STOP.
06:03:21.810  CV001.Start  WRITE  Set to false by SEQ_STOP.
06:03:21.810  CV002.Start  WRITE  Set to false by SEQ_STOP.
06:03:21.810  CV003.Start  WRITE  Set to false by SEQ_STOP.
06:03:30.000  CV003.EStop  WRITE  Set to true.
06:03:30.000  CV003.EStop  ESTOP_PRESSED  Actuated by the operator.
06:03:30.000  CV003.Safety  SAFETY_TRIP  Channel3 open; relay de-energised.
06:03:30.100  PERM_CV003  PERMISSIVE_LOST  CV003.SafetyOk dropped.
06:03:30.200  INT_CV003  INTERLOCK_TRIP  PERM_CV003.Ok abnormal.
06:03:30.210  CV003.Start  WRITE  Set to false by INT_CV003.
```

The last line is the interlock's write at 210.210 s. After the weld there is
**no** `CV003.Starter CONTACTOR_OPENED`, `CV003.Motor DE_ENERGISED` or
`STOPPED`, and no `CV003.ZeroSpeed ZERO_SPEED`: neither the run command, nor the
sequence's abort writes, nor the e-stop opens a welded contactor, and CV003 runs
to the end (R108).

`feed-starve.log` — **64 lines**; line 64 is the only one after start-up:

```
06:01:20.000  Feed  FAULT  starve injected.
```

Nothing trips, nothing stops, no alarm (R105). The fall of the belt scales is
checked by `AStarvedFeedEmptiesTheBeltsInTransportOrder`, not by the log.

- [ ] **Step 8: Run the project again**

Run: `dotnet test tests/Dse.Samples.Tests --nologo`
Expected: `Passed:    55` (44 + 11).

- [ ] **Step 9: Build and test the solution**

Run: `dotnet build Dse.sln -c Release --nologo` — expect `0 Warning(s)`, `0 Error(s)`.
Run: `dotnet test Dse.sln --nologo` — expect **1312**.
Run: `git diff --stat 51980e2 -- src/` — expect only `src/Dse.Components/Flow/BulkSource.cs`.

- [ ] **Step 10: Commit**

```bash
git add samples/mine-conveyors/scenarios/welded-contactor.json samples/mine-conveyors/scenarios/feed-starve.json samples/mine-conveyors/expected/welded-contactor.log samples/mine-conveyors/expected/feed-starve.log tests/Dse.Samples.Tests/Sample.cs tests/Dse.Samples.Tests/Stories.cs tests/Dse.Samples.Tests/MineConveyorTests.cs
```

```bash
git commit -m "feat(samples): add the welded-contactor and feed-starve scenarios, checked by state too" -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>"
```

---

### Task 7: The sample README and its quote test

**Model:** sonnet.

**Files:**
- Create: `samples/mine-conveyors/README.md`
- Test: `tests/Dse.Samples.Tests/SampleReadmeTests.cs`

**Interfaces:**
- Consumes: `Sample.Readme`, `Sample.Names`, `Sample.Scenarios`, `Sample.Golden(name)`.
- Produces: the README convention — each scenario quotes its log in exactly one
  block fenced as `` ```text expected/<name>.log ``, in `Sample.Names` order, and
  names its command `run samples/mine-conveyors/scenarios/<name>.json --expect
  samples/mine-conveyors/expected/<name>.log`.

- [ ] **Step 1: Write the failing tests**

`tests/Dse.Samples.Tests/SampleReadmeTests.cs`:

```csharp
using System.Text.RegularExpressions;

namespace Dse.Samples.Tests;

/// <summary>
/// The sample's README quotes each scenario's log in a block fenced as
/// <c>```text expected/&lt;name&gt;.log</c>. Every quoted line must be a whole
/// line of that golden, so the README cannot drift from what the plant does.
/// </summary>
public partial class SampleReadmeTests
{
    [GeneratedRegex(@"^```text (expected/[a-z-]+\.log)\n(.*?)^```", RegexOptions.Multiline | RegexOptions.Singleline)]
    private static partial Regex QuotedBlock();

    private static string Readme => File.ReadAllText(Sample.Readme).ReplaceLineEndings("\n");

    [Fact]
    public void EveryScenarioHasOneQuotedBlockAndItsCommand()
    {
        string readme = Readme;
        string[] quoted = QuotedBlock().Matches(readme).Select(m => m.Groups[1].Value).ToArray();

        Assert.Equal(Sample.Names.Select(n => $"expected/{n}.log"), quoted);
        Assert.All(Sample.Names, name => Assert.Contains(
            $"run samples/mine-conveyors/scenarios/{name}.json --expect samples/mine-conveyors/expected/{name}.log",
            readme,
            StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(Sample.Scenarios), MemberType = typeof(Sample))]
    public void EveryQuotedLineIsAWholeLineOfItsGolden(string name)
    {
        Match block = Assert.Single(QuotedBlock().Matches(Readme), m => m.Groups[1].Value == $"expected/{name}.log");
        HashSet<string> golden = [.. File.ReadAllText(Sample.Golden(name)).ReplaceLineEndings("\n").Split('\n')];
        string[] lines = block.Groups[2].Value.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.NotEmpty(lines);
        Assert.All(lines, line => Assert.True(golden.Contains(line), $"README quotes a line '{name}' does not log: '{line}'."));
    }
}
```

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/Dse.Samples.Tests --nologo --filter "FullyQualifiedName~SampleReadmeTests"`
Expected: 9 failed, each with `FileNotFoundException` for `mine-conveyors/README.md`.

- [ ] **Step 3: Write the README**

`samples/mine-conveyors/README.md`:

````markdown
# Mine conveyors

A reference sample: three belt conveyors in series carrying ore from a feeder to
a stockpile, with the PLC logic a real line would have — a sequenced start, a
sequenced stop, cascade interlocks, permissives and current alarms — declared in
one plant file, and eight scenarios that break it in eight ways. There is no C#
here. Everything is data the `dse` command line runs.

```
Feed ──▶ CV001 (60 m) ──▶ CH1 ──▶ CV002 (40 m) ──▶ CH2 ──▶ CV003 (30 m) ──▶ Stockpile
```

| file | what it is |
|---|---|
| `plant.json` | the line and its twelve controllers |
| `scenarios/*.json` | the eight scenarios below |
| `expected/*.log` | the golden event log of each scenario |

## The control philosophy

**Start downstream first.** `SEQ_START` resets the three safety relays, then
starts CV003, waits until its speed sensor reads 1.74 m/s (90 % of its running
speed), then CV002, then CV001, then enables the feeder. The feeder is declared
`"enabled": false`: like a PLC output it is off at power-up, and only
`SEQ_START` turns it on. A belt never starts
onto a belt that is not already carrying material away. Each proving step has a
15 s timeout; a timeout aborts the sequence, which drops every run command and
the feeder.

**Stop upstream first.** `SEQ_STOP` disables the feeder, waits 40 s for CV001
to run out, stops CV001 and waits for its zero-speed switch, lets CV002 run out
for 20 s, stops it, lets CV003 run out for 15 s, and stops it. The line stops
empty.

**Cascade interlocks.** Each belt's interlock stops it when the belt downstream
of it stops, and the feeder's interlock stops the feeder when CV001 stops. An
interlock reads the downstream belt's **zero-speed switch** (`CV002.Stopped`),
the instrument, as a real PLC is wired — never the true speed. That is why a
failed switch trips a healthy line (scenario 6). Each interlock also trips on
its own belt's overload relay (`CVn.Tripped`) and on its permissive, and it is
latched: `SEQ_START` resets it only once the belt downstream is proved.

**Safety is hardwired.** The pull-keys and the e-stop of each belt are wired
through the belt's own safety relay to its starter, inside the conveyor. They
stop the motor with no PLC involved. The PLC sees the relay only through
`CVn.SafetyOk`, which `PERM_CVn` watches; the interlock then drops the run
command, so that resetting the relay can never restart the belt by itself.

**Alarms.** `ALM_CVn` watches each motor's current: `Hi` a little above the
loaded running current, `HiHi` a little above the motor's rated current, both
with a 3 s on-delay. The start inrush (about six times rated) is above both for
under 1.9 s, so neither raises on a normal start. There are no standing alarms:
an idle line raises nothing.

## Sizing

Realistic in kind, modest in size: this is a demonstration line, not a sized
design. The belts are horizontal, 1 m wide, and run at 1.95 m/s with no load
(1 480 rpm motors, 25:1 gearboxes, 0.63 m pulleys). The feeder delivers
288 t/h, about 40 % of what a belt can carry. The motors' thermal time
constant is the model's default 60 s, far shorter than a real motor's, so an
overload trips in a minute or two rather than in twenty.

| | CV001 | CV002 | CV003 |
|---|---|---|---|
| length | 60 m | 40 m | 30 m |
| motor | 4 kW, 8.2 A | 3 kW, 6.3 A | 2.2 kW, 4.7 A |
| current, belt empty | 4.2 A | 3.1 A | 2.3 A |
| current, belt at 288 t/h | 6.3 A | 4.5 A | 3.4 A |
| start inrush peak | 48.7 A | 37.4 A | 27.9 A |
| time to 90 % speed | 2.3 s | 2.3 s | 2.3 s |
| `Hi` / `HiHi` | 7.5 / 8.6 A | 5.7 / 6.6 A | 4.3 / 4.9 A |

Every scenario starts from a cold plant, writes `SEQ_START.Start` at 1 s, lets
the line fill (the feeder starts at 9.4 s), and injects its event at 80 s, when
CV003's scale is still rising (268 t/h); it reaches full rate at about 86 s.
Each runs in well under a second.

## Running a scenario

From the repository root:

```bash
dotnet run --project src/Dse.Cli -- validate samples/mine-conveyors/plant.json
dotnet run --project src/Dse.Cli -- tags samples/mine-conveyors/plant.json
dotnet run --project src/Dse.Cli -- run samples/mine-conveyors/scenarios/pull-key.json
```

Add `--expect samples/mine-conveyors/expected/<name>.log` to check a run against
its golden: exit 0 if nothing changed, 4 if the behaviour did.

## 1. Normal start and stop

`SEQ_START` at 1 s; `SEQ_STOP` at 90 s. Each belt is proved at speed before the
next one starts, and on the way down each belt stops only after the one above
it has stopped and it has run out:

```text expected/normal-start-stop.log
06:00:02.210  CV003.Start  WRITE  Set to true by SEQ_START.
06:00:04.600  SEQ_START  STEP_ENTERED  3: Start CV002.
06:00:07.000  SEQ_START  STEP_ENTERED  4: Start CV001.
06:00:09.400  SEQ_START  STEP_ENTERED  5: Start the feed.
06:00:11.400  SEQ_START  SEQUENCE_COMPLETE  Finished after 6 steps.
06:01:30.210  Feed.Enabled  WRITE  Set to false by SEQ_STOP.
06:02:10.210  CV001.Start  WRITE  Set to false by SEQ_STOP.
06:02:38.410  CV002.Start  WRITE  Set to false by SEQ_STOP.
06:03:01.610  CV003.Start  WRITE  Set to false by SEQ_STOP.
06:03:09.600  SEQ_STOP  SEQUENCE_COMPLETE  Finished after 6 steps.
```

The cascade interlocks trip during the stop too — `INT_FEED` when CV001 stops,
`INT_CV001` when CV002 stops, `INT_CV002` when CV003 stops. That is harmless
(each writes a `Start` that is already false) and expected: an interlock does not
know a stop was planned. Between `SEQUENCE_COMPLETE` and `SEQ_STOP` nothing
trips, and no alarm raises in the whole run.

```bash
dotnet run --project src/Dse.Cli -- run samples/mine-conveyors/scenarios/normal-start-stop.json --expect samples/mine-conveyors/expected/normal-start-stop.log
```

## 2. Pull-key

An operator pulls CV002's first pull-wire at 80 s. The relay drops the contactor
at once; the PLC follows; once CV002's switch reports it stopped, CV001 is
stopped by its interlock, and then the feeder. CV003 carries on and runs empty.

```text expected/pull-key.log
06:01:20.000  CV002.PullKey1  PULLKEY_PULLED  Actuated by the operator.
06:01:20.000  CV002.Safety  SAFETY_TRIP  Channel1 open; relay de-energised.
06:01:20.000  CV002.Starter  CONTACTOR_OPENED  Motor de-energised.
06:01:20.100  PERM_CV002  PERMISSIVE_LOST  CV002.SafetyOk dropped.
06:01:20.200  INT_CV002  INTERLOCK_TRIP  PERM_CV002.Ok abnormal.
06:01:27.820  CV002.ZeroSpeed  ZERO_SPEED  Speed below 0.02 m/s for 1 s.
06:01:27.900  INT_CV001  INTERLOCK_TRIP  CV002.Stopped abnormal.
06:01:27.910  CV001.Start  WRITE  Set to false by INT_CV001.
06:01:35.800  INT_FEED  INTERLOCK_TRIP  CV001.Stopped abnormal.
```

```bash
dotnet run --project src/Dse.Cli -- run samples/mine-conveyors/scenarios/pull-key.json --expect samples/mine-conveyors/expected/pull-key.log
```

## 3. Emergency stop

CV001's e-stop is pressed at 80 s. The relay stops CV001; once its switch
reports it stopped, the feeder's interlock stops the feeder. CV002 and CV003 are
downstream and keep running.

```text expected/e-stop.log
06:01:20.000  CV001.EStop  ESTOP_PRESSED  Actuated by the operator.
06:01:20.000  CV001.Safety  SAFETY_TRIP  Channel3 open; relay de-energised.
06:01:20.000  CV001.Starter  CONTACTOR_OPENED  Motor de-energised.
06:01:20.200  INT_CV001  INTERLOCK_TRIP  PERM_CV001.Ok abnormal.
06:01:27.810  CV001.ZeroSpeed  ZERO_SPEED  Speed below 0.02 m/s for 1 s.
06:01:27.900  INT_FEED  INTERLOCK_TRIP  CV001.Stopped abnormal.
06:01:27.910  Feed.Enabled  WRITE  Set to false by INT_FEED.
```

```bash
dotnet run --project src/Dse.Cli -- run samples/mine-conveyors/scenarios/e-stop.json --expect samples/mine-conveyors/expected/e-stop.log
```

## 4. Motor overload

A step of 1.0 in CV003's motor thermal state at 80 s — a blocked fan, a hot
restart — takes it past the overload relay's trip level at once. The whole line
stops behind it, one belt at a time, each on the zero-speed switch of the belt
below it. The current does not rise: an overload trip opens the contactor, and
the current falls to zero.

```text expected/overload.log
06:01:20.000  CV003.Motor  FAULT  thermal-bias injected: amount=1.
06:01:20.000  CV003.Starter  OVERLOAD_TRIP  Thermal state 1.2884784008371077 reached the trip level 1.1.
06:01:20.100  INT_CV003  INTERLOCK_TRIP  CV003.Tripped abnormal.
06:01:27.900  INT_CV002  INTERLOCK_TRIP  CV003.Stopped abnormal.
06:01:35.800  INT_CV001  INTERLOCK_TRIP  CV002.Stopped abnormal.
06:01:43.700  INT_FEED  INTERLOCK_TRIP  CV001.Stopped abnormal.
```

```bash
dotnet run --project src/Dse.Cli -- run samples/mine-conveyors/scenarios/overload.json --expect samples/mine-conveyors/expected/overload.log
```

## 5. Chute blockage

Ore bridges in CH1 at 80 s. The chute fills in 12.5 s; then CV001 cannot
discharge, the ore backs up along it, the belt loads, the current climbs through
`Hi` and `HiHi`, the motor heats, and the overload relay trips — the engine's
causal chain, with no rule anywhere that says "a blocked chute trips the belt
feeding it". Nothing interlocks CV001 before its own overload does. A real
transfer chute carries a blocked-chute probe interlocked to the belt feeding it,
which trips that belt within seconds; this sample deliberately has none, so the
overload chain can play out, and it does not interlock on `CH1.Full` instead —
that tag is the model's truth, not an instrument a PLC could wire.

```text expected/chute-blockage.log
06:01:20.000  CH1  FAULT  blockage injected.
06:01:32.500  CH1  FULL  Chute is full.
06:01:53.400  ALM_CV001  ALARM_RAISED  Hi: 7.705095441377381 above 7.5.
06:02:09.300  ALM_CV001  ALARM_RAISED  HiHi: 8.806547735328072 above 8.6.
06:03:09.700  CV001.Starter  OVERLOAD_TRIP  Thermal state 1.1000144443933113 reached the trip level 1.1.
06:03:09.800  INT_CV001  INTERLOCK_TRIP  CV001.Tripped abnormal.
06:03:17.500  INT_FEED  INTERLOCK_TRIP  CV001.Stopped abnormal.
```

```bash
dotnet run --project src/Dse.Cli -- run samples/mine-conveyors/scenarios/chute-blockage.json --expect samples/mine-conveyors/expected/chute-blockage.log
```

## 6. Failed zero-speed switch

CV002's zero-speed switch fails low at 80 s: it reads stopped while the belt
runs at full speed. The PLC believes the switch — it has nothing else to
believe — and stops CV001 and the feeder. CV002 and CV003 never stop. This is a
nuisance trip, and it is the price of an interlock that reads the instrument.

```text expected/failed-zero-speed.log
06:01:20.000  CV002.ZeroSpeed  FAULT  fail-low injected.
06:01:20.990  CV002.ZeroSpeed  ZERO_SPEED  Speed below 0.02 m/s for 1 s.
06:01:21.000  INT_CV001  INTERLOCK_TRIP  CV002.Stopped abnormal.
06:01:21.010  CV001.Start  WRITE  Set to false by INT_CV001.
06:01:28.900  INT_FEED  INTERLOCK_TRIP  CV001.Stopped abnormal.
```

```bash
dotnet run --project src/Dse.Cli -- run samples/mine-conveyors/scenarios/failed-zero-speed.json --expect samples/mine-conveyors/expected/failed-zero-speed.log
```

## 7. Welded contactor

CV003's contactor welds at 80 s. At 90 s the operator stops the line:
`SEQ_STOP` stops CV001 and CV002, then drops CV003's run command — and CV003
keeps running. The stop sequence times out waiting for CV003's switch and
faults. At 210 s the e-stop is pressed: the safety relay drops, the PLC trips
`INT_CV003` — and CV003 *still* runs, at full speed, to the end of the run. The
relay de-energises the contactor's coil, and welded contacts do not open when
their coil drops. The PLC's command and the hardwired safety circuit both act
through the one contactor, so one weld defeats both.

That is why a real safety circuit of Category 3 / PL d does not stop a motor
through a single contactor: it switches two in series, each able to break the
motor current alone, and monitors them — a normally-closed auxiliary contact of
each is fed back to the safety relay, which will not reset while either reports
closed. A weld is then detected at the next stop and cannot defeat the e-stop.
This sample's starter has one contactor and no feedback; a redundant,
monitored safety-contactor starter is a later component.

```text expected/welded-contactor.log
06:01:20.000  CV003.Starter  FAULT  contactor-welded injected.
06:03:01.610  CV003.Start  WRITE  Set to false by SEQ_STOP.
06:03:21.800  SEQ_STOP  SEQUENCE_FAULTED  Step 6 timed out after 20 s.
06:03:30.000  CV003.EStop  ESTOP_PRESSED  Actuated by the operator.
06:03:30.000  CV003.Safety  SAFETY_TRIP  Channel3 open; relay de-energised.
06:03:30.200  INT_CV003  INTERLOCK_TRIP  PERM_CV003.Ok abnormal.
06:03:30.210  CV003.Start  WRITE  Set to false by INT_CV003.
```

Nothing after that: CV003's contactor never opens, and its zero-speed switch
never reports it stopped.

```bash
dotnet run --project src/Dse.Cli -- run samples/mine-conveyors/scenarios/welded-contactor.json --expect samples/mine-conveyors/expected/welded-contactor.log
```

## 8. Feed starve

The ore supply runs out at 80 s. Nothing trips, nothing stops and no alarm
raises: the belts run on, empty. The log shows only the fault:

```text expected/feed-starve.log
06:01:20.000  Feed  FAULT  starve injected.
```

The story is in the belt scales, not the log. Sampled every 100 ms, each scale
falls to 5 t/h or less, and stays there, in transport order: CV001 at 104.2 s,
CV002 at 133.7 s, CV003 at 153.7 s. The sample's tests check that order from
the scales' values, and check that nothing trips, stops or alarms.

```bash
dotnet run --project src/Dse.Cli -- run samples/mine-conveyors/scenarios/feed-starve.json --expect samples/mine-conveyors/expected/feed-starve.log
```

## Power-up

The first second of every run is the same. The belt interlocks trip at 0.000 s,
because each reads its permissive, whose `Ok` starts false until `SEQ_START`
resets the safety relays. `INT_FEED` trips at 1.000 s, when CV001's zero-speed
switch first reports the belt stopped, and writes `Feed.Enabled` false — which
it already is: the feeder is declared disabled, so no ore moves until
`SEQ_START` enables it at 9.41 s.
````

- [ ] **Step 4: Run the tests to see them pass**

Run: `dotnet test tests/Dse.Samples.Tests --nologo`
Expected: `Passed:    64` (55 + 9).

- [ ] **Step 5: Check the README's numbers and commands**

Every number in the README's sizing table and in its feed-starve section is in
this plan's Measurements or R105 (rounded to one decimal where the README shows
one). Run one quoted command to be sure it works from the repository root:

Run: `dotnet run --project src/Dse.Cli -- run samples/mine-conveyors/scenarios/pull-key.json --expect samples/mine-conveyors/expected/pull-key.log`
Expected: `Matched samples/mine-conveyors/expected/pull-key.log (81 events).` and exit 0.

- [ ] **Step 6: Build and test the solution**

Run: `dotnet build Dse.sln -c Release --nologo` — expect `0 Warning(s)`, `0 Error(s)`.
Run: `dotnet test Dse.sln --nologo` — expect **1321**.

- [ ] **Step 7: Commit**

```bash
git add samples/mine-conveyors/README.md tests/Dse.Samples.Tests/SampleReadmeTests.cs
```

```bash
git commit -m "docs(samples): describe the mine-conveyor line and quote each scenario's log" -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>"
```

---

### Task 8: Documentation and main-spec amendments

**Model:** sonnet.

**Files:**
- Modify: `README.md` (status paragraph; command-line section)
- Modify: `docs/control-blocks.md` (the worked example's closing paragraph)
- Modify: `docs/superpowers/specs/2026-09-02-industrial-process-simulation-engine-design.md` (header, §4, §15.1, §15.2)

**Interfaces:**
- Consumes: the sample as built in Tasks 3–7.
- Produces: no code. `tests/Dse.Control.Tests/DocumentationTests.cs` reads
  `docs/control-blocks.md` and must still find every token it checks (none is
  removed here) and no `\r`. The 6a spec was amended with this plan and is not
  touched here.

- [ ] **Step 1: `README.md` — status**

Replace

```
`controllers` section — `Dse.Control.Catalogue` registers them — or attached in
code; the reference samples are planned.
```

with

```
`controllers` section — `Dse.Control.Catalogue` registers them — or attached in
code.

The first reference sample, `samples/mine-conveyors/`, is three conveyors, a
feeder and a stockpile with a sequenced start and stop, cascade interlocks,
permissives and alarms, and eight scenarios — a normal start and stop, a
pull-key, an e-stop, an overload, a blocked chute, a failed zero-speed switch, a
welded contactor and a starved feed — each with its golden log. It is data only: no C#. The second
sample, a wheel line of discrete items, is planned (plan 6b).
```

- [ ] **Step 2: `README.md` — command line**

Replace

```
dotnet run --project src/Dse.Cli -- run scenario.json --expect golden.log  # replay a scenario; exit 4 if the log changed
```

with

```
dotnet run --project src/Dse.Cli -- run scenario.json --expect golden.log  # replay a scenario; exit 4 if the log changed
dotnet run --project src/Dse.Cli -- run samples/mine-conveyors/scenarios/pull-key.json  # the sample: a pull-key stops the line
```

and replace

```
See [scenarios](docs/scenarios.md), [control blocks](docs/control-blocks.md),
```

with

```
See the [mine-conveyor sample](samples/mine-conveyors/README.md),
[scenarios](docs/scenarios.md), [control blocks](docs/control-blocks.md),
```

- [ ] **Step 3: `docs/control-blocks.md`**

Replace

```
still tripped. Plan 6's mine-conveyor sample interlocks the feed on the belt
instead, so this gap is not carried forward as a pattern to copy.
```

with

```
still tripped. The mine-conveyor sample interlocks the feed on the belt
instead, so this gap is not carried forward as a pattern to copy.

### The full-size example

`samples/mine-conveyors/` is the control layer at plant scale: twelve blocks
over three conveyors — a permissive per belt on its safety relay, cascade
interlocks that read the downstream belt's zero-speed switch, current alarms,
and a start and a stop sequencer — with eight scenarios and their goldens. Its
README explains each design choice; its `plant.json` is the file to copy from.
```

- [ ] **Step 4: Main spec — header and §4**

In `docs/superpowers/specs/2026-09-02-industrial-process-simulation-engine-design.md`,
replace

```
**Date:** 2026-09-02
**Status:** Approved for implementation planning
```

with

```
**Date:** 2026-09-02
**Status:** Approved for implementation planning

**Amended 2026-09-25 by plan 6a** (`docs/superpowers/specs/2026-09-25-mine-conveyor-sample-design.md`):
§4's samples and §15.1 describe the mine-conveyor sample as it was built — a
data folder, not a C# project — and §15.2 names plan 6b.
```

and replace

```
Samples: `samples/Dse.Samples.MineConveyors`, `samples/Dse.Samples.WheelLine`.
```

with

```
Samples: `samples/mine-conveyors/` — a plant file with its controllers,
scenarios, golden logs and a README; data only, run by `dse`, and tested by
`tests/Dse.Samples.Tests` — and the wheel line (plan 6b).
```

- [ ] **Step 5: Main spec — §15.1 and §15.2**

Replace

```
pull-keys, e-stops, safety relay and starter. A demo controller in `Dse.Control`
provides sequenced start, interlocks and permissives.

Demonstrates: plant start, sequenced conveyor start, material flow, speed
change, belt scale measurement, pull-key activation, emergency stop,
upstream/downstream interlocks, motor overload, material accumulation, fault
injection, deterministic replay.
```

with

```
pull-keys, e-stops, safety relay and starter. The plant file's `controllers`
section provides sequenced start and stop, interlocks, permissives and alarms
from the `Dse.Control` blocks; the sample is a data folder,
`samples/mine-conveyors/`, with no C#.

Demonstrates: plant start, sequenced conveyor start, material flow, speed
change, belt scale measurement, pull-key activation, emergency stop,
upstream/downstream interlocks, motor overload, material accumulation, fault
injection, deterministic replay. The drives are direct-on-line, so "speed
change" is the start, stop and trip transients and the speed sag under load; a
variable-speed drive is a later plan.
```

and replace

```
temperature interlock rejects them. None of it is conveyor-specific code.
```

with

```
temperature interlock rejects them. None of it is conveyor-specific code.

The wheel line is plan 6b. Its chain needs component behaviour that does not
exist yet — a graded slow-cycle fault on a process unit, a blocked furnace batch
that keeps soaking, a reject path for discrete items — which 6b designs first.
```

- [ ] **Step 6: Check nothing else moved**

Run: `git diff --stat`
Expected: exactly the three files of this task.
Run: `grep -c $'\r' docs/control-blocks.md README.md` — expect `docs/control-blocks.md:0` and `README.md:0` (exit 1 is expected).
Run: `dotnet test Dse.sln --nologo` — expect **1321**, including
`Dse.Control.Tests`' two `DocumentationTests`.

- [ ] **Step 7: Commit**

```bash
git add README.md docs/control-blocks.md docs/superpowers/specs/2026-09-02-industrial-process-simulation-engine-design.md
```

```bash
git commit -m "docs: point at the mine-conveyor sample and amend the main spec to match it" -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>"
```

---

## Spec coverage

| Spec section | Requirement | Task |
|---|---|---|
| 1, criterion 1 | `dse validate` reports nothing; the schema accepts the plant | 3 (`ThePlantValidatesWithTwelveControllers`, `TheLoaderReportsNothingAtAllNotEvenAWarning`, `TheGeneratedSchemaAcceptsThePlantAndRejectsAMistakeInIt`) |
| 1, criterion 2 | all eight scenarios match through `dse run --expect`, in-process | 4–6 (`EveryScenarioMatchesItsGolden`, R113) |
| 1, criterion 3 | each chain holds as an ordered subsequence; each absence holds | 2 (`CausalChain`, `StateChain`), 4–6 (`EveryScenarioTellsItsStory`, R115); 6 (the two state facts, R105, R108, R121) |
| 1, criterion 4 | record → replay byte for byte, through `ScenarioRecorder` | 4–6 (`EveryScenarioReplaysByteForByteFromARecording`) |
| 1, criterion 5 | `src/` changes only for a real need, with its own test and ruling; no package under `src/`; existing tests and goldens unchanged except as that change requires | 1 (R112: two facts, two regenerated catalogue goldens, measured diff); every later task's `git diff --stat 51980e2 -- src/` |
| 1, criterion 6 | README quotes are verbatim golden lines | 7 (`SampleReadmeTests`) |
| 2 | layout; the test project, its references, `Dse.sln`, copies to output | 2, 3 (R113, R114) |
| 3 | the plant: topology, sizing, measured per-conveyor numbers, defaults | 3; Measurements; R118, R119 |
| 4 | `SEQ_START`, `SEQ_STOP`, interlocks on the instrument, permissives, alarms, the feeder off at power-up | 1, 3 (R103, R104, R106, R111, R112) |
| 5 | the eight scenarios, their chains and absences; fault ids and tags confirmed | 4, 5, 6 (R105, R107–R110, R117); Measurements (names confirmed) |
| 6 | the test list | 2–7 (plus R116's two checks) |
| 7 | sample README; main spec §15.1, project list, §15.2; `README.md`; `docs/control-blocks.md` | 7, 8 (R120) |

## Test-count arithmetic

Measured on `51980e2` with `dotnet test Dse.sln`: **1255**.

| Project | Before | Task deltas | After |
|---|---|---|---|
| Dse.Io.Abstractions | 37 | — | 37 |
| Dse.Core | 461 | — | 461 |
| Dse.Components | 126 | T1 +2 (`ASourceBuiltDisabledCreatesNothingUntilItsEnabledTagIsWritten`, `ASourceIsEnabledAtPowerUpUnlessTheFileSaysOtherwise`) | 128 |
| Dse.Realtime | 56 | — | 56 |
| Dse.Configuration | 209 | — (T1 regenerates its schema golden) | 209 |
| Dse.Scenarios | 163 | — | 163 |
| Dse.Cli | 76 | — | 76 |
| Dse.Control | 105 | — | 105 |
| Dse.Control.Catalogue | 22 | — | 22 |
| Dse.Samples (new) | 0 | T2 +16 (11 `CausalChainTests` + 5 `StateChainTests`), T3 +3, T4 +13 (1 fact + 4 theories × 3), T5 +12 (4 × 3), T6 +11 (4 × 2 + 3 facts), T7 +9 (1 fact + 8 rows) | 64 |
| **Total** | **1255** | +66 | **1321** |

Running totals after each task: 1257, 1273, 1276, 1289, 1301, 1312, 1321, 1321.
Measured: the finished work, built and run in a scratch copy of the repository,
reports 1257 for the existing solution with Task 1 applied and `Passed: 63` for
the new project; an independent check of all eight tasks confirmed 1320. The
trace guard and its fact (Task 6) were added after that check, for 1321.

## Self-review

**Spec coverage.** Every section maps to a task above. Where a ruling departs
from the spec as first approved (R103–R115, R121), the spec has been amended in
place with a note under its header.

**Placeholder scan.** Every code step carries its code; every JSON and Markdown
deliverable is in full; every golden step names the command, the files to read
and a checklist of measured lines; no step says "similar to".

**Type consistency.** `EventPattern(string? Source, string Code, string
MessageFragment = "")`, `Absence(EventPattern? After, EventPattern Forbidden,
EventPattern? Until = null)`, `CausalChain.FindChain/FindAbsence/Line` (T2; used
T4–T6); `TagSample(TimeSpan Time, double Value)`,
`StateChain.SettlesAtOrBelow/FindFallInOrder` (T2; used T3, T6); `CliRun`,
`Cli.Run` (T3; used T3, T4); `Sample.Names/Scenarios/Catalogue/Root/Plant/Readme/
SourceRoot/Updating/Scenario/Golden/SourceGolden/Run/Schedule/Trace` (T3; used
T4–T7); `Story(IReadOnlyList<EventPattern> Chain, IReadOnlyList<Absence>
Absences)`, `Stories.All` (T4; extended T5, T6); `BulkSource(…, bool enabled =
true)` and the `enabled` parameter (T1; used T3).

**Verification.** The whole deliverable — the `BulkSource` change and its tests,
the plant, eight scenarios, eight goldens, the README and the ten test files —
was built and run in a scratch copy of the repository at `51980e2` (warnings as
errors): the existing 1255 tests plus Task 1's two pass, with only the two
catalogue goldens changed as quoted; the new project reports 63 passed; the
`DSE_UPDATE_GOLDEN=1` path regenerated all eight goldens byte-identical to
`dse run`'s output. The intermediate states of Tasks 3 and 4 were run in the
draft and gave the stated counts.

**Review Focus.** Each of the five lines has its test in the owning task (Tasks
1, 2, 4, 6 and 7), named in the section.
