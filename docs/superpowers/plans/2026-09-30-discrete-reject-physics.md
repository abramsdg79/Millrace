# Discrete Reject Physics Implementation Plan (plan 6b.1)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the wheel line's causal chain physically possible: an
`item-process-unit` that keeps heating a batch it cannot discharge
(`heatWhileHeld`) and a graded `slow-cycle{fraction}` fault; a `reject-gate`
measuring station that holds each item for a dwell and then passes it or
diverts it while its writable `Reject` is true; and a `coil` control block
that drives a Bool tag to follow a condition, writing on its first scan and on
each change — then prove the physical half of the chain end to end with the
`SimulationBuilder`.

**Architecture:** `Millrace.Components` only for the physics: `ItemProcessUnit`
gains one constructor argument, one fault and a fault-id switch; `RejectGate` is
a new flow leaf with two discrete outlets, `Out` and `RejectOut`, showing its
item on exactly one of them per tick. `Millrace.Control` gains `Coil`, an
`IScanBlock`; `Millrace.Control.Catalogue` gains `CoilCatalogue` and a
`ControlCatalogue.ConditionOf` helper. No change in `Millrace.Core`,
`Millrace.Io.Abstractions`, `Millrace.Configuration`, `Millrace.Scenarios`, `Millrace.Cli` or
`Millrace.Realtime`: the flow graph already builds one link per connected outlet
(R163), and the plant schema and catalogue export are generated.

**Tech Stack:** .NET 10 (`net10.0`, SDK 10.0.401, runtime 10.0.12), C#, xUnit
2.9.3. No package under `src/`. JsonSchema.Net 8.0.5, test-only and pinned
(unchanged).

**Spec:** `docs/superpowers/specs/2026-09-30-discrete-reject-physics-design.md`
(all of it, as amended by this plan — R170 gives the amendment note the
controller adds to the spec with the plan commit), refining the discrete flow
of plan 2, the component library of plan 3 and the control blocks of
`2026-09-22-control-blocks-design.md` (5c).

**Plan sequence:** This is plan 6b.1. Plans 1–5d, 6a, 6a.1, 6c, 6d and 6e are
merged on `master`; this plan starts from `45a1b52` (the commit that added the
6b.1 spec). Measured on that commit with `dotnet test Millrace.sln`: **1445 tests**,
all passing — 37 `Millrace.Io.Abstractions` / 498 `Millrace.Core` / 137 `Millrace.Components`
/ 57 `Millrace.Realtime` / 230 `Millrace.Configuration` / 167 `Millrace.Scenarios` / 78
`Millrace.Cli` / 140 `Millrace.Control` / 23 `Millrace.Control.Catalogue` / 78 `Millrace.Samples`.
Release build `0 Warning(s)`, `0 Error(s)`.

**Task shape.** Four tasks, sequential, each leaving the whole suite green:

- **Task 1 — `heatWhileHeld` and `slow-cycle`.** `ItemProcessUnit` and its
  descriptor; the components export and the plant schema move by the two
  additions.
- **Task 2 — `reject-gate`.** The new flow leaf, its descriptor, registration,
  conformance fixture and factory test; the two-outlet seam is the review focus
  (Opus reviewer). The components export and the plant schema move by the new
  type; main spec §18 is amended.
- **Task 3 — `coil`.** The block, its catalogue entry, a plant file that aims a
  pyrometer at a gate and claims the gate's `Reject` for a coil, and the
  control-blocks page (Opus reviewer for the scan semantics). The control
  catalogue export and the plant schema move by the new block.
- **Task 4 — the integration test.** Criterion 6, with measured numbers; no
  production code.

A task regenerates, in the same task, every golden its change reaches (R164);
otherwise the golden tests would fail between tasks.

## Global Constraints

- **`src/` changes in exactly seven files.** After Task 4,
  `git diff --stat 45a1b52 -- src/` lists exactly
  `src/Millrace.Components/ComponentsModule.cs`,
  `src/Millrace.Components/Flow/ItemProcessUnit.cs`,
  `src/Millrace.Components/Flow/RejectGate.cs` (new),
  `src/Millrace.Control/Coil.cs` (new),
  `src/Millrace.Control.Catalogue/CoilCatalogue.cs` (new),
  `src/Millrace.Control.Catalogue/ControlCatalogue.cs` and
  `src/Millrace.Control.Catalogue/ControlModule.cs`. Nothing under `src/Millrace.Core`,
  `src/Millrace.Io.Abstractions`, `src/Millrace.Configuration`, `src/Millrace.Scenarios`,
  `src/Millrace.Cli` or `src/Millrace.Realtime`. `git grep -n PackageReference -- 'src/*.csproj'`
  prints nothing.
- **With the defaults, nothing existing changes** (criterion 1). No event-log
  golden moves: measured with this plan's whole change applied, every
  `samples/mine-conveyors/expected/*.log`, every `Millrace.Scenarios.Tests` golden
  (including `item-line-blinded-counter.log`, whose plant has an
  `item-process-unit`) and `conveyor-control.log` are byte-identical. The only
  goldens that move are `tests/Millrace.Components.Tests/Catalogue/Golden/components-catalogue.json`,
  `tests/Millrace.Configuration.Tests/Golden/plant.schema.json` and
  `tests/Millrace.Control.Catalogue.Tests/Golden/control-catalogue.json`, each by
  additions only (0 lines removed; measured sizes in *Measurements*).
- **Goldens are generated and read, never invented or hand-edited.** Regenerate
  only with `MILLRACE_UPDATE_GOLDEN=1` and the `--filter` the task names, then read
  the whole `git diff` of each file that moved, check it against the shape this
  plan quotes, and quote the `--stat` in the task report. After an update run,
  do a normal run (a `bin/` copy can be stale until the next build). A
  deliberately red run of a `Golden.Assert` test leaves a git-ignored
  `*.actual` beside the golden (`tests/Shared/Golden.cs`): delete any that
  `git status --short --ignored -- 'tests/**/*.actual'` lists before committing.
- **Determinism.** No `System.Random`, no `string.GetHashCode()`, no
  `Dictionary`/`HashSet` iteration order reaching a result. Every message is
  built with `CultureInfo.InvariantCulture` (`string.Create`). The gate's dwell
  compares an accumulated `double` with a fixed `1e-9` tolerance (R156).
- **Warnings are errors** (`Directory.Build.props`: `TreatWarningsAsErrors`,
  `EnforceCodeStyleInBuild`, `GenerateDocumentationFile`, `Nullable`). The
  Release build prints `0 Warning(s)` and `0 Error(s)` after every task:
  `dotnet build Millrace.sln -c Release --nologo`.
- **xUnit analyzers run under warnings-as-errors:** prefer `Assert.Single`,
  `Assert.Contains`, `Assert.DoesNotContain`, `Assert.Empty`, `Assert.All`;
  never `Assert.True(x.Any())` or `Assert.Equal(1, x.Count())`. Fault arguments
  in tests are spelled `new FaultArguments(new FaultArgument("fraction", 0.5))`
  — `new FaultArguments(new("fraction", 0.5))` does not compile (CS8752).
- **Test names** are long descriptive PascalCase sentences, like the existing
  ones.
- **Messages are verbatim.** Copy every message, description and log text in
  this plan byte for byte, including the em dash (`—`, U+2014) in descriptions
  and documentation and the degree sign in `°C`. The event messages, the
  `WRITE` lines, the `FAULT` line, the catalogue descriptions (through the
  goldens) and the gate's refused-deposit message are asserted; the gate's two
  other guard messages — the dwell's `ArgumentOutOfRangeException` and
  `WithdrawItem`'s "nothing ready to leave" — are checked only by exception
  type (the dwell) or not at all (unreachable through the flow graph).
- **Tick timing.** Outputs are published in phase 2 (`Evaluate`) from the state
  phase 3 left on the tick before, so an output lags material by one tick. In
  phase 3 the flow graph visits nodes most-downstream first and, for each,
  transfers its outgoing links and then calls its `Advance`; so a node's
  `Advance` runs **before** its upstream deposits into it that tick, and a
  deposit on tick N is first advanced on tick N+1. A write scheduled with
  `WriteAt`/`WriteIn`, and a block's write queued on tick N−1, land in phase 1
  of the tick, before phase 3 reads them.
- **Report every measurement.** Where an expected value in this plan (a test
  count, a tick, a temperature, a message, a golden's shape) disagrees with what
  the code produces, report the measured value in the task report; never adjust
  an assertion or widen a window to fit without saying so.
- **Markdown files** are edited exactly as this plan shows: LF line endings, no
  tabs, a final newline.
- **Git, for every task.** One git command per `Bash` call. `git add` names
  paths explicitly — never `git add -A`, never `git add .`. **Never `git
  stash`.** Commit messages are conventional (`feat(components): …`): a subject
  line, a blank line, a body wrapped at about 78 columns, and the trailer as the
  last line, copied verbatim:

  ```
  Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
  ```

  The trailer identifies the **session** that planned and drives this work, not
  the model that implements a given task: it is the same on every commit,
  whichever implementer model a task names. Never put it on the subject line.
  Write each message with the Write tool to
  `.superpowers/sdd/6b.1/msg-taskN.txt` and commit with `git commit -F`.
- **Commands**, from the repository root: `dotnet build Millrace.sln -c Release --nologo`
  (expect `0 Warning(s)`, `0 Error(s)`) and `dotnet test Millrace.sln --nologo`
  (expect the task's total). `.superpowers/` is git-ignored; scratch work and
  commit messages go under `.superpowers/sdd/6b.1/` and are never added. Inside
  a worktree the harness refuses Bash text that mentions git inside a heredoc,
  `$(…)` or a variable, and any complex command containing "Github": write
  files with the Write tool and run each command as the plain command this plan
  shows.

## Review Focus

The five input classes the spec implies but does not name, most likely to bite
first. Each has its pinning test in the owning task.

1. **The `Reject` value changing while an item waits on a refused outlet.** The
   spec says the choice "is taken again on each later tick"; the author expects
   an item that `Out` refuses to leave by `RejectOut` on the very tick `Reject`
   rises, an item that `RejectOut` refuses to leave by `Out` on the tick it
   falls, and a kicker that sticks while an item waits on a refused reject
   chute to send it out. Tests: Task 2,
   `RejectGateTests.AnItemThatOutRefusesWaitsAndTheChoiceIsTakenAgainOnEveryTick`,
   `AnItemTheRejectOutletRefusesWaitsUntilRejectFalls`,
   `AKickerThatSticksWhileAnItemWaitsOnARefusedRejectSendsItOut`.
2. **A `Reject` write that lands exactly on the release tick, or one tick
   late.** The value is read in phase 3 of the release tick (R157), so a write
   landing in phase 1 of that tick diverts the item and a write one tick later
   diverts the next one; a value that was true when the item arrived and false
   when it leaves does not reject it. Tests: Task 2,
   `AWriteThatLandsOnTheReleaseTickDivertsThatItem` (tick 7),
   `AWriteOneTickLateDivertsTheNextItem` (tick 12),
   `OnlyTheValueOnTheReleaseTickCounts`.
3. **A `slow-cycle` fraction outside [0, 1], a stretch that is not a whole
   number of ticks, and a clear in mid-hold.** The author expects `-1` to run at
   full rate, `1.5` to stop the timer like `1`, `0.75` on a 0.5 s step to
   stretch a 2 s hold to exactly 16 ticks, and a clear to keep the time already
   counted (tick 12 — neither the unslowed 11, the always-slowed 15, nor the
   restarted 13). Tests: Task 1,
   `ItemProcessUnitTests.ASlowCycleStretchesATimedHoldByOneOverOneMinusTheFraction`
   (rows `0.5`, `0.75`, `0`, `-1`), `AtFullSlowCycleATimedHoldNeverCompletes`
   (rows `1`, `1.5`), `ClearingASlowCycleRestoresFullRateAndKeepsTheTimeAlreadyCounted`.
4. **`heatWhileHeld` at the phase boundaries.** The unit changes phase inside
   `Advance`: Idle→Filling on the tick after the first deposit,
   Filling→Processing, Processing→Discharging. The author expects each held
   item to be heated exactly once per tick from the tick after it arrives —
   never twice on a transition tick, never skipped on the Idle tick. Measured:
   after 20 ticks the three items of a jammed batch have had exactly 17, 15 and
   13 steps. Tests: Task 1,
   `WithHeatWhileHeldAJammedBatchHeatsOnEveryTickTheUnitHoldsIt`,
   `WithHeatWhileHeldABatchBlockedDownstreamHeatsExactlyAsAJammedOne`,
   `WithoutHeatWhileHeldAHeldBatchHeatsOnlyWhileProcessing` (4 steps).
5. **A coil whose first scan finds it de-energised, and a condition of bad
   quality.** "Writes on its first scan" must include writing `false` (a PLC
   output is driven, not left floating), and "the same treatment of tag quality
   as the interlock" means the value is used whatever its quality — the
   interlock never reads quality (R160). Tests: Task 3,
   `CoilTests.TheFirstScanWritesTheOutputEvenWhenItIsFalse`,
   `ItReadsTheConditionsValueWhateverItsQualityAsTheInterlockDoes` (rows
   `Uncertain`, `Bad`), and through a plant file
   `RejectStationTests.TheGateThePyrometerOnItAndTheCoilsClaimBind`
   (`Set to false by COIL01.` on tick 1).

Also pinned, beyond the five: a reject outlet left unconnected holds a rejected
item on the station (`AnUnconnectedRejectOutletHoldsARejectedItemOnTheStation`,
R157); a non-finite dwell (`RejectsADwellThatIsNotAPositiveFiniteTime`, rows
`0`, `-1`, `NaN`, `+∞`); a `Reject` input a signal drives is published
read-only (`ARejectInputASignalDrivesIsPublishedReadOnly`); `slow-cycle` leaves a
temperature hold alone even at fraction 1; `slow-cycle` and `discharge-jam`
cleared independently, and a stopped (`fraction` 1) hold resuming on clear
(`ClearingAFullSlowCycleResumesTheStoppedHold`, tick 15); mass conserved over
240 ticks of a toggling kicker; an unconnected `Out` holding a passed item
(`AnUnconnectedOutHoldsAPassedItemOnTheStation`); a deposit on an occupied
station refused with its message (`ADepositOnAnOccupiedStationIsRefused`); a
coil output that is read-only is `MR115` at `$.controllers[1].parameters.output`;
and the dwell that covers the pyrometer → alarm → coil chain, and the one that
does not (`RejectChainTests.AHotItemIsRejectedOnlyWhenTheDwellCoversTheChain`,
R168).

## Decisions settled here (rulings R155–R170)

These refine the spec where the code, or a measured run, forced a choice.
R170 gives the amendment note that records them in the spec.

- **R155 — The reject outlet is `RejectOut`; the input keeps the name
  `Reject`.** Spec criterion 3 names a flow outlet `Reject` and an input
  `Reject`. `CatalogueBuilder.RequireUniquePortNames` refuses a descriptor whose
  signal and flow port names collide ignoring case (a plant file matches port
  names that way): measured, adding such a descriptor to a `CatalogueBuilder`
  throws `ArgumentException` (`RequireUniquePortNames`, called from
  `CatalogueBuilder.Add`). The input keeps `Reject`, because it is the
  tag the 6b.2 coil drives (`GATE.Reject`) and the name the spec's decisions
  table uses; the outlet becomes `RejectOut`, beside `Out`. A plant file wires
  `{ "from": "GATE.RejectOut", "to": "SCRAP.In" }`.
- **R156 — Dwell timing.** The dwell accumulates `dt` in `Advance` while an
  item is held, and the item may leave once the accumulation is at least
  `dwellSeconds − 1e-9` (ten steps of 0.1 s sum to `0.9999999999999999`).
  Because a deposit lands after the gate's own `Advance` (Global Constraints,
  *Tick timing*), the first accumulation is on the tick after arrival, and
  because links transfer before `Advance`, the item leaves on the tick after the
  dwell completes: **deposited on tick N, it leaves on tick
  N + ⌈dwell / dt⌉ + 1 at the earliest** — the same one-tick hand-off a
  `DiscreteBelt` (a 4 m belt at 1 m/s on a 0.5 s step holds an item 9 ticks) and
  an `ItemProcessUnit` have. The departing item frees the station before the
  upstream link runs, so the next item enters on that same tick. Measured with
  a 2 s dwell on a 0.5 s step and one billet a second: item 1 on the station
  ticks 2–6, item 2 ticks 7–11, item 3 from tick 12 — one item per
  `dwell + dt` (2.5 s). This is the rule the spec's "on the first tick the
  dwell is complete it is offered" becomes in the engine's phase order.
- **R157 — Routing, the event, and a missing outlet.** `TryPeekItem(outlet)`
  shows the item only on the outlet `Reject` (as of phase 3 of that tick, so a
  write landing in phase 1 of the release tick counts) chooses, `RejectOut`
  when `Reject` is true and the kicker is not `stuck`, else `Out`; the flow
  graph asks each outlet in port order (`Out`, then `RejectOut`), so an item
  leaves by one outlet or waits, and the next tick asks again. `WithdrawItem`
  has no `TickContext`, so it records the rejected item's id and the gate logs
  `REJECTED` — `Item <id> rejected.` — from its own `Advance`, which runs
  immediately after its links on the same tick: the event's tick is the tick
  the item leaves. `DepositItem` on an occupied station throws
  `InvalidOperationException` (`Reject gate 'G' already holds Billet#1; it takes
  one item at a time.`) — unreachable through the flow graph, which asks
  `CanAcceptItem` first. A `RejectOut` left unconnected is allowed, like any
  unconnected outlet: an item it should take waits on the station, blocking the
  line, until `Reject` falls — measured and pinned. `stuck` switches on the
  fault id like every other fault in this plan.
- **R158 — `heatWhileHeld`: once per tick per held item, in every phase.**
  `Advance` applies the transforms first when `heatWhileHeld` is set and the
  phase is not `Processing` (the `Processing` case applies them itself, as
  today), then runs the phase switch. So an item is heated on every tick from
  the tick after its deposit — on the Idle tick that notices the first deposit,
  through Filling and Processing, and in Discharging whether jammed or blocked —
  and exactly once on a tick that changes phase. Measured (3-billet batch, 2 s
  hold, 10 s time constant toward 1200 °C from 25 °C, 0.5 s step, items landing
  on ticks 2, 4, 6, discharge jammed): after tick 19 the items hold
  `1200 − 1175 × 0.95^k` for k = 17, 15, 13; without `heatWhileHeld` all three
  hold k = 4 (`242.95515625` °C). Heating during filling also advances a
  residence-accumulator's state, so a `state-at-least` hold can be met sooner —
  intended: a continuous furnace soaks what it holds. The hold timer
  (`elapsed`, `Progress`) still counts only in Processing.
- **R159 — `slow-cycle` arithmetic.** While applied, `elapsed += dt × (1 − f)`
  with `f = Math.Clamp(fraction, 0, 1)` (so `-1` is full rate and `1.5` stops the
  timer), copying `DrivePulley`'s `belt-slip`. A timed hold completes on the
  first tick the accumulated timer reaches the hold (`Hold.ForSeconds` compares
  with `1e-12`), so the stretch is quantised to whole ticks: measured on a
  0.5 s step, a 2 s hold whose `PROCESSING` is logged on tick 7 releases on tick
  11 unslowed, 15 at 0.5 (0.25 s a tick), 23 at 0.75 (0.125 s a tick). Clearing
  restores full rate from the next landing tick and keeps what was counted
  (cleared at 5 s: tick 12). The `DISCHARGING` message prints the hold timer —
  `Hold satisfied after 2.00 s; discharging 3 items.` at any fraction — not the
  wall time it took; `Progress` follows the same timer (`0, 0.125, …, 0.875` at
  0.5). `ApplyFault`/`ClearFault` switch on the fault id; an unknown id does
  nothing (the descriptor refuses it earlier, at scheduling). The `FAULT` event
  reads `slow-cycle injected: fraction=0.5.` for the default.
- **R160 — The coil's scan.** `Energised = Input(0).AsBool == normal`, set on
  every scan. The coil writes `output = Energised` when it has not scanned
  before or when `Energised` differs from the value it last wrote, and records
  that value; it raises no events. It reads the condition's value whatever its
  quality, exactly as `Interlock` and `Permissive` do (measured: neither reads
  `TagValue.Quality`; a Bool tag published by a component has no quality
  source, so a non-Good Bool reaches a block only from a unit test). Measured
  over a plant (10 ms step, 100 ms scan, output claimed): the first scan at tick
  0 writes, landing on tick 1 (`Set to true by COIL01.`); a trip written for
  tick 50 is published at the end of tick 50, seen by the scan at tick 60, and
  its write lands on tick 61.
- **R161 — The coil in the catalogue.** `condition` is `Param.Group` over the
  shared `ControlCatalogue.ConditionGroup` (one condition, the shape the
  interlock's list items have), `output` is
  `Param.Tag(…, TagKind.Bool, writes: true)`. A new
  `ControlCatalogue.ConditionOf(ParameterValues)` builds one `Condition`;
  `Conditions` reuses it. The group's `normal` description stays "The value
  that means the condition is satisfied." — for a coil that is the value that
  energises it. Measured binding paths: a read-only output → `$.output`; a
  non-Bool condition tag → `$.condition.tag`; a missing `normal` →
  `$.condition.normal`. In a plant file a read-only output is `MR115` at
  `$.controllers[<n>].parameters.output`. A coil in code whose output is not a
  read-write Bool is `MR014`, as for every block.
- **R162 — Observation and the pyrometer.** `RejectGate.TryObserve` ignores
  position and window and reports the item on the station, or nothing, like a
  process unit. Its descriptor lists `Provides = [typeof(IMaterialObservable)]`
  (conformance requires it, because `pyrometer`, `belt-scale` and
  `part-counter` have `IMaterialObservable` reference parameters), so a plant
  file's pyrometer may name a gate as its `target` — measured through
  `reject-station.json`. The pyrometer's own descriptions ("a belt, a conveyor,
  a chute or a process unit") are not edited: that would move the
  components export and the plant schema by more than additions.
- **R163 — No Core change.** `FlowGraph.Build` makes one `FlowLink` per
  connected `FlowOutlet` in port order, and `TransferItems` asks each link's
  producer for that link's outlet; a `FlowOutlet` still feeds exactly one inlet.
  The conservation audit sums `MassHeld`/`MassDestroyed`, so a gate that holds
  its item and hands it to exactly one sink conserves mass with no new code —
  measured over 240 ticks of a toggling kicker (drift 0).
- **R164 — What moves.** Goldens: three, by additions only, each regenerated in
  the task that reaches it (sizes in *Measurements*). Existing tests changed:
  `ComponentsExportTests.TheShippedCatalogueHasTheExpectedCounts` (29 → 30
  components, Task 2); `ControlCatalogueTests.EveryPublicScanBlockInMillraceControlHasADescriptor`
  (5 → 6), `TheModuleRegistersFiveBlocksAndTwoTransitions` (renamed
  `TheModuleRegistersSixBlocksAndTwoTransitions`, `"coil"` added to its list)
  and `TheControlCatalogueHasTheExpectedCounts` (5 → 6);
  `PlantSchemaTests.HasOneBranchPerBlockType` (5 → 6) (all Task 3). Helpers
  changed: `ItemProcessUnitTests.Build` gains two optional parameters (existing
  calls unchanged); `tests/Millrace.Control.Tests/Scan.cs` gains
  `Set(string, TagValue)`; the two conformance fixture files gain one entry
  each. Measured: with the whole change applied every other existing test
  passes unchanged.
- **R165 — Where the tests live.** Component behaviour in `Millrace.Components.Tests`
  (`ItemProcessUnitTests`, new `RejectGateTests`, new `RejectLineTests`,
  `Catalogue/FlowFactoryTests`); the coil in `Millrace.Control.Tests` (new
  `CoilTests`, pure scans plus one plant over the `Vessel` fake with a claim)
  and `Millrace.Control.Catalogue.Tests` (`BlockFactoryTests`); the plant file in
  `Millrace.Configuration.Tests`, whose `Plants.Catalogue` holds both modules — a new
  valid corpus plant, `Plants/valid/reject-station.json` (which adds a row to
  `CorpusTests.EveryValidPlantLoadsCleanAndBuilds` and one to
  `SchemaAgreementTests`' valid-plant theory), and a new `RejectStationTests`.
- **R166 — The integration test's numbers (criterion 6).** Chosen so the
  unblocked line is steady and the blocked one over-soaks within ten simulated
  minutes, on a 0.5 s step: one 20 kg billet at 25 °C every 45 s; a one-billet
  furnace, `heatWhileHeld`, `thermal-transfer` with a 20 s time constant toward a
  1250 °C zone, discharging at `temperature-at-least` 1100 °C (about 42 s in); a
  2 s reject gate; a 3 m belt at 0.5 m/s with 1 m minimum spacing (it holds at
  most four blanks); a press with a 15 s timed hold forming a `Wheel` at 0.95
  yield; an over-soak limit of 1150 °C. Measured: unblocked, every billet
  reaches the station at `1100.1974549962738` °C, the furnace is in Discharging
  for one tick per billet and the belt never holds two; 11 wheels in 600 s.
  With `slow-cycle` 0.9 on the press (150 s a wheel) from tick 0: the belt holds
  four blanks from 314.5 s, billet 8 waits on the station from 402 s, billet 9
  reaches its target at 446.5 s (tick 893) and is held in Discharging until
  tick 1100, crossing 1150 °C at tick 909 and holding `1239.7668708398605` °C at
  500 s; it reaches the station at 550.5 s at 1249.21 °C. `Reject` written true
  for 560 s and false for 570 s sends it to the reject sink on tick 1120
  (`Item 9 rejected.`); billet 10 then reaches the station at its target and
  waits for the belt. The test asserts the counts and ids exactly, the held
  billet's temperature within 1235–1245 °C (±5 °C around the measured
  1239.77 °C), and the rejected billet's within 1245–1250 °C — a window whose
  top is the 1250 °C zone it approaches from below (measured 1249.21 °C).
- **R167 — Documentation.** Measured by grep over `README.md`, `docs/`
  (excluding `docs/superpowers/`) and `samples/`: components and faults are
  described in `docs/architecture.md` (*Material flow*, *Faults*),
  `docs/authoring-a-component.md` (§7) and the root `README.md` status; blocks
  in `docs/control-blocks.md`, `docs/architecture.md` (*The control layer*)
  and the `README.md`. `docs/scenarios.md` names no fault per type (it defers to
  `millrace catalog export`) and does not change; nor do
  `docs/configuration-diagnostics.md` or any sample README. Only
  `docs/control-blocks.md` is pinned by tests (`DocumentationTests`); its new
  coil section quotes the write messages inline, never as a timestamped line,
  because `EveryLogLineThePageQuotesIsAWholeLineOfTheWorkedExampleGolden`
  asserts exactly three such lines. Main spec §18 is amended in Task 2 (spec
  criterion 5). The dwell-sizing rule (R168) is stated twice: generally in
  `docs/architecture.md`'s *Material flow* (Task 2) and for the pyrometer →
  alarm → coil chain in the coil section (Task 3). `docs/architecture.md`'s
  *Catalogue, schema and loader* says `ControlModule` "registers the five
  shipped ones"; Task 3 makes it six (measured by grep for `five` over `docs/`
  and `README.md`: the only other hits are the two in `docs/control-blocks.md`
  Task 3 changes and "runs five phases", which is about the tick).
- **R168 — Size the dwell to cover the decision.** The spec's measuring
  station works only if the decision lands before the item leaves, and nothing
  enforces that. Measured with a pyrometer on the gate, an alarm (`HiHi`) on
  its value and a coil claiming `GATE.Reject`, one 1000 °C billet every 7.3 s
  (so arrivals fall at every scan phase), 100 ms step: from the deposit tick
  N, the pyrometer reads the item at N+1, the alarm raises on its first scan at
  or after N+2, the coil writes on its first scan after that, and the write
  lands one tick later. With the alarm scanning every *a* ticks and the coil
  every *c*, the worst-case landing is N + a + c + 2, plus `a·⌈onDelay / (a·dt)⌉`
  for an on-delay (the alarm counts it in whole scans, from the scan after the
  first crossing one). The item leaves at N + ⌈dwell / dt⌉ + 1 at the earliest
  (R156), so the decision is certain only when
  **⌈dwell / dt⌉ ≥ a + c + 1 + a·⌈onDelay / (a·dt)⌉**, plus whatever lag the
  instrument's own filter adds. Measured (landing − deposit, worst case over
  eight arrivals; hot billets rejected of 8): a = c = 1 → 4 ticks, dwell 0.3 s
  8, 0.2 s 0; a = 2, c = 1 → 5, dwell 0.4 s 8, 0.3 s 4; a = 3, c = 2 → 7, dwell
  0.6 s 8, 0.5 s 6; a = c = 1 with a 0.3 s on-delay → 7, dwell 0.6 s 8, 0.5 s 0;
  a = 2, c = 1, 0.3 s on-delay → 9, dwell 0.7 s 4, 0.6 s 0; on a 500 ms step
  with 500 ms scans, dwell 1 s passes every hot billet and 1.5 s rejects them
  all. A write that lands after the item left is not lost: `Reject` stays true
  and diverts the next item. The same latency decides when `Reject` falls
  again, so the item after a rejected one is judged by the same chain (an alarm
  clears only below its limit less its deadband). A sum of on-delay scans can
  land a hair short of the target in floating point (as the sequencer's step
  clock does, `docs/control-blocks.md`), so size an on-delay chain with one
  alarm scan to spare. The gate's `dwellSeconds` description is not changed
  (the goldens stay as measured); the rule is documented and pinned by
  `RejectChainTests` (three rows: a = c = 1 at 0.3 s and 0.2 s, a = 2 at 0.4 s).
- **R169 — 6b.2 sizes its dwell by R168 and proves the whole chain.** The
  wheel-line sample's reject station must choose `dwellSeconds` from its step,
  its alarm's and coil's scan periods, the alarm's on-delay and the
  pyrometer's lag by R168's inequality, and its goldens must show the full
  pyrometer → alarm → coil → gate chain rejecting an over-soaked billet and
  passing the good one after it. This plan proves the physical half
  (criterion 6) and the rule; it does not choose 6b.2's numbers.
- **R170 — The spec amendment.** The controller adds this note to the spec,
  under its title, with the plan commit (as 6c, 6d and 6e did; no task edits
  the 6b.1 spec):

  ```markdown
  **Amended 2026-09-30 by the plan**
  (`docs/superpowers/plans/2026-09-30-discrete-reject-physics.md`, rulings
  R155–R170), where the code forced a choice:

  - **The reject outlet is `RejectOut` (R155).** A component's signal and flow
    port names must differ ignoring case, so the flow outlet cannot share the
    input's name `Reject`; the input keeps it, as the tag a coil drives.
  - **Dwell timing (R156):** the dwell is counted from the tick after the item
    arrives and the item leaves on the tick after it completes — deposited on
    tick N, it leaves on tick N + ⌈dwell / dt⌉ + 1 at the earliest, the one-tick
    hand-off every flow node has; the next item may enter on that tick.
  - **`Reject` is read in phase 3 of the release tick (R157)**, so a write that
    lands in phase 1 of that tick counts; `REJECTED` is logged on the tick the
    item leaves. An unconnected `RejectOut` holds a rejected item on the station.
  - **`heatWhileHeld` heats each held item exactly once per tick, from the tick
    after it arrives (R158)**, including the Idle tick that notices the first
    deposit.
  - **`slow-cycle` is quantised to whole ticks (R159)**, a fraction outside
    [0, 1] is clamped, and the hold-satisfied message prints the hold timer, not
    the wall time.
  - **The coil, like the interlock and the permissive, uses the condition's value
    whatever its quality (R160)**: none of them reads quality.
  - **The pyrometer's descriptions are unchanged (R162)**; it may still target a
    reject gate, which provides `IMaterialObservable`.
  - **Size the dwell to cover the decision (R168):** with the alarm scanning
    every *a* ticks and the coil every *c*, a gate honours the decision only
    when ⌈dwell / dt⌉ ≥ a + c + 1 + a·⌈onDelay / (a·dt)⌉, plus the instrument's
    lag — 3 × dt when both scan every tick. A shorter dwell lets the item leave
    before the write lands.
  - **6b.2 sizes its dwell by that rule and proves the full pyrometer → alarm →
    coil → gate chain (R169).**
  ```

## Measurements

Scratch runs on `45a1b52` plus this plan's whole change, in a throwaway `git
worktree` (removed after), and on each task's intermediate state.

**Suite:** 1504 tests, all passing; Release build `0 Warning(s)`, `0 Error(s)`.
Per project: 37 Io.Abstractions / 498 Core / 176 Components / 57 Realtime /
237 Configuration / 167 Scenarios / 78 Cli / 149 Control / 27 Control.Catalogue
/ 78 Samples.

**Goldens** (`git diff --stat`, all additions):

| golden | Task 1 | Task 2 | Task 3 | total |
|---|---|---|---|---|
| `components-catalogue.json` | +19 (`heatWhileHeld` parameter, `slow-cycle` fault) | +105 (`reject-gate`, between `pyrometer` and `safety-relay`) | — | +124 |
| `plant.schema.json` | +5 (`heatWhileHeld` property) | +37 (a `oneOf` ref and `component.reject-gate`) | +55 (a `oneOf` ref and `block.coil`) | +97 |
| `control-catalogue.json` | — | — | +37 (`coil`, between `alarm` and `interlock`) | +37 |

**`item-process-unit` timelines** (3-billet batch from one billet a second, 2 s
timed hold, 0.5 s step; `Phase`/`Progress` are the published outputs, a tick
behind): items land on ticks 2, 4, 6; `FILLING` on tick 3, `PROCESSING` on
tick 7; unslowed, `DISCHARGING` on tick 11 and all three leave on tick 12
(`IDLE`); `Progress` in Processing reads `0, 0.25, 0.5, 0.75`. With
`heatWhileHeld` and a 10 s time constant toward 1200 °C, item 1 reads
`83.75` °C at the end of tick 3 and `459.456943573584` °C at tick 11
(k = 9); jammed, `708.7086061174666` °C at tick 19 (k = 17). At `slow-cycle`
0.5, `DISCHARGING` on tick 15 and `Progress` reads `0, 0.125, …, 0.875`.

**`reject-gate` timeline** (2 s dwell, one billet a second, 0.5 s step): the
station holds item 1 on ticks 2–6, item 2 on 7–11, item 3 from 12; `Occupied`
reads true from tick 3; with `Reject` written true for 3.0 s (tick 6),
`REJECTED` `Item 1 rejected.` on tick 7, `Item 2 rejected.` on tick 12.

**Coil over a plant**: `V1.Fill` `WRITE` on ticks 1 (`Set to true by
COIL01.`), 61 (`Set to false by COIL01.`), 91 (`Set to true by COIL01.`) —
trip written for 500 ms, cleared for 800 ms; `V1.Fill` published `ReadOnly`,
`ClaimedBy` `COIL01`; a `WriteIn` to it throws `InvalidOperationException`.

**Integration** (R166): as stated there.

## File structure

```
src/Millrace.Components/Flow/ItemProcessUnit.cs                         heatWhileHeld; slow-cycle; fault-id switch (Task 1)
tests/Millrace.Components.Tests/ItemProcessUnitTests.cs                 Build helper; + 9 facts, + theory of 4, + theory of 2 (Task 1)
tests/Millrace.Components.Tests/Catalogue/FlowFactoryTests.cs           + 1 fact (Task 1), + 1 fact (Task 2)
tests/Millrace.Components.Tests/Catalogue/Golden/components-catalogue.json   regenerated (Tasks 1, 2)
tests/Millrace.Configuration.Tests/Golden/plant.schema.json             regenerated (Tasks 1, 2, 3)
docs/architecture.md                                               Material flow + Faults (Tasks 1, 2), control layer (Task 3)
docs/authoring-a-component.md                                      §7 fault-id switch (Task 1)
src/Millrace.Components/Flow/RejectGate.cs                              new (Task 2)
src/Millrace.Components/ComponentsModule.cs                             registers RejectGate (Task 2)
tests/Millrace.Components.Tests/RejectGateTests.cs                      new: 16 facts, theory of 4 (Task 2)
tests/Millrace.Components.Tests/Catalogue/ComponentsFixtures.cs         reject-gate fixture (Task 2)
tests/Millrace.Components.Tests/Catalogue/ComponentsExportTests.cs      29 → 30 (Task 2)
README.md                                                          status (Tasks 2, 3, 4)
docs/superpowers/specs/2026-09-02-industrial-process-simulation-engine-design.md   §18 (Task 2)
src/Millrace.Control/Coil.cs                                            new (Task 3)
src/Millrace.Control.Catalogue/CoilCatalogue.cs                         new (Task 3)
src/Millrace.Control.Catalogue/ControlCatalogue.cs                      ConditionOf (Task 3)
src/Millrace.Control.Catalogue/ControlModule.cs                         registers the coil (Task 3)
tests/Millrace.Control.Tests/Scan.cs                                    Set(string, TagValue) (Task 3)
tests/Millrace.Control.Tests/CoilTests.cs                               new: 6 facts, theory of 2 (Task 3)
tests/Millrace.Control.Tests/DocumentationTests.cs                      + 1 fact (Task 3)
tests/Millrace.Control.Catalogue.Tests/BlockFactoryTests.cs             + 1 fact, + theory of 3 (Task 3)
tests/Millrace.Control.Catalogue.Tests/ControlFixtures.cs               coil fixture (Task 3)
tests/Millrace.Control.Catalogue.Tests/ControlCatalogueTests.cs         3 tests changed, 1 renamed (Task 3)
tests/Millrace.Control.Catalogue.Tests/Golden/control-catalogue.json    regenerated (Task 3)
tests/Millrace.Configuration.Tests/PlantSchemaTests.cs                  5 → 6 (Task 3)
tests/Millrace.Configuration.Tests/Plants/valid/reject-station.json     new corpus plant (Task 3)
tests/Millrace.Configuration.Tests/RejectStationTests.cs                new: 2 facts (Task 3)
tests/Millrace.Configuration.Tests/RejectChainTests.cs                  new: theory of 3, the dwell covering the PLC chain (Task 3)
docs/control-blocks.md                                             six blocks; the Coil section (Task 3)
tests/Millrace.Components.Tests/RejectLineTests.cs                      new: 2 facts (Task 4)
```

## Task map

| # | Task | Implementer | Reviewer | Tests after |
|---|---|---|---|---|
| 1 | `item-process-unit`: `heatWhileHeld`, `slow-cycle`; two goldens; docs | sonnet | sonnet | 1461 |
| 2 | `reject-gate`: the leaf, descriptor, registration, conformance; two goldens; docs, §18 | sonnet | **opus** (two-outlet seam, dwell timing) | 1482 |
| 3 | `coil`: the block, catalogue, plant file, claim; two goldens; control-blocks page | sonnet | **opus** (scan semantics, first scan, quality) | 1502 |
| 4 | Criterion 6 integration test; README | sonnet | sonnet | 1504 |

Every task's brief contains its complete code; Sonnet implements throughout
because every task edits existing files and the commit trailer must be right
first time. The whole-branch review at the end is Opus. Tasks are sequential:
Task 3's plant file needs Task 2's gate, Task 4 needs everything.

---

### Task 1: `item-process-unit` heats while held and runs slow on `slow-cycle`

**Model:** implementer sonnet; reviewer sonnet.

**Files:**
- Modify: `src/Millrace.Components/Flow/ItemProcessUnit.cs` (summary; a fault constant and descriptor; the factory; a parameter; a constructor argument and property; `Advance`; `ApplyFault`/`ClearFault`)
- Test: `tests/Millrace.Components.Tests/ItemProcessUnitTests.cs` (helper changed; + 9 facts, + theory of 4, + theory of 2 = 15)
- Test: `tests/Millrace.Components.Tests/Catalogue/FlowFactoryTests.cs` (+ 1 fact)
- Regenerate: `tests/Millrace.Components.Tests/Catalogue/Golden/components-catalogue.json`, `tests/Millrace.Configuration.Tests/Golden/plant.schema.json`
- Modify: `docs/architecture.md` (*Material flow*, *Faults*), `docs/authoring-a-component.md` (§7)

**Interfaces:**
- Consumes: `FaultDescriptor`, `FaultParameter`, `FaultArguments.Get` (unchanged);
  `Param.Bool(name, description, @default)`; `ParameterValues.Bool(name)` (returns
  the default when the file omits it); `Hold.ForSeconds`, `Hold.TemperatureAtLeast`;
  `ThermalTransfer(timeConstantSeconds)`; the test fakes `Setpoint`.
- Produces: `public const string ItemProcessUnit.SlowCycle = "slow-cycle"`;
  `public bool ItemProcessUnit.HeatWhileHeld { get; }`; the constructor
  `ItemProcessUnit(string id, int batchSize, IHoldCondition hold, MaterialType? output = null, double yield = 1.0, IReadOnlyList<IMaterialTransform>? transforms = null, bool heatWhileHeld = false)`;
  catalogue parameter `heatWhileHeld` (bool, default false); fault
  `slow-cycle(fraction)` (default 0.5).

- [ ] **Step 1: Write the failing unit tests**

In `tests/Millrace.Components.Tests/ItemProcessUnitTests.cs`, replace

```csharp
using Millrace.Core;
using Millrace.Core.Flow;
```

with

```csharp
using Millrace.Core;
using Millrace.Core.Faults;
using Millrace.Core.Flow;
```

Replace

```csharp
    private static Plant Build(int batchSize, IHoldCondition hold, MaterialType? output = null, double yield = 1.0, IReadOnlyList<IMaterialTransform>? transforms = null, double interval = 1.0)
    {
        var source = new ItemSource("Billets", Billet, 20.0, interval, new MaterialProperties(7800.0, 0.0, 25.0));
        var unit = new ItemProcessUnit("Furnace", batchSize, hold, output, yield, transforms);
        var sink = new ItemSink("Out");
```

with

```csharp
    private static Plant Build(
        int batchSize,
        IHoldCondition hold,
        MaterialType? output = null,
        double yield = 1.0,
        IReadOnlyList<IMaterialTransform>? transforms = null,
        double interval = 1.0,
        bool heatWhileHeld = false,
        int sinkCapacity = int.MaxValue)
    {
        var source = new ItemSource("Billets", Billet, 20.0, interval, new MaterialProperties(7800.0, 0.0, 25.0));
        var unit = new ItemProcessUnit("Furnace", batchSize, hold, output, yield, transforms, heatWhileHeld);
        var sink = new ItemSink("Out", sinkCapacity);
```

Replace

```csharp
    private static IEnumerable<string> Phases(Simulation sim) =>
        sim.Events.Records.Where(r => r.Source == "Furnace").Select(r => r.Code);
```

with

```csharp
    private static IEnumerable<string> Phases(Simulation sim) =>
        sim.Events.Records.Where(r => r.Source == "Furnace").Select(r => r.Code);

    /// <summary>A billet that entered at 25 °C after <paramref name="ticks"/> half-second steps toward a 1200 °C zone with a 10 s time constant.</summary>
    private static double Heated(int ticks) => 1200.0 - (1175.0 * Math.Pow(0.95, ticks));

    /// <summary>The tick the first DISCHARGING was logged on.</summary>
    private static long DischargeTick(Simulation sim) =>
        sim.Events.Records.First(r => r.Source == "Furnace" && r.Code == "DISCHARGING").Tick;
```

Replace the end of the class

```csharp
            Assert.Single(plant.Sim.Events.Records, r => r.Source == "Furnace" && r.Code == "DISCHARGING").Message);
    }
}
```

with

```csharp
            Assert.Single(plant.Sim.Events.Records, r => r.Source == "Furnace" && r.Code == "DISCHARGING").Message);
    }

    [Fact]
    public void WithHeatWhileHeldAJammedBatchHeatsOnEveryTickTheUnitHoldsIt()
    {
        Plant plant = Build(batchSize: 3, Hold.ForSeconds(2.0), transforms: [new ThermalTransfer(10.0)], heatWhileHeld: true);
        plant.Sim.InjectFaultAt(TimeSpan.Zero, "Furnace", ItemProcessUnit.DischargeJam);

        // Items land on ticks 2, 4, 6 and are heated from the next tick on, in
        // Idle, Filling, Processing (ticks 8-11) and Discharging alike: after
        // tick 19 they have had 17, 15 and 13 steps.
        plant.Sim.RunFor(TimeSpan.FromSeconds(10));

        Assert.Equal(ProcessPhase.Discharging, plant.Unit.CurrentPhase);
        Assert.Equal(new long[] { 1, 2, 3 }, plant.Unit.Items.Select(i => i.Id));
        Assert.Equal(Heated(17), plant.Unit.Items[0].Properties.Temperature, 9);
        Assert.Equal(Heated(15), plant.Unit.Items[1].Properties.Temperature, 9);
        Assert.Equal(Heated(13), plant.Unit.Items[2].Properties.Temperature, 9);
    }

    [Fact]
    public void WithHeatWhileHeldABatchBlockedDownstreamHeatsExactlyAsAJammedOne()
    {
        Plant plant = Build(batchSize: 3, Hold.ForSeconds(2.0), transforms: [new ThermalTransfer(10.0)], heatWhileHeld: true, sinkCapacity: 0);

        plant.Sim.RunFor(TimeSpan.FromSeconds(10));

        Assert.Equal(ProcessPhase.Discharging, plant.Unit.CurrentPhase);
        Assert.Equal(0L, plant.Sink.Count.Value);
        Assert.Equal(Heated(17), plant.Unit.Items[0].Properties.Temperature, 9);
        Assert.Equal(Heated(15), plant.Unit.Items[1].Properties.Temperature, 9);
        Assert.Equal(Heated(13), plant.Unit.Items[2].Properties.Temperature, 9);
    }

    [Fact]
    public void WithoutHeatWhileHeldAHeldBatchHeatsOnlyWhileProcessing()
    {
        Plant plant = Build(batchSize: 3, Hold.ForSeconds(2.0), transforms: [new ThermalTransfer(10.0)], sinkCapacity: 0);

        plant.Sim.RunFor(TimeSpan.FromSeconds(10));

        Assert.Equal(ProcessPhase.Discharging, plant.Unit.CurrentPhase);
        Assert.All(plant.Unit.Items, i => Assert.Equal(Heated(4), i.Properties.Temperature, 9));
    }

    [Theory]
    [InlineData(0.5, 15L)]
    [InlineData(0.75, 23L)]
    [InlineData(0.0, 11L)]
    [InlineData(-1.0, 11L)]
    public void ASlowCycleStretchesATimedHoldByOneOverOneMinusTheFraction(double fraction, long dischargeTick)
    {
        Plant plant = Build(batchSize: 3, Hold.ForSeconds(2.0));
        plant.Sim.InjectFaultAt(TimeSpan.Zero, "Furnace", ItemProcessUnit.SlowCycle, new FaultArguments(new FaultArgument("fraction", fraction)));

        // PROCESSING is logged on tick 7; unslowed, the 2 s hold is four ticks.
        plant.Sim.RunFor(TimeSpan.FromSeconds(15));

        Assert.Equal(dischargeTick, DischargeTick(plant.Sim));
        Assert.Equal(
            "Hold satisfied after 2.00 s; discharging 3 items.",
            plant.Sim.Events.Records.First(r => r.Source == "Furnace" && r.Code == "DISCHARGING").Message);
    }

    [Fact]
    public void ASlowCycleDefaultsToHalfRate()
    {
        Plant plant = Build(batchSize: 3, Hold.ForSeconds(2.0));
        plant.Sim.InjectFaultAt(TimeSpan.Zero, "Furnace", ItemProcessUnit.SlowCycle);

        plant.Sim.RunFor(TimeSpan.FromSeconds(10));

        Assert.Equal(15L, DischargeTick(plant.Sim));
        Assert.Contains(plant.Sim.Events.Records, r => r.Source == "Furnace" && r.Message == "slow-cycle injected: fraction=0.5.");
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.5)]
    public void AtFullSlowCycleATimedHoldNeverCompletes(double fraction)
    {
        Plant plant = Build(batchSize: 3, Hold.ForSeconds(2.0));
        plant.Sim.InjectFaultAt(TimeSpan.Zero, "Furnace", ItemProcessUnit.SlowCycle, new FaultArguments(new FaultArgument("fraction", fraction)));

        plant.Sim.RunFor(TimeSpan.FromSeconds(60));

        Assert.Equal(ProcessPhase.Processing, plant.Unit.CurrentPhase);
        Assert.Equal(0.0, plant.Unit.Progress.Value);
        Assert.DoesNotContain(plant.Sim.Events.Records, r => r.Source == "Furnace" && r.Code == "DISCHARGING");
    }

    [Fact]
    public void ASlowCycleLeavesATemperatureHoldAlone()
    {
        IMaterialTransform[] heat = [new ThermalTransfer(10.0)];
        Plant plain = Build(batchSize: 3, Hold.TemperatureAtLeast(200.0), transforms: heat);
        Plant slowed = Build(batchSize: 3, Hold.TemperatureAtLeast(200.0), transforms: heat);
        slowed.Sim.InjectFaultAt(TimeSpan.Zero, "Furnace", ItemProcessUnit.SlowCycle, new FaultArguments(new FaultArgument("fraction", 1.0)));

        plain.Sim.RunFor(TimeSpan.FromSeconds(10));
        slowed.Sim.RunFor(TimeSpan.FromSeconds(10));

        // 192.6 °C after three processing steps, 243.0 °C after four: released on tick 11 either way.
        Assert.Equal(11L, DischargeTick(plain.Sim));
        Assert.Equal(11L, DischargeTick(slowed.Sim));
    }

    [Fact]
    public void ClearingAFullSlowCycleResumesTheStoppedHold()
    {
        Plant plant = Build(batchSize: 3, Hold.ForSeconds(2.0));
        plant.Sim.InjectFaultAt(TimeSpan.Zero, "Furnace", ItemProcessUnit.SlowCycle, new FaultArguments(new FaultArgument("fraction", 1.0)));
        plant.Sim.ClearFaultAt(TimeSpan.FromSeconds(6), "Furnace", ItemProcessUnit.SlowCycle);   // tick 12

        // Ticks 8-11 count nothing; ticks 12-15 count the whole 2 s.
        plant.Sim.RunFor(TimeSpan.FromSeconds(10));

        Assert.Equal(15L, DischargeTick(plant.Sim));
    }

    [Fact]
    public void ClearingASlowCycleRestoresFullRateAndKeepsTheTimeAlreadyCounted()
    {
        Plant plant = Build(batchSize: 3, Hold.ForSeconds(2.0));
        plant.Sim.InjectFaultAt(TimeSpan.Zero, "Furnace", ItemProcessUnit.SlowCycle);
        plant.Sim.ClearFaultAt(TimeSpan.FromSeconds(5), "Furnace", ItemProcessUnit.SlowCycle);

        // Ticks 8 and 9 count 0.25 s each, ticks 10 on count 0.5 s: 2 s after tick 12.
        // Unslowed it would be tick 11, always slowed tick 15, restarted on clear tick 13.
        plant.Sim.RunFor(TimeSpan.FromSeconds(10));

        Assert.Equal(12L, DischargeTick(plant.Sim));
    }

    [Fact]
    public void ProgressFollowsTheSlowedHoldTimer()
    {
        Plant plant = Build(batchSize: 3, Hold.ForSeconds(2.0));
        plant.Sim.InjectFaultAt(TimeSpan.Zero, "Furnace", ItemProcessUnit.SlowCycle);
        plant.Sim.RunFor(TimeSpan.FromSeconds(4));   // ticks 0-7: PROCESSING on tick 7

        var progress = new List<double>();
        for (int i = 0; i < 8; i++)
        {
            plant.Sim.Tick();                          // ticks 8-15; each publishes the timer as of the tick before
            progress.Add(plant.Unit.Progress.Value);
        }

        Assert.Equal([0.0, 0.125, 0.25, 0.375, 0.5, 0.625, 0.75, 0.875], progress);
        Assert.Equal(ProcessPhase.Discharging, plant.Unit.CurrentPhase);
    }

    [Fact]
    public void ASlowCycleAndADischargeJamAreIndependent()
    {
        Plant plant = Build(batchSize: 3, Hold.ForSeconds(2.0));
        plant.Sim.InjectFaultAt(TimeSpan.Zero, "Furnace", ItemProcessUnit.DischargeJam);
        plant.Sim.InjectFaultAt(TimeSpan.Zero, "Furnace", ItemProcessUnit.SlowCycle);

        plant.Sim.RunFor(TimeSpan.FromSeconds(10));
        Assert.Equal(15L, DischargeTick(plant.Sim));
        Assert.Equal(0L, plant.Sink.Count.Value);

        plant.Sim.ClearFaultIn(TimeSpan.Zero, "Furnace", ItemProcessUnit.SlowCycle);
        plant.Sim.RunFor(TimeSpan.FromSeconds(2));
        Assert.Equal(ProcessPhase.Discharging, plant.Unit.CurrentPhase);
        Assert.Equal(0.0, plant.Sink.MassReceived);

        plant.Sim.ClearFaultIn(TimeSpan.Zero, "Furnace", ItemProcessUnit.DischargeJam);
        plant.Sim.RunFor(TimeSpan.FromSeconds(1));
        Assert.Equal(60.0, plant.Sink.MassReceived);
    }
}
```

- [ ] **Step 2: Write the failing factory test**

In `tests/Millrace.Components.Tests/Catalogue/FlowFactoryTests.cs`, replace

```csharp
    [Fact]
    public void ABulkMaterialCannotFeedAnItemSource()
```

with

```csharp
    [Fact]
    public void AnItemUnitHeatsWhileHeldOnlyWhenTheFileSaysSo()
    {
        const string Batch = """ "batchSize": 1, "hold": { "type": "for-seconds", "seconds": 5 } """;
        ItemProcessUnit plain = MechanicalFactoryTests.Build<ItemProcessUnit>(ItemProcessUnit.Descriptor, $$"""{ {{Batch}} }""", Context());
        ItemProcessUnit furnace = MechanicalFactoryTests.Build<ItemProcessUnit>(
            ItemProcessUnit.Descriptor, $$"""{ {{Batch}}, "heatWhileHeld": true }""", Context());

        Assert.False(plain.HeatWhileHeld);
        Assert.True(furnace.HeatWhileHeld);
    }

    [Fact]
    public void ABulkMaterialCannotFeedAnItemSource()
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/Millrace.Components.Tests --nologo --filter "FullyQualifiedName~Millrace.Components.Tests.ItemProcessUnitTests|FullyQualifiedName~Millrace.Components.Tests.Catalogue.FlowFactoryTests"`
Expected: the test project does not build. Measured, twelve distinct errors:
eleven `error CS0117: 'ItemProcessUnit' does not contain a definition for 'SlowCycle'`,
two `error CS1061: 'ItemProcessUnit' does not contain a definition for 'HeatWhileHeld' …`
(the factory test) and one
`error CS1729: 'ItemProcessUnit' does not contain a constructor that takes 7 arguments`
(the `Build` helper's call).

- [ ] **Step 4: Implement `heatWhileHeld` and `slow-cycle`**

In `src/Millrace.Components/Flow/ItemProcessUnit.cs`, replace

```csharp
/// applies the yield, and discharges in arrival order. A furnace and a press
/// are configurations of this class.
/// </summary>
```

with

```csharp
/// applies the yield, and discharges in arrival order. With
/// <c>heatWhileHeld</c> the transforms run on every tick it holds items, in
/// every phase, so a batch that cannot leave keeps soaking. A furnace and a
/// press are configurations of this class.
/// </summary>
```

Replace

```csharp
    public const string DischargeJam = "discharge-jam";

    private static readonly FaultDescriptor[] Faults =
    [
        new(DischargeJam, "The discharge fails to open; the batch stays in the unit until the fault is cleared."),
    ];
```

with

```csharp
    public const string DischargeJam = "discharge-jam";

    /// <summary>The unit runs slow: the hold timer advances at a fraction of real time.</summary>
    public const string SlowCycle = "slow-cycle";

    private static readonly FaultDescriptor[] Faults =
    [
        new(DischargeJam, "The discharge fails to open; the batch stays in the unit until the fault is cleared."),
        new(SlowCycle, "The unit runs slow; a timed hold takes longer. Temperature and state holds are unaffected.",
            new FaultParameter("fraction", "", 0.5, "Fraction of the hold timer's rate lost, 0..1; at 1 a timed hold never completes.")),
    ];
```

Replace

```csharp
            p.Objects<IMaterialTransform>("transforms")))
```

with

```csharp
            p.Objects<IMaterialTransform>("transforms"),
            p.Bool("heatWhileHeld")))
```

Replace

```csharp
            Param.ObjectList("transforms", "Applied, in order, every tick while holding.", ObjectSlots.Transform),
        ],
```

with

```csharp
            Param.ObjectList("transforms", "Applied, in order, every tick while holding.", ObjectSlots.Transform),
            Param.Bool(
                "heatWhileHeld",
                "Apply the transforms on every tick the unit holds items — filling, processing and discharging — not only while processing, so a batch that cannot leave keeps heating.",
                @default: false),
        ],
```

Replace

```csharp
    private bool _jammed;
    private bool _pendingFill;
```

with

```csharp
    private bool _jammed;
    private double _slowFraction;
    private bool _pendingFill;
```

Replace

```csharp
        IReadOnlyList<IMaterialTransform>? transforms = null)
        : base(id)
```

with

```csharp
        IReadOnlyList<IMaterialTransform>? transforms = null,
        bool heatWhileHeld = false)
        : base(id)
```

Replace

```csharp
        _transforms = transforms is null ? [] : transforms.ToArray();

        In = AddInlet("In", PayloadKind.Discrete);
```

with

```csharp
        _transforms = transforms is null ? [] : transforms.ToArray();
        HeatWhileHeld = heatWhileHeld;

        In = AddInlet("In", PayloadKind.Discrete);
```

Replace

```csharp
    /// <summary>Fraction of each item's mass that survives release.</summary>
    public double Yield { get; }
```

with

```csharp
    /// <summary>Fraction of each item's mass that survives release.</summary>
    public double Yield { get; }

    /// <summary>Whether the transforms run on every tick the unit holds items, not only while processing.</summary>
    public bool HeatWhileHeld { get; }
```

Replace

```csharp
    public override void Advance(in TickContext ctx)
    {
        switch (_phase)
```

with

```csharp
    public override void Advance(in TickContext ctx)
    {
        // Processing applies the transforms itself; every other phase does it here,
        // so a held item is heated exactly once per tick (R158).
        if (HeatWhileHeld && _phase != ProcessPhase.Processing)
        {
            ApplyTransforms(ctx.Dt);
        }

        switch (_phase)
```

Replace

```csharp
                ApplyTransforms(ctx.Dt);
                _elapsed += ctx.Dt;
```

with

```csharp
                ApplyTransforms(ctx.Dt);
                _elapsed += ctx.Dt * (1.0 - _slowFraction);
```

Replace

```csharp
    public void ApplyFault(string faultId, FaultArguments arguments) => _jammed = true;

    public void ClearFault(string faultId) => _jammed = false;
```

with

```csharp
    public void ApplyFault(string faultId, FaultArguments arguments)
    {
        switch (faultId)
        {
            case DischargeJam:
                _jammed = true;
                break;
            case SlowCycle:
                _slowFraction = Math.Clamp(arguments.Get("fraction"), 0.0, 1.0);
                break;
        }
    }

    public void ClearFault(string faultId)
    {
        switch (faultId)
        {
            case DischargeJam:
                _jammed = false;
                break;
            case SlowCycle:
                _slowFraction = 0.0;
                break;
        }
    }
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/Millrace.Components.Tests --nologo --filter "FullyQualifiedName~Millrace.Components.Tests.ItemProcessUnitTests|FullyQualifiedName~Millrace.Components.Tests.Catalogue.FlowFactoryTests"`
Expected: PASS, 29 (23 `ItemProcessUnitTests` + 6 `FlowFactoryTests`).

Run: `dotnet test tests/Millrace.Components.Tests --nologo --filter "FullyQualifiedName~Millrace.Components.Tests.Catalogue"`
Expected: FAIL — 1 failed, 17 passed, 18 total: only
`ComponentsExportTests.TheShippedCatalogueExportsExactlyTheGoldenFile` (the
export gained the parameter and the fault). `EveryDescriptorMatchesWhatItBuilds`
passes: the descriptor and the instance agree on `slow-cycle(fraction)`.

- [ ] **Step 6: Regenerate the two goldens and read them**

Run: `MILLRACE_UPDATE_GOLDEN=1 dotnet test tests/Millrace.Components.Tests --nologo --filter "FullyQualifiedName~ComponentsExportTests.TheShippedCatalogueExportsExactlyTheGoldenFile"`
Run: `MILLRACE_UPDATE_GOLDEN=1 dotnet test tests/Millrace.Configuration.Tests --nologo --filter "FullyQualifiedName~PlantSchemaTests.MatchesTheGoldenFile"`

Run: `git diff --stat` — expect `components-catalogue.json | 19 +` and
`plant.schema.json | 5 +` (no `-`). Read both diffs whole. The export gains,
after `transforms` in `item-process-unit`'s parameters:

```json
        {
          "name": "heatWhileHeld",
          "kind": "bool",
          "required": false,
          "description": "Apply the transforms on every tick the unit holds items — filling, processing and discharging — not only while processing, so a batch that cannot leave keeps heating.",
          "default": false
        }
```

and, after `discharge-jam` in its faults:

```json
        {
          "id": "slow-cycle",
          "description": "The unit runs slow; a timed hold takes longer. Temperature and state holds are unaffected.",
          "parameters": [
            {
              "name": "fraction",
              "unit": "",
              "default": 0.5,
              "description": "Fraction of the hold timer's rate lost, 0..1; at 1 a timed hold never completes."
            }
          ]
        }
```

The schema gains, after `transforms` in `component.item-process-unit`'s
parameters:

```json
            "heatWhileHeld": {
              "description": "Apply the transforms on every tick the unit holds items — filling, processing and discharging — not only while processing, so a batch that cannot leave keeps heating.",
              "type": "boolean",
              "default": false
            }
```

(faults are not part of the schema).

- [ ] **Step 7: Document it**

In `docs/architecture.md`, replace

```markdown
Transforms (`IMaterialTransform`) run on resident material every tick, before
it moves and whatever the speed, with ambient conditions taken from the node's
signal inputs.
```

with

```markdown
Transforms (`IMaterialTransform`) run on resident material every tick, before
it moves and whatever the speed, with ambient conditions taken from the node's
signal inputs. A process unit runs them only while it processes, unless it is
built with `heatWhileHeld`: then they run on every tick it holds items —
filling, processing and discharging — so a batch that cannot leave keeps
heating, as it would in a real furnace.
```

(the sentence that follows, `Bulk cells pass an empty state span…`, stays), and replace

```markdown
is what it should have read. Physical faults — bearing friction, belt slip,
a welded contactor, a blocked chute — are declared per component.
```

with

```markdown
is what it should have read. Physical faults — bearing friction, belt slip,
a welded contactor, a blocked chute, a process unit that runs slow
(`slow-cycle`) — are declared per component. A component switches on the fault
id, so its faults are independent and may be active together.
```

In `docs/authoring-a-component.md`, replace

```markdown
parameters and their defaults, and change *state* in `ApplyFault` /
`ClearFault`. Faults arrive through the event queue at a tick boundary. An
```

with

```markdown
parameters and their defaults, and change *state* in `ApplyFault` /
`ClearFault`, switching on the fault id: a component's faults are independent
and may be active together, as `item-process-unit`'s `discharge-jam` and
`slow-cycle` are. Faults arrive through the event queue at a tick boundary. An
```

- [ ] **Step 8: Run everything**

Run: `dotnet test tests/Millrace.Components.Tests --nologo` — expect PASS, 153.
Run: `dotnet test tests/Millrace.Configuration.Tests --nologo` — expect PASS, 230.
Run: `dotnet build Millrace.sln -c Release --nologo` — expect `0 Warning(s)`, `0 Error(s)`.
Run: `dotnet test Millrace.sln --nologo` — expect **1461**: 37 / 498 / 153 / 57 /
230 / 167 / 78 / 140 / 23 / 78.
Run: `git status --short` — expect exactly the seven files of Step 9. Run
`git status --short --ignored -- 'tests/**/*.actual'` and delete every file it lists.

- [ ] **Step 9: Commit**

```bash
git add src/Millrace.Components/Flow/ItemProcessUnit.cs tests/Millrace.Components.Tests/ItemProcessUnitTests.cs tests/Millrace.Components.Tests/Catalogue/FlowFactoryTests.cs tests/Millrace.Components.Tests/Catalogue/Golden/components-catalogue.json tests/Millrace.Configuration.Tests/Golden/plant.schema.json docs/architecture.md docs/authoring-a-component.md
git commit -F .superpowers/sdd/6b.1/msg-task1.txt
```

with `.superpowers/sdd/6b.1/msg-task1.txt` (written with the Write tool) holding:

```
feat(components): heat a held batch and add a slow-cycle fault

An item-process-unit built with heatWhileHeld runs its transforms on
every tick it holds items — filling, processing and discharging, jammed
or blocked — so a batch that cannot leave keeps heating, as a furnace
does. Each held item is heated once per tick from the tick after it
arrives. The hold timer still counts only while processing. The default
is off, and nothing existing changes.

The new slow-cycle fault (fraction, default 0.5, clamped to 0..1) slows
the hold timer to dt × (1 − fraction): a 2 s timed hold takes 4 s at
0.5 and never completes at 1; temperature and state holds are
unaffected, and a clear keeps the time already counted. ApplyFault and
ClearFault now switch on the fault id, so discharge-jam and slow-cycle
are independent. The components export and the plant schema gain the
parameter and the fault.

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
```

Then `git log -1 --format='%h%n%s%n--%n%b'` and check the subject, the blank
line and the trailer.

---

### Task 2: The `reject-gate` measuring station

**Model:** implementer sonnet; reviewer **opus** (the two-outlet seam, R156–R157, R163).

**Files:**
- Create: `src/Millrace.Components/Flow/RejectGate.cs`
- Modify: `src/Millrace.Components/ComponentsModule.cs` (registration)
- Test: `tests/Millrace.Components.Tests/RejectGateTests.cs` (new: 16 facts, theory of 4 = 20)
- Test: `tests/Millrace.Components.Tests/Catalogue/FlowFactoryTests.cs` (+ 1 fact)
- Test: `tests/Millrace.Components.Tests/Catalogue/ComponentsFixtures.cs` (conformance fixture)
- Test: `tests/Millrace.Components.Tests/Catalogue/ComponentsExportTests.cs` (29 → 30)
- Regenerate: `tests/Millrace.Components.Tests/Catalogue/Golden/components-catalogue.json`, `tests/Millrace.Configuration.Tests/Golden/plant.schema.json`
- Modify: `docs/architecture.md` (*Material flow*, *Faults*), `README.md` (status), `docs/superpowers/specs/2026-09-02-industrial-process-simulation-engine-design.md` (§18)

**Interfaces:**
- Consumes: `FlowComponentBase` (`AddInlet`, `AddOutlet`, `AddInput`, `AddOutput`),
  `IItemConsumer`, `IItemProducer`, `IMaterialObservable`, `IFaultTarget`,
  `ITagProvider`; `TagBinding.Write(string, InputPort<bool>, string)`,
  `TagBinding.Read(string, OutputPort<bool>, string)`,
  `TagBinding.Read(string, OutputPort<long>, string unit, string description)`;
  `InitContext.RegisterTelemetry`; `TickContext.Log`; test fakes `Switch`;
  `Pyrometer(string, IMaterialObservable, double positionM, double windowM, InstrumentSpec)`.
- Produces: `public sealed class RejectGate : FlowComponentBase, IItemConsumer, IItemProducer, IMaterialObservable, IFaultTarget, ITagProvider`
  with `const string Stuck = "stuck"`, `static ComponentDescriptor Descriptor`
  (`reject-gate`), constructor `RejectGate(string id, double dwellSeconds)`,
  ports `In`, `Out`, `RejectOut` (flow), `Reject` (`InputPort<bool>`),
  `Occupied` (`OutputPort<bool>`), `Passed`, `Rejected` (`OutputPort<long>`),
  properties `DwellSeconds`, `Item` (`ItemInstance?`); tags `Reject` (RW),
  `Occupied`, `Passed`, `Rejected` (RO); telemetry `Passed`, `Rejected`; event
  `REJECTED` — `Item <id> rejected.`

- [ ] **Step 1: Write the failing tests**

Create `tests/Millrace.Components.Tests/RejectGateTests.cs`:

```csharp
using Millrace.Components.Flow;
using Millrace.Components.Instruments;
using Millrace.Components.Tests.Fakes;
using Millrace.Core;
using Millrace.Core.Flow;
using Millrace.Core.Logging;
using Millrace.Core.Time;
using Millrace.Io;
using Xunit;

namespace Millrace.Components.Tests;

public class RejectGateTests
{
    private static readonly MaterialType Billet = new("Billet", PayloadKind.Discrete);

    private static SimulationOptions Options() => new()
    {
        Seed = 1UL,
        StartTime = new DateTimeOffset(2026, 1, 1, 6, 0, 0, TimeSpan.Zero),
        TimeStep = TimeSpan.FromSeconds(0.5),
    };

    private sealed record Rig(Simulation Sim, ItemSource Source, RejectGate Gate, ItemSink Good, ItemSink Scrap);

    /// <summary>
    /// Billets every <paramref name="interval"/> s at 900 °C into a gate with a 2 s dwell;
    /// Out feeds Good unless <paramref name="connectOut"/> is false, RejectOut feeds Scrap
    /// unless <paramref name="connectReject"/> is false.
    /// </summary>
    private static Rig Build(
        double interval = 1.0,
        int goodCapacity = int.MaxValue,
        int scrapCapacity = int.MaxValue,
        bool connectReject = true,
        bool connectOut = true,
        Switch? reject = null,
        Pyrometer? pyrometer = null,
        RejectGate? gate = null)
    {
        var source = new ItemSource("Billets", Billet, 20.0, interval, new MaterialProperties(7800.0, 0.0, 900.0));
        gate ??= new RejectGate("Gate", 2.0);
        var good = new ItemSink("Good", goodCapacity);
        var scrap = new ItemSink("Scrap", scrapCapacity);
        source.Out.ConnectTo(gate.In);
        if (connectOut)
        {
            gate.Out.ConnectTo(good.In);
        }

        if (connectReject)
        {
            gate.RejectOut.ConnectTo(scrap.In);
        }

        var builder = new SimulationBuilder(Options()).Add(good).Add(scrap).Add(gate).Add(source);
        if (reject is not null)
        {
            reject.Out.ConnectTo(gate.Reject);
            builder.Add(reject);
        }

        if (pyrometer is not null)
        {
            builder.Add(pyrometer);
        }

        return new Rig(builder.Build(), source, gate, good, scrap);
    }

    private static IEnumerable<SimEventRecord> Rejections(Simulation sim) =>
        sim.Events.Records.Where(r => r.Source == "Gate" && r.Code == "REJECTED");

    [Fact]
    public void AnItemDwellsThenLeavesByOutAndTheNextEntersOnTheSameTick()
    {
        Rig rig = Build();
        var onStation = new List<long>();

        for (int tick = 0; tick < 13; tick++)
        {
            rig.Sim.Tick();
            onStation.Add(rig.Gate.Item?.Id ?? 0L);
        }

        // Item 1 is deposited on tick 2, dwells through ticks 3-6 (4 × 0.5 s = 2 s)
        // and leaves on tick 7, when item 2 takes its place.
        Assert.Equal([0L, 0L, 1L, 1L, 1L, 1L, 1L, 2L, 2L, 2L, 2L, 2L, 3L], onStation);
        Assert.Equal(2L, rig.Good.LastItem!.Id);
        Assert.Equal(40.0, rig.Good.MassReceived);
        Assert.Equal(0.0, rig.Scrap.MassReceived);
        Assert.Empty(Rejections(rig.Sim));
    }

    [Fact]
    public void AWriteThatLandsOnTheReleaseTickDivertsThatItem()
    {
        Rig rig = Build();
        rig.Sim.WriteAt(TimeSpan.FromSeconds(3.5), "Gate.Reject", TagValue.Bool(true));   // tick 7

        rig.Sim.RunFor(TimeSpan.FromSeconds(4));   // ticks 0-7

        Assert.Equal(1L, rig.Scrap.LastItem!.Id);
        Assert.Equal(0.0, rig.Good.MassReceived);
        SimEventRecord rejected = Assert.Single(Rejections(rig.Sim));
        Assert.Equal((7L, "Item 1 rejected."), (rejected.Tick, rejected.Message));
    }

    [Fact]
    public void AWriteOneTickLateDivertsTheNextItem()
    {
        Rig rig = Build();
        rig.Sim.WriteAt(TimeSpan.FromSeconds(4), "Gate.Reject", TagValue.Bool(true));   // tick 8

        rig.Sim.RunFor(TimeSpan.FromSeconds(6.5));   // ticks 0-12

        Assert.Equal(1L, rig.Good.LastItem!.Id);
        Assert.Equal(2L, rig.Scrap.LastItem!.Id);
        Assert.Equal(12L, Assert.Single(Rejections(rig.Sim)).Tick);
    }

    [Fact]
    public void OnlyTheValueOnTheReleaseTickCounts()
    {
        Rig rig = Build();
        rig.Sim.WriteAt(TimeSpan.FromSeconds(1), "Gate.Reject", TagValue.Bool(true));      // before item 1 arrives
        rig.Sim.WriteAt(TimeSpan.FromSeconds(3.5), "Gate.Reject", TagValue.Bool(false));   // its release tick

        rig.Sim.RunFor(TimeSpan.FromSeconds(4));

        Assert.Equal(1L, rig.Good.LastItem!.Id);
        Assert.Null(rig.Scrap.LastItem);
    }

    [Fact]
    public void AnItemThatOutRefusesWaitsAndTheChoiceIsTakenAgainOnEveryTick()
    {
        Rig rig = Build(goodCapacity: 0);

        rig.Sim.RunFor(TimeSpan.FromSeconds(10));
        Assert.Equal(1L, rig.Gate.Item!.Id);
        Assert.True(rig.Gate.Occupied.Value);
        Assert.Equal(0L, rig.Gate.Passed.Value);

        rig.Sim.WriteIn(TimeSpan.Zero, "Gate.Reject", TagValue.Bool(true));   // tick 20
        rig.Sim.Tick();

        Assert.Equal(1L, rig.Scrap.LastItem!.Id);
        Assert.Equal(20L, Assert.Single(Rejections(rig.Sim)).Tick);
        Assert.Equal(2L, rig.Gate.Item!.Id);
    }

    [Fact]
    public void AnItemTheRejectOutletRefusesWaitsUntilRejectFalls()
    {
        Rig rig = Build(scrapCapacity: 0);
        rig.Sim.WriteAt(TimeSpan.Zero, "Gate.Reject", TagValue.Bool(true));

        rig.Sim.RunFor(TimeSpan.FromSeconds(10));
        Assert.Equal(1L, rig.Gate.Item!.Id);
        Assert.Equal(0.0, rig.Good.MassReceived);

        rig.Sim.WriteIn(TimeSpan.Zero, "Gate.Reject", TagValue.Bool(false));
        rig.Sim.Tick();

        Assert.Equal(1L, rig.Good.LastItem!.Id);
        Assert.Empty(Rejections(rig.Sim));
    }

    [Fact]
    public void AnUnconnectedRejectOutletHoldsARejectedItemOnTheStation()
    {
        var reject = new Switch("Kick", value: true);
        Rig rig = Build(connectReject: false, reject: reject);

        rig.Sim.RunFor(TimeSpan.FromSeconds(10));
        Assert.Equal(1L, rig.Gate.Item!.Id);

        reject.Value = false;
        rig.Sim.Tick();
        Assert.Equal(1L, rig.Good.LastItem!.Id);
    }

    [Fact]
    public void AnUnconnectedOutHoldsAPassedItemOnTheStation()
    {
        Rig rig = Build(connectOut: false);

        rig.Sim.RunFor(TimeSpan.FromSeconds(10));
        Assert.Equal(1L, rig.Gate.Item!.Id);

        rig.Sim.WriteIn(TimeSpan.Zero, "Gate.Reject", TagValue.Bool(true));   // tick 20
        rig.Sim.Tick();
        Assert.Equal(1L, rig.Scrap.LastItem!.Id);
        Assert.Equal(20L, Assert.Single(Rejections(rig.Sim)).Tick);
    }

    [Fact]
    public void ADepositOnAnOccupiedStationIsRefused()
    {
        var gate = new RejectGate("G", 1.0);
        gate.DepositItem(gate.In, new ItemInstance(1L, Billet, 20.0, default));

        InvalidOperationException refused = Assert.Throws<InvalidOperationException>(
            () => gate.DepositItem(gate.In, new ItemInstance(2L, Billet, 20.0, default)));
        Assert.Equal("Reject gate 'G' already holds Billet#1; it takes one item at a time.", refused.Message);
    }

    [Fact]
    public void TheStationHoldsOneItemAtATime()
    {
        Rig rig = Build(interval: 0.5, goodCapacity: 0);

        rig.Sim.RunFor(TimeSpan.FromSeconds(20));

        Assert.Equal(1L, rig.Gate.Item!.Id);
        Assert.Equal(20.0, rig.Gate.MassHeld);
        Assert.False(rig.Gate.CanAcceptItem(rig.Gate.In, new ItemInstance(99L, Billet, 20.0, default)));
        Assert.True(rig.Source.Queued.Value > 30);
        Assert.Equal(0.0, rig.Sim.MassBalance.Drift, 9);
    }

    [Fact]
    public void AStuckKickerPassesEveryItemUntilCleared()
    {
        Rig rig = Build();
        rig.Sim.WriteAt(TimeSpan.Zero, "Gate.Reject", TagValue.Bool(true));
        rig.Sim.InjectFaultAt(TimeSpan.Zero, "Gate", RejectGate.Stuck);
        rig.Sim.ClearFaultAt(TimeSpan.FromSeconds(7), "Gate", RejectGate.Stuck);   // tick 14

        rig.Sim.RunFor(TimeSpan.FromSeconds(9));   // items leave on ticks 7, 12 and 17

        Assert.Equal(40.0, rig.Good.MassReceived);
        Assert.Equal(2L, rig.Good.LastItem!.Id);
        Assert.Equal(3L, rig.Scrap.LastItem!.Id);
    }

    [Fact]
    public void AKickerThatSticksWhileAnItemWaitsOnARefusedRejectSendsItOut()
    {
        Rig rig = Build(scrapCapacity: 0);
        rig.Sim.WriteAt(TimeSpan.Zero, "Gate.Reject", TagValue.Bool(true));

        rig.Sim.RunFor(TimeSpan.FromSeconds(10));
        rig.Sim.InjectFaultIn(TimeSpan.Zero, "Gate", RejectGate.Stuck);
        rig.Sim.Tick();

        Assert.Equal(1L, rig.Good.LastItem!.Id);
    }

    [Fact]
    public void APyrometerOnTheStationSeesTheItemOrItsBackground()
    {
        var gate = new RejectGate("Gate", 2.0);
        var pyrometer = new Pyrometer("TT", gate, positionM: 0.0, windowM: 0.0, new InstrumentSpec("degC", 0.0, 1500.0));
        Rig rig = Build(interval: 5.0, pyrometer: pyrometer, gate: gate);
        var readings = new List<(bool Occupied, double Reading)>();

        for (int tick = 0; tick < 40; tick++)
        {
            bool occupied = gate.Item is not null;   // what phase 2 of the next tick sees
            rig.Sim.Tick();
            readings.Add((occupied, pyrometer.Value.Value));
        }

        Assert.Contains((true, 900.0), readings);
        Assert.Contains((false, 20.0), readings);
        Assert.All(readings, r => Assert.Equal(r.Occupied ? 900.0 : 20.0, r.Reading));
    }

    [Fact]
    public void EveryItemThatEntersLeavesByExactlyOneOutlet()
    {
        var reject = new Switch("Kick");
        Rig rig = Build(interval: 0.5, reject: reject);

        for (int tick = 0; tick < 240; tick++)
        {
            reject.Value = tick % 7 < 3;
            rig.Sim.Tick();
        }

        rig.Sim.Tick();   // publish the last hand-off
        Assert.True(rig.Good.Count.Value > 5);
        Assert.True(rig.Scrap.Count.Value > 5);
        Assert.Equal(rig.Good.Count.Value, rig.Gate.Passed.Value);
        Assert.Equal(rig.Scrap.Count.Value, rig.Gate.Rejected.Value);
        Assert.Equal((double)rig.Gate.Passed.Value, rig.Sim.Telemetry.Read("Gate.Passed"));
        Assert.Equal((double)rig.Gate.Rejected.Value, rig.Sim.Telemetry.Read("Gate.Rejected"));
        Assert.Equal(rig.Scrap.Count.Value, Rejections(rig.Sim).LongCount());
        Assert.Equal(
            rig.Sim.MassBalance.Created,
            rig.Good.MassReceived + rig.Scrap.MassReceived + rig.Gate.MassHeld + rig.Source.MassHeld,
            9);
        Assert.Equal(0.0, rig.Sim.MassBalance.Drift, 9);
    }

    [Fact]
    public void TheGatePublishesAWritableRejectAndReadOnlyState()
    {
        Assert.Equal(
            new[]
            {
                ("Reject", TagKind.Bool, TagAccess.ReadWrite),
                ("Occupied", TagKind.Bool, TagAccess.ReadOnly),
                ("Passed", TagKind.Int64, TagAccess.ReadOnly),
                ("Rejected", TagKind.Int64, TagAccess.ReadOnly),
            },
            new RejectGate("G", 1.0).DescribeTags().Select(t => (t.Name, t.Kind, t.Access)));
    }

    [Fact]
    public void ARejectInputASignalDrivesIsPublishedReadOnly()
    {
        Rig free = Build();
        Rig wired = Build(reject: new Switch("Kick"));

        Assert.Equal(TagAccess.ReadWrite, free.Sim.IO.Directory.Find("Gate.Reject").Access);
        Assert.Equal(TagAccess.ReadOnly, wired.Sim.IO.Directory.Find("Gate.Reject").Access);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void RejectsADwellThatIsNotAPositiveFiniteTime(double dwell)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RejectGate("G", dwell));
    }
}
```

In `tests/Millrace.Components.Tests/Catalogue/FlowFactoryTests.cs`, replace

```csharp
    [Fact]
    public void ABulkMaterialCannotFeedAnItemSource()
```

with

```csharp
    [Fact]
    public void ARejectGateReadsItsDwellAndHasTwoOutlets()
    {
        RejectGate gate = MechanicalFactoryTests.Build<RejectGate>(RejectGate.Descriptor, """{ "dwellSeconds": 1.5 }""");

        Assert.Equal(1.5, gate.DwellSeconds);
        Assert.Equal(["Out", "RejectOut"], gate.Ports.OfType<FlowOutlet>().Select(p => p.Name));
    }

    [Fact]
    public void ABulkMaterialCannotFeedAnItemSource()
```

In `tests/Millrace.Components.Tests/Catalogue/ComponentsFixtures.cs`, replace

```csharp
        .Parameters("item-process-unit", """{ "batchSize": 4, "hold": { "type": "for-seconds", "seconds": 10 } }""")
```

with

```csharp
        .Parameters("item-process-unit", """{ "batchSize": 4, "hold": { "type": "for-seconds", "seconds": 10 } }""")
        .Parameters("reject-gate", """{ "dwellSeconds": 2 }""")
```

In `tests/Millrace.Components.Tests/Catalogue/ComponentsExportTests.cs`, replace

```csharp
        Assert.Equal(29, document.RootElement.GetProperty("components").GetArrayLength());
```

with

```csharp
        Assert.Equal(30, document.RootElement.GetProperty("components").GetArrayLength());
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Millrace.Components.Tests --nologo --filter "FullyQualifiedName~Millrace.Components.Tests.RejectGateTests"`
Expected: the test project does not build — `error CS0246: The type or
namespace name 'RejectGate' could not be found` (measured: reported in
`RejectGateTests.cs` only; the compiler stops before it reports the use in
`FlowFactoryTests.cs`).

- [ ] **Step 3: Write the gate**

Create `src/Millrace.Components/Flow/RejectGate.cs`:

```csharp
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Millrace.Core.Catalogue;
using Millrace.Core.Contexts;
using Millrace.Core.Faults;
using Millrace.Core.Flow;
using Millrace.Core.Graph;
using Millrace.Core.Io;
using Millrace.Core.Telemetry;
using Millrace.Io;

namespace Millrace.Components.Flow;

/// <summary>
/// A measuring station with a kicker: holds one item for a dwell, then sends
/// it on through <c>Out</c>, or through <c>RejectOut</c> when the
/// <c>Reject</c> input is true on the tick it leaves. The dwell is what gives
/// an instrument and a PLC time to decide while the item is still on the
/// station. The choice is taken again on every tick the chosen outlet refuses
/// the item.
/// </summary>
public sealed class RejectGate : FlowComponentBase, IItemConsumer, IItemProducer, IMaterialObservable, IFaultTarget, ITagProvider
{
    /// <summary>The kicker does not fire: every item leaves by <c>Out</c> until cleared.</summary>
    public const string Stuck = "stuck";

    // Dwell accumulates dt once per tick; the tolerance absorbs the rounding of
    // many small steps (ten steps of 0.1 s sum to 0.9999999999999999 s).
    private const double DwellTolerance = 1e-9;

    private static readonly FaultDescriptor[] Faults =
    [
        new(Stuck, "The kicker does not fire; every item leaves by Out whatever Reject says, until the fault is cleared."),
    ];

    public static ComponentDescriptor Descriptor { get; } = new(
        "reject-gate",
        ComponentCategory.Flow,
        "A measuring station for discrete items: holds each for a dwell, then passes it on, or diverts it to the reject outlet while Reject is true.",
        (id, p) => new RejectGate(id, p.Double("dwellSeconds")))
    {
        Parameters =
        [
            Param.Double("dwellSeconds", "How long each item stays on the station before it may leave.", "s", min: 0.0, exclusiveMin: true),
        ],
        Ports =
        [
            PortSpec.In<bool>("Reject", description: "True sends the item leaving now to RejectOut. Defaults to false."),
            PortSpec.Out<bool>("Occupied"),
            PortSpec.Out<long>("Passed", "count"),
            PortSpec.Out<long>("Rejected", "count"),
        ],
        FlowPorts =
        [
            PortSpec.Inlet("In", PayloadKind.Discrete),
            PortSpec.Outlet("Out", PayloadKind.Discrete, "Items that pass."),
            PortSpec.Outlet("RejectOut", PayloadKind.Discrete, "Items rejected."),
        ],
        Faults = Faults,
        Tags =
        [
            new TagEntry("Reject", TagKind.Bool, TagAccess.ReadWrite),
            new TagEntry("Occupied", TagKind.Bool, TagAccess.ReadOnly),
            new TagEntry("Passed", TagKind.Int64, TagAccess.ReadOnly, "count"),
            new TagEntry("Rejected", TagKind.Int64, TagAccess.ReadOnly, "count"),
        ],
        Telemetry = [new TelemetryKey("Passed", "count"), new TelemetryKey("Rejected", "count")],
        Provides = [typeof(IMaterialObservable)],
    };

    private ItemInstance? _item;
    private double _dwelt;
    private long _passed;
    private long _rejected;
    private long _justRejected = -1L;
    private bool _stuck;
    private TelemetryHandle _passedTelemetry;
    private TelemetryHandle _rejectedTelemetry;

    public RejectGate(string id, double dwellSeconds)
        : base(id)
    {
        if (!(dwellSeconds > 0.0) || !double.IsFinite(dwellSeconds))
        {
            throw new ArgumentOutOfRangeException(nameof(dwellSeconds), dwellSeconds, "The dwell must be a positive, finite number of seconds.");
        }

        DwellSeconds = dwellSeconds;

        In = AddInlet("In", PayloadKind.Discrete);
        Out = AddOutlet("Out", PayloadKind.Discrete);
        RejectOut = AddOutlet("RejectOut", PayloadKind.Discrete);
        Reject = AddInput<bool>("Reject");
        Occupied = AddOutput<bool>("Occupied");
        Passed = AddOutput<long>("Passed");
        Rejected = AddOutput<long>("Rejected");
    }

    public FlowInlet In { get; }

    /// <summary>Where a passed item leaves.</summary>
    public FlowOutlet Out { get; }

    /// <summary>Where a rejected item leaves.</summary>
    public FlowOutlet RejectOut { get; }

    /// <summary>True sends the item leaving now to <see cref="RejectOut"/>. Unconnected reads false.</summary>
    public InputPort<bool> Reject { get; }

    /// <summary>An item is on the station, as of the last evaluate.</summary>
    public OutputPort<bool> Occupied { get; }

    /// <summary>Items that left by <see cref="Out"/>, cumulative.</summary>
    public OutputPort<long> Passed { get; }

    /// <summary>Items that left by <see cref="RejectOut"/>, cumulative.</summary>
    public OutputPort<long> Rejected { get; }

    /// <summary>Seconds each item stays before it may leave.</summary>
    public double DwellSeconds { get; }

    /// <summary>The item on the station, or null.</summary>
    public ItemInstance? Item => _item;

    public override double MassHeld => _item?.Mass ?? 0.0;

    public IReadOnlyList<FaultDescriptor> SupportedFaults => Faults;

    public IEnumerable<TagBinding> DescribeTags() =>
    [
        TagBinding.Write("Reject", Reject, "Divert the item leaving now to the reject outlet"),
        TagBinding.Read("Occupied", Occupied, "An item is on the station"),
        TagBinding.Read("Passed", Passed, "count", "Items passed"),
        TagBinding.Read("Rejected", Rejected, "count", "Items rejected"),
    ];

    public override void Initialize(in InitContext ctx)
    {
        _passedTelemetry = ctx.RegisterTelemetry("Passed", "count");
        _rejectedTelemetry = ctx.RegisterTelemetry("Rejected", "count");
    }

    public override void Evaluate(in TickContext ctx)
    {
        Occupied.Value = _item is not null;
        Passed.Value = _passed;
        Rejected.Value = _rejected;
        _passedTelemetry.Write(_passed);
        _rejectedTelemetry.Write(_rejected);
    }

    public override void Advance(in TickContext ctx)
    {
        // WithdrawItem has no context; its links ran just before this, on the same tick (R157).
        if (_justRejected >= 0L)
        {
            ctx.Log(Id, "REJECTED", string.Create(CultureInfo.InvariantCulture, $"Item {_justRejected} rejected."));
            _justRejected = -1L;
        }

        if (_item is not null)
        {
            _dwelt += ctx.Dt;
        }
    }

    public bool CanAcceptItem(FlowInlet inlet, ItemInstance item) => _item is null;

    public void DepositItem(FlowInlet inlet, ItemInstance item)
    {
        if (_item is not null)
        {
            throw new InvalidOperationException($"Reject gate '{Id}' already holds {_item}; it takes one item at a time.");
        }

        _item = item;
        _dwelt = 0.0;
    }

    public bool TryPeekItem(FlowOutlet outlet, [NotNullWhen(true)] out ItemInstance? item)
    {
        if (_item is not null && _dwelt >= DwellSeconds - DwellTolerance && ReferenceEquals(outlet, Chosen()))
        {
            item = _item;
            return true;
        }

        item = null;
        return false;
    }

    public ItemInstance WithdrawItem(FlowOutlet outlet)
    {
        if (!TryPeekItem(outlet, out ItemInstance? item))
        {
            throw new InvalidOperationException($"Reject gate '{Id}' has nothing ready to leave by '{outlet.Name}'.");
        }

        _item = null;
        _dwelt = 0.0;
        if (ReferenceEquals(outlet, RejectOut))
        {
            _rejected++;
            _justRejected = item.Id;
        }
        else
        {
            _passed++;
        }

        return item;
    }

    /// <summary>The item on the station; position and window are ignored.</summary>
    public bool TryObserve(double position, double window, out MaterialObservation observation)
    {
        if (_item is null)
        {
            observation = default;
            return false;
        }

        observation = new MaterialObservation(_item.Mass, 0.0, _item.Properties, _item.Id);
        return true;
    }

    public void ApplyFault(string faultId, FaultArguments arguments)
    {
        if (string.Equals(faultId, Stuck, StringComparison.Ordinal))
        {
            _stuck = true;
        }
    }

    public void ClearFault(string faultId)
    {
        if (string.Equals(faultId, Stuck, StringComparison.Ordinal))
        {
            _stuck = false;
        }
    }

    /// <summary>The outlet the item leaves by this tick: RejectOut while Reject is true and the kicker works.</summary>
    private FlowOutlet Chosen() => Reject.Value && !_stuck ? RejectOut : Out;
}
```

In `src/Millrace.Components/ComponentsModule.cs`, replace

```csharp
        builder.Add(ItemProcessUnit.Descriptor);
```

with

```csharp
        builder.Add(ItemProcessUnit.Descriptor);
        builder.Add(RejectGate.Descriptor);
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/Millrace.Components.Tests --nologo --filter "FullyQualifiedName~Millrace.Components.Tests.RejectGateTests"`
Expected: PASS, 20.

Run: `dotnet test tests/Millrace.Components.Tests --nologo --filter "FullyQualifiedName~Millrace.Components.Tests.Catalogue"`
Expected: FAIL — 1 failed, 18 passed, 19 total: only
`ComponentsExportTests.TheShippedCatalogueExportsExactlyTheGoldenFile`.
`EveryDescriptorMatchesWhatItBuilds` and
`EveryConcreteNodeTransformAndHoldHasADescriptor` pass (the gate's ports,
flow ports, fault, tags, telemetry and `Provides` all match), and
`TheShippedCatalogueHasTheExpectedCounts` passes with 30.

- [ ] **Step 5: Regenerate the two goldens and read them**

Run: `MILLRACE_UPDATE_GOLDEN=1 dotnet test tests/Millrace.Components.Tests --nologo --filter "FullyQualifiedName~ComponentsExportTests.TheShippedCatalogueExportsExactlyTheGoldenFile"`
Run: `MILLRACE_UPDATE_GOLDEN=1 dotnet test tests/Millrace.Configuration.Tests --nologo --filter "FullyQualifiedName~PlantSchemaTests.MatchesTheGoldenFile"`

Run: `git diff --stat` — expect `components-catalogue.json | 105 +` and
`plant.schema.json | 37 +` (no `-`). Read both diffs whole. The export gains
one component object between `pyrometer` and `safety-relay` (the catalogue
sorts by type): `"type": "reject-gate"`, `"category": "flow"`, the
description above, one parameter `dwellSeconds` (`"unit": "s"`,
`"minimum": 0`, `"exclusiveMinimum": true`, required), four ports (`Reject`
in bool with its description; `Occupied` out bool; `Passed`, `Rejected` out
long `count`), three flow ports (`In`; `Out` "Items that pass."; `RejectOut`
"Items rejected."), the `stuck` fault with no parameters, four tags,
telemetry `Passed`/`Rejected` in `count`, and `"provides": [ "IMaterialObservable" ]`.
The schema gains `{ "$ref": "#/$defs/component.reject-gate" }` in the
components `oneOf` between `pyrometer` and `safety-relay`, and a
`component.reject-gate` definition requiring `id`, `type` and `parameters`,
whose parameters require `dwellSeconds`
(`"description": "How long each item stays on the station before it may leave. Unit: s."`,
`"type": "number"`, `"exclusiveMinimum": 0`).

- [ ] **Step 6: Document it**

In `docs/architecture.md`, replace

```markdown
items at continuous positions with no diffusion; items queue behind a blocked
head at the minimum spacing.
```

with

```markdown
items at continuous positions with no diffusion; items queue behind a blocked
head at the minimum spacing.

A node may have more than one outlet; each still feeds exactly one inlet.
`RejectGate` (`reject-gate`) has `Out` and `RejectOut` and holds one item at a
time. It shows that item through `TryPeekItem` on exactly one outlet — the one
its `Reject` input chooses on that tick — so an item leaves by one outlet or
waits, and the choice is taken again on every tick it waits. The item stays for
`dwellSeconds`, counted from the tick after it arrives, and leaves on the tick
after that: deposited on tick N, it leaves on tick N + ⌈dwell / dt⌉ + 1 at the
earliest, the same one-tick hand-off a belt and a process unit have, and the
next item may enter on that same tick. The dwell is what lets an instrument
read the item and a control block decide before it leaves.

Size the dwell to cover that decision. From the tick an item arrives, its
instrument reads it on the next tick; an alarm raises on its first scan after
that, plus its on-delay rounded up to whole alarm scans; the block that writes
`Reject` writes on its first scan after the alarm publishes; and the write
lands one tick later. With the alarm scanning every *a* ticks and the writer
every *c* ticks, the decision is certain to land in time only when
⌈dwell / dt⌉ ≥ a + c + 1, plus the on-delay's ticks rounded up to a multiple
of *a*, plus any lag the instrument adds: 3 × dt when both scan every tick,
because the write then lands four ticks after the item arrives. A shorter dwell
lets the item leave before the decision, and the same latency decides when
`Reject` falls again for the item that follows.
```

and replace

```markdown
(`slow-cycle`) — are declared per component. A component switches on the fault
id, so its faults are independent and may be active together.
```

with

```markdown
(`slow-cycle`), a reject kicker that does not fire (`stuck`) — are declared per
component. A component switches on the fault id, so its faults are independent
and may be active together.
```

In `README.md`, rewrap the status paragraph whole: replace

```markdown
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
generated JSON Schema, and a `millrace` command line sit on top.
```

with

```markdown
Under construction. This repository contains the simulation core
(deterministic clock, per-component random streams, typed signal ports with
latched inputs, composites, topological resolution with algebraic-loop
detection, validation, telemetry, an ordered event log, a runner), the
material layer (bulk and discrete payloads, typed flow ports, offer/accept
transport, cell-based and position-based belts, residence transforms, a
per-tick mass conservation audit), the fault channel, and the first component
library: sources, sinks, a transfer chute, a former, bulk and item process
units, a reject gate, three transforms, an instrument base with the full
sensor-fault vocabulary, seven instruments, a motor with an I²t thermal model,
a drivetrain, a safety circuit, a starter, and a `Conveyor` composite that
trips its own overload when the belt downstream of it blocks — plus the I/O
and real-time layers. A component catalogue, declarative JSON plants with a
generated JSON Schema, and a `millrace` command line sit on top.
```

In `docs/superpowers/specs/2026-09-02-industrial-process-simulation-engine-design.md`
(§18, spec criterion 5), replace

```markdown
Pumps, valves, tanks, pipes, fans, crushers, feeders, hoppers, splitters,
mergers, disintegrators; pressure, level, flow and vibration sensors; guard
```

with

```markdown
Pumps, valves, tanks, pipes, fans, crushers, feeders, hoppers, general
splitters and mergers (a routed reject station for discrete items,
`reject-gate`, exists since plan 6b.1), disintegrators; pressure, level, flow
and vibration sensors; guard
```

- [ ] **Step 7: Run everything**

Run: `dotnet test tests/Millrace.Components.Tests --nologo` — expect PASS, 174.
Run: `dotnet test tests/Millrace.Configuration.Tests --nologo` — expect PASS, 230.
Run: `dotnet build Millrace.sln -c Release --nologo` — expect `0 Warning(s)`, `0 Error(s)`.
Run: `dotnet test Millrace.sln --nologo` — expect **1482**: 37 / 498 / 174 / 57 /
230 / 167 / 78 / 140 / 23 / 78.
Run: `git status --short` — expect exactly the eleven files of Step 8. Run
`git status --short --ignored -- 'tests/**/*.actual'` and delete every file it lists.

- [ ] **Step 8: Commit**

```bash
git add src/Millrace.Components/Flow/RejectGate.cs src/Millrace.Components/ComponentsModule.cs tests/Millrace.Components.Tests/RejectGateTests.cs tests/Millrace.Components.Tests/Catalogue/FlowFactoryTests.cs tests/Millrace.Components.Tests/Catalogue/ComponentsFixtures.cs tests/Millrace.Components.Tests/Catalogue/ComponentsExportTests.cs tests/Millrace.Components.Tests/Catalogue/Golden/components-catalogue.json tests/Millrace.Configuration.Tests/Golden/plant.schema.json docs/architecture.md README.md docs/superpowers/specs/2026-09-02-industrial-process-simulation-engine-design.md
git commit -F .superpowers/sdd/6b.1/msg-task2.txt
```

with `.superpowers/sdd/6b.1/msg-task2.txt`:

```
feat(components): add a reject gate for discrete items

reject-gate is a measuring station with a kicker: it holds one item for
dwellSeconds, then offers it on Out, or on RejectOut while its writable
Reject input is true on that tick. If the chosen outlet refuses, the
item waits and the choice is taken again every tick. An item deposited
on tick N leaves on tick N + ceil(dwell / dt) + 1 at the earliest, the
one-tick hand-off every flow node has, and the next enters that tick.
A rejected item logs REJECTED "Item <id> rejected.".

It publishes Reject read-write and Occupied, Passed and Rejected
read-only, counts both outlets in telemetry, lets a pyrometer see the
item on the station, and has a stuck fault that sends every item out.
The reject outlet is RejectOut because a component's port names must
differ ignoring case. No Core change: the flow graph already links
each outlet. Main spec §18 now names the routed reject station.

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
```

Then `git log -1 --format='%h%n%s%n--%n%b'`.

---

### Task 3: The `coil` control block

**Model:** implementer sonnet; reviewer **opus** (the scan semantics, R160–R161).

**Files:**
- Create: `src/Millrace.Control/Coil.cs`, `src/Millrace.Control.Catalogue/CoilCatalogue.cs`
- Modify: `src/Millrace.Control.Catalogue/ControlCatalogue.cs` (`ConditionOf`; summary), `src/Millrace.Control.Catalogue/ControlModule.cs` (registration; summary)
- Test: `tests/Millrace.Control.Tests/Scan.cs` (`Set(string, TagValue)`)
- Test: `tests/Millrace.Control.Tests/CoilTests.cs` (new: 6 facts, theory of 2 = 8)
- Test: `tests/Millrace.Control.Tests/DocumentationTests.cs` (+ 1 fact)
- Test: `tests/Millrace.Control.Catalogue.Tests/BlockFactoryTests.cs` (+ 1 fact, + theory of 3)
- Test: `tests/Millrace.Control.Catalogue.Tests/ControlFixtures.cs` (coil fixture)
- Test: `tests/Millrace.Control.Catalogue.Tests/ControlCatalogueTests.cs` (three changed, one of them renamed)
- Test: `tests/Millrace.Configuration.Tests/PlantSchemaTests.cs` (5 → 6)
- Test: `tests/Millrace.Configuration.Tests/Plants/valid/reject-station.json` (new corpus plant: + 1 `CorpusTests` row, + 1 `SchemaAgreementTests` row)
- Test: `tests/Millrace.Configuration.Tests/RejectStationTests.cs` (new: 2 facts)
- Test: `tests/Millrace.Configuration.Tests/RejectChainTests.cs` (new: theory of 3, R168; written after the coil exists)
- Regenerate: `tests/Millrace.Control.Catalogue.Tests/Golden/control-catalogue.json`, `tests/Millrace.Configuration.Tests/Golden/plant.schema.json`
- Modify: `docs/control-blocks.md` (six blocks; the `Coil` section), `docs/architecture.md` (*The control layer*), `README.md` (status)

**Interfaces:**
- Consumes: `IScanBlock`, `ScanInputs.Input(int)`, `ScanOutputs.Write(int, TagValue)`,
  `ScanOutputs.Set(int, TagValue)`, `TagRef`, `TagSpec`, `Condition(string Tag, bool Normal)`;
  `BlockDescriptor(type, description, ownedTags, factory)`, `Param.Group`,
  `Param.Tag(name, description, kind, writes)`, `ParameterValues.Group/Tag/Bool`,
  `ControlCatalogue.ConditionGroup`, `ControlCatalogue.Output`;
  `SimulationBuilder.AddScanBlock(IScanBlock, IReadOnlyList<string> claims)`;
  the `Vessel` fake (`V1.Fill`, `V1.Trip` RW; `V1.Tripped`, `V1.Running` RO);
  the gate of Task 2.
- Produces: `public sealed class Coil : IScanBlock` with constructor
  `Coil(string id, Condition condition, string output, TimeSpan scanPeriod)`;
  `Inputs = [condition.Tag (Bool)]`, `Writes = [output (Bool)]`,
  `Outputs = [Energised (Bool, "The condition is at its normal value; the output is driven true")]`,
  `Commands = []`, no events. `public static class CoilCatalogue` with
  `BlockDescriptor Descriptor` (`coil`: parameters `condition` group, `output`
  tag). `internal static Condition ControlCatalogue.ConditionOf(ParameterValues group)`.

- [ ] **Step 1: Write the failing block tests**

In `tests/Millrace.Control.Tests/Scan.cs`, replace

```csharp
    public Scan Set(string tag, long value) => SetInput(tag, TagValue.Int64(value));
```

with

```csharp
    public Scan Set(string tag, long value) => SetInput(tag, TagValue.Int64(value));

    /// <summary>Sets an input to a whole tag value, quality included.</summary>
    public Scan Set(string tag, TagValue value) => SetInput(tag, value);
```

Create `tests/Millrace.Control.Tests/CoilTests.cs`:

```csharp
using Millrace.Control.Tests.Fakes;
using Millrace.Core;
using Millrace.Core.Time;
using Millrace.Io;

namespace Millrace.Control.Tests;

public class CoilTests
{
    private static readonly TimeSpan Period = TimeSpan.FromMilliseconds(100);

    /// <summary>Drives V1.Fill true while V1.Tripped is false.</summary>
    private static Coil Make(bool normal = false) => new("COIL01", new Condition("V1.Tripped", normal), "V1.Fill", Period);

    private static string? Written(Scan scan) =>
        scan.TryWrite("V1.Fill", out TagValue value) ? value.ToString() : null;

    [Fact]
    public void ThePinsAreTheConditionTheOutputAndEnergised()
    {
        Coil coil = Make();

        Assert.Equal(new TagRef("V1.Tripped", TagKind.Bool), Assert.Single(coil.Inputs));
        Assert.Equal(new TagRef("V1.Fill", TagKind.Bool), Assert.Single(coil.Writes));
        TagSpec energised = Assert.Single(coil.Outputs);
        Assert.Equal(("Energised", TagKind.Bool), (energised.Name, energised.Kind));
        Assert.Empty(coil.Commands);
    }

    [Fact]
    public void TheConstructorRejectsBlankNamesAndANonPositivePeriod()
    {
        Assert.Throws<ArgumentException>(() => new Coil(" ", new Condition("V1.Tripped", false), "V1.Fill", Period));
        Assert.Throws<ArgumentException>(() => new Coil("COIL01", new Condition(" ", false), "V1.Fill", Period));
        Assert.Throws<ArgumentException>(() => new Coil("COIL01", new Condition("V1.Tripped", false), " ", Period));
        Assert.Throws<ArgumentNullException>(() => new Coil("COIL01", null!, "V1.Fill", Period));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Coil("COIL01", new Condition("V1.Tripped", false), "V1.Fill", TimeSpan.Zero));
    }

    [Fact]
    public void TheFirstScanWritesTheOutputEvenWhenItIsFalse()
    {
        var scan = new Scan(Make());
        scan.Set("V1.Tripped", true).Once();

        Assert.Equal("false", Written(scan));
        Assert.False(scan.Bool("Energised"));
    }

    [Fact]
    public void ItEnergisesAndDeEnergisesWithItsConditionAndWritesOnlyOnTransitions()
    {
        var scan = new Scan(Make());
        var writes = new List<string?>();

        foreach (bool tripped in new[] { false, false, false, true, true, false, false })
        {
            scan.Set("V1.Tripped", tripped).Once();
            writes.Add(Written(scan));
        }

        Assert.Equal(["true", null, null, "false", null, "true", null], writes);
        Assert.True(scan.Bool("Energised"));
        Assert.Empty(scan.Events);
    }

    [Fact]
    public void NormalTrueInvertsTheCoil()
    {
        var scan = new Scan(Make(normal: true));

        scan.Set("V1.Tripped", false).Once();
        Assert.Equal("false", Written(scan));
        Assert.False(scan.Bool("Energised"));

        scan.Set("V1.Tripped", true).Once();
        Assert.Equal("true", Written(scan));
        Assert.True(scan.Bool("Energised"));
    }

    [Theory]
    [InlineData(Quality.Uncertain)]
    [InlineData(Quality.Bad)]
    public void ItReadsTheConditionsValueWhateverItsQualityAsTheInterlockDoes(Quality quality)
    {
        var coil = new Scan(Make(normal: true));
        var interlock = new Scan(new Interlock("INT01", [new Condition("V1.Tripped", true)], [], Period));
        TagValue value = TagValue.Bool(true, new TagQuality(quality, QualityDetail.SensorFailure));

        coil.Set("V1.Tripped", value).Once();
        interlock.Set("V1.Tripped", value).Once();

        Assert.True(coil.Bool("Energised"));
        Assert.Equal("true", Written(coil));
        Assert.False(interlock.Bool("Tripped"));
    }

    [Fact]
    public void OverAPlantItDrivesItsClaimedOutputAndLogsEachWriteWithItsId()
    {
        var options = new SimulationOptions
        {
            Seed = 1UL,
            StartTime = new DateTimeOffset(2026, 1, 1, 6, 0, 0, TimeSpan.Zero),
            TimeStep = TimeSpan.FromMilliseconds(10),
        };
        Simulation sim = new SimulationBuilder(options)
            .Add(new Vessel("V1", 10.0))
            .AddScanBlock(Make(), ["V1.Fill"])
            .Build();
        sim.WriteAt(TimeSpan.FromMilliseconds(500), "V1.Trip", TagValue.Bool(true));
        sim.WriteAt(TimeSpan.FromMilliseconds(800), "V1.Trip", TagValue.Bool(false));

        sim.RunFor(TimeSpan.FromSeconds(1.2));

        // Scans at ticks 0, 10, 20, …: tick 0 energises (lands tick 1); Tripped is
        // published at the end of tick 50, seen by the scan at tick 60 (lands 61);
        // cleared at the end of tick 80, seen at tick 90 (lands 91).
        Assert.Equal(
            [(1L, "Set to true by COIL01."), (61L, "Set to false by COIL01."), (91L, "Set to true by COIL01.")],
            sim.Events.Records.Where(r => r.Source == "V1.Fill").Select(r => (r.Tick, r.Message)));
        Assert.True(sim.IO.ReadBool("V1.Fill"));
        Assert.True(sim.IO.ReadBool("COIL01.Energised"));
        TagDescriptor fill = sim.IO.Directory.Find("V1.Fill");
        Assert.Equal((TagAccess.ReadOnly, "COIL01"), (fill.Access, fill.ClaimedBy));
        Assert.Throws<InvalidOperationException>(() => sim.WriteIn(TimeSpan.Zero, "V1.Fill", TagValue.Bool(false)));
    }
}
```

In `tests/Millrace.Control.Tests/DocumentationTests.cs`, replace

```csharp
    [Fact]
    public void EveryLogLineThePageQuotesIsAWholeLineOfTheWorkedExampleGolden()
```

with

```csharp
    [Fact]
    public void TheControlBlocksPageDescribesTheCoil()
    {
        string page = Page();

        foreach (string token in new[]
                 {
                     "## `Coil`", "new Coil(", "\"type\": \"coil\"", "\"claims\": [ \"GATE.Reject\" ]", "Energised",
                     "on its first scan and on every scan", "Set to true by COIL01.", "carries six of them", "registers the six below",
                 })
        {
            Assert.Contains(token, page, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("carries five of them", page, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryLogLineThePageQuotesIsAWholeLineOfTheWorkedExampleGolden()
```

- [ ] **Step 2: Write the failing catalogue tests**

In `tests/Millrace.Control.Catalogue.Tests/BlockFactoryTests.cs`, replace the end of the class

```csharp
        Assert.Contains("[0, 31536000]", issue.Message, StringComparison.Ordinal);
    }
}
```

with

```csharp
        Assert.Contains("[0, 31536000]", issue.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ACoilReadsItsConditionAndCommandsItsOutput()
    {
        ParameterValues values = Bind.Values(
            CoilCatalogue.Descriptor.Parameters, """{ "condition": { "tag": "V1.Tripped", "normal": false }, "output": "V1.Fill" }""");

        IScanBlock block = CoilCatalogue.Descriptor.Factory("COIL01", Bind.Period, values);

        Assert.IsType<Coil>(block);
        Assert.Equal(new TagRef("V1.Tripped", TagKind.Bool), Assert.Single(block.Inputs));
        Assert.Equal(new TagRef("V1.Fill", TagKind.Bool), Assert.Single(block.Writes));
        Assert.Equal(Bind.Period, block.ScanPeriod);
        Assert.Equal(
            (new TagSpec("COIL01.Energised", TagKind.Bool, "", "The condition is at its normal value; the output is driven true"), TagAccess.ReadOnly),
            Assert.Single(CoilCatalogue.Descriptor.OwnedTags("COIL01", values)));
    }

    [Theory]
    [InlineData("""{ "condition": { "tag": "V1.Tripped", "normal": false }, "output": "V1.Running" }""", "$.output")]
    [InlineData("""{ "condition": { "tag": "V1.Level", "normal": false }, "output": "V1.Fill" }""", "$.condition.tag")]
    [InlineData("""{ "condition": { "tag": "V1.Tripped" }, "output": "V1.Fill" }""", "$.condition.normal")]
    public void ACoilRefusesAReadOnlyOutputANonBoolConditionAndAMissingNormal(string json, string path)
    {
        (ParameterValues? values, List<BindingIssue> issues) = Bind.TryValues(CoilCatalogue.Descriptor.Parameters, json);

        Assert.Null(values);
        Assert.Equal(path, Assert.Single(issues).Path);
    }
}
```

In `tests/Millrace.Control.Catalogue.Tests/ControlFixtures.cs`, replace

```csharp
        .BlockParameters("alarm", """{ "input": "V1.Level", "limits": [ { "kind": "hi", "value": 80 } ] }""")
```

with

```csharp
        .BlockParameters("alarm", """{ "input": "V1.Level", "limits": [ { "kind": "hi", "value": 80 } ] }""")
        .BlockParameters("coil", """{ "condition": { "tag": "V1.Tripped", "normal": true }, "output": "V1.Fill" }""")
```

In `tests/Millrace.Control.Catalogue.Tests/ControlCatalogueTests.cs`, replace

```csharp
        Assert.Equal(5, blocks.Count);
```

with

```csharp
        Assert.Equal(6, blocks.Count);
```

replace

```csharp
    public void TheModuleRegistersFiveBlocksAndTwoTransitions()
```

with

```csharp
    public void TheModuleRegistersSixBlocksAndTwoTransitions()
```

replace

```csharp
        Assert.Equal(["alarm", "interlock", "permissive", "sequencer", "timer"], catalogue.Blocks.Select(b => b.Type));
```

with

```csharp
        Assert.Equal(["alarm", "coil", "interlock", "permissive", "sequencer", "timer"], catalogue.Blocks.Select(b => b.Type));
```

and replace

```csharp
        Assert.Equal(5, root.GetProperty("blocks").GetArrayLength());
```

with

```csharp
        Assert.Equal(6, root.GetProperty("blocks").GetArrayLength());
```

- [ ] **Step 3: Write the failing plant-file tests**

In `tests/Millrace.Configuration.Tests/PlantSchemaTests.cs`, replace

```csharp
        Assert.Equal(5, branches.Length);
```

with

```csharp
        Assert.Equal(6, branches.Length);
```

Create `tests/Millrace.Configuration.Tests/Plants/valid/reject-station.json` (the
furnace's zone temperature is a writable tag that reads 20 °C until written,
so nothing reaches the alarm in the corpus test's 2 s; this plant proves
binding, not the chain):

```json
{
  "defaults": { "timeStepMs": 100 },
  "materials": [
    { "name": "billet", "kind": "discrete", "properties": { "density": 7800, "moisture": 0, "temperature": 20 }, "description": "A steel billet." }
  ],
  "components": [
    { "id": "SRC", "type": "item-source", "parameters": { "material": "billet", "itemMassKg": 12, "intervalSeconds": 5 } },
    { "id": "FCE", "type": "item-process-unit", "parameters": {
        "batchSize": 2, "heatWhileHeld": true,
        "hold": { "type": "for-seconds", "seconds": 30 },
        "transforms": [ { "type": "thermal-transfer", "timeConstantSeconds": 60 } ] } },
    { "id": "GATE", "type": "reject-gate", "parameters": { "dwellSeconds": 2 } },
    { "id": "GOOD", "type": "item-sink" },
    { "id": "SCRAP", "type": "item-sink" },
    { "id": "TT", "type": "pyrometer",
      "parameters": { "target": "GATE", "positionM": 0, "windowM": 0, "spec": { "unit": "°C", "rangeLow": 0, "rangeHigh": 1400 } } }
  ],
  "flows": [
    { "from": "SRC.Out", "to": "FCE.In" }, { "from": "FCE.Out", "to": "GATE.In" },
    { "from": "GATE.Out", "to": "GOOD.In" }, { "from": "GATE.RejectOut", "to": "SCRAP.In" }
  ],
  "tags": [ { "name": "FCE.ZONE_SP", "port": "FCE.AmbientTemperature", "access": "write", "unit": "°C", "rangeLow": 0, "rangeHigh": 1400 } ],
  "controllers": [
    { "id": "ALM01", "type": "alarm", "scanPeriodMs": 100,
      "parameters": { "input": "TT.Value", "limits": [ { "kind": "hi-hi", "value": 1200 } ] } },
    { "id": "COIL01", "type": "coil", "scanPeriodMs": 100, "claims": [ "GATE.Reject" ],
      "parameters": { "condition": { "tag": "ALM01.HiHi.Active", "normal": true }, "output": "GATE.Reject" } }
  ]
}
```

Create `tests/Millrace.Configuration.Tests/RejectStationTests.cs`:

```csharp
using Millrace.Core;
using Millrace.Io;

namespace Millrace.Configuration.Tests;

/// <summary>Spec 6b.1 criteria 3-5 in a plant file: a reject gate, a pyrometer aimed at it, and a coil that claims its Reject.</summary>
public class RejectStationTests
{
    private static string Plant => Corpus.Read("valid", "reject-station.json");

    [Fact]
    public void TheGateThePyrometerOnItAndTheCoilsClaimBind()
    {
        LoadResult result = Plants.Load(Plant);

        Assert.True(result.IsValid, result.ToText());
        Simulation simulation = result.Builder!.Build();
        TagDescriptor reject = simulation.IO.Directory.Find("GATE.Reject");
        Assert.Equal((TagKind.Bool, TagAccess.ReadOnly, "COIL01"), (reject.Kind, reject.Access, reject.ClaimedBy));
        Assert.Equal(TagAccess.ReadOnly, simulation.IO.Directory.Find("GATE.Occupied").Access);
        Assert.Equal(TagKind.Int64, simulation.IO.Directory.Find("GATE.Rejected").Kind);
        Assert.Equal(TagAccess.ReadOnly, simulation.IO.Directory.Find("COIL01.Energised").Access);

        simulation.RunFor(TimeSpan.FromSeconds(1));

        // The coil's first scan, at tick 0, writes the alarm's power-up state; it lands on tick 1.
        Assert.Equal(
            (1L, "Set to false by COIL01."),
            simulation.Events.Records.Where(r => r.Source == "GATE.Reject").Select(r => (r.Tick, r.Message)).Single());
        Assert.False(simulation.IO.ReadBool("COIL01.Energised"));
    }

    [Fact]
    public void ACoilWhoseOutputIsReadOnlyIsMr115AtTheOutput()
    {
        ConfigDiagnostic d = Plants.Only(Plant.Replace("\"output\": \"GATE.Reject\"", "\"output\": \"GATE.Occupied\"", StringComparison.Ordinal)
            .Replace("\"claims\": [ \"GATE.Reject\" ],", string.Empty, StringComparison.Ordinal));   // a coil may not claim what it does not command

        Assert.Equal(("MR115", "$.controllers[1].parameters.output"), (d.Code, d.Path));
    }
}
```

- [ ] **Step 4: Run the tests to verify they fail**

Run: `dotnet test tests/Millrace.Control.Tests --nologo --filter "FullyQualifiedName~Millrace.Control.Tests.CoilTests|FullyQualifiedName~Millrace.Control.Tests.DocumentationTests"`
Expected: the test project does not build — `error CS0246: The type or
namespace name 'Coil' could not be found`.

Run: `dotnet test tests/Millrace.Control.Catalogue.Tests --nologo`
Expected: the test project does not build — measured, four
`error CS0103: The name 'CoilCatalogue' does not exist in the current context`
and one `error CS0246: The type or namespace name 'Coil' could not be found`.

Run: `dotnet test tests/Millrace.Configuration.Tests --nologo --filter "FullyQualifiedName~RejectStationTests|FullyQualifiedName~CorpusTests.EveryValidPlantLoadsCleanAndBuilds|FullyQualifiedName~SchemaAgreementTests|FullyQualifiedName~PlantSchemaTests.HasOneBranchPerBlockType"`
Expected: FAIL — 5 failed, 55 passed, 60 total. The loader does not know
`coil` yet (measured: `MR102` at `$.controllers[1].type`), so
`TheGateThePyrometerOnItAndTheCoilsClaimBind`,
`ACoilWhoseOutputIsReadOnlyIsMr115AtTheOutput` and
`CorpusTests.EveryValidPlantLoadsCleanAndBuilds(name: "reject-station.json")`
fail; the schema has no `block.coil` branch, so
`SchemaAgreementTests.TheSchemaAcceptsEveryPlantTheLoaderAccepts(name: "reject-station.json")`
fails; and `HasOneBranchPerBlockType` counts 5, not 6. (A theory row's
arguments are not part of its `FullyQualifiedName`, so the corpus rows are
selected through their test methods.)

- [ ] **Step 5: Write the coil**

Create `src/Millrace.Control/Coil.cs`:

```csharp
using Millrace.Io;

namespace Millrace.Control;

/// <summary>
/// An output coil: energised while one Bool tag equals its normal value, and
/// driving one read-write Bool tag to follow. It writes on its first scan and
/// on every scan where the value changes — never otherwise, so the log carries
/// one <c>WRITE</c> per transition. Claim the output so nothing else changes
/// it between transitions.
/// </summary>
public sealed class Coil : IScanBlock
{
    private readonly Condition _condition;
    private bool _scanned;
    private bool _written;

    /// <summary>Creates a coil.</summary>
    /// <param name="id">The block id; prefixes <c>Energised</c>.</param>
    /// <param name="condition">The Bool tag watched, and the value that energises the coil.</param>
    /// <param name="output">The read-write Bool tag the coil drives.</param>
    /// <param name="scanPeriod">How often the block scans.</param>
    public Coil(string id, Condition condition, string output, TimeSpan scanPeriod)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(condition);
        ArgumentException.ThrowIfNullOrWhiteSpace(condition.Tag, nameof(condition));
        ArgumentException.ThrowIfNullOrWhiteSpace(output);

        if (scanPeriod <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(scanPeriod), scanPeriod, "The scan period must be positive.");
        }

        _condition = condition;
        Id = id;
        ScanPeriod = scanPeriod;
        Inputs = [new TagRef(condition.Tag, TagKind.Bool)];
        Writes = [new TagRef(output, TagKind.Bool)];
    }

    /// <inheritdoc/>
    public string Id { get; }

    /// <inheritdoc/>
    public TimeSpan ScanPeriod { get; }

    /// <inheritdoc/>
    public IReadOnlyList<TagRef> Inputs { get; }

    /// <inheritdoc/>
    public IReadOnlyList<TagRef> Writes { get; }

    /// <inheritdoc/>
    public IReadOnlyList<TagSpec> Outputs { get; } =
    [
        new TagSpec("Energised", TagKind.Bool, "", "The condition is at its normal value; the output is driven true"),
    ];

    /// <inheritdoc/>
    public IReadOnlyList<TagSpec> Commands { get; } = [];

    /// <inheritdoc/>
    public void Scan(in ScanInputs inputs, ref ScanOutputs outputs)
    {
        // The value is used whatever its quality, as the interlock and the permissive do (R160).
        bool energised = inputs.Input(0).AsBool == _condition.Normal;

        if (!_scanned || energised != _written)
        {
            outputs.Write(0, TagValue.Bool(energised));
            _written = energised;
            _scanned = true;
        }

        outputs.Set(0, TagValue.Bool(energised));
    }
}
```

- [ ] **Step 6: Register it in the catalogue**

Create `src/Millrace.Control.Catalogue/CoilCatalogue.cs`:

```csharp
using Millrace.Core.Catalogue;
using Millrace.Io;

namespace Millrace.Control.Catalogue;

/// <summary>The <see cref="Coil"/> in a plant file: <c>{ "type": "coil", … }</c>.</summary>
public static class CoilCatalogue
{
    public static BlockDescriptor Descriptor { get; } = new(
        "coil",
        "An output coil: drives a Bool tag true while a condition is at its normal value and false otherwise, writing on its first scan and on each change.",
        (id, p) =>
        [
            ControlCatalogue.Output(id, "Energised", TagKind.Bool, string.Empty, "The condition is at its normal value; the output is driven true"),
        ],
        (id, period, p) => new Coil(id, ControlCatalogue.ConditionOf(p.Group("condition")), p.Tag("output"), period))
    {
        Parameters =
        [
            Param.Group("condition", "The Bool tag watched, and the value that energises the coil.", ControlCatalogue.ConditionGroup),
            Param.Tag("output", "The Bool tag the coil drives; claim it so nothing else writes it.", TagKind.Bool, writes: true),
        ],
    };
}
```

In `src/Millrace.Control.Catalogue/ControlCatalogue.cs`, replace

```csharp
/// <summary>What the five block descriptors share: the condition and write groups, the transition slot, the duration bound.</summary>
```

with

```csharp
/// <summary>What the block descriptors share: the condition and write groups, the transition slot, the duration bound.</summary>
```

and replace

```csharp
    internal static IReadOnlyList<Condition> Conditions(ParameterValues p, string name) =>
        p.Groups(name).Select(g => new Condition(g.Tag("tag"), g.Bool("normal"))).ToList();
```

with

```csharp
    internal static IReadOnlyList<Condition> Conditions(ParameterValues p, string name) =>
        p.Groups(name).Select(ConditionOf).ToList();

    internal static Condition ConditionOf(ParameterValues group) => new(group.Tag("tag"), group.Bool("normal"));
```

In `src/Millrace.Control.Catalogue/ControlModule.cs`, replace

```csharp
/// The five control blocks of <c>Millrace.Control</c> and the sequencer's
```

with

```csharp
/// The six control blocks of <c>Millrace.Control</c> and the sequencer's
```

and replace

```csharp
        builder.AddBlock(SequencerCatalogue.Descriptor);
```

with

```csharp
        builder.AddBlock(SequencerCatalogue.Descriptor);
        builder.AddBlock(CoilCatalogue.Descriptor);
```

- [ ] **Step 7: Run the block and catalogue tests**

Run: `dotnet test tests/Millrace.Control.Tests --nologo --filter "FullyQualifiedName~Millrace.Control.Tests.CoilTests"`
Expected: PASS, 8.

Create `tests/Millrace.Configuration.Tests/RejectChainTests.cs` (R168; it needs the
coil to compile, so it comes after it, and its `0.2` row is its own failing
case):

```csharp
using Millrace.Components.Flow;
using Millrace.Components.Instruments;
using Millrace.Control;
using Millrace.Core;
using Millrace.Core.Flow;
using Millrace.Core.Time;

namespace Millrace.Configuration.Tests;

/// <summary>
/// R169: a reject gate's dwell must cover the PLC chain that decides it — the
/// pyrometer's tick, an alarm scan, a coil scan and the tick the write lands.
/// Built in code here because this project is the one that sees both
/// Millrace.Components and Millrace.Control.
/// </summary>
public class RejectChainTests
{
    [Theory]
    [InlineData(100, 100, 0.3, 8)]   // scans every tick: the write lands 4 ticks after arrival, the item may leave after 3 + 1
    [InlineData(100, 100, 0.2, 0)]   // one tick short: every hot item passes
    [InlineData(200, 100, 0.4, 8)]   // an alarm scanning every other tick needs one more tick of dwell
    public void AHotItemIsRejectedOnlyWhenTheDwellCoversTheChain(int alarmScanMs, int coilScanMs, double dwellSeconds, int rejected)
    {
        var options = new SimulationOptions
        {
            Seed = 1UL,
            StartTime = new DateTimeOffset(2026, 1, 1, 6, 0, 0, TimeSpan.Zero),
            TimeStep = TimeSpan.FromMilliseconds(100),
        };
        var billet = new MaterialType("Billet", PayloadKind.Discrete);
        var source = new ItemSource("SRC", billet, 10.0, 7.3, new MaterialProperties(7800.0, 0.0, 1000.0));
        var gate = new RejectGate("GATE", dwellSeconds);
        var good = new ItemSink("GOOD");
        var scrap = new ItemSink("SCRAP");
        source.Out.ConnectTo(gate.In);
        gate.Out.ConnectTo(good.In);
        gate.RejectOut.ConnectTo(scrap.In);
        var pyrometer = new Pyrometer("TT", gate, 0.0, 0.0, new InstrumentSpec("degC", 0.0, 1500.0));
        Simulation sim = new SimulationBuilder(options)
            .Add(good).Add(scrap).Add(gate).Add(source).Add(pyrometer)
            .AddScanBlock(new Alarm(
                "ALM01", "TT.Value", [new AlarmLimit(AlarmLimitKind.HiHi, 900.0, 0.0, TimeSpan.Zero)], TimeSpan.FromMilliseconds(alarmScanMs)))
            .AddScanBlock(new Coil("COIL01", new Condition("ALM01.HiHi.Active", true), "GATE.Reject", TimeSpan.FromMilliseconds(coilScanMs)), ["GATE.Reject"])
            .Build();

        sim.RunFor(TimeSpan.FromSeconds(60));   // eight 1000 °C billets, 7.3 s apart

        Assert.Equal(rejected * 10.0, scrap.MassReceived);
        Assert.Equal((8 - rejected) * 10.0, good.MassReceived);
    }
}
```

Run: `dotnet test tests/Millrace.Configuration.Tests --nologo --filter "FullyQualifiedName~Millrace.Configuration.Tests.RejectChainTests"`
Expected: PASS, 3.

Run: `dotnet test tests/Millrace.Control.Catalogue.Tests --nologo`
Expected: FAIL — exactly 1 failed, 26 passed:
`ControlCatalogueTests.TheControlCatalogueExportsExactlyTheGoldenFile`
(`EveryDescriptorMatchesWhatItBuilds` passes: the fixture's owned tag
`probe.Energised` matches the instance by name, kind, access, unit and
description).

- [ ] **Step 8: Regenerate the two goldens and read them**

Run: `MILLRACE_UPDATE_GOLDEN=1 dotnet test tests/Millrace.Control.Catalogue.Tests --nologo --filter "FullyQualifiedName~ControlCatalogueTests.TheControlCatalogueExportsExactlyTheGoldenFile"`
Run: `MILLRACE_UPDATE_GOLDEN=1 dotnet test tests/Millrace.Configuration.Tests --nologo --filter "FullyQualifiedName~PlantSchemaTests.MatchesTheGoldenFile"`

Run: `git diff --stat` — expect `control-catalogue.json | 37 +` and
`plant.schema.json | 55 +` (no `-`). Read both diffs whole. The control
export gains, between `alarm` and `interlock`:

```json
    {
      "type": "coil",
      "module": "Millrace.Control",
      "description": "An output coil: drives a Bool tag true while a condition is at its normal value and false otherwise, writing on its first scan and on each change.",
      "parameters": [
        {
          "name": "condition",
          "kind": "group",
          "required": true,
          "description": "The Bool tag watched, and the value that energises the coil.",
          "group": "Condition",
          "parameters": [
            {
              "name": "tag",
              "kind": "tag",
              "required": true,
              "description": "The Bool tag watched.",
              "tagKind": "bool"
            },
            {
              "name": "normal",
              "kind": "bool",
              "required": true,
              "description": "The value that means the condition is satisfied."
            }
          ]
        },
        {
          "name": "output",
          "kind": "tag",
          "required": true,
          "description": "The Bool tag the coil drives; claim it so nothing else writes it.",
          "tagKind": "bool",
          "writes": true
        }
      ]
    },
```

The schema gains `{ "$ref": "#/$defs/block.coil" }` in the controllers
`oneOf` between `alarm` and `interlock`, and a `block.coil` definition with the
controller keys every block has (`id`, `type` const `coil`, `scanPeriodMs`,
`claims`, `parameters`), whose parameters require `condition`
(`"$ref": "#/$defs/group.Condition"`) and `output` (a string described
`The Bool tag the coil drives; claim it so nothing else writes it. The full name of a Bool tag the block commands, so it must be read-write. The schema cannot check that it exists; the loader does.`).

- [ ] **Step 9: Document it**

In `docs/control-blocks.md`, replace

```markdown
reads, scanned at its own period, whose outputs are ordinary tags. `Millrace.Control`
carries five of them — a timer, a permissive, an interlock, an alarm and a
sequencer — and `Millrace.Core` carries the host that scans them.
```

with

```markdown
reads, scanned at its own period, whose outputs are ordinary tags. `Millrace.Control`
carries six of them — a timer, a permissive, an interlock, an alarm, a
sequencer and a coil — and `Millrace.Core` carries the host that scans them.
```

replace

```markdown
`Millrace.Control.Catalogue`, registers the five below, and `millrace catalog export`
```

with

```markdown
`Millrace.Control.Catalogue`, registers the six below, and `millrace catalog export`
```

and insert, immediately before the line `## Writing your own`, the following
section (it quotes write messages inline, never as a timestamped line — R167):

````markdown
## `Coil`

```csharp
new Coil("COIL01", new Condition("ALM_TT01.HiHi.Active", true), "GATE.Reject", TimeSpan.FromMilliseconds(100))
```

In a plant file (claim the output — *Claiming a tag*, above):

```json
{ "id": "COIL01", "type": "coil", "scanPeriodMs": 100,
  "claims": [ "GATE.Reject" ],
  "parameters": {
    "condition": { "tag": "ALM_TT01.HiHi.Active", "normal": true },
    "output": "GATE.Reject" } }
```

| pin | kind | |
|---|---|---|
| condition | Bool | the tag watched (`Inputs`) |
| output | Bool | the read-write tag driven (`Writes`) |
| `Energised` | Bool | output |

An output coil: the rung's result drives one Bool tag. `Energised` is true
while the condition's tag equals `normal` — so `normal: true` follows the tag
and `normal: false` inverts it — re-evaluated every scan and never latched. The
coil writes `output` to match `Energised` **on its first scan and on every scan
where the value changes**, and at no other time: a real output coil rewrites
its output every scan, but with the tag claimed nothing else can change it, so
writing on change leaves the tag in the same state with one `WRITE` per
transition — `Set to true by COIL01.` as it energises, `Set to false by
COIL01.` as it drops. The first scan, at tick 0, writes whatever the primed
image gives it: an alarm's `Active` powers up false, so a coil on it first
writes `false`. Like the interlock, it reads the condition's value whatever its
quality. No events.

Use a coil where a plant output must follow logic — a reject kicker driven by
an over-temperature alarm — and an interlock where it must latch until a reset.
Claim the output: unclaimed, anything may write it between transitions, and the
coil does not put it back until its condition next changes.

**Size a reject station's dwell to cover the chain.** From the tick an item
reaches a `reject-gate`, the pyrometer on it reads it on the next tick, the
alarm raises on its first scan after that (plus its on-delay, rounded up to
whole alarm scans), the coil writes on its first scan after the alarm
publishes, and the write lands one tick later. With the alarm scanning every
*a* ticks and the coil every *c*, the gate honours the decision only when
⌈dwell / dt⌉ ≥ a + c + 1, plus the on-delay in ticks rounded up to a multiple
of *a*, plus any lag the instrument adds. When both scan every tick the write
lands four ticks after the item arrives, so the dwell must be at least 3 × dt:
on a 100 ms step with 100 ms scans a 0.3 s dwell rejects every hot billet and a
0.2 s dwell none. A shorter dwell lets the item leave before the decision; the
same latency decides when `Reject` falls for the item that follows.

````

In `docs/architecture.md`, replace

```markdown
descriptors in the catalogue; `ControlModule`, in `Millrace.Control.Catalogue`,
registers the five shipped ones.
```

with

```markdown
descriptors in the catalogue; `ControlModule`, in `Millrace.Control.Catalogue`,
registers the six shipped ones.
```

and replace

```markdown
A control block is a PLC rung: `Millrace.Control` holds five of them — a timer, a
permissive, an interlock, an alarm and a sequencer — and sees
```

with

```markdown
A control block is a PLC rung: `Millrace.Control` holds six of them — a timer, a
permissive, an interlock, an alarm, a sequencer and a coil — and sees
```

In `README.md`, rewrap the control-layer paragraph whole: replace

```markdown
On top of that sits the control layer: a scan-block contract in
`Millrace.Io.Abstractions`, a host in `Millrace.Core` that scans each block at its own
period through the event queue and publishes its outputs as ordinary tags, and
`Millrace.Control` — a timer, a permissive, an interlock, an alarm and a sequencer,
which reference the I/O contract alone. Blocks are declared in a plant file's
`controllers` section — `Millrace.Control.Catalogue` registers them — or attached in
code.
```

with

```markdown
On top of that sits the control layer: a scan-block contract in
`Millrace.Io.Abstractions`, a host in `Millrace.Core` that scans each block at its own
period through the event queue and publishes its outputs as ordinary tags, and
`Millrace.Control` — a timer, a permissive, an interlock, an alarm, a sequencer and
a coil, which reference the I/O contract alone. Blocks are declared in a plant
file's `controllers` section — `Millrace.Control.Catalogue` registers them — or
attached in code.
```

- [ ] **Step 10: Run everything**

Run: `dotnet test tests/Millrace.Control.Tests --nologo` — expect PASS, 149.
Run: `dotnet test tests/Millrace.Control.Catalogue.Tests --nologo` — expect PASS, 27.
Run: `dotnet test tests/Millrace.Configuration.Tests --nologo` — expect PASS, 237.
Run: `dotnet build Millrace.sln -c Release --nologo` — expect `0 Warning(s)`, `0 Error(s)`.
Run: `dotnet test Millrace.sln --nologo` — expect **1502**: 37 / 498 / 174 / 57 /
237 / 167 / 78 / 149 / 27 / 78.
Run: `git status --short` — expect exactly the nineteen files of Step 11. Run
`git status --short --ignored -- 'tests/**/*.actual'` and delete every file it lists.

- [ ] **Step 11: Commit**

```bash
git add src/Millrace.Control/Coil.cs src/Millrace.Control.Catalogue/CoilCatalogue.cs src/Millrace.Control.Catalogue/ControlCatalogue.cs src/Millrace.Control.Catalogue/ControlModule.cs tests/Millrace.Control.Tests/Scan.cs tests/Millrace.Control.Tests/CoilTests.cs tests/Millrace.Control.Tests/DocumentationTests.cs tests/Millrace.Control.Catalogue.Tests/BlockFactoryTests.cs tests/Millrace.Control.Catalogue.Tests/ControlFixtures.cs tests/Millrace.Control.Catalogue.Tests/ControlCatalogueTests.cs tests/Millrace.Control.Catalogue.Tests/Golden/control-catalogue.json tests/Millrace.Configuration.Tests/PlantSchemaTests.cs tests/Millrace.Configuration.Tests/Plants/valid/reject-station.json tests/Millrace.Configuration.Tests/RejectStationTests.cs tests/Millrace.Configuration.Tests/RejectChainTests.cs tests/Millrace.Configuration.Tests/Golden/plant.schema.json docs/control-blocks.md docs/architecture.md README.md
git commit -F .superpowers/sdd/6b.1/msg-task3.txt
```

with `.superpowers/sdd/6b.1/msg-task3.txt`:

```
feat(control): add an output coil block

Coil drives one read-write Bool tag to follow a condition: Energised is
true while the condition's tag equals its normal value, and the coil
writes the output to match on its first scan and on every scan where the
value changes — never otherwise, so the log carries one WRITE per
transition ("Set to true by COIL01."). Like the interlock it uses the
condition's value whatever its quality. Claim the output so nothing else
changes it between transitions.

The catalogue registers it as "coil" with a condition group and an
output tag; a plant file aims a pyrometer at a reject gate and claims the
gate's Reject for a coil. The control catalogue export and the plant
schema gain the block; docs/control-blocks.md describes it, with the rule
for sizing a reject station's dwell to cover the pyrometer, alarm and
coil scans, which RejectChainTests pins.

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
```

Then `git log -1 --format='%h%n%s%n--%n%b'`.

---

### Task 4: The physical half of the chain, end to end (criterion 6)

**Model:** implementer sonnet; reviewer sonnet.

**Files:**
- Test: `tests/Millrace.Components.Tests/RejectLineTests.cs` (new: 2 facts)
- Modify: `README.md` (the wheel-line sentence)

**Interfaces:**
- Consumes: everything of Tasks 1–2 — `ItemProcessUnit(…, heatWhileHeld: true)`,
  `ItemProcessUnit.SlowCycle`, `RejectGate`, `RejectGate.Reject` (as the tag
  `Gate.Reject`); `DiscreteBelt(string id, double length, double maxSpeed, double minSpacing)`;
  `ItemSource`, `ItemSink`, `ThermalTransfer`, `Hold.TemperatureAtLeast`,
  `Hold.ForSeconds`; the `Setpoint` fake.
- Produces: tests only.

This task adds no production code, so its test passes on its first run; Step 2
proves it can fail by taking away the feature it depends on.

- [ ] **Step 1: Write the integration test**

Create `tests/Millrace.Components.Tests/RejectLineTests.cs`:

```csharp
using Millrace.Components.Flow;
using Millrace.Components.Tests.Fakes;
using Millrace.Components.Transforms;
using Millrace.Core;
using Millrace.Core.Faults;
using Millrace.Core.Flow;
using Millrace.Core.Logging;
using Millrace.Core.Time;
using Millrace.Io;
using Xunit;

namespace Millrace.Components.Tests;

/// <summary>
/// Spec 6b.1 criterion 6, the physical half of the wheel line's causal chain:
/// billets → furnace (heat while held, zone above the discharge target) →
/// reject gate → belt → press. A slow press queues blanks on the belt, the
/// gate cannot pass its billet, the furnace cannot discharge, and the billet
/// it holds over-soaks toward the zone temperature.
/// </summary>
public class RejectLineTests
{
    /// <summary>The over-soak limit: well above the 1100 °C discharge target, well below the 1250 °C zone.</summary>
    private const double OverSoak = 1150.0;

    private static readonly MaterialType Billet = new("Billet", PayloadKind.Discrete);
    private static readonly MaterialType Wheel = new("Wheel", PayloadKind.Discrete);

    private static SimulationOptions Options() => new()
    {
        Seed = 1UL,
        StartTime = new DateTimeOffset(2026, 1, 1, 6, 0, 0, TimeSpan.Zero),
        TimeStep = TimeSpan.FromSeconds(0.5),
    };

    private sealed record Line(
        Simulation Sim, ItemProcessUnit Furnace, RejectGate Gate, DiscreteBelt Belt, ItemProcessUnit Press, ItemSink Wheels, ItemSink Scrap);

    /// <summary>
    /// One 20 kg billet at 25 °C every 45 s; a one-billet furnace in a 1250 °C zone
    /// (20 s time constant) that discharges at 1100 °C, about 42 s in; a 2 s
    /// measuring station; a 3 m belt at 0.5 m/s holding at most four blanks 1 m
    /// apart; a press that forms a wheel in 15 s.
    /// </summary>
    private static Line Build()
    {
        var source = new ItemSource("Billets", Billet, 20.0, 45.0, new MaterialProperties(7800.0, 0.0, 25.0));
        var furnace = new ItemProcessUnit(
            "Furnace", 1, Hold.TemperatureAtLeast(1100.0), transforms: [new ThermalTransfer(20.0)], heatWhileHeld: true);
        var zone = new Setpoint("Zone", 1250.0);
        var gate = new RejectGate("Gate", 2.0);
        var belt = new DiscreteBelt("Belt", length: 3.0, maxSpeed: 1.0, minSpacing: 1.0);
        var speed = new Setpoint("BeltSpeed", 0.5);
        var press = new ItemProcessUnit("Press", 1, Hold.ForSeconds(15.0), Wheel, yield: 0.95);
        var wheels = new ItemSink("Wheels");
        var scrap = new ItemSink("Scrap");
        source.Out.ConnectTo(furnace.In);
        furnace.Out.ConnectTo(gate.In);
        gate.Out.ConnectTo(belt.In);
        gate.RejectOut.ConnectTo(scrap.In);
        belt.Out.ConnectTo(press.In);
        press.Out.ConnectTo(wheels.In);
        zone.Out.ConnectTo(furnace.AmbientTemperature);
        speed.Out.ConnectTo(belt.Speed);
        Simulation sim = new SimulationBuilder(Options())
            .Add(wheels).Add(scrap).Add(press).Add(belt).Add(gate).Add(furnace).Add(source).Add(zone).Add(speed)
            .Build();
        return new Line(sim, furnace, gate, belt, press, wheels, scrap);
    }

    [Fact]
    public void UnblockedEveryBilletLeavesTheFurnaceAtItsDischargeTarget()
    {
        Line line = Build();
        double hottest = 0.0;
        int longestHold = 0;
        int hold = 0;
        int mostOnBelt = 0;

        for (int tick = 0; tick < 1200; tick++)   // 600 s
        {
            line.Sim.Tick();
            hottest = Math.Max(hottest, line.Gate.Item?.Properties.Temperature ?? 0.0);
            hold = line.Furnace.CurrentPhase == ProcessPhase.Discharging ? hold + 1 : 0;
            longestHold = Math.Max(longestHold, hold);
            mostOnBelt = Math.Max(mostOnBelt, line.Belt.Items.Count);
        }

        // Measured: every billet reaches the station at 1100.197 °C, the furnace is
        // in Discharging for one tick per billet, and the belt never holds two.
        Assert.InRange(hottest, 1100.0, 1101.0);
        Assert.Equal(1, longestHold);
        Assert.Equal(1, mostOnBelt);
        Assert.True(line.Wheels.Count.Value >= 10);
        Assert.Null(line.Scrap.LastItem);
        Assert.Equal(0.0, line.Sim.MassBalance.Drift, 9);
    }

    [Fact]
    public void ASlowPressBacksUpTheLineOverSoaksTheHeldBilletAndTheGateRejectsIt()
    {
        Line line = Build();
        line.Sim.InjectFaultAt(
            TimeSpan.Zero, "Press", ItemProcessUnit.SlowCycle, new FaultArguments(new FaultArgument("fraction", 0.9)));
        line.Sim.WriteAt(TimeSpan.FromSeconds(560), "Gate.Reject", TagValue.Bool(true));
        line.Sim.WriteAt(TimeSpan.FromSeconds(570), "Gate.Reject", TagValue.Bool(false));

        // The press now takes 150 s a wheel. By 446.5 s the belt holds four blanks,
        // billet 8 waits on the station and billet 9, at its target, cannot leave.
        line.Sim.RunFor(TimeSpan.FromSeconds(500));   // ticks 0-999

        Assert.Equal(4, line.Belt.Items.Count);
        Assert.Equal(8L, line.Gate.Item!.Id);
        Assert.Equal(ProcessPhase.Discharging, line.Furnace.CurrentPhase);
        ItemInstance held = Assert.Single(line.Furnace.Items);
        Assert.Equal(9L, held.Id);
        Assert.InRange(held.Properties.Temperature, 1235.0, 1245.0);   // measured 1239.77 °C
        Assert.True(held.Properties.Temperature > OverSoak);

        // Billet 9 reaches the station at 550.5 s, over-soaked; the write that lands
        // at 560 s sends it to the reject outlet on that tick.
        line.Sim.RunFor(TimeSpan.FromSeconds(100));   // ticks 1000-1199

        Assert.Equal(9L, line.Scrap.LastItem!.Id);
        Assert.InRange(line.Scrap.LastItem.Properties.Temperature, 1245.0, 1250.0);   // measured 1249.21 °C
        Assert.Equal(20.0, line.Scrap.MassReceived);
        SimEventRecord rejected = Assert.Single(line.Sim.Events.Records, r => r.Code == "REJECTED");
        Assert.Equal((1120L, "Gate", "Item 9 rejected."), (rejected.Tick, rejected.Source, rejected.Message));
        Assert.Equal(10L, line.Gate.Item!.Id);   // the next billet, at its target, waits for the belt
        Assert.Equal(0.0, line.Sim.MassBalance.Drift, 9);
    }
}
```

Run: `dotnet test tests/Millrace.Components.Tests --nologo --filter "FullyQualifiedName~Millrace.Components.Tests.RejectLineTests"`
Expected: PASS, 2.

- [ ] **Step 2: Prove the over-soak depends on `heatWhileHeld`**

In `tests/Millrace.Components.Tests/RejectLineTests.cs`, temporarily change
`heatWhileHeld: true` to `heatWhileHeld: false` in `Build`, and run the same
command.
Expected: FAIL — exactly 1 failed,
`ASlowPressBacksUpTheLineOverSoaksTheHeldBilletAndTheGateRejectsIt`, at
`Assert.InRange(held.Properties.Temperature, 1235.0, 1245.0)` with the held
billet at its discharge temperature — measured
`Actual: 1100.1974549962738` (a batch that cannot leave no longer heats).
`UnblockedEveryBilletLeavesTheFurnaceAtItsDischargeTarget` still passes.
Change it back to `heatWhileHeld: true`, run the command again, and expect
PASS, 2. Read `Build` once more to confirm it says `heatWhileHeld: true`.

- [ ] **Step 3: Update the README**

In `README.md`, rewrap the samples paragraph whole: replace

```markdown
The first reference sample, `samples/mine-conveyors/`, is three conveyors, a
feeder and a stockpile with a sequenced start and stop, cascade interlocks,
permissives and alarms, and nine scenarios — a normal start and stop, a
pull-key, an e-stop, an overload, a blocked chute, a failed zero-speed switch, a
welded contactor, a starved feed and a start written while the line is tripped —
each with its golden log. It is data only: no C#. The second
sample, a wheel line of discrete items, is planned (plan 6b).
```

with

```markdown
The first reference sample, `samples/mine-conveyors/`, is three conveyors, a
feeder and a stockpile with a sequenced start and stop, cascade interlocks,
permissives and alarms, and nine scenarios — a normal start and stop, a
pull-key, an e-stop, an overload, a blocked chute, a failed zero-speed switch,
a welded contactor, a starved feed and a start written while the line is
tripped — each with its golden log. It is data only: no C#. The second sample,
a wheel line of discrete items, is planned (plan 6b.2); the physics and the
reject path it needs are in place (plan 6b.1).
```

- [ ] **Step 4: Run everything**

Run: `dotnet test tests/Millrace.Components.Tests --nologo` — expect PASS, 176.
Run: `dotnet build Millrace.sln -c Release --nologo` — expect `0 Warning(s)`, `0 Error(s)`.
Run: `dotnet test Millrace.sln --nologo` — expect **1504**: 37 / 498 / 176 / 57 /
237 / 167 / 78 / 149 / 27 / 78.
Run: `git diff --stat 45a1b52 -- src/` — expect exactly the seven files of the
Global Constraints.
Run: `git status --short` — expect exactly the two files of Step 5.

- [ ] **Step 5: Commit**

```bash
git add tests/Millrace.Components.Tests/RejectLineTests.cs README.md
git commit -F .superpowers/sdd/6b.1/msg-task4.txt
```

with `.superpowers/sdd/6b.1/msg-task4.txt`:

```
test(components): prove the wheel line's reject physics end to end

Billets feed a heat-while-held furnace in a 1250 °C zone that discharges
at 1100 °C, then a reject gate, a belt and a press. Unblocked, every
billet reaches the gate at its target and the belt never holds two. With
the press slowed by slow-cycle 0.9, blanks fill the belt, the gate
cannot pass its billet, the furnace is held in Discharging and the
billet it holds climbs past 1150 °C toward the zone; Reject written true
sends it to the reject sink ("Item 9 rejected."). The full PLC chain —
pyrometer, alarm, coil — is the wheel-line sample's, plan 6b.2.

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
```

Then `git log -1 --format='%h%n%s%n--%n%b'`.

---

## Spec coverage

| Spec criterion / section | Task | Tests |
|---|---|---|
| 1. `heatWhileHeld` (bool, default false) in the catalogue | 1 | `FlowFactoryTests.AnItemUnitHeatsWhileHeldOnlyWhenTheFileSaysSo`; `ComponentsExportTests.TheShippedCatalogueExportsExactlyTheGoldenFile`; `PlantSchemaTests.MatchesTheGoldenFile` |
| 1. transforms on every tick it holds items — Filling, Processing, Discharging, jammed or blocked | 1 | `ItemProcessUnitTests.WithHeatWhileHeldAJammedBatchHeatsOnEveryTickTheUnitHoldsIt`, `WithHeatWhileHeldABatchBlockedDownstreamHeatsExactlyAsAJammedOne` |
| 1. hold timer (`elapsed`, `Progress`) counts only in Processing | 1 | `ProgressFollowsTheSlowedHoldTimer`; `WithHeatWhileHeldAJammedBatchHeatsOnEveryTickTheUnitHoldsIt` (4 processing ticks, ticks 8–11) |
| 1. false: exactly as today; every existing test and golden unchanged | 1–4 | `WithoutHeatWhileHeldAHeldBatchHeatsOnlyWhileProcessing`; every existing `ItemProcessUnitTests` fact unchanged; `CorpusTests.EveryValidScenarioRunsCleanAndMatchesItsGolden` (`item-line-blinded-counter`); full suite (R164) |
| 2. `slow-cycle{fraction}` default 0.5, clamped [0, 1]; `dt × (1 − fraction)`; 10 s → 20 s at 0.5; never at 1 | 1 | `ASlowCycleStretchesATimedHoldByOneOverOneMinusTheFraction` (4 rows), `ASlowCycleDefaultsToHalfRate`, `AtFullSlowCycleATimedHoldNeverCompletes` (2 rows) |
| 2. temperature and state holds unaffected | 1 | `ASlowCycleLeavesATemperatureHoldAlone` |
| 2. clear restores full rate; time counted stays | 1 | `ClearingASlowCycleRestoresFullRateAndKeepsTheTimeAlreadyCounted`, `ClearingAFullSlowCycleResumesTheStoppedHold` |
| 2. `ApplyFault`/`ClearFault` switch on the id; `discharge-jam` and `slow-cycle` independent | 1 | `ASlowCycleAndADischargeJamAreIndependent`; `ADischargeJamHoldsTheBatch` (unchanged) |
| 2. `Progress` stays consistent | 1 | `ProgressFollowsTheSlowedHoldTimer`, `AtFullSlowCycleATimedHoldNeverCompletes` (`Progress` 0) |
| 3. flow: `In`; `Out` and the reject outlet (`RejectOut`, R155) | 2 | `FlowFactoryTests.ARejectGateReadsItsDwellAndHasTwoOutlets`; `ComponentsCatalogueTests.EveryDescriptorMatchesWhatItBuilds` |
| 3. `dwellSeconds` (> 0) | 2 | `RejectsADwellThatIsNotAPositiveFiniteTime` (4 rows); `ARejectGateReadsItsDwellAndHasTwoOutlets` |
| 3. `Reject` RW; `Occupied`, `Passed`, `Rejected` RO; telemetry | 2, 3 | `TheGatePublishesAWritableRejectAndReadOnlyState`, `ARejectInputASignalDrivesIsPublishedReadOnly`, `EveryItemThatEntersLeavesByExactlyOneOutlet` (telemetry); `RejectStationTests.TheGateThePyrometerOnItAndTheCoilsClaimBind` |
| 3. at most one item; accepts only when empty | 2 | `TheStationHoldsOneItemAtATime`, `ADepositOnAnOccupiedStationIsRefused`, `AnItemDwellsThenLeavesByOutAndTheNextEntersOnTheSameTick` |
| 3. dwell; offered on the first tick the dwell is complete (R156) | 2 | `AnItemDwellsThenLeavesByOutAndTheNextEntersOnTheSameTick` |
| 3. routed by `Reject` read on that tick (R157) | 2 | `AWriteThatLandsOnTheReleaseTickDivertsThatItem`, `AWriteOneTickLateDivertsTheNextItem`, `OnlyTheValueOnTheReleaseTickCounts` |
| 3. refused: waits, choice taken again each tick | 2 | `AnItemThatOutRefusesWaitsAndTheChoiceIsTakenAgainOnEveryTick`, `AnItemTheRejectOutletRefusesWaitsUntilRejectFalls`, `AnUnconnectedRejectOutletHoldsARejectedItemOnTheStation`, `AnUnconnectedOutHoldsAPassedItemOnTheStation` |
| 3. `REJECTED` — `Item <id> rejected.` | 2, 4 | `AWriteThatLandsOnTheReleaseTickDivertsThatItem`, `EveryItemThatEntersLeavesByExactlyOneOutlet`; `RejectLineTests.ASlowPressBacksUpTheLineOverSoaksTheHeldBilletAndTheGateRejectsIt` |
| 3. `IMaterialObservable`: item, or background when empty | 2, 3 | `APyrometerOnTheStationSeesTheItemOrItsBackground`; `reject-station.json` (`"target": "GATE"`) |
| 3. fault `stuck` | 2 | `AStuckKickerPassesEveryItemUntilCleared`, `AKickerThatSticksWhileAnItemWaitsOnARefusedRejectSendsItOut` |
| 3. mass conserved; exactly one outlet | 2, 4 | `EveryItemThatEntersLeavesByExactlyOneOutlet`, `TheStationHoldsOneItemAtATime`; `RejectLineTests` (drift 0) |
| 4. `condition {tag, normal}`, `output` | 3 | `CoilTests.ThePinsAreTheConditionTheOutputAndEnergised`; `BlockFactoryTests.ACoilReadsItsConditionAndCommandsItsOutput` |
| 4. energised while tag equals `normal`; `normal: false` inverts | 3 | `ItEnergisesAndDeEnergisesWithItsConditionAndWritesOnlyOnTransitions`, `NormalTrueInvertsTheCoil` |
| 4. quality as the interlock (R160) | 3 | `ItReadsTheConditionsValueWhateverItsQualityAsTheInterlockDoes` (2 rows) |
| 4. writes on first scan and on change only | 3 | `TheFirstScanWritesTheOutputEvenWhenItIsFalse`, `ItEnergisesAndDeEnergisesWithItsConditionAndWritesOnlyOnTransitions`, `OverAPlantItDrivesItsClaimedOutputAndLogsEachWriteWithItsId` |
| 4. owned output `Energised` | 3 | `ThePinsAreTheConditionTheOutputAndEnergised`; `ControlCatalogueTests.EveryDescriptorMatchesWhatItBuilds`; `ACoilReadsItsConditionAndCommandsItsOutput` |
| 4. output read-write Bool; pin checks; may be claimed | 3 | `ACoilRefusesAReadOnlyOutputANonBoolConditionAndAMissingNormal` (3 rows); `RejectStationTests.ACoilWhoseOutputIsReadOnlyIsMr115AtTheOutput`, `TheGateThePyrometerOnItAndTheCoilsClaimBind`; `OverAPlantItDrivesItsClaimedOutputAndLogsEachWriteWithItsId` |
| 5. catalogue with descriptions; plant schema golden | 1, 2, 3 | the three golden tests; `ComponentsExportTests.TheShippedCatalogueHasTheExpectedCounts`; `ControlCatalogueTests.TheModuleRegistersSixBlocksAndTwoTransitions`, `TheControlCatalogueHasTheExpectedCounts`, `EveryPublicScanBlockInMillraceControlHasADescriptor`; `PlantSchemaTests.HasOneBranchPerBlockType`, `HasOneBranchPerComponentType`; `SchemaAgreementTests` (`reject-station.json` row) |
| 5. docs where components, faults and blocks are documented (R167) | 1, 2, 3, 4 | `DocumentationTests.TheControlBlocksPageDescribesTheCoil`, `EveryLogLineThePageQuotesIsAWholeLineOfTheWorkedExampleGolden` (unchanged, still 3) |
| 5. main spec §18 amended | 2 | reviewed text (Task 2 Step 6) |
| §1 decision "a measuring station … the pyrometer, the alarm scan and the coil's write all complete while the item is still on the station" (R168, R169) | 2, 3 | `RejectChainTests.AHotItemIsRejectedOnlyWhenTheDwellCoversTheChain` (3 rows); the sizing paragraphs in `docs/architecture.md` and `docs/control-blocks.md` |
| 6. integration: source → furnace → gate → belt → press; queue, Discharging, over-soak past a limit it would not reach unblocked; reject sink | 4 | `RejectLineTests.UnblockedEveryBilletLeavesTheFurnaceAtItsDischargeTarget`, `ASlowPressBacksUpTheLineOverSoaksTheHeldBilletAndTheGateRejectsIt` |
| §2 no Core change (R163) | 1–4 | `git diff --stat 45a1b52 -- src/` (Task 4 Step 4) |
| §4 existing goldens unchanged but the schema and catalogue exports | 1–4 | full suite at every task (R164) |
| Review Focus 1–5 | 1, 2, 3 | the tests named there |

## Test-count arithmetic

Baseline on `45a1b52` (measured): 1445 = 37 + 498 + 137 + 57 + 230 + 167 + 78 +
140 + 23 + 78.

| Task | Added | Project totals after | Suite |
|---|---|---|---|
| 1 | `ItemProcessUnitTests` 9 facts + theory 4 + theory 2 = 15; `FlowFactoryTests` 1 → Components +16 | Components 153 | 1461 |
| 2 | `RejectGateTests` 16 facts + theory 4 = 20; `FlowFactoryTests` 1 → Components +21 (one existing count changed, none added by that) | Components 174 | 1482 |
| 3 | `CoilTests` 6 facts + theory 2 = 8, `DocumentationTests` 1 → Control +9; `BlockFactoryTests` 1 fact + theory 3 = 4 → Control.Catalogue +4 (three existing changed, one renamed); `RejectStationTests` 2 + the `reject-station.json` rows of `CorpusTests.EveryValidPlantLoadsCleanAndBuilds` and `SchemaAgreementTests` 2 + `RejectChainTests` theory 3 → Configuration +7 (one existing changed) | Control 149, Control.Catalogue 27, Configuration 237 | 1502 |
| 4 | `RejectLineTests` 2 → Components +2 | Components 176 | 1504 |

Final: **1504** = 37 Io.Abstractions / 498 Core / 176 Components / 57 Realtime
/ 237 Configuration / 167 Scenarios / 78 Cli / 149 Control / 27
Control.Catalogue / 78 Samples (measured in the scratch run). Existing tests
changed: five (`ComponentsExportTests.TheShippedCatalogueHasTheExpectedCounts`,
`ControlCatalogueTests.EveryPublicScanBlockInMillraceControlHasADescriptor`,
`TheModuleRegistersFiveBlocksAndTwoTransitions` → `TheModuleRegistersSixBlocksAndTwoTransitions`,
`TheControlCatalogueHasTheExpectedCounts`, `PlantSchemaTests.HasOneBranchPerBlockType`,
R164); none removed. Goldens regenerated: `components-catalogue.json` (Tasks 1,
2), `plant.schema.json` (Tasks 1, 2, 3), `control-catalogue.json` (Task 3); no
event-log golden moves.
