# Wheel Line Sample Implementation Plan (plan 6b.2)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship the wheel-line reference sample of main spec §15.2 as a data
folder — `samples/wheel-line/` holding a plant file with five controllers, six
scenarios, their golden event logs and a README — and grow
`tests/Millrace.Samples.Tests` to run every scenario through `millrace run --expect`,
assert its causal chain and absences, check its quiet tail, its mass balance
and its byte-for-byte replay, and prove on live runs what the log cannot show:
a held billet over-soaking only while the press is slow, the belt filling and
draining, and over-soaked billets becoming wheels when the kicker sticks.

**Architecture:** No change under `src/` (spec §2). The sample is JSON the `millrace`
command line of plans 5a–6b.1 already runs. The test project gains a
`WheelLine` helper beside the mine conveyors' `Sample` (R180) — its own paths,
names, cached runs and a live `Watch` that calls back after every tick — and
reuses `Sample.Catalogue`, `Sample.Updating`, `Sample.Schedule`, `Cli`,
`CausalChain`, `EventPattern`, `Absence` and `Story` unchanged. Every existing
test, sample file and golden is unchanged; the only existing files that change
are `tests/Millrace.Samples.Tests/Millrace.Samples.Tests.csproj` (one `None` item),
`README.md`, `docs/control-blocks.md` and the main spec.

**Tech Stack:** .NET 10 (`net10.0`, SDK 10.0.401), C#, xUnit 2.9.3. No package
under `src/`. JsonSchema.Net 8.0.5, test-only and pinned (unchanged).

**Spec:** `docs/superpowers/specs/2026-10-02-wheel-line-sample-design.md` (all
of it, as amended by this plan — R183 gives the amendment note the controller
adds to the spec with the plan commit), on top of
`docs/superpowers/specs/2026-09-30-discrete-reject-physics-design.md` (6b.1:
`heatWhileHeld`, `slow-cycle`, `reject-gate`, `coil`, R168, R169) and following
the shape of `docs/superpowers/specs/2026-09-25-mine-conveyor-sample-design.md`
(6a).

**Plan sequence:** This is plan 6b.2. Plans 1–5d, 6a, 6a.1, 6c, 6d, 6e and 6b.1
are merged on `master`; this plan starts from `4c77cb7` (the commit that added
the 6b.2 spec). Measured on that commit with `dotnet test Millrace.sln`: **1506
tests**, all passing — 37 `Millrace.Io.Abstractions` / 498 `Millrace.Core` / 176
`Millrace.Components` / 57 `Millrace.Realtime` / 239 `Millrace.Configuration` / 167
`Millrace.Scenarios` / 78 `Millrace.Cli` / 149 `Millrace.Control` / 27 `Millrace.Control.Catalogue`
/ 78 `Millrace.Samples`. Release build `0 Warning(s)`, `0 Error(s)`.

**Task shape.** Four tasks, sequential, each leaving the whole suite green:

- **Task 1 — the plant.** `plant.json`, the test project's copy of the sample,
  the `WheelLine` helper, and the plant's tests: validate, loader, schema, the
  two claims in `millrace tags`, and a scenario refused for writing a claimed tag.
- **Task 2 — the six scenarios.** The scenario files, their goldens (generated
  and read), the six stories, and the per-scenario theories: line start,
  golden, story, quiet tail, mass balance, replay.
- **Task 3 — the live-run facts.** The R168 timing of the reject decision, the
  slow-press causal chain told by temperatures and the belt's count, and the
  stuck kicker's over-soaked wheels. No file but the test file changes.
- **Task 4 — the documentation.** The sample README, its quote tests, the root
  `README.md`, `docs/control-blocks.md` and the main spec's §4 and §15.2.

## Global Constraints

- **No change under `src/`** (spec §2). After every task,
  `git diff --stat 4c77cb7 -- src/` prints nothing. If a scenario seems to need
  an engine change, stop and report it as a blocker; do not edit `src/`.
  `git grep -n PackageReference -- 'src/*.csproj'` prints nothing.
- **Existing tests and files are unchanged** except
  `tests/Millrace.Samples.Tests/Millrace.Samples.Tests.csproj` (Task 1), `README.md`,
  `docs/control-blocks.md` and
  `docs/superpowers/specs/2026-09-02-industrial-process-simulation-engine-design.md`
  (Task 4). No existing golden moves: every `samples/mine-conveyors/expected/*.log`
  and every golden under `tests/` is byte-identical after every task.
- **Goldens are generated and read, never invented or hand-edited.** A
  wheel-line golden is written by `WheelLineTests.EveryScenarioMatchesItsGolden`
  itself when `MILLRACE_UPDATE_GOLDEN=1`: it runs `millrace run <scenario> --out
  samples/wheel-line/expected/<name>.log` in-process, at the source path. Run
  the update **for this project and this theory only** —
  `MILLRACE_UPDATE_GOLDEN=1 dotnet test tests/Millrace.Samples.Tests --nologo --filter "FullyQualifiedName~WheelLineTests.EveryScenarioMatchesItsGolden"`
  — never over the whole solution, which would rewrite every other project's
  goldens, and never with a filter that also matches
  `MineConveyorTests.EveryScenarioMatchesItsGolden`. Then **read each new
  golden in full** with a file-reading tool, check it against the task's
  checklist, quote the checked lines in the task report, and run the project
  again *without* the variable: the rebuild copies the new goldens to the test
  output, where `--expect` reads them (a `bin/` copy is stale until then). A
  failed `--expect` leaves a git-ignored `<name>.log.actual` beside the output
  copy: `git status --short --ignored -- 'tests/**/*.actual' 'samples/**/*.actual'`
  must list nothing before a commit. If a golden disagrees with a checklist,
  report the measured line and say which changed and why; never adjust a
  checklist, a story, a number or a duration to fit without saying so.
- **Report every measurement.** Where an expected value in this plan (a test
  count, a line count, a time, a temperature, an id) disagrees with what the
  code produces, report the measured value in the task report; never widen a
  window or edit an assertion to fit without saying so.
- **Determinism.** No `System.Random`, no `string.GetHashCode()`, no
  `Dictionary`/`HashSet` iteration order reaching an assertion or an output
  (the tests use `SortedSet`/`SortedDictionary` and ordinal sorts). String
  comparisons are ordinal; formatting uses `CultureInfo.InvariantCulture`.
- **Warnings are errors** (`Directory.Build.props`: `TreatWarningsAsErrors`,
  `EnforceCodeStyleInBuild`, `GenerateDocumentationFile`, `Nullable`). The
  Release build prints `0 Warning(s)` and `0 Error(s)` after every task:
  `dotnet build Millrace.sln -c Release --nologo`.
- **xUnit analyzers run under warnings-as-errors:** prefer `Assert.Single`,
  `Assert.Contains`, `Assert.DoesNotContain`, `Assert.Empty`, `Assert.All`;
  never `Assert.True(x.Any())` or `Assert.Equal(1, x.Count())`. Theories take
  their rows from `WheelLine.Scenarios` (a `TheoryData<string>`).
- **Test names** are long descriptive PascalCase sentences, like the existing
  ones.
- **Messages are verbatim.** Copy every JSON file, message, story fragment and
  README line in this plan byte for byte, including the em dash (`—`, U+2014),
  the degree sign in `°C`, the ceiling brackets `⌈ ⌉`, the middle dot `·` and
  the ellipsis `…`.
- **Timing rule** (it shapes every golden here): the step is 100 ms and every
  block scans every 100 ms. A scan at tick N sees the image published at the
  end of tick N−1 (its first scan, at tick 0, sees the *primed* image); its
  queued writes land at phase 1 of tick N+1, so a block's write is logged
  100 ms after the scan that made it. A scenario `write` at `at` lands at
  phase 1 of tick `at / 100 ms`; since a scan reads the image published at the
  end of the tick before, the tick-0 scan never sees a t = 0 write. In phase 3
  the flow graph visits nodes most-downstream first, so a billet the gate
  releases on tick N is replaced by the furnace's on the same tick N.
- **Names.** Tag names are exactly what `millrace tags samples/wheel-line/plant.json`
  prints and match ordinally. A scenario's file name, its golden's file name
  and its `WheelLine.Names` entry are the same kebab-case word.
- **JSON and Markdown files** are written exactly as this plan shows them: LF
  line endings, two-space indentation, a final newline, no tabs.
- **Git, for every task.** One git command per `Bash` call. `git add` names
  paths explicitly — never `git add -A`, never `git add .`. **Never `git
  stash`.** Commit messages are conventional (`test(samples): …`): a subject
  line, a blank line, a body wrapped at about 78 columns, and the trailer as
  the last line, copied verbatim:

  ```
  Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
  ```

  The trailer identifies the **session** that planned and drives this work,
  not the model that implements a given task: it is the same on every commit,
  whichever implementer model a task names. Never put it on the subject line.
  Write each message with the Write tool to `.superpowers/sdd/6b.2/msg-taskN.txt`
  and commit with `git commit -F`; then run
  `git log -1 --format='%h%n%s%n--%n%b'` and check the subject, the blank line
  and the trailer.
- **Commands**, from the repository root: `dotnet build Millrace.sln -c Release --nologo`
  (expect `0 Warning(s)`, `0 Error(s)`) and `dotnet test Millrace.sln --nologo`
  (expect the task's total). `.superpowers/` is git-ignored; scratch work and
  commit messages go under `.superpowers/sdd/6b.2/` and are never added.
  Inside a worktree the harness refuses Bash text that mentions git inside a
  heredoc, `$(…)`, a variable or a loop, and any complex command containing
  "Github" (the repository path): write files with the Write tool and run each
  command as the plain command this plan shows.

## Review Focus

The five input classes the spec implies but does not name, most likely to bite
first. Each has its pinning test in the owning task.

1. **The line before the line-start writes land.** At tick 0 every block scans
   the *primed* image: `FCE.ZONE_SP` is still 20 °C (an input's default),
   `Bay.Full` false, `ALM_PYRO.HiHi.Active` false. The author expects the
   operator's writes to bring the line up with no alarm, no trip and exactly
   one coil write — `Set to false by COIL_REJECT.` at 0.100 s — in every
   scenario. Measured: without its on-delay `ALM_ZONE` raises `Lo: 20.0 below
   1200.` at 0 s (R174). Tests: Task 2,
   `WheelLineTests.EveryScenarioOpensWithTheLineStartAndTheCoilsFirstScan` (6
   rows: the first three log lines exactly), the `zone-low` absence of any
   `ALM_ZONE` raise before the 1050 °C write, and the `normal-run` absences (no
   `ALARM_RAISED`, no `INTERLOCK_TRIP`, no second `GATE.Reject` write).
2. **A billet that reaches the gate on the very tick the one before it
   leaves.** The over-soaked billet lands on the gate in the tick the good one
   departs, so the pyrometer never reads an empty station between them; the
   author expects the decision to land with ticks to spare (R168) and
   `Reject` to fall again before the next billet. Tests: Task 3,
   `TheRejectDecisionLandsSevenTicksBeforeTheOverSoakedBilletLeaves` (HiHi at
   N + 2, the write at N + 4, `REJECTED` at N + 11); Task 2, the `slow-press`
   chain (`Set to false by COIL_REJECT.` after `REJECTED`) and its absence of a
   second `REJECTED`.
3. **An over-soak the pyrometer cannot see.** A good billet waiting on the gate
   for room on the belt hides the over-soaking billet behind it in the furnace.
   The author expects no pyrometer alarm and no reject while a jam lasts, and
   over-soak only while the press is slow. Tests: Task 2, the `press-jam`
   absences (no `ALM_PYRO` raise and no `REJECTED` between the jam and its
   clear); Task 3,
   `ASlowPressOverSoaksTheHeldBilletOnlyWhileItIsSlowAndTheBeltFillsAndDrains`
   (every sample past 1150 °C lies between 300 s and 1140 s).
4. **A commanded reject that does not happen, and an item's identity through
   the press's re-type.** With the kicker stuck the coil still writes `Reject`
   true; the author expects the over-soaked billets to leave as wheels, found
   by id, and nothing in the bay. Test: Task 3,
   `AStuckKickerTurnsTheOverSoakedBilletsIntoWheels` (ids 13 and 14 arrive in
   `Wheels` as `Wheel`; `Bay.Count` 0; `GATE.Rejected` 0); Task 2, the
   `stuck-kicker` absence of `REJECTED`.
5. **A full reject outlet, a refused reset, and a queue alarm that must not
   chatter.** When the bay is full, `INT_BAY` must stop the saw and a reset must
   be refused while `Bay.Full` stays true; `ALM_QUEUE`, watching a detector
   whose reading drops whenever the queue shifts, must raise once and clear
   once. Tests: Task 2, the `pyro-fail-high` story (`FULL`, the trip, `Set to
   false by INT_BAY.`, the reset written, no `INTERLOCK_RESET`, no billet into
   the furnace after the trip, `Reject` never falling) and the absence "no
   `ALM_QUEUE` raise after its clear" in `slow-press`, `stuck-kicker` and
   `press-jam`.

Also pinned, beyond the five: every scenario conserves mass on every tick and
accounts for every billet as a wheel, a reject or line content
(`EveryScenarioConservesMassOnEveryTick`); a scenario that writes either
claimed tag is refused with `MR206` before tick 0
(`AScenarioThatWritesAClaimedTagIsRefusedBeforeTickZero`); the README's dwell
arithmetic is read back against the plant file
(`TheDwellArithmeticIsThisPlantsAndTheLineStartIsEveryGoldensFirstThreeLines`).

## Decisions settled here (rulings R171–R183)

These refine the spec where the code, or a measured run, forced a choice. R183
gives the amendment note that records them in the spec.

- **R171 — The line's numbers.** Chosen and measured on a scratch copy of
  `4c77cb7` (Measurements): a 100 ms step and 100 ms scans everywhere; one
  400 kg billet at 25 °C every 60 s, a two-billet charging table
  (`queueCapacity` 2); a one-billet furnace (`batchSize` 1, `heatWhileHeld`),
  `thermal-transfer` with a 24 s time constant toward a 1250 °C zone,
  discharging at `temperature-at-least` 1100 °C (50.2 s from charge); a 1 s
  gate dwell (R175); a 4-billet reject bay; a 10 m belt at 0.5 m/s with 2 m
  minimum spacing (it holds six) cooling with a 1200 s time constant; a press
  with a 40 s `for-seconds` cycle forming `Wheel` at 0.92 yield. `ALM_PYRO`:
  `Hi` 1130 °C and `HiHi` 1150 °C, deadband 10 °C, no on-delay. Spec criterion
  4 holds, measured: every billet reaches the gate at 1100.0226 °C (29.98 °C
  under `Hi`); a billet held in `slow-press` reaches 1249.999 °C (99.999 °C
  above `HiHi`); the belt holds one blank at most in steady running. A batch
  of one keeps every billet at its own target: in a larger batch the first
  billet in would over-soak waiting for the last.
- **R172 — The saw sets the line's rate; the press has a third in hand.** Spec
  criterion 5 says `normal-run` makes "wheels at the press rate". A press no
  faster than the line can never clear a queue — after `slow-press`'s clear
  the belt would stay full forever — so the press's 40 s cycle is shorter than
  the saw's 60 s, and steady running makes one wheel per billet, one a minute
  (measured: ten wheels from ten billets, `Hold satisfied after 40.00 s` each).
  The README says so.
- **R173 — `ALM_QUEUE` watches a hot-metal detector, not `CV.ItemCount`.** An
  `alarm` reads a Double tag (`AlarmCatalogue`: `Param.Tag("input", …,
  TagKind.Double)`), and a `discrete-belt`'s `ItemCount` port is an `int`, which
  the `tags` envelope publishes as Int64. Measured: `millrace validate` on an
  `ALM_QUEUE` with `"input": "CV.ItemCount"` reports `MR114
  $.controllers[2].parameters.input` — `'CV.ItemCount' is an Int64 tag, but
  'input' needs a Double tag.` No catalogue component converts an Int64 to a
  Double, and a signal link between different value types is `MR109`. The
  smallest data-only way round is the instrument real lines use: `HMD`, a
  `pyrometer` aimed at `CV` at 4 m (`windowM` 0.1, `lagSeconds` 5) — blanks
  queue 2 m apart from the 10 m head, so a blank standing at 4 m means four
  are queued — and `ALM_QUEUE` `Hi` 200 °C, deadband 50 °C, on-delay 60 s.
  Measured: a passing blank lifts the lagged reading to 103.284 °C at most
  (steady running never raises); a standing blank raises it to about 1044 °C;
  the 60 s on-delay waits out the transient queues a 40 s press cycle leaves
  while the backlog drains (with 30 s, `press-jam` raised and cleared again
  2.6 s apart at 06:27:30); the 5 s lag holds the reading above the 150 °C
  clear level through the 4 s gap when the queue shifts up one place (no
  chatter in `slow-press`). Measured with `lagSeconds` 0: `slow-press` raises
  at 06:09:59.600, clears at 06:11:52.200 (`Hi: 20.0 back within limits.`)
  when the queue shifts with the belt still full, raises again at
  06:12:55.800, clears at 06:18:32.400, and raises and clears 1.7 s apart at
  06:19:36.000; `press-jam` clears on the clear's first shift. `CV.ItemCount` stays read-bound through the `tags`
  envelope (spec criterion 1): `millrace tags` lists it and Task 3's slow-press
  fact traces it. The spec's criterion 2 and 4 wording is amended (R183).
- **R174 — `ALM_ZONE` has a 5 s on-delay because the first scan reads the
  primed image.** `FCE.ZONE_SP` primes at the `AmbientTemperature` default,
  20 °C, and the operator's t = 0 write is not in the image the tick-0 scan
  reads. Measured in `normal-run` with no on-delay:
  `06:00:00.000  ALM_ZONE  ALARM_RAISED  Lo: 20.0 below 1200.` and
  `06:00:00.100  ALM_ZONE  ALARM_CLEARED  Lo: 1250.0 back within limits.`
  With 5 s neither appears, in any scenario. `ALM_ZONE` reads the
  write-bound `FCE.ZONE_SP` with no diagnostic (measured): an alarm may watch
  a read-write tag. `Lo` 1200 °C is where the furnace stops keeping up:
  measured, a 1200 °C zone heats a billet in 59.0 s, inside the saw's 60 s;
  `Hi` 1300 °C is never reached. `INT_BAY` does not trip at power-up: its
  condition `Bay.Full` primes false, its normal value (measured: no
  `INTERLOCK_TRIP` in `normal-run`).
- **R175 — The dwell, by R168: 1 s.** With dt = 0.1 s, a = c = 1, no `HiHi`
  on-delay and `PYRO` `lagSeconds` 0: ⌈1.0 / 0.1⌉ = 10 ≥ a + c + 1 + 0 = 3,
  seven ticks to spare (R168 asks for one). Measured in `slow-press`: the
  over-soaked billet lands on the gate at 06:18:36.000 (tick N = 11160, the
  tick `FCE` logs `IDLE`), the pyrometer reads it at N + 1, `HiHi` raises at
  N + 2 (06:18:36.200), the coil's write lands at N + 4 (06:18:36.400) and
  `REJECTED` is logged at N + 11 (06:18:37.100); the alarm clears at N + 13 and
  `Reject` falls at N + 15 (06:18:37.500), 49 s before the next billet. The
  same offsets hold in `press-jam` (06:20:04.000 → .200, .400, 06:20:05.100)
  and, from the deposit at 06:05:50.400, `pyro-fail-high`'s first reject at
  06:05:51.500. `PYRO`'s `"lagSeconds": 0` is written explicitly in the plant
  so the README's arithmetic can be read back from it.
- **R176 — A producing scenario ends its shift by starving the saw.** Spec
  criterion 5 asks for a quiet tail of at least 500 ticks (50 s). A running
  line logs four `FCE` and four `PRESS` phase events per billet, so its log
  never falls quiet; and `Billets.Enabled` is claimed by `INT_BAY`, so a
  scenario cannot write it (`MR206`, pinned). `normal-run`, `slow-press`,
  `stuck-kicker` and `press-jam` therefore inject `Billets` `starve` (at 600 s,
  2100 s, 2100 s, 2100 s), the line runs empty, and the run goes on quietly;
  `zone-low` and `pyro-fail-high` stop by themselves. Measured quiet tails:
  882, 882, 775, 882, 2482 and 990 ticks. Durations: 800, 2300, 2300, 2300,
  600, 700 s.
- **R177 — A jam cannot fill the bay; the bay fills in `pyro-fail-high`.** The
  spec's `press-jam` row expects rejects to accumulate until `Bay` is `FULL`.
  Measured, they cannot: once the belt is full a good billet waits on the gate
  for room, the furnace holds the next billet behind it, and that billet —
  over-soaking, 1250 °C — never reaches the pyrometer while the jam lasts; the
  gate's own billet is at its target, so nothing is rejected (06:12:50.300 to
  the end of a 3000 s run with no clear: no `ALM_PYRO` event, no `REJECTED`).
  No scenario can empty an `item-sink` either: it has no fault, no writable tag
  and no capacity reset. The smallest scenario-level resolution:
  - `press-jam` jams at 300 s and is cleared at 1200 s; its story is the hidden
    over-soak — no pyrometer alarm and no reject while jammed — and the reject
    of the billet held 433.7 s as soon as the clear moves the belt (`Item 12
    rejected.` at 06:20:05.100); the queue drains and production recovers.
  - `pyro-fail-high` carries the bay: every billet is rejected (items 5–8),
    `Bay` `FULL` after the fourth, `INT_BAY` trips and writes
    `Billets.Enabled` false, the saw stops and the line runs dry. The
    operator's `INT_BAY.Reset` at 600 s is refused (the interlock resets only
    when every condition is normal), and the scenario ends with the line held.
  The README says both, and why.
- **R178 — Scenario timing.** Every scenario writes `FCE.ZONE_SP` 1250 and
  `CV.SPEED_SP` 0.5 at 0 s (6b.1 R169). Faults go in at 300 s, after three
  wheels (`slow-cycle` 0.9 in `slow-press` and `stuck-kicker`, `discharge-jam`,
  `fail-high`); `stuck-kicker` sticks the kicker at 600 s, before the first
  over-soaked billet reaches the gate; `slow-cycle` is cleared at 1140 s, 23 s
  after `slow-press`'s first reject, so the press's 400 s cycle does not hold a
  second billet long enough to over-soak; the jam is cleared at 1200 s;
  `zone-low` writes 1050 °C at 330 s; `pyro-fail-high` writes `INT_BAY.Reset`
  true at 600 s and false at 601 s. Seed 11 (nothing in the plant draws a
  random number); start time `2026-04-06T06:00:00+02:00`.
- **R179 — `ALM_PYRO`'s `Hi` raises with `HiHi`.** The spec gives the alarm a
  warning (`Hi`) and an over-soak (`HiHi`) limit. A billet reaches the gate
  either at its target or already soaked to the zone (the pyrometer reads the
  empty station's 20 °C between billets, so the reading jumps), so in every
  scenario the two raise in the same scan. Both are kept, as the spec asks;
  `COIL_REJECT` follows `HiHi` only.
- **R180 — The test side: a `WheelLine` helper beside `Sample`, not a
  generalised `Sample`.** `Sample` hard-codes the mine conveyors (its `Root`,
  `Names`, `FindSourceRoot`), `MineConveyorTests` and `SampleReadmeTests` read
  it throughout, and `MineConveyorTests.TheScenarioFolderHoldsExactly…`
  compares `Stories.All`'s keys with `Sample.Names`. Generalising would rewrite
  every mine-conveyor test; a sibling is smaller and leaves them unchanged.
  `WheelLine.cs` copies `Sample`'s path members and its run cache (about 40
  lines) and reuses `Sample.Catalogue`, `Sample.Updating` and `Sample.Schedule`.
  `Sample.Trace` cannot serve: it reads every tag `AsDouble`, which throws on
  the Int64 `CV.ItemCount`, and the stories need item truth no tag carries (a
  billet's temperature in the furnace, the ids reaching a sink). So `WheelLine`
  has `Watch(name, afterEachTick)` — load, schedule through `Sample.Schedule`,
  tick to the end, call back after every tick, return the simulation — and
  `Component<T>(simulation, id)`. The stories live in `WheelLineStories`, the
  quote tests in `WheelLineReadmeTests`; the csproj gains one `None` item that
  copies `samples/wheel-line/**` to `wheel-line/` in the output.
- **R181 — What the live-run facts read.** The slow-press fact samples, after
  every tick, the hottest billet held in `FCE` or standing on `GATE` (model
  truth: the over-soak happens in the furnace, where no instrument looks),
  `CV.ItemCount` (`AsInt64`) and whether `FCE.Phase` reads Discharging. The
  stuck-kicker fact collects the ids of billets standing on the gate above
  1150 °C (measured {13, 14}: 13 at 1250.00 °C, 14 at 1169.49 °C) and the id
  and material of every item `Wheels` receives (`ItemSink.LastItem`; the press's
  `ChangeType` keeps the id). The mass fact checks `|MassBalance.Drift|` after
  every tick (measured 0 in all six) and that every billet made is a wheel at
  its pre-flash mass (`Wheels.MassReceived / 0.92`), a reject
  (`Bay.MassReceived`) or still held.
- **R182 — Documentation.** Measured by grep over `README.md`, `docs/`
  (excluding `docs/superpowers/`) and `samples/`: the samples are listed in the
  root `README.md` (status paragraph, command-line block, the closing "See
  the …" list) and in `docs/control-blocks.md` (*The full-size example*); no
  other page lists them (`docs/architecture.md`, `docs/scenarios.md` and the
  diagnostics pages do not change). The main spec's header, §4's sample list and
  §15.2 are amended. `WheelLineReadmeTests.TheRootReadmeTheControlBlocksPageAndTheMainSpecNameTheWheelLine`
  pins all three; `DocumentationTests.EveryLogLineThePageQuotesIsAWholeLineOfTheWorkedExampleGolden`
  still finds exactly three timestamped lines in `docs/control-blocks.md`
  because the new paragraph quotes none.
- **R183 — The spec amendment.** The controller adds this note to the 6b.2 spec,
  under its title, with the plan commit (as 6c, 6d, 6e and 6b.1 did; no task
  edits the 6b.2 spec):

  ```markdown
  **Amended 2026-10-02 by the plan**
  (`docs/superpowers/plans/2026-10-02-wheel-line-sample.md`, rulings
  R171–R183), where the code or a measured run forced a choice:

  - **`ALM_QUEUE` watches a hot-metal detector (R173).** An `alarm` reads a
    Double tag and `CV.ItemCount` is Int64 (`MR114`), so `ALM_QUEUE` raises on
    `HMD.Value`, a pyrometer aimed at the belt's queue-full position (4 m, 5 s
    lag; `Hi` 200 °C, 60 s on-delay). `CV.ItemCount` stays read-bound.
    Criterion 1 gains `HMD` (a second `pyrometer`, observing `CV`); criteria
    2 and 4 read "the belt queue" as that detector.
  - **The saw sets the line's rate (R172)**: one billet a minute; the 40 s
    press has spare capacity, so `normal-run` makes one wheel per billet.
  - **`ALM_ZONE` has a 5 s on-delay (R174)**: the tick-0 scan reads the primed
    20 °C, not the line-start write.
  - **The dwell is 1 s (R175)**: ⌈1.0 / 0.1⌉ = 10 ≥ 3, seven ticks to spare;
    measured HiHi at N + 2, the write at N + 4, `REJECTED` at N + 11.
  - **A producing scenario ends its shift with `Billets` `starve` (R176)**, so
    its log falls quiet; `Billets.Enabled` is claimed and cannot be written.
  - **A jam cannot fill the bay (R177).** While the belt is full a good billet
    waits on the gate and hides the over-soaking one behind it, and an
    `item-sink` cannot be emptied. `press-jam` shows the hidden over-soak,
    then the reject when the jam is cleared; `pyro-fail-high` fills the bay:
    `FULL`, `INT_BAY` trips, the saw stops, a reset is refused and the line
    ends held.
  - **`ALM_PYRO`'s `Hi` and `HiHi` raise together (R179)**: the reading jumps
    from the empty station's 20 °C.
  ```

## Measurements

Scratch runs in a throwaway `git worktree` of `4c77cb7` (removed after), with
this plan's files, staged task by task (each stage built and tested as the
task leaves it).

**Suite after Task 4:** 1562 tests, all passing; Release build `0 Warning(s)`,
`0 Error(s)`. Per project: 37 Io.Abstractions / 498 Core / 176 Components / 57
Realtime / 239 Configuration / 167 Scenarios / 78 Cli / 149 Control / 27
Control.Catalogue / 134 Samples. After Task 1: 84 Samples (1512); Task 2: 121
(1549); Task 3: 124 (1552). `Millrace.Samples.Tests` runs in about 18 s (Debug),
as before.

**`millrace validate samples/wheel-line/plant.json`:**

```
OK  samples/wheel-line/plant.json
  components    9
  leaves        9
  signal links  0
  flow links    6
  tags          39 (3 explicit)
  controllers   5
  time step     100 ms
```

**Steady running** (`normal-run`, and every scenario before 300 s): a billet
charged at x:00.000 enters the furnace at x:00.100, reaches 1100 °C and logs
`Hold satisfied after 50.20 s` at x:50.300, and lands on the gate at x:50.400
at 1100.0226 °C (one extra tick of heating in Discharging); it leaves at
x:51.500, rides 20 s to the press at 1082 °C, and is a 368 kg wheel 40 s later
(`Hold satisfied after 40.00 s` at x+1:51.700). The belt holds one blank at
most, `HMD` reads 103.284 °C at most, the bay stays empty. Ten wheels in
`normal-run`.

**Per scenario** (all measured end to end):

| scenario | events | last event (tick) | quiet ticks | billets made | wheels | bay | the chain, measured |
|---|---|---|---|---|---|---|---|
| `normal-run` | 84 | 06:11:51.800 (7118) | 882 | 10 | 10 | 0 | no alarm, no reject, no trip |
| `slow-press` | 267 | 06:36:51.800 (22118) | 882 | 32 | 31 | 1 | `ALM_QUEUE` Hi 06:10:00.500 (1044.1); billet 13 at target 06:13:50.300, past 1150 °C at 840.2 s, held 285.7 s; HiHi 06:18:36.200 (1250.0); write .400; `Item 13 rejected.` 06:18:37.100; `Reject` false 06:18:37.500; clear 06:19:00; belt full (6) from 651.7 s, back to one blank at most from 1981.5 s; `ALM_QUEUE` clears 06:27:50.400 (148.2) |
| `stuck-kicker` | 271 | 06:37:02.500 (22225) | 775 | 32 | 32 | 0 | as `slow-press` to the coil's write; no `REJECTED`; billets 13 (1250.00 °C) and 14 (1169.49 °C) become wheels 13 and 14 (at the press 1029.47 °C and 963.39 °C); HiHi clears 06:20:21.700, `Reject` false .900 |
| `press-jam` | 243 | 06:36:51.800 (22118) | 882 | 29 | 28 | 1 | `ALM_QUEUE` Hi 06:10:00.500; billet 12 held from 06:12:50.300 for 433.7 s, no `ALM_PYRO` event while jammed; clear 06:20:00; HiHi 06:20:04.200; `Item 12 rejected.` 06:20:05.100; `ALM_QUEUE` clears 06:26:52.700 (147.8) |
| `zone-low` | 39 | 06:05:51.800 (3518) | 2482 | 7 | 4 | 0 | write 1050 at 06:05:30; `Lo: 1050.0 below 1200.` 06:05:35.200; billet 5 settles at 1050.00 °C and never discharges; the press finishes wheel 4 at 06:05:51.700 and starves; the table fills (2 queued) |
| `pyro-fail-high` | 64 | 06:10:01.000 (6010) | 990 | 8 | 4 | 4 | Hi and HiHi `1400.0` 06:05:00.100; `Reject` true .300; items 5–8 rejected at x:51.500; `Bay FULL` 06:08:51.600; trip .700; `Billets.Enabled` false .800; reset written 06:10:00/01, no `INTERLOCK_RESET`; no billet charged after the trip |

**Mass:** `|MassBalance.Drift|` is 0 on every tick of every scenario. Billets
made × 400 kg = wheels ÷ 0.92 + bay + held: 4000, 12800, 12800, 11600, 2800
(1200 kg held: one billet in the furnace and two on the table), 3200 kg.

**Record → replay:** all six byte-identical; each recording holds exactly the
scenario's actions (3, 5, 6, 5, 3, 5).

**Wall time**, `millrace run … --expect …` from a shell (Release, process start
included): 0.27 s (`normal-run`), 0.46 s (`slow-press`), 0.43 s
(`stuck-kicker`), 0.41 s (`press-jam`), 0.24 s (`zone-low`), 0.25 s
(`pyro-fail-high`).

**Names confirmed** against `millrace tags` (39 tags) and `millrace catalog export`:
fault ids `slow-cycle` and `discharge-jam` (`item-process-unit`), `stuck`
(`reject-gate`), `fail-high` (`pyrometer`), `starve` (`item-source`); tags
`FCE.ZONE_SP`, `CV.SPEED_SP`, `CV.ItemCount`, `FCE.Phase`, `PYRO.Value`,
`HMD.Value`, `GATE.Reject`, `GATE.Rejected`, `Bay.Full`, `Billets.Enabled`,
`INT_BAY.Reset`, `ALM_PYRO.HiHi.Active`. `millrace tags` prints the two claims as

```
Billets.Enabled  Bool  ReadOnly  Minting enabled  claimed by INT_BAY
GATE.Reject  Bool  ReadOnly  Divert the item leaving now to the reject outlet  claimed by COIL_REJECT
```

## File structure

```
tests/Millrace.Samples.Tests/Millrace.Samples.Tests.csproj    + copies samples/wheel-line/** to the output (Task 1)
samples/wheel-line/
  plant.json                    the line and its five controllers (Task 1)
  scenarios/<name>.json         six scenarios (Task 2)
  expected/<name>.log           six goldens, generated (Task 2)
  README.md                     the line, the controllers, the dwell, one section per scenario (Task 4)
tests/Millrace.Samples.Tests/
  WheelLine.cs                  paths, names, cached runs, Watch, Component (Task 1)
  WheelLineTests.cs             plant tests (Task 1); scenario theories (Task 2); live-run facts (Task 3)
  WheelLineStories.cs           the six stories (Task 2)
  WheelLineReadmeTests.cs       README quotes, dwell arithmetic, repository pages (Task 4)
README.md                       status, command line, links (Task 4)
docs/control-blocks.md          The full-size example (Task 4)
docs/superpowers/specs/2026-09-02-industrial-process-simulation-engine-design.md   header, §4, §15.2 (Task 4)
```

The 6b.2 spec (`docs/superpowers/specs/2026-10-02-wheel-line-sample-design.md`)
is amended with this plan, before execution (R183); no task edits it.

## Task map

| # | Task | Implementer | Reviewer | Tests after |
|---|---|---|---|---|
| 1 | The plant, the helper, the plant tests | sonnet | **opus** (the numbers, R171–R175) | 1512 |
| 2 | Six scenarios, goldens, stories, per-scenario theories | sonnet | **opus** (each golden read against its story, R176–R178) | 1549 |
| 3 | The live-run facts: R168 timing, slow-press chain, stuck kicker | sonnet | **opus** (the causal-chain assertions, R181) | 1552 |
| 4 | Sample README, quote tests, root README, control-blocks, main spec | sonnet | sonnet | 1562 |

Every task's brief contains its complete content; Sonnet implements throughout
(the commit trailer must be right first time). The whole-branch review at the
end is Opus. Tasks are sequential: Task 2's scenarios need Task 1's plant,
Task 3 reads Task 2's runs, Task 4 quotes Task 2's goldens.

---

### Task 1: The plant

**Model:** implementer sonnet; reviewer **opus** (the numbers: R171–R175).

**Files:**
- Modify: `tests/Millrace.Samples.Tests/Millrace.Samples.Tests.csproj` (one `None` item)
- Create: `tests/Millrace.Samples.Tests/WheelLine.cs`
- Create: `tests/Millrace.Samples.Tests/WheelLineTests.cs` (4 facts + theory of 2 = 6)
- Create: `samples/wheel-line/plant.json`

**Interfaces:**
- Consumes: `Sample.Catalogue`, `Sample.Schedule(Simulation, ScenarioAction)`,
  `Cli.Run(params string[])`, `CliRun`, `ExitCodes`, `PlantLoader.Load`,
  `PlantSchema.Generate`, `ScenarioLoader.Parse`, `ScenarioRunner.Run`,
  `Simulation.Components`, `Simulation.Tick()` (all existing).
- Produces: `WheelLine.Names`, `WheelLine.Scenarios`, `WheelLine.Root`,
  `WheelLine.Plant`, `WheelLine.Readme`, `WheelLine.SourceRoot`,
  `WheelLine.Scenario(name)`, `WheelLine.Golden(name)`,
  `WheelLine.SourceGolden(name)`, `WheelLine.Run(name)`,
  `WheelLine.Watch(name, Action<Simulation>)`,
  `WheelLine.Component<T>(Simulation, id)`; the plant's ids and tags
  (`Billets`, `FCE`, `GATE`, `Bay`, `CV`, `PRESS`, `Wheels`, `PYRO`, `HMD`;
  `FCE.ZONE_SP`, `CV.SPEED_SP`, `CV.ItemCount`; `ALM_PYRO`, `COIL_REJECT`,
  `ALM_QUEUE`, `ALM_ZONE`, `INT_BAY`).

- [ ] **Step 1: Copy the sample into the test output**

In `tests/Millrace.Samples.Tests/Millrace.Samples.Tests.csproj`, replace

```xml
    <None Include="..\..\samples\mine-conveyors\**\*"
          Link="mine-conveyors\%(RecursiveDir)%(Filename)%(Extension)" CopyToOutputDirectory="PreserveNewest" />
```

with

```xml
    <None Include="..\..\samples\mine-conveyors\**\*"
          Link="mine-conveyors\%(RecursiveDir)%(Filename)%(Extension)" CopyToOutputDirectory="PreserveNewest" />
    <None Include="..\..\samples\wheel-line\**\*"
          Link="wheel-line\%(RecursiveDir)%(Filename)%(Extension)" CopyToOutputDirectory="PreserveNewest" />
```

- [ ] **Step 2: Write the helper**

Create `tests/Millrace.Samples.Tests/WheelLine.cs`:

```csharp
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Millrace.Configuration;
using Millrace.Core;
using Millrace.Core.Graph;
using Millrace.Scenarios;

namespace Millrace.Samples.Tests;

/// <summary>
/// The wheel-line sample's files: read from the copy in the test output, and
/// written — only when MILLRACE_UPDATE_GOLDEN=1 — at their source under
/// <c>samples/wheel-line/</c>. The catalogue, the update switch and the way a
/// scenario action is scheduled are the mine-conveyor sample's, from
/// <see cref="Sample"/>.
/// </summary>
public static class WheelLine
{
    /// <summary>The six scenarios, in the order the README tells them.</summary>
    public static IReadOnlyList<string> Names { get; } =
    [
        "normal-run",
        "slow-press",
        "stuck-kicker",
        "press-jam",
        "zone-low",
        "pyro-fail-high",
    ];

    public static TheoryData<string> Scenarios => new(Names);

    public static string Root { get; } = Path.Combine(AppContext.BaseDirectory, "wheel-line");

    public static string Plant => Path.Combine(Root, "plant.json");

    public static string Readme => Path.Combine(Root, "README.md");

    /// <summary>The sample's folder in the repository, found from this source file.</summary>
    public static string SourceRoot { get; } = FindSourceRoot();

    public static string Scenario(string name) => Path.Combine(Root, "scenarios", name + ".json");

    public static string Golden(string name) => Path.Combine(Root, "expected", name + ".log");

    public static string SourceGolden(string name) => Path.Combine(SourceRoot, "expected", name + ".log");

    private static readonly ConcurrentDictionary<string, Lazy<ScenarioRunResult>> Runs = new(StringComparer.Ordinal);

    /// <summary>
    /// A scenario's run through the same runner <c>millrace run</c> uses, once per test
    /// process: a run is deterministic, so every test that reads it shares it.
    /// </summary>
    public static ScenarioRunResult Run(string name) =>
        Runs.GetOrAdd(name, n => new Lazy<ScenarioRunResult>(() => RunOnce(n))).Value;

    /// <summary>
    /// Runs a scenario live, to its full duration, and calls
    /// <paramref name="afterEachTick"/> after every tick. The event log holds
    /// events only; a story told by item temperatures, ids or a belt's count needs
    /// this. Returns the simulation as the run left it.
    /// </summary>
    public static Simulation Watch(string name, Action<Simulation> afterEachTick)
    {
        ArgumentNullException.ThrowIfNull(afterEachTick);
        string path = Scenario(name);
        Scenario scenario = ScenarioLoader.Parse(File.ReadAllText(path)).Scenario
            ?? throw new InvalidOperationException($"'{name}' does not parse.");
        LoadResult load = PlantLoader.Load(File.ReadAllText(scenario.ResolvePlantPath(path)), Sample.Catalogue, scenario.ToLoadOptions());
        Simulation simulation = load.Builder?.Build() ?? throw new InvalidOperationException(load.ToText());
        foreach (ScenarioAction action in scenario.Timeline)
        {
            Sample.Schedule(simulation, action);
        }

        long ticks = scenario.Duration.Ticks / load.Options!.TimeStep.Ticks;
        for (long tick = 1; tick <= ticks; tick++)
        {
            simulation.Tick();
            afterEachTick(simulation);
        }

        return simulation;
    }

    /// <summary>The leaf with this id, as the type the test needs.</summary>
    public static T Component<T>(Simulation simulation, string id)
        where T : class, ISimComponent
    {
        ArgumentNullException.ThrowIfNull(simulation);
        return simulation.Components.OfType<T>().Single(c => string.Equals(c.Id, id, StringComparison.Ordinal));
    }

    private static ScenarioRunResult RunOnce(string name)
    {
        string path = Scenario(name);
        ScenarioParseResult parsed = ScenarioLoader.Parse(File.ReadAllText(path));
        Scenario scenario = parsed.Scenario ?? throw new InvalidOperationException($"'{name}' does not parse: {parsed.ToText()}");
        ScenarioRunResult result = ScenarioRunner.Run(scenario, File.ReadAllText(scenario.ResolvePlantPath(path)), Sample.Catalogue);
        return result.IsValid ? result : throw new InvalidOperationException($"'{name}' does not run: {result.ToText()}");
    }

    private static string FindSourceRoot([CallerFilePath] string thisFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", "..", "samples", "wheel-line"));
}
```

- [ ] **Step 3: Write the failing plant tests**

Create `tests/Millrace.Samples.Tests/WheelLineTests.cs` (4 facts and a theory of 2):

```csharp
using System.Text.Json;
using Millrace.Cli;
using Millrace.Configuration;
using Millrace.Scenarios;
using Json.Schema;

namespace Millrace.Samples.Tests;

/// <summary>
/// The wheel-line sample, end to end: the plant validates and the schema
/// accepts it; every scenario opens with the line start, matches its golden
/// through <c>millrace run --expect</c>, tells its story, settles before it ends,
/// conserves mass and replays byte for byte from a recording; and the stories
/// the log cannot tell — a held billet's temperature, the belt's count, which
/// billet became which wheel — hold on a live run.
/// </summary>
public class WheelLineTests
{
    [Fact]
    public void ThePlantValidatesWithFiveControllers()
    {
        CliRun run = Cli.Run("validate", WheelLine.Plant);

        Assert.Equal(ExitCodes.Ok, run.ExitCode);
        Assert.Empty(run.Err);
        Assert.StartsWith($"OK  {WheelLine.Plant}\n", run.Out, StringComparison.Ordinal);
        Assert.Contains("  components    9\n", run.Out, StringComparison.Ordinal);
        Assert.Contains("  flow links    6\n", run.Out, StringComparison.Ordinal);
        Assert.Contains("  tags          39 (3 explicit)\n", run.Out, StringComparison.Ordinal);
        Assert.Contains("  controllers   5\n", run.Out, StringComparison.Ordinal);
    }

    [Fact]
    public void TheLoaderReportsNothingAtAllNotEvenAWarning()
    {
        LoadResult load = PlantLoader.Load(File.ReadAllText(WheelLine.Plant), Sample.Catalogue);

        Assert.True(load.IsValid, load.ToText());
        Assert.Empty(load.Diagnostics);
    }

    [Fact]
    public void TheGeneratedSchemaAcceptsThePlantAndRejectsAMistakeInIt()
    {
        JsonSchema schema = JsonSchema.FromText(PlantSchema.Generate(Sample.Catalogue));
        string plant = File.ReadAllText(WheelLine.Plant);
        string broken = plant.Replace("\"dwellSeconds\": 1 }", "\"dwellSeconds\": \"1\" }", StringComparison.Ordinal);

        Assert.NotEqual(plant, broken);
        Assert.True(schema.Evaluate(JsonDocument.Parse(plant).RootElement).IsValid);
        Assert.False(schema.Evaluate(JsonDocument.Parse(broken).RootElement).IsValid);
    }

    [Fact]
    public void TheRejectGateIsClaimedByTheCoilAndTheBilletSourceByTheBayInterlock()
    {
        CliRun run = Cli.Run("tags", WheelLine.Plant);

        Assert.Equal(ExitCodes.Ok, run.ExitCode);
        string[] claimed = run.Out.Split('\n').Where(l => l.Contains("  claimed by ", StringComparison.Ordinal)).ToArray();
        Assert.Equal(
            [
                "Billets.Enabled  Bool  ReadOnly  Minting enabled  claimed by INT_BAY",
                "GATE.Reject  Bool  ReadOnly  Divert the item leaving now to the reject outlet  claimed by COIL_REJECT",
            ],
            claimed);
        Assert.Contains("CV.ItemCount  Int64  ReadOnly  count  Blanks on the belt\n", run.Out, StringComparison.Ordinal);
        Assert.Contains("FCE.ZONE_SP  Double  ReadWrite  °C  [0, 1400]  Furnace zone temperature setpoint\n", run.Out, StringComparison.Ordinal);
        Assert.Contains("CV.SPEED_SP  Double  ReadWrite  m/s  [0, 1]  Belt speed setpoint\n", run.Out, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("GATE.Reject", "COIL_REJECT")]
    [InlineData("Billets.Enabled", "INT_BAY")]
    public void AScenarioThatWritesAClaimedTagIsRefusedBeforeTickZero(string tag, string block)
    {
        Scenario scenario = ScenarioLoader.Parse(
            $$"""{ "plant": "../plant.json", "duration": 60, "timeline": [ { "at": 10, "write": "{{tag}}", "value": true } ] }""").Scenario!;

        ScenarioRunResult result = ScenarioRunner.Run(scenario, File.ReadAllText(WheelLine.Plant), Sample.Catalogue);

        ConfigDiagnostic d = Assert.Single(result.Diagnostics);
        Assert.Equal(("MR206", "$.timeline[0].write"), (d.Code, d.Path));
        Assert.Equal($"Tag '{tag}' is claimed by {block}; a scenario cannot write it.", d.Message);
        Assert.Null(result.Events);
    }
}
```

- [ ] **Step 4: Run them to see them fail**

Run: `dotnet test tests/Millrace.Samples.Tests --nologo --filter "FullyQualifiedName~WheelLineTests"`
Expected: `Failed: 6, Passed: 0` — `samples/wheel-line/plant.json` does not
exist yet, so `validate` and `tags` exit non-zero and the others cannot read it.

- [ ] **Step 5: Write the plant**

Create `samples/wheel-line/plant.json`:

```json
{
  "defaults": { "seed": 11, "timeStepMs": 100, "startTime": "2026-04-06T06:00:00+02:00" },
  "materials": [
    { "name": "Billet", "kind": "discrete", "properties": { "density": 7850, "moisture": 0, "temperature": 25 },
      "description": "A round steel billet, cut to weight for one wheel." },
    { "name": "Wheel", "kind": "discrete", "properties": { "density": 7850, "moisture": 0, "temperature": 25 },
      "description": "A forged wheel blank." }
  ],
  "components": [
    { "id": "Billets", "type": "item-source",
      "parameters": { "material": "Billet", "itemMassKg": 400, "intervalSeconds": 60, "queueCapacity": 2 } },
    { "id": "FCE", "type": "item-process-unit", "parameters": {
        "batchSize": 1, "heatWhileHeld": true,
        "hold": { "type": "temperature-at-least", "celsius": 1100 },
        "transforms": [ { "type": "thermal-transfer", "timeConstantSeconds": 24 } ] } },
    { "id": "GATE", "type": "reject-gate", "parameters": { "dwellSeconds": 1 } },
    { "id": "Bay", "type": "item-sink", "parameters": { "capacity": 4 } },
    { "id": "CV", "type": "discrete-belt", "parameters": {
        "lengthM": 10, "maxSpeedMps": 1, "minSpacingM": 2,
        "transforms": [ { "type": "thermal-transfer", "timeConstantSeconds": 1200 } ] } },
    { "id": "PRESS", "type": "item-process-unit", "parameters": {
        "batchSize": 1, "hold": { "type": "for-seconds", "seconds": 40 }, "output": "Wheel", "yield": 0.92 } },
    { "id": "Wheels", "type": "item-sink" },
    { "id": "PYRO", "type": "pyrometer", "parameters": {
        "target": "GATE", "positionM": 0, "windowM": 0,
        "spec": { "unit": "°C", "rangeLow": 0, "rangeHigh": 1400, "lagSeconds": 0 } } },
    { "id": "HMD", "type": "pyrometer", "parameters": {
        "target": "CV", "positionM": 4, "windowM": 0.1,
        "spec": { "unit": "°C", "rangeLow": 0, "rangeHigh": 1400, "lagSeconds": 5 } } }
  ],
  "flows": [
    { "from": "Billets.Out",    "to": "FCE.In" },
    { "from": "FCE.Out",        "to": "GATE.In" },
    { "from": "GATE.Out",       "to": "CV.In" },
    { "from": "GATE.RejectOut", "to": "Bay.In" },
    { "from": "CV.Out",         "to": "PRESS.In" },
    { "from": "PRESS.Out",      "to": "Wheels.In" }
  ],
  "tags": [
    { "name": "FCE.ZONE_SP", "port": "FCE.AmbientTemperature", "access": "write", "unit": "°C", "rangeLow": 0, "rangeHigh": 1400,
      "description": "Furnace zone temperature setpoint" },
    { "name": "CV.SPEED_SP", "port": "CV.Speed", "access": "write", "unit": "m/s", "rangeLow": 0, "rangeHigh": 1,
      "description": "Belt speed setpoint" },
    { "name": "CV.ItemCount", "port": "CV.ItemCount", "access": "read", "unit": "count",
      "description": "Blanks on the belt" }
  ],
  "controllers": [
    { "id": "ALM_PYRO", "type": "alarm", "scanPeriodMs": 100, "parameters": { "input": "PYRO.Value", "limits": [
        { "kind": "hi", "value": 1130, "deadband": 10 },
        { "kind": "hi-hi", "value": 1150, "deadband": 10 } ] } },
    { "id": "COIL_REJECT", "type": "coil", "scanPeriodMs": 100, "claims": [ "GATE.Reject" ],
      "parameters": { "condition": { "tag": "ALM_PYRO.HiHi.Active", "normal": true }, "output": "GATE.Reject" } },
    { "id": "ALM_QUEUE", "type": "alarm", "scanPeriodMs": 100, "parameters": { "input": "HMD.Value", "limits": [
        { "kind": "hi", "value": 200, "deadband": 50, "onDelayS": 60 } ] } },
    { "id": "ALM_ZONE", "type": "alarm", "scanPeriodMs": 100, "parameters": { "input": "FCE.ZONE_SP", "limits": [
        { "kind": "lo", "value": 1200, "deadband": 10, "onDelayS": 5 },
        { "kind": "hi", "value": 1300, "deadband": 10, "onDelayS": 5 } ] } },
    { "id": "INT_BAY", "type": "interlock", "scanPeriodMs": 100, "claims": [ "Billets.Enabled" ],
      "parameters": {
        "conditions": [ { "tag": "Bay.Full", "normal": false } ],
        "trip": [ { "tag": "Billets.Enabled", "value": false } ],
        "reset": [ { "tag": "Billets.Enabled", "value": true } ] } }
  ]
}
```

- [ ] **Step 6: Run the plant tests**

Run: `dotnet run --project src/Millrace.Cli -- validate samples/wheel-line/plant.json`
Expected, exactly:

```
OK  samples/wheel-line/plant.json
  components    9
  leaves        9
  signal links  0
  flow links    6
  tags          39 (3 explicit)
  controllers   5
  time step     100 ms
```

Run: `dotnet test tests/Millrace.Samples.Tests --nologo` — expect PASS, **84**.

- [ ] **Step 7: Run everything**

Run: `dotnet build Millrace.sln -c Release --nologo` — expect `0 Warning(s)`, `0 Error(s)`.
Run: `dotnet test Millrace.sln --nologo` — expect **1512**: 37 / 498 / 176 / 57 /
239 / 167 / 78 / 149 / 27 / 84.
Run: `git diff --stat 4c77cb7 -- src/` — expect nothing.
Run: `git status --short` — expect exactly the four paths of Step 8.

- [ ] **Step 8: Commit**

```bash
git add tests/Millrace.Samples.Tests/Millrace.Samples.Tests.csproj tests/Millrace.Samples.Tests/WheelLine.cs tests/Millrace.Samples.Tests/WheelLineTests.cs samples/wheel-line/plant.json
git commit -F .superpowers/sdd/6b.2/msg-task1.txt
```

with `.superpowers/sdd/6b.2/msg-task1.txt` (written with the Write tool):

```
feat(samples): add the wheel line's plant

A billet saw, a heat-while-held furnace, a reject gate measuring each
billet for 1 s, a 10 m cooling belt and a press that forges a wheel,
with a reject bay on the gate's RejectOut. A pyrometer on the gate feeds
ALM_PYRO, whose HiHi drives COIL_REJECT, which claims GATE.Reject;
INT_BAY stops the saw when the bay is full and claims Billets.Enabled.
The zone setpoint and belt speed are write-bound through the plant's
tags envelope; CV.ItemCount is read-bound.

An alarm reads Double tags and the belt's count is Int64 (MR114), so
the queue alarm watches a hot-metal detector at the belt's queue-full
position. The zone alarm has a 5 s on-delay: the first scan reads the
primed 20 °C, not the line-start write.

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
```

Then `git log -1 --format='%h%n%s%n--%n%b'` and check the subject, the blank
line and the trailer.

---

### Task 2: Six scenarios, their goldens and their stories

**Model:** implementer sonnet; reviewer **opus** (each golden read against its
story; R176–R178).

**Files:**
- Create: `samples/wheel-line/scenarios/normal-run.json`, `slow-press.json`,
  `stuck-kicker.json`, `press-jam.json`, `zone-low.json`, `pyro-fail-high.json`
- Generate: `samples/wheel-line/expected/<name>.log` (six)
- Create: `tests/Millrace.Samples.Tests/WheelLineStories.cs`
- Modify: `tests/Millrace.Samples.Tests/WheelLineTests.cs` (replaced in full: + 1
  fact and six theories of 6 = 37 tests)

**Interfaces:**
- Consumes: Task 1's `WheelLine` and plant; `CausalChain.FindChain`,
  `CausalChain.FindAbsence`, `CausalChain.Line`, `EventPattern`, `Absence`,
  `Story` (existing, unchanged); `ScenarioRecorder`, `ScenarioJson.Write`,
  `WriteAction`, `MassBalance`, `ItemSink.MassReceived`.
- Produces: `WheelLineStories.All` (six stories keyed by scenario name); the
  six goldens Task 3 and Task 4 read.

- [ ] **Step 1: Write the six scenarios**

Create `samples/wheel-line/scenarios/normal-run.json`:

```json
{
  "plant": "../plant.json",
  "duration": 800,
  "timeline": [
    { "at": 0, "write": "FCE.ZONE_SP", "value": 1250 },
    { "at": 0, "write": "CV.SPEED_SP", "value": 0.5 },
    { "at": 600, "fault": "Billets", "id": "starve" }
  ]
}
```

Create `samples/wheel-line/scenarios/slow-press.json`:

```json
{
  "plant": "../plant.json",
  "duration": 2300,
  "timeline": [
    { "at": 0,    "write": "FCE.ZONE_SP", "value": 1250 },
    { "at": 0,    "write": "CV.SPEED_SP", "value": 0.5 },
    { "at": 300,  "fault": "PRESS", "id": "slow-cycle", "args": { "fraction": 0.9 } },
    { "at": 1140, "clear": "PRESS", "id": "slow-cycle" },
    { "at": 2100, "fault": "Billets", "id": "starve" }
  ]
}
```

Create `samples/wheel-line/scenarios/stuck-kicker.json`:

```json
{
  "plant": "../plant.json",
  "duration": 2300,
  "timeline": [
    { "at": 0,    "write": "FCE.ZONE_SP", "value": 1250 },
    { "at": 0,    "write": "CV.SPEED_SP", "value": 0.5 },
    { "at": 300,  "fault": "PRESS", "id": "slow-cycle", "args": { "fraction": 0.9 } },
    { "at": 600,  "fault": "GATE", "id": "stuck" },
    { "at": 1140, "clear": "PRESS", "id": "slow-cycle" },
    { "at": 2100, "fault": "Billets", "id": "starve" }
  ]
}
```

Create `samples/wheel-line/scenarios/press-jam.json`:

```json
{
  "plant": "../plant.json",
  "duration": 2300,
  "timeline": [
    { "at": 0,    "write": "FCE.ZONE_SP", "value": 1250 },
    { "at": 0,    "write": "CV.SPEED_SP", "value": 0.5 },
    { "at": 300,  "fault": "PRESS", "id": "discharge-jam" },
    { "at": 1200, "clear": "PRESS", "id": "discharge-jam" },
    { "at": 2100, "fault": "Billets", "id": "starve" }
  ]
}
```

Create `samples/wheel-line/scenarios/zone-low.json`:

```json
{
  "plant": "../plant.json",
  "duration": 600,
  "timeline": [
    { "at": 0,   "write": "FCE.ZONE_SP", "value": 1250 },
    { "at": 0,   "write": "CV.SPEED_SP", "value": 0.5 },
    { "at": 330, "write": "FCE.ZONE_SP", "value": 1050 }
  ]
}
```

Create `samples/wheel-line/scenarios/pyro-fail-high.json`:

```json
{
  "plant": "../plant.json",
  "duration": 700,
  "timeline": [
    { "at": 0,   "write": "FCE.ZONE_SP", "value": 1250 },
    { "at": 0,   "write": "CV.SPEED_SP", "value": 0.5 },
    { "at": 300, "fault": "PYRO", "id": "fail-high" },
    { "at": 600, "write": "INT_BAY.Reset", "value": true },
    { "at": 601, "write": "INT_BAY.Reset", "value": false }
  ]
}
```

- [ ] **Step 2: Write the stories**

Create `tests/Millrace.Samples.Tests/WheelLineStories.cs`:

```csharp
namespace Millrace.Samples.Tests;

/// <summary>
/// The six wheel-line stories of the design (6b.2 spec criterion 5, as the
/// plan's rulings amend it), as the measured event logs tell them. Each chain
/// is an ordered subsequence of the scenario's log; each absence is bounded by
/// a start point that must itself occur. What the log cannot show — a held
/// billet's temperature, the belt's count, which billet became which wheel —
/// is asserted on a live run in <c>WheelLineTests</c>.
/// </summary>
public static class WheelLineStories
{
    private static readonly EventPattern ZoneStart = E("FCE.ZONE_SP", "WRITE", "Set to 1250.");

    private static readonly EventPattern BeltStart = E("CV.SPEED_SP", "WRITE", "Set to 0.5.");

    private static readonly EventPattern CoilFirstScan = E("GATE.Reject", "WRITE", "Set to false by COIL_REJECT.");

    private static readonly EventPattern RejectOn = E("GATE.Reject", "WRITE", "Set to true by COIL_REJECT.");

    private static readonly EventPattern Starve = E("Billets", "FAULT", "starve injected.");

    private static readonly EventPattern QueueHigh = E("ALM_QUEUE", "ALARM_RAISED", "Hi: ");

    private static readonly EventPattern QueueClear = E("ALM_QUEUE", "ALARM_CLEARED", "Hi: ");

    public static IReadOnlyDictionary<string, Story> All { get; } = new Dictionary<string, Story>(StringComparer.Ordinal)
    {
        ["normal-run"] = new(
            [
                ZoneStart,
                BeltStart,
                CoilFirstScan,
                E("FCE", "DISCHARGING", "Hold satisfied after 50.20 s; discharging 1 items."),
                E("PRESS", "DISCHARGING", "Hold satisfied after 40.00 s; discharging 1 items."),
                Starve,
                E("PRESS", "IDLE"),
            ],
            [
                new(null, E(null, "ALARM_RAISED")),
                new(null, E("GATE", "REJECTED")),
                new(null, E(null, "INTERLOCK_TRIP")),
                new(CoilFirstScan, E("GATE.Reject", "WRITE")),
            ]),

        ["slow-press"] = new(
            [
                ZoneStart,
                BeltStart,
                CoilFirstScan,
                E("PRESS", "FAULT", "slow-cycle injected: fraction=0.9."),
                QueueHigh,
                E("ALM_PYRO", "ALARM_RAISED", "Hi: 1250.0 above 1130."),
                E("ALM_PYRO", "ALARM_RAISED", "HiHi: 1250.0 above 1150."),
                RejectOn,
                E("GATE", "REJECTED", "Item 13 rejected."),
                E("ALM_PYRO", "ALARM_CLEARED", "HiHi: "),
                E("GATE.Reject", "WRITE", "Set to false by COIL_REJECT."),
                E("PRESS", "FAULT_CLEARED", "slow-cycle cleared."),
                QueueClear,
                Starve,
            ],
            [
                new(null, E("ALM_PYRO", "ALARM_RAISED"), E("PRESS", "FAULT", "slow-cycle")),
                new(E("GATE", "REJECTED"), E("GATE", "REJECTED")),
                new(E("PRESS", "FAULT_CLEARED"), E("ALM_PYRO", "ALARM_RAISED")),
                new(QueueClear, E("ALM_QUEUE", "ALARM_RAISED")),
                new(null, E("Bay", "FULL")),
                new(null, E(null, "INTERLOCK_TRIP")),
            ]),

        ["stuck-kicker"] = new(
            [
                ZoneStart,
                BeltStart,
                CoilFirstScan,
                E("PRESS", "FAULT", "slow-cycle injected: fraction=0.9."),
                E("GATE", "FAULT", "stuck injected."),
                QueueHigh,
                E("ALM_PYRO", "ALARM_RAISED", "HiHi: 1250.0 above 1150."),
                RejectOn,
                E("PRESS", "FAULT_CLEARED", "slow-cycle cleared."),
                E("ALM_PYRO", "ALARM_CLEARED", "HiHi: "),
                E("GATE.Reject", "WRITE", "Set to false by COIL_REJECT."),
                QueueClear,
                Starve,
            ],
            [
                new(null, E("GATE", "REJECTED")),
                new(QueueClear, E("ALM_QUEUE", "ALARM_RAISED")),
                new(null, E(null, "INTERLOCK_TRIP")),
            ]),

        ["press-jam"] = new(
            [
                ZoneStart,
                BeltStart,
                CoilFirstScan,
                E("PRESS", "FAULT", "discharge-jam injected."),
                QueueHigh,
                E("PRESS", "FAULT_CLEARED", "discharge-jam cleared."),
                E("ALM_PYRO", "ALARM_RAISED", "HiHi: 1250.0 above 1150."),
                RejectOn,
                E("GATE", "REJECTED", "Item 12 rejected."),
                E("GATE.Reject", "WRITE", "Set to false by COIL_REJECT."),
                QueueClear,
                Starve,
            ],
            [
                // While the press is jammed the over-soaking billet is in the furnace,
                // behind a good one waiting on the gate: the pyrometer cannot see it.
                new(E("PRESS", "FAULT", "discharge-jam"), E("ALM_PYRO", "ALARM_RAISED"), E("PRESS", "FAULT_CLEARED")),
                new(E("PRESS", "FAULT", "discharge-jam"), E("GATE", "REJECTED"), E("PRESS", "FAULT_CLEARED")),
                new(E("GATE", "REJECTED"), E("GATE", "REJECTED")),
                new(QueueClear, E("ALM_QUEUE", "ALARM_RAISED")),
                new(null, E("Bay", "FULL")),
                new(null, E(null, "INTERLOCK_TRIP")),
            ]),

        ["zone-low"] = new(
            [
                ZoneStart,
                BeltStart,
                CoilFirstScan,
                E("FCE.ZONE_SP", "WRITE", "Set to 1050."),
                E("ALM_ZONE", "ALARM_RAISED", "Lo: 1050.0 below 1200."),
                E("PRESS", "IDLE"),
            ],
            [
                // The zone alarm's on-delay keeps it quiet at power-up, before the line-start write lands.
                new(null, E("ALM_ZONE", "ALARM_RAISED"), E("FCE.ZONE_SP", "WRITE", "Set to 1050.")),
                new(E("FCE.ZONE_SP", "WRITE", "Set to 1050."), E("FCE", "DISCHARGING")),
                new(E("FCE.ZONE_SP", "WRITE", "Set to 1050."), E("PRESS", "FILLING")),
                new(null, E("GATE", "REJECTED")),
                new(null, E(null, "INTERLOCK_TRIP")),
            ]),

        ["pyro-fail-high"] = new(
            [
                ZoneStart,
                BeltStart,
                CoilFirstScan,
                E("PYRO", "FAULT", "fail-high injected."),
                E("ALM_PYRO", "ALARM_RAISED", "HiHi: 1400.0 above 1150."),
                RejectOn,
                E("GATE", "REJECTED", "Item 5 rejected."),
                E("GATE", "REJECTED", "Item 6 rejected."),
                E("GATE", "REJECTED", "Item 7 rejected."),
                E("GATE", "REJECTED", "Item 8 rejected."),
                E("Bay", "FULL", "Capacity reached; accepting nothing more."),
                E("INT_BAY", "INTERLOCK_TRIP", "Bay.Full abnormal."),
                E("Billets.Enabled", "WRITE", "Set to false by INT_BAY."),
                E("INT_BAY.Reset", "WRITE", "Set to true."),
                E("INT_BAY.Reset", "WRITE", "Set to false."),
            ],
            [
                new(E("PYRO", "FAULT"), E("GATE.Reject", "WRITE", "Set to false by COIL_REJECT.")),
                new(E("GATE", "REJECTED"), E("PRESS", "FILLING")),
                new(E("INT_BAY", "INTERLOCK_TRIP"), E("FCE", "FILLING")),

                // The bay cannot be emptied from a scenario, so the operator's reset is refused.
                new(null, E("INT_BAY", "INTERLOCK_RESET")),
            ]),
    };

    private static EventPattern E(string? source, string code, string fragment = "") => new(source, code, fragment);
}
```

- [ ] **Step 3: Add the scenario theories**

Replace the whole of `tests/Millrace.Samples.Tests/WheelLineTests.cs` with (Task 1's
six tests unchanged, then the folder fact and six theories):

```csharp
using System.Globalization;
using System.Text.Json;
using Millrace.Cli;
using Millrace.Components.Flow;
using Millrace.Configuration;
using Millrace.Core;
using Millrace.Core.Flow;
using Millrace.Core.Logging;
using Millrace.Scenarios;
using Json.Schema;

namespace Millrace.Samples.Tests;

/// <summary>
/// The wheel-line sample, end to end: the plant validates and the schema
/// accepts it; every scenario opens with the line start, matches its golden
/// through <c>millrace run --expect</c>, tells its story, settles before it ends,
/// conserves mass and replays byte for byte from a recording; and the stories
/// the log cannot tell — a held billet's temperature, the belt's count, which
/// billet became which wheel — hold on a live run.
/// </summary>
public class WheelLineTests
{
    [Fact]
    public void ThePlantValidatesWithFiveControllers()
    {
        CliRun run = Cli.Run("validate", WheelLine.Plant);

        Assert.Equal(ExitCodes.Ok, run.ExitCode);
        Assert.Empty(run.Err);
        Assert.StartsWith($"OK  {WheelLine.Plant}\n", run.Out, StringComparison.Ordinal);
        Assert.Contains("  components    9\n", run.Out, StringComparison.Ordinal);
        Assert.Contains("  flow links    6\n", run.Out, StringComparison.Ordinal);
        Assert.Contains("  tags          39 (3 explicit)\n", run.Out, StringComparison.Ordinal);
        Assert.Contains("  controllers   5\n", run.Out, StringComparison.Ordinal);
    }

    [Fact]
    public void TheLoaderReportsNothingAtAllNotEvenAWarning()
    {
        LoadResult load = PlantLoader.Load(File.ReadAllText(WheelLine.Plant), Sample.Catalogue);

        Assert.True(load.IsValid, load.ToText());
        Assert.Empty(load.Diagnostics);
    }

    [Fact]
    public void TheGeneratedSchemaAcceptsThePlantAndRejectsAMistakeInIt()
    {
        JsonSchema schema = JsonSchema.FromText(PlantSchema.Generate(Sample.Catalogue));
        string plant = File.ReadAllText(WheelLine.Plant);
        string broken = plant.Replace("\"dwellSeconds\": 1 }", "\"dwellSeconds\": \"1\" }", StringComparison.Ordinal);

        Assert.NotEqual(plant, broken);
        Assert.True(schema.Evaluate(JsonDocument.Parse(plant).RootElement).IsValid);
        Assert.False(schema.Evaluate(JsonDocument.Parse(broken).RootElement).IsValid);
    }

    [Fact]
    public void TheRejectGateIsClaimedByTheCoilAndTheBilletSourceByTheBayInterlock()
    {
        CliRun run = Cli.Run("tags", WheelLine.Plant);

        Assert.Equal(ExitCodes.Ok, run.ExitCode);
        string[] claimed = run.Out.Split('\n').Where(l => l.Contains("  claimed by ", StringComparison.Ordinal)).ToArray();
        Assert.Equal(
            [
                "Billets.Enabled  Bool  ReadOnly  Minting enabled  claimed by INT_BAY",
                "GATE.Reject  Bool  ReadOnly  Divert the item leaving now to the reject outlet  claimed by COIL_REJECT",
            ],
            claimed);
        Assert.Contains("CV.ItemCount  Int64  ReadOnly  count  Blanks on the belt\n", run.Out, StringComparison.Ordinal);
        Assert.Contains("FCE.ZONE_SP  Double  ReadWrite  °C  [0, 1400]  Furnace zone temperature setpoint\n", run.Out, StringComparison.Ordinal);
        Assert.Contains("CV.SPEED_SP  Double  ReadWrite  m/s  [0, 1]  Belt speed setpoint\n", run.Out, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("GATE.Reject", "COIL_REJECT")]
    [InlineData("Billets.Enabled", "INT_BAY")]
    public void AScenarioThatWritesAClaimedTagIsRefusedBeforeTickZero(string tag, string block)
    {
        Scenario scenario = ScenarioLoader.Parse(
            $$"""{ "plant": "../plant.json", "duration": 60, "timeline": [ { "at": 10, "write": "{{tag}}", "value": true } ] }""").Scenario!;

        ScenarioRunResult result = ScenarioRunner.Run(scenario, File.ReadAllText(WheelLine.Plant), Sample.Catalogue);

        ConfigDiagnostic d = Assert.Single(result.Diagnostics);
        Assert.Equal(("MR206", "$.timeline[0].write"), (d.Code, d.Path));
        Assert.Equal($"Tag '{tag}' is claimed by {block}; a scenario cannot write it.", d.Message);
        Assert.Null(result.Events);
    }

    [Fact]
    public void TheScenarioFolderHoldsExactlyTheSixScenariosEachWithAGoldenAndAStory()
    {
        string[] onDisk = Directory.GetFiles(Path.Combine(WheelLine.SourceRoot, "scenarios"), "*.json")
            .Select(f => Path.GetFileNameWithoutExtension(f))
            .Order(StringComparer.Ordinal)
            .ToArray();
        string[] expected = WheelLine.Names.Order(StringComparer.Ordinal).ToArray();

        Assert.Equal(expected, onDisk);
        Assert.Equal(expected, WheelLineStories.All.Keys.Order(StringComparer.Ordinal).ToArray());
        Assert.All(WheelLine.Names, name => Assert.True(File.Exists(WheelLine.SourceGolden(name)), $"'{name}' has no golden."));
    }

    [Theory]
    [MemberData(nameof(WheelLine.Scenarios), MemberType = typeof(WheelLine))]
    public void EveryScenarioOpensWithTheLineStartAndTheCoilsFirstScan(string name)
    {
        Scenario scenario = ScenarioLoader.Parse(File.ReadAllText(WheelLine.Scenario(name))).Scenario!;
        IReadOnlyList<SimEventRecord> events = WheelLine.Run(name).Events!.Records;

        WriteAction zone = Assert.IsType<WriteAction>(scenario.Timeline[0]);
        WriteAction belt = Assert.IsType<WriteAction>(scenario.Timeline[1]);
        Assert.Equal((TimeSpan.Zero, "FCE.ZONE_SP"), (zone.At, zone.Tag));
        Assert.Equal((TimeSpan.Zero, "CV.SPEED_SP"), (belt.At, belt.Tag));
        Assert.Equal(
            [
                "06:00:00.000  FCE.ZONE_SP  WRITE  Set to 1250.",
                "06:00:00.000  CV.SPEED_SP  WRITE  Set to 0.5.",
                "06:00:00.100  GATE.Reject  WRITE  Set to false by COIL_REJECT.",
            ],
            events.Take(3).Select(CausalChain.Line));
    }

    [Theory]
    [MemberData(nameof(WheelLine.Scenarios), MemberType = typeof(WheelLine))]
    public void EveryScenarioMatchesItsGolden(string name)
    {
        if (Sample.Updating)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(WheelLine.SourceGolden(name))!);
            CliRun made = Cli.Run("run", WheelLine.Scenario(name), "--out", WheelLine.SourceGolden(name));
            Assert.Equal(ExitCodes.Ok, made.ExitCode);
            return;
        }

        string golden = WheelLine.Golden(name);

        CliRun run = Cli.Run("run", WheelLine.Scenario(name), "--expect", golden);

        Assert.True(run.ExitCode == ExitCodes.Ok, run.Err);
        Assert.Empty(run.Err);
        Assert.StartsWith($"Matched {golden} (", run.Out, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(WheelLine.Scenarios), MemberType = typeof(WheelLine))]
    public void EveryScenarioTellsItsStory(string name)
    {
        IReadOnlyList<SimEventRecord> events = WheelLine.Run(name).Events!.Records;
        Story story = WheelLineStories.All[name];

        Assert.Null(CausalChain.FindChain(events, story.Chain));
        Assert.All(story.Absences, absence => Assert.Null(CausalChain.FindAbsence(events, absence)));
    }

    [Theory]
    [MemberData(nameof(WheelLine.Scenarios), MemberType = typeof(WheelLine))]
    public void EveryScenarioSettlesAtLeastFiveHundredTicksBeforeItEnds(string name)
    {
        ScenarioRunResult result = WheelLine.Run(name);

        long quietTicks = result.Summary!.Ticks - result.Events!.Records[^1].Tick;

        Assert.True(
            quietTicks >= 500,
            $"'{name}' logs its last event {quietTicks} ticks before the end; lengthen its duration so the consequence settles.");
    }

    [Theory]
    [MemberData(nameof(WheelLine.Scenarios), MemberType = typeof(WheelLine))]
    public void EveryScenarioConservesMassOnEveryTick(string name)
    {
        double worst = 0.0;

        Simulation end = WheelLine.Watch(name, sim => worst = Math.Max(worst, Math.Abs(sim.MassBalance.Drift)));

        MassBalance balance = end.MassBalance;
        ItemSink wheels = WheelLine.Component<ItemSink>(end, "Wheels");
        ItemSink bay = WheelLine.Component<ItemSink>(end, "Bay");
        Assert.True(worst <= 1e-9, string.Create(CultureInfo.InvariantCulture, $"'{name}' drifts {worst} kg."));
        Assert.True(balance.Created > 0.0, $"'{name}' makes no billet.");

        // Every billet made is a wheel (92 % of it: the press's yield; the rest is flash), a reject,
        // or still in the line.
        Assert.Equal(balance.Created, (wheels.MassReceived / 0.92) + bay.MassReceived + balance.Held, 6);
    }

    [Theory]
    [MemberData(nameof(WheelLine.Scenarios), MemberType = typeof(WheelLine))]
    public void EveryScenarioReplaysByteForByteFromARecording(string name)
    {
        string path = WheelLine.Scenario(name);
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

- [ ] **Step 4: Run them to see the golden tests fail**

Run: `dotnet test tests/Millrace.Samples.Tests --nologo --filter "FullyQualifiedName~WheelLineTests"`
Expected: `Failed: 7, Passed: 36` — the six `EveryScenarioMatchesItsGolden`
rows (there is no golden to `--expect`) and
`TheScenarioFolderHoldsExactlyTheSixScenariosEachWithAGoldenAndAStory`
(`'normal-run' has no golden.`). Everything else — line start, stories, quiet
tails, mass, replay — needs no golden and passes already. Report the measured
failures if they differ.

- [ ] **Step 5: Generate the goldens**

Run: `MILLRACE_UPDATE_GOLDEN=1 dotnet test tests/Millrace.Samples.Tests --nologo --filter "FullyQualifiedName~WheelLineTests.EveryScenarioMatchesItsGolden"`
Expected: 6 passed, and six new files under `samples/wheel-line/expected/`.

- [ ] **Step 6: Read every golden in full and check it**

Read each file with a file-reading tool, all of it. Check:

- Line counts: `normal-run.log` 84, `slow-press.log` 267, `stuck-kicker.log`
  271, `press-jam.log` 243, `zone-low.log` 39, `pyro-fail-high.log` 64.
- Every golden's first three lines are exactly

  ```
  06:00:00.000  FCE.ZONE_SP  WRITE  Set to 1250.
  06:00:00.000  CV.SPEED_SP  WRITE  Set to 0.5.
  06:00:00.100  GATE.Reject  WRITE  Set to false by COIL_REJECT.
  ```

  and no golden holds an `ALM_ZONE` line at 06:00:00 or an `INTERLOCK_TRIP`
  before its fault.
- `normal-run.log`: ten `PRESS  DISCHARGING  Hold satisfied after 40.00 s;
  discharging 1 items.` lines; `06:10:00.000  Billets  FAULT  starve
  injected.`; last line `06:11:51.800  PRESS  IDLE  Batch discharged; ready
  for the next.`; no `ALARM`, `REJECTED` or `INTERLOCK` line.
- `slow-press.log`, in order:

  ```
  06:05:00.000  PRESS  FAULT  slow-cycle injected: fraction=0.9.
  06:10:00.500  ALM_QUEUE  ALARM_RAISED  Hi: 1044.1 above 200.
  06:13:50.300  FCE  DISCHARGING  Hold satisfied after 50.20 s; discharging 1 items.
  06:18:36.000  FCE  IDLE  Batch discharged; ready for the next.
  06:18:36.200  ALM_PYRO  ALARM_RAISED  Hi: 1250.0 above 1130.
  06:18:36.200  ALM_PYRO  ALARM_RAISED  HiHi: 1250.0 above 1150.
  06:18:36.400  GATE.Reject  WRITE  Set to true by COIL_REJECT.
  06:18:37.100  GATE  REJECTED  Item 13 rejected.
  06:18:37.300  ALM_PYRO  ALARM_CLEARED  HiHi: 20.0 back within limits.
  06:18:37.500  GATE.Reject  WRITE  Set to false by COIL_REJECT.
  06:19:00.000  PRESS  FAULT_CLEARED  slow-cycle cleared.
  06:27:50.400  ALM_QUEUE  ALARM_CLEARED  Hi: 148.2 back within limits.
  06:35:00.000  Billets  FAULT  starve injected.
  ```

  with no `FCE` line between the `06:13:50.300` `DISCHARGING` and the
  `06:18:36.000` `IDLE` (the furnace held), one `REJECTED` only, and last line
  `06:36:51.800  PRESS  IDLE  Batch discharged; ready for the next.`
- `stuck-kicker.log`: `06:10:00.000  GATE  FAULT  stuck injected.`; the same
  `ALM_QUEUE` raise and `06:18:36.400  GATE.Reject  WRITE  Set to true by
  COIL_REJECT.`; `06:20:21.700  ALM_PYRO  ALARM_CLEARED  HiHi: 20.0 back within
  limits.`, `06:20:21.900  GATE.Reject  WRITE  Set to false by COIL_REJECT.`,
  `06:29:51.000  ALM_QUEUE  ALARM_CLEARED  Hi: 147.9 back within limits.`; no
  `REJECTED`; last line `06:37:02.500  PRESS  IDLE  Batch discharged; ready for
  the next.`
- `press-jam.log`: `06:05:00.000  PRESS  FAULT  discharge-jam injected.`,
  `06:12:50.300  FCE  DISCHARGING  …` with the next `FCE` line
  `06:20:04.000  FCE  IDLE  …`; no `ALM_PYRO` line before `06:20:00.000  PRESS
  FAULT_CLEARED  discharge-jam cleared.`; then `06:20:04.200  ALM_PYRO
  ALARM_RAISED  HiHi: 1250.0 above 1150.`, `06:20:05.100  GATE  REJECTED  Item
  12 rejected.`, `06:20:05.500  GATE.Reject  WRITE  Set to false by
  COIL_REJECT.`, `06:26:52.700  ALM_QUEUE  ALARM_CLEARED  Hi: 147.8 back within
  limits.`; no `Bay  FULL`; last line `06:36:51.800  PRESS  IDLE  …`.
- `zone-low.log`: ends with exactly

  ```
  06:05:30.000  FCE.ZONE_SP  WRITE  Set to 1050.
  06:05:35.200  ALM_ZONE  ALARM_RAISED  Lo: 1050.0 below 1200.
  06:05:51.700  PRESS  DISCHARGING  Hold satisfied after 40.00 s; discharging 1 items.
  06:05:51.800  PRESS  IDLE  Batch discharged; ready for the next.
  ```

- `pyro-fail-high.log`: `06:05:00.000  PYRO  FAULT  fail-high injected.`,
  `Hi: 1400.0 above 1130.` and `HiHi: 1400.0 above 1150.` at 06:05:00.100,
  `Set to true by COIL_REJECT.` at 06:05:00.300, `Item 5 rejected.` …
  `Item 8 rejected.` at 06:05:51.500 … 06:08:51.500, then ending exactly

  ```
  06:08:51.600  Bay  FULL  Capacity reached; accepting nothing more.
  06:08:51.700  INT_BAY  INTERLOCK_TRIP  Bay.Full abnormal.
  06:08:51.800  Billets.Enabled  WRITE  Set to false by INT_BAY.
  06:10:00.000  INT_BAY.Reset  WRITE  Set to true.
  06:10:01.000  INT_BAY.Reset  WRITE  Set to false.
  ```

Quote the checked lines in the task report.

- [ ] **Step 7: Run the project, then everything**

Run: `dotnet test tests/Millrace.Samples.Tests --nologo` — expect PASS, **121**
(this run rebuilds and copies the goldens to the output).
Run: `dotnet build Millrace.sln -c Release --nologo` — expect `0 Warning(s)`, `0 Error(s)`.
Run: `dotnet test Millrace.sln --nologo` — expect **1549**: 37 / 498 / 176 / 57 /
239 / 167 / 78 / 149 / 27 / 121.
Run: `git status --short --ignored -- 'tests/**/*.actual' 'samples/**/*.actual'`
— delete anything it lists.
Run: `git diff --stat 4c77cb7 -- src/ samples/mine-conveyors/` — expect nothing.
Run: `git status --short` — expect exactly the paths of Step 8.

- [ ] **Step 8: Commit**

```bash
git add samples/wheel-line/scenarios samples/wheel-line/expected tests/Millrace.Samples.Tests/WheelLineStories.cs tests/Millrace.Samples.Tests/WheelLineTests.cs
git commit -F .superpowers/sdd/6b.2/msg-task2.txt
```

with `.superpowers/sdd/6b.2/msg-task2.txt`:

```
test(samples): run the wheel line's six scenarios against goldens

normal-run, slow-press, stuck-kicker, press-jam, zone-low and
pyro-fail-high, each opening with the operator's line-start writes and
each with its golden event log, its causal chain and absences, a quiet
tail of at least 500 ticks, mass conserved on every tick, and a
byte-identical replay from a recording.

A producing line logs every billet, so those scenarios end the shift by
starving the saw. A jam cannot fill the reject bay — a good billet
waiting on the gate hides the over-soaking one behind it — so press-jam
shows that hidden over-soak and its reject when the jam clears, and
pyro-fail-high, which rejects every billet, fills the bay: INT_BAY
stops the saw and refuses a reset, and the line ends held.

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
```

Then `git log -1 --format='%h%n%s%n--%n%b'`.

---

### Task 3: What the log cannot show

**Model:** implementer sonnet; reviewer **opus** (the causal-chain assertions,
R175, R181).

**Files:**
- Modify: `tests/Millrace.Samples.Tests/WheelLineTests.cs` (+ 3 facts, a constant
  and a helper)

**Interfaces:**
- Consumes: `WheelLine.Run`, `WheelLine.Watch`, `WheelLine.Component<T>`;
  `ItemProcessUnit.Items`, `RejectGate.Item`, `ItemSink.LastItem`,
  `ItemInstance.Id`/`Type`/`Properties`, `ProcessPhase.Discharging`,
  `TagValue.AsInt64`, `SimulationClock.Elapsed` (all existing).
- Produces: nothing other tasks use.

These facts pin behaviour Task 2's goldens already show; they pass on their
first run. A failure is a measurement: report it, with the value measured.

- [ ] **Step 1: Add the constant**

In `tests/Millrace.Samples.Tests/WheelLineTests.cs`, replace

```csharp
public class WheelLineTests
{
    [Fact]
    public void ThePlantValidatesWithFiveControllers()
```

with

```csharp
public class WheelLineTests
{
    /// <summary>ALM_PYRO's HiHi limit: the over-soak the coil rejects on, °C.</summary>
    private const double HiHi = 1150.0;

    [Fact]
    public void ThePlantValidatesWithFiveControllers()
```

- [ ] **Step 2: Add the three facts**

Replace the end of the file

```csharp
        Assert.True(replay.IsValid, replay.ToText());
        Assert.Equal(live.Events.ToText(), replay.Events!.ToText());
    }
}
```

with

```csharp
        Assert.True(replay.IsValid, replay.ToText());
        Assert.Equal(live.Events.ToText(), replay.Events!.ToText());
    }

    [Fact]
    public void TheRejectDecisionLandsSevenTicksBeforeTheOverSoakedBilletLeaves()
    {
        IReadOnlyList<SimEventRecord> events = WheelLine.Run("slow-press").Events!.Records;
        int rejected = events.ToList().FindIndex(r => r.Source == "GATE" && r.Code == "REJECTED");
        Assert.True(rejected >= 0, "slow-press rejects nothing.");

        // The billet lands on the gate on the tick the furnace logs IDLE (tick N).
        long n = events.Take(rejected).Last(r => r.Source == "FCE" && r.Code == "IDLE").Tick;
        long hiHi = events.Take(rejected).Last(r => r.Source == "ALM_PYRO" && r.Message.StartsWith("HiHi: ", StringComparison.Ordinal)).Tick;
        long write = events.Take(rejected).Last(r => r.Source == "GATE.Reject" && r.Message == "Set to true by COIL_REJECT.").Tick;

        // R168 with a = c = 1, no on-delay and no lag: the pyrometer reads at N + 1, the alarm
        // raises at N + 2, the coil's write lands at N + a + c + 2 = N + 4; the billet may leave at
        // N + ceil(1.0 s / 0.1 s) + 1 = N + 11.
        Assert.Equal((n + 2, n + 4, n + 11), (hiHi, write, events[rejected].Tick));
    }

    [Fact]
    public void ASlowPressOverSoaksTheHeldBilletOnlyWhileItIsSlowAndTheBeltFillsAndDrains()
    {
        var hottest = new List<(TimeSpan Time, double Celsius)>();
        var count = new List<(TimeSpan Time, long Blanks)>();
        var discharging = new List<(TimeSpan Time, bool Held)>();
        ItemProcessUnit? furnace = null;
        RejectGate? gate = null;

        WheelLine.Watch("slow-press", sim =>
        {
            furnace ??= WheelLine.Component<ItemProcessUnit>(sim, "FCE");
            gate ??= WheelLine.Component<RejectGate>(sim, "GATE");
            TimeSpan now = sim.Clock.Elapsed;
            double inFurnace = furnace.Items.Count > 0 ? furnace.Items.Max(i => i.Properties.Temperature) : 0.0;
            hottest.Add((now, Math.Max(inFurnace, gate.Item?.Properties.Temperature ?? 0.0)));
            count.Add((now, sim.IO.Read("CV.ItemCount").AsInt64));
            discharging.Add((now, sim.IO.Read("FCE.Phase").AsInt64 == (long)ProcessPhase.Discharging));
        });

        TimeSpan slow = TimeSpan.FromSeconds(300);
        TimeSpan cleared = TimeSpan.FromSeconds(1140);

        // Steady before the fault: one blank at most on the belt, every billet at its 1100 °C target.
        Assert.All(count.Where(s => s.Time <= slow), s => Assert.True(s.Blanks <= 1, $"{s.Blanks} blanks at {s.Time}."));
        Assert.All(hottest.Where(s => s.Time <= slow), s => Assert.True(s.Celsius < 1101.0, $"{s.Celsius} °C at {s.Time}."));

        // Slow: the belt fills (six blanks at 2 m spacing on 10 m), the furnace holds a billet in
        // Discharging for minutes (measured 285.7 s), and that billet soaks toward its 1250 °C zone
        // (measured 1249.999 °C) — well above HiHi.
        Assert.Contains(count, s => s.Blanks == 6 && s.Time > slow && s.Time < cleared);
        Assert.True(LongestRun(discharging) >= TimeSpan.FromSeconds(280), $"The furnace held a billet for only {LongestRun(discharging)}.");
        Assert.InRange(hottest.Max(s => s.Celsius), 1245.0, 1250.0);

        // Past HiHi only while the press is slow (measured 840.2 s to 1117.1 s).
        Assert.All(hottest.Where(s => s.Celsius > HiHi), s => Assert.InRange(s.Time, slow, cleared));

        // After the clear the queue drains: from 2000 s on (measured 1981.5 s) one blank at most.
        Assert.All(count.Where(s => s.Time >= TimeSpan.FromSeconds(2000)), s => Assert.True(s.Blanks <= 1, $"{s.Blanks} blanks at {s.Time}."));
    }

    [Fact]
    public void AStuckKickerTurnsTheOverSoakedBilletsIntoWheels()
    {
        var overSoaked = new SortedSet<long>();
        var wheels = new SortedDictionary<long, string>();
        RejectGate? gate = null;
        ItemSink? sink = null;

        Simulation end = WheelLine.Watch("stuck-kicker", sim =>
        {
            gate ??= WheelLine.Component<RejectGate>(sim, "GATE");
            sink ??= WheelLine.Component<ItemSink>(sim, "Wheels");
            if (gate.Item is { } onGate && onGate.Properties.Temperature > HiHi)
            {
                overSoaked.Add(onGate.Id);
            }

            if (sink.LastItem is { } wheel)
            {
                wheels[wheel.Id] = wheel.Type.Name;
            }
        });

        // Billet 13 soaked to 1250 °C behind the slow press; billet 14 (1169.5 °C) was held behind it
        // while the stuck kicker kept 13 on the gate. Both became wheels; nothing reached the bay.
        Assert.Equal([13L, 14L], overSoaked);
        Assert.All(overSoaked, id => Assert.Equal("Wheel", Assert.Contains(id, (IDictionary<long, string>)wheels)));
        Assert.Equal(0L, WheelLine.Component<ItemSink>(end, "Bay").Count.Value);
        Assert.Equal(0L, end.IO.Read("GATE.Rejected").AsInt64);
    }

    /// <summary>The longest stretch of consecutive samples that are true.</summary>
    private static TimeSpan LongestRun(List<(TimeSpan Time, bool Held)> samples)
    {
        TimeSpan longest = TimeSpan.Zero;
        TimeSpan? since = null;
        TimeSpan previous = TimeSpan.Zero;
        foreach ((TimeSpan time, bool held) in samples)
        {
            since = held ? since ?? previous : null;
            if (since is { } start && time - start > longest)
            {
                longest = time - start;
            }

            previous = time;
        }

        return longest;
    }
}
```

- [ ] **Step 3: Run them**

Run: `dotnet test tests/Millrace.Samples.Tests --nologo --filter "FullyQualifiedName~WheelLineTests.TheRejectDecision|FullyQualifiedName~WheelLineTests.ASlowPress|FullyQualifiedName~WheelLineTests.AStuckKicker"`
— expect PASS, 3.
Run: `dotnet test tests/Millrace.Samples.Tests --nologo` — expect PASS, **124**.

- [ ] **Step 4: Run everything**

Run: `dotnet build Millrace.sln -c Release --nologo` — expect `0 Warning(s)`, `0 Error(s)`.
Run: `dotnet test Millrace.sln --nologo` — expect **1552**: 37 / 498 / 176 / 57 /
239 / 167 / 78 / 149 / 27 / 124.
Run: `git status --short` — expect only `tests/Millrace.Samples.Tests/WheelLineTests.cs`.

- [ ] **Step 5: Commit**

```bash
git add tests/Millrace.Samples.Tests/WheelLineTests.cs
git commit -F .superpowers/sdd/6b.2/msg-task3.txt
```

with `.superpowers/sdd/6b.2/msg-task3.txt`:

```
test(samples): prove the wheel line's chain on live runs

The reject decision lands as R168 predicts: HiHi two ticks after the
over-soaked billet reaches the gate, the coil's write four, the billet
leaves at eleven — seven ticks to spare with a 1 s dwell. In slow-press
the belt fills, the furnace holds a billet for 285 s and it soaks to the
1250 °C zone, past HiHi only while the press is slow; after the clear
the queue drains. With the kicker stuck, billets 13 and 14 pass HiHi and
arrive in the Wheels sink as wheels, followed there by id.

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
```

Then `git log -1 --format='%h%n%s%n--%n%b'`.

---

### Task 4: The README and the repository's pages

**Model:** implementer sonnet; reviewer sonnet.

**Files:**
- Create: `samples/wheel-line/README.md`
- Create: `tests/Millrace.Samples.Tests/WheelLineReadmeTests.cs` (4 facts + theory of 6 = 10)
- Modify: `README.md` (three replacements)
- Modify: `docs/control-blocks.md` (one insertion)
- Modify: `docs/superpowers/specs/2026-09-02-industrial-process-simulation-engine-design.md` (four replacements)

**Interfaces:**
- Consumes: `WheelLine.Names`, `WheelLine.Scenarios`, `WheelLine.Readme`,
  `WheelLine.Golden`, `WheelLine.Plant`; Task 2's goldens.
- Produces: nothing other tasks use.

- [ ] **Step 1: Write the sample README**

Create `samples/wheel-line/README.md`:

````markdown
# Wheel line

The second reference sample: a forging cell that heats steel billets in a
furnace, checks each one's temperature at the furnace exit, carries the good
ones on a belt to a press and forges each into a wheel. Six scenarios break it
in six ways. As in `samples/mine-conveyors/`, there is no C# here: everything is
data the `millrace` command line runs.

It is a different domain on purpose. The mine conveyors move bulk ore; here
each billet is an item with its own id, mass and temperature, the press changes
what it is (a billet goes in, a wheel comes out, lighter by its flash), and the
line's rate is set by cycle times rather than by belt speed. The same engine,
the same control blocks and the same scenario files run both; nothing in the
engine knows what a wheel is.

```
Billets ──▶ FCE ──▶ GATE ──▶ CV (10 m) ──▶ PRESS ──▶ Wheels
                     │
                     └─ RejectOut ──▶ Bay (4 billets)

PYRO looks at GATE; HMD looks at CV, 4 m from its tail.
```

| file | what it is |
|---|---|
| `plant.json` | the line and its five controllers |
| `scenarios/*.json` | the six scenarios below |
| `expected/*.log` | the golden event log of each scenario |

## The line

| id | type | what it does |
|---|---|---|
| `Billets` | `item-source` | the saw: one 400 kg billet a minute at 25 °C; its charging table holds two, and the saw waits when it is full |
| `FCE` | `item-process-unit` | the furnace, one billet at a time: heats it toward the zone temperature (time constant 24 s) and discharges it when it reaches 1100 °C. `heatWhileHeld`: a billet that cannot leave keeps soaking |
| `GATE` | `reject-gate` | the measuring station at the furnace exit: holds each billet for 1 s, then passes it to the belt, or kicks it to `Bay` while `Reject` is true |
| `Bay` | `item-sink`, capacity 4 | the reject cradle |
| `CV` | `discrete-belt` | 10 m at 0.5 m/s, blanks at least 2 m apart, so it holds six; a blank cools in transit (time constant 1200 s) |
| `PRESS` | `item-process-unit` | forges one blank in 40 s into a `Wheel`, keeping 92 % of its mass |
| `Wheels` | `item-sink` | finished wheels |
| `PYRO` | `pyrometer` | aimed at `GATE`, no lag: reads the billet on the station, 20 °C when it is empty |
| `HMD` | `pyrometer` | a hot-metal detector aimed at the belt 4 m from its tail, 5 s lag: reads hot only when a blank stands in front of it |

Two inputs sit at their defaults until something writes them (the zone at
20 °C, the belt stopped): the furnace's zone temperature and the belt's speed. The plant binds them as tags,
`FCE.ZONE_SP` and `CV.SPEED_SP`, through its `tags` envelope (with
`CV.ItemCount`, the belt's count, bound read-only beside them). There is no
burner model and no drive; the setpoints are the plant.

**Starting the line.** There is no start sequencer. Every scenario opens with
the operator bringing the line up: at 0 s it writes the zone setpoint, 1250 °C,
and the belt speed, 0.5 m/s. On its first scan the coil drives `GATE.Reject`
false, as a PLC drives every output at power-up. Every golden starts with the
same three lines:

```
06:00:00.000  FCE.ZONE_SP  WRITE  Set to 1250.
06:00:00.000  CV.SPEED_SP  WRITE  Set to 0.5.
06:00:00.100  GATE.Reject  WRITE  Set to false by COIL_REJECT.
```

**Ending the shift.** A running line logs every billet's passage, so a scenario
whose line is still producing ends it by starving the saw (`Billets` `starve`),
lets the line run empty, and runs on quietly for at least 50 s.

## The controllers

All five scan every 100 ms.

| block | type | watches | does |
|---|---|---|---|
| `ALM_PYRO` | `alarm` | `PYRO.Value` | `Hi` 1130 °C (warning), `HiHi` 1150 °C (over-soak), deadband 10 °C, no on-delay |
| `COIL_REJECT` | `coil` | `ALM_PYRO.HiHi.Active` | drives `GATE.Reject` to follow it; claims `GATE.Reject` |
| `ALM_QUEUE` | `alarm` | `HMD.Value` | `Hi` 200 °C, deadband 50 °C, 60 s on-delay: the queue has stood at the detector for a minute |
| `ALM_ZONE` | `alarm` | `FCE.ZONE_SP` | `Lo` 1200 °C and `Hi` 1300 °C, deadband 10 °C, 5 s on-delay |
| `INT_BAY` | `interlock` | `Bay.Full`, normal false | trip: `Billets.Enabled` false; reset: `Billets.Enabled` true; claims `Billets.Enabled` |

**The reject is the PLC's decision.** The pyrometer measures, the alarm
decides, the coil drives the kicker. The coil claims `GATE.Reject`, so nothing
else — no HMI, no scenario — can write it: `millrace tags` lists it `ReadOnly …
claimed by COIL_REJECT`. `INT_BAY` stops the saw when the reject cradle is full
and claims `Billets.Enabled` the same way.

**Why the queue alarm watches a hot-metal detector.** An `alarm` watches a
Double tag, and a belt's `ItemCount` is an Int64 — `millrace validate` refuses an
alarm on it (`MR114`). Lines detect a queue the way this one does: a
hot-metal detector at the queue-full position. Blanks stand 2 m apart from the
head, so a blank standing at 4 m means at least four are queued. A blank
passing at belt speed is in its view for under half a second, and the 5 s lag
keeps the reading low (103 °C at most in steady running); a blank standing
there brings it up to its own temperature within a few seconds, and the 60 s
on-delay waits out the queue a normal press cycle leaves. The lag also holds
the reading up through the 4 s gap while the queue shifts up a place, standing
in for the off-delay a PLC would put on a real detector: with no lag,
`slow-press` clears and re-raises `ALM_QUEUE` at every shift while the belt is
still backed up.

**Why the zone alarm has an on-delay.** At power-up a block's first scan reads
the primed tag image, in which `FCE.ZONE_SP` is still 20 °C: the operator's
write at 0 s is not yet in the image that scan reads. Without an on-delay
`ALM_ZONE` raises `Lo` at 0 s and clears it 0.1 s later; the 5 s on-delay
keeps the start quiet.

## Sizing

Realistic in kind, compressed in time: a real billet takes hours to heat, not
a minute. This is a demonstration line, not a sized design.

| | |
|---|---|
| billet heated from 25 °C to 1100 °C in a 1250 °C zone | 50.2 s |
| billet at the gate, steady running | 1100.02 °C — 30 °C under `Hi` |
| billet held in the furnace | soaks toward 1250 °C: past `HiHi` 10 s after it reaches its target |
| blanks on the belt, steady running | one at most |
| `HMD`, steady running | 103 °C at most — under `ALM_QUEUE`'s 200 °C |
| wheels, steady running | one a minute, the saw's rate; the 40 s press has a third in hand, which is what lets it clear a queue |

A zone at `ALM_ZONE`'s `Lo` limit, 1200 °C, heats a billet in 59.0 s — just
inside the minute the saw allows. Below it the furnace falls behind.

## The reject decision: sizing the dwell

The gate must hold a billet long enough for the pyrometer to read it, the alarm
to raise, the coil to write `Reject` and the write to land — before the billet
leaves. From the tick the billet lands on the station, *N*, the pyrometer reads
it at *N* + 1, the alarm raises on its first scan after that, the coil writes on
its first scan after the alarm publishes, and the write lands one tick later;
the billet may leave at *N* + ⌈dwell / dt⌉ + 1. With the alarm scanning every
*a* ticks and the coil every *c*, the decision is certain when

⌈dwell / dt⌉ ≥ a + c + 1 + a·⌈onDelay / (a·dt)⌉, plus the pyrometer's lag.

On this line dt = 0.1 s, both blocks scan every 100 ms (a = c = 1), `HiHi` has
no on-delay and `PYRO` no lag:

⌈1.0 s / 0.1 s⌉ = 10 ≥ 1 + 1 + 1 + 0 = 3

— seven ticks to spare. The slow-press golden shows it: the over-soaked billet
lands at 06:18:36.000 (*N*), `HiHi` raises at *N* + 2, the coil's write lands at
*N* + 4 and the billet leaves, rejected, at *N* + 11.

## Running a scenario

From the repository root:

```bash
dotnet run --project src/Millrace.Cli -- validate samples/wheel-line/plant.json
dotnet run --project src/Millrace.Cli -- tags samples/wheel-line/plant.json
dotnet run --project src/Millrace.Cli -- run samples/wheel-line/scenarios/slow-press.json
```

Add `--expect samples/wheel-line/expected/<name>.log` to check a run against
its golden: exit 0 if nothing changed, 4 if the behaviour did.

## 1. Normal run

The line starts, makes ten wheels, one a minute, and runs empty when the saw
stops at 600 s. Each billet leaves the furnace 50.2 s after it enters and is
forged 40 s after it reaches the press. No alarm raises, nothing is rejected,
and `Reject` is never written again after the first scan.

```text expected/normal-run.log
06:01:50.300  FCE  DISCHARGING  Hold satisfied after 50.20 s; discharging 1 items.
06:02:51.700  PRESS  DISCHARGING  Hold satisfied after 40.00 s; discharging 1 items.
06:10:00.000  Billets  FAULT  starve injected.
06:11:51.800  PRESS  IDLE  Batch discharged; ready for the next.
```

`dotnet run --project src/Millrace.Cli -- run samples/wheel-line/scenarios/normal-run.json --expect samples/wheel-line/expected/normal-run.log`

## 2. Slow press

The causal chain of the main spec, end to end. At 300 s the press slows to a
tenth of its rate (`slow-cycle`, fraction 0.9): a wheel every 400 s while a
billet still arrives every 60 s. The blanks queue on the belt; when the queue
has stood at the detector for a minute, `ALM_QUEUE` raises. The belt fills, a
good billet waits on the gate for room, and the furnace cannot discharge the
billet behind it: it reached its target at 06:13:50.300 and is held, soaking,
for 285.7 s. When the press next takes a blank the belt moves up, the good
billet goes on, and the over-soaked one reaches the gate at 1250 °C — `Hi` and
`HiHi` raise together, the coil writes `Reject`, and the billet goes to the bay.
The gate empties, the alarm clears and the coil drops `Reject` before the next
billet arrives. The press is put right at 1140 s; the queue drains, `ALM_QUEUE`
clears, and the line runs normally until the saw stops at 2100 s.

```text expected/slow-press.log
06:05:00.000  PRESS  FAULT  slow-cycle injected: fraction=0.9.
06:10:00.500  ALM_QUEUE  ALARM_RAISED  Hi: 1044.1 above 200.
06:13:50.300  FCE  DISCHARGING  Hold satisfied after 50.20 s; discharging 1 items.
06:18:36.000  FCE  IDLE  Batch discharged; ready for the next.
06:18:36.200  ALM_PYRO  ALARM_RAISED  Hi: 1250.0 above 1130.
06:18:36.200  ALM_PYRO  ALARM_RAISED  HiHi: 1250.0 above 1150.
06:18:36.400  GATE.Reject  WRITE  Set to true by COIL_REJECT.
06:18:37.100  GATE  REJECTED  Item 13 rejected.
06:18:37.300  ALM_PYRO  ALARM_CLEARED  HiHi: 20.0 back within limits.
06:18:37.500  GATE.Reject  WRITE  Set to false by COIL_REJECT.
06:19:00.000  PRESS  FAULT_CLEARED  slow-cycle cleared.
06:27:50.400  ALM_QUEUE  ALARM_CLEARED  Hi: 148.2 back within limits.
```

Only billet 13 is rejected. The rejection itself makes room: the next billet
reaches the gate at its target and waits there, good, for the belt.

`dotnet run --project src/Millrace.Cli -- run samples/wheel-line/scenarios/slow-press.json --expect samples/wheel-line/expected/slow-press.log`

## 3. Stuck kicker

The slow press again, but the kicker's actuator sticks at 600 s (`GATE`
`stuck`). The PLC does everything right — `HiHi` raises on billet 13 and the
coil writes `Reject` true — and nothing is rejected: the kicker does not move,
and the over-soaked billet waits on the gate for the belt instead of going to
the bay. Held behind it, billet 14 soaks past `HiHi` too (1169.5 °C). Both
become wheels.

```text expected/stuck-kicker.log
06:10:00.000  GATE  FAULT  stuck injected.
06:18:36.200  ALM_PYRO  ALARM_RAISED  HiHi: 1250.0 above 1150.
06:18:36.400  GATE.Reject  WRITE  Set to true by COIL_REJECT.
06:19:00.000  PRESS  FAULT_CLEARED  slow-cycle cleared.
06:20:21.700  ALM_PYRO  ALARM_CLEARED  HiHi: 20.0 back within limits.
06:20:21.900  GATE.Reject  WRITE  Set to false by COIL_REJECT.
```

The log has no `REJECTED` line; a command is not a confirmation. A real reject
station proves the kick — a sensor on the reject chute, or the gate's own
`Rejected` count — and alarms when a commanded reject does not arrive. This
sample deliberately has no such check, so the failure shows; the tests follow
billets 13 and 14 by id into the `Wheels` sink.

`dotnet run --project src/Millrace.Cli -- run samples/wheel-line/scenarios/stuck-kicker.json --expect samples/wheel-line/expected/stuck-kicker.log`

## 4. Press jam

The press's discharge jams at 300 s: the next wheel it forges (06:05:51.700)
cannot leave. The belt fills, a good billet waits on the gate, and the billet behind it is held in the furnace from
06:12:50.300 — and the pyrometer sees none of it: it looks at the gate, where
the billet is good. While the jam lasts nothing is rejected and `ALM_PYRO`
stays quiet. The jam is cleared at 1200 s; the belt moves up, the good billet
goes on, and the billet that has been soaking for seven minutes reaches the
gate and is rejected five seconds after the clear.

```text expected/press-jam.log
06:05:00.000  PRESS  FAULT  discharge-jam injected.
06:10:00.500  ALM_QUEUE  ALARM_RAISED  Hi: 1044.1 above 200.
06:12:50.300  FCE  DISCHARGING  Hold satisfied after 50.20 s; discharging 1 items.
06:20:00.000  PRESS  FAULT_CLEARED  discharge-jam cleared.
06:20:04.200  ALM_PYRO  ALARM_RAISED  HiHi: 1250.0 above 1150.
06:20:04.400  GATE.Reject  WRITE  Set to true by COIL_REJECT.
06:20:05.100  GATE  REJECTED  Item 12 rejected.
06:20:05.500  GATE.Reject  WRITE  Set to false by COIL_REJECT.
06:26:52.700  ALM_QUEUE  ALARM_CLEARED  Hi: 147.8 back within limits.
```

A jam does not fill the reject bay. With the belt full and a good billet on the
gate, no over-soaked billet can reach the pyrometer, so rejects cannot
accumulate during a jam; the full bay and `INT_BAY` are shown by scenario 6,
where they can.

`dotnet run --project src/Millrace.Cli -- run samples/wheel-line/scenarios/press-jam.json --expect samples/wheel-line/expected/press-jam.log`

## 5. Zone low

At 330 s the zone setpoint is written to 1050 °C, below the 1100 °C discharge
target. `ALM_ZONE` raises `Lo` after its 5 s on-delay. The billet in the
furnace climbs toward 1050 °C and never reaches its target, so the furnace
never discharges again; the press finishes the blank it holds and then
starves. The saw fills its table and waits. Nothing is rejected — nothing
reaches the gate.

```text expected/zone-low.log
06:05:30.000  FCE.ZONE_SP  WRITE  Set to 1050.
06:05:35.200  ALM_ZONE  ALARM_RAISED  Lo: 1050.0 below 1200.
06:05:51.700  PRESS  DISCHARGING  Hold satisfied after 40.00 s; discharging 1 items.
06:05:51.800  PRESS  IDLE  Batch discharged; ready for the next.
```

`dotnet run --project src/Millrace.Cli -- run samples/wheel-line/scenarios/zone-low.json --expect samples/wheel-line/expected/zone-low.log`

## 6. Pyrometer fails high

At 300 s the pyrometer fails high: it reads the top of its range, 1400 °C,
whatever is on the gate. `HiHi` raises and the coil holds `Reject` true, so
every billet is kicked to the bay, good ones included. That is the fail-safe
direction for a reject: a failed instrument costs good billets, never passes a
bad one. After four the bay is full, `INT_BAY` trips and stops the saw, and
the line runs dry. The operator's reset at 600 s is refused — the interlock
resets only when every condition is normal, and the bay is still full.

```text expected/pyro-fail-high.log
06:05:00.000  PYRO  FAULT  fail-high injected.
06:05:00.100  ALM_PYRO  ALARM_RAISED  HiHi: 1400.0 above 1150.
06:05:00.300  GATE.Reject  WRITE  Set to true by COIL_REJECT.
06:05:51.500  GATE  REJECTED  Item 5 rejected.
06:08:51.500  GATE  REJECTED  Item 8 rejected.
06:08:51.600  Bay  FULL  Capacity reached; accepting nothing more.
06:08:51.700  INT_BAY  INTERLOCK_TRIP  Bay.Full abnormal.
06:08:51.800  Billets.Enabled  WRITE  Set to false by INT_BAY.
06:10:00.000  INT_BAY.Reset  WRITE  Set to true.
```

The scenario ends with the line held. A real reject cradle is emptied by a
crane or a forklift, after which the reset is accepted and writes
`Billets.Enabled` true; an `item-sink` cannot be emptied, so no scenario can
show that recovery.

`dotnet run --project src/Millrace.Cli -- run samples/wheel-line/scenarios/pyro-fail-high.json --expect samples/wheel-line/expected/pyro-fail-high.log`

## Known limits

- **No burner or zone controller.** The zone temperature is a written setpoint,
  reached at once; a real zone heats and cools at a rate its burners set.
- **No descaler.** Scale on the billet's surface, and the descaler that blasts
  it off before the press, are not modelled.
- **One press.** A real cell may feed two presses or a press and a ring mill.
- **Lumped billet temperature.** Each billet has one temperature; a real one is
  hotter at its core than its skin as it heats, and the reverse as it cools.
- **The zone held as a written setpoint.** `ALM_ZONE` watches the setpoint, not
  a thermocouple, because there is no zone model to measure.
- **Cold blanks are not rejected.** Blanks queued on the belt behind a slow
  press cool — to about 620 °C in scenario 2 — and are forged anyway; a real
  press would refuse them. A second pyrometer at the press would be the check.
- **No proof of the kick** (scenario 3).
````

- [ ] **Step 2: Write the README tests**

Create `tests/Millrace.Samples.Tests/WheelLineReadmeTests.cs`:

````csharp
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Millrace.Samples.Tests;

/// <summary>
/// The wheel-line README quotes each scenario's log in a block fenced as
/// <c>```text expected/&lt;name&gt;.log</c>; every quoted line must be a whole
/// line of that golden. Its dwell arithmetic must be this plant's, and the
/// repository's own pages must point at the sample.
/// </summary>
public partial class WheelLineReadmeTests
{
    [GeneratedRegex(@"^```text (expected/[a-z-]+\.log)\n(.*?)^```", RegexOptions.Multiline | RegexOptions.Singleline)]
    private static partial Regex QuotedBlock();

    private static string Readme => File.ReadAllText(WheelLine.Readme).ReplaceLineEndings("\n");

    [Fact]
    public void EveryScenarioHasOneQuotedBlockAndItsCommand()
    {
        string readme = Readme;
        string[] quoted = QuotedBlock().Matches(readme).Select(m => m.Groups[1].Value).ToArray();

        Assert.Equal(WheelLine.Names.Select(n => $"expected/{n}.log"), quoted);
        Assert.All(WheelLine.Names, name => Assert.Contains(
            $"run samples/wheel-line/scenarios/{name}.json --expect samples/wheel-line/expected/{name}.log",
            readme,
            StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(WheelLine.Scenarios), MemberType = typeof(WheelLine))]
    public void EveryQuotedLineIsAWholeLineOfItsGolden(string name)
    {
        Match block = Assert.Single(QuotedBlock().Matches(Readme), m => m.Groups[1].Value == $"expected/{name}.log");
        HashSet<string> golden = [.. File.ReadAllText(WheelLine.Golden(name)).ReplaceLineEndings("\n").Split('\n')];
        string[] lines = block.Groups[2].Value.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.NotEmpty(lines);
        Assert.All(lines, line => Assert.True(golden.Contains(line), $"README quotes a line '{name}' does not log: '{line}'."));
    }

    [Fact]
    public void TheDwellArithmeticIsThisPlantsAndTheLineStartIsEveryGoldensFirstThreeLines()
    {
        string readme = Readme;
        using JsonDocument plant = JsonDocument.Parse(File.ReadAllText(WheelLine.Plant));
        JsonElement root = plant.RootElement;
        JsonElement gate = root.GetProperty("components").EnumerateArray().Single(c => c.GetProperty("id").GetString() == "GATE");
        JsonElement pyro = root.GetProperty("components").EnumerateArray().Single(c => c.GetProperty("id").GetString() == "PYRO");
        JsonElement[] controllers = root.GetProperty("controllers").EnumerateArray().ToArray();
        JsonElement hiHi = controllers.Single(c => c.GetProperty("id").GetString() == "ALM_PYRO")
            .GetProperty("parameters").GetProperty("limits").EnumerateArray()
            .Single(l => l.GetProperty("kind").GetString() == "hi-hi");

        // The README's numbers: dt 0.1 s, both blocks every 100 ms, a 1 s dwell, no on-delay, no lag.
        Assert.Equal(100, root.GetProperty("defaults").GetProperty("timeStepMs").GetInt32());
        Assert.Equal(1.0, gate.GetProperty("parameters").GetProperty("dwellSeconds").GetDouble());
        Assert.Equal(0.0, pyro.GetProperty("parameters").GetProperty("spec").GetProperty("lagSeconds").GetDouble());
        Assert.False(hiHi.TryGetProperty("onDelayS", out _));
        Assert.All(controllers, c => Assert.Equal(100, c.GetProperty("scanPeriodMs").GetInt32()));

        // The coil follows HiHi, not Hi (the two raise together, R179), and drives the kicker.
        JsonElement coil = controllers.Single(c => c.GetProperty("id").GetString() == "COIL_REJECT").GetProperty("parameters");
        Assert.Equal("ALM_PYRO.HiHi.Active", coil.GetProperty("condition").GetProperty("tag").GetString());
        Assert.Equal("GATE.Reject", coil.GetProperty("output").GetString());
        Assert.Contains("⌈dwell / dt⌉ ≥ a + c + 1 + a·⌈onDelay / (a·dt)⌉, plus the pyrometer's lag.", readme, StringComparison.Ordinal);
        Assert.Contains("⌈1.0 s / 0.1 s⌉ = 10 ≥ 1 + 1 + 1 + 0 = 3", readme, StringComparison.Ordinal);

        const string LineStart =
            "06:00:00.000  FCE.ZONE_SP  WRITE  Set to 1250.\n" +
            "06:00:00.000  CV.SPEED_SP  WRITE  Set to 0.5.\n" +
            "06:00:00.100  GATE.Reject  WRITE  Set to false by COIL_REJECT.\n";
        Assert.Contains("```\n" + LineStart + "```\n", readme, StringComparison.Ordinal);
        Assert.All(WheelLine.Names, name => Assert.StartsWith(
            LineStart, File.ReadAllText(WheelLine.Golden(name)).ReplaceLineEndings("\n"), StringComparison.Ordinal));
    }

    [Fact]
    public void TheReadmeListsTheKnownLimitsAndWhyTheQueueAlarmWatchesADetector()
    {
        string readme = Readme;

        foreach (string token in new[]
                 {
                     "**No burner or zone controller.**", "**No descaler.**", "**One press.**",
                     "**Lumped billet temperature.**", "**The zone held as a written setpoint.**",
                     "a belt's `ItemCount` is an Int64", "(`MR114`)", "claimed by COIL_REJECT",
                     "the full bay and `INT_BAY` are shown by scenario 6",
                 })
        {
            Assert.Contains(token, readme, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TheRootReadmeTheControlBlocksPageAndTheMainSpecNameTheWheelLine()
    {
        string readme = RepositoryFile("README.md");
        string controlBlocks = RepositoryFile("docs", "control-blocks.md");
        string mainSpec = RepositoryFile("docs", "superpowers", "specs", "2026-09-02-industrial-process-simulation-engine-design.md");

        Assert.Contains("`samples/wheel-line/`, is a forging cell of discrete items", readme, StringComparison.Ordinal);
        Assert.Contains("[wheel-line sample](samples/wheel-line/README.md)", readme, StringComparison.Ordinal);
        Assert.Contains("run samples/wheel-line/scenarios/slow-press.json", readme, StringComparison.Ordinal);
        Assert.DoesNotContain("is planned (plan 6b.2)", readme, StringComparison.Ordinal);
        Assert.Contains("`samples/wheel-line/` puts the coil to work", controlBlocks, StringComparison.Ordinal);
        Assert.Contains("The wheel line is implemented in `samples/wheel-line/` (plan 6b.2)", mainSpec, StringComparison.Ordinal);
        Assert.DoesNotContain("The wheel line is plan 6b. Its chain needs", mainSpec, StringComparison.Ordinal);
    }

    /// <summary>Two levels up from this file is the repository root.</summary>
    private static string RepositoryFile(string first, params string[] rest) =>
        File.ReadAllText(Path.Combine([RepositoryRoot(), first, .. rest])).ReplaceLineEndings("\n");

    private static string RepositoryRoot([CallerFilePath] string thisFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", ".."));
}
````

- [ ] **Step 3: Run them to see the repository-pages fact fail**

Run: `dotnet test tests/Millrace.Samples.Tests --nologo --filter "FullyQualifiedName~WheelLineReadmeTests"`
Expected: `Failed: 1, Passed: 9` —
`TheRootReadmeTheControlBlocksPageAndTheMainSpecNameTheWheelLine` (the root
README does not name the sample yet).

- [ ] **Step 4: Point the repository's pages at the sample**

In `README.md`, replace

```markdown
tripped — each with its golden log. It is data only: no C#. The second sample,
a wheel line of discrete items, is planned (plan 6b.2); the physics and the
reject path it needs are in place (plan 6b.1).
```

with

```markdown
tripped — each with its golden log. It is data only: no C#. The second sample,
`samples/wheel-line/`, is a forging cell of discrete items — a billet saw, a
furnace, a measuring station with a pyrometer and a reject kicker, a belt and a
press — whose PLC rejects an over-soaked billet through an alarm and a coil,
with six scenarios: a normal run, a slow press, a stuck kicker, a press jam, a
low furnace zone and a pyrometer failed high. It is data only too.
```

replace

```markdown
dotnet run --project src/Millrace.Cli -- run samples/mine-conveyors/scenarios/pull-key.json  # the sample: a pull-key stops the line
```

with

```markdown
dotnet run --project src/Millrace.Cli -- run samples/mine-conveyors/scenarios/pull-key.json  # the sample: a pull-key stops the line
dotnet run --project src/Millrace.Cli -- run samples/wheel-line/scenarios/slow-press.json      # the second: a slow press over-soaks a billet and the PLC rejects it
```

and replace

```markdown
See the [mine-conveyor sample](samples/mine-conveyors/README.md),
[scenarios](docs/scenarios.md), [control blocks](docs/control-blocks.md),
```

with

```markdown
See the [mine-conveyor sample](samples/mine-conveyors/README.md), the
[wheel-line sample](samples/wheel-line/README.md),
[scenarios](docs/scenarios.md), [control blocks](docs/control-blocks.md),
```

In `docs/control-blocks.md`, replace

```markdown
the cascade is tripped, and nothing moves — not then, and not after the reset.
```

with

```markdown
the cascade is tripped, and nothing moves — not then, and not after the reset.

`samples/wheel-line/` puts the coil to work: a pyrometer on a reject station
feeds an alarm whose `HiHi` a claimed coil follows to drive the kicker, the
station's dwell sized by the rule under `Coil` below, and an interlock on the
reject cradle's `Full` stops the billet saw. Its six scenarios include a stuck
kicker the PLC cannot see and a pyrometer that fails high and rejects every
billet.
```

In `docs/superpowers/specs/2026-09-02-industrial-process-simulation-engine-design.md`,
replace

```markdown
data folder, not a C# project — and §15.2 names plan 6b.
```

with

```markdown
data folder, not a C# project — and §15.2 names plan 6b.

**Amended 2026-10-02 by plan 6b.2** (`docs/superpowers/specs/2026-10-02-wheel-line-sample-design.md`):
§4's samples and §15.2 describe the wheel-line sample as it was built — a data
folder, `samples/wheel-line/`, like the mine conveyors.
```

replace

```markdown
`tests/Millrace.Samples.Tests` — and the wheel line (plan 6b).
```

with

```markdown
`tests/Millrace.Samples.Tests` — and `samples/wheel-line/`, the same shape (plan 6b.2).
```

replace

```markdown
Billet source → furnace → conveyor → press → sink. Roughly five components
beyond the conveyor sample.
```

with

```markdown
Billet source → furnace → reject station (→ reject bay) → conveyor → press →
sink, with a pyrometer on the station and a hot-metal detector on the
conveyor: nine components, none of them conveyor-specific.
```

and replace

```markdown
The wheel line is plan 6b. Its chain needs component behaviour that does not
exist yet — a graded slow-cycle fault on a process unit, a blocked furnace batch
that keeps soaking, a reject path for discrete items — which 6b designs first.
```

with

```markdown
The wheel line is implemented in `samples/wheel-line/` (plan 6b.2), a data
folder like the mine conveyors, on the component behaviour plan 6b.1 added: a
graded slow-cycle fault on a process unit, a furnace batch that keeps soaking
while it is held, and a reject station for discrete items. The temperature
interlock is a pyrometer, an alarm and a coil that drives the station's kicker;
a hot-metal detector on the conveyor raises the queue alarm.
```

- [ ] **Step 5: Run everything**

Run: `dotnet test tests/Millrace.Samples.Tests --nologo` — expect PASS, **134**.
Run: `dotnet test tests/Millrace.Control.Tests --nologo` — expect PASS, 149
(`DocumentationTests` unchanged: the new paragraph quotes no log line).
Run: `dotnet build Millrace.sln -c Release --nologo` — expect `0 Warning(s)`, `0 Error(s)`.
Run: `dotnet test Millrace.sln --nologo` — expect **1562**: 37 / 498 / 176 / 57 /
239 / 167 / 78 / 149 / 27 / 134.
Run: `git diff --stat 4c77cb7 -- src/` — expect nothing.
Run: `git status --short` — expect exactly the five paths of Step 6.

- [ ] **Step 6: Commit**

```bash
git add samples/wheel-line/README.md tests/Millrace.Samples.Tests/WheelLineReadmeTests.cs README.md docs/control-blocks.md docs/superpowers/specs/2026-09-02-industrial-process-simulation-engine-design.md
git commit -F .superpowers/sdd/6b.2/msg-task4.txt
```

with `.superpowers/sdd/6b.2/msg-task4.txt`:

```
docs(samples): explain the wheel line and point the docs at it

The sample README gives the line and its controllers, the operator's
line-start writes, why the queue alarm watches a hot-metal detector and
why the zone alarm has an on-delay, the sizing, the dwell arithmetic of
R168 for this line (ceil(1.0 / 0.1) = 10 >= 3), each scenario with its
quoted golden lines, and the known limits. Tests pin every quoted line
to its golden and the arithmetic to the plant file. The root README,
the control-blocks page and the main spec's §4 and §15.2 now name the
sample.

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
```

Then `git log -1 --format='%h%n%s%n--%n%b'`.

---

## Spec coverage

| Spec criterion / section | Task | Tests |
|---|---|---|
| 1. `plant.json` loads with no diagnostic | 1 | `WheelLineTests.ThePlantValidatesWithFiveControllers`, `TheLoaderReportsNothingAtAllNotEvenAWarning` |
| 1. agrees with the generated plant schema | 1 | `TheGeneratedSchemaAcceptsThePlantAndRejectsAMistakeInIt` |
| 1. flow Billets → FCE → GATE → CV → PRESS → Wheels; RejectOut → Bay (capacity); PYRO observes GATE | 1, 2 | `ThePlantValidatesWithFiveControllers` (6 flow links); every golden; `EveryScenarioConservesMassOnEveryTick` |
| 1. FCE `heatWhileHeld`, `thermal-transfer`, `temperature-at-least` 1100 °C; `AmbientTemperature` write-bound | 1, 2, 3 | `TheRejectGateIsClaimed…` (`FCE.ZONE_SP` line); `normal-run` story (`Hold satisfied after 50.20 s`); `ASlowPressOverSoaks…` |
| 1. CV `thermal-transfer`; `Speed` write-bound; `ItemCount` read-bound | 1, 3 | `TheRejectGateIsClaimed…` (`CV.SPEED_SP`, `CV.ItemCount` lines); `ASlowPressOverSoaks…` (traces `CV.ItemCount`) |
| 1. PRESS `batchSize` 1, timed cycle, `output` `Wheel`, yield < 1 | 1, 2, 3 | `EveryScenarioConservesMassOnEveryTick` (÷ 0.92); `AStuckKickerTurnsTheOverSoakedBilletsIntoWheels` (`Wheel`) |
| 1. materials `Billet` and `Wheel` | 1 | `ThePlantValidatesWithFiveControllers`; `AStuckKicker…` |
| 2. every controller every 100 ms | 1, 4 | `TheDwellArithmeticIsThisPlants…` (every `scanPeriodMs` 100) |
| 2. `ALM_PYRO` Hi and HiHi on `PYRO.Value` | 2 | `slow-press` story (`Hi: 1250.0 above 1130.`, `HiHi: 1250.0 above 1150.`) |
| 2. `COIL_REJECT` follows HiHi, claims `GATE.Reject` | 1, 2 | `TheRejectGateIsClaimed…`; `AScenarioThatWritesAClaimedTagIsRefusedBeforeTickZero` (row 1); every story's `Set to … by COIL_REJECT.` |
| 2. `ALM_QUEUE` Hi (R173: on `HMD.Value`) | 2 | `slow-press`, `stuck-kicker`, `press-jam` stories (raise, clear, no re-raise); `normal-run` absence of `ALARM_RAISED` |
| 2. `ALM_ZONE` Lo and Hi on the zone setpoint | 2 | `zone-low` story (`Lo: 1050.0 below 1200.`, none before the write) |
| 2. `INT_BAY` on `Bay.Full`; trip/reset writes; claims `Billets.Enabled` | 1, 2 | `TheRejectGateIsClaimed…`; `AScenarioThatWritesAClaimedTag…` (row 2); `pyro-fail-high` story |
| 3. dwell by R168 with margin; README arithmetic | 3, 4 | `TheRejectDecisionLandsSevenTicksBeforeTheOverSoakedBilletLeaves`; `TheDwellArithmeticIsThisPlantsAndTheLineStartIsEveryGoldensFirstThreeLines` |
| 4. steady billets within a few degrees of target, margin below Hi | 2, 3 | `normal-run` absences; `ASlowPressOverSoaks…` (< 1101 °C before 300 s) |
| 4. over-soaked billet well above HiHi in `slow-press` | 3 | `ASlowPressOverSoaks…` (1245–1250 °C) |
| 4. steady queue below `ALM_QUEUE` Hi | 2, 3 | `normal-run` absence of `ALARM_RAISED`; `ASlowPressOverSoaks…` (≤ 1 blank before 300 s) |
| 5. six scenarios, goldens, line-start writes, ≥ 500-tick quiet tail | 2 | `TheScenarioFolderHoldsExactlyTheSixScenarios…`, `EveryScenarioMatchesItsGolden`, `EveryScenarioOpensWithTheLineStartAndTheCoilsFirstScan`, `EveryScenarioSettlesAtLeastFiveHundredTicksBeforeItEnds` |
| 5. the table's six rows (as R177 amends `press-jam` and `pyro-fail-high`) | 2, 3 | `EveryScenarioTellsItsStory` (6 rows); `ASlowPressOverSoaks…`; `AStuckKicker…` |
| 6. validate + schema; golden; replay; quiet tail; `millrace tags` claims | 1, 2 | as above; `EveryScenarioReplaysByteForByteFromARecording` |
| 6. `slow-press` causal chain with temperature and `ItemCount` traces | 2, 3 | `slow-press` story; `ASlowPressOverSoaksTheHeldBilletOnlyWhileItIsSlowAndTheBeltFillsAndDrains` |
| 6. `stuck-kicker`: a wheel made from an over-soaked billet | 3 | `AStuckKickerTurnsTheOverSoakedBilletsIntoWheels` |
| 6. mass conservation in every scenario | 2 | `EveryScenarioConservesMassOnEveryTick` (6 rows) |
| 7. README: line, why a different domain, flow and controller tables, line-start writes, scenarios with quotes and reasons, R168 arithmetic, known limits; quotes pinned | 4 | `WheelLineReadmeTests` (all five) |
| 8. root README, `docs/`, main spec §15.2 | 4 | `TheRootReadmeTheControlBlocksPageAndTheMainSpecNameTheWheelLine` |
| §2 no new component, block or Core change | 1–4 | `git diff --stat 4c77cb7 -- src/` empty (every task) |
| Review Focus 1–5 | 2, 3 | the tests named there |

## Test-count arithmetic

Baseline on `4c77cb7` (measured): 1506 = 37 + 498 + 176 + 57 + 239 + 167 + 78 +
149 + 27 + 78.

| Task | Added to `Millrace.Samples.Tests` | Samples | Suite |
|---|---|---|---|
| 1 | `WheelLineTests`: 4 facts + `AScenarioThatWritesAClaimedTagIsRefusedBeforeTickZero` (2 rows) = 6 | 84 | 1512 |
| 2 | `WheelLineTests`: `TheScenarioFolderHolds…` 1 + six theories × 6 rows (`EveryScenarioOpensWith…`, `EveryScenarioMatchesItsGolden`, `EveryScenarioTellsItsStory`, `EveryScenarioSettles…`, `EveryScenarioConservesMass…`, `EveryScenarioReplays…`) = 37 | 121 | 1549 |
| 3 | `WheelLineTests`: 3 facts | 124 | 1552 |
| 4 | `WheelLineReadmeTests`: 4 facts + `EveryQuotedLineIsAWholeLineOfItsGolden` (6 rows) = 10 | 134 | 1562 |

Final: **1562** = 37 Io.Abstractions / 498 Core / 176 Components / 57 Realtime /
239 Configuration / 167 Scenarios / 78 Cli / 149 Control / 27
Control.Catalogue / 134 Samples (each stage measured in the scratch worktree).
Existing tests changed: none. Existing goldens changed: none.

## Self-review

- Every file this plan creates is shown in full; the two in-place edits of
  `WheelLineTests.cs` (Task 3) and the documentation replacements (Task 4) give
  the exact old and new text, each old text occurring once.
- Every number in the stories, the tests and the README was measured on the
  staged scratch worktree; the goldens are generated, never written here.
- The spec's three unachievable or underspecified points are ruled on: the
  Int64 queue signal (R173), the jam that cannot fill the bay and the bay that
  cannot be emptied (R177), and the quiet tail of a producing line (R176). No
  ruling needs a change under `src/`.
- Names checked across files: `WheelLine.Names` = the scenario files =
  `WheelLineStories.All`'s keys = the README's quoted blocks, in the same order.
