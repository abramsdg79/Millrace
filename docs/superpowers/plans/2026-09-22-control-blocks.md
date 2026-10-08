# Control Blocks Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give the engine a control layer: a pure scan-block contract in
`Millrace.Io.Abstractions`, a scan host in `Millrace.Core` that schedules each block
through the event queue at its own period and publishes the block's outputs as
ordinary tags, five blocks in a new `Millrace.Control` project (timer, permissive,
interlock, alarm, sequencer), and a committed golden event log of the conveyor
plant run under all four of them — plus the two crash paths plan 5b parked.

**Architecture:** One interface, `IScanBlock`, and two structs, `ScanInputs`
and `ScanOutputs`, are the whole contract: a block sees values, a tick, a clock
reading and two elapsed times, and it answers with outputs, writes and events.
It never sees a directory, a binding, a clock or a log. `SimulationBuilder.
AddScanBlock` validates each block against the built tag list (`MR013`–
`MR015`), turns its `Outputs` and `Commands` into ordinary `TagBinding`s over
ordinary ports, and hands `Simulation` a plan per block. `Simulation` schedules
one self-rescheduling `ScanEvent` per block at tick 0; each scan reads the image
published at the end of the previous tick, calls `Scan`, stores the outputs into
the block's own ports, queues its writes through `TagImage.Write`, and logs its
events under the block id. `Millrace.Control` then contains five pure classes that
know nothing but the contract.

**Tech Stack:** .NET 10 (`net10.0`), C#, xUnit. No external runtime
dependencies anywhere under `src/`.

**Spec:** `docs/superpowers/specs/2026-09-22-control-blocks-design.md` (all of
it), an addendum to
`docs/superpowers/specs/2026-09-02-industrial-process-simulation-engine-design.md`
refining its sections 4, 11 and 17.

**Plan sequence:** This is plan 5c. Plans 1–5b are merged on `master` at
`6efd7b0` (947 tests green, Release build with `0 Warning(s)`). Blocks are
**built and attached in code** in 5c: there is no `controllers` section in the
plant JSON, no catalogue descriptor for a block, no loader stage and no new
configuration diagnostic beyond `MR013`–`MR015`. `millrace run` therefore cannot
attach blocks, and the worked example's golden is a **test** golden, not a CLI
one. Nothing in this plan may reference the reference samples of plan 6.

**Task shape.** The spec's suggested ten tasks are kept, with two adjustments
the code argued for:

- **The worked example is its own task (Task 10), separate from the per-block
  host tests (Task 9).** Both are `Millrace.Control.Tests`, but a reviewer can
  sensibly reject the worked example — whose golden is a generated artifact that
  must be read line by line — while approving five small host tests, and vice
  versa. Folding them together would make one task carry four blocks, a 120 s
  run, a 47-tag directory assertion and a golden all at once.
- **Task 1 fixes three crashes, not two.** Plan 5b parked the scenario
  `timeStepMs` overflow and the plant `defaults.timeStepMs` sub-tick. Measured
  on `6efd7b0`, a plant `defaults.timeStepMs` of `1e30` crashes `millrace validate`
  the same way the scenario one does — `System.OverflowException` out of
  `TimeSpan.FromMilliseconds`, exit 134. The same guard covers both, so the
  plant loader gets both checks (R65).

## Global Constraints

- Target framework `net10.0` for every project. **`Millrace.Control` references
  `Millrace.Io.Abstractions` and nothing else** — no `Millrace.Core`, no `Millrace.Components`.
  `Millrace.Core` still references only `Millrace.Io.Abstractions`. `Millrace.Io.Abstractions`
  references nothing. **Zero external runtime package references** in any
  shipping project: `grep -rn "PackageReference" src/` must print nothing. Test
  projects use the same test package versions as
  `tests/Millrace.Core.Tests/Millrace.Core.Tests.csproj`; `Millrace.Control.Tests` adds no
  package at all.
- `Nullable` enabled, `TreatWarningsAsErrors` true, `GenerateDocumentationFile`
  true (a `<see cref>` to a type that does not exist yet is a **build error**;
  reference only types that already exist when the file is compiled),
  deterministic builds. These come from `Directory.Build.props`; a csproj
  repeats only `TargetFramework`, `ImplicitUsings` and `Nullable`.
- **Never use `System.Random`. Never use `string.GetHashCode()`** for anything
  that affects behaviour. **Never let a `Dictionary` or `HashSet` iteration
  order reach an output**: every exported list is sorted with
  `StringComparer.Ordinal` first, or is in declaration order.
- All formatting and parsing uses `CultureInfo.InvariantCulture`. Messages that
  embed a number use `string.Create(CultureInfo.InvariantCulture, $"...")`.
- **Names.** Tag names are exactly what `millrace tags <plant>` prints
  (`CV001.Start`, `Feed.Enabled`) and match ordinally. Component ids for faults
  are the flattened leaf ids (`CV001.Motor`). Fault ids and argument names are
  what the catalogue declares (`thermal-bias`, `amount`).
- A block's owned tag is `<block id>.<pin name>`: `INT01.Ok`, `CUR01.Hi.Active`.
  Pin names carry no block id and no leading or trailing dot.
- Diagnostic messages are human sentences ending in a full stop. A `Fix` begins
  with an **imperative** sentence ending in a full stop and may add one more
  sentence. A `ValidationError.Message` is one string holding both, split at its
  first sentence by the loader, exactly as `MR001`–`MR011` already are.
  Descriptions in `DiagnosticInfo.Explanation` are sentences ending in a full
  stop; titles do not end in a full stop.
- **Every event message a block raises is a sentence ending in a full stop.**
  `EventLog.ToText()` is used exactly as it stands and is **not** changed: every
  golden log in this plan, and the four 5b goldens, depend on its bytes.
- **Report every measurement, never widen a window.** Where an expected value
  stated in this plan disagrees with what the code produces, report the
  measurement in the task report and say which one you changed and why. A golden
  log's *content* is never invented: it is generated with `MILLRACE_UPDATE_GOLDEN=1`,
  read, checked against the task's checklist, and reported.
- xUnit analyzers run under warnings-as-errors: prefer `Assert.Single`,
  `Assert.Contains`, `Assert.Empty` over `Assert.True(x.Any())` and
  `Assert.Equal(1, x.Count())`.
- Licence: MIT. Namespaces: `Millrace.Io` (the contract), `Millrace.Core`,
  `Millrace.Core.Control`, `Millrace.Core.Io`, `Millrace.Control`, `Millrace.Control.Tests`.
- **Git, for every task.** One git command per `Bash` call. `git add` names
  paths explicitly — never `git add -A`, never `git add .`. **Never `git
  stash`.** Every commit message ends with a body trailer, on its own line after
  a blank line, copied verbatim:

  ```
  Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
  ```

  Never put the trailer on the subject line.
- Build and test commands, from the repository root:
  `dotnet build Millrace.sln -c Release --nologo` (expect `0 Warning(s)`, `0
  Error(s)`) and `dotnet test Millrace.sln --nologo`.

## Decisions settled here (carry forward as rulings R65–R79)

These refine the spec where writing real code against the real APIs forced a
choice. Where one differs from the spec's wording, this plan wins and says why.

- **R65 — the plant loader gets both time-step guards, not one.** Spec 8 parks
  the overflow for `ScenarioLoader.ReadTimeStep` and the sub-tick for
  `StructureStage.ReadDefaults`. Measured on `6efd7b0`, `defaults.timeStepMs:
  1e30` crashes `millrace validate` out of `TimeSpan.FromMilliseconds` with exit 134,
  exactly as the scenario one does, and `timeStepMs: 0.00001` crashes later in
  `SimulationClock`. Both are `MR103` in the plant loader and `MR202` in the
  scenario loader, and the bound is the same in both: **more than 86 400 000 ms
  (one day) is out of range**, checked *before* `TimeSpan.FromMilliseconds` is
  called.
- **R66 — a block's owned tags are ordinary ports.** `TagBinding`'s private
  constructor takes a `Port`, and `Port` cannot be null. Rather than invent a
  synthetic port type, an output becomes an `OutputPort<bool|double|long>` owned
  by the block id and an `TagBinding.Read` over it; a command becomes an
  `InputPort<…>` (not required, default `false`/`0`) and a `TagBinding.Write`
  over it. `Build()`'s existing `tag.BindExternal()` loop then marks every
  command externally driven for free, and `TagImage` needs no change at all: an
  output refuses writes because `TagBinding.Read` has no apply path, and a
  command accepts them because `TagBinding.Write` does.
- **R67 — `ScanInputs` and `ScanOutputs` are publicly constructible.** Spec 2
  calls `ScanOutputs` "owned and reused by the host", but `Millrace.Control.Tests`'
  pure suites — spec 6 — must build both without a `Simulation`. Both therefore
  have public constructors, and `ScanOutputs` has a public `Reset()`,
  `TryOutput`, `TryWrite` and `Events` for the host to read back. Every field of
  `ScanOutputs` is a reference (two arrays, two flag arrays and a `List`), so a
  copy of the struct aliases the same buffers and the mutable-struct trap cannot
  bite; there is a test that says so. `default(ScanOutputs)` is unusable and the
  XML doc says so.
- **R68 — the precise claim is: a scan that neither raises an event nor queues a
  write allocates nothing.** Spec 2 asks that "nothing on the scan path
  allocates once the host is initialised", which is not quite achievable and
  would be misleading if left unqualified. Input, command, output and write
  buffers are allocated once; `ScanOutputs.Reset` clears rather than
  reallocates; `List<BlockEvent>` reaches its steady-state capacity after the
  first few events. Two things still allocate: a `Raise` message is a string,
  and a queued write goes out through `TagImage.Write`, whose
  `ConcurrentQueue<PendingWrite>.Enqueue` allocates a segment slot. Both are
  rare by construction — a block raises and commands on transitions, not every
  scan. This is stated, not tested: asserting "no allocation" needs a GC-budget
  harness this repository does not have, and such a test is notoriously flaky
  under a shared test host. `docs/control-blocks.md` carries the qualified
  sentence, not the spec's.
- **R69 — `AddScanBlock` validation lives in `SimulationBuilder.Validate()`, in
  two passes.** Blocks are checked after `CollectTags` so the plant's tags are
  known. Pass one checks the period (`MR013`) and the ids and owned names
  (`MR015`) and *creates* the owned bindings; pass two checks `Inputs` and
  `Writes` (`MR014`) against plant tags **and every block's owned tags**. Two
  passes is what makes spec 5's composition rule true regardless of add order:
  an interlock may list `PERM01.Ok` whether `PERM01` was added before it or
  after.
- **R70 — `ScanEvent`s are scheduled in the `Simulation` constructor, before any
  user event, so the *tick-0* scans hold sequence numbers `0 … n−1`.** At tick 0
  a block therefore scans before a `WriteAt(TimeSpan.Zero, …)` lands. From tick
  1 on this is no longer true: a rescheduled `ScanEvent` takes a *fresh*
  sequence number at the moment it reschedules, so it sorts after any write
  already scheduled for the same tick. That is harmless and deliberate — a scan
  reads the image published at the end of the previous tick, never the live
  ports, so whether a same-tick write is applied before or after the scan
  changes nothing the block sees. `docs/control-blocks.md` states the tick-0
  rule, which is the only one an author can observe.
- **R71 — an interlock issues its writes on the trip scan only.** Spec 4 says
  so, and the alternative — re-commanding every scan while tripped — would put a
  `WRITE` record in the event log ten times a second. "Holding the run command
  low" means nothing re-raises it; the golden shows exactly one
  `CV001.Start  WRITE  Set to false.` after the trip.
- **R72 — the alarm's `Acked` starts true, survives a clear, and `Ack`
  acknowledges a limit that has already returned to normal.** Spec 4 asks that
  the ISA-18.2 "cleared, unacknowledged" state be readable from the
  `Active`/`Acked` pair. That needs four distinguishable states, so:
  `(false, true)` normal, `(true, false)` unacknowledged alarm, `(true, true)`
  acknowledged alarm, `(false, false)` cleared and unacknowledged. A raise sets
  `Acked` false; a clear leaves `Acked` alone. Spec 4's "sets `Acked` on every
  active limit" is widened to "every unacknowledged limit", because otherwise
  `(false, false)` is a state nothing can ever leave.
- **R73 — the worked example needs an interlock-reset step.** Measured: at tick
  0 `PERM01.Ok` is false (the safety relay has not been reset, and an owned
  output port reads its default before its block's first scan publishes), so
  `INT01` trips at `06:00:00.000` on `PERM01.Ok`. That is correct interlock
  behaviour — a plant powers up latched — so `SEQ01` gains a second step that
  pulses `INT01.Reset` after the safety relay is healthy. Spec 5's step list is
  extended, not contradicted; it is six steps, not five.
- **R74 — the worked example's alarm raises on the start inrush, not on the
  thermal-bias overload.** Spec 5 says the golden must show "the alarm raising
  as the current climbs" during the 40 s `thermal-bias` injection. Measured (see
  the header of Task 10), `thermal-bias` does not raise `CV001.Current` at all:
  it biases the starter's thermal state, which trips the overload relay on the
  injection tick and de-energises the motor, so the current *falls*. The real
  current excursion in this plant is the start inrush — 11.882303362471239 A
  peak, above 8 A for 0.40 s and above 3 A for 1.37 s, against a running current
  of 1.379925633697662 – 1.5557235827003415 A. The alarm limits are chosen from
  those measurements and the golden shows the alarm raising at the start. This
  is reported, not hidden.
- **R75 — `tests/Millrace.Control.Tests` does not reference `Millrace.Scenarios`, and does
  reference `Millrace.Realtime`.** Spec 9 lists the first and not the second, but
  `ScenarioRunner` builds its own `Simulation` and 5c gives it no way to attach
  a block, so no test in this plan can use it; while spec 1's success criterion
  1 requires a block's output to be visible in `LiveState` and in tick frames,
  which needs a real `RealtimeHub`. The project references `Millrace.Control`,
  `Millrace.Core`, `Millrace.Components`, `Millrace.Configuration` and — added in Task 9, where
  it is first needed, as plan 5b's R61 added the same one — `Millrace.Realtime`, and
  links `tests/Shared/Golden.cs` and the valid plants exactly as
  `Millrace.Scenarios.Tests` links them (plan 5b's R62). `src/Millrace.Control` still sees
  `Millrace.Io.Abstractions` and nothing else.
- **R76 — `TagRef` and `TagSpec` validate their names.** A blank pin name would
  otherwise surface as an `ArgumentException` from `TagBinding.ValidName` at
  `Build()`, far from the block that wrote it. Both records check the name in a
  property initialiser, using the same rules `TagBinding` uses: not null, not
  whitespace, no whitespace inside, no empty segment.
- **R77 — a block's writes reach `IActionRecorder`.** They land through
  `TagImage.Write`, which is one of the three recorded sites, so
  `ScenarioRecorder` recording a live run with blocks attached will record the
  blocks' own commands as scenario writes. In 5c nothing can attach a block to a
  `ScenarioRunner` run, so this cannot bite; it is listed as a parked follow-up
  for the plan that puts blocks in the plant file.
- **R78 — `Simulation.ScanBlockCount`, not a list.** The only thing a test needs
  from outside is "how many blocks does this plant have", and a property that
  allocated a list of ids on every read would be worse than useless. The block
  objects themselves are the caller's; it already has them.
- **R79 — a step's transition is evaluated on the scan *after* the scan that
  enters it.** The entry writes of a sequencer step are queued during the entry
  scan and land at phase 1 of the next tick, so evaluating the transition on the
  entry scan would read a plant that has not seen the writes. Each step
  therefore takes at least two scans. `docs/control-blocks.md` says so.

## File structure

```
src/Millrace.Io.Abstractions/
  TagRef.cs                  new — a plant tag a block reads or commands
  TagSpec.cs                 new — a tag a block owns
  TagNameRules.cs            new — internal; the shared name check
  BlockEvent.cs              new — a code and a message
  ScanInputs.cs              new — what one scan is given
  ScanOutputs.cs             new — what one scan answers
  IScanBlock.cs              new — the contract
src/Millrace.Core/
  Control/OwnedTag.cs        new — internal; a block-owned port, binding and store
  Control/ScanBlockPlan.cs   new — internal; a validated block, ready to run
  Control/ScanBlockRuntime.cs new — internal; one scan's read/scan/store/write/log
  SimulationBuilder.cs       + AddScanBlock, CollectBlocks, MR013–MR015
  Simulation.cs              + ScanBlockCount, ScanEvent
src/Millrace.Control/             new project
  Millrace.Control.csproj
  Condition.cs               a Bool condition with its normal polarity
  BlockWrite.cs              a tag and the value to command
  Timer.cs  TimerMode.cs
  Permissive.cs
  Interlock.cs
  Alarm.cs  AlarmLimit.cs  AlarmLimitKind.cs
  Sequencer.cs  SequenceStep.cs  StepTransition.cs  PredicateOperator.cs
src/Millrace.Scenarios/ScenarioLoader.cs        MR202 for an enormous timeStepMs
src/Millrace.Configuration/
  Loading/StructureStage.cs                MR103 for enormous and sub-tick
  DiagnosticsReference.cs                  trailer MR001–MR011 → MR001–MR015
tests/Millrace.Io.Abstractions.Tests/ScanBlockContractTests.cs      new
tests/Millrace.Core.Tests/
  Fakes/EchoBlock.cs                       new — the stub block
  ScanBlockValidationTests.cs              new
  ScanBlockHostTests.cs                    new
tests/Millrace.Control.Tests/                   new project
  Millrace.Control.Tests.csproj                 + Millrace.Realtime in Task 9, Golden.cs and the plants in Task 10
  Scan.cs                                  the pure harness
  Fakes/Vessel.cs                          the small plant for host tests
  TimerTests.cs  PermissiveTests.cs  InterlockTests.cs
  AlarmTests.cs  SequencerTests.cs
  HostTests.cs   WorkedExampleTests.cs  DocumentationTests.cs
  Golden/conveyor-control.log              generated in Task 10
tests/Millrace.Configuration.Tests/ParseAndStructureTests.cs        + 4
tests/Millrace.Configuration.Tests/DiagnosticsReferenceTests.cs     + 1
tests/Millrace.Scenarios.Tests/ScenarioParseTests.cs                + 1
tests/Millrace.Cli.Tests/
  Plants/sub-tick.json  Scenarios/huge-step.json               new fixtures
  ValidateCommandTests.cs  RunCommandTests.cs                  + 1 each
docs/control-blocks.md                     new
docs/architecture.md                       + "The control layer"
docs/configuration-diagnostics.md          regenerated
README.md                                  status and module list
Millrace.sln                                    + Millrace.Control, Millrace.Control.Tests
```

## Task map

| # | Task | Deliverable | Tests after |
|---|---|---|---|
| 1 | The parked crashes | `MR202` and `MR103` instead of exit 134 | 954 |
| 2 | The contract | `TagRef`, `TagSpec`, `BlockEvent`, `IScanBlock`, `ScanInputs`, `ScanOutputs` | 968 |
| 3 | Host: validation | `AddScanBlock`, `MR013`–`MR015`, owned tags in the directory | 987 |
| 4 | Host: the scan | `ScanEvent`, the timing rule pinned tick-exactly, determinism | 1001 |
| 5 | `Millrace.Control` + `Timer` | the project and TON/TOF/TP | 1016 |
| 6 | `Permissive` + `Interlock` | first-out, latch, reset, trip writes | 1035 |
| 7 | `Alarm` | limits, deadband, on-delay, the four-state pair | 1056 |
| 8 | `Sequencer` | six operators, `After`, timeouts, five commands | 1083 |
| 9 | Host tests per block | each block over a small plant, plus `LiveState`, frames and the recorder | 1091 |
| 10 | The worked example | the committed golden | 1094 |
| 11 | Documentation | `docs/control-blocks.md`, architecture, README, the regenerated page | 1096 |

Per-task deltas, reconciled against `[Fact]` methods and `[Theory]` rows:
7, 14, 19 (17 facts + a two-row theory), 14, 15, 19 (8 + 11), 21, 27 (21 facts +
a six-row theory), 8, 3, 2. `tests/Millrace.Control.Tests` runs 15, 34, 55, 82, 90,
93 and 94 tests at the end of Tasks 5 to 11. Per project at the end: 37
`Millrace.Io.Abstractions`, 416 `Millrace.Core`, 56 `Millrace.Realtime`, 126 `Millrace.Components`,
137 `Millrace.Configuration`, 161 `Millrace.Scenarios`, 69 `Millrace.Cli`, 94 `Millrace.Control`.

Tasks are sequential. Starting point: **947** tests
(23 `Millrace.Io.Abstractions` / 383 `Millrace.Core` / 56 `Millrace.Realtime` /
126 `Millrace.Components` / 132 `Millrace.Configuration` / 160 `Millrace.Scenarios` /
67 `Millrace.Cli`). Suggested models, following plans 3–5b: every task here creates
or edits logic and wants the larger model; so does every reviewer, and Task 11's
prose check against the source.

---

### Task 1: The two parked crashes — a time step out of range is a diagnostic

Plan 5b's final review parked three ways a positive, finite `timeStepMs` still
kills the process. All three are the same mistake: a `double` handed to
`TimeSpan.FromMilliseconds` without a bound, and a `TimeSpan` used as a step
without checking it is at least one tick. Reproduce each from the shipped
binary, then fix it where the value is read.

**Files:**
- Modify: `src/Millrace.Scenarios/ScenarioLoader.cs` (`ReadTimeStep`)
- Modify: `src/Millrace.Configuration/Loading/StructureStage.cs` (`ReadDefaults`)
- Create: `tests/Millrace.Cli.Tests/Plants/sub-tick.json`
- Create: `tests/Millrace.Cli.Tests/Scenarios/huge-step.json`
- Test: `tests/Millrace.Scenarios.Tests/ScenarioParseTests.cs`,
  `tests/Millrace.Configuration.Tests/ParseAndStructureTests.cs`,
  `tests/Millrace.Cli.Tests/ValidateCommandTests.cs`,
  `tests/Millrace.Cli.Tests/RunCommandTests.cs`

**Interfaces:**
- Consumes: `ScenarioDiagnostics.Error(string code, string path, string message,
  string fix) → ConfigDiagnostic` (internal) and `ScenarioDiagnostics.BadValue`
  = `"MR202"`; `LoadState.Error(string code, string path, string message,
  string fix)` and `ConfigDiagnostics.BadParameter` = `"MR103"`;
  `Cli.Run(params string[]) → CliRun(int ExitCode, string Out, string Err)`,
  `Cli.Plant(string)`, `Cli.Scenario(string)`, `ExitCodes.PlantInvalid` = 1.
- Produces: no new public API. Two new private constants,
  `ScenarioLoader.MaxTimeStepMs` and `StructureStage.MaxTimeStepMs`, both
  `86_400_000.0`.

- [ ] **Step 1: Reproduce all three crashes from the built binary**

Run, from the repository root:

```bash
dotnet build Millrace.sln -c Release --nologo
```

Expected: `0 Warning(s)`, `0 Error(s)`.

```bash
mkdir -p /tmp/millrace-5c && cp tests/Millrace.Configuration.Tests/Plants/valid/conveyor-line.json /tmp/millrace-5c/ && printf '{\n  "plant": "conveyor-line.json",\n  "timeStepMs": 1e30,\n  "duration": 1\n}\n' > /tmp/millrace-5c/overflow.json && dotnet run --project src/Millrace.Cli -c Release --no-build -- run /tmp/millrace-5c/overflow.json; echo "EXIT=$?"
```

Expected: `Unhandled exception. System.OverflowException: TimeSpan overflowed
because the duration is too long.` with `ScenarioLoader.ReadTimeStep` in the
stack, and `EXIT=134`.

```bash
sed 's/"timeStepMs": 10/"timeStepMs": 0.00001/' /tmp/millrace-5c/conveyor-line.json > /tmp/millrace-5c/subtick.json && dotnet run --project src/Millrace.Cli -c Release --no-build -- validate /tmp/millrace-5c/subtick.json; echo "EXIT=$?"
```

Expected: `Unhandled exception. System.ArgumentOutOfRangeException: The time
step must be positive.` with `SimulationClock..ctor` in the stack, and
`EXIT=134`.

```bash
sed 's/"timeStepMs": 10/"timeStepMs": 1e30/' /tmp/millrace-5c/conveyor-line.json > /tmp/millrace-5c/hugeplant.json && dotnet run --project src/Millrace.Cli -c Release --no-build -- validate /tmp/millrace-5c/hugeplant.json; echo "EXIT=$?"
```

Expected: `System.OverflowException` with `StructureStage.ReadDefaults` in the
stack, and `EXIT=134`. This is the third crash, which the spec does not name
(R65). Record all three exit codes in the task report.

- [ ] **Step 2: Write the failing tests**

In `tests/Millrace.Scenarios.Tests/ScenarioParseTests.cs`, add one row to the
`[Theory] ABadTopLevelValueIsMr202` (at line 116 today; its rows already
include `"timeStepMs": 0`, `1e-9` and `0.00001`, and it asserts the code, the
path and the message as a tuple). Put the new row immediately after the
`0.00001` row:

```csharp
    [InlineData("""{ "plant": "p.json", "duration": 10, "timeStepMs": 1e30 }""", "$.timeStepMs", "\"timeStepMs\" is 1E+30 ms, which is longer than a day.")]
```

In `tests/Millrace.Configuration.Tests/ParseAndStructureTests.cs`, add two rows to
`BadDefaultsAreParameterErrors`, after the existing `"timeStepMs": "10"` row:

```csharp
    [InlineData("\"timeStepMs\": 10", "\"timeStepMs\": 1e30", "$.defaults.timeStepMs")]
    [InlineData("\"timeStepMs\": 10", "\"timeStepMs\": 0.00001", "$.defaults.timeStepMs")]
```

and add two facts immediately after that theory's method:

```csharp
    [Fact]
    public void ASubTickTimeStepIsReportedNotCrashed()
    {
        ConfigDiagnostic d = Plants.Only(
            Plants.Minimal.Replace("\"timeStepMs\": 10", "\"timeStepMs\": 0.00001", StringComparison.Ordinal));

        Assert.Equal("MR103", d.Code);
        Assert.Equal("$.defaults.timeStepMs", d.Path);
        Assert.Equal("\"timeStepMs\" must be at least one tick (0.0001 ms).", d.Message);
        Assert.Equal("Use the simulation step in milliseconds, such as 10.", d.Fix);
    }

    [Fact]
    public void ATimeStepLongerThanADayIsReportedNotCrashed()
    {
        ConfigDiagnostic d = Plants.Only(
            Plants.Minimal.Replace("\"timeStepMs\": 10", "\"timeStepMs\": 1e30", StringComparison.Ordinal));

        Assert.Equal("MR103", d.Code);
        Assert.Equal("$.defaults.timeStepMs", d.Path);
        Assert.Equal("\"timeStepMs\" is 1E+30 ms, which is longer than a day.", d.Message);
        Assert.Equal("Use the simulation step in milliseconds, such as 10.", d.Fix);
    }
```

Create `tests/Millrace.Cli.Tests/Plants/sub-tick.json` — `minimal.json` with a
sub-tick step:

```json
{
  "defaults": { "seed": 1, "timeStepMs": 0.00001, "startTime": "2026-01-01T06:00:00Z" },
  "materials": [
    { "name": "ore", "kind": "bulk", "properties": { "density": 2000, "moisture": 0.03, "temperature": 15 } }
  ],
  "components": [
    { "id": "FEED", "type": "bulk-source", "parameters": { "material": "ore", "rateKgPerS": 20 } },
    { "id": "CHUTE", "type": "transfer-chute", "parameters": { "capacityKg": 200 } },
    { "id": "PILE", "type": "bulk-sink" }
  ],
  "flows": [
    { "from": "FEED.Out", "to": "CHUTE.In" },
    { "from": "CHUTE.Out", "to": "PILE.In" }
  ]
}
```

Create `tests/Millrace.Cli.Tests/Scenarios/huge-step.json`:

```json
{
  "plant": "../Plants/minimal.json",
  "timeStepMs": 1e30,
  "duration": 5
}
```

Add to `tests/Millrace.Cli.Tests/ValidateCommandTests.cs`, at the end of the class:

```csharp
    [Fact]
    public void ASubTickTimeStepExitsOneInsteadOfCrashing()
    {
        CliRun run = Cli.Run("validate", Cli.Plant("sub-tick.json"));

        Assert.Equal(ExitCodes.PlantInvalid, run.ExitCode);
        Assert.Empty(run.Out);
        Assert.Contains("MR103 $.defaults.timeStepMs", run.Err, StringComparison.Ordinal);
        Assert.Contains("at least one tick", run.Err, StringComparison.Ordinal);
    }
```

Add to `tests/Millrace.Cli.Tests/RunCommandTests.cs`, at the end of the class:

```csharp
    [Fact]
    public void AnEnormousTimeStepExitsOneInsteadOfCrashing()
    {
        CliRun run = Cli.Run("run", Cli.Scenario("huge-step.json"));

        Assert.Equal(ExitCodes.PlantInvalid, run.ExitCode);
        Assert.Empty(run.Out);
        Assert.Contains("MR202 $.timeStepMs", run.Err, StringComparison.Ordinal);
        Assert.Contains("longer than a day", run.Err, StringComparison.Ordinal);
    }
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/Millrace.Configuration.Tests --nologo --filter "FullyQualifiedName~ParseAndStructureTests"`
Expected: FAIL — the three new plant cases crash the test host or report no
diagnostic.

Run: `dotnet test tests/Millrace.Scenarios.Tests --nologo --filter "FullyQualifiedName~ScenarioParseTests"`
Expected: FAIL with `System.OverflowException` on the `1e30` row.

Run: `dotnet test tests/Millrace.Cli.Tests --nologo --filter "FullyQualifiedName~ValidateCommandTests|FullyQualifiedName~RunCommandTests"`
Expected: FAIL on the two new facts.

- [ ] **Step 4: Bound the scenario time step**

In `src/Millrace.Scenarios/ScenarioLoader.cs`, add the constant beside the other
private constants of the class:

```csharp
    /// <summary>A time step longer than a day is a mistake, and 1e30 ms overflows <see cref="TimeSpan"/>.</summary>
    private const double MaxTimeStepMs = 86_400_000.0;
```

and replace the body of `ReadTimeStep` with:

```csharp
    private static TimeSpan? ReadTimeStep(JsonElement root, List<ConfigDiagnostic> diagnostics)
    {
        if (!root.TryGetProperty("timeStepMs", out JsonElement element))
        {
            return null;
        }

        if (element.ValueKind == JsonValueKind.Number && element.TryGetDouble(out double ms) && double.IsFinite(ms) && ms > 0.0)
        {
            if (ms > MaxTimeStepMs)
            {
                diagnostics.Add(ScenarioDiagnostics.Error(
                    ScenarioDiagnostics.BadValue,
                    "$.timeStepMs",
                    string.Create(CultureInfo.InvariantCulture, $"\"timeStepMs\" is {ms} ms, which is longer than a day."),
                    "Use the simulation step in milliseconds, such as 10."));
                return null;
            }

            TimeSpan step = TimeSpan.FromMilliseconds(ms);
            if (step.Ticks > 0)
            {
                return step;
            }

            diagnostics.Add(ScenarioDiagnostics.Error(
                ScenarioDiagnostics.BadValue,
                "$.timeStepMs",
                "\"timeStepMs\" must be at least one tick (0.0001 ms).",
                "Use the simulation step in milliseconds, such as 10."));
            return null;
        }

        diagnostics.Add(ScenarioDiagnostics.Error(
            ScenarioDiagnostics.BadValue,
            "$.timeStepMs",
            "\"timeStepMs\" must be a number greater than zero.",
            "Use the simulation step in milliseconds, such as 10."));
        return null;
    }
```

`System.Globalization` is already imported by this file.

- [ ] **Step 5: Bound the plant time step**

In `src/Millrace.Configuration/Loading/StructureStage.cs`, add the constant beside
the class's other private statics:

```csharp
    /// <summary>A time step longer than a day is a mistake, and 1e30 ms overflows <see cref="TimeSpan"/>.</summary>
    private const double MaxTimeStepMs = 86_400_000.0;
```

and replace the `timeStepMs` block inside `ReadDefaults` (the `TimeSpan? step =
null;` paragraph) with:

```csharp
        TimeSpan? step = null;
        if (defaults.TryGetProperty("timeStepMs", out JsonElement stepElement))
        {
            if (stepElement.ValueKind == JsonValueKind.Number && stepElement.TryGetDouble(out double ms) && double.IsFinite(ms) && ms > 0.0)
            {
                if (ms > MaxTimeStepMs)
                {
                    state.Error(
                        ConfigDiagnostics.BadParameter,
                        "$.defaults.timeStepMs",
                        string.Create(CultureInfo.InvariantCulture, $"\"timeStepMs\" is {ms} ms, which is longer than a day."),
                        "Use the simulation step in milliseconds, such as 10.");
                }
                else
                {
                    TimeSpan candidate = TimeSpan.FromMilliseconds(ms);
                    if (candidate.Ticks > 0)
                    {
                        step = candidate;
                    }
                    else
                    {
                        state.Error(
                            ConfigDiagnostics.BadParameter,
                            "$.defaults.timeStepMs",
                            "\"timeStepMs\" must be at least one tick (0.0001 ms).",
                            "Use the simulation step in milliseconds, such as 10.");
                    }
                }
            }
            else
            {
                state.Error(ConfigDiagnostics.BadParameter, "$.defaults.timeStepMs", "\"timeStepMs\" must be a number greater than zero.", "Use the simulation step in milliseconds, such as 10.");
            }
        }
```

`System.Globalization` is already imported by this file.

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/Millrace.Configuration.Tests --nologo`
Expected: PASS, 136 tests.

Run: `dotnet test tests/Millrace.Scenarios.Tests --nologo`
Expected: PASS, 161 tests.

Run: `dotnet test tests/Millrace.Cli.Tests --nologo`
Expected: PASS, 69 tests.

- [ ] **Step 7: Re-run the three crashes from the binary**

```bash
dotnet build Millrace.sln -c Release --nologo
```

Expected: `0 Warning(s)`, `0 Error(s)`.

```bash
dotnet run --project src/Millrace.Cli -c Release --no-build -- run /tmp/millrace-5c/overflow.json; echo "EXIT=$?"
```

Expected: `MR202 $.timeStepMs` on stderr with the `longer than a day` message
and a `Fix:` line, and `EXIT=1`.

```bash
dotnet run --project src/Millrace.Cli -c Release --no-build -- validate /tmp/millrace-5c/subtick.json; echo "EXIT=$?"
```

Expected: `MR103 $.defaults.timeStepMs`, `at least one tick`, `EXIT=1`.

```bash
dotnet run --project src/Millrace.Cli -c Release --no-build -- validate /tmp/millrace-5c/hugeplant.json; echo "EXIT=$?"
```

Expected: `MR103 $.defaults.timeStepMs`, `longer than a day`, `EXIT=1`.

Paste all three outputs into the task report.

- [ ] **Step 8: Run everything**

Run: `dotnet test Millrace.sln --nologo`
Expected: PASS, 947 + 7 = **954** tests. Report the number the runner prints.

- [ ] **Step 9: Commit**

```bash
git add src/Millrace.Scenarios/ScenarioLoader.cs src/Millrace.Configuration/Loading/StructureStage.cs
```

```bash
git add tests/Millrace.Scenarios.Tests/ScenarioParseTests.cs tests/Millrace.Configuration.Tests/ParseAndStructureTests.cs tests/Millrace.Cli.Tests/ValidateCommandTests.cs tests/Millrace.Cli.Tests/RunCommandTests.cs tests/Millrace.Cli.Tests/Plants/sub-tick.json tests/Millrace.Cli.Tests/Scenarios/huge-step.json
```

```bash
git commit -m "$(cat <<'MSG'
fix(loaders): bound a time step instead of overflowing or aborting

A timeStepMs of 1e30 overflowed TimeSpan in both loaders and a positive
sub-tick step passed the plant loader and aborted in SimulationClock, all
three as exit 134. They are MR202 and MR103 now, before tick 0.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
MSG
)"
```

---

### Task 2: The contract — `IScanBlock` and the two scan structs

Everything `Millrace.Control` is allowed to see. Six small files in
`Millrace.Io.Abstractions`, no dependency on `Millrace.Core`, and a test suite that builds
a block and scans it with nothing but these types — which is the proof that a
block author needs no engine.

**Files:**
- Create: `src/Millrace.Io.Abstractions/TagNameRules.cs`
- Create: `src/Millrace.Io.Abstractions/TagRef.cs`
- Create: `src/Millrace.Io.Abstractions/TagSpec.cs`
- Create: `src/Millrace.Io.Abstractions/BlockEvent.cs`
- Create: `src/Millrace.Io.Abstractions/ScanInputs.cs`
- Create: `src/Millrace.Io.Abstractions/ScanOutputs.cs`
- Create: `src/Millrace.Io.Abstractions/IScanBlock.cs`
- Test: `tests/Millrace.Io.Abstractions.Tests/ScanBlockContractTests.cs`

**Interfaces:**
- Consumes: `TagValue.Bool(bool, TagQuality = default)`,
  `TagValue.Double(double, TagQuality = default)`,
  `TagValue.Int64(long, TagQuality = default)`, `TagValue.Kind`,
  `TagValue.AsBool/AsDouble/AsInt64`; `TagKind.Bool|Double|Int64`.
- Produces:
  - `public sealed record TagRef(string Name, TagKind Kind)`
  - `public sealed record TagSpec(string Name, TagKind Kind, string Unit = "", string Description = "")`
  - `public readonly record struct BlockEvent(string Code, string Message)`
  - `public readonly struct ScanInputs` with
    `ScanInputs(ReadOnlyMemory<TagValue> inputs, ReadOnlyMemory<TagValue> commands, long tick, DateTimeOffset now, double deltaSeconds, double elapsed)`,
    `TagValue Input(int index)`, `TagValue Command(int index)`,
    `int InputCount`, `int CommandCount`, `long Tick`, `DateTimeOffset Now`,
    `double DeltaSeconds`, `double Elapsed`
  - `public struct ScanOutputs` with `ScanOutputs(int outputCount, int writeCount)`,
    `void Set(int index, TagValue value)`, `void Write(int index, TagValue value)`,
    `void Raise(string code, string message)`, `void Reset()`,
    `bool TryOutput(int index, out TagValue value)`,
    `bool TryWrite(int index, out TagValue value)`,
    `IReadOnlyList<BlockEvent> Events`, `int OutputCount`, `int WriteCount`
  - `public interface IScanBlock` with `string Id`, `TimeSpan ScanPeriod`,
    `IReadOnlyList<TagRef> Inputs`, `IReadOnlyList<TagRef> Writes`,
    `IReadOnlyList<TagSpec> Outputs`, `IReadOnlyList<TagSpec> Commands`,
    `void Scan(in ScanInputs inputs, ref ScanOutputs outputs)`

- [ ] **Step 1: Write the failing test**

Create `tests/Millrace.Io.Abstractions.Tests/ScanBlockContractTests.cs`:

```csharp
using Millrace.Io;

namespace Millrace.Io.Abstractions.Tests;

public class ScanBlockContractTests
{
    /// <summary>A block written against the abstractions alone: it echoes its input and counts its scans.</summary>
    private sealed class Echo : IScanBlock
    {
        public string Id => "ECHO";

        public TimeSpan ScanPeriod => TimeSpan.FromMilliseconds(100);

        public IReadOnlyList<TagRef> Inputs { get; } = [new TagRef("T.Enable", TagKind.Bool)];

        public IReadOnlyList<TagRef> Writes { get; } = [new TagRef("U.Enable", TagKind.Bool)];

        public IReadOnlyList<TagSpec> Outputs { get; } = [new TagSpec("Q", TagKind.Bool, "", "The echo")];

        public IReadOnlyList<TagSpec> Commands { get; } = [new TagSpec("Enable", TagKind.Bool, "", "Run the echo")];

        public int Scans { get; private set; }

        public void Scan(in ScanInputs inputs, ref ScanOutputs outputs)
        {
            Scans++;
            bool value = inputs.Input(0).AsBool && inputs.Command(0).AsBool;
            outputs.Set(0, TagValue.Bool(value));
            outputs.Write(0, TagValue.Bool(value));
            if (value)
            {
                outputs.Raise("ECHOED", "The input is high.");
            }
        }
    }

    private static ScanInputs Inputs(bool input, bool command) => new(
        new[] { TagValue.Bool(input) },
        new[] { TagValue.Bool(command) },
        tick: 7L,
        now: new DateTimeOffset(2026, 1, 1, 6, 0, 0, TimeSpan.Zero),
        deltaSeconds: 0.01,
        elapsed: 0.1);

    [Fact]
    public void ATagRefRejectsABlankName()
    {
        Assert.Throws<ArgumentException>(() => new TagRef("  ", TagKind.Bool));
    }

    [Fact]
    public void ATagSpecRejectsANameWithAnEmptySegment()
    {
        Assert.Throws<ArgumentException>(() => new TagSpec(".Active", TagKind.Bool));
        Assert.Throws<ArgumentException>(() => new TagSpec("Hi..Active", TagKind.Bool));
        Assert.Throws<ArgumentException>(() => new TagSpec("Hi Active", TagKind.Bool));
    }

    [Fact]
    public void ATagSpecDefaultsUnitAndDescriptionToEmpty()
    {
        var spec = new TagSpec("Q", TagKind.Bool);

        Assert.Equal("Q", spec.Name);
        Assert.Equal(TagKind.Bool, spec.Kind);
        Assert.Equal(string.Empty, spec.Unit);
        Assert.Equal(string.Empty, spec.Description);
    }

    [Fact]
    public void ABlockEventCarriesACodeAndAMessage()
    {
        var raised = new BlockEvent("ALARM_RAISED", "Hi: 82.3 above 80.");

        Assert.Equal("ALARM_RAISED", raised.Code);
        Assert.Equal("Hi: 82.3 above 80.", raised.Message);
    }

    [Fact]
    public void ScanInputsExposeValuesAndTiming()
    {
        ScanInputs inputs = Inputs(input: true, command: false);

        Assert.Equal(1, inputs.InputCount);
        Assert.Equal(1, inputs.CommandCount);
        Assert.True(inputs.Input(0).AsBool);
        Assert.False(inputs.Command(0).AsBool);
        Assert.Equal(7L, inputs.Tick);
        Assert.Equal(new DateTimeOffset(2026, 1, 1, 6, 0, 0, TimeSpan.Zero), inputs.Now);
        Assert.Equal(0.01, inputs.DeltaSeconds);
        Assert.Equal(0.1, inputs.Elapsed);
    }

    [Fact]
    public void ScanInputsRejectAnIndexOutsideThePins()
    {
        ScanInputs inputs = Inputs(input: true, command: true);

        Assert.Throws<ArgumentOutOfRangeException>(() => inputs.Input(1));
        Assert.Throws<ArgumentOutOfRangeException>(() => inputs.Input(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => inputs.Command(1));
    }

    [Fact]
    public void AnOutputThatWasNotSetIsNotReported()
    {
        var outputs = new ScanOutputs(2, 1);

        Assert.False(outputs.TryOutput(0, out _));
        Assert.False(outputs.TryWrite(0, out _));
        Assert.Empty(outputs.Events);
    }

    [Fact]
    public void SetAndWriteAreReadBackByIndex()
    {
        var outputs = new ScanOutputs(2, 1);

        outputs.Set(1, TagValue.Double(4.5));
        outputs.Write(0, TagValue.Bool(true));

        Assert.False(outputs.TryOutput(0, out _));
        Assert.True(outputs.TryOutput(1, out TagValue set));
        Assert.Equal(4.5, set.AsDouble);
        Assert.True(outputs.TryWrite(0, out TagValue written));
        Assert.True(written.AsBool);
    }

    [Fact]
    public void RaiseCollectsEventsInOrder()
    {
        var outputs = new ScanOutputs(1, 0);

        outputs.Raise("FIRST", "One.");
        outputs.Raise("SECOND", "Two.");

        Assert.Equal(2, outputs.Events.Count);
        Assert.Equal("FIRST", outputs.Events[0].Code);
        Assert.Equal("Two.", outputs.Events[1].Message);
    }

    [Fact]
    public void ResetClearsOutputsWritesAndEvents()
    {
        var outputs = new ScanOutputs(1, 1);
        outputs.Set(0, TagValue.Bool(true));
        outputs.Write(0, TagValue.Bool(true));
        outputs.Raise("CODE", "Message.");

        outputs.Reset();

        Assert.False(outputs.TryOutput(0, out _));
        Assert.False(outputs.TryWrite(0, out _));
        Assert.Empty(outputs.Events);
    }

    [Fact]
    public void ScanOutputsRejectAnIndexOutsideTheirBuffers()
    {
        var outputs = new ScanOutputs(1, 1);

        Assert.Throws<ArgumentOutOfRangeException>(() => outputs.Set(1, TagValue.Bool(true)));
        Assert.Throws<ArgumentOutOfRangeException>(() => outputs.Write(-1, TagValue.Bool(true)));
        Assert.Throws<ArgumentOutOfRangeException>(() => outputs.TryOutput(1, out _));
    }

    [Fact]
    public void RaiseRejectsABlankCodeOrMessage()
    {
        var outputs = new ScanOutputs(1, 0);

        Assert.Throws<ArgumentException>(() => outputs.Raise(" ", "Message."));
        Assert.Throws<ArgumentException>(() => outputs.Raise("CODE", " "));
    }

    [Fact]
    public void ACopyOfScanOutputsSharesItsBuffers()
    {
        var outputs = new ScanOutputs(1, 0);
        ScanOutputs copy = outputs;

        copy.Set(0, TagValue.Bool(true));
        copy.Raise("CODE", "Message.");

        Assert.True(outputs.TryOutput(0, out TagValue value));
        Assert.True(value.AsBool);
        Assert.Single(outputs.Events);
    }

    [Fact]
    public void ABlockNeedsNothingButTheseTypesToScan()
    {
        var block = new Echo();
        var outputs = new ScanOutputs(block.Outputs.Count, block.Writes.Count);

        ScanInputs low = Inputs(input: true, command: false);
        block.Scan(in low, ref outputs);
        Assert.True(outputs.TryOutput(0, out TagValue first));
        Assert.False(first.AsBool);
        Assert.Empty(outputs.Events);

        outputs.Reset();
        ScanInputs high = Inputs(input: true, command: true);
        block.Scan(in high, ref outputs);
        Assert.True(outputs.TryOutput(0, out TagValue second));
        Assert.True(second.AsBool);
        Assert.True(outputs.TryWrite(0, out TagValue commanded));
        Assert.True(commanded.AsBool);
        Assert.Equal("ECHOED", Assert.Single(outputs.Events).Code);
        Assert.Equal(2, block.Scans);
        Assert.Equal(TimeSpan.FromMilliseconds(100), block.ScanPeriod);
        Assert.Equal("ECHO", block.Id);
        Assert.Equal("Enable", Assert.Single(block.Commands).Name);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/Millrace.Io.Abstractions.Tests --nologo --filter FullyQualifiedName~ScanBlockContractTests`
Expected: FAIL — the project does not build: `TagRef`, `TagSpec`, `BlockEvent`,
`ScanInputs`, `ScanOutputs` and `IScanBlock` do not exist.

- [ ] **Step 3: Write the contract**

`src/Millrace.Io.Abstractions/TagNameRules.cs`:

```csharp
namespace Millrace.Io;

/// <summary>
/// The one place a tag or pin name is checked. The rules match
/// <c>TagBinding.ValidName</c> in <c>Millrace.Core</c>, which cannot be referenced
/// from here; if one changes, both change.
/// </summary>
internal static class TagNameRules
{
    /// <summary>Returns the name, or throws <see cref="ArgumentException"/> naming <paramref name="parameter"/>.</summary>
    internal static string Check(string name, string parameter)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name, parameter);

        if (name.StartsWith('.') || name.EndsWith('.') || name.Contains("..", StringComparison.Ordinal))
        {
            throw new ArgumentException($"Name '{name}' has an empty segment.", parameter);
        }

        foreach (char c in name)
        {
            if (char.IsWhiteSpace(c))
            {
                throw new ArgumentException($"Name '{name}' contains whitespace.", parameter);
            }
        }

        return name;
    }
}
```

`src/Millrace.Io.Abstractions/TagRef.cs`:

```csharp
namespace Millrace.Io;

/// <summary>
/// A plant tag a scan block reads or commands, by the full name
/// <c>millrace tags</c> prints. The kind must match the tag's; the host reports a
/// mismatch as MR014 rather than converting.
/// </summary>
/// <param name="Name">The tag's full name, such as <c>CV001.Start</c>.</param>
/// <param name="Kind">The kind the block expects.</param>
public sealed record TagRef(string Name, TagKind Kind)
{
    /// <summary>The tag's full name, such as <c>CV001.Start</c>.</summary>
    public string Name { get; init; } = TagNameRules.Check(Name, nameof(Name));
}
```

`src/Millrace.Io.Abstractions/TagSpec.cs`:

```csharp
namespace Millrace.Io;

/// <summary>
/// A tag a scan block owns, named relative to the block: the host publishes it
/// as <c>&lt;block id&gt;.&lt;name&gt;</c>. An output is read-only to everyone
/// else; a command is read-write, so a command bus, a scenario write and a
/// test's <c>WriteAt</c> all reach it.
/// </summary>
/// <param name="Name">The pin name, such as <c>Ok</c> or <c>Hi.Active</c>.</param>
/// <param name="Kind">The value kind.</param>
/// <param name="Unit">Engineering unit; empty for discrete tags.</param>
/// <param name="Description">A sentence fragment for humans.</param>
public sealed record TagSpec(string Name, TagKind Kind, string Unit = "", string Description = "")
{
    /// <summary>The pin name, such as <c>Ok</c> or <c>Hi.Active</c>.</summary>
    public string Name { get; init; } = TagNameRules.Check(Name, nameof(Name));
}
```

`src/Millrace.Io.Abstractions/BlockEvent.cs`:

```csharp
namespace Millrace.Io;

/// <summary>
/// One thing a scan block wants in the event log. The host records it with the
/// block's id as the source and the tick the scan ran on. The message is a
/// sentence ending in a full stop.
/// </summary>
/// <param name="Code">A short upper-case code, such as <c>INTERLOCK_TRIP</c>.</param>
/// <param name="Message">A sentence ending in a full stop.</param>
public readonly record struct BlockEvent(string Code, string Message);
```

`src/Millrace.Io.Abstractions/ScanInputs.cs`:

```csharp
namespace Millrace.Io;

/// <summary>
/// What one scan is given. Values are as of the end of the previous tick — the
/// PLC asymmetry — indexed by position in the block's <see cref="IScanBlock.Inputs"/>
/// and <see cref="IScanBlock.Commands"/> lists.
/// </summary>
public readonly struct ScanInputs
{
    private readonly ReadOnlyMemory<TagValue> _inputs;
    private readonly ReadOnlyMemory<TagValue> _commands;

    /// <summary>Creates the inputs of one scan. The host reuses its buffers; a test may pass arrays.</summary>
    /// <param name="inputs">One value per <see cref="IScanBlock.Inputs"/> entry, in order.</param>
    /// <param name="commands">One value per <see cref="IScanBlock.Commands"/> entry, in order.</param>
    /// <param name="tick">The tick this scan is running on.</param>
    /// <param name="now">The simulation time of that tick.</param>
    /// <param name="deltaSeconds">The simulation time step in seconds.</param>
    /// <param name="elapsed">Simulation seconds since this block's previous scan; zero on the first.</param>
    public ScanInputs(
        ReadOnlyMemory<TagValue> inputs,
        ReadOnlyMemory<TagValue> commands,
        long tick,
        DateTimeOffset now,
        double deltaSeconds,
        double elapsed)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(tick);
        ArgumentOutOfRangeException.ThrowIfNegative(elapsed);

        _inputs = inputs;
        _commands = commands;
        Tick = tick;
        Now = now;
        DeltaSeconds = deltaSeconds;
        Elapsed = elapsed;
    }

    /// <summary>The tick this scan is running on.</summary>
    public long Tick { get; }

    /// <summary>The simulation time of that tick. Never wall-clock.</summary>
    public DateTimeOffset Now { get; }

    /// <summary>The simulation time step in seconds.</summary>
    public double DeltaSeconds { get; }

    /// <summary>Simulation seconds since this block's previous scan; zero on the first.</summary>
    public double Elapsed { get; }

    /// <summary>How many input pins this scan carries.</summary>
    public int InputCount => _inputs.Length;

    /// <summary>How many command pins this scan carries.</summary>
    public int CommandCount => _commands.Length;

    /// <summary>The value of the input pin at <paramref name="index"/>.</summary>
    public TagValue Input(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _inputs.Length);
        return _inputs.Span[index];
    }

    /// <summary>The value of the command pin at <paramref name="index"/>.</summary>
    public TagValue Command(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _commands.Length);
        return _commands.Span[index];
    }
}
```

`src/Millrace.Io.Abstractions/ScanOutputs.cs`:

```csharp
namespace Millrace.Io;

/// <summary>
/// What one scan answers: values for the tags the block owns, values to command
/// on the tags it may write, and events for the log. An output not set in a
/// scan holds its previous value, as a PLC output does.
/// </summary>
/// <remarks>
/// Every field is a reference, so a copy of this struct writes through to the
/// same buffers; the host allocates one per block and calls <see cref="Reset"/>
/// before each scan. <c>default(ScanOutputs)</c> has no buffers and cannot be
/// used — always construct one.
/// </remarks>
public struct ScanOutputs
{
    private readonly TagValue[] _outputs;
    private readonly bool[] _outputSet;
    private readonly TagValue[] _writes;
    private readonly bool[] _writeSet;
    private readonly List<BlockEvent> _events;

    /// <summary>Allocates the buffers for a block with the given pin counts. Called once per block.</summary>
    public ScanOutputs(int outputCount, int writeCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(outputCount);
        ArgumentOutOfRangeException.ThrowIfNegative(writeCount);

        _outputs = new TagValue[outputCount];
        _outputSet = new bool[outputCount];
        _writes = new TagValue[writeCount];
        _writeSet = new bool[writeCount];
        _events = [];
    }

    /// <summary>How many owned outputs this buffer covers.</summary>
    public readonly int OutputCount => _outputs.Length;

    /// <summary>How many commandable tags this buffer covers.</summary>
    public readonly int WriteCount => _writes.Length;

    /// <summary>The events raised in this scan, in the order they were raised.</summary>
    public readonly IReadOnlyList<BlockEvent> Events => _events;

    /// <summary>Publishes a value on the owned output at <paramref name="index"/>.</summary>
    public readonly void Set(int index, TagValue value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _outputs.Length);
        _outputs[index] = value;
        _outputSet[index] = true;
    }

    /// <summary>Commands the plant tag at <paramref name="index"/> in the block's writes.</summary>
    public readonly void Write(int index, TagValue value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _writes.Length);
        _writes[index] = value;
        _writeSet[index] = true;
    }

    /// <summary>Records an event under the block's id. The message is a sentence ending in a full stop.</summary>
    public readonly void Raise(string code, string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        _events.Add(new BlockEvent(code, message));
    }

    /// <summary>Whether the output at <paramref name="index"/> was set in this scan, and its value.</summary>
    public readonly bool TryOutput(int index, out TagValue value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _outputs.Length);
        value = _outputs[index];
        return _outputSet[index];
    }

    /// <summary>Whether the write at <paramref name="index"/> was commanded in this scan, and its value.</summary>
    public readonly bool TryWrite(int index, out TagValue value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _writes.Length);
        value = _writes[index];
        return _writeSet[index];
    }

    /// <summary>Forgets what the previous scan set, wrote and raised. The buffers are kept.</summary>
    public readonly void Reset()
    {
        Array.Clear(_outputSet);
        Array.Clear(_writeSet);
        _events.Clear();
    }
}
```

`src/Millrace.Io.Abstractions/IScanBlock.cs`:

```csharp
namespace Millrace.Io;

/// <summary>
/// A control block: a pure function of the plant it reads and the state it
/// keeps, scanned at its own period by the host. It never sees a directory, a
/// binding, a clock or a log.
/// </summary>
/// <remarks>
/// Three pin classes. <see cref="Inputs"/> are plant tags it reads;
/// <see cref="Writes"/> are plant tags it may command; <see cref="Outputs"/> and
/// <see cref="Commands"/> are tags it owns, published by the host as
/// <c>&lt;Id&gt;.&lt;Name&gt;</c>. Every list is fixed for the life of the
/// block: the host reads them once, at <c>Build()</c>.
/// </remarks>
public interface IScanBlock
{
    /// <summary>Unique across components and blocks; prefixes every owned tag.</summary>
    string Id { get; }

    /// <summary>How often the block scans. A positive whole number of time steps.</summary>
    TimeSpan ScanPeriod { get; }

    /// <summary>Plant tags this block reads, by full name.</summary>
    IReadOnlyList<TagRef> Inputs { get; }

    /// <summary>Plant tags this block may command, by full name. Each must be read-write.</summary>
    IReadOnlyList<TagRef> Writes { get; }

    /// <summary>Tags this block owns and publishes read-only, named relative to the block.</summary>
    IReadOnlyList<TagSpec> Outputs { get; }

    /// <summary>Tags this block owns and publishes read-write, named relative to the block.</summary>
    IReadOnlyList<TagSpec> Commands { get; }

    /// <summary>One scan. Must not throw, must not block, and must not keep either argument.</summary>
    void Scan(in ScanInputs inputs, ref ScanOutputs outputs);
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test tests/Millrace.Io.Abstractions.Tests --nologo --filter FullyQualifiedName~ScanBlockContractTests`
Expected: PASS, 14 tests.

- [ ] **Step 5: Run everything**

Run: `dotnet build Millrace.sln -c Release --nologo` — expect `0 Warning(s)`, `0 Error(s)`.
Run: `dotnet test Millrace.sln --nologo`
Expected: PASS, 954 + 14 = **968** tests. Report the number the runner prints.

- [ ] **Step 6: Commit**

```bash
git add src/Millrace.Io.Abstractions/TagNameRules.cs src/Millrace.Io.Abstractions/TagRef.cs src/Millrace.Io.Abstractions/TagSpec.cs src/Millrace.Io.Abstractions/BlockEvent.cs src/Millrace.Io.Abstractions/ScanInputs.cs src/Millrace.Io.Abstractions/ScanOutputs.cs src/Millrace.Io.Abstractions/IScanBlock.cs tests/Millrace.Io.Abstractions.Tests/ScanBlockContractTests.cs
```

```bash
git commit -m "$(cat <<'MSG'
feat(io): add the scan-block contract

IScanBlock, ScanInputs and ScanOutputs are everything a control block may
see: values in, values and events out, three pin classes, no engine. A
block can be written and scanned against Millrace.Io.Abstractions alone.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
MSG
)"
```

---

### Task 3: The host, part one — `AddScanBlock`, `MR013`–`MR015`, owned tags

A block is attached at build time and checked then. This task adds the builder
surface, the three diagnostics, and the machinery that turns a `TagSpec` into a
real port and a real `TagBinding` so a block's outputs and commands are ordinary
tags (R66). Nothing runs yet: `Simulation` gains a block count and stores the
plans, and Task 4 makes them scan.

**Files:**
- Create: `src/Millrace.Core/Control/OwnedTag.cs`
- Create: `src/Millrace.Core/Control/ScanBlockPlan.cs`
- Modify: `src/Millrace.Core/SimulationBuilder.cs`
- Modify: `src/Millrace.Core/Simulation.cs` (constructor, `ScanBlockCount`)
- Create: `tests/Millrace.Core.Tests/Fakes/EchoBlock.cs`
- Test: `tests/Millrace.Core.Tests/ScanBlockValidationTests.cs`

**Interfaces:**
- Consumes: `IScanBlock`, `TagRef`, `TagSpec` (Task 2);
  `TagBinding.Read(string name, OutputPort<bool> port, string description = "")`,
  `TagBinding.Read(string name, OutputPort<double> port, string unit, double rangeLow = NaN, double rangeHigh = NaN, string description = "", OutputPort<TagQuality>? quality = null)`,
  `TagBinding.Read(string name, OutputPort<long> port, string unit = "count", string description = "")`,
  `TagBinding.Write(string name, InputPort<bool> port, string description = "")`,
  `TagBinding.Write(string name, InputPort<double> port, string unit, double rangeLow = NaN, double rangeHigh = NaN, string description = "")`,
  `TagBinding.Write(string name, InputPort<long> port, string unit = "count", string description = "")`,
  `TagBinding.Name/Kind/Access`;
  `OutputPort<T>(string name, string ownerId)` with a settable `Value`;
  `InputPort<T>(string name, string ownerId, T defaultValue, bool isRequired, bool isLatched = false)`;
  `ValidationError(string Code, string Message, IReadOnlyList<string> ComponentIds)`;
  `SimulationValidationException`; `SimulationOptions.TimeStep`.
- Produces:
  - `public SimulationBuilder AddScanBlock(IScanBlock block)`
  - `public int Simulation.ScanBlockCount { get; }`
  - `internal sealed class Millrace.Core.Control.OwnedTag` with `string Name`,
    `TagBinding Binding`, `void Store(TagValue value)`,
    `static OwnedTag Output(string blockId, string fullName, TagSpec spec)`,
    `static OwnedTag Command(string blockId, string fullName, TagSpec spec)`
  - `internal sealed class Millrace.Core.Control.ScanBlockPlan` with
    `ScanBlockPlan(IScanBlock block, long periodTicks, OwnedTag[] outputs, OwnedTag[] commands)`,
    `IScanBlock Block`, `long PeriodTicks`, `OwnedTag[] Outputs`, `OwnedTag[] Commands`
  - `internal Simulation(ISimComponent[] components, FlowGraph flow, SimulationOptions options, TagImage io, ScanBlockPlan[] blocks)`

- [ ] **Step 1: Write the stub block**

Create `tests/Millrace.Core.Tests/Fakes/EchoBlock.cs`:

```csharp
using Millrace.Io;

namespace Millrace.Core.Tests.Fakes;

/// <summary>
/// The stub control block the host tests use. Its pins are declared fluently,
/// so one class covers a block with no pins, a block that echoes its first
/// input, a block that commands a tag and a block that raises events.
/// </summary>
public sealed class EchoBlock : IScanBlock
{
    private readonly List<TagRef> _inputs = [];
    private readonly List<TagRef> _writes = [];
    private readonly List<TagSpec> _outputs = [];
    private readonly List<TagSpec> _commands = [];

    public EchoBlock(string id, TimeSpan scanPeriod)
    {
        Id = id;
        ScanPeriod = scanPeriod;
    }

    public string Id { get; }

    public TimeSpan ScanPeriod { get; }

    public IReadOnlyList<TagRef> Inputs => _inputs;

    public IReadOnlyList<TagRef> Writes => _writes;

    public IReadOnlyList<TagSpec> Outputs => _outputs;

    public IReadOnlyList<TagSpec> Commands => _commands;

    /// <summary>The tick of every scan, in order.</summary>
    public List<long> ScanTicks { get; } = [];

    /// <summary>The <c>Elapsed</c> every scan was given, in order.</summary>
    public List<double> ElapsedSeconds { get; } = [];

    /// <summary>What input 0 held on every scan, in order.</summary>
    public List<bool> Seen { get; } = [];

    /// <summary>What command 0 held on every scan, in order.</summary>
    public List<bool> SeenCommand { get; } = [];

    /// <summary>Stop publishing the echo, so the held-output rule can be observed.</summary>
    public bool Silent { get; set; }

    /// <summary>Command write 0 on the next scan, once.</summary>
    public bool WriteOnce { get; set; }

    /// <summary>Raise one event on the next scan, once.</summary>
    public bool RaiseOnce { get; set; }

    /// <summary>Raise one event on every scan.</summary>
    public bool RaiseEveryScan { get; set; }

    public EchoBlock Reads(string tag, TagKind kind = TagKind.Bool)
    {
        _inputs.Add(new TagRef(tag, kind));
        return this;
    }

    public EchoBlock MayWrite(string tag, TagKind kind = TagKind.Bool)
    {
        _writes.Add(new TagRef(tag, kind));
        return this;
    }

    public EchoBlock Publishes(string name, TagKind kind = TagKind.Bool, string unit = "", string description = "")
    {
        _outputs.Add(new TagSpec(name, kind, unit, description));
        return this;
    }

    public EchoBlock Accepts(string name, TagKind kind = TagKind.Bool, string unit = "", string description = "")
    {
        _commands.Add(new TagSpec(name, kind, unit, description));
        return this;
    }

    public void Scan(in ScanInputs inputs, ref ScanOutputs outputs)
    {
        ScanTicks.Add(inputs.Tick);
        ElapsedSeconds.Add(inputs.Elapsed);

        bool value = inputs.InputCount > 0 && inputs.Input(0).AsBool;
        Seen.Add(value);
        SeenCommand.Add(inputs.CommandCount > 0 && inputs.Command(0).AsBool);

        if (!Silent && outputs.OutputCount > 0)
        {
            outputs.Set(0, TagValue.Bool(value));
        }

        if (WriteOnce && outputs.WriteCount > 0)
        {
            outputs.Write(0, TagValue.Bool(true));
            WriteOnce = false;
        }

        if (RaiseOnce)
        {
            outputs.Raise("ECHO", "The stub raised an event.");
            RaiseOnce = false;
        }

        if (RaiseEveryScan)
        {
            outputs.Raise("SCANNED", "The stub scanned.");
        }
    }
}
```

- [ ] **Step 2: Write the failing test**

Create `tests/Millrace.Core.Tests/ScanBlockValidationTests.cs`:

```csharp
using Millrace.Core.Io;
using Millrace.Core.Tests.Fakes;
using Millrace.Core.Time;
using Millrace.Core.Validation;
using Millrace.Io;
using Xunit;

namespace Millrace.Core.Tests;

public class ScanBlockValidationTests
{
    private static SimulationOptions Options => new()
    {
        Seed = 1UL,
        StartTime = new DateTimeOffset(2026, 1, 1, 6, 0, 0, TimeSpan.Zero),
        TimeStep = TimeSpan.FromMilliseconds(10),
    };

    private static SimulationBuilder Plant() => new SimulationBuilder(Options).Add(new Thermostat("T")).Add(new Fuse("F"));

    /// <summary>A block with one Bool input, one Bool output and one Bool command, scanning every other tick.</summary>
    private static EchoBlock Block(string id) =>
        new EchoBlock(id, TimeSpan.FromMilliseconds(20))
            .Reads("T.Enable")
            .Publishes("Q", TagKind.Bool, "", "The echo")
            .Accepts("Cmd", TagKind.Bool, "", "A command");

    private static ValidationError Only(SimulationBuilder builder)
    {
        ValidationResult result = builder.Validate();
        Assert.False(result.IsValid);
        return Assert.Single(result.Errors);
    }

    [Fact]
    public void APlantWithNoBlocksHasNoBlocksAndNoExtraTags()
    {
        Simulation sim = Plant().Build();

        Assert.Equal(0, sim.ScanBlockCount);
        Assert.Equal(3, sim.IO.Directory.Count);
    }

    [Fact]
    public void OwnedTagsJoinTheDirectoryWithTheirKindUnitAndDescription()
    {
        Simulation sim = Plant()
            .AddScanBlock(new EchoBlock("B", TimeSpan.FromMilliseconds(20))
                .Reads("T.Enable")
                .Publishes("Q", TagKind.Bool, "", "The echo")
                .Publishes("Count", TagKind.Int64, "", "Scans so far")
                .Publishes("Age", TagKind.Double, "s", "Seconds since the last scan")
                .Accepts("Cmd", TagKind.Bool, "", "A command"))
            .Build();

        Assert.Equal(1, sim.ScanBlockCount);
        Assert.Equal(7, sim.IO.Directory.Count);

        TagDescriptor q = sim.IO.Directory.Find("B.Q");
        Assert.Equal(TagKind.Bool, q.Kind);
        Assert.Equal(TagAccess.ReadOnly, q.Access);
        Assert.Equal("The echo", q.Description);

        TagDescriptor count = sim.IO.Directory.Find("B.Count");
        Assert.Equal(TagKind.Int64, count.Kind);
        Assert.Equal("count", count.Unit);

        TagDescriptor age = sim.IO.Directory.Find("B.Age");
        Assert.Equal(TagKind.Double, age.Kind);
        Assert.Equal("s", age.Unit);

        TagDescriptor command = sim.IO.Directory.Find("B.Cmd");
        Assert.Equal(TagAccess.ReadWrite, command.Access);
    }

    [Fact]
    public void ACommandIsWritableAndAnOutputIsNot()
    {
        Simulation sim = Plant().AddScanBlock(Block("B")).Build();

        sim.IO.Write("B.Cmd", TagValue.Bool(true));
        Assert.Equal(1, sim.IO.PendingWrites);

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => sim.IO.Write("B.Q", TagValue.Bool(true)));
        Assert.Contains("read-only", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AZeroScanPeriodIsMr013()
    {
        SimulationBuilder builder = Plant().AddScanBlock(
            new EchoBlock("B", TimeSpan.Zero).Reads("T.Enable"));

        ValidationError error = Only(builder);
        Assert.Equal("MR013", error.Code);
        Assert.Equal(
            "Block 'B' has a scan period of 0 ms. A scan period must be positive and a whole number of 10 ms steps.",
            error.Message);
    }

    [Fact]
    public void AScanPeriodThatIsNotAMultipleOfTheStepIsMr013()
    {
        SimulationBuilder builder = Plant().AddScanBlock(
            new EchoBlock("B", TimeSpan.FromMilliseconds(15)).Reads("T.Enable"));

        ValidationError error = Only(builder);
        Assert.Equal("MR013", error.Code);
        Assert.Equal(
            "Block 'B' scans every 15 ms, which is not a whole number of 10 ms steps. Use a period that is a multiple of the time step.",
            error.Message);
    }

    [Fact]
    public void AnUnknownInputTagIsMr014()
    {
        SimulationBuilder builder = Plant().AddScanBlock(
            new EchoBlock("B", TimeSpan.FromMilliseconds(20)).Reads("T.Nope"));

        ValidationError error = Only(builder);
        Assert.Equal("MR014", error.Code);
        Assert.Equal(
            "Block 'B' reads tag 'T.Nope', which the plant does not have. Check the name against 'millrace tags', or bind the port it should read.",
            error.Message);
    }

    [Fact]
    public void AnInputOfTheWrongKindIsMr014()
    {
        SimulationBuilder builder = Plant().AddScanBlock(
            new EchoBlock("B", TimeSpan.FromMilliseconds(20)).Reads("T.Output"));

        ValidationError error = Only(builder);
        Assert.Equal("MR014", error.Code);
        Assert.Equal(
            "Block 'B' reads tag 'T.Output' as a Bool, but the plant publishes a Double. Declare the pin with the kind the tag has.",
            error.Message);
    }

    [Fact]
    public void AnUnknownWriteTagIsMr014()
    {
        SimulationBuilder builder = Plant().AddScanBlock(
            new EchoBlock("B", TimeSpan.FromMilliseconds(20)).Reads("T.Enable").MayWrite("T.Nope"));

        ValidationError error = Only(builder);
        Assert.Equal("MR014", error.Code);
        Assert.Equal(
            "Block 'B' commands tag 'T.Nope', which the plant does not have. Check the name against 'millrace tags', or bind the port it should command.",
            error.Message);
    }

    [Fact]
    public void AWriteToAReadOnlyTagIsMr014()
    {
        SimulationBuilder builder = Plant().AddScanBlock(
            new EchoBlock("B", TimeSpan.FromMilliseconds(20)).Reads("T.Enable").MayWrite("T.Output", TagKind.Double));

        ValidationError error = Only(builder);
        Assert.Equal("MR014", error.Code);
        Assert.Equal(
            "Block 'B' commands tag 'T.Output', which is read-only. Command a read-write tag, or bind that port as a writable tag.",
            error.Message);
    }

    [Fact]
    public void AWriteOfTheWrongKindIsMr014()
    {
        SimulationBuilder builder = Plant().AddScanBlock(
            new EchoBlock("B", TimeSpan.FromMilliseconds(20)).Reads("T.Enable").MayWrite("T.Setpoint"));

        ValidationError error = Only(builder);
        Assert.Equal("MR014", error.Code);
        Assert.Equal(
            "Block 'B' commands tag 'T.Setpoint' as a Bool, but the plant publishes a Double. Declare the pin with the kind the tag has.",
            error.Message);
    }

    [Fact]
    public void ABlockIdThatIsAlreadyAComponentIdIsMr015()
    {
        SimulationBuilder builder = Plant().AddScanBlock(
            new EchoBlock("F", TimeSpan.FromMilliseconds(20)).Reads("T.Enable").Publishes("Q"));

        ValidationError error = Only(builder);
        Assert.Equal("MR015", error.Code);
        Assert.Equal(
            "Block id 'F' is already a component id. Ids must be unique across components and blocks; rename one of them.",
            error.Message);
    }

    [Fact]
    public void TwoBlocksWithTheSameIdIsMr015()
    {
        SimulationBuilder builder = Plant()
            .AddScanBlock(new EchoBlock("B", TimeSpan.FromMilliseconds(20)).Reads("T.Enable").Publishes("Q"))
            .AddScanBlock(new EchoBlock("B", TimeSpan.FromMilliseconds(20)).Reads("T.Enable").Publishes("R"));

        ValidationError error = Only(builder);
        Assert.Equal("MR015", error.Code);
        Assert.Equal(
            "Duplicate block id 'B'. Ids must be unique across components and blocks; rename one of them.",
            error.Message);
    }

    [Fact]
    public void AnOwnedTagThatCollidesWithAPlantTagIsMr015()
    {
        var thermostat = new Thermostat("T");
        SimulationBuilder builder = new SimulationBuilder(Options)
            .Add(thermostat)
            .Bind("B.Q", TagBinding.Read("Q", thermostat.Output, "%", 0.0, 200.0, "Bound out of the way"))
            .AddScanBlock(new EchoBlock("B", TimeSpan.FromMilliseconds(20)).Reads("T.Enable").Publishes("Q"));

        ValidationError error = Only(builder);
        Assert.Equal("MR015", error.Code);
        Assert.Equal(
            "Block 'B' owns tag 'B.Q', which the plant already has. Rename the block or the pin; a block's tag is its id followed by the pin name.",
            error.Message);
    }

    [Fact]
    public void ABlockThatDeclaresOnePinNameTwiceIsMr015()
    {
        SimulationBuilder builder = Plant().AddScanBlock(
            new EchoBlock("B", TimeSpan.FromMilliseconds(20)).Reads("T.Enable").Publishes("Q").Accepts("Q"));

        ValidationError error = Only(builder);
        Assert.Equal("MR015", error.Code);
        Assert.Equal(
            "Block 'B' declares tag 'B.Q' twice. Give each output and command its own name.",
            error.Message);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ABlockMayReadAnotherBlocksOutputWhicheverOrderTheyWereAdded(bool readerFirst)
    {
        var reader = new EchoBlock("A", TimeSpan.FromMilliseconds(20)).Reads("B.Q").Publishes("Q");
        var writer = new EchoBlock("B", TimeSpan.FromMilliseconds(20)).Reads("T.Enable").Publishes("Q");

        SimulationBuilder builder = Plant();
        if (readerFirst)
        {
            builder.AddScanBlock(reader).AddScanBlock(writer);
        }
        else
        {
            builder.AddScanBlock(writer).AddScanBlock(reader);
        }

        Assert.True(builder.Validate().IsValid);
    }

    [Fact]
    public void AddScanBlockAfterBuildThrows()
    {
        SimulationBuilder builder = Plant();
        builder.Build();

        Assert.Throws<InvalidOperationException>(() => builder.AddScanBlock(Block("B")));
    }

    [Fact]
    public void AddScanBlockRejectsNullAndABlankId()
    {
        Assert.Throws<ArgumentNullException>(() => Plant().AddScanBlock(null!));
        Assert.Throws<ArgumentException>(
            () => Plant().AddScanBlock(new EchoBlock(" ", TimeSpan.FromMilliseconds(20))));
    }

    [Fact]
    public void BuildThrowsWhenABlockIsInvalid()
    {
        SimulationBuilder builder = Plant().AddScanBlock(
            new EchoBlock("B", TimeSpan.FromMilliseconds(20)).Reads("T.Nope"));

        SimulationValidationException error = Assert.Throws<SimulationValidationException>(() => builder.Build());
        Assert.Equal("MR014", Assert.Single(error.Result.Errors).Code);
    }
}
```

`SimulationValidationException.Result` is a `ValidationResult`; both already
exist in `Millrace.Core.Validation`.

- [ ] **Step 3: Run the test to verify it fails**

Run: `dotnet test tests/Millrace.Core.Tests --nologo --filter FullyQualifiedName~ScanBlockValidationTests`
Expected: FAIL — the project does not build: `AddScanBlock` and `ScanBlockCount`
do not exist.

- [ ] **Step 4: Write the owned tag**

Create `src/Millrace.Core/Control/OwnedTag.cs`:

```csharp
using Millrace.Core.Graph;
using Millrace.Core.Io;
using Millrace.Io;

namespace Millrace.Core.Control;

/// <summary>
/// A tag a scan block owns: a port nothing else in the plant touches, the
/// binding that publishes it, and — for an output — the way the host stores a
/// scan's value into that port. An output is an <see cref="OutputPort{T}"/> the
/// host writes and the image captures; a command is an
/// <see cref="InputPort{T}"/> a writable binding drives, which is what makes a
/// command an ordinary tag write (R66).
/// </summary>
internal sealed class OwnedTag
{
    private readonly Action<TagValue>? _store;

    private OwnedTag(string name, TagBinding binding, Action<TagValue>? store)
    {
        Name = name;
        Binding = binding;
        _store = store;
    }

    /// <summary>The full tag name: the block id, a dot, and the pin name.</summary>
    internal string Name { get; }

    /// <summary>The binding the directory publishes.</summary>
    internal TagBinding Binding { get; }

    /// <summary>Stores a scanned value into the port. A command has nothing to store.</summary>
    internal void Store(TagValue value) => _store?.Invoke(value);

    /// <summary>A read-only owned tag over a fresh output port.</summary>
    internal static OwnedTag Output(string blockId, string fullName, TagSpec spec)
    {
        switch (spec.Kind)
        {
            case TagKind.Bool:
            {
                var port = new OutputPort<bool>(spec.Name, blockId);
                return new OwnedTag(
                    fullName,
                    TagBinding.Read(fullName, port, spec.Description),
                    value => port.Value = value.AsBool);
            }

            case TagKind.Double:
            {
                var port = new OutputPort<double>(spec.Name, blockId);
                return new OwnedTag(
                    fullName,
                    TagBinding.Read(fullName, port, spec.Unit, double.NaN, double.NaN, spec.Description),
                    value => port.Value = value.AsDouble);
            }

            default:
            {
                var port = new OutputPort<long>(spec.Name, blockId);
                return new OwnedTag(
                    fullName,
                    TagBinding.Read(fullName, port, Unit(spec), spec.Description),
                    value => port.Value = value.AsInt64);
            }
        }
    }

    /// <summary>A read-write owned tag over a fresh input port.</summary>
    internal static OwnedTag Command(string blockId, string fullName, TagSpec spec)
    {
        switch (spec.Kind)
        {
            case TagKind.Bool:
            {
                var port = new InputPort<bool>(spec.Name, blockId, false, isRequired: false);
                return new OwnedTag(fullName, TagBinding.Write(fullName, port, spec.Description), null);
            }

            case TagKind.Double:
            {
                var port = new InputPort<double>(spec.Name, blockId, 0.0, isRequired: false);
                return new OwnedTag(
                    fullName,
                    TagBinding.Write(fullName, port, spec.Unit, double.NaN, double.NaN, spec.Description),
                    null);
            }

            default:
            {
                var port = new InputPort<long>(spec.Name, blockId, 0L, isRequired: false);
                return new OwnedTag(fullName, TagBinding.Write(fullName, port, Unit(spec), spec.Description), null);
            }
        }
    }

    /// <summary>An integer tag with no declared unit publishes "count", as every other integer tag does.</summary>
    private static string Unit(TagSpec spec) => spec.Unit.Length == 0 ? "count" : spec.Unit;
}
```

- [ ] **Step 5: Write the block plan**

Create `src/Millrace.Core/Control/ScanBlockPlan.cs`:

```csharp
using Millrace.Io;

namespace Millrace.Core.Control;

/// <summary>
/// A block that passed validation, with everything the runtime needs that only
/// the builder knows: how many ticks its period is, and the owned tags it
/// publishes, in pin order.
/// </summary>
internal sealed class ScanBlockPlan
{
    internal ScanBlockPlan(IScanBlock block, long periodTicks, OwnedTag[] outputs, OwnedTag[] commands)
    {
        Block = block;
        PeriodTicks = periodTicks;
        Outputs = outputs;
        Commands = commands;
    }

    /// <summary>The block itself.</summary>
    internal IScanBlock Block { get; }

    /// <summary>The scan period in ticks; at least one.</summary>
    internal long PeriodTicks { get; }

    /// <summary>The owned outputs, in <see cref="IScanBlock.Outputs"/> order.</summary>
    internal OwnedTag[] Outputs { get; }

    /// <summary>The owned commands, in <see cref="IScanBlock.Commands"/> order.</summary>
    internal OwnedTag[] Commands { get; }
}
```

- [ ] **Step 6: Add the builder surface and the three checks**

In `src/Millrace.Core/SimulationBuilder.cs`, add to the using block at the top:

```csharp
using System.Globalization;
using Millrace.Core.Control;
```

Add the field beside `_explicitTags`:

```csharp
    private readonly List<IScanBlock> _blocks = [];
```

Add the method immediately after `Bind`:

```csharp
    /// <summary>
    /// Attaches a control block (spec 5c §3). The block is checked at
    /// <see cref="Build"/>: its period against the time step (MR013), its
    /// inputs and writes against the tag directory (MR014), and its id and
    /// owned tag names against everything else in the plant (MR015). Its
    /// outputs and commands become ordinary tags.
    /// </summary>
    public SimulationBuilder AddScanBlock(IScanBlock block)
    {
        ThrowIfBuilt();
        ArgumentNullException.ThrowIfNull(block);
        ArgumentException.ThrowIfNullOrWhiteSpace(block.Id, nameof(block));
        _blocks.Add(block);
        return this;
    }
```

Change the two `Validate` members to carry the plans:

```csharp
    /// <summary>Checks the plant without building it. Used by tooling and by Build.</summary>
    public ValidationResult Validate() => Validate(out _, out _);
```

```csharp
    private ValidationResult Validate(out List<TagBinding> tags, out List<ScanBlockPlan> blocks)
```

and, inside that method, replace the line `tags = CollectTags(seen, errors);`
with:

```csharp
        tags = CollectTags(seen, errors);
        blocks = CollectBlocks(seen, tags, errors);
```

Everything after that line in `Validate` is unchanged: the writable-port set
built from `tags` now includes the block commands, which is right — a block
command is externally driven exactly as a plant command is.

Replace the body of `Build` with:

```csharp
    /// <summary>Validates and constructs the simulation. Throws if the plant is invalid.</summary>
    public Simulation Build()
    {
        ThrowIfBuilt();

        ValidationResult result = Validate(out List<TagBinding> tags, out List<ScanBlockPlan> blocks);
        if (!result.IsValid)
        {
            throw new SimulationValidationException(result);
        }

        GraphResolver.TryResolve(_components, out ISimComponent[] ordered, out _);
        FlowGraph flow = FlowGraph.Build(FlowNodes());

        foreach (TagBinding tag in tags)
        {
            tag.BindExternal();
        }

        FreezePorts();
        _built = true;

        var image = new TagImage(new TagDirectory(tags));
        return new Simulation(ordered, flow, _options, image, [.. blocks]);
    }
```

Add the three checks as private members, after `CollectTags`:

```csharp
    /// <summary>
    /// R69: two passes. The first checks each block's period and identity and
    /// creates its owned tags; the second checks every block's inputs and
    /// writes against the plant's tags *and* every block's owned tags, so a
    /// block may read another block's output whichever order they were added.
    /// </summary>
    private List<ScanBlockPlan> CollectBlocks(
        HashSet<string> componentIds,
        List<TagBinding> tags,
        List<ValidationError> errors)
    {
        var plans = new List<ScanBlockPlan>(_blocks.Count);
        if (_blocks.Count == 0)
        {
            return plans;
        }

        var byName = new Dictionary<string, TagBinding>(StringComparer.Ordinal);
        foreach (TagBinding binding in tags)
        {
            byName[binding.Name] = binding;
        }

        var blockIds = new HashSet<string>(StringComparer.Ordinal);
        long stepTicks = _options.TimeStep.Ticks;
        double stepMs = _options.TimeStep.TotalMilliseconds;

        foreach (IScanBlock block in _blocks)
        {
            if (block.ScanPeriod <= TimeSpan.Zero)
            {
                errors.Add(new ValidationError(
                    "MR013",
                    string.Create(CultureInfo.InvariantCulture,
                        $"Block '{block.Id}' has a scan period of {block.ScanPeriod.TotalMilliseconds} ms. " +
                        $"A scan period must be positive and a whole number of {stepMs} ms steps."),
                    [block.Id]));
            }
            else if (block.ScanPeriod.Ticks % stepTicks != 0L)
            {
                errors.Add(new ValidationError(
                    "MR013",
                    string.Create(CultureInfo.InvariantCulture,
                        $"Block '{block.Id}' scans every {block.ScanPeriod.TotalMilliseconds} ms, which is not a " +
                        $"whole number of {stepMs} ms steps. Use a period that is a multiple of the time step."),
                    [block.Id]));
            }

            if (componentIds.Contains(block.Id))
            {
                errors.Add(new ValidationError(
                    "MR015",
                    $"Block id '{block.Id}' is already a component id. Ids must be unique across components and " +
                    $"blocks; rename one of them.",
                    [block.Id]));
            }
            else if (!blockIds.Add(block.Id))
            {
                errors.Add(new ValidationError(
                    "MR015",
                    $"Duplicate block id '{block.Id}'. Ids must be unique across components and blocks; rename " +
                    $"one of them.",
                    [block.Id]));
            }

            var ownNames = new HashSet<string>(StringComparer.Ordinal);
            var outputs = new List<OwnedTag>(block.Outputs.Count);
            var commands = new List<OwnedTag>(block.Commands.Count);

            foreach (TagSpec spec in block.Outputs)
            {
                AddOwned(block, spec, command: false, ownNames, byName, tags, outputs, errors);
            }

            foreach (TagSpec spec in block.Commands)
            {
                AddOwned(block, spec, command: true, ownNames, byName, tags, commands, errors);
            }

            long periodTicks = block.ScanPeriod.Ticks > 0L ? block.ScanPeriod.Ticks / stepTicks : 0L;
            plans.Add(new ScanBlockPlan(block, Math.Max(1L, periodTicks), [.. outputs], [.. commands]));
        }

        foreach (IScanBlock block in _blocks)
        {
            foreach (TagRef pin in block.Inputs)
            {
                CheckPin(block, pin, commanded: false, byName, errors);
            }

            foreach (TagRef pin in block.Writes)
            {
                CheckPin(block, pin, commanded: true, byName, errors);
            }
        }

        return plans;
    }

    /// <summary>Creates one owned tag, or reports MR015 and creates nothing.</summary>
    private static void AddOwned(
        IScanBlock block,
        TagSpec spec,
        bool command,
        HashSet<string> ownNames,
        Dictionary<string, TagBinding> byName,
        List<TagBinding> tags,
        List<OwnedTag> owned,
        List<ValidationError> errors)
    {
        string name = $"{block.Id}.{spec.Name}";

        if (!ownNames.Add(name))
        {
            errors.Add(new ValidationError(
                "MR015",
                $"Block '{block.Id}' declares tag '{name}' twice. Give each output and command its own name.",
                [block.Id]));
            return;
        }

        if (byName.ContainsKey(name))
        {
            errors.Add(new ValidationError(
                "MR015",
                $"Block '{block.Id}' owns tag '{name}', which the plant already has. Rename the block or the " +
                $"pin; a block's tag is its id followed by the pin name.",
                [block.Id]));
            return;
        }

        OwnedTag tag = command
            ? OwnedTag.Command(block.Id, name, spec)
            : OwnedTag.Output(block.Id, name, spec);

        byName[name] = tag.Binding;
        tags.Add(tag.Binding);
        owned.Add(tag);
    }

    /// <summary>Checks one input or write pin against the tag it names (MR014).</summary>
    private static void CheckPin(
        IScanBlock block,
        TagRef pin,
        bool commanded,
        Dictionary<string, TagBinding> byName,
        List<ValidationError> errors)
    {
        string verb = commanded ? "commands" : "reads";

        if (!byName.TryGetValue(pin.Name, out TagBinding? binding))
        {
            errors.Add(new ValidationError(
                "MR014",
                $"Block '{block.Id}' {verb} tag '{pin.Name}', which the plant does not have. Check the name " +
                $"against 'millrace tags', or bind the port it should {(commanded ? "command" : "read")}.",
                [block.Id]));
            return;
        }

        if (binding.Kind != pin.Kind)
        {
            errors.Add(new ValidationError(
                "MR014",
                $"Block '{block.Id}' {verb} tag '{pin.Name}' as a {pin.Kind}, but the plant publishes a " +
                $"{binding.Kind}. Declare the pin with the kind the tag has.",
                [block.Id]));
            return;
        }

        if (commanded && binding.Access != TagAccess.ReadWrite)
        {
            errors.Add(new ValidationError(
                "MR014",
                $"Block '{block.Id}' commands tag '{pin.Name}', which is read-only. Command a read-write tag, " +
                $"or bind that port as a writable tag.",
                [block.Id]));
        }
    }
```

- [ ] **Step 7: Carry the plans into the simulation**

In `src/Millrace.Core/Simulation.cs`, add `using Millrace.Core.Control;` to the using
block, add the field beside `_faultTargets`:

```csharp
    private readonly ScanBlockPlan[] _blockPlans;
```

change the constructor signature and its first lines to:

```csharp
    internal Simulation(
        ISimComponent[] components,
        FlowGraph flow,
        SimulationOptions options,
        TagImage io,
        ScanBlockPlan[] blocks)
    {
        _components = components;
        _flow = flow;
        _seed = options.Seed;
        _checkConservation = options.CheckConservation;
        _conservationTolerance = options.ConservationTolerance;
        _blockPlans = blocks;
        Clock = new SimulationClock(options.StartTime, options.TimeStep);
        IO = io;
```

(the rest of the constructor is unchanged), and add the property immediately
after `Components`:

```csharp
    /// <summary>How many control blocks are attached (spec 5c §3). Zero for a plant with none.</summary>
    public int ScanBlockCount => _blockPlans.Length;
```

- [ ] **Step 8: Run the test to verify it passes**

Run: `dotnet test tests/Millrace.Core.Tests --nologo --filter FullyQualifiedName~ScanBlockValidationTests`
Expected: PASS, 19 tests (17 facts and a two-row theory).

- [ ] **Step 9: Run everything**

Run: `dotnet build Millrace.sln -c Release --nologo` — expect `0 Warning(s)`, `0 Error(s)`.
Run: `dotnet test Millrace.sln --nologo`
Expected: PASS, 968 + 19 = **987** tests. Report the number the runner prints.

- [ ] **Step 10: Commit**

```bash
git add src/Millrace.Core/Control/OwnedTag.cs src/Millrace.Core/Control/ScanBlockPlan.cs src/Millrace.Core/SimulationBuilder.cs src/Millrace.Core/Simulation.cs
```

```bash
git add tests/Millrace.Core.Tests/Fakes/EchoBlock.cs tests/Millrace.Core.Tests/ScanBlockValidationTests.cs
```

```bash
git commit -m "$(cat <<'MSG'
feat(core): validate and publish a scan block's tags

AddScanBlock checks a block's period (MR013), its inputs and writes
(MR014) and its id and owned names (MR015), then turns its outputs and
commands into ordinary read-only and read-write tags.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
MSG
)"
```

---

### Task 4: The host, part two — the self-rescheduling scan and the timing rule

Spec 3, stated once and pinned tick-exactly here: a scan at tick *N* sees inputs
as of the end of tick *N−1*; its outputs are published at the end of tick *N*;
a write it queued lands at phase 1 of tick *N+1*.

**Files:**
- Create: `src/Millrace.Core/Control/ScanBlockRuntime.cs`
- Modify: `src/Millrace.Core/Simulation.cs` (build the runtimes, schedule, `ScanEvent`)
- Test: `tests/Millrace.Core.Tests/ScanBlockHostTests.cs`

**Interfaces:**
- Consumes: `ScanBlockPlan`, `OwnedTag` (Task 3); `ScanInputs`, `ScanOutputs`,
  `BlockEvent` (Task 2); `TagImage.Read(int)`, `TagImage.Write(int, TagValue)`,
  `TagImage.Directory`; `TagDirectory.Find(string) → TagDescriptor`;
  `EventLog.Record(long tick, DateTimeOffset simTime, string source, string code, string message)`;
  `EventQueue.Schedule(long dueTick, ISimEvent) → long`;
  `SimulationClock.TickCount/Now/DeltaSeconds`.
- Produces:
  - `internal sealed class Millrace.Core.Control.ScanBlockRuntime` with
    `ScanBlockRuntime(ScanBlockPlan plan, TagImage io, EventLog log)`,
    `long PeriodTicks`, `void Scan(long tick, DateTimeOffset now, double deltaSeconds)`
  - no new public API beyond Task 3's `ScanBlockCount`

- [ ] **Step 1: Write the failing test**

Create `tests/Millrace.Core.Tests/ScanBlockHostTests.cs`:

```csharp
using Millrace.Core.Logging;
using Millrace.Core.Tests.Fakes;
using Millrace.Core.Time;
using Millrace.Io;
using Xunit;

namespace Millrace.Core.Tests;

public class ScanBlockHostTests
{
    private static SimulationOptions Options => new()
    {
        Seed = 1UL,
        StartTime = new DateTimeOffset(2026, 1, 1, 6, 0, 0, TimeSpan.Zero),
        TimeStep = TimeSpan.FromMilliseconds(10),
    };

    private static readonly TimeSpan TwoSteps = TimeSpan.FromMilliseconds(20);

    /// <summary>Two thermostats: the block reads T.Enable and commands U.Enable.</summary>
    private static SimulationBuilder Plant() =>
        new SimulationBuilder(Options).Add(new Thermostat("T")).Add(new Thermostat("U"));

    private static EchoBlock Echo(string id) =>
        new EchoBlock(id, TwoSteps)
            .Reads("T.Enable")
            .MayWrite("U.Enable")
            .Publishes("Q", TagKind.Bool, "", "The echo")
            .Accepts("Cmd", TagKind.Bool, "", "A command");

    [Fact]
    public void APlantWithNoBlockScansNothing()
    {
        Simulation sim = Plant().Build();

        sim.RunFor(TimeSpan.FromMilliseconds(100));

        Assert.Equal(0, sim.ScanBlockCount);
        Assert.Empty(sim.Events.Records);
        Assert.Equal(6, sim.IO.Directory.Count);
    }

    [Fact]
    public void TheFirstScanRunsAtTickZero()
    {
        EchoBlock block = Echo("B");
        Simulation sim = Plant().AddScanBlock(block).Build();

        sim.Tick();

        Assert.Equal(new long[] { 0L }, block.ScanTicks);
    }

    [Fact]
    public void ScansHappenEveryPeriodAndNotBetween()
    {
        EchoBlock block = Echo("B");
        Simulation sim = Plant().AddScanBlock(block).Build();

        sim.RunFor(TimeSpan.FromMilliseconds(100));   // ticks 0..9

        Assert.Equal(new long[] { 0L, 2L, 4L, 6L, 8L }, block.ScanTicks);
    }

    [Fact]
    public void AnInputIsSeenAsOfTheEndOfThePreviousTick()
    {
        EchoBlock block = Echo("B");
        Simulation sim = Plant().AddScanBlock(block).Build();
        sim.WriteAt(TimeSpan.FromMilliseconds(30), "T.Enable", TagValue.Bool(true));

        sim.RunFor(TimeSpan.FromMilliseconds(50));    // ticks 0..4

        Assert.Equal(new long[] { 0L, 2L, 4L }, block.ScanTicks);
        Assert.Equal(new[] { false, false, true }, block.Seen);
    }

    [Fact]
    public void AnOutputIsPublishedAtTheEndOfTheScanTick()
    {
        EchoBlock block = Echo("B");
        Simulation sim = Plant().AddScanBlock(block).Build();
        sim.WriteAt(TimeSpan.FromMilliseconds(30), "T.Enable", TagValue.Bool(true));

        sim.RunFor(TimeSpan.FromMilliseconds(40));    // ticks 0..3
        Assert.False(sim.IO.ReadBool("B.Q"));

        sim.RunFor(TimeSpan.FromMilliseconds(10));    // tick 4: the scan that sees it
        Assert.True(sim.IO.ReadBool("B.Q"));
    }

    [Fact]
    public void AWriteQueuedByABlockLandsAtPhaseOneOfTheNextTick()
    {
        EchoBlock block = Echo("B");
        block.WriteOnce = true;
        Simulation sim = Plant().AddScanBlock(block).Build();

        sim.Tick();                                   // tick 0: the scan queues the write
        Assert.Empty(sim.Events.Records);
        Assert.False(sim.IO.ReadBool("U.Enable"));

        sim.Tick();                                   // tick 1: phase 1 applies it
        SimEventRecord record = Assert.Single(sim.Events.Records);
        Assert.Equal(1L, record.Tick);
        Assert.Equal("U.Enable", record.Source);
        Assert.Equal("WRITE", record.Code);
        Assert.Equal("Set to true.", record.Message);
        Assert.True(sim.IO.ReadBool("U.Enable"));
    }

    [Fact]
    public void AnOutputNotSetHoldsItsPreviousValue()
    {
        EchoBlock block = Echo("B");
        Simulation sim = Plant().AddScanBlock(block).Build();
        sim.WriteAt(TimeSpan.FromMilliseconds(30), "T.Enable", TagValue.Bool(true));
        sim.WriteAt(TimeSpan.FromMilliseconds(50), "T.Enable", TagValue.Bool(false));

        sim.RunFor(TimeSpan.FromMilliseconds(50));    // ticks 0..4; Q went true at tick 4
        Assert.True(sim.IO.ReadBool("B.Q"));

        block.Silent = true;
        sim.RunFor(TimeSpan.FromMilliseconds(50));    // ticks 5..9; the block publishes nothing
        Assert.Equal(new[] { false, false, true, false, false }, block.Seen);
        Assert.True(sim.IO.ReadBool("B.Q"));
    }

    [Fact]
    public void AnEventIsLoggedUnderTheBlockId()
    {
        EchoBlock block = Echo("B");
        block.RaiseOnce = true;
        Simulation sim = Plant().AddScanBlock(block).Build();

        sim.Tick();

        SimEventRecord record = Assert.Single(sim.Events.Records);
        Assert.Equal(0L, record.Tick);
        Assert.Equal("B", record.Source);
        Assert.Equal("ECHO", record.Code);
        Assert.Equal("The stub raised an event.", record.Message);
        Assert.Equal(new DateTimeOffset(2026, 1, 1, 6, 0, 0, TimeSpan.Zero), record.SimTime);
    }

    [Fact]
    public void ACommandIsWritableAndTheBlockSeesItOneScanLater()
    {
        EchoBlock block = Echo("B");
        Simulation sim = Plant().AddScanBlock(block).Build();
        sim.WriteAt(TimeSpan.FromMilliseconds(30), "B.Cmd", TagValue.Bool(true));

        sim.RunFor(TimeSpan.FromMilliseconds(50));    // ticks 0..4

        Assert.Equal(new[] { false, false, true }, block.SeenCommand);
        SimEventRecord record = Assert.Single(sim.Events.Records);
        Assert.Equal(3L, record.Tick);
        Assert.Equal("B.Cmd", record.Source);
    }

    [Fact]
    public void AnOwnedOutputRefusesAWrite()
    {
        Simulation sim = Plant().AddScanBlock(Echo("B")).Build();

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => sim.WriteAt(TimeSpan.FromMilliseconds(10), "B.Q", TagValue.Bool(true)));
        Assert.Contains("read-only", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ElapsedIsZeroOnTheFirstScanAndThePeriodAfterwards()
    {
        EchoBlock block = Echo("B");
        Simulation sim = Plant().AddScanBlock(block).Build();

        sim.RunFor(TimeSpan.FromMilliseconds(60));    // ticks 0..5; scans at 0, 2, 4

        Assert.Equal(3, block.ElapsedSeconds.Count);
        Assert.Equal(0.0, block.ElapsedSeconds[0]);
        Assert.Equal(0.02, block.ElapsedSeconds[1], 12);
        Assert.Equal(0.02, block.ElapsedSeconds[2], 12);
    }

    [Fact]
    public void BlocksScanInAddOrderAmongEquals()
    {
        var first = new EchoBlock("A", TimeSpan.FromMilliseconds(10)) { RaiseEveryScan = true };
        var second = new EchoBlock("B", TimeSpan.FromMilliseconds(10)) { RaiseEveryScan = true };
        Simulation sim = Plant().AddScanBlock(first).AddScanBlock(second).Build();

        sim.Tick();

        Assert.Equal(new[] { "A", "B" }, sim.Events.Records.Select(r => r.Source).ToArray());
    }

    [Fact]
    public void ABlockReadsAnotherBlocksOutputOneScanLate()
    {
        EchoBlock producer = Echo("B");
        var consumer = new EchoBlock("C", TwoSteps).Reads("B.Q").Publishes("Q");
        Simulation sim = Plant().AddScanBlock(producer).AddScanBlock(consumer).Build();
        sim.WriteAt(TimeSpan.FromMilliseconds(30), "T.Enable", TagValue.Bool(true));

        sim.RunFor(TimeSpan.FromMilliseconds(70));    // ticks 0..6; scans at 0, 2, 4, 6

        Assert.Equal(new[] { false, false, true, true }, producer.Seen);
        Assert.Equal(new[] { false, false, false, true }, consumer.Seen);
    }

    [Fact]
    public void TwoRunsOfThePlantWithBlocksAreByteIdentical()
    {
        static Simulation Build()
        {
            Simulation sim = Plant()
                .AddScanBlock(new EchoBlock("A", TimeSpan.FromMilliseconds(10)) { RaiseEveryScan = true })
                .AddScanBlock(new EchoBlock("B", TwoSteps).Reads("T.Enable").MayWrite("U.Enable").Publishes("Q"))
                .Build();
            sim.WriteAt(TimeSpan.FromMilliseconds(30), "T.Enable", TagValue.Bool(true));
            return sim;
        }

        Simulation first = Build();
        Simulation second = Build();
        first.RunFor(TimeSpan.FromSeconds(1));
        second.RunFor(TimeSpan.FromSeconds(1));

        Assert.Equal(first.Events.ToText(), second.Events.ToText());
        Assert.Equal(first.IO.Snapshot().ToArray(), second.IO.Snapshot().ToArray());
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/Millrace.Core.Tests --nologo --filter FullyQualifiedName~ScanBlockHostTests`
Expected: FAIL — `TheFirstScanRunsAtTickZero` reports an empty `ScanTicks`:
nothing schedules a scan yet.

- [ ] **Step 3: Write the runtime**

Create `src/Millrace.Core/Control/ScanBlockRuntime.cs`:

```csharp
using Millrace.Core.Io;
using Millrace.Core.Logging;
using Millrace.Io;

namespace Millrace.Core.Control;

/// <summary>
/// One block, running. Resolves its pins to image indices once, then does the
/// same five things on every scan: read the published image, call
/// <see cref="IScanBlock.Scan"/>, store the outputs into the block's own ports,
/// queue the writes, and log the events under the block's id.
/// </summary>
/// <remarks>
/// Every buffer is allocated here, once. A scan that raises no event allocates
/// nothing (R68).
/// </remarks>
internal sealed class ScanBlockRuntime
{
    private readonly ScanBlockPlan _plan;
    private readonly TagImage _io;
    private readonly EventLog _log;
    private readonly int[] _inputIndices;
    private readonly int[] _writeIndices;
    private readonly int[] _commandIndices;
    private readonly TagValue[] _inputValues;
    private readonly TagValue[] _commandValues;
    private ScanOutputs _outputs;
    private long _lastScanTick = -1L;

    internal ScanBlockRuntime(ScanBlockPlan plan, TagImage io, EventLog log)
    {
        _plan = plan;
        _io = io;
        _log = log;

        _inputIndices = Indices(io, plan.Block.Inputs);
        _writeIndices = Indices(io, plan.Block.Writes);
        _commandIndices = new int[plan.Commands.Length];
        for (int i = 0; i < _commandIndices.Length; i++)
        {
            _commandIndices[i] = io.Directory.Find(plan.Commands[i].Name).Index;
        }

        _inputValues = new TagValue[_inputIndices.Length];
        _commandValues = new TagValue[_commandIndices.Length];
        _outputs = new ScanOutputs(plan.Outputs.Length, _writeIndices.Length);
    }

    /// <summary>The scan period in ticks; at least one.</summary>
    internal long PeriodTicks => _plan.PeriodTicks;

    /// <summary>One scan, during phase 1 of <paramref name="tick"/>.</summary>
    internal void Scan(long tick, DateTimeOffset now, double deltaSeconds)
    {
        for (int i = 0; i < _inputIndices.Length; i++)
        {
            _inputValues[i] = _io.Read(_inputIndices[i]);
        }

        for (int i = 0; i < _commandIndices.Length; i++)
        {
            _commandValues[i] = _io.Read(_commandIndices[i]);
        }

        double elapsed = _lastScanTick < 0L ? 0.0 : (tick - _lastScanTick) * deltaSeconds;
        var inputs = new ScanInputs(_inputValues, _commandValues, tick, now, deltaSeconds, elapsed);

        _outputs.Reset();
        _plan.Block.Scan(in inputs, ref _outputs);
        _lastScanTick = tick;

        for (int i = 0; i < _plan.Outputs.Length; i++)
        {
            if (_outputs.TryOutput(i, out TagValue value))
            {
                _plan.Outputs[i].Store(value);
            }
        }

        for (int i = 0; i < _writeIndices.Length; i++)
        {
            if (_outputs.TryWrite(i, out TagValue value))
            {
                _io.Write(_writeIndices[i], value);
            }
        }

        IReadOnlyList<BlockEvent> events = _outputs.Events;
        for (int i = 0; i < events.Count; i++)
        {
            _log.Record(tick, now, _plan.Block.Id, events[i].Code, events[i].Message);
        }
    }

    private static int[] Indices(TagImage io, IReadOnlyList<TagRef> pins)
    {
        var indices = new int[pins.Count];
        for (int i = 0; i < indices.Length; i++)
        {
            indices[i] = io.Directory.Find(pins[i].Name).Index;
        }

        return indices;
    }
}
```

- [ ] **Step 4: Schedule the scans**

In `src/Millrace.Core/Simulation.cs`, add the field beside `_blockPlans`:

```csharp
    private readonly ScanBlockRuntime[] _blocks;
```

and, at the end of the constructor body (after the `_faultTargets` loop), add:

```csharp
        // R70: scheduled here, so every block holds a lower sequence number than
        // anything a caller schedules later and scans first at tick 0.
        _blocks = new ScanBlockRuntime[blocks.Length];
        for (int i = 0; i < blocks.Length; i++)
        {
            _blocks[i] = new ScanBlockRuntime(blocks[i], io, Events);
            _queue.Schedule(0L, new ScanEvent(this, _blocks[i]));
        }
```

Add the event class beside `WriteEvent`, inside `Simulation`:

```csharp
    /// <summary>One block's scan, which reschedules itself one period later.</summary>
    private sealed class ScanEvent : ISimEvent
    {
        private readonly Simulation _simulation;
        private readonly ScanBlockRuntime _runtime;

        internal ScanEvent(Simulation simulation, ScanBlockRuntime runtime)
        {
            _simulation = simulation;
            _runtime = runtime;
        }

        public void Apply()
        {
            SimulationClock clock = _simulation.Clock;
            _runtime.Scan(clock.TickCount, clock.Now, clock.DeltaSeconds);
            _simulation._queue.Schedule(clock.TickCount + _runtime.PeriodTicks, this);
        }
    }
```

The period is at least one tick (MR013 and `Math.Max(1L, …)` in Task 3), so the
rescheduled event is never due on the tick that is draining and the drain always
terminates.

- [ ] **Step 5: Run the test to verify it passes**

Run: `dotnet test tests/Millrace.Core.Tests --nologo --filter FullyQualifiedName~ScanBlockHostTests`
Expected: PASS, 14 tests.

- [ ] **Step 6: Run everything**

Run: `dotnet build Millrace.sln -c Release --nologo` — expect `0 Warning(s)`, `0 Error(s)`.
Run: `dotnet test Millrace.sln --nologo`
Expected: PASS, 987 + 14 = **1001** tests. The four 5b golden logs must still
match — they are in `Millrace.Scenarios.Tests`, which runs plants with no blocks.
Report the number the runner prints.

- [ ] **Step 7: Commit**

```bash
git add src/Millrace.Core/Control/ScanBlockRuntime.cs src/Millrace.Core/Simulation.cs tests/Millrace.Core.Tests/ScanBlockHostTests.cs
```

```bash
git commit -m "$(cat <<'MSG'
feat(core): scan control blocks through the event queue

One self-rescheduling ScanEvent per block, first due at tick 0. A scan at
tick N reads the image published at the end of tick N-1, publishes its
outputs at the end of tick N, and its writes land at phase 1 of N+1.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
MSG
)"
```

---

### Task 5: `Millrace.Control` and the `Timer`

The new project, its test project with the pure scan harness every later task
uses, and the first block: IEC 61131-3 TON, TOF and TP.

**Files:**
- Create: `src/Millrace.Control/Millrace.Control.csproj`
- Create: `src/Millrace.Control/TimerMode.cs`
- Create: `src/Millrace.Control/Timer.cs`
- Create: `tests/Millrace.Control.Tests/Millrace.Control.Tests.csproj`
- Create: `tests/Millrace.Control.Tests/Scan.cs`
- Test: `tests/Millrace.Control.Tests/TimerTests.cs`
- Modify: `Millrace.sln`

**Interfaces:**
- Consumes: `IScanBlock`, `TagRef`, `TagSpec`, `ScanInputs`, `ScanOutputs`,
  `BlockEvent`, `TagValue`, `TagKind` (Task 2, `Millrace.Io.Abstractions`).
- Produces:
  - `public enum Millrace.Control.TimerMode { OnDelay, OffDelay, Pulse }`
  - `public sealed class Millrace.Control.Timer : IScanBlock` with
    `Timer(string id, TimerMode mode, string input, TimeSpan preset, TimeSpan scanPeriod)`,
    `TimerMode Mode`, `TimeSpan Preset`; outputs `Q` (Bool) then `ET` (Double,
    unit `s`); no writes, no commands
  - `internal sealed class Millrace.Control.Tests.Scan` with
    `Scan(IScanBlock block, double deltaSeconds = 0.01)`,
    `Scan Set(string tag, bool|double|long value)`,
    `Scan Command(string pin, bool value)`, `Scan Once()`, `Scan Times(int count)`,
    `bool Bool(string pin)`, `double Double(string pin)`, `long Int64(string pin)`,
    `bool TryWrite(string tag, out TagValue value)`,
    `List<BlockEvent> Events`, `IReadOnlyList<BlockEvent> LastEvents`,
    `IReadOnlyList<KeyValuePair<string, TagValue>> LastWrites`, `string Codes()`,
    `double DeltaSeconds`, `static DateTimeOffset Start`

- [ ] **Step 1: Create the two projects**

Create `src/Millrace.Control/Millrace.Control.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\Millrace.Io.Abstractions\Millrace.Io.Abstractions.csproj" />
  </ItemGroup>

</Project>
```

Create `tests/Millrace.Control.Tests/Millrace.Control.Tests.csproj`:

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
    <ProjectReference Include="..\..\src\Millrace.Control\Millrace.Control.csproj" />
    <ProjectReference Include="..\..\src\Millrace.Core\Millrace.Core.csproj" />
    <ProjectReference Include="..\..\src\Millrace.Components\Millrace.Components.csproj" />
    <ProjectReference Include="..\..\src\Millrace.Configuration\Millrace.Configuration.csproj" />
  </ItemGroup>

</Project>
```

`Millrace.Scenarios` is deliberately absent (R75). Two more things arrive where they
are first used: `Millrace.Realtime` in Task 9, and `tests/Shared/Golden.cs` with the
linked plants in Task 10.

Add both to the solution, one command per call:

```bash
dotnet sln Millrace.sln add src/Millrace.Control/Millrace.Control.csproj --solution-folder src
```

```bash
dotnet sln Millrace.sln add tests/Millrace.Control.Tests/Millrace.Control.Tests.csproj --solution-folder tests
```

- [ ] **Step 2: Write the scan harness**

Create `tests/Millrace.Control.Tests/Scan.cs`:

```csharp
using Millrace.Io;

namespace Millrace.Control.Tests;

/// <summary>
/// Scans a block the way the host does, with no <c>Simulation</c>: the values
/// set here are what the next scan reads, an output not set in a scan holds its
/// previous value, and <c>Elapsed</c> is zero on the first scan and the block's
/// period on every one after.
/// </summary>
internal sealed class Scan
{
    private readonly IScanBlock _block;
    private readonly TagValue[] _inputs;
    private readonly TagValue[] _commands;
    private readonly TagValue[] _published;
    private ScanOutputs _outputs;
    private TimeSpan _sinceStart;
    private long _tick;
    private bool _scanned;

    public Scan(IScanBlock block, double deltaSeconds = 0.01)
    {
        _block = block;
        DeltaSeconds = deltaSeconds;
        _inputs = new TagValue[block.Inputs.Count];
        _commands = new TagValue[block.Commands.Count];
        _published = new TagValue[block.Outputs.Count];

        for (int i = 0; i < _inputs.Length; i++)
        {
            _inputs[i] = Zero(block.Inputs[i].Kind);
        }

        for (int i = 0; i < _commands.Length; i++)
        {
            _commands[i] = Zero(block.Commands[i].Kind);
        }

        for (int i = 0; i < _published.Length; i++)
        {
            _published[i] = Zero(block.Outputs[i].Kind);
        }

        _outputs = new ScanOutputs(block.Outputs.Count, block.Writes.Count);
    }

    /// <summary>The simulation time step the scans pretend to run at.</summary>
    public double DeltaSeconds { get; }

    /// <summary>The simulation time of the first scan.</summary>
    public static DateTimeOffset Start { get; } = new(2026, 1, 1, 6, 0, 0, TimeSpan.Zero);

    /// <summary>Every event raised since this harness was created, in order.</summary>
    public List<BlockEvent> Events { get; } = [];

    /// <summary>The events the most recent scan raised.</summary>
    public IReadOnlyList<BlockEvent> LastEvents { get; private set; } = [];

    /// <summary>The writes the most recent scan commanded, in the block's <c>Writes</c> order.</summary>
    public IReadOnlyList<KeyValuePair<string, TagValue>> LastWrites { get; private set; } = [];

    public Scan Set(string tag, bool value) => SetInput(tag, TagValue.Bool(value));

    public Scan Set(string tag, double value) => SetInput(tag, TagValue.Double(value));

    public Scan Set(string tag, long value) => SetInput(tag, TagValue.Int64(value));

    /// <summary>Sets a command pin, by its relative name.</summary>
    public Scan Command(string pin, bool value)
    {
        _commands[Index(_block.Commands, pin, "command")] = TagValue.Bool(value);
        return this;
    }

    /// <summary>Runs one scan.</summary>
    public Scan Once()
    {
        if (_scanned)
        {
            _sinceStart += _block.ScanPeriod;
            _tick += PeriodTicks;
        }

        double elapsed = _scanned ? _block.ScanPeriod.TotalSeconds : 0.0;
        var inputs = new ScanInputs(_inputs, _commands, _tick, Start + _sinceStart, DeltaSeconds, elapsed);

        _outputs.Reset();
        _block.Scan(in inputs, ref _outputs);
        _scanned = true;

        for (int i = 0; i < _published.Length; i++)
        {
            if (_outputs.TryOutput(i, out TagValue value))
            {
                _published[i] = value;
            }
        }

        var writes = new List<KeyValuePair<string, TagValue>>();
        for (int i = 0; i < _block.Writes.Count; i++)
        {
            if (_outputs.TryWrite(i, out TagValue value))
            {
                writes.Add(new KeyValuePair<string, TagValue>(_block.Writes[i].Name, value));
            }
        }

        LastWrites = writes;
        LastEvents = _outputs.Events.ToArray();
        Events.AddRange(LastEvents);
        return this;
    }

    /// <summary>Runs <paramref name="count"/> scans with the inputs as they stand.</summary>
    public Scan Times(int count)
    {
        for (int i = 0; i < count; i++)
        {
            Once();
        }

        return this;
    }

    /// <summary>The value published on a Bool output.</summary>
    public bool Bool(string pin) => Published(pin).AsBool;

    /// <summary>The value published on a Double output.</summary>
    public double Double(string pin) => Published(pin).AsDouble;

    /// <summary>The value published on an Int64 output.</summary>
    public long Int64(string pin) => Published(pin).AsInt64;

    /// <summary>What the most recent scan commanded on <paramref name="tag"/>, if anything.</summary>
    public bool TryWrite(string tag, out TagValue value)
    {
        foreach (KeyValuePair<string, TagValue> write in LastWrites)
        {
            if (string.Equals(write.Key, tag, StringComparison.Ordinal))
            {
                value = write.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    /// <summary>Every event code so far, comma-separated: a readable assertion for a whole sequence.</summary>
    public string Codes() => string.Join(",", Events.Select(e => e.Code));

    private long PeriodTicks => (long)Math.Round(_block.ScanPeriod.TotalSeconds / DeltaSeconds);

    private Scan SetInput(string tag, TagValue value)
    {
        _inputs[Index(_block.Inputs, tag, "input")] = value;
        return this;
    }

    private TagValue Published(string pin) => _published[Index(_block.Outputs, pin, "output")];

    private static TagValue Zero(TagKind kind) => kind switch
    {
        TagKind.Bool => TagValue.Bool(false),
        TagKind.Double => TagValue.Double(0.0),
        _ => TagValue.Int64(0L),
    };

    private static int Index(IReadOnlyList<TagRef> pins, string name, string noun)
    {
        for (int i = 0; i < pins.Count; i++)
        {
            if (string.Equals(pins[i].Name, name, StringComparison.Ordinal))
            {
                return i;
            }
        }

        throw new ArgumentException(
            $"No {noun} '{name}'. The block has: {string.Join(", ", pins.Select(p => p.Name))}.", nameof(name));
    }

    private static int Index(IReadOnlyList<TagSpec> pins, string name, string noun)
    {
        for (int i = 0; i < pins.Count; i++)
        {
            if (string.Equals(pins[i].Name, name, StringComparison.Ordinal))
            {
                return i;
            }
        }

        throw new ArgumentException(
            $"No {noun} '{name}'. The block has: {string.Join(", ", pins.Select(p => p.Name))}.", nameof(name));
    }
}
```

- [ ] **Step 3: Write the failing test**

Create `tests/Millrace.Control.Tests/TimerTests.cs`:

```csharp
using Millrace.Io;

namespace Millrace.Control.Tests;

public class TimerTests
{
    private static readonly TimeSpan Period = TimeSpan.FromMilliseconds(100);

    private static Timer Make(TimerMode mode, double presetSeconds = 0.3) =>
        new("TMR01", mode, "T.Enable", TimeSpan.FromSeconds(presetSeconds), Period);

    [Fact]
    public void TheConstructorRejectsABlankId()
    {
        Assert.Throws<ArgumentException>(
            () => new Timer(" ", TimerMode.OnDelay, "T.Enable", TimeSpan.FromSeconds(1), Period));
    }

    [Fact]
    public void TheConstructorRejectsABlankInput()
    {
        Assert.Throws<ArgumentException>(
            () => new Timer("TMR01", TimerMode.OnDelay, " ", TimeSpan.FromSeconds(1), Period));
    }

    [Fact]
    public void TheConstructorRejectsANegativePreset()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new Timer("TMR01", TimerMode.OnDelay, "T.Enable", TimeSpan.FromSeconds(-1), Period));
    }

    [Fact]
    public void TheConstructorRejectsANonPositiveScanPeriod()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new Timer("TMR01", TimerMode.OnDelay, "T.Enable", TimeSpan.FromSeconds(1), TimeSpan.Zero));
    }

    [Fact]
    public void ThePinsAreDeclaredInOrder()
    {
        Timer timer = Make(TimerMode.OnDelay);

        Assert.Equal("TMR01", timer.Id);
        Assert.Equal(Period, timer.ScanPeriod);
        Assert.Equal(TimerMode.OnDelay, timer.Mode);
        Assert.Equal(TimeSpan.FromSeconds(0.3), timer.Preset);
        Assert.Equal("T.Enable", Assert.Single(timer.Inputs).Name);
        Assert.Equal(TagKind.Bool, timer.Inputs[0].Kind);
        Assert.Empty(timer.Writes);
        Assert.Empty(timer.Commands);
        Assert.Equal(2, timer.Outputs.Count);
        Assert.Equal("Q", timer.Outputs[0].Name);
        Assert.Equal(TagKind.Bool, timer.Outputs[0].Kind);
        Assert.Equal("ET", timer.Outputs[1].Name);
        Assert.Equal(TagKind.Double, timer.Outputs[1].Kind);
        Assert.Equal("s", timer.Outputs[1].Unit);
    }

    [Fact]
    public void OnDelayHoldsQLowUntilThePresetHasElapsed()
    {
        var scan = new Scan(Make(TimerMode.OnDelay));

        scan.Set("T.Enable", true).Once();
        Assert.False(scan.Bool("Q"));
        Assert.Equal(0.0, scan.Double("ET"));

        scan.Times(2);                                 // 0.1 s, then 0.2 s
        Assert.False(scan.Bool("Q"));
        Assert.Equal(0.2, scan.Double("ET"), 12);

        scan.Once();                                   // 0.3 s
        Assert.True(scan.Bool("Q"));
        Assert.Equal(0.3, scan.Double("ET"), 12);
    }

    [Fact]
    public void OnDelayResetsWhenTheInputFalls()
    {
        // Only the harness's very first scan carries Elapsed 0; every scan after
        // it carries 0.1, so after the reset the count is 0.1, 0.2, 0.3 and the
        // third re-enabled scan — not the fourth — reaches the preset again.
        var scan = new Scan(Make(TimerMode.OnDelay));
        scan.Set("T.Enable", true).Times(4);           // 0, 0.1, 0.2, 0.3
        Assert.True(scan.Bool("Q"));

        scan.Set("T.Enable", false).Once();
        Assert.False(scan.Bool("Q"));
        Assert.Equal(0.0, scan.Double("ET"));

        scan.Set("T.Enable", true).Times(2);           // 0.1, 0.2
        Assert.False(scan.Bool("Q"));
        Assert.Equal(0.2, scan.Double("ET"), 12);

        scan.Once();                                   // 0.3
        Assert.True(scan.Bool("Q"));
    }

    [Fact]
    public void OnDelayWithAZeroPresetIsImmediate()
    {
        var scan = new Scan(Make(TimerMode.OnDelay, presetSeconds: 0.0));

        scan.Set("T.Enable", true).Once();

        Assert.True(scan.Bool("Q"));
        Assert.Equal(0.0, scan.Double("ET"));
    }

    [Fact]
    public void OffDelayHoldsQHighForThePresetAfterTheInputFalls()
    {
        var scan = new Scan(Make(TimerMode.OffDelay));
        scan.Set("T.Enable", true).Times(2);
        Assert.True(scan.Bool("Q"));

        scan.Set("T.Enable", false).Times(2);          // 0.1 s, 0.2 s
        Assert.True(scan.Bool("Q"));

        scan.Once();                                   // 0.3 s
        Assert.False(scan.Bool("Q"));
        Assert.Equal(0.3, scan.Double("ET"), 12);
    }

    [Fact]
    public void OffDelayRestartsWhenTheInputReturns()
    {
        var scan = new Scan(Make(TimerMode.OffDelay));
        scan.Set("T.Enable", true).Once();
        scan.Set("T.Enable", false).Times(2);
        Assert.True(scan.Bool("Q"));

        scan.Set("T.Enable", true).Once();
        Assert.True(scan.Bool("Q"));
        Assert.Equal(0.0, scan.Double("ET"));

        scan.Set("T.Enable", false).Times(2);
        Assert.True(scan.Bool("Q"));
        scan.Once();
        Assert.False(scan.Bool("Q"));
    }

    [Fact]
    public void PulseGivesAPresetLongPulseOnARisingEdge()
    {
        var scan = new Scan(Make(TimerMode.Pulse));

        scan.Set("T.Enable", true).Once();
        Assert.True(scan.Bool("Q"));
        Assert.Equal(0.0, scan.Double("ET"));

        scan.Times(2);                                 // 0.1 s, 0.2 s
        Assert.True(scan.Bool("Q"));

        scan.Once();                                   // 0.3 s
        Assert.False(scan.Bool("Q"));
        Assert.Equal(0.3, scan.Double("ET"), 12);
    }

    [Fact]
    public void PulseIsNotRetriggeredWhileItRuns()
    {
        var scan = new Scan(Make(TimerMode.Pulse));
        scan.Set("T.Enable", true).Once();

        scan.Set("T.Enable", false).Once();
        scan.Set("T.Enable", true).Once();             // a second rising edge, mid-pulse
        Assert.True(scan.Bool("Q"));
        Assert.Equal(0.2, scan.Double("ET"), 12);

        scan.Once();
        Assert.False(scan.Bool("Q"));                  // the pulse still ends at 0.3 s
    }

    [Fact]
    public void ElapsedTimeIsQuantisedToTheScanPeriod()
    {
        var scan = new Scan(Make(TimerMode.OnDelay, presetSeconds: 1.0));

        scan.Set("T.Enable", true).Times(3);

        Assert.Equal(0.2, scan.Double("ET"), 12);      // never 0.25 or 0.17: two periods
    }

    [Fact]
    public void ElapsedTimeStopsAtThePreset()
    {
        var scan = new Scan(Make(TimerMode.OnDelay));

        scan.Set("T.Enable", true).Times(10);

        Assert.True(scan.Bool("Q"));
        Assert.Equal(0.3, scan.Double("ET"), 12);
    }

    [Fact]
    public void ATimerRaisesNoEvents()
    {
        var scan = new Scan(Make(TimerMode.Pulse));

        scan.Set("T.Enable", true).Times(5);
        scan.Set("T.Enable", false).Times(5);

        Assert.Empty(scan.Events);
    }
}
```

- [ ] **Step 4: Run the test to verify it fails**

Run: `dotnet test tests/Millrace.Control.Tests --nologo`
Expected: FAIL — the project does not build: `Timer` and `TimerMode` do not
exist.

- [ ] **Step 5: Write the timer**

Create `src/Millrace.Control/TimerMode.cs`:

```csharp
namespace Millrace.Control;

/// <summary>The three IEC 61131-3 timers.</summary>
public enum TimerMode
{
    /// <summary>TON. <c>Q</c> goes high once the input has held high for the preset.</summary>
    OnDelay,

    /// <summary>TOF. <c>Q</c> stays high for the preset after the input falls.</summary>
    OffDelay,

    /// <summary>TP. A preset-long pulse on a rising edge, not retriggerable while it runs.</summary>
    Pulse,
}
```

Create `src/Millrace.Control/Timer.cs`:

```csharp
using Millrace.Io;

namespace Millrace.Control;

/// <summary>
/// An IEC 61131-3 timer over one Bool tag. <c>ET</c> accumulates the scan
/// period, so it is quantised to it: a 100 ms timer on a 10 ms plant measures
/// in tenths of a second, exactly as a PLC does.
/// </summary>
public sealed class Timer : IScanBlock
{
    private readonly double _preset;
    private bool _q;
    private bool _previous;
    private double _elapsed;

    /// <summary>Creates a timer.</summary>
    /// <param name="id">The block id; prefixes <c>Q</c> and <c>ET</c>.</param>
    /// <param name="mode">Which of the three timers this is.</param>
    /// <param name="input">The full name of the Bool tag to time.</param>
    /// <param name="preset">The delay or pulse length. Zero is allowed and acts immediately.</param>
    /// <param name="scanPeriod">How often the block scans.</param>
    public Timer(string id, TimerMode mode, string input, TimeSpan preset, TimeSpan scanPeriod)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(input);

        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(
                nameof(mode), mode, "A timer is OnDelay, OffDelay or Pulse.");
        }

        if (preset < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(preset), preset, "The preset must not be negative.");
        }

        if (scanPeriod <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(scanPeriod), scanPeriod, "The scan period must be positive.");
        }

        Id = id;
        Mode = mode;
        Preset = preset;
        ScanPeriod = scanPeriod;
        _preset = preset.TotalSeconds;
        Inputs = [new TagRef(input, TagKind.Bool)];
    }

    /// <inheritdoc/>
    public string Id { get; }

    /// <summary>Which of the three timers this is.</summary>
    public TimerMode Mode { get; }

    /// <summary>The delay or pulse length.</summary>
    public TimeSpan Preset { get; }

    /// <inheritdoc/>
    public TimeSpan ScanPeriod { get; }

    /// <inheritdoc/>
    public IReadOnlyList<TagRef> Inputs { get; }

    /// <inheritdoc/>
    public IReadOnlyList<TagRef> Writes { get; } = [];

    /// <inheritdoc/>
    public IReadOnlyList<TagSpec> Outputs { get; } =
    [
        new TagSpec("Q", TagKind.Bool, "", "Timer output"),
        new TagSpec("ET", TagKind.Double, "s", "Elapsed time, quantised to the scan period"),
    ];

    /// <inheritdoc/>
    public IReadOnlyList<TagSpec> Commands { get; } = [];

    /// <inheritdoc/>
    public void Scan(in ScanInputs inputs, ref ScanOutputs outputs)
    {
        bool input = inputs.Input(0).AsBool;

        switch (Mode)
        {
            case TimerMode.OnDelay:
                if (input)
                {
                    if (_elapsed < _preset)
                    {
                        _elapsed = Math.Min(_preset, _elapsed + inputs.Elapsed);
                    }

                    _q = _elapsed >= _preset;
                }
                else
                {
                    _elapsed = 0.0;
                    _q = false;
                }

                break;

            case TimerMode.OffDelay:
                if (input)
                {
                    _elapsed = 0.0;
                    _q = true;
                }
                else if (_q)
                {
                    _elapsed = Math.Min(_preset, _elapsed + inputs.Elapsed);
                    _q = _elapsed < _preset;
                }

                break;

            default:
                if (input && !_previous && !_q)
                {
                    _q = true;
                    _elapsed = 0.0;
                }
                else if (_q)
                {
                    _elapsed = Math.Min(_preset, _elapsed + inputs.Elapsed);
                    _q = _elapsed < _preset;
                }

                break;
        }

        _previous = input;
        outputs.Set(0, TagValue.Bool(_q));
        outputs.Set(1, TagValue.Double(_elapsed));
    }
}
```

- [ ] **Step 6: Run the test to verify it passes**

Run: `dotnet test tests/Millrace.Control.Tests --nologo`
Expected: PASS, 15 tests.

- [ ] **Step 7: Run everything**

Run: `dotnet build Millrace.sln -c Release --nologo` — expect `0 Warning(s)`, `0 Error(s)`.
Run: `grep -rn "PackageReference" src/` — expect no output.
Run: `dotnet test Millrace.sln --nologo`
Expected: PASS, 1001 + 15 = **1016** tests. Report the number the runner prints.

- [ ] **Step 8: Commit**

```bash
git add src/Millrace.Control/Millrace.Control.csproj src/Millrace.Control/TimerMode.cs src/Millrace.Control/Timer.cs Millrace.sln
```

```bash
git add tests/Millrace.Control.Tests/Millrace.Control.Tests.csproj tests/Millrace.Control.Tests/Scan.cs tests/Millrace.Control.Tests/TimerTests.cs
```

```bash
git commit -m "$(cat <<'MSG'
feat(control): add Millrace.Control and the IEC 61131-3 timer

The new project references Millrace.Io.Abstractions and nothing else. Timer
covers TON, TOF and TP; ET accumulates the scan period, so it is
quantised to it exactly as a PLC's is.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
MSG
)"
```

---

### Task 6: `Permissive` and `Interlock`

Two blocks over the same shape — a list of Bool conditions with a normal
polarity — and the difference that matters on a plant: a permissive is
re-evaluated every scan and never latches, an interlock latches and clears only
on a reset while everything is healthy.

**Files:**
- Create: `src/Millrace.Control/Condition.cs`
- Create: `src/Millrace.Control/BlockWrite.cs`
- Create: `src/Millrace.Control/Permissive.cs`
- Create: `src/Millrace.Control/Interlock.cs`
- Test: `tests/Millrace.Control.Tests/PermissiveTests.cs`
- Test: `tests/Millrace.Control.Tests/InterlockTests.cs`

**Interfaces:**
- Consumes: the contract of Task 2; `Scan` of Task 5.
- Produces:
  - `public sealed record Millrace.Control.Condition(string Tag, bool Normal)`
  - `public sealed record Millrace.Control.BlockWrite(string Tag, TagValue Value)`
  - `public sealed class Millrace.Control.Permissive : IScanBlock` with
    `Permissive(string id, IReadOnlyList<Condition> conditions, TimeSpan scanPeriod)`;
    outputs `Ok` (Bool) then `FirstOut` (Int64); no writes, no commands;
    events `PERMISSIVE_LOST`, `PERMISSIVE_OK`
  - `public sealed class Millrace.Control.Interlock : IScanBlock` with
    `Interlock(string id, IReadOnlyList<Condition> conditions, IReadOnlyList<BlockWrite> tripWrites, TimeSpan scanPeriod)`;
    outputs `Ok`, `Tripped` (Bool) then `FirstOut` (Int64); command `Reset`
    (Bool); events `INTERLOCK_TRIP`, `INTERLOCK_RESET`

- [ ] **Step 1: Write the failing tests**

Create `tests/Millrace.Control.Tests/PermissiveTests.cs`:

```csharp
using Millrace.Io;

namespace Millrace.Control.Tests;

public class PermissiveTests
{
    private static readonly TimeSpan Period = TimeSpan.FromMilliseconds(100);

    /// <summary>Safety must be healthy (normal true) and the pile must not be full (normal false).</summary>
    private static Permissive Make() => new(
        "PERM01",
        [new Condition("CV001.SafetyOk", true), new Condition("Pile.Full", false)],
        Period);

    private static Scan Healthy()
    {
        var scan = new Scan(Make());
        scan.Set("CV001.SafetyOk", true).Set("Pile.Full", false);
        return scan;
    }

    [Fact]
    public void TheConstructorRejectsNoConditions()
    {
        Assert.Throws<ArgumentException>(() => new Permissive("PERM01", [], Period));
    }

    [Fact]
    public void TheConstructorRejectsABlankConditionTag()
    {
        Assert.Throws<ArgumentException>(
            () => new Permissive("PERM01", [new Condition(" ", true)], Period));
    }

    [Fact]
    public void ThePinsAreDeclaredInConditionOrder()
    {
        Permissive permissive = Make();

        Assert.Equal("PERM01", permissive.Id);
        Assert.Equal(2, permissive.Inputs.Count);
        Assert.Equal("CV001.SafetyOk", permissive.Inputs[0].Name);
        Assert.Equal("Pile.Full", permissive.Inputs[1].Name);
        Assert.All(permissive.Inputs, pin => Assert.Equal(TagKind.Bool, pin.Kind));
        Assert.Empty(permissive.Writes);
        Assert.Empty(permissive.Commands);
        Assert.Equal(2, permissive.Outputs.Count);
        Assert.Equal("Ok", permissive.Outputs[0].Name);
        Assert.Equal("FirstOut", permissive.Outputs[1].Name);
        Assert.Equal(TagKind.Int64, permissive.Outputs[1].Kind);
    }

    [Fact]
    public void OkIsTrueWhenEveryConditionIsNormal()
    {
        Scan scan = Healthy().Once();

        Assert.True(scan.Bool("Ok"));
        Assert.Equal(-1L, scan.Int64("FirstOut"));
        Assert.Empty(scan.Events);
    }

    [Fact]
    public void OkFallsAndFirstOutNamesTheFirstAbnormalCondition()
    {
        Scan scan = Healthy().Once();

        scan.Set("Pile.Full", true).Once();

        Assert.False(scan.Bool("Ok"));
        Assert.Equal(1L, scan.Int64("FirstOut"));
        BlockEvent raised = Assert.Single(scan.LastEvents);
        Assert.Equal("PERMISSIVE_LOST", raised.Code);
        Assert.Equal("Pile.Full dropped.", raised.Message);
    }

    [Fact]
    public void FirstOutKeepsTheFirstOfTwoSimultaneousLosses()
    {
        Scan scan = Healthy().Once();

        scan.Set("CV001.SafetyOk", false).Set("Pile.Full", true).Once();

        Assert.Equal(0L, scan.Int64("FirstOut"));
        Assert.Equal("CV001.SafetyOk dropped.", Assert.Single(scan.LastEvents).Message);
    }

    [Fact]
    public void OkReturnsAndFirstOutClearsWithoutAReset()
    {
        Scan scan = Healthy().Once();
        scan.Set("Pile.Full", true).Once();

        scan.Set("Pile.Full", false).Once();

        Assert.True(scan.Bool("Ok"));
        Assert.Equal(-1L, scan.Int64("FirstOut"));
        BlockEvent raised = Assert.Single(scan.LastEvents);
        Assert.Equal("PERMISSIVE_OK", raised.Code);
        Assert.Equal("All conditions normal.", raised.Message);
    }

    [Fact]
    public void AnEventIsRaisedOnEachChangeAndNotInBetween()
    {
        Scan scan = Healthy().Once();
        scan.Set("Pile.Full", true).Times(3);
        scan.Set("Pile.Full", false).Times(3);

        Assert.Equal("PERMISSIVE_LOST,PERMISSIVE_OK", scan.Codes());
    }
}
```

Create `tests/Millrace.Control.Tests/InterlockTests.cs`:

```csharp
using Millrace.Io;

namespace Millrace.Control.Tests;

public class InterlockTests
{
    private static readonly TimeSpan Period = TimeSpan.FromMilliseconds(100);

    /// <summary>Trips on an overload (normal false) or on the permissive dropping (normal true).</summary>
    private static Interlock Make() => new(
        "INT01",
        [new Condition("CV001.Tripped", false), new Condition("PERM01.Ok", true)],
        [new BlockWrite("CV001.Start", TagValue.Bool(false))],
        Period);

    private static Scan Healthy()
    {
        var scan = new Scan(Make());
        scan.Set("CV001.Tripped", false).Set("PERM01.Ok", true);
        return scan;
    }

    [Fact]
    public void TheConstructorRejectsNoConditions()
    {
        Assert.Throws<ArgumentException>(() => new Interlock("INT01", [], [], Period));
    }

    [Fact]
    public void TheConstructorRejectsAWriteToABlankTag()
    {
        Assert.Throws<ArgumentException>(() => new Interlock(
            "INT01",
            [new Condition("CV001.Tripped", false)],
            [new BlockWrite(" ", TagValue.Bool(false))],
            Period));
    }

    [Fact]
    public void TheConstructorRejectsTwoWritesToOneTag()
    {
        Assert.Throws<ArgumentException>(() => new Interlock(
            "INT01",
            [new Condition("CV001.Tripped", false)],
            [new BlockWrite("CV001.Start", TagValue.Bool(false)), new BlockWrite("CV001.Start", TagValue.Bool(true))],
            Period));
    }

    [Fact]
    public void ThePinsIncludeTheResetCommandAndTheTripWrites()
    {
        Interlock interlock = Make();

        Assert.Equal(2, interlock.Inputs.Count);
        Assert.Equal("CV001.Start", Assert.Single(interlock.Writes).Name);
        Assert.Equal(TagKind.Bool, interlock.Writes[0].Kind);
        Assert.Equal("Reset", Assert.Single(interlock.Commands).Name);
        Assert.Equal(3, interlock.Outputs.Count);
        Assert.Equal("Ok", interlock.Outputs[0].Name);
        Assert.Equal("Tripped", interlock.Outputs[1].Name);
        Assert.Equal("FirstOut", interlock.Outputs[2].Name);
    }

    [Fact]
    public void TrippedLatchesAndOkFallsOnTheFirstAbnormalCondition()
    {
        Scan scan = Healthy().Once();
        Assert.True(scan.Bool("Ok"));
        Assert.False(scan.Bool("Tripped"));

        scan.Set("CV001.Tripped", true).Once();

        Assert.False(scan.Bool("Ok"));
        Assert.True(scan.Bool("Tripped"));
        Assert.Equal(0L, scan.Int64("FirstOut"));
        BlockEvent raised = Assert.Single(scan.LastEvents);
        Assert.Equal("INTERLOCK_TRIP", raised.Code);
        Assert.Equal("CV001.Tripped abnormal.", raised.Message);
    }

    [Fact]
    public void TheTripWritesGoOutOnTheTripScanOnly()
    {
        Scan scan = Healthy().Once();
        Assert.False(scan.TryWrite("CV001.Start", out _));

        scan.Set("CV001.Tripped", true).Once();
        Assert.True(scan.TryWrite("CV001.Start", out TagValue value));
        Assert.False(value.AsBool);

        scan.Once();
        Assert.False(scan.TryWrite("CV001.Start", out _));
    }

    [Fact]
    public void TheLatchHoldsAfterTheConditionReturns()
    {
        Scan scan = Healthy().Once();
        scan.Set("CV001.Tripped", true).Once();

        scan.Set("CV001.Tripped", false).Times(3);

        Assert.True(scan.Bool("Tripped"));
        Assert.Equal(0L, scan.Int64("FirstOut"));
    }

    [Fact]
    public void AResetRisingEdgeWithEveryConditionNormalClearsTheLatch()
    {
        Scan scan = Healthy().Once();
        scan.Set("CV001.Tripped", true).Once();
        scan.Set("CV001.Tripped", false).Once();

        scan.Command("Reset", true).Once();

        Assert.True(scan.Bool("Ok"));
        Assert.False(scan.Bool("Tripped"));
        Assert.Equal(-1L, scan.Int64("FirstOut"));
        BlockEvent raised = Assert.Single(scan.LastEvents);
        Assert.Equal("INTERLOCK_RESET", raised.Code);
        Assert.Equal("Reset with all conditions normal.", raised.Message);
    }

    [Fact]
    public void AResetWhileAConditionIsStillAbnormalIsRefused()
    {
        Scan scan = Healthy().Once();
        scan.Set("PERM01.Ok", false).Once();

        scan.Command("Reset", true).Times(3);

        Assert.True(scan.Bool("Tripped"));
        Assert.Equal(1L, scan.Int64("FirstOut"));
        Assert.Equal("INTERLOCK_TRIP", scan.Codes());
    }

    [Fact]
    public void AHeldHighResetDoesNotClearASecondTrip()
    {
        Scan scan = Healthy().Once();
        scan.Set("CV001.Tripped", true).Once();
        scan.Set("CV001.Tripped", false).Once();
        scan.Command("Reset", true).Once();
        Assert.False(scan.Bool("Tripped"));

        scan.Set("CV001.Tripped", true).Once();        // Reset is still high
        scan.Set("CV001.Tripped", false).Times(3);

        Assert.True(scan.Bool("Tripped"));
        Assert.Equal("INTERLOCK_TRIP,INTERLOCK_RESET,INTERLOCK_TRIP", scan.Codes());
    }

    [Fact]
    public void TheSecondConditionNamesItselfWhenItTripsFirst()
    {
        Scan scan = Healthy().Once();

        scan.Set("PERM01.Ok", false).Once();

        Assert.Equal(1L, scan.Int64("FirstOut"));
        Assert.Equal("PERM01.Ok abnormal.", Assert.Single(scan.LastEvents).Message);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Millrace.Control.Tests --nologo`
Expected: FAIL — the project does not build: `Condition`, `BlockWrite`,
`Permissive` and `Interlock` do not exist.

- [ ] **Step 3: Write the two shared records**

Create `src/Millrace.Control/Condition.cs`:

```csharp
namespace Millrace.Control;

/// <summary>
/// One Bool tag a permissive or an interlock watches, with the value that means
/// "normal". A conveyor's <c>SafetyOk</c> is normal true; its <c>Tripped</c> is
/// normal false.
/// </summary>
/// <param name="Tag">The full name of a Bool tag.</param>
/// <param name="Normal">The value that means the condition is satisfied.</param>
public sealed record Condition(string Tag, bool Normal);
```

Create `src/Millrace.Control/BlockWrite.cs`:

```csharp
using Millrace.Io;

namespace Millrace.Control;

/// <summary>
/// A tag a block commands and the value it commands on it: an interlock's trip
/// write, a sequencer's step entry write, an abort write.
/// </summary>
/// <param name="Tag">The full name of a read-write tag.</param>
/// <param name="Value">The value to command. Its kind must match the tag's.</param>
public sealed record BlockWrite(string Tag, TagValue Value);
```

- [ ] **Step 4: Write the permissive**

Create `src/Millrace.Control/Permissive.cs`:

```csharp
using Millrace.Io;

namespace Millrace.Control;

/// <summary>
/// The conditions something needs before it may start. <c>Ok</c> is
/// re-evaluated on every scan and never latches; <c>FirstOut</c> is the index
/// of the first condition to leave normal while <c>Ok</c> was true, and −1 when
/// nothing is out.
/// </summary>
public sealed class Permissive : IScanBlock
{
    private readonly Condition[] _conditions;
    private bool _ok = true;
    private long _firstOut = -1L;

    /// <summary>Creates a permissive.</summary>
    /// <param name="id">The block id; prefixes <c>Ok</c> and <c>FirstOut</c>.</param>
    /// <param name="conditions">At least one Bool tag with its normal polarity, in report order.</param>
    /// <param name="scanPeriod">How often the block scans.</param>
    public Permissive(string id, IReadOnlyList<Condition> conditions, TimeSpan scanPeriod)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(conditions);

        if (conditions.Count == 0)
        {
            throw new ArgumentException("A permissive needs at least one condition.", nameof(conditions));
        }

        if (scanPeriod <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(scanPeriod), scanPeriod, "The scan period must be positive.");
        }

        _conditions = [.. conditions];
        var pins = new TagRef[_conditions.Length];
        for (int i = 0; i < _conditions.Length; i++)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(_conditions[i].Tag, nameof(conditions));
            pins[i] = new TagRef(_conditions[i].Tag, TagKind.Bool);
        }

        Id = id;
        ScanPeriod = scanPeriod;
        Inputs = pins;
    }

    /// <inheritdoc/>
    public string Id { get; }

    /// <inheritdoc/>
    public TimeSpan ScanPeriod { get; }

    /// <inheritdoc/>
    public IReadOnlyList<TagRef> Inputs { get; }

    /// <inheritdoc/>
    public IReadOnlyList<TagRef> Writes { get; } = [];

    /// <inheritdoc/>
    public IReadOnlyList<TagSpec> Outputs { get; } =
    [
        new TagSpec("Ok", TagKind.Bool, "", "Every condition is normal"),
        new TagSpec("FirstOut", TagKind.Int64, "", "Index of the first condition to leave normal, or -1"),
    ];

    /// <inheritdoc/>
    public IReadOnlyList<TagSpec> Commands { get; } = [];

    /// <inheritdoc/>
    public void Scan(in ScanInputs inputs, ref ScanOutputs outputs)
    {
        bool ok = true;
        int first = -1;

        for (int i = 0; i < _conditions.Length; i++)
        {
            if (inputs.Input(i).AsBool != _conditions[i].Normal)
            {
                ok = false;
                if (first < 0)
                {
                    first = i;
                }
            }
        }

        if (!ok && _ok)
        {
            _firstOut = first;
            outputs.Raise("PERMISSIVE_LOST", $"{_conditions[first].Tag} dropped.");
        }
        else if (ok && !_ok)
        {
            _firstOut = -1L;
            outputs.Raise("PERMISSIVE_OK", "All conditions normal.");
        }

        _ok = ok;
        outputs.Set(0, TagValue.Bool(ok));
        outputs.Set(1, TagValue.Int64(_firstOut));
    }
}
```

- [ ] **Step 5: Write the interlock**

Create `src/Millrace.Control/Interlock.cs`:

```csharp
using Millrace.Io;

namespace Millrace.Control;

/// <summary>
/// The conditions that stop a running thing. Any abnormal condition latches
/// <c>Tripped</c>, captures <c>FirstOut</c> and sends the trip writes — on the
/// trip scan only (R71). The latch clears on a rising edge of <c>Reset</c> while
/// every condition is normal, and on nothing else.
/// </summary>
public sealed class Interlock : IScanBlock
{
    private readonly Condition[] _conditions;
    private readonly BlockWrite[] _tripWrites;
    private bool _tripped;
    private bool _previousReset;
    private long _firstOut = -1L;

    /// <summary>Creates an interlock.</summary>
    /// <param name="id">The block id; prefixes <c>Ok</c>, <c>Tripped</c>, <c>FirstOut</c> and <c>Reset</c>.</param>
    /// <param name="conditions">At least one Bool tag with its normal polarity, in report order.</param>
    /// <param name="tripWrites">What to command when the interlock trips; may be empty. One write per tag.</param>
    /// <param name="scanPeriod">How often the block scans.</param>
    public Interlock(
        string id,
        IReadOnlyList<Condition> conditions,
        IReadOnlyList<BlockWrite> tripWrites,
        TimeSpan scanPeriod)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(conditions);
        ArgumentNullException.ThrowIfNull(tripWrites);

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

        var pins = new TagRef[_conditions.Length];
        for (int i = 0; i < _conditions.Length; i++)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(_conditions[i].Tag, nameof(conditions));
            pins[i] = new TagRef(_conditions[i].Tag, TagKind.Bool);
        }

        var writes = new TagRef[_tripWrites.Length];
        for (int i = 0; i < _tripWrites.Length; i++)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(_tripWrites[i].Tag, nameof(tripWrites));
            for (int j = 0; j < i; j++)
            {
                if (string.Equals(_tripWrites[i].Tag, _tripWrites[j].Tag, StringComparison.Ordinal))
                {
                    throw new ArgumentException(
                        $"Tag '{_tripWrites[i].Tag}' is commanded twice. Command each tag once.",
                        nameof(tripWrites));
                }
            }

            writes[i] = new TagRef(_tripWrites[i].Tag, _tripWrites[i].Value.Kind);
        }

        Id = id;
        ScanPeriod = scanPeriod;
        Inputs = pins;
        Writes = writes;
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
        }

        outputs.Set(0, TagValue.Bool(!_tripped));
        outputs.Set(1, TagValue.Bool(_tripped));
        outputs.Set(2, TagValue.Int64(_firstOut));
    }
}
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/Millrace.Control.Tests --nologo`
Expected: PASS, 34 tests (15 timer, 8 permissive, 11 interlock).

- [ ] **Step 7: Run everything**

Run: `dotnet build Millrace.sln -c Release --nologo` — expect `0 Warning(s)`, `0 Error(s)`.
Run: `dotnet test Millrace.sln --nologo`
Expected: PASS, 1016 + 19 = **1035** tests. Report the number the runner prints.

- [ ] **Step 8: Commit**

```bash
git add src/Millrace.Control/Condition.cs src/Millrace.Control/BlockWrite.cs src/Millrace.Control/Permissive.cs src/Millrace.Control/Interlock.cs
```

```bash
git add tests/Millrace.Control.Tests/PermissiveTests.cs tests/Millrace.Control.Tests/InterlockTests.cs
```

```bash
git commit -m "$(cat <<'MSG'
feat(control): add the permissive and the interlock

A permissive is re-evaluated every scan and never latches; an interlock
latches, captures first-out, commands its trip writes on the trip scan
and clears only on a reset edge while every condition is normal.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
MSG
)"
```

---

### Task 7: `Alarm`

One Double tag, up to four limits, and the ISA-18.2 pair per limit: `Active`
says whether the process is out of limits, `Acked` says whether anything is
outstanding. Four states, all reachable and all readable (R72).

| `Active` | `Acked` | state |
|---|---|---|
| false | true | normal |
| true | false | unacknowledged alarm |
| true | true | acknowledged alarm |
| false | false | cleared, unacknowledged |

**Files:**
- Create: `src/Millrace.Control/AlarmLimitKind.cs`
- Create: `src/Millrace.Control/AlarmLimit.cs`
- Create: `src/Millrace.Control/Alarm.cs`
- Test: `tests/Millrace.Control.Tests/AlarmTests.cs`

**Interfaces:**
- Consumes: the contract of Task 2; `Scan` of Task 5.
- Produces:
  - `public enum Millrace.Control.AlarmLimitKind { LoLo, Lo, Hi, HiHi }`
  - `public sealed record Millrace.Control.AlarmLimit(AlarmLimitKind Kind, double Value, double Deadband, TimeSpan OnDelay)`
  - `public sealed class Millrace.Control.Alarm : IScanBlock` with
    `Alarm(string id, string input, IReadOnlyList<AlarmLimit> limits, TimeSpan scanPeriod)`;
    outputs `<Kind>.Active` then `<Kind>.Acked` per limit, limits in ascending
    kind order; command `Ack` (Bool); no writes; events `ALARM_RAISED`,
    `ALARM_CLEARED`, `ALARM_ACKED`

- [ ] **Step 1: Write the failing test**

Create `tests/Millrace.Control.Tests/AlarmTests.cs`:

```csharp
using Millrace.Io;

namespace Millrace.Control.Tests;

public class AlarmTests
{
    private static readonly TimeSpan Period = TimeSpan.FromMilliseconds(100);

    private static AlarmLimit Limit(AlarmLimitKind kind, double value, double deadband = 5.0, double onDelaySeconds = 0.0) =>
        new(kind, value, deadband, TimeSpan.FromSeconds(onDelaySeconds));

    private static Alarm Make(params AlarmLimit[] limits) =>
        new("CUR01", "CV001.Current", limits, Period);

    /// <summary>Hi at 80 and HiHi at 90, both with a deadband of 5 and no on-delay.</summary>
    private static Scan Running()
    {
        var scan = new Scan(Make(Limit(AlarmLimitKind.Hi, 80.0), Limit(AlarmLimitKind.HiHi, 90.0)));
        scan.Set("CV001.Current", 40.0);
        return scan;
    }

    [Fact]
    public void TheConstructorRejectsNoLimits()
    {
        Assert.Throws<ArgumentException>(() => Make());
    }

    [Fact]
    public void TheConstructorRejectsABlankInput()
    {
        Assert.Throws<ArgumentException>(
            () => new Alarm("CUR01", " ", [Limit(AlarmLimitKind.Hi, 80.0)], Period));
    }

    [Fact]
    public void TheConstructorRejectsTwoLimitsOfTheSameKind()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(
            () => Make(Limit(AlarmLimitKind.Hi, 80.0), Limit(AlarmLimitKind.Hi, 85.0)));
        Assert.Contains("twice", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheConstructorRejectsLimitsOutOfOrder()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(
            () => Make(Limit(AlarmLimitKind.Hi, 95.0), Limit(AlarmLimitKind.HiHi, 90.0)));
        Assert.Contains("LoLo < Lo < Hi < HiHi", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheConstructorRejectsANegativeDeadband()
    {
        Assert.Throws<ArgumentException>(
            () => Make(new AlarmLimit(AlarmLimitKind.Hi, 80.0, -1.0, TimeSpan.Zero)));
    }

    [Fact]
    public void TheConstructorRejectsANegativeOnDelay()
    {
        Assert.Throws<ArgumentException>(
            () => Make(new AlarmLimit(AlarmLimitKind.Hi, 80.0, 5.0, TimeSpan.FromSeconds(-1))));
    }

    [Fact]
    public void ThePinsAreTwoPerLimitInAscendingOrder()
    {
        Alarm alarm = Make(
            Limit(AlarmLimitKind.HiHi, 90.0),
            Limit(AlarmLimitKind.LoLo, 10.0, deadband: 2.0),
            Limit(AlarmLimitKind.Hi, 80.0),
            Limit(AlarmLimitKind.Lo, 20.0, deadband: 2.0));

        Assert.Equal("CV001.Current", Assert.Single(alarm.Inputs).Name);
        Assert.Equal(TagKind.Double, alarm.Inputs[0].Kind);
        Assert.Empty(alarm.Writes);
        Assert.Equal("Ack", Assert.Single(alarm.Commands).Name);
        Assert.Equal(
            new[] { "LoLo.Active", "LoLo.Acked", "Lo.Active", "Lo.Acked", "Hi.Active", "Hi.Acked", "HiHi.Active", "HiHi.Acked" },
            alarm.Outputs.Select(o => o.Name).ToArray());
        Assert.All(alarm.Outputs, spec => Assert.Equal(TagKind.Bool, spec.Kind));
    }

    [Fact]
    public void ALimitStartsNormalAndAcknowledged()
    {
        Scan scan = Running().Once();

        Assert.False(scan.Bool("Hi.Active"));
        Assert.True(scan.Bool("Hi.Acked"));
        Assert.Empty(scan.Events);
    }

    [Fact]
    public void AHiLimitRaisesWhenTheValueCrosses()
    {
        Scan scan = Running().Once();

        scan.Set("CV001.Current", 82.3).Once();

        Assert.True(scan.Bool("Hi.Active"));
        Assert.False(scan.Bool("Hi.Acked"));
        BlockEvent raised = Assert.Single(scan.LastEvents);
        Assert.Equal("ALARM_RAISED", raised.Code);
        Assert.Equal("Hi: 82.3 above 80.", raised.Message);
    }

    [Fact]
    public void TheOnDelayMustElapseBeforeTheAlarmRaises()
    {
        var scan = new Scan(Make(Limit(AlarmLimitKind.Hi, 80.0, onDelaySeconds: 0.2)));
        scan.Set("CV001.Current", 40.0).Once();

        scan.Set("CV001.Current", 82.3).Once();        // detected, delay 0.0
        Assert.False(scan.Bool("Hi.Active"));

        scan.Once();                                   // delay 0.1
        Assert.False(scan.Bool("Hi.Active"));

        scan.Once();                                   // delay 0.2
        Assert.True(scan.Bool("Hi.Active"));
        Assert.Equal("ALARM_RAISED", Assert.Single(scan.LastEvents).Code);
    }

    [Fact]
    public void AnOnDelayOfZeroRaisesOnTheFirstScanAcross()
    {
        var scan = new Scan(Make(Limit(AlarmLimitKind.Hi, 80.0)));

        scan.Set("CV001.Current", 82.3).Once();

        Assert.True(scan.Bool("Hi.Active"));
    }

    [Fact]
    public void ABriefExcursionShorterThanTheOnDelayDoesNotRaise()
    {
        var scan = new Scan(Make(Limit(AlarmLimitKind.Hi, 80.0, onDelaySeconds: 0.3)));
        scan.Set("CV001.Current", 40.0).Once();

        scan.Set("CV001.Current", 82.3).Times(2);
        scan.Set("CV001.Current", 40.0).Once();
        scan.Set("CV001.Current", 82.3).Times(2);

        Assert.False(scan.Bool("Hi.Active"));
        Assert.Empty(scan.Events);
    }

    [Fact]
    public void TheDeadbandKeepsTheAlarmActiveUntilTheValueRecrosses()
    {
        Scan scan = Running().Once();
        scan.Set("CV001.Current", 82.3).Once();

        scan.Set("CV001.Current", 76.0).Once();        // inside the deadband
        Assert.True(scan.Bool("Hi.Active"));
        Assert.Empty(scan.LastEvents);

        scan.Set("CV001.Current", 71.5).Once();        // below 80 - 5
        Assert.False(scan.Bool("Hi.Active"));
        BlockEvent cleared = Assert.Single(scan.LastEvents);
        Assert.Equal("ALARM_CLEARED", cleared.Code);
        Assert.Equal("Hi: 71.5 back within limits.", cleared.Message);
    }

    [Fact]
    public void ALoLimitRaisesBelowAndClearsAbove()
    {
        var scan = new Scan(Make(Limit(AlarmLimitKind.Lo, 20.0, deadband: 2.0)));
        scan.Set("CV001.Current", 40.0).Once();

        scan.Set("CV001.Current", 18.5).Once();
        Assert.True(scan.Bool("Lo.Active"));
        Assert.Equal("Lo: 18.5 below 20.", Assert.Single(scan.LastEvents).Message);

        scan.Set("CV001.Current", 21.0).Once();        // inside the deadband
        Assert.True(scan.Bool("Lo.Active"));

        scan.Set("CV001.Current", 22.0).Once();        // at 20 + 2
        Assert.False(scan.Bool("Lo.Active"));
    }

    [Fact]
    public void ALoLoLimitRaisesClearsAndIsAcknowledged()
    {
        var scan = new Scan(Make(Limit(AlarmLimitKind.LoLo, 10.0, deadband: 2.0, onDelaySeconds: 0.2)));
        scan.Set("CV001.Current", 40.0).Once();
        Assert.False(scan.Bool("LoLo.Active"));
        Assert.True(scan.Bool("LoLo.Acked"));

        scan.Set("CV001.Current", 8.5).Once();         // detected, delay 0.0
        Assert.False(scan.Bool("LoLo.Active"));

        scan.Times(2);                                 // 0.1, then 0.2
        Assert.True(scan.Bool("LoLo.Active"));
        Assert.False(scan.Bool("LoLo.Acked"));
        BlockEvent raised = Assert.Single(scan.LastEvents);
        Assert.Equal("ALARM_RAISED", raised.Code);
        Assert.Equal("LoLo: 8.5 below 10.", raised.Message);

        scan.Command("Ack", true).Once();
        Assert.True(scan.Bool("LoLo.Acked"));
        Assert.Equal("LoLo acknowledged.", Assert.Single(scan.LastEvents).Message);

        scan.Set("CV001.Current", 11.0).Once();        // inside the deadband
        Assert.True(scan.Bool("LoLo.Active"));

        scan.Set("CV001.Current", 12.5).Once();        // above 10 + 2
        Assert.False(scan.Bool("LoLo.Active"));
        Assert.Equal("LoLo: 12.5 back within limits.", Assert.Single(scan.LastEvents).Message);
    }

    [Fact]
    public void EveryLimitIsIndependent()
    {
        Scan scan = Running().Once();

        scan.Set("CV001.Current", 85.0).Once();
        Assert.True(scan.Bool("Hi.Active"));
        Assert.False(scan.Bool("HiHi.Active"));

        scan.Set("CV001.Current", 95.0).Once();
        Assert.True(scan.Bool("Hi.Active"));
        Assert.True(scan.Bool("HiHi.Active"));

        scan.Set("CV001.Current", 84.0).Once();        // below 90 - 5, still above 80
        Assert.True(scan.Bool("Hi.Active"));
        Assert.False(scan.Bool("HiHi.Active"));
    }

    [Fact]
    public void AnAckRisingEdgeAcknowledgesEveryUnacknowledgedLimit()
    {
        Scan scan = Running().Once();
        scan.Set("CV001.Current", 95.0).Once();
        Assert.False(scan.Bool("Hi.Acked"));
        Assert.False(scan.Bool("HiHi.Acked"));

        scan.Command("Ack", true).Once();

        Assert.True(scan.Bool("Hi.Active"));
        Assert.True(scan.Bool("Hi.Acked"));
        Assert.True(scan.Bool("HiHi.Acked"));
        Assert.Equal(2, scan.LastEvents.Count);
        Assert.Equal("ALARM_ACKED", scan.LastEvents[0].Code);
        Assert.Equal("Hi acknowledged.", scan.LastEvents[0].Message);
        Assert.Equal("HiHi acknowledged.", scan.LastEvents[1].Message);
    }

    [Fact]
    public void AHeldHighAckDoesNotAcknowledgeTheNextAlarm()
    {
        Scan scan = Running().Once();
        scan.Set("CV001.Current", 82.3).Once();
        scan.Command("Ack", true).Once();
        Assert.True(scan.Bool("Hi.Acked"));

        scan.Set("CV001.Current", 71.5).Once();        // clears
        scan.Set("CV001.Current", 82.3).Once();        // raises again, Ack still high

        Assert.True(scan.Bool("Hi.Active"));
        Assert.False(scan.Bool("Hi.Acked"));
    }

    [Fact]
    public void ReturnToNormalWhileUnacknowledgedLeavesBothFalse()
    {
        Scan scan = Running().Once();
        scan.Set("CV001.Current", 82.3).Once();

        scan.Set("CV001.Current", 71.5).Once();

        Assert.False(scan.Bool("Hi.Active"));
        Assert.False(scan.Bool("Hi.Acked"));
    }

    [Fact]
    public void AnAckAfterReturnToNormalClearsTheOutstandingState()
    {
        Scan scan = Running().Once();
        scan.Set("CV001.Current", 82.3).Once();
        scan.Set("CV001.Current", 71.5).Once();

        scan.Command("Ack", true).Once();

        Assert.False(scan.Bool("Hi.Active"));
        Assert.True(scan.Bool("Hi.Acked"));
        Assert.Equal("ALARM_ACKED", Assert.Single(scan.LastEvents).Code);
    }

    [Fact]
    public void TheThreeEventsArriveInOrderOverOneExcursion()
    {
        Scan scan = Running().Once();
        scan.Set("CV001.Current", 82.3).Once();
        scan.Command("Ack", true).Once();
        scan.Command("Ack", false).Once();
        scan.Set("CV001.Current", 71.5).Once();

        Assert.Equal("ALARM_RAISED,ALARM_ACKED,ALARM_CLEARED", scan.Codes());
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/Millrace.Control.Tests --nologo`
Expected: FAIL — the project does not build: `Alarm`, `AlarmLimit` and
`AlarmLimitKind` do not exist.

- [ ] **Step 3: Write the limit kinds**

Create `src/Millrace.Control/AlarmLimitKind.cs`:

```csharp
namespace Millrace.Control;

/// <summary>
/// The four limits an analog alarm may carry, declared in ascending order so
/// that sorting by this enum sorts by value: a configured set must satisfy
/// <c>LoLo &lt; Lo &lt; Hi &lt; HiHi</c>.
/// </summary>
public enum AlarmLimitKind
{
    /// <summary>The lower trip.</summary>
    LoLo,

    /// <summary>The lower warning.</summary>
    Lo,

    /// <summary>The upper warning.</summary>
    Hi,

    /// <summary>The upper trip.</summary>
    HiHi,
}
```

Create `src/Millrace.Control/AlarmLimit.cs`:

```csharp
namespace Millrace.Control;

/// <summary>
/// One configured limit. The alarm raises when the value crosses
/// <paramref name="Value"/> and stays across for <paramref name="OnDelay"/>,
/// and clears when it recrosses by <paramref name="Deadband"/>.
/// </summary>
/// <param name="Kind">Which limit this is.</param>
/// <param name="Value">The limit, in the tag's engineering unit.</param>
/// <param name="Deadband">How far back inside the limit the value must come before the alarm clears. Zero is allowed.</param>
/// <param name="OnDelay">How long the value must stay across before the alarm raises. Zero is allowed.</param>
public sealed record AlarmLimit(AlarmLimitKind Kind, double Value, double Deadband, TimeSpan OnDelay);
```

- [ ] **Step 4: Write the alarm**

Create `src/Millrace.Control/Alarm.cs`:

```csharp
using System.Globalization;
using Millrace.Io;

namespace Millrace.Control;

/// <summary>
/// An analog alarm over one Double tag. Each configured limit publishes the
/// ISA-18.2 pair <c>&lt;Kind&gt;.Active</c> and <c>&lt;Kind&gt;.Acked</c>:
/// (false, true) is normal, (true, false) an unacknowledged alarm, (true, true)
/// an acknowledged one, and (false, false) "cleared, unacknowledged". A rising
/// edge of <c>Ack</c> acknowledges every limit that has anything outstanding,
/// whether it is still active or has already returned to normal.
/// </summary>
public sealed class Alarm : IScanBlock
{
    private readonly AlarmLimit[] _limits;
    private readonly bool[] _active;
    private readonly bool[] _acked;
    private readonly bool[] _crossing;
    private readonly double[] _delay;
    private bool _previousAck;

    /// <summary>Creates an alarm.</summary>
    /// <param name="id">The block id; prefixes every owned tag.</param>
    /// <param name="input">The full name of the Double tag to watch.</param>
    /// <param name="limits">One to four limits, which must ascend LoLo &lt; Lo &lt; Hi &lt; HiHi.</param>
    /// <param name="scanPeriod">How often the block scans.</param>
    public Alarm(string id, string input, IReadOnlyList<AlarmLimit> limits, TimeSpan scanPeriod)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(input);
        ArgumentNullException.ThrowIfNull(limits);

        if (limits.Count == 0)
        {
            throw new ArgumentException("An alarm needs at least one limit.", nameof(limits));
        }

        if (scanPeriod <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(scanPeriod), scanPeriod, "The scan period must be positive.");
        }

        _limits = [.. limits.OrderBy(limit => limit.Kind)];
        var specs = new TagSpec[_limits.Length * 2];

        for (int i = 0; i < _limits.Length; i++)
        {
            AlarmLimit limit = _limits[i];

            if (!Enum.IsDefined(limit.Kind))
            {
                throw new ArgumentException(
                    $"'{limit.Kind}' is not a limit kind. Use LoLo, Lo, Hi or HiHi.", nameof(limits));
            }

            if (!double.IsFinite(limit.Value))
            {
                throw new ArgumentException(
                    $"The {limit.Kind} limit must be a finite number.", nameof(limits));
            }

            if (!double.IsFinite(limit.Deadband) || limit.Deadband < 0.0)
            {
                throw new ArgumentException(
                    $"The {limit.Kind} deadband must be zero or a finite positive number.", nameof(limits));
            }

            if (limit.OnDelay < TimeSpan.Zero)
            {
                throw new ArgumentException(
                    $"The {limit.Kind} on-delay must not be negative.", nameof(limits));
            }

            if (i > 0 && _limits[i - 1].Kind == limit.Kind)
            {
                throw new ArgumentException(
                    $"The {limit.Kind} limit is configured twice. Configure each limit once.", nameof(limits));
            }

            if (i > 0 && _limits[i - 1].Value >= limit.Value)
            {
                throw new ArgumentException(
                    string.Create(CultureInfo.InvariantCulture,
                        $"Limits must ascend LoLo < Lo < Hi < HiHi, but {_limits[i - 1].Kind} is " +
                        $"{_limits[i - 1].Value} and {limit.Kind} is {limit.Value}."),
                    nameof(limits));
            }

            string kind = limit.Kind.ToString();
            specs[2 * i] = new TagSpec($"{kind}.Active", TagKind.Bool, "", $"The {kind} limit is in alarm");
            specs[(2 * i) + 1] = new TagSpec($"{kind}.Acked", TagKind.Bool, "", $"Nothing is outstanding on the {kind} limit");
        }

        _active = new bool[_limits.Length];
        _acked = new bool[_limits.Length];
        _crossing = new bool[_limits.Length];
        _delay = new double[_limits.Length];
        Array.Fill(_acked, true);

        Id = id;
        ScanPeriod = scanPeriod;
        Inputs = [new TagRef(input, TagKind.Double)];
        Outputs = specs;
    }

    /// <inheritdoc/>
    public string Id { get; }

    /// <inheritdoc/>
    public TimeSpan ScanPeriod { get; }

    /// <inheritdoc/>
    public IReadOnlyList<TagRef> Inputs { get; }

    /// <inheritdoc/>
    public IReadOnlyList<TagRef> Writes { get; } = [];

    /// <inheritdoc/>
    public IReadOnlyList<TagSpec> Outputs { get; }

    /// <inheritdoc/>
    public IReadOnlyList<TagSpec> Commands { get; } =
    [
        new TagSpec("Ack", TagKind.Bool, "", "Acknowledges every outstanding limit on a rising edge"),
    ];

    /// <inheritdoc/>
    public void Scan(in ScanInputs inputs, ref ScanOutputs outputs)
    {
        double value = inputs.Input(0).AsDouble;
        bool ack = inputs.Command(0).AsBool;
        bool ackEdge = ack && !_previousAck;
        _previousAck = ack;

        for (int i = 0; i < _limits.Length; i++)
        {
            AlarmLimit limit = _limits[i];
            bool high = limit.Kind is AlarmLimitKind.Hi or AlarmLimitKind.HiHi;

            if (!_active[i])
            {
                if (high ? value > limit.Value : value < limit.Value)
                {
                    _delay[i] = _crossing[i] ? _delay[i] + inputs.Elapsed : 0.0;
                    _crossing[i] = true;

                    if (_delay[i] >= limit.OnDelay.TotalSeconds)
                    {
                        _active[i] = true;
                        _acked[i] = false;
                        _crossing[i] = false;
                        _delay[i] = 0.0;
                        outputs.Raise("ALARM_RAISED", string.Create(CultureInfo.InvariantCulture,
                            $"{limit.Kind}: {value} {(high ? "above" : "below")} {limit.Value}."));
                    }
                }
                else
                {
                    _crossing[i] = false;
                    _delay[i] = 0.0;
                }
            }
            else if (high ? value <= limit.Value - limit.Deadband : value >= limit.Value + limit.Deadband)
            {
                _active[i] = false;
                outputs.Raise("ALARM_CLEARED", string.Create(CultureInfo.InvariantCulture,
                    $"{limit.Kind}: {value} back within limits."));
            }

            if (ackEdge && !_acked[i])
            {
                _acked[i] = true;
                outputs.Raise("ALARM_ACKED", $"{limit.Kind} acknowledged.");
            }

            outputs.Set(2 * i, TagValue.Bool(_active[i]));
            outputs.Set((2 * i) + 1, TagValue.Bool(_acked[i]));
        }
    }
}
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `dotnet test tests/Millrace.Control.Tests --nologo`
Expected: PASS, 55 tests (15 timer, 8 permissive, 11 interlock, 21 alarm).

- [ ] **Step 6: Run everything**

Run: `dotnet build Millrace.sln -c Release --nologo` — expect `0 Warning(s)`, `0 Error(s)`.
Run: `dotnet test Millrace.sln --nologo`
Expected: PASS, 1035 + 21 = **1056** tests. Report the number the runner prints.

- [ ] **Step 7: Commit**

```bash
git add src/Millrace.Control/AlarmLimitKind.cs src/Millrace.Control/AlarmLimit.cs src/Millrace.Control/Alarm.cs tests/Millrace.Control.Tests/AlarmTests.cs
```

```bash
git commit -m "$(cat <<'MSG'
feat(control): add the analog alarm

Up to four limits over one tag, each with a deadband and an on-delay, and
each publishing the ISA-18.2 Active/Acked pair so that "cleared,
unacknowledged" is a state a reader can see.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
MSG
)"
```

---

### Task 8: `Sequencer`

A linear sequence of steps. Each step writes on entry, then waits for a
predicate or a delay, with an optional timeout. Five rising-edge commands drive
it. A step's transition is evaluated on the scan *after* the one that enters it,
because the entry writes have not landed yet (R79).

**Files:**
- Create: `src/Millrace.Control/PredicateOperator.cs`
- Create: `src/Millrace.Control/StepTransition.cs`
- Create: `src/Millrace.Control/SequenceStep.cs`
- Create: `src/Millrace.Control/Sequencer.cs`
- Test: `tests/Millrace.Control.Tests/SequencerTests.cs`

**Interfaces:**
- Consumes: the contract of Task 2; `BlockWrite` of Task 6; `Scan` of Task 5.
- Produces:
  - `public enum Millrace.Control.PredicateOperator { Equal, NotEqual, Less, LessOrEqual, Greater, GreaterOrEqual }`
  - `public sealed class Millrace.Control.StepTransition` with
    `static StepTransition When(string tag, PredicateOperator op, TagValue value)`,
    `static StepTransition After(TimeSpan delay)`, `string? Tag`,
    `PredicateOperator Operator`, `TagValue Value`, `TimeSpan Delay`, `bool IsTimed`
  - `public sealed class Millrace.Control.SequenceStep` with
    `SequenceStep(string name, IReadOnlyList<BlockWrite> entryWrites, StepTransition transition, TimeSpan? timeout = null)`,
    `string Name`, `IReadOnlyList<BlockWrite> EntryWrites`,
    `StepTransition Transition`, `TimeSpan? Timeout`
  - `public sealed class Millrace.Control.Sequencer : IScanBlock` with
    `Sequencer(string id, IReadOnlyList<SequenceStep> steps, TimeSpan scanPeriod, IReadOnlyList<BlockWrite>? abortWrites = null)`;
    outputs `Step` (Int64), `Running`, `Held`, `Complete`, `Faulted` (Bool),
    `StepTime` (Double, unit `s`); commands `Start`, `Hold`, `Resume`, `Abort`,
    `Reset` (Bool); events `STEP_ENTERED`, `SEQUENCE_COMPLETE`,
    `SEQUENCE_FAULTED`, `SEQUENCE_ABORTED`

- [ ] **Step 1: Write the failing test**

Create `tests/Millrace.Control.Tests/SequencerTests.cs`:

```csharp
using Millrace.Io;

namespace Millrace.Control.Tests;

public class SequencerTests
{
    private static readonly TimeSpan Period = TimeSpan.FromMilliseconds(100);

    private static BlockWrite Start(bool value) => new("CV001.Start", TagValue.Bool(value));

    /// <summary>Start the belt, wait for speed, then stop it and wait for the belt to rest.</summary>
    private static Sequencer Make(TimeSpan? firstTimeout = null) => new(
        "SEQ01",
        [
            new SequenceStep(
                "Start the belt",
                [Start(true)],
                StepTransition.When("CV001.Speed", PredicateOperator.GreaterOrEqual, TagValue.Double(1.0)),
                firstTimeout),
            new SequenceStep("Run", [], StepTransition.After(TimeSpan.FromSeconds(0.3))),
            new SequenceStep(
                "Stop the belt",
                [Start(false)],
                StepTransition.When("CV001.Stopped", PredicateOperator.Equal, TagValue.Bool(true))),
        ],
        Period,
        [Start(false)]);

    private static Scan Idle(TimeSpan? firstTimeout = null)
    {
        var scan = new Scan(Make(firstTimeout));
        scan.Set("CV001.Speed", 0.0).Set("CV001.Stopped", false);
        return scan;
    }

    /// <summary>Pulses a command: one scan high, one scan low.</summary>
    private static Scan Pulse(Scan scan, string command)
    {
        scan.Command(command, true).Once();
        scan.Command(command, false);
        return scan;
    }

    [Fact]
    public void TheConstructorRejectsNoSteps()
    {
        Assert.Throws<ArgumentException>(() => new Sequencer("SEQ01", [], Period));
    }

    [Fact]
    public void TheConstructorRejectsAStepNameEndingInAFullStop()
    {
        Assert.Throws<ArgumentException>(() => new Sequencer(
            "SEQ01",
            [new SequenceStep("Start the belt.", [], StepTransition.After(TimeSpan.FromSeconds(1)))],
            Period));
    }

    [Fact]
    public void TheConstructorRejectsAnOrderingOperatorOnABoolPredicate()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(() => new Sequencer(
            "SEQ01",
            [new SequenceStep(
                "Wait",
                [],
                StepTransition.When("CV001.Stopped", PredicateOperator.Greater, TagValue.Bool(true)))],
            Period));
        Assert.Contains("Bool", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheConstructorRejectsTwoWritesToOneTagOfDifferentKinds()
    {
        Assert.Throws<ArgumentException>(() => new Sequencer(
            "SEQ01",
            [
                new SequenceStep("One", [Start(true)], StepTransition.After(TimeSpan.FromSeconds(1))),
                new SequenceStep(
                    "Two",
                    [new BlockWrite("CV001.Start", TagValue.Double(1.0))],
                    StepTransition.After(TimeSpan.FromSeconds(1))),
            ],
            Period));
    }

    [Fact]
    public void TheConstructorRejectsANonPositiveTimeout()
    {
        Assert.Throws<ArgumentException>(() => new Sequencer(
            "SEQ01",
            [new SequenceStep("One", [], StepTransition.After(TimeSpan.FromSeconds(1)), TimeSpan.Zero)],
            Period));
    }

    [Fact]
    public void ThePinsAreThePredicateTagsTheWriteTagsAndTheFiveCommands()
    {
        Sequencer sequencer = Make();

        Assert.Equal(new[] { "CV001.Speed", "CV001.Stopped" }, sequencer.Inputs.Select(p => p.Name).ToArray());
        Assert.Equal(TagKind.Double, sequencer.Inputs[0].Kind);
        Assert.Equal(TagKind.Bool, sequencer.Inputs[1].Kind);
        Assert.Equal("CV001.Start", Assert.Single(sequencer.Writes).Name);
        Assert.Equal(
            new[] { "Start", "Hold", "Resume", "Abort", "Reset" },
            sequencer.Commands.Select(p => p.Name).ToArray());
        Assert.Equal(
            new[] { "Step", "Running", "Held", "Complete", "Faulted", "StepTime" },
            sequencer.Outputs.Select(p => p.Name).ToArray());
        Assert.Equal(TagKind.Int64, sequencer.Outputs[0].Kind);
        Assert.Equal(TagKind.Double, sequencer.Outputs[5].Kind);
        Assert.Equal("s", sequencer.Outputs[5].Unit);
    }

    [Fact]
    public void AStartRisingEdgeFromIdleEntersStepOne()
    {
        Scan scan = Idle().Once();
        Assert.Equal(0L, scan.Int64("Step"));
        Assert.False(scan.Bool("Running"));

        Pulse(scan, "Start");

        Assert.Equal(1L, scan.Int64("Step"));
        Assert.True(scan.Bool("Running"));
        Assert.True(scan.TryWrite("CV001.Start", out TagValue value));
        Assert.True(value.AsBool);
        BlockEvent entered = Assert.Single(scan.LastEvents);
        Assert.Equal("STEP_ENTERED", entered.Code);
        Assert.Equal("1: Start the belt.", entered.Message);
    }

    [Fact]
    public void AHeldHighStartDoesNotRetrigger()
    {
        Scan scan = Idle().Once();
        scan.Command("Start", true).Once();
        scan.Set("CV001.Speed", 2.0).Times(3);         // step 1 satisfied, then step 2 runs

        Assert.Equal(2L, scan.Int64("Step"));
        Assert.Equal("STEP_ENTERED,STEP_ENTERED", scan.Codes());
    }

    [Fact]
    public void EntryWritesGoOutOnTheScanThatEntersTheStepAndNoOther()
    {
        Scan scan = Idle().Once();
        Pulse(scan, "Start");
        Assert.True(scan.TryWrite("CV001.Start", out _));

        scan.Once();
        Assert.False(scan.TryWrite("CV001.Start", out _));
    }

    [Fact]
    public void TheSequenceAdvancesWhenThePredicateIsSatisfied()
    {
        Scan scan = Idle().Once();
        Pulse(scan, "Start");

        scan.Once();                                   // step 1's transition is not satisfied
        Assert.Equal(1L, scan.Int64("Step"));

        scan.Set("CV001.Speed", 1.0).Once();
        Assert.Equal(2L, scan.Int64("Step"));
        Assert.Equal("2: Run.", scan.LastEvents[0].Message);
    }

    [Theory]
    [InlineData(PredicateOperator.Equal, 1.0, true)]
    [InlineData(PredicateOperator.NotEqual, 1.0, false)]
    [InlineData(PredicateOperator.Less, 0.5, true)]
    [InlineData(PredicateOperator.LessOrEqual, 1.0, true)]
    [InlineData(PredicateOperator.Greater, 1.5, true)]
    [InlineData(PredicateOperator.GreaterOrEqual, 1.0, true)]
    public void EveryComparisonOperatorIsHonoured(PredicateOperator op, double actual, bool advances)
    {
        var sequencer = new Sequencer(
            "SEQ01",
            [
                new SequenceStep("Wait", [], StepTransition.When("CV001.Speed", op, TagValue.Double(1.0))),
                new SequenceStep("Done", [], StepTransition.After(TimeSpan.FromSeconds(10))),
            ],
            Period);

        var scan = new Scan(sequencer);
        scan.Set("CV001.Speed", 0.0).Once();
        Pulse(scan, "Start");
        scan.Set("CV001.Speed", actual).Once();

        Assert.Equal(advances ? 2L : 1L, scan.Int64("Step"));
    }

    [Fact]
    public void AnAfterTransitionAdvancesWhenTheStepClockReachesIt()
    {
        Scan scan = Idle().Once();
        Pulse(scan, "Start");
        scan.Set("CV001.Speed", 2.0).Once();           // into step 2, After(0.3)

        Assert.Equal(2L, scan.Int64("Step"));
        scan.Times(2);                                 // 0.1 s, 0.2 s
        Assert.Equal(2L, scan.Int64("Step"));

        scan.Once();                                   // 0.3 s
        Assert.Equal(3L, scan.Int64("Step"));
    }

    [Fact]
    public void HoldFreezesTheStepClockAndResumeContinues()
    {
        Scan scan = Idle().Once();
        Pulse(scan, "Start");
        scan.Set("CV001.Speed", 2.0).Once();           // into step 2
        scan.Once();                                   // 0.1 s

        Pulse(scan, "Hold");
        Assert.True(scan.Bool("Held"));
        scan.Times(5);
        Assert.Equal(2L, scan.Int64("Step"));
        Assert.Equal(0.1, scan.Double("StepTime"), 12);

        Pulse(scan, "Resume");
        Assert.False(scan.Bool("Held"));
        scan.Times(2);                                 // 0.2 s, 0.3 s
        Assert.Equal(3L, scan.Int64("Step"));
    }

    [Fact]
    public void AbortReturnsToIdleAndIssuesTheAbortWrites()
    {
        Scan scan = Idle().Once();
        Pulse(scan, "Start");
        scan.Set("CV001.Speed", 2.0).Once();           // into step 2

        scan.Command("Abort", true).Once();

        Assert.Equal(0L, scan.Int64("Step"));
        Assert.False(scan.Bool("Running"));
        Assert.True(scan.TryWrite("CV001.Start", out TagValue value));
        Assert.False(value.AsBool);
        BlockEvent aborted = Assert.Single(scan.LastEvents);
        Assert.Equal("SEQUENCE_ABORTED", aborted.Code);
        Assert.Equal("Aborted at step 2.", aborted.Message);
    }

    [Fact]
    public void ATimeoutFaultsTheStepAndStopsTheSequence()
    {
        Scan scan = Idle(firstTimeout: TimeSpan.FromSeconds(0.2)).Once();
        Pulse(scan, "Start");

        scan.Times(2);                                 // 0.1 s, then 0.2 s

        Assert.True(scan.Bool("Faulted"));
        Assert.False(scan.Bool("Running"));
        Assert.Equal(1L, scan.Int64("Step"));
        BlockEvent faulted = Assert.Single(scan.LastEvents);
        Assert.Equal("SEQUENCE_FAULTED", faulted.Code);
        Assert.Equal("Step 1 timed out after 0.2 s.", faulted.Message);
    }

    [Fact]
    public void StartIsIgnoredWhileFaulted()
    {
        Scan scan = Idle(firstTimeout: TimeSpan.FromSeconds(0.2)).Once();
        Pulse(scan, "Start");
        scan.Times(2);
        Assert.True(scan.Bool("Faulted"));

        Pulse(scan, "Start");

        Assert.True(scan.Bool("Faulted"));
        Assert.False(scan.Bool("Running"));
    }

    [Fact]
    public void ResetFromFaultedReturnsToIdle()
    {
        Scan scan = Idle(firstTimeout: TimeSpan.FromSeconds(0.2)).Once();
        Pulse(scan, "Start");
        scan.Times(2);

        Pulse(scan, "Reset");

        Assert.False(scan.Bool("Faulted"));
        Assert.Equal(0L, scan.Int64("Step"));
        Assert.Equal(0.0, scan.Double("StepTime"));
    }

    [Fact]
    public void TheLastStepCompletesTheSequence()
    {
        Scan scan = Idle().Once();
        Pulse(scan, "Start");
        scan.Set("CV001.Speed", 2.0).Once();           // into step 2
        scan.Times(3);                                 // After(0.3) elapses, into step 3
        Assert.Equal(3L, scan.Int64("Step"));

        scan.Set("CV001.Stopped", true).Once();

        Assert.True(scan.Bool("Complete"));
        Assert.False(scan.Bool("Running"));
        Assert.Equal(0L, scan.Int64("Step"));
        BlockEvent complete = Assert.Single(scan.LastEvents);
        Assert.Equal("SEQUENCE_COMPLETE", complete.Code);
        Assert.Equal("Finished after 3 steps.", complete.Message);
    }

    [Fact]
    public void ResetFromCompleteReturnsToIdleAndStartRunsItAgain()
    {
        Scan scan = Idle().Once();
        Pulse(scan, "Start");
        scan.Set("CV001.Speed", 2.0).Once();
        scan.Times(3);
        scan.Set("CV001.Stopped", true).Once();
        Assert.True(scan.Bool("Complete"));

        Pulse(scan, "Reset");
        Assert.False(scan.Bool("Complete"));

        scan.Set("CV001.Speed", 0.0).Set("CV001.Stopped", false);
        Pulse(scan, "Start");
        Assert.Equal(1L, scan.Int64("Step"));
    }

    [Fact]
    public void StepTimeReportsTheTimeInTheCurrentStep()
    {
        Scan scan = Idle().Once();
        Pulse(scan, "Start");
        Assert.Equal(0.0, scan.Double("StepTime"));

        scan.Times(2);
        Assert.Equal(0.2, scan.Double("StepTime"), 12);

        scan.Set("CV001.Speed", 2.0).Once();           // into step 2
        Assert.Equal(0.0, scan.Double("StepTime"));
    }

    [Fact]
    public void AbortIsIgnoredWhenTheSequenceIsIdle()
    {
        Scan scan = Idle().Once();

        scan.Command("Abort", true).Once();

        Assert.Equal(0L, scan.Int64("Step"));
        Assert.Empty(scan.Events);
    }

    [Fact]
    public void ResumeWithoutAHoldDoesNothing()
    {
        Scan scan = Idle().Once();
        Pulse(scan, "Start");

        Pulse(scan, "Resume");

        Assert.False(scan.Bool("Held"));
        Assert.Equal(1L, scan.Int64("Step"));
        Assert.Equal("STEP_ENTERED", scan.Codes());
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/Millrace.Control.Tests --nologo`
Expected: FAIL — the project does not build: `Sequencer`, `SequenceStep`,
`StepTransition` and `PredicateOperator` do not exist.

- [ ] **Step 3: Write the transition types**

Create `src/Millrace.Control/PredicateOperator.cs`:

```csharp
namespace Millrace.Control;

/// <summary>How a step's predicate compares a tag with a value.</summary>
public enum PredicateOperator
{
    /// <summary><c>==</c>. The only operator, with <see cref="NotEqual"/>, that a Bool tag allows.</summary>
    Equal,

    /// <summary><c>!=</c>.</summary>
    NotEqual,

    /// <summary><c>&lt;</c>.</summary>
    Less,

    /// <summary><c>&lt;=</c>.</summary>
    LessOrEqual,

    /// <summary><c>&gt;</c>.</summary>
    Greater,

    /// <summary><c>&gt;=</c>.</summary>
    GreaterOrEqual,
}
```

Create `src/Millrace.Control/StepTransition.cs`:

```csharp
using Millrace.Io;

namespace Millrace.Control;

/// <summary>
/// What ends a step: a comparison against a plant tag, or a delay on the step
/// clock. There is no third kind.
/// </summary>
public sealed class StepTransition
{
    private StepTransition(string? tag, PredicateOperator op, TagValue value, TimeSpan delay)
    {
        Tag = tag;
        Operator = op;
        Value = value;
        Delay = delay;
    }

    /// <summary>The tag compared, or null for a timed transition.</summary>
    public string? Tag { get; }

    /// <summary>How the tag is compared. Meaningless for a timed transition.</summary>
    public PredicateOperator Operator { get; }

    /// <summary>What the tag is compared with. Its kind is the kind the pin declares.</summary>
    public TagValue Value { get; }

    /// <summary>How long the step runs. Meaningless for a predicate transition.</summary>
    public TimeSpan Delay { get; }

    /// <summary>True when this is a delay rather than a comparison.</summary>
    public bool IsTimed => Tag is null;

    /// <summary>The step ends when <paramref name="tag"/> compares as asked.</summary>
    public static StepTransition When(string tag, PredicateOperator op, TagValue value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);

        if (!Enum.IsDefined(op))
        {
            throw new ArgumentOutOfRangeException(nameof(op), op, "That is not a comparison operator.");
        }

        if (value.Kind == TagKind.Bool && op is not (PredicateOperator.Equal or PredicateOperator.NotEqual))
        {
            throw new ArgumentException(
                $"A Bool tag cannot be compared with {op}. Use Equal or NotEqual.", nameof(op));
        }

        return new StepTransition(tag, op, value, TimeSpan.Zero);
    }

    /// <summary>The step ends when its clock reaches <paramref name="delay"/>.</summary>
    public static StepTransition After(TimeSpan delay)
    {
        if (delay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(delay), delay, "A delay must not be negative.");
        }

        return new StepTransition(null, PredicateOperator.Equal, TagValue.Bool(false), delay);
    }
}
```

Create `src/Millrace.Control/SequenceStep.cs`:

```csharp
namespace Millrace.Control;

/// <summary>
/// One step of a linear sequence: what it commands on entry, what ends it, and
/// how long it may take before the sequence faults.
/// </summary>
public sealed class SequenceStep
{
    /// <summary>Creates a step.</summary>
    /// <param name="name">A short phrase, used in the step's event message. It must not end in a full stop: the block adds one.</param>
    /// <param name="entryWrites">What to command on the scan that enters the step; may be empty.</param>
    /// <param name="transition">What ends the step.</param>
    /// <param name="timeout">How long the step may run before the sequence faults, or null for no limit.</param>
    public SequenceStep(
        string name,
        IReadOnlyList<BlockWrite> entryWrites,
        StepTransition transition,
        TimeSpan? timeout = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(entryWrites);
        ArgumentNullException.ThrowIfNull(transition);

        if (name.EndsWith('.'))
        {
            throw new ArgumentException(
                $"Step name '{name}' must not end in a full stop; the block adds one.", nameof(name));
        }

        if (timeout is { } limit && limit <= TimeSpan.Zero)
        {
            throw new ArgumentException("A step timeout must be positive, or absent.", nameof(timeout));
        }

        Name = name;
        EntryWrites = [.. entryWrites];
        Transition = transition;
        Timeout = timeout;
    }

    /// <summary>A short phrase, used in the step's event message.</summary>
    public string Name { get; }

    /// <summary>What the step commands on entry.</summary>
    public IReadOnlyList<BlockWrite> EntryWrites { get; }

    /// <summary>What ends the step.</summary>
    public StepTransition Transition { get; }

    /// <summary>How long the step may run before the sequence faults, or null.</summary>
    public TimeSpan? Timeout { get; }
}
```

- [ ] **Step 4: Write the sequencer**

Create `src/Millrace.Control/Sequencer.cs`:

```csharp
using System.Globalization;
using Millrace.Io;

namespace Millrace.Control;

/// <summary>
/// A linear sequence. <c>Start</c> from idle enters step 1; each step commands
/// its entry writes on the scan that enters it and is then tested, one scan
/// later, against its transition and its timeout. <c>Hold</c> freezes the step
/// clock, <c>Resume</c> continues, <c>Abort</c> returns to idle with the abort
/// writes, and <c>Reset</c> returns to idle from <c>Faulted</c> or
/// <c>Complete</c>. Every command is rising-edge sensitive, so a tag left high
/// does not retrigger.
/// </summary>
public sealed class Sequencer : IScanBlock
{
    private const int StartCommand = 0;
    private const int HoldCommand = 1;
    private const int ResumeCommand = 2;
    private const int AbortCommand = 3;
    private const int ResetCommand = 4;

    private readonly SequenceStep[] _steps;
    private readonly BlockWrite[] _abortWrites;
    private readonly int[] _predicateInput;      // per step: the input index its predicate reads, or -1
    private readonly int[][] _entryWriteIndex;   // per step: the write index of each entry write
    private readonly int[] _abortWriteIndex;
    private readonly bool[] _previous = new bool[5];

    private int _step;                           // 0 = idle
    private bool _running;
    private bool _held;
    private bool _complete;
    private bool _faulted;
    private double _stepTime;

    /// <summary>Creates a sequencer.</summary>
    /// <param name="id">The block id; prefixes every owned tag.</param>
    /// <param name="steps">At least one step, in order.</param>
    /// <param name="scanPeriod">How often the block scans.</param>
    /// <param name="abortWrites">What to command when the sequence is aborted; may be null or empty.</param>
    public Sequencer(
        string id,
        IReadOnlyList<SequenceStep> steps,
        TimeSpan scanPeriod,
        IReadOnlyList<BlockWrite>? abortWrites = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(steps);

        if (steps.Count == 0)
        {
            throw new ArgumentException("A sequence needs at least one step.", nameof(steps));
        }

        if (scanPeriod <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(scanPeriod), scanPeriod, "The scan period must be positive.");
        }

        _steps = [.. steps];
        _abortWrites = abortWrites is null ? [] : [.. abortWrites];

        var inputs = new List<TagRef>();
        var writes = new List<TagRef>();
        _predicateInput = new int[_steps.Length];
        _entryWriteIndex = new int[_steps.Length][];

        for (int i = 0; i < _steps.Length; i++)
        {
            SequenceStep step = _steps[i];
            _predicateInput[i] = step.Transition.IsTimed
                ? -1
                : Pin(inputs, new TagRef(step.Transition.Tag!, step.Transition.Value.Kind), "read", nameof(steps));

            int[] indices = new int[step.EntryWrites.Count];
            for (int w = 0; w < indices.Length; w++)
            {
                BlockWrite write = step.EntryWrites[w];
                ArgumentException.ThrowIfNullOrWhiteSpace(write.Tag, nameof(steps));
                indices[w] = Pin(writes, new TagRef(write.Tag, write.Value.Kind), "commanded", nameof(steps));
            }

            _entryWriteIndex[i] = indices;
        }

        _abortWriteIndex = new int[_abortWrites.Length];
        for (int w = 0; w < _abortWrites.Length; w++)
        {
            BlockWrite write = _abortWrites[w];
            ArgumentException.ThrowIfNullOrWhiteSpace(write.Tag, nameof(abortWrites));
            _abortWriteIndex[w] = Pin(writes, new TagRef(write.Tag, write.Value.Kind), "commanded", nameof(abortWrites));
        }

        Id = id;
        ScanPeriod = scanPeriod;
        Inputs = inputs;
        Writes = writes;
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
        new TagSpec("Step", TagKind.Int64, "", "The step running, or 0 when idle"),
        new TagSpec("Running", TagKind.Bool, "", "A step is running"),
        new TagSpec("Held", TagKind.Bool, "", "The step clock is frozen"),
        new TagSpec("Complete", TagKind.Bool, "", "The last step finished"),
        new TagSpec("Faulted", TagKind.Bool, "", "A step timed out"),
        new TagSpec("StepTime", TagKind.Double, "s", "Time in the current step"),
    ];

    /// <inheritdoc/>
    public IReadOnlyList<TagSpec> Commands { get; } =
    [
        new TagSpec("Start", TagKind.Bool, "", "Enters step 1 from idle on a rising edge"),
        new TagSpec("Hold", TagKind.Bool, "", "Freezes the step clock on a rising edge"),
        new TagSpec("Resume", TagKind.Bool, "", "Continues a held step on a rising edge"),
        new TagSpec("Abort", TagKind.Bool, "", "Returns to idle on a rising edge"),
        new TagSpec("Reset", TagKind.Bool, "", "Returns to idle from faulted or complete on a rising edge"),
    ];

    /// <inheritdoc/>
    public void Scan(in ScanInputs inputs, ref ScanOutputs outputs)
    {
        Span<bool> edges = stackalloc bool[5];
        for (int i = 0; i < 5; i++)
        {
            bool value = inputs.Command(i).AsBool;
            edges[i] = value && !_previous[i];
            _previous[i] = value;
        }

        if (edges[AbortCommand] && _running)
        {
            outputs.Raise("SEQUENCE_ABORTED",
                string.Create(CultureInfo.InvariantCulture, $"Aborted at step {_step}."));
            for (int w = 0; w < _abortWrites.Length; w++)
            {
                outputs.Write(_abortWriteIndex[w], _abortWrites[w].Value);
            }

            GoIdle();
        }
        else if (edges[ResetCommand] && (_faulted || _complete))
        {
            _faulted = false;
            _complete = false;
            GoIdle();
        }
        else if (edges[StartCommand] && !_running && !_faulted && !_complete)
        {
            Enter(1, ref outputs);
        }
        else if (edges[HoldCommand] && _running)
        {
            _held = true;
        }
        else if (edges[ResumeCommand] && _held)
        {
            _held = false;
        }
        else if (_running && !_held)
        {
            _stepTime += inputs.Elapsed;
            SequenceStep step = _steps[_step - 1];

            if (Satisfied(step, _predicateInput[_step - 1], in inputs))
            {
                if (_step == _steps.Length)
                {
                    outputs.Raise("SEQUENCE_COMPLETE",
                        string.Create(CultureInfo.InvariantCulture, $"Finished after {_steps.Length} steps."));
                    _complete = true;
                    GoIdle();
                }
                else
                {
                    Enter(_step + 1, ref outputs);
                }
            }
            else if (step.Timeout is { } limit && _stepTime >= limit.TotalSeconds)
            {
                outputs.Raise("SEQUENCE_FAULTED", string.Create(CultureInfo.InvariantCulture,
                    $"Step {_step} timed out after {limit.TotalSeconds} s."));
                _faulted = true;
                _running = false;
                _held = false;
            }
        }

        outputs.Set(0, TagValue.Int64(_step));
        outputs.Set(1, TagValue.Bool(_running));
        outputs.Set(2, TagValue.Bool(_held));
        outputs.Set(3, TagValue.Bool(_complete));
        outputs.Set(4, TagValue.Bool(_faulted));
        outputs.Set(5, TagValue.Double(_stepTime));
    }

    /// <summary>Adds a pin if it is new, and returns its index. Two pins on one tag must agree on the kind.</summary>
    private static int Pin(List<TagRef> pins, TagRef pin, string verb, string parameter)
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
                    $"Tag '{pin.Name}' is {verb} as a {pins[i].Kind} and as a {pin.Kind}. Use one kind.",
                    parameter);
            }

            return i;
        }

        pins.Add(pin);
        return pins.Count - 1;
    }

    private bool Satisfied(SequenceStep step, int inputIndex, in ScanInputs inputs)
    {
        if (step.Transition.IsTimed)
        {
            return _stepTime >= step.Transition.Delay.TotalSeconds;
        }

        TagValue actual = inputs.Input(inputIndex);
        TagValue wanted = step.Transition.Value;

        if (actual.Kind == TagKind.Bool)
        {
            bool equal = actual.AsBool == wanted.AsBool;
            return step.Transition.Operator == PredicateOperator.Equal ? equal : !equal;
        }

        double left = actual.Kind == TagKind.Double ? actual.AsDouble : actual.AsInt64;
        double right = wanted.Kind == TagKind.Double ? wanted.AsDouble : wanted.AsInt64;

        return step.Transition.Operator switch
        {
            PredicateOperator.Equal => left == right,
            PredicateOperator.NotEqual => left != right,
            PredicateOperator.Less => left < right,
            PredicateOperator.LessOrEqual => left <= right,
            PredicateOperator.Greater => left > right,
            _ => left >= right,
        };
    }

    private void Enter(int step, ref ScanOutputs outputs)
    {
        _step = step;
        _running = true;
        _held = false;
        _stepTime = 0.0;

        SequenceStep entered = _steps[step - 1];
        int[] indices = _entryWriteIndex[step - 1];
        for (int w = 0; w < indices.Length; w++)
        {
            outputs.Write(indices[w], entered.EntryWrites[w].Value);
        }

        outputs.Raise("STEP_ENTERED",
            string.Create(CultureInfo.InvariantCulture, $"{step}: {entered.Name}."));
    }

    private void GoIdle()
    {
        _step = 0;
        _running = false;
        _held = false;
        _stepTime = 0.0;
    }
}
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `dotnet test tests/Millrace.Control.Tests --nologo`
Expected: PASS, 82 tests (15 timer, 8 permissive, 11 interlock, 21 alarm,
21 sequencer facts and a six-row sequencer theory).

- [ ] **Step 6: Run everything**

Run: `dotnet build Millrace.sln -c Release --nologo` — expect `0 Warning(s)`, `0 Error(s)`.
Run: `dotnet test Millrace.sln --nologo`
Expected: PASS, 1056 + 27 = **1083** tests. Report the number the runner prints.

- [ ] **Step 7: Commit**

```bash
git add src/Millrace.Control/PredicateOperator.cs src/Millrace.Control/StepTransition.cs src/Millrace.Control/SequenceStep.cs src/Millrace.Control/Sequencer.cs tests/Millrace.Control.Tests/SequencerTests.cs
```

```bash
git commit -m "$(cat <<'MSG'
feat(control): add the linear sequencer

Ordered steps with entry writes, a predicate or a delay to leave by and an
optional timeout, driven by five rising-edge commands. A step's transition
is tested the scan after the one that entered it, because its writes land
at phase 1 of the next tick.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
MSG
)"
```

---

### Task 9: Each block over a small plant

The pure suites prove the logic; these prove the wiring. One test per block over
a one-component plant, driven through `WriteAt` exactly as an operator or a
scenario would drive it, asserting what the tag image holds and what the event
log says.

Two of the eight are about the rest of the engine rather than about a block:
spec 1's success criterion 1 says a block's outputs must be "visible in
`millrace tags`, `LiveState`, tick frames and the scenario recorder with no change to
`Millrace.Realtime`". `millrace tags` and the directory are covered in Task 3; the other
three are covered here, through a real `RealtimeHub` and a real
`IActionRecorder`.

**Files:**
- Create: `tests/Millrace.Control.Tests/Fakes/Vessel.cs`
- Modify: `tests/Millrace.Control.Tests/Millrace.Control.Tests.csproj` (add `Millrace.Realtime`)
- Test: `tests/Millrace.Control.Tests/HostTests.cs`

**Interfaces:**
- Consumes: `SimulationBuilder(SimulationOptions)`, `.Add(ISimNode)`,
  `.AddScanBlock(IScanBlock)`, `.Build()`; `Simulation.RunFor(TimeSpan)`,
  `.WriteAt(TimeSpan, string, TagValue)`, `.AttachFrameSink(ITickFrameSink)`,
  `.AttachActionRecorder(IActionRecorder)`, `.IO`, `.Events`, `.ScanBlockCount`;
  `TagImage.ReadBool/ReadDouble/ReadInt64` (extension methods on `ITagReader`),
  `TagImage.Snapshot()`, `TagImage.Directory`; `EventLog.Records`, `.ToText()`;
  `ComponentBase(string id)`, `AddInput<T>(string name, T defaultValue = default, bool required = false)`,
  `AddOutput<T>(string name)`, `ITagProvider.DescribeTags()`,
  `TagBinding.Read/Write`;
  `RealtimeHub(ITagDirectory directory, int ringCapacity = 4096, int recentEventCapacity = 256)`
  — an `ITickFrameSink` — with `.Pump() → int`, `.State → LiveState`;
  `LiveState.Get(string) → TagState`, `.Tick`, `.Snapshot() → StateSnapshot`;
  `TagState(TagValue Value, long LastChangeTick, DateTimeOffset LastChangeTime)`;
  `StateSnapshot.RecentEvents → IReadOnlyList<DiscreteEvent>`;
  `IActionRecorder.Wrote(long tick, string tag, TagValue value)`,
  `.Faulted(long, string, string, FaultArguments)`, `.Cleared(long, string, string)`;
  every block of Tasks 5–8.
- Produces: `public sealed class Millrace.Control.Tests.Fakes.Vessel` with
  `Vessel(string id, double ratePerSecond)`, tags `Fill` (Bool RW), `Trip`
  (Bool RW), `Level` (Double RO, unit `%`, range 0–100), `Running` (Bool RO),
  `Tripped` (Bool RO).

- [ ] **Step 1: Write the small plant and reference `Millrace.Realtime`**

In `tests/Millrace.Control.Tests/Millrace.Control.Tests.csproj`, add one line to the
`ProjectReference` group, after `Millrace.Configuration`:

```xml
    <ProjectReference Include="..\..\src\Millrace.Realtime\Millrace.Realtime.csproj" />
```

`src/Millrace.Control` still cannot see `Millrace.Realtime`, and does not want to: this is
the test project proving the *engine's* seam, exactly as plan 5b's R61 added the
same reference to `Millrace.Scenarios.Tests` in the task that first needed it.

Create `tests/Millrace.Control.Tests/Fakes/Vessel.cs`:

```csharp
using Millrace.Core.Contexts;
using Millrace.Core.Graph;
using Millrace.Core.Io;

namespace Millrace.Control.Tests.Fakes;

/// <summary>
/// A tank that fills while it is commanded to and is not tripped. Small enough
/// to reason about tick by tick, and rich enough for every block: a Double to
/// alarm on, two Bools to interlock and time on, and two commands to write.
/// </summary>
public sealed class Vessel : ComponentBase, ITagProvider
{
    private readonly double _ratePerSecond;

    public Vessel(string id, double ratePerSecond)
        : base(id)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(ratePerSecond);

        _ratePerSecond = ratePerSecond;
        Fill = AddInput<bool>("Fill");
        Trip = AddInput<bool>("Trip");
        Level = AddOutput<double>("Level");
        Running = AddOutput<bool>("Running");
        Tripped = AddOutput<bool>("Tripped");
    }

    public InputPort<bool> Fill { get; }

    public InputPort<bool> Trip { get; }

    public OutputPort<double> Level { get; }

    public OutputPort<bool> Running { get; }

    public OutputPort<bool> Tripped { get; }

    public override void Evaluate(in TickContext ctx)
    {
        bool running = Fill.Value && !Trip.Value;
        Level.Value = Math.Clamp(Level.Value + (running ? _ratePerSecond * ctx.Dt : 0.0), 0.0, 100.0);
        Running.Value = running;
        Tripped.Value = Trip.Value;
    }

    public IEnumerable<TagBinding> DescribeTags() =>
    [
        TagBinding.Write("Fill", Fill, "Fill command"),
        TagBinding.Write("Trip", Trip, "Trip command"),
        TagBinding.Read("Level", Level, "%", 0.0, 100.0, "Vessel level"),
        TagBinding.Read("Running", Running, "Filling"),
        TagBinding.Read("Tripped", Tripped, "Tripped"),
    ];
}
```

If `ComponentBase.AddInput<T>` has a different parameter shape from
`AddInput<bool>("Fill")`, copy the shape `tests/Millrace.Core.Tests/Fakes/Thermostat.cs`
uses and report the correction.

- [ ] **Step 2: Write the failing test**

Create `tests/Millrace.Control.Tests/HostTests.cs`:

```csharp
using Millrace.Control.Tests.Fakes;
using Millrace.Core;
using Millrace.Core.Faults;
using Millrace.Core.Logging;
using Millrace.Core.Time;
using Millrace.Io;
using Millrace.Realtime;

namespace Millrace.Control.Tests;

public class HostTests
{
    private static readonly TimeSpan Period = TimeSpan.FromMilliseconds(100);

    /// <summary>Records every write that landed. This plant injects no faults.</summary>
    private sealed class Spy : IActionRecorder
    {
        public List<(string Tag, long Tick, string Value)> Writes { get; } = [];

        public void Wrote(long tick, string tag, TagValue value) => Writes.Add((tag, tick, value.ToString()));

        public void Faulted(long tick, string componentId, string faultId, FaultArguments arguments) =>
            throw new NotSupportedException("The vessel plant injects no faults.");

        public void Cleared(long tick, string componentId, string faultId) =>
            throw new NotSupportedException("The vessel plant clears no faults.");
    }

    private static SimulationOptions Options => new()
    {
        Seed = 1UL,
        StartTime = new DateTimeOffset(2026, 1, 1, 6, 0, 0, TimeSpan.Zero),
        TimeStep = TimeSpan.FromMilliseconds(10),
    };

    /// <summary>One vessel filling at 10 % a second.</summary>
    private static SimulationBuilder Plant() => new SimulationBuilder(Options).Add(new Vessel("V1", 10.0));

    private static bool Has(Simulation sim, string source, string code) =>
        sim.Events.Records.Any(r =>
            string.Equals(r.Source, source, StringComparison.Ordinal) &&
            string.Equals(r.Code, code, StringComparison.Ordinal));

    [Fact]
    public void ATimerOverAPlantPublishesQAndElapsedTime()
    {
        Simulation sim = Plant()
            .AddScanBlock(new Timer("TMR01", TimerMode.OnDelay, "V1.Running", TimeSpan.FromSeconds(0.5), Period))
            .Build();
        sim.WriteAt(TimeSpan.FromMilliseconds(100), "V1.Fill", TagValue.Bool(true));

        // Running is published at the end of tick 10, so the scan at tick 20 is
        // the first that sees it; five more periods reach the 0.5 s preset.
        sim.RunFor(TimeSpan.FromMilliseconds(600));    // ticks 0..59
        Assert.False(sim.IO.ReadBool("TMR01.Q"));

        sim.RunFor(TimeSpan.FromMilliseconds(10));     // tick 60
        Assert.True(sim.IO.ReadBool("TMR01.Q"));
        Assert.Equal(0.5, sim.IO.ReadDouble("TMR01.ET"), 12);
    }

    [Fact]
    public void APermissiveOverAPlantPublishesOkAndFirstOut()
    {
        Simulation sim = Plant()
            .AddScanBlock(new Permissive(
                "PERM01",
                [new Condition("V1.Tripped", false), new Condition("V1.Running", true)],
                Period))
            .Build();
        sim.WriteAt(TimeSpan.FromMilliseconds(100), "V1.Fill", TagValue.Bool(true));

        sim.Tick();                                    // tick 0: nothing is running yet
        Assert.False(sim.IO.ReadBool("PERM01.Ok"));
        Assert.Equal(1L, sim.IO.ReadInt64("PERM01.FirstOut"));
        SimEventRecord lost = Assert.Single(sim.Events.Records);
        Assert.Equal("PERM01", lost.Source);
        Assert.Equal("PERMISSIVE_LOST", lost.Code);
        Assert.Equal("V1.Running dropped.", lost.Message);

        sim.RunFor(TimeSpan.FromMilliseconds(500));
        Assert.True(sim.IO.ReadBool("PERM01.Ok"));
        Assert.Equal(-1L, sim.IO.ReadInt64("PERM01.FirstOut"));
        Assert.True(Has(sim, "PERM01", "PERMISSIVE_OK"));
    }

    [Fact]
    public void AnInterlockOverAPlantTripsAndHoldsTheFillCommandLow()
    {
        Simulation sim = Plant()
            .AddScanBlock(new Interlock(
                "INT01",
                [new Condition("V1.Tripped", false)],
                [new BlockWrite("V1.Fill", TagValue.Bool(false))],
                Period))
            .Build();
        sim.WriteAt(TimeSpan.FromMilliseconds(100), "V1.Fill", TagValue.Bool(true));
        sim.WriteAt(TimeSpan.FromMilliseconds(500), "V1.Trip", TagValue.Bool(true));

        sim.RunFor(TimeSpan.FromSeconds(1));

        Assert.True(sim.IO.ReadBool("INT01.Tripped"));
        Assert.False(sim.IO.ReadBool("INT01.Ok"));
        Assert.False(sim.IO.ReadBool("V1.Fill"));
        Assert.True(Has(sim, "INT01", "INTERLOCK_TRIP"));
        Assert.Single(sim.Events.Records.Where(r =>
            string.Equals(r.Source, "V1.Fill", StringComparison.Ordinal) &&
            string.Equals(r.Message, "Set to false.", StringComparison.Ordinal)));
    }

    [Fact]
    public void AnAlarmOverAPlantRaisesAndIsAcknowledged()
    {
        Simulation sim = Plant()
            .AddScanBlock(new Alarm(
                "LVL01",
                "V1.Level",
                [new AlarmLimit(AlarmLimitKind.Hi, 20.0, 5.0, TimeSpan.Zero)],
                Period))
            .Build();
        sim.WriteAt(TimeSpan.FromMilliseconds(100), "V1.Fill", TagValue.Bool(true));

        // The Ack lands at phase 1 of tick 270 and is published at the end of
        // that tick, so the 100 ms scan at tick 280 is the first that sees it.
        // The run covers ticks 0..299, so 2700 ms is inside it and 2900 would
        // not be: the scan that would see a 2900 ms write is tick 300.
        sim.WriteAt(TimeSpan.FromMilliseconds(2700), "LVL01.Ack", TagValue.Bool(true));

        sim.RunFor(TimeSpan.FromSeconds(3));

        Assert.True(sim.IO.ReadBool("LVL01.Hi.Active"));
        Assert.True(sim.IO.ReadBool("LVL01.Hi.Acked"));
        Assert.True(Has(sim, "LVL01", "ALARM_RAISED"));
        Assert.True(Has(sim, "LVL01", "ALARM_ACKED"));
    }

    [Fact]
    public void ASequencerOverAPlantStepsAndCompletes()
    {
        Simulation sim = Plant()
            .AddScanBlock(new Sequencer(
                "SEQ01",
                [
                    new SequenceStep(
                        "Open the valve",
                        [new BlockWrite("V1.Fill", TagValue.Bool(true))],
                        StepTransition.When("V1.Level", PredicateOperator.GreaterOrEqual, TagValue.Double(5.0)),
                        TimeSpan.FromSeconds(5)),
                    new SequenceStep("Hold the level", [], StepTransition.After(TimeSpan.FromSeconds(0.5))),
                    new SequenceStep(
                        "Close the valve",
                        [new BlockWrite("V1.Fill", TagValue.Bool(false))],
                        StepTransition.When("V1.Running", PredicateOperator.Equal, TagValue.Bool(false)),
                        TimeSpan.FromSeconds(5)),
                ],
                Period,
                [new BlockWrite("V1.Fill", TagValue.Bool(false))]))
            .Build();
        sim.WriteAt(TimeSpan.FromMilliseconds(100), "SEQ01.Start", TagValue.Bool(true));
        sim.WriteAt(TimeSpan.FromMilliseconds(300), "SEQ01.Start", TagValue.Bool(false));

        sim.RunFor(TimeSpan.FromSeconds(5));

        Assert.True(sim.IO.ReadBool("SEQ01.Complete"));
        Assert.False(sim.IO.ReadBool("SEQ01.Running"));
        Assert.Equal(0L, sim.IO.ReadInt64("SEQ01.Step"));
        Assert.Equal(
            3,
            sim.Events.Records.Count(r =>
                string.Equals(r.Source, "SEQ01", StringComparison.Ordinal) &&
                string.Equals(r.Code, "STEP_ENTERED", StringComparison.Ordinal)));
        Assert.True(Has(sim, "SEQ01", "SEQUENCE_COMPLETE"));
    }

    [Fact]
    public void ABlockOutputReachesTheRealtimeLiveStateAndItsTickFrames()
    {
        Simulation sim = Plant()
            .AddScanBlock(new Permissive("PERM01", [new Condition("V1.Running", true)], Period))
            .Build();
        var hub = new RealtimeHub(sim.IO.Directory);
        sim.AttachFrameSink(hub);
        sim.WriteAt(TimeSpan.FromMilliseconds(100), "V1.Fill", TagValue.Bool(true));

        sim.RunFor(TimeSpan.FromMilliseconds(500));    // ticks 0..49
        hub.Pump();

        // The write lands at phase 1 of tick 10 and Running is published at the
        // end of it, so the 100 ms scan at tick 20 is the first that sees it and
        // PERM01.Ok goes true in the frame published at the end of tick 20.
        TagState ok = hub.State.Get("PERM01.Ok");
        Assert.True(ok.Value.AsBool);
        Assert.Equal(20L, ok.LastChangeTick);
        Assert.Equal(49L, hub.State.Tick);
        Assert.Equal(-1L, hub.State.Get("PERM01.FirstOut").Value.AsInt64);

        Assert.Contains(
            hub.State.Snapshot().RecentEvents,
            e => string.Equals(e.Source, "PERM01", StringComparison.Ordinal)
                 && string.Equals(e.Code, "PERMISSIVE_OK", StringComparison.Ordinal));
    }

    [Fact]
    public void ABlockWriteIsRecordedOnTheTickItLands()
    {
        Simulation sim = Plant()
            .AddScanBlock(new Interlock(
                "INT01",
                [new Condition("V1.Tripped", false)],
                [new BlockWrite("V1.Fill", TagValue.Bool(false))],
                Period))
            .Build();
        var spy = new Spy();
        sim.AttachActionRecorder(spy);
        sim.WriteAt(TimeSpan.FromMilliseconds(100), "V1.Fill", TagValue.Bool(true));
        sim.WriteAt(TimeSpan.FromMilliseconds(500), "V1.Trip", TagValue.Bool(true));

        sim.RunFor(TimeSpan.FromSeconds(1));

        // Trip lands at tick 50 and is published at the end of it; the scan at
        // tick 60 trips and queues its write, which lands at phase 1 of tick 61.
        Assert.Equal(
            new[] { ("V1.Fill", 10L, "true"), ("V1.Trip", 50L, "true"), ("V1.Fill", 61L, "false") },
            spy.Writes.ToArray());
    }

    [Fact]
    public void TwoRunsOfThePlantWithEveryBlockAreByteIdentical()
    {
        static Simulation Build()
        {
            Simulation sim = Plant()
                .AddScanBlock(new Timer("TMR01", TimerMode.OnDelay, "V1.Running", TimeSpan.FromSeconds(0.5), Period))
                .AddScanBlock(new Permissive("PERM01", [new Condition("V1.Tripped", false)], Period))
                .AddScanBlock(new Interlock(
                    "INT01",
                    [new Condition("V1.Tripped", false)],
                    [new BlockWrite("V1.Fill", TagValue.Bool(false))],
                    Period))
                .AddScanBlock(new Alarm(
                    "LVL01",
                    "V1.Level",
                    [new AlarmLimit(AlarmLimitKind.Hi, 20.0, 5.0, TimeSpan.Zero)],
                    Period))
                .Build();
            sim.WriteAt(TimeSpan.FromMilliseconds(100), "V1.Fill", TagValue.Bool(true));
            sim.WriteAt(TimeSpan.FromMilliseconds(2500), "V1.Trip", TagValue.Bool(true));
            return sim;
        }

        Simulation first = Build();
        Simulation second = Build();
        first.RunFor(TimeSpan.FromSeconds(3));
        second.RunFor(TimeSpan.FromSeconds(3));

        Assert.Equal(4, first.ScanBlockCount);
        Assert.Equal(first.Events.ToText(), second.Events.ToText());
        Assert.Equal(first.IO.Snapshot().ToArray(), second.IO.Snapshot().ToArray());
    }
}
```

- [ ] **Step 3: Run the test to verify it fails**

Run: `dotnet test tests/Millrace.Control.Tests --nologo --filter FullyQualifiedName~HostTests`
Expected: FAIL — the project does not build: `Vessel` does not exist.

- [ ] **Step 4: Run the test to verify it passes**

Write `Fakes/Vessel.cs` (Step 1) and run again.

Run: `dotnet test tests/Millrace.Control.Tests --nologo --filter FullyQualifiedName~HostTests`
Expected: PASS, 8 tests. If a tick-exact expectation in
`ATimerOverAPlantPublishesQAndElapsedTime`,
`ABlockOutputReachesTheRealtimeLiveStateAndItsTickFrames` or
`ABlockWriteIsRecordedOnTheTickItLands` is off by one scan, **report the
measurement and correct the plan's number** — do not widen the assertion to a
range.

- [ ] **Step 5: Run everything**

Run: `dotnet build Millrace.sln -c Release --nologo` — expect `0 Warning(s)`, `0 Error(s)`.
Run: `dotnet test Millrace.sln --nologo`
Expected: PASS, 1083 + 8 = **1091** tests. Report the number the runner prints.

- [ ] **Step 6: Commit**

```bash
git add tests/Millrace.Control.Tests/Millrace.Control.Tests.csproj tests/Millrace.Control.Tests/Fakes/Vessel.cs tests/Millrace.Control.Tests/HostTests.cs
```

```bash
git commit -m "$(cat <<'MSG'
test(control): run every block over a real plant

One host test per block over a one-component vessel, driven through
WriteAt as an operator would drive it; a block output reaching LiveState
and the tick frames; a block write reaching the action recorder on the
tick it lands; and two byte-identical runs of a plant with four blocks.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
MSG
)"
```

---

### Task 10: The worked example and its golden

Spec 5's worked example: the `conveyor-line` plant with a permissive, an
interlock, an alarm and a sequencer, run for 120 s, committed as a golden event
log. Blocks are attached in code (spec 1), so this is a test golden, not a CLI
one.

**The measurements the alarm limits come from.** Run on `6efd7b0` with a
scratch program that loaded
`tests/Millrace.Configuration.Tests/Plants/valid/conveyor-line.json`, reset the
safety relay at 1 s, started the belt at 5 s, injected `CV001.Motor`
`thermal-bias` `amount=0.8` at 40 s, and sampled `CV001.Current` every tick for
120 s:

| quantity | measured |
|---|---|
| running current, 10–39 s | min 1.379925633697662 A, max 1.5557235827003415 A, mean 1.421536916508591 A |
| start inrush peak | 11.882303362471239 A, 0.01 s after the contactor closes |
| ticks above 8 A | 40 (0.40 s) |
| ticks above 5 A | 87 (0.87 s) |
| ticks above 3 A | 137 (1.37 s) |
| ticks above 2 A | 177 (1.77 s) |
| during `thermal-bias` | the current does **not** climb: the starter trips on the injection tick and the motor de-energises, so the current falls to ~0 (R74) |

The same run is reproducible from this repository at any time with:

```bash
dotnet run --project src/Millrace.Cli -c Release -- run tests/Millrace.Scenarios.Tests/Scenarios/valid/conveyor-start-and-fault.json
```

which prints the committed 5b log for the same plant and shows
`CV001.Starter  OVERLOAD_TRIP` on the injection tick — the reason the current
falls rather than climbs.

So the alarm is `Hi` at **3.0 A** (deadband 0.2 A, on-delay 0.5 s) and `HiHi` at
**8.0 A** (deadband 0.5 A, on-delay 0.1 s): both well above the 1.38–1.56 A
running band and both inside the inrush window the measurements give.

**The blocks**, exactly as spec 5 names them, with the extra interlock-reset
step R73 requires:

- **PERM01** — `Permissive`, 100 ms: `CV001.SafetyOk` normal true,
  `Pile.Full` normal false.
- **INT01** — `Interlock`, 100 ms: `CV001.Tripped` normal false, `PERM01.Ok`
  normal true; trip write `CV001.Start = false`.
- **CUR01** — `Alarm` on `CV001.Current`, 100 ms, the two limits above.
- **SEQ01** — `Sequencer`, 200 ms, six steps and an abort write of
  `CV001.Start = false`.

**Files:**
- Modify: `tests/Millrace.Control.Tests/Millrace.Control.Tests.csproj` (link
  `tests/Shared/Golden.cs` and the valid plants)
- Test: `tests/Millrace.Control.Tests/WorkedExampleTests.cs`
- Create: `tests/Millrace.Control.Tests/Golden/conveyor-control.log` (generated)

**Interfaces:**
- Consumes: `PlantLoader.Load(string json, ComponentCatalogue catalogue, LoadOptions options) → LoadResult`
  with `LoadResult.IsValid`, `.Builder`, `.ToText()`;
  `new CatalogueBuilder().Add<ComponentsModule>().Build()`;
  `Simulation.InjectFaultAt(TimeSpan, string, string, FaultArguments?)`;
  `FaultArguments(params FaultArgument[])` and `FaultArgument(string Name, double Value)`;
  `Millrace.Tests.Shared.Golden.Assert(string relativePath, string actual)`.
- Produces: no new source. One committed golden log.

- [ ] **Step 1: Link the shared helper and the plants**

In `tests/Millrace.Control.Tests/Millrace.Control.Tests.csproj`, add two item groups
before the `ProjectReference` group:

```xml
  <ItemGroup>
    <Compile Include="..\Shared\Golden.cs" Link="Shared\Golden.cs" />
  </ItemGroup>

  <ItemGroup>
    <None Include="..\Millrace.Configuration.Tests\Plants\valid\*.json"
          Link="Plants\%(Filename)%(Extension)" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
```

- [ ] **Step 2: Write the worked example**

Create `tests/Millrace.Control.Tests/WorkedExampleTests.cs`:

```csharp
using Millrace.Components;
using Millrace.Configuration;
using Millrace.Core;
using Millrace.Core.Catalogue;
using Millrace.Core.Faults;
using Millrace.Io;
using Millrace.Tests.Shared;

namespace Millrace.Control.Tests;

/// <summary>
/// Spec 5c §5's worked example: the conveyor plant under a permissive, an
/// interlock, a current alarm and a start-up sequence, for two simulated
/// minutes. The blocks are attached in code because 5c gives the plant file no
/// way to describe them.
/// </summary>
public class WorkedExampleTests
{
    private static readonly TimeSpan Fast = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan Slow = TimeSpan.FromMilliseconds(200);

    private static ComponentCatalogue Catalogue { get; } = new CatalogueBuilder().Add<ComponentsModule>().Build();

    private static BlockWrite Start(bool value) => new("CV001.Start", TagValue.Bool(value));

    private static Simulation Build()
    {
        string json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Plants", "conveyor-line.json"));
        LoadResult result = PlantLoader.Load(json, Catalogue, new LoadOptions());
        Assert.True(result.IsValid, result.ToText());

        SimulationBuilder builder = result.Builder!;

        builder.AddScanBlock(new Permissive(
            "PERM01",
            [new Condition("CV001.SafetyOk", true), new Condition("Pile.Full", false)],
            Fast));

        builder.AddScanBlock(new Interlock(
            "INT01",
            [new Condition("CV001.Tripped", false), new Condition("PERM01.Ok", true)],
            [Start(false)],
            Fast));

        builder.AddScanBlock(new Alarm(
            "CUR01",
            "CV001.Current",
            [
                new AlarmLimit(AlarmLimitKind.Hi, 3.0, 0.2, TimeSpan.FromSeconds(0.5)),
                new AlarmLimit(AlarmLimitKind.HiHi, 8.0, 0.5, TimeSpan.FromSeconds(0.1)),
            ],
            Fast));

        builder.AddScanBlock(new Sequencer(
            "SEQ01",
            [
                new SequenceStep(
                    "Reset the safety relay",
                    [new BlockWrite("CV001.SafetyReset", TagValue.Bool(true))],
                    StepTransition.After(TimeSpan.FromSeconds(1))),
                new SequenceStep(
                    "Reset the interlock",
                    [
                        new BlockWrite("CV001.SafetyReset", TagValue.Bool(false)),
                        new BlockWrite("INT01.Reset", TagValue.Bool(true)),
                    ],
                    StepTransition.After(TimeSpan.FromSeconds(1))),
                new SequenceStep(
                    "Start the belt",
                    [new BlockWrite("INT01.Reset", TagValue.Bool(false)), Start(true)],
                    StepTransition.When("CV001.Speed", PredicateOperator.GreaterOrEqual, TagValue.Double(1.0)),
                    TimeSpan.FromSeconds(15)),
                new SequenceStep(
                    "Run the feed",
                    [new BlockWrite("Feed.Enabled", TagValue.Bool(true))],
                    StepTransition.After(TimeSpan.FromSeconds(60))),
                new SequenceStep(
                    "Stop the feed",
                    [new BlockWrite("Feed.Enabled", TagValue.Bool(false))],
                    StepTransition.After(TimeSpan.FromSeconds(2))),
                new SequenceStep(
                    "Stop the belt",
                    [Start(false)],
                    StepTransition.When("CV001.Stopped", PredicateOperator.Equal, TagValue.Bool(true)),
                    TimeSpan.FromSeconds(60)),
            ],
            Slow,
            [Start(false)]));

        Simulation sim = builder.Build();
        sim.WriteAt(TimeSpan.FromSeconds(1), "SEQ01.Start", TagValue.Bool(true));
        sim.WriteAt(TimeSpan.FromSeconds(2), "SEQ01.Start", TagValue.Bool(false));
        sim.InjectFaultAt(
            TimeSpan.FromSeconds(40),
            "CV001.Motor",
            "thermal-bias",
            new FaultArguments(new FaultArgument("amount", 0.8)));
        return sim;
    }

    [Fact]
    public void TheBlocksOwnTagsJoinThePlantsDirectory()
    {
        Simulation sim = Build();

        Assert.Equal(4, sim.ScanBlockCount);
        foreach (string name in new[]
                 {
                     "PERM01.Ok", "PERM01.FirstOut",
                     "INT01.Ok", "INT01.Tripped", "INT01.FirstOut", "INT01.Reset",
                     "CUR01.Hi.Active", "CUR01.Hi.Acked", "CUR01.HiHi.Active", "CUR01.HiHi.Acked", "CUR01.Ack",
                     "SEQ01.Step", "SEQ01.Running", "SEQ01.Held", "SEQ01.Complete", "SEQ01.Faulted", "SEQ01.StepTime",
                     "SEQ01.Start", "SEQ01.Hold", "SEQ01.Resume", "SEQ01.Abort", "SEQ01.Reset",
                 })
        {
            Assert.True(sim.IO.Directory.TryFind(name, out _), $"The directory has no tag '{name}'.");
        }

        // 25 plant tags (millrace tags conveyor-line.json) plus 22 owned ones.
        Assert.Equal(47, sim.IO.Directory.Count);
    }

    [Fact]
    public void TheWorkedExampleMatchesItsGolden()
    {
        Simulation sim = Build();

        sim.RunFor(TimeSpan.FromSeconds(120));

        Golden.Assert("Golden/conveyor-control.log", sim.Events.ToText());
    }

    [Fact]
    public void TheWorkedExampleRunsTwiceByteIdentically()
    {
        Simulation first = Build();
        Simulation second = Build();

        first.RunFor(TimeSpan.FromSeconds(120));
        second.RunFor(TimeSpan.FromSeconds(120));

        Assert.Equal(first.Events.ToText(), second.Events.ToText());
        Assert.Equal(first.IO.Snapshot().ToArray(), second.IO.Snapshot().ToArray());
    }
}
```

- [ ] **Step 3: Run the tests to see the golden is missing**

Run: `dotnet test tests/Millrace.Control.Tests --nologo --filter FullyQualifiedName~WorkedExampleTests`
Expected: `TheBlocksOwnTagsJoinThePlantsDirectory` and
`TheWorkedExampleRunsTwiceByteIdentically` PASS;
`TheWorkedExampleMatchesItsGolden` FAILS with `Golden file '…/Golden/conveyor-control.log'
does not exist.` If the tag count is not 47, report the number and the
directory listing and correct the plan's number.

- [ ] **Step 4: Generate the golden, then read it**

Run:

```bash
MILLRACE_UPDATE_GOLDEN=1 dotnet test tests/Millrace.Control.Tests --nologo --filter FullyQualifiedName~TheWorkedExampleMatchesItsGolden
```

Then **read the whole file** with your file-reading tool:
`tests/Millrace.Control.Tests/Golden/conveyor-control.log`.

- [ ] **Step 5: Check the golden against this checklist and report it**

Confirm each row against the text you just read, and put the answer — with the
actual line, copied — in the task report. Nothing here is invented: if a row is
absent, say so and explain from the log what happened instead.

| # | What the golden must show | Where it comes from |
|---|---|---|
| 1 | `06:00:00.000  PERM01  PERMISSIVE_LOST  CV001.SafetyOk dropped.` | the safety relay is de-energised at power-up |
| 2 | `06:00:00.000  INT01  INTERLOCK_TRIP  PERM01.Ok abnormal.` | `PERM01.Ok` reads false before `PERM01`'s first publish (R73) |
| 3 | one `CV001.Start  WRITE  Set to false.` right after row 2 | the interlock's trip write, on the trip scan only (R71) |
| 4 | `SEQ01  STEP_ENTERED  1: Reset the safety relay.` at about 1.2 s | `SEQ01.Start` lands at 1 s; the 200 ms scan sees it next |
| 5 | `CV001.Safety  SAFETY_RESET  All channels healthy; relay energised.` | step 1's entry write reaches the plant |
| 6 | `PERM01  PERMISSIVE_OK  All conditions normal.` | both conditions normal once the relay is up |
| 7 | `SEQ01  STEP_ENTERED  2: Reset the interlock.` | one second later |
| 8 | `INT01  INTERLOCK_RESET  Reset with all conditions normal.` | the rising edge of `INT01.Reset` while healthy |
| 9 | `SEQ01  STEP_ENTERED  3: Start the belt.` and `CV001.Starter  CONTACTOR_CLOSED  Motor energised.` | step 3's `CV001.Start` write |
| 10 | `CUR01  ALARM_RAISED  HiHi: <value> above 8.` within ~0.3 s of the contactor closing | the measured inrush, 11.88 A peak, above 8 A for 0.40 s |
| 11 | `CUR01  ALARM_RAISED  Hi: <value> above 3.` | above 3 A for 1.37 s, on-delay 0.5 s |
| 12 | `CUR01  ALARM_CLEARED  HiHi: <value> back within limits.` then the same for `Hi` | the current settles to the 1.38–1.56 A band |
| 13 | `SEQ01  STEP_ENTERED  4: Run the feed.` | `CV001.Speed` reaches 1.0 m/s |
| 14 | `06:00:40.000  CV001.Motor  FAULT  thermal-bias injected: amount=0.8.` | the scenario's injection |
| 15 | `06:00:40.000  CV001.Starter  OVERLOAD_TRIP  …` and `CONTACTOR_OPENED` | the plant's own reaction |
| 16 | `INT01  INTERLOCK_TRIP  CV001.Tripped abnormal.` within 0.2 s of row 15 | the interlock sees the trip one scan later |
| 17 | a second `CV001.Start  WRITE  Set to false.` right after row 16, and **no third** | the trip write, once (R71) |
| 18 | `SEQ01  STEP_ENTERED  5: Stop the feed.` and `6: Stop the belt.` | the `After(60)` and `After(2)` steps elapse |
| 19 | **either** `SEQ01  SEQUENCE_COMPLETE  Finished after 6 steps.` **or** `SEQ01  SEQUENCE_FAULTED  Step 6 timed out after 60 s.` | whichever the run produces — report which, with its timestamp |
| 20 | no `CUR01  ALARM_RAISED` after 40 s | the current falls at the overload trip; it does not climb (R74) |

Report the total line count of the golden and paste the first fifteen and the
last ten lines into the task report.

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/Millrace.Control.Tests --nologo`
Expected: PASS, 93 tests.

- [ ] **Step 7: Run everything**

Run: `dotnet build Millrace.sln -c Release --nologo` — expect `0 Warning(s)`, `0 Error(s)`.
Run: `dotnet test Millrace.sln --nologo`
Expected: PASS, 1091 + 3 = **1094** tests. The four 5b goldens are untouched.
Report the number the runner prints.

- [ ] **Step 8: Commit**

```bash
git add tests/Millrace.Control.Tests/Millrace.Control.Tests.csproj tests/Millrace.Control.Tests/WorkedExampleTests.cs tests/Millrace.Control.Tests/Golden/conveyor-control.log
```

```bash
git commit -m "$(cat <<'MSG'
test(control): commit the worked example's golden log

The conveyor plant under a permissive, an interlock, a current alarm and
a six-step sequence, for two simulated minutes. The alarm limits come
from measured running and inrush currents, not from guesses.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
MSG
)"
```

---

### Task 11: Documentation

Four pages: a new one for the control layer, a section in the architecture, the
README's status and module list, and the regenerated diagnostics reference with
its widened trailer range.

**Files:**
- Create: `docs/control-blocks.md`
- Modify: `docs/architecture.md` (a new final section)
- Modify: `README.md`
- Modify: `src/Millrace.Configuration/DiagnosticsReference.cs` (the trailer)
- Modify: `docs/configuration-diagnostics.md` (generated)
- Test: `tests/Millrace.Configuration.Tests/DiagnosticsReferenceTests.cs`
- Test: `tests/Millrace.Control.Tests/DocumentationTests.cs`

**Interfaces:**
- Consumes: `DiagnosticsReference.Render()` and
  `Golden.Assert(string relativePath, string actual)`, both unchanged.
- Produces: no new API. One constant's text changes.

- [ ] **Step 1: Write the failing tests**

Add to `tests/Millrace.Configuration.Tests/DiagnosticsReferenceTests.cs`, at the end
of the class:

```csharp
    [Fact]
    public void ThePlantValidationTrailerCoversTheBlockCodes()
    {
        string page = DiagnosticsReference.Render();

        Assert.Contains("## MR001–MR015 — plant validation\n", page, StringComparison.Ordinal);
        Assert.Contains("MR013", page, StringComparison.Ordinal);
        Assert.Contains("MR014", page, StringComparison.Ordinal);
        Assert.Contains("MR015", page, StringComparison.Ordinal);

        // Plan 5a ruled there is no MR012, and the page must never print it as
        // though it were a code. The trailer says "The numbering skips 012."
        Assert.DoesNotContain("MR012", page, StringComparison.Ordinal);
    }
```

Create `tests/Millrace.Control.Tests/DocumentationTests.cs`:

```csharp
using System.Runtime.CompilerServices;

namespace Millrace.Control.Tests;

public class DocumentationTests
{
    [Fact]
    public void TheControlBlocksPageNamesEveryBlockEveryEventAndEveryDiagnostic()
    {
        string page = Page();

        foreach (string token in new[]
                 {
                     "Timer", "Permissive", "Interlock", "Alarm", "Sequencer",
                     "PERMISSIVE_LOST", "PERMISSIVE_OK",
                     "INTERLOCK_TRIP", "INTERLOCK_RESET",
                     "ALARM_RAISED", "ALARM_CLEARED", "ALARM_ACKED",
                     "STEP_ENTERED", "SEQUENCE_COMPLETE", "SEQUENCE_FAULTED", "SEQUENCE_ABORTED",
                     "MR013", "MR014", "MR015",
                     "AddScanBlock", "IScanBlock", "ScanInputs", "ScanOutputs",
                 })
        {
            Assert.Contains(token, page, StringComparison.Ordinal);
        }

        Assert.DoesNotContain('\r', page);
    }

    /// <summary>Two levels up from this file is the repository root.</summary>
    private static string Page([CallerFilePath] string callerFile = "") =>
        File.ReadAllText(Path.Combine(Path.GetDirectoryName(callerFile)!, "..", "..", "docs", "control-blocks.md"));
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Millrace.Configuration.Tests --nologo --filter FullyQualifiedName~DiagnosticsReferenceTests`
Expected: FAIL — the trailer still says `MR001–MR011`.

Run: `dotnet test tests/Millrace.Control.Tests --nologo --filter FullyQualifiedName~DocumentationTests`
Expected: FAIL with `FileNotFoundException` — `docs/control-blocks.md` does not
exist.

- [ ] **Step 3: Widen the trailer**

In `src/Millrace.Configuration/DiagnosticsReference.cs`, replace the
`ConfigurationTrailer` constant with:

```csharp
    private const string ConfigurationTrailer =
        "## MR001–MR015 — plant validation\n\n" +
        "Codes below MR100 come from `SimulationBuilder.Validate()` and mean the same for a plant built in code:\n" +
        "duplicate ids, unconnected required inputs, algebraic loops, belts too fast for their cells, incompatible\n" +
        "flow links, tag conflicts — and, for a control block attached with `AddScanBlock`, a scan period that is\n" +
        "not a positive whole number of time steps (MR013), a pin naming a tag the plant does not have, publishes\n" +
        "with another kind or will not accept a command (MR014), and a block id or owned tag name that collides\n" +
        "with something the plant already has (MR015). The numbering skips 012. The loader passes them through\n" +
        "with the path of the first component involved; their message is split at its first sentence into message\n" +
        "and fix. See `docs/architecture.md` and `docs/control-blocks.md`.\n";
```

The sentence is "The numbering skips 012", not "There is no MR012", so that the
page never prints `MR012` as though it were a code and Step 1's
`Assert.DoesNotContain("MR012", page)` can stand.

- [ ] **Step 4: Write `docs/control-blocks.md`**

Create `docs/control-blocks.md`:

````markdown
# Control blocks

A control block is a PLC rung: a small, stateful, pure function of the tags it
reads, scanned at its own period, whose outputs are ordinary tags. `Millrace.Control`
carries five of them — a timer, a permissive, an interlock, an alarm and a
sequencer — and `Millrace.Core` carries the host that scans them.

`Millrace.Control` references `Millrace.Io.Abstractions` and nothing else. A block cannot
see a `Simulation`, a directory, a binding, a clock or an event log, which is
why a block's unit tests need none of them.

## The contract

```csharp
public sealed record TagRef(string Name, TagKind Kind);
public sealed record TagSpec(string Name, TagKind Kind, string Unit = "", string Description = "");
public readonly record struct BlockEvent(string Code, string Message);

public interface IScanBlock
{
    string Id { get; }                        // prefixes every owned tag: "INT01" → "INT01.Ok"
    TimeSpan ScanPeriod { get; }
    IReadOnlyList<TagRef>  Inputs   { get; }  // plant tags it reads, full names
    IReadOnlyList<TagRef>  Writes   { get; }  // plant tags it may command, full names
    IReadOnlyList<TagSpec> Outputs  { get; }  // tags it owns, read-only to the plant, relative names
    IReadOnlyList<TagSpec> Commands { get; }  // tags it owns, read-write, relative names
    void Scan(in ScanInputs inputs, ref ScanOutputs outputs);
}
```

Three pin classes. **Inputs** are read. **Writes** are commanded. **Outputs**
and **commands** are owned: the host publishes them as `<Id>.<Name>`, outputs
read-only and commands read-write. A command is therefore an ordinary tag write
— from a `CommandBus`, a scenario `write`, `Simulation.WriteAt` or a bound port
— and plan 5b's recorder captures it like any other.

`ScanInputs` gives `Input(int)` over `Inputs` order, `Command(int)` over
`Commands` order, `Tick`, `Now`, `DeltaSeconds` (the simulation step) and
`Elapsed` (simulation seconds since this block's previous scan, zero on the
first). `ScanOutputs` takes `Set(int, TagValue)` over `Outputs` order,
`Write(int, TagValue)` over `Writes` order and `Raise(code, message)`. **An
output not set in a scan holds its previous value**, as a PLC output does.

A scan that neither raises an event nor queues a write allocates nothing: the
host allocates every buffer once and clears rather than reallocates them. An
event message is a string, and a queued write goes through a concurrent queue,
so both allocate a little — which is why a block raises and commands on
transitions rather than on every scan.

## Attaching a block

```csharp
builder.AddScanBlock(new Interlock(
    "INT01",
    [new Condition("CV001.Tripped", false), new Condition("PERM01.Ok", true)],
    [new BlockWrite("CV001.Start", TagValue.Bool(false))],
    TimeSpan.FromMilliseconds(100)));
```

Blocks are attached **in code**. Describing them in the plant JSON — a
`controllers` section, catalogue descriptors, a loader stage and schema — is a
later plan: the block API gets a shakedown before it is frozen into a file
format. `millrace run` therefore cannot attach blocks yet.

`Build()` checks every block:

| code | check |
|---|---|
| MR013 | `ScanPeriod` is a positive whole number of time steps. |
| MR014 | Every `Inputs` and `Writes` entry names a tag the plant has, with the same kind; every `Writes` entry is read-write. |
| MR015 | The block id is unique across components and blocks, and no owned tag name collides with an existing tag. |

`Inputs` and `Writes` are resolved against the plant's tags **and every block's
owned tags**, whichever order the blocks were added, so an interlock may list
`PERM01.Ok` before `PERM01` is added.

## The timing rule

A scan at tick *N*:

- sees every input and command **as of the end of tick *N−1***;
- publishes its outputs at the end of tick ***N***, so anything that reads them
  — another block, a bound component input, an HMI — sees them at tick ***N+1***;
- queues its writes, which land at **phase 1 of tick *N+1***.

At a 100 ms period on a 10 ms step, a block reacts to the plant between 10 ms
and 110 ms late. This is the PLC asymmetry, and it is what makes scan order
among blocks due on the same tick irrelevant to the result: every block in a
tick reads the same previous publish. Blocks added first scan first, and that
order is fixed for the life of the plant.

Two consequences worth stating:

- **A block's first scan is at tick 0**, before any write scheduled for tick 0
  lands, and it reads the primed image — the plant's initial values, and the
  *default* of every owned output that has not been published yet. An interlock
  watching `PERM01.Ok` therefore trips at tick 0, exactly as a real plant powers
  up latched. Reset it as part of your start-up sequence.
- **A sequencer step takes at least two scans**: its entry writes are queued on
  the scan that enters it and land one tick later, so its transition is first
  tested on the following scan.

A plant with no blocks schedules nothing and adds nothing to the directory.

## Composition

Blocks compose through tags only. There is no block-to-block wiring API and no
ordering declaration: an interlock lists `PERM01.Ok` or `CUR01.Hi.Active` as an
input and sees it one scan late by the timing rule, like two rungs in different
scan groups.

Operators are writes. `Ack`, `Reset`, `Start`, `Hold`, `Resume` and `Abort` are
read-write tags, so a `CommandBus` write, a scenario `write` action and a test's
`WriteAt` are the same thing.

## `Timer`

```csharp
new Timer("TMR01", TimerMode.OnDelay, "CV001.Running", TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(100))
```

| pin | kind | |
|---|---|---|
| input | Bool | the tag to time |
| `Q` | Bool | output |
| `ET` | Double, `s` | elapsed time |

`OnDelay` (IEC TON) raises `Q` once the input has held true for `Preset`;
`OffDelay` (TOF) holds `Q` true for `Preset` after the input falls; `Pulse` (TP)
gives one `Preset`-long pulse on a rising edge and is not retriggerable while it
runs. A preset of zero acts immediately. `ET` accumulates the scan period, so it
is quantised to it: a 100 ms timer measures in tenths of a second. No events.

## `Permissive`

```csharp
new Permissive("PERM01",
    [new Condition("CV001.SafetyOk", true), new Condition("Pile.Full", false)],
    TimeSpan.FromMilliseconds(100))
```

The conditions something needs before it may **start**. `Ok` is every condition
at its normal polarity, re-evaluated every scan and **never latched**.
`FirstOut` is the index of the first condition to leave normal while `Ok` was
true, and −1 when nothing is out.

Events: `PERMISSIVE_LOST` — `CV001.SafetyOk dropped.` — and `PERMISSIVE_OK` —
`All conditions normal.`

## `Interlock`

```csharp
new Interlock("INT01",
    [new Condition("CV001.Tripped", false), new Condition("PERM01.Ok", true)],
    [new BlockWrite("CV001.Start", TagValue.Bool(false))],
    TimeSpan.FromMilliseconds(100))
```

The conditions that **stop** a running thing. Any abnormal condition latches
`Tripped`, drops `Ok`, captures `FirstOut` and sends the trip writes — **on the
trip scan only**, so the event log carries one `WRITE` record, not one every
scan. The latch clears on a rising edge of the `Reset` command while every
condition is normal, and on nothing else.

Events: `INTERLOCK_TRIP` — `CV001.Tripped abnormal.` — and `INTERLOCK_RESET` —
`Reset with all conditions normal.`

## `Alarm`

```csharp
new Alarm("CUR01", "CV001.Current",
    [
        new AlarmLimit(AlarmLimitKind.Hi,   3.0, 0.2, TimeSpan.FromSeconds(0.5)),
        new AlarmLimit(AlarmLimitKind.HiHi, 8.0, 0.5, TimeSpan.FromSeconds(0.1)),
    ],
    TimeSpan.FromMilliseconds(100))
```

One Double tag and up to four limits, which must ascend
`LoLo < Lo < Hi < HiHi` among those configured. Each limit publishes
`<Kind>.Active` and `<Kind>.Acked`, and the pair is the ISA-18.2 state:

| `Active` | `Acked` | state |
|---|---|---|
| false | true | normal |
| true | false | unacknowledged alarm |
| true | true | acknowledged alarm |
| false | false | cleared, unacknowledged |

A limit raises when the value crosses it and stays across for `OnDelay` (zero is
allowed), and clears when it recrosses by `Deadband`. A raise clears `Acked`; a
clear leaves `Acked` alone. A rising edge of the `Ack` command acknowledges
every limit with anything outstanding, still active or not.

Events: `ALARM_RAISED` — `Hi: 82.3 above 80.` — `ALARM_CLEARED` —
`Hi: 71.5 back within limits.` — and `ALARM_ACKED` — `Hi acknowledged.`

## `Sequencer`

```csharp
new Sequencer("SEQ01",
    [
        new SequenceStep("Start the belt",
            [new BlockWrite("CV001.Start", TagValue.Bool(true))],
            StepTransition.When("CV001.Speed", PredicateOperator.GreaterOrEqual, TagValue.Double(1.0)),
            TimeSpan.FromSeconds(15)),
        new SequenceStep("Run the feed",
            [new BlockWrite("Feed.Enabled", TagValue.Bool(true))],
            StepTransition.After(TimeSpan.FromSeconds(60))),
    ],
    TimeSpan.FromMilliseconds(200),
    [new BlockWrite("CV001.Start", TagValue.Bool(false))])
```

A linear sequence — there is no branching, and no parallel step. Each step names
what it commands on entry, what ends it, and how long it may run.

A transition is either `StepTransition.When(tag, op, value)` with `op` in
`Equal`, `NotEqual`, `Less`, `LessOrEqual`, `Greater`, `GreaterOrEqual` (a Bool
tag allows only the first two), or `StepTransition.After(delay)` on the step
clock. A step whose `timeout` elapses first faults the sequence.

Outputs are `Step` (Int64, 0 when idle), `Running`, `Held`, `Complete`,
`Faulted` and `StepTime` (Double, `s`). Commands are `Start`, `Hold`, `Resume`,
`Abort` and `Reset`, all rising-edge sensitive so a tag left high does not
retrigger: `Start` from idle enters step 1, `Hold` freezes the step clock,
`Resume` continues, `Abort` returns to idle with the abort writes, and `Reset`
returns to idle from `Faulted` or `Complete`.

Events: `STEP_ENTERED` — `2: Start the belt.` — `SEQUENCE_COMPLETE` —
`Finished after 6 steps.` — `SEQUENCE_FAULTED` —
`Step 2 timed out after 30 s.` — and `SEQUENCE_ABORTED` — `Aborted at step 3.`

## Writing your own

Implement `IScanBlock`. Validate constructor parameters with `ArgumentException`
as a component does. Build the pin lists once, in the constructor, and never
change them. Keep state in fields, set every output on every scan (or
deliberately do not, and document that it holds), and make every event message a
sentence ending in a full stop — the event log is a golden-file format, and its
bytes are a contract.

`tests/Millrace.Control.Tests/Scan.cs` shows the pattern for a pure test: values in,
published values, writes and events out, no `Simulation`.

## What is not here

Branching sequential function charts; PID; alarm shelving, priorities and a
dedicated `LiveState.Alarms`; blocks described in the plant file. All are later
plans.
````

- [ ] **Step 5: Regenerate the diagnostics page and read it**

Run:

```bash
MILLRACE_UPDATE_GOLDEN=1 dotnet test tests/Millrace.Configuration.Tests --nologo --filter FullyQualifiedName~TheCommittedReferencePageIsCurrent
```

Then **read** `docs/configuration-diagnostics.md` and confirm the trailer now
reads `## MR001–MR015 — plant validation` and that nothing above it changed:
`git diff --stat docs/configuration-diagnostics.md` must show one file, and
`git diff docs/configuration-diagnostics.md` must touch only the trailer.
`docs/scenario-diagnostics.md` quotes the range `MR100–MR112`, not this one,
so it is unchanged — confirm with `git status --short docs/`.

- [ ] **Step 6: Add the architecture section**

In `docs/architecture.md`, append a new section at the end of the file, after
"Scenarios and replay":

```markdown
## The control layer

A control block is a PLC rung: `Millrace.Control` holds five of them — a timer, a
permissive, an interlock, an alarm and a sequencer — and sees
`Millrace.Io.Abstractions` and nothing else. A block is a pure `IScanBlock`: values
and two elapsed times in, values, writes and events out. It never sees a
`Simulation`, a directory, a binding, a clock or a log, which is why its unit
tests need none of them.

`SimulationBuilder.AddScanBlock` checks each block at `Build()` — `MR013` for
the period, `MR014` for the pins, `MR015` for the names — and turns its
declared outputs and commands into ordinary tags over ordinary ports: an output
is an `OutputPort<T>` behind a read-only binding, a command an `InputPort<T>`
behind a writable one. Nothing in `TagImage`, `Millrace.Realtime` or the scenario
recorder had to learn what a block is. `Simulation` then schedules one
self-rescheduling `ScanEvent` per block, first due at tick 0, drained in phase 1
in schedule order.

The timing rule is one sentence: **a scan at tick N sees the image published at
the end of tick N−1, publishes its own outputs at the end of tick N, and its
writes land at phase 1 of tick N+1.** At a 100 ms period on a 10 ms step a block
reacts between 10 and 110 ms late — the asymmetry a real PLC has — and it is
what makes scan order among blocks due on the same tick irrelevant: they all
read the same previous publish. A plant with no blocks schedules nothing and
adds nothing to the directory, which is why the four scenario goldens of plan 5b
are byte-identical across this change.

Blocks are attached in code. Describing them in the plant file is a later plan;
the API gets a shakedown before it is frozen into a format.

See [control blocks](control-blocks.md) for each block's pins, parameters and
events.
```

- [ ] **Step 7: Update the README**

In `README.md`, in the **Status** section, replace the sentence

```
Scenarios are files too: a plant, the engine overrides, a duration and a
timeline of writes and fault injections, replayed by `millrace run` against a
committed golden event log, and recordable from a live run. The control blocks
and the reference samples are planned.
```

with

```
Scenarios are files too: a plant, the engine overrides, a duration and a
timeline of writes and fault injections, replayed by `millrace run` against a
committed golden event log, and recordable from a live run.

On top of that sits the control layer: a scan-block contract in
`Millrace.Io.Abstractions`, a host in `Millrace.Core` that scans each block at its own
period through the event queue and publishes its outputs as ordinary tags, and
`Millrace.Control` — a timer, a permissive, an interlock, an alarm and a sequencer,
which reference the I/O contract alone. Blocks are attached in code for now;
describing them in the plant file, and the reference samples, are planned.
```

and, in the same section's "See" paragraph, change

```
See `docs/superpowers/specs/` for the design, `docs/superpowers/plans/` for the
implementation plans, and `docs/architecture.md` for how the engine works.
```

to

```
See `docs/superpowers/specs/` for the design, `docs/superpowers/plans/` for the
implementation plans, `docs/architecture.md` for how the engine works, and
`docs/control-blocks.md` for the control layer.
```

Finally, in the closing links paragraph, change

```
See [scenarios](docs/scenarios.md), [authoring a component](docs/authoring-a-component.md),
the [configuration diagnostics](docs/configuration-diagnostics.md) and the
[scenario diagnostics](docs/scenario-diagnostics.md).
```

to

```
See [scenarios](docs/scenarios.md), [control blocks](docs/control-blocks.md),
[authoring a component](docs/authoring-a-component.md), the
[configuration diagnostics](docs/configuration-diagnostics.md) and the
[scenario diagnostics](docs/scenario-diagnostics.md).
```

- [ ] **Step 8: Run the tests to verify they pass**

Run: `dotnet test tests/Millrace.Configuration.Tests --nologo`
Expected: PASS, 137 tests.

Run: `dotnet test tests/Millrace.Control.Tests --nologo`
Expected: PASS, 94 tests.

- [ ] **Step 9: Run everything**

Run: `dotnet build Millrace.sln -c Release --nologo` — expect `0 Warning(s)`, `0 Error(s)`.
Run: `dotnet test Millrace.sln --nologo`
Expected: PASS, 1094 + 2 = **1096** tests. Report the number the runner prints.

- [ ] **Step 10: Commit**

```bash
git add docs/control-blocks.md docs/architecture.md docs/configuration-diagnostics.md README.md src/Millrace.Configuration/DiagnosticsReference.cs
```

```bash
git add tests/Millrace.Configuration.Tests/DiagnosticsReferenceTests.cs tests/Millrace.Control.Tests/DocumentationTests.cs
```

```bash
git commit -m "$(cat <<'MSG'
docs: document the control layer

A page for the contract, the timing rule and the five blocks; a section
in the architecture; the README's status and module list; and the
diagnostics reference regenerated for MR013 to MR015.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
MSG
)"
```

---

## Completion check

Before the final whole-branch review, confirm each of the spec's success
criteria (section 1) with a command, and put the output in the report:

| Criterion | How |
|---|---|
| a block written against `Millrace.Io.Abstractions` alone scans at its own period, reads the plant sampled and stale, and its outputs are ordinary tags | Task 2's `ABlockNeedsNothingButTheseTypesToScan`; `dotnet test --filter "FullyQualifiedName~ScanBlockHostTests"`; `grep -n "ProjectReference" src/Millrace.Control/Millrace.Control.csproj` shows one line, `Millrace.Io.Abstractions` |
| each block has a pure suite needing no `Simulation`, plus a host-level test | `dotnet test tests/Millrace.Control.Tests --nologo` — 94 tests; `grep -rn "Simulation" tests/Millrace.Control.Tests/TimerTests.cs tests/Millrace.Control.Tests/PermissiveTests.cs tests/Millrace.Control.Tests/InterlockTests.cs tests/Millrace.Control.Tests/AlarmTests.cs tests/Millrace.Control.Tests/SequencerTests.cs` prints nothing |
| a block's outputs are visible in `LiveState`, tick frames and the recorder, with no change to `Millrace.Realtime` | Task 9's `ABlockOutputReachesTheRealtimeLiveStateAndItsTickFrames` and `ABlockWriteIsRecordedOnTheTickItLands`; `git diff --stat 6efd7b0 -- src/Millrace.Realtime/` prints nothing |
| the worked example runs to a committed golden showing the sequence stepping, the alarm raising, the interlock tripping and holding the run command low, and the sequence's end | Task 10 Step 5's checklist, answered row by row with lines copied from the generated file |
| a plant with no blocks behaves exactly as before: the 947 existing tests and the four 5b goldens are unchanged | `dotnet test Millrace.sln --nologo` and `git diff --stat 6efd7b0 -- tests/Millrace.Scenarios.Tests/Golden/` prints nothing |
| zero external package references anywhere under `src/`; `Millrace.Control` references only `Millrace.Io.Abstractions` | `grep -rn "PackageReference" src/` prints nothing; the csproj grep above |
| the two parked crash paths are diagnostics, not exit 134 | Task 1 Step 7's three commands, all exit 1 |
| Release build, zero warnings | `dotnet build Millrace.sln -c Release --nologo` |
| the generated pages are current | `dotnet test --filter "FullyQualifiedName~DiagnosticsReferenceTests|FullyQualifiedName~ScenarioDiagnosticsReferenceTests|FullyQualifiedName~DocumentationTests"` |

Record, as plans 1–5b did, a **"Rulings made during execution"** section at the
end of this file for every place the code had to differ from the plan, and a
**"Parked follow-ups"** list from the final review. The parked list starts with:

- **Blocks in the plant file** — a `controllers` section, catalogue descriptors
  for blocks, a loader stage, schema and diagnostics, and `millrace run` attaching
  them. The whole reason 5c attaches in code.
- **A recording of a run with blocks replays the blocks' own writes** (R77).
  `ScenarioRecorder` sees a block's write because it lands through
  `TagImage.Write`, one of the three recorded sites. Harmless in 5c, because
  nothing can attach a block to a `ScenarioRunner` run; the plan that changes
  that must decide whether a recording excludes writes whose source is a block.
- The spec's own out-of-scope list, unchanged: branching sequential function
  charts, PID, alarm shelving and priorities, a dedicated `LiveState.Alarms`,
  CSV telemetry, `--speed`, and a JSON Schema for scenario files.

---

## Self-review

Run once, by the plan's author, after writing it. Findings are fixed inline
above; this section records what was checked and what was found.

### 1. Spec coverage

| Spec section | Requirement | Task |
|---|---|---|
| 1 Scope | five blocks in a new `Millrace.Control` | 5, 6, 7, 8 |
| 1 Scope | a scan host in `Millrace.Core` scheduling through the event queue | 3, 4 |
| 1 Scope | alarm state as tags the alarm block owns | 7 (`<Kind>.Active`/`.Acked` are `Outputs`) |
| 1 Scope | the two crash paths parked by 5b | 1 (three, not two — R65) |
| 1 Scope | blocks attached in code; `millrace run` cannot attach them; the golden is a test golden and the docs say so | 10 (a `Millrace.Control.Tests` golden), 11 (`docs/control-blocks.md`, "Attaching a block") |
| 1 Success 1 | a block against the abstractions alone; ordinary tags | 2, 3, 4 |
| 1 Success 1 | outputs visible in `millrace tags`, `LiveState`, tick frames and the recorder, with no change to `Millrace.Realtime` | 3 (the directory, which is what `millrace tags` prints), 9 (`ABlockOutputReachesTheRealtimeLiveStateAndItsTickFrames`, `ABlockWriteIsRecordedOnTheTickItLands`); `src/Millrace.Realtime` appears in no task's file list |
| 1 Success 2 | a pure suite per block plus a host-level test | 5–8 (pure), 9 (host) |
| 1 Success 3 | the worked example's golden, produced and read | 10 |
| 1 Success 4 | 947 tests and the four 5b goldens unchanged | every task's "run everything" step; the Completion check's `git diff --stat` |
| 1 Success 5 | zero packages under `src/`; `Millrace.Control` sees only the abstractions | Global Constraints; checked in Task 5 Step 7 and the Completion check |
| 2 The contract | `TagRef`, `TagSpec`, `BlockEvent`, `IScanBlock` verbatim | 2 |
| 2 `ScanInputs` members | `Input`, `Command`, `Tick`, `Now`, `DeltaSeconds`, `Elapsed` | 2 |
| 2 `ScanOutputs` members | `Set`, `Write`, `Raise`; an unset output holds; no allocation | 2 (+ `Reset`/`TryOutput`/`TryWrite`/`Events` for the host and the pure tests, R67; R68 on the message string) |
| 3 `AddScanBlock` and the MR013–MR015 table | | 3 |
| 3 Owned tags become directory entries, outputs read-only, commands read-write, with the unit and description | | 3 (`OwnedTag`, R66) |
| 3 A command is an ordinary tag write the 5b recorder captures | | 4 (`ACommandIsWritableAndTheBlockSeesItOneScanLater`), R77 |
| 3 `Build()` schedules one `ScanEvent` per block at tick 0; each reschedules; phase 1; add order among equals | | 4 (R70) |
| 3 One scan: read, supply commands, `Scan`, store, queue, log with `source` = block id | | 4 (`ScanBlockRuntime.Scan`) |
| 3 The timing rule, tick-exact | | 4 (four tests pin it) |
| 3 A plant with no blocks schedules nothing and adds nothing | | 3, 4 |
| 4 Timer TON/TOF/TP, `ET` quantised, no events | | 5 |
| 4 Permissive: `Ok`, `FirstOut`, never latched, two events | | 6 |
| 4 Interlock: latch, `Ok`, `Tripped`, `FirstOut`, trip writes on the trip scan, reset edge, two events | | 6 (R71) |
| 4 Alarm: limits, on-delay, deadband, ack, the unacknowledged-return state, ordering, three events | | 7 (R72 widens ack) |
| 6 "for each limit" | a behavioural case for `Hi`, `HiHi`, `Lo` and `LoLo`, not only the pin order | 7 (`AHiLimitRaisesWhenTheValueCrosses`, `EveryLimitIsIndependent` for `HiHi`, `ALoLimitRaisesBelowAndClearsAbove`, `ALoLoLimitRaisesClearsAndIsAcknowledged`) |
| 4 Sequencer: steps, six operators, `After`, timeout, five rising-edge commands, entry writes, four events | | 8 (R79 on when a transition is first tested) |
| 4 Owned names follow the plant naming rules; every message ends in a full stop | | 2 (`TagNameRules`), Global Constraints, 5–8 |
| 5 Composition through tags only; no wiring API; one scan late | | 3 (two-pass validation, R69), 4 (`ABlockReadsAnotherBlocksOutputOneScanLate`), 11 |
| 5 Operators are writes | | 4, 11 |
| 5 The worked example: PERM01, INT01, CUR01, SEQ01, limits from a measured run, the scenario's two actions | | 10 (R73 adds the interlock-reset step; R74 reports where the current excursion really is) |
| 6 `Millrace.Core.Tests`: validation per code; the stub block's timing tests; determinism | | 3, 4 |
| 6 `Millrace.Control.Tests`: pure suites per block; a host test per block; the golden; run twice identically | | 5–10 |
| 7 Documentation: `control-blocks.md`, architecture, README, regenerated diagnostics with the new range | | 11 |
| 8 The parked crash paths, with tests reproducing from the binary first | | 1 |
| 9 Layout table | | 5 (projects), 9 (`Millrace.Realtime`), 10 (`Golden.cs`, the plants), R75 (the test project's references) |

No requirement is unimplemented. Three places where this plan does not do what
the spec's words say, each argued from a measurement or from the source and each
recorded as a ruling: **R73** (the worked example needs an interlock-reset step,
because `INT01` trips at tick 0), **R74** (the alarm raises on the start inrush,
because `thermal-bias` does not raise the current), and **R75** (the test
project does not reference `Millrace.Scenarios`, because nothing in 5c can use it).
**R65** adds a third crash the spec did not name.

### 2. Placeholder scan

Searched for `TBD`, `TODO`, `implement later`, `fill in details`, `add
appropriate`, `handle edge cases`, `similar to Task`, `and so on`, and for steps
that describe without showing.

- Found and fixed: none. Every code step carries the file's whole content or an
  exact replacement block, and every test step carries the test.
- Two places state content that cannot be known before a run, and both say so
  rather than inventing it: **Task 10's golden**, whose body Step 4 generates,
  Step 5 checks against a twenty-row checklist and the report quotes; and the
  measured `CV001.Current` numbers, which are in the Task 10 header with the run
  that produced them. `docs/configuration-diagnostics.md` is generated (Task 11
  Step 5) from prose stated in full in Step 3, so its content is determined.
- Task 9 Step 1 and Task 3 Step 2 each carry one "if the existing API differs,
  copy the existing shape and report" note. Both name the exact existing file to
  copy from, so neither is a placeholder.

### 3. Type consistency

Checked every name used across task boundaries against its definition.

- `TagRef(Name, Kind)` and `TagSpec(Name, Kind, Unit, Description)` — defined in
  2, used in 3 (`OwnedTag`, `CheckPin`), 4 (`ScanBlockRuntime.Indices`), 5–8 and
  9. `TagSpec` has four members everywhere; no task passes a fifth.
- `ScanInputs(ReadOnlyMemory<TagValue>, ReadOnlyMemory<TagValue>, long, DateTimeOffset, double, double)`
  — 2, constructed in 4 (`ScanBlockRuntime`), in 5's `Scan` harness and in 2's
  own test. Same six arguments, same order, in all three.
- `ScanOutputs(int outputCount, int writeCount)`, `.Set/.Write/.Raise/.Reset/
  .TryOutput/.TryWrite/.Events/.OutputCount/.WriteCount` — 2, used in 4, 5's
  harness and every block. No block calls anything else.
- `IScanBlock.Id/ScanPeriod/Inputs/Writes/Outputs/Commands/Scan` — 2, implemented
  by `EchoBlock` (3), `Timer` (5), `Permissive`/`Interlock` (6), `Alarm` (7),
  `Sequencer` (8) and by 2's own test stub. Every implementation declares all
  seven.
- `OwnedTag.Output(string blockId, string fullName, TagSpec spec)` and
  `.Command(...)` — 3; called from `SimulationBuilder.AddOwned` in the same
  task. `OwnedTag.Name/Binding/Store` — 3, read by `ScanBlockRuntime` in 4.
- `ScanBlockPlan(IScanBlock, long, OwnedTag[], OwnedTag[])` with
  `Block/PeriodTicks/Outputs/Commands` — 3, consumed by `ScanBlockRuntime` and
  `Simulation` in 4.
- `ScanBlockRuntime(ScanBlockPlan, TagImage, EventLog)`, `.PeriodTicks`,
  `.Scan(long, DateTimeOffset, double)` — 4, called only from `Simulation.ScanEvent`
  in the same task.
- `SimulationBuilder.AddScanBlock(IScanBlock) → SimulationBuilder` — 3, used in
  3, 4, 9, 10. `Simulation.ScanBlockCount` — 3, asserted in 3, 4, 9, 10.
- `Simulation(ISimComponent[], FlowGraph, SimulationOptions, TagImage, ScanBlockPlan[])`
  — the signature is changed once, in 3, and its only caller,
  `SimulationBuilder.Build`, is rewritten in the same step.
- `Condition(string Tag, bool Normal)` — 6, used by `Permissive` and `Interlock`
  in 6, by `HostTests` in 9 and by the worked example in 10.
- `BlockWrite(string Tag, TagValue Value)` — 6, used by `Interlock` in 6, by
  `SequenceStep`/`Sequencer` in 8, and in 9 and 10. Defined once, in Task 6,
  which is why Task 8's "Consumes" names Task 6.
- `TimerMode.OnDelay/OffDelay/Pulse` and `Timer(id, mode, input, preset, scanPeriod)`
  — 5, used in 9 and in `docs/control-blocks.md`.
- `AlarmLimitKind.LoLo/Lo/Hi/HiHi`, `AlarmLimit(Kind, Value, Deadband, OnDelay)`,
  `Alarm(id, input, limits, scanPeriod)` — 7, used in 9 and 10 with four
  positional arguments every time.
- `PredicateOperator` (six members), `StepTransition.When/After` with
  `Tag/Operator/Value/Delay/IsTimed`, `SequenceStep(name, entryWrites, transition, timeout = null)`,
  `Sequencer(id, steps, scanPeriod, abortWrites = null)` — 8, used in 9 and 10.
- `Scan(IScanBlock, double)` and its members — 5, used in 6, 7 and 8. `Set` is
  overloaded on `bool`/`double`/`long` for **inputs**; `Command` takes a pin
  name and a `bool`. No test calls `Set` on a command pin or `Command` on an
  input.
- `EchoBlock(string, TimeSpan)` with `.Reads/.MayWrite/.Publishes/.Accepts` and
  `.ScanTicks/.ElapsedSeconds/.Seen/.SeenCommand/.Silent/.WriteOnce/.RaiseOnce/.RaiseEveryScan`
  — 3, used in 3 and 4. `Publishes` and `Accepts` take
  `(name, kind = Bool, unit = "", description = "")` in both tasks.
- `Vessel(string, double)` with tags `V1.Fill/V1.Trip/V1.Level/V1.Running/V1.Tripped`
  — 9, used only in 9.
- `Golden.Assert(string relativePath, string actual)` — existing, used in 10 and
  by the diagnostics tests in 11.
- `ScenarioDiagnostics.BadValue`/`Error` and `ConfigDiagnostics.BadParameter`/
  `LoadState.Error` — existing, used only in 1.

Three inconsistencies found and fixed during this review:

- **Test counts.** The first draft's chain (954 → 968 → 986 → 1000 → 1014 →
  1033 → 1052 → 1078 → 1084 → 1087 → 1089) counted `[Fact]` methods from the
  task outline rather than from the written tests. Counting the written ones:
  Task 3 is 19 (17 facts and a two-row theory), Task 5 is 15, Task 7 is 21 and
  Task 8 is 27 (21 facts and a six-row theory). With the two tests section 4
  adds to Task 9 and the one it adds to Task 7, the chain is
  **947 → 954 → 968 → 987 → 1001 → 1016 → 1035 → 1056 → 1083 → 1091 → 1094 →
  1096**, and the task map now carries the per-task deltas, the per-project
  totals and `Millrace.Control.Tests`' running total so the next drift is visible.
- **Collection expressions in `Assert.Equal`.** Several assertions in Tasks 3
  and 4 were written `Assert.Equal([0L, 2L], block.ScanTicks)`, where a
  collection expression can bind either to `Assert.Equal<T>(T, T)` with
  `T = List<long>` or to the `IEnumerable<T>` overload. They are now
  `new long[] { … }` and `new[] { … }`, which binds unambiguously. The same
  applies to the `.Select(…).ToArray()` assertions in Tasks 7, 8 and 9.
- **`SimulationValidationException`'s member.** Task 3's test read
  `error.Result.Errors` behind a hedge. Confirmed against
  `src/Millrace.Core/Validation/SimulationValidationException.cs`: the property is
  `Result`, of type `ValidationResult`, with `Errors`. The hedge is gone.

### 4. Amendments after the independent review

A separate reviewer checked every spec row and 21 claims this plan makes about
the existing source (all 21 correct) and found ten things to fix. All are fixed
above; recorded here so the execution reports know what moved.

- **`OnDelayResetsWhenTheInputFalls` asserted the wrong side of a boundary.**
  `Scan.Once()` gives `Elapsed = 0` only on the harness's *first* scan, so after
  the input falls and returns, three scans accumulate 0.1 + 0.1 + 0.1 = the
  0.3 s preset and `Q` is true, not false. The test now runs two scans, asserts
  `Q` false and `ET` 0.2, then one more and asserts `Q` true — the boundary
  stated from both sides. The other eight timer tests were re-derived scan by
  scan against the same rule and are correct as written; so is Task 9's
  `ATimerOverAPlantPublishesQAndElapsedTime`, whose `Elapsed` comes from the
  host.
- **`AnAlarmOverAPlantRaisesAndIsAcknowledged` acknowledged nothing.**
  `LVL01.Ack` at 2900 ms lands at phase 1 of tick 290 and is published at the
  end of it, so the first 100 ms scan that could see it is tick 300 — and
  `RunFor(3 s)` covers ticks 0 to 299. The write is at 2700 ms now, with the
  tick arithmetic in a comment beside it.
- **The `MR012` assertion contradicted the trailer.** Task 11's new test
  asserts the page never prints `MR012`, while the draft trailer said "There is
  no MR012." The trailer now says "The numbering skips 012", the assertion
  stands, and Step 3 says why the sentence is phrased that way.
- **`LoLo` had no behavioural test** — only the pin-order one. Task 7 gains
  `ALoLoLimitRaisesClearsAndIsAcknowledged`, which walks a `LoLo` limit through
  on-delay, raise, ack, deadband and clear, mirroring the `Hi` cases. `Lo`
  already had `ALoLimitRaisesBelowAndClearsAbove`; `HiHi` has
  `EveryLimitIsIndependent`.
- **Spec 1's "visible in `LiveState`, tick frames and the scenario recorder" was
  untested.** Task 9 gains two tests — a `RealtimeHub` attached as the frame
  sink, asserting `PERM01.Ok` reaches `LiveState` with `LastChangeTick == 20`
  and its event reaches `RecentEvents`; and an `IActionRecorder` spy asserting
  an interlock's trip write is recorded as `("V1.Fill", 61, "false")`, which
  pins R77 rather than merely asserting it. `tests/Millrace.Control.Tests` therefore
  references `Millrace.Realtime`, added in Task 9 where it is first needed; R75, the
  file-structure tree and the layout row all say so, and `src/Millrace.Control` is
  unchanged.
- **R68 claimed more than is true.** A queued write goes through
  `TagImage.Write`, whose `ConcurrentQueue.Enqueue` allocates. The ruling and
  `docs/control-blocks.md` now claim exactly what holds — a scan that neither
  raises nor writes allocates nothing — and the ruling says why this is argued
  rather than tested.
- **R70 over-claimed the sequence numbers.** Only the tick-0 scans hold
  `0 … n−1`; a rescheduled `ScanEvent` takes a fresh sequence number and sorts
  *after* a same-tick `WriteAt`. Reworded, with the reason it is harmless: a
  scan reads the published image, never the live ports.
- Cosmetic: Task 1 now names the real theory, `ABadTopLevelValueIsMr202`
  (`tests/Millrace.Scenarios.Tests/ScenarioParseTests.cs:116`); Task 5's "Produces"
  block lists `double DeltaSeconds`; and `docs/control-blocks.md`'s
  `CUR01.Hi.Active` is kept — it is what the id rule produces, and the spec's
  `CV001.Current.Hi.Active` is being corrected there instead.

