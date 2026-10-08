# Scenarios, Replay and `millrace run` Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make a plant runnable from a file: a JSON scenario that names a plant,
a seed, a step and a timeline of writes, fault injections and clearances; a
loader whose every error names its fix before tick 0; a runner that produces an
event log; committed golden logs as the regression artifact; an in-process
recorder proven by a record-and-replay round trip; and `millrace run`, which
distinguishes "the configuration is broken" (exit 1) from "the behaviour
changed" (exit 4).

**Architecture:** Two small seams in `Millrace.Core` — `Simulation.WriteAt`/`WriteIn`
(a write that lands on exactly the tick it names, through a new
`TagImage.ApplyNow`) and `IActionRecorder` (one recorder, called from the three
places an action lands) — carry everything the rest needs. `Millrace.Scenarios` is a
new project over `Millrace.Core` and `Millrace.Configuration`: `ScenarioLoader.Parse` is a
pure structural pass producing `MR200`–`MR204`; `ScenarioRunner.Run` loads the
plant, schedules every action with its checks (`MR203`, `MR205`, `MR206`) and
runs; `ScenarioJson.Write` renders a `Scenario` deterministically;
`ScenarioRecorder` turns a live run into one; `GoldenLog.Compare` judges a log
against a committed one. `Millrace.Cli` gains `run` and `ExitCodes.LogMismatch = 4`.

**Tech Stack:** .NET 10 (`net10.0`), C#, `System.Text.Json`, xUnit. No external
runtime dependencies anywhere under `src/`.

**Spec:** `docs/superpowers/specs/2026-09-22-scenarios-and-run-design.md` (all of
it), which refines sections 4, 13, 14 and 17 of
`docs/superpowers/specs/2026-09-02-industrial-process-simulation-engine-design.md`
and builds on `docs/superpowers/specs/2026-09-20-catalogue-configuration-cli-design.md`.

**Plan sequence:** This is plan 5b. Plans 1–5a are merged on `master` at
`ae46ba2` (751 tests green, Release build with `0 Warning(s)`). Plan 5c adds
`Millrace.Control` (interlock, permissive, sequencer, timer, alarm) and the questions
that belong with it; plan 6 the two reference samples. Nothing in this plan may
reference those subsystems: no controller, no alarm, no sample. `EventLog.ToText()`
is used exactly as it stands and is **not** changed — every golden log in this
plan, and every one plan 5c writes, depends on its bytes.

**Task shape.** The spec's nine suggested tasks are kept, in order, with two
adjustments the code argued for:

- **The invalid corpus is split in two directories, not one.** `MR200`–`MR204`
  are found by `ScenarioLoader.Parse` with no plant and no file system, so they
  are fixtures of Task 3 (`Scenarios/invalid/`). `MR205` and `MR206` need a
  plant on disk and a built simulation, so they are fixtures of Task 5
  (`Scenarios/unrunnable/`). The "every code has a fixture" test therefore
  arrives with the last fixture, in Task 5, and no task needs plan 5a's
  shrinking `Pending` array.
- **Task 6 (`GoldenLog`) comes after Task 5 (the runner), not before.** The
  runner's own corpus test uses `tests/Shared/Golden.cs`, which already exists;
  `GoldenLog.Compare` exists for `millrace run --expect` and is pure text, so it can
  be written and reviewed on its own between the runner and the recorder.

## Global Constraints

- Target framework `net10.0` for every project. **`Millrace.Scenarios` references
  `Millrace.Core` and `Millrace.Configuration` and nothing else** — no `Millrace.Components`,
  no `Millrace.Realtime`. `Millrace.Cli` adds `Millrace.Scenarios` to its existing references.
  `Millrace.Core` still references only `Millrace.Io.Abstractions`. `Millrace.Configuration`
  references `Millrace.Core` and `Millrace.Components`. **Zero external runtime package
  references** in any shipping project: `grep -rn "PackageReference" src/` must
  print nothing. Test projects use the same test package versions as
  `tests/Millrace.Core.Tests/Millrace.Core.Tests.csproj`; `Millrace.Scenarios.Tests` adds no
  package at all.
- `Nullable` enabled, `TreatWarningsAsErrors` true, `GenerateDocumentationFile`
  true (a `<see cref>` to a type that does not exist yet is a **build error**;
  reference only types that already exist when the file is compiled),
  deterministic builds. These come from `Directory.Build.props`; a csproj
  repeats only `TargetFramework`, `ImplicitUsings` and `Nullable`.
- **Never use `System.Random`. Never use `string.GetHashCode()`** for anything
  that affects behaviour. **Never let a `Dictionary` or `HashSet` iteration
  order reach an output**: every exported list is sorted with
  `StringComparer.Ordinal` first, or is in file order.
- All formatting and parsing uses `CultureInfo.InvariantCulture`. Messages that
  embed a number use `string.Create(CultureInfo.InvariantCulture, $"...")`.
- **JSON output is deterministic**: `Utf8JsonWriter` with
  `CatalogueJson.WriterOptions`, properties written in the order the code writes
  them, arrays in file or landing order, and the final text normalised through
  `CatalogueJson.Finish` to `\n` line endings with one trailing `\n`.
- **Names.** Tag names are exactly what `millrace tags <plant>` prints
  (`CV001.Start`, `FEED.Rate`) and match ordinally. Component ids for faults are
  the flattened leaf ids (`CV001.Motor`). Fault ids and argument names are what
  the catalogue declares (`thermal-bias`, `amount`).
- Diagnostic messages are human sentences ending in a full stop. A `Fix` begins
  with an **imperative** sentence ending in a full stop and may add one more
  sentence. Descriptions in `DiagnosticInfo.Explanation` are sentences ending in
  a full stop; titles do not end in a full stop.
- **`ExitCodes.LogMismatch = 4` is new.** Exit 1 still means "the configuration
  is broken"; 4 means "it ran, and the behaviour changed".
- **Report every measurement, never widen a window.** Where an expected value
  stated in this plan disagrees with what the code produces, report the
  measurement in the task report and say which one you changed and why. A golden
  log's *content* is never invented: it is generated, read and reported.
- xUnit analyzers run under warnings-as-errors: prefer `Assert.Single`,
  `Assert.Contains`, `Assert.Empty` over `Assert.True(x.Any())` and
  `Assert.Equal(1, x.Count())`.
- Licence: MIT. Namespaces: `Millrace.Core`, `Millrace.Core.Io`, `Millrace.Scenarios`,
  `Millrace.Cli`, `Millrace.Cli.Commands`, `Millrace.Scenarios.Tests`.
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

## Decisions settled here (carry forward as rulings R50–R64)

These refine the spec where writing real code against the real APIs forced a
choice. Where one differs from the spec's wording, this plan wins and says why.

- **R50 — a scheduled write is a private `WriteEvent` that builds its own
  `TickContext`.** `ISimEvent.Apply()` takes no arguments, and `FaultEvent`
  already reaches back through `_simulation.Clock` and `_simulation.Events`.
  `WriteEvent.Apply` constructs `new TickContext(clock.TickCount,
  clock.DeltaSeconds, clock.Now, _simulation.Events)` — the same values the
  tick's own context holds, because the drain happens inside that tick — and
  hands it to `TagImage.ApplyNow`. No field holding "the current context", no
  change to `ISimEvent`.
- **R51 — the recorder lives on `TagImage` as well as on `Simulation`.**
  `AttachActionRecorder` stores it on the simulation (for `FaultEvent` and for
  the second-attach check) and passes it to `TagImage.SetActionRecorder`, which
  is where `ApplyPendingWrites` and `ApplyNow` need it. One attach, one
  recorder, two holders.
- **R52 — scenario times are integer ticks, never `TimeSpan.FromSeconds`.**
  `at` and `duration` are read as `TimeSpan.FromTicks((long)Math.Round(seconds *
  TimeSpan.TicksPerSecond))`, so "is it on a tick?" is `time.Ticks % step.Ticks
  == 0` — exact integer arithmetic, no epsilon. Seconds are written back as
  `time.Ticks / (double)TimeSpan.TicksPerSecond`. A time must be finite, `>= 0`
  and `<= 1e9` seconds; beyond that it is `MR202`.
- **R53 — `Parse` checks tick alignment only when the scenario declares
  `timeStepMs`.** The step is otherwise the plant's, which `Parse` may not read.
  `at >= duration` is always checkable and is checked in `Parse`. The runner
  repeats **both** checks against the effective step, so a `Scenario` built in
  code (by `ScenarioRecorder`, or by a test) is checked just as a parsed one is.
  `at >= 0` is **MR202** in both places, never MR203: the spec puts it in
  `Parse` as an out-of-range value, and the runner's copy — which only a
  code-built scenario can trip — uses the same code, path and wording.
- **R54 — the ISO 8601 start-time formats are duplicated, deliberately.**
  `StructureStage.TryParseStartTime` is `private` inside `Millrace.Configuration`.
  Exposing it would add public API that nothing else wants, and
  `InternalsVisibleTo` for eight lines of format strings is worse. `ScenarioLoader`
  carries its own copy with a comment naming the original. If one changes, both
  change.
- **R55 — a scenario's round trip is asserted on the rendered text, not on
  record equality.** `FaultAction` carries `IReadOnlyList<FaultArgument>`, and a
  positional record compares a list by reference, so `Parse(Write(s)).Scenario
  == s` is false for any scenario with fault arguments. The round trip is
  therefore `Write(Parse(Write(s)).Scenario!) == Write(s)`, plus field-by-field
  assertions — which is the stronger property anyway, because the rendered text
  is what a recording is stored as. (The spec has since been amended to say
  the same; R55 records why.)
- **R56 — a JSON number is an `Int64` value only when it is written as an
  integer literal.** `JsonElement.TryGetInt64` returns false for `5.0` and for
  `1e2` (measured). So `5` may drive a `Bool`-free tag of either numeric kind,
  `5.0` and `5.5` drive a `Double` tag only, and `5.0` on an `Int64` tag is
  `MR206`. `docs/scenarios.md` says so.
- **R57 — the runner checks a write's tag itself before calling `WriteAt`, and
  still catches.** Pre-checking the directory gives `MR206` a message that
  names the tag, its kind and the offending value, and a `Suggest` list of near
  names; the `try`/`catch` around every schedule call remains as the spec asks,
  so an engine exception can never escape as a crash. Fault and clear actions
  rely on the catch alone: `Simulation`'s own messages already name what exists.
- **R58 — the JSON path of a fault's `MR206` comes from the exception's
  `ParamName`.** `faultId` → `$.timeline[i].id`; `given` → `$.timeline[i].args`;
  anything else → `$.timeline[i].fault` (or `.clear`). Measured against
  `Simulation.Descriptor` and `FaultDescriptor.Resolve`.
- **R59 — `IsValid` is severity-based, as `LoadResult.IsValid` is.**
  `Diagnostics.All(d => d.Severity != DiagnosticSeverity.Error)`. 5b emits only
  errors, so this equals the spec's "no diagnostics", and a future warning will
  not stop a run.
- **R60 — `DiagnosticsReference.Render` gains two optional arguments, not two
  pages of copied prose.** The spec's `Render(string title,
  IReadOnlyList<DiagnosticInfo> codes)` is the required shape; the real
  signature is `Render(string title, IReadOnlyList<DiagnosticInfo> codes, string?
  introduction = null, string? trailer = null)`, and the existing `Render()`
  passes the configuration page's introduction and trailer verbatim, so
  `docs/configuration-diagnostics.md` stays **byte-identical**. `Millrace.Scenarios`
  supplies its own prose through `ScenarioDiagnosticsReference.Render()`. (The
  spec has since been amended to this signature; R60 records why.)
- **R61 — `tests/Millrace.Scenarios.Tests` references `Millrace.Realtime` too.** The
  spec's layout table lists `Millrace.Scenarios`, `Millrace.Components` and
  `tests/Shared`, but section 8's record-and-replay proof drives the live run
  through `CommandBus`, which lives in `Millrace.Realtime`. The reference is added in
  Task 7, where it is first needed. `src/Millrace.Scenarios` still cannot see it.
  (The spec's layout table has since been amended to list it; R61 records why.)
- **R62 — the valid plants are linked, not copied.** `Millrace.Scenarios.Tests`
  publishes `..\Millrace.Configuration.Tests\Plants\valid\*.json` into its own
  output as `Plants\*.json`. One corpus of plants, two test projects, no
  divergence. `Plants\broken.json` is local to `Millrace.Scenarios.Tests` because
  nothing else wants it.
- **R63 — the `.actual` file is written on any mismatch, in both formats, and
  `--format json` names it.** The spec's table describes the text row only.
  Writing it in JSON mode too, and adding `"actual": "<path>"` beside
  `"match": false`, costs nothing and means a script never has to guess.
- **R64 — the command class is `RunScenario`, not `Run`.** `CommandTable` names
  a command's entry point as `Commands.<Class>.Run`, and `Commands.Run.Run`
  reads as a mistake. The command word on the command line is still `run`.

## File structure

```
src/Millrace.Core/
  IActionRecorder.cs                     new — the one recording seam
  Simulation.cs                          + WriteAt/WriteIn, AttachActionRecorder, ActionRecorder, WriteEvent
  Io/TagImage.cs                         + ApplyNow, CheckWritable, SetActionRecorder
src/Millrace.Scenarios/                       new project
  Millrace.Scenarios.csproj
  ScenarioValueKind.cs  ScenarioValue.cs
  ScenarioAction.cs                      ScenarioAction, WriteAction, FaultAction, ClearAction
  Scenario.cs
  ScenarioDiagnostics.cs
  ScenarioParseResult.cs  ScenarioLoader.cs
  ScenarioJson.cs
  RunSummary.cs  ScenarioRunResult.cs  ScenarioRunner.cs
  LogComparison.cs  GoldenLog.cs
  ScenarioRecorder.cs
  ScenarioDiagnosticsReference.cs
src/Millrace.Configuration/
  DiagnosticsReference.cs                Render becomes parametric (R60)
src/Millrace.Cli/
  ExitCodes.cs                           + LogMismatch = 4
  CommandTable.cs                        + --expect, + the run row, + exit code 4 in the help
  Commands/PlantFile.cs                  + TryRead, + ReportDiagnostics (reuse, same bytes out)
  Commands/RunScenario.cs                new
tests/
  Millrace.Core.Tests/WriteAtTests.cs         new
  Millrace.Core.Tests/ActionRecorderTests.cs  new
  Millrace.Scenarios.Tests/                   new project
    Millrace.Scenarios.Tests.csproj  Corpus.cs  CorpusTests.cs
    ScenarioModelTests.cs  ScenarioDiagnosticsTests.cs  ScenarioParseTests.cs
    ScenarioJsonTests.cs  ScenarioRunnerTests.cs  GoldenLogTests.cs
    ScenarioRecorderTests.cs  RecordAndReplayTests.cs  ScenarioDiagnosticsReferenceTests.cs
    Plants/broken.json
    Scenarios/valid/*.json  Scenarios/invalid/*.json  Scenarios/unrunnable/*.json
    Golden/*.log                         generated, read, committed
  Millrace.Cli.Tests/RunCommandTests.cs       new
  Millrace.Cli.Tests/Scenarios/*.json         new
docs/
  scenarios.md                           new — the format, the `at` rule, the golden workflow
  scenario-diagnostics.md                new — generated
  architecture.md                        + "Scenarios and replay"; phases 1, 4 and 5 corrected
  README.md                              + `millrace run`, + status
Millrace.sln                                  + two projects
```

## Task map

| # | Task | Deliverable |
|---|---|---|
| 1 | `Simulation.WriteAt` | a write that lands on exactly the tick it names |
| 2 | `IActionRecorder` | one recorder sees every action that took effect |
| 3 | Scenario model and loader | the project, `Scenario`, `MR200`–`MR204`, the corpus |
| 4 | `ScenarioJson.Write` | deterministic rendering, text round trip |
| 5 | `ScenarioRunner` | plant load, scheduling with checks, golden logs |
| 6 | `GoldenLog.Compare` | where two logs diverged, with context |
| 7 | `ScenarioRecorder` | a live run recorded, written, parsed and replayed |
| 8 | `millrace run` | every row of the spec's table, exit code 4 |
| 9 | Documentation | generated reference, `docs/scenarios.md`, architecture, README |

Tasks are sequential. Suggested models, following plans 3–5a: every task here
creates or edits logic and wants the larger model; so does every reviewer, and
Task 9's prose check against the source.

---

### Task 1: `Simulation.WriteAt` — a write that lands on the tick it names

Today a write reaches the plant only through `TagImage.Write`, which queues it
for phase 1 of the *next* tick. A scenario cannot use that: "the action at 5 s"
would land at 5 s + one step, while a fault at 5 s lands at 5 s. This task adds
the scheduled write, which resolves its tag at schedule time (so a bad action
fails where it is written, not mid-run) and applies it during the phase-1 event
drain.

Order within a tick after this task, unchanged for queued writes:

1. `IO.ApplyPendingWrites` — every externally queued write, in enqueue order.
2. `DrainDueEvents` — scheduled events in `(dueTick, sequence)` order: scheduled
   writes and faults interleaved exactly as they were scheduled.

**Files:**
- Modify: `src/Millrace.Core/Io/TagImage.cs` (extract `Check`, add `CheckWritable` and `ApplyNow`)
- Modify: `src/Millrace.Core/Simulation.cs` (add `WriteAt`, `WriteIn`, private `WriteEvent`)
- Test: `tests/Millrace.Core.Tests/WriteAtTests.cs`

**Interfaces:**
- Consumes: `TagDirectory.Find(string) → TagDescriptor` (throws `KeyNotFoundException`); `TagBinding.Apply(TagValue)`; `TickContext(long tick, double dt, DateTimeOffset simTime, EventLog log)` and its `Log(source, code, message)`; `EventQueue.Schedule(long dueTick, ISimEvent) → long`; `Millrace.Core.Tests.Fakes.Thermostat` (tags `T.Setpoint` `Double` RW, `T.Enable` `Bool` RW, `T.Output` `Double` RO) and `Millrace.Core.Tests.Fakes.Fuse` (fault `Fuse.Blow` = `"blow"`, parameter `resistance`, default `1e6`).
- Produces:
  - `public long Simulation.WriteAt(TimeSpan fromStart, string tag, TagValue value)`
  - `public long Simulation.WriteIn(TimeSpan delay, string tag, TagValue value)`
  - `internal int TagImage.CheckWritable(string name, TagValue value)` — the index, or throws exactly what `Write` throws
  - `internal void TagImage.ApplyNow(int index, TagValue value, in TickContext ctx)`

- [ ] **Step 1: Write the failing test**

`tests/Millrace.Core.Tests/WriteAtTests.cs`:

```csharp
using Millrace.Core.Tests.Fakes;
using Millrace.Core.Time;
using Millrace.Io;
using Xunit;

namespace Millrace.Core.Tests;

public class WriteAtTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 6, 0, 0, TimeSpan.Zero);

    private static SimulationOptions Options => new()
    {
        Seed = 1UL,
        StartTime = Start,
        TimeStep = TimeSpan.FromMilliseconds(10),
    };

    private static Simulation Plant() => new SimulationBuilder(Options).Add(new Thermostat("T")).Build();

    private static Simulation PlantWithFuse() =>
        new SimulationBuilder(Options).Add(new Thermostat("T")).Add(new Fuse("F")).Build();

    [Fact]
    public void AnUnknownTagThrowsAtScheduleTime()
    {
        Simulation sim = Plant();

        Assert.Throws<KeyNotFoundException>(
            () => sim.WriteAt(TimeSpan.FromSeconds(1), "T.Nope", TagValue.Bool(true)));
    }

    [Fact]
    public void AReadOnlyTagThrowsAtScheduleTime()
    {
        Simulation sim = Plant();

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => sim.WriteAt(TimeSpan.FromSeconds(1), "T.Output", TagValue.Double(1.0)));
        Assert.Contains("read-only", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AKindMismatchThrowsAtScheduleTime()
    {
        Simulation sim = Plant();

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => sim.WriteAt(TimeSpan.FromSeconds(1), "T.Enable", TagValue.Double(1.0)));
        Assert.Contains("Bool tag", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheWriteLandsOnTheNamedTickWithAWriteRecord()
    {
        Simulation sim = Plant();
        sim.WriteAt(TimeSpan.FromMilliseconds(50), "T.Enable", TagValue.Bool(true));

        sim.RunFor(TimeSpan.FromMilliseconds(50));
        Assert.Empty(sim.Events.Records);          // ticks 0..4: nothing yet

        sim.Tick();                                 // tick 5 = 50 ms

        Assert.Equal(
            new[] { (5L, "T.Enable", "WRITE", "Set to true.") },
            sim.Events.Records.Select(r => (r.Tick, r.Source, r.Code, r.Message)));
        Assert.Equal(40.0, sim.IO.ReadDouble("T.Output"));
    }

    [Fact]
    public void AQueuedWriteOnTheSameTickLandsFirst()
    {
        Simulation sim = Plant();
        sim.RunFor(TimeSpan.FromMilliseconds(10));   // tick 0 has run; the clock is at tick 1

        sim.IO.WriteDouble("T.Setpoint", 30.0);      // queued: phase 1 of tick 1
        sim.WriteAt(TimeSpan.FromMilliseconds(10), "T.Enable", TagValue.Bool(true));
        sim.Tick();

        Assert.Equal(
            new[] { ("T.Setpoint", "Set to 30."), ("T.Enable", "Set to true.") },
            sim.Events.Records.Select(r => (r.Source, r.Message)));
        Assert.Equal(60.0, sim.IO.ReadDouble("T.Output"));
    }

    [Fact]
    public void AScheduledWriteAndAFaultInterleaveInScheduleOrder()
    {
        Simulation faultFirst = PlantWithFuse();
        faultFirst.InjectFaultAt(TimeSpan.FromMilliseconds(20), "F", Fuse.Blow);
        faultFirst.WriteAt(TimeSpan.FromMilliseconds(20), "T.Enable", TagValue.Bool(true));
        faultFirst.RunFor(TimeSpan.FromMilliseconds(30));

        Simulation writeFirst = PlantWithFuse();
        writeFirst.WriteAt(TimeSpan.FromMilliseconds(20), "T.Enable", TagValue.Bool(true));
        writeFirst.InjectFaultAt(TimeSpan.FromMilliseconds(20), "F", Fuse.Blow);
        writeFirst.RunFor(TimeSpan.FromMilliseconds(30));

        Assert.Equal(new[] { "FAULT", "WRITE" }, faultFirst.Events.Records.Select(r => r.Code));
        Assert.Equal(new[] { "WRITE", "FAULT" }, writeFirst.Events.Records.Select(r => r.Code));
    }

    [Fact]
    public void WriteInIsRelativeToNow()
    {
        Simulation sim = Plant();
        sim.RunFor(TimeSpan.FromMilliseconds(100));   // the clock is at tick 10

        sim.WriteIn(TimeSpan.FromMilliseconds(30), "T.Enable", TagValue.Bool(true));
        sim.RunFor(TimeSpan.FromMilliseconds(40));

        Assert.Equal(13L, Assert.Single(sim.Events.Records).Tick);
    }

    [Fact]
    public void TwoRunsWithTheSameScheduledWritesAreByteIdentical()
    {
        static string Run()
        {
            Simulation sim = Plant();
            sim.WriteAt(TimeSpan.FromMilliseconds(50), "T.Enable", TagValue.Bool(true));
            sim.WriteAt(TimeSpan.FromMilliseconds(120), "T.Setpoint", TagValue.Double(33.5));
            sim.WriteAt(TimeSpan.FromMilliseconds(200), "T.Enable", TagValue.Bool(false));
            sim.RunFor(TimeSpan.FromMilliseconds(300));
            return sim.Events.ToText();
        }

        string first = Run();
        string second = Run();

        Assert.Equal(first, second);
        Assert.Contains("T.Setpoint  WRITE  Set to 33.5.", first, StringComparison.Ordinal);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/Millrace.Core.Tests --nologo --filter FullyQualifiedName~WriteAtTests`
Expected: build FAILS — `Simulation` has no `WriteAt`.

- [ ] **Step 3: Give `TagImage` the two new seams**

In `src/Millrace.Core/Io/TagImage.cs`, replace the body of `Write(int, TagValue)`
with a call to a new private `Check`, and add `CheckWritable` and `ApplyNow`
beside it. The exception messages must not change — `IoIntegrationTests` and
`CommandBus` callers depend on them.

```csharp
    /// <inheritdoc/>
    public void Write(int index, TagValue value)
    {
        Check(index, value);
        _writes.Enqueue(new PendingWrite(index, value));
    }

    /// <inheritdoc/>
    public void Write(string name, TagValue value) => Write(Directory.Find(name).Index, value);

    /// <summary>
    /// Resolves a name and runs exactly the checks <see cref="Write(int, TagValue)"/>
    /// runs, without queueing anything. This is what lets
    /// <c>Simulation.WriteAt</c> fail at the call site rather than mid-run.
    /// </summary>
    internal int CheckWritable(string name, TagValue value)
    {
        int index = Directory.Find(name).Index;
        Check(index, value);
        return index;
    }

    /// <summary>
    /// Applies a value to a binding immediately and logs it as <c>WRITE</c>,
    /// exactly as <see cref="ApplyPendingWrites"/> does. Phase 1 only: called
    /// from the event drain, where a scheduled write lands on the tick it named.
    /// </summary>
    internal void ApplyNow(int index, TagValue value, in TickContext ctx)
    {
        TagBinding binding = _bindings[index];
        binding.Apply(value);
        ctx.Log(binding.Name, "WRITE", $"Set to {value}.");
    }

    private TagBinding Check(int index, TagValue value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _bindings.Length);

        TagBinding binding = _bindings[index];
        if (binding.Access != TagAccess.ReadWrite)
        {
            throw new InvalidOperationException($"Tag '{binding.Name}' is read-only.");
        }

        if (binding.Kind != value.Kind)
        {
            throw new InvalidOperationException(
                $"Tag '{binding.Name}' is a {binding.Kind} tag; cannot write a {value.Kind}.");
        }

        return binding;
    }
```

- [ ] **Step 4: Give `Simulation` the scheduled write**

In `src/Millrace.Core/Simulation.cs`, add the two methods immediately after
`ClearFaultIn`:

```csharp
    /// <summary>
    /// Schedules a write for phase 1 of the tick at <paramref name="fromStart"/>.
    /// The tag is resolved and checked now — unknown tag, read-only tag, kind
    /// mismatch — so a mistake fails here rather than mid-run, and the value
    /// lands on exactly the tick named: queued writes first, then scheduled
    /// events in sequence order.
    /// </summary>
    public long WriteAt(TimeSpan fromStart, string tag, TagValue value) =>
        ScheduleAt(fromStart, WriteEvent.Create(this, tag, value));

    /// <summary>Schedules a write relative to the current simulation time.</summary>
    public long WriteIn(TimeSpan delay, string tag, TagValue value) =>
        ScheduleIn(delay, WriteEvent.Create(this, tag, value));
```

and the event class immediately before `private sealed class FaultEvent`:

```csharp
    /// <summary>A resolved write, applied and logged when it lands.</summary>
    private sealed class WriteEvent : ISimEvent
    {
        private readonly Simulation _simulation;
        private readonly int _index;
        private readonly TagValue _value;

        private WriteEvent(Simulation simulation, int index, TagValue value)
        {
            _simulation = simulation;
            _index = index;
            _value = value;
        }

        public static WriteEvent Create(Simulation simulation, string tag, TagValue value)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(tag);
            return new WriteEvent(simulation, simulation.IO.CheckWritable(tag, value), value);
        }

        public void Apply()
        {
            SimulationClock clock = _simulation.Clock;
            var context = new TickContext(clock.TickCount, clock.DeltaSeconds, clock.Now, _simulation.Events);
            _simulation.IO.ApplyNow(_index, _value, in context);
        }
    }
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `dotnet test tests/Millrace.Core.Tests --nologo --filter FullyQualifiedName~WriteAtTests`
Expected: PASS, 8 tests.

- [ ] **Step 6: Run everything**

Run: `dotnet build Millrace.sln -c Release --nologo` — expect `0 Warning(s)`, `0 Error(s)`.
Run: `dotnet test Millrace.sln --nologo`
Expected: PASS, 751 + 8 = 759 tests. Report the number the runner prints.

- [ ] **Step 7: Commit**

```bash
git add src/Millrace.Core/Simulation.cs src/Millrace.Core/Io/TagImage.cs tests/Millrace.Core.Tests/WriteAtTests.cs
```

```bash
git commit -m "$(cat <<'MSG'
feat(core): schedule a write for the tick it names

A scheduled write resolves its tag at schedule time and applies it during
the phase-1 event drain, so "at 5 s" means the same for a write as it
already does for a fault.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
MSG
)"
```

---

### Task 2: `IActionRecorder` — one recorder sees every action that took effect

A recording must not care how an action arrived. `CommandBus` writes, scenario
writes, direct `InjectFaultAt` calls and test code all end in one of three
places, and this task calls the recorder from all three, stamped with the tick
the action took effect. Fault arguments are the **resolved** set — every
declared parameter present, defaults filled in — so a recording does not depend
on a descriptor's defaults staying put.

`ICommandRecorder` on `CommandBus` is unchanged and is a different concern: it
also sees *rejected* commands, which is audit, not replay.

**Files:**
- Create: `src/Millrace.Core/IActionRecorder.cs`
- Modify: `src/Millrace.Core/Io/TagImage.cs` (field, `SetActionRecorder`, two call sites)
- Modify: `src/Millrace.Core/Simulation.cs` (`ActionRecorder`, `AttachActionRecorder`, two call sites in `FaultEvent.Apply`)
- Test: `tests/Millrace.Core.Tests/ActionRecorderTests.cs`

**Interfaces:**
- Consumes: Task 1's `TagImage.ApplyNow` and `Simulation.WriteAt`; `FaultArguments` (`Count`, `this[int]`, `TryGet`, `ToString()`); `FaultArgument(string Name, double Value)`; `FaultDescriptor.Resolve(FaultArguments) → FaultArguments`.
- Produces (namespace `Millrace.Core`):
  - `public interface IActionRecorder` with
    `void Wrote(long tick, string tag, TagValue value);`
    `void Faulted(long tick, string componentId, string faultId, FaultArguments arguments);`
    `void Cleared(long tick, string componentId, string faultId);`
  - `public IActionRecorder? Simulation.ActionRecorder { get; private set; }`
  - `public void Simulation.AttachActionRecorder(IActionRecorder recorder)` — throws `InvalidOperationException` on a second attach
  - `internal void TagImage.SetActionRecorder(IActionRecorder recorder)`

- [ ] **Step 1: Write the failing test**

`tests/Millrace.Core.Tests/ActionRecorderTests.cs`:

```csharp
using Millrace.Core.Faults;
using Millrace.Core.Tests.Fakes;
using Millrace.Core.Time;
using Millrace.Io;
using Xunit;

namespace Millrace.Core.Tests;

public class ActionRecorderTests
{
    private static SimulationOptions Options => new()
    {
        Seed = 1UL,
        StartTime = new DateTimeOffset(2026, 1, 1, 6, 0, 0, TimeSpan.Zero),
        TimeStep = TimeSpan.FromMilliseconds(10),
    };

    /// <summary>Every call, flattened to a comparable tuple.</summary>
    private sealed class Spy : IActionRecorder
    {
        public List<(string Kind, long Tick, string Target, string Detail)> Calls { get; } = [];

        public void Wrote(long tick, string tag, TagValue value) =>
            Calls.Add(("wrote", tick, tag, value.ToString()));

        public void Faulted(long tick, string componentId, string faultId, FaultArguments arguments) =>
            Calls.Add(("faulted", tick, componentId, $"{faultId}({arguments})"));

        public void Cleared(long tick, string componentId, string faultId) =>
            Calls.Add(("cleared", tick, componentId, faultId));
    }

    private static (Simulation Sim, Spy Spy) Plant()
    {
        Simulation sim = new SimulationBuilder(Options).Add(new Thermostat("T")).Add(new Fuse("F")).Build();
        var spy = new Spy();
        sim.AttachActionRecorder(spy);
        return (sim, spy);
    }

    [Fact]
    public void AQueuedWriteIsRecordedWithTheTickItLanded()
    {
        (Simulation sim, Spy spy) = Plant();
        sim.RunFor(TimeSpan.FromMilliseconds(30));

        sim.IO.WriteBool("T.Enable", true);
        sim.Tick();

        Assert.Equal(("wrote", 3L, "T.Enable", "true"), Assert.Single(spy.Calls));
    }

    [Fact]
    public void AScheduledWriteIsRecordedWithTheTickItLanded()
    {
        (Simulation sim, Spy spy) = Plant();
        sim.WriteAt(TimeSpan.FromMilliseconds(70), "T.Setpoint", TagValue.Double(12.5));

        sim.RunFor(TimeSpan.FromMilliseconds(100));

        Assert.Equal(("wrote", 7L, "T.Setpoint", "12.5"), Assert.Single(spy.Calls));
    }

    [Fact]
    public void AFaultCarriesTheResolvedArgumentsAndAClearFollowsIt()
    {
        (Simulation sim, Spy spy) = Plant();
        sim.InjectFaultAt(TimeSpan.FromMilliseconds(20), "F", Fuse.Blow);
        sim.ClearFaultAt(TimeSpan.FromMilliseconds(40), "F", Fuse.Blow);

        sim.RunFor(TimeSpan.FromMilliseconds(50));

        Assert.Equal(
            new[]
            {
                ("faulted", 2L, "F", "blow(resistance=1000000)"),
                ("cleared", 4L, "F", "blow"),
            },
            spy.Calls);
    }

    [Fact]
    public void EveryPathReachesTheRecorderInLandingOrder()
    {
        (Simulation sim, Spy spy) = Plant();
        sim.RunFor(TimeSpan.FromMilliseconds(10));
        sim.IO.WriteBool("T.Enable", true);                                        // queued: tick 1
        sim.WriteAt(TimeSpan.FromMilliseconds(10), "T.Setpoint", TagValue.Double(5.0));
        sim.InjectFaultAt(TimeSpan.FromMilliseconds(10), "F", Fuse.Blow, new FaultArguments(new FaultArgument("resistance", 2.0)));

        sim.Tick();

        Assert.Equal(
            new[] { "wrote", "wrote", "faulted" },
            spy.Calls.Select(c => c.Kind));
        Assert.Equal(new[] { "T.Enable", "T.Setpoint", "F" }, spy.Calls.Select(c => c.Target));
        Assert.Equal("blow(resistance=2)", spy.Calls[2].Detail);
    }

    [Fact]
    public void ASecondAttachThrows()
    {
        (Simulation sim, _) = Plant();

        Assert.Throws<InvalidOperationException>(() => sim.AttachActionRecorder(new Spy()));
    }

    [Fact]
    public void RecordingChangesNothingAboutTheRun()
    {
        static string Run(bool record)
        {
            Simulation sim = new SimulationBuilder(Options).Add(new Thermostat("T")).Add(new Fuse("F")).Build();
            if (record)
            {
                sim.AttachActionRecorder(new Spy());
            }

            sim.WriteAt(TimeSpan.FromMilliseconds(50), "T.Enable", TagValue.Bool(true));
            sim.InjectFaultAt(TimeSpan.FromMilliseconds(70), "F", Fuse.Blow);
            sim.RunFor(TimeSpan.FromMilliseconds(100));
            return sim.Events.ToText();
        }

        Assert.Equal(Run(record: false), Run(record: true));
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/Millrace.Core.Tests --nologo --filter FullyQualifiedName~ActionRecorderTests`
Expected: build FAILS — `IActionRecorder` does not exist.

- [ ] **Step 3: Write the interface**

`src/Millrace.Core/IActionRecorder.cs`:

```csharp
using Millrace.Core.Faults;
using Millrace.Io;

namespace Millrace.Core;

/// <summary>
/// Sees every action that took effect, stamped with the tick it took effect on,
/// whichever path it came by: a scenario, a command bus, or test code. This is
/// the seam a recording is made through; what it is called from is the three
/// places an action lands, so nothing can slip past it.
/// </summary>
/// <remarks>
/// Not to be confused with <c>Millrace.Realtime.ICommandRecorder</c>, which also sees
/// commands the bus <em>rejected</em>. That is an audit trail; this is a replay.
/// </remarks>
public interface IActionRecorder
{
    /// <summary>A value reached a tag, queued or scheduled.</summary>
    void Wrote(long tick, string tag, TagValue value);

    /// <summary>A fault was applied. <paramref name="arguments"/> is the resolved set: every declared parameter, defaults filled in.</summary>
    void Faulted(long tick, string componentId, string faultId, FaultArguments arguments);

    /// <summary>A fault was cleared.</summary>
    void Cleared(long tick, string componentId, string faultId);
}
```

- [ ] **Step 4: Call it from the two write sites**

In `src/Millrace.Core/Io/TagImage.cs`, add the field beside `_writes`:

```csharp
    private IActionRecorder? _recorder;
```

add the setter beside `Prime`:

```csharp
    /// <summary>The recorder the simulation attached, or none. Set once, through <c>Simulation.AttachActionRecorder</c>.</summary>
    internal void SetActionRecorder(IActionRecorder recorder) => _recorder = recorder;
```

and add one line to each of the two apply paths:

```csharp
    internal void ApplyNow(int index, TagValue value, in TickContext ctx)
    {
        TagBinding binding = _bindings[index];
        binding.Apply(value);
        ctx.Log(binding.Name, "WRITE", $"Set to {value}.");
        _recorder?.Wrote(ctx.Tick, binding.Name, value);
    }
```

```csharp
    internal int ApplyPendingWrites(in TickContext ctx)
    {
        int budget = _writes.Count;
        int applied = 0;
        while (applied < budget && _writes.TryDequeue(out PendingWrite write))
        {
            TagBinding binding = _bindings[write.Index];
            binding.Apply(write.Value);
            ctx.Log(binding.Name, "WRITE", $"Set to {write.Value}.");
            _recorder?.Wrote(ctx.Tick, binding.Name, write.Value);
            applied++;
        }

        return applied;
    }
```

- [ ] **Step 5: Attach it, and call it from the fault site**

In `src/Millrace.Core/Simulation.cs`, add beside `AttachFrameSink`:

```csharp
    /// <summary>Where every action that takes effect is reported, or null when nothing is attached.</summary>
    public IActionRecorder? ActionRecorder { get; private set; }

    /// <summary>
    /// Attaches the one action recorder (spec 5b §6.2). May be called at any
    /// time before or during a run — a late-attached recorder sees actions from
    /// then on — but only once, as <see cref="AttachFrameSink"/> is.
    /// </summary>
    public void AttachActionRecorder(IActionRecorder recorder)
    {
        ArgumentNullException.ThrowIfNull(recorder);
        if (ActionRecorder is not null)
        {
            throw new InvalidOperationException(
                "An action recorder is already attached. The simulation records to exactly one recorder; " +
                "fan-out is the recorder's own job.");
        }

        ActionRecorder = recorder;
        IO.SetActionRecorder(recorder);
    }
```

and replace `FaultEvent.Apply` with:

```csharp
        public void Apply()
        {
            SimulationClock clock = _simulation.Clock;
            if (_arguments is null)
            {
                _target.ClearFault(_faultId);
                _simulation.Events.Record(clock.TickCount, clock.Now, _target.Id, "FAULT_CLEARED", $"{_faultId} cleared.");
                _simulation.ActionRecorder?.Cleared(clock.TickCount, _target.Id, _faultId);
                return;
            }

            _target.ApplyFault(_faultId, _arguments);
            string detail = _arguments.Count == 0 ? string.Empty : $": {_arguments}";
            _simulation.Events.Record(clock.TickCount, clock.Now, _target.Id, "FAULT", $"{_faultId} injected{detail}.");
            _simulation.ActionRecorder?.Faulted(clock.TickCount, _target.Id, _faultId, _arguments);
        }
```

- [ ] **Step 6: Run the test to verify it passes**

Run: `dotnet test tests/Millrace.Core.Tests --nologo --filter FullyQualifiedName~ActionRecorderTests`
Expected: PASS, 6 tests.

If `blow(resistance=1000000)` does not match, report the string
`FaultArguments.ToString()` actually produced and correct the test to it — the
formatting is `double.ToString(CultureInfo.InvariantCulture)`, which prints
`1000000` for `1e6`, but measure rather than assume.

- [ ] **Step 7: Run everything**

Run: `dotnet build Millrace.sln -c Release --nologo` — expect `0 Warning(s)`, `0 Error(s)`.
Run: `dotnet test Millrace.sln --nologo`
Expected: PASS, 759 + 6 = 765 tests.

- [ ] **Step 8: Commit**

```bash
git add src/Millrace.Core/IActionRecorder.cs src/Millrace.Core/Simulation.cs src/Millrace.Core/Io/TagImage.cs tests/Millrace.Core.Tests/ActionRecorderTests.cs
```

```bash
git commit -m "$(cat <<'MSG'
feat(core): report every action that takes effect to one recorder

Queued writes, scheduled writes, injections and clearances all reach
IActionRecorder with the tick they landed on and, for a fault, the
resolved arguments. This is the seam a scenario recording is made through.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
MSG
)"
```

---

### Task 3: `Millrace.Scenarios` — the model, the diagnostics and `ScenarioLoader.Parse`

The new project and everything that can be decided without a plant: well-formed
JSON, known keys, types, ranges, `duration > 0`, one shape per action with its
required companions, and — when the scenario itself declares `timeStepMs` (R53)
— tick alignment. No file I/O, no catalogue, no engine.

**The diagnostic codes** (final; they mirror `ConfigDiagnostics`' contract
exactly, same `ConfigDiagnostic` record, same "a fix is not optional" rule):

| Code | Title | When |
|---|---|---|
| MR200 | Scenario is not valid JSON | Parse failure; the message carries the parser's line and column. |
| MR201 | Unknown key | A key the format does not define, at the top level or in an action. |
| MR202 | Value missing, of the wrong type, or out of range | `plant` absent, `duration` absent or `≤ 0`, `seed` negative, `startTime` without an offset, a non-numeric `arg`, a `value` that is not a JSON bool or number. |
| MR203 | Time is not on a tick, or not before the end | `at` or `duration` is not a multiple of the step; `at ≥ duration`. |
| MR204 | Action is malformed | None or more than one of `write`/`fault`/`clear`; `value` missing on a write; `id` missing on a fault or clear; `args` on a clear; `id` or `args` on a write. |
| MR205 | Plant file is invalid | Task 5. A wrapper line with the plant path; the plant's own diagnostics follow. |
| MR206 | Action does not bind to the plant | Task 5. Unknown tag, read-only tag, kind mismatch, unknown component, unknown fault id, undeclared fault argument. |

**Files:**
- Create: `src/Millrace.Scenarios/Millrace.Scenarios.csproj`
- Create: `src/Millrace.Scenarios/ScenarioValueKind.cs`, `ScenarioValue.cs`, `ScenarioAction.cs`, `Scenario.cs`
- Create: `src/Millrace.Scenarios/ScenarioDiagnostics.cs`, `ScenarioParseResult.cs`, `ScenarioLoader.cs`
- Create: `tests/Millrace.Scenarios.Tests/Millrace.Scenarios.Tests.csproj`, `Corpus.cs`
- Test: `tests/Millrace.Scenarios.Tests/ScenarioModelTests.cs`, `ScenarioDiagnosticsTests.cs`, `ScenarioParseTests.cs`, `CorpusTests.cs`
- Create: `tests/Millrace.Scenarios.Tests/Scenarios/valid/*.json` (4 files), `Scenarios/invalid/*.json` (5 files)
- Modify: `Millrace.sln`

**Interfaces:**
- Consumes: `ConfigDiagnostic(string code, DiagnosticSeverity severity, string path, string message, string fix)` with `ToText()`; `DiagnosticInfo(string Code, string Title, string Explanation)`; `DiagnosticSeverity.Error`; `LoadOptions { TimeSpan? TimeStep; ulong? Seed; DateTimeOffset? StartTime; }` — all `Millrace.Configuration`. `Suggest.Fix(string given, IEnumerable<string> candidates, string noun)`, `Suggest.Closest`, `Suggest.List` — `Millrace.Core.Catalogue`. `FaultArgument(string Name, double Value)` — `Millrace.Core.Faults`. `TagKind` and `TagValue` — `Millrace.Io`.
- Produces (namespace `Millrace.Scenarios`):
  - `public enum ScenarioValueKind { Bool, Number, Integer }`
  - `public sealed record ScenarioValue` — `Kind`, `Boolean`, `Number`, `Integer`; factories `OfBool(bool)`, `OfNumber(double)`, `OfInteger(long)`; `TagValue? ToTagValue(TagKind kind)`; `ToString()` renders it as the file would
  - `public abstract record ScenarioAction(TimeSpan At)`
  - `public sealed record WriteAction(TimeSpan At, string Tag, ScenarioValue Value) : ScenarioAction(At)`
  - `public sealed record FaultAction(TimeSpan At, string ComponentId, string FaultId, IReadOnlyList<FaultArgument> Arguments) : ScenarioAction(At)`
  - `public sealed record ClearAction(TimeSpan At, string ComponentId, string FaultId) : ScenarioAction(At)`
  - `public sealed record Scenario(string PlantPath, ulong? Seed, DateTimeOffset? StartTime, TimeSpan? TimeStep, TimeSpan Duration, IReadOnlyList<ScenarioAction> Timeline)` with `string ResolvePlantPath(string scenarioFilePath)` and `LoadOptions ToLoadOptions()`
  - `public static class ScenarioDiagnostics` — constants `Syntax = "MR200"`, `UnknownKey = "MR201"`, `BadValue = "MR202"`, `BadTime = "MR203"`, `BadAction = "MR204"`, `PlantInvalid = "MR205"`, `DoesNotBind = "MR206"`; `All`; `internal` factories `Error`, `OffTick`, `NotBeforeTheEnd`, `Seconds`
  - `public sealed class ScenarioParseResult` — `Diagnostics`, `IsValid`, `Scenario`, `ToText()`
  - `public static class ScenarioLoader { public static ScenarioParseResult Parse(string json); }`

- [ ] **Step 1: Create the two projects and add them to the solution**

`src/Millrace.Scenarios/Millrace.Scenarios.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\Millrace.Core\Millrace.Core.csproj" />
    <ProjectReference Include="..\Millrace.Configuration\Millrace.Configuration.csproj" />
  </ItemGroup>
  <ItemGroup>
    <InternalsVisibleTo Include="Millrace.Scenarios.Tests" />
  </ItemGroup>
</Project>
```

`tests/Millrace.Scenarios.Tests/Millrace.Scenarios.Tests.csproj` — the plants are linked
from the configuration corpus (R62), so the two projects cannot drift:

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
    <Compile Include="..\Shared\Golden.cs" Link="Shared\Golden.cs" />
  </ItemGroup>

  <ItemGroup>
    <None Include="Scenarios\**\*.json" CopyToOutputDirectory="PreserveNewest" />
    <None Include="Plants\*.json" CopyToOutputDirectory="PreserveNewest" />
    <None Include="..\Millrace.Configuration.Tests\Plants\valid\*.json"
          Link="Plants\%(Filename)%(Extension)" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\Millrace.Scenarios\Millrace.Scenarios.csproj" />
    <ProjectReference Include="..\..\src\Millrace.Components\Millrace.Components.csproj" />
  </ItemGroup>

</Project>
```

```bash
dotnet sln Millrace.sln add src/Millrace.Scenarios/Millrace.Scenarios.csproj --solution-folder src
```

```bash
dotnet sln Millrace.sln add tests/Millrace.Scenarios.Tests/Millrace.Scenarios.Tests.csproj --solution-folder tests
```

- [ ] **Step 2: Write the failing model and diagnostics tests**

`tests/Millrace.Scenarios.Tests/ScenarioModelTests.cs`:

```csharp
using Millrace.Configuration;
using Millrace.Core.Faults;
using Millrace.Io;

namespace Millrace.Scenarios.Tests;

public class ScenarioModelTests
{
    private static Scenario Minimal(params ScenarioAction[] timeline) => new(
        "conveyor-line.json", 42UL, new DateTimeOffset(2026, 1, 1, 6, 0, 0, TimeSpan.Zero),
        TimeSpan.FromMilliseconds(10), TimeSpan.FromSeconds(120), timeline);

    [Fact]
    public void ThePlantPathIsResolvedBesideTheScenarioFile()
    {
        Scenario scenario = Minimal();

        string resolved = scenario.ResolvePlantPath(Path.Combine("/plants", "line", "run.json"));

        Assert.Equal(Path.GetFullPath(Path.Combine("/plants", "line", "conveyor-line.json")), resolved);
    }

    [Fact]
    public void ARelativePlantPathMayClimbOut()
    {
        var scenario = Minimal() with { PlantPath = Path.Combine("..", "shared", "plant.json") };

        string resolved = scenario.ResolvePlantPath(Path.Combine("/plants", "line", "run.json"));

        Assert.Equal(Path.GetFullPath(Path.Combine("/plants", "shared", "plant.json")), resolved);
    }

    [Fact]
    public void AnAbsolutePlantPathIsUsedAsItStands()
    {
        string absolute = Path.GetFullPath(Path.Combine("/elsewhere", "plant.json"));
        var scenario = Minimal() with { PlantPath = absolute };

        Assert.Equal(absolute, scenario.ResolvePlantPath(Path.Combine("/plants", "run.json")));
    }

    [Fact]
    public void TheOverridesBecomeLoadOptions()
    {
        LoadOptions options = Minimal().ToLoadOptions();

        Assert.Equal(42UL, options.Seed);
        Assert.Equal(TimeSpan.FromMilliseconds(10), options.TimeStep);
        Assert.Equal(new DateTimeOffset(2026, 1, 1, 6, 0, 0, TimeSpan.Zero), options.StartTime);
    }

    [Fact]
    public void AnAbsentOverrideStaysAbsentSoThePlantDecides()
    {
        var scenario = new Scenario("p.json", null, null, null, TimeSpan.FromSeconds(1), []);

        LoadOptions options = scenario.ToLoadOptions();

        Assert.Null(options.Seed);
        Assert.Null(options.TimeStep);
        Assert.Null(options.StartTime);
    }

    [Theory]
    [InlineData(TagKind.Bool, true)]
    [InlineData(TagKind.Double, false)]
    [InlineData(TagKind.Int64, false)]
    public void ABoolValueFitsOnlyABoolTag(TagKind kind, bool fits)
    {
        Assert.Equal(fits, ScenarioValue.OfBool(true).ToTagValue(kind) is not null);
    }

    [Theory]
    [InlineData(TagKind.Bool, false)]
    [InlineData(TagKind.Double, true)]
    [InlineData(TagKind.Int64, false)]
    public void AFractionalNumberFitsOnlyADoubleTag(TagKind kind, bool fits)
    {
        Assert.Equal(fits, ScenarioValue.OfNumber(1.5).ToTagValue(kind) is not null);
    }

    [Theory]
    [InlineData(TagKind.Bool, false)]
    [InlineData(TagKind.Double, true)]
    [InlineData(TagKind.Int64, true)]
    public void AWholeNumberFitsEitherNumericTag(TagKind kind, bool fits)
    {
        Assert.Equal(fits, ScenarioValue.OfInteger(7L).ToTagValue(kind) is not null);
    }

    [Fact]
    public void AValueCarriesItsPayloadThrough()
    {
        Assert.True(ScenarioValue.OfBool(true).ToTagValue(TagKind.Bool)!.Value.AsBool);
        Assert.Equal(1.5, ScenarioValue.OfNumber(1.5).ToTagValue(TagKind.Double)!.Value.AsDouble);
        Assert.Equal(7.0, ScenarioValue.OfInteger(7L).ToTagValue(TagKind.Double)!.Value.AsDouble);
        Assert.Equal(7L, ScenarioValue.OfInteger(7L).ToTagValue(TagKind.Int64)!.Value.AsInt64);
    }

    [Fact]
    public void AValuePrintsAsTheFileWouldWriteIt()
    {
        Assert.Equal("true", ScenarioValue.OfBool(true).ToString());
        Assert.Equal("false", ScenarioValue.OfBool(false).ToString());
        Assert.Equal("1.5", ScenarioValue.OfNumber(1.5).ToString());
        Assert.Equal("7", ScenarioValue.OfInteger(7L).ToString());
    }

    [Fact]
    public void AnActionKnowsItsOwnTimeWhateverItsShape()
    {
        ScenarioAction[] actions =
        [
            new WriteAction(TimeSpan.FromSeconds(5), "CV001.Start", ScenarioValue.OfBool(true)),
            new FaultAction(TimeSpan.FromSeconds(30), "CV001.Motor", "thermal-bias", [new FaultArgument("amount", 0.8)]),
            new ClearAction(TimeSpan.FromSeconds(60.5), "CV001.Motor", "thermal-bias"),
        ];

        Assert.Equal(
            new[] { 5.0, 30.0, 60.5 },
            actions.Select(a => a.At.TotalSeconds));
    }
}
```

`tests/Millrace.Scenarios.Tests/ScenarioDiagnosticsTests.cs`:

```csharp
using Millrace.Configuration;

namespace Millrace.Scenarios.Tests;

public class ScenarioDiagnosticsTests
{
    [Fact]
    public void TheTableListsEveryCodeOnceInOrder()
    {
        string[] codes = ScenarioDiagnostics.All.Select(d => d.Code).ToArray();

        Assert.Equal(7, codes.Length);
        Assert.Equal("MR200", codes[0]);
        Assert.Equal("MR206", codes[^1]);
        Assert.Equal(codes.Order(StringComparer.Ordinal), codes);
        Assert.Equal(codes.Length, codes.Distinct(StringComparer.Ordinal).Count());
        Assert.All(ScenarioDiagnostics.All, d =>
        {
            Assert.EndsWith(".", d.Explanation, StringComparison.Ordinal);
            Assert.False(d.Title.EndsWith('.'));
        });
    }

    [Fact]
    public void ScenarioCodesDoNotCollideWithConfigurationCodes()
    {
        var configuration = ConfigDiagnostics.All.Select(d => d.Code).ToHashSet(StringComparer.Ordinal);

        Assert.All(ScenarioDiagnostics.All, d => Assert.DoesNotContain(d.Code, configuration));
    }
}
```

Run: `dotnet test tests/Millrace.Scenarios.Tests --nologo`
Expected: build FAILS — `Scenario` does not exist.

- [ ] **Step 3: Write the model**

`src/Millrace.Scenarios/ScenarioValueKind.cs`:

```csharp
namespace Millrace.Scenarios;

/// <summary>Which JSON shape a write's value had. The tag's kind is not known until scheduling.</summary>
public enum ScenarioValueKind
{
    /// <summary>A JSON <c>true</c> or <c>false</c>.</summary>
    Bool,

    /// <summary>A JSON number that is not an integer literal: <c>1.5</c>, <c>5.0</c>, <c>1e2</c>.</summary>
    Number,

    /// <summary>A JSON integer literal: <c>7</c>.</summary>
    Integer,
}
```

`src/Millrace.Scenarios/ScenarioValue.cs`:

```csharp
using System.Globalization;
using Millrace.Io;

namespace Millrace.Scenarios;

/// <summary>
/// A write's value exactly as the file gave it. It stays in this form until the
/// tag's kind is known, at scheduling: a scenario is checked against a plant,
/// not guessed at while parsing.
/// </summary>
public sealed record ScenarioValue
{
    private ScenarioValue(ScenarioValueKind kind, bool boolean, double number, long integer)
    {
        Kind = kind;
        Boolean = boolean;
        Number = number;
        Integer = integer;
    }

    /// <summary>Which of the three payloads is meaningful.</summary>
    public ScenarioValueKind Kind { get; }

    /// <summary>The payload of a <see cref="ScenarioValueKind.Bool"/> value.</summary>
    public bool Boolean { get; }

    /// <summary>The payload of a numeric value; for an integer, the same number as a double.</summary>
    public double Number { get; }

    /// <summary>The payload of a <see cref="ScenarioValueKind.Integer"/> value.</summary>
    public long Integer { get; }

    /// <summary>A JSON <c>true</c> or <c>false</c>.</summary>
    public static ScenarioValue OfBool(bool value) => new(ScenarioValueKind.Bool, value, 0.0, 0L);

    /// <summary>A JSON number that is not an integer literal.</summary>
    public static ScenarioValue OfNumber(double value) => new(ScenarioValueKind.Number, false, value, 0L);

    /// <summary>A JSON integer literal, which may drive an Int64 tag or a Double one.</summary>
    public static ScenarioValue OfInteger(long value) => new(ScenarioValueKind.Integer, false, value, value);

    /// <summary>The tag value for a tag of <paramref name="kind"/>, or null when this value cannot be one.</summary>
    public TagValue? ToTagValue(TagKind kind) => (kind, Kind) switch
    {
        (TagKind.Bool, ScenarioValueKind.Bool) => TagValue.Bool(Boolean),
        (TagKind.Double, ScenarioValueKind.Number or ScenarioValueKind.Integer) => TagValue.Double(Number),
        (TagKind.Int64, ScenarioValueKind.Integer) => TagValue.Int64(Integer),
        _ => null,
    };

    /// <summary>What the file said, for a diagnostic: <c>true</c>, <c>1.5</c>, <c>7</c>.</summary>
    public override string ToString() => Kind switch
    {
        ScenarioValueKind.Bool => Boolean ? "true" : "false",
        ScenarioValueKind.Integer => Integer.ToString(CultureInfo.InvariantCulture),
        _ => Number.ToString("R", CultureInfo.InvariantCulture),
    };
}
```

`src/Millrace.Scenarios/ScenarioAction.cs`:

```csharp
using Millrace.Core.Faults;

namespace Millrace.Scenarios;

/// <summary>
/// One thing a scenario does, and when. <c>At</c> is the tick at which the plant
/// sees it, for every shape alike — that is the whole point of
/// <c>Simulation.WriteAt</c>.
/// </summary>
/// <param name="At">Time from the start of the run.</param>
public abstract record ScenarioAction(TimeSpan At);

/// <summary>A value reaches a tag.</summary>
public sealed record WriteAction(TimeSpan At, string Tag, ScenarioValue Value) : ScenarioAction(At);

/// <summary>A fault is injected into a component. <paramref name="Arguments"/> may omit any the descriptor defaults.</summary>
public sealed record FaultAction(
    TimeSpan At,
    string ComponentId,
    string FaultId,
    IReadOnlyList<FaultArgument> Arguments) : ScenarioAction(At);

/// <summary>A fault is cleared.</summary>
public sealed record ClearAction(TimeSpan At, string ComponentId, string FaultId) : ScenarioAction(At);
```

`src/Millrace.Scenarios/Scenario.cs`:

```csharp
using Millrace.Configuration;

namespace Millrace.Scenarios;

/// <summary>
/// A run, as a file describes it: which plant, how the engine is set up, how
/// long, and what happens. Immutable and pure — resolving the plant path is the
/// only thing here that knows the file system exists, and it only does string
/// arithmetic.
/// </summary>
/// <param name="PlantPath">The plant file, relative to the scenario file that named it, or absolute.</param>
/// <param name="Seed">Overrides the plant's <c>defaults.seed</c>.</param>
/// <param name="StartTime">Overrides the plant's <c>defaults.startTime</c>.</param>
/// <param name="TimeStep">Overrides the plant's <c>defaults.timeStepMs</c>.</param>
/// <param name="Duration">How long to run, from the start. A whole number of ticks.</param>
/// <param name="Timeline">The actions, in file order. Actions on one tick fire in this order.</param>
public sealed record Scenario(
    string PlantPath,
    ulong? Seed,
    DateTimeOffset? StartTime,
    TimeSpan? TimeStep,
    TimeSpan Duration,
    IReadOnlyList<ScenarioAction> Timeline)
{
    /// <summary>
    /// The plant's absolute path. A scenario is a sibling of its plant, so a
    /// directory that moves keeps working.
    /// </summary>
    public string ResolvePlantPath(string scenarioFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scenarioFilePath);
        string directory = Path.GetDirectoryName(Path.GetFullPath(scenarioFilePath)) ?? string.Empty;
        return Path.GetFullPath(Path.Combine(directory, PlantPath));
    }

    /// <summary>The overrides, for <c>PlantLoader.Load</c>. Absent here means the plant decides.</summary>
    public LoadOptions ToLoadOptions() => new()
    {
        Seed = Seed,
        StartTime = StartTime,
        TimeStep = TimeStep,
    };
}
```

- [ ] **Step 4: Write the diagnostics table**

`src/Millrace.Scenarios/ScenarioDiagnostics.cs`:

```csharp
using System.Globalization;
using Millrace.Configuration;

namespace Millrace.Scenarios;

/// <summary>
/// Every scenario diagnostic code. The reference page in docs/ is generated
/// from <see cref="All"/>, so the page cannot drift from the codes.
/// </summary>
public static class ScenarioDiagnostics
{
    /// <summary>The scenario file could not be parsed.</summary>
    public const string Syntax = "MR200";

    /// <summary>A key the format does not define.</summary>
    public const string UnknownKey = "MR201";

    /// <summary>A value is missing, of the wrong type, or out of range.</summary>
    public const string BadValue = "MR202";

    /// <summary>A time is not on a tick, or an action is not before the end of the run.</summary>
    public const string BadTime = "MR203";

    /// <summary>An action does not have exactly one shape with its required companions.</summary>
    public const string BadAction = "MR204";

    /// <summary>The plant the scenario names has errors of its own.</summary>
    public const string PlantInvalid = "MR205";

    /// <summary>An action names something the plant does not have.</summary>
    public const string DoesNotBind = "MR206";

    /// <summary>The codes, in order, with what each means.</summary>
    public static IReadOnlyList<DiagnosticInfo> All { get; } =
    [
        new(Syntax, "Scenario is not valid JSON",
            "The text could not be parsed. Comments and trailing commas are allowed; everything else must be strict JSON. The message gives the line and column."),
        new(UnknownKey, "Unknown key",
            "An object has a key the format does not define. Keys match exactly, including case. Nothing is ignored silently, so a misspelt key cannot quietly do nothing."),
        new(BadValue, "Value missing, of the wrong type, or out of range",
            "A required value is absent, has the wrong JSON type, or is outside its range: no \"plant\", no \"duration\", a duration of zero or less, a negative seed, a start time without an offset, a fault argument that is not a number, or a write value that is neither a JSON boolean nor a number."),
        new(BadTime, "Time is not on a tick, or not before the end",
            "A time is not a whole number of time steps, or an action is scheduled at or after the end of the run. A run of 120 s at 10 ms runs ticks 0 to 11999, so an action at 120 s would never fire."),
        new(BadAction, "Action is malformed",
            "An action does not have exactly one of \"write\", \"fault\" and \"clear\", or lacks a companion that shape requires, or carries one that belongs to another shape."),
        new(PlantInvalid, "Plant file is invalid",
            "The plant the scenario names has errors of its own. This diagnostic names the plant; the plant's own diagnostics follow it unchanged, with their codes, their paths and their fixes. A plant file that cannot be read at all is the command line's exit code 3, not a diagnostic."),
        new(DoesNotBind, "Action does not bind to the plant",
            "An action names a tag, a component, a fault or a fault argument the plant does not have, or writes a tag that is read-only or of another kind. Every action is bound before tick 0, so a bad scenario never produces a partial log."),
    ];

    internal static ConfigDiagnostic Error(string code, string path, string message, string fix) =>
        new(code, DiagnosticSeverity.Error, path, message, fix);

    /// <summary>A time in whole seconds and fractions, for a message. Exact: the tick count divided by a constant.</summary>
    internal static double Seconds(TimeSpan time) => time.Ticks / (double)TimeSpan.TicksPerSecond;

    /// <summary><paramref name="what"/> is a capitalised noun phrase: "The duration", "The action time".</summary>
    internal static ConfigDiagnostic OffTick(string path, string what, TimeSpan time, TimeSpan step) => Error(
        BadTime,
        path,
        string.Create(CultureInfo.InvariantCulture, $"{what} {Seconds(time)} s is not a whole number of {step.TotalMilliseconds} ms steps."),
        "Move it to a multiple of the time step, or change the step.");

    internal static ConfigDiagnostic NotBeforeTheEnd(string path, TimeSpan at, TimeSpan duration) => Error(
        BadTime,
        path,
        string.Create(CultureInfo.InvariantCulture, $"An action at {Seconds(at)} s is not before the end of the run at {Seconds(duration)} s."),
        "Move the action earlier, or extend \"duration\"; the last tick of the run starts one step before the end.");
}
```

- [ ] **Step 5: Run the model and diagnostics tests**

Run: `dotnet test tests/Millrace.Scenarios.Tests --nologo`
Expected: PASS, 19 tests (8 facts + 3 theories of 3 rows in `ScenarioModelTests`, 2 facts in `ScenarioDiagnosticsTests`). Report the number the runner prints.

- [ ] **Step 6: Write the failing parser tests**

`tests/Millrace.Scenarios.Tests/ScenarioParseTests.cs`:

```csharp
using Millrace.Configuration;
using Millrace.Core.Faults;

namespace Millrace.Scenarios.Tests;

public class ScenarioParseTests
{
    /// <summary>A scenario with one action, built from the pieces a test wants to vary.</summary>
    private static string Json(string body) => $"{{ \"plant\": \"p.json\", \"duration\": 10, {body} }}";

    private static ConfigDiagnostic Only(string json) => Assert.Single(ScenarioLoader.Parse(json).Diagnostics);

    [Fact]
    public void TheSpecsExampleParses()
    {
        ScenarioParseResult result = ScenarioLoader.Parse("""
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
            """);

        Assert.Empty(result.Diagnostics);
        Assert.True(result.IsValid);
        Scenario scenario = result.Scenario!;
        Assert.Equal("conveyor-line.json", scenario.PlantPath);
        Assert.Equal(42UL, scenario.Seed);
        Assert.Equal(new DateTimeOffset(2026, 1, 1, 6, 0, 0, TimeSpan.Zero), scenario.StartTime);
        Assert.Equal(TimeSpan.FromMilliseconds(10), scenario.TimeStep);
        Assert.Equal(TimeSpan.FromSeconds(120), scenario.Duration);

        var write = Assert.IsType<WriteAction>(scenario.Timeline[0]);
        Assert.Equal((TimeSpan.FromSeconds(5), "CV001.Start"), (write.At, write.Tag));
        Assert.Equal(ScenarioValue.OfBool(true), write.Value);

        var fault = Assert.IsType<FaultAction>(scenario.Timeline[1]);
        Assert.Equal((TimeSpan.FromSeconds(30), "CV001.Motor", "thermal-bias"), (fault.At, fault.ComponentId, fault.FaultId));
        Assert.Equal(new[] { new FaultArgument("amount", 0.8) }, fault.Arguments);

        var clear = Assert.IsType<ClearAction>(scenario.Timeline[2]);
        Assert.Equal((TimeSpan.FromMilliseconds(60500), "CV001.Motor", "thermal-bias"), (clear.At, clear.ComponentId, clear.FaultId));
    }

    [Fact]
    public void TheSmallestScenarioIsAPlantAndADuration()
    {
        ScenarioParseResult result = ScenarioLoader.Parse("""{ "plant": "p.json", "duration": 1 }""");

        Assert.Empty(result.Diagnostics);
        Scenario scenario = result.Scenario!;
        Assert.Null(scenario.Seed);
        Assert.Null(scenario.StartTime);
        Assert.Null(scenario.TimeStep);
        Assert.Empty(scenario.Timeline);
    }

    [Fact]
    public void BrokenJsonIsMr200WithAPosition()
    {
        ConfigDiagnostic diagnostic = Only("""{ "plant": "p.json", "duration": """);

        Assert.Equal(("MR200", "$"), (diagnostic.Code, diagnostic.Path));
        Assert.StartsWith("The scenario is not valid JSON at line ", diagnostic.Message, StringComparison.Ordinal);
        Assert.StartsWith("Correct the JSON at that position.", diagnostic.Fix, StringComparison.Ordinal);
    }

    [Fact]
    public void ANonObjectIsMr202()
    {
        ConfigDiagnostic diagnostic = Only("[ ]");

        Assert.Equal(("MR202", "$", "A scenario file is a JSON object."), (diagnostic.Code, diagnostic.Path, diagnostic.Message));
    }

    [Fact]
    public void AnUnknownTopLevelKeyIsMr201AndNamesTheNearest()
    {
        ConfigDiagnostic diagnostic = Only("""{ "plant": "p.json", "duration": 10, "seedd": 3 }""");

        Assert.Equal(("MR201", "$.seedd"), (diagnostic.Code, diagnostic.Path));
        Assert.Equal("'seedd' is not a key a scenario has.", diagnostic.Message);
        Assert.Contains("'seed' is closest.", diagnostic.Fix, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnknownActionKeyIsMr201()
    {
        ConfigDiagnostic diagnostic = Only(Json("""
            "timeline": [ { "at": 1, "write": "T", "value": true, "vaue": 2 } ]
            """));

        Assert.Equal(("MR201", "$.timeline[0].vaue"), (diagnostic.Code, diagnostic.Path));
        Assert.Contains("'value' is closest.", diagnostic.Fix, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{ "duration": 10 }""", "$.plant", "A scenario needs a \"plant\": the path of the plant file, relative to this scenario.")]
    [InlineData("""{ "plant": 3, "duration": 10 }""", "$.plant", "A scenario needs a \"plant\": the path of the plant file, relative to this scenario.")]
    [InlineData("""{ "plant": "p.json" }""", "$.duration", "A scenario needs a \"duration\": how many seconds to run.")]
    [InlineData("""{ "plant": "p.json", "duration": 0 }""", "$.duration", "\"duration\" must be a number of seconds greater than zero and at most 1000000000.")]
    [InlineData("""{ "plant": "p.json", "duration": -1 }""", "$.duration", "\"duration\" must be a number of seconds greater than zero and at most 1000000000.")]
    [InlineData("""{ "plant": "p.json", "duration": 10, "seed": -1 }""", "$.seed", "\"seed\" must be a whole number, zero or greater.")]
    [InlineData("""{ "plant": "p.json", "duration": 10, "timeStepMs": 0 }""", "$.timeStepMs", "\"timeStepMs\" must be a number greater than zero.")]
    [InlineData("""{ "plant": "p.json", "duration": 10, "startTime": "2026-01-01T06:00:00" }""", "$.startTime", "\"startTime\" must be an ISO 8601 date and time with an offset.")]
    [InlineData("""{ "plant": "p.json", "duration": 10, "timeline": 3 }""", "$.timeline", "\"timeline\" must be an array of actions.")]
    public void ABadTopLevelValueIsMr202(string json, string path, string message)
    {
        ConfigDiagnostic diagnostic = Only(json);

        Assert.Equal(("MR202", path, message), (diagnostic.Code, diagnostic.Path, diagnostic.Message));
        Assert.EndsWith(".", diagnostic.Fix, StringComparison.Ordinal);
    }

    [Fact]
    public void AnOffsetStartTimeIsKept()
    {
        ScenarioParseResult result = ScenarioLoader.Parse(
            """{ "plant": "p.json", "duration": 10, "startTime": "2026-03-01T08:00:00+02:00" }""");

        Assert.Empty(result.Diagnostics);
        Assert.Equal(new DateTimeOffset(2026, 3, 1, 8, 0, 0, TimeSpan.FromHours(2)), result.Scenario!.StartTime);
    }

    [Theory]
    [InlineData("""{ "at": 1 }""", "$.timeline[0]", "An action needs exactly one of \"write\", \"fault\" or \"clear\"; this one has none.")]
    [InlineData("""{ "at": 1, "write": "T", "value": true, "clear": "C", "id": "f" }""", "$.timeline[0]", "An action needs exactly one of \"write\", \"fault\" or \"clear\"; this one has 2.")]
    [InlineData("""{ "at": 1, "write": "T" }""", "$.timeline[0]", "A write action needs a \"value\".")]
    [InlineData("""{ "at": 1, "fault": "C" }""", "$.timeline[0]", "A fault action needs an \"id\" naming the fault.")]
    [InlineData("""{ "at": 1, "clear": "C" }""", "$.timeline[0]", "A clear action needs an \"id\" naming the fault.")]
    [InlineData("""{ "at": 1, "clear": "C", "id": "f", "args": { "x": 1 } }""", "$.timeline[0].args", "A clear action takes no \"args\".")]
    [InlineData("""{ "at": 1, "write": "T", "value": true, "id": "f" }""", "$.timeline[0].id", "\"id\" belongs to a fault or a clear, not to a write.")]
    [InlineData("""{ "at": 1, "write": "T", "value": true, "args": { "x": 1 } }""", "$.timeline[0].args", "\"args\" belongs to a fault, not to a write.")]
    [InlineData("""3""", "$.timeline[0]", "A timeline entry is an object.")]
    public void AMalformedActionIsMr204(string action, string path, string message)
    {
        ConfigDiagnostic diagnostic = Only(Json($"\"timeline\": [ {action} ]"));

        Assert.Equal(("MR204", path, message), (diagnostic.Code, diagnostic.Path, diagnostic.Message));
        Assert.EndsWith(".", diagnostic.Fix, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{ "write": "T", "value": true }""", "$.timeline[0].at", "An action needs an \"at\": its time in seconds from the start of the run.")]
    [InlineData("""{ "at": "soon", "write": "T", "value": true }""", "$.timeline[0].at", "\"at\" must be a number of seconds, zero or greater and at most 1000000000.")]
    [InlineData("""{ "at": -1, "write": "T", "value": true }""", "$.timeline[0].at", "\"at\" must be a number of seconds, zero or greater and at most 1000000000.")]
    [InlineData("""{ "at": 1, "write": "", "value": true }""", "$.timeline[0].write", "\"write\" must be the name of a tag.")]
    [InlineData("""{ "at": 1, "write": "T", "value": "on" }""", "$.timeline[0].value", "\"value\" must be true, false or a number.")]
    [InlineData("""{ "at": 1, "fault": 3, "id": "f" }""", "$.timeline[0].fault", "\"fault\" must be the name of a component.")]
    [InlineData("""{ "at": 1, "clear": 3, "id": "f" }""", "$.timeline[0].clear", "\"clear\" must be the name of a component.")]
    [InlineData("""{ "at": 1, "fault": "C", "id": 3 }""", "$.timeline[0].id", "\"id\" must be the name of a fault.")]
    [InlineData("""{ "at": 1, "fault": "C", "id": "f", "args": 3 }""", "$.timeline[0].args", "\"args\" must be an object of numbers.")]
    [InlineData("""{ "at": 1, "fault": "C", "id": "f", "args": { "amount": "lots" } }""", "$.timeline[0].args.amount", "Fault argument 'amount' must be a number.")]
    public void ABadActionValueIsMr202(string action, string path, string message)
    {
        ConfigDiagnostic diagnostic = Only(Json($"\"timeline\": [ {action} ]"));

        Assert.Equal(("MR202", path, message), (diagnostic.Code, diagnostic.Path, diagnostic.Message));
        Assert.EndsWith(".", diagnostic.Fix, StringComparison.Ordinal);
    }

    [Fact]
    public void AnActionAtTheEndOfTheRunIsMr203()
    {
        ConfigDiagnostic diagnostic = Only(Json("""
            "timeline": [ { "at": 10, "write": "T", "value": true } ]
            """));

        Assert.Equal(("MR203", "$.timeline[0].at"), (diagnostic.Code, diagnostic.Path));
        Assert.Equal("An action at 10 s is not before the end of the run at 10 s.", diagnostic.Message);
        Assert.StartsWith("Move the action earlier", diagnostic.Fix, StringComparison.Ordinal);
    }

    [Fact]
    public void ADeclaredStepMakesAnOffTickActionMr203()
    {
        ConfigDiagnostic diagnostic = Only("""
            {
              "plant": "p.json", "timeStepMs": 10, "duration": 10,
              "timeline": [ { "at": 5.005, "write": "T", "value": true } ]
            }
            """);

        Assert.Equal(("MR203", "$.timeline[0].at"), (diagnostic.Code, diagnostic.Path));
        Assert.Equal("The action time 5.005 s is not a whole number of 10 ms steps.", diagnostic.Message);
    }

    [Fact]
    public void ADeclaredStepMakesAnOffTickDurationMr203()
    {
        ConfigDiagnostic diagnostic = Only("""{ "plant": "p.json", "timeStepMs": 100, "duration": 10.55 }""");

        Assert.Equal(("MR203", "$.duration"), (diagnostic.Code, diagnostic.Path));
        Assert.Equal("The duration 10.55 s is not a whole number of 100 ms steps.", diagnostic.Message);
    }

    [Fact]
    public void WithoutADeclaredStepTickAlignmentWaitsForThePlant()
    {
        ScenarioParseResult result = ScenarioLoader.Parse(Json("""
            "timeline": [ { "at": 5.005, "write": "T", "value": true } ]
            """));

        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void EveryProblemIsReportedNotJustTheFirst()
    {
        ScenarioParseResult result = ScenarioLoader.Parse("""
            {
              "duration": 0,
              "timeline": [
                { "at": 1 },
                { "at": 2, "write": "T", "value": "on" }
              ]
            }
            """);

        Assert.Equal(
            new[] { "MR202", "MR202", "MR204", "MR202" },
            result.Diagnostics.Select(d => d.Code));
        Assert.Equal(
            new[] { "$.plant", "$.duration", "$.timeline[0]", "$.timeline[1].value" },
            result.Diagnostics.Select(d => d.Path));
        Assert.Null(result.Scenario);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void NumbersKeepTheShapeTheFileWroteThem()
    {
        ScenarioParseResult result = ScenarioLoader.Parse(Json("""
            "timeline": [
              { "at": 1, "write": "A", "value": 7 },
              { "at": 2, "write": "B", "value": 7.0 },
              { "at": 3, "write": "C", "value": 7.5 }
            ]
            """));

        Assert.Empty(result.Diagnostics);
        Assert.Equal(
            new[] { ScenarioValueKind.Integer, ScenarioValueKind.Number, ScenarioValueKind.Number },
            result.Scenario!.Timeline.Cast<WriteAction>().Select(w => w.Value.Kind));
    }

    [Fact]
    public void CommentsAndTrailingCommasAreAllowed()
    {
        ScenarioParseResult result = ScenarioLoader.Parse("""
            {
              // the smallest useful scenario
              "plant": "p.json",
              "duration": 1,
            }
            """);

        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void TheTextOfAResultIsTheDiagnosticsBlankLineSeparated()
    {
        ScenarioParseResult result = ScenarioLoader.Parse("""{ "plant": "p.json" }""");

        Assert.Equal(Assert.Single(result.Diagnostics).ToText() + "\n", result.ToText());
        Assert.Equal(string.Empty, ScenarioLoader.Parse("""{ "plant": "p.json", "duration": 1 }""").ToText());
    }
}
```

Run: `dotnet test tests/Millrace.Scenarios.Tests --nologo --filter FullyQualifiedName~ScenarioParseTests`
Expected: build FAILS — `ScenarioLoader` does not exist.

- [ ] **Step 7: Write the parse result**

`src/Millrace.Scenarios/ScenarioParseResult.cs`:

```csharp
using Millrace.Configuration;

namespace Millrace.Scenarios;

/// <summary>
/// What a scenario file came to: the scenario, or every structural reason it is
/// not one. The scenario is null whenever there is an error, so a caller cannot
/// half-run a broken file.
/// </summary>
public sealed class ScenarioParseResult
{
    internal ScenarioParseResult(IReadOnlyList<ConfigDiagnostic> diagnostics, Scenario? scenario)
    {
        Diagnostics = diagnostics;
        Scenario = scenario;
    }

    /// <summary>Every problem found, in a fixed order: unknown keys, then the header, then the timeline.</summary>
    public IReadOnlyList<ConfigDiagnostic> Diagnostics { get; }

    public bool IsValid => Diagnostics.All(d => d.Severity != DiagnosticSeverity.Error);

    /// <summary>The parsed scenario, or null when anything is wrong.</summary>
    public Scenario? Scenario { get; }

    /// <summary>Every diagnostic, blank-line separated, ending in a newline; empty when there are none.</summary>
    public string ToText() =>
        Diagnostics.Count == 0 ? string.Empty : string.Join("\n\n", Diagnostics.Select(d => d.ToText())) + "\n";
}
```

- [ ] **Step 8: Write the loader**

`src/Millrace.Scenarios/ScenarioLoader.cs`:

```csharp
using System.Globalization;
using System.Text.Json;
using Millrace.Configuration;
using Millrace.Core.Catalogue;
using Millrace.Core.Faults;

namespace Millrace.Scenarios;

/// <summary>
/// Turns scenario text into a <see cref="Scenario"/>, or into every structural
/// reason it cannot be one. Pure: no file system, no catalogue, no plant.
/// Whatever needs the plant — does that tag exist, is that component a fault
/// target — is <c>ScenarioRunner</c>'s job.
/// </summary>
public static class ScenarioLoader
{
    /// <summary>The largest time a scenario may name, in seconds: about 31 years, and far inside <see cref="TimeSpan"/>.</summary>
    internal const double MaxSeconds = 1.0e9;

    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static readonly string[] TopLevelKeys = ["plant", "seed", "startTime", "timeStepMs", "duration", "timeline"];

    private static readonly string[] ActionKeys = ["at", "write", "value", "fault", "clear", "id", "args"];

    // The same forms the plant loader's defaults.startTime accepts; see
    // Millrace.Configuration.Loading.StructureStage.TryParseStartTime (R54). If one
    // changes, change both.
    private static readonly string[] StartTimeUtcFormats =
    [
        "yyyy'-'MM'-'dd'T'HH':'mm':'ss'.'FFFFFFF'Z'",
        "yyyy'-'MM'-'dd'T'HH':'mm':'ss'Z'",
    ];

    private static readonly string[] StartTimeOffsetFormats =
    [
        "yyyy'-'MM'-'dd'T'HH':'mm':'ss'.'FFFFFFFzzz",
        "yyyy'-'MM'-'dd'T'HH':'mm':'sszzz",
    ];

    /// <summary>Parses scenario text, collecting every structural problem rather than stopping at the first.</summary>
    public static ScenarioParseResult Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        var diagnostics = new List<ConfigDiagnostic>();
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json, DocumentOptions);
        }
        catch (JsonException ex)
        {
            long line = (ex.LineNumber ?? 0) + 1;
            long column = (ex.BytePositionInLine ?? 0) + 1;
            diagnostics.Add(ScenarioDiagnostics.Error(
                ScenarioDiagnostics.Syntax,
                "$",
                string.Create(CultureInfo.InvariantCulture, $"The scenario is not valid JSON at line {line}, column {column}: {FirstSentence(ex.Message)}"),
                "Correct the JSON at that position. Comments and trailing commas are allowed; everything else must be strict JSON."));
            return new ScenarioParseResult(diagnostics, null);
        }

        using (document)
        {
            Scenario? scenario = Read(document.RootElement, diagnostics);
            return new ScenarioParseResult(diagnostics, diagnostics.Count == 0 ? scenario : null);
        }
    }

    // System.Text.Json appends "LineNumber: n | BytePositionInLine: m." to its messages; that is already in ours.
    private static string FirstSentence(string message)
    {
        int cut = message.IndexOf(" LineNumber:", StringComparison.Ordinal);
        string text = (cut < 0 ? message : message[..cut]).TrimEnd();
        return text.EndsWith('.') ? text : text + ".";
    }

    private static Scenario? Read(JsonElement root, List<ConfigDiagnostic> diagnostics)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            diagnostics.Add(ScenarioDiagnostics.Error(
                ScenarioDiagnostics.BadValue,
                "$",
                "A scenario file is a JSON object.",
                "Wrap the content in { … } with a \"plant\" and a \"duration\"."));
            return null;
        }

        CheckKeys(root, "$", TopLevelKeys, "a scenario", diagnostics);

        string? plant = ReadPlant(root, diagnostics);
        ulong? seed = ReadSeed(root, diagnostics);
        DateTimeOffset? startTime = ReadStartTime(root, diagnostics);
        TimeSpan? step = ReadTimeStep(root, diagnostics);
        TimeSpan? duration = ReadDuration(root, step, diagnostics);
        List<ScenarioAction> timeline = ReadTimeline(root, duration, step, diagnostics);

        return plant is null || duration is null
            ? null
            : new Scenario(plant, seed, startTime, step, duration.Value, timeline);
    }

    private static void CheckKeys(JsonElement element, string path, string[] allowed, string owner, List<ConfigDiagnostic> diagnostics)
    {
        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (Array.IndexOf(allowed, property.Name) < 0)
            {
                diagnostics.Add(ScenarioDiagnostics.Error(
                    ScenarioDiagnostics.UnknownKey,
                    path == "$" ? $"$.{property.Name}" : $"{path}.{property.Name}",
                    $"'{property.Name}' is not a key {owner} has.",
                    Suggest.Fix(property.Name, allowed, "keys")));
            }
        }
    }

    private static string? ReadPlant(JsonElement root, List<ConfigDiagnostic> diagnostics)
    {
        if (root.TryGetProperty("plant", out JsonElement element)
            && element.ValueKind == JsonValueKind.String
            && element.GetString() is { Length: > 0 } path)
        {
            return path;
        }

        diagnostics.Add(ScenarioDiagnostics.Error(
            ScenarioDiagnostics.BadValue,
            "$.plant",
            "A scenario needs a \"plant\": the path of the plant file, relative to this scenario.",
            "Add \"plant\": \"conveyor-line.json\", naming a plant file beside this one."));
        return null;
    }

    private static ulong? ReadSeed(JsonElement root, List<ConfigDiagnostic> diagnostics)
    {
        if (!root.TryGetProperty("seed", out JsonElement element))
        {
            return null;
        }

        if (element.ValueKind == JsonValueKind.Number && element.TryGetUInt64(out ulong seed))
        {
            return seed;
        }

        diagnostics.Add(ScenarioDiagnostics.Error(
            ScenarioDiagnostics.BadValue,
            "$.seed",
            "\"seed\" must be a whole number, zero or greater.",
            "Use a non-negative integer such as 1."));
        return null;
    }

    private static DateTimeOffset? ReadStartTime(JsonElement root, List<ConfigDiagnostic> diagnostics)
    {
        if (!root.TryGetProperty("startTime", out JsonElement element))
        {
            return null;
        }

        if (element.ValueKind == JsonValueKind.String && TryParseStartTime(element.GetString(), out DateTimeOffset parsed))
        {
            return parsed;
        }

        diagnostics.Add(ScenarioDiagnostics.Error(
            ScenarioDiagnostics.BadValue,
            "$.startTime",
            "\"startTime\" must be an ISO 8601 date and time with an offset.",
            "Write it like \"2026-01-01T06:00:00Z\" or \"2026-03-01T08:00:00+02:00\"."));
        return null;
    }

    /// <summary>An ISO 8601 date-time WITH an offset. An offset-less string would adopt the host's time zone, so it is rejected.</summary>
    private static bool TryParseStartTime(string? text, out DateTimeOffset parsed) =>
        DateTimeOffset.TryParseExact(text, StartTimeUtcFormats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out parsed)
        || DateTimeOffset.TryParseExact(text, StartTimeOffsetFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed);

    private static TimeSpan? ReadTimeStep(JsonElement root, List<ConfigDiagnostic> diagnostics)
    {
        if (!root.TryGetProperty("timeStepMs", out JsonElement element))
        {
            return null;
        }

        if (element.ValueKind == JsonValueKind.Number && element.TryGetDouble(out double ms) && double.IsFinite(ms) && ms > 0.0)
        {
            return TimeSpan.FromMilliseconds(ms);
        }

        diagnostics.Add(ScenarioDiagnostics.Error(
            ScenarioDiagnostics.BadValue,
            "$.timeStepMs",
            "\"timeStepMs\" must be a number greater than zero.",
            "Use the simulation step in milliseconds, such as 10."));
        return null;
    }

    private static TimeSpan? ReadDuration(JsonElement root, TimeSpan? step, List<ConfigDiagnostic> diagnostics)
    {
        if (!root.TryGetProperty("duration", out JsonElement element))
        {
            diagnostics.Add(ScenarioDiagnostics.Error(
                ScenarioDiagnostics.BadValue,
                "$.duration",
                "A scenario needs a \"duration\": how many seconds to run.",
                "Add \"duration\": 120."));
            return null;
        }

        if (!TryReadSeconds(element, out TimeSpan duration) || duration <= TimeSpan.Zero)
        {
            diagnostics.Add(ScenarioDiagnostics.Error(
                ScenarioDiagnostics.BadValue,
                "$.duration",
                "\"duration\" must be a number of seconds greater than zero and at most 1000000000.",
                "Use the number of seconds to run, such as 120."));
            return null;
        }

        if (step is { } declared && duration.Ticks % declared.Ticks != 0L)
        {
            diagnostics.Add(ScenarioDiagnostics.OffTick("$.duration", "The duration", duration, declared));
        }

        return duration;
    }

    /// <summary>Seconds to exact ticks (R52). False for anything that is not a finite number in [0, <see cref="MaxSeconds"/>].</summary>
    private static bool TryReadSeconds(JsonElement element, out TimeSpan value)
    {
        value = TimeSpan.Zero;
        if (element.ValueKind != JsonValueKind.Number
            || !element.TryGetDouble(out double seconds)
            || !double.IsFinite(seconds)
            || seconds < 0.0
            || seconds > MaxSeconds)
        {
            return false;
        }

        value = TimeSpan.FromTicks((long)Math.Round(seconds * TimeSpan.TicksPerSecond, MidpointRounding.AwayFromZero));
        return true;
    }

    private static List<ScenarioAction> ReadTimeline(JsonElement root, TimeSpan? duration, TimeSpan? step, List<ConfigDiagnostic> diagnostics)
    {
        var timeline = new List<ScenarioAction>();
        if (!root.TryGetProperty("timeline", out JsonElement array))
        {
            return timeline;
        }

        if (array.ValueKind != JsonValueKind.Array)
        {
            diagnostics.Add(ScenarioDiagnostics.Error(
                ScenarioDiagnostics.BadValue,
                "$.timeline",
                "\"timeline\" must be an array of actions.",
                "Write \"timeline\": [ { \"at\": 5, \"write\": \"CV001.Start\", \"value\": true } ], or remove the key."));
            return timeline;
        }

        int index = 0;
        foreach (JsonElement element in array.EnumerateArray())
        {
            string path = string.Create(CultureInfo.InvariantCulture, $"$.timeline[{index}]");
            if (ReadAction(element, path, duration, step, diagnostics) is { } action)
            {
                timeline.Add(action);
            }

            index++;
        }

        return timeline;
    }

    private static ScenarioAction? ReadAction(
        JsonElement element, string path, TimeSpan? duration, TimeSpan? step, List<ConfigDiagnostic> diagnostics)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            diagnostics.Add(ScenarioDiagnostics.Error(
                ScenarioDiagnostics.BadAction,
                path,
                "A timeline entry is an object.",
                "Write { \"at\": 5, \"write\": \"CV001.Start\", \"value\": true }."));
            return null;
        }

        CheckKeys(element, path, ActionKeys, "an action", diagnostics);
        TimeSpan? at = ReadAt(element, path, duration, step, diagnostics);

        bool hasWrite = element.TryGetProperty("write", out JsonElement write);
        bool hasFault = element.TryGetProperty("fault", out JsonElement fault);
        bool hasClear = element.TryGetProperty("clear", out JsonElement clear);
        int shapes = (hasWrite ? 1 : 0) + (hasFault ? 1 : 0) + (hasClear ? 1 : 0);
        if (shapes != 1)
        {
            diagnostics.Add(ScenarioDiagnostics.Error(
                ScenarioDiagnostics.BadAction,
                path,
                shapes == 0
                    ? "An action needs exactly one of \"write\", \"fault\" or \"clear\"; this one has none."
                    : string.Create(CultureInfo.InvariantCulture, $"An action needs exactly one of \"write\", \"fault\" or \"clear\"; this one has {shapes}."),
                shapes == 0
                    ? "Add \"write\": \"<tag>\" with a \"value\", or \"fault\" or \"clear\": \"<component>\" with an \"id\"."
                    : "Keep one of them and give each of the others an action of its own."));
            return null;
        }

        if (hasWrite)
        {
            return ReadWrite(element, path, at, write, diagnostics);
        }

        return hasFault
            ? ReadFault(element, path, at, fault, diagnostics)
            : ReadClear(element, path, at, clear, diagnostics);
    }

    private static TimeSpan? ReadAt(
        JsonElement element, string path, TimeSpan? duration, TimeSpan? step, List<ConfigDiagnostic> diagnostics)
    {
        if (!element.TryGetProperty("at", out JsonElement value))
        {
            diagnostics.Add(ScenarioDiagnostics.Error(
                ScenarioDiagnostics.BadValue,
                $"{path}.at",
                "An action needs an \"at\": its time in seconds from the start of the run.",
                "Add \"at\": 5."));
            return null;
        }

        if (!TryReadSeconds(value, out TimeSpan at))
        {
            diagnostics.Add(ScenarioDiagnostics.Error(
                ScenarioDiagnostics.BadValue,
                $"{path}.at",
                "\"at\" must be a number of seconds, zero or greater and at most 1000000000.",
                "Give the time from the start of the run in seconds, such as 5."));
            return null;
        }

        if (step is { } declared && at.Ticks % declared.Ticks != 0L)
        {
            diagnostics.Add(ScenarioDiagnostics.OffTick($"{path}.at", "The action time", at, declared));
        }

        if (duration is { } end && at >= end)
        {
            diagnostics.Add(ScenarioDiagnostics.NotBeforeTheEnd($"{path}.at", at, end));
        }

        return at;
    }

    private static ScenarioAction? ReadWrite(
        JsonElement element, string path, TimeSpan? at, JsonElement tag, List<ConfigDiagnostic> diagnostics)
    {
        string? name = ReadName(tag, $"{path}.write", "write", "a tag", "\"CV001.Start\"", diagnostics);
        Reject(element, path, "id", "\"id\" belongs to a fault or a clear, not to a write.", diagnostics);
        Reject(element, path, "args", "\"args\" belongs to a fault, not to a write.", diagnostics);

        if (!element.TryGetProperty("value", out JsonElement value))
        {
            diagnostics.Add(ScenarioDiagnostics.Error(
                ScenarioDiagnostics.BadAction,
                path,
                "A write action needs a \"value\".",
                "Add \"value\": true for a Bool tag, or a number for a Double or an Int64 tag."));
            return null;
        }

        ScenarioValue? parsed = ReadValue(value);
        if (parsed is null)
        {
            diagnostics.Add(ScenarioDiagnostics.Error(
                ScenarioDiagnostics.BadValue,
                $"{path}.value",
                "\"value\" must be true, false or a number.",
                "Write true or false for a Bool tag, a number for a Double tag, or a whole number for an Int64 tag."));
            return null;
        }

        return name is null || at is null ? null : new WriteAction(at.Value, name, parsed);
    }

    private static ScenarioAction? ReadFault(
        JsonElement element, string path, TimeSpan? at, JsonElement component, List<ConfigDiagnostic> diagnostics)
    {
        string? target = ReadName(component, $"{path}.fault", "fault", "a component", "\"CV001.Motor\"", diagnostics);
        string? faultId = ReadFaultId(element, path, "A fault action needs an \"id\" naming the fault.", diagnostics);
        List<FaultArgument>? arguments = ReadArguments(element, path, diagnostics);

        return target is null || faultId is null || arguments is null || at is null
            ? null
            : new FaultAction(at.Value, target, faultId, arguments);
    }

    private static ScenarioAction? ReadClear(
        JsonElement element, string path, TimeSpan? at, JsonElement component, List<ConfigDiagnostic> diagnostics)
    {
        string? target = ReadName(component, $"{path}.clear", "clear", "a component", "\"CV001.Motor\"", diagnostics);
        string? faultId = ReadFaultId(element, path, "A clear action needs an \"id\" naming the fault.", diagnostics);
        if (element.TryGetProperty("args", out _))
        {
            diagnostics.Add(ScenarioDiagnostics.Error(
                ScenarioDiagnostics.BadAction,
                $"{path}.args",
                "A clear action takes no \"args\".",
                "Remove \"args\"; clearing a fault takes no arguments."));
            return null;
        }

        return target is null || faultId is null || at is null ? null : new ClearAction(at.Value, target, faultId);
    }

    private static string? ReadFaultId(JsonElement element, string path, string missing, List<ConfigDiagnostic> diagnostics)
    {
        if (!element.TryGetProperty("id", out JsonElement id))
        {
            diagnostics.Add(ScenarioDiagnostics.Error(
                ScenarioDiagnostics.BadAction,
                path,
                missing,
                "Add \"id\": \"thermal-bias\"; `millrace catalog export` lists each component's fault ids."));
            return null;
        }

        return ReadName(id, $"{path}.id", "id", "a fault", "\"thermal-bias\"", diagnostics);
    }

    private static List<FaultArgument>? ReadArguments(JsonElement element, string path, List<ConfigDiagnostic> diagnostics)
    {
        var arguments = new List<FaultArgument>();
        if (!element.TryGetProperty("args", out JsonElement args))
        {
            return arguments;
        }

        if (args.ValueKind != JsonValueKind.Object)
        {
            diagnostics.Add(ScenarioDiagnostics.Error(
                ScenarioDiagnostics.BadValue,
                $"{path}.args",
                "\"args\" must be an object of numbers.",
                "Write \"args\": { \"amount\": 0.8 }, or remove the key."));
            return null;
        }

        bool sound = true;
        foreach (JsonProperty property in args.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.Number
                && property.Value.TryGetDouble(out double number)
                && double.IsFinite(number))
            {
                arguments.Add(new FaultArgument(property.Name, number));
                continue;
            }

            sound = false;
            diagnostics.Add(ScenarioDiagnostics.Error(
                ScenarioDiagnostics.BadValue,
                $"{path}.args.{property.Name}",
                $"Fault argument '{property.Name}' must be a number.",
                "Give a number, such as 0.8; every fault argument is numeric."));
        }

        return sound ? arguments : null;
    }

    private static string? ReadName(
        JsonElement element, string path, string key, string what, string example, List<ConfigDiagnostic> diagnostics)
    {
        if (element.ValueKind == JsonValueKind.String && element.GetString() is { Length: > 0 } name)
        {
            return name;
        }

        diagnostics.Add(ScenarioDiagnostics.Error(
            ScenarioDiagnostics.BadValue,
            path,
            $"\"{key}\" must be the name of {what}.",
            $"Give a name such as {example}."));
        return null;
    }

    private static void Reject(JsonElement element, string path, string key, string message, List<ConfigDiagnostic> diagnostics)
    {
        if (element.TryGetProperty(key, out _))
        {
            diagnostics.Add(ScenarioDiagnostics.Error(
                ScenarioDiagnostics.BadAction,
                $"{path}.{key}",
                message,
                "Remove the key, or make this an action of the kind it belongs to."));
        }
    }

    /// <summary>A JSON number is an Int64 value only when it is written as an integer literal (R56).</summary>
    private static ScenarioValue? ReadValue(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.True => ScenarioValue.OfBool(true),
        JsonValueKind.False => ScenarioValue.OfBool(false),
        JsonValueKind.Number when element.TryGetInt64(out long whole) => ScenarioValue.OfInteger(whole),
        JsonValueKind.Number when element.TryGetDouble(out double number) && double.IsFinite(number) => ScenarioValue.OfNumber(number),
        _ => null,
    };
}
```

- [ ] **Step 9: Run the parser tests**

Run: `dotnet test tests/Millrace.Scenarios.Tests --nologo --filter FullyQualifiedName~ScenarioParseTests`
Expected: PASS, 43 tests (15 facts + theory rows 9 + 9 + 10). Report the number the runner prints.

If a message does not match, **report which** and change the test only after
checking the source string is the one this plan specifies.

- [ ] **Step 10: Write the corpus files**

`tests/Millrace.Scenarios.Tests/Scenarios/valid/conveyor-start-and-fault.json` — the
spec's own example, over the conveyor plant, with the plant's start time and
step and the scenario's own seed:

```json
{
  "plant": "../../Plants/conveyor-line.json",
  "seed": 42,
  "duration": 120,
  "timeline": [
    { "at": 5,    "write": "CV001.Start", "value": true },
    { "at": 30,   "fault": "CV001.Motor", "id": "thermal-bias", "args": { "amount": 0.8 } },
    { "at": 60.5, "clear": "CV001.Motor", "id": "thermal-bias" }
  ]
}
```

`tests/Millrace.Scenarios.Tests/Scenarios/valid/minimal-feed-throttled.json`:

```json
{
  "plant": "../../Plants/minimal.json",
  "duration": 30,
  "timeline": [
    { "at": 10, "write": "FEED.Rate", "value": 5 },
    { "at": 20, "write": "FEED.Enabled", "value": false }
  ]
}
```

`tests/Millrace.Scenarios.Tests/Scenarios/valid/instrumented-belt-drift.json` — the
plant declares neither a seed nor a start time, so this scenario supplies all
three overrides:

```json
{
  "plant": "../../Plants/instrumented-belt.json",
  "seed": 7,
  "startTime": "2026-03-01T08:00:00+02:00",
  "timeStepMs": 10,
  "duration": 40,
  "timeline": [
    { "at": 2,  "write": "BELT.SPEED_SP", "value": 1.5 },
    { "at": 10, "fault": "WT", "id": "drift", "args": { "rate": 0.05 } },
    { "at": 25, "clear": "WT", "id": "drift" }
  ]
}
```

`tests/Millrace.Scenarios.Tests/Scenarios/valid/item-line-blinded-counter.json`:

```json
{
  "plant": "../../Plants/item-line.json",
  "duration": 60,
  "timeline": [
    { "at": 1,  "write": "RB.SPEED_SP", "value": 0.8 },
    { "at": 20, "fault": "PC", "id": "blinded" },
    { "at": 40, "clear": "PC", "id": "blinded" }
  ]
}
```

`tests/Millrace.Scenarios.Tests/Scenarios/invalid/MR200-unterminated-timeline.json`:

```json
{
  "plant": "../../Plants/minimal.json",
  "duration": 10,
  "timeline": [
}
```

`tests/Millrace.Scenarios.Tests/Scenarios/invalid/MR201-unknown-key.json`:

```json
{
  "plant": "../../Plants/minimal.json",
  "duration": 10,
  "seedd": 3
}
```

`tests/Millrace.Scenarios.Tests/Scenarios/invalid/MR202-duration-is-zero.json`:

```json
{
  "plant": "../../Plants/minimal.json",
  "duration": 0
}
```

`tests/Millrace.Scenarios.Tests/Scenarios/invalid/MR203-off-tick-action.json`:

```json
{
  "plant": "../../Plants/minimal.json",
  "timeStepMs": 10,
  "duration": 10,
  "timeline": [
    { "at": 5.005, "write": "FEED.Enabled", "value": false }
  ]
}
```

`tests/Millrace.Scenarios.Tests/Scenarios/invalid/MR204-two-shapes-in-one-action.json`:

```json
{
  "plant": "../../Plants/minimal.json",
  "duration": 10,
  "timeline": [
    { "at": 1, "write": "FEED.Enabled", "value": false, "clear": "CHUTE", "id": "blockage" }
  ]
}
```

- [ ] **Step 11: Write the corpus helper and its tests**

`tests/Millrace.Scenarios.Tests/Corpus.cs` — public because xUnit's `MemberData`
reads it:

```csharp
using Millrace.Components;
using Millrace.Core.Catalogue;

namespace Millrace.Scenarios.Tests;

/// <summary>The scenario files under Scenarios/, copied beside the test assembly, and the plants they name.</summary>
public static class Corpus
{
    /// <summary>The shipped catalogue. A scenario needs one only to load its plant.</summary>
    public static ComponentCatalogue Catalogue { get; } = new CatalogueBuilder().Add<ComponentsModule>().Build();

    private static string Root => Path.Combine(AppContext.BaseDirectory, "Scenarios");

    /// <summary>The absolute path of a corpus file, which is what resolves its plant.</summary>
    public static string PathOf(string kind, string name) => Path.Combine(Root, kind, name);

    public static string Text(string kind, string name) => File.ReadAllText(PathOf(kind, name));

    /// <summary>A plant of the linked corpus, by file name.</summary>
    public static string PlantPath(string name) => Path.Combine(AppContext.BaseDirectory, "Plants", name);

    public static IEnumerable<object[]> Valid() => Names("valid");

    public static IEnumerable<object[]> Invalid() => Names("invalid");

    private static IEnumerable<object[]> Names(string kind) =>
        Directory.EnumerateFiles(Path.Combine(Root, kind), "*.json")
            .Select(Path.GetFileName)
            .Order(StringComparer.Ordinal)
            .Select(name => new object[] { name! });
}
```

`tests/Millrace.Scenarios.Tests/CorpusTests.cs`:

```csharp
using Millrace.Configuration;

namespace Millrace.Scenarios.Tests;

public class CorpusTests
{
    [Theory]
    [MemberData(nameof(Corpus.Valid), MemberType = typeof(Corpus))]
    public void EveryValidScenarioParsesClean(string name)
    {
        ScenarioParseResult result = ScenarioLoader.Parse(Corpus.Text("valid", name));

        Assert.True(result.IsValid, result.ToText());
        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Scenario);
    }

    [Theory]
    [MemberData(nameof(Corpus.Valid), MemberType = typeof(Corpus))]
    public void EveryValidScenarioNamesAPlantThatExists(string name)
    {
        ScenarioParseResult result = ScenarioLoader.Parse(Corpus.Text("valid", name));

        string plant = result.Scenario!.ResolvePlantPath(Corpus.PathOf("valid", name));

        Assert.True(File.Exists(plant), $"'{name}' names a plant at '{plant}', which is not there.");
    }

    /// <summary>
    /// One row per file under Scenarios/invalid: the file, the code it must
    /// yield, and the JSON path it must point at. A fixture that reported the
    /// right code at the wrong place would be a silent regression, so the path
    /// is pinned here rather than being merely "starts with $".
    /// </summary>
    public static IEnumerable<object[]> InvalidPaths() =>
    [
        ["MR200-unterminated-timeline.json", "MR200", "$"],
        ["MR201-unknown-key.json", "MR201", "$.seedd"],
        ["MR202-duration-is-zero.json", "MR202", "$.duration"],
        ["MR203-off-tick-action.json", "MR203", "$.timeline[0].at"],
        ["MR204-two-shapes-in-one-action.json", "MR204", "$.timeline[0]"],
    ];

    [Theory]
    [MemberData(nameof(InvalidPaths))]
    public void EveryInvalidScenarioYieldsExactlyTheCodeAndPathItsNameClaims(string name, string code, string path)
    {
        Assert.StartsWith(code, name, StringComparison.Ordinal);

        ScenarioParseResult result = ScenarioLoader.Parse(Corpus.Text("invalid", name));

        Assert.False(result.IsValid);
        Assert.Null(result.Scenario);
        ConfigDiagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((code, path), (diagnostic.Code, diagnostic.Path));
    }

    [Fact]
    public void EveryInvalidFixtureHasARow()
    {
        var rows = InvalidPaths().Select(row => (string)row[0]).ToHashSet(StringComparer.Ordinal);

        Assert.All(Corpus.Invalid(), file => Assert.Contains((string)file[0], rows));
        Assert.Equal(Corpus.Invalid().Count(), rows.Count);
    }

    [Theory]
    [MemberData(nameof(Corpus.Invalid), MemberType = typeof(Corpus))]
    public void EveryDiagnosticNamesItsFix(string name)
    {
        ScenarioParseResult result = ScenarioLoader.Parse(Corpus.Text("invalid", name));

        Assert.All(result.Diagnostics, d =>
        {
            Assert.EndsWith(".", d.Message, StringComparison.Ordinal);
            Assert.EndsWith(".", d.Fix, StringComparison.Ordinal);
            Assert.StartsWith("$", d.Path, StringComparison.Ordinal);
            Assert.Equal(DiagnosticSeverity.Error, d.Severity);
        });
    }
}
```

- [ ] **Step 12: Run the corpus tests**

Run: `dotnet test tests/Millrace.Scenarios.Tests --nologo`
Expected: PASS, 19 + 43 + 19 = 81 tests (the corpus adds 4 + 4 + 5 rows, the
fixture-table guard, and 5 more rows). Report the number the runner prints.

- [ ] **Step 13: Run everything**

Run: `dotnet build Millrace.sln -c Release --nologo` — expect `0 Warning(s)`, `0 Error(s)`.
Run: `dotnet test Millrace.sln --nologo`
Expected: PASS, 765 + 81 = 846 tests.

Run: `grep -rn "PackageReference" src/` — expect no output.

- [ ] **Step 14: Commit**

```bash
git add Millrace.sln src/Millrace.Scenarios tests/Millrace.Scenarios.Tests
```

```bash
git commit -m "$(cat <<'MSG'
feat(scenarios): add the scenario model and its structural loader

ScenarioLoader.Parse finds every structural problem in a scenario file —
MR200 to MR204 — with a JSON path and a fix, and touches neither the
file system nor a plant. A corpus of four valid and five invalid files
pins the codes.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
MSG
)"
```

---

### Task 4: `ScenarioJson.Write` — a scenario rendered deterministically

A recording is worth nothing if it cannot be written down and read back. This
task renders a `Scenario` the way the spec's example is laid out — same key
order, same shapes — through the same `Utf8JsonWriter` options and the same
`Finish` normalisation every other JSON document in the repository uses.

Round-tripping is asserted on the **rendered text** (R55): `FaultAction` carries
a list, and a positional record compares a list by reference, so record equality
would be false for any scenario with fault arguments.

**Files:**
- Create: `src/Millrace.Scenarios/ScenarioJson.cs`
- Test: `tests/Millrace.Scenarios.Tests/ScenarioJsonTests.cs`

**Interfaces:**
- Consumes: Task 3's `Scenario`, `ScenarioAction` and friends, `ScenarioValue`, `ScenarioLoader.Parse`, `ScenarioDiagnostics.Seconds(TimeSpan) → double`; `CatalogueJson.WriterOptions` (`Indented = true`, `UnsafeRelaxedJsonEscaping`) and `CatalogueJson.Finish(MemoryStream) → string`.
- Produces: `public static class ScenarioJson { public static string Write(Scenario scenario); }` — throws `ArgumentException` with parameter `scenario` for a non-finite number, which JSON cannot represent, and with parameter `action` for an action type outside the three the format has.

- [ ] **Step 1: Write the failing test**

`tests/Millrace.Scenarios.Tests/ScenarioJsonTests.cs`:

```csharp
using Millrace.Core.Faults;

namespace Millrace.Scenarios.Tests;

public class ScenarioJsonTests
{
    private static Scenario Example() => new(
        "conveyor-line.json",
        42UL,
        new DateTimeOffset(2026, 1, 1, 6, 0, 0, TimeSpan.Zero),
        TimeSpan.FromMilliseconds(10),
        TimeSpan.FromSeconds(120),
        [
            new WriteAction(TimeSpan.FromSeconds(5), "CV001.Start", ScenarioValue.OfBool(true)),
            new FaultAction(TimeSpan.FromSeconds(30), "CV001.Motor", "thermal-bias", [new FaultArgument("amount", 0.8)]),
            new ClearAction(TimeSpan.FromMilliseconds(60500), "CV001.Motor", "thermal-bias"),
        ]);

    [Fact]
    public void TheSpecsExampleRendersInTheSpecsOrder()
    {
        string json = ScenarioJson.Write(Example());

        Assert.Equal(
            """
            {
              "plant": "conveyor-line.json",
              "seed": 42,
              "startTime": "2026-01-01T06:00:00Z",
              "timeStepMs": 10,
              "duration": 120,
              "timeline": [
                {
                  "at": 5,
                  "write": "CV001.Start",
                  "value": true
                },
                {
                  "at": 30,
                  "fault": "CV001.Motor",
                  "id": "thermal-bias",
                  "args": {
                    "amount": 0.8
                  }
                },
                {
                  "at": 60.5,
                  "clear": "CV001.Motor",
                  "id": "thermal-bias"
                }
              ]
            }

            """.ReplaceLineEndings("\n"),
            json);
    }

    [Fact]
    public void AnOmittedOverrideIsAnOmittedKey()
    {
        string json = ScenarioJson.Write(new Scenario("p.json", null, null, null, TimeSpan.FromSeconds(1), []));

        Assert.Equal(
            """
            {
              "plant": "p.json",
              "duration": 1,
              "timeline": []
            }

            """.ReplaceLineEndings("\n"),
            json);
    }

    [Fact]
    public void WhatItWritesParsesBackToWhatItWrites()
    {
        string json = ScenarioJson.Write(Example());

        ScenarioParseResult parsed = ScenarioLoader.Parse(json);

        Assert.Empty(parsed.Diagnostics);
        Assert.Equal(json, ScenarioJson.Write(parsed.Scenario!));
    }

    [Fact]
    public void EveryFieldSurvivesTheRoundTrip()
    {
        Scenario original = Example();

        Scenario parsed = ScenarioLoader.Parse(ScenarioJson.Write(original)).Scenario!;

        Assert.Equal(
            (original.PlantPath, original.Seed, original.StartTime, original.TimeStep, original.Duration),
            (parsed.PlantPath, parsed.Seed, parsed.StartTime, parsed.TimeStep, parsed.Duration));
        Assert.Equal(original.Timeline[0], parsed.Timeline[0]);
        Assert.Equal(original.Timeline[2], parsed.Timeline[2]);
        var fault = Assert.IsType<FaultAction>(parsed.Timeline[1]);
        Assert.Equal((TimeSpan.FromSeconds(30), "CV001.Motor", "thermal-bias"), (fault.At, fault.ComponentId, fault.FaultId));
        Assert.Equal(new[] { new FaultArgument("amount", 0.8) }, fault.Arguments);
    }

    [Fact]
    public void AnOffsetStartTimeKeepsItsOffset()
    {
        var scenario = new Scenario(
            "p.json", null, new DateTimeOffset(2026, 3, 1, 8, 0, 0, TimeSpan.FromHours(2)), null, TimeSpan.FromSeconds(1), []);

        string json = ScenarioJson.Write(scenario);

        Assert.Contains("\"startTime\": \"2026-03-01T08:00:00+02:00\"", json, StringComparison.Ordinal);
        Assert.Equal(scenario.StartTime, ScenarioLoader.Parse(json).Scenario!.StartTime);
    }

    [Fact]
    public void AFractionOfASecondSurvives()
    {
        var scenario = new Scenario(
            "p.json", null, new DateTimeOffset(2026, 1, 1, 6, 0, 0, 250, TimeSpan.Zero), null, TimeSpan.FromSeconds(1), []);

        string json = ScenarioJson.Write(scenario);

        Assert.Contains("\"startTime\": \"2026-01-01T06:00:00.25Z\"", json, StringComparison.Ordinal);
        Assert.Equal(scenario.StartTime, ScenarioLoader.Parse(json).Scenario!.StartTime);
    }

    [Fact]
    public void AWholeDoubleIsWrittenAsAnIntegerLiteralAndComesBackAsOne()
    {
        var scenario = new Scenario("p.json", null, null, null, TimeSpan.FromSeconds(10),
            [new WriteAction(TimeSpan.FromSeconds(1), "T", ScenarioValue.OfNumber(5.0))]);

        string json = ScenarioJson.Write(scenario);

        Assert.Contains("\"value\": 5", json, StringComparison.Ordinal);
        var parsed = Assert.IsType<WriteAction>(ScenarioLoader.Parse(json).Scenario!.Timeline[0]);
        Assert.Equal(ScenarioValueKind.Integer, parsed.Value.Kind);
        Assert.Equal(json, ScenarioJson.Write(ScenarioLoader.Parse(json).Scenario!));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void ANonFiniteWriteCannotBeWritten(double value)
    {
        var scenario = new Scenario("p.json", null, null, null, TimeSpan.FromSeconds(10),
            [new WriteAction(TimeSpan.FromSeconds(1), "T", ScenarioValue.OfNumber(value))]);

        ArgumentException error = Assert.Throws<ArgumentException>(() => ScenarioJson.Write(scenario));
        Assert.Contains("JSON cannot represent", error.Message, StringComparison.Ordinal);
        Assert.Equal("scenario", error.ParamName);
    }

    [Fact]
    public void ANonFiniteFaultArgumentCannotBeWritten()
    {
        var scenario = new Scenario("p.json", null, null, null, TimeSpan.FromSeconds(10),
            [new FaultAction(TimeSpan.FromSeconds(1), "C", "f", [new FaultArgument("amount", double.NaN)])]);

        ArgumentException error = Assert.Throws<ArgumentException>(() => ScenarioJson.Write(scenario));
        Assert.Contains("amount=NaN", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheTextIsNormalised()
    {
        string json = ScenarioJson.Write(Example());

        Assert.DoesNotContain('\r', json);
        Assert.EndsWith("}\n", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\n\n", json, StringComparison.Ordinal);
    }

    [Fact]
    public void WritingIsDeterministic()
    {
        Assert.Equal(ScenarioJson.Write(Example()), ScenarioJson.Write(Example()));
    }
}
```

Run: `dotnet test tests/Millrace.Scenarios.Tests --nologo --filter FullyQualifiedName~ScenarioJsonTests`
Expected: build FAILS — `ScenarioJson` does not exist.

- [ ] **Step 2: Write the writer**

`src/Millrace.Scenarios/ScenarioJson.cs`:

```csharp
using System.Globalization;
using System.Text.Json;
using Millrace.Core.Catalogue;
using Millrace.Core.Faults;

namespace Millrace.Scenarios;

/// <summary>
/// A scenario as deterministic JSON: same scenario, same bytes. The key order
/// is the format's documented order, the timeline is in landing order, and the
/// text is normalised to <c>\n</c> with one trailing newline — so a recording
/// can be committed and diffed.
/// </summary>
public static class ScenarioJson
{
    /// <summary>Renders <paramref name="scenario"/>. Parsing the result yields a scenario that renders identically.</summary>
    public static string Write(Scenario scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        RejectNonFinite(scenario);

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, CatalogueJson.WriterOptions))
        {
            writer.WriteStartObject();
            writer.WriteString("plant", scenario.PlantPath);
            if (scenario.Seed is { } seed)
            {
                writer.WriteNumber("seed", seed);
            }

            if (scenario.StartTime is { } start)
            {
                writer.WriteString("startTime", Iso(start));
            }

            if (scenario.TimeStep is { } step)
            {
                writer.WriteNumber("timeStepMs", step.TotalMilliseconds);
            }

            writer.WriteNumber("duration", ScenarioDiagnostics.Seconds(scenario.Duration));
            writer.WriteStartArray("timeline");
            foreach (ScenarioAction action in scenario.Timeline)
            {
                WriteOne(writer, action);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return CatalogueJson.Finish(stream);
    }

    private static void WriteOne(Utf8JsonWriter writer, ScenarioAction action)
    {
        writer.WriteStartObject();
        writer.WriteNumber("at", ScenarioDiagnostics.Seconds(action.At));
        switch (action)
        {
            case WriteAction write:
                writer.WriteString("write", write.Tag);
                WriteValue(writer, write.Value);
                break;

            case FaultAction fault:
                writer.WriteString("fault", fault.ComponentId);
                writer.WriteString("id", fault.FaultId);
                if (fault.Arguments.Count > 0)
                {
                    writer.WriteStartObject("args");
                    foreach (FaultArgument argument in fault.Arguments)
                    {
                        writer.WriteNumber(argument.Name, argument.Value);
                    }

                    writer.WriteEndObject();
                }

                break;

            case ClearAction clear:
                writer.WriteString("clear", clear.ComponentId);
                writer.WriteString("id", clear.FaultId);
                break;

            default:
                throw new ArgumentException(
                    $"'{action.GetType().Name}' is not an action this format has. A scenario carries writes, faults and clears.",
                    nameof(action));
        }

        writer.WriteEndObject();
    }

    private static void WriteValue(Utf8JsonWriter writer, ScenarioValue value)
    {
        switch (value.Kind)
        {
            case ScenarioValueKind.Bool:
                writer.WriteBoolean("value", value.Boolean);
                break;
            case ScenarioValueKind.Integer:
                writer.WriteNumber("value", value.Integer);
                break;
            default:
                writer.WriteNumber("value", value.Number);
                break;
        }
    }

    /// <summary>
    /// JSON has neither NaN nor infinity. A scenario that carries one cannot be
    /// written, and the message says which action and which number.
    /// </summary>
    private static void RejectNonFinite(Scenario scenario)
    {
        for (int i = 0; i < scenario.Timeline.Count; i++)
        {
            switch (scenario.Timeline[i])
            {
                case WriteAction { Value.Kind: ScenarioValueKind.Number } write when !double.IsFinite(write.Value.Number):
                    throw new ArgumentException(
                        string.Create(CultureInfo.InvariantCulture,
                            $"Action {i} writes {write.Value.Number} to '{write.Tag}', which JSON cannot represent. A scenario carries finite numbers only."),
                        nameof(scenario));

                case FaultAction fault:
                    foreach (FaultArgument argument in fault.Arguments)
                    {
                        if (!double.IsFinite(argument.Value))
                        {
                            throw new ArgumentException(
                                string.Create(CultureInfo.InvariantCulture,
                                    $"Action {i} gives '{fault.FaultId}' the argument {argument.Name}={argument.Value}, which JSON cannot represent. A scenario carries finite numbers only."),
                                nameof(scenario));
                        }
                    }

                    break;
            }
        }
    }

    /// <summary>
    /// Round-trip ISO 8601: a literal <c>Z</c> at offset zero, <c>±hh:mm</c>
    /// otherwise, and a fractional part only when there is one — a bare
    /// <c>'.'</c> would be emitted whatever the value, and would not parse back.
    /// </summary>
    private static string Iso(DateTimeOffset value)
    {
        string fraction = value.Ticks % TimeSpan.TicksPerSecond == 0L ? string.Empty : "'.'FFFFFFF";
        return value.Offset == TimeSpan.Zero
            ? value.ToString($"yyyy'-'MM'-'dd'T'HH':'mm':'ss{fraction}'Z'", CultureInfo.InvariantCulture)
            : value.ToString($"yyyy'-'MM'-'dd'T'HH':'mm':'ss{fraction}zzz", CultureInfo.InvariantCulture);
    }
}
```

- [ ] **Step 3: Run the test to verify it passes**

Run: `dotnet test tests/Millrace.Scenarios.Tests --nologo --filter FullyQualifiedName~ScenarioJsonTests`
Expected: PASS, 12 tests (10 facts + a theory of 2 rows).

If the exact document in the first test differs, **print the actual document**
into the report before changing anything: the indent is `Utf8JsonWriter`'s own
and the plan asserts it as measured, not as hoped.

- [ ] **Step 4: Run everything**

Run: `dotnet build Millrace.sln -c Release --nologo` — expect `0 Warning(s)`, `0 Error(s)`.
Run: `dotnet test Millrace.sln --nologo`
Expected: PASS, 846 + 12 = 858 tests.

- [ ] **Step 5: Commit**

```bash
git add src/Millrace.Scenarios/ScenarioJson.cs tests/Millrace.Scenarios.Tests/ScenarioJsonTests.cs
```

```bash
git commit -m "$(cat <<'MSG'
feat(scenarios): render a scenario as deterministic JSON

Same key order as the format reference, invariant numbers, round-trip ISO
start times, \n line endings and one trailing newline, so a recording can
be committed and diffed. What it writes parses back to what it writes.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
MSG
)"
```

---

### Task 5: `ScenarioRunner` — load the plant, schedule with checks, run

The whole point of the plan: a scenario and a plant go in, an event log comes
out, and every way the pair can disagree is a diagnostic reported **before tick
0**. The runner is pure given the two texts, so a test runs a whole scenario
from strings and `millrace run` is a thin shell around it.

Order of work, and it matters:

1. `PlantLoader.Load(plantJson, catalogue, scenario.ToLoadOptions())`. Invalid →
   one `MR205` naming the plant, then the plant's own diagnostics **unchanged**.
2. `Build()`, and take the effective step from `load.Options.TimeStep` — the
   scenario's override if it gave one, else the plant's `defaults`, else 10 ms.
3. The duration is a whole number of steps.
4. Every action, in order: at a whole number of steps, before the end, and
   bound — `WriteAt`, `InjectFaultAt` or `ClearFaultAt`, each of which resolves
   its names now and throws on a bad one, which becomes `MR206`.
5. Any diagnostic at all → return with no log. Otherwise `RunFor(duration)`.

**Files:**
- Create: `src/Millrace.Scenarios/RunSummary.cs`, `ScenarioRunResult.cs`, `ScenarioRunner.cs`
- Modify: `tests/Millrace.Scenarios.Tests/Corpus.cs` (two run helpers, the `Unrunnable` member)
- Modify: `tests/Millrace.Scenarios.Tests/CorpusTests.cs` (the run, golden, unrunnable and coverage tests)
- Create: `tests/Millrace.Scenarios.Tests/Plants/broken.json`
- Create: `tests/Millrace.Scenarios.Tests/Scenarios/unrunnable/MR205-plant-is-invalid.json`, `Scenarios/unrunnable/MR206-unknown-tag.json`
- Create: `tests/Millrace.Scenarios.Tests/Golden/*.log` (four, generated)
- Test: `tests/Millrace.Scenarios.Tests/ScenarioRunnerTests.cs`

**Interfaces:**
- Consumes: `PlantLoader.Load(string json, ComponentCatalogue catalogue, LoadOptions? options = null) → LoadResult`; `LoadResult.IsValid`, `.Diagnostics`, `.Builder` (`SimulationBuilder?`), `.Options` (`SimulationOptions?`); `SimulationBuilder.Build() → Simulation`; `SimulationOptions.TimeStep`; `Simulation.IO.Directory` (`TryFind(string, out TagDescriptor)`, `Tags`), `.WriteAt`, `.InjectFaultAt`, `.ClearFaultAt`, `.RunFor`, `.Clock.TickCount`, `.Events`; `TagDescriptor.Name/Kind/Access`; `FaultArguments(params FaultArgument[])`; `Suggest.Fix`; `Golden.Assert(string relativePath, string actual)`.
- Produces (namespace `Millrace.Scenarios`):
  - `public sealed record RunSummary(long Ticks, int Events, int ActionsScheduled)`
  - `public sealed class ScenarioRunResult` — `Diagnostics`, `IsValid`, `Events` (`EventLog?`), `Summary` (`RunSummary?`), `ToText()`
  - `public static class ScenarioRunner { public static ScenarioRunResult Run(Scenario scenario, string plantJson, ComponentCatalogue catalogue); }`
- Produces (test-only, `Millrace.Scenarios.Tests.Corpus`):
  - `public static ScenarioRunResult Run(string scenarioJson, string plantFile)`
  - `public static (ScenarioParseResult Parsed, ScenarioRunResult? Result) RunFile(string kind, string name)`
  - `public static IEnumerable<object[]> Unrunnable()`

- [ ] **Step 1: Write the failing runner tests**

`tests/Millrace.Scenarios.Tests/ScenarioRunnerTests.cs`:

```csharp
using Millrace.Configuration;

namespace Millrace.Scenarios.Tests;

public class ScenarioRunnerTests
{
    private static ScenarioRunResult Run(string plant, string body) =>
        Corpus.Run($$"""{ "plant": "{{plant}}", {{body}} }""", plant);

    private static ConfigDiagnostic Only(string plant, string body) => Assert.Single(Run(plant, body).Diagnostics);

    [Fact]
    public void TheConveyorScenarioRunsAndLogsEveryAction()
    {
        ScenarioRunResult result = Run("conveyor-line.json", """
            "seed": 42, "duration": 120,
            "timeline": [
              { "at": 5,    "write": "CV001.Start", "value": true },
              { "at": 30,   "fault": "CV001.Motor", "id": "thermal-bias", "args": { "amount": 0.8 } },
              { "at": 60.5, "clear": "CV001.Motor", "id": "thermal-bias" }
            ]
            """);

        Assert.Empty(result.Diagnostics);
        Assert.True(result.IsValid);
        Assert.Equal(new RunSummary(12000L, result.Summary!.Events, 3), result.Summary);
        string log = result.Events!.ToText().ReplaceLineEndings("\n");
        Assert.Contains("06:00:05.000  CV001.Start  WRITE  Set to true.\n", log, StringComparison.Ordinal);
        Assert.Contains("06:00:30.000  CV001.Motor  FAULT  thermal-bias injected: amount=0.8.\n", log, StringComparison.Ordinal);
        Assert.Contains("06:01:00.500  CV001.Motor  FAULT_CLEARED  thermal-bias cleared.\n", log, StringComparison.Ordinal);
    }

    [Fact]
    public void TwoRunsOfOneScenarioAreByteIdentical()
    {
        const string Body = """
            "duration": 30,
            "timeline": [ { "at": 10, "write": "FEED.Rate", "value": 5 } ]
            """;

        Assert.Equal(
            Run("minimal.json", Body).Events!.ToText(),
            Run("minimal.json", Body).Events!.ToText());
    }

    [Fact]
    public void AScenarioWithNoTimelineStillRuns()
    {
        ScenarioRunResult result = Run("minimal.json", "\"duration\": 2");

        Assert.Empty(result.Diagnostics);
        Assert.Equal(200L, result.Summary!.Ticks);
        Assert.Equal(0, result.Summary.ActionsScheduled);
    }

    [Fact]
    public void AScenarioOverrideBeatsThePlantsDefaults()
    {
        ScenarioRunResult result = Run("minimal.json", """
            "startTime": "2026-05-04T12:00:00Z", "timeStepMs": 100, "duration": 2,
            "timeline": [ { "at": 1, "write": "FEED.Enabled", "value": false } ]
            """);

        Assert.Empty(result.Diagnostics);
        Assert.Equal(20L, result.Summary!.Ticks);
        Assert.StartsWith("12:00:01.000  FEED.Enabled  WRITE  Set to false.", result.Events!.ToText(), StringComparison.Ordinal);
    }

    [Fact]
    public void AnInvalidPlantIsMr205ThenThePlantsOwnDiagnostics()
    {
        ScenarioRunResult result = Corpus.Run("""{ "plant": "broken.json", "duration": 1 }""", "broken.json");

        Assert.False(result.IsValid);
        Assert.Null(result.Events);
        Assert.Null(result.Summary);
        ConfigDiagnostic first = result.Diagnostics[0];
        Assert.Equal(("MR205", "$.plant"), (first.Code, first.Path));
        Assert.Equal("The plant 'broken.json' has 2 errors of its own; they follow.", first.Message);
        Assert.StartsWith("Fix the plant file", first.Fix, StringComparison.Ordinal);
        Assert.Equal(new[] { "MR103", "MR102" }, result.Diagnostics.Skip(1).Select(d => d.Code));
    }

    [Fact]
    public void TheRunnerChecksTheTimesOfAScenarioBuiltInCode()
    {
        var scenario = new Scenario("minimal.json", null, null, null, TimeSpan.FromSeconds(10),
        [
            new WriteAction(TimeSpan.FromSeconds(10), "FEED.Enabled", ScenarioValue.OfBool(false)),
            new WriteAction(TimeSpan.FromMilliseconds(1005), "FEED.Enabled", ScenarioValue.OfBool(false)),
        ]);

        ScenarioRunResult result = ScenarioRunner.Run(
            scenario, File.ReadAllText(Corpus.PlantPath("minimal.json")), Corpus.Catalogue);

        Assert.Equal(new[] { "MR203", "MR203" }, result.Diagnostics.Select(d => d.Code));
        Assert.Equal(
            new[]
            {
                "An action at 10 s is not before the end of the run at 10 s.",
                "The action time 1.005 s is not a whole number of 10 ms steps.",
            },
            result.Diagnostics.Select(d => d.Message));
    }

    [Fact]
    public void AnOffTickActionOnThePlantsOwnStepIsMr203()
    {
        ConfigDiagnostic diagnostic = Only("minimal.json", """
            "duration": 10,
            "timeline": [ { "at": 1.005, "write": "FEED.Enabled", "value": false } ]
            """);

        Assert.Equal(("MR203", "$.timeline[0].at"), (diagnostic.Code, diagnostic.Path));
        Assert.Equal("The action time 1.005 s is not a whole number of 10 ms steps.", diagnostic.Message);
    }

    [Fact]
    public void AnOffTickDurationOnThePlantsOwnStepIsMr203()
    {
        ConfigDiagnostic diagnostic = Only("minimal.json", "\"duration\": 10.005");

        Assert.Equal(("MR203", "$.duration"), (diagnostic.Code, diagnostic.Path));
        Assert.Equal("The duration 10.005 s is not a whole number of 10 ms steps.", diagnostic.Message);
    }

    [Fact]
    public void AnUnknownTagIsMr206AndSuggestsTheNearest()
    {
        ConfigDiagnostic diagnostic = Only("minimal.json", """
            "duration": 10,
            "timeline": [ { "at": 1, "write": "FEED.Ratte", "value": 5 } ]
            """);

        Assert.Equal(("MR206", "$.timeline[0].write"), (diagnostic.Code, diagnostic.Path));
        Assert.Equal("There is no tag 'FEED.Ratte' in this plant.", diagnostic.Message);
        Assert.Contains("'FEED.Rate' is closest.", diagnostic.Fix, StringComparison.Ordinal);
    }

    [Fact]
    public void AReadOnlyTagIsMr206()
    {
        ConfigDiagnostic diagnostic = Only("minimal.json", """
            "duration": 10,
            "timeline": [ { "at": 1, "write": "PILE.Full", "value": true } ]
            """);

        Assert.Equal(("MR206", "$.timeline[0].write"), (diagnostic.Code, diagnostic.Path));
        Assert.Equal("Tag 'PILE.Full' is read-only; a scenario cannot write it.", diagnostic.Message);
        Assert.StartsWith("Write a tag whose access is ReadWrite;", diagnostic.Fix, StringComparison.Ordinal);
    }

    [Fact]
    public void AValueOfTheWrongKindIsMr206()
    {
        ConfigDiagnostic diagnostic = Only("minimal.json", """
            "duration": 10,
            "timeline": [ { "at": 1, "write": "FEED.Enabled", "value": 5 } ]
            """);

        Assert.Equal(("MR206", "$.timeline[0].value"), (diagnostic.Code, diagnostic.Path));
        Assert.Equal("Tag 'FEED.Enabled' is a Bool tag; 5 is not a Bool value.", diagnostic.Message);
        Assert.Equal("Write true or false.", diagnostic.Fix);
    }

    [Fact]
    public void AFractionOnAnIntegerTagIsMr206()
    {
        ConfigDiagnostic diagnostic = Only("item-line.json", """
            "duration": 10,
            "timeline": [ { "at": 1, "write": "BIN.Count", "value": 5.5 } ]
            """);

        // BIN.Count is read-only, so the access check fires first: that is the order the runner promises.
        Assert.Equal(("MR206", "$.timeline[0].write"), (diagnostic.Code, diagnostic.Path));
        Assert.Equal("Tag 'BIN.Count' is read-only; a scenario cannot write it.", diagnostic.Message);
    }

    [Fact]
    public void AnUnknownComponentIsMr206AtTheShapeKey()
    {
        ConfigDiagnostic diagnostic = Only("minimal.json", """
            "duration": 10,
            "timeline": [ { "at": 1, "fault": "NOPE", "id": "blockage" } ]
            """);

        Assert.Equal(("MR206", "$.timeline[0].fault"), (diagnostic.Code, diagnostic.Path));
        Assert.StartsWith("No component 'NOPE' in the plant.", diagnostic.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("(Parameter", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AComponentWithNoFaultsIsMr206()
    {
        ConfigDiagnostic diagnostic = Only("minimal.json", """
            "duration": 10,
            "timeline": [ { "at": 1, "fault": "PILE", "id": "blockage" } ]
            """);

        Assert.Equal(("MR206", "$.timeline[0].fault"), (diagnostic.Code, diagnostic.Path));
        Assert.StartsWith("Component 'PILE' is not an IFaultTarget", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnknownFaultIdIsMr206AtTheIdPath()
    {
        ConfigDiagnostic diagnostic = Only("minimal.json", """
            "duration": 10,
            "timeline": [ { "at": 1, "fault": "CHUTE", "id": "blokage" } ]
            """);

        Assert.Equal(("MR206", "$.timeline[0].id"), (diagnostic.Code, diagnostic.Path));
        Assert.Equal("'CHUTE' supports no fault 'blokage'. Supported: blockage.", diagnostic.Message);
    }

    [Fact]
    public void AnUndeclaredFaultArgumentIsMr206AtTheArgsPath()
    {
        ConfigDiagnostic diagnostic = Only("minimal.json", """
            "duration": 10,
            "timeline": [ { "at": 1, "fault": "CHUTE", "id": "blockage", "args": { "amount": 1 } } ]
            """);

        Assert.Equal(("MR206", "$.timeline[0].args"), (diagnostic.Code, diagnostic.Path));
        Assert.Equal("Fault 'blockage' has no parameter 'amount'. Declared: none.", diagnostic.Message);
    }

    [Fact]
    public void AClearOfAnUnknownFaultIsMr206AtTheClearPath()
    {
        ConfigDiagnostic diagnostic = Only("minimal.json", """
            "duration": 10,
            "timeline": [ { "at": 1, "clear": "NOPE", "id": "blockage" } ]
            """);

        Assert.Equal(("MR206", "$.timeline[0].clear"), (diagnostic.Code, diagnostic.Path));
    }

    [Fact]
    public void EveryProblemIsCollectedAndNothingRuns()
    {
        ScenarioRunResult result = Run("minimal.json", """
            "duration": 10,
            "timeline": [
              { "at": 1, "write": "FEED.Ratte", "value": 5 },
              { "at": 2.003, "write": "FEED.Enabled", "value": false },
              { "at": 3, "fault": "CHUTE", "id": "blokage" }
            ]
            """);

        Assert.Equal(new[] { "MR206", "MR203", "MR206" }, result.Diagnostics.Select(d => d.Code));
        Assert.Equal(
            new[] { "$.timeline[0].write", "$.timeline[1].at", "$.timeline[2].id" },
            result.Diagnostics.Select(d => d.Path));
        Assert.Null(result.Events);
        Assert.Null(result.Summary);
        Assert.EndsWith("\n", result.ToText(), StringComparison.Ordinal);
    }
}
```

- [ ] **Step 2: Write the broken plant and the unrunnable fixtures**

`tests/Millrace.Scenarios.Tests/Plants/broken.json` — a negative capacity and a
misspelt type, exactly two errors:

```json
{
  "defaults": { "seed": 1, "timeStepMs": 10, "startTime": "2026-01-01T06:00:00Z" },
  "materials": [
    { "name": "ore", "kind": "bulk", "properties": { "density": 2000, "moisture": 0.03, "temperature": 15 } }
  ],
  "components": [
    { "id": "FEED", "type": "bulk-source", "parameters": { "material": "ore", "rateKgPerS": 20 } },
    { "id": "CHUTE", "type": "transfer-chute", "parameters": { "capacityKg": -1 } },
    { "id": "PILE", "type": "bulk-snik" }
  ],
  "flows": [
    { "from": "FEED.Out", "to": "CHUTE.In" },
    { "from": "CHUTE.Out", "to": "PILE.In" }
  ]
}
```

`tests/Millrace.Scenarios.Tests/Scenarios/unrunnable/MR205-plant-is-invalid.json`:

```json
{
  "plant": "../../Plants/broken.json",
  "duration": 1
}
```

`tests/Millrace.Scenarios.Tests/Scenarios/unrunnable/MR206-unknown-tag.json`:

```json
{
  "plant": "../../Plants/minimal.json",
  "duration": 10,
  "timeline": [
    { "at": 1, "write": "FEED.Ratte", "value": 5 }
  ]
}
```

- [ ] **Step 3: Extend the corpus helper**

Add to `tests/Millrace.Scenarios.Tests/Corpus.cs`, inside `public static class Corpus`:

```csharp
    public static IEnumerable<object[]> Unrunnable() => Names("unrunnable");

    /// <summary>Parses scenario text and runs it against a plant of the linked corpus. The text must parse.</summary>
    public static ScenarioRunResult Run(string scenarioJson, string plantFile)
    {
        ScenarioParseResult parsed = ScenarioLoader.Parse(scenarioJson);
        if (parsed.Scenario is null)
        {
            throw new InvalidOperationException("The scenario under test does not parse:\n" + parsed.ToText());
        }

        return ScenarioRunner.Run(parsed.Scenario, File.ReadAllText(PlantPath(plantFile)), Catalogue);
    }

    /// <summary>Parses a corpus file and, if it parses, runs it against the plant it names.</summary>
    public static (ScenarioParseResult Parsed, ScenarioRunResult? Result) RunFile(string kind, string name)
    {
        ScenarioParseResult parsed = ScenarioLoader.Parse(Text(kind, name));
        if (parsed.Scenario is null)
        {
            return (parsed, null);
        }

        string plantJson = File.ReadAllText(parsed.Scenario.ResolvePlantPath(PathOf(kind, name)));
        return (parsed, ScenarioRunner.Run(parsed.Scenario, plantJson, Catalogue));
    }
```

- [ ] **Step 4: Add the corpus run, golden, unrunnable and coverage tests**

Append to `tests/Millrace.Scenarios.Tests/CorpusTests.cs`, inside `public class CorpusTests`:

```csharp
    [Theory]
    [MemberData(nameof(Corpus.Valid), MemberType = typeof(Corpus))]
    public void EveryValidScenarioRunsCleanAndMatchesItsGolden(string name)
    {
        (_, ScenarioRunResult? result) = Corpus.RunFile("valid", name);

        Assert.True(result!.IsValid, result.ToText());
        Assert.NotNull(result.Summary);
        Golden.Assert($"Golden/{Path.GetFileNameWithoutExtension(name)}.log", result.Events!.ToText());
    }

    [Theory]
    [MemberData(nameof(Corpus.Valid), MemberType = typeof(Corpus))]
    public void EveryValidScenarioIsByteIdenticalOnASecondRun(string name)
    {
        (_, ScenarioRunResult? first) = Corpus.RunFile("valid", name);
        (_, ScenarioRunResult? second) = Corpus.RunFile("valid", name);

        Assert.Equal(first!.Events!.ToText(), second!.Events!.ToText());
        Assert.Equal(first.Summary, second.Summary);
    }

    [Theory]
    [MemberData(nameof(Corpus.Unrunnable), MemberType = typeof(Corpus))]
    public void EveryUnrunnableScenarioLeadsWithTheCodeInItsName(string name)
    {
        string expected = name[..name.IndexOf('-', StringComparison.Ordinal)];

        (ScenarioParseResult parsed, ScenarioRunResult? result) = Corpus.RunFile("unrunnable", name);

        Assert.Empty(parsed.Diagnostics);
        Assert.False(result!.IsValid);
        Assert.Equal(expected, result.Diagnostics[0].Code);
        Assert.Null(result.Events);
    }

    [Fact]
    public void EveryScenarioCodeHasAFixture()
    {
        var covered = Corpus.Invalid()
            .Concat(Corpus.Unrunnable())
            .Select(row => ((string)row[0])[..6])
            .ToHashSet(StringComparer.Ordinal);

        Assert.All(ScenarioDiagnostics.All, d => Assert.Contains(d.Code, covered));
    }
```

and add `using Millrace.Tests.Shared;` to the top of the file.

Run: `dotnet test tests/Millrace.Scenarios.Tests --nologo`
Expected: build FAILS — `ScenarioRunner` does not exist.

- [ ] **Step 5: Write the result types**

`src/Millrace.Scenarios/RunSummary.cs`:

```csharp
namespace Millrace.Scenarios;

/// <summary>What a run came to, in three numbers.</summary>
/// <param name="Ticks">Ticks executed: the duration divided by the time step.</param>
/// <param name="Events">Records in the event log.</param>
/// <param name="ActionsScheduled">Actions the scenario declared, all of which bound.</param>
public sealed record RunSummary(long Ticks, int Events, int ActionsScheduled);
```

`src/Millrace.Scenarios/ScenarioRunResult.cs`:

```csharp
using Millrace.Configuration;
using Millrace.Core.Logging;

namespace Millrace.Scenarios;

/// <summary>
/// The outcome of running a scenario: either a log and a summary, or every
/// reason the scenario and the plant do not fit together. Never both — a
/// scenario that did not bind produces no partial log.
/// </summary>
public sealed class ScenarioRunResult
{
    internal ScenarioRunResult(IReadOnlyList<ConfigDiagnostic> diagnostics, EventLog? events, RunSummary? summary)
    {
        Diagnostics = diagnostics;
        Events = events;
        Summary = summary;
    }

    /// <summary>Every problem found, in order: the plant's, then the duration's, then each action's.</summary>
    public IReadOnlyList<ConfigDiagnostic> Diagnostics { get; }

    public bool IsValid => Diagnostics.All(d => d.Severity != DiagnosticSeverity.Error);

    /// <summary>The run's event log — the regression artifact — or null when nothing ran.</summary>
    public EventLog? Events { get; }

    /// <summary>Null when nothing ran.</summary>
    public RunSummary? Summary { get; }

    /// <summary>Every diagnostic, blank-line separated, ending in a newline; empty when there are none.</summary>
    public string ToText() =>
        Diagnostics.Count == 0 ? string.Empty : string.Join("\n\n", Diagnostics.Select(d => d.ToText())) + "\n";
}
```

- [ ] **Step 6: Write the runner**

`src/Millrace.Scenarios/ScenarioRunner.cs`:

```csharp
using System.Globalization;
using Millrace.Configuration;
using Millrace.Core;
using Millrace.Core.Catalogue;
using Millrace.Core.Faults;
using Millrace.Io;

namespace Millrace.Scenarios;

/// <summary>
/// Runs a scenario against a plant. Pure given the two texts — the caller reads
/// the files — so a test runs a whole scenario from strings. Nothing ticks
/// until every action has bound.
/// </summary>
public static class ScenarioRunner
{
    private const string BindFix =
        "Use a component, a fault and arguments the plant declares; `millrace catalog export` lists every component's faults.";

    public static ScenarioRunResult Run(Scenario scenario, string plantJson, ComponentCatalogue catalogue)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        ArgumentNullException.ThrowIfNull(plantJson);
        ArgumentNullException.ThrowIfNull(catalogue);

        LoadResult load = PlantLoader.Load(plantJson, catalogue, scenario.ToLoadOptions());
        if (!load.IsValid)
        {
            return new ScenarioRunResult(PlantDiagnostics(scenario, load), null, null);
        }

        Simulation simulation = load.Builder!.Build();
        TimeSpan step = load.Options!.TimeStep;
        var diagnostics = new List<ConfigDiagnostic>();

        if (scenario.Duration.Ticks % step.Ticks != 0L)
        {
            diagnostics.Add(ScenarioDiagnostics.OffTick("$.duration", "The duration", scenario.Duration, step));
        }

        for (int i = 0; i < scenario.Timeline.Count; i++)
        {
            Schedule(simulation, scenario, i, step, diagnostics);
        }

        if (diagnostics.Count > 0)
        {
            return new ScenarioRunResult(diagnostics, null, null);
        }

        simulation.RunFor(scenario.Duration);
        return new ScenarioRunResult(
            [],
            simulation.Events,
            new RunSummary(simulation.Clock.TickCount, simulation.Events.Records.Count, scenario.Timeline.Count));
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
                "Fix the plant file and run the scenario again; `millrace validate` reports exactly these errors."),
        };

        diagnostics.AddRange(load.Diagnostics);
        return diagnostics;
    }

    private static void Schedule(
        Simulation simulation, Scenario scenario, int index, TimeSpan step, List<ConfigDiagnostic> diagnostics)
    {
        ScenarioAction action = scenario.Timeline[index];
        string path = string.Create(CultureInfo.InvariantCulture, $"$.timeline[{index}]");

        // "at >= 0" is MR202 wherever it is checked (spec 3): the parser rejects
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

        if (tag.Access != TagAccess.ReadWrite)
        {
            diagnostics.Add(ScenarioDiagnostics.Error(
                ScenarioDiagnostics.DoesNotBind,
                $"{path}.write",
                $"Tag '{tag.Name}' is read-only; a scenario cannot write it.",
                "Write a tag whose access is ReadWrite; `millrace tags <plant>` shows each tag's access."));
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
    /// MR206 at the JSON path of the part that was wrong (R58). The engine's
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

- [ ] **Step 7: Run the runner tests, without the goldens**

Run: `dotnet test tests/Millrace.Scenarios.Tests --nologo --filter FullyQualifiedName~ScenarioRunnerTests`
Expected: PASS, 18 tests.

Two expectations here are measurements, not predictions. If either differs,
**report the actual string** and correct the test to it:
- `"The plant 'broken.json' has 2 errors of its own; they follow."` — the count
  comes from `broken.json` producing exactly `MR103` then `MR102`.
- `"'CHUTE' supports no fault 'blokage'. Supported: blockage."` and
  `"Fault 'blockage' has no parameter 'amount'. Declared: none."` — these are
  `Simulation`'s and `FaultDescriptor`'s own words.

- [ ] **Step 8: Generate the golden logs, then read them**

```bash
MILLRACE_UPDATE_GOLDEN=1 dotnet test tests/Millrace.Scenarios.Tests --nologo --filter FullyQualifiedName~CorpusTests
```

Four files appear under `tests/Millrace.Scenarios.Tests/Golden/`. **Read all four**
(`cat` each one, or open it) before committing anything. Then verify this
checklist and **put the answers in the task report** — the run's own behaviour
is not predicted here, only the lines the scenario's actions must produce:

| Golden | Must contain, exactly |
|---|---|
| `conveyor-start-and-fault.log` | `06:00:05.000  CV001.Start  WRITE  Set to true.` |
| | `06:00:30.000  CV001.Motor  FAULT  thermal-bias injected: amount=0.8.` |
| | `06:01:00.500  CV001.Motor  FAULT_CLEARED  thermal-bias cleared.` |
| `minimal-feed-throttled.log` | `06:00:10.000  FEED.Rate  WRITE  Set to 5.` |
| | `06:00:20.000  FEED.Enabled  WRITE  Set to false.` |
| `instrumented-belt-drift.log` | `08:00:02.000  BELT.SPEED_SP  WRITE  Set to 1.5.` |
| | `08:00:10.000  WT  FAULT  drift injected: rate=0.05.` |
| | `08:00:25.000  WT  FAULT_CLEARED  drift cleared.` |
| `item-line-blinded-counter.log` | `00:00:01.000  RB.SPEED_SP  WRITE  Set to 0.8.` |
| | `00:00:20.000  PC  FAULT  blinded injected.` |
| | `00:00:40.000  PC  FAULT_CLEARED  blinded cleared.` |

Also report, per file: the **total number of lines**, and the **distinct codes**
that appear (`WRITE`, `FAULT`, `FAULT_CLEARED`, and whatever the plants
themselves logged — `OVERLOAD_TRIP`, `AT_SPEED`, `STOPPED`, `FULL` and so on).
If a golden is empty apart from the action lines, say so; that is information,
not a failure.

Do **not** edit a golden by hand. If one of the lines above is missing, the
cause is in the code or the scenario file, and the report must say which.

- [ ] **Step 9: Run the corpus tests against the committed goldens**

```bash
dotnet test tests/Millrace.Scenarios.Tests --nologo
```

Expected: PASS, 81 + 18 + 4 + 4 + 2 + 1 = 110 tests. Report the number.

- [ ] **Step 10: Run everything**

Run: `dotnet build Millrace.sln -c Release --nologo` — expect `0 Warning(s)`, `0 Error(s)`.
Run: `dotnet test Millrace.sln --nologo`
Expected: PASS, 858 + 29 = 887 tests.

- [ ] **Step 11: Commit**

```bash
git add src/Millrace.Scenarios tests/Millrace.Scenarios.Tests
```

```bash
git commit -m "$(cat <<'MSG'
feat(scenarios): run a scenario against a plant, with every check first

ScenarioRunner loads the plant, binds every action against the built
simulation and refuses to tick if anything is wrong: MR205 wraps the
plant's own diagnostics, MR206 names the tag, component, fault or
argument that does not exist, MR203 the time that is not on a tick.
Four committed golden logs pin the behaviour of the four valid plants.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
MSG
)"
```

---

### Task 6: `GoldenLog.Compare` — where two logs diverged

`millrace run --expect` and the tests must judge a mismatch the same way, so the
judgement lives beside the runner rather than in the command. Both texts are
normalised first — `\r\n` to `\n`, exactly one trailing newline — because a log
that travelled through a Windows checkout is not a behaviour change. On a
mismatch the report names the first differing line, shows three lines of context
either side from **both** texts, and gives both line counts. No unified diff in
v1: "here is where it diverged, and the whole actual log is in a file beside the
golden" is the honest version, and it is what `tests/Shared/Golden.cs` already
does.

**Files:**
- Create: `src/Millrace.Scenarios/LogComparison.cs`, `src/Millrace.Scenarios/GoldenLog.cs`
- Test: `tests/Millrace.Scenarios.Tests/GoldenLogTests.cs`

**Interfaces:**
- Consumes: nothing beyond the BCL.
- Produces (namespace `Millrace.Scenarios`):
  - `public sealed record LogComparison(bool Matched, int FirstDifferentLine, int ExpectedLines, int ActualLines, string Report)` — `FirstDifferentLine` is 1-based and 0 when matched; `Report` always ends in `\n`
  - `public static class GoldenLog`
    - `public static string Normalise(string text)` — `\n` endings, exactly one trailing newline, or empty for an empty log
    - `public static LogComparison Compare(string expected, string actual)`

- [ ] **Step 1: Write the failing test**

`tests/Millrace.Scenarios.Tests/GoldenLogTests.cs`:

```csharp
namespace Millrace.Scenarios.Tests;

public class GoldenLogTests
{
    private const string Six = "a\nb\nc\nd\ne\nf\n";

    [Fact]
    public void IdenticalLogsMatch()
    {
        LogComparison comparison = GoldenLog.Compare(Six, Six);

        Assert.True(comparison.Matched);
        Assert.Equal(0, comparison.FirstDifferentLine);
        Assert.Equal((6, 6), (comparison.ExpectedLines, comparison.ActualLines));
        Assert.Equal("The logs match (6 lines).\n", comparison.Report);
    }

    [Fact]
    public void CarriageReturnsAreNotADifference()
    {
        Assert.True(GoldenLog.Compare("a\r\nb\r\n", "a\nb\n").Matched);
    }

    [Theory]
    [InlineData("a\nb")]
    [InlineData("a\nb\n")]
    [InlineData("a\nb\n\n\n")]
    public void TrailingNewlinesAreNotADifference(string actual)
    {
        Assert.True(GoldenLog.Compare("a\nb\n", actual).Matched);
    }

    [Fact]
    public void TwoEmptyLogsMatch()
    {
        LogComparison comparison = GoldenLog.Compare(string.Empty, "\n");

        Assert.True(comparison.Matched);
        Assert.Equal((0, 0), (comparison.ExpectedLines, comparison.ActualLines));
    }

    [Fact]
    public void AnEmptyActualAgainstAFullExpectedDivergesAtLineOne()
    {
        LogComparison comparison = GoldenLog.Compare(Six, string.Empty);

        Assert.False(comparison.Matched);
        Assert.Equal(1, comparison.FirstDifferentLine);
        Assert.Equal((6, 0), (comparison.ExpectedLines, comparison.ActualLines));
        Assert.Contains("actual (0 lines):\n  (no lines)\n", comparison.Report, StringComparison.Ordinal);
    }

    [Fact]
    public void TheFirstDifferenceIsReportedWithContextFromBothTexts()
    {
        LogComparison comparison = GoldenLog.Compare(Six, "a\nb\nc\nD\ne\nf\n");

        Assert.False(comparison.Matched);
        Assert.Equal(4, comparison.FirstDifferentLine);
        Assert.Equal(
            """
            The logs differ at line 4.
            expected (6 lines):
                  1 | a
                  2 | b
                  3 | c
            >     4 | d
                  5 | e
                  6 | f
            actual (6 lines):
                  1 | a
                  2 | b
                  3 | c
            >     4 | D
                  5 | e
                  6 | f

            """.ReplaceLineEndings("\n"),
            comparison.Report);
    }

    [Fact]
    public void ContextIsAtMostThreeLinesEitherSide()
    {
        LogComparison comparison = GoldenLog.Compare(
            "1\n2\n3\n4\n5\n6\n7\n8\n9\n10\n11\n",
            "1\n2\n3\n4\n5\n6\nX\n8\n9\n10\n11\n");

        Assert.Equal(7, comparison.FirstDifferentLine);
        Assert.Contains("      4 | 4\n", comparison.Report, StringComparison.Ordinal);
        Assert.DoesNotContain("      3 | 3\n", comparison.Report, StringComparison.Ordinal);
        Assert.Contains("     10 | 10\n", comparison.Report, StringComparison.Ordinal);
        Assert.DoesNotContain("     11 | 11\n", comparison.Report, StringComparison.Ordinal);
    }

    [Fact]
    public void AShorterActualDivergesAfterItsLastLine()
    {
        LogComparison comparison = GoldenLog.Compare(Six, "a\nb\nc\n");

        Assert.False(comparison.Matched);
        Assert.Equal(4, comparison.FirstDifferentLine);
        Assert.Equal((6, 3), (comparison.ExpectedLines, comparison.ActualLines));
        Assert.Contains("actual (3 lines):", comparison.Report, StringComparison.Ordinal);
    }

    [Fact]
    public void ALongerActualDivergesAfterTheExpectedsLastLine()
    {
        LogComparison comparison = GoldenLog.Compare("a\nb\nc\n", Six);

        Assert.False(comparison.Matched);
        Assert.Equal(4, comparison.FirstDifferentLine);
        Assert.Equal((3, 6), (comparison.ExpectedLines, comparison.ActualLines));
        Assert.Contains(">     4 | d\n", comparison.Report, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("\n", "")]
    [InlineData("a", "a\n")]
    [InlineData("a\n", "a\n")]
    [InlineData("a\r\nb", "a\nb\n")]
    [InlineData("a\n\n\n", "a\n")]
    public void NormaliseGivesOneTrailingNewlineAndNoCarriageReturns(string text, string expected)
    {
        Assert.Equal(expected, GoldenLog.Normalise(text));
    }

    [Fact]
    public void TheReportAlwaysEndsWithOneNewline()
    {
        Assert.EndsWith("\n", GoldenLog.Compare(Six, Six).Report, StringComparison.Ordinal);
        Assert.EndsWith("\n", GoldenLog.Compare(Six, "x\n").Report, StringComparison.Ordinal);
    }
}
```

Run: `dotnet test tests/Millrace.Scenarios.Tests --nologo --filter FullyQualifiedName~GoldenLogTests`
Expected: build FAILS — `GoldenLog` does not exist.

- [ ] **Step 2: Write the comparison record**

`src/Millrace.Scenarios/LogComparison.cs`:

```csharp
namespace Millrace.Scenarios;

/// <summary>
/// What comparing a run's log with a committed one came to. A mismatch reports
/// where, not what the whole difference is: the whole actual log is written
/// beside the golden, and a diff tool is better at the rest.
/// </summary>
/// <param name="Matched">True when the two logs are the same once normalised.</param>
/// <param name="FirstDifferentLine">One-based; zero when they match.</param>
/// <param name="ExpectedLines">Lines in the committed log.</param>
/// <param name="ActualLines">Lines in the run's log.</param>
/// <param name="Report">The human report, ending in a newline. Never empty.</param>
public sealed record LogComparison(
    bool Matched,
    int FirstDifferentLine,
    int ExpectedLines,
    int ActualLines,
    string Report);
```

- [ ] **Step 3: Write the comparison**

`src/Millrace.Scenarios/GoldenLog.cs`:

```csharp
using System.Globalization;
using System.Text;

namespace Millrace.Scenarios;

/// <summary>
/// Judges a run's event log against a committed one. The command line and the
/// tests share this, so "the behaviour changed" means the same in both.
/// </summary>
public static class GoldenLog
{
    private const int Context = 3;

    /// <summary><c>\n</c> line endings and exactly one trailing newline; an empty log stays empty.</summary>
    public static string Normalise(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        string normalised = text.ReplaceLineEndings("\n").TrimEnd('\n');
        return normalised.Length == 0 ? string.Empty : normalised + "\n";
    }

    /// <summary>Compares two logs after normalising both.</summary>
    public static LogComparison Compare(string expected, string actual)
    {
        string left = Normalise(expected);
        string right = Normalise(actual);
        string[] expectedLines = Lines(left);
        string[] actualLines = Lines(right);

        if (string.Equals(left, right, StringComparison.Ordinal))
        {
            return new LogComparison(
                true,
                0,
                expectedLines.Length,
                actualLines.Length,
                string.Create(CultureInfo.InvariantCulture, $"The logs match ({actualLines.Length} lines).\n"));
        }

        int index = 0;
        while (index < expectedLines.Length
               && index < actualLines.Length
               && string.Equals(expectedLines[index], actualLines[index], StringComparison.Ordinal))
        {
            index++;
        }

        var report = new StringBuilder();
        report.Append(string.Create(CultureInfo.InvariantCulture, $"The logs differ at line {index + 1}.\n"));
        Block(report, "expected", expectedLines, index);
        Block(report, "actual", actualLines, index);
        return new LogComparison(false, index + 1, expectedLines.Length, actualLines.Length, report.ToString());
    }

    private static string[] Lines(string normalised) =>
        normalised.Length == 0 ? [] : normalised.TrimEnd('\n').Split('\n');

    private static void Block(StringBuilder report, string label, string[] lines, int index)
    {
        report.Append(string.Create(CultureInfo.InvariantCulture, $"{label} ({lines.Length} lines):\n"));
        int from = Math.Max(0, index - Context);
        int to = Math.Min(lines.Length - 1, index + Context);
        if (to < from)
        {
            report.Append("  (no lines)\n");
            return;
        }

        for (int i = from; i <= to; i++)
        {
            report.Append(string.Create(
                CultureInfo.InvariantCulture, $"{(i == index ? '>' : ' ')} {i + 1,5} | {lines[i]}\n"));
        }
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test tests/Millrace.Scenarios.Tests --nologo --filter FullyQualifiedName~GoldenLogTests`
Expected: PASS, 18 tests (9 facts + theory rows 3 + 6).

The exact report in `TheFirstDifferenceIsReportedWithContextFromBothTexts` is
arithmetic on the format string above: a marker character, a space, the line
number right-aligned in five columns, `" | "`, the line. If it differs, print
the actual report and correct the test to it, saying so.

- [ ] **Step 5: Run everything**

Run: `dotnet build Millrace.sln -c Release --nologo` — expect `0 Warning(s)`, `0 Error(s)`.
Run: `dotnet test Millrace.sln --nologo`
Expected: PASS, 887 + 18 = 905 tests.

- [ ] **Step 6: Commit**

```bash
git add src/Millrace.Scenarios/LogComparison.cs src/Millrace.Scenarios/GoldenLog.cs tests/Millrace.Scenarios.Tests/GoldenLogTests.cs
```

```bash
git commit -m "$(cat <<'MSG'
feat(scenarios): say where two event logs diverged

GoldenLog normalises line endings and the trailing newline, then reports
the first differing line with three lines of context from both texts and
both line counts. The command line and the tests judge a mismatch the
same way because they share this.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
MSG
)"
```

---

### Task 7: `ScenarioRecorder` — a live run recorded, written, parsed and replayed

The seam from Task 2 has one consumer in 5b: a recorder that accumulates
actions in landing order and turns them into a `Scenario` whose overrides are
the run's **actual** seed, start time and step, and whose timeline is the
recording with `at = tick × step`.

Then the proof the whole plan exists for: a plant is built, a `CommandBus`
issues a write and `InjectFaultAt`/`ClearFaultAt` are called on a live run with
the recorder attached; the recording is written as JSON, parsed back, and
replayed by `ScenarioRunner` against the same plant text; **the two event logs
are byte-identical**. If `Simulation.WriteAt` did not exist, this test could not
pass — a replayed write would land a tick late.

Recording has no CLI surface in 5b. Nothing external drives a run yet; the seam
is proven here and is ready for the first adapter.

**Files:**
- Create: `src/Millrace.Scenarios/ScenarioRecorder.cs`
- Modify: `tests/Millrace.Scenarios.Tests/Millrace.Scenarios.Tests.csproj` (add `Millrace.Realtime`, R61)
- Test: `tests/Millrace.Scenarios.Tests/ScenarioRecorderTests.cs`, `RecordAndReplayTests.cs`

**Interfaces:**
- Consumes: `IActionRecorder` and `Simulation.AttachActionRecorder` (Task 2); `SimulationOptions` (`Seed` `ulong`, `StartTime`, `TimeStep`); `FaultArguments` (`Count`, `this[int]`); `CommandBus(ITagWriter writer, ICommandRecorder? recorder = null)` and `CommandOutcome.Accepted` from `Millrace.Realtime`; `PlantLoader.Load`; `ScenarioJson.Write`; `ScenarioLoader.Parse`; `ScenarioRunner.Run`.
- Produces (namespace `Millrace.Scenarios`):
  - `public sealed class ScenarioRecorder : IActionRecorder`
    - `public int Count { get; }`
    - `public Scenario ToScenario(string plantPath, SimulationOptions options, TimeSpan duration)`

- [ ] **Step 1: Add the real-time reference**

In `tests/Millrace.Scenarios.Tests/Millrace.Scenarios.Tests.csproj`, add to the project
reference group:

```xml
    <ProjectReference Include="..\..\src\Millrace.Realtime\Millrace.Realtime.csproj" />
```

so that group reads:

```xml
  <ItemGroup>
    <ProjectReference Include="..\..\src\Millrace.Scenarios\Millrace.Scenarios.csproj" />
    <ProjectReference Include="..\..\src\Millrace.Components\Millrace.Components.csproj" />
    <ProjectReference Include="..\..\src\Millrace.Realtime\Millrace.Realtime.csproj" />
  </ItemGroup>
```

`src/Millrace.Scenarios` gains nothing: it still references `Millrace.Core` and
`Millrace.Configuration` only.

- [ ] **Step 2: Write the failing recorder tests**

`tests/Millrace.Scenarios.Tests/ScenarioRecorderTests.cs`:

```csharp
using Millrace.Core.Faults;
using Millrace.Core.Time;
using Millrace.Io;

namespace Millrace.Scenarios.Tests;

public class ScenarioRecorderTests
{
    private static SimulationOptions Options => new()
    {
        Seed = 9UL,
        StartTime = new DateTimeOffset(2026, 1, 1, 6, 0, 0, TimeSpan.Zero),
        TimeStep = TimeSpan.FromMilliseconds(10),
    };

    [Fact]
    public void AnEmptyRecordingIsAnEmptyTimeline()
    {
        Scenario scenario = new ScenarioRecorder().ToScenario("p.json", Options, TimeSpan.FromSeconds(1));

        Assert.Empty(scenario.Timeline);
        Assert.Equal(0, new ScenarioRecorder().Count);
    }

    [Fact]
    public void TheOverridesAreTheRunsActualOptions()
    {
        Scenario scenario = new ScenarioRecorder().ToScenario("p.json", Options, TimeSpan.FromSeconds(120));

        Assert.Equal("p.json", scenario.PlantPath);
        Assert.Equal(9UL, scenario.Seed);
        Assert.Equal(new DateTimeOffset(2026, 1, 1, 6, 0, 0, TimeSpan.Zero), scenario.StartTime);
        Assert.Equal(TimeSpan.FromMilliseconds(10), scenario.TimeStep);
        Assert.Equal(TimeSpan.FromSeconds(120), scenario.Duration);
    }

    [Fact]
    public void EachActionIsTimedAtItsTickTimesTheStep()
    {
        var recorder = new ScenarioRecorder();
        recorder.Wrote(500L, "CV001.Start", TagValue.Bool(true));
        recorder.Faulted(3000L, "CV001.Motor", "thermal-bias", new FaultArguments(new FaultArgument("amount", 0.8)));
        recorder.Cleared(6050L, "CV001.Motor", "thermal-bias");

        Scenario scenario = recorder.ToScenario("p.json", Options, TimeSpan.FromSeconds(120));

        Assert.Equal(3, recorder.Count);
        Assert.Equal(
            new[] { 5.0, 30.0, 60.5 },
            scenario.Timeline.Select(a => a.At.TotalSeconds));
    }

    [Fact]
    public void EachShapeIsRecordedWithItsOwnFields()
    {
        var recorder = new ScenarioRecorder();
        recorder.Wrote(100L, "T.Enable", TagValue.Bool(true));
        recorder.Faulted(200L, "F", "blow", new FaultArguments(new FaultArgument("resistance", 2.5)));
        recorder.Cleared(300L, "F", "blow");

        Scenario scenario = recorder.ToScenario("p.json", Options, TimeSpan.FromSeconds(10));

        var write = Assert.IsType<WriteAction>(scenario.Timeline[0]);
        Assert.Equal(("T.Enable", ScenarioValue.OfBool(true)), (write.Tag, write.Value));
        var fault = Assert.IsType<FaultAction>(scenario.Timeline[1]);
        Assert.Equal(("F", "blow"), (fault.ComponentId, fault.FaultId));
        Assert.Equal(new[] { new FaultArgument("resistance", 2.5) }, fault.Arguments);
        var clear = Assert.IsType<ClearAction>(scenario.Timeline[2]);
        Assert.Equal(("F", "blow"), (clear.ComponentId, clear.FaultId));
    }

    [Fact]
    public void EachTagKindKeepsItsValueKind()
    {
        var recorder = new ScenarioRecorder();
        recorder.Wrote(0L, "B", TagValue.Bool(false));
        recorder.Wrote(0L, "D", TagValue.Double(1.5));
        recorder.Wrote(0L, "I", TagValue.Int64(7L));

        Scenario scenario = recorder.ToScenario("p.json", Options, TimeSpan.FromSeconds(1));

        Assert.Equal(
            new[] { ScenarioValueKind.Bool, ScenarioValueKind.Number, ScenarioValueKind.Integer },
            scenario.Timeline.Cast<WriteAction>().Select(w => w.Value.Kind));
    }

    [Fact]
    public void ARecordingIsWrittenAndParsedBack()
    {
        var recorder = new ScenarioRecorder();
        recorder.Wrote(500L, "CV001.Start", TagValue.Bool(true));
        recorder.Faulted(3000L, "CV001.Motor", "thermal-bias", new FaultArguments(new FaultArgument("amount", 0.8)));

        string json = ScenarioJson.Write(recorder.ToScenario("p.json", Options, TimeSpan.FromSeconds(120)));
        ScenarioParseResult parsed = ScenarioLoader.Parse(json);

        Assert.Empty(parsed.Diagnostics);
        Assert.Equal(json, ScenarioJson.Write(parsed.Scenario!));
    }

    [Fact]
    public void ARecordingIsImmutableOnceTaken()
    {
        var recorder = new ScenarioRecorder();
        recorder.Wrote(0L, "T", TagValue.Bool(true));
        Scenario first = recorder.ToScenario("p.json", Options, TimeSpan.FromSeconds(1));

        recorder.Wrote(100L, "T", TagValue.Bool(false));

        Assert.Single(first.Timeline);
        Assert.Equal(2, recorder.ToScenario("p.json", Options, TimeSpan.FromSeconds(1)).Timeline.Count);
    }
}
```

`tests/Millrace.Scenarios.Tests/RecordAndReplayTests.cs`:

```csharp
using Millrace.Configuration;
using Millrace.Core;
using Millrace.Core.Faults;
using Millrace.Realtime;

namespace Millrace.Scenarios.Tests;

public class RecordAndReplayTests
{
    /// <summary>
    /// A live run driven the way a real one is driven — a command bus for the
    /// write, the fault API for the rest — recorded, written, parsed and
    /// replayed. The two logs must be byte-identical, which is only true
    /// because a scheduled write lands on the tick it names.
    /// </summary>
    private static (Simulation Live, ScenarioRecorder Recorder, LoadResult Load, string PlantJson) LiveRun()
    {
        string plantJson = File.ReadAllText(Corpus.PlantPath("conveyor-line.json"));
        LoadResult load = PlantLoader.Load(plantJson, Corpus.Catalogue);
        Assert.True(load.IsValid, load.ToText());

        Simulation live = load.Builder!.Build();
        var recorder = new ScenarioRecorder();
        live.AttachActionRecorder(recorder);
        var bus = new CommandBus(live.IO);

        live.RunFor(TimeSpan.FromSeconds(5));
        Assert.Equal(CommandOutcome.Accepted, bus.WriteBool("CV001.Start", true));
        live.InjectFaultAt(
            TimeSpan.FromSeconds(30), "CV001.Motor", "thermal-bias", new FaultArguments(new FaultArgument("amount", 0.8)));
        live.ClearFaultAt(TimeSpan.FromMilliseconds(60500), "CV001.Motor", "thermal-bias");
        live.RunFor(TimeSpan.FromSeconds(115));

        return (live, recorder, load, plantJson);
    }

    [Fact]
    public void TheRecordingIsTheScenarioFileAPersonWouldHaveWritten()
    {
        (_, ScenarioRecorder recorder, LoadResult load, _) = LiveRun();

        string json = ScenarioJson.Write(recorder.ToScenario("conveyor-line.json", load.Options!, TimeSpan.FromSeconds(120)));

        Assert.Equal(
            """
            {
              "plant": "conveyor-line.json",
              "seed": 1,
              "startTime": "2026-01-01T06:00:00Z",
              "timeStepMs": 10,
              "duration": 120,
              "timeline": [
                {
                  "at": 5,
                  "write": "CV001.Start",
                  "value": true
                },
                {
                  "at": 30,
                  "fault": "CV001.Motor",
                  "id": "thermal-bias",
                  "args": {
                    "amount": 0.8
                  }
                },
                {
                  "at": 60.5,
                  "clear": "CV001.Motor",
                  "id": "thermal-bias"
                }
              ]
            }

            """.ReplaceLineEndings("\n"),
            json);
    }

    [Fact]
    public void TheReplayOfARecordedRunIsByteIdenticalToIt()
    {
        (Simulation live, ScenarioRecorder recorder, LoadResult load, string plantJson) = LiveRun();

        Scenario recorded = recorder.ToScenario("conveyor-line.json", load.Options!, TimeSpan.FromSeconds(120));
        ScenarioParseResult parsed = ScenarioLoader.Parse(ScenarioJson.Write(recorded));
        Assert.Empty(parsed.Diagnostics);

        ScenarioRunResult replay = ScenarioRunner.Run(parsed.Scenario!, plantJson, Corpus.Catalogue);

        Assert.True(replay.IsValid, replay.ToText());
        Assert.Equal(live.Events.ToText(), replay.Events!.ToText());
        Assert.Equal(12000L, replay.Summary!.Ticks);
        Assert.Equal(live.Events.Records.Count, replay.Summary.Events);
        Assert.NotEmpty(live.Events.Records);
    }
}
```

Run: `dotnet test tests/Millrace.Scenarios.Tests --nologo --filter FullyQualifiedName~Record`
Expected: build FAILS — `ScenarioRecorder` does not exist.

- [ ] **Step 3: Write the recorder**

`src/Millrace.Scenarios/ScenarioRecorder.cs`:

```csharp
using Millrace.Core;
using Millrace.Core.Faults;
using Millrace.Core.Time;
using Millrace.Io;

namespace Millrace.Scenarios;

/// <summary>
/// Accumulates every action that took effect, in landing order, and turns the
/// lot into a <see cref="Scenario"/>. Attach it with
/// <c>Simulation.AttachActionRecorder</c> and a live run — a command bus, an
/// operator, a test — becomes a file that replays to the same event log.
/// </summary>
public sealed class ScenarioRecorder : IActionRecorder
{
    private readonly List<Recorded> _actions = [];

    /// <summary>Actions recorded so far.</summary>
    public int Count => _actions.Count;

    /// <inheritdoc/>
    public void Wrote(long tick, string tag, TagValue value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);
        _actions.Add(new Recorded(Kind.Write, tick, tag, string.Empty, From(value), []));
    }

    /// <inheritdoc/>
    public void Faulted(long tick, string componentId, string faultId, FaultArguments arguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(componentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(faultId);
        ArgumentNullException.ThrowIfNull(arguments);

        var copied = new FaultArgument[arguments.Count];
        for (int i = 0; i < copied.Length; i++)
        {
            copied[i] = arguments[i];
        }

        _actions.Add(new Recorded(Kind.Fault, tick, componentId, faultId, null, copied));
    }

    /// <inheritdoc/>
    public void Cleared(long tick, string componentId, string faultId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(componentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(faultId);
        _actions.Add(new Recorded(Kind.Clear, tick, componentId, faultId, null, []));
    }

    /// <summary>
    /// The recording as a scenario over <paramref name="plantPath"/>. The
    /// overrides are the run's actual options, so a replay cannot inherit a
    /// different default; each action's time is its tick times the step.
    /// </summary>
    public Scenario ToScenario(string plantPath, SimulationOptions options, TimeSpan duration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(plantPath);
        ArgumentNullException.ThrowIfNull(options);

        var timeline = new List<ScenarioAction>(_actions.Count);
        foreach (Recorded recorded in _actions)
        {
            TimeSpan at = TimeSpan.FromTicks(options.TimeStep.Ticks * recorded.Tick);
            timeline.Add(recorded.Action switch
            {
                Kind.Write => new WriteAction(at, recorded.Target, recorded.Value!),
                Kind.Fault => new FaultAction(at, recorded.Target, recorded.FaultId, recorded.Arguments),
                _ => new ClearAction(at, recorded.Target, recorded.FaultId),
            });
        }

        return new Scenario(plantPath, options.Seed, options.StartTime, options.TimeStep, duration, timeline);
    }

    private static ScenarioValue From(TagValue value) => value.Kind switch
    {
        TagKind.Bool => ScenarioValue.OfBool(value.AsBool),
        TagKind.Int64 => ScenarioValue.OfInteger(value.AsInt64),
        _ => ScenarioValue.OfNumber(value.AsDouble),
    };

    private enum Kind
    {
        Write,
        Fault,
        Clear,
    }

    private sealed record Recorded(
        Kind Action,
        long Tick,
        string Target,
        string FaultId,
        ScenarioValue? Value,
        IReadOnlyList<FaultArgument> Arguments);
}
```

- [ ] **Step 4: Run the tests**

Run: `dotnet test tests/Millrace.Scenarios.Tests --nologo --filter FullyQualifiedName~Record`
Expected: PASS, 9 tests (7 in `ScenarioRecorderTests`, 2 in `RecordAndReplayTests`).

`TheReplayOfARecordedRunIsByteIdenticalToIt` is the plan's load-bearing test. If
it fails, do **not** loosen it. Report, in the task report:
- the first line at which the two logs differ (feed both through
  `GoldenLog.Compare` and paste the report);
- the tick and source of that line in each log.

A one-tick offset on the `CV001.Start` write means `WriteAt` is not landing in
the drain of the tick it named; a difference that starts at the fault means the
options did not round-trip.

- [ ] **Step 5: Run everything**

Run: `dotnet build Millrace.sln -c Release --nologo` — expect `0 Warning(s)`, `0 Error(s)`.
Run: `dotnet test Millrace.sln --nologo`
Expected: PASS, 905 + 9 = 914 tests.

- [ ] **Step 6: Commit**

```bash
git add src/Millrace.Scenarios/ScenarioRecorder.cs tests/Millrace.Scenarios.Tests
```

```bash
git commit -m "$(cat <<'MSG'
feat(scenarios): record a live run and replay it to the same log

ScenarioRecorder turns every action that took effect into a Scenario
whose overrides are the run's actual seed, start time and step. A
conveyor run driven by a command bus and the fault API is recorded,
written as JSON, parsed back and replayed; the two event logs are
byte-identical.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
MSG
)"
```

---

### Task 8: `millrace run` — the command, and exit code 4

`millrace run <scenario.json> [--expect <golden.log>] [--out <file>] [--format text|json] [--assembly <path>]...`

| case | stdout | stderr | exit |
|---|---|---|---|
| runs, no `--expect` | the event log text (nothing when `--out` took it) | | 0 |
| runs, `--expect` matches | `Matched <golden> (<N> events).` | | 0 |
| runs, `--expect` differs | | the first divergence with context, the line counts, and the path of the `.actual` file written beside the golden | 4 |
| scenario or plant invalid | (`--format json`: the document with `ok: false`) | the diagnostics, then a count line (`--format text`) | 1 |
| scenario, plant or golden unreadable; `--out` or `.actual` unwritable | | `Cannot read '…': …` / `Cannot write '…': …` | 3 |
| usage error | | the problem, then a pointer to `--help` | 2 |

`--format json` emits `{ "ok", "scenario", "plant", "ticks", "events": [ { "tick",
"time", "source", "code", "message" } ], "match" and "firstDifferentLine" and
"actual" (only with `--expect`, and the last two only on a mismatch),
"diagnostics" }`. `ok` says the configuration is sound; `match` says the
behaviour is unchanged. They are different questions, which is the whole reason
exit 4 exists.

There is no `--update`. `millrace run s.json --out golden.log` is how a golden is
made or remade, on purpose, and the change is reviewed in version control.

**Files:**
- Modify: `src/Millrace.Cli/ExitCodes.cs`, `src/Millrace.Cli/CommandTable.cs`, `src/Millrace.Cli/Millrace.Cli.csproj`
- Modify: `src/Millrace.Cli/Commands/PlantFile.cs` (extract `TryRead` and `ReportDiagnostics`; output bytes unchanged)
- Create: `src/Millrace.Cli/Commands/RunScenario.cs`
- Modify: `tests/Millrace.Cli.Tests/Cli.cs` (a `Scenario` path helper, and `Built` moved here), `tests/Millrace.Cli.Tests/PluginTests.cs` (use it), `tests/Millrace.Cli.Tests/Millrace.Cli.Tests.csproj` (copy `Scenarios/`)
- Create: `tests/Millrace.Cli.Tests/Scenarios/*.json` (5 files)
- Test: `tests/Millrace.Cli.Tests/RunCommandTests.cs`

**Interfaces:**
- Consumes: `ScenarioLoader.Parse`, `ScenarioParseResult` (`Scenario`, `Diagnostics`, `ToText()`), `Scenario.ResolvePlantPath`, `ScenarioRunner.Run`, `ScenarioRunResult` (`IsValid`, `Diagnostics`, `Events`, `Summary`, `ToText()`), `RunSummary.Events`/`.Ticks`, `GoldenLog.Normalise`/`.Compare`, `LogComparison` — all `Millrace.Scenarios`. `CliContext` (`Out`, `Err`, `Json`, `Catalogue`, `CommandLine`), `CommandTable.Out`/`.Format`/`.Assembly`, `CatalogueJson.WriterOptions`/`.Finish`/`.Camel`, `SimEventRecord`.
- Produces:
  - `public const int ExitCodes.LogMismatch = 4;`
  - `internal static readonly OptionSpec CommandTable.Expect`
  - `internal static class RunScenario { public static int Run(CliContext context); }`
  - `public static bool PlantFile.TryRead(CliContext context, string path, out string text)`
  - `public static void PlantFile.ReportDiagnostics(CliContext context, string path, string diagnostics, int errors)`
  - `public static string Cli.Scenario(string name)`, `public static string Cli.Built(string project)` — public members of the existing `internal static class Cli`, as `Cli.Plant` already is (test-only)

- [ ] **Step 1: Write the failing test**

First, the fixtures. `tests/Millrace.Cli.Tests/Scenarios/minimal.json`:

```json
{
  "plant": "../Plants/minimal.json",
  "duration": 5,
  "timeline": [
    { "at": 2, "write": "FEED.Enabled", "value": false }
  ]
}
```

`tests/Millrace.Cli.Tests/Scenarios/no-duration.json`:

```json
{
  "plant": "../Plants/minimal.json"
}
```

`tests/Millrace.Cli.Tests/Scenarios/broken-plant.json`:

```json
{
  "plant": "../Plants/broken.json",
  "duration": 1
}
```

`tests/Millrace.Cli.Tests/Scenarios/missing-plant.json`:

```json
{
  "plant": "../Plants/no-such-plant.json",
  "duration": 1
}
```

`tests/Millrace.Cli.Tests/Scenarios/sample.json`:

```json
{
  "plant": "../Plants/sample.json",
  "duration": 1
}
```

In `tests/Millrace.Cli.Tests/Millrace.Cli.Tests.csproj`, add beside the `Plants` item:

```xml
    <None Include="Scenarios\**\*.json" CopyToOutputDirectory="PreserveNewest" />
```

In `tests/Millrace.Cli.Tests/Cli.cs`, add two helpers (the second is moved verbatim
from `PluginTests`, so both files can use it):

```csharp
    public static string Scenario(string name) => Path.Combine(AppContext.BaseDirectory, "Scenarios", name);

    /// <summary>tests/&lt;project&gt;/bin/&lt;configuration&gt;/&lt;tfm&gt;/&lt;project&gt;.dll, found from this assembly's own output directory.</summary>
    public static string Built(string project)
    {
        var output = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        string tfm = output.Name;
        string configuration = output.Parent!.Name;
        string tests = output.Parent.Parent!.Parent!.Parent!.FullName;
        return Path.Combine(tests, project, "bin", configuration, tfm, project + ".dll");
    }
```

and in `tests/Millrace.Cli.Tests/PluginTests.cs` delete its private `Built` method
and its XML comment, leaving the two fields as:

```csharp
    private static readonly string Sample = Cli.Built("Millrace.Cli.Tests.SampleModule");
    private static readonly string Clash = Cli.Built("Millrace.Cli.Tests.ClashModule");
```

`tests/Millrace.Cli.Tests/RunCommandTests.cs`:

```csharp
using System.Text.Json;

namespace Millrace.Cli.Tests;

public class RunCommandTests
{
    private static readonly string Sample = Cli.Built("Millrace.Cli.Tests.SampleModule");

    private static string TempPath(string extension) =>
        Path.Combine(Path.GetTempPath(), $"millrace-run-{Guid.NewGuid():N}{extension}");

    [Fact]
    public void AScenarioRunsAndPrintsItsEventLog()
    {
        CliRun run = Cli.Run("run", Cli.Scenario("minimal.json"));

        Assert.Equal(ExitCodes.Ok, run.ExitCode);
        Assert.Empty(run.Err);
        Assert.Contains("FEED.Enabled  WRITE  Set to false.", run.Out, StringComparison.Ordinal);
        Assert.DoesNotContain('\r', run.Out);
    }

    [Fact]
    public void OutTakesTheLogAndStandardOutputStaysEmpty()
    {
        string file = TempPath(".log");
        try
        {
            CliRun run = Cli.Run("run", Cli.Scenario("minimal.json"), "--out", file);

            Assert.Equal(ExitCodes.Ok, run.ExitCode);
            Assert.Empty(run.Out);
            Assert.Empty(run.Err);
            Assert.Contains("FEED.Enabled  WRITE  Set to false.", File.ReadAllText(file), StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void AnExpectedLogThatMatchesPrintsTheMatchLine()
    {
        string golden = TempPath(".log");
        try
        {
            Assert.Equal(ExitCodes.Ok, Cli.Run("run", Cli.Scenario("minimal.json"), "--out", golden).ExitCode);

            CliRun run = Cli.Run("run", Cli.Scenario("minimal.json"), "--expect", golden);

            Assert.Equal(ExitCodes.Ok, run.ExitCode);
            Assert.Empty(run.Err);
            Assert.StartsWith($"Matched {golden} (", run.Out, StringComparison.Ordinal);
            Assert.EndsWith(" events).\n", run.Out, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(golden);
        }
    }

    [Fact]
    public void AnExpectedLogThatDiffersIsExitFourAndWritesTheActualBesideIt()
    {
        string golden = TempPath(".log");
        File.WriteAllText(golden, "06:00:00.000  NOPE  WRONG  Not this.\n");
        try
        {
            CliRun run = Cli.Run("run", Cli.Scenario("minimal.json"), "--expect", golden);

            Assert.Equal(ExitCodes.LogMismatch, run.ExitCode);
            Assert.Empty(run.Out);
            Assert.Contains("The logs differ at line 1.", run.Err, StringComparison.Ordinal);
            Assert.Contains("expected (1 lines):", run.Err, StringComparison.Ordinal);
            Assert.Contains($"The actual log is at '{golden}.actual'.", run.Err, StringComparison.Ordinal);
            Assert.Contains("FEED.Enabled  WRITE  Set to false.", File.ReadAllText(golden + ".actual"), StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(golden);
            File.Delete(golden + ".actual");
        }
    }

    [Fact]
    public void AnInvalidScenarioIsExitOneWithItsDiagnosticsOnStandardError()
    {
        CliRun run = Cli.Run("run", Cli.Scenario("no-duration.json"));

        Assert.Equal(ExitCodes.PlantInvalid, run.ExitCode);
        Assert.Empty(run.Out);
        Assert.Contains("MR202 $.duration", run.Err, StringComparison.Ordinal);
        Assert.Contains("  Fix: ", run.Err, StringComparison.Ordinal);
        Assert.EndsWith("1 error in no-duration.json\n", run.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void AnInvalidPlantIsExitOneAndLeadsWithMr205()
    {
        CliRun run = Cli.Run("run", Cli.Scenario("broken-plant.json"));

        Assert.Equal(ExitCodes.PlantInvalid, run.ExitCode);
        Assert.Empty(run.Out);
        Assert.StartsWith("MR205 $.plant", run.Err, StringComparison.Ordinal);
        Assert.Contains("MR102", run.Err, StringComparison.Ordinal);
        Assert.EndsWith("3 errors in broken-plant.json\n", run.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void AMissingScenarioFileIsExitThree()
    {
        CliRun run = Cli.Run("run", Cli.Scenario("no-such-scenario.json"));

        Assert.Equal(ExitCodes.Unreadable, run.ExitCode);
        Assert.Empty(run.Out);
        Assert.Contains("Cannot read", run.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void AMissingPlantFileIsExitThree()
    {
        CliRun run = Cli.Run("run", Cli.Scenario("missing-plant.json"));

        Assert.Equal(ExitCodes.Unreadable, run.ExitCode);
        Assert.Contains("no-such-plant.json", run.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void AMissingGoldenFileIsExitThree()
    {
        CliRun run = Cli.Run("run", Cli.Scenario("minimal.json"), "--expect", TempPath(".log"));

        Assert.Equal(ExitCodes.Unreadable, run.ExitCode);
        Assert.Empty(run.Out);
        Assert.Contains("Cannot read", run.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnwritableOutIsExitThree()
    {
        string file = Path.Combine(Path.GetTempPath(), $"millrace-run-{Guid.NewGuid():N}", "log.txt");

        CliRun run = Cli.Run("run", Cli.Scenario("minimal.json"), "--out", file);

        Assert.Equal(ExitCodes.Unreadable, run.ExitCode);
        Assert.StartsWith("Cannot write '", run.Err, StringComparison.Ordinal);
        Assert.DoesNotContain("   at ", run.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEmptyPathIsExitThreeNotACrash()
    {
        CliRun run = Cli.Run("run", "");

        Assert.Equal(ExitCodes.Unreadable, run.ExitCode);
        Assert.Empty(run.Out);
        Assert.StartsWith("Cannot read '': ", run.Err, StringComparison.Ordinal);
        Assert.DoesNotContain("   at ", run.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void JsonFormatCarriesTheEventsAndTheTicks()
    {
        CliRun run = Cli.Run("run", Cli.Scenario("minimal.json"), "--format", "json");

        Assert.Equal(ExitCodes.Ok, run.ExitCode);
        Assert.Empty(run.Err);
        using JsonDocument document = JsonDocument.Parse(run.Out);
        JsonElement root = document.RootElement;
        Assert.True(root.GetProperty("ok").GetBoolean());
        Assert.EndsWith("minimal.json", root.GetProperty("scenario").GetString(), StringComparison.Ordinal);
        Assert.EndsWith("minimal.json", root.GetProperty("plant").GetString(), StringComparison.Ordinal);
        Assert.Equal(500L, root.GetProperty("ticks").GetInt64());
        Assert.False(root.TryGetProperty("match", out _));
        Assert.Empty(root.GetProperty("diagnostics").EnumerateArray());
        JsonElement written = root.GetProperty("events").EnumerateArray()
            .Single(e => e.GetProperty("source").GetString() == "FEED.Enabled");
        Assert.Equal(["tick", "time", "source", "code", "message"], written.EnumerateObject().Select(p => p.Name));
        Assert.Equal(200L, written.GetProperty("tick").GetInt64());
        Assert.Equal("WRITE", written.GetProperty("code").GetString());
    }

    [Fact]
    public void JsonFormatOfAnInvalidScenarioIsOkFalseOnStandardOutput()
    {
        CliRun run = Cli.Run("run", Cli.Scenario("no-duration.json"), "--format", "json");

        Assert.Equal(ExitCodes.PlantInvalid, run.ExitCode);
        Assert.Empty(run.Err);
        using JsonDocument document = JsonDocument.Parse(run.Out);
        JsonElement root = document.RootElement;
        Assert.False(root.GetProperty("ok").GetBoolean());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("plant").ValueKind);
        Assert.Equal(0L, root.GetProperty("ticks").GetInt64());
        Assert.Empty(root.GetProperty("events").EnumerateArray());
        JsonElement diagnostic = Assert.Single(root.GetProperty("diagnostics").EnumerateArray());
        Assert.Equal("MR202", diagnostic.GetProperty("code").GetString());
        Assert.Equal("error", diagnostic.GetProperty("severity").GetString());
    }

    [Fact]
    public void JsonFormatWithExpectCarriesTheMatch()
    {
        string golden = TempPath(".log");
        File.WriteAllText(golden, "06:00:00.000  NOPE  WRONG  Not this.\n");
        try
        {
            CliRun run = Cli.Run("run", Cli.Scenario("minimal.json"), "--expect", golden, "--format", "json");

            Assert.Equal(ExitCodes.LogMismatch, run.ExitCode);
            Assert.Empty(run.Err);
            using JsonDocument document = JsonDocument.Parse(run.Out);
            JsonElement root = document.RootElement;
            Assert.True(root.GetProperty("ok").GetBoolean());
            Assert.False(root.GetProperty("match").GetBoolean());
            Assert.Equal(1, root.GetProperty("firstDifferentLine").GetInt32());
            Assert.Equal(golden + ".actual", root.GetProperty("actual").GetString());
        }
        finally
        {
            File.Delete(golden);
            File.Delete(golden + ".actual");
        }
    }

    [Fact]
    public void APlantThatNeedsAPluginRunsWithAssembly()
    {
        CliRun with = Cli.Run("run", Cli.Scenario("sample.json"), "--assembly", Sample);
        CliRun without = Cli.Run("run", Cli.Scenario("sample.json"));

        Assert.Equal(ExitCodes.Ok, with.ExitCode);
        Assert.Equal(ExitCodes.PlantInvalid, without.ExitCode);
        Assert.Contains("MR205", without.Err, StringComparison.Ordinal);
        Assert.Contains("MR102", without.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void TheHelpListsRunAndItsExitCode()
    {
        CliRun run = Cli.Run("help");

        Assert.Equal(ExitCodes.Ok, run.ExitCode);
        Assert.Contains("run <scenario.json>", run.Out, StringComparison.Ordinal);
        Assert.Contains("4 the event log differs", run.Out, StringComparison.Ordinal);
    }

    [Fact]
    public void TheCommandsHelpListsItsOptions()
    {
        CliRun run = Cli.Run("run", "--help");

        Assert.Equal(ExitCodes.Ok, run.ExitCode);
        Assert.Contains("--expect <golden.log>", run.Out, StringComparison.Ordinal);
        Assert.Contains("--out <file>", run.Out, StringComparison.Ordinal);
        Assert.Contains("--assembly <path>", run.Out, StringComparison.Ordinal);
        Assert.DoesNotContain("--time-step", run.Out, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("run")]
    [InlineData("run a.json b.json")]
    [InlineData("run a.json --expect")]
    [InlineData("run a.json --time-step 5")]
    public void MalformedInvocationsAreUsageErrors(string line)
    {
        CliRun run = Cli.Run(line.Split(' '));

        Assert.Equal(ExitCodes.Usage, run.ExitCode);
        Assert.Empty(run.Out);
        Assert.NotEmpty(run.Err);
    }
}
```

Run: `dotnet test tests/Millrace.Cli.Tests --nologo --filter FullyQualifiedName~RunCommandTests`
Expected: build FAILS — `ExitCodes.LogMismatch` does not exist.

- [ ] **Step 2: Add the project reference and the exit code**

In `src/Millrace.Cli/Millrace.Cli.csproj`, add to the project reference group:

```xml
    <ProjectReference Include="..\Millrace.Scenarios\Millrace.Scenarios.csproj" />
```

In `src/Millrace.Cli/ExitCodes.cs`, add:

```csharp
    /// <summary>The scenario ran, and its event log differs from the one <c>--expect</c> named.</summary>
    public const int LogMismatch = 4;
```

- [ ] **Step 3: Add the option, the command row and the help line**

In `src/Millrace.Cli/CommandTable.cs`, add the option beside the others:

```csharp
    public static readonly OptionSpec Expect = new(
        "--expect", "golden.log", "Compare the event log with this file; exit 4 if they differ.");
```

add the row at the end of `All`:

```csharp
        new(["run"], "scenario.json", "Run a scenario against its plant and print the event log.", [Expect, Out, Format, Assembly], Commands.RunScenario.Run),
```

and replace the exit-code line in `GeneralHelp`:

```csharp
        lines.Add("Exit codes: 0 success; 1 the plant or scenario has errors; 2 usage error; " +
                  "3 a file or assembly could not be read; 4 the event log differs from --expect.");
```

- [ ] **Step 4: Extract the shared reading and reporting**

In `src/Millrace.Cli/Commands/PlantFile.cs`, replace `TryBuild`'s reading block and
`ReportInvalid` so the two pieces `run` needs are public, leaving every byte the
existing commands write unchanged:

```csharp
    /// <summary>Exit code 0 with a loaded plant and its built simulation, or a non-zero code after reporting why.</summary>
    public static int TryBuild(CliContext context, out LoadResult? result, out Simulation? simulation)
    {
        result = null;
        simulation = null;
        string path = context.CommandLine.Argument!;
        if (!TryRead(context, path, out string json))
        {
            return ExitCodes.Unreadable;
        }

        result = PlantLoader.Load(json, context.Catalogue, new LoadOptions { TimeStep = context.TimeStep });
        if (!result.IsValid)
        {
            ReportInvalid(context, path, result);
            return ExitCodes.PlantInvalid;
        }

        simulation = result.Builder!.Build();
        return ExitCodes.Ok;
    }

    /// <summary>Reads a file, reporting <c>Cannot read '…'</c> on standard error and returning false.</summary>
    public static bool TryRead(CliContext context, string path, out string text)
    {
        try
        {
            text = File.ReadAllText(path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            context.Err.Write($"Cannot read '{path}': {ex.Message}\n");
            text = string.Empty;
            return false;
        }
    }

    /// <summary>Writes rendered diagnostics and a count line to standard error. Text output only.</summary>
    public static void ReportDiagnostics(CliContext context, string path, string diagnostics, int errors)
    {
        context.Err.Write(diagnostics);
        context.Err.Write(string.Create(
            CultureInfo.InvariantCulture, $"\n{errors} error{(errors == 1 ? string.Empty : "s")} in {Path.GetFileName(path)}\n"));
    }

    private static void ReportInvalid(CliContext context, string path, LoadResult result)
    {
        if (context.Json)
        {
            context.Out.Write(ValidationJson(path, result, summary: null));
            return;
        }

        ReportDiagnostics(context, path, result.ToText(), result.Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error));
    }
```

- [ ] **Step 5: Write the command**

`src/Millrace.Cli/Commands/RunScenario.cs`:

```csharp
using System.Globalization;
using System.Text.Json;
using Millrace.Configuration;
using Millrace.Core.Catalogue;
using Millrace.Core.Logging;
using Millrace.Scenarios;

namespace Millrace.Cli.Commands;

/// <summary>
/// Runs a scenario against the plant it names. The command is a shell:
/// <c>ScenarioRunner</c> decides what happened and <c>GoldenLog</c> decides
/// whether it matched, so a test and the command line agree by construction.
/// </summary>
internal static class RunScenario
{
    public static int Run(CliContext context)
    {
        string path = context.CommandLine.Argument!;
        if (!PlantFile.TryRead(context, path, out string json))
        {
            return ExitCodes.Unreadable;
        }

        ScenarioParseResult parsed = ScenarioLoader.Parse(json);
        if (parsed.Scenario is null)
        {
            return Invalid(context, path, null, parsed.Diagnostics, parsed.ToText());
        }

        string plantPath = parsed.Scenario.ResolvePlantPath(path);
        if (!PlantFile.TryRead(context, plantPath, out string plantJson))
        {
            return ExitCodes.Unreadable;
        }

        ScenarioRunResult result = ScenarioRunner.Run(parsed.Scenario, plantJson, context.Catalogue);
        return result.IsValid
            ? Deliver(context, path, plantPath, result)
            : Invalid(context, path, plantPath, result.Diagnostics, result.ToText());
    }

    private static int Deliver(CliContext context, string path, string plantPath, ScenarioRunResult result)
    {
        string log = GoldenLog.Normalise(result.Events!.ToText());
        string? expect = context.CommandLine.Single(CommandTable.Expect);
        LogComparison? comparison = null;
        string? actualPath = null;

        if (expect is not null)
        {
            if (!PlantFile.TryRead(context, expect, out string golden))
            {
                return ExitCodes.Unreadable;
            }

            comparison = GoldenLog.Compare(golden, log);
            if (!comparison.Matched)
            {
                actualPath = expect + ".actual";
                if (!TryWrite(context, actualPath, log))
                {
                    return ExitCodes.Unreadable;
                }
            }
        }

        string payload = context.Json
            ? Json(path, plantPath, result, comparison, actualPath, result.Diagnostics)
            : log;
        string? outPath = context.CommandLine.Single(CommandTable.Out);
        if (outPath is not null)
        {
            if (!TryWrite(context, outPath, payload))
            {
                return ExitCodes.Unreadable;
            }
        }
        else if (context.Json || expect is null)
        {
            context.Out.Write(payload);
        }

        if (comparison is null)
        {
            return ExitCodes.Ok;
        }

        if (comparison.Matched)
        {
            if (!context.Json)
            {
                context.Out.Write(string.Create(
                    CultureInfo.InvariantCulture, $"Matched {expect} ({result.Summary!.Events} events).\n"));
            }

            return ExitCodes.Ok;
        }

        if (!context.Json)
        {
            context.Err.Write(comparison.Report);
            context.Err.Write($"The actual log is at '{actualPath}'.\n");
        }

        return ExitCodes.LogMismatch;
    }

    private static int Invalid(
        CliContext context, string path, string? plantPath, IReadOnlyList<ConfigDiagnostic> diagnostics, string text)
    {
        if (context.Json)
        {
            context.Out.Write(Json(path, plantPath, null, null, null, diagnostics));
            return ExitCodes.PlantInvalid;
        }

        PlantFile.ReportDiagnostics(context, path, text, diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error));
        return ExitCodes.PlantInvalid;
    }

    private static bool TryWrite(CliContext context, string path, string payload)
    {
        try
        {
            File.WriteAllText(path, payload);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            context.Err.Write($"Cannot write '{path}': {ex.Message}\n");
            return false;
        }
    }

    /// <summary><c>ok</c> is "the configuration is sound"; <c>match</c> is "the behaviour is unchanged".</summary>
    private static string Json(
        string scenarioPath,
        string? plantPath,
        ScenarioRunResult? result,
        LogComparison? comparison,
        string? actualPath,
        IReadOnlyList<ConfigDiagnostic> diagnostics)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, CatalogueJson.WriterOptions))
        {
            writer.WriteStartObject();
            writer.WriteBoolean("ok", result is not null);
            writer.WriteString("scenario", scenarioPath);
            if (plantPath is null)
            {
                writer.WriteNull("plant");
            }
            else
            {
                writer.WriteString("plant", plantPath);
            }

            writer.WriteNumber("ticks", result?.Summary?.Ticks ?? 0L);

            writer.WriteStartArray("events");
            if (result?.Events is { } log)
            {
                foreach (SimEventRecord record in log.Records)
                {
                    writer.WriteStartObject();
                    writer.WriteNumber("tick", record.Tick);
                    writer.WriteString("time", record.SimTime.ToString("o", CultureInfo.InvariantCulture));
                    writer.WriteString("source", record.Source);
                    writer.WriteString("code", record.Code);
                    writer.WriteString("message", record.Message);
                    writer.WriteEndObject();
                }
            }

            writer.WriteEndArray();

            if (comparison is not null)
            {
                writer.WriteBoolean("match", comparison.Matched);
                if (!comparison.Matched)
                {
                    writer.WriteNumber("firstDifferentLine", comparison.FirstDifferentLine);
                    writer.WriteString("actual", actualPath!);
                }
            }

            writer.WriteStartArray("diagnostics");
            foreach (ConfigDiagnostic diagnostic in diagnostics)
            {
                writer.WriteStartObject();
                writer.WriteString("code", diagnostic.Code);
                writer.WriteString("severity", CatalogueJson.Camel(diagnostic.Severity.ToString()));
                writer.WriteString("path", diagnostic.Path);
                writer.WriteString("message", diagnostic.Message);
                writer.WriteString("fix", diagnostic.Fix);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return CatalogueJson.Finish(stream);
    }
}
```

- [ ] **Step 6: Run the command tests**

Run: `dotnet test tests/Millrace.Cli.Tests --nologo --filter FullyQualifiedName~RunCommandTests`
Expected: PASS, 21 tests (17 facts + a theory of 4 rows).

`AnInvalidPlantIsExitOneAndLeadsWithMr205` expects **3 errors** — one `MR205`
plus the two in `tests/Millrace.Cli.Tests/Plants/broken.json`. Measure it; if the
count differs, report the diagnostics the command printed and correct the test.

- [ ] **Step 7: Run everything**

Run: `dotnet build Millrace.sln -c Release --nologo` — expect `0 Warning(s)`, `0 Error(s)`.
Run: `dotnet test Millrace.sln --nologo`
Expected: PASS, 914 + 21 = 935 tests.

Then walk the golden workflow by hand and paste the output in the report. Use
the CLI test corpus, whose scenarios and plants are both **in the source tree**
(`tests/Millrace.Cli.Tests/Scenarios/minimal.json` names `../Plants/minimal.json`,
which is a committed file). The `Millrace.Scenarios.Tests` corpus cannot be used
here: R62 links its plants into the build output only, so a scenario there
resolves its plant to a path that does not exist in the source tree and the
command would exit 3.

```bash
dotnet run --project src/Millrace.Cli -- run tests/Millrace.Cli.Tests/Scenarios/minimal.json --out /tmp/millrace-run-check.log
```

```bash
dotnet run --project src/Millrace.Cli -- run tests/Millrace.Cli.Tests/Scenarios/minimal.json --expect /tmp/millrace-run-check.log
```

Expected: the first prints nothing and exits 0; the second prints
`Matched /tmp/millrace-run-check.log (<N> events).` and exits 0. Check both with
`echo $?`. That the *committed* goldens under `tests/Millrace.Scenarios.Tests/Golden`
still match is what `CorpusTests` asserts, in the test run above.

- [ ] **Step 8: Commit**

```bash
git add src/Millrace.Cli tests/Millrace.Cli.Tests
```

```bash
git commit -m "$(cat <<'MSG'
feat(cli): add `millrace run`, with exit 4 for a changed event log

A scenario runs from the command line and prints its log; --expect
compares it with a committed golden and exits 4, writing the actual log
beside it, when the behaviour changed. Exit 1 still means the
configuration is broken: a script can tell the two apart.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
MSG
)"
```

---

### Task 9: Documentation — the generated reference, the format, the architecture

Three pages and a README paragraph. Two of them are generated from code and
pinned by a golden test, so they cannot drift; one is hand-written because it
explains a workflow, not a table.

`DiagnosticsReference.Render` becomes parametric (R60) so `Millrace.Scenarios` can
render its own page without `Millrace.Configuration` ever seeing it. The
configuration page must come out **byte-identical** — its golden test is the
proof.

The stale "not yet implemented" text for tick phases 4 and 5 is corrected here:
both shipped in plan 4, and phase 1 now has two halves worth naming.

**Files:**
- Modify: `src/Millrace.Configuration/DiagnosticsReference.cs`
- Create: `src/Millrace.Scenarios/ScenarioDiagnosticsReference.cs`
- Test: `tests/Millrace.Scenarios.Tests/ScenarioDiagnosticsReferenceTests.cs`
- Modify: `tests/Millrace.Configuration.Tests/DiagnosticsReferenceTests.cs`
- Create: `docs/scenario-diagnostics.md` (generated), `docs/scenarios.md`
- Modify: `docs/architecture.md`, `README.md`

**Interfaces:**
- Consumes: `ConfigDiagnostics.All`, `ScenarioDiagnostics.All`, `DiagnosticInfo`, `Golden.Assert`.
- Produces:
  - `public static string DiagnosticsReference.Render(string title, IReadOnlyList<DiagnosticInfo> codes, string? introduction = null, string? trailer = null)`
  - `public static string DiagnosticsReference.Render()` — unchanged output
  - `public static class ScenarioDiagnosticsReference { public static string Render(); }`

- [ ] **Step 1: Write the failing tests**

`tests/Millrace.Scenarios.Tests/ScenarioDiagnosticsReferenceTests.cs`:

```csharp
using Millrace.Tests.Shared;

namespace Millrace.Scenarios.Tests;

public class ScenarioDiagnosticsReferenceTests
{
    [Fact]
    public void TheCommittedReferencePageIsCurrent()
    {
        // Two levels up from this file is the repository root. Regenerate with MILLRACE_UPDATE_GOLDEN=1.
        Golden.Assert("../../docs/scenario-diagnostics.md", ScenarioDiagnosticsReference.Render());
    }

    [Fact]
    public void EveryCodeHasASection()
    {
        string page = ScenarioDiagnosticsReference.Render();

        Assert.All(ScenarioDiagnostics.All, d => Assert.Contains($"## {d.Code} — {d.Title}\n", page, StringComparison.Ordinal));
        Assert.StartsWith("# Scenario diagnostics\n", page, StringComparison.Ordinal);
        Assert.Contains("| MR206 | Action does not bind to the plant |\n", page, StringComparison.Ordinal);
        Assert.EndsWith("\n", page, StringComparison.Ordinal);
        Assert.DoesNotContain('\r', page);
    }
}
```

Append to `tests/Millrace.Configuration.Tests/DiagnosticsReferenceTests.cs`, inside
`public class DiagnosticsReferenceTests`:

```csharp
    [Fact]
    public void APageMayBeRenderedFromNothingButATitleAndCodes()
    {
        string page = DiagnosticsReference.Render(
            "Example diagnostics",
            [new DiagnosticInfo("MR900", "Something is wrong", "It went wrong.")]);

        Assert.Equal(
            "# Example diagnostics\n\n"
            + "| Code | Meaning |\n|---|---|\n| MR900 | Something is wrong |\n\n"
            + "## MR900 — Something is wrong\n\nIt went wrong.\n\n",
            page);
    }
```

Run: `dotnet test tests/Millrace.Scenarios.Tests --nologo --filter FullyQualifiedName~ScenarioDiagnosticsReferenceTests`
Expected: build FAILS — `ScenarioDiagnosticsReference` does not exist.

- [ ] **Step 2: Make `DiagnosticsReference.Render` parametric**

Replace `src/Millrace.Configuration/DiagnosticsReference.cs` entirely. The two
constants are the existing page's prose, moved verbatim — do not reword them,
or the golden test fails and the diff will not say why.

```csharp
using System.Text;

namespace Millrace.Configuration;

/// <summary>
/// Renders a diagnostics reference page from a code table, so a page cannot
/// drift from the codes. <see cref="Render()"/> is
/// docs/configuration-diagnostics.md; the parametric overload is how another
/// family of codes — scenarios, say — renders its own page without this
/// assembly having to know about it.
/// </summary>
public static class DiagnosticsReference
{
    private const string ConfigurationIntroduction =
        "<!-- Generated from ConfigDiagnostics.All by DiagnosticsReference.Render(). Do not edit by hand:\n" +
        "     run the Millrace.Configuration tests with MILLRACE_UPDATE_GOLDEN=1, read the result, commit it. -->\n\n" +
        "`millrace validate` and `PlantLoader.Load` report every problem in a plant file as a diagnostic with four\n" +
        "parts: a **code**, a **JSON path** into the file (`$.components[3].parameters.motor.ratedPowerW`), a\n" +
        "**message** saying what is wrong, and a **fix** saying what to do. A diagnostic without a fix cannot be\n" +
        "constructed.\n\n" +
        "The loader works in stages — parse, structure, references, instantiate, wire, build — and stops at the\n" +
        "end of the first stage that found an error, having reported *every* error that stage could find. Fixing\n" +
        "what is reported may therefore reveal errors from a later stage.\n\n";

    private const string ConfigurationTrailer =
        "## MR001–MR011 — plant validation\n\n" +
        "Codes below MR100 come from `SimulationBuilder.Validate()` and mean the same for a plant built in code:\n" +
        "duplicate ids, unconnected required inputs, algebraic loops, belts too fast for their cells, incompatible\n" +
        "flow links, tag conflicts. The loader passes them through with the path of the first component involved;\n" +
        "their message is split at its first sentence into message and fix. See `docs/architecture.md`.\n";

    /// <summary>docs/configuration-diagnostics.md, unchanged.</summary>
    public static string Render() =>
        Render("Configuration diagnostics", ConfigDiagnostics.All, ConfigurationIntroduction, ConfigurationTrailer);

    /// <summary>
    /// A title, an optional introduction, the table of codes, a section per
    /// code, and an optional trailer. Every part ends in a blank line, so the
    /// page is valid Markdown whichever parts are given.
    /// </summary>
    public static string Render(
        string title,
        IReadOnlyList<DiagnosticInfo> codes,
        string? introduction = null,
        string? trailer = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(codes);

        var page = new StringBuilder();
        page.Append("# ").Append(title).Append("\n\n");
        if (!string.IsNullOrEmpty(introduction))
        {
            page.Append(introduction);
        }

        page.Append("| Code | Meaning |\n|---|---|\n");
        foreach (DiagnosticInfo info in codes)
        {
            page.Append("| ").Append(info.Code).Append(" | ").Append(info.Title).Append(" |\n");
        }

        page.Append('\n');
        foreach (DiagnosticInfo info in codes)
        {
            page.Append("## ").Append(info.Code).Append(" — ").Append(info.Title).Append("\n\n");
            page.Append(info.Explanation).Append("\n\n");
        }

        if (!string.IsNullOrEmpty(trailer))
        {
            page.Append(trailer);
        }

        return page.ToString();
    }
}
```

- [ ] **Step 3: Write the scenario page renderer**

`src/Millrace.Scenarios/ScenarioDiagnosticsReference.cs`:

```csharp
using Millrace.Configuration;

namespace Millrace.Scenarios;

/// <summary>Renders docs/scenario-diagnostics.md from <see cref="ScenarioDiagnostics.All"/>, so the page cannot drift from the codes.</summary>
public static class ScenarioDiagnosticsReference
{
    private const string Introduction =
        "<!-- Generated from ScenarioDiagnostics.All by ScenarioDiagnosticsReference.Render(). Do not edit by hand:\n" +
        "     run the Millrace.Scenarios tests with MILLRACE_UPDATE_GOLDEN=1, read the result, commit it. -->\n\n" +
        "`millrace run`, `ScenarioLoader.Parse` and `ScenarioRunner.Run` report every problem in a scenario file as a\n" +
        "diagnostic with four parts: a **code**, a **JSON path** into the file (`$.timeline[1].args.amount`), a\n" +
        "**message** saying what is wrong, and a **fix** saying what to do. It is the same `ConfigDiagnostic` a\n" +
        "plant file's problems arrive as, and a diagnostic without a fix cannot be constructed.\n\n" +
        "Checking happens in two passes. `ScenarioLoader.Parse` is structural and needs no plant: MR200 to\n" +
        "MR204, and MR203 for a time that is not on the step the scenario itself declared. `ScenarioRunner`\n" +
        "then loads the plant and binds every action against the built simulation: MR205, MR206, and MR203\n" +
        "against the step the plant actually runs at. **Every check happens before tick 0**, so a scenario that\n" +
        "is wrong never produces a partial event log.\n\n";

    private const string Trailer =
        "## MR100–MR112 — the plant's own diagnostics\n\n" +
        "A scenario names a plant, and that plant is loaded by the same loader `millrace validate` uses. When the\n" +
        "plant has errors of its own they follow the MR205 line unchanged, with their codes, their paths and\n" +
        "their fixes. See [configuration diagnostics](configuration-diagnostics.md).\n";

    /// <summary>The page, ending in a newline.</summary>
    public static string Render() =>
        DiagnosticsReference.Render("Scenario diagnostics", ScenarioDiagnostics.All, Introduction, Trailer);
}
```

- [ ] **Step 4: Generate the page, read it, and check the configuration page did not move**

```bash
MILLRACE_UPDATE_GOLDEN=1 dotnet test tests/Millrace.Scenarios.Tests --nologo --filter FullyQualifiedName~ScenarioDiagnosticsReferenceTests
```

```bash
git status --short docs
```

Expected: `docs/scenario-diagnostics.md` is new and
**`docs/configuration-diagnostics.md` is not modified**. If it is, the prose
constants in Step 2 differ from the original; `git diff docs/configuration-diagnostics.md`
says exactly where, and the constants are what changes, never the golden.

Read `docs/scenario-diagnostics.md` before committing it. It must have a
`# Scenario diagnostics` title, a seven-row table, a section per code, and the
`MR100–MR112` trailer.

- [ ] **Step 5: Write `docs/scenarios.md`**

````markdown
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
```

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
| `timeStepMs` | no | A number greater than zero; fractions are allowed. Overrides `defaults.timeStepMs`. |
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

`ScenarioRecorder` implements `IActionRecorder`, the seam every action that
takes effect passes through — a command bus write, a scenario write, a direct
`InjectFaultAt`. Attach it, drive the plant however you like, and ask for the
file:

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
````

- [ ] **Step 6: Correct and extend `docs/architecture.md`**

In the **The tick** section, replace item 1:

```markdown
1. **Drain events** due at or before this tick, ordered by `(dueTick, sequence)`.
   The sequence number is monotonic, so events due on the same tick always fire
   in the order they were scheduled.
```

with:

```markdown
1. **Apply queued writes, then drain events** due at or before this tick.
   Writes queued from outside land first, in enqueue order; then scheduled
   events, ordered by `(dueTick, sequence)`. The sequence number is monotonic,
   so events due on the same tick always fire in the order they were scheduled.
   This is what lets `Simulation.WriteAt` and `InjectFaultAt` share one rule:
   the time you name is the tick at which the plant sees it.
```

and replace items 4 and 5:

```markdown
4. **Publish the I/O image** — the snapshot external readers see. Not yet
   implemented.
5. **Emit the tick frame.** Not yet implemented; the event log is already
   appended during evaluation.
```

with:

```markdown
4. **Publish the I/O image.** Every binding is captured into a fresh array,
   diffed against the previous snapshot into a `DirtyMask`, and published with
   a volatile swap, so a reader on any thread sees a whole tick's values or
   none of them.
5. **Emit the tick frame.** The published array, its dirty mask and this tick's
   event-log records are wrapped in an immutable `TickFrame` and handed to the
   attached `ITickFrameSink`, if there is one, in a single non-blocking call.
```

In the **The I/O image** section, after the sentence ending `so the write
timeline is in the event stream.`, add:

```markdown
`Simulation.WriteAt(fromStart, tag, value)` is the other way in: it resolves
and checks the tag when the write is *scheduled* — unknown tag, read-only tag,
kind mismatch, all at the call site — and applies it during the phase-1 event
drain, so the value lands on exactly the tick named rather than the one after.
Whichever way a write arrives, it is reported to the simulation's one
`IActionRecorder` with the tick it landed on, as fault injections and
clearances are.
```

At the end of the file, after the **Catalogue, schema and loader** section, add:

```markdown
## Scenarios and replay

A scenario is a JSON file: a plant to run, the three engine overrides, a
duration, and a timeline of writes, fault injections and clearances. `Millrace.Scenarios`
sees `Millrace.Core` and `Millrace.Configuration` and nothing else.

    ScenarioLoader.Parse   structural: shape, types, ranges — no plant, no file system
    ScenarioRunner.Run     load the plant, bind every action, then and only then tick
    ScenarioJson.Write     a Scenario back to deterministic JSON
    GoldenLog.Compare      where this run's log left the committed one
    ScenarioRecorder       a live run, as a scenario

The two passes are the point. Everything that can be known without a plant is
`MR200`–`MR204`; everything that needs one is `MR205` (the plant has its own
errors, which follow unchanged) and `MR206` (this action names something the
plant does not have). Both run to completion and collect every problem, and
`RunFor` is not called if there is a single one — **a bad scenario never
produces a partial log**.

The event log is the regression artifact, and `EventLog.ToText()` is its format
by contract. `millrace run --expect golden.log` exits 4, not 1, when the two differ:
"the configuration is broken" and "the behaviour changed" are different
questions, and a script should not have to guess which it got.

Replay works because a scheduled write lands on the tick it names.
`TagImage.Write` queues for phase 1 of the *next* tick, which is right for a
live command arriving from outside; a replayed write must land where the
original landed, so `Simulation.WriteAt` applies through `TagImage.ApplyNow`
during the event drain, logging the identical `WRITE` record. `IActionRecorder`
watches all three landing sites — the queued-write drain, `ApplyNow` and
`FaultEvent.Apply` — so a recording captures actions by where they took effect,
not by where they came from. `Millrace.Realtime`'s `ICommandRecorder` is a different
thing and stays: it also sees commands the bus *rejected*, which is an audit
trail, not a replay.

See [scenarios](scenarios.md) for the file format and the golden workflow.
```

- [ ] **Step 7: Update `README.md`**

Replace the last paragraph of **Status**:

```markdown
Scenarios and replay, the control blocks, and the reference samples are
planned.
```

with:

```markdown
Scenarios are files too: a plant, the engine overrides, a duration and a
timeline of writes and fault injections, replayed by `millrace run` against a
committed golden event log, and recordable from a live run. The control blocks
and the reference samples are planned.
```

In the **Command line** block, which already has four, add a fifth line:

```markdown
dotnet run --project src/Millrace.Cli -- run scenario.json --expect golden.log  # replay a scenario; exit 4 if the log changed
```

and replace the closing sentence:

```markdown
See [authoring a component](docs/authoring-a-component.md) and the
[configuration diagnostics](docs/configuration-diagnostics.md).
```

with:

````markdown
A scenario over that plant looks like this:

```json
{
  "plant": "plant.json",
  "seed": 42,
  "duration": 120,
  "timeline": [
    { "at": 5,  "write": "CV001.Start", "value": true },
    { "at": 30, "fault": "CV001.Motor", "id": "thermal-bias", "args": { "amount": 0.8 } }
  ]
}
```

See [scenarios](docs/scenarios.md), [authoring a component](docs/authoring-a-component.md),
the [configuration diagnostics](docs/configuration-diagnostics.md) and the
[scenario diagnostics](docs/scenario-diagnostics.md).
````

- [ ] **Step 8: Check the prose against the source**

Every claim in the three documents is checkable. Verify these and report any
that were wrong **before** committing, correcting the prose, never the code:

- `docs/scenarios.md` says `5.0` does not fit an `Int64` tag. Confirm with the
  Task 3 test `NumbersKeepTheShapeTheFileWroteThem` and the Task 5 test
  `AValueOfTheWrongKindIsMr206`.
- `docs/scenarios.md` says the engine's defaults are seed 0,
  `2026-01-01T00:00:00Z` and 10 ms. Check `src/Millrace.Core/Time/SimulationOptions.cs`.
- `docs/architecture.md` phase 4 says "diffed … into a `DirtyMask` … published
  with a volatile swap". Check `TagImage.Publish`.
- `docs/architecture.md` says `Millrace.Scenarios` references `Millrace.Core` and
  `Millrace.Configuration` only. Check `src/Millrace.Scenarios/Millrace.Scenarios.csproj`.
- The `README.md` scenario example must run. Save the plant example and the
  scenario example beside each other as real files in a scratch directory and
  run `millrace run` on the pair; the scenario must not report a diagnostic. If it
  does, fix the example, not the loader. (The README plant has no `CV001.Motor`
  fault target unless the conveyor is in it — it is; `CV001` is a `conveyor`.)

- [ ] **Step 9: Run everything**

Run: `dotnet build Millrace.sln -c Release --nologo` — expect `0 Warning(s)`, `0 Error(s)`.
Run: `dotnet test Millrace.sln --nologo`
Expected: PASS, 935 + 3 = 938 tests.

Run: `git status --short docs` — `docs/configuration-diagnostics.md` must not appear.

- [ ] **Step 10: Commit**

```bash
git add src/Millrace.Configuration/DiagnosticsReference.cs src/Millrace.Scenarios/ScenarioDiagnosticsReference.cs
```

```bash
git add tests/Millrace.Scenarios.Tests/ScenarioDiagnosticsReferenceTests.cs tests/Millrace.Configuration.Tests/DiagnosticsReferenceTests.cs
```

```bash
git add docs/scenarios.md docs/scenario-diagnostics.md docs/architecture.md README.md
```

```bash
git commit -m "$(cat <<'MSG'
docs: add the scenario format, the generated scenario diagnostics and the tick correction

DiagnosticsReference.Render becomes parametric so Millrace.Scenarios renders
its own page without Millrace.Configuration knowing about it; the
configuration page is byte-identical. docs/scenarios.md explains the
format, the `at` rule and the golden workflow, and architecture.md's
"not yet implemented" text for tick phases 4 and 5 — both shipped in plan
4 — is corrected.

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
| a scenario over a valid plant runs from the command line and prints the log; twice gives byte-identical logs | Task 8 Step 7's two commands over `tests/Millrace.Cli.Tests/Scenarios/minimal.json` — `--out /tmp/millrace-run-check.log`, then `--expect /tmp/millrace-run-check.log` giving `Matched … (N events).` and `echo $?` → 0. (Not a `Millrace.Scenarios.Tests` scenario: R62 puts its plants in the build output only.) |
| the committed golden logs still match | `dotnet test --filter FullyQualifiedName~CorpusTests` |
| `--expect` tells "broken" (1) from "changed" (4), and says where | `dotnet test --filter "FullyQualifiedName~RunCommandTests"` |
| every way a scenario can be wrong is reported before tick 0, with a code, a path and a fix; no partial log | `dotnet test --filter "FullyQualifiedName~CorpusTests|FullyQualifiedName~ScenarioParseTests|FullyQualifiedName~ScenarioRunnerTests"` |
| a live run driven by `CommandBus` and direct injections records, writes, parses, replays, and the logs are byte-identical | `dotnet test --filter FullyQualifiedName~RecordAndReplayTests` |
| zero external package references under `src/` | `grep -rn "PackageReference" src/` prints nothing |
| Release build, zero warnings; the original 751 tests still pass | `dotnet build Millrace.sln -c Release --nologo` and `dotnet test Millrace.sln --nologo` |
| the generated pages are current | `dotnet test --filter "FullyQualifiedName~DiagnosticsReferenceTests|FullyQualifiedName~ScenarioDiagnosticsReferenceTests"` |

Record, as plans 1–5a did, a **"Rulings made during execution"** section at the
end of this file for every place the code had to differ from the plan, and a
**"Parked follow-ups"** list from the final review. The spec's own parked list
is unchanged by this plan: dispatcher failure observability, frame pooling, CSV
telemetry export, `--speed` for a paced `millrace run`, and a hand-written JSON
Schema for scenario files.

---

## Self-review

Run once, by the plan's author, after writing it. Findings are fixed inline
above; this section records what was checked and what was found.

### 1. Spec coverage

| Spec section | Requirement | Task |
|---|---|---|
| 1 Scope | `EventLog.ToText()` kept as shipped | Global Constraints; no task touches it |
| 1 Scope | recording hooks `IActionRecorder`, not `ICommandRecorder` | 2 |
| 1 Success 1 | runs from the command line, twice identically | 5 (determinism theory), 8, Completion check |
| 1 Success 2 | `--expect` distinguishes exit 1 from exit 4, says where | 6, 8 |
| 1 Success 3 | every wrongness reported before tick 0, no partial log | 3, 5 |
| 1 Success 4 | record and replay byte-identical | 7 |
| 1 Success 5 | zero packages under `src/` | Global Constraints; checked in 3 and the Completion check |
| 2 The scenario file | keys, types, precedence, `duration` | 3 (`Scenario`, `ScenarioLoader`, `ToLoadOptions`) |
| 2 Actions | write/fault/clear, values by tag kind | 3 (`ScenarioValue`, `ReadAction`), 5 (`Bind`) |
| 2 Rules for `at` | on a tick, `at < duration`, the plant sees it then, file order | 1 (the engine rule), 3 (R53 parse checks), 5 (runner checks) |
| 2 Ids and names | flattened leaf ids, directory tag names | 3 corpus, 5 tests, `docs/scenarios.md` |
| 2 Deliberately absent | no `$schema`, no assertions, no inline plant, no comment | 3 (`TopLevelKeys` has six entries; `MR201` rejects the rest) |
| 3 `ScenarioLoader.Parse` | pure, structural, `ScenarioParseResult` | 3 |
| 3 `Scenario` record and `ResolvePlantPath` | | 3 |
| 3 Plant load, `MR205` | wrapper then the plant's own, unchanged | 5 |
| 3 Scheduling with checks, `MR206`, collect all | | 5 |
| 3 Codes table `MR200`–`MR206` | | 3 (table and `ScenarioDiagnostics.All`), 5 (205, 206) |
| 3 `DiagnosticsReference.Render` parametric; a second page | | 9 (R60) |
| 4 `ScenarioRunner.Run`, `RunSummary`, `Events`, `IsValid` | | 5 |
| 4 `GoldenLog.Compare`, normalisation, first divergence, context, counts | | 6 |
| 5 `millrace run`, every table row, `--format json`, no `--update` | | 8 |
| 5 `ExitCodes.LogMismatch = 4` | | 8 |
| 5 reuses `PlantFile`'s reading and rendering; `--assembly` works | | 8 |
| 6.1 `WriteAt`/`WriteIn`, `ApplyNow`, order within a tick | | 1 |
| 6.2 `IActionRecorder`, `AttachActionRecorder`, three landing sites, resolved arguments, second attach throws | | 2 |
| 7 `ScenarioRecorder.ToScenario`, `ScenarioJson.Write`, round trip, no CLI surface | | 4, 7 |
| 8 Testing — `Millrace.Core.Tests` list (three bullets) | | 1 (the `WriteAt` bullet and the determinism bullet), 2 (the `IActionRecorder` bullet) |
| 8 Testing — `Millrace.Scenarios.Tests` list | | 3, 4, 5, 6, 7, 9 |
| 8 Testing — the invalid corpus asserts **code and JSON path** | | 3 (`CorpusTests.InvalidPaths`, one row per fixture, guarded by `EveryInvalidFixtureHasARow`) |
| 8 Testing — `Millrace.Cli.Tests` list | | 8 |
| 9 Documentation — four items | | 9 |
| 10 Layout table | | 3 (projects), 8 (`Millrace.Cli` reference), R61 (the test project's extra) |

No gap found. Two additions the spec did not name, both recorded as rulings:
`Plants/broken.json` in `Millrace.Scenarios.Tests` (R62) and the `Millrace.Realtime`
reference in the test project (R61).

### 2. Placeholder scan

Searched for `TBD`, `TODO`, `implement later`, `fill in details`, `add
appropriate`, `handle edge cases`, `similar to Task`, `and so on` inside code
blocks, and for steps that describe without showing.

- Found and fixed: none. Every code step carries the file's whole content or an
  exact replacement block, and every test step carries the test.
- The one place content is *not* stated in advance is each golden event log's
  body, which cannot be known before a run. Task 5 Step 8 generates them, orders
  the implementer to read all four, and lists the exact lines each must contain
  plus what to report. That is deliberate, and the plan says so rather than
  inventing log text.
- `docs/scenario-diagnostics.md` is likewise generated (Task 9 Step 4), from
  prose that *is* stated in full in Step 3, so its content is fully determined.

### 3. Type consistency

Checked every name used across task boundaries against its definition.

- `ScenarioValue.OfBool/OfNumber/OfInteger`, `.Kind/.Boolean/.Number/.Integer`,
  `.ToTagValue(TagKind)` — defined in 3, used in 4, 5, 7. Consistent.
- `WriteAction(At, Tag, Value)`, `FaultAction(At, ComponentId, FaultId,
  Arguments)`, `ClearAction(At, ComponentId, FaultId)` — defined in 3, used in
  4, 5, 7. No `Path` member anywhere; the runner computes `$.timeline[i]` from
  the index, which is why `Timeline` is documented as being in file order.
- `Scenario(PlantPath, Seed, StartTime, TimeStep, Duration, Timeline)`,
  `.ResolvePlantPath`, `.ToLoadOptions` — 3, used in 4, 5, 7, 8.
- `ScenarioDiagnostics.Error/OffTick/NotBeforeTheEnd/Seconds` are `internal`
  and used only inside `Millrace.Scenarios` (loader, runner, `ScenarioJson`).
  `ScenarioJson` calling `ScenarioDiagnostics.Seconds` crosses files, not
  assemblies. Consistent.
- `ScenarioParseResult.Scenario/Diagnostics/IsValid/ToText` and
  `ScenarioRunResult.Events/Summary/Diagnostics/IsValid/ToText` — 3 and 5, used
  in 8. Both `IsValid` are severity-based (R59); both `ToText` render the same
  way `LoadResult.ToText` does.
- `RunSummary(Ticks, Events, ActionsScheduled)` — 5, used in 5's tests, 7's
  replay assertion and 8's JSON. The CLI reads `Summary!.Events` for the match
  line and `Summary?.Ticks` for the document. Consistent.
- `LogComparison(Matched, FirstDifferentLine, ExpectedLines, ActualLines,
  Report)` — 6, used in 8. The CLI writes `comparison.Report`, never a
  `ToText()`; there is no `ToText()` on it. Fixed during review: an earlier
  draft had both `Context` and `ToText()`, which would have been two names for
  one string.
- `GoldenLog.Normalise`/`Compare` — 6, used in 8.
- `IActionRecorder.Wrote/Faulted/Cleared` — 2, implemented in 7, spied on in
  2's test. Same parameter order everywhere.
- `Simulation.WriteAt/WriteIn/AttachActionRecorder/ActionRecorder` — 1 and 2,
  used in 5, 7 and their tests.
- `TagImage.CheckWritable/ApplyNow/SetActionRecorder` are `internal` and used
  only from `Simulation`, in the same assembly.
- `PlantFile.TryRead/ReportDiagnostics` — 8, and `TryBuild` is rewritten in the
  same step to use them, so no caller sees a changed byte.
- `Corpus.Catalogue/PathOf/Text/PlantPath/Valid/Invalid` — 3;
  `Corpus.Unrunnable/Run/RunFile` — 5. `CorpusTests` in 3 uses only the first
  set; the tests appended in 5 use both. Consistent.
- `Cli.Scenario/Built` — 8. `Built` is moved out of `PluginTests` in the same
  step, and `PluginTests`' two fields are rewritten to call it, so the name
  exists exactly once.
- `ExitCodes.Ok/PlantInvalid/Usage/Unreadable/LogMismatch` — the first four
  exist; `LogMismatch` is added in 8 and used in 8's tests only.

One inconsistency found and fixed during the review: Task 4's writer method was
originally named `WriteAction`, which is also the record's name, so
`case WriteAction write:` inside it would not have compiled. It is `WriteOne`.

### 4. Amendments after the independent review

A separate reviewer checked spec coverage and 31 claims this plan makes about
the existing source (all 31 correct) and found eight things to fix. All are
fixed above; recorded here so the execution reports know what moved:

- **A negative `at` is MR202 everywhere.** `ScenarioRunner.Schedule` reported
  it as MR203 while `ScenarioLoader` reported it as MR202. Spec 3 puts
  `at >= 0` in the parser as an out-of-range value, so the runner's copy — which
  only a code-built `Scenario` can trip — now uses MR202 with the same path and
  wording. R53 says so explicitly.
- **`ScenarioJson.Write`'s "Produces" line** claimed parameter `scenario` for
  every throw; the unknown-action-shape throw uses `action`. Both are listed.
- **The invalid corpus now pins each fixture's JSON path**, not just its code
  (spec 8 asks for both). `CorpusTests.InvalidPaths` carries one row per file
  and `EveryInvalidFixtureHasARow` fails if a fixture is added without a row.
- **Test counts.** `GoldenLogTests` is 18, not 17; `RunCommandTests` is 21, not
  20; Task 3 is 81 with the new rows. The chain is now
  751 → 759 → 765 → 846 → 858 → 887 → 905 → 914 → 935 → 938.
- **The by-hand `millrace run` check** pointed at a `Millrace.Scenarios.Tests` scenario,
  whose plant R62 links into the build output only, so it would have exited 3
  from the source tree. Task 8 Step 7 and the completion check now use
  `tests/Millrace.Cli.Tests/Scenarios/minimal.json`, whose plant is committed, and
  demonstrate the `--out` then `--expect` workflow; the committed goldens are
  covered by `CorpusTests` instead.
- Cosmetic: `Cli.Scenario`/`Cli.Built` are `public` members of an `internal`
  class, as `Cli.Plant` already is; `CorpusTests.cs` is in the file-structure
  tree; the README edit adds a fifth command-line row, not a fourth; and the
  spec-coverage row for `Millrace.Core.Tests` names its three bullets correctly.
