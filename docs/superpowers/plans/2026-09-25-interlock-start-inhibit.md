# Interlock Start Inhibit Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make an interlock a true start inhibit — a `Permit` input on
`motor-starter` (and so on `conveyor`, as `CVn.Permit`) and on `bulk-source`,
ANDed into the run logic, driven by new interlock **reset writes** (sent once, on
the scan that accepts a reset) — and prove it in the mine-conveyor sample, whose
four interlocks now write `Permit` false on trip and, on reset, `Permit` true
**and their command false** (the owner's seal-in ruling, R126), whose `SEQ_START`
resets each interlock one step before it starts the device, and with a new
scenario 9, `start-while-tripped`, in which a start written while the cascade is
tripped moves nothing — not then, and not after the line is reset.

**Architecture:** Two `src/` changes. `Dse.Components` (Task 1): each of the
three components gains a `Permit` Bool input, default true, bound as a writable
tag, and ANDed into `MotorStarter`'s `closed` and `BulkSource.Advance`; the
conveyor exposes its starter's. `Dse.Control` / `Dse.Control.Catalogue` (Task 2):
`Interlock` gains a five-argument constructor with `resetWrites`; its write pins
are the trip tags in order followed by each tag only the reset writes name, one
pin per distinct tag (R122), so an interlock with no reset writes declares
exactly the pins it has today. The sample (Tasks 3–4) is data: `plant.json`
(permit writes; `SEQ_START` restructured from six steps to nine), one new
scenario, regenerated goldens, re-pinned stories, a README section. No change
under `src/Dse.Core` or `src/Dse.Io.Abstractions`: the scan host already queues
each write pin at most once per scan and checks every pin against the directory
(DSE014), reset-only pins included.

**Tech Stack:** .NET 10 (`net10.0`, SDK 10.0.401), C#, xUnit 2.9.3. No package
under `src/`. JsonSchema.Net 8.0.5, test-only and pinned (unchanged).

**Spec:** `docs/superpowers/specs/2026-09-25-interlock-start-inhibit-design.md`
(all of it, as amended by this plan — its "Amended 2026-09-25 by the plan"
note, already applied), refining the
interlock of `docs/superpowers/specs/2026-09-22-control-blocks-design.md` (5c) and
the sample of `docs/superpowers/specs/2026-09-25-mine-conveyor-sample-design.md`
(6a, 6a.1).

**Plan sequence:** This is plan 6c. Plans 1–5d, 6a and 6a.1 are merged on
`master`; this plan starts from `aa3c403` (the commit that added the 6c spec).
Measured on that commit with `dotnet test Dse.sln`: **1323 tests**, all passing —
37 `Dse.Io.Abstractions` / 461 `Dse.Core` / 128 `Dse.Components` / 56
`Dse.Realtime` / 209 `Dse.Configuration` / 163 `Dse.Scenarios` / 76 `Dse.Cli` /
105 `Dse.Control` / 22 `Dse.Control.Catalogue` / 66 `Dse.Samples`.

**Task shape.** Five tasks; the spec's suggested six, with one merge the code
argued for:

- **The three `Permit` inputs are one task (Task 1).** Adding a tag moves the
  same exact-count assertions whichever component adds it (the worked example's
  plant has one conveyor and one feeder: 47 tags becomes 49), and the same
  components-catalogue golden. Split, both tasks would edit the same five
  count assertions and one comment line, and regenerate the same golden twice.
- **The scenario-9 README section goes with scenario 9 (Task 4),** not with the
  documentation task: `SampleReadmeTests.EveryScenarioHasOneQuotedBlockAndItsCommand`
  requires a quoted block for every `Sample.Names` entry, so the suite is red
  between the two otherwise.
- **The README quote fixes go with the golden regeneration (Task 3):** three
  quoted blocks (normal-start-stop, overload, chute-blockage) quote lines that
  move when `SEQ_START` gains its reset steps (R124, R129).

## Global Constraints

- **`src/` changes only in Tasks 1 and 2.** After Task 2,
  `git diff --stat aa3c403 -- src/` lists exactly
  `src/Dse.Components/Conveyors/Conveyor.cs`,
  `src/Dse.Components/Flow/BulkSource.cs`,
  `src/Dse.Components/Mechanical/MotorStarter.cs`,
  `src/Dse.Control.Catalogue/InterlockCatalogue.cs` and
  `src/Dse.Control/Interlock.cs`. If a scenario exposes any other engine defect,
  stop and report it rather than fixing `src/` inside a sample task.
  `git grep -n PackageReference -- 'src/*.csproj'` prints nothing (a plain
  `grep -r` over `src/` also hits `obj/` after a build). `Dse.Control` still
  references only `Dse.Io.Abstractions`.
- **Defaults change nothing** (spec criterion 5). With `Permit` unwired and no
  reset writes, every existing plant behaves exactly as before: the four
  `Dse.Scenarios.Tests` goldens, `tests/Dse.Control.Tests/Golden/conveyor-control.log`
  and every other event log outside `samples/mine-conveyors/` stay
  byte-identical, and the three catalogue/schema goldens change by added lines
  only. Measured: regenerating every golden in the solution with this plan's
  whole change applied rewrites only those three files and the sample goldens.
- **Determinism.** No `System.Random`, no `string.GetHashCode()`, no
  `Dictionary`/`HashSet` iteration order reaching an assertion or an output.
  String comparisons are ordinal. All formatting uses
  `CultureInfo.InvariantCulture`.
- **Warnings are errors** (`Directory.Build.props`: `TreatWarningsAsErrors`,
  `EnforceCodeStyleInBuild`, `GenerateDocumentationFile`, `Nullable`). The
  Release build prints `0 Warning(s)` and `0 Error(s)`.
- **xUnit analyzers run under warnings-as-errors:** prefer `Assert.Single`,
  `Assert.Contains`, `Assert.DoesNotContain`, `Assert.Empty`, `Assert.All`;
  never `Assert.True(x.Any())` or `Assert.Equal(1, x.Count())`.
- **Goldens are generated and read, never invented or hand-edited.** The three
  catalogue/schema goldens use `tests/Shared/Golden.cs`: regenerate each with
  `DSE_UPDATE_GOLDEN=1` and a `--filter` naming its one test, then read the whole
  `git diff` of that file. A sample golden is written by the golden theory when
  `DSE_UPDATE_GOLDEN=1`:
  `DSE_UPDATE_GOLDEN=1 dotnet test tests/Dse.Samples.Tests --nologo --filter "FullyQualifiedName~EveryScenarioMatchesItsGolden"`
  — **for this project and this theory only**, never over the whole solution.
  Then **read each changed golden in full** with a file-reading tool, check it
  against the task's checklist, quote the checked lines in the task report, and
  run the project again *without* the variable (the rebuild copies the new
  goldens to the test output, where `--expect` reads them). If a golden disagrees
  with a checklist, report the measured line and say which changed and why; never
  adjust a checklist, a story, a threshold or a duration to fit without saying so.
- **Report every measurement.** Where an expected value in this plan (a test
  count, a line count, a time, a value) disagrees with what the code produces,
  report the measured value in the task report.
- **Timing rule** (it shapes every golden here): **a scan at tick N sees the
  image published at the end of tick N−1; its queued writes land at phase 1 of
  tick N+1.** A scenario `write` at `at` lands at phase 1 of tick `at / 10 ms`.
  Interlocks scan at 100 ms, the sequencers at 200 ms, after the interlocks in
  file order. A `SEQ_START` reset step entered at T lands `INT_CVn.Reset` at
  T+10 ms; the interlock's scan at T+100 ms accepts it and publishes `Ok`; its
  reset writes (`CVn.Start` false, `CVn.Permit` true, in pin order) land at
  T+110 ms; the sequencer's scan at T+200 ms sees `Ok` and enters the start step,
  whose `CVn.Start` true lands at T+210 ms — after the reset's `false` (R124).
- **Names.** `CVn.Permit` (conveyor), `Feed.Permit` (bulk-source), `K1.Permit`
  (a bare starter) — exactly what `dse tags` prints; the mine-conveyor plant now
  has 124 tags (120 + 4). The new scenario, its golden and its `Sample.Names`
  entry are all `start-while-tripped`.
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
  `.superpowers/` is git-ignored; scratch work goes under `.superpowers/sdd/6c/`
  and is never added.

## Review Focus

The five failure modes the spec implies but its test list does not pin, most
likely to bite first. Each has its test in the owning task, named here.

1. **A reset's `false` racing the sequence's own start** — the reset writes drop
   the command (R126), so a sequence that reset an interlock and started its
   device in one step would have its `Start` overwritten 0.1 s later and time
   out. The author expects `SEQ_START` to start every belt, each after its
   interlock's reset has landed, and the speed proving to complete inside its
   15 s timeout. Tests: Task 3, the re-pinned `normal-start-stop` chain (reset
   step, `INTERLOCK_RESET`, `CVn.Start … Set to false by INT_CVn.`, start step,
   `CVn.Start … Set to true by SEQ_START.`, `CONTACTOR_CLOSED`, in that order),
   its three `Until`-bounded absences (no contactor closes before its
   `SEQ_START` start write) and its `SEQUENCE_FAULTED` absence, run by
   `MineConveyorTests.EveryScenarioTellsItsStory`; the step times in Task 3's
   golden checklist.
2. **A tag in both lists** (the permit pattern) — the author expects one write
   pin, the trip value on the trip scan, the reset value on the reset scan, each
   logged `… by INT01.`, neither recorded as an operator action (R77). Tests:
   Task 2, `InterlockTests.ATagInBothListsIsOneWritePinAfterTheTripPins`,
   `TheTripScanSendsTheTripWritesAndNoResetWrite`, and
   `HostTests.AResetWriteLandsTheTickAfterTheResetScanAndIsNotRecorded`.
3. **A refused or held reset** — a reset while a condition is still abnormal, or
   a `Reset` command held high after it was accepted, must not give the permit
   back (or send it twice). Tests: Task 2,
   `InterlockTests.ARefusedResetSendsNoResetWrite` and
   `TheResetWritesGoOutOnTheAcceptedResetScanOnly` (holds `Reset` high for three
   more scans).
4. **A welded contactor under a false permit** — the permit is logic in the
   run circuit, not a second contactor; the author expects a weld to defeat it
   exactly as it defeats `Start` and the e-stop (6a R108). Test: Task 1,
   `MotorStarterTests.AWeldedContactorStaysClosedWithoutAPermit`.
5. **An author who lists a tag twice under `reset`, or loads an old plant** —
   the first expects a `DSE111` naming the tag and the list, not a crash or a
   silent double write; the second expects nothing to change. Tests: Task 2, the
   corpus fixture `DSE111-reset-write-twice.json` (run by
   `CorpusTests.EveryInvalidPlantYieldsExactlyTheCodeInItsName` and
   `SchemaAgreementTests.TheSchemaRejectsStructuralErrorsAndOnlyThose`) and
   `InterlockTests.WithoutResetWritesThePinsAreExactlyTheTripWrites`; the
   unchanged event-log goldens of `Dse.Scenarios.Tests`, `Dse.Control.Tests` and
   `Dse.Cli.Tests`, which Tasks 1 and 2 run without regenerating.

## Decisions settled here (rulings R122–R132)

These refine the spec where the code, or a measured run, forced a choice. The 6c
spec is amended in place to agree (its "Amended 2026-09-25 by the plan" note), as plans 5d and
6a did.

- **R122 — One write pin per distinct tag; trip pins first.**
  `ScanOutputs.Write(index, value)` indexes the block's `Writes` list, one slot
  per pin, and `ScanBlockRuntime` resolves each pin to an image index once and
  queues at most one write per pin per scan, in pin order. So the interlock
  builds `Writes` as: the trip tags, in trip order (pin `i` is trip write `i`,
  exactly as today); then each reset tag not already a pin, in reset order. A
  reset write on a trip tag reuses that pin (the `Sequencer.Pin` pattern). The
  trip and reset branches are exclusive (`if (!allNormal && !_tripped) … else if
  (_tripped && resetEdge && allNormal)`), so a shared pin never receives two
  values in one scan. The host's DSE014 check walks every pin in `Writes`, so a
  reset-only tag that does not exist, has the wrong kind or is read-only is
  reported exactly as a trip tag is — no Core change. An interlock with no reset
  writes declares byte-identical `Writes` (criterion 5). The host queues one
  scan's writes in pin order, not list order: the sample's reset list is
  `[Permit true, Start false]`, and it logs `CVn.Start … false` before
  `CVn.Permit … true` in the same tick (measured; they land together, so the
  order is cosmetic).
- **R123 — The constructor and its validation.** Spec §3 asks for "the existing
  signature as an overload". The new constructor is
  `Interlock(string id, IReadOnlyList<Condition> conditions, IReadOnlyList<BlockWrite> tripWrites, TimeSpan scanPeriod, IReadOnlyList<BlockWrite> resetWrites)`
  — reset writes last, after the period, as `Sequencer`'s `abortWrites` — and the
  four-argument constructor chains to it with `[]`. `resetWrites` may be empty,
  not null (`ArgumentNullException`). The spec is silent on two cases: a blank
  reset tag (`ArgumentException`, parameter `resetWrites`) and one tag written as
  two kinds across the lists (`ArgumentException`: `Tag 'X' is commanded as a
  Double on trip and as a Bool on reset. Use one kind.`) — impossible from a plant
  file, where every `value` converts to its tag's kind. The duplicate message now
  names its list: `Tag 'X' is commanded twice on trip.` / `… twice on reset.`,
  then `Command each tag once in each list.` (the trip wording changes; no test,
  doc or golden pins it). The catalogue descriptor's `reset` parameter is a
  `GroupList` of `BlockWrite`, optional, described exactly as spec §3 says; the
  descriptor's own one-line description is left as it is, so the control
  catalogue golden changes by additions only.
- **R124 — `SEQ_START` resets each interlock one step before it commands the
  device (owner's ruling on R126).** With the reset dropping the command, the old
  step "reset `INT_CVn` and write `CVn.Start` true" would have its start
  overwritten by the reset's `false` 0.1 s later. The six steps become nine:
  1 "Reset the safety relays" (unchanged, after 1 s); 2 "Reset CV003's
  interlock" (safety resets released, `INT_CV003.Reset` true; when
  `INT_CV003.Ok == true`, timeout 5 s); 3 "Start CV003" (`INT_CV003.Reset` false,
  `CV003.Start` true; when `CV003.Speed >= 1.74`, timeout 15 s); 4/5 the same for
  CV002; 6/7 for CV001; 8 "Reset the feed's interlock" (`INT_FEED.Reset` true;
  when `INT_FEED.Ok == true`, 5 s); 9 "Start the feed" (`INT_FEED.Reset` false,
  `Feed.Enabled` true; after 1 s). A reset step waits for `Ok` rather than a
  fixed delay, so it ends on the first sequencer scan after the reset was
  accepted, and its timeout faults the sequence (abort writes) if an interlock
  refuses. Measured: steps enter at 2.200, 2.400, 4.800, 5.000, 7.400, 7.600,
  10.000, 10.200 s, complete at 11.200 s (was 11.400); contactors close at
  CV003 2.410 s (was 2.210), CV002 5.010 s (4.610), CV001 7.610 s (7.010); ore
  moves from 10.210 s (9.410). **Speed-proving margin restored:** each belt's
  first published speed ≥ 1.74 m/s is at the end of 4.670 s (CV003), 7.280 s
  (CV002) and 9.900 s (CV001), and the proving scans at 4.800, 7.400 and
  10.000 s read them with 120, 110 and 90 ms to spare — about the 6a figure. (The
  earlier draft of this plan, with the reset and the start in one step, had one
  tick of slack for CV003 and none for CV002 and CV001.) Each proving step takes
  2.4 s of its 15 s timeout; each reset step 0.2 s of its 5 s. No threshold
  change is needed.
- **R125 — Scenario 9's shape.** Duration 150 s (the spec gives none). It proves
  both halves of the inhibit and a fresh start: (a) 80 s pull-key on CV002 (the
  cascade trips `INT_CV001`, `INT_FEED`); 100 s `CV001.Start` and `Feed.Enabled`
  written true — refused. (b) The line is put right by hand, as the plant
  permits: 105 s pull-key released; 106/107 s `CV002.SafetyReset` pulse; 108/109 s
  `INT_CV002.Reset` pulse (its reset drops `CV002.Start`); 110 s `CV002.Start`
  true (CV002 must run before `INT_CV001` will reset); 115/116 s
  `INT_CV001.Reset` pulse — CV001 gets its permit back and its start is dropped;
  it stays stopped. (c) 125 s `CV001.Start` true — a fresh start, and CV001
  runs. 130/131 s `INT_FEED.Reset` pulse (it needs CV001 running) — the permit
  returns, `Feed.Enabled` is dropped, the feeder stays off to the end. The last
  event is at 131.000 s: 19 s quiet. State check, sampled every 100 ms:
  `CV001.Speed` ≤ 0.02 m/s and `CV001.TonnesPerHour` ≤ 5 t/h from 100 to 125 s
  (measured maxima at the test's 100 ms sampling 0.0060 m/s and 0.0485 t/h;
  0.056 t/h at 10 ms sampling); `Feed.HopperMass` exactly 0 kg from
  100 s to the end (measured 0; a running feeder holds 0.8 kg in the hopper while
  CV001 draws it); `CV001.Speed` ≥ 1.74 m/s at 150 s (measured 1.932). Measured
  on the plant *without* the permit writes, with a timeline that stops at the
  100 s writes (no restoration of CV002): the writes close CV001's contactor at
  100.000 s, its scale reaches 287.4 t/h, and CH1 reports `FULL` at 114.26 s.
  With scenario 9's full timeline CV002 restarts at 110 s, so that figure is
  what the line would have done *left alone*; the README says so. The first 99
  lines of the golden are the pull-key golden.
- **R126 — The reset drops the command (owner's ruling; spec error corrected).**
  Spec §1 said "The trip still drops `Start` / `Enabled`, so a reset alone never
  restarts a device". The first draft of this plan measured that false:
  `CVn.Start` and `Feed.Enabled` are held values, so a start written during the
  trip was refused but kept, and the reset released it (CV001 closed at
  115.110 s on the permit write). The owner ruled that the interlock's reset
  writes also drop the command, as a seal-in circuit forgets a start pressed
  while the interlock was open: `INT_CVn` resets write `CVn.Permit` true and
  `CVn.Start` false; `INT_FEED` writes `Feed.Permit` true and `Feed.Enabled`
  false. A device runs again only on a fresh command after the reset (scenario 9,
  measured). This is sample data, not block behaviour: the block sends whatever
  reset writes it is given, and `docs/control-blocks.md` documents the pattern
  (both writes, and the two-step reset-then-start a sequence needs).
- **R127 — Tag-shape tests move by additions, like the goldens.** Criterion 6
  allows an existing test to change "only where criterion 5 allows", and criterion
  5 names goldens. Four new tags move eight exact-shape assertions that no golden
  covers: `ComponentTagTests.StarterPublishesCommandsAndStates` and
  `BulkSourceAndSinkPublishFeedAndTotals` (+1 row each),
  `ConveyorIoTests.TheConveyorPublishesExactlyItsFace` (+`CV001.Permit`),
  `WorkedExampleTests.TheBlocksOwnTagsJoinThePlantsDirectory` and
  `TheJsonWorkedExampleHasTheSameBlocksAndDirectory` (47 → 49, "25 plant tags" →
  27), `ValidateCommandTests.AControlledPlantCountsItsControllers` and
  `TheJsonSummaryCountsControllersAfterExplicitTags` (47 → 49), and
  `TagsCommandTests.AControlledPlantListsItsBlocksTags` (47 → 49). Ruled: each
  changes by the added tags only; the event logs those plants write are unchanged.
  `MineConveyorTests.TheScenarioFolderHoldsExactlyTheEightScenariosEachWithAGoldenAndAStory`
  is renamed `…TheNamedScenarios…` (its body is unchanged; "eight" becomes false).
- **R128 — Where the new ports and tags sit.** `Permit` follows `Reset` on the
  starter (port and tag), `Start` on the conveyor, `Enabled` on the source, in
  both the descriptor and `DescribeTags`. Descriptions: starter port "Run permit
  from an interlock; false holds the contactor open. Defaults to true when
  unwired."; conveyor port "Run permit to the starter; false holds the contactor
  open. Defaults to true."; source port "Run permit from an interlock; false stops
  the feeder whatever Enabled says. Defaults to true."; tag bindings "Run permit;
  false holds the contactor open" / "Run permit; false stops the feeder". The
  descriptors' one-line descriptions are left unchanged (a changed line would not
  be an addition); the `MotorStarter` class summary gains the permit. Measured:
  `components-catalogue.json` +33 lines in six hunks, nothing removed.
- **R129 — What else moves in the sample goldens, measured.** The start-up
  (lines 1–78, identical in all nine goldens; was 63) gains the eight trip
  `Permit … Set to false by …` lines at 0.010 s, the four reset steps with their
  `INTERLOCK_RESET` and two reset writes each, and moves every start-up line from
  step 3 on (R124); `SEQUENCE_COMPLETE` reads "Finished after 9 steps." at
  11.200 s. After 80 s each golden gains one `Permit … Set to false by …` line per
  interlock trip, directly after that interlock's `Start`/`Enabled` write; every
  other event after 80 s keeps its time and text, except in two goldens whose
  values change because the belts and the feed started later: **overload**,
  `OVERLOAD_TRIP` "Thermal state 1.2884784008371077" → "1.285658707267615" (still on
  the injection tick; 6a R109 holds); **chute-blockage**, `Hi` 7.705095441377381 →
  7.705084760820622, `HiHi` 8.806547735328072 → 8.806537054771315 (same ticks),
  `OVERLOAD_TRIP` 3:09.700 → 3:09.770 with "1.1000357303409216", CV001
  `CONTACTOR_OPENED` 09.770, `DE_ENERGISED` 09.780, `ZERO_SPEED` 17.490 → 17.560,
  `STOPPED` 17.570 → 17.640; the interlock trips stay at 3:09.800 (6a R110's
  "+109.70 s" becomes +109.77 s). Feed-starve's settle times are unchanged
  (104.2, 133.7, 153.7 s); CV003 reads 263.1 t/h at 80 s (was 267.7) and first
  reaches 282 t/h at 85.9 s; welded-contactor's minimum CV003 speed from 80 s is
  1.910 m/s. Line counts: normal-start-stop 94 → 112, pull-key 81 → 99, e-stop
  75 → 92, overload 85 → 104, chute-blockage 78 → 95, failed-zero-speed 73 → 90,
  welded-contactor 99 → 117, feed-starve 64 → 79; start-while-tripped 132. After
  80 s every existing chain and absence holds unchanged; `normal-start-stop`'s
  start-up chain is rewritten for the nine steps, and Task 3 re-pins all eight
  with the `Permit` lines.
- **R130 — A duplicate reset tag is `DSE111` at the controller.** The loader
  wraps the constructor's `ArgumentException` as `DSE111 $.controllers[0]`:
  "'INT01' (interlock) rejected its parameters: Tag 'FEED.Permit' is commanded
  twice on reset. Command each tag once in each list." A corpus fixture pins it;
  the schema accepts it (a semantic error).
- **R131 — Documentation scope.** `docs/authoring-a-component.md` does not list
  the starter's ports (its `Permit = AddInput<bool>("Permit", defaultValue: true)`
  line is a generic example, and now matches the shipped pattern): unchanged, as
  spec §6 allows. `docs/control-blocks.md` gains the reset writes and a "run
  permit" subsection; the root `README.md` and the sample README count nine
  scenarios.
- **R132 — Spec amendments.** The 6c spec already carries its "Amended
  2026-09-25 by the plan" note (applied with this plan, before execution; no task
  edits it). Task 5 adds a dated amendment line to the 6a and 5c specs.

## Measurements

Scratch runs on `aa3c403` plus this plan's whole change (a copy of the
repository under `.superpowers/sdd/6c/scratch/`, deleted after).

**Suite:** 1348 tests (1346 measured in scratch, plus the two R123 validation facts added after an independent check), all passing; Release build `0 Warning(s)`, `0 Error(s)`.
Per project: 37 / 461 / 132 Components / 56 / 211 Configuration / 163 / 76 / 117
Control / 23 Control.Catalogue / 72 Samples.

**Criterion 5:** `DSE_UPDATE_GOLDEN=1 dotnet test Dse.sln` over the whole
solution (scratch only — never in the repository), run on the finished change,
rewrote nothing: every golden already matched. Against `aa3c403` the only
goldens changed outside `samples/mine-conveyors/` are
`components-catalogue.json` (+33 −0), `control-catalogue.json` (+23 −0) and
`plant.schema.json` (+7 −0). The four `Dse.Scenarios.Tests` goldens,
`conveyor-control.log` (5c) and the CLI's `dse run --expect` of the 5c scenario
are byte-identical. With Tasks 1–2 alone, the sample goldens are byte-identical
too and all 66 sample tests pass (1341 in all).

**Start-up, per belt** (R124):

| | CV003 | CV002 | CV001 | Feed |
|---|---|---|---|---|
| reset step entered / `INTERLOCK_RESET` | 2.200 / 2.300 s | 4.800 / 4.900 s | 7.400 / 7.500 s | 10.000 / 10.100 s |
| reset writes land (command false, permit true) | 2.310 s | 4.910 s | 7.510 s | 10.110 s |
| start step entered / command lands | 2.400 / 2.410 s | 5.000 / 5.010 s | 7.600 / 7.610 s | 10.200 / 10.210 s |
| contactor closes (ore moves, for the feed): 6a → now | 2.210 → 2.410 s | 4.610 → 5.010 s | 7.010 → 7.610 s | 9.410 → 10.210 s |
| first published speed ≥ 1.74 m/s | end of 4.670 s | end of 7.280 s | end of 9.900 s | — |
| proving scan / slack / timeout | 4.800 s / 120 ms / 15 s | 7.400 s / 110 ms / 15 s | 10.000 s / 90 ms / 15 s | — |

**Scenario 9** (`start-while-tripped`), the lines after the pull-key's cascade
(lines 100–132 of 132; lines 1–99 are the pull-key golden):

```
06:01:40.000  CV001.Start  WRITE  Set to true.
06:01:40.000  Feed.Enabled  WRITE  Set to true.
06:01:45.000  CV002.PullKey1  PULLKEY_RESET  Released.
06:01:46.000  CV002.Safety  SAFETY_RESET  All channels healthy; relay energised.
06:01:48.100  INT_CV002  INTERLOCK_RESET  Reset with all conditions normal.
06:01:48.110  CV002.Start  WRITE  Set to false by INT_CV002.
06:01:48.110  CV002.Permit  WRITE  Set to true by INT_CV002.
06:01:50.000  CV002.Start  WRITE  Set to true.
06:01:50.000  CV002.Starter  CONTACTOR_CLOSED  Motor energised.
06:01:55.100  INT_CV001  INTERLOCK_RESET  Reset with all conditions normal.
06:01:55.110  CV001.Start  WRITE  Set to false by INT_CV001.
06:01:55.110  CV001.Permit  WRITE  Set to true by INT_CV001.
06:02:05.000  CV001.Start  WRITE  Set to true.
06:02:05.000  CV001.Starter  CONTACTOR_CLOSED  Motor energised.
06:02:10.100  INT_FEED  INTERLOCK_RESET  Reset with all conditions normal.
06:02:10.110  Feed.Enabled  WRITE  Set to false by INT_FEED.
06:02:10.110  Feed.Permit  WRITE  Set to true by INT_FEED.
06:02:11.000  INT_FEED.Reset  WRITE  Set to false.
```

(Abridged: the operator's own `WRITE` lines for the pulses, `PERMISSIVE_OK`,
`ENERGISED`, `MOTION` and `AT_SPEED` lines are omitted here; the golden has them.)

## File structure

```
src/Dse.Components/Mechanical/MotorStarter.cs      + Permit input, tag, AND in closed (Task 1)
src/Dse.Components/Conveyors/Conveyor.cs           + Permit port, tag, Expose (Task 1)
src/Dse.Components/Flow/BulkSource.cs              + Permit input, tag, AND in Advance (Task 1)
src/Dse.Control/Interlock.cs                       + reset writes, five-argument constructor (Task 2)
src/Dse.Control.Catalogue/InterlockCatalogue.cs    + reset parameter (Task 2)
tests/Dse.Components.Tests/MotorStarterTests.cs    + 2 facts; Build takes an optional permit (Task 1)
tests/Dse.Components.Tests/ConveyorIoTests.cs      + 1 fact; face +CV001.Permit (Task 1)
tests/Dse.Components.Tests/SourceSinkTests.cs      + 1 fact (Task 1)
tests/Dse.Components.Tests/ComponentTagTests.cs    + 2 rows (Task 1)
tests/Dse.Components.Tests/Catalogue/Golden/components-catalogue.json   regenerated (Task 1)
tests/Dse.Control.Tests/WorkedExampleTests.cs      47 → 49 (Task 1)
tests/Dse.Cli.Tests/ValidateCommandTests.cs        47 → 49 (Task 1)
tests/Dse.Cli.Tests/TagsCommandTests.cs            47 → 49 (Task 1)
tests/Dse.Control.Tests/InterlockTests.cs          + 10 facts (Task 2)
tests/Dse.Control.Tests/HostTests.cs               + 1 fact (Task 2)
tests/Dse.Control.Catalogue.Tests/BlockFactoryTests.cs            + 1 fact (Task 2)
tests/Dse.Control.Catalogue.Tests/Golden/control-catalogue.json   regenerated (Task 2)
tests/Dse.Configuration.Tests/Golden/plant.schema.json            regenerated (Task 2)
tests/Dse.Configuration.Tests/Plants/invalid/DSE111-reset-write-twice.json   new (Task 2)
samples/mine-conveyors/plant.json                  interlocks gain permit/reset writes; SEQ_START in nine steps (Task 3)
samples/mine-conveyors/expected/*.log              eight regenerated (Task 3), one new (Task 4)
samples/mine-conveyors/scenarios/start-while-tripped.json   new (Task 4)
samples/mine-conveyors/README.md                   quotes (Task 3), section 9 (Task 4), prose (Task 5)
tests/Dse.Samples.Tests/Stories.cs                 re-pinned (Task 3), scenario 9 (Task 4)
tests/Dse.Samples.Tests/Sample.cs                  + start-while-tripped (Task 4)
tests/Dse.Samples.Tests/MineConveyorTests.cs       + 1 fact, one rename (Task 4)
tests/Dse.Control.Tests/DocumentationTests.cs      + 1 fact (Task 5)
docs/control-blocks.md                             reset writes, run permit (Task 5)
README.md                                          nine scenarios (Task 5)
docs/superpowers/specs/2026-09-25-mine-conveyor-sample-design.md   amendment line (Task 5)
docs/superpowers/specs/2026-09-22-control-blocks-design.md         amendment line (Task 5)
```

## Task map

| # | Task | Model | Tests after |
|---|---|---|---|
| 1 | `Permit` on `motor-starter`, `conveyor`, `bulk-source`; components golden; tag counts | opus | 1327 |
| 2 | Interlock reset writes; catalogue `reset`; control-catalogue and schema goldens; DSE111 fixture | opus | 1341 |
| 3 | The sample's interlocks write the permit and drop the command on reset; `SEQ_START` in nine steps; eight goldens regenerated; stories re-pinned | opus | 1341 |
| 4 | Scenario 9, `start-while-tripped`, end to end | opus | 1347 |
| 5 | README prose, `docs/control-blocks.md`, root README, 6a/5c amendment lines | sonnet | 1348 |

Tasks are sequential. Reviewers use the larger model for Tasks 1–4 (each changes
a shipped component or block, or a golden that must be read against its story).

---

### Task 1: A run permit on the starter, the conveyor and the feeder

**Model:** opus.

**Files:**
- Modify: `src/Dse.Components/Mechanical/MotorStarter.cs` (summary, descriptor ports and tags, constructor, property, `DescribeTags`, `Evaluate`)
- Modify: `src/Dse.Components/Conveyors/Conveyor.cs` (descriptor ports and tags, one `Expose`)
- Modify: `src/Dse.Components/Flow/BulkSource.cs` (descriptor ports and tags, constructor, property, `DescribeTags`, `Advance`)
- Test: `tests/Dse.Components.Tests/MotorStarterTests.cs` (+2 facts; `Build` gains an optional permit)
- Test: `tests/Dse.Components.Tests/ConveyorIoTests.cs` (+1 fact; one face row)
- Test: `tests/Dse.Components.Tests/SourceSinkTests.cs` (+1 fact)
- Test: `tests/Dse.Components.Tests/ComponentTagTests.cs` (+2 rows, R127)
- Test: `tests/Dse.Control.Tests/WorkedExampleTests.cs`, `tests/Dse.Cli.Tests/ValidateCommandTests.cs`, `tests/Dse.Cli.Tests/TagsCommandTests.cs` (47 → 49, R127)
- Regenerate: `tests/Dse.Components.Tests/Catalogue/Golden/components-catalogue.json`

**Interfaces:**
- Consumes: `AddInput<bool>(string name, bool defaultValue)`,
  `TagBinding.Write(string name, InputPort<bool> port, string description = "")`,
  `protected void Expose(string alias, Port port)` on `CompositeComponent` (existing).
- Produces: `public InputPort<bool> Permit { get; }` on `MotorStarter` and on
  `BulkSource`; the tags `<starter>.Permit`, `CVn.Permit`, `<source>.Permit`
  (Bool, read-write, default true) that Task 3's interlocks write.

- [ ] **Step 1: Write the failing tests and move the tag-shape assertions**

In `tests/Dse.Components.Tests/MotorStarterTests.cs`, replace the `Build` helper

```csharp
    private static Rig Build()
    {
        var starter = new MotorStarter("K1");
        var command = new Switch("Cmd");
        var safety = new Switch("Safe", true);
        var thermal = new Setpoint("Theta", 0.5);
        var reset = new Switch("Reset");
        command.Out.ConnectTo(starter.Command);
        safety.Out.ConnectTo(starter.SafetyOk);
        thermal.Out.ConnectTo(starter.ThermalState);
        reset.Out.ConnectTo(starter.Reset);
        Simulation sim = new SimulationBuilder(Options()).Add(starter).Add(command).Add(safety).Add(thermal).Add(reset).Build();
        return new Rig(sim, starter, command, safety, thermal, reset);
    }
```

with

```csharp
    /// <summary>The starter and its drivers; the permit is wired only when a test passes one, so it reads its default otherwise.</summary>
    private static Rig Build(Switch? permit = null)
    {
        var starter = new MotorStarter("K1");
        var command = new Switch("Cmd");
        var safety = new Switch("Safe", true);
        var thermal = new Setpoint("Theta", 0.5);
        var reset = new Switch("Reset");
        command.Out.ConnectTo(starter.Command);
        safety.Out.ConnectTo(starter.SafetyOk);
        thermal.Out.ConnectTo(starter.ThermalState);
        reset.Out.ConnectTo(starter.Reset);
        SimulationBuilder builder = new SimulationBuilder(Options()).Add(starter).Add(command).Add(safety).Add(thermal).Add(reset);
        if (permit is not null)
        {
            permit.Out.ConnectTo(starter.Permit);
            builder.Add(permit);
        }

        return new Rig(builder.Build(), starter, command, safety, thermal, reset);
    }
```

and insert before `    [Fact]\n    public void RejectsAResetLevelAboveTheTripLevel()`:

```csharp
    [Fact]
    public void AFalsePermitHoldsTheContactorOpenWhateverTheCommand()
    {
        var permit = new Switch("Permit");
        Rig rig = Build(permit);
        rig.Command.Value = true;

        rig.Sim.RunFor(TimeSpan.FromSeconds(1));
        Assert.False(rig.Starter.Contactor.Value);
        Assert.Empty(rig.Sim.Events.Records);           // a refused command logs nothing

        permit.Value = true;
        rig.Sim.Tick();
        Assert.True(rig.Starter.Contactor.Value);

        permit.Value = false;                            // an interlock contact in series: opening it drops a running starter
        rig.Sim.Tick();
        Assert.False(rig.Starter.Contactor.Value);
        Assert.Equal(["CONTACTOR_CLOSED", "CONTACTOR_OPENED"], rig.Sim.Events.Records.Select(r => r.Code));
    }

    [Fact]
    public void AWeldedContactorStaysClosedWithoutAPermit()
    {
        var permit = new Switch("Permit");
        Rig rig = Build(permit);
        rig.Sim.InjectFaultIn(TimeSpan.Zero, "K1", MotorStarter.ContactorWelded);

        rig.Sim.RunFor(TimeSpan.FromSeconds(1));

        Assert.False(rig.Starter.Permit.Value);
        Assert.True(rig.Starter.Contactor.Value);
    }

```

In `tests/Dse.Components.Tests/ConveyorIoTests.cs`, in
`TheConveyorPublishesExactlyItsFace`, replace

```csharp
                "CV001.Contactor", "CV001.Current", "CV001.EStop", "CV001.EStop.Ok",
```

with

```csharp
                "CV001.Contactor", "CV001.Current", "CV001.EStop", "CV001.EStop.Ok", "CV001.Permit",
```

and insert before `    [Fact]\n    public void AFailedSpeedSensorReadsBadOnTheWire()`:

```csharp
    [Fact]
    public void APermitWrittenFalseHoldsTheBeltStoppedUntilItIsWrittenTrue()
    {
        Simulation sim = Build();
        sim.IO.WriteBool("CV001.Permit", false);
        StartUp(sim);
        sim.RunFor(TimeSpan.FromSeconds(10));

        Assert.True(sim.IO.ReadBool("CV001.Start"));
        Assert.False(sim.IO.ReadBool("CV001.Contactor"));
        Assert.True(sim.IO.ReadBool("CV001.Stopped"));
        Assert.DoesNotContain(sim.Events.Records, r => r.Source == "CV001.Starter" && r.Code == "CONTACTOR_CLOSED");

        sim.IO.WriteBool("CV001.Permit", true);
        sim.RunFor(TimeSpan.FromSeconds(10));

        Assert.True(sim.IO.ReadBool("CV001.Contactor"));
        Assert.InRange(sim.IO.ReadDouble("CV001.Speed"), 1.75, 1.95);
    }

```

In `tests/Dse.Components.Tests/SourceSinkTests.cs`, insert before
`    [Fact]\n    public void ASinkWithCapacityFillsOnceAndSaysSo()`:

```csharp
    [Fact]
    public void ASourceWithoutItsPermitCreatesNothingWhateverEnabledSays()
    {
        var source = new BulkSource("Feed", Ore, rateKgPerSecond: 2.0);
        var sink = new BulkSink("Pile");
        source.Out.ConnectTo(sink.In);
        Simulation sim = new SimulationBuilder(Options()).Add(sink).Add(source).Build();
        sim.WriteIn(TimeSpan.Zero, "Feed.Permit", TagValue.Bool(false));

        sim.RunFor(TimeSpan.FromSeconds(2));
        Assert.True(sim.IO.Read("Feed.Enabled").AsBool);
        Assert.Equal(0.0, sim.MassBalance.Created, 9);

        sim.WriteIn(TimeSpan.Zero, "Feed.Permit", TagValue.Bool(true));
        sim.RunFor(TimeSpan.FromSeconds(1));
        Assert.Equal(2.0, sim.MassBalance.Created, 9);
    }

```

In `tests/Dse.Components.Tests/ComponentTagTests.cs`, in
`StarterPublishesCommandsAndStates`, replace

```csharp
                ("Reset", TagKind.Bool, TagAccess.ReadWrite),
                ("Contactor", TagKind.Bool, TagAccess.ReadOnly),
```

with

```csharp
                ("Reset", TagKind.Bool, TagAccess.ReadWrite),
                ("Permit", TagKind.Bool, TagAccess.ReadWrite),
                ("Contactor", TagKind.Bool, TagAccess.ReadOnly),
```

and in `BulkSourceAndSinkPublishFeedAndTotals` replace

```csharp
                ("Enabled", TagKind.Bool, TagAccess.ReadWrite),
                ("Rate", TagKind.Double, TagAccess.ReadWrite),
```

with

```csharp
                ("Enabled", TagKind.Bool, TagAccess.ReadWrite),
                ("Permit", TagKind.Bool, TagAccess.ReadWrite),
                ("Rate", TagKind.Double, TagAccess.ReadWrite),
```

In `tests/Dse.Control.Tests/WorkedExampleTests.cs` make three replacements:
`        // 25 plant tags (dse tags conveyor-line.json) plus 22 owned ones.` →
`        // 27 plant tags (dse tags conveyor-line.json) plus 22 owned ones.`;
`        Assert.Equal(47, sim.IO.Directory.Count);` →
`        Assert.Equal(49, sim.IO.Directory.Count);`;
`        Assert.Equal(47, fromJson.IO.Directory.Count);` →
`        Assert.Equal(49, fromJson.IO.Directory.Count);`.

In `tests/Dse.Cli.Tests/ValidateCommandTests.cs` replace
`        Assert.Matches(@"  tags          47 \(0 explicit\)\n  controllers   4\n  time step     10 ms\n", run.Out);`
with
`        Assert.Matches(@"  tags          49 \(0 explicit\)\n  controllers   4\n  time step     10 ms\n", run.Out);`
and `        Assert.Equal(47, summary.GetProperty("tags").GetInt32());` with
`        Assert.Equal(49, summary.GetProperty("tags").GetInt32());`.

In `tests/Dse.Cli.Tests/TagsCommandTests.cs` replace
`        Assert.Equal(47, run.Out.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);`
with
`        Assert.Equal(49, run.Out.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);`.

- [ ] **Step 2: Run the component tests to see them fail**

Run: `dotnet test tests/Dse.Components.Tests --nologo`
Expected: the build fails with `error CS1061: 'MotorStarter' does not contain a definition for 'Permit'` (and the same for `BulkSource` if the compiler reaches it).

- [ ] **Step 3: Add the permit to the starter**

In `src/Dse.Components/Mechanical/MotorStarter.cs` make these replacements.

The class summary's second sentence,

```csharp
/// A direct-on-line starter: a contactor and a thermal overload relay. The
/// contactor closes on command when the safety circuit allows and the relay
/// is not tripped. The relay trips when the motor's thermal state crosses
```

becomes

```csharp
/// A direct-on-line starter: a contactor and a thermal overload relay. The
/// contactor closes on command when the safety circuit allows, the run permit
/// is given and the relay is not tripped. The relay trips when the motor's thermal state crosses
```

The ports — after
`            PortSpec.In<bool>("Reset", description: "Overload reset, rising edge."),`
add

```csharp
            PortSpec.In<bool>("Permit", description: "Run permit from an interlock; false holds the contactor open. Defaults to true when unwired."),
```

The tags — after `            new TagEntry("Reset", TagKind.Bool, TagAccess.ReadWrite),` add

```csharp
            new TagEntry("Permit", TagKind.Bool, TagAccess.ReadWrite),
```

The constructor — after `        Reset = AddInput<bool>("Reset");` add

```csharp
        Permit = AddInput<bool>("Permit", defaultValue: true);
```

The property — after `    public InputPort<bool> Reset { get; }` add a blank line and

```csharp
    /// <summary>Run permit, an interlock contact in series with the run command. False holds the contactor open. Unconnected reads true.</summary>
    public InputPort<bool> Permit { get; }
```

The binding — after `        TagBinding.Write("Reset", Reset, "Overload reset, rising edge"),` add

```csharp
        TagBinding.Write("Permit", Permit, "Run permit; false holds the contactor open"),
```

The run logic,

```csharp
        bool closed = _welded || (Command.Value && SafetyOk.Value && !_tripped && !_open);
```

becomes

```csharp
        bool closed = _welded || (Command.Value && SafetyOk.Value && Permit.Value && !_tripped && !_open);
```

- [ ] **Step 4: Expose it on the conveyor**

In `src/Dse.Components/Conveyors/Conveyor.cs`: after
`            PortSpec.In<bool>("Start", description: "Run command to the starter."),` add

```csharp
            PortSpec.In<bool>("Permit", description: "Run permit to the starter; false holds the contactor open. Defaults to true."),
```

after `            new TagEntry("Start", TagKind.Bool, TagAccess.ReadWrite),` add

```csharp
            new TagEntry("Permit", TagKind.Bool, TagAccess.ReadWrite),
```

and after `        Expose("Start", Starter.Command);` add

```csharp
        Expose("Permit", Starter.Permit);
```

- [ ] **Step 5: Add the permit to the feeder**

In `src/Dse.Components/Flow/BulkSource.cs`: after
`            PortSpec.In<bool>("Enabled", description: "Defaults to the enabled parameter."),` add

```csharp
            PortSpec.In<bool>("Permit", description: "Run permit from an interlock; false stops the feeder whatever Enabled says. Defaults to true."),
```

after `            new TagEntry("Enabled", TagKind.Bool, TagAccess.ReadWrite),` add

```csharp
            new TagEntry("Permit", TagKind.Bool, TagAccess.ReadWrite),
```

after `        Enabled = AddInput<bool>("Enabled", defaultValue: enabled);` add

```csharp
        Permit = AddInput<bool>("Permit", defaultValue: true);
```

after `    public InputPort<bool> Enabled { get; }` add a blank line and

```csharp
    /// <summary>Run permit, an interlock contact in series with <see cref="Enabled"/>. False stops the feeder. Unconnected reads true.</summary>
    public InputPort<bool> Permit { get; }
```

after `        TagBinding.Write("Enabled", Enabled, "Feeder enabled"),` add

```csharp
        TagBinding.Write("Permit", Permit, "Run permit; false stops the feeder"),
```

and in `Advance`,

```csharp
        if (_starved || !Enabled.Value)
```

becomes

```csharp
        if (_starved || !Enabled.Value || !Permit.Value)
```

- [ ] **Step 6: Run the component tests**

Run: `dotnet test tests/Dse.Components.Tests --nologo`
Expected: 131 passed, 1 failed —
`ComponentsExportTests.TheShippedCatalogueExportsExactlyTheGoldenFile` (the
catalogue golden lists ports and tags). The four new facts and the two moved
shape tests pass, and so does the conformance sweep (descriptor and built ports
agree).

- [ ] **Step 7: Regenerate the components golden and read its diff**

Run: `DSE_UPDATE_GOLDEN=1 dotnet test tests/Dse.Components.Tests --nologo --filter "FullyQualifiedName~TheShippedCatalogueExportsExactlyTheGoldenFile"`
Then read `git diff tests/Dse.Components.Tests/Catalogue/Golden/components-catalogue.json`
in full. Checklist (measured): **+33 lines, 0 removed, six hunks**, each adding
one object and nothing else —

- `bulk-source` ports: a `Permit` port (`"direction": "in"`, `"valueType": "bool"`,
  the source description of Step 5) directly after the `Enabled` port;
- `bulk-source` tags: `{ "name": "Permit", "kind": "bool", "access": "readWrite" }`
  directly after `Enabled`;
- `conveyor` ports: a `Permit` port with the conveyor description of Step 4
  directly after `Start`;
- `conveyor` tags: `Permit` directly after `Start`;
- `motor-starter` ports: a `Permit` port with the starter description of Step 3
  directly after `Reset`;
- `motor-starter` tags: `Permit` directly after `Reset`.

No `description` of any component changes. `plant.schema.json` does **not**
change in this task (the schema lists parameters, not ports).

- [ ] **Step 8: Build and test the solution**

Run: `dotnet build Dse.sln -c Release --nologo` — expect `0 Warning(s)`, `0 Error(s)`.
Run: `dotnet test Dse.sln --nologo` — expect **1327** (Components 132; every
other project as at `aa3c403`). The sample goldens, the scenario goldens and
`conveyor-control.log` are untouched: `git status --short` lists only the eleven
files of this task.

- [ ] **Step 9: Commit**

```bash
git add src/Dse.Components/Mechanical/MotorStarter.cs src/Dse.Components/Conveyors/Conveyor.cs src/Dse.Components/Flow/BulkSource.cs tests/Dse.Components.Tests/MotorStarterTests.cs tests/Dse.Components.Tests/ConveyorIoTests.cs tests/Dse.Components.Tests/SourceSinkTests.cs tests/Dse.Components.Tests/ComponentTagTests.cs tests/Dse.Components.Tests/Catalogue/Golden/components-catalogue.json tests/Dse.Control.Tests/WorkedExampleTests.cs tests/Dse.Cli.Tests/ValidateCommandTests.cs tests/Dse.Cli.Tests/TagsCommandTests.cs
```

```bash
git commit -m "feat(components): give the starter, the conveyor and the feeder a run permit" -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>"
```

---

### Task 2: Interlock reset writes

**Model:** opus.

**Files:**
- Modify: `src/Dse.Control/Interlock.cs` (whole file below)
- Modify: `src/Dse.Control.Catalogue/InterlockCatalogue.cs` (factory, one parameter)
- Test: `tests/Dse.Control.Tests/InterlockTests.cs` (+2 helpers, +10 facts)
- Test: `tests/Dse.Control.Tests/HostTests.cs` (+1 fact)
- Test: `tests/Dse.Control.Catalogue.Tests/BlockFactoryTests.cs` (+1 fact)
- Create: `tests/Dse.Configuration.Tests/Plants/invalid/DSE111-reset-write-twice.json` (+2 theory rows)
- Regenerate: `tests/Dse.Control.Catalogue.Tests/Golden/control-catalogue.json`, `tests/Dse.Configuration.Tests/Golden/plant.schema.json`

**Interfaces:**
- Consumes: `BlockWrite(string Tag, TagValue Value)`, `Condition(string Tag, bool Normal)`,
  `ScanOutputs.Write(int index, TagValue value)`, `ControlCatalogue.Writes(ParameterValues p, string name)`,
  `ControlCatalogue.WriteGroup` (existing); Task 1's `Permit` tags (in the fixture).
- Produces: `Interlock(string id, IReadOnlyList<Condition> conditions, IReadOnlyList<BlockWrite> tripWrites, TimeSpan scanPeriod, IReadOnlyList<BlockWrite> resetWrites)`
  (the four-argument constructor unchanged); the `interlock` plant-file parameter
  `reset` (a list of `{ "tag", "value" }`, optional) that Task 3's plant uses.

- [ ] **Step 1: Write the failing pure tests**

In `tests/Dse.Control.Tests/InterlockTests.cs`, after the `Healthy()` helper
(ending `        return scan;\n    }`), insert

```csharp

    /// <summary>The run-permit pattern: drop Start and Permit on trip, give Permit back on reset.</summary>
    private static Interlock MakeWithPermit() => new(
        "INT01",
        [new Condition("CV001.Tripped", false), new Condition("PERM01.Ok", true)],
        [new BlockWrite("CV001.Start", TagValue.Bool(false)), new BlockWrite("CV001.Permit", TagValue.Bool(false))],
        Period,
        [new BlockWrite("CV001.Permit", TagValue.Bool(true))]);

    private static Scan HealthyWithPermit()
    {
        var scan = new Scan(MakeWithPermit());
        scan.Set("CV001.Tripped", false).Set("PERM01.Ok", true);
        return scan;
    }
```

and before the class's closing brace append

```csharp

    [Fact]
    public void ATagInBothListsIsOneWritePinAfterTheTripPins()
    {
        Interlock interlock = MakeWithPermit();

        Assert.Equal(
            new[] { new TagRef("CV001.Start", TagKind.Bool), new TagRef("CV001.Permit", TagKind.Bool) },
            interlock.Writes);
    }

    [Fact]
    public void AResetOnlyTagGetsItsOwnPinAfterTheTripPins()
    {
        var interlock = new Interlock(
            "INT01",
            [new Condition("CV001.Tripped", false)],
            [new BlockWrite("CV001.Start", TagValue.Bool(false))],
            Period,
            [new BlockWrite("HORN.Silence", TagValue.Bool(true))]);

        Assert.Equal(["CV001.Start", "HORN.Silence"], interlock.Writes.Select(w => w.Name));
    }

    [Fact]
    public void WithoutResetWritesThePinsAreExactlyTheTripWrites()
    {
        var four = new Interlock("INT01", [new Condition("CV001.Tripped", false)], [new BlockWrite("CV001.Start", TagValue.Bool(false))], Period);
        var five = new Interlock("INT01", [new Condition("CV001.Tripped", false)], [new BlockWrite("CV001.Start", TagValue.Bool(false))], Period, []);

        Assert.Equal(four.Writes, five.Writes);
        Assert.Equal(new TagRef("CV001.Start", TagKind.Bool), Assert.Single(five.Writes));
    }

    [Fact]
    public void TheTripScanSendsTheTripWritesAndNoResetWrite()
    {
        Scan scan = HealthyWithPermit().Once();
        Assert.Empty(scan.LastWrites);

        scan.Set("CV001.Tripped", true).Once();

        Assert.Equal(
            new[] { ("CV001.Start", false), ("CV001.Permit", false) },
            scan.LastWrites.Select(w => (w.Key, w.Value.AsBool)));
    }

    [Fact]
    public void TheResetWritesGoOutOnTheAcceptedResetScanOnly()
    {
        Scan scan = HealthyWithPermit().Once();
        scan.Set("CV001.Tripped", true).Once();
        scan.Set("CV001.Tripped", false).Once();
        Assert.Empty(scan.LastWrites);

        scan.Command("Reset", true).Once();
        Assert.Equal("INTERLOCK_RESET", Assert.Single(scan.LastEvents).Code);
        Assert.Equal(new[] { ("CV001.Permit", true) }, scan.LastWrites.Select(w => (w.Key, w.Value.AsBool)));

        scan.Times(3);                                   // Reset held high: no second reset, no second write
        Assert.Empty(scan.LastWrites);
        Assert.Equal("INTERLOCK_TRIP,INTERLOCK_RESET", scan.Codes());
    }

    [Fact]
    public void ARefusedResetSendsNoResetWrite()
    {
        Scan scan = HealthyWithPermit().Once();
        scan.Set("PERM01.Ok", false).Once();

        scan.Command("Reset", true).Once();              // refused: PERM01.Ok still abnormal
        Assert.Empty(scan.LastWrites);
        scan.Command("Reset", false).Once();
        scan.Command("Reset", true).Once();              // a second edge, still refused
        Assert.Empty(scan.LastWrites);

        Assert.True(scan.Bool("Tripped"));
        Assert.Equal("INTERLOCK_TRIP", scan.Codes());
    }

    [Fact]
    public void TheConstructorRejectsTwoResetWritesToOneTag()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(() => new Interlock(
            "INT01",
            [new Condition("CV001.Tripped", false)],
            [],
            Period,
            [new BlockWrite("CV001.Permit", TagValue.Bool(true)), new BlockWrite("CV001.Permit", TagValue.Bool(false))]));

        Assert.Equal("resetWrites", error.ParamName);
        Assert.Contains("'CV001.Permit' is commanded twice on reset.", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheConstructorRejectsNullResetWrites()
    {
        ArgumentNullException error = Assert.Throws<ArgumentNullException>(() => new Interlock(
            "INT01",
            [new Condition("CV001.Tripped", false)],
            [],
            Period,
            null!));

        Assert.Equal("resetWrites", error.ParamName);
    }

    [Fact]
    public void TheConstructorRejectsAResetWriteToABlankTag()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(() => new Interlock(
            "INT01",
            [new Condition("CV001.Tripped", false)],
            [],
            Period,
            [new BlockWrite(" ", TagValue.Bool(true))]));

        Assert.Equal("resetWrites", error.ParamName);
    }

    [Fact]
    public void TheConstructorRejectsATagCommandedAsTwoKinds()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(() => new Interlock(
            "INT01",
            [new Condition("CV001.Tripped", false)],
            [new BlockWrite("V1.Setpoint", TagValue.Double(0.0))],
            Period,
            [new BlockWrite("V1.Setpoint", TagValue.Bool(true))]));

        Assert.Equal("resetWrites", error.ParamName);
        Assert.Contains("as a Double on trip and as a Bool on reset", error.Message, StringComparison.Ordinal);
    }
```

(`Scan.LastWrites` lists the writes of the most recent scan in the block's
`Writes` order; `Scan.Codes()` every event code so far — both exist.)

- [ ] **Step 2: Write the failing host, catalogue and loader tests**

In `tests/Dse.Control.Tests/HostTests.cs`, insert before
`    [Fact]\n    public void TwoRunsOfThePlantWithEveryBlockAreByteIdentical()`:

```csharp
    [Fact]
    public void AResetWriteLandsTheTickAfterTheResetScanAndIsNotRecorded()
    {
        Simulation sim = Plant()
            .AddScanBlock(new Interlock(
                "INT01",
                [new Condition("V1.Tripped", false)],
                [new BlockWrite("V1.Fill", TagValue.Bool(false))],
                Period,
                [new BlockWrite("V1.Fill", TagValue.Bool(true))]))
            .Build();
        var spy = new Spy();
        sim.AttachActionRecorder(spy);
        sim.WriteAt(TimeSpan.FromMilliseconds(100), "V1.Fill", TagValue.Bool(true));
        sim.WriteAt(TimeSpan.FromMilliseconds(500), "V1.Trip", TagValue.Bool(true));
        sim.WriteAt(TimeSpan.FromMilliseconds(700), "V1.Trip", TagValue.Bool(false));
        sim.WriteAt(TimeSpan.FromMilliseconds(900), "INT01.Reset", TagValue.Bool(true));

        sim.RunFor(TimeSpan.FromSeconds(1.5));

        // The reset lands at phase 1 of tick 90 and is published at its end; the
        // scan at tick 100 is the first to see it, accepts it and queues the reset
        // write, which lands at phase 1 of tick 101 — one pin, V1.Fill, carries
        // both the trip write and the reset write.
        Assert.Equal(
            new[] { (61L, "Set to false by INT01."), (101L, "Set to true by INT01.") },
            sim.Events.Records
                .Where(r => string.Equals(r.Source, "V1.Fill", StringComparison.Ordinal) && r.Message.EndsWith("by INT01.", StringComparison.Ordinal))
                .Select(r => (r.Tick, r.Message))
                .ToArray());
        Assert.Equal(100L, Assert.Single(sim.Events.Records, r => string.Equals(r.Code, "INTERLOCK_RESET", StringComparison.Ordinal)).Tick);
        Assert.True(sim.IO.ReadBool("V1.Fill"));
        Assert.Equal(4, spy.Writes.Count);
    }

```

In `tests/Dse.Control.Catalogue.Tests/BlockFactoryTests.cs`, insert before
`    [Fact]\n    public void AnAlarmOwnsAPairPerConfiguredLimitInLimitOrder()`:

```csharp
    [Fact]
    public void AnInterlocksResetWritesBindAndShareAPinWithItsTripWrites()
    {
        ParameterValues values = Bind.Values(
            InterlockCatalogue.Descriptor.Parameters,
            """
            { "conditions": [ { "tag": "V1.Tripped", "normal": false } ],
              "trip": [ { "tag": "V1.Fill", "value": false } ],
              "reset": [ { "tag": "V1.Fill", "value": true } ] }
            """);

        IScanBlock block = InterlockCatalogue.Descriptor.Factory("INT01", Bind.Period, values);

        Assert.Equal(new TagRef("V1.Fill", TagKind.Bool), Assert.Single(block.Writes));
    }

```

Create `tests/Dse.Configuration.Tests/Plants/invalid/DSE111-reset-write-twice.json`:

```json
{
  "defaults": { "seed": 1, "timeStepMs": 10, "startTime": "2026-01-01T06:00:00Z" },
  "materials": [ { "name": "ore", "kind": "bulk" } ],
  "components": [
    { "id": "FEED", "type": "bulk-source", "parameters": { "material": "ore", "rateKgPerS": 20 } },
    { "id": "CHUTE", "type": "transfer-chute", "parameters": { "capacityKg": 200 } },
    { "id": "PILE", "type": "bulk-sink" }
  ],
  "flows": [ { "from": "FEED.Out", "to": "CHUTE.In" }, { "from": "CHUTE.Out", "to": "PILE.In" } ],
  "controllers": [
    { "id": "INT01", "type": "interlock", "scanPeriodMs": 100,
      "parameters": {
        "conditions": [ { "tag": "CHUTE.Full", "normal": false } ],
        "trip": [ { "tag": "FEED.Enabled", "value": false }, { "tag": "FEED.Permit", "value": false } ],
        "reset": [ { "tag": "FEED.Permit", "value": true }, { "tag": "FEED.Permit", "value": true } ] } }
  ]
}
```

(The corpus is copied to the test output by the project's existing glob; the
corpus theory and the schema-agreement theory each gain a row.)

- [ ] **Step 3: Run them to see them fail**

Run: `dotnet test tests/Dse.Control.Tests --nologo`
Expected: the build fails with `error CS1729: 'Interlock' does not contain a constructor that takes 5 arguments`.
Run: `dotnet test tests/Dse.Control.Catalogue.Tests --nologo`
Expected: 22 passed, 1 failed — `AnInterlocksResetWritesBindAndShareAPinWithItsTripWrites` (the binder does not know `reset`).
Run: `dotnet test tests/Dse.Configuration.Tests --nologo`
Expected: 209 passed, 2 failed — the two `DSE111-reset-write-twice.json` rows (measured: the loader reports `DSE101 $.controllers[0].parameters.reset — 'reset' is not a parameter here.`; the schema rejects the key).

- [ ] **Step 4: Write the interlock**

Replace the whole of `src/Dse.Control/Interlock.cs` with:

```csharp
using Dse.Io;

namespace Dse.Control;

/// <summary>
/// The conditions that stop a running thing. Any abnormal condition latches
/// <c>Tripped</c>, captures <c>FirstOut</c> and sends the trip writes — on the
/// trip scan only (R71). The latch clears on a rising edge of <c>Reset</c> while
/// every condition is normal, and on nothing else; the scan that clears it sends
/// the reset writes, once. A tag may be in both lists: that is how an interlock
/// holds a device's run permit off while it is tripped (R122).
/// </summary>
public sealed class Interlock : IScanBlock
{
    private readonly Condition[] _conditions;
    private readonly BlockWrite[] _tripWrites;
    private readonly BlockWrite[] _resetWrites;
    private readonly int[] _resetWriteIndex;
    private bool _tripped;
    private bool _previousReset;
    private long _firstOut = -1L;

    /// <summary>Creates an interlock with no reset writes.</summary>
    /// <param name="id">The block id; prefixes <c>Ok</c>, <c>Tripped</c>, <c>FirstOut</c> and <c>Reset</c>.</param>
    /// <param name="conditions">At least one Bool tag with its normal polarity, in report order.</param>
    /// <param name="tripWrites">What to command when the interlock trips; may be empty. One write per tag.</param>
    /// <param name="scanPeriod">How often the block scans.</param>
    public Interlock(
        string id,
        IReadOnlyList<Condition> conditions,
        IReadOnlyList<BlockWrite> tripWrites,
        TimeSpan scanPeriod)
        : this(id, conditions, tripWrites, scanPeriod, [])
    {
    }

    /// <summary>Creates an interlock.</summary>
    /// <param name="id">The block id; prefixes <c>Ok</c>, <c>Tripped</c>, <c>FirstOut</c> and <c>Reset</c>.</param>
    /// <param name="conditions">At least one Bool tag with its normal polarity, in report order.</param>
    /// <param name="tripWrites">What to command when the interlock trips; may be empty. One write per tag.</param>
    /// <param name="scanPeriod">How often the block scans.</param>
    /// <param name="resetWrites">
    /// What to command on the scan that accepts a reset; may be empty. One write per tag; a tag may also be a
    /// trip write, with the same kind, and then the block has one write pin for it.
    /// </param>
    public Interlock(
        string id,
        IReadOnlyList<Condition> conditions,
        IReadOnlyList<BlockWrite> tripWrites,
        TimeSpan scanPeriod,
        IReadOnlyList<BlockWrite> resetWrites)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(conditions);
        ArgumentNullException.ThrowIfNull(tripWrites);
        ArgumentNullException.ThrowIfNull(resetWrites);

        if (conditions.Count == 0)
        {
            throw new ArgumentException("An interlock needs at least one condition.", nameof(conditions));
        }

        if (scanPeriod <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(scanPeriod), scanPeriod, "The scan period must be positive.");
        }

        _conditions = [.. conditions];
        _tripWrites = [.. tripWrites];
        _resetWrites = [.. resetWrites];

        var pins = new TagRef[_conditions.Length];
        for (int i = 0; i < _conditions.Length; i++)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(_conditions[i].Tag, nameof(conditions));
            pins[i] = new TagRef(_conditions[i].Tag, TagKind.Bool);
        }

        // Trip writes take pins 0..n-1 in order, so an interlock with no reset
        // writes declares exactly the pins it always has.
        var writes = new List<TagRef>(_tripWrites.Length + _resetWrites.Length);
        RejectRepeats(_tripWrites, "on trip", nameof(tripWrites));
        foreach (BlockWrite write in _tripWrites)
        {
            writes.Add(new TagRef(write.Tag, write.Value.Kind));
        }

        RejectRepeats(_resetWrites, "on reset", nameof(resetWrites));
        _resetWriteIndex = new int[_resetWrites.Length];
        for (int i = 0; i < _resetWrites.Length; i++)
        {
            _resetWriteIndex[i] = Pin(writes, new TagRef(_resetWrites[i].Tag, _resetWrites[i].Value.Kind), nameof(resetWrites));
        }

        Id = id;
        ScanPeriod = scanPeriod;
        Inputs = pins;
        Writes = [.. writes];
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
        new TagSpec("Ok", TagKind.Bool, "", "Not tripped"),
        new TagSpec("Tripped", TagKind.Bool, "", "Latched by an abnormal condition"),
        new TagSpec("FirstOut", TagKind.Int64, "", "Index of the condition that tripped, or -1"),
    ];

    /// <inheritdoc/>
    public IReadOnlyList<TagSpec> Commands { get; } =
    [
        new TagSpec("Reset", TagKind.Bool, "", "Clears the latch on a rising edge when every condition is normal"),
    ];

    /// <inheritdoc/>
    public void Scan(in ScanInputs inputs, ref ScanOutputs outputs)
    {
        bool reset = inputs.Command(0).AsBool;
        bool resetEdge = reset && !_previousReset;
        _previousReset = reset;

        bool allNormal = true;
        int first = -1;

        for (int i = 0; i < _conditions.Length; i++)
        {
            if (inputs.Input(i).AsBool != _conditions[i].Normal)
            {
                allNormal = false;
                if (first < 0)
                {
                    first = i;
                }
            }
        }

        if (!allNormal && !_tripped)
        {
            _tripped = true;
            _firstOut = first;
            outputs.Raise("INTERLOCK_TRIP", $"{_conditions[first].Tag} abnormal.");
            for (int i = 0; i < _tripWrites.Length; i++)
            {
                outputs.Write(i, _tripWrites[i].Value);
            }
        }
        else if (_tripped && resetEdge && allNormal)
        {
            _tripped = false;
            _firstOut = -1L;
            outputs.Raise("INTERLOCK_RESET", "Reset with all conditions normal.");
            for (int i = 0; i < _resetWrites.Length; i++)
            {
                outputs.Write(_resetWriteIndex[i], _resetWrites[i].Value);
            }
        }

        outputs.Set(0, TagValue.Bool(!_tripped));
        outputs.Set(1, TagValue.Bool(_tripped));
        outputs.Set(2, TagValue.Int64(_firstOut));
    }

    /// <summary>Rejects a blank tag, and a tag commanded twice within one list.</summary>
    private static void RejectRepeats(BlockWrite[] list, string when, string parameter)
    {
        for (int i = 0; i < list.Length; i++)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(list[i].Tag, parameter);
            for (int j = 0; j < i; j++)
            {
                if (string.Equals(list[i].Tag, list[j].Tag, StringComparison.Ordinal))
                {
                    throw new ArgumentException(
                        $"Tag '{list[i].Tag}' is commanded twice {when}. Command each tag once in each list.",
                        parameter);
                }
            }
        }
    }

    /// <summary>The index of the pin on this tag, added if it is new. A trip and a reset write on one tag must agree on the kind.</summary>
    private static int Pin(List<TagRef> pins, TagRef pin, string parameter)
    {
        for (int i = 0; i < pins.Count; i++)
        {
            if (!string.Equals(pins[i].Name, pin.Name, StringComparison.Ordinal))
            {
                continue;
            }

            if (pins[i].Kind != pin.Kind)
            {
                throw new ArgumentException(
                    $"Tag '{pin.Name}' is commanded as a {pins[i].Kind} on trip and as a {pin.Kind} on reset. Use one kind.",
                    parameter);
            }

            return i;
        }

        pins.Add(pin);
        return pins.Count - 1;
    }
}
```

- [ ] **Step 5: Add `reset` to the catalogue descriptor**

In `src/Dse.Control.Catalogue/InterlockCatalogue.cs`, the factory

```csharp
        (id, period, p) => new Interlock(
            id, ControlCatalogue.Conditions(p, "conditions"), ControlCatalogue.Writes(p, "trip"), period))
```

becomes

```csharp
        (id, period, p) => new Interlock(
            id, ControlCatalogue.Conditions(p, "conditions"), ControlCatalogue.Writes(p, "trip"), period, ControlCatalogue.Writes(p, "reset")))
```

and after the `trip` parameter line
(`            Param.GroupList("trip", "What to command when the interlock trips, once, on the trip scan.", ControlCatalogue.WriteGroup),`)
add

```csharp
            Param.GroupList("reset", "What to command when a reset is accepted, once, on the reset scan.", ControlCatalogue.WriteGroup),
```

The descriptor's description line stays as it is (R123).

- [ ] **Step 6: Run the three projects**

Run: `dotnet test tests/Dse.Control.Tests --nologo` — expect **116** passed (105 + 10 + 1).
Run: `dotnet test tests/Dse.Control.Catalogue.Tests --nologo` — expect 22 passed, 1 failed:
`ControlCatalogueTests.TheControlCatalogueExportsExactlyTheGoldenFile`.
Run: `dotnet test tests/Dse.Configuration.Tests --nologo` — expect 210 passed, 1 failed:
`PlantSchemaTests.MatchesTheGoldenFile`. (The fixture's two rows now pass: the
loader reports exactly one `DSE111` — "'INT01' (interlock) rejected its
parameters: Tag 'FEED.Permit' is commanded twice on reset. Command each tag once
in each list." — and the schema accepts the file.)

- [ ] **Step 7: Regenerate the two goldens and read their diffs**

Run: `DSE_UPDATE_GOLDEN=1 dotnet test tests/Dse.Control.Catalogue.Tests --nologo --filter "FullyQualifiedName~TheControlCatalogueExportsExactlyTheGoldenFile"`
Run: `DSE_UPDATE_GOLDEN=1 dotnet test tests/Dse.Configuration.Tests --nologo --filter "FullyQualifiedName~PlantSchemaTests.MatchesTheGoldenFile"`
Then read `git diff tests/Dse.Control.Catalogue.Tests/Golden/control-catalogue.json tests/Dse.Configuration.Tests/Golden/plant.schema.json`
in full. Checklist (measured):

- `control-catalogue.json`: **+23 lines, 0 removed, one hunk** inside the
  `interlock` entry, after the `trip` parameter: a parameter `"name": "reset"`,
  `"kind": "groupList"`, `"required": false`, the description of Step 5,
  `"group": "BlockWrite"`, and the same two child parameters (`tag` with
  `"writes": true`, `value` with `"tagParameter": "tag"`) as `trip`. The
  interlock's `description` line is unchanged.
- `plant.schema.json`: **+7 lines, 0 removed, one hunk** inside
  `block.interlock`'s `parameters.properties`, after `trip`: a `"reset"` property
  with the Step 5 description, `"type": "array"` and
  `"items": { "$ref": "#/$defs/group.BlockWrite" }`. `required` is still
  `["conditions"]`.

- [ ] **Step 8: Build and test the solution**

Run: `dotnet build Dse.sln -c Release --nologo` — expect `0 Warning(s)`, `0 Error(s)`.
Run: `dotnet test Dse.sln --nologo` — expect **1341** (Control 116, Control.Catalogue
23, Configuration 211). The sample goldens are untouched (the plant has no
`reset` yet): `git status --short` lists only the eight files of this task, and
`git diff --stat aa3c403 -- src/` lists exactly the five files of the Global
Constraints.

- [ ] **Step 9: Commit**

```bash
git add src/Dse.Control/Interlock.cs src/Dse.Control.Catalogue/InterlockCatalogue.cs tests/Dse.Control.Tests/InterlockTests.cs tests/Dse.Control.Tests/HostTests.cs tests/Dse.Control.Catalogue.Tests/BlockFactoryTests.cs tests/Dse.Configuration.Tests/Plants/invalid/DSE111-reset-write-twice.json tests/Dse.Control.Catalogue.Tests/Golden/control-catalogue.json tests/Dse.Configuration.Tests/Golden/plant.schema.json
```

```bash
git commit -m "feat(control): let an interlock send writes once, on the scan that accepts a reset" -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>"
```

---

### Task 3: The sample's interlocks hold the permit, and `SEQ_START` resets before it starts

**Model:** opus.

**Files:**
- Modify: `samples/mine-conveyors/plant.json` (four interlocks; `SEQ_START`'s steps)
- Modify: `tests/Dse.Samples.Tests/Stories.cs` (whole file below)
- Regenerate: the eight `samples/mine-conveyors/expected/*.log`
- Modify: `samples/mine-conveyors/README.md` (three quoted blocks)

**Interfaces:**
- Consumes: Task 1's `CVn.Permit`, `Feed.Permit`; Task 2's `reset` parameter;
  `EventPattern(string? Source, string Code, string MessageFragment = "")`,
  `Absence(EventPattern? After, EventPattern Forbidden, EventPattern? Until = null)` (existing).
- Produces: the plant Task 4's scenario runs on — each interlock's reset writes
  `Permit` true **and** the command false (R126); `SEQ_START`'s nine steps (R124),
  whose names Task 4's golden checklist quotes; `Stories.StartComplete` and the
  `E(...)` helper Task 4 extends.

- [ ] **Step 1: Re-pin the stories**

Replace the whole of `tests/Dse.Samples.Tests/Stories.cs` with:

```csharp
namespace Dse.Samples.Tests;

/// <summary>A scenario's causal chain, in order, and what must not happen.</summary>
public sealed record Story(IReadOnlyList<EventPattern> Chain, IReadOnlyList<Absence> Absences);

/// <summary>
/// The stories of the design (6a spec section 5, 6c spec section 4), as the measured event logs
/// tell them. Each chain is an ordered subsequence of the scenario's log; each
/// absence is bounded by a start point that must itself occur. Feed-starve's
/// fall in transport order is state, not events: see
/// <c>MineConveyorTests.AStarvedFeedEmptiesTheBeltsInTransportOrder</c>.
/// </summary>
public static class Stories
{
    private static readonly EventPattern StartComplete = E("SEQ_START", "SEQUENCE_COMPLETE");

    public static IReadOnlyDictionary<string, Story> All { get; } = new Dictionary<string, Story>(StringComparer.Ordinal)
    {
        ["normal-start-stop"] = new(
            [
                E("SEQ_START", "STEP_ENTERED", "2: Reset CV003's interlock."),
                E("INT_CV003", "INTERLOCK_RESET"),
                E("CV003.Start", "WRITE", "Set to false by INT_CV003."),
                E("CV003.Permit", "WRITE", "Set to true by INT_CV003."),
                E("SEQ_START", "STEP_ENTERED", "3: Start CV003."),
                E("CV003.Start", "WRITE", "Set to true by SEQ_START."),
                E("CV003.Starter", "CONTACTOR_CLOSED"),
                E("SEQ_START", "STEP_ENTERED", "4: Reset CV002's interlock."),
                E("INT_CV002", "INTERLOCK_RESET"),
                E("CV002.Start", "WRITE", "Set to false by INT_CV002."),
                E("CV002.Permit", "WRITE", "Set to true by INT_CV002."),
                E("SEQ_START", "STEP_ENTERED", "5: Start CV002."),
                E("CV002.Start", "WRITE", "Set to true by SEQ_START."),
                E("CV002.Starter", "CONTACTOR_CLOSED"),
                E("SEQ_START", "STEP_ENTERED", "6: Reset CV001's interlock."),
                E("INT_CV001", "INTERLOCK_RESET"),
                E("CV001.Start", "WRITE", "Set to false by INT_CV001."),
                E("CV001.Permit", "WRITE", "Set to true by INT_CV001."),
                E("SEQ_START", "STEP_ENTERED", "7: Start CV001."),
                E("CV001.Start", "WRITE", "Set to true by SEQ_START."),
                E("CV001.Starter", "CONTACTOR_CLOSED"),
                E("SEQ_START", "STEP_ENTERED", "8: Reset the feed's interlock."),
                E("INT_FEED", "INTERLOCK_RESET"),
                E("Feed.Enabled", "WRITE", "Set to false by INT_FEED."),
                E("Feed.Permit", "WRITE", "Set to true by INT_FEED."),
                E("SEQ_START", "STEP_ENTERED", "9: Start the feed."),
                E("Feed.Enabled", "WRITE", "Set to true by SEQ_START."),
                StartComplete,
                E("SEQ_STOP", "STEP_ENTERED", "1: Stop the feed."),
                E("Feed.Enabled", "WRITE", "Set to false by SEQ_STOP."),
                E("CV001.Starter", "CONTACTOR_OPENED"),
                E("INT_FEED", "INTERLOCK_TRIP", "CV001.Contactor abnormal."),
                E("Feed.Permit", "WRITE", "Set to false by INT_FEED."),
                E("CV001.ZeroSpeed", "ZERO_SPEED"),
                E("CV002.Starter", "CONTACTOR_OPENED"),
                E("INT_CV001", "INTERLOCK_TRIP", "CV002.Contactor abnormal."),
                E("CV001.Permit", "WRITE", "Set to false by INT_CV001."),
                E("CV002.ZeroSpeed", "ZERO_SPEED"),
                E("CV003.Starter", "CONTACTOR_OPENED"),
                E("INT_CV002", "INTERLOCK_TRIP", "CV003.Contactor abnormal."),
                E("CV002.Permit", "WRITE", "Set to false by INT_CV002."),
                E("CV003.ZeroSpeed", "ZERO_SPEED"),
                E("SEQ_STOP", "SEQUENCE_COMPLETE"),
            ],
            [
                new(StartComplete, E(null, "INTERLOCK_TRIP"), E("SEQ_STOP.Start", "WRITE", "Set to true.")),
                new(null, E(null, "ALARM_RAISED")),
                new(null, E(null, "SEQUENCE_FAULTED")),

                // Each interlock's reset drops the command it releases, so SEQ_START resets it a
                // step before it commands the device (6c R124); nothing closes before its start.
                new(null, E("CV003.Starter", "CONTACTOR_CLOSED"), E("CV003.Start", "WRITE", "Set to true by SEQ_START.")),
                new(null, E("CV002.Starter", "CONTACTOR_CLOSED"), E("CV002.Start", "WRITE", "Set to true by SEQ_START.")),
                new(null, E("CV001.Starter", "CONTACTOR_CLOSED"), E("CV001.Start", "WRITE", "Set to true by SEQ_START.")),
            ]),

        ["pull-key"] = new(
            [
                StartComplete,
                E("CV002.PullKey1", "PULLKEY_PULLED"),
                E("CV002.Safety", "SAFETY_TRIP", "Channel1 open"),
                E("CV002.Starter", "CONTACTOR_OPENED"),
                E("PERM_CV002", "PERMISSIVE_LOST", "CV002.SafetyOk dropped."),
                E("INT_CV001", "INTERLOCK_TRIP", "CV002.Contactor abnormal."),
                E("CV001.Start", "WRITE", "Set to false by INT_CV001."),
                E("CV001.Permit", "WRITE", "Set to false by INT_CV001."),
                E("CV001.Starter", "CONTACTOR_OPENED"),
                E("INT_CV002", "INTERLOCK_TRIP", "PERM_CV002.Ok abnormal."),
                E("INT_FEED", "INTERLOCK_TRIP", "CV001.Contactor abnormal."),
                E("CV002.Permit", "WRITE", "Set to false by INT_CV002."),
                E("Feed.Enabled", "WRITE", "Set to false by INT_FEED."),
                E("Feed.Permit", "WRITE", "Set to false by INT_FEED."),
                E("CV002.ZeroSpeed", "ZERO_SPEED"),
                E("CV001.ZeroSpeed", "ZERO_SPEED"),
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
                E("INT_FEED", "INTERLOCK_TRIP", "CV001.Contactor abnormal."),
                E("Feed.Enabled", "WRITE", "Set to false by INT_FEED."),
                E("Feed.Permit", "WRITE", "Set to false by INT_FEED."),
                E("INT_CV001", "INTERLOCK_TRIP", "PERM_CV001.Ok abnormal."),
                E("CV001.Start", "WRITE", "Set to false by INT_CV001."),
                E("CV001.Permit", "WRITE", "Set to false by INT_CV001."),
                E("CV001.ZeroSpeed", "ZERO_SPEED"),
            ],
            [
                new(E("CV001.EStop", "ESTOP_PRESSED"), E("INT_CV002", "INTERLOCK_TRIP")),
                new(E("CV001.EStop", "ESTOP_PRESSED"), E("INT_CV003", "INTERLOCK_TRIP")),
                new(E("CV001.EStop", "ESTOP_PRESSED"), E("CV002.Starter", "CONTACTOR_OPENED")),
                new(E("CV001.EStop", "ESTOP_PRESSED"), E("CV003.Starter", "CONTACTOR_OPENED")),
            ]),

        ["overload"] = new(
            [
                StartComplete,
                E("CV003.Motor", "FAULT", "thermal-bias injected"),
                E("CV003.Starter", "OVERLOAD_TRIP"),
                E("CV003.Starter", "CONTACTOR_OPENED"),
                E("INT_CV003", "INTERLOCK_TRIP", "CV003.Tripped abnormal."),
                E("INT_CV002", "INTERLOCK_TRIP", "CV003.Contactor abnormal."),
                E("CV003.Permit", "WRITE", "Set to false by INT_CV003."),
                E("CV002.Start", "WRITE", "Set to false by INT_CV002."),
                E("CV002.Permit", "WRITE", "Set to false by INT_CV002."),
                E("CV002.Starter", "CONTACTOR_OPENED"),
                E("INT_CV001", "INTERLOCK_TRIP", "CV002.Contactor abnormal."),
                E("CV001.Start", "WRITE", "Set to false by INT_CV001."),
                E("CV001.Permit", "WRITE", "Set to false by INT_CV001."),
                E("CV001.Starter", "CONTACTOR_OPENED"),
                E("INT_FEED", "INTERLOCK_TRIP", "CV001.Contactor abnormal."),
                E("Feed.Enabled", "WRITE", "Set to false by INT_FEED."),
                E("Feed.Permit", "WRITE", "Set to false by INT_FEED."),
                E("CV003.ZeroSpeed", "ZERO_SPEED"),
                E("CV002.ZeroSpeed", "ZERO_SPEED"),
                E("CV001.ZeroSpeed", "ZERO_SPEED"),
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
                E("CV001.Starter", "CONTACTOR_OPENED"),
                E("INT_CV001", "INTERLOCK_TRIP", "CV001.Tripped abnormal."),
                E("INT_FEED", "INTERLOCK_TRIP", "CV001.Contactor abnormal."),
                E("CV001.Start", "WRITE", "Set to false by INT_CV001."),
                E("CV001.Permit", "WRITE", "Set to false by INT_CV001."),
                E("Feed.Enabled", "WRITE", "Set to false by INT_FEED."),
                E("Feed.Permit", "WRITE", "Set to false by INT_FEED."),
                E("CV001.ZeroSpeed", "ZERO_SPEED"),
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
                E("CV001.Permit", "WRITE", "Set to false by INT_CV001."),
                E("CV001.Starter", "CONTACTOR_OPENED"),
                E("INT_FEED", "INTERLOCK_TRIP", "CV001.Contactor abnormal."),
                E("Feed.Enabled", "WRITE", "Set to false by INT_FEED."),
                E("Feed.Permit", "WRITE", "Set to false by INT_FEED."),
                E("CV001.ZeroSpeed", "ZERO_SPEED"),
            ],
            [
                new(E("CV002.ZeroSpeed", "FAULT"), E("CV002.Starter", "CONTACTOR_OPENED")),
                new(E("CV002.ZeroSpeed", "FAULT"), E("CV003.Starter", "CONTACTOR_OPENED")),
                new(E("CV002.ZeroSpeed", "FAULT"), E("INT_CV002", "INTERLOCK_TRIP")),
                new(E("CV002.ZeroSpeed", "FAULT"), E("INT_CV003", "INTERLOCK_TRIP")),
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
                E("CV003.Permit", "WRITE", "Set to false by INT_CV003."),
            ],
            [
                new(E("CV003.Starter", "FAULT"), E("CV003.Starter", "CONTACTOR_OPENED")),
                new(E("CV003.Starter", "FAULT"), E("CV003.Motor", "DE_ENERGISED")),
                new(E("CV003.Starter", "FAULT"), E("CV003.ZeroSpeed", "ZERO_SPEED")),
                new(E("CV003.Starter", "FAULT"), E("CV003.Motor", "STOPPED")),
                new(E("CV003.Starter", "FAULT"), E("INT_CV002", "INTERLOCK_TRIP")),
            ]),

        ["feed-starve"] = new(
            [
                E("Feed.Permit", "WRITE", "Set to true by INT_FEED."),
                StartComplete,
                E("Feed", "FAULT", "starve injected."),
            ],
            [
                new(StartComplete, E(null, "INTERLOCK_TRIP")),
                new(StartComplete, E(null, "ALARM_RAISED")),
                new(StartComplete, E(null, "CONTACTOR_OPENED")),
            ]),
    };

    private static EventPattern E(string? source, string code, string fragment = "") => new(source, code, fragment);
}
```

- [ ] **Step 2: Run the stories to see them fail**

Run: `dotnet test tests/Dse.Samples.Tests --nologo --filter "FullyQualifiedName~EveryScenarioTellsItsStory"`
Expected: 8 failed — each names its first missing pattern (a `Permit` write, or
normal-start-stop's `2: Reset CV003's interlock.`).

- [ ] **Step 3: Give the interlocks the permit and restructure `SEQ_START`**

In `samples/mine-conveyors/plant.json` make five replacements. The four
interlocks' trip lists gain the `Permit` write and each gains a `reset` list —
`Permit` true and the command false (R126):

```json
        "trip": [ { "tag": "CV003.Start", "value": false } ] } },
```

becomes

```json
        "trip": [
          { "tag": "CV003.Start",  "value": false },
          { "tag": "CV003.Permit", "value": false } ],
        "reset": [
          { "tag": "CV003.Permit", "value": true },
          { "tag": "CV003.Start",  "value": false } ] } },
```

```json
        "trip": [ { "tag": "CV002.Start", "value": false } ] } },
```

becomes

```json
        "trip": [
          { "tag": "CV002.Start",  "value": false },
          { "tag": "CV002.Permit", "value": false } ],
        "reset": [
          { "tag": "CV002.Permit", "value": true },
          { "tag": "CV002.Start",  "value": false } ] } },
```

```json
        "trip": [ { "tag": "CV001.Start", "value": false } ] } },
```

becomes

```json
        "trip": [
          { "tag": "CV001.Start",  "value": false },
          { "tag": "CV001.Permit", "value": false } ],
        "reset": [
          { "tag": "CV001.Permit", "value": true },
          { "tag": "CV001.Start",  "value": false } ] } },
```

```json
        "trip": [ { "tag": "Feed.Enabled", "value": false } ] } },
```

becomes

```json
        "trip": [
          { "tag": "Feed.Enabled", "value": false },
          { "tag": "Feed.Permit",  "value": false } ],
        "reset": [
          { "tag": "Feed.Permit",  "value": true },
          { "tag": "Feed.Enabled", "value": false } ] } },
```

And `SEQ_START`'s steps 2–6 (from `{ "name": "Start CV003",` through the
`"Release the feed reset"` step and its closing `] ,`) become eight steps, so that
each interlock's reset — and its `false` — lands before the step that commands
the device (R124). Replace

```json
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
```

with

```json
          { "name": "Reset CV003's interlock",
            "writes": [
              { "tag": "CV001.SafetyReset", "value": false },
              { "tag": "CV002.SafetyReset", "value": false },
              { "tag": "CV003.SafetyReset", "value": false },
              { "tag": "INT_CV003.Reset",   "value": true } ],
            "transition": { "type": "when", "tag": "INT_CV003.Ok", "op": "==", "value": true },
            "timeoutS": 5 },
          { "name": "Start CV003",
            "writes": [
              { "tag": "INT_CV003.Reset", "value": false },
              { "tag": "CV003.Start",     "value": true } ],
            "transition": { "type": "when", "tag": "CV003.Speed", "op": ">=", "value": 1.74 },
            "timeoutS": 15 },
          { "name": "Reset CV002's interlock",
            "writes": [ { "tag": "INT_CV002.Reset", "value": true } ],
            "transition": { "type": "when", "tag": "INT_CV002.Ok", "op": "==", "value": true },
            "timeoutS": 5 },
          { "name": "Start CV002",
            "writes": [
              { "tag": "INT_CV002.Reset", "value": false },
              { "tag": "CV002.Start",     "value": true } ],
            "transition": { "type": "when", "tag": "CV002.Speed", "op": ">=", "value": 1.74 },
            "timeoutS": 15 },
          { "name": "Reset CV001's interlock",
            "writes": [ { "tag": "INT_CV001.Reset", "value": true } ],
            "transition": { "type": "when", "tag": "INT_CV001.Ok", "op": "==", "value": true },
            "timeoutS": 5 },
          { "name": "Start CV001",
            "writes": [
              { "tag": "INT_CV001.Reset", "value": false },
              { "tag": "CV001.Start",     "value": true } ],
            "transition": { "type": "when", "tag": "CV001.Speed", "op": ">=", "value": 1.74 },
            "timeoutS": 15 },
          { "name": "Reset the feed's interlock",
            "writes": [ { "tag": "INT_FEED.Reset", "value": true } ],
            "transition": { "type": "when", "tag": "INT_FEED.Ok", "op": "==", "value": true },
            "timeoutS": 5 },
          { "name": "Start the feed",
            "writes": [
              { "tag": "INT_FEED.Reset", "value": false },
              { "tag": "Feed.Enabled",   "value": true } ],
            "transition": { "type": "after", "delayS": 1 } } ],
```

Step 1 ("Reset the safety relays"), the `abort` list, `SEQ_STOP` and every other
controller are unchanged.

- [ ] **Step 4: Regenerate the eight goldens**

Run: `DSE_UPDATE_GOLDEN=1 dotnet test tests/Dse.Samples.Tests --nologo --filter "FullyQualifiedName~EveryScenarioMatchesItsGolden"`
Expected: 8 passed. `git status --short samples/` lists the eight
`expected/*.log` and `plant.json`.

- [ ] **Step 5: Read every golden in full and check it**

Read each of the eight files with a file-reading tool, top to bottom. Checklist
(measured; R124, R129):

- **All eight — lines 1–78 identical** (compare `head -78` of each; they are
  byte-identical). In them, in this order among their neighbours:
  - at 0.010 s, each interlock's two trip writes, command then permit:
    `CV003.Start … Set to false by INT_CV003.`, `CV003.Permit … Set to false by INT_CV003.`,
    the same for CV002, CV001, then `Feed.Enabled`, `Feed.Permit` by `INT_FEED`;
  - `06:00:02.200  SEQ_START  STEP_ENTERED  2: Reset CV003's interlock.`,
    `06:00:02.210  INT_CV003.Reset  WRITE  Set to true by SEQ_START.`,
    `06:00:02.300  INT_CV003  INTERLOCK_RESET  Reset with all conditions normal.`,
    `06:00:02.310  CV003.Start  WRITE  Set to false by INT_CV003.`,
    `06:00:02.310  CV003.Permit  WRITE  Set to true by INT_CV003.` (pin order: the
    command before the permit, R122),
    `06:00:02.400  SEQ_START  STEP_ENTERED  3: Start CV003.`,
    `06:00:02.410  CV003.Start  WRITE  Set to true by SEQ_START.`,
    `06:00:02.410  CV003.Starter  CONTACTOR_CLOSED  Motor energised.`;
  - `06:00:04.800 … 4: Reset CV002's interlock.`, `06:00:04.900  INT_CV002  INTERLOCK_RESET …`,
    `06:00:04.910` `CV002.Start` false / `CV002.Permit` true by `INT_CV002`,
    `06:00:05.000 … 5: Start CV002.`, `06:00:05.010  CV002.Starter  CONTACTOR_CLOSED  Motor energised.`,
    `06:00:05.400  CV003.Motor  AT_SPEED  Reached 146.17986855114356 rad/s.`;
  - `06:00:07.400 … 6: Reset CV001's interlock.`, `06:00:07.500  INT_CV001  INTERLOCK_RESET …`,
    `06:00:07.600 … 7: Start CV001.`, `06:00:07.610  CV001.Starter  CONTACTOR_CLOSED  Motor energised.`,
    `06:00:08.000  CV002.Motor  AT_SPEED  Reached 146.1547694598214 rad/s.`;
  - `06:00:10.000 … 8: Reset the feed's interlock.`, `06:00:10.100  INT_FEED  INTERLOCK_RESET …`,
    `06:00:10.110  Feed.Enabled  WRITE  Set to false by INT_FEED.`,
    `06:00:10.110  Feed.Permit  WRITE  Set to true by INT_FEED.`,
    `06:00:10.200 … 9: Start the feed.`, `06:00:10.210  Feed.Enabled  WRITE  Set to true by SEQ_START.`,
    `06:00:10.590  CV001.Motor  AT_SPEED  Reached 145.98559884017868 rad/s.`;
  - line 78: `06:00:11.200  SEQ_START  SEQUENCE_COMPLETE  Finished after 9 steps.`
  - no `SEQUENCE_FAULTED`, no `INTERLOCK_TRIP` after 0.000 s in these lines.
- **After line 78, every golden is its old tail** (the lines after the old
  line 63) **plus one `Permit … Set to false by …` line per interlock trip**,
  directly after that interlock's `Start`/`Enabled` write, with only these
  value changes:
  - **normal-start-stop, 112 lines:** gains `06:02:10.310  Feed.Permit …`,
    `06:02:38.510  CV001.Permit …`, `06:03:01.710  CV002.Permit …`.
  - **pull-key, 99:** gains `06:01:20.110  CV001.Permit …`,
    `06:01:20.210  CV002.Permit …`, `06:01:20.210  Feed.Permit …`.
  - **e-stop, 92:** gains `06:01:20.110  Feed.Permit …`, `06:01:20.210  CV001.Permit …`.
  - **overload, 104:** gains `06:01:20.110  CV003.Permit …`, `06:01:20.110  CV002.Permit …`,
    `06:01:20.210  CV001.Permit …`, `06:01:20.310  Feed.Permit …`; and
    `06:01:20.000  CV003.Starter  OVERLOAD_TRIP  Thermal state 1.285658707267615 reached the trip level 1.1.`
  - **chute-blockage, 95:**
    `06:01:53.400  ALM_CV001  ALARM_RAISED  Hi: 7.705084760820622 above 7.5.`,
    `06:02:09.300  ALM_CV001  ALARM_RAISED  HiHi: 8.806537054771315 above 8.6.`,
    `06:03:09.770  CV001.Starter  OVERLOAD_TRIP  Thermal state 1.1000357303409216 reached the trip level 1.1.`,
    `06:03:09.770  CV001.Starter  CONTACTOR_OPENED  Motor de-energised.`, the two
    interlock trips still at 3:09.800, their four writes at 3:09.810, and
    `06:03:17.560  CV001.ZeroSpeed  ZERO_SPEED  Speed below 0.02 m/s for 1 s.`
  - **failed-zero-speed, 90:** gains `06:01:21.010  CV001.Permit …`, `06:01:21.110  Feed.Permit …`.
  - **welded-contactor, 117:** gains `06:02:10.310  Feed.Permit …`,
    `06:02:38.510  CV001.Permit …`, `06:03:30.210  CV003.Permit  WRITE  Set to false by INT_CV003.`;
    no `CV003.Starter  CONTACTOR_OPENED` after 80 s.
  - **feed-starve, 79:** nothing after 80 s but `06:01:20.000  Feed  FAULT  starve injected.`

Quote the checked lines in the task report.

- [ ] **Step 6: Correct the three README blocks whose quoted lines moved**

In `samples/mine-conveyors/README.md`, in the `expected/normal-start-stop.log`
block replace

```text
06:00:02.210  CV003.Start  WRITE  Set to true by SEQ_START.
06:00:04.600  SEQ_START  STEP_ENTERED  3: Start CV002.
06:00:07.000  SEQ_START  STEP_ENTERED  4: Start CV001.
06:00:09.400  SEQ_START  STEP_ENTERED  5: Start the feed.
06:00:11.400  SEQ_START  SEQUENCE_COMPLETE  Finished after 6 steps.
```

with

```text
06:00:02.200  SEQ_START  STEP_ENTERED  2: Reset CV003's interlock.
06:00:02.310  CV003.Start  WRITE  Set to false by INT_CV003.
06:00:02.310  CV003.Permit  WRITE  Set to true by INT_CV003.
06:00:02.410  CV003.Start  WRITE  Set to true by SEQ_START.
06:00:05.000  SEQ_START  STEP_ENTERED  5: Start CV002.
06:00:07.600  SEQ_START  STEP_ENTERED  7: Start CV001.
06:00:10.200  SEQ_START  STEP_ENTERED  9: Start the feed.
06:00:11.200  SEQ_START  SEQUENCE_COMPLETE  Finished after 9 steps.
```

in the `expected/overload.log` block replace

```text
06:01:20.000  CV003.Starter  OVERLOAD_TRIP  Thermal state 1.2884784008371077 reached the trip level 1.1.
```

with

```text
06:01:20.000  CV003.Starter  OVERLOAD_TRIP  Thermal state 1.285658707267615 reached the trip level 1.1.
```

and in the `expected/chute-blockage.log` block replace

```text
06:01:53.400  ALM_CV001  ALARM_RAISED  Hi: 7.705095441377381 above 7.5.
06:02:09.300  ALM_CV001  ALARM_RAISED  HiHi: 8.806547735328072 above 8.6.
06:03:09.700  CV001.Starter  OVERLOAD_TRIP  Thermal state 1.1000144443933113 reached the trip level 1.1.
```

with

```text
06:01:53.400  ALM_CV001  ALARM_RAISED  Hi: 7.705084760820622 above 7.5.
06:02:09.300  ALM_CV001  ALARM_RAISED  HiHi: 8.806537054771315 above 8.6.
06:03:09.770  CV001.Starter  OVERLOAD_TRIP  Thermal state 1.1000357303409216 reached the trip level 1.1.
```

Nothing else in the README changes in this task (Task 5 rewrites its prose).

- [ ] **Step 7: Run the sample project and the solution**

Run: `dotnet test tests/Dse.Samples.Tests --nologo` — expect **66** passed.
Run: `dotnet build Dse.sln -c Release --nologo` — expect `0 Warning(s)`, `0 Error(s)`.
Run: `dotnet test Dse.sln --nologo` — expect **1341**.

- [ ] **Step 8: Commit**

```bash
git add samples/mine-conveyors/plant.json samples/mine-conveyors/expected/normal-start-stop.log samples/mine-conveyors/expected/pull-key.log samples/mine-conveyors/expected/e-stop.log samples/mine-conveyors/expected/overload.log samples/mine-conveyors/expected/chute-blockage.log samples/mine-conveyors/expected/failed-zero-speed.log samples/mine-conveyors/expected/welded-contactor.log samples/mine-conveyors/expected/feed-starve.log samples/mine-conveyors/README.md tests/Dse.Samples.Tests/Stories.cs
```

```bash
git commit -m "feat(samples): hold each interlocked device off with a run permit, reset before start" -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>"
```

---

### Task 4: Scenario 9, a start while the line is tripped

**Model:** opus.

**Files:**
- Create: `samples/mine-conveyors/scenarios/start-while-tripped.json`
- Create (generated): `samples/mine-conveyors/expected/start-while-tripped.log`
- Modify: `tests/Dse.Samples.Tests/Sample.cs` (one name, one summary)
- Modify: `tests/Dse.Samples.Tests/Stories.cs` (three fields, one story)
- Modify: `tests/Dse.Samples.Tests/MineConveyorTests.cs` (+1 fact, one rename)
- Modify: `samples/mine-conveyors/README.md` (intro counts, section 9)

**Interfaces:**
- Consumes: Task 3's plant (reset writes drop the command) and `Stories`;
  `Sample.Trace(string name, IReadOnlyList<string> tags, TimeSpan every)`,
  `TagSample(TimeSpan Time, double Value)` (existing).
- Produces: the ninth `Sample.Names` entry, `start-while-tripped`, that every
  theory in the project now runs.

- [ ] **Step 1: Name the scenario, write its story and its state check**

In `tests/Dse.Samples.Tests/Sample.cs` replace
`    /// <summary>The eight scenarios, in the order the README tells them.</summary>` with
`    /// <summary>The nine scenarios, in the order the README tells them.</summary>`
and

```csharp
        "feed-starve",
    ];
```

with

```csharp
        "feed-starve",
        "start-while-tripped",
    ];
```

In `tests/Dse.Samples.Tests/Stories.cs`, replace
`/// The stories of the design` with `/// The nine stories of the design`; after
`    private static readonly EventPattern StartComplete = E("SEQ_START", "SEQUENCE_COMPLETE");`
insert a blank line and

```csharp
    /// <summary>
    /// The operator's writes in start-while-tripped: no "by", so a block's write never matches.
    /// <c>OperatorStart</c> occurs twice — at 100 s, refused, and at 125 s, the fresh start.
    /// </summary>
    private static readonly EventPattern OperatorStart = E("CV001.Start", "WRITE", "Set to true.");

    private static readonly EventPattern OperatorEnable = E("Feed.Enabled", "WRITE", "Set to true.");

    private static readonly EventPattern OperatorFeedReset = E("INT_FEED.Reset", "WRITE", "Set to true.");
```

(before `All`, so they are initialised first; the fragment `Set to true.` cannot
match `Set to true by SEQ_START.`, and an operator's `INT_FEED.Reset` write is
logged `Set to true.` while `SEQ_START`'s is `Set to true by SEQ_START.`), and
replace the end of the dictionary,

```csharp
                new(StartComplete, E(null, "CONTACTOR_OPENED")),
            ]),
    };
```

with

```csharp
                new(StartComplete, E(null, "CONTACTOR_OPENED")),
            ]),

        ["start-while-tripped"] = new(
            [
                StartComplete,
                E("CV002.PullKey1", "PULLKEY_PULLED"),
                E("CV002.Starter", "CONTACTOR_OPENED"),
                E("INT_CV001", "INTERLOCK_TRIP", "CV002.Contactor abnormal."),
                E("CV001.Start", "WRITE", "Set to false by INT_CV001."),
                E("CV001.Permit", "WRITE", "Set to false by INT_CV001."),
                E("CV001.Starter", "CONTACTOR_OPENED"),
                E("INT_FEED", "INTERLOCK_TRIP", "CV001.Contactor abnormal."),
                E("Feed.Enabled", "WRITE", "Set to false by INT_FEED."),
                E("Feed.Permit", "WRITE", "Set to false by INT_FEED."),
                E("CV001.ZeroSpeed", "ZERO_SPEED"),
                OperatorStart,
                OperatorEnable,
                E("CV002.Safety", "SAFETY_RESET"),
                E("INT_CV002", "INTERLOCK_RESET"),
                E("CV002.Starter", "CONTACTOR_CLOSED"),
                E("INT_CV001", "INTERLOCK_RESET"),
                E("CV001.Start", "WRITE", "Set to false by INT_CV001."),
                E("CV001.Permit", "WRITE", "Set to true by INT_CV001."),
                OperatorStart,
                E("CV001.Starter", "CONTACTOR_CLOSED"),
                OperatorFeedReset,
                E("INT_FEED", "INTERLOCK_RESET"),
                E("Feed.Enabled", "WRITE", "Set to false by INT_FEED."),
                E("Feed.Permit", "WRITE", "Set to true by INT_FEED."),
            ],
            [
                // From the refused start to the fresh one: through the trip and through
                // the reset, CV001 never closes (the second OperatorStart ends the window).
                new(OperatorStart, E("CV001.Starter", "CONTACTOR_CLOSED"), OperatorStart),
                new(OperatorStart, E("CV001.Motor", "ENERGISED"), OperatorStart),
                new(OperatorEnable, E("Feed.Permit", "WRITE", "Set to true"), OperatorFeedReset),
                new(OperatorFeedReset, E("Feed.Enabled", "WRITE", "Set to true")),
            ]),
    };
```

(`FindAbsence` ends a window at the first match of `Until` *after* its start, so
`new(OperatorStart, …, OperatorStart)` spans exactly 100 s to 125 s: the refused
start, the trip, the reset, up to the fresh start.)

In `tests/Dse.Samples.Tests/MineConveyorTests.cs` rename
`TheScenarioFolderHoldsExactlyTheEightScenariosEachWithAGoldenAndAStory` to
`TheScenarioFolderHoldsExactlyTheNamedScenariosEachWithAGoldenAndAStory` (body
unchanged, R127), and insert before
`    [Fact]\n    public void ATraceCannotSampleFasterThanTheTimeStep()`:

```csharp
    [Fact]
    public void AStartWrittenWhileTrippedMovesNothingAndTheResetDoesNotReleaseIt()
    {
        IReadOnlyDictionary<string, IReadOnlyList<TagSample>> traces = Sample.Trace(
            "start-while-tripped", ["CV001.Speed", "CV001.TonnesPerHour", "Feed.HopperMass"], TimeSpan.FromMilliseconds(100));
        List<TagSample> Between(string tag, double from, double to) =>
            traces[tag].Where(s => s.Time >= TimeSpan.FromSeconds(from) && s.Time < TimeSpan.FromSeconds(to)).ToList();

        // The trace's last sample lands at 00:02:30 (150.000 s, the full duration);
        // guard against an empty series making Assert.All pass on nothing.
        Assert.Contains(traces["CV001.Speed"], s => s.Time >= TimeSpan.FromSeconds(149));

        // (a) and (b): from the refused start at 100 s, through the interlock's reset at 115 s,
        // to the operator's fresh start at 125 s, CV001 does not move.
        Assert.All(Between("CV001.Speed", 100, 125), s => Assert.True(
            s.Value <= 0.02, string.Create(CultureInfo.InvariantCulture, $"CV001 moved at {s.Value} m/s at {s.Time}.")));
        Assert.All(Between("CV001.TonnesPerHour", 100, 125), s => Assert.True(
            s.Value <= 5.0, string.Create(CultureInfo.InvariantCulture, $"CV001 carried {s.Value} t/h at {s.Time}.")));

        // The feeder makes nothing from 100 s to the end: not while tripped, not after its reset at 130 s.
        Assert.All(Between("Feed.HopperMass", 100, 151), s => Assert.True(
            s.Value == 0.0, string.Create(CultureInfo.InvariantCulture, $"The feeder made {s.Value} kg by {s.Time}.")));

        // (c): the fresh start at 125 s works.
        Assert.True(traces["CV001.Speed"][^1].Value >= 1.74);
    }
```

- [ ] **Step 2: Run the project to see it fail**

Run: `dotnet test tests/Dse.Samples.Tests --nologo`
Expected: 8 failed — the folder test, the five `start-while-tripped` theory rows
(golden, story, settle, replay, README quote), the README command test and the
new fact, each on the missing scenario; 64 passed.

- [ ] **Step 3: Write the scenario**

`samples/mine-conveyors/scenarios/start-while-tripped.json`:

```json
{
  "plant": "../plant.json",
  "duration": 150,
  "timeline": [
    { "at": 1,   "write": "SEQ_START.Start",   "value": true },
    { "at": 2,   "write": "SEQ_START.Start",   "value": false },
    { "at": 80,  "write": "CV002.PullKey1",    "value": true },
    { "at": 100, "write": "CV001.Start",       "value": true },
    { "at": 100, "write": "Feed.Enabled",      "value": true },
    { "at": 105, "write": "CV002.PullKey1",    "value": false },
    { "at": 106, "write": "CV002.SafetyReset", "value": true },
    { "at": 107, "write": "CV002.SafetyReset", "value": false },
    { "at": 108, "write": "INT_CV002.Reset",   "value": true },
    { "at": 109, "write": "INT_CV002.Reset",   "value": false },
    { "at": 110, "write": "CV002.Start",       "value": true },
    { "at": 115, "write": "INT_CV001.Reset",   "value": true },
    { "at": 116, "write": "INT_CV001.Reset",   "value": false },
    { "at": 125, "write": "CV001.Start",       "value": true },
    { "at": 130, "write": "INT_FEED.Reset",    "value": true },
    { "at": 131, "write": "INT_FEED.Reset",    "value": false }
  ]
}
```

- [ ] **Step 4: Generate its golden and read it**

Run: `DSE_UPDATE_GOLDEN=1 dotnet test tests/Dse.Samples.Tests --nologo --filter "FullyQualifiedName~EveryScenarioMatchesItsGolden"`
Expected: 9 passed; `git status --short samples/` shows the new golden and
scenario only (the other eight are rewritten byte-identically).

Read `samples/mine-conveyors/expected/start-while-tripped.log` in full.
Checklist (measured, R125): **132 lines**; lines 1–99 byte-identical to
`pull-key.log` (`diff <(head -99 …/pull-key.log) <(head -99 …/start-while-tripped.log)`
prints nothing); then, in order:

- `06:01:40.000  CV001.Start  WRITE  Set to true.` and `06:01:40.000  Feed.Enabled  WRITE  Set to true.`;
- `06:01:45.000  CV002.PullKey1  PULLKEY_RESET  Released.`,
  `06:01:46.000  CV002.Safety  SAFETY_RESET  All channels healthy; relay energised.`,
  `06:01:48.100  INT_CV002  INTERLOCK_RESET  Reset with all conditions normal.`,
  `06:01:48.110` `CV002.Start` false / `CV002.Permit` true by `INT_CV002`,
  `06:01:50.000  CV002.Starter  CONTACTOR_CLOSED  Motor energised.`;
- `06:01:55.100  INT_CV001  INTERLOCK_RESET  Reset with all conditions normal.`,
  `06:01:55.110  CV001.Start  WRITE  Set to false by INT_CV001.`,
  `06:01:55.110  CV001.Permit  WRITE  Set to true by INT_CV001.` — and **no
  `CV001.Starter` line until** `06:02:05.000  CV001.Start  WRITE  Set to true.`,
  `06:02:05.000  CV001.Starter  CONTACTOR_CLOSED  Motor energised.`;
- `06:02:10.100  INT_FEED  INTERLOCK_RESET  Reset with all conditions normal.`,
  `06:02:10.110  Feed.Enabled  WRITE  Set to false by INT_FEED.`,
  `06:02:10.110  Feed.Permit  WRITE  Set to true by INT_FEED.`;
- last line `06:02:11.000  INT_FEED.Reset  WRITE  Set to false.`

- [ ] **Step 5: Add scenario 9 to the sample README**

In `samples/mine-conveyors/README.md` make three edits.

`one plant file, and eight scenarios that break it in eight ways. There is no C#`
becomes
`one plant file, and nine scenarios that break it in nine ways. There is no C#`.

In the file table, the row

```markdown
| `scenarios/*.json` | the eight scenarios below |
```

becomes

```markdown
| `scenarios/*.json` | the nine scenarios below |
```

Insert, immediately before the line `## What this demo line leaves out on purpose`:

````markdown
## 9. A start while the line is tripped

An operator pulls CV002's pull-wire at 80 s, as in scenario 2, and the cascade
stops CV001 and the feeder. At 100 s, with the pull-key still out and nothing
reset, someone writes `CV001.Start` true and `Feed.Enabled` true, as a start
button on an HMI would. Both writes land, and nothing moves: when each
interlock tripped it wrote its device's `Permit` false as well as its command,
and the starter and the feeder AND their command with that permit, the way an
interlock contact sits in series in a real run circuit.

Then the line is put right, by hand: the pull-key is restored at 105 s,
CV002's safety relay reset at 106 s, `INT_CV002` reset at 108 s and CV002
started at 110 s; `INT_CV001` is reset at 115 s. That reset gives CV001 its
permit back — and, like a seal-in circuit broken by the interlock, drops the
start written at 100 s, so CV001 stays stopped. It runs only when the operator
starts it again, at 125 s. `INT_FEED`, reset at 130 s, likewise drops the
enable written at 100 s: the feeder stays off to the end of the run.

```text expected/start-while-tripped.log
06:01:20.100  INT_CV001  INTERLOCK_TRIP  CV002.Contactor abnormal.
06:01:20.110  CV001.Start  WRITE  Set to false by INT_CV001.
06:01:20.110  CV001.Permit  WRITE  Set to false by INT_CV001.
06:01:20.110  CV001.Starter  CONTACTOR_OPENED  Motor de-energised.
06:01:20.200  INT_FEED  INTERLOCK_TRIP  CV001.Contactor abnormal.
06:01:20.210  Feed.Enabled  WRITE  Set to false by INT_FEED.
06:01:20.210  Feed.Permit  WRITE  Set to false by INT_FEED.
06:01:40.000  CV001.Start  WRITE  Set to true.
06:01:40.000  Feed.Enabled  WRITE  Set to true.
06:01:55.100  INT_CV001  INTERLOCK_RESET  Reset with all conditions normal.
06:01:55.110  CV001.Start  WRITE  Set to false by INT_CV001.
06:01:55.110  CV001.Permit  WRITE  Set to true by INT_CV001.
06:02:05.000  CV001.Start  WRITE  Set to true.
06:02:05.000  CV001.Starter  CONTACTOR_CLOSED  Motor energised.
06:02:10.100  INT_FEED  INTERLOCK_RESET  Reset with all conditions normal.
06:02:10.110  Feed.Enabled  WRITE  Set to false by INT_FEED.
06:02:10.110  Feed.Permit  WRITE  Set to true by INT_FEED.
```

Between 100 s and the fresh start at 125 s, sampled every 100 ms, CV001's
speed stays under 0.01 m/s and its scale under 0.05 t/h; the feeder's hopper
stays empty from 100 s to the end. Before the permit existed, the writes at
100 s closed CV001's contactor at once and ran it at full speed onto the
stopped CV002 — and, left alone, would have filled CH1 at 114.26 s.

```bash
dotnet run --project src/Dse.Cli -- run samples/mine-conveyors/scenarios/start-while-tripped.json --expect samples/mine-conveyors/expected/start-while-tripped.log
```
````

(Measured behind the prose, R125, at 100 ms sampling: from 100 s to 125 s
CV001's speed ≤ 0.0060 m/s and its scale ≤ 0.0485 t/h; the hopper 0 kg from
100 s to 150 s; CV001 at 1.93 m/s at the end. Without the permit, on a timeline
that stops at the 100 s writes: `CONTACTOR_CLOSED` at 100.000 s and `CH1 FULL`
at 114.26 s.)

- [ ] **Step 6: Run the project and the solution**

Run: `dotnet test tests/Dse.Samples.Tests --nologo` — expect **72** passed (66 + five
theory rows + one fact). Report the new fact's measured maxima. The replay row's
recording holds exactly the sixteen timeline actions.
Run: `dotnet build Dse.sln -c Release --nologo` — expect `0 Warning(s)`, `0 Error(s)`.
Run: `dotnet test Dse.sln --nologo` — expect **1347**.

- [ ] **Step 7: Commit**

```bash
git add samples/mine-conveyors/scenarios/start-while-tripped.json samples/mine-conveyors/expected/start-while-tripped.log samples/mine-conveyors/README.md tests/Dse.Samples.Tests/Sample.cs tests/Dse.Samples.Tests/Stories.cs tests/Dse.Samples.Tests/MineConveyorTests.cs
```

```bash
git commit -m "feat(samples): show a start written while tripped moving nothing, then or after the reset" -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>"
```

---

### Task 5: Documentation

**Model:** sonnet.

**Files:**
- Test: `tests/Dse.Control.Tests/DocumentationTests.cs` (+1 fact)
- Modify: `docs/control-blocks.md` (full-size example paragraph, `Interlock` section)
- Modify: `samples/mine-conveyors/README.md` (start paragraph, run permits, sizing note, normal stop, leaves-out bullet, power-up)
- Modify: `README.md` (sample paragraph)
- Modify: `docs/superpowers/specs/2026-09-25-mine-conveyor-sample-design.md`, `docs/superpowers/specs/2026-09-22-control-blocks-design.md` (one amendment paragraph each)

**Interfaces:**
- Consumes: everything above; no code.
- Produces: nothing later tasks use.

- [ ] **Step 1: Write the failing documentation test**

In `tests/Dse.Control.Tests/DocumentationTests.cs`, insert before
`    /// <summary>Two levels up from this file is the repository root.</summary>`:

```csharp
    [Fact]
    public void TheControlBlocksPageDescribesTheResetWritesAndTheRunPermit()
    {
        string page = Page();

        foreach (string token in new[]
                 {
                     "\"reset\"", "CV001.Permit", "run permit", "seal-in", "Set to true by INT01.", "start-while-tripped",
                 })
        {
            Assert.Contains(token, page, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("does not hold its output", page, StringComparison.Ordinal);
    }
```

Run: `dotnet test tests/Dse.Control.Tests --nologo --filter "FullyQualifiedName~DocumentationTests"`
Expected: 2 passed, 1 failed — the new fact, on `"reset"`.

- [ ] **Step 2: Update `docs/control-blocks.md`**

Replace

```markdown
zero-speed switch, current alarms, and a start and a stop sequencer — with
eight scenarios and their goldens. Its
README explains each design choice; its `plant.json` is the file to copy from.
An interlock there trips once, on the trip scan, and does not hold its output
off against a later write — read the README's "leaves out on purpose" section
before wiring a new writer of a tag an interlock also writes.
```

with

```markdown
zero-speed switch, current alarms, and a start and a stop sequencer — with
nine scenarios and their goldens. Its
README explains each design choice; its `plant.json` is the file to copy from.
Its interlocks hold their devices off with the run permit described under
`Interlock` below; its scenario 9, `start-while-tripped`, writes a start while
the cascade is tripped, and nothing moves — not then, and not after the reset.
```

In the `## Interlock` section replace

````markdown
```csharp
new Interlock("INT01",
    [new Condition("CV001.Tripped", false), new Condition("PERM01.Ok", true)],
    [new BlockWrite("CV001.Start", TagValue.Bool(false))],
    TimeSpan.FromMilliseconds(100))
```

In a plant file (`trip` may be left out):

```json
{ "id": "INT01", "type": "interlock", "scanPeriodMs": 100,
  "parameters": {
    "conditions": [ { "tag": "CV001.Tripped", "normal": false }, { "tag": "PERM01.Ok", "normal": true } ],
    "trip": [ { "tag": "CV001.Start", "value": false } ] } }
```
````

with

````markdown
```csharp
new Interlock("INT01",
    [new Condition("CV001.Tripped", false), new Condition("PERM01.Ok", true)],
    [new BlockWrite("CV001.Start", TagValue.Bool(false)), new BlockWrite("CV001.Permit", TagValue.Bool(false))],
    TimeSpan.FromMilliseconds(100),
    [new BlockWrite("CV001.Permit", TagValue.Bool(true)), new BlockWrite("CV001.Start", TagValue.Bool(false))])
```

The last argument, the reset writes, may be left out: the four-argument
constructor is an interlock with none.

In a plant file (`trip` and `reset` may be left out):

```json
{ "id": "INT01", "type": "interlock", "scanPeriodMs": 100,
  "parameters": {
    "conditions": [ { "tag": "CV001.Tripped", "normal": false }, { "tag": "PERM01.Ok", "normal": true } ],
    "trip": [ { "tag": "CV001.Start", "value": false }, { "tag": "CV001.Permit", "value": false } ],
    "reset": [ { "tag": "CV001.Permit", "value": true }, { "tag": "CV001.Start", "value": false } ] } }
```
````

and replace

```markdown
scan. The latch clears on a rising edge of the `Reset` command while every
condition is normal, and on nothing else.

Events: `INTERLOCK_TRIP` — `CV001.Tripped abnormal.` — and `INTERLOCK_RESET` —
`Reset with all conditions normal.`
```

with

```markdown
scan. The latch clears on a rising edge of the `Reset` command while every
condition is normal, and on nothing else. The scan that clears it sends the
reset writes, once; a refused reset sends nothing. Within each list a tag may
appear once. A tag may be in both lists, with the same kind; the block then has
one write pin for it. `Writes` lists the trip tags first, in order, then any tag
only the reset writes — and the host sends one scan's writes in that pin order,
so the reset above logs `CV001.Start` before `CV001.Permit`.

Events: `INTERLOCK_TRIP` — `CV001.Tripped abnormal.` — and `INTERLOCK_RESET` —
`Reset with all conditions normal.`

### Holding a device off: the run permit

A trip write is sent once. On its own it drops a run command, but anything that
writes the command true again while the interlock is still tripped starts the
device. To make an interlock a true start inhibit — an interlock contact in
series in the run circuit — give the device a run permit and drive it from both
lists: on trip, the command false and `Permit` false; on reset, `Permit` true
and the command false again. `motor-starter` (and so `conveyor`, as
`CVn.Permit`) and `bulk-source` have a `Permit` input, true when unwired, ANDed
into their run logic; a welded contactor still defeats it. The reset's `false`
is the seal-in: a command written during the trip is refused while the permit
is false and forgotten when it comes back, so a device runs again only on a
fresh command after the reset. A sequence that resets an interlock and starts
its device must therefore do it in two steps — reset, wait for the interlock's
`Ok`, then command — or the reset's `false` lands after its start. Each trip and
each reset log their writes, `Set to false by INT01.` and `Set to true by INT01.`
The mine-conveyor sample uses this pattern on every interlock.
```

- [ ] **Step 3: Update the sample README's prose**

In `samples/mine-conveyors/README.md` make six replacements.

(1) The start paragraph,

```markdown
**Start downstream first.** `SEQ_START` resets the three safety relays, then
starts CV003, waits until its speed sensor reads 1.74 m/s (90 % of its running
speed), then CV002, then CV001, then enables the feeder. The feeder is declared
```

becomes

```markdown
**Start downstream first.** `SEQ_START` resets the three safety relays, then
resets CV003's interlock, starts CV003 and waits until its speed sensor reads
1.74 m/s (90 % of its running speed), then does the same for CV002, then CV001,
then resets the feeder's interlock and enables the feeder. The feeder is declared
```

(2) The end of the cascade paragraph,

```markdown
belt's overload relay (`CVn.Tripped`) and on its permissive, and it is latched:
`SEQ_START` resets it only once the belt downstream is proved. That latch
gates `SEQ_START`'s own reset step, not the run command itself — see the first
item under "What this demo line leaves out on purpose" below for what
that means for a write that does not go through `SEQ_START`.
```

becomes

```markdown
belt's overload relay (`CVn.Tripped`) and on its permissive, and it is latched:
`SEQ_START` resets it only once the belt downstream is proved.

**Run permits.** A tripped interlock does more than drop its device's command
once. It also writes the device's `Permit` false, and gives it back only on the
scan that accepts a reset. A starter closes only while its `Start`, its safety
relay and its `Permit` all allow it; the feeder makes ore only while `Enabled`
and `Permit` are both true. So while an interlock is tripped, no write of
`Start` or `Enabled`, from anywhere, starts its device. The reset that gives the
permit back also writes the command false, as a seal-in circuit does when the
interlock breaks it: a start written during the trip is forgotten, and a device
runs again only on a fresh start (scenario 9). That is why `SEQ_START` resets
each interlock one step before it starts the device — its own `Start` must land
after the reset's `false` — and each reset step waits for the interlock's `Ok`,
with a 5 s timeout.
```

(3) The fill sentence,

```markdown
the line fill (the feeder starts at 9.4 s), and injects its event at 80 s, when
CV003's scale is still rising (268 t/h); it reaches full rate at about 86 s.
```

becomes

```markdown
the line fill (ore starts moving at 10.21 s), and injects its event at 80 s, when
CV003's scale is still rising (263 t/h); it reaches full rate at about 86 s.
```

(4) In section 1,

```markdown
(each writes a `Start` that is already false) and expected: an interlock does not
know a stop was planned. Between `SEQUENCE_COMPLETE` and `SEQ_STOP` nothing
```

becomes

```markdown
(each writes a `Start` that is already false, and takes the belt's permit away
until the next `SEQ_START`) and expected: an interlock does not know a stop was
planned. Between `SEQUENCE_COMPLETE` and `SEQ_STOP` nothing
```

(5) The first "leaves out on purpose" bullet,

```markdown
- An interlock here trips once: it writes its trip values on the trip scan
  only and does not hold the output off, unlike a PLC interlock ANDed into the
  run rung. Anything that later writes `Start` or `Enabled` true bypasses it.
  In this plant only `SEQ_START` writes those tags, and it resets each
  interlock just before it writes them; treat any new writer — an HMI start
  button, a second sequence — as unprotected until the interlock gates it too.
```

becomes

```markdown
- `CVn.Start` and `Feed.Enabled` are held values, not momentary push-buttons:
  the interlock's reset writes stand in for the seal-in contact a real run
  circuit has. Anything that writes one of them true *after* the reset starts
  the device, exactly as `SEQ_START` does; that is the fresh start a real
  operator gives, but nothing here asks who gave it.
```

(6) The end of "Power-up",

```markdown
starts open. `INT_FEED` writes `Feed.Enabled` false — which it already is: the
feeder is declared disabled, so no ore moves until `SEQ_START` enables it at
9.41 s.
```

becomes

```markdown
starts open. Each writes its device's command false and its `Permit` false at
0.010 s. `Feed.Enabled` is false already: the feeder is declared disabled, so
no ore moves until `SEQ_START`, a step after resetting `INT_FEED`, enables it at
10.21 s.
```

- [ ] **Step 4: Update the root README**

In `README.md` replace

```markdown
permissives and alarms, and eight scenarios — a normal start and stop, a
pull-key, an e-stop, an overload, a blocked chute, a failed zero-speed switch, a
welded contactor and a starved feed — each with its golden log. It is data only: no C#. The second
```

with

```markdown
permissives and alarms, and nine scenarios — a normal start and stop, a
pull-key, an e-stop, an overload, a blocked chute, a failed zero-speed switch, a
welded contactor, a starved feed and a start written while the line is tripped —
each with its golden log. It is data only: no C#. The second
```

- [ ] **Step 5: Add the amendment lines to the 6a and 5c specs**

In `docs/superpowers/specs/2026-09-25-mine-conveyor-sample-design.md`, after the
paragraph that begins `**Amended 2026-09-25 (6a.1).**` (ending `regenerated
goldens only; no engine change.`), insert a blank line and:

```markdown
**Amended 2026-09-25 (6c).** The interlocks now hold their devices off:
`motor-starter` (so `CVn.Permit`) and `bulk-source` gained a `Permit` input;
each interlock writes it false on trip, and on its accepted reset writes it true
and drops the command, so a device restarts only on a fresh start. `SEQ_START`
resets each interlock one step before it starts the device (nine steps). A ninth
scenario, `start-while-tripped`, shows a start written during a trip moving
nothing, then or after the reset. See
`2026-09-25-interlock-start-inhibit-design.md` and its plan (R122–R132).
```

In `docs/superpowers/specs/2026-09-22-control-blocks-design.md`, after the first
paragraph (`Date: 2026-09-22. Addendum to … both merged.`), insert a blank line
and:

```markdown
**Amended 2026-09-25 (6c).** The interlock gained optional reset writes, sent
once on the scan that accepts a reset. With a device's `Permit` written false on
trip, and true together with the command false on reset, it is a true start
inhibit with seal-in behaviour. R71's "trip writes on the trip scan only" is
unchanged. See `2026-09-25-interlock-start-inhibit-design.md` and its plan
(R122–R132).
```

- [ ] **Step 6: Run everything**

Run: `dotnet test tests/Dse.Control.Tests --nologo --filter "FullyQualifiedName~DocumentationTests"` — expect 3 passed.
Run: `dotnet build Dse.sln -c Release --nologo` — expect `0 Warning(s)`, `0 Error(s)`.
Run: `dotnet test Dse.sln --nologo` — expect **1348**: 37 / 461 / 132 / 56 / 211 /
163 / 76 / 117 / 23 / 72. The sample README quote tests still pass (Step 3
touched prose only).
Run: `git grep -n PackageReference -- 'src/*.csproj'` — expect no output.
Run: `git diff --stat aa3c403 -- src/` — expect exactly the five files of the
Global Constraints.

- [ ] **Step 7: Commit**

```bash
git add tests/Dse.Control.Tests/DocumentationTests.cs docs/control-blocks.md samples/mine-conveyors/README.md README.md docs/superpowers/specs/2026-09-25-mine-conveyor-sample-design.md docs/superpowers/specs/2026-09-22-control-blocks-design.md
```

```bash
git commit -m "docs: describe the interlock's reset writes and the run permit" -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>"
```

---

## Spec coverage

| Spec | Requirement | Task | Test(s) |
|---|---|---|---|
| §1 criterion 1 | starter with `Permit` false stays open under a command; closes when true; weld defeats it | 1 | `AFalsePermitHoldsTheContactorOpenWhateverTheCommand`, `AWeldedContactorStaysClosedWithoutAPermit`, `APermitWrittenFalseHoldsTheBeltStoppedUntilItIsWrittenTrue` |
| §1 criterion 2 | source with `Permit` false creates no mass | 1 | `ASourceWithoutItsPermitCreatesNothingWhateverEnabledSays` |
| §1 criterion 3 | reset writes once, on the accepted reset scan, never on a refused one | 2 | `TheResetWritesGoOutOnTheAcceptedResetScanOnly`, `ARefusedResetSendsNoResetWrite`, `TheTripScanSendsTheTripWritesAndNoResetWrite`, `AResetWriteLandsTheTickAfterTheResetScanAndIsNotRecorded` |
| §1 criterion 4 | scenario 9; the eight still tell their stories | 3, 4 | `EveryScenarioTellsItsStory` (nine rows), `AStartWrittenWhileTrippedMovesNothingAndTheResetDoesNotReleaseIt` |
| §1 criterion 5 | defaults change nothing; catalogue/schema goldens by additions | 1, 2 | untouched event-log goldens; Task 1 Step 7, Task 2 Step 7 checklists; `WithoutResetWritesThePinsAreExactlyTheTripWrites` |
| §1 criterion 6 | no packages; `Dse.Control` references; 0 warnings; tests pass | all | Task 5 Step 6 commands; R127 for the eight moved assertions |
| §2 | starter `Permit` port and tag; `closed` formula; no refusal event | 1 | `AFalsePermitHolds…` (`Assert.Empty(Events)` before the permit) |
| §2 | conveyor exposes `CVn.Permit` | 1 | `TheConveyorPublishesExactlyItsFace`, `APermitWrittenFalseHolds…` |
| §2 | bulk-source `Permit`; `enabled` parameter unchanged | 1 | `ASourceWithoutItsPermit…`; 6a's `ASourceIsEnabledAtPowerUpUnlessTheFileSaysOtherwise` unchanged |
| §2 | descriptors and conformance | 1 | components golden; the existing conformance sweep |
| §3 | reset writes validated like trip writes (one per tag; not null; no blank tag; one kind per tag, R123) | 2 | `TheConstructorRejectsTwoResetWritesToOneTag`, `TheConstructorRejectsNullResetWrites`, `TheConstructorRejectsAResetWriteToABlankTag`, `TheConstructorRejectsATagCommandedAsTwoKinds`, DSE111 fixture |
| §3 | `Writes` = union, one pin per distinct tag | 2 | `ATagInBothListsIsOneWritePinAfterTheTripPins`, `AResetOnlyTagGetsItsOwnPinAfterTheTripPins` |
| §3 | catalogue `reset: [BlockWrite]`, default empty, spec wording | 2 | `AnInterlocksResetWritesBindAndShareAPinWithItsTripWrites`, control-catalogue and schema goldens |
| §3 | existing constructor kept as an overload | 2 | every existing `new Interlock(…, Period)` call compiles; `WithoutResetWrites…` |
| §3 | documented pattern in `docs/control-blocks.md` | 5 | `TheControlBlocksPageDescribesTheResetWritesAndTheRunPermit` |
| §4 | plant table: four interlocks' trip and reset writes | 3 | re-pinned stories; golden checklist |
| §4 (amended) | `SEQ_START` resets each interlock a step before it starts the device; measured, margin reported | 3 | R124; the re-pinned start-up chain, its three `Until` absences and `SEQUENCE_FAULTED` absence |
| §1 (amended) | a reset never restarts a device — the reset writes drop the command | 3, 4 | the `Set to false by INT_…` reset writes in the chains; scenario 9's (b) half |
| §4 | eight goldens regenerated and read; chains re-pinned | 3 | Step 5 checklist; `Stories.cs` |
| §4 (amended) | scenario 9: (a) refused while tripped, (b) not released by the reset, (c) a fresh start works; chain, absences, state check, golden, replay, README | 4 | the five theory rows and `AStartWrittenWhileTrippedMovesNothingAndTheResetDoesNotReleaseIt` |
| §4 | README bullet replaced by how the inhibit works | 5 | Step 3 (1) and (4) |
| §5 | test list | 1–5 | as above |
| §6 | `docs/control-blocks.md`; sample README; authoring page only if it lists ports; 6a and 5c amendment lines | 5 | R131; Step 5 |

## Test-count arithmetic

| After | Io.Abs | Core | Components | Realtime | Configuration | Scenarios | Cli | Control | Control.Cat | Samples | Total |
|---|---|---|---|---|---|---|---|---|---|---|---|
| `aa3c403` | 37 | 461 | 128 | 56 | 209 | 163 | 76 | 105 | 22 | 66 | 1323 |
| Task 1 | | | +4 → 132 | | | | | | | | 1327 |
| Task 2 | | | | | +2 → 211 | | | +11 → 116 | +1 → 23 | | 1341 |
| Task 3 | | | | | | | | | | +0 | 1341 |
| Task 4 | | | | | | | | | | +6 → 72 | 1347 |
| Task 5 | | | | | | | | +1 → 117 | | | 1348 |

Task 1: 2 starter facts, 1 conveyor fact, 1 source fact (the eight R127
assertions move but add no test). Task 2: 10 `InterlockTests` facts (two of them R123's null and blank reset-list checks), 1
`HostTests` fact, 1 `BlockFactoryTests` fact, and 2 corpus rows
(`CorpusTests.EveryInvalidPlantYieldsExactlyTheCodeInItsName`,
`SchemaAgreementTests.TheSchemaRejectsStructuralErrorsAndOnlyThose`). Task 4: five
theories gain a row each (`EveryScenarioMatchesItsGolden`,
`EveryScenarioTellsItsStory`, `EveryScenarioSettlesAtLeastFiveSecondsBeforeItEnds`,
`EveryScenarioReplaysByteForByteFromARecording`,
`SampleReadmeTests.EveryQuotedLineIsAWholeLineOfItsGolden`) plus one fact. Task 5:
one documentation fact. Measured in scratch: 1346 before the two R123 facts were added; 1348 with them.
