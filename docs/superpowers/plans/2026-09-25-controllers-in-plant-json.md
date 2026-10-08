# Controllers in the Plant File Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let a plant file declare the five 5c control blocks in a `controllers`
section — catalogued, schema-checked, resolved and diagnosed like components —
so `millrace validate`, `millrace tags` and `millrace run` handle a controlled plant with no
C#; attribute every block write in the event log and keep block writes out of
recordings (R77 closed).

**Architecture:** A third catalogue entry kind, `BlockDescriptor`, sits beside
`ComponentDescriptor` and `ObjectDescriptor` in `Millrace.Core/Catalogue`, with two
new parameter kinds, `Tag` and `Value`, that the existing `ParameterBinder`
resolves against a tag table held by the `BindingContext`. A new project,
`Millrace.Control.Catalogue`, registers the five blocks and the `transition` slot
through `ControlModule`, so `Millrace.Control` keeps seeing `Millrace.Io.Abstractions`
alone. The loader reads `controllers` in its structure stage and resolves them
in its build stage, once the plant alone has validated — against `SimulationBuilder.PlantTags()`, a new
public Core query, plus every controller's declared owned tags — before any
block is constructed; Core's write queue gains an origin so a block's write is
logged `Set to … by <id>.` and never reaches `IActionRecorder`.

**Tech Stack:** .NET 10 (`net10.0`, SDK 10.0.401), C#, xUnit 2.9.3. No external
runtime dependencies under `src/`. JsonSchema.Net 8.0.5 stays test-only.

**Spec:** `docs/superpowers/specs/2026-09-25-controllers-in-plant-json-design.md`
(all of it), building on the 5a catalogue spec
(`2026-09-20-catalogue-configuration-cli-design.md`), 5b
(`2026-09-22-scenarios-and-run-design.md`) and 5c
(`2026-09-22-control-blocks-design.md`).

**Plan sequence:** This is plan 5d. Plans 1–5c are merged on `master`; this plan
starts from `834758b` (the commit that added the 5d spec). Measured on that
commit: **1108 tests**, all passing — 37 `Millrace.Io.Abstractions` / 420 `Millrace.Core`
/ 126 `Millrace.Components` / 56 `Millrace.Realtime` / 137 `Millrace.Configuration` / 161
`Millrace.Scenarios` / 69 `Millrace.Cli` / 102 `Millrace.Control`. Nothing here may reference
the reference samples of plan 6.

**Task shape.** Sixteen tasks. The spec's suggested order is kept with three
changes the code forced:

- **`PlantTags()` is its own task (Task 2).** Spec 4.4 assumes a public query
  for the plant's tags "if none exists". None does: `SimulationBuilder` exposes
  only `Validate()` (errors) and `Build()`. It is a public Core API with its own
  test cycle.
- **The schema (Task 10) comes before the loader (Task 11).** Adding
  `controllers` to `PlantSchemas.TopLevelKeys` is what lets the loader accept the
  key, and `PlantSchemaTests.TheRootAndDefaultsKeySetsMatchTheirSchemas` asserts
  that list equals the schema's root properties — so the key, the schema and its
  golden must move together, and the loader then builds on them.
- **Four invalid fixtures move into the loader task (Task 11).**
  `CorpusTests.EveryConfigurationCodeHasAnInvalidPlant` fails the moment
  `MR113`–`MR115` join `ConfigDiagnostics.All`, so the task that adds the codes
  adds one fixture per code; Task 12 adds the rest of the corpus.

## Global Constraints

- **Determinism.** Never use `System.Random` or `string.GetHashCode()` for
  anything that affects behaviour. Never let a `Dictionary` or `HashSet`
  iteration order reach an output: every exported list is sorted with
  `StringComparer.Ordinal` first, or is in declaration or file order. All
  formatting and parsing uses `CultureInfo.InvariantCulture`; messages that embed
  a number use `string.Create(CultureInfo.InvariantCulture, $"...")`.
- **Zero package references under `src/`:** `grep -rn "PackageReference" src/`
  prints nothing. **`Millrace.Control` references `Millrace.Io.Abstractions` and nothing
  else** — `src/Millrace.Control/Millrace.Control.csproj` is not edited by this plan.
  `Millrace.Core` still references only `Millrace.Io.Abstractions`. The new
  `src/Millrace.Control.Catalogue` references `Millrace.Core` and `Millrace.Control`. Test
  projects use the package versions of `tests/Millrace.Core.Tests/Millrace.Core.Tests.csproj`
  (`coverlet.collector` 6.0.4, `Microsoft.NET.Test.Sdk` 17.14.1, `xunit` 2.9.3,
  `xunit.runner.visualstudio` 3.1.4); JsonSchema.Net 8.0.5 stays pinned in
  `tests/Millrace.Configuration.Tests` only.
- **Warnings are errors** (`Directory.Build.props`: `TreatWarningsAsErrors`,
  `EnforceCodeStyleInBuild`, `GenerateDocumentationFile`, `Nullable`). The
  Release build prints `0 Warning(s)` and `0 Error(s)`. A `<see cref>` to a type
  that does not exist yet is a build error. A csproj repeats only
  `TargetFramework`, `ImplicitUsings` and `Nullable` (plus `IsPackable` for
  tests).
- **xUnit analyzers run under warnings-as-errors:** prefer `Assert.Single`,
  `Assert.Contains`, `Assert.DoesNotContain`, `Assert.Empty`; never
  `Assert.True(x.Any())` or `Assert.Equal(1, x.Count())`. Where a tuple would mix
  `string` and `string?` (`JsonElement.GetString()`, `StepTransition.Tag`),
  assert the members separately: nullable-mismatched tuple inference is a
  warning, therefore an error.
- **Tests spell fault arguments `new FaultArgument("n", v)`** and fault argument
  sets `new FaultArguments(new FaultArgument("amount", 0.8))`.
- **Messages.** Every event message and every diagnostic message is a sentence
  ending in a full stop. A diagnostic `Fix` begins with an imperative sentence
  ending in a full stop and may add one more sentence. `DiagnosticInfo` titles do
  not end in a full stop; explanations do. `EventLog.ToText()` is not changed:
  every golden depends on its bytes.
- **Goldens are generated and read, never invented.** A golden (event log,
  catalogue export, plant schema, diagnostics page) is written by running its
  test with `MILLRACE_UPDATE_GOLDEN=1` (the mechanism in `tests/Shared/Golden.cs`),
  then the whole file — or its whole `git diff` — is **read** with a file-reading
  tool and checked against the task's checklist, and the task report quotes what
  was checked. If a golden disagrees with the checklist, report the measurement
  and say which one changed and why; never widen a window to make a number fit.
- **Report every measurement.** Where an expected value stated in this plan
  (a test count, a line number, a tag count) disagrees with what the code
  produces, report the measured value in the task report.
- **Timing rule** (restated in every task with tick-counted assertions): **a
  scan at tick N sees the image published at the end of tick N−1; its outputs
  are visible from the end of tick N (read at N+1); its queued writes land at
  phase 1 of tick N+1.** A `WriteAt(t)` lands at phase 1 of tick `t / step`.
- **Names.** Tag names are exactly what `millrace tags <plant>` prints and match
  ordinally. A block's owned tag is `<block id>.<pin>` (`INT01.Ok`,
  `CUR01.HiHi.Active`). Block type names are kebab-case and share one namespace
  with component types. Group definitions are PascalCase (R88).
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
  `dotnet build Millrace.sln -c Release --nologo` (expect `0 Warning(s)`,
  `0 Error(s)`) and `dotnet test Millrace.sln --nologo` (expect the task's total).
  `.superpowers/` is **not** git-ignored in this repository (measured: `git
  status` lists it as untracked); never add it.

## Review Focus

The five inputs the spec implies but its test list does not pin, most likely to
bite first. Each has its test in the owning task, named here.

1. **A `scanPeriodMs` of `1e30`, `0.00001`, `0`, `-5`, `"100"` or none at all** —
   the plant-file author expects a `MR103` at `$.controllers[i].scanPeriodMs`,
   not the exit-134 `OverflowException` that R65 found for `timeStepMs`.
   Test: Task 11, `ControllerTests.AScanPeriodIsAPositiveNumberOfAtMostADay`
   (six rows).
2. **A duration of `1e30` s** (`presetS`, `timeoutS`, `delayS`, `onDelayS`) — the
   author expects `MR103` at the parameter, not an `OverflowException` out of
   `TimeSpan.FromSeconds` (measured: it overflows at `1e12` s) reported as a
   module defect. Tests: Task 8, `BlockFactoryTests.ADurationLongerThanAYearIsOutOfRange`;
   Task 11, `ControllerTests.ADurationLongerThanAYearIsMr103AtTheParameter`.
3. **A tag of the wrong kind where a block needs Bool or Double** (a permissive
   condition on `CHUTE.Level`, an alarm on a Bool) — the author expects `MR114`
   at that parameter's path, not a `MR014` at the whole entry after
   resolution claimed success. Tests: Task 4,
   `TagParameterTests.ATagOfTheWrongKindIsAnIssueAtTheTag`; Task 11,
   `ControllerTests.EveryTagErrorIsReportedBeforeAnyBlockIsBuilt`.
4. **A write to a tag that looks writable but is not** — an input a signal link
   drives (its declared read-write tag is published read-only, R23) or another
   block's output — the author expects `MR115`, not a crash in `TagImage.Write`
   mid-run. Tests: Task 2, `PlantTagsTests.AWritableTagOnADrivenInputIsReadOnly`;
   Task 11, `ControllerTests.AWriteToAnInputASignalLinkDrivesIsMr115` and
   `AWriteToAnotherBlocksOutputIsMr115`.
5. **One mistake, one diagnostic** — a value whose tag did not resolve gets no
   second `MR114`; a plant's own tag conflict is reported as `MR009`/`MR010`,
   not as a controller's `MR113`; and a plugin whose `OwnedTags` or factory
   throws, returns null or builds a block with the wrong id gets one `MR111`
   naming the module, not a crash (R100). Tests: Task 4,
   `TagParameterTests.AnUnknownTagSuggestsTheNearestName`; Task 11,
   `ControllerTests.AnUnknownTagSuggestsTheNearestAndIsReportedOnce`,
   `APlantTagConflictIsReportedAsItselfNotAsAnUnknownTag` and
   `AModuleDefectIsMr111NamingTheModule` (five rows).

## Decisions settled here (rulings R80–R102)

These refine the spec where writing real code against the real APIs forced a
choice. Where one differs from the spec's wording, this plan wins and says why.

- **R80 — Controllers are resolved in the build stage, after the plant validates, not in the
  instantiate stage.** Spec 4.2 puts tag resolution between component
  construction and block construction in stage 4. That cannot work against this
  code: the `SimulationBuilder` is created in `BuildStage.Run`; explicit `tags`
  binds become `TagBinding`s in `WireStage.Bind`; and a tag's access depends on
  wiring — a declared read-write tag on an input a signal link drives is
  published read-only (R23, `SimulationBuilder.CollectTags`). So the resolution
  pass, `Loading/ControllerPass.cs`, runs inside `BuildStage.Run` in three
  steps: the components and binds are added and `Validate()` runs on the plant
  alone — any error there (a `MR009`/`MR010` tag conflict, whose tag
  `PlantTags()` leaves out) is reported as itself and the stage stops, so a
  controller naming that tag never gets a misleading `MR113`/`MR115`; then the
  controllers are resolved and built; then the blocks are added and `Validate()`
  runs again for `MR013`–`MR015`. There is still no new stage in
  `PlantLoader.Stages`, and spec 4.4's guarantee — every tag and value resolved,
  every error collected, before any block is constructed — holds.
- **R81 — `SimulationBuilder.PlantTags()` is the one definition of the plant's
  tags.** It returns `new TagDirectory(CollectTags(...)).Tags`: component tags,
  composite exposures and explicit binds, sorted and indexed exactly as `Build()`
  would, with R23 applied, and without block-owned tags. Tags `Validate()` would
  reject (`MR009`–`MR011`) are left out, not reported. `ITagProvider`'s summary
  ("Called once, at `Build()`") becomes "called at `Validate()`, `Build()` and
  `PlantTags()`; return fresh bindings and change nothing" — already true of
  every provider, since `Validate()` followed by `Build()` calls it twice today.
- **R82 — `BlockDescriptor.OwnedTags` is called with the check-mode
  parameters.** The table must exist before construct-mode binding (which
  resolves tags against it), so the loader calls `OwnedTags(id, values)` with the
  `ParameterValues` the structure stage bound with `construct: false`. There,
  scalars, enums, strings, groups and tag names are final, values are
  provisional (R84) and object parameters are the `ObjectDescriptor`
  placeholder. `OwnedTags` must not read object parameters or values; its XML
  doc says so. Conformance calls it with construct-mode values, which is a
  superset.
- **R83 — `Param.Tag` carries an optional required kind and a write flag; a tag
  of the wrong kind is `MR114` at the parameter.** Spec 3.2 converts only
  values, so a permissive condition naming a Double tag would pass resolution
  and fail as Core's `MR014` — contradicting spec 4.4 ("`MR014` … is not
  expected to fire for a plant that passed this stage"). `Param.Tag(name,
  description, TagKind? kind = null, bool writes = false)`: conditions and the
  timer input require Bool, the alarm input Double; a `write`'s tag has
  `writes: true` and must be read-write (`MR115`). `MR114`'s title widens to
  "A controller's tag or value is of the wrong kind".
- **R84 — Without a tag table, a tag is a name and a value is provisional.**
  In check mode, and in construct mode on a `BindingContext` that was never
  given tags (conformance, a plain `Bind` call), `Tag` binds as its string and
  `Value` binds `true`/`false` as Bool and any finite number as Double. Only a
  context given a table through `BindingContext.UseTags` resolves. A value's
  string, object or null is `MR103` everywhere.
- **R85 — In the tag table the first entry for a name wins.** Plant tags go in
  before owned tags. A block's owned tag that duplicates a plant tag (a `tags`
  bind named `PERM01.Ok`) is not a resolution error: Core's `MR015` reports it
  at `Build`'s validation, and the loader places it on the controller (R92).
- **R86 — Durations are bounded at one year; a scan period at one day and one
  tick.** Measured: `TimeSpan.FromSeconds(1e12)` and `TimeSpan.FromSeconds(1e30)`
  throw `OverflowException` (not an `ArgumentException`), which the loader would
  report as a module defect. Every `…S` duration is `Param.Double(…, "s", min: 0,
  max: 31_536_000)` (365 days; `ControlCatalogue.MaxSeconds`), so an absurd one is
  `MR103` at its path and the schema agrees. `scanPeriodMs` uses R65's bounds:
  more than 86 400 000 ms is `MR103` ("longer than a day"), checked before
  `TimeSpan.FromMilliseconds`, and a period of zero ticks is `MR103` ("at least
  one tick"); the schema declares `exclusiveMinimum: 0, maximum: 86400000`.
- **R87 — A controller id follows the component id rule, not `TagNameRules`.**
  Spec 4.1 says `TagNameRules`, which allows dots. A dotted block id (`INT.01`)
  would make its tags `INT.01.Ok`, indistinguishable from a leaf tag, and
  `BuildStage.PathOf` splits ids at the first dot. So: non-empty, no dot, no
  whitespace — `MR103` otherwise — and the schema uses the component id pattern
  `^[^.\s]+$`.
- **R88 — Group definitions are PascalCase, named after the records they
  build.** `GroupDefinition.Name` is documented "PascalCase, unique across a
  catalogue" and the schema keys `$defs` by it. Spec 3.3's `condition` and
  `write` are `Condition` and `BlockWrite`; the alarm limit and sequencer step
  groups are `AlarmLimit` and `SequenceStep`. Schema keys: `group.Condition`,
  `group.BlockWrite`, `group.AlarmLimit`, `group.SequenceStep`,
  `object.transition`, `object.transition.when`, `object.transition.after`,
  `block.<type>`.
- **R89 — Defaults the spec leaves open.** The timer `mode` is required (no
  default); an alarm limit's `deadband` and `onDelayS` default to 0; `trip`,
  `abort` and a step's `writes` default to empty; `timeoutS` is optional. The
  alarm's "1–4 limits" is min 1 in the descriptor and max 4 in the constructor:
  `ParameterDescriptor` has no maximum count, and a fifth limit necessarily
  repeats a kind, which `Alarm`'s constructor rejects (`MR111`). No `MaxCount`
  is added.
- **R90 — The catalogue export's `blocks` array sits between `components` and
  `objects`, and a block entry carries `type`, `module`, `description` and
  `parameters` — no owned-tag list.** Owned tags are a function of the
  parameters (the alarm's follow its limits), not data; `millrace tags` lists them for
  a real plant and `docs/control-blocks.md` per block. A parameter entry gains
  `tagKind`, `writes` and `tagParameter` when set. Placing `blocks` before
  `objects` makes the components-only golden change by exactly one inserted line,
  `  "blocks": [],`. `formatVersion` stays 1.
- **R91 — A block type that equals a component type is rejected when it is
  added, not at `Build()`.** Spec 3.1 says `Build()`; the code rejects every
  other clash at `Add`, so `AddBlock` and `Add(ComponentDescriptor)` check the
  other dictionary and name both modules.
- **R92 — Where Core's block diagnostics land.** `MR013` lands on
  `$.controllers[i].scanPeriodMs`; `MR014` and `MR015` on `$.controllers[i]`.
  The loader records no inner path for `MR014`: after R80/R83 resolution a plant
  file cannot reach it.
- **R93 — The JSON-versus-code round trip lives in `Millrace.Control.Tests`.** Spec 8
  lists it under `Millrace.Configuration.Tests`, but the code-built worked example is
  `WorkedExampleTests.Build()` in `Millrace.Control.Tests`, which already loads plants
  through `PlantLoader` and links the valid corpus. Putting the round trip beside
  it avoids a second 80-line copy of the example.
- **R94 — `ControlModule.Name` is `Millrace.Control`; `controllers` is the last root
  property of the schema; a catalogue without blocks allows no controllers.**
  An empty `oneOf` is not valid JSON Schema (`schemaArray` has `minItems: 1`), so
  with no block types `controllers` is `{ "type": "array", "maxItems": 0 }`.
- **R95 — `PlantSummary` gains `Controllers`, defaulted to 0** so the 5a
  positional constructor stays source-compatible (`WireAndBuildTests` builds
  `new PlantSummary(3, 0, 2, 0)`). `validate` prints `controllers   N` after the
  `tags` line; the JSON summary gains `"controllers"` after `"explicitTags"`.
- **R96 — The diagnostics page trailer stays `MR001–MR015`.** Spec 4.5 says the
  trailer "extends to MR115"; the trailer describes Core's pass-through codes,
  which do not change. `MR113`–`MR115` enter the page's table from
  `ConfigDiagnostics.All`; the trailer and introduction gain a sentence on
  controllers. The titles of `MR102` and `MR107` and the explanations of
  `MR103` and `MR111` widen to cover controllers.
- **R97 — A component may not declare a `Tag` or `Value` parameter.** The
  component pipeline has no tag table, so such a parameter would silently never
  resolve; `CatalogueBuilder.Add(ComponentDescriptor)` rejects it. Objects may
  declare them (the `when` transition does). An object in a slot a component
  uses that declares a tag is not resolved — no shipped slot does; out of scope.
- **R98 — Eleven existing tests change.** Spec criterion 6 allows updates
  "only where criterion 4 allows", which did not anticipate all of them.
  *Four because the spec's decisions invert what they assert:*
  `ScanBlockHostTests.AWriteQueuedByABlockLandsAtPhaseOneOfTheNextTick` and
  `HostTests.AnInterlockOverAPlantTripsAndHoldsTheFillCommandLow` assert the
  unattributed message (Task 1); `HostTests.ABlockWriteIsRecordedOnTheTickItLands`
  asserts R77's old behaviour and becomes `ABlockWriteIsNotRecorded` (Task 1);
  `ConfigDiagnosticTests.TheTableListsEveryCodeOnceInOrder` counts 13 codes
  (Task 11). *Seven for additions criterion 4 allows:*
  `PlantSchemaTests.DeclaresItsDialectAndClosesTheRoot` (root key list, Task 10);
  `ValidateCommandTests.AValidPlantPrintsASummary` and
  `JsonFormatOfAValidPlantCarriesTheSummary` (the `controllers` line and member,
  Task 15); `PluginTests.APluginsTypesAppearInTheCatalogueExport` (module list)
  and `ThePluginPassesCatalogueConformance` (a `latch` fixture, Task 15);
  `ExportCommandTests`' `Shipped` catalogue (Task 15); and
  `WorkedExampleTests`, whose `Catalogue` gains `ControlModule` and whose
  `Build()` moves its timeline into a shared `Drive()` (Task 13) — its three
  facts and their assertions are unchanged.
- **R99 — `ScenarioRecorder`'s summary is reworded.** Spec 2 says `Millrace.Scenarios`
  changes "none in code"; its XML summary says it "accumulates every action that
  took effect", which R77's closure makes false. Only the doc comment changes.
- **R100 — Every defect in a plugin's block descriptor is `MR111` naming the
  module.** Beyond a throwing `OwnedTags` or factory, `ControllerPass` catches an
  `OwnedTags` that returns null or a null tag, a factory that returns null, and a
  factory whose block has an id or a scan period other than the ones it was
  given (which would otherwise publish unresolved tags or throw out of
  `AddScanBlock`). The equivalent gap for components — a component factory that
  returns null makes `BindingContext.AddNode` throw outside the instantiate
  stage's `try` — predates this plan and stays out of scope.
- **R101 — The schema golden's `description` line changes, and that is not an
  addition.** It lists the catalogue's modules ("Generated from the catalogue of
  modules: Millrace.Components, Millrace.Control. …"), so adding `ControlModule` to the
  configuration tests' catalogue rewrites one pre-existing line. Criterion 4's
  "only by the additions of §4" is read as allowing it; Task 10 checks it is the
  only rewritten line. The components-only catalogue golden changes by one
  inserted line and nothing else (R90).
- **R102 — Conformance compares an owned tag's unit and description too.** Spec
  3.4 says the descriptor declares "names, kinds, units and descriptions" and
  "the conformance check proves the declaration"; spec 3.5 lists only name, kind
  and access. The plan follows 3.4: for an owned tag that matches by name, kind
  and access, a differing unit or description is a mismatch of its own.

## File structure

```
src/Millrace.Core/
  Io/TagImage.cs                     + origin on the queue; ApplyNow logs "by <id>", skips the recorder
  Io/ITagProvider.cs                 summary: may be called more than once (R81)
  Control/ScanBlockRuntime.cs        enqueues with the block id
  Simulation.cs                      WriteEvent passes origin null
  IActionRecorder.cs                 "every external action"
  SimulationBuilder.cs               + PlantTags() (R81)
  Catalogue/BlockDescriptor.cs       new
  Catalogue/CatalogueBuilder.cs      + AddBlock, shared type namespace, Tag/Value checks
  Catalogue/ComponentCatalogue.cs    + Blocks, TryGetBlock, ModuleOf(BlockDescriptor)
  Catalogue/ParameterKind.cs         + Tag, Value
  Catalogue/ParameterDescriptor.cs   + RequiredKind, IsWriteTarget, TagParameter
  Catalogue/Param.cs                 + Tag, Value
  Catalogue/ParameterValues.cs       + Tag, Value
  Catalogue/BindingContext.cs        + UseTags, TryGetTag, TagNames, ResolvesTags
  Catalogue/BindingIssue.cs          + UnknownTag, WrongTagKind, ReadOnlyTag
  Catalogue/ParameterBinder.cs       + Tag and Value binding and conversion
  Catalogue/CatalogueJson.cs         + blocks array, tagKind/writes/tagParameter
  Testing/CatalogueConformance.cs    + blocks
  Testing/ConformanceFixtures.cs     + BlockParameters (several per type)
src/Millrace.Control.Catalogue/           new project
  Millrace.Control.Catalogue.csproj
  ControlModule.cs  ControlCatalogue.cs  TransitionCatalogue.cs
  TimerCatalogue.cs  PermissiveCatalogue.cs  InterlockCatalogue.cs
  AlarmCatalogue.cs  SequencerCatalogue.cs
src/Millrace.Configuration/
  Millrace.Configuration.csproj           + Millrace.Control.Catalogue
  PlantSchema.cs                     + controllers, block branches, Tag/Value
  Loading/PlantSchemas.cs            + "controllers", ControllerKeys
  Loading/LoadState.cs               + ControllerEntry, Controllers
  Loading/StructureStage.cs          + ReadControllers
  Loading/ControllerPass.cs          new (R80)
  Loading/BuildStage.cs              runs the pass; PathOf learns controllers
  Loading/InstantiateStage.cs        AsSentence becomes internal
  ConfigDiagnostics.cs               + MR113–MR115; widened texts
  DiagnosticsReference.cs            intro and trailer sentences
  PlantSummary.cs  PlantLoader.cs    + Controllers
src/Millrace.Scenarios/ScenarioRecorder.cs  summary only (R99)
src/Millrace.Cli/
  Millrace.Cli.csproj  CliApp.cs  CommandTable.cs  Commands/Validate.cs
tests/Millrace.Core.Tests/
  ActionRecorderTests.cs  ScanBlockHostTests.cs  PlantTagsTests.cs (new)
  Catalogue/CatalogueModelTests.cs  Catalogue/TagParameterTests.cs (new)
  Catalogue/CatalogueConformanceTests.cs  Catalogue/CatalogueJsonTests.cs
tests/Millrace.Control.Catalogue.Tests/   new project
  Millrace.Control.Catalogue.Tests.csproj  Bind.cs  ControlFixtures.cs
  ControlCatalogueTests.cs  TransitionTests.cs  BlockFactoryTests.cs
  Golden/control-catalogue.json      generated in Task 9
tests/Millrace.Components.Tests/Catalogue/Golden/components-catalogue.json   + one line (Task 9)
tests/Millrace.Configuration.Tests/
  Plants.cs  TestModule.cs  ControllerTests.cs (new)  PlantSchemaTests.cs
  SchemaAgreementTests.cs  ConfigDiagnosticTests.cs
  Golden/plant.schema.json           regenerated in Task 10
  Plants/valid/conveyor-control.json new (Task 12)
  Plants/invalid/                    + 12 fixtures (Tasks 11, 12)
tests/Millrace.Control.Tests/
  Millrace.Control.Tests.csproj  HostTests.cs  WorkedExampleTests.cs  DocumentationTests.cs
  Golden/conveyor-control.log        regenerated in Task 1
tests/Millrace.Scenarios.Tests/
  Millrace.Scenarios.Tests.csproj  ControlledRecordAndReplayTests.cs (new)
tests/Millrace.Cli.Tests/
  Millrace.Cli.Tests.csproj  Cli.cs  ValidateCommandTests.cs  TagsCommandTests.cs
  RunCommandTests.cs  PluginTests.cs  ExportCommandTests.cs
  Plants/sample-block.json  Scenarios/conveyor-control.json   new
tests/Millrace.Cli.Tests.SampleModule/Latch.cs  SampleCatalogueModule.cs
docs/control-blocks.md  docs/authoring-a-component.md  docs/scenarios.md
docs/architecture.md  README.md  docs/configuration-diagnostics.md (regenerated)
docs/superpowers/specs/2026-09-20-catalogue-configuration-cli-design.md   §4.1 amended
Millrace.sln                              + Millrace.Control.Catalogue, Millrace.Control.Catalogue.Tests
```

## Task map

| # | Task | Model | Tests after |
|---|---|---|---|
| 1 | Write origin: attributed block writes, recorder sees external writes only; 5c golden regenerated | opus | 1110 |
| 2 | `SimulationBuilder.PlantTags()` | opus | 1113 |
| 3 | `BlockDescriptor`, `AddBlock`, `ComponentCatalogue.Blocks` | sonnet | 1118 |
| 4 | `Tag` and `Value` parameter kinds: binding, resolution, conversion | opus | 1140 |
| 5 | Conformance for blocks | sonnet | 1147 |
| 6 | `Millrace.Control.Catalogue`: project, `ControlModule`, shared groups, `transition` slot | sonnet | 1156 |
| 7 | Timer, permissive and interlock descriptors | sonnet | 1162 |
| 8 | Alarm and sequencer descriptors; the reflection sweep | sonnet | 1167 |
| 9 | Catalogue export of blocks; two catalogue goldens | sonnet | 1170 |
| 10 | Plant schema for controllers; schema golden | sonnet | 1175 |
| 11 | Loader: `controllers` read, resolved, built; `MR113`–`MR115`; diagnostics page | opus | 1213 |
| 12 | The corpus: worked example plant, invalid fixtures, schema agreement | sonnet | 1237 |
| 13 | The worked example from JSON, byte-identical to the code-built one | opus | 1239 |
| 14 | Recording a controlled run replays byte for byte (R77) | sonnet | 1241 |
| 15 | CLI: default catalogue, `validate`, `tags`, `run --expect`, a plugin block | sonnet | 1248 |
| 16 | Documentation and the 5a spec amendment | sonnet | 1249 |

Tasks are sequential. Reviewers use the larger model for Tasks 1, 2, 4, 11 and 13.

---

### Task 1: The write origin — a block's write is attributed and is not recorded

Model: opus

Spec 5. One queue keeps today's order; each pending write carries an origin (a
block id, or null). `ApplyNow` stays the one landing site: it logs
`Set to {value} by {origin}.` when there is an origin and `Set to {value}.`
otherwise, and calls `IActionRecorder.Wrote` only when there is none. Then the
5c golden is regenerated and its diff read.

**Measured on `834758b`** (read from `tests/Millrace.Control.Tests/Golden/conveyor-control.log`
and `WorkedExampleTests.Build()`): the golden has 40 lines and 12 `WRITE` lines.
Two are the test's own `WriteAt` of `SEQ01.Start` (lines 5 and 10). The other
ten are block-issued: `INT01`'s trip writes (lines 3 and 33) and `SEQ01`'s step
entry writes (lines 7, 12, 13, 16, 17, 25, 37, 39). No abort write occurs in
this run, so spec 7's "abort writes" contribute no line.

**Timing rule:** a scan at tick N sees the image published at the end of tick
N−1; its outputs are visible from the end of tick N; its queued writes land at
phase 1 of tick N+1. A `WriteAt(t)` lands at phase 1 of tick `t / step`.

**Files:**
- Modify: `src/Millrace.Core/Io/TagImage.cs` (`Write`, `ApplyNow`, `ApplyPendingWrites`, `PendingWrite`)
- Modify: `src/Millrace.Core/Control/ScanBlockRuntime.cs` (the write loop and the class remarks)
- Modify: `src/Millrace.Core/Simulation.cs` (`WriteEvent.Apply`)
- Modify: `src/Millrace.Core/IActionRecorder.cs` (summaries)
- Modify: `src/Millrace.Scenarios/ScenarioRecorder.cs` (summary only, R99)
- Test: `tests/Millrace.Core.Tests/ActionRecorderTests.cs`, `tests/Millrace.Core.Tests/ScanBlockHostTests.cs`,
  `tests/Millrace.Control.Tests/HostTests.cs`
- Regenerate: `tests/Millrace.Control.Tests/Golden/conveyor-control.log`

**Interfaces:**
- Consumes: `TagImage.Write(int index, TagValue value)` (public, `ITagWriter`),
  `internal void ApplyNow(int index, TagValue value, in TickContext ctx)`,
  `TickContext.Log(string source, string code, string message)`,
  `IActionRecorder.Wrote(long tick, string tag, TagValue value)`; test fakes
  `EchoBlock(string id, TimeSpan scanPeriod)` with `.MayWrite(string tag)` and
  `WriteOnce`, `Thermostat(string id)` (tags `T.Setpoint`, `T.Enable`, `T.Output`).
- Produces: `internal void TagImage.Write(int index, TagValue value, string origin)`;
  `internal void TagImage.ApplyNow(int index, TagValue value, string? origin, in TickContext ctx)`.
  The event-log format `Set to {value} by {origin}.` for a block's write.

- [ ] **Step 1: Write the failing Core tests**

In `tests/Millrace.Core.Tests/ActionRecorderTests.cs`, add `using Millrace.Core.Logging;`
to the usings, and add these two facts at the end of the class:

```csharp
    [Fact]
    public void ABlockWriteIsLoggedByItsBlockAndNotRecorded()
    {
        EchoBlock block = new EchoBlock("B", TimeSpan.FromMilliseconds(20)).MayWrite("T.Enable");
        block.WriteOnce = true;
        Simulation sim = new SimulationBuilder(Options).Add(new Thermostat("T")).AddScanBlock(block).Build();
        var spy = new Spy();
        sim.AttachActionRecorder(spy);

        sim.Tick();                                   // tick 0: the scan queues the write
        sim.Tick();                                   // tick 1: phase 1 lands it

        SimEventRecord record = Assert.Single(sim.Events.Records);
        Assert.Equal(1L, record.Tick);
        Assert.Equal("T.Enable", record.Source);
        Assert.Equal("WRITE", record.Code);
        Assert.Equal("Set to true by B.", record.Message);
        Assert.True(sim.IO.ReadBool("T.Enable"));
        Assert.Empty(spy.Calls);
    }

    [Fact]
    public void ABlockWriteAndAnExternalWriteOnOneTickLandInEnqueueOrder()
    {
        EchoBlock block = new EchoBlock("B", TimeSpan.FromMilliseconds(20)).MayWrite("T.Enable");
        block.WriteOnce = true;
        Simulation sim = new SimulationBuilder(Options).Add(new Thermostat("T")).AddScanBlock(block).Build();
        var spy = new Spy();
        sim.AttachActionRecorder(spy);

        sim.Tick();                                   // tick 0: the scan queues true
        sim.IO.WriteBool("T.Enable", false);          // queued behind it
        sim.Tick();                                   // tick 1: both land, in enqueue order

        Assert.Equal(
            new[] { (1L, "T.Enable", "Set to true by B."), (1L, "T.Enable", "Set to false.") },
            sim.Events.Records.Select(r => (r.Tick, r.Source, r.Message)));
        Assert.False(sim.IO.ReadBool("T.Enable"));
        Assert.Equal(("wrote", 1L, "T.Enable", "false"), Assert.Single(spy.Calls));
    }
```

In `tests/Millrace.Core.Tests/ScanBlockHostTests.cs`, in
`AWriteQueuedByABlockLandsAtPhaseOneOfTheNextTick`, replace

```csharp
        Assert.Equal("Set to true.", record.Message);
```

with

```csharp
        Assert.Equal("Set to true by B.", record.Message);
```

- [ ] **Step 2: Update the two 5c host tests the decision inverts (R98)**

In `tests/Millrace.Control.Tests/HostTests.cs`, in
`AnInterlockOverAPlantTripsAndHoldsTheFillCommandLow`, replace

```csharp
            string.Equals(r.Message, "Set to false.", StringComparison.Ordinal));
```

with

```csharp
            string.Equals(r.Message, "Set to false by INT01.", StringComparison.Ordinal));
```

Replace the whole method `ABlockWriteIsRecordedOnTheTickItLands` with:

```csharp
    [Fact]
    public void ABlockWriteIsNotRecorded()
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

        // Timing rule: the trip lands at phase 1 of tick 50 and is published at the
        // end of it; the 100 ms scan at tick 60 is the first to see it, trips, and
        // queues its write, which lands at phase 1 of tick 61. That write is the
        // block's own: it is logged "by INT01" and never reaches the recorder (R77).
        Assert.Equal(
            new[] { ("V1.Fill", 10L, "true"), ("V1.Trip", 50L, "true") },
            spy.Writes.ToArray());
        SimEventRecord write = Assert.Single(sim.Events.Records, r =>
            r.Tick == 61L && string.Equals(r.Source, "V1.Fill", StringComparison.Ordinal));
        Assert.Equal("WRITE", write.Code);
        Assert.Equal("Set to false by INT01.", write.Message);
    }
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/Millrace.Core.Tests --nologo --filter "FullyQualifiedName~ActionRecorderTests|FullyQualifiedName~ScanBlockHostTests"`
Expected: FAIL — `ABlockWriteIsLoggedByItsBlockAndNotRecorded`,
`ABlockWriteAndAnExternalWriteOnOneTickLandInEnqueueOrder` and
`AWriteQueuedByABlockLandsAtPhaseOneOfTheNextTick` fail on the message
(`Expected: "Set to true by B."`, `Actual: "Set to true."`); every other test in
the two classes passes.

Run: `dotnet test tests/Millrace.Control.Tests --nologo --filter FullyQualifiedName~HostTests`
Expected: FAIL — `AnInterlockOverAPlantTripsAndHoldsTheFillCommandLow` and
`ABlockWriteIsNotRecorded`.

- [ ] **Step 4: Give the write queue an origin**

In `src/Millrace.Core/Io/TagImage.cs`, replace the public `Write(int, TagValue)`:

```csharp
    /// <inheritdoc/>
    public void Write(int index, TagValue value)
    {
        Check(index, value);
        _writes.Enqueue(new PendingWrite(index, value, null));
    }

    /// <summary>
    /// Queues a write a control block issued. It lands exactly as an external
    /// write does — at phase 1 of the next tick, in enqueue order, so the last
    /// writer still wins — but it is logged <c>Set to … by &lt;origin&gt;.</c>
    /// and never reaches the action recorder.
    /// </summary>
    internal void Write(int index, TagValue value, string origin)
    {
        Check(index, value);
        _writes.Enqueue(new PendingWrite(index, value, origin));
    }
```

Replace `ApplyNow` and its summary:

```csharp
    /// <summary>
    /// The one place a write lands: applies the value to a binding and logs it
    /// as <c>WRITE</c>. An external write (no origin) is reported to the action
    /// recorder; a block's write is attributed in the log and is not, because
    /// replaying the external actions re-runs the block, which issues it again.
    /// Phase 1 only, called both from the queued-write drain and from a
    /// scheduled <c>WriteEvent</c>.
    /// </summary>
    internal void ApplyNow(int index, TagValue value, string? origin, in TickContext ctx)
    {
        TagBinding binding = _bindings[index];
        binding.Apply(value);
        if (origin is null)
        {
            ctx.Log(binding.Name, "WRITE", $"Set to {value}.");
            _recorder?.Wrote(ctx.Tick, binding.Name, value);
        }
        else
        {
            ctx.Log(binding.Name, "WRITE", $"Set to {value} by {origin}.");
        }
    }
```

In `ApplyPendingWrites`, replace `ApplyNow(write.Index, write.Value, in ctx);`
with `ApplyNow(write.Index, write.Value, write.Origin, in ctx);`, and replace the
record at the end of the file with:

```csharp
    private readonly record struct PendingWrite(int Index, TagValue Value, string? Origin);
```

In `src/Millrace.Core/Simulation.cs`, in `WriteEvent.Apply`, replace
`_simulation.IO.ApplyNow(_index, _value, in context);` with
`_simulation.IO.ApplyNow(_index, _value, origin: null, in context);`.

In `src/Millrace.Core/Control/ScanBlockRuntime.cs`, replace
`_io.Write(_writeIndices[i], value);` with
`_io.Write(_writeIndices[i], value, _plan.Block.Id);`, and replace the second
`<para>` of the class remarks with:

```csharp
    /// <para>
    /// Two blocks may name the same tag in their <see cref="IScanBlock.Writes"/>.
    /// Both writes are queued and applied at phase 1 of the next tick in enqueue
    /// order, and blocks scan in the order they were added, so the block added
    /// later wins. That is deterministic and it is exactly what a PLC does with a
    /// double coil — but, as on a PLC, it is usually a mistake. Every block write
    /// is logged <c>Set to … by &lt;block id&gt;.</c> and none reaches the action
    /// recorder.
    /// </para>
```

- [ ] **Step 5: Narrow the recorder's contract in its documentation**

In `src/Millrace.Core/IActionRecorder.cs`, replace the interface summary and the
`Wrote` summary:

```csharp
/// <summary>
/// Sees every external action that took effect, stamped with the tick it took
/// effect on, whichever path it came by: a scenario, a command bus, or test code.
/// A write a control block issued is not one: replaying the external actions
/// re-runs the block, which issues it again. This is the seam a recording is made
/// through; what it is called from is the three places an action lands, so no
/// external action can slip past it.
/// </summary>
```

```csharp
    /// <summary>An external value reached a tag, queued or scheduled. Never called for a block's own write.</summary>
    void Wrote(long tick, string tag, TagValue value);
```

In `src/Millrace.Scenarios/ScenarioRecorder.cs`, replace the first sentence of the
class summary (R99) so the summary reads:

```csharp
/// <summary>
/// Accumulates every external action that took effect, in landing order, and
/// turns the lot into a <see cref="Scenario"/>. A control block's own writes are
/// not recorded: the replay re-runs the block. Attach it with
/// <c>Simulation.AttachActionRecorder</c> and a live run — a command bus, an
/// operator, a test — becomes a file that replays to the same event log.
/// </summary>
```

- [ ] **Step 6: Run the Core and host tests to verify they pass**

Run: `dotnet test tests/Millrace.Core.Tests --nologo`
Expected: PASS, **422** tests.

Run: `dotnet test tests/Millrace.Control.Tests --nologo --filter FullyQualifiedName~HostTests`
Expected: PASS.

Run: `dotnet test tests/Millrace.Control.Tests --nologo --filter FullyQualifiedName~TheWorkedExampleMatchesItsGolden`
Expected: FAIL — `Output differs from golden file '…/Golden/conveyor-control.log'.`
That is the attribution reaching the 5c golden.

- [ ] **Step 7: Regenerate the 5c golden, then read its diff**

Run:

```bash
MILLRACE_UPDATE_GOLDEN=1 dotnet test tests/Millrace.Control.Tests --nologo --filter FullyQualifiedName~TheWorkedExampleMatchesItsGolden
```

```bash
git diff --numstat tests/Millrace.Control.Tests/Golden/conveyor-control.log
```

Expected: `10	10	tests/Millrace.Control.Tests/Golden/conveyor-control.log`.

```bash
git diff --unified=0 tests/Millrace.Control.Tests/Golden/conveyor-control.log
```

**Read the whole diff**, then check it against this checklist and put the
answers — with the lines copied — in the task report:

| # | Check | Expected |
|---|---|---|
| 1 | changed lines | exactly lines 3, 7, 12, 13, 16, 17, 25, 33, 37, 39 |
| 2 | line 3 | `06:00:00.010  CV001.Start  WRITE  Set to false by INT01.` |
| 3 | line 7 | `06:00:01.210  CV001.SafetyReset  WRITE  Set to true by SEQ01.` |
| 4 | lines 12–13 | `06:00:02.210  CV001.SafetyReset  WRITE  Set to false by SEQ01.` / `06:00:02.210  INT01.Reset  WRITE  Set to true by SEQ01.` |
| 5 | lines 16–17 | `06:00:03.210  INT01.Reset  WRITE  Set to false by SEQ01.` / `06:00:03.210  CV001.Start  WRITE  Set to true by SEQ01.` |
| 6 | line 25 | `06:00:04.010  Feed.Enabled  WRITE  Set to true by SEQ01.` |
| 7 | line 33 | `06:00:40.110  CV001.Start  WRITE  Set to false by INT01.` |
| 8 | lines 37, 39 | `06:01:04.010  Feed.Enabled  WRITE  Set to false by SEQ01.` / `06:01:06.210  CV001.Start  WRITE  Set to false by SEQ01.` |
| 9 | the test's own writes | lines 5 and 10 (`SEQ01.Start  WRITE  Set to true.` / `Set to false.`) are **not** in the diff |
| 10 | everything else | no timestamp, source or code changed; the file still has 40 lines |

If any row differs, stop and report it: the attribution must change nothing but
the message suffix.

- [ ] **Step 8: Run everything**

Run: `dotnet build Millrace.sln -c Release --nologo` — expect `0 Warning(s)`, `0 Error(s)`.
Run: `dotnet test Millrace.sln --nologo`
Expected: PASS, **1110** tests (Core 422, Control 102, the rest unchanged). The
four 5b goldens are untouched — they have no blocks.

- [ ] **Step 9: Commit**

```bash
git add src/Millrace.Core/Io/TagImage.cs src/Millrace.Core/Control/ScanBlockRuntime.cs src/Millrace.Core/Simulation.cs src/Millrace.Core/IActionRecorder.cs src/Millrace.Scenarios/ScenarioRecorder.cs tests/Millrace.Core.Tests/ActionRecorderTests.cs tests/Millrace.Core.Tests/ScanBlockHostTests.cs tests/Millrace.Control.Tests/HostTests.cs tests/Millrace.Control.Tests/Golden/conveyor-control.log
```

```bash
git commit -m "$(cat <<'MSG'
feat(core): attribute block writes and keep them out of recordings

A pending write carries its origin. A block's write lands exactly where
it did, is logged "Set to ... by <id>." and never reaches the action
recorder, so a recording holds the external actions only and a replay
re-derives every block write (R77 closed). The 5c golden changes on the
ten block-issued WRITE lines and nowhere else.

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
MSG
)"
```

---

### Task 2: `SimulationBuilder.PlantTags()` — the plant's tags before `Build()`

Model: opus

Spec 4.4 needs "the plant's tags … as `Build()` would produce them (obtained
from Core, through a public `SimulationBuilder` query if none exists)". None
exists (R81). `PlantTags()` runs the builder's own `CollectTags` and wraps the
result in a `TagDirectory`, so the names, order, indices, kinds and accesses are
`Build()`'s own — including R23's read-only downgrade of a writable tag on a
driven input, which is what later makes `MR115` right.

**Files:**
- Modify: `src/Millrace.Core/SimulationBuilder.cs` (new public method after `Validate()`)
- Modify: `src/Millrace.Core/Io/ITagProvider.cs` (the `DescribeTags` summary)
- Create: `tests/Millrace.Core.Tests/PlantTagsTests.cs`

**Interfaces:**
- Consumes: `private List<TagBinding> CollectTags(HashSet<string> componentIds, List<ValidationError> errors)`;
  `internal TagDirectory(IEnumerable<TagBinding> fullyNamed)` and its public
  `IReadOnlyList<TagDescriptor> Tags`; test fakes `Thermostat`, `Setpoint(string id, double value)`
  (`Out` is an `OutputPort<double>`), `EchoBlock`.
- Produces: `public IReadOnlyList<TagDescriptor> SimulationBuilder.PlantTags()`.

- [ ] **Step 1: Write the failing tests**

Create `tests/Millrace.Core.Tests/PlantTagsTests.cs`:

```csharp
using Millrace.Core.Io;
using Millrace.Core.Tests.Fakes;
using Millrace.Core.Time;
using Millrace.Io;
using Xunit;

namespace Millrace.Core.Tests;

public class PlantTagsTests
{
    private static SimulationOptions Options => new()
    {
        Seed = 1UL,
        StartTime = new DateTimeOffset(2026, 1, 1, 6, 0, 0, TimeSpan.Zero),
        TimeStep = TimeSpan.FromMilliseconds(10),
    };

    [Fact]
    public void ThePlantTagsAreTheDirectoryBuildPublishes()
    {
        var u = new Thermostat("U");
        SimulationBuilder builder = new SimulationBuilder(Options)
            .Add(new Thermostat("T"))
            .Add(u)
            .Bind("U.SP", TagBinding.Write("x", u.Setpoint, "°C", 0.0, 50.0, "Renamed setpoint"));

        IReadOnlyList<TagDescriptor> tags = builder.PlantTags();
        Simulation sim = builder.Build();

        Assert.Equal(sim.IO.Directory.Tags, tags);
        Assert.Contains(tags, t => string.Equals(t.Name, "U.SP", StringComparison.Ordinal));
        Assert.DoesNotContain(tags, t => string.Equals(t.Name, "U.Setpoint", StringComparison.Ordinal));
    }

    [Fact]
    public void AWritableTagOnADrivenInputIsReadOnly()
    {
        var t = new Thermostat("T");
        var s = new Setpoint("S", 12.0);
        s.Out.ConnectTo(t.Setpoint);
        SimulationBuilder builder = new SimulationBuilder(Options).Add(s).Add(t);

        IReadOnlyList<TagDescriptor> tags = builder.PlantTags();

        Assert.Equal(TagAccess.ReadOnly, Assert.Single(tags, d => string.Equals(d.Name, "T.Setpoint", StringComparison.Ordinal)).Access);
        Assert.Equal(TagAccess.ReadWrite, Assert.Single(tags, d => string.Equals(d.Name, "T.Enable", StringComparison.Ordinal)).Access);
    }

    [Fact]
    public void ABlocksOwnedTagsAreNotPlantTagsAndTheBuilderStillBuilds()
    {
        SimulationBuilder builder = new SimulationBuilder(Options)
            .Add(new Thermostat("T"))
            .AddScanBlock(new EchoBlock("B", TimeSpan.FromMilliseconds(20)).Reads("T.Enable").Publishes("Q"));

        IReadOnlyList<TagDescriptor> tags = builder.PlantTags();
        Simulation sim = builder.Build();

        Assert.DoesNotContain(tags, t => string.Equals(t.Name, "B.Q", StringComparison.Ordinal));
        Assert.True(sim.IO.Directory.TryFind("B.Q", out _));
        Assert.Equal(tags.Count + 1, sim.IO.Directory.Count);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet build tests/Millrace.Core.Tests --nologo`
Expected: FAIL — `error CS1061: 'SimulationBuilder' does not contain a definition for 'PlantTags'`.

- [ ] **Step 3: Add the query**

In `src/Millrace.Core/SimulationBuilder.cs`, immediately after
`public ValidationResult Validate() => Validate(out _, out _);`, add:

```csharp
    /// <summary>
    /// The tags the plant added so far would publish, exactly as <see cref="Build"/>
    /// would put them in the directory — component tags, composite exposures and
    /// explicit binds, sorted by name and indexed, with the access Build gives them
    /// (a writable tag on an input a link drives is read-only, R23) — without
    /// building anything. A block's owned tags are not included, and neither is a
    /// tag <see cref="Validate()"/> would reject (MR009–MR011). The plant loader
    /// resolves a plant file's controllers against this list.
    /// </summary>
    public IReadOnlyList<TagDescriptor> PlantTags()
    {
        var componentIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (ISimComponent component in _components)
        {
            componentIds.Add(component.Id);
        }

        return new TagDirectory(CollectTags(componentIds, [])).Tags;
    }
```

In `src/Millrace.Core/Io/ITagProvider.cs`, replace the `DescribeTags` summary with:

```csharp
    /// <summary>
    /// The component's bindings. Called by <c>Validate()</c>, <c>Build()</c> and
    /// <c>PlantTags()</c>, so possibly more than once: return fresh bindings over
    /// the same ports and change nothing.
    /// </summary>
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/Millrace.Core.Tests --nologo --filter FullyQualifiedName~PlantTagsTests`
Expected: PASS, 3 tests.

- [ ] **Step 5: Run everything**

Run: `dotnet build Millrace.sln -c Release --nologo` — expect `0 Warning(s)`, `0 Error(s)`.
Run: `dotnet test Millrace.sln --nologo`
Expected: PASS, **1113** tests (Core 425).

- [ ] **Step 6: Commit**

```bash
git add src/Millrace.Core/SimulationBuilder.cs src/Millrace.Core/Io/ITagProvider.cs tests/Millrace.Core.Tests/PlantTagsTests.cs
```

```bash
git commit -m "$(cat <<'MSG'
feat(core): answer which tags a plant has before it is built

PlantTags() runs the builder's own tag collection and returns what the
directory would hold, R23 downgrade included, without building. The
loader resolves controllers against it, so there is one definition of
a plant's tags.

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
MSG
)"
```

---

### Task 3: `BlockDescriptor` and the block entries of a catalogue

Model: sonnet

Spec 3.1. A third entry kind: a type, a description, a parameter tree, the tags
an instance will own (as a function of id and parameters) and a factory that
takes the scan period. Component and block types share one namespace; a clash is
rejected when it is added, naming both modules (R91).

**Files:**
- Create: `src/Millrace.Core/Catalogue/BlockDescriptor.cs`
- Modify: `src/Millrace.Core/Catalogue/CatalogueBuilder.cs`
- Modify: `src/Millrace.Core/Catalogue/ComponentCatalogue.cs`
- Test: `tests/Millrace.Core.Tests/Catalogue/CatalogueModelTests.cs`

**Interfaces:**
- Consumes: `IScanBlock`, `TagSpec(string Name, TagKind Kind, string Unit = "", string Description = "")`
  and `TagAccess` from `Millrace.Io`; `CatalogueBuilder`'s private `RequireKebabCase`,
  `RequireUniqueParameters`, `RequireMaterialStateNamesSibling`, `Duplicate` and
  `_current`; test fake `EchoBlock(id, period).Publishes(string name)`.
- Produces:
  - `public sealed class BlockDescriptor(string type, string description, Func<string, ParameterValues, IReadOnlyList<(TagSpec Spec, TagAccess Access)>> ownedTags, Func<string, TimeSpan, ParameterValues, IScanBlock> factory)`
    with `Type`, `Description`, `OwnedTags`, `Factory`, `IReadOnlyList<ParameterDescriptor> Parameters { get; init; }`.
  - `public CatalogueBuilder CatalogueBuilder.AddBlock(BlockDescriptor descriptor)`.
  - `public IReadOnlyList<BlockDescriptor> ComponentCatalogue.Blocks` (sorted by type),
    `public bool TryGetBlock(string type, [NotNullWhen(true)] out BlockDescriptor? descriptor)`,
    `public string ModuleOf(BlockDescriptor descriptor)`.

- [ ] **Step 1: Write the failing tests**

In `tests/Millrace.Core.Tests/Catalogue/CatalogueModelTests.cs`, add
`using Millrace.Core.Tests.Fakes;` and `using Millrace.Io;` to the usings. Add these
members after the existing `ModuleB` class:

```csharp
    private static BlockDescriptor Echo(string type) =>
        new(type,
            "Echoes a tag.",
            (id, p) => [(new TagSpec($"{id}.Q", TagKind.Bool), TagAccess.ReadOnly)],
            (id, period, p) => new EchoBlock(id, period).Publishes("Q"));

    private sealed class BlocksA : ICatalogueModule
    {
        public string Name => "A";

        public void Register(CatalogueBuilder builder) => builder.AddBlock(Echo("echo"));
    }

    private sealed class BlocksB : ICatalogueModule
    {
        public string Name => "B";

        public void Register(CatalogueBuilder builder) => builder.AddBlock(Echo("echo"));
    }

    private sealed class DelayBlockB : ICatalogueModule
    {
        public string Name => "B";

        public void Register(CatalogueBuilder builder) => builder.AddBlock(Echo("delay"));
    }
```

and these facts at the end of the class:

```csharp
    [Fact]
    public void ABlockIsFoundByTypeWithTheModuleThatRegisteredIt()
    {
        ComponentCatalogue catalogue = new CatalogueBuilder().Add(new BlocksA()).AddBlock(Echo("aaa")).Build();

        Assert.Equal(["aaa", "echo"], catalogue.Blocks.Select(b => b.Type));
        Assert.True(catalogue.TryGetBlock("echo", out BlockDescriptor? echo));
        Assert.Equal("A", catalogue.ModuleOf(echo));
        Assert.Equal("(direct)", catalogue.ModuleOf(catalogue.Blocks[0]));
        Assert.False(catalogue.TryGetBlock("delay", out _));
        Assert.Empty(catalogue.Components);
    }

    [Fact]
    public void ADuplicateBlockTypeNamesBothModules()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => new CatalogueBuilder().Add(new BlocksA()).Add(new BlocksB()));

        Assert.Contains("'echo'", ex.Message, StringComparison.Ordinal);
        Assert.Contains("module 'A'", ex.Message, StringComparison.Ordinal);
        Assert.Contains("module 'B'", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ABlockTypeEqualToAComponentTypeNamesBothModules()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => new CatalogueBuilder().Add(new ModuleA()).Add(new DelayBlockB()));

        Assert.Equal(
            "Type 'delay' is registered as a component by module 'A' and as a block by module 'B'. " +
            "Rename one of them; component and block types share one namespace.",
            ex.Message);
    }

    [Fact]
    public void AComponentTypeEqualToABlockTypeNamesBothModules()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => new CatalogueBuilder().Add(new DelayBlockB()).Add(new ModuleA()));

        Assert.Equal(
            "Type 'delay' is registered as a block by module 'B' and as a component by module 'A'. " +
            "Rename one of them; component and block types share one namespace.",
            ex.Message);
    }

    [Fact]
    public void ABlockTypeMustBeKebabCase()
    {
        Assert.Throws<ArgumentException>(() => new CatalogueBuilder().AddBlock(Echo("Echo_Block")));
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet build tests/Millrace.Core.Tests --nologo`
Expected: FAIL — `error CS0246: The type or namespace name 'BlockDescriptor' could not be found`.

- [ ] **Step 3: Write `BlockDescriptor`**

Create `src/Millrace.Core/Catalogue/BlockDescriptor.cs`:

```csharp
using Millrace.Io;

namespace Millrace.Core.Catalogue;

/// <summary>
/// Everything a tool or an agent needs to know about a control block type: its
/// parameters, the tags an instance will own, and the factory that builds one.
/// Hand-written beside the module that registers it; <c>CatalogueConformance</c>
/// keeps it honest.
/// </summary>
public sealed class BlockDescriptor
{
    public BlockDescriptor(
        string type,
        string description,
        Func<string, ParameterValues, IReadOnlyList<(TagSpec Spec, TagAccess Access)>> ownedTags,
        Func<string, TimeSpan, ParameterValues, IScanBlock> factory)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ArgumentNullException.ThrowIfNull(ownedTags);
        ArgumentNullException.ThrowIfNull(factory);
        Type = type;
        Description = description;
        OwnedTags = ownedTags;
        Factory = factory;
    }

    /// <summary>Kebab-case, unique among components and blocks: <c>interlock</c>.</summary>
    public string Type { get; }

    public string Description { get; }

    /// <summary>
    /// The tags a block of this type with this id and these parameters will own,
    /// by full name (<c>INT01.Ok</c>): outputs <see cref="TagAccess.ReadOnly"/>,
    /// commands <see cref="TagAccess.ReadWrite"/>. A function of the parameters
    /// alone, called before any block exists, with the parameters bound for
    /// checking: scalars, enums, strings, groups and tag names are final, but
    /// values are provisional and object parameters are not yet built, so it must
    /// read neither.
    /// </summary>
    public Func<string, ParameterValues, IReadOnlyList<(TagSpec Spec, TagAccess Access)>> OwnedTags { get; }

    /// <summary>Builds a block from its id, its scan period and its resolved parameters.</summary>
    public Func<string, TimeSpan, ParameterValues, IScanBlock> Factory { get; }

    public IReadOnlyList<ParameterDescriptor> Parameters { get; init; } = [];
}
```

- [ ] **Step 4: Register blocks in the builder**

In `src/Millrace.Core/Catalogue/CatalogueBuilder.cs`:

Add the dictionary after `_components`:

```csharp
    private readonly Dictionary<string, (BlockDescriptor Descriptor, string Module)> _blocks = new(StringComparer.Ordinal);
```

In `Add(ComponentDescriptor descriptor)`, immediately before
`if (_components.TryGetValue(descriptor.Type, out var existing))`, add:

```csharp
        if (_blocks.TryGetValue(descriptor.Type, out var block))
        {
            throw SharedName(descriptor.Type, block.Module, "a block", "a component");
        }
```

Add, after `Add(ComponentDescriptor)`:

```csharp
    public CatalogueBuilder AddBlock(BlockDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        RequireKebabCase(descriptor.Type, "Block type");
        RequireUniqueParameters(descriptor.Parameters, $"block '{descriptor.Type}'");
        RequireMaterialStateNamesSibling(descriptor.Parameters, $"block '{descriptor.Type}'");
        if (_components.TryGetValue(descriptor.Type, out var component))
        {
            throw SharedName(descriptor.Type, component.Module, "a component", "a block");
        }

        if (_blocks.TryGetValue(descriptor.Type, out var existing))
        {
            throw Duplicate("Block type", descriptor.Type, existing.Module);
        }

        _blocks[descriptor.Type] = (descriptor, _current);
        return this;
    }
```

Replace `Build()`:

```csharp
    public ComponentCatalogue Build() => new(
        _components.Values.OrderBy(e => e.Descriptor.Type, StringComparer.Ordinal).ToList(),
        _blocks.Values.OrderBy(e => e.Descriptor.Type, StringComparer.Ordinal).ToList(),
        _objects.Values
            .OrderBy(e => e.Descriptor.Slot, StringComparer.Ordinal)
            .ThenBy(e => e.Descriptor.Type, StringComparer.Ordinal)
            .ToList(),
        _materials.Values.OrderBy(e => e.Descriptor.Material.Name, StringComparer.Ordinal).ToList(),
        _modules.ToList());
```

Add, after `Duplicate`:

```csharp
    private InvalidOperationException SharedName(string name, string firstModule, string first, string second) => new(
        $"Type '{name}' is registered as {first} by module '{firstModule}' and as {second} by module '{_current}'. " +
        "Rename one of them; component and block types share one namespace.");
```

- [ ] **Step 5: Expose blocks from the catalogue**

In `src/Millrace.Core/Catalogue/ComponentCatalogue.cs`, add the field after
`_components`:

```csharp
    private readonly Dictionary<string, (BlockDescriptor Descriptor, string Module)> _blocks;
```

Replace the constructor's signature and first lines so it reads:

```csharp
    internal ComponentCatalogue(
        List<(ComponentDescriptor Descriptor, string Module)> components,
        List<(BlockDescriptor Descriptor, string Module)> blocks,
        List<(ObjectDescriptor Descriptor, string Module)> objects,
        List<(MaterialDescriptor Descriptor, string Module)> materials,
        List<string> modules)
    {
        Components = components.Select(e => e.Descriptor).ToList();
        Blocks = blocks.Select(e => e.Descriptor).ToList();
        Objects = objects.Select(e => e.Descriptor).ToList();
        Materials = materials.Select(e => e.Descriptor).ToList();
        Modules = modules;
        Slots = Objects.Select(o => o.Slot).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        _components = components.ToDictionary(e => e.Descriptor.Type, StringComparer.Ordinal);
        _blocks = blocks.ToDictionary(e => e.Descriptor.Type, StringComparer.Ordinal);
        _objects = objects.ToDictionary(e => (e.Descriptor.Slot, e.Descriptor.Type));
        _materials = materials.ToDictionary(e => e.Descriptor.Material.Name, StringComparer.Ordinal);
    }
```

Add after the `Components` property:

```csharp
    /// <summary>Sorted by type name.</summary>
    public IReadOnlyList<BlockDescriptor> Blocks { get; }
```

Add after `TryGetComponent`:

```csharp
    public bool TryGetBlock(string type, [NotNullWhen(true)] out BlockDescriptor? descriptor)
    {
        bool found = _blocks.TryGetValue(type, out var entry);
        descriptor = found ? entry.Descriptor : null;
        return found;
    }
```

Add after `ModuleOf(ComponentDescriptor)`:

```csharp
    /// <inheritdoc cref="ModuleOf(ComponentDescriptor)"/>
    public string ModuleOf(BlockDescriptor descriptor) => _blocks[descriptor.Type].Module;
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/Millrace.Core.Tests --nologo --filter FullyQualifiedName~CatalogueModelTests`
Expected: PASS, including the five new facts.

- [ ] **Step 7: Run everything**

Run: `dotnet build Millrace.sln -c Release --nologo` — expect `0 Warning(s)`, `0 Error(s)`.
Run: `dotnet test Millrace.sln --nologo`
Expected: PASS, **1118** tests (Core 430). The components golden is unchanged:
nothing exports blocks yet.

- [ ] **Step 8: Commit**

```bash
git add src/Millrace.Core/Catalogue/BlockDescriptor.cs src/Millrace.Core/Catalogue/CatalogueBuilder.cs src/Millrace.Core/Catalogue/ComponentCatalogue.cs tests/Millrace.Core.Tests/Catalogue/CatalogueModelTests.cs
```

```bash
git commit -m "$(cat <<'MSG'
feat(catalogue): add block descriptors beside components and objects

A block type declares its parameters, the tags an instance will own and
a factory that takes the scan period. Component and block types share
one namespace, and a clash names both modules when it is added.

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
MSG
)"
```

---

### Task 4: The `Tag` and `Value` parameter kinds

Model: opus

Spec 3.2, modelled on the `Material` / `MaterialState` sibling pair. A `Tag` is
a string the binder resolves against the context's tag table; a `Value` is a
boolean or a number converted to the kind of the tag its sibling names. Every
cell of spec 3.2's table is a test row. Resolution happens only in construct
mode on a context given a table (R84); a wrong-kind tag is an issue at the tag
(R83); a value whose tag did not resolve is not reported twice.

**Files:**
- Modify: `src/Millrace.Core/Catalogue/ParameterKind.cs`, `ParameterDescriptor.cs`, `Param.cs`,
  `ParameterValues.cs`, `BindingContext.cs`, `BindingIssue.cs`, `ParameterBinder.cs`,
  `CatalogueBuilder.cs`
- Create: `tests/Millrace.Core.Tests/Catalogue/TagParameterTests.cs`

**Interfaces:**
- Consumes: `ParameterBinder.Bind(IReadOnlyList<ParameterDescriptor> schema, JsonElement json, string path, BindingContext context, bool construct, List<BindingIssue> issues)`;
  `Suggest.Closest(string, IEnumerable<string>)`; `TagValue.Bool/Double/Int64`; `CatalogueBuilder.AddBlock` (Task 3).
- Produces:
  - `ParameterKind.Tag`, `ParameterKind.Value` (appended after `ObjectList`).
  - `ParameterDescriptor.RequiredKind` (`TagKind?`), `IsWriteTarget` (`bool`), `TagParameter` (`string`, empty when unset).
  - `Param.Tag(string name, string description, TagKind? kind = null, bool writes = false)`;
    `Param.Value(string name, string description, string tagParameter)`.
  - `ParameterValues.Tag(string name) → string`; `ParameterValues.Value(string name) → TagValue`.
  - `BindingContext.UseTags(IEnumerable<(string Name, TagKind Kind, TagAccess Access)> tags) → BindingContext`
    (once; first entry for a name wins), `bool ResolvesTags`, `IReadOnlyList<string> TagNames`,
    `bool TryGetTag(string name, out TagKind kind, out TagAccess access)`.
  - `BindingIssueKind.UnknownTag`, `WrongTagKind`, `ReadOnlyTag`.
  - Issue texts: `'{name}' is not a tag in this plant.` / fix `Use a tag the plant has — '{closest}' is closest.`
    (or `Use a tag the plant has; ` + "`millrace tags`" + ` lists them.`);
    `'{name}' is {a kind} tag, but '{parameter}' needs {a kind} tag.`;
    `'{name}' is read-only, so a block cannot command it.`;
    `{raw} does not fit '{tag}', which is {a kind} tag.` with fix `Write true or false.` /
    `Write a whole number.` / `Write a number.` — where `{a kind}` is `a Bool`, `a Double` or `an Int64`.

- [ ] **Step 1: Write the failing tests**

Create `tests/Millrace.Core.Tests/Catalogue/TagParameterTests.cs`:

```csharp
using System.Text.Json;
using Millrace.Core.Catalogue;
using Millrace.Core.Graph;
using Millrace.Io;

namespace Millrace.Core.Tests.Catalogue;

public class TagParameterTests
{
    private static readonly ParameterDescriptor[] Write =
    [
        Param.Tag("tag", "The tag commanded.", writes: true),
        Param.Value("value", "What it is set to.", "tag"),
    ];

    private static readonly ParameterDescriptor[] Compare =
    [
        Param.Tag("tag", "The tag compared."),
        Param.Value("value", "What it is compared with.", "tag"),
    ];

    private static readonly (string Name, TagKind Kind, TagAccess Access)[] Tags =
    [
        ("CV001.Start", TagKind.Bool, TagAccess.ReadWrite),
        ("CV001.Speed", TagKind.Double, TagAccess.ReadOnly),
        ("Feed.Rate", TagKind.Double, TagAccess.ReadWrite),
        ("Batch.Count", TagKind.Int64, TagAccess.ReadWrite),
        ("PERM01.FirstOut", TagKind.Int64, TagAccess.ReadOnly),
    ];

    private static (ParameterValues? Values, List<BindingIssue> Issues) Bind(
        IReadOnlyList<ParameterDescriptor> schema, string json, bool resolve = true, bool construct = true)
    {
        var context = new BindingContext(new CatalogueBuilder().Build());
        if (resolve)
        {
            context.UseTags(Tags);
        }

        using JsonDocument document = JsonDocument.Parse(json);
        var issues = new List<BindingIssue>();
        ParameterValues? values = ParameterBinder.Bind(schema, document.RootElement, "$.p", context, construct, issues);
        return (values, issues);
    }

    [Fact]
    public void ATagAndAValueAreRequiredAndTheValueNamesItsTag()
    {
        ParameterDescriptor tag = Param.Tag("input", "The tag watched.", TagKind.Double);
        ParameterDescriptor value = Param.Value("value", "The value.", "input");

        Assert.True(tag.IsRequired);
        Assert.True(value.IsRequired);
        Assert.Equal(ParameterKind.Tag, tag.Kind);
        Assert.Equal(TagKind.Double, tag.RequiredKind);
        Assert.False(tag.IsWriteTarget);
        Assert.Equal(ParameterKind.Value, value.Kind);
        Assert.Equal("input", value.TagParameter);
    }

    [Theory]
    [InlineData("true", "CV001.Start", "Bool true")]
    [InlineData("true", "Batch.Count", "WrongTagKind")]
    [InlineData("true", "Feed.Rate", "WrongTagKind")]
    [InlineData("3", "CV001.Start", "WrongTagKind")]
    [InlineData("3", "Batch.Count", "Int64 3")]
    [InlineData("3", "Feed.Rate", "Double 3")]
    [InlineData("3.0", "Batch.Count", "Int64 3")]
    [InlineData("3e0", "Batch.Count", "Int64 3")]
    [InlineData("1.5", "CV001.Start", "WrongTagKind")]
    [InlineData("1.5", "Batch.Count", "WrongTagKind")]
    [InlineData("1.5", "Feed.Rate", "Double 1.5")]
    [InlineData("1e20", "Batch.Count", "WrongTagKind")]
    public void AValueConvertsToItsTagsKind(string value, string tag, string expected)
    {
        (ParameterValues? values, List<BindingIssue> issues) = Bind(Write, $$"""{ "tag": "{{tag}}", "value": {{value}} }""");

        string actual = values is null
            ? Assert.Single(issues).Kind.ToString()
            : $"{values.Value("value").Kind} {values.Value("value")}";
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void AnUnknownTagSuggestsTheNearestName()
    {
        (ParameterValues? values, List<BindingIssue> issues) = Bind(Write, """{ "tag": "CV001.Strat", "value": true }""");

        Assert.Null(values);
        BindingIssue issue = Assert.Single(issues);           // the value is not reported a second time
        Assert.Equal(BindingIssueKind.UnknownTag, issue.Kind);
        Assert.Equal("$.p.tag", issue.Path);
        Assert.Equal("'CV001.Strat' is not a tag in this plant.", issue.Message);
        Assert.Equal("Use a tag the plant has — 'CV001.Start' is closest.", issue.Fix);
    }

    [Fact]
    public void ATagOfTheWrongKindIsAnIssueAtTheTag()
    {
        ParameterDescriptor[] schema = [Param.Tag("input", "A Bool tag.", TagKind.Bool)];

        (ParameterValues? values, List<BindingIssue> issues) = Bind(schema, """{ "input": "PERM01.FirstOut" }""");

        Assert.Null(values);
        BindingIssue issue = Assert.Single(issues);
        Assert.Equal(BindingIssueKind.WrongTagKind, issue.Kind);
        Assert.Equal("$.p.input", issue.Path);
        Assert.Equal("'PERM01.FirstOut' is an Int64 tag, but 'input' needs a Bool tag.", issue.Message);
    }

    [Fact]
    public void ACommandedTagMustBeReadWrite()
    {
        (ParameterValues? values, List<BindingIssue> issues) = Bind(Write, """{ "tag": "CV001.Speed", "value": 1.0 }""");

        Assert.Null(values);
        BindingIssue issue = Assert.Single(issues);
        Assert.Equal(BindingIssueKind.ReadOnlyTag, issue.Kind);
        Assert.Equal("$.p.tag", issue.Path);
        Assert.Equal("'CV001.Speed' is read-only, so a block cannot command it.", issue.Message);
    }

    [Fact]
    public void AReadOnlyTagMayBeCompared()
    {
        (ParameterValues? values, List<BindingIssue> issues) = Bind(Compare, """{ "tag": "PERM01.FirstOut", "value": 2 }""");

        Assert.Empty(issues);
        Assert.Equal("PERM01.FirstOut", values!.Tag("tag"));
        Assert.Equal(TagValue.Int64(2), values.Value("value"));
    }

    [Fact]
    public void AValueMustBeABooleanOrANumber()
    {
        (_, List<BindingIssue> issues) = Bind(Write, """{ "tag": "CV001.Start", "value": "on" }""");

        BindingIssue issue = Assert.Single(issues);
        Assert.Equal(BindingIssueKind.BadParameter, issue.Kind);
        Assert.Equal("$.p.value", issue.Path);
        Assert.Equal("'value' must be true, false or a number, but it is a string.", issue.Message);
    }

    [Fact]
    public void WithoutATagTableATagIsANameAndAValueIsProvisional()
    {
        (ParameterValues? values, List<BindingIssue> issues) = Bind(Write, """{ "tag": "Nowhere.Tag", "value": 3 }""", resolve: false);

        Assert.Empty(issues);
        Assert.Equal("Nowhere.Tag", values!.Tag("tag"));
        Assert.Equal(TagValue.Double(3.0), values.Value("value"));
    }

    [Fact]
    public void InCheckModeTagsAreNotResolvedEvenWithATable()
    {
        (ParameterValues? values, List<BindingIssue> issues) = Bind(Write, """{ "tag": "Nowhere.Tag", "value": true }""", construct: false);

        Assert.Empty(issues);
        Assert.Equal(TagValue.Bool(true), values!.Value("value"));
    }

    [Fact]
    public void AValueMustNameASiblingTagParameter()
    {
        var descriptor = new ObjectDescriptor("probe", "set", "Sets a tag.", p => new object())
        {
            Parameters = [Param.Tag("target", "The tag."), Param.Value("value", "The value.", "tag")],
        };

        var ex = Assert.Throws<ArgumentException>(() => new CatalogueBuilder().Add(descriptor));

        Assert.Contains("'tag'", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Declared tag parameters: target.", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AComponentMayNotDeclareATagParameter()
    {
        var descriptor = new ComponentDescriptor("tagged", ComponentCategory.Signal, "Names a tag.", (id, p) => new UnitDelay<bool>(id))
        {
            Parameters = [Param.Tag("input", "A tag.")],
        };

        var ex = Assert.Throws<ArgumentException>(() => new CatalogueBuilder().Add(descriptor));

        Assert.Contains("only a block or an object may declare", ex.Message, StringComparison.Ordinal);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet build tests/Millrace.Core.Tests --nologo`
Expected: FAIL — `error CS0117: 'Param' does not contain a definition for 'Tag'`.

- [ ] **Step 3: Add the kinds, the descriptor properties and the factories**

In `src/Millrace.Core/Catalogue/ParameterKind.cs`, append after `ObjectList,`:

```csharp
    /// <summary>The full name of a tag a block reads or commands; resolved by the loader.</summary>
    Tag,
    /// <summary>A boolean or a number for the tag a sibling <see cref="Tag"/> parameter names, converted to its kind.</summary>
    Value,
```

In `src/Millrace.Core/Catalogue/ParameterDescriptor.cs`, add `using Millrace.Io;`, add
these properties after `MaterialParameter`:

```csharp
    /// <summary>For <see cref="ParameterKind.Tag"/>: the kind the tag must have, or null for any.</summary>
    public TagKind? RequiredKind { get; init; }

    /// <summary>For <see cref="ParameterKind.Tag"/>: the block commands this tag, so it must be read-write.</summary>
    public bool IsWriteTarget { get; init; }

    /// <summary>For <see cref="ParameterKind.Value"/>: the sibling tag parameter whose kind it takes.</summary>
    public string TagParameter { get; init; } = string.Empty;
```

and extend the `IsRequired` arm for references:

```csharp
        ParameterKind.Reference or ParameterKind.Material or ParameterKind.MaterialState or ParameterKind.Object
            or ParameterKind.Tag or ParameterKind.Value => !IsOptional,
```

In `src/Millrace.Core/Catalogue/Param.cs`, add `using Millrace.Io;` and, after
`MaterialState`:

```csharp
    /// <summary>
    /// The full name of a tag: a plant tag or one a block owns. With
    /// <paramref name="kind"/>, the tag must have that kind; with
    /// <paramref name="writes"/>, the block commands it, so it must be read-write.
    /// </summary>
    public static ParameterDescriptor Tag(string name, string description, TagKind? kind = null, bool writes = false) =>
        new(name, ParameterKind.Tag, description)
        {
            RequiredKind = kind,
            IsWriteTarget = writes,
        };

    /// <summary>A boolean or a number for the tag the sibling <paramref name="tagParameter"/> names, converted to that tag's kind.</summary>
    public static ParameterDescriptor Value(string name, string description, string tagParameter)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tagParameter);
        return new ParameterDescriptor(name, ParameterKind.Value, description) { TagParameter = tagParameter };
    }
```

In `src/Millrace.Core/Catalogue/ParameterValues.cs`, add `using Millrace.Io;` and, after
`StateIndex`:

```csharp
    /// <summary>A <see cref="ParameterKind.Tag"/>: the tag's full name.</summary>
    public string Tag(string name) => Get<string>(name);

    /// <summary>A <see cref="ParameterKind.Value"/>, of the kind of the tag its sibling names.</summary>
    public TagValue Value(string name) => Get<TagValue>(name);
```

In `src/Millrace.Core/Catalogue/BindingIssue.cs`, append to `BindingIssueKind` after
`Rejected,`:

```csharp
    /// <summary>A tag name the context's tag table does not hold. Construct mode with a tag table only.</summary>
    UnknownTag,
    /// <summary>A tag of the wrong kind, or a value that does not fit its tag's kind. Construct mode with a tag table only.</summary>
    WrongTagKind,
    /// <summary>A block commands a read-only tag. Construct mode with a tag table only.</summary>
    ReadOnlyTag,
```

- [ ] **Step 4: Give the binding context a tag table**

In `src/Millrace.Core/Catalogue/BindingContext.cs`, add `using Millrace.Io;`, a field
after `_nodes`:

```csharp
    private Dictionary<string, (TagKind Kind, TagAccess Access)>? _tags;
```

and, after `NodeIds`:

```csharp
    /// <summary>True once <see cref="UseTags"/> was called: tag parameters then resolve in construct mode.</summary>
    public bool ResolvesTags => _tags is not null;

    /// <summary>Every tag name in the table, sorted; empty when there is no table.</summary>
    public IReadOnlyList<string> TagNames =>
        _tags is null ? [] : _tags.Keys.Order(StringComparer.Ordinal).ToList();

    /// <summary>
    /// The tags a <see cref="ParameterKind.Tag"/> parameter may name. Called once;
    /// a name given twice keeps its first entry — a plant tag before a block's
    /// owned tag — and the builder reports the clash (MR015).
    /// </summary>
    public BindingContext UseTags(IEnumerable<(string Name, TagKind Kind, TagAccess Access)> tags)
    {
        ArgumentNullException.ThrowIfNull(tags);
        if (_tags is not null)
        {
            throw new InvalidOperationException("This context already has a tag table.");
        }

        _tags = new Dictionary<string, (TagKind Kind, TagAccess Access)>(StringComparer.Ordinal);
        foreach ((string name, TagKind kind, TagAccess access) in tags)
        {
            _tags.TryAdd(name, (kind, access));
        }

        return this;
    }

    public bool TryGetTag(string name, out TagKind kind, out TagAccess access)
    {
        if (_tags is not null && _tags.TryGetValue(name, out var entry))
        {
            (kind, access) = entry;
            return true;
        }

        kind = default;
        access = default;
        return false;
    }
```

- [ ] **Step 5: Bind tags and values**

In `src/Millrace.Core/Catalogue/ParameterBinder.cs`, add `using Millrace.Io;`.

In `BindObject`, change the ordering line so values bind after their tags:

```csharp
        // Material states index a sibling material and values convert to a sibling tag's kind, so both bind last.
        foreach (ParameterDescriptor parameter in schema.OrderBy(p => p.Kind is ParameterKind.MaterialState or ParameterKind.Value ? 1 : 0))
```

In `TryBindValue`'s switch, before `default:`, add:

```csharp
            case ParameterKind.Tag:
                return TryBindTag(parameter, element, path, context, construct, issues, out bound);

            case ParameterKind.Value:
                return TryBindTagValue(parameter, element, path, context, construct, issues, siblings, out bound);
```

Add these methods after `TryBindState`:

```csharp
    private static bool TryBindTag(
        ParameterDescriptor parameter,
        JsonElement element,
        string path,
        BindingContext context,
        bool construct,
        List<BindingIssue> issues,
        out object? bound)
    {
        bound = null;
        if (element.ValueKind != JsonValueKind.String)
        {
            issues.Add(WrongType(path, parameter, "a tag name (a string)", element));
            return false;
        }

        string name = element.GetString()!;
        if (construct && context.ResolvesTags)
        {
            if (!context.TryGetTag(name, out TagKind kind, out TagAccess access))
            {
                string? closest = Suggest.Closest(name, context.TagNames);
                issues.Add(new BindingIssue(
                    BindingIssueKind.UnknownTag,
                    path,
                    $"'{name}' is not a tag in this plant.",
                    closest is null
                        ? "Use a tag the plant has; `millrace tags` lists them."
                        : $"Use a tag the plant has — '{closest}' is closest."));
                return false;
            }

            if (parameter.RequiredKind is { } required && kind != required)
            {
                issues.Add(new BindingIssue(
                    BindingIssueKind.WrongTagKind,
                    path,
                    $"'{name}' is {A(kind)} tag, but '{parameter.Name}' needs {A(required)} tag.",
                    $"Name {A(required)} tag; `millrace tags` lists every tag with its kind."));
                return false;
            }

            if (parameter.IsWriteTarget && access != TagAccess.ReadWrite)
            {
                issues.Add(new BindingIssue(
                    BindingIssueKind.ReadOnlyTag,
                    path,
                    $"'{name}' is read-only, so a block cannot command it.",
                    "Command a read-write tag; `millrace tags` shows each tag's access. An input a signal link drives is read-only."));
                return false;
            }
        }

        bound = name;
        return true;
    }

    private static bool TryBindTagValue(
        ParameterDescriptor parameter,
        JsonElement element,
        string path,
        BindingContext context,
        bool construct,
        List<BindingIssue> issues,
        Dictionary<string, object?> siblings,
        out object? bound)
    {
        bound = null;
        bool isBool = element.ValueKind is JsonValueKind.True or JsonValueKind.False;
        double number = 0.0;
        if (!isBool && (element.ValueKind != JsonValueKind.Number || !element.TryGetDouble(out number) || !double.IsFinite(number)))
        {
            issues.Add(WrongType(path, parameter, "true, false or a number", element));
            return false;
        }

        if (!construct || !context.ResolvesTags)
        {
            // R84: nothing to convert against, so the value is provisional.
            bound = isBool ? TagValue.Bool(element.GetBoolean()) : TagValue.Double(number);
            return true;
        }

        if (!siblings.TryGetValue(parameter.TagParameter, out object? sibling)
            || sibling is not string tag
            || !context.TryGetTag(tag, out TagKind kind, out _))
        {
            // The tag did not resolve, and that is already reported: one mistake, one diagnostic.
            return false;
        }

        TagValue? converted = kind switch
        {
            TagKind.Bool => isBool ? TagValue.Bool(element.GetBoolean()) : (TagValue?)null,
            TagKind.Int64 => !isBool && TryWhole(element, number, out long whole) ? TagValue.Int64(whole) : (TagValue?)null,
            _ => isBool ? (TagValue?)null : TagValue.Double(number),
        };

        if (converted is not { } value)
        {
            issues.Add(new BindingIssue(
                BindingIssueKind.WrongTagKind,
                path,
                $"{element.GetRawText()} does not fit '{tag}', which is {A(kind)} tag.",
                kind switch
                {
                    TagKind.Bool => "Write true or false.",
                    TagKind.Int64 => "Write a whole number.",
                    _ => "Write a number.",
                }));
            return false;
        }

        bound = value;
        return true;
    }

    /// <summary>An integer-valued JSON number — <c>3</c>, <c>3.0</c>, <c>3e0</c> — that fits in 64 bits.</summary>
    private static bool TryWhole(JsonElement element, double number, out long whole)
    {
        if (element.TryGetInt64(out whole))
        {
            return true;
        }

        // TryGetInt64 refuses "3.0" and "3e0" (measured); the double path accepts them.
        if (Math.Floor(number) == number && number >= -9.2233720368547758E18 && number < 9.2233720368547758E18)
        {
            whole = (long)number;
            return true;
        }

        whole = 0L;
        return false;
    }

    private static string A(TagKind kind) => kind == TagKind.Int64 ? "an Int64" : $"a {kind}";
```

In `Example`, before `_ => "a value",`, add:

```csharp
        ParameterKind.Tag => "the full name of a tag, such as CV001.Start",
        ParameterKind.Value => "true, false or a number",
```

- [ ] **Step 6: Guard the new kinds in the builder**

In `src/Millrace.Core/Catalogue/CatalogueBuilder.cs`:

In `Add(ComponentDescriptor)`, after `RequireMaterialStateNamesSibling(...)`, add
`RequireNoTagParameters(descriptor.Parameters, $"component '{descriptor.Type}'");`.

In `AddBlock`, after `RequireMaterialStateNamesSibling(...)`, add
`RequireValueNamesTagSibling(descriptor.Parameters, $"block '{descriptor.Type}'");`.

In `Add(ObjectDescriptor)`, after `RequireMaterialStateNamesSibling(...)`, add
`RequireValueNamesTagSibling(descriptor.Parameters, $"{descriptor.Slot} '{descriptor.Type}'");`.

Add these methods after `RequireMaterialStateNamesSibling`:

```csharp
    private static void RequireValueNamesTagSibling(IReadOnlyList<ParameterDescriptor> parameters, string owner)
    {
        List<string> tags = parameters
            .Where(p => p.Kind == ParameterKind.Tag)
            .Select(p => p.Name)
            .ToList();

        foreach (ParameterDescriptor parameter in parameters)
        {
            if (parameter.Kind == ParameterKind.Value && !tags.Contains(parameter.TagParameter, StringComparer.Ordinal))
            {
                string declared = tags.Count == 0 ? "none" : string.Join(", ", tags.Order(StringComparer.Ordinal));
                throw new ArgumentException(
                    $"Parameter '{parameter.Name}' on {owner} is a value for the tag named by '{parameter.TagParameter}', " +
                    $"but no tag parameter of that name is declared beside it. Declared tag parameters: {declared}.",
                    nameof(parameters));
            }

            if (parameter.Children.Count > 0)
            {
                RequireValueNamesTagSibling(parameter.Children, $"{owner}, group '{parameter.Name}'");
            }
        }
    }

    /// <summary>R97: a component has no tag table to resolve against; it reads and drives signals through its ports.</summary>
    private static void RequireNoTagParameters(IReadOnlyList<ParameterDescriptor> parameters, string owner)
    {
        foreach (ParameterDescriptor parameter in parameters)
        {
            if (parameter.Kind is ParameterKind.Tag or ParameterKind.Value)
            {
                throw new ArgumentException(
                    $"Parameter '{parameter.Name}' on {owner} is a {CatalogueJson.Camel(parameter.Kind.ToString())} parameter, " +
                    "which only a block or an object may declare. A component reads and drives signals through its ports.",
                    nameof(parameters));
            }

            if (parameter.Children.Count > 0)
            {
                RequireNoTagParameters(parameter.Children, $"{owner}, group '{parameter.Name}'");
            }
        }
    }
```

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test tests/Millrace.Core.Tests --nologo --filter FullyQualifiedName~TagParameterTests`
Expected: PASS, 22 tests (10 facts and 12 theory rows).

- [ ] **Step 8: Run everything**

Run: `dotnet build Millrace.sln -c Release --nologo` — expect `0 Warning(s)`, `0 Error(s)`.
Run: `dotnet test Millrace.sln --nologo`
Expected: PASS, **1140** tests (Core 452). `ParameterBinderTests` is unchanged:
no existing schema has a `Tag` or `Value`, so the reordering of `MaterialState`
binding is behaviour-neutral.

- [ ] **Step 9: Commit**

```bash
git add src/Millrace.Core/Catalogue/ParameterKind.cs src/Millrace.Core/Catalogue/ParameterDescriptor.cs src/Millrace.Core/Catalogue/Param.cs src/Millrace.Core/Catalogue/ParameterValues.cs src/Millrace.Core/Catalogue/BindingContext.cs src/Millrace.Core/Catalogue/BindingIssue.cs src/Millrace.Core/Catalogue/ParameterBinder.cs src/Millrace.Core/Catalogue/CatalogueBuilder.cs tests/Millrace.Core.Tests/Catalogue/TagParameterTests.cs
```

```bash
git commit -m "$(cat <<'MSG'
feat(catalogue): resolve tag parameters and convert values to their kind

A Tag parameter is a tag's full name, checked against the context's tag
table for existence, kind and, for a write target, access. A Value is a
boolean or a number converted to its sibling tag's kind; a value whose
tag failed is not reported twice. Without a table both stay unresolved.

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
MSG
)"
```

---

### Task 5: Conformance for blocks

Model: sonnet

Spec 3.5. `CatalogueConformance.Check` builds a probe of every block type from
each of that type's fixtures and reports every difference between the
descriptor's `OwnedTags` and the instance's `Outputs` (read-only) and `Commands`
(read-write), by full name, kind and access, and — for a tag that matches on
those three — every difference in unit or description (R102; spec 3.4 says the
conformance check proves all four). A factory that reads an undeclared
parameter throws out of `ParameterValues`, which `TryBuild` already reports, as
for components; a test pins that for blocks. A type may carry several fixtures; each is checked and, when
there is more than one, labelled `type (fixture n)`.

**Files:**
- Modify: `src/Millrace.Core/Testing/CatalogueConformance.cs`
- Modify: `src/Millrace.Core/Testing/ConformanceFixtures.cs`
- Test: `tests/Millrace.Core.Tests/Catalogue/CatalogueConformanceTests.cs`

**Interfaces:**
- Consumes: `ComponentCatalogue.Blocks`, `BlockDescriptor.OwnedTags` and `.Factory` (Task 3);
  `Param.Tag`, `ParameterValues.Tag` (Task 4); the private `BindFixture`, `TryBuild`
  and `Diff` of `CatalogueConformance`.
- Produces: `public static TimeSpan CatalogueConformance.ProbePeriod` (100 ms);
  `public ConformanceFixtures ConformanceFixtures.BlockParameters(string blockType, string json)`
  (callable repeatedly for one type). Mismatch texts:
  `"{label}: owned tag '{name}' ({Kind} {Access}) is in the descriptor but not on the instance."`,
  `"{label}: owned tag '{name}' ({Kind} {Access}) is on the instance but not in the descriptor."`,
  `"{label}: owned tag '{name}' has unit '{declared}' in the descriptor but '{actual}' on the instance."`,
  `"{label}: owned tag '{name}' is described '{declared}' in the descriptor but '{actual}' on the instance."`.

- [ ] **Step 1: Write the failing tests**

In `tests/Millrace.Core.Tests/Catalogue/CatalogueConformanceTests.cs`, add
`using Millrace.Core.Tests.Fakes;` to the usings and add these members at the end of
the class:

```csharp
    private static BlockDescriptor EchoDescriptor(Func<string, ParameterValues, IReadOnlyList<(TagSpec Spec, TagAccess Access)>> owned) =>
        new("echo",
            "Echoes a tag.",
            owned,
            (id, period, p) => new EchoBlock(id, period).Reads(p.Tag("input")).Publishes("Q").Accepts("Cmd"))
        {
            Parameters =
            [
                Param.Tag("input", "The tag echoed.", TagKind.Bool),
                Param.Bool("extra", "Declare one more tag than the block has.", @default: false),
            ],
        };

    private static IReadOnlyList<(TagSpec Spec, TagAccess Access)> HonestTags(string id, ParameterValues p)
    {
        var tags = new List<(TagSpec Spec, TagAccess Access)>
        {
            (new TagSpec($"{id}.Q", TagKind.Bool), TagAccess.ReadOnly),
            (new TagSpec($"{id}.Cmd", TagKind.Bool), TagAccess.ReadWrite),
        };
        if (p.Bool("extra"))
        {
            tags.Add((new TagSpec($"{id}.Extra", TagKind.Bool), TagAccess.ReadOnly));
        }

        return tags;
    }

    private static ConformanceReport CheckBlock(BlockDescriptor descriptor, params string[] fixtures)
    {
        var f = new ConformanceFixtures();
        foreach (string json in fixtures)
        {
            f.BlockParameters("echo", json);
        }

        return CatalogueConformance.Check(new CatalogueBuilder().AddBlock(descriptor).Build(), f);
    }

    [Fact]
    public void AnHonestBlockDescriptorHasNoMismatches()
    {
        ConformanceReport report = CheckBlock(EchoDescriptor(HonestTags), """{ "input": "X.In" }""");

        Assert.Empty(report.Mismatches);
        Assert.Contains(typeof(EchoBlock), report.BuiltTypes);
    }

    [Fact]
    public void ReportsAnOwnedTagTheInstanceLacks()
    {
        ConformanceReport report = CheckBlock(EchoDescriptor(HonestTags), """{ "input": "X.In", "extra": true }""");

        Assert.Equal(
            "echo: owned tag 'probe.Extra' (Bool ReadOnly) is in the descriptor but not on the instance.",
            Assert.Single(report.Mismatches));
    }

    [Fact]
    public void ReportsAnOwnedTagTheDescriptorForgot()
    {
        ConformanceReport report = CheckBlock(
            EchoDescriptor((id, p) => [(new TagSpec($"{id}.Q", TagKind.Bool), TagAccess.ReadOnly)]),
            """{ "input": "X.In" }""");

        Assert.Equal(
            "echo: owned tag 'probe.Cmd' (Bool ReadWrite) is on the instance but not in the descriptor.",
            Assert.Single(report.Mismatches));
    }

    [Fact]
    public void ReportsAKindOrAccessMismatch()
    {
        ConformanceReport report = CheckBlock(
            EchoDescriptor((id, p) =>
            [
                (new TagSpec($"{id}.Q", TagKind.Double), TagAccess.ReadOnly),
                (new TagSpec($"{id}.Cmd", TagKind.Bool), TagAccess.ReadOnly),
            ]),
            """{ "input": "X.In" }""");

        Assert.Equal(
            new[]
            {
                "echo: owned tag 'probe.Cmd' (Bool ReadOnly) is in the descriptor but not on the instance.",
                "echo: owned tag 'probe.Q' (Double ReadOnly) is in the descriptor but not on the instance.",
                "echo: owned tag 'probe.Cmd' (Bool ReadWrite) is on the instance but not in the descriptor.",
                "echo: owned tag 'probe.Q' (Bool ReadOnly) is on the instance but not in the descriptor.",
            },
            report.Mismatches);
    }

    [Fact]
    public void ReportsAUnitOrDescriptionMismatch()
    {
        ConformanceReport report = CheckBlock(
            EchoDescriptor((id, p) =>
            [
                (new TagSpec($"{id}.Q", TagKind.Bool, "s"), TagAccess.ReadOnly),
                (new TagSpec($"{id}.Cmd", TagKind.Bool, "", "A command"), TagAccess.ReadWrite),
            ]),
            """{ "input": "X.In" }""");

        Assert.Equal(
            new[]
            {
                "echo: owned tag 'probe.Q' has unit 's' in the descriptor but '' on the instance.",
                "echo: owned tag 'probe.Cmd' is described 'A command' in the descriptor but '' on the instance.",
            },
            report.Mismatches);
    }

    [Fact]
    public void ReportsAParameterTheFactoryReadsButTheDescriptorDoesNotDeclare()
    {
        var descriptor = new BlockDescriptor(
            "echo",
            "Echoes a tag.",
            HonestTags,
            (id, period, p) => new EchoBlock(id, TimeSpan.FromSeconds(p.Double("gain"))).Publishes("Q").Accepts("Cmd"))
        {
            Parameters = [Param.Bool("extra", "Declare one more tag than the block has.", @default: false)],
        };

        ConformanceReport report = CheckBlock(descriptor, "{}");

        string mismatch = Assert.Single(report.Mismatches);
        Assert.StartsWith("echo: the factory threw KeyNotFoundException:", mismatch, StringComparison.Ordinal);
        Assert.Contains("'gain', which its descriptor does not declare", mismatch, StringComparison.Ordinal);
    }

    [Fact]
    public void ChecksEveryFixtureOfABlockTypeAndLabelsEach()
    {
        ConformanceReport report = CheckBlock(
            EchoDescriptor(HonestTags),
            """{ "input": "X.In" }""",
            """{ "input": "X.In", "extra": true }""");

        Assert.Equal(
            "echo (fixture 2): owned tag 'probe.Extra' (Bool ReadOnly) is in the descriptor but not on the instance.",
            Assert.Single(report.Mismatches));
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet build tests/Millrace.Core.Tests --nologo`
Expected: FAIL — `error CS1061: 'ConformanceFixtures' does not contain a definition for 'BlockParameters'`.

- [ ] **Step 3: Let fixtures carry block parameters**

In `src/Millrace.Core/Testing/ConformanceFixtures.cs`, add a field after `_objects`:

```csharp
    private readonly Dictionary<string, List<string>> _blocks = new(StringComparer.Ordinal);
```

add after `ObjectParameters`:

```csharp
    /// <summary>
    /// A <c>parameters</c> object for a probe of a block type. Call it again for
    /// the same type to check it with several — an alarm with one limit and with four.
    /// </summary>
    public ConformanceFixtures BlockParameters(string blockType, string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(blockType);
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        if (!_blocks.TryGetValue(blockType, out List<string>? list))
        {
            list = [];
            _blocks[blockType] = list;
        }

        list.Add(json);
        return this;
    }
```

and after the second `ParametersFor`:

```csharp
    internal IReadOnlyList<string> BlockParametersFor(string blockType) =>
        _blocks.TryGetValue(blockType, out List<string>? list) ? list : ["{}"];
```

- [ ] **Step 4: Check blocks**

In `src/Millrace.Core/Testing/CatalogueConformance.cs`, add `using System.Globalization;`,
and after the `ProbeId` constant:

```csharp
    /// <summary>The scan period every block probe is built with.</summary>
    public static TimeSpan ProbePeriod { get; } = TimeSpan.FromMilliseconds(100);
```

In `Check`, after the `foreach (ComponentDescriptor descriptor in catalogue.Components)`
loop and before `return`, add:

```csharp
        foreach (BlockDescriptor descriptor in catalogue.Blocks)
        {
            IReadOnlyList<string> jsons = fixtures.BlockParametersFor(descriptor.Type);
            for (int i = 0; i < jsons.Count; i++)
            {
                string label = jsons.Count == 1
                    ? descriptor.Type
                    : string.Create(CultureInfo.InvariantCulture, $"{descriptor.Type} (fixture {i + 1})");
                ParameterValues? values = BindFixture(descriptor.Parameters, jsons[i], fixtures.NewContext(catalogue), label, mismatches);
                if (values is null || !TryBuild(() => descriptor.Factory(ProbeId, ProbePeriod, values), label, mismatches, out object? made))
                {
                    continue;
                }

                var block = (IScanBlock)made;
                built.Add(block.GetType());
                CompareBlock(descriptor, label, values, block, mismatches);
            }
        }
```

Add after `Compare`:

```csharp
    private static void CompareBlock(
        BlockDescriptor descriptor, string label, ParameterValues values, IScanBlock block, List<string> mismatches)
    {
        if (!string.Equals(block.Id, ProbeId, StringComparison.Ordinal))
        {
            mismatches.Add($"{label}: the instance's id is '{block.Id}', not the id its factory was given ('{ProbeId}').");
        }

        if (block.ScanPeriod != ProbePeriod)
        {
            mismatches.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"{label}: the instance scans every {block.ScanPeriod.TotalMilliseconds} ms, not at the period its factory was given ({ProbePeriod.TotalMilliseconds} ms)."));
        }

        IReadOnlyList<(TagSpec Spec, TagAccess Access)> declared;
        try
        {
            declared = descriptor.OwnedTags(ProbeId, values);
        }
#pragma warning disable CA1031 // Reported, not rethrown: a broken OwnedTags is a finding like any other.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            mismatches.Add($"{label}: OwnedTags threw {ex.GetType().Name}: {ex.Message}");
            return;
        }

        var actual = block.Outputs.Select(s => (Spec: s, Access: TagAccess.ReadOnly))
            .Concat(block.Commands.Select(s => (Spec: s, Access: TagAccess.ReadWrite)))
            .Select(t => (Name: $"{block.Id}.{t.Spec.Name}", t.Spec.Kind, t.Access, t.Spec.Unit, t.Spec.Description))
            .ToList();

        Diff(
            label, "owned tag",
            declared.Select(t => $"'{t.Spec.Name}' ({t.Spec.Kind} {t.Access})"),
            actual.Select(t => $"'{t.Name}' ({t.Kind} {t.Access})"),
            mismatches);

        // R102: a tag that matches by name, kind and access must also match by unit and description.
        foreach ((TagSpec spec, TagAccess access) in declared)
        {
            foreach (var match in actual.Where(a =>
                         string.Equals(a.Name, spec.Name, StringComparison.Ordinal) && a.Kind == spec.Kind && a.Access == access))
            {
                if (!string.Equals(match.Unit, spec.Unit, StringComparison.Ordinal))
                {
                    mismatches.Add($"{label}: owned tag '{spec.Name}' has unit '{spec.Unit}' in the descriptor but '{match.Unit}' on the instance.");
                }

                if (!string.Equals(match.Description, spec.Description, StringComparison.Ordinal))
                {
                    mismatches.Add($"{label}: owned tag '{spec.Name}' is described '{spec.Description}' in the descriptor but '{match.Description}' on the instance.");
                }
            }
        }
    }
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/Millrace.Core.Tests --nologo --filter FullyQualifiedName~CatalogueConformanceTests`
Expected: PASS, including the seven new facts.

- [ ] **Step 6: Run everything**

Run: `dotnet build Millrace.sln -c Release --nologo` — expect `0 Warning(s)`, `0 Error(s)`.
Run: `dotnet test Millrace.sln --nologo`
Expected: PASS, **1147** tests (Core 459).

- [ ] **Step 7: Commit**

```bash
git add src/Millrace.Core/Testing/CatalogueConformance.cs src/Millrace.Core/Testing/ConformanceFixtures.cs tests/Millrace.Core.Tests/Catalogue/CatalogueConformanceTests.cs
```

```bash
git commit -m "$(cat <<'MSG'
feat(testing): check block descriptors against the blocks they build

Conformance builds a probe of every block type from each of its
fixtures and reports every owned tag whose name, kind or access differs
between the descriptor and the instance.

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
MSG
)"
```

---

### Task 6: `Millrace.Control.Catalogue` — the project, `ControlModule`, the shared groups and the `transition` slot

Model: sonnet

Spec 2 and 3.3. A new project that references `Millrace.Core` and `Millrace.Control`, so
`Millrace.Control` itself keeps seeing `Millrace.Io.Abstractions` alone. This task
registers the two transition objects; Tasks 7 and 8 add the five blocks. A new
test project runs conformance over the module.

**Files:**
- Create: `src/Millrace.Control.Catalogue/Millrace.Control.Catalogue.csproj`
- Create: `src/Millrace.Control.Catalogue/ControlModule.cs`
- Create: `src/Millrace.Control.Catalogue/ControlCatalogue.cs`
- Create: `src/Millrace.Control.Catalogue/TransitionCatalogue.cs`
- Create: `tests/Millrace.Control.Catalogue.Tests/Millrace.Control.Catalogue.Tests.csproj`
- Create: `tests/Millrace.Control.Catalogue.Tests/ControlFixtures.cs`
- Create: `tests/Millrace.Control.Catalogue.Tests/Bind.cs`
- Create: `tests/Millrace.Control.Catalogue.Tests/ControlCatalogueTests.cs`
- Create: `tests/Millrace.Control.Catalogue.Tests/TransitionTests.cs`
- Modify: `Millrace.sln`

**Interfaces:**
- Consumes: `ObjectDescriptor(string slot, string type, string description, Func<ParameterValues, object> factory)`,
  `GroupDefinition(string name, params ParameterDescriptor[])`, `Param.Tag`, `Param.Value`,
  `Param.Double`, `Param.Bool`, `Param.Enum`, `ParameterValues.Groups/Tag/Value/Bool`;
  `StepTransition.When(string tag, PredicateOperator op, TagValue value)`,
  `StepTransition.After(TimeSpan delay)`, `Condition(string Tag, bool Normal)`,
  `BlockWrite(string Tag, TagValue Value)`; `CatalogueConformance.Check`,
  `ConformanceFixtures.ObjectParameters`, `BindingContext.UseTags`.
- Produces (namespace `Millrace.Control.Catalogue`):
  - `public sealed class ControlModule : ICatalogueModule` — `Name` = `"Millrace.Control"` (R94).
  - `public static class ControlCatalogue` — `TransitionSlot` = `"transition"`, `MaxSeconds` =
    `31_536_000.0`, `GroupDefinition ConditionGroup` (`"Condition"`: `tag` Tag Bool, `normal` Bool),
    `GroupDefinition WriteGroup` (`"BlockWrite"`: `tag` Tag writes, `value` Value of `tag`);
    internal helpers `Seconds(name, description, double? @default = null)`,
    `Conditions(ParameterValues p, string name)`, `Writes(ParameterValues p, string name)`,
    `Output(id, pin, kind, unit, description)`, `Command(id, pin, description)`.
  - `public static class TransitionCatalogue` — `ObjectDescriptor When`, `ObjectDescriptor After`.
  - Tests: `ControlFixtures.Catalogue`, `ControlFixtures.Create()`, `Bind.Period`,
    `Bind.Values(schema, json)`, `Bind.TryValues(schema, json)` with the tag table
    `V1.Level` (Double RO), `V1.Fill` (Bool RW), `V1.Tripped` (Bool RO), `V1.Running` (Bool RO).

- [ ] **Step 1: Create the source project**

Create `src/Millrace.Control.Catalogue/Millrace.Control.Catalogue.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\Millrace.Core\Millrace.Core.csproj" />
    <ProjectReference Include="..\Millrace.Control\Millrace.Control.csproj" />
  </ItemGroup>

  <ItemGroup>
    <InternalsVisibleTo Include="Millrace.Control.Catalogue.Tests" />
  </ItemGroup>

</Project>
```

Create `src/Millrace.Control.Catalogue/ControlCatalogue.cs`:

```csharp
using Millrace.Core.Catalogue;
using Millrace.Io;

namespace Millrace.Control.Catalogue;

/// <summary>What the five block descriptors share: the condition and write groups, the transition slot, the duration bound.</summary>
public static class ControlCatalogue
{
    /// <summary>The object slot a sequencer step's transition comes from.</summary>
    public const string TransitionSlot = "transition";

    /// <summary>
    /// The longest duration a block parameter accepts: a year, in seconds.
    /// <c>TimeSpan.FromSeconds</c> overflows near 9.2e11 s, so without a bound an
    /// absurd duration would surface as an overflow rather than a range error (R86).
    /// </summary>
    public const double MaxSeconds = 31_536_000.0;

    /// <summary>A Bool tag with the value that means "normal" — a permissive's or an interlock's condition.</summary>
    public static GroupDefinition ConditionGroup { get; } = new(
        "Condition",
        Param.Tag("tag", "The Bool tag watched.", TagKind.Bool),
        Param.Bool("normal", "The value that means the condition is satisfied."));

    /// <summary>A tag a block commands and the value it commands — a trip, entry or abort write.</summary>
    public static GroupDefinition WriteGroup { get; } = new(
        "BlockWrite",
        Param.Tag("tag", "The tag commanded.", writes: true),
        Param.Value("value", "The value commanded.", "tag"));

    /// <summary>A duration in seconds: zero or more, at most <see cref="MaxSeconds"/>.</summary>
    internal static ParameterDescriptor Seconds(string name, string description, double? @default = null) =>
        Param.Double(name, description, "s", @default, min: 0.0, max: MaxSeconds);

    internal static IReadOnlyList<Condition> Conditions(ParameterValues p, string name) =>
        p.Groups(name).Select(g => new Condition(g.Tag("tag"), g.Bool("normal"))).ToList();

    internal static IReadOnlyList<BlockWrite> Writes(ParameterValues p, string name) =>
        p.Groups(name).Select(g => new BlockWrite(g.Tag("tag"), g.Value("value"))).ToList();

    internal static (TagSpec Spec, TagAccess Access) Output(string id, string pin, TagKind kind, string unit, string description) =>
        (new TagSpec($"{id}.{pin}", kind, unit, description), TagAccess.ReadOnly);

    internal static (TagSpec Spec, TagAccess Access) Command(string id, string pin, string description) =>
        (new TagSpec($"{id}.{pin}", TagKind.Bool, string.Empty, description), TagAccess.ReadWrite);
}
```

Create `src/Millrace.Control.Catalogue/TransitionCatalogue.cs`:

```csharp
using Millrace.Core.Catalogue;

namespace Millrace.Control.Catalogue;

/// <summary>The two things that end a sequencer step, as objects in the <c>transition</c> slot.</summary>
public static class TransitionCatalogue
{
    private static readonly string[] Operators = ["==", "!=", "<", "<=", ">", ">="];

    /// <summary><c>{ "type": "when", "tag": …, "op": …, "value": … }</c>.</summary>
    public static ObjectDescriptor When { get; } = new(
        ControlCatalogue.TransitionSlot,
        "when",
        "Ends the step when a tag compares with a value as asked.",
        p => StepTransition.When(p.Tag("tag"), Operator(p.String("op")), p.Value("value")))
    {
        Parameters =
        [
            Param.Tag("tag", "The tag compared."),
            Param.Enum("op", "How the tag is compared. A Bool tag allows only == and !=.", Operators),
            Param.Value("value", "What the tag is compared with.", "tag"),
        ],
    };

    /// <summary><c>{ "type": "after", "delayS": … }</c>.</summary>
    public static ObjectDescriptor After { get; } = new(
        ControlCatalogue.TransitionSlot,
        "after",
        "Ends the step when its clock reaches a delay.",
        p => StepTransition.After(TimeSpan.FromSeconds(p.Double("delayS"))))
    {
        Parameters = [ControlCatalogue.Seconds("delayS", "How long the step runs.")],
    };

    private static PredicateOperator Operator(string op) => op switch
    {
        "==" => PredicateOperator.Equal,
        "!=" => PredicateOperator.NotEqual,
        "<" => PredicateOperator.Less,
        "<=" => PredicateOperator.LessOrEqual,
        ">" => PredicateOperator.Greater,
        _ => PredicateOperator.GreaterOrEqual,
    };
}
```

Create `src/Millrace.Control.Catalogue/ControlModule.cs`:

```csharp
using Millrace.Core.Catalogue;

namespace Millrace.Control.Catalogue;

/// <summary>
/// The five control blocks of <c>Millrace.Control</c> and the sequencer's
/// <c>transition</c> slot, for a plant file's <c>controllers</c> section.
/// </summary>
public sealed class ControlModule : ICatalogueModule
{
    public string Name => "Millrace.Control";

    public void Register(CatalogueBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Transitions
        builder.Add(TransitionCatalogue.After);
        builder.Add(TransitionCatalogue.When);
    }
}
```

- [ ] **Step 2: Create the test project**

Create `tests/Millrace.Control.Catalogue.Tests/Millrace.Control.Catalogue.Tests.csproj`:

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
    <ProjectReference Include="..\..\src\Millrace.Control.Catalogue\Millrace.Control.Catalogue.csproj" />
  </ItemGroup>

</Project>
```

Create `tests/Millrace.Control.Catalogue.Tests/ControlFixtures.cs`:

```csharp
using Millrace.Core.Catalogue;
using Millrace.Core.Testing;

namespace Millrace.Control.Catalogue.Tests;

/// <summary>The module under test and what conformance needs to build a probe of each type.</summary>
internal static class ControlFixtures
{
    public static ComponentCatalogue Catalogue { get; } = new CatalogueBuilder().Add<ControlModule>().Build();

    public static ConformanceFixtures Create() => new ConformanceFixtures()
        .ObjectParameters(ControlCatalogue.TransitionSlot, "when", """{ "tag": "V1.Level", "op": ">=", "value": 80 }""")
        .ObjectParameters(ControlCatalogue.TransitionSlot, "after", """{ "delayS": 5 }""");
}
```

Create `tests/Millrace.Control.Catalogue.Tests/Bind.cs`:

```csharp
using System.Text.Json;
using Millrace.Core.Catalogue;
using Millrace.Io;

namespace Millrace.Control.Catalogue.Tests;

/// <summary>Binds parameters the way the loader's controller pass does: construct mode, against a tag table.</summary>
internal static class Bind
{
    public static readonly TimeSpan Period = TimeSpan.FromMilliseconds(100);

    private static readonly (string Name, TagKind Kind, TagAccess Access)[] Tags =
    [
        ("V1.Level", TagKind.Double, TagAccess.ReadOnly),
        ("V1.Fill", TagKind.Bool, TagAccess.ReadWrite),
        ("V1.Tripped", TagKind.Bool, TagAccess.ReadOnly),
        ("V1.Running", TagKind.Bool, TagAccess.ReadOnly),
    ];

    public static ParameterValues Values(IReadOnlyList<ParameterDescriptor> schema, string json)
    {
        (ParameterValues? values, List<BindingIssue> issues) = TryValues(schema, json);
        Assert.True(values is not null, string.Join("\n", issues.Select(i => $"{i.Path}: {i.Message}")));
        return values!;
    }

    public static (ParameterValues? Values, List<BindingIssue> Issues) TryValues(IReadOnlyList<ParameterDescriptor> schema, string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        var issues = new List<BindingIssue>();
        BindingContext context = new BindingContext(ControlFixtures.Catalogue).UseTags(Tags);
        ParameterValues? values = ParameterBinder.Bind(schema, document.RootElement, "$", context, construct: true, issues);
        return (values, issues);
    }
}
```

- [ ] **Step 3: Write the tests**

Create `tests/Millrace.Control.Catalogue.Tests/ControlCatalogueTests.cs`:

```csharp
using Millrace.Core.Testing;

namespace Millrace.Control.Catalogue.Tests;

public class ControlCatalogueTests
{
    private static readonly ConformanceReport Report =
        CatalogueConformance.Check(ControlFixtures.Catalogue, ControlFixtures.Create());

    [Fact]
    public void EveryDescriptorMatchesWhatItBuilds()
    {
        Assert.Empty(Report.Mismatches);
    }
}
```

Create `tests/Millrace.Control.Catalogue.Tests/TransitionTests.cs`:

```csharp
using Millrace.Core.Catalogue;
using Millrace.Io;

namespace Millrace.Control.Catalogue.Tests;

public class TransitionTests
{
    [Theory]
    [InlineData("==", PredicateOperator.Equal)]
    [InlineData("!=", PredicateOperator.NotEqual)]
    [InlineData("<", PredicateOperator.Less)]
    [InlineData("<=", PredicateOperator.LessOrEqual)]
    [InlineData(">", PredicateOperator.Greater)]
    [InlineData(">=", PredicateOperator.GreaterOrEqual)]
    public void AWhenTransitionMapsEveryOperator(string op, PredicateOperator expected)
    {
        ParameterValues values = Bind.Values(
            TransitionCatalogue.When.Parameters, $$"""{ "tag": "V1.Level", "op": "{{op}}", "value": 80 }""");

        var transition = (StepTransition)TransitionCatalogue.When.Factory(values);

        Assert.False(transition.IsTimed);
        Assert.Equal("V1.Level", transition.Tag);
        Assert.Equal(expected, transition.Operator);
        Assert.Equal(TagValue.Double(80.0), transition.Value);
    }

    [Fact]
    public void AnAfterTransitionRunsTheStepClock()
    {
        var transition = (StepTransition)TransitionCatalogue.After.Factory(
            Bind.Values(TransitionCatalogue.After.Parameters, """{ "delayS": 2.5 }"""));

        Assert.True(transition.IsTimed);
        Assert.Equal(TimeSpan.FromSeconds(2.5), transition.Delay);
    }

    [Fact]
    public void ABoolTagCannotBeOrdered()
    {
        ParameterValues values = Bind.Values(
            TransitionCatalogue.When.Parameters, """{ "tag": "V1.Fill", "op": "<", "value": true }""");

        var ex = Assert.Throws<ArgumentException>(() => TransitionCatalogue.When.Factory(values));

        Assert.Contains("A Bool tag cannot be compared with Less", ex.Message, StringComparison.Ordinal);
    }
}
```

- [ ] **Step 4: Add both projects to the solution**

```bash
dotnet sln Millrace.sln add src/Millrace.Control.Catalogue/Millrace.Control.Catalogue.csproj --solution-folder src
```

```bash
dotnet sln Millrace.sln add tests/Millrace.Control.Catalogue.Tests/Millrace.Control.Catalogue.Tests.csproj --solution-folder tests
```

Run: `dotnet sln Millrace.sln list` — both new projects are listed.

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/Millrace.Control.Catalogue.Tests --nologo`
Expected: PASS, **9** tests (1 conformance, 6 operator rows, 2 facts).

Run: `grep -n "ProjectReference" src/Millrace.Control/Millrace.Control.csproj`
Expected: exactly one line, naming `Millrace.Io.Abstractions.csproj`.

- [ ] **Step 6: Run everything**

Run: `dotnet build Millrace.sln -c Release --nologo` — expect `0 Warning(s)`, `0 Error(s)`.
Run: `dotnet test Millrace.sln --nologo`
Expected: PASS, **1156** tests (Control.Catalogue 9).

- [ ] **Step 7: Commit**

```bash
git add src/Millrace.Control.Catalogue/Millrace.Control.Catalogue.csproj src/Millrace.Control.Catalogue/ControlModule.cs src/Millrace.Control.Catalogue/ControlCatalogue.cs src/Millrace.Control.Catalogue/TransitionCatalogue.cs tests/Millrace.Control.Catalogue.Tests/Millrace.Control.Catalogue.Tests.csproj tests/Millrace.Control.Catalogue.Tests/ControlFixtures.cs tests/Millrace.Control.Catalogue.Tests/Bind.cs tests/Millrace.Control.Catalogue.Tests/ControlCatalogueTests.cs tests/Millrace.Control.Catalogue.Tests/TransitionTests.cs Millrace.sln
```

```bash
git commit -m "$(cat <<'MSG'
feat(control): add the control catalogue module and the transition slot

Millrace.Control.Catalogue sees Core and Millrace.Control, so the blocks keep
seeing the I/O contract alone. It carries the shared condition and write
groups and the sequencer's when/after transitions.

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
MSG
)"
```

---

### Task 7: The timer, permissive and interlock descriptors

Model: sonnet

Spec 3.4, the three blocks with fixed owned tags and no nested objects. Each
descriptor's factory calls the 5c constructor; its `OwnedTags` repeats the 5c
block's own names, kinds, units and descriptions, and conformance proves it.

**Files:**
- Create: `src/Millrace.Control.Catalogue/TimerCatalogue.cs`
- Create: `src/Millrace.Control.Catalogue/PermissiveCatalogue.cs`
- Create: `src/Millrace.Control.Catalogue/InterlockCatalogue.cs`
- Modify: `src/Millrace.Control.Catalogue/ControlModule.cs`
- Modify: `tests/Millrace.Control.Catalogue.Tests/ControlFixtures.cs`
- Create: `tests/Millrace.Control.Catalogue.Tests/BlockFactoryTests.cs`

**Interfaces:**
- Consumes: `Timer(string id, TimerMode mode, string input, TimeSpan preset, TimeSpan scanPeriod)`
  with `Mode`, `Preset`, `ScanPeriod`, `Inputs`; `Permissive(string id, IReadOnlyList<Condition>, TimeSpan)`;
  `Interlock(string id, IReadOnlyList<Condition>, IReadOnlyList<BlockWrite>, TimeSpan)`;
  `ControlCatalogue.ConditionGroup`, `WriteGroup`, `Seconds`, `Conditions`, `Writes`, `Output`, `Command` (Task 6);
  `Bind.Values`, `Bind.Period` (Task 6).
- Produces: `TimerCatalogue.Descriptor` (`"timer"`), `PermissiveCatalogue.Descriptor`
  (`"permissive"`), `InterlockCatalogue.Descriptor` (`"interlock"`), registered by `ControlModule`.

- [ ] **Step 1: Write the failing tests**

Create `tests/Millrace.Control.Catalogue.Tests/BlockFactoryTests.cs`:

```csharp
using Millrace.Core.Catalogue;
using Millrace.Io;

namespace Millrace.Control.Catalogue.Tests;

public class BlockFactoryTests
{
    [Theory]
    [InlineData("on-delay", TimerMode.OnDelay)]
    [InlineData("off-delay", TimerMode.OffDelay)]
    [InlineData("pulse", TimerMode.Pulse)]
    public void ATimerMapsEveryMode(string mode, TimerMode expected)
    {
        ParameterValues values = Bind.Values(
            TimerCatalogue.Descriptor.Parameters, $$"""{ "mode": "{{mode}}", "input": "V1.Running", "presetS": 1.5 }""");

        var timer = (Timer)TimerCatalogue.Descriptor.Factory("TMR01", Bind.Period, values);

        Assert.Equal(expected, timer.Mode);
        Assert.Equal(TimeSpan.FromSeconds(1.5), timer.Preset);
        Assert.Equal(Bind.Period, timer.ScanPeriod);
        Assert.Equal(new TagRef("V1.Running", TagKind.Bool), Assert.Single(timer.Inputs));
    }

    [Fact]
    public void APermissiveReadsItsConditionsInOrder()
    {
        ParameterValues values = Bind.Values(
            PermissiveCatalogue.Descriptor.Parameters,
            """{ "conditions": [ { "tag": "V1.Tripped", "normal": false }, { "tag": "V1.Running", "normal": true } ] }""");

        IScanBlock block = PermissiveCatalogue.Descriptor.Factory("PERM01", Bind.Period, values);

        Assert.Equal(["V1.Tripped", "V1.Running"], block.Inputs.Select(i => i.Name));
        Assert.Empty(block.Writes);
    }

    [Fact]
    public void AnInterlockCommandsItsTripWrites()
    {
        ParameterValues values = Bind.Values(
            InterlockCatalogue.Descriptor.Parameters,
            """{ "conditions": [ { "tag": "V1.Tripped", "normal": false } ], "trip": [ { "tag": "V1.Fill", "value": false } ] }""");

        IScanBlock block = InterlockCatalogue.Descriptor.Factory("INT01", Bind.Period, values);

        Assert.Equal(new TagRef("V1.Tripped", TagKind.Bool), Assert.Single(block.Inputs));
        Assert.Equal(new TagRef("V1.Fill", TagKind.Bool), Assert.Single(block.Writes));
    }

    [Fact]
    public void AnInterlocksTripWritesDefaultToNone()
    {
        ParameterValues values = Bind.Values(
            InterlockCatalogue.Descriptor.Parameters, """{ "conditions": [ { "tag": "V1.Tripped", "normal": false } ] }""");

        IScanBlock block = InterlockCatalogue.Descriptor.Factory("INT01", Bind.Period, values);

        Assert.Empty(block.Writes);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet build tests/Millrace.Control.Catalogue.Tests --nologo`
Expected: FAIL — `error CS0103: The name 'TimerCatalogue' does not exist in the current context`.

- [ ] **Step 3: Write the three descriptors**

Create `src/Millrace.Control.Catalogue/TimerCatalogue.cs`:

```csharp
using Millrace.Core.Catalogue;
using Millrace.Io;

namespace Millrace.Control.Catalogue;

/// <summary>The <see cref="Timer"/> in a plant file: <c>{ "type": "timer", … }</c>.</summary>
public static class TimerCatalogue
{
    private static readonly string[] Modes = ["on-delay", "off-delay", "pulse"];

    public static BlockDescriptor Descriptor { get; } = new(
        "timer",
        "An IEC 61131-3 timer over one Bool tag: on-delay (TON), off-delay (TOF) or pulse (TP).",
        (id, p) =>
        [
            ControlCatalogue.Output(id, "Q", TagKind.Bool, string.Empty, "Timer output"),
            ControlCatalogue.Output(id, "ET", TagKind.Double, "s", "Elapsed time, quantised to the scan period"),
        ],
        (id, period, p) => new Timer(id, Mode(p.String("mode")), p.Tag("input"), TimeSpan.FromSeconds(p.Double("presetS")), period))
    {
        Parameters =
        [
            Param.Enum(
                "mode",
                "on-delay raises Q once the input has held true for the preset; off-delay holds Q for the preset after the input falls; pulse gives one preset-long pulse on a rising edge.",
                Modes),
            Param.Tag("input", "The Bool tag timed.", TagKind.Bool),
            ControlCatalogue.Seconds("presetS", "The delay or pulse length. Zero acts immediately."),
        ],
    };

    private static TimerMode Mode(string mode) => mode switch
    {
        "on-delay" => TimerMode.OnDelay,
        "off-delay" => TimerMode.OffDelay,
        _ => TimerMode.Pulse,
    };
}
```

Create `src/Millrace.Control.Catalogue/PermissiveCatalogue.cs`:

```csharp
using Millrace.Core.Catalogue;
using Millrace.Io;

namespace Millrace.Control.Catalogue;

/// <summary>The <see cref="Permissive"/> in a plant file: <c>{ "type": "permissive", … }</c>.</summary>
public static class PermissiveCatalogue
{
    public static BlockDescriptor Descriptor { get; } = new(
        "permissive",
        "The conditions something needs before it may start: Ok while every condition is normal, never latched.",
        (id, p) =>
        [
            ControlCatalogue.Output(id, "Ok", TagKind.Bool, string.Empty, "Every condition is normal"),
            ControlCatalogue.Output(id, "FirstOut", TagKind.Int64, string.Empty, "Index of the first condition to leave normal, or -1"),
        ],
        (id, period, p) => new Permissive(id, ControlCatalogue.Conditions(p, "conditions"), period))
    {
        Parameters =
        [
            Param.GroupList("conditions", "The Bool tags that must be normal, in report order.", ControlCatalogue.ConditionGroup, minCount: 1),
        ],
    };
}
```

Create `src/Millrace.Control.Catalogue/InterlockCatalogue.cs`:

```csharp
using Millrace.Core.Catalogue;
using Millrace.Io;

namespace Millrace.Control.Catalogue;

/// <summary>The <see cref="Interlock"/> in a plant file: <c>{ "type": "interlock", … }</c>.</summary>
public static class InterlockCatalogue
{
    public static BlockDescriptor Descriptor { get; } = new(
        "interlock",
        "The conditions that stop a running thing: any abnormal condition latches Tripped and sends the trip writes once, until Reset.",
        (id, p) =>
        [
            ControlCatalogue.Output(id, "Ok", TagKind.Bool, string.Empty, "Not tripped"),
            ControlCatalogue.Output(id, "Tripped", TagKind.Bool, string.Empty, "Latched by an abnormal condition"),
            ControlCatalogue.Output(id, "FirstOut", TagKind.Int64, string.Empty, "Index of the condition that tripped, or -1"),
            ControlCatalogue.Command(id, "Reset", "Clears the latch on a rising edge when every condition is normal"),
        ],
        (id, period, p) => new Interlock(
            id, ControlCatalogue.Conditions(p, "conditions"), ControlCatalogue.Writes(p, "trip"), period))
    {
        Parameters =
        [
            Param.GroupList("conditions", "The Bool tags whose abnormal value trips the interlock, in report order.", ControlCatalogue.ConditionGroup, minCount: 1),
            Param.GroupList("trip", "What to command when the interlock trips, once, on the trip scan.", ControlCatalogue.WriteGroup),
        ],
    };
}
```

In `src/Millrace.Control.Catalogue/ControlModule.cs`, add before `// Transitions`:

```csharp
        // Blocks
        builder.AddBlock(TimerCatalogue.Descriptor);
        builder.AddBlock(PermissiveCatalogue.Descriptor);
        builder.AddBlock(InterlockCatalogue.Descriptor);

```

In `tests/Millrace.Control.Catalogue.Tests/ControlFixtures.cs`, append to the chain in
`Create()` (after the `after` line, before the `;`):

```csharp
        .BlockParameters("timer", """{ "mode": "on-delay", "input": "V1.Running", "presetS": 2 }""")
        .BlockParameters("permissive", """{ "conditions": [ { "tag": "V1.Tripped", "normal": false } ] }""")
        .BlockParameters(
            "interlock",
            """{ "conditions": [ { "tag": "V1.Tripped", "normal": false } ], "trip": [ { "tag": "V1.Fill", "value": false } ] }""")
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/Millrace.Control.Catalogue.Tests --nologo`
Expected: PASS, **15** tests. `EveryDescriptorMatchesWhatItBuilds` now checks
three blocks; if it reports a mismatch, the descriptor is wrong — the 5c block
is the reference.

- [ ] **Step 5: Run everything**

Run: `dotnet build Millrace.sln -c Release --nologo` — expect `0 Warning(s)`, `0 Error(s)`.
Run: `dotnet test Millrace.sln --nologo`
Expected: PASS, **1162** tests (Control.Catalogue 15).

- [ ] **Step 6: Commit**

```bash
git add src/Millrace.Control.Catalogue/TimerCatalogue.cs src/Millrace.Control.Catalogue/PermissiveCatalogue.cs src/Millrace.Control.Catalogue/InterlockCatalogue.cs src/Millrace.Control.Catalogue/ControlModule.cs tests/Millrace.Control.Catalogue.Tests/ControlFixtures.cs tests/Millrace.Control.Catalogue.Tests/BlockFactoryTests.cs
```

```bash
git commit -m "$(cat <<'MSG'
feat(control): describe the timer, permissive and interlock

Each descriptor builds the 5c block and declares its owned tags;
conformance proves the declaration against the instance.

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
MSG
)"
```

---

### Task 8: The alarm and sequencer descriptors, and the reflection sweep

Model: sonnet

Spec 3.4 and 3.5. The alarm's owned tags follow its configured limits, in limit
order (the constructor sorts by kind, so file order never matters); conformance
checks it with one limit and with four. The sequencer's steps take a
`transition` object. A reflection sweep proves every public concrete
`IScanBlock` in `Millrace.Control` has a descriptor.

**Files:**
- Create: `src/Millrace.Control.Catalogue/AlarmCatalogue.cs`
- Create: `src/Millrace.Control.Catalogue/SequencerCatalogue.cs`
- Modify: `src/Millrace.Control.Catalogue/ControlModule.cs`
- Modify: `tests/Millrace.Control.Catalogue.Tests/ControlFixtures.cs`
- Test: `tests/Millrace.Control.Catalogue.Tests/BlockFactoryTests.cs`, `ControlCatalogueTests.cs`

**Interfaces:**
- Consumes: `Alarm(string id, string input, IReadOnlyList<AlarmLimit> limits, TimeSpan scanPeriod)`,
  `AlarmLimit(AlarmLimitKind Kind, double Value, double Deadband, TimeSpan OnDelay)`,
  `Sequencer(string id, IReadOnlyList<SequenceStep> steps, TimeSpan scanPeriod, IReadOnlyList<BlockWrite>? abortWrites = null)`,
  `SequenceStep(string name, IReadOnlyList<BlockWrite> entryWrites, StepTransition transition, TimeSpan? timeout = null)`;
  `ParameterValues.Object<T>`, `.Has`; Task 6's `ControlCatalogue` helpers and `TransitionSlot`.
- Produces: `AlarmCatalogue.Descriptor` (`"alarm"`), `AlarmCatalogue.LimitGroup`
  (`"AlarmLimit"`), `SequencerCatalogue.Descriptor` (`"sequencer"`),
  `SequencerCatalogue.StepGroup` (`"SequenceStep"`).

- [ ] **Step 1: Write the failing tests**

Append to `tests/Millrace.Control.Catalogue.Tests/BlockFactoryTests.cs`, inside the class:

```csharp
    [Fact]
    public void AnAlarmOwnsAPairPerConfiguredLimitInLimitOrder()
    {
        ParameterValues values = Bind.Values(
            AlarmCatalogue.Descriptor.Parameters,
            """{ "input": "V1.Level", "limits": [ { "kind": "hi-hi", "value": 95 }, { "kind": "lo", "value": 20 } ] }""");

        Assert.Equal(
            new[]
            {
                ("CUR01.Lo.Active", TagAccess.ReadOnly),
                ("CUR01.Lo.Acked", TagAccess.ReadOnly),
                ("CUR01.HiHi.Active", TagAccess.ReadOnly),
                ("CUR01.HiHi.Acked", TagAccess.ReadOnly),
                ("CUR01.Ack", TagAccess.ReadWrite),
            },
            AlarmCatalogue.Descriptor.OwnedTags("CUR01", values).Select(t => (t.Spec.Name, t.Access)));
    }

    [Fact]
    public void ASequencerReadsItsTransitionsAndCommandsItsStepAndAbortWrites()
    {
        ParameterValues values = Bind.Values(
            SequencerCatalogue.Descriptor.Parameters,
            """
            { "steps": [
                { "name": "Fill", "writes": [ { "tag": "V1.Fill", "value": true } ],
                  "transition": { "type": "when", "tag": "V1.Level", "op": ">=", "value": 80 }, "timeoutS": 30 },
                { "name": "Settle", "transition": { "type": "after", "delayS": 5 } } ],
              "abort": [ { "tag": "V1.Fill", "value": false } ] }
            """);

        IScanBlock block = SequencerCatalogue.Descriptor.Factory("SEQ01", Bind.Period, values);

        Assert.Equal(new TagRef("V1.Level", TagKind.Double), Assert.Single(block.Inputs));
        Assert.Equal(new TagRef("V1.Fill", TagKind.Bool), Assert.Single(block.Writes));
        Assert.Equal(Bind.Period, block.ScanPeriod);
    }

    [Fact]
    public void ADurationLongerThanAYearIsOutOfRange()
    {
        (ParameterValues? values, List<BindingIssue> issues) = Bind.TryValues(
            TimerCatalogue.Descriptor.Parameters, """{ "mode": "pulse", "input": "V1.Running", "presetS": 1e30 }""");

        Assert.Null(values);
        BindingIssue issue = Assert.Single(issues);
        Assert.Equal(BindingIssueKind.BadParameter, issue.Kind);
        Assert.Equal("$.presetS", issue.Path);
        Assert.Contains("[0, 31536000]", issue.Message, StringComparison.Ordinal);
    }
```

Append to `tests/Millrace.Control.Catalogue.Tests/ControlCatalogueTests.cs`, adding
`using Millrace.Core.Catalogue;` and `using Millrace.Io;` to its usings:

```csharp
    [Fact]
    public void EveryPublicScanBlockInMillraceControlHasADescriptor()
    {
        List<Type> blocks = typeof(Timer).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false, IsPublic: true } && typeof(IScanBlock).IsAssignableFrom(t))
            .ToList();
        var built = new HashSet<Type>(Report.BuiltTypes);

        Assert.Equal(5, blocks.Count);
        Assert.Empty(blocks.Where(t => !built.Contains(t)).Select(t => t.FullName));
    }

    [Fact]
    public void TheModuleRegistersFiveBlocksAndTwoTransitions()
    {
        ComponentCatalogue catalogue = ControlFixtures.Catalogue;

        Assert.Equal(["Millrace.Control"], catalogue.Modules);
        Assert.Empty(catalogue.Components);
        Assert.Equal(["alarm", "interlock", "permissive", "sequencer", "timer"], catalogue.Blocks.Select(b => b.Type));
        Assert.Equal(
            new[] { ("transition", "after"), ("transition", "when") },
            catalogue.Objects.Select(o => (o.Slot, o.Type)));
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet build tests/Millrace.Control.Catalogue.Tests --nologo`
Expected: FAIL — `error CS0103: The name 'AlarmCatalogue' does not exist in the current context`.

- [ ] **Step 3: Write the two descriptors**

Create `src/Millrace.Control.Catalogue/AlarmCatalogue.cs`:

```csharp
using Millrace.Core.Catalogue;
using Millrace.Io;

namespace Millrace.Control.Catalogue;

/// <summary>The <see cref="Alarm"/> in a plant file: <c>{ "type": "alarm", … }</c>.</summary>
public static class AlarmCatalogue
{
    private static readonly string[] Kinds = ["lo-lo", "lo", "hi", "hi-hi"];

    /// <summary>One limit: its kind, its value, its deadband and its on-delay.</summary>
    public static GroupDefinition LimitGroup { get; } = new(
        "AlarmLimit",
        Param.Enum("kind", "Which limit. Configured limits must ascend lo-lo < lo < hi < hi-hi, each at most once.", Kinds),
        Param.Double("value", "The limit, in the tag's engineering unit."),
        Param.Double("deadband", "How far back inside the limit the value must come before the alarm clears.", @default: 0.0, min: 0.0),
        ControlCatalogue.Seconds("onDelayS", "How long the value must stay across before the alarm raises.", 0.0));

    public static BlockDescriptor Descriptor { get; } = new(
        "alarm",
        "An ISA-18.2 analog alarm over one Double tag: one to four limits, each with a deadband and an on-delay, acknowledged by Ack.",
        OwnedTags,
        (id, period, p) => new Alarm(id, p.Tag("input"), p.Groups("limits").Select(Limit).ToList(), period))
    {
        Parameters =
        [
            Param.Tag("input", "The Double tag watched.", TagKind.Double),
            Param.GroupList("limits", "One to four limits, each kind at most once.", LimitGroup, minCount: 1),
        ],
    };

    /// <summary>A pair per configured limit, in limit order — the order the constructor sorts them into — then Ack.</summary>
    private static IReadOnlyList<(TagSpec Spec, TagAccess Access)> OwnedTags(string id, ParameterValues p)
    {
        var tags = new List<(TagSpec Spec, TagAccess Access)>();
        foreach (AlarmLimitKind kind in p.Groups("limits").Select(g => Kind(g.String("kind"))).Order())
        {
            string name = kind.ToString();
            tags.Add(ControlCatalogue.Output(id, $"{name}.Active", TagKind.Bool, string.Empty, $"The {name} limit is in alarm"));
            tags.Add(ControlCatalogue.Output(id, $"{name}.Acked", TagKind.Bool, string.Empty, $"Nothing is outstanding on the {name} limit"));
        }

        tags.Add(ControlCatalogue.Command(id, "Ack", "Acknowledges every outstanding limit on a rising edge"));
        return tags;
    }

    private static AlarmLimit Limit(ParameterValues g) =>
        new(Kind(g.String("kind")), g.Double("value"), g.Double("deadband"), TimeSpan.FromSeconds(g.Double("onDelayS")));

    private static AlarmLimitKind Kind(string kind) => kind switch
    {
        "lo-lo" => AlarmLimitKind.LoLo,
        "lo" => AlarmLimitKind.Lo,
        "hi" => AlarmLimitKind.Hi,
        _ => AlarmLimitKind.HiHi,
    };
}
```

Create `src/Millrace.Control.Catalogue/SequencerCatalogue.cs`:

```csharp
using Millrace.Core.Catalogue;
using Millrace.Io;

namespace Millrace.Control.Catalogue;

/// <summary>The <see cref="Sequencer"/> in a plant file: <c>{ "type": "sequencer", … }</c>.</summary>
public static class SequencerCatalogue
{
    /// <summary>One step: its name, its entry writes, its transition and its optional timeout.</summary>
    public static GroupDefinition StepGroup { get; } = new(
        "SequenceStep",
        Param.String("name", "A short phrase for the step's event message, without a full stop."),
        Param.GroupList("writes", "What to command on the scan that enters the step.", ControlCatalogue.WriteGroup),
        Param.Object("transition", "What ends the step: when a tag compares as asked, or after a delay.", ControlCatalogue.TransitionSlot),
        Param.Double(
            "timeoutS",
            "How long the step may run before the sequence faults and sends the abort writes. Omit for no limit.",
            "s",
            min: 0.0,
            max: ControlCatalogue.MaxSeconds,
            exclusiveMin: true,
            optional: true));

    public static BlockDescriptor Descriptor { get; } = new(
        "sequencer",
        "A linear sequence of steps, each with entry writes, a transition and an optional timeout, run by Start, Hold, Resume, Abort and Reset.",
        (id, p) =>
        [
            ControlCatalogue.Output(id, "Step", TagKind.Int64, string.Empty, "The step running, or 0 when idle"),
            ControlCatalogue.Output(id, "Running", TagKind.Bool, string.Empty, "A step is running"),
            ControlCatalogue.Output(id, "Held", TagKind.Bool, string.Empty, "The step clock is frozen"),
            ControlCatalogue.Output(id, "Complete", TagKind.Bool, string.Empty, "The last step finished"),
            ControlCatalogue.Output(id, "Faulted", TagKind.Bool, string.Empty, "A step timed out"),
            ControlCatalogue.Output(id, "StepTime", TagKind.Double, "s", "Time in the current step"),
            ControlCatalogue.Command(id, "Start", "Enters step 1 from idle on a rising edge"),
            ControlCatalogue.Command(id, "Hold", "Freezes the step clock on a rising edge"),
            ControlCatalogue.Command(id, "Resume", "Continues a held step on a rising edge"),
            ControlCatalogue.Command(id, "Abort", "Returns to idle on a rising edge"),
            ControlCatalogue.Command(id, "Reset", "Returns to idle from faulted or complete on a rising edge"),
        ],
        (id, period, p) => new Sequencer(id, p.Groups("steps").Select(Step).ToList(), period, ControlCatalogue.Writes(p, "abort")))
    {
        Parameters =
        [
            Param.GroupList("steps", "The steps, in order. There is no branching and no parallel step.", StepGroup, minCount: 1),
            Param.GroupList("abort", "What to command on an Abort or a step timeout.", ControlCatalogue.WriteGroup),
        ],
    };

    private static SequenceStep Step(ParameterValues g) => new(
        g.String("name"),
        ControlCatalogue.Writes(g, "writes"),
        g.Object<StepTransition>("transition"),
        g.Has("timeoutS") ? TimeSpan.FromSeconds(g.Double("timeoutS")) : null);
}
```

In `src/Millrace.Control.Catalogue/ControlModule.cs`, after
`builder.AddBlock(InterlockCatalogue.Descriptor);`, add:

```csharp
        builder.AddBlock(AlarmCatalogue.Descriptor);
        builder.AddBlock(SequencerCatalogue.Descriptor);
```

In `tests/Millrace.Control.Catalogue.Tests/ControlFixtures.cs`, append to the chain
in `Create()`:

```csharp
        .BlockParameters("alarm", """{ "input": "V1.Level", "limits": [ { "kind": "hi", "value": 80 } ] }""")
        .BlockParameters(
            "alarm",
            """
            { "input": "V1.Level", "limits": [
                { "kind": "hi-hi", "value": 95, "deadband": 2, "onDelayS": 1 },
                { "kind": "lo-lo", "value": 5 },
                { "kind": "hi", "value": 80 },
                { "kind": "lo", "value": 20 } ] }
            """)
        .BlockParameters(
            "sequencer",
            """
            { "steps": [
                { "name": "Fill", "writes": [ { "tag": "V1.Fill", "value": true } ],
                  "transition": { "type": "when", "tag": "V1.Level", "op": ">=", "value": 80 }, "timeoutS": 30 },
                { "name": "Settle", "transition": { "type": "after", "delayS": 5 } } ],
              "abort": [ { "tag": "V1.Fill", "value": false } ] }
            """)
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/Millrace.Control.Catalogue.Tests --nologo`
Expected: PASS, **20** tests. `EveryDescriptorMatchesWhatItBuilds` checks the
alarm twice (labelled `alarm (fixture 1)` and `alarm (fixture 2)` if anything is
wrong) and the sequencer once.

- [ ] **Step 5: Run everything**

Run: `dotnet build Millrace.sln -c Release --nologo` — expect `0 Warning(s)`, `0 Error(s)`.
Run: `dotnet test Millrace.sln --nologo`
Expected: PASS, **1167** tests (Control.Catalogue 20).

- [ ] **Step 6: Commit**

```bash
git add src/Millrace.Control.Catalogue/AlarmCatalogue.cs src/Millrace.Control.Catalogue/SequencerCatalogue.cs src/Millrace.Control.Catalogue/ControlModule.cs tests/Millrace.Control.Catalogue.Tests/ControlFixtures.cs tests/Millrace.Control.Catalogue.Tests/BlockFactoryTests.cs tests/Millrace.Control.Catalogue.Tests/ControlCatalogueTests.cs
```

```bash
git commit -m "$(cat <<'MSG'
feat(control): describe the alarm and the sequencer

The alarm's owned tags follow its configured limits in limit order and
conformance checks it with one limit and with four; a sequencer step
takes a when/after transition. A sweep proves every public block in
Millrace.Control has a descriptor.

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
MSG
)"
```

---

### Task 9: The catalogue export of blocks, and two catalogue goldens

Model: sonnet

Spec 3.5. `CatalogueJson.Export` gains a `blocks` array, sorted by type,
between `components` and `objects` (R90), with the same fixed property order.
A parameter entry gains `tagKind`, `writes` and `tagParameter` when set. The
components-only golden changes by one inserted line; a new golden records the
control catalogue.

**Files:**
- Modify: `src/Millrace.Core/Catalogue/CatalogueJson.cs`
- Test: `tests/Millrace.Core.Tests/Catalogue/CatalogueJsonTests.cs`
- Regenerate: `tests/Millrace.Components.Tests/Catalogue/Golden/components-catalogue.json`
- Modify: `tests/Millrace.Control.Catalogue.Tests/Millrace.Control.Catalogue.Tests.csproj` (link `Golden.cs`)
- Test: `tests/Millrace.Control.Catalogue.Tests/ControlCatalogueTests.cs`
- Create (generated): `tests/Millrace.Control.Catalogue.Tests/Golden/control-catalogue.json`

**Interfaces:**
- Consumes: `ComponentCatalogue.Blocks`, `ModuleOf(BlockDescriptor)` (Task 3);
  `ParameterDescriptor.RequiredKind`, `IsWriteTarget`, `TagParameter` (Task 4);
  `Millrace.Tests.Shared.Golden.Assert(string relativePath, string actual)`.
- Produces: export shape `{ formatVersion, modules, components, blocks, objects, materials }`;
  a block entry `{ type, module, description, parameters }`; parameter members
  `tagKind` (camel-cased `TagKind`: `bool`, `double`, `int64`), `writes` (`true`),
  `tagParameter`, written after `minCount`.

- [ ] **Step 1: Write the failing Core test**

In `tests/Millrace.Core.Tests/Catalogue/CatalogueJsonTests.cs`, add to `Module.Register`,
after the `ObjectDescriptor` line:

```csharp
            builder.AddBlock(new BlockDescriptor(
                "echo",
                "Echoes a tag.",
                (id, p) => [(new TagSpec($"{id}.Q", TagKind.Bool), TagAccess.ReadOnly)],
                (id, period, p) => throw new NotSupportedException("The export never builds a block."))
            {
                Parameters =
                [
                    Param.Tag("input", "The tag read.", TagKind.Bool),
                    Param.Tag("target", "The tag commanded.", writes: true),
                    Param.Value("value", "The value commanded.", "target"),
                ],
            });
```

and add this fact:

```csharp
    [Fact]
    public void WritesBlocksBetweenComponentsAndObjects()
    {
        using JsonDocument document = JsonDocument.Parse(Json);
        JsonElement root = document.RootElement;

        Assert.Equal(
            ["formatVersion", "modules", "components", "blocks", "objects", "materials"],
            root.EnumerateObject().Select(p => p.Name));

        JsonElement echo = Assert.Single(root.GetProperty("blocks").EnumerateArray());
        Assert.Equal(["type", "module", "description", "parameters"], echo.EnumerateObject().Select(p => p.Name));
        Assert.Equal("echo", echo.GetProperty("type").GetString());
        Assert.Equal("Test", echo.GetProperty("module").GetString());

        JsonElement input = echo.GetProperty("parameters")[0];
        JsonElement target = echo.GetProperty("parameters")[1];
        JsonElement value = echo.GetProperty("parameters")[2];
        Assert.Equal("tag", input.GetProperty("kind").GetString());
        Assert.Equal("bool", input.GetProperty("tagKind").GetString());
        Assert.False(input.TryGetProperty("writes", out _));
        Assert.True(target.GetProperty("writes").GetBoolean());
        Assert.False(target.TryGetProperty("tagKind", out _));
        Assert.Equal("value", value.GetProperty("kind").GetString());
        Assert.Equal("target", value.GetProperty("tagParameter").GetString());
    }
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/Millrace.Core.Tests --nologo --filter FullyQualifiedName~CatalogueJsonTests`
Expected: FAIL — `WritesBlocksBetweenComponentsAndObjects`: the root member list
has no `blocks`.

- [ ] **Step 3: Export blocks**

In `src/Millrace.Core/Catalogue/CatalogueJson.cs`, in `Export`, between the
`components` array's `writer.WriteEndArray();` and `writer.WriteStartArray("objects");`,
add:

```csharp
            writer.WriteStartArray("blocks");
            foreach (BlockDescriptor block in catalogue.Blocks)
            {
                writer.WriteStartObject();
                writer.WriteString("type", block.Type);
                writer.WriteString("module", catalogue.ModuleOf(block));
                writer.WriteString("description", block.Description);
                writer.WritePropertyName("parameters");
                WriteParameters(writer, block.Parameters);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
```

In `WriteParameters`, after the `minCount` block and before
`writer.WriteEndObject();`, add:

```csharp
            if (parameter.RequiredKind is { } tagKind)
            {
                writer.WriteString("tagKind", Camel(tagKind.ToString()));
            }

            if (parameter.IsWriteTarget)
            {
                writer.WriteBoolean("writes", true);
            }

            if (parameter.TagParameter.Length > 0)
            {
                writer.WriteString("tagParameter", parameter.TagParameter);
            }
```

- [ ] **Step 4: Run the Core tests and see the components golden move**

Run: `dotnet test tests/Millrace.Core.Tests --nologo`
Expected: PASS, **460** tests.

Run: `dotnet test tests/Millrace.Components.Tests --nologo --filter FullyQualifiedName~ComponentsExportTests`
Expected: FAIL — `TheShippedCatalogueExportsExactlyTheGoldenFile`: output differs
from `components-catalogue.json`.

- [ ] **Step 5: Regenerate the components golden and read its diff**

```bash
MILLRACE_UPDATE_GOLDEN=1 dotnet test tests/Millrace.Components.Tests --nologo --filter FullyQualifiedName~TheShippedCatalogueExportsExactlyTheGoldenFile
```

```bash
git diff --unified=1 tests/Millrace.Components.Tests/Catalogue/Golden/components-catalogue.json
```

Read the diff. Expected, and report it copied: **exactly one inserted line,
`  "blocks": [],`, directly after the `  ],` that closes `components` and before
`  "objects": [`; nothing removed** (`git diff --numstat` prints `1	0	…`).

- [ ] **Step 6: Write the control catalogue's golden test**

In `tests/Millrace.Control.Catalogue.Tests/Millrace.Control.Catalogue.Tests.csproj`, add
before the `ProjectReference` item group:

```xml
  <ItemGroup>
    <Compile Include="..\Shared\Golden.cs" Link="Shared\Golden.cs" />
  </ItemGroup>
```

In `tests/Millrace.Control.Catalogue.Tests/ControlCatalogueTests.cs`, add
`using System.Text.Json;` and `using Millrace.Tests.Shared;`, and these facts:

```csharp
    [Fact]
    public void TheControlCatalogueExportsExactlyTheGoldenFile()
    {
        Golden.Assert("Golden/control-catalogue.json", CatalogueJson.Export(ControlFixtures.Catalogue));
    }

    [Fact]
    public void TheControlCatalogueHasTheExpectedCounts()
    {
        using JsonDocument document = JsonDocument.Parse(CatalogueJson.Export(ControlFixtures.Catalogue));
        JsonElement root = document.RootElement;

        Assert.Empty(root.GetProperty("components").EnumerateArray());
        Assert.Equal(5, root.GetProperty("blocks").GetArrayLength());
        Assert.Equal(2, root.GetProperty("objects").GetArrayLength());
        Assert.Empty(root.GetProperty("materials").EnumerateArray());
    }
```

Run: `dotnet test tests/Millrace.Control.Catalogue.Tests --nologo --filter FullyQualifiedName~TheControlCatalogue`
Expected: `TheControlCatalogueHasTheExpectedCounts` PASSES;
`TheControlCatalogueExportsExactlyTheGoldenFile` FAILS with `Golden file
'…/Golden/control-catalogue.json' does not exist.`

- [ ] **Step 7: Generate the control golden and read it**

```bash
MILLRACE_UPDATE_GOLDEN=1 dotnet test tests/Millrace.Control.Catalogue.Tests --nologo --filter FullyQualifiedName~TheControlCatalogueExportsExactlyTheGoldenFile
```

**Read the whole file** `tests/Millrace.Control.Catalogue.Tests/Golden/control-catalogue.json`
and check, reporting each answer with the line copied:

| # | What the golden must show |
|---|---|
| 1 | `"formatVersion": 1`, `"modules": [ "Millrace.Control" ]`, `"components": []`, `"materials": []` |
| 2 | five blocks in the order `alarm`, `interlock`, `permissive`, `sequencer`, `timer`, each `"module": "Millrace.Control"` and exactly the members `type`, `module`, `description`, `parameters` |
| 3 | every `…S` duration (`presetS`, `onDelayS`, `timeoutS`, `delayS`) has `"unit": "s"`, `"minimum": 0` and `"maximum": 31536000`; `timeoutS` also has `"optional": true` and `"exclusiveMinimum": true` |
| 4 | the `conditions` lists carry `"group": "Condition"` with `tag` (`"kind": "tag"`, `"tagKind": "bool"`) and `normal` (`"kind": "bool"`, `"required": true`) |
| 5 | `trip`, `abort` and a step's `writes` carry `"group": "BlockWrite"` with `tag` (`"writes": true`, no `tagKind`) and `value` (`"kind": "value"`, `"tagParameter": "tag"`), and are `"required": false` |
| 6 | the alarm's `input` has `"tagKind": "double"`; the timer's `input` `"tagKind": "bool"`; `limits` has `"minCount": 1` and `"group": "AlarmLimit"` |
| 7 | the sequencer's `steps` has `"group": "SequenceStep"` and its `transition` child `"kind": "object"`, `"slot": "transition"` |
| 8 | two objects, `after` then `when`, both `"slot": "transition"`; `when`'s `op` lists `==`, `!=`, `<`, `<=`, `>`, `>=` |

Run: `dotnet test tests/Millrace.Control.Catalogue.Tests --nologo`
Expected: PASS, **22** tests.

- [ ] **Step 8: Run everything**

Run: `dotnet build Millrace.sln -c Release --nologo` — expect `0 Warning(s)`, `0 Error(s)`.
Run: `dotnet test Millrace.sln --nologo`
Expected: PASS, **1170** tests (Core 460, Components 126, Control.Catalogue 22).

- [ ] **Step 9: Commit**

```bash
git add src/Millrace.Core/Catalogue/CatalogueJson.cs tests/Millrace.Core.Tests/Catalogue/CatalogueJsonTests.cs tests/Millrace.Components.Tests/Catalogue/Golden/components-catalogue.json tests/Millrace.Control.Catalogue.Tests/Millrace.Control.Catalogue.Tests.csproj tests/Millrace.Control.Catalogue.Tests/ControlCatalogueTests.cs tests/Millrace.Control.Catalogue.Tests/Golden/control-catalogue.json
```

```bash
git commit -m "$(cat <<'MSG'
feat(catalogue): export block types beside component types

The export gains a blocks array between components and objects, and a
parameter entry says which tag kind it needs, whether the block writes
it, and which tag a value belongs to. The components golden gains one
line; the control catalogue gets its own golden.

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
MSG
)"
```

---

### Task 10: The plant schema for `controllers`

Model: sonnet

Spec 4.6. `PlantSchema.Generate` adds a root `controllers` array whose items are
a `oneOf` over `block.<type>` branches, each requiring `id`, `type` and
`scanPeriodMs`; the shared groups and the `transition` slot come out of the
existing `$defs` machinery; `Tag` is a string and `Value` a boolean or a number.
The configuration tests' catalogue gains `ControlModule`, so the schema golden
is regenerated and read.

**Files:**
- Modify: `src/Millrace.Configuration/Millrace.Configuration.csproj` (reference `Millrace.Control.Catalogue`)
- Modify: `src/Millrace.Configuration/PlantSchema.cs`
- Modify: `src/Millrace.Configuration/Loading/PlantSchemas.cs` (`TopLevelKeys`)
- Modify: `tests/Millrace.Configuration.Tests/Plants.cs` (the catalogue)
- Test: `tests/Millrace.Configuration.Tests/PlantSchemaTests.cs`
- Regenerate: `tests/Millrace.Configuration.Tests/Golden/plant.schema.json`

**Interfaces:**
- Consumes: `ComponentCatalogue.Blocks` (Task 3); `ParameterKind.Tag/Value`,
  `RequiredKind`, `IsWriteTarget`, `TagParameter` (Task 4); `ControlModule` (Tasks 6–8).
- Produces: schema root property `controllers` (last, after `tags`); `$defs`
  keys `block.<type>`; with no block types, `controllers` is
  `{ "description": …, "type": "array", "maxItems": 0 }` (R94).
  `PlantSchemas.TopLevelKeys` = `["$schema", "defaults", "materials", "components", "signals", "flows", "tags", "controllers"]`.

- [ ] **Step 1: Point the configuration tests at the control catalogue**

In `src/Millrace.Configuration/Millrace.Configuration.csproj`, add to the project
references:

```xml
    <ProjectReference Include="..\Millrace.Control.Catalogue\Millrace.Control.Catalogue.csproj" />
```

In `tests/Millrace.Configuration.Tests/Plants.cs`, add `using Millrace.Control.Catalogue;`
and replace the `Catalogue` property with:

```csharp
    public static ComponentCatalogue Catalogue { get; } =
        new CatalogueBuilder().Add<ComponentsModule>().Add<ControlModule>().Build();
```

In `src/Millrace.Configuration/Loading/PlantSchemas.cs`, replace `TopLevelKeys` with:

```csharp
    public static readonly string[] TopLevelKeys = ["$schema", "defaults", "materials", "components", "signals", "flows", "tags", "controllers"];
```

(Until Task 11 the loader accepts the key and ignores it; Task 11 reads it.)

- [ ] **Step 2: Write the failing tests**

In `tests/Millrace.Configuration.Tests/PlantSchemaTests.cs`, add
`using Millrace.Components;` and `using Millrace.Core.Catalogue;`. In
`DeclaresItsDialectAndClosesTheRoot`, replace the expected key list with:

```csharp
            ["$schema", "defaults", "materials", "components", "signals", "flows", "tags", "controllers"],
```

and add these facts:

```csharp
    [Fact]
    public void HasOneBranchPerBlockType()
    {
        using JsonDocument document = JsonDocument.Parse(Text);
        string[] branches = document.RootElement
            .GetProperty("properties").GetProperty("controllers").GetProperty("items").GetProperty("oneOf")
            .EnumerateArray().Select(b => b.GetProperty("$ref").GetString()!).ToArray();

        Assert.Equal(Plants.Catalogue.Blocks.Select(b => $"#/$defs/block.{b.Type}"), branches);
        Assert.Equal(5, branches.Length);
    }

    [Fact]
    public void ABlockBranchRequiresItsScanPeriodAndClosesItsParameters()
    {
        using JsonDocument document = JsonDocument.Parse(Text);
        JsonElement timer = Defs(document).GetProperty("block.timer");
        JsonElement properties = timer.GetProperty("properties");
        JsonElement period = properties.GetProperty("scanPeriodMs");

        Assert.Equal("timer", properties.GetProperty("type").GetProperty("const").GetString());
        Assert.Equal(["id", "type", "scanPeriodMs", "parameters"], timer.GetProperty("required").EnumerateArray().Select(r => r.GetString()));
        Assert.False(timer.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal("number", period.GetProperty("type").GetString());
        Assert.Equal(0.0, period.GetProperty("exclusiveMinimum").GetDouble());
        Assert.Equal(86_400_000.0, period.GetProperty("maximum").GetDouble());
        Assert.Equal("^[^.\\s]+$", properties.GetProperty("id").GetProperty("pattern").GetString());
        Assert.False(properties.GetProperty("parameters").GetProperty("additionalProperties").GetBoolean());
    }

    [Fact]
    public void ATagIsAStringAndAValueIsABooleanOrANumber()
    {
        using JsonDocument document = JsonDocument.Parse(Text);
        JsonElement write = Defs(document).GetProperty("group.BlockWrite").GetProperty("properties");

        Assert.Equal("string", write.GetProperty("tag").GetProperty("type").GetString());
        Assert.Contains("loader", write.GetProperty("tag").GetProperty("description").GetString(), StringComparison.Ordinal);
        Assert.Equal(["boolean", "number"], write.GetProperty("value").GetProperty("type").EnumerateArray().Select(t => t.GetString()));
    }

    [Fact]
    public void TheTransitionSlotIsAOneOfOverWhenAndAfter()
    {
        using JsonDocument document = JsonDocument.Parse(Text);
        JsonElement defs = Defs(document);
        JsonElement when = defs.GetProperty("object.transition.when");

        Assert.Equal(
            ["#/$defs/object.transition.after", "#/$defs/object.transition.when"],
            defs.GetProperty("object.transition").GetProperty("oneOf").EnumerateArray().Select(b => b.GetProperty("$ref").GetString()));
        Assert.Equal(["type", "tag", "op", "value"], when.GetProperty("required").EnumerateArray().Select(r => r.GetString()));
        Assert.Equal(
            ["==", "!=", "<", "<=", ">", ">="],
            when.GetProperty("properties").GetProperty("op").GetProperty("enum").EnumerateArray().Select(o => o.GetString()));
    }

    [Fact]
    public void ACatalogueWithoutBlocksAcceptsNoControllers()
    {
        ComponentCatalogue components = new CatalogueBuilder().Add<ComponentsModule>().Build();
        using JsonDocument document = JsonDocument.Parse(PlantSchema.Generate(components));
        JsonElement controllers = document.RootElement.GetProperty("properties").GetProperty("controllers");

        Assert.Equal(0, controllers.GetProperty("maxItems").GetInt32());
        Assert.False(controllers.TryGetProperty("items", out _));
    }
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/Millrace.Configuration.Tests --nologo --filter "FullyQualifiedName~PlantSchemaTests|FullyQualifiedName~SchemaAgreementTests"`
Expected: FAIL — **every** test in both classes, each with
`System.TypeInitializationException`. Both classes generate the schema in a
static field initializer (`PlantSchemaTests.Text`, `SchemaAgreementTests.Schema`),
and `PlantSchema.Generate(Plants.Catalogue)` now throws
`InvalidOperationException: Parameter kind Tag has no schema.` inside it, so the
type cannot initialize and even `ACatalogueWithoutBlocksAcceptsNoControllers`,
which does not use `Text`, fails the same way. The inner exception names the
`Tag` kind; report it.

- [ ] **Step 4: Generate the controllers schema**

In `src/Millrace.Configuration/PlantSchema.cs`, add `using Millrace.Io;`.

In `Generate`, after the `foreach (string slot in catalogue.Slots)` loop, add:

```csharp
        foreach (BlockDescriptor block in catalogue.Blocks)
        {
            defs[$"block.{block.Type}"] = w => WriteBlock(w, block);
            CollectGroups(block.Parameters, defs);
        }
```

and after `WriteArrayOf(writer, "tags", "envelope.tag");` add
`WriteControllers(writer, catalogue);`.

In `WriteComponent`, replace the four lines that write the `id` property with a
call:

```csharp
        WriteId(writer, "Unique in the plant. No dot and no whitespace: a dot separates a component from its port in an address.");
```

Add these methods after `WriteComponent`:

```csharp
    private static void WriteId(Utf8JsonWriter writer, string description)
    {
        writer.WriteStartObject("id");
        writer.WriteString("description", description);
        writer.WriteString("type", "string");
        writer.WriteString("pattern", "^[^.\\s]+$");
        writer.WriteEndObject();
    }

    private static void WriteControllers(Utf8JsonWriter writer, ComponentCatalogue catalogue)
    {
        writer.WriteStartObject("controllers");
        writer.WriteString("description", "Control blocks, scanned in the order listed: when two write one tag on one tick, the later wins.");
        writer.WriteString("type", "array");
        if (catalogue.Blocks.Count == 0)
        {
            // An empty oneOf is not valid JSON Schema; with no block types there is nothing to declare (R94).
            writer.WriteNumber("maxItems", 0);
        }
        else
        {
            writer.WritePropertyName("items");
            WriteOneOf(writer, catalogue.Blocks.Select(b => $"block.{b.Type}"));
        }

        writer.WriteEndObject();
    }

    private static void WriteBlock(Utf8JsonWriter writer, BlockDescriptor block)
    {
        writer.WriteStartObject();
        writer.WriteString("description", block.Description);
        writer.WriteString("type", "object");
        writer.WriteBoolean("additionalProperties", false);
        writer.WriteStartArray("required");
        writer.WriteStringValue("id");
        writer.WriteStringValue("type");
        writer.WriteStringValue("scanPeriodMs");
        if (block.Parameters.Any(p => p.IsRequired))
        {
            writer.WriteStringValue("parameters");
        }

        writer.WriteEndArray();
        writer.WriteStartObject("properties");

        WriteId(writer, "Unique across the plant's components and controllers. No dot and no whitespace: it prefixes every tag the block owns, as in INT01.Ok.");

        writer.WriteStartObject("type");
        writer.WriteString("const", block.Type);
        writer.WriteEndObject();

        writer.WriteStartObject("scanPeriodMs");
        writer.WriteString("description", "How often the block scans, in milliseconds: a whole number of time steps, at most a day.");
        writer.WriteString("type", "number");
        writer.WriteNumber("exclusiveMinimum", 0);
        writer.WriteNumber("maximum", 86_400_000);
        writer.WriteEndObject();

        writer.WritePropertyName("parameters");
        WriteParameterObject(writer, block.Parameters, description: null, typeConst: null);

        writer.WriteEndObject();
        writer.WriteEndObject();
    }
```

In `WriteParameter`'s switch, before `default:`, add:

```csharp
            case ParameterKind.Tag:
                writer.WriteString("description", $"{parameter.Description} The full name of {TagNoun(parameter)}.{LoaderChecks}");
                writer.WriteString("type", "string");
                break;

            case ParameterKind.Value:
                writer.WriteString(
                    "description",
                    $"{parameter.Description} Converted to the kind of the tag named by '{parameter.TagParameter}'. The schema cannot check that it fits; the loader does.");
                writer.WriteStartArray("type");
                writer.WriteStringValue("boolean");
                writer.WriteStringValue("number");
                writer.WriteEndArray();
                break;
```

and add after `Describe`:

```csharp
    private static string TagNoun(ParameterDescriptor parameter)
    {
        string noun = parameter.RequiredKind switch
        {
            null => "a tag",
            TagKind.Int64 => "an Int64 tag",
            { } kind => $"a {kind} tag",
        };

        return parameter.IsWriteTarget ? $"{noun} the block commands, so it must be read-write" : noun;
    }
```

- [ ] **Step 5: Run the tests; everything but the golden passes**

Run: `dotnet test tests/Millrace.Configuration.Tests --nologo`
Expected: every test PASSES except `PlantSchemaTests.MatchesTheGoldenFile`
(output differs from `plant.schema.json`). `TheSchemaIsItselfValidDraft202012`
passes: the generated schema is valid draft 2020-12.

- [ ] **Step 6: Regenerate the schema golden and read its diff**

```bash
MILLRACE_UPDATE_GOLDEN=1 dotnet test tests/Millrace.Configuration.Tests --nologo --filter FullyQualifiedName~MatchesTheGoldenFile
```

```bash
git diff --stat tests/Millrace.Configuration.Tests/Golden/plant.schema.json
```

```bash
git diff tests/Millrace.Configuration.Tests/Golden/plant.schema.json
```

**Read the whole diff**, then check and report, copying the lines:

| # | What the diff must show |
|---|---|
| 1 | (R101) the `description` line changes to `Generated from the catalogue of modules: Millrace.Components, Millrace.Control. Do not edit; run \`millrace schema export\`.` — the only changed pre-existing text besides row 2 |
| 2 | the root `tags` property's closing brace gains a comma, and a `controllers` property follows with `"type": "array"` and `items.oneOf` over `block.alarm`, `block.interlock`, `block.permissive`, `block.sequencer`, `block.timer` |
| 3 | twelve new `$defs`, each inserted in sorted position: `block.alarm`, `block.interlock`, `block.permissive`, `block.sequencer`, `block.timer`, `group.AlarmLimit`, `group.BlockWrite`, `group.Condition`, `group.SequenceStep`, `object.transition`, `object.transition.after`, `object.transition.when` |
| 4 | every `block.*` requires `id`, `type`, `scanPeriodMs`, `parameters`, closes itself and its parameters, and declares `scanPeriodMs` with `exclusiveMinimum: 0` and `maximum: 86400000` |
| 5 | every `Tag` property is `"type": "string"` with a description ending `The schema cannot check that it exists; the loader does.`; every `Value` property is `"type": [ "boolean", "number" ]` |
| 6 | no existing `component.*`, `group.*`, `object.hold*`, `object.transform*` or `envelope.*` definition changed |

- [ ] **Step 7: Run everything**

Run: `dotnet build Millrace.sln -c Release --nologo` — expect `0 Warning(s)`, `0 Error(s)`.
Run: `dotnet test Millrace.sln --nologo`
Expected: PASS, **1175** tests (Configuration 142).

- [ ] **Step 8: Commit**

```bash
git add src/Millrace.Configuration/Millrace.Configuration.csproj src/Millrace.Configuration/PlantSchema.cs src/Millrace.Configuration/Loading/PlantSchemas.cs tests/Millrace.Configuration.Tests/Plants.cs tests/Millrace.Configuration.Tests/PlantSchemaTests.cs tests/Millrace.Configuration.Tests/Golden/plant.schema.json
```

```bash
git commit -m "$(cat <<'MSG'
feat(schema): describe the controllers section of a plant file

A controller is one oneOf branch per block type, requiring its id, type
and scan period. Tag parameters are strings the loader resolves; values
are booleans or numbers the loader converts. The golden is regenerated
with the control catalogue.

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
MSG
)"
```

---

### Task 11: The loader — `controllers` read, resolved and built

Model: opus

Spec 4.1–4.5 with R80. The structure stage walks each controller as it walks a
component: unknown key `MR101`, unknown type `MR102`, a bad or missing
parameter or `scanPeriodMs` `MR103`, an id equal to another controller's or a
component's `MR107`. The build stage adds components and binds and validates
the plant alone; only if that is clean does it run `ControllerPass`: it builds the tag table from `builder.PlantTags()` and
every controller's `OwnedTags`, binds every controller in construct mode
against it (`MR113`–`MR115`, all collected), and only then calls the
factories in file order (`MR111`, including every plugin defect, R100) and adds
the blocks; a second `Validate()` then passes Core's `MR013`–`MR015` through
onto the controller's path (R92).

**Files:**
- Modify: `src/Millrace.Configuration/Loading/PlantSchemas.cs` (`ControllerKeys`)
- Modify: `src/Millrace.Configuration/Loading/LoadState.cs` (`ControllerEntry`, `Controllers`)
- Modify: `src/Millrace.Configuration/Loading/StructureStage.cs` (`ReadControllers` and helpers)
- Create: `src/Millrace.Configuration/Loading/ControllerPass.cs`
- Modify: `src/Millrace.Configuration/Loading/BuildStage.cs` (runs the pass; `PathOf`)
- Modify: `src/Millrace.Configuration/Loading/InstantiateStage.cs` (`AsSentence` becomes internal)
- Modify: `src/Millrace.Configuration/ConfigDiagnostics.cs`, `DiagnosticsReference.cs`,
  `PlantSummary.cs`, `PlantLoader.cs`
- Modify: `tests/Millrace.Configuration.Tests/TestModule.cs` (five defective blocks, R100)
- Modify: `tests/Millrace.Configuration.Tests/ConfigDiagnosticTests.cs` (13 → 16 codes, R98)
- Create: `tests/Millrace.Configuration.Tests/ControllerTests.cs`
- Create: `tests/Millrace.Configuration.Tests/Plants/invalid/MR113-unknown-tag.json`,
  `MR114-fraction-into-an-int64-tag.json`, `MR114-bool-into-a-double-tag.json`,
  `MR115-write-to-a-read-only-tag.json`
- Regenerate: `docs/configuration-diagnostics.md`

**Interfaces:**
- Consumes: `SimulationBuilder.PlantTags()` (Task 2); `ComponentCatalogue.TryGetBlock`,
  `Blocks`, `ModuleOf(BlockDescriptor)`, `BlockDescriptor.OwnedTags/Factory/Parameters`
  (Task 3); `BindingContext.UseTags`, `BindingIssueKind.UnknownTag/WrongTagKind/ReadOnlyTag`
  (Task 4); `ParameterBinder.Bind`; `LoadState.Error`, `AddIssues`, `HasErrors`;
  `SimulationBuilder.AddScanBlock(IScanBlock)`; `SimulationBuilder.Validate()` → `ValidationResult` with
  `IReadOnlyList<ValidationError> Errors`; `Permissive` and `Condition` (for the `wrong-id` test block).
- Produces:
  - `ConfigDiagnostics.UnknownTag` = `"MR113"`, `WrongKind` = `"MR114"`, `ReadOnlyTag` = `"MR115"`.
  - `internal sealed class ControllerEntry(int index, string id, BlockDescriptor descriptor, TimeSpan scanPeriod, JsonElement parameters, ParameterValues checkedValues)`
    with `Path` = `$.controllers[i]`, `ParametersPath`; `LoadState.Controllers`.
  - `internal static class ControllerPass` — `bool Run(LoadState state, SimulationBuilder builder)`.
  - `public sealed record PlantSummary(int Components, int SignalLinks, int FlowLinks, int ExplicitTags, int Controllers = 0)` (R95).
  - `PlantSchemas.ControllerKeys` = `["id", "type", "scanPeriodMs", "parameters"]`.

- [ ] **Step 1: Add five defective blocks to the test module**

In `tests/Millrace.Configuration.Tests/TestModule.cs`, add `using Millrace.Control;` to the
usings and, at the end of `Register` (R100 — every way a plugin's block
descriptor can be wrong must become `MR111`, never a crash):

```csharp
        builder.AddBlock(new BlockDescriptor(
            "broken-block",
            "Its factory is wrong.",
            (id, p) => [],
            (id, period, p) => throw new InvalidOperationException("bad wiring")));
        builder.AddBlock(new BlockDescriptor(
            "tagless",
            "Its owned-tag function is wrong.",
            (id, p) => throw new InvalidOperationException("no tags today"),
            (id, period, p) => throw new InvalidOperationException("never reached")));
        builder.AddBlock(new BlockDescriptor(
            "null-tags",
            "Its owned-tag function returns nothing at all.",
            (id, p) => null!,
            (id, period, p) => throw new InvalidOperationException("never reached")));
        builder.AddBlock(new BlockDescriptor(
            "null-block",
            "Its factory returns nothing at all.",
            (id, p) => [],
            (id, period, p) => null!));
        builder.AddBlock(new BlockDescriptor(
            "wrong-id",
            "Its factory ignores the id it is given.",
            (id, p) => [],
            (id, period, p) => new Permissive("OTHER", [new Condition("PILE.Full", false)], period)));
```

- [ ] **Step 2: Write the failing loader tests**

Create `tests/Millrace.Configuration.Tests/ControllerTests.cs`:

```csharp
using Millrace.Core;
using Millrace.Io;

namespace Millrace.Configuration.Tests;

public class ControllerTests
{
    /// <summary>The minimal plant; <c>CONTROLLERS</c> is replaced by the entries under test.</summary>
    private const string Base = """
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
            CONTROLLERS
          ]
        }
        """;

    private const string Perm01 =
        """{ "id": "PERM01", "type": "permissive", "scanPeriodMs": 100, "parameters": { "conditions": [ { "tag": "CHUTE.Full", "normal": false } ] } }""";

    private const string Int01 =
        """{ "id": "INT01", "type": "interlock", "scanPeriodMs": 100, "parameters": { "conditions": [ { "tag": "PERM01.Ok", "normal": true } ], "trip": [ { "tag": "FEED.Enabled", "value": false } ] } }""";

    private static string Plant(params string[] controllers) =>
        Base.Replace("CONTROLLERS", string.Join(",\n    ", controllers), StringComparison.Ordinal);

    [Fact]
    public void AControlledPlantLoadsBuildsAndCountsItsControllers()
    {
        LoadResult result = Plants.Load(Plant(Perm01, Int01));

        Assert.True(result.IsValid, result.ToText());
        Assert.Equal(new PlantSummary(3, 0, 2, 0, 2), result.Summary);

        Simulation simulation = result.Builder!.Build();
        Assert.Equal(2, simulation.ScanBlockCount);
        Assert.True(simulation.IO.Directory.TryFind("INT01.Reset", out TagDescriptor reset));
        Assert.Equal(TagAccess.ReadWrite, reset.Access);
        simulation.RunFor(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void ABlockMayNameALaterBlocksTag()
    {
        LoadResult result = Plants.Load(Plant(Int01, Perm01));

        Assert.True(result.IsValid, result.ToText());
        Assert.Equal(2, result.Builder!.Build().ScanBlockCount);
    }

    [Fact]
    public void AnUnknownTagSuggestsTheNearestAndIsReportedOnce()
    {
        ConfigDiagnostic d = Plants.Only(Plant(Perm01, Int01.Replace("FEED.Enabled", "FEED.Enabeld", StringComparison.Ordinal)));

        Assert.Equal("MR113", d.Code);
        Assert.Equal("$.controllers[1].parameters.trip[0].tag", d.Path);
        Assert.Equal("'FEED.Enabeld' is not a tag in this plant.", d.Message);
        Assert.Equal("Use a tag the plant has — 'FEED.Enabled' is closest.", d.Fix);
    }

    [Fact]
    public void EveryTagErrorIsReportedBeforeAnyBlockIsBuilt()
    {
        string json = Plant(
            Perm01.Replace("CHUTE.Full", "CHUTE.Level", StringComparison.Ordinal),
            Int01.Replace("PERM01.Ok", "PERM01.Okay", StringComparison.Ordinal).Replace("FEED.Enabled", "CHUTE.Full", StringComparison.Ordinal));

        Assert.Equal(
            new[]
            {
                ("MR114", "$.controllers[0].parameters.conditions[0].tag"),
                ("MR113", "$.controllers[1].parameters.conditions[0].tag"),
                ("MR115", "$.controllers[1].parameters.trip[0].tag"),
            },
            Plants.Load(json).Diagnostics.Select(d => (d.Code, d.Path)));
    }

    [Fact]
    public void AWriteToAnInputASignalLinkDrivesIsMr115()
    {
        string json = Plant(Perm01, Int01).Replace(
            "\"flows\":", "\"signals\": [ { \"from\": \"CHUTE.Full\", \"to\": \"FEED.Enabled\" } ],\n  \"flows\":", StringComparison.Ordinal);

        ConfigDiagnostic d = Plants.Only(json);

        Assert.Equal("MR115", d.Code);
        Assert.Equal("$.controllers[1].parameters.trip[0].tag", d.Path);
        Assert.Equal("'FEED.Enabled' is read-only, so a block cannot command it.", d.Message);
    }

    [Fact]
    public void AWriteToAnotherBlocksOutputIsMr115()
    {
        ConfigDiagnostic d = Plants.Only(Plant(Perm01, Int01.Replace("\"tag\": \"FEED.Enabled\"", "\"tag\": \"PERM01.Ok\"", StringComparison.Ordinal)));

        Assert.Equal("MR115", d.Code);
        Assert.Equal("$.controllers[1].parameters.trip[0].tag", d.Path);
    }

    [Fact]
    public void AScanPeriodOffTheStepIsMr013AtTheScanPeriod()
    {
        ConfigDiagnostic d = Plants.Only(Plant(Perm01.Replace("\"scanPeriodMs\": 100", "\"scanPeriodMs\": 15", StringComparison.Ordinal), Int01));

        Assert.Equal("MR013", d.Code);
        Assert.Equal("$.controllers[0].scanPeriodMs", d.Path);
        Assert.Equal("Block 'PERM01' scans every 15 ms, which is not a whole number of 10 ms steps.", d.Message);
        Assert.Equal("Use a period that is a multiple of the time step.", d.Fix);
    }

    [Theory]
    [InlineData("\"scanPeriodMs\": 0, ", "\"scanPeriodMs\" must be a number greater than zero.")]
    [InlineData("\"scanPeriodMs\": -5, ", "\"scanPeriodMs\" must be a number greater than zero.")]
    [InlineData("\"scanPeriodMs\": \"100\", ", "\"scanPeriodMs\" must be a number greater than zero.")]
    [InlineData("\"scanPeriodMs\": 1e30, ", "\"scanPeriodMs\" is 1E+30 ms, which is longer than a day.")]
    [InlineData("\"scanPeriodMs\": 0.00001, ", "\"scanPeriodMs\" must be at least one tick (0.0001 ms).")]
    [InlineData("", "A controller needs \"scanPeriodMs\"; there is no default.")]
    public void AScanPeriodIsAPositiveNumberOfAtMostADay(string replacement, string message)
    {
        ConfigDiagnostic d = Plants.Only(Plant(Perm01.Replace("\"scanPeriodMs\": 100, ", replacement, StringComparison.Ordinal)));

        Assert.Equal("MR103", d.Code);
        Assert.Equal("$.controllers[0].scanPeriodMs", d.Path);
        Assert.Equal(message, d.Message);
    }

    [Theory]
    [InlineData("INT.01")]
    [InlineData("INT 01")]
    [InlineData("")]
    public void AControllerIdFollowsTheComponentIdRules(string id)
    {
        ConfigDiagnostic d = Plants.Only(Plant(Perm01, Int01.Replace("\"id\": \"INT01\"", $"\"id\": \"{id}\"", StringComparison.Ordinal)));

        Assert.Equal("MR103", d.Code);
        Assert.Equal("$.controllers[1].id", d.Path);
    }

    [Theory]
    [InlineData("PERM01", "CHUTE", "$.controllers[0].id", "A component is already called 'CHUTE'.")]
    [InlineData("INT01", "PERM01", "$.controllers[1].id", "Another controller is already called 'PERM01'.")]
    public void AControllerIdIsUniqueAcrossComponentsAndControllers(string from, string to, string path, string message)
    {
        string json = Plant(Perm01, Int01).Replace($"\"id\": \"{from}\"", $"\"id\": \"{to}\"", StringComparison.Ordinal);

        ConfigDiagnostic d = Plants.Only(json);

        Assert.Equal("MR107", d.Code);
        Assert.Equal(path, d.Path);
        Assert.Equal(message, d.Message);
    }

    [Fact]
    public void AnUnknownBlockTypeSuggestsTheNearest()
    {
        ConfigDiagnostic d = Plants.Only(Plant(Perm01.Replace("\"permissive\"", "\"permisive\"", StringComparison.Ordinal)));

        Assert.Equal("MR102", d.Code);
        Assert.Equal("$.controllers[0].type", d.Path);
        Assert.Equal("'permisive' is not a block type in this catalogue.", d.Message);
        Assert.Contains("'permissive' is closest", d.Fix, StringComparison.Ordinal);
    }

    [Fact]
    public void AControllersSectionMustBeAnArray()
    {
        ConfigDiagnostic d = Plants.Only(Plants.Minimal.Replace("\"flows\":", "\"controllers\": { },\n  \"flows\":", StringComparison.Ordinal));

        Assert.Equal("MR103", d.Code);
        Assert.Equal("$.controllers", d.Path);
        Assert.Equal("\"controllers\" must be an array.", d.Message);
    }

    [Fact]
    public void AnOwnedTagThatABindAlreadyNamedIsMr015AtTheController()
    {
        string json = Plant(Perm01, Int01).Replace(
            "\"flows\":", "\"tags\": [ { \"name\": \"PERM01.Ok\", \"port\": \"PILE.Full\" } ],\n  \"flows\":", StringComparison.Ordinal);

        ConfigDiagnostic d = Plants.Only(json);

        Assert.Equal("MR015", d.Code);
        Assert.Equal("$.controllers[0]", d.Path);
        Assert.Contains("'PERM01.Ok'", d.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ABlockConstructorsRefusalIsMr111AtTheController()
    {
        const string LevelAlarm =
            """{ "id": "LVL01", "type": "alarm", "scanPeriodMs": 100, "parameters": { "input": "CHUTE.Level", "limits": [ { "kind": "hi", "value": 0.9 }, { "kind": "hi-hi", "value": 0.5 } ] } }""";

        ConfigDiagnostic d = Plants.Only(Plant(LevelAlarm));

        Assert.Equal("MR111", d.Code);
        Assert.Equal("$.controllers[0]", d.Path);
        Assert.Equal(
            "'LVL01' (alarm) rejected its parameters: Limits must ascend LoLo < Lo < Hi < HiHi, but Hi is 0.9 and HiHi is 0.5.",
            d.Message);
    }

    [Theory]
    [InlineData("broken-block", "The factory for type 'broken-block' failed with InvalidOperationException: bad wiring.")]
    [InlineData("tagless", "The owned-tag function of type 'tagless' failed with InvalidOperationException: no tags today.")]
    [InlineData("null-tags", "The owned-tag function of type 'null-tags' returned null or a null tag.")]
    [InlineData("null-block", "The factory for type 'null-block' returned null.")]
    [InlineData("wrong-id", "The factory for type 'wrong-id' built block 'OTHER' scanning every 100 ms, but was given 'B1' and 100 ms.")]
    public void AModuleDefectIsMr111NamingTheModule(string type, string message)
    {
        string json = $$"""{ "components": [ { "id": "PILE", "type": "bulk-sink" } ], "controllers": [ { "id": "B1", "type": "{{type}}", "scanPeriodMs": 100 } ] }""";

        ConfigDiagnostic d = TestPlants.Only(json);

        Assert.Equal("MR111", d.Code);
        Assert.Equal("$.controllers[0]", d.Path);
        Assert.Equal(message, d.Message);
        Assert.Contains("module 'Test'", d.Fix, StringComparison.Ordinal);
    }

    [Fact]
    public void APlantTagConflictIsReportedAsItselfNotAsAnUnknownTag()
    {
        // FEED.Go binds FEED.Enabled for writing, but a signal link already drives that input: MR010.
        // PlantTags() leaves the rejected tag out, so resolving INT01 first would have said MR113 (R80).
        string json = Plant(Int01.Replace("FEED.Enabled", "FEED.Go", StringComparison.Ordinal).Replace("PERM01.Ok\", \"normal\": true", "CHUTE.Full\", \"normal\": false", StringComparison.Ordinal))
            .Replace(
                "\"flows\":",
                "\"signals\": [ { \"from\": \"CHUTE.Full\", \"to\": \"FEED.Enabled\" } ],\n" +
                "  \"tags\": [ { \"name\": \"FEED.Go\", \"port\": \"FEED.Enabled\", \"access\": \"write\" } ],\n  \"flows\":",
                StringComparison.Ordinal);

        ConfigDiagnostic d = Plants.Only(json);

        Assert.Equal("MR010", d.Code);
        Assert.Equal("$.components[0]", d.Path);
        Assert.Contains("'FEED.Go'", d.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEmptyControllersSectionIsValid()
    {
        LoadResult result = Plants.Load(Plants.Minimal.Replace("\"flows\":", "\"controllers\": [],\n  \"flows\":", StringComparison.Ordinal));

        Assert.True(result.IsValid, result.ToText());
        Assert.Equal(0, result.Summary!.Controllers);
        Assert.Equal(0, result.Builder!.Build().ScanBlockCount);
    }

    [Fact]
    public void ADurationLongerThanAYearIsMr103AtTheParameter()
    {
        const string SlowTimer =
            """{ "id": "TMR01", "type": "timer", "scanPeriodMs": 100, "parameters": { "mode": "on-delay", "input": "CHUTE.Full", "presetS": 1e30 } }""";

        ConfigDiagnostic d = Plants.Only(Plant(SlowTimer));

        Assert.Equal("MR103", d.Code);
        Assert.Equal("$.controllers[0].parameters.presetS", d.Path);
    }
}
```

In `tests/Millrace.Configuration.Tests/ConfigDiagnosticTests.cs`, in
`TheTableListsEveryCodeOnceInOrder`, replace `Assert.Equal(13, codes.Length);`
with `Assert.Equal(16, codes.Length);` and `Assert.Equal("MR112", codes[^1]);`
with `Assert.Equal("MR115", codes[^1]);` (R98).

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet build tests/Millrace.Configuration.Tests --nologo`
Expected: FAIL — `error CS1729: 'PlantSummary' does not contain a constructor that takes 5 arguments`.

- [ ] **Step 4: The summary, the codes and the state**

Replace `src/Millrace.Configuration/PlantSummary.cs` with:

```csharp
namespace Millrace.Configuration;

/// <summary>What a valid plant file declared. Counts are of entries in the file, not of flattened leaves.</summary>
public sealed record PlantSummary(int Components, int SignalLinks, int FlowLinks, int ExplicitTags, int Controllers = 0);
```

In `src/Millrace.Configuration/PlantLoader.cs`, replace the summary construction with
`new PlantSummary(state.Components.Count, state.Signals.Count, state.Flows.Count, state.Tags.Count, state.Controllers.Count)`.

In `src/Millrace.Configuration/Loading/PlantSchemas.cs`, add after `ComponentKeys`:

```csharp
    public static readonly string[] ControllerKeys = ["id", "type", "scanPeriodMs", "parameters"];
```

In `src/Millrace.Configuration/Loading/LoadState.cs`, add after `ComponentEntry`:

```csharp
internal sealed class ControllerEntry(
    int index, string id, BlockDescriptor descriptor, TimeSpan scanPeriod, JsonElement parameters, ParameterValues checkedValues)
{
    public int Index { get; } = index;

    public string Id { get; } = id;

    public BlockDescriptor Descriptor { get; } = descriptor;

    public TimeSpan ScanPeriod { get; } = scanPeriod;

    /// <summary>The <c>parameters</c> object, or <c>default</c> when the entry has none.</summary>
    public JsonElement Parameters { get; } = parameters;

    /// <summary>The parameters as the structure stage bound them, for <see cref="BlockDescriptor.OwnedTags"/> (R82).</summary>
    public ParameterValues CheckedValues { get; } = checkedValues;

    public string Path => $"$.controllers[{Index}]";

    public string ParametersPath => $"{Path}.parameters";
}
```

and in `LoadState`, after `BuildOrder`:

```csharp
    /// <summary>In file order, which is scan order.</summary>
    public List<ControllerEntry> Controllers { get; } = [];
```

In `src/Millrace.Configuration/Loading/InstantiateStage.cs`, change
`private static string AsSentence(string message)` to
`internal static string AsSentence(string message)`.

Replace `src/Millrace.Configuration/ConfigDiagnostics.cs` with:

```csharp
using Millrace.Core.Catalogue;

namespace Millrace.Configuration;

/// <summary>Every configuration diagnostic code. The reference page in docs/ is generated from <see cref="All"/>.</summary>
public static class ConfigDiagnostics
{
    public const string Syntax = "MR100";
    public const string UnknownKey = "MR101";
    public const string UnknownType = "MR102";
    public const string BadParameter = "MR103";
    public const string MissingReference = "MR104";
    public const string MissingCapability = "MR105";
    public const string ReferenceCycle = "MR106";
    public const string Duplicate = "MR107";
    public const string UnknownAddress = "MR108";
    public const string CannotConnect = "MR109";
    public const string UnknownState = "MR110";
    public const string Rejected = "MR111";
    public const string TagCannotBind = "MR112";
    public const string UnknownTag = "MR113";
    public const string WrongKind = "MR114";
    public const string ReadOnlyTag = "MR115";

    public static IReadOnlyList<DiagnosticInfo> All { get; } =
    [
        new(Syntax, "The file is not valid JSON",
            "The text could not be parsed. Comments and trailing commas are allowed; everything else must be strict JSON. The message gives the line and column."),
        new(UnknownKey, "Unknown key",
            "An object has a key the loader does not know. Keys match exactly, including case. Nothing is ignored silently, so a misspelt optional parameter cannot quietly fall back to its default."),
        new(UnknownType, "Unknown component, block, object or material type",
            "A \"type\" names something that is not in the catalogue — a component, a controller's block, an object in a slot — or a material name is not defined in the plant or the catalogue. Custom types need their assembly loaded with --assembly."),
        new(BadParameter, "Parameter missing, of the wrong type, or out of range",
            "A required parameter is absent, a value has the wrong JSON type, a number is outside its declared range, an enum value is not allowed, or a list is shorter than its minimum. A controller's scanPeriodMs is required: a number above zero, at least one tick and at most a day."),
        new(MissingReference, "Reference to a component that does not exist",
            "A reference parameter holds an id that no component in the plant has. Ids match exactly."),
        new(MissingCapability, "Referenced component lacks the required capability",
            "The referenced component exists but cannot supply what the parameter needs — a belt scale pointed at a motor. The fix lists the components that can."),
        new(ReferenceCycle, "Reference cycle",
            "Components reference each other in a loop, so none of them can be built first."),
        new(Duplicate, "Duplicate id or material name",
            "Two components, two controllers, or a component and a controller share an id — they share one set of ids, because a block's id prefixes its tags — or a material is defined twice (the catalogue's materials and the plant's share one namespace)."),
        new(UnknownAddress, "Unknown component or port in an address",
            "A signal, flow or tag address is not of the form <component>.<port>, or names a component or port that does not exist. Port names match ignoring case."),
        new(CannotConnect, "The two ports cannot be connected",
            "A link joins ports that cannot be joined: two outputs, different value types, a signal port under \"flows\", bulk into discrete, an input that is already driven."),
        new(UnknownState, "Unknown material state",
            "A state name is not one of the states the named material declares."),
        new(Rejected, "A constructor rejected its parameters",
            "Every parameter was individually valid but the component, block or object refused the combination — a reset level above the trip level, a belt length that is not a whole number of cells, alarm limits that do not ascend. The message is the constructor's own. A factory that fails any other way is a defect in its module, and the fix names the module."),
        new(TagCannotBind, "A tag cannot bind that port",
            "A tag names a port that has no tag kind (a flow port, an enum output), or asks to write an output."),
        new(UnknownTag, "A controller names a tag the plant does not have",
            "A controller's tag parameter names no component tag, composite exposure or \"tags\" bind of the plant, and no tag a controller owns. Names match exactly, including case; the fix suggests the nearest, and `millrace tags` lists them all."),
        new(WrongKind, "A controller's tag or value is of the wrong kind",
            "A controller's value does not fit the kind of the tag it is for — 1.5 for an Int64 tag, true for a Double one — or a controller names a tag of a kind the block cannot use, such as a Double tag as an interlock condition. Integer-valued numbers fit Int64 and Double tags; only true and false fit a Bool tag."),
        new(ReadOnlyTag, "A controller commands a read-only tag",
            "A controller writes a tag that does not accept writes: a measured value, another block's output, or a command input a signal link already drives, whose tag the plant publishes read-only."),
    ];

    internal static ConfigDiagnostic Error(string code, string path, string message, string fix) =>
        new(code, DiagnosticSeverity.Error, path, message, fix);

    internal static ConfigDiagnostic From(BindingIssue issue) => Error(
        issue.Kind switch
        {
            BindingIssueKind.UnknownKey => UnknownKey,
            BindingIssueKind.UnknownType or BindingIssueKind.UnknownMaterial => UnknownType,
            BindingIssueKind.BadParameter => BadParameter,
            BindingIssueKind.UnknownState => UnknownState,
            BindingIssueKind.MissingReference => MissingReference,
            BindingIssueKind.MissingCapability => MissingCapability,
            BindingIssueKind.Rejected => Rejected,
            BindingIssueKind.UnknownTag => UnknownTag,
            BindingIssueKind.WrongTagKind => WrongKind,
            BindingIssueKind.ReadOnlyTag => ReadOnlyTag,
            _ => throw new InvalidOperationException($"Binding issue kind {issue.Kind} has no diagnostic code."),
        },
        issue.Path,
        issue.Message,
        issue.Fix);
}
```

In `src/Millrace.Configuration/DiagnosticsReference.cs`, replace the two constants
with (R96 — the trailer heading is unchanged):

```csharp
    private const string ConfigurationIntroduction =
        "<!-- Generated from ConfigDiagnostics.All by DiagnosticsReference.Render(). Do not edit by hand:\n" +
        "     run the Millrace.Configuration tests with MILLRACE_UPDATE_GOLDEN=1, read the result, commit it. -->\n\n" +
        "`millrace validate` and `PlantLoader.Load` report every problem in a plant file as a diagnostic with four\n" +
        "parts: a **code**, a **JSON path** into the file (`$.components[3].parameters.motor.ratedPowerW`), a\n" +
        "**message** saying what is wrong, and a **fix** saying what to do. A diagnostic without a fix cannot be\n" +
        "constructed.\n\n" +
        "The loader works in stages — parse, structure, references, instantiate, wire, build — and stops at the\n" +
        "end of the first stage that found an error, having reported *every* error that stage could find. Fixing\n" +
        "what is reported may therefore reveal errors from a later stage.\n\n" +
        "A plant's `controllers` are resolved in the build stage, once the plant itself has validated: every tag\n" +
        "they name and every value they give is checked against the plant's tags and the blocks' own\n" +
        "(MR113–MR115) before any block is built.\n\n";

    private const string ConfigurationTrailer =
        "## MR001–MR015 — plant validation\n\n" +
        "Codes below MR100 come from `SimulationBuilder.Validate()` and mean the same for a plant built in code:\n" +
        "duplicate ids, unconnected required inputs, algebraic loops, belts too fast for their cells, incompatible\n" +
        "flow links, tag conflicts — and, for a control block declared under `controllers` or attached with\n" +
        "`AddScanBlock`, a scan period that is not a positive whole number of time steps (MR013), a pin naming a\n" +
        "tag the plant does not have, publishes with another kind or will not accept a command (MR014), and a\n" +
        "block id or owned tag name that collides with something the plant already has (MR015). The numbering\n" +
        "skips 012. The loader passes them through with the path of the first component involved, or of the\n" +
        "controller (for MR013, its `scanPeriodMs`); their message is split at its first sentence into message\n" +
        "and fix. A plant file's controllers are resolved before the blocks are validated, so MR113–MR115\n" +
        "report what MR014 would. See `docs/architecture.md` and `docs/control-blocks.md`.\n";
```

- [ ] **Step 5: Read controllers in the structure stage**

In `src/Millrace.Configuration/Loading/StructureStage.cs`:

Replace the `MaxTimeStepMs` summary with
`/// <summary>A time step or a scan period longer than a day is a mistake, and 1e30 ms overflows <see cref="TimeSpan"/>.</summary>`.

In `Run`, replace `ReadComponents(state, root);` with
`HashSet<string> componentIds = ReadComponents(state, root);`, and after
`ReadTags(state, root);` add `ReadControllers(state, root, componentIds);`.

Change `ReadComponents` to return the ids it saw — replace its signature and
early return, and add the return at its end:

```csharp
    private static HashSet<string> ReadComponents(LoadState state, JsonElement root)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        if (!root.TryGetProperty("components", out JsonElement components) || components.ValueKind != JsonValueKind.Array)
        {
            state.Error(ConfigDiagnostics.BadParameter, "$.components", "A plant needs a \"components\" array.", "Add \"components\": [ { \"id\": …, \"type\": … } ].");
            return seen;
        }

        int index = 0;
        foreach (JsonElement element in components.EnumerateArray())
        {
            ReadComponent(state, element, index, seen);
            index++;
        }

        return seen;
    }
```

Add these methods after `ReadType`:

```csharp
    private static void ReadControllers(LoadState state, JsonElement root, HashSet<string> componentIds)
    {
        if (!root.TryGetProperty("controllers", out JsonElement controllers))
        {
            return;
        }

        if (controllers.ValueKind != JsonValueKind.Array)
        {
            state.Error(
                ConfigDiagnostics.BadParameter,
                "$.controllers",
                "\"controllers\" must be an array.",
                "Write \"controllers\": [ { \"id\": …, \"type\": …, \"scanPeriodMs\": … } ].");
            return;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        int index = 0;
        foreach (JsonElement element in controllers.EnumerateArray())
        {
            ReadController(state, element, index, seen, componentIds);
            index++;
        }
    }

    private static void ReadController(LoadState state, JsonElement element, int index, HashSet<string> seen, HashSet<string> componentIds)
    {
        string path = string.Create(CultureInfo.InvariantCulture, $"$.controllers[{index}]");
        if (element.ValueKind != JsonValueKind.Object)
        {
            state.Error(ConfigDiagnostics.BadParameter, path, "A controller is an object.", "Write { \"id\": …, \"type\": …, \"scanPeriodMs\": …, \"parameters\": { … } }.");
            return;
        }

        CheckKeys(state, element, path, PlantSchemas.ControllerKeys, "keys");

        string? id = ReadControllerId(state, element, path, seen, componentIds);
        BlockDescriptor? descriptor = ReadBlockType(state, element, path);
        TimeSpan? period = ReadScanPeriod(state, element, path);

        JsonElement parameters = default;
        if (element.TryGetProperty("parameters", out JsonElement given))
        {
            parameters = given;
        }

        if (descriptor is null)
        {
            return;
        }

        var issues = new List<BindingIssue>();
        ParameterValues? values = ParameterBinder.Bind(descriptor.Parameters, parameters, $"{path}.parameters", state.Context, construct: false, issues);
        state.AddIssues(issues);

        if (id is not null && period is { } scanPeriod && values is not null)
        {
            state.Controllers.Add(new ControllerEntry(index, id, descriptor, scanPeriod, parameters, values));
        }
    }

    /// <summary>R87: the component id rule, because a block id prefixes every tag the block owns.</summary>
    private static string? ReadControllerId(LoadState state, JsonElement element, string path, HashSet<string> seen, HashSet<string> componentIds)
    {
        if (!element.TryGetProperty("id", out JsonElement idElement) || idElement.ValueKind != JsonValueKind.String)
        {
            state.Error(ConfigDiagnostics.BadParameter, $"{path}.id", "A controller needs a string \"id\".", "Add \"id\": \"…\" with a name unique among the plant's components and controllers.");
            return null;
        }

        string id = idElement.GetString()!;
        if (id.Length == 0 || id.Contains('.', StringComparison.Ordinal) || id.Any(char.IsWhiteSpace))
        {
            state.Error(
                ConfigDiagnostics.BadParameter,
                $"{path}.id",
                $"'{id}' cannot be a controller id: an id is non-empty and has no dot and no whitespace.",
                "Use a hyphen or an underscore instead. The id prefixes every tag the block owns, as in INT01.Ok.");
            return null;
        }

        if (componentIds.Contains(id))
        {
            state.Error(ConfigDiagnostics.Duplicate, $"{path}.id", $"A component is already called '{id}'.", "Give this controller an id of its own; components and controllers share one set of ids.");
            return null;
        }

        if (!seen.Add(id))
        {
            state.Error(ConfigDiagnostics.Duplicate, $"{path}.id", $"Another controller is already called '{id}'.", "Give this controller an id of its own.");
            return null;
        }

        return id;
    }

    private static BlockDescriptor? ReadBlockType(LoadState state, JsonElement element, string path)
    {
        if (!element.TryGetProperty("type", out JsonElement typeElement) || typeElement.ValueKind != JsonValueKind.String)
        {
            state.Error(ConfigDiagnostics.BadParameter, $"{path}.type", "A controller needs a string \"type\".", "Add \"type\": \"…\" naming a block type; `millrace catalog export` lists them under \"blocks\".");
            return null;
        }

        string type = typeElement.GetString()!;
        if (state.Catalogue.TryGetBlock(type, out BlockDescriptor? descriptor))
        {
            return descriptor;
        }

        string? closest = Suggest.Closest(type, state.Catalogue.Blocks.Select(b => b.Type));
        state.Error(
            ConfigDiagnostics.UnknownType,
            $"{path}.type",
            $"'{type}' is not a block type in this catalogue.",
            closest is null
                ? "Run `millrace catalog export` to list the block types. A custom type needs its assembly loaded with --assembly."
                : $"Use a block type that `millrace catalog export` lists — '{closest}' is closest. A custom type needs its assembly loaded with --assembly.");
        return null;
    }

    /// <summary>R86: positive, at most a day (checked before TimeSpan conversion), and at least one tick.</summary>
    private static TimeSpan? ReadScanPeriod(LoadState state, JsonElement element, string path)
    {
        const string Fix = "Give the scan period in milliseconds, a whole number of time steps, such as 100.";
        string at = $"{path}.scanPeriodMs";
        if (!element.TryGetProperty("scanPeriodMs", out JsonElement periodElement))
        {
            state.Error(ConfigDiagnostics.BadParameter, at, "A controller needs \"scanPeriodMs\"; there is no default.", Fix);
            return null;
        }

        if (periodElement.ValueKind != JsonValueKind.Number || !periodElement.TryGetDouble(out double ms) || !double.IsFinite(ms) || ms <= 0.0)
        {
            state.Error(ConfigDiagnostics.BadParameter, at, "\"scanPeriodMs\" must be a number greater than zero.", Fix);
            return null;
        }

        if (ms > MaxTimeStepMs)
        {
            state.Error(
                ConfigDiagnostics.BadParameter,
                at,
                string.Create(CultureInfo.InvariantCulture, $"\"scanPeriodMs\" is {ms} ms, which is longer than a day."),
                Fix);
            return null;
        }

        TimeSpan period = TimeSpan.FromMilliseconds(ms);
        if (period.Ticks <= 0L)
        {
            state.Error(ConfigDiagnostics.BadParameter, at, "\"scanPeriodMs\" must be at least one tick (0.0001 ms).", Fix);
            return null;
        }

        return period;
    }
```

- [ ] **Step 6: Resolve and build them in the build stage**

Create `src/Millrace.Configuration/Loading/ControllerPass.cs`:

```csharp
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Millrace.Core;
using Millrace.Core.Catalogue;
using Millrace.Io;

namespace Millrace.Configuration.Loading;

/// <summary>
/// The build stage's middle step (R80), run once the plant alone has validated: the controllers. Every tag a controller
/// may name is known before any block exists — the plant's own, from the
/// builder, and every controller's owned tags, from its descriptor — so every
/// tag and value in every controller is resolved, and every error reported,
/// before the first factory runs.
/// </summary>
internal static class ControllerPass
{
    /// <summary>Adds every controller's block to <paramref name="builder"/> in file order; false after reporting why not.</summary>
    public static bool Run(LoadState state, SimulationBuilder builder)
    {
        if (state.Controllers.Count == 0)
        {
            return true;
        }

        // Plant tags first: on a name clash the plant's entry wins and Validate() reports MR015 (R85).
        var tags = new List<(string Name, TagKind Kind, TagAccess Access)>();
        foreach (TagDescriptor tag in builder.PlantTags())
        {
            tags.Add((tag.Name, tag.Kind, tag.Access));
        }

        foreach (ControllerEntry entry in state.Controllers)
        {
            foreach ((TagSpec spec, TagAccess access) in OwnedTags(state, entry))
            {
                tags.Add((spec.Name, spec.Kind, access));
            }
        }

        if (state.HasErrors)
        {
            return false;
        }

        state.Context.UseTags(tags);

        var bound = new List<(ControllerEntry Entry, ParameterValues Values)>(state.Controllers.Count);
        foreach (ControllerEntry entry in state.Controllers)
        {
            var issues = new List<BindingIssue>();
            ParameterValues? values = ParameterBinder.Bind(
                entry.Descriptor.Parameters, entry.Parameters, entry.ParametersPath, state.Context, construct: true, issues);
            state.AddIssues(issues);
            if (values is not null)
            {
                bound.Add((entry, values));
            }
        }

        if (state.HasErrors)
        {
            return false;
        }

        var blocks = new List<IScanBlock>(bound.Count);
        foreach ((ControllerEntry entry, ParameterValues values) in bound)
        {
            if (TryBuild(state, entry, values, out IScanBlock? block))
            {
                blocks.Add(block);
            }
        }

        if (state.HasErrors)
        {
            return false;
        }

        // File order is scan order, and the later block wins a same-tick write.
        foreach (IScanBlock block in blocks)
        {
            builder.AddScanBlock(block);
        }

        return true;
    }

    private static IReadOnlyList<(TagSpec Spec, TagAccess Access)> OwnedTags(LoadState state, ControllerEntry entry)
    {
        try
        {
            IReadOnlyList<(TagSpec Spec, TagAccess Access)>? owned = entry.Descriptor.OwnedTags(entry.Id, entry.CheckedValues);
            if (owned is null || owned.Any(t => t.Spec is null))
            {
                state.Error(
                    ConfigDiagnostics.Rejected,
                    entry.Path,
                    $"The owned-tag function of type '{entry.Descriptor.Type}' returned null or a null tag.",
                    ModuleDefect(state, entry));
                return [];
            }

            return owned;
        }
#pragma warning disable CA1031 // A defective third-party descriptor must become a diagnostic, not a crash.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            state.Error(
                ConfigDiagnostics.Rejected,
                entry.Path,
                $"The owned-tag function of type '{entry.Descriptor.Type}' failed with {ex.GetType().Name}: {InstantiateStage.AsSentence(ex.Message)}",
                ModuleDefect(state, entry));
            return [];
        }
    }

    private static bool TryBuild(LoadState state, ControllerEntry entry, ParameterValues values, [NotNullWhen(true)] out IScanBlock? block)
    {
        try
        {
            IScanBlock? made = entry.Descriptor.Factory(entry.Id, entry.ScanPeriod, values);
            if (made is null)
            {
                state.Error(
                    ConfigDiagnostics.Rejected,
                    entry.Path,
                    $"The factory for type '{entry.Descriptor.Type}' returned null.",
                    ModuleDefect(state, entry));
                block = null;
                return false;
            }

            // An id the factory was not given would publish tags nothing resolved against, and one that
            // breaks the name rules would throw out of AddScanBlock; a period it was not given breaks MR013's path.
            if (!string.Equals(made.Id, entry.Id, StringComparison.Ordinal) || made.ScanPeriod != entry.ScanPeriod)
            {
                state.Error(
                    ConfigDiagnostics.Rejected,
                    entry.Path,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"The factory for type '{entry.Descriptor.Type}' built block '{made.Id}' scanning every {made.ScanPeriod.TotalMilliseconds} ms, but was given '{entry.Id}' and {entry.ScanPeriod.TotalMilliseconds} ms."),
                    ModuleDefect(state, entry));
                block = null;
                return false;
            }

            block = made;
            return true;
        }
        catch (ArgumentException ex)
        {
            state.Error(
                ConfigDiagnostics.Rejected,
                entry.Path,
                $"'{entry.Id}' ({entry.Descriptor.Type}) rejected its parameters: {InstantiateStage.AsSentence(ex.Message)}",
                "Change the parameter the message names; each value is valid alone, the combination is not.");
        }
#pragma warning disable CA1031 // A defective third-party factory must become a diagnostic, not a crash.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            state.Error(
                ConfigDiagnostics.Rejected,
                entry.Path,
                $"The factory for type '{entry.Descriptor.Type}' failed with {ex.GetType().Name}: {InstantiateStage.AsSentence(ex.Message)}",
                ModuleDefect(state, entry));
        }

        block = null;
        return false;
    }

    private static string ModuleDefect(LoadState state, ControllerEntry entry) =>
        $"Report this to the author of module '{state.Catalogue.ModuleOf(entry.Descriptor)}' together with this plant file; " +
        "it is a defect in the module, not in the plant. The module's conformance test should have caught it.";
}
```

In `src/Millrace.Configuration/Loading/BuildStage.cs`, replace the loop

```csharp
        foreach (ValidationError error in builder.Validate().Errors)
        {
            (string message, string fix) = Split(error.Message);
            state.Error(error.Code, PathOf(state, error), message, fix);
        }
```

with (R80 — the plant is validated alone first, so a plant error such as
`MR009` or `MR010`, whose tag `PlantTags()` leaves out, is reported as itself
and never as a misleading `MR113`/`MR115`; only a valid plant gets its
controllers, and the second `Validate()` checks the blocks, `MR013`–`MR015`):

```csharp
        IReadOnlyList<ValidationError> errors = builder.Validate().Errors;
        if (errors.Count == 0 && state.Controllers.Count > 0)
        {
            if (!ControllerPass.Run(state, builder))
            {
                return;
            }

            errors = builder.Validate().Errors;
        }

        foreach (ValidationError error in errors)
        {
            (string message, string fix) = Split(error.Message);
            state.Error(error.Code, PathOf(state, error), message, fix);
        }
```

and replace `PathOf` with:

```csharp
    private static string PathOf(LoadState state, ValidationError error)
    {
        foreach (string id in error.ComponentIds)
        {
            int dot = id.IndexOf('.', StringComparison.Ordinal);
            string top = dot < 0 ? id : id[..dot];
            ComponentEntry? entry = state.Components.FirstOrDefault(c => string.Equals(c.Id, top, StringComparison.Ordinal));
            if (entry is not null)
            {
                return entry.Path;
            }

            ControllerEntry? controller = state.Controllers.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.Ordinal));
            if (controller is not null)
            {
                // R92: a scan period off the step is the one block check with a key of its own.
                return string.Equals(error.Code, "MR013", StringComparison.Ordinal) ? $"{controller.Path}.scanPeriodMs" : controller.Path;
            }
        }

        return "$";
    }
```

Update `BuildStage`'s class summary to
`/// <summary>Stage 6: hand the plant to Core; if it is valid, resolve and add the controllers (R80) and hand it over again.</summary>`.

- [ ] **Step 7: Add one invalid fixture per new code**

`CorpusTests.EveryConfigurationCodeHasAnInvalidPlant` requires a fixture for
every code in `ConfigDiagnostics.All`. Create
`tests/Millrace.Configuration.Tests/Plants/invalid/MR113-unknown-tag.json`:

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
    { "id": "PERM01", "type": "permissive", "scanPeriodMs": 100,
      "parameters": { "conditions": [ { "tag": "CHUTE.Ful", "normal": false } ] } }
  ]
}
```

Create `tests/Millrace.Configuration.Tests/Plants/invalid/MR114-fraction-into-an-int64-tag.json`:

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
    { "id": "PERM01", "type": "permissive", "scanPeriodMs": 100,
      "parameters": { "conditions": [ { "tag": "CHUTE.Full", "normal": false } ] } },
    { "id": "SEQ01", "type": "sequencer", "scanPeriodMs": 100,
      "parameters": { "steps": [
        { "name": "Wait for a first-out",
          "transition": { "type": "when", "tag": "PERM01.FirstOut", "op": "==", "value": 1.5 } } ] } }
  ]
}
```

Create `tests/Millrace.Configuration.Tests/Plants/invalid/MR114-bool-into-a-double-tag.json`:

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
        "trip": [ { "tag": "FEED.Rate", "value": true } ] } }
  ]
}
```

Create `tests/Millrace.Configuration.Tests/Plants/invalid/MR115-write-to-a-read-only-tag.json`:

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
        "trip": [ { "tag": "CHUTE.Level", "value": 0.5 } ] } }
  ]
}
```

- [ ] **Step 8: Run the loader tests**

Run: `dotnet test tests/Millrace.Configuration.Tests --nologo --filter "FullyQualifiedName~ControllerTests|FullyQualifiedName~CorpusTests|FullyQualifiedName~SchemaAgreementTests|FullyQualifiedName~ConfigDiagnosticTests"`
Expected: PASS. `ControllerTests` runs 30 tests; each of the four new fixtures
yields only its named code, and the schema accepts all four (they are semantic).

Run: `dotnet test tests/Millrace.Configuration.Tests --nologo --filter FullyQualifiedName~TheCommittedReferencePageIsCurrent`
Expected: FAIL — the reference page differs from `docs/configuration-diagnostics.md`.

- [ ] **Step 9: Regenerate the diagnostics page and read it**

```bash
MILLRACE_UPDATE_GOLDEN=1 dotnet test tests/Millrace.Configuration.Tests --nologo --filter FullyQualifiedName~TheCommittedReferencePageIsCurrent
```

```bash
git diff docs/configuration-diagnostics.md
```

Read the diff and report: the table gains rows `MR113`, `MR114`, `MR115`
after `MR112`; `MR102`'s and `MR107`'s titles read `Unknown component, block,
object or material type` and `Duplicate id or material name`; three new
sections follow `MR112`'s; the introduction gains the controllers paragraph;
the trailer heading is still `## MR001–MR015 — plant validation` and the page
nowhere prints `MR012`.

- [ ] **Step 10: Run everything**

Run: `dotnet build Millrace.sln -c Release --nologo` — expect `0 Warning(s)`, `0 Error(s)`.
Run: `dotnet test Millrace.sln --nologo`
Expected: PASS, **1213** tests (Configuration 180: 142 + 30 `ControllerTests` +
4 rows each in `EveryInvalidPlantYieldsExactlyTheCodeInItsName` and
`TheSchemaRejectsStructuralErrorsAndOnlyThose`).

- [ ] **Step 11: Commit**

```bash
git add src/Millrace.Configuration/Loading/PlantSchemas.cs src/Millrace.Configuration/Loading/LoadState.cs src/Millrace.Configuration/Loading/StructureStage.cs src/Millrace.Configuration/Loading/ControllerPass.cs src/Millrace.Configuration/Loading/BuildStage.cs src/Millrace.Configuration/Loading/InstantiateStage.cs src/Millrace.Configuration/ConfigDiagnostics.cs src/Millrace.Configuration/DiagnosticsReference.cs src/Millrace.Configuration/PlantSummary.cs src/Millrace.Configuration/PlantLoader.cs tests/Millrace.Configuration.Tests/TestModule.cs tests/Millrace.Configuration.Tests/ControllerTests.cs tests/Millrace.Configuration.Tests/ConfigDiagnosticTests.cs tests/Millrace.Configuration.Tests/Plants/invalid/MR113-unknown-tag.json tests/Millrace.Configuration.Tests/Plants/invalid/MR114-fraction-into-an-int64-tag.json tests/Millrace.Configuration.Tests/Plants/invalid/MR114-bool-into-a-double-tag.json tests/Millrace.Configuration.Tests/Plants/invalid/MR115-write-to-a-read-only-tag.json docs/configuration-diagnostics.md
```

```bash
git commit -m "$(cat <<'MSG'
feat(config): load controllers from the plant file

The structure stage reads each controller like a component. The build
stage resolves every tag and value against the plant's tags and the
blocks' own before any block is built (MR113-MR115), then builds them
in file order. Core's block checks land on the controller's path.

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
MSG
)"
```

---

### Task 12: The corpus — the worked example as a plant file, the remaining invalid fixtures, schema agreement

Model: sonnet

Spec 7 and 8. `valid/conveyor-control.json` is the 5c worked example —
`conveyor-line` plus `PERM01`, `INT01`, `CUR01` and `SEQ01`, in that order, with
exactly the 5c parameters (`WorkedExampleTests.Build()`). Eight more invalid
fixtures cover the structural and semantic cases spec 8 lists. Every fixture
joins the two corpus theories and the two schema-agreement theories
automatically; a new agreement theory asserts that the schema and the loader
reject the same controller mistakes.

**Files:**
- Create: `tests/Millrace.Configuration.Tests/Plants/valid/conveyor-control.json`
- Create (all under `tests/Millrace.Configuration.Tests/Plants/invalid/`):
  `MR102-unknown-block-type.json`, `MR103-controller-without-scan-period.json`,
  `MR103-unknown-transition-operator.json`, `MR103-negative-preset.json`,
  `MR101-unknown-controller-key.json`, `MR107-controller-id-is-a-component-id.json`,
  `MR111-alarm-limits-out-of-order.json`, `MR013-scan-period-off-the-step.json`
- Test: `tests/Millrace.Configuration.Tests/SchemaAgreementTests.cs`

**Interfaces:**
- Consumes: the loader of Task 11, the schema of Task 10, `Corpus.Valid()`,
  `Corpus.Invalid()`, `Corpus.Read(kind, name)`, `Plants.Load(json)`.
- Produces: `Plants/valid/conveyor-control.json`, which Tasks 13, 14 and 15 load
  (it is linked into `Millrace.Control.Tests`, `Millrace.Scenarios.Tests` and, in Task 15,
  `Millrace.Cli.Tests`).

- [ ] **Step 1: Write the worked example as a plant file**

Create `tests/Millrace.Configuration.Tests/Plants/valid/conveyor-control.json`:

```json
{
  "defaults": { "seed": 1, "timeStepMs": 10, "startTime": "2026-01-01T06:00:00Z" },
  "materials": [
    { "name": "Ore", "kind": "bulk", "properties": { "density": 2000, "moisture": 0.03, "temperature": 15 } }
  ],
  "components": [
    { "id": "Pile", "type": "bulk-sink" },
    { "id": "Chute", "type": "transfer-chute", "parameters": { "capacityKg": 200 } },
    { "id": "CV001", "type": "conveyor", "parameters": {
        "lengthM": 10, "cellSizeM": 0.5, "beltWidthM": 0.8, "angleOfReposeDeg": 20, "materialDensityKgM3": 2000,
        "emptyBeltMassKg": 250, "frictionCoefficient": 0.04, "pulleyDiameterM": 0.5, "gearRatio": 20,
        "motor": { "ratedPowerW": 750, "ratedSpeedRadPerS": 150, "ratedCurrentA": 2 },
        "tailDragN": 80 } },
    { "id": "Feed", "type": "bulk-source", "parameters": { "material": "Ore", "rateKgPerS": 20 } }
  ],
  "flows": [
    { "from": "Feed.Out", "to": "CV001.In" },
    { "from": "CV001.Out", "to": "Chute.In" },
    { "from": "Chute.Out", "to": "Pile.In" }
  ],
  "controllers": [
    { "id": "PERM01", "type": "permissive", "scanPeriodMs": 100,
      "parameters": {
        "conditions": [
          { "tag": "CV001.SafetyOk", "normal": true },
          { "tag": "Pile.Full",      "normal": false } ] } },
    { "id": "INT01", "type": "interlock", "scanPeriodMs": 100,
      "parameters": {
        "conditions": [
          { "tag": "CV001.Tripped", "normal": false },
          { "tag": "PERM01.Ok",     "normal": true } ],
        "trip": [ { "tag": "CV001.Start", "value": false } ] } },
    { "id": "CUR01", "type": "alarm", "scanPeriodMs": 100,
      "parameters": {
        "input": "CV001.Current",
        "limits": [
          { "kind": "hi",    "value": 3.0, "deadband": 0.2, "onDelayS": 0.5 },
          { "kind": "hi-hi", "value": 8.0, "deadband": 0.5, "onDelayS": 0.1 } ] } },
    { "id": "SEQ01", "type": "sequencer", "scanPeriodMs": 200,
      "parameters": {
        "steps": [
          { "name": "Reset the safety relay",
            "writes": [ { "tag": "CV001.SafetyReset", "value": true } ],
            "transition": { "type": "after", "delayS": 1 } },
          { "name": "Reset the interlock",
            "writes": [ { "tag": "CV001.SafetyReset", "value": false }, { "tag": "INT01.Reset", "value": true } ],
            "transition": { "type": "after", "delayS": 1 } },
          { "name": "Start the belt",
            "writes": [ { "tag": "INT01.Reset", "value": false }, { "tag": "CV001.Start", "value": true } ],
            "transition": { "type": "when", "tag": "CV001.Speed", "op": ">=", "value": 1.0 },
            "timeoutS": 15 },
          { "name": "Run the feed",
            "writes": [ { "tag": "Feed.Enabled", "value": true } ],
            "transition": { "type": "after", "delayS": 60 } },
          { "name": "Stop the feed",
            "writes": [ { "tag": "Feed.Enabled", "value": false } ],
            "transition": { "type": "after", "delayS": 2 } },
          { "name": "Stop the belt",
            "writes": [ { "tag": "CV001.Start", "value": false } ],
            "transition": { "type": "when", "tag": "CV001.Stopped", "op": "==", "value": true },
            "timeoutS": 60 } ],
        "abort": [ { "tag": "CV001.Start", "value": false } ] } }
  ]
}
```

Everything above `controllers` is byte-identical to `valid/conveyor-line.json`;
check it with `diff <(head -21 tests/Millrace.Configuration.Tests/Plants/valid/conveyor-control.json) tests/Millrace.Configuration.Tests/Plants/valid/conveyor-line.json`,
which must report exactly two differing lines: line 20 (`  ]` in
`conveyor-line.json`, `  ],` here) and line 21 (`}` there, `  "controllers": [`
here).

- [ ] **Step 2: Write the eight invalid fixtures**

Each is the minimal plant with one controller mistake. Create
`tests/Millrace.Configuration.Tests/Plants/invalid/MR102-unknown-block-type.json`:

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
    { "id": "PERM01", "type": "permisive", "scanPeriodMs": 100,
      "parameters": { "conditions": [ { "tag": "CHUTE.Full", "normal": false } ] } }
  ]
}
```

`MR103-controller-without-scan-period.json`:

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
    { "id": "PERM01", "type": "permissive",
      "parameters": { "conditions": [ { "tag": "CHUTE.Full", "normal": false } ] } }
  ]
}
```

`MR103-unknown-transition-operator.json`:

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
    { "id": "SEQ01", "type": "sequencer", "scanPeriodMs": 100,
      "parameters": { "steps": [
        { "name": "Fill the chute",
          "transition": { "type": "when", "tag": "CHUTE.Level", "op": "=>", "value": 0.5 } } ] } }
  ]
}
```

`MR103-negative-preset.json`:

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
    { "id": "TMR01", "type": "timer", "scanPeriodMs": 100,
      "parameters": { "mode": "on-delay", "input": "CHUTE.Full", "presetS": -1 } }
  ]
}
```

`MR101-unknown-controller-key.json`:

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
    { "id": "PERM01", "type": "permissive", "scanPeriodMs": 100, "period": 100,
      "parameters": { "conditions": [ { "tag": "CHUTE.Full", "normal": false } ] } }
  ]
}
```

`MR107-controller-id-is-a-component-id.json`:

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
    { "id": "CHUTE", "type": "permissive", "scanPeriodMs": 100,
      "parameters": { "conditions": [ { "tag": "CHUTE.Full", "normal": false } ] } }
  ]
}
```

`MR111-alarm-limits-out-of-order.json` (the constructor sorts limits by kind,
so "out of order" means values that do not ascend with the kinds):

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
    { "id": "LVL01", "type": "alarm", "scanPeriodMs": 100,
      "parameters": {
        "input": "CHUTE.Level",
        "limits": [ { "kind": "hi", "value": 0.9 }, { "kind": "hi-hi", "value": 0.5 } ] } }
  ]
}
```

`MR013-scan-period-off-the-step.json`:

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
    { "id": "PERM01", "type": "permissive", "scanPeriodMs": 15,
      "parameters": { "conditions": [ { "tag": "CHUTE.Full", "normal": false } ] } }
  ]
}
```

- [ ] **Step 3: Write the controller agreement theory**

In `tests/Millrace.Configuration.Tests/SchemaAgreementTests.cs`, add after
`BothValidatorsRejectTheSameStructuralMistakes`. Every `from` has been checked
against `Good` byte for byte: `"scanPeriodMs": 100,` ends its line (a newline,
not a space, follows the comma), which is why that row has no trailing space;
the `Assert.NotEqual` guard catches a row that silently replaces nothing.

```csharp
    [Theory]
    [InlineData("\"scanPeriodMs\": 100", "\"scanPeriodMs\": 0")]
    [InlineData("\"scanPeriodMs\": 100,", "")]
    [InlineData("\"type\": \"timer\"", "\"type\": \"timmer\"")]
    [InlineData("\"mode\": \"on-delay\"", "\"mode\": \"on_delay\"")]
    [InlineData("\"presetS\": 2", "\"presetS\": 2, \"preset\": 2")]
    [InlineData("\"id\": \"TMR01\"", "\"id\": \"TMR.01\"")]
    public void BothValidatorsRejectTheSameControllerMistakes(string from, string to)
    {
        const string Good = """
            { "components": [ { "id": "PILE", "type": "bulk-sink" } ],
              "controllers": [ { "id": "TMR01", "type": "timer", "scanPeriodMs": 100,
                                 "parameters": { "mode": "on-delay", "input": "PILE.Full", "presetS": 2 } } ] }
            """;
        string bad = Good.Replace(from, to, StringComparison.Ordinal);

        Assert.NotEqual(Good, bad);                   // every `from` must occur in Good, byte for byte
        Assert.True(Accepts(Good));
        Assert.True(Plants.Load(Good).IsValid, Plants.Load(Good).ToText());
        Assert.False(Accepts(bad));
        Assert.False(Plants.Load(bad).IsValid);
    }
```

- [ ] **Step 4: Run the configuration tests**

Run: `dotnet test tests/Millrace.Configuration.Tests --nologo`
Expected: PASS, **204** tests: 180 + 1 valid and 8 invalid rows in each of the two
corpus theories (18) + 6 agreement rows. In particular:
`EveryValidPlantLoadsCleanAndBuilds(conveyor-control.json)` loads with no
diagnostic and runs two seconds; `TheSchemaAcceptsEveryPlantTheLoaderAccepts(conveyor-control.json)`
passes; the schema rejects `MR101-unknown-controller-key`, `MR102-unknown-block-type`
and the three new `MR103` fixtures, and accepts `MR107-controller-id-is-a-component-id`,
`MR111-alarm-limits-out-of-order` and `MR013-scan-period-off-the-step`. If a
fixture yields a second code, report it and fix the fixture, not the loader.

- [ ] **Step 5: Run everything**

Run: `dotnet build Millrace.sln -c Release --nologo` — expect `0 Warning(s)`, `0 Error(s)`.
Run: `dotnet test Millrace.sln --nologo`
Expected: PASS, **1237** tests (Configuration 204). The new valid plant is also
copied beside `Millrace.Control.Tests` and `Millrace.Scenarios.Tests` by their existing
`Plants\valid\*.json` links; nothing there iterates it.

- [ ] **Step 6: Commit**

```bash
git add tests/Millrace.Configuration.Tests/Plants/valid/conveyor-control.json tests/Millrace.Configuration.Tests/Plants/invalid/MR102-unknown-block-type.json tests/Millrace.Configuration.Tests/Plants/invalid/MR103-controller-without-scan-period.json tests/Millrace.Configuration.Tests/Plants/invalid/MR103-unknown-transition-operator.json tests/Millrace.Configuration.Tests/Plants/invalid/MR103-negative-preset.json tests/Millrace.Configuration.Tests/Plants/invalid/MR101-unknown-controller-key.json tests/Millrace.Configuration.Tests/Plants/invalid/MR107-controller-id-is-a-component-id.json tests/Millrace.Configuration.Tests/Plants/invalid/MR111-alarm-limits-out-of-order.json tests/Millrace.Configuration.Tests/Plants/invalid/MR013-scan-period-off-the-step.json tests/Millrace.Configuration.Tests/SchemaAgreementTests.cs
```

```bash
git commit -m "$(cat <<'MSG'
test(config): add the controlled plant and its invalid fixtures

The 5c worked example becomes a plant file. One fixture per controller
mistake the spec names joins the corpus, and the schema and the loader
are held to rejecting the same structural controller mistakes.

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
MSG
)"
```

---

### Task 13: The worked example from JSON, byte-identical to the code-built one

Model: opus

Spec 7, criterion 2 (R93). The JSON-built plant and the 5c code-built plant,
driven identically for 120 s, must produce the same directory and the same
`EventLog.ToText()` byte for byte — and that text must be the golden Task 1
regenerated. Nothing is regenerated here: a mismatch is a finding.

**Files:**
- Modify: `tests/Millrace.Control.Tests/Millrace.Control.Tests.csproj` (reference `Millrace.Control.Catalogue`)
- Test: `tests/Millrace.Control.Tests/WorkedExampleTests.cs`

**Interfaces:**
- Consumes: `valid/conveyor-control.json` (Task 12, linked as `Plants/conveyor-control.json`);
  `ControlModule`; `PlantLoader.Load(string, ComponentCatalogue, LoadOptions)`;
  the existing `WorkedExampleTests.Build()`; `Golden.Assert`.
- Produces: `WorkedExampleTests.Drive(Simulation)` (private) — the shared timeline.

- [ ] **Step 1: Reference the control catalogue**

In `tests/Millrace.Control.Tests/Millrace.Control.Tests.csproj`, add to the project
references:

```xml
    <ProjectReference Include="..\..\src\Millrace.Control.Catalogue\Millrace.Control.Catalogue.csproj" />
```

- [ ] **Step 2: Share the timeline and add the JSON twin**

In `tests/Millrace.Control.Tests/WorkedExampleTests.cs`, add
`using Millrace.Control.Catalogue;`, replace the `Catalogue` property with:

```csharp
    private static ComponentCatalogue Catalogue { get; } =
        new CatalogueBuilder().Add<ComponentsModule>().Add<ControlModule>().Build();
```

replace the last lines of `Build()` — from `Simulation sim = builder.Build();`
to `return sim;` — with:

```csharp
        Simulation sim = builder.Build();
        Drive(sim);
        return sim;
    }

    /// <summary>The operator's part of the example: start the sequence, release Start, inject the overload.</summary>
    private static void Drive(Simulation sim)
    {
        sim.WriteAt(TimeSpan.FromSeconds(1), "SEQ01.Start", TagValue.Bool(true));
        sim.WriteAt(TimeSpan.FromSeconds(2), "SEQ01.Start", TagValue.Bool(false));
        sim.InjectFaultAt(
            TimeSpan.FromSeconds(40),
            "CV001.Motor",
            "thermal-bias",
            new FaultArguments(new FaultArgument("amount", 0.8)));
    }

    /// <summary>The same plant and blocks, declared in <c>conveyor-control.json</c>.</summary>
    private static Simulation BuildFromJson()
    {
        string json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Plants", "conveyor-control.json"));
        LoadResult result = PlantLoader.Load(json, Catalogue, new LoadOptions());
        Assert.True(result.IsValid, result.ToText());

        Simulation sim = result.Builder!.Build();
        Drive(sim);
        return sim;
```

(the closing brace of `BuildFromJson` is the one that closed `Build()`), and add
these facts after `TheWorkedExampleRunsTwiceByteIdentically`:

```csharp
    [Fact]
    public void TheJsonWorkedExampleHasTheSameBlocksAndDirectory()
    {
        Simulation fromJson = BuildFromJson();
        Simulation inCode = Build();

        Assert.Equal(4, fromJson.ScanBlockCount);
        Assert.Equal(47, fromJson.IO.Directory.Count);
        Assert.Equal(inCode.IO.Directory.ToText(), fromJson.IO.Directory.ToText());
    }

    [Fact]
    public void TheJsonWorkedExampleWritesTheCodeBuiltEventLogByteForByte()
    {
        Simulation fromJson = BuildFromJson();
        Simulation inCode = Build();

        fromJson.RunFor(TimeSpan.FromSeconds(120));
        inCode.RunFor(TimeSpan.FromSeconds(120));

        Assert.Equal(inCode.Events.ToText(), fromJson.Events.ToText());
        Assert.Equal(inCode.IO.Snapshot().ToArray(), fromJson.IO.Snapshot().ToArray());
        Golden.Assert("Golden/conveyor-control.log", fromJson.Events.ToText());
    }
```

- [ ] **Step 3: Run the worked-example tests**

Run: `dotnet test tests/Millrace.Control.Tests --nologo --filter FullyQualifiedName~WorkedExampleTests`
Expected: PASS, 5 tests. Do **not** set `MILLRACE_UPDATE_GOLDEN`: the golden is
Task 1's. If `TheJsonWorkedExampleWritesTheCodeBuiltEventLogByteForByte` fails,
diff the two logs line by line, report the first divergent line and its cause
(a parameter in `conveyor-control.json` that differs from `Build()`, a block
order, a value kind), and fix the JSON — never the golden.

- [ ] **Step 4: Run everything**

Run: `dotnet build Millrace.sln -c Release --nologo` — expect `0 Warning(s)`, `0 Error(s)`.
Run: `dotnet test Millrace.sln --nologo`
Expected: PASS, **1239** tests (Control 104).

- [ ] **Step 5: Commit**

```bash
git add tests/Millrace.Control.Tests/Millrace.Control.Tests.csproj tests/Millrace.Control.Tests/WorkedExampleTests.cs
```

```bash
git commit -m "$(cat <<'MSG'
test(control): prove the plant-file worked example matches the code

The worked example declared in conveyor-control.json builds the same
47-tag directory and writes the code-built event log byte for byte,
which is the committed 5c golden.

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
MSG
)"
```

---

### Task 14: A recording of a controlled run replays byte for byte (R77 closed)

Model: sonnet

Spec 5 and criterion 3. Record the worked example's run: the recording holds
exactly the three external actions — `SEQ01.Start` true at 1 s and false at 2 s,
the `thermal-bias` fault at 40 s — and none of the ten block writes; replaying it
through `ScenarioRunner` re-derives every block write and reproduces the live
event log byte for byte.

**Timing rule:** a scan at tick N sees the image published at the end of tick
N−1; its outputs are visible from the end of tick N; its queued writes land at
phase 1 of tick N+1. The `WriteAt(1 s)` lands at phase 1 of tick 100 and is
recorded at tick 100, so its `at` is `100 × 10 ms = 1` s.

**Files:**
- Modify: `tests/Millrace.Scenarios.Tests/Millrace.Scenarios.Tests.csproj` (reference `Millrace.Control.Catalogue`)
- Create: `tests/Millrace.Scenarios.Tests/ControlledRecordAndReplayTests.cs`

**Interfaces:**
- Consumes: `ScenarioRecorder` (`Count`, `ToScenario(string plantPath, SimulationOptions options, TimeSpan duration)`),
  `ScenarioJson.Write(Scenario)`, `ScenarioLoader.Parse(string)`,
  `ScenarioRunner.Run(Scenario, string plantJson, ComponentCatalogue)`,
  `Corpus.PlantPath(string)`; `ControlModule`; Task 1's attribution.
- Produces: no new API.

- [ ] **Step 1: Reference the control catalogue**

In `tests/Millrace.Scenarios.Tests/Millrace.Scenarios.Tests.csproj`, add to the project
references:

```xml
    <ProjectReference Include="..\..\src\Millrace.Control.Catalogue\Millrace.Control.Catalogue.csproj" />
```

- [ ] **Step 2: Write the tests**

Create `tests/Millrace.Scenarios.Tests/ControlledRecordAndReplayTests.cs`:

```csharp
using Millrace.Components;
using Millrace.Configuration;
using Millrace.Control.Catalogue;
using Millrace.Core;
using Millrace.Core.Catalogue;
using Millrace.Core.Faults;
using Millrace.Io;

namespace Millrace.Scenarios.Tests;

/// <summary>
/// R77, closed: a recording of a run with control blocks holds the external
/// actions only. Replaying them re-runs the blocks, which issue their own writes
/// again, so the replay's event log is the live run's byte for byte.
/// </summary>
public class ControlledRecordAndReplayTests
{
    private static readonly ComponentCatalogue Catalogue =
        new CatalogueBuilder().Add<ComponentsModule>().Add<ControlModule>().Build();

    private static (Simulation Live, ScenarioRecorder Recorder, LoadResult Load, string PlantJson) LiveRun()
    {
        string plantJson = File.ReadAllText(Corpus.PlantPath("conveyor-control.json"));
        LoadResult load = PlantLoader.Load(plantJson, Catalogue);
        Assert.True(load.IsValid, load.ToText());

        Simulation live = load.Builder!.Build();
        var recorder = new ScenarioRecorder();
        live.AttachActionRecorder(recorder);
        live.WriteAt(TimeSpan.FromSeconds(1), "SEQ01.Start", TagValue.Bool(true));
        live.WriteAt(TimeSpan.FromSeconds(2), "SEQ01.Start", TagValue.Bool(false));
        live.InjectFaultAt(
            TimeSpan.FromSeconds(40), "CV001.Motor", "thermal-bias", new FaultArguments(new FaultArgument("amount", 0.8)));
        live.RunFor(TimeSpan.FromSeconds(120));

        return (live, recorder, load, plantJson);
    }

    [Fact]
    public void ARecordingOfAControlledRunHoldsOnlyTheExternalActions()
    {
        (Simulation live, ScenarioRecorder recorder, LoadResult load, _) = LiveRun();

        Assert.Contains("CV001.Start  WRITE  Set to false by INT01.", live.Events.ToText(), StringComparison.Ordinal);
        Assert.Equal(3, recorder.Count);
        Assert.Equal(
            """
            {
              "plant": "conveyor-control.json",
              "seed": 1,
              "startTime": "2026-01-01T06:00:00Z",
              "timeStepMs": 10,
              "duration": 120,
              "timeline": [
                {
                  "at": 1,
                  "write": "SEQ01.Start",
                  "value": true
                },
                {
                  "at": 2,
                  "write": "SEQ01.Start",
                  "value": false
                },
                {
                  "at": 40,
                  "fault": "CV001.Motor",
                  "id": "thermal-bias",
                  "args": {
                    "amount": 0.8
                  }
                }
              ]
            }

            """.ReplaceLineEndings("\n"),
            ScenarioJson.Write(recorder.ToScenario("conveyor-control.json", load.Options!, TimeSpan.FromSeconds(120))));
    }

    [Fact]
    public void TheReplayOfAControlledRunIsByteIdenticalToIt()
    {
        (Simulation live, ScenarioRecorder recorder, LoadResult load, string plantJson) = LiveRun();

        Scenario recorded = recorder.ToScenario("conveyor-control.json", load.Options!, TimeSpan.FromSeconds(120));
        ScenarioParseResult parsed = ScenarioLoader.Parse(ScenarioJson.Write(recorded));
        Assert.Empty(parsed.Diagnostics);

        ScenarioRunResult replay = ScenarioRunner.Run(parsed.Scenario!, plantJson, Catalogue);

        Assert.True(replay.IsValid, replay.ToText());
        Assert.Equal(live.Events.ToText(), replay.Events!.ToText());
        Assert.Equal(12000L, replay.Summary!.Ticks);
        Assert.Equal(live.Events.Records.Count, replay.Summary.Events);
    }
}
```

- [ ] **Step 3: Run the tests**

Run: `dotnet test tests/Millrace.Scenarios.Tests --nologo --filter FullyQualifiedName~ControlledRecordAndReplayTests`
Expected: PASS, 2 tests. Before Task 1 the recording would have held 13 actions
(the ten block writes too) and the replay would have applied every block write
twice; report `recorder.Count` as measured.

- [ ] **Step 4: Run everything**

Run: `dotnet build Millrace.sln -c Release --nologo` — expect `0 Warning(s)`, `0 Error(s)`.
Run: `dotnet test Millrace.sln --nologo`
Expected: PASS, **1241** tests (Scenarios 163). The four 5b goldens are
unchanged: `git status --short tests/Millrace.Scenarios.Tests/Golden` prints nothing.

- [ ] **Step 5: Commit**

```bash
git add tests/Millrace.Scenarios.Tests/Millrace.Scenarios.Tests.csproj tests/Millrace.Scenarios.Tests/ControlledRecordAndReplayTests.cs
```

```bash
git commit -m "$(cat <<'MSG'
test(scenarios): record a controlled run and replay it byte for byte

The recording of the worked example holds the three external actions
and none of the blocks' writes; the replay re-runs the blocks and
reproduces the live event log exactly.

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
MSG
)"
```

---

### Task 15: The CLI — default catalogue, `validate`, `tags`, `run --expect`, a plugin block

Model: sonnet

Spec 6. The default catalogue becomes `ComponentsModule` + `ControlModule`;
`validate` prints `controllers   N` after the `tags` line and its JSON summary
gains `"controllers"` after `"explicitTags"` (R95), always present; `tags` and
`run` change in no code but are proven on the controlled plant; the sample
plugin registers a block type, seen by `catalog export`, `schema export` and
`validate`.

**Files:**
- Modify: `src/Millrace.Cli/Millrace.Cli.csproj`, `src/Millrace.Cli/CliApp.cs`, `src/Millrace.Cli/CommandTable.cs`,
  `src/Millrace.Cli/Commands/Validate.cs`
- Create: `tests/Millrace.Cli.Tests.SampleModule/Latch.cs`
- Modify: `tests/Millrace.Cli.Tests.SampleModule/SampleCatalogueModule.cs`
- Modify: `tests/Millrace.Cli.Tests/Millrace.Cli.Tests.csproj`, `tests/Millrace.Cli.Tests/Cli.cs`
- Create: `tests/Millrace.Cli.Tests/Plants/sample-block.json`, `tests/Millrace.Cli.Tests/Scenarios/conveyor-control.json`
- Test: `tests/Millrace.Cli.Tests/ValidateCommandTests.cs`, `TagsCommandTests.cs`,
  `RunCommandTests.cs`, `PluginTests.cs`, `ExportCommandTests.cs`

**Interfaces:**
- Consumes: `ControlModule` (Tasks 6–8); `PlantSummary.Controllers` (Task 11);
  `valid/conveyor-control.json` (Task 12); the regenerated
  `tests/Millrace.Control.Tests/Golden/conveyor-control.log` (Task 1);
  `BlockDescriptor`, `Param.Tag`, `CatalogueBuilder.AddBlock`.
- Produces: `Cli.Golden(string name)` (test helper); the plugin block type
  `latch` (module `Sample`) with parameters `set`, `reset` (Bool tags) and owned
  output `Q`.

- [ ] **Step 1: Write the failing tests**

In `tests/Millrace.Cli.Tests/Cli.cs`, add after `Scenario`:

```csharp
    public static string Golden(string name) => Path.Combine(AppContext.BaseDirectory, "Golden", name);
```

In `tests/Millrace.Cli.Tests/Millrace.Cli.Tests.csproj`, add to the item group that holds
the `Plants` and `Scenarios` items:

```xml
    <None Include="..\Millrace.Configuration.Tests\Plants\valid\conveyor-control.json"
          Link="Plants\conveyor-control.json" CopyToOutputDirectory="PreserveNewest" />
    <None Include="..\Millrace.Control.Tests\Golden\conveyor-control.log"
          Link="Golden\conveyor-control.log" CopyToOutputDirectory="PreserveNewest" />
```

Create `tests/Millrace.Cli.Tests/Scenarios/conveyor-control.json`:

```json
{
  "plant": "../Plants/conveyor-control.json",
  "duration": 120,
  "timeline": [
    { "at": 1,  "write": "SEQ01.Start", "value": true },
    { "at": 2,  "write": "SEQ01.Start", "value": false },
    { "at": 40, "fault": "CV001.Motor", "id": "thermal-bias", "args": { "amount": 0.8 } }
  ]
}
```

Create `tests/Millrace.Cli.Tests/Plants/sample-block.json`:

```json
{
  "materials": [ { "name": "ore", "kind": "bulk" } ],
  "components": [
    { "id": "FEED", "type": "bulk-source", "parameters": { "material": "ore", "rateKgPerS": 20 } },
    { "id": "CHUTE", "type": "transfer-chute", "parameters": { "capacityKg": 200 } },
    { "id": "PILE", "type": "bulk-sink" }
  ],
  "flows": [ { "from": "FEED.Out", "to": "CHUTE.In" }, { "from": "CHUTE.Out", "to": "PILE.In" } ],
  "controllers": [
    { "id": "LATCH01", "type": "latch", "scanPeriodMs": 100,
      "parameters": { "set": "CHUTE.Full", "reset": "FEED.Enabled" } }
  ]
}
```

In `tests/Millrace.Cli.Tests/ValidateCommandTests.cs`: in `AValidPlantPrintsASummary`
add `Assert.Matches(@"controllers\s+0\n", run.Out);` after the `flow links`
assertion; in `JsonFormatOfAValidPlantCarriesTheSummary` add
`Assert.Equal(0, summary.GetProperty("controllers").GetInt32());` after the
`flowLinks` assertion; and add these facts:

```csharp
    [Fact]
    public void AControlledPlantCountsItsControllers()
    {
        CliRun run = Cli.Run("validate", Cli.Plant("conveyor-control.json"));

        Assert.Equal(ExitCodes.Ok, run.ExitCode);
        Assert.Empty(run.Err);
        Assert.Matches(@"components\s+4\n", run.Out);
        Assert.Matches(@"  tags          47 \(0 explicit\)\n  controllers   4\n  time step     10 ms\n", run.Out);
    }

    [Fact]
    public void TheJsonSummaryCountsControllersAfterExplicitTags()
    {
        CliRun run = Cli.Run("validate", Cli.Plant("conveyor-control.json"), "--format", "json");

        Assert.Equal(ExitCodes.Ok, run.ExitCode);
        using JsonDocument document = JsonDocument.Parse(run.Out);
        JsonElement summary = document.RootElement.GetProperty("summary");
        Assert.Equal(
            ["components", "leaves", "signalLinks", "flowLinks", "tags", "explicitTags", "controllers", "timeStepMs"],
            summary.EnumerateObject().Select(p => p.Name));
        Assert.Equal(4, summary.GetProperty("controllers").GetInt32());
        Assert.Equal(47, summary.GetProperty("tags").GetInt32());
    }
```

In `tests/Millrace.Cli.Tests/TagsCommandTests.cs`, add:

```csharp
    [Fact]
    public void AControlledPlantListsItsBlocksTags()
    {
        CliRun run = Cli.Run("tags", Cli.Plant("conveyor-control.json"));

        Assert.Equal(ExitCodes.Ok, run.ExitCode);
        Assert.Empty(run.Err);
        Assert.Contains("INT01.Reset  Bool  ReadWrite", run.Out, StringComparison.Ordinal);
        Assert.Contains("CUR01.HiHi.Active  Bool  ReadOnly", run.Out, StringComparison.Ordinal);
        Assert.Contains("SEQ01.StepTime  Double  ReadOnly  s", run.Out, StringComparison.Ordinal);
        Assert.Equal(47, run.Out.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
    }
```

In `tests/Millrace.Cli.Tests/RunCommandTests.cs`, add:

```csharp
    [Fact]
    public void TheWorkedExampleRunsFromItsFilesAndMatchesTheControlGolden()
    {
        string golden = Cli.Golden("conveyor-control.log");

        CliRun run = Cli.Run("run", Cli.Scenario("conveyor-control.json"), "--expect", golden);

        Assert.Equal(ExitCodes.Ok, run.ExitCode);
        Assert.Empty(run.Err);
        Assert.Equal($"Matched {golden} (40 events).\n", run.Out);
    }
```

In `tests/Millrace.Cli.Tests/PluginTests.cs`: in
`APluginsTypesAppearInTheCatalogueExport`, replace the modules assertion with

```csharp
        Assert.Equal(["Millrace.Components", "Millrace.Control", "Sample"], document.RootElement.GetProperty("modules").EnumerateArray().Select(m => m.GetString()));
```

in `ThePluginPassesCatalogueConformance`, replace the fixtures argument and add
two assertions at the end:

```csharp
        ConformanceReport report = CatalogueConformance.Check(
            builder.Build(),
            new ConformanceFixtures()
                .Parameters("hysteresis-switch", """{ "onAbove": 80, "offBelow": 60 }""")
                .BlockParameters("latch", """{ "set": "X.Set", "reset": "X.Reset" }"""));
```

```csharp
        Assert.DoesNotContain(report.Mismatches, m => m.StartsWith("latch", StringComparison.Ordinal));
        Assert.Contains(report.BuiltTypes, t => t.Name == "Latch");
```

and add these facts:

```csharp
    [Fact]
    public void APluginsBlockTypeAppearsInTheCatalogueExport()
    {
        CliRun run = Cli.Run("catalog", "export", "--assembly", Sample);

        Assert.Equal(ExitCodes.Ok, run.ExitCode);
        using JsonDocument document = JsonDocument.Parse(run.Out);
        JsonElement latch = document.RootElement.GetProperty("blocks").EnumerateArray().Single(b => b.GetProperty("type").GetString() == "latch");
        Assert.Equal("Sample", latch.GetProperty("module").GetString());
        Assert.Equal(["set", "reset"], latch.GetProperty("parameters").EnumerateArray().Select(p => p.GetProperty("name").GetString()));
    }

    [Fact]
    public void APluginsBlockTypeAppearsInTheSchema()
    {
        CliRun run = Cli.Run("schema", "export", "--assembly", Sample);

        Assert.Equal(ExitCodes.Ok, run.ExitCode);
        using JsonDocument document = JsonDocument.Parse(run.Out);
        Assert.True(document.RootElement.GetProperty("$defs").TryGetProperty("block.latch", out _));
        Assert.Contains(
            document.RootElement.GetProperty("properties").GetProperty("controllers").GetProperty("items").GetProperty("oneOf").EnumerateArray(),
            b => b.GetProperty("$ref").GetString() == "#/$defs/block.latch");
    }

    [Fact]
    public void APlantUsingAPluginBlockValidatesWithItAndNotWithout()
    {
        CliRun with = Cli.Run("validate", Cli.Plant("sample-block.json"), "--assembly", Sample);
        CliRun without = Cli.Run("validate", Cli.Plant("sample-block.json"));

        Assert.Equal(ExitCodes.Ok, with.ExitCode);
        Assert.Matches(@"controllers\s+1\n", with.Out);
        Assert.Equal(ExitCodes.PlantInvalid, without.ExitCode);
        Assert.Contains("MR102 $.controllers[0].type", without.Err, StringComparison.Ordinal);
        Assert.Contains("--assembly", without.Err, StringComparison.Ordinal);
    }
```

In `tests/Millrace.Cli.Tests/ExportCommandTests.cs`, add `using Millrace.Control.Catalogue;`
and replace `Shipped` with:

```csharp
    private static readonly ComponentCatalogue Shipped =
        new CatalogueBuilder().Add<ComponentsModule>().Add<ControlModule>().Build();
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Millrace.Cli.Tests --nologo`
Expected: the build succeeds — `ControlModule` already reaches the test project
through `Millrace.Cli` → `Millrace.Configuration` (Task 10) — and these fail:
`ExportCommandTests.CatalogExportPrintsExactlyTheLibrarysExport` and
`SchemaExportPrintsExactlyTheLibrarysSchema` (the CLI still ships components
only), the four validate tests that look for `controllers`, the tags and run
tests on `conveyor-control.json` (`MR102`: the CLI does not know `permissive`),
`APluginsTypesAppearInTheCatalogueExport` (module list) and the three plugin
block tests (`latch` is not registered yet).

- [ ] **Step 3: Add the plugin block**

Create `tests/Millrace.Cli.Tests.SampleModule/Latch.cs`:

```csharp
using Millrace.Core.Catalogue;
using Millrace.Io;

namespace Millrace.Cli.Tests.SampleModule;

/// <summary>
/// A set/reset latch, reset dominant: the worked example of a block type a
/// plugin registers, in docs/authoring-a-component.md. <c>Q</c> goes true while
/// <c>set</c> is true, false while <c>reset</c> is true, and holds in between.
/// </summary>
public sealed class Latch : IScanBlock
{
    public static BlockDescriptor Descriptor { get; } = new(
        "latch",
        "A set/reset latch, reset dominant: Q follows set until reset clears it.",
        (id, p) => [(new TagSpec($"{id}.Q", TagKind.Bool, string.Empty, "Latched"), TagAccess.ReadOnly)],
        (id, period, p) => new Latch(id, p.Tag("set"), p.Tag("reset"), period))
    {
        Parameters =
        [
            Param.Tag("set", "Sets the latch while true.", TagKind.Bool),
            Param.Tag("reset", "Clears the latch while true; wins over set.", TagKind.Bool),
        ],
    };

    private bool _q;

    public Latch(string id, string set, string reset, TimeSpan scanPeriod)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        Id = id;
        ScanPeriod = scanPeriod;
        Inputs = [new TagRef(set, TagKind.Bool), new TagRef(reset, TagKind.Bool)];
    }

    public string Id { get; }

    public TimeSpan ScanPeriod { get; }

    public IReadOnlyList<TagRef> Inputs { get; }

    public IReadOnlyList<TagRef> Writes { get; } = [];

    public IReadOnlyList<TagSpec> Outputs { get; } = [new TagSpec("Q", TagKind.Bool, string.Empty, "Latched")];

    public IReadOnlyList<TagSpec> Commands { get; } = [];

    public void Scan(in ScanInputs inputs, ref ScanOutputs outputs)
    {
        if (inputs.Input(1).AsBool)
        {
            _q = false;
        }
        else if (inputs.Input(0).AsBool)
        {
            _q = true;
        }

        outputs.Set(0, TagValue.Bool(_q));
    }
}
```

In `tests/Millrace.Cli.Tests.SampleModule/SampleCatalogueModule.cs`, add
`builder.AddBlock(Latch.Descriptor);` after `builder.Add(HysteresisSwitch.Descriptor);`.

- [ ] **Step 4: Ship the control blocks in the CLI**

In `src/Millrace.Cli/Millrace.Cli.csproj`, add to the project references:

```xml
    <ProjectReference Include="..\Millrace.Control.Catalogue\Millrace.Control.Catalogue.csproj" />
```

In `src/Millrace.Cli/CliApp.cs`, add `using Millrace.Control.Catalogue;` and replace the
builder line with:

```csharp
        CatalogueBuilder builder = new CatalogueBuilder().Add<ComponentsModule>().Add<ControlModule>();
```

In `src/Millrace.Cli/CommandTable.cs`, replace the `Assembly` option's help with
`"Load catalogue modules from this assembly, in addition to the shipped components and control blocks."`
and the `catalog export` summary with
`"Print every component, block, transform, transition, hold and material type as JSON."`.

In `src/Millrace.Cli/Commands/Validate.cs`, in the JSON branch add
`w.WriteNumber("controllers", summary.Controllers);` after the `explicitTags`
line, and replace the text block with:

```csharp
        context.Out.Write(string.Create(CultureInfo.InvariantCulture, $"""
            OK  {path}
              components    {summary.Components}
              leaves        {leaves}
              signal links  {summary.SignalLinks}
              flow links    {summary.FlowLinks}
              tags          {tags} ({summary.ExplicitTags} explicit)
              controllers   {summary.Controllers}
              time step     {stepMs} ms

            """).ReplaceLineEndings("\n"));
```

- [ ] **Step 5: Run the CLI tests**

Run: `dotnet test tests/Millrace.Cli.Tests --nologo`
Expected: PASS, **76** tests. `TheWorkedExampleRunsFromItsFilesAndMatchesTheControlGolden`
proves criterion 2 through the shipped binary's code path: a scenario file
writes the block command `SEQ01.Start` (resolved by `MR206` against the built
plant) and the log matches the 5c golden. If it fails, read the `.actual` file
it leaves beside the linked golden in the test output directory, report the
first differing line, and fix the scenario — never the golden.

- [ ] **Step 6: Check the shipped binary by hand**

```bash
dotnet run --project src/Millrace.Cli -c Release -- validate tests/Millrace.Configuration.Tests/Plants/valid/conveyor-control.json
```

Expected: exit 0 and the summary with `controllers   4`.

```bash
dotnet run --project src/Millrace.Cli -c Release -- validate tests/Millrace.Configuration.Tests/Plants/invalid/MR113-unknown-tag.json
```

Expected: exit 1 and `MR113 $.controllers[0].parameters.conditions[0].tag`
with `Fix: Use a tag the plant has — 'CHUTE.Full' is closest.` Paste both outputs
into the task report.

- [ ] **Step 7: Run everything**

Run: `dotnet build Millrace.sln -c Release --nologo` — expect `0 Warning(s)`, `0 Error(s)`.
Run: `dotnet test Millrace.sln --nologo`
Expected: PASS, **1248** tests (Cli 76).

- [ ] **Step 8: Commit**

```bash
git add src/Millrace.Cli/Millrace.Cli.csproj src/Millrace.Cli/CliApp.cs src/Millrace.Cli/CommandTable.cs src/Millrace.Cli/Commands/Validate.cs tests/Millrace.Cli.Tests.SampleModule/Latch.cs tests/Millrace.Cli.Tests.SampleModule/SampleCatalogueModule.cs tests/Millrace.Cli.Tests/Millrace.Cli.Tests.csproj tests/Millrace.Cli.Tests/Cli.cs tests/Millrace.Cli.Tests/Plants/sample-block.json tests/Millrace.Cli.Tests/Scenarios/conveyor-control.json tests/Millrace.Cli.Tests/ValidateCommandTests.cs tests/Millrace.Cli.Tests/TagsCommandTests.cs tests/Millrace.Cli.Tests/RunCommandTests.cs tests/Millrace.Cli.Tests/PluginTests.cs tests/Millrace.Cli.Tests/ExportCommandTests.cs
```

```bash
git commit -m "$(cat <<'MSG'
feat(cli): ship the control blocks and count a plant's controllers

The default catalogue carries the five blocks; validate reports the
controllers, tags lists their tags, and run replays the worked example
from files against the 5c golden. A plugin may register a block type.

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
MSG
)"
```

---

### Task 16: Documentation, and the 5a spec amendment

Model: sonnet

Spec 9. Every page says what the code now does: blocks in the plant file, the
file-order rule, write attribution, recordings of external actions only, block
registration for authors, and the `--out` ruling in the 5a spec. A documentation
test pins the control-blocks page.

**Files:**
- Modify: `docs/control-blocks.md`, `docs/authoring-a-component.md`, `docs/scenarios.md`,
  `docs/architecture.md`, `README.md`
- Modify: `docs/superpowers/specs/2026-09-20-catalogue-configuration-cli-design.md` (§4.1)
- Test: `tests/Millrace.Control.Tests/DocumentationTests.cs`
- Verify (already regenerated in Task 11): `docs/configuration-diagnostics.md`

**Interfaces:**
- Consumes: every name the earlier tasks produced; the regenerated 5c golden's
  line 33 (`06:00:40.110  CV001.Start  WRITE  Set to false by INT01.`).
- Produces: no code.

- [ ] **Step 1: Write the failing documentation test**

In `tests/Millrace.Control.Tests/DocumentationTests.cs`, add:

```csharp
    [Fact]
    public void TheControlBlocksPageDescribesThePlantFile()
    {
        string page = Page();

        foreach (string token in new[]
                 {
                     "\"controllers\"", "scanPeriodMs", "presetS", "timeoutS", "delayS", "onDelayS",
                     "MR113", "MR114", "MR115", "Set to false by INT01.", "File order is scan order",
                     "ControlModule", "Millrace.Control.Catalogue",
                 })
        {
            Assert.Contains(token, page, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("cannot attach blocks", page, StringComparison.Ordinal);
    }
```

Run: `dotnet test tests/Millrace.Control.Tests --nologo --filter FullyQualifiedName~DocumentationTests`
Expected: FAIL — `"controllers"` is not on the page.

- [ ] **Step 2: `docs/control-blocks.md`**

In **Attaching a block**, replace the paragraph that begins "Blocks are attached
**in code**." (ending "`millrace run` therefore cannot attach blocks yet.") with:

```markdown
That is the code form. A plant file declares the same block under
`controllers` — see *In the plant file* below — and `millrace validate`, `millrace tags`
and `millrace run` handle it with no C#.
```

At the end of **Attaching a block** (after the paragraph ending "so an interlock
may list `PERM01.Ok` before `PERM01` is added."), insert:

````markdown
### In the plant file

A plant file lists its blocks under `controllers`, a sibling of `components`.
Each entry has the component envelope — `id`, `type`, `parameters` — plus
`scanPeriodMs`, which is required: a PLC task period has no sensible default.

```json
"controllers": [
  { "id": "INT01", "type": "interlock", "scanPeriodMs": 100,
    "parameters": {
      "conditions": [
        { "tag": "CV001.Tripped", "normal": false },
        { "tag": "PERM01.Ok",     "normal": true } ],
      "trip": [ { "tag": "CV001.Start", "value": false } ] } }
]
```

Durations are seconds and end in `S` — `presetS`, `timeoutS`, `delayS`,
`onDelayS` — and are at most a year. Only the scan period is in milliseconds,
like `timeStepMs` and a PLC task: a whole number of time steps, at most a day. A
controller id follows the component id rule — no dot, no whitespace — and is
unique across components and controllers.

A tag parameter is a tag's full name, a plant tag or one a block owns, in any
file order: an interlock may list `PERM01.Ok` before `PERM01` is declared. A
value is `true`, `false` or a number, converted to the kind of the tag it is for:
an integer-valued number fits an Int64 or a Double tag, a fraction only a Double,
and only `true` and `false` fit a Bool. The loader resolves every tag and every
value before it builds any block, and reports every mistake at its path:

| code | check |
|---|---|
| MR113 | The tag exists; the fix names the nearest one. |
| MR114 | The tag is of a kind the block can use, and the value fits the tag. |
| MR115 | A tag the block commands is read-write — not a measured value, another block's output, or an input a signal link drives. |

Block types come from the catalogue: `ControlModule`, in
`Millrace.Control.Catalogue`, registers the five below, and `millrace catalog export`
lists them under `"blocks"`. A module loaded with `--assembly` may register
more.

**File order is scan order.** Blocks due on the same tick scan in the order the
`controllers` array lists them, and when two write one tag on one tick the later
one wins.
````

In **The worked example**, replace the log excerpt's last line
`06:00:40.110  CV001.Start  WRITE  Set to false.` with
`06:00:40.110  CV001.Start  WRITE  Set to false by INT01.`, and add after the
excerpt's closing fence:

```markdown
A write a block issues is logged with its origin — `Set to false by INT01.` —
and every other write keeps the plain `Set to false.`, so the log tells a
block's command from an operator's. The example is also a plant file,
`tests/Millrace.Configuration.Tests/Plants/valid/conveyor-control.json`; with the
scenario `tests/Millrace.Cli.Tests/Scenarios/conveyor-control.json`, `millrace run`
reproduces this golden byte for byte.
```

After the C# example in each block section, add its plant-file form:

In **`Timer`**:

````markdown
In a plant file (`mode` is `on-delay`, `off-delay` or `pulse`):

```json
{ "id": "TMR01", "type": "timer", "scanPeriodMs": 100,
  "parameters": { "mode": "on-delay", "input": "CV001.Running", "presetS": 5 } }
```
````

In **`Permissive`**:

````markdown
In a plant file:

```json
{ "id": "PERM01", "type": "permissive", "scanPeriodMs": 100,
  "parameters": { "conditions": [
    { "tag": "CV001.SafetyOk", "normal": true },
    { "tag": "Pile.Full",      "normal": false } ] } }
```
````

In **`Interlock`** (`trip` defaults to none):

````markdown
In a plant file (`trip` may be left out):

```json
{ "id": "INT01", "type": "interlock", "scanPeriodMs": 100,
  "parameters": {
    "conditions": [ { "tag": "CV001.Tripped", "normal": false }, { "tag": "PERM01.Ok", "normal": true } ],
    "trip": [ { "tag": "CV001.Start", "value": false } ] } }
```
````

In **`Alarm`**:

````markdown
In a plant file (`kind` is `lo-lo`, `lo`, `hi` or `hi-hi`; `deadband` and
`onDelayS` default to 0):

```json
{ "id": "CUR01", "type": "alarm", "scanPeriodMs": 100,
  "parameters": {
    "input": "CV001.Current",
    "limits": [
      { "kind": "hi",    "value": 3.0, "deadband": 0.2, "onDelayS": 0.5 },
      { "kind": "hi-hi", "value": 8.0, "deadband": 0.5, "onDelayS": 0.1 } ] } }
```
````

In **`Sequencer`**:

````markdown
In a plant file (`op` is `==`, `!=`, `<`, `<=`, `>` or `>=`; `writes`, `abort`
and `timeoutS` may be left out):

```json
{ "id": "SEQ01", "type": "sequencer", "scanPeriodMs": 200,
  "parameters": {
    "steps": [
      { "name": "Start the belt",
        "writes": [ { "tag": "CV001.Start", "value": true } ],
        "transition": { "type": "when", "tag": "CV001.Speed", "op": ">=", "value": 1.0 },
        "timeoutS": 15 },
      { "name": "Run the feed",
        "writes": [ { "tag": "Feed.Enabled", "value": true } ],
        "transition": { "type": "after", "delayS": 60 } } ],
    "abort": [ { "tag": "CV001.Start", "value": false } ] } }
```
````

In **What is not here**, replace the paragraph with:

```markdown
Branching sequential function charts; PID; alarm shelving, priorities and a
dedicated `LiveState.Alarms`. All are later plans.
```

- [ ] **Step 3: `docs/authoring-a-component.md`**

Append at the end of the file:

````markdown
## 11. Registering a block

A control block is registered the same way, with a `BlockDescriptor` instead of
a `ComponentDescriptor`. `tests/Millrace.Cli.Tests.SampleModule/Latch.cs` is the
worked example:

```csharp
public static BlockDescriptor Descriptor { get; } = new(
    "latch",
    "A set/reset latch, reset dominant: Q follows set until reset clears it.",
    (id, p) => [(new TagSpec($"{id}.Q", TagKind.Bool, string.Empty, "Latched"), TagAccess.ReadOnly)],
    (id, period, p) => new Latch(id, p.Tag("set"), p.Tag("reset"), period))
{
    Parameters =
    [
        Param.Tag("set", "Sets the latch while true.", TagKind.Bool),
        Param.Tag("reset", "Clears the latch while true; wins over set.", TagKind.Bool),
    ],
};
```

- **`OwnedTags`** returns every tag the block will own, by full name — outputs
  `ReadOnly`, commands `ReadWrite` — as a function of the id and the parameters
  alone. The loader calls it before any block exists, to resolve every tag a
  controller names, so it must not read a value or an object parameter.
- **The factory** takes the id, the scan period and the resolved parameters.
- **`Param.Tag(name, description, kind, writes)`** is a tag name the loader
  resolves: give `kind` when the block needs one, and `writes: true` when the
  block commands the tag. **`Param.Value(name, description, tagParameter)`** is
  a value the loader converts to the kind of its sibling tag parameter. A
  component may not declare either.
- Register it with `builder.AddBlock(Latch.Descriptor)`. Block and component
  types share one namespace.
- Prove it with `new ConformanceFixtures().BlockParameters("latch", """{ … }""")`
  — call it again to check the type with several fixtures. Conformance compares
  `OwnedTags` with the instance's `Outputs` and `Commands` by name, kind and
  access.
````

- [ ] **Step 4: `docs/scenarios.md`**

In **Recording a live run**, replace the first sentence's "the seam every action
that takes effect passes through" with "the seam every external action that
takes effect passes through", and append at the end of the section:

```markdown
A recording holds external actions only. A write a control block issues is not
one: it is logged `Set to … by <block id>.` and never reaches the recorder,
because replaying the external actions re-runs the block, which issues it again.
A recorded run of a plant with `controllers` therefore replays byte for byte.
```

- [ ] **Step 5: `docs/architecture.md`**

In **Catalogue, schema and loader**, after the paragraph that begins "The
**loader** runs six stages", add:

```markdown
A plant's **controllers** are read in the structure stage like components and
resolved in the build stage, after the plant alone has passed `Validate()`. The tag table is
`SimulationBuilder.PlantTags()` — the directory `Build()` would publish, R23
downgrades included — plus every controller's declared owned tags, so every tag
and value is checked (`MR113`–`MR115`) before any block is built; the blocks
are then added in file order and pass `Validate()`'s `MR013`–`MR015` like a
block attached in code. Block descriptors live beside component and object
descriptors in the catalogue; `ControlModule`, in `Millrace.Control.Catalogue`,
registers the five shipped ones.
```

In **Scenarios and replay**, replace the sentence that begins "`IActionRecorder`
watches all three landing sites" with:

```markdown
`IActionRecorder` watches all three landing sites — the queued-write drain,
`ApplyNow` and `FaultEvent.Apply` — for external actions, so a recording
captures actions by where they took effect, not by where they came from. A
control block's write carries its origin: it is logged `by <block id>` and is
not recorded, because a replay re-runs the block.
```

In **The control layer**, replace "Nothing in `TagImage`, `Millrace.Realtime` or the
scenario recorder had to learn what a block is." with "Nothing in `Millrace.Realtime`
or the scenario recorder had to learn what a block is; `TagImage` learned only a
write's origin, so the log attributes a block's write and the recorder skips
it.", and replace the paragraph "Blocks are attached in code. Describing them in
the plant file is a later plan; the API gets a shakedown before it is frozen
into a format." with:

```markdown
Blocks are declared in a plant file's `controllers` section or attached in
code. `Millrace.Control.Catalogue` — which sees `Millrace.Core` and `Millrace.Control` —
registers them in the catalogue through `ControlModule`, so `Millrace.Control` itself
still sees the I/O contract alone.
```

- [ ] **Step 6: `README.md`**

In **Status**, replace "Blocks are attached in code for now; describing them in
the plant file, and the reference samples, are planned." with "Blocks are
declared in a plant file's `controllers` section — `Millrace.Control.Catalogue`
registers them — or attached in code; the reference samples are planned."

In **Command line**, replace the `catalog export` comment with
`# every component, block, transform, hold and material, as JSON`, and after the
scenario example's closing fence add:

````markdown
A plant adds control blocks under `controllers`; a scenario may write their
commands (`SEQ01.Start`, `INT01.Reset`) like any other tag:

```json
"controllers": [
  { "id": "INT01", "type": "interlock", "scanPeriodMs": 100,
    "parameters": {
      "conditions": [ { "tag": "CV001.Tripped", "normal": false } ],
      "trip": [ { "tag": "CV001.Start", "value": false } ] } }
]
```
````

- [ ] **Step 7: Amend the 5a spec's §4.1**

In `docs/superpowers/specs/2026-09-20-catalogue-configuration-cli-design.md`,
§4.1, replace "`--out <file>` (payload to a file);" with "`--out <file>`
(payload to a file — accepted by `catalog export`, `schema export` and `run`
only; `validate` and `tags` write to standard output, amended by plan 5d);", and
replace "On success `validate` prints components, flattened leaves, signal links,
flow links, tags, and the time step used." with "On success `validate` prints
components, flattened leaves, signal links, flow links, tags, controllers (plan
5d), and the time step used."

- [ ] **Step 8: Run the documentation tests and read the pages**

Run: `dotnet test tests/Millrace.Control.Tests --nologo --filter FullyQualifiedName~DocumentationTests`
Expected: PASS, 2 tests.

Run: `dotnet test tests/Millrace.Configuration.Tests --nologo --filter FullyQualifiedName~DiagnosticsReferenceTests`
Expected: PASS — `docs/configuration-diagnostics.md` is still Task 11's.

Read `docs/control-blocks.md` from **Attaching a block** to the end and check
every JSON snippet against `tests/Millrace.Configuration.Tests/Plants/valid/conveyor-control.json`
and the descriptors: every key is a parameter the descriptor declares, every
duration ends in `S`. Report any snippet you corrected.

- [ ] **Step 9: Run everything and check the constraints**

Run: `dotnet build Millrace.sln -c Release --nologo` — expect `0 Warning(s)`, `0 Error(s)`.
Run: `dotnet test Millrace.sln --nologo`
Expected: PASS, **1249** tests — 37 Io.Abstractions / 460 Core / 126 Components /
56 Realtime / 204 Configuration / 163 Scenarios / 76 Cli / 105 Control / 22
Control.Catalogue.

Run: `grep -rn "PackageReference" src/` — expect no output.
Run: `grep -n "ProjectReference" src/Millrace.Control/Millrace.Control.csproj` — expect one
line, `Millrace.Io.Abstractions`.
Run: `git status --short tests/Millrace.Scenarios.Tests/Golden` — expect no output
(the four 5b goldens are untouched).

- [ ] **Step 10: Commit**

```bash
git add docs/control-blocks.md docs/authoring-a-component.md docs/scenarios.md docs/architecture.md README.md docs/superpowers/specs/2026-09-20-catalogue-configuration-cli-design.md tests/Millrace.Control.Tests/DocumentationTests.cs
```

```bash
git commit -m "$(cat <<'MSG'
docs: describe controllers in the plant file

The control-blocks page gains the plant-file form of every block, the
file-order rule and write attribution; authors learn to register a
block; recordings are external actions only; the 5a spec records that
validate and tags write to standard output.

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
MSG
)"
```

---

## Spec coverage

| Spec section | Requirement | Task |
|---|---|---|
| 1, criterion 1 | every block declarable; `validate`, `tags`, `run` with no C# | 6–8, 11, 15 |
| 1, criterion 2 | JSON-built and code-built worked example, byte-identical logs | 13 (tests), 15 (CLI `run --expect`) |
| 1, criterion 3 | a recording holds only external actions and replays byte for byte | 1, 14 |
| 1, criterion 4 | 5b goldens unchanged; 5c golden changes only by ` by <id>`; `validate` only by `controllers`; export goldens only by additions | 1, 9, 10, 15 (measured diffs; the schema golden's module-list `description` line is the one rewritten pre-existing line, R101) |
| 1, criterion 5 | `Millrace.Control` references only `Millrace.Io.Abstractions`; no package under `src/` | 6, 16 (checks) |
| 1, criterion 6 | Release build 0 warnings; every existing test passes | every task; R98 lists the eleven changed tests |
| 2 | project layout, new projects | 6 (Control.Catalogue + tests), 10 (Configuration reference), 15 (CLI reference) |
| 3.1 | `BlockDescriptor`, `AddBlock`, `Blocks`/`TryGetBlock`/`ModuleOf`, shared namespace | 3 (R91) |
| 3.2 | `Tag` / `Value` kinds, conversion table | 4 (every cell a row; R83, R84) |
| 3.3 | `condition`, `write`, `transition` | 6 (R88) |
| 3.4 | the five blocks, owned tags, constructor checks as `MR111` | 7, 8, 11 (R89) |
| 3.5 | conformance for blocks (owned tags incl. unit and description, an undeclared parameter read); several fixtures; export `blocks` | 5 (R102), 8, 9 (R90) |
| 4.1 | the `controllers` section (including an empty one) | 11 (R87) |
| 4.2 | the stages gain a controllers pass | 11 (R80: build stage, after a first `Validate()`) |
| 4.3 | file order is scan order | 11 (`ControllerPass` adds in file order), 16 (docs) |
| 4.4 | resolve tags and values before any block; `PlantTags` query | 2 (R81), 4, 11 (R82, R85) |
| 4.5 | `MR113`–`MR115`; diagnostics page regenerated | 11 (R96) |
| 4.6 | schema: `oneOf` per block type, `$defs`, `Tag`/`Value`; agreement | 10 (R94), 12 |
| 5 | the write origin; `IActionRecorder` narrowed | 1 (R99) |
| 6 | CLI default catalogue, `validate` count, help line, plugin blocks | 15 (R95) |
| 7 | worked example plant file, round trip, CLI golden, 5c golden diff measured | 1, 12, 13, 15 (R93) |
| 8 | the test list | 1–15 (placement noted per task) |
| 9 | documentation, 5a §4.1 amendment | 16 |
| 10 | JsonSchema.Net pin; `--out` ruling; `MR112` taken | Global Constraints, 16, 11 |

## Test-count arithmetic

Measured on `834758b` with `dotnet test Millrace.sln`: **1108**.

| Project | Before | Task deltas | After |
|---|---|---|---|
| Millrace.Io.Abstractions | 37 | — | 37 |
| Millrace.Core | 420 | T1 +2, T2 +3, T3 +5, T4 +22 (10 facts + 12 rows), T5 +7, T9 +1 | 460 |
| Millrace.Components | 126 | — (T9 regenerates its golden) | 126 |
| Millrace.Realtime | 56 | — | 56 |
| Millrace.Configuration | 137 | T10 +5, T11 +38 (30 `ControllerTests` + 4 fixtures × 2 theories), T12 +24 (9 fixtures × 2 theories + 6 agreement rows) | 204 |
| Millrace.Scenarios | 161 | T14 +2 | 163 |
| Millrace.Cli | 69 | T15 +7 (2 validate, 1 tags, 1 run, 3 plugin) | 76 |
| Millrace.Control | 102 | T1 ±0 (one test renamed), T13 +2, T16 +1 | 105 |
| Millrace.Control.Catalogue (new) | 0 | T6 +9, T7 +6, T8 +5, T9 +2 | 22 |
| **Total** | **1108** | +141 | **1249** |

Running totals after each task: 1110, 1113, 1118, 1140, 1147, 1156, 1162, 1167,
1170, 1175, 1213, 1237, 1239, 1241, 1248, 1249.

## Self-review

**Spec coverage.** Every section maps to a task above. Where a ruling departs
from the spec as first approved (R80, R83, R86–R88, R91, R92, R96, R100–R102),
the spec has been amended in place to agree, with a note under its header.

**Placeholder scan.** Every code step carries its code; every golden step names
the command, the file to read and a checklist; no step says "similar to".

**Type consistency.** Names used across tasks: `BlockDescriptor(type,
description, ownedTags, factory)` with `OwnedTags: Func<string, ParameterValues,
IReadOnlyList<(TagSpec Spec, TagAccess Access)>>` and `Factory: Func<string,
TimeSpan, ParameterValues, IScanBlock>` (T3; used T5, T6–T9, T11, T15);
`CatalogueBuilder.AddBlock` (T3); `ComponentCatalogue.Blocks/TryGetBlock/ModuleOf`
(T3; used T5, T9, T10, T11); `Param.Tag(name, description, TagKind? kind = null,
bool writes = false)`, `Param.Value(name, description, tagParameter)`,
`ParameterValues.Tag/Value`, `BindingContext.UseTags/TryGetTag/TagNames/ResolvesTags`,
`BindingIssueKind.UnknownTag/WrongTagKind/ReadOnlyTag` (T4; used T6–T11);
`ConformanceFixtures.BlockParameters`, `CatalogueConformance.ProbePeriod` (T5);
`ControlCatalogue.ConditionGroup/WriteGroup/TransitionSlot/MaxSeconds` (T6);
`SimulationBuilder.PlantTags()` (T2; used T11); `TagImage.Write(int, TagValue,
string)` and `ApplyNow(int, TagValue, string?, in TickContext)` (T1);
`ConfigDiagnostics.UnknownTag/WrongKind/ReadOnlyTag` (T11);
`PlantSummary.Controllers` (T11; used T15); `ControllerEntry`, `ControllerPass`
(T11).

**Review Focus.** Each of the five lines has its test in the owning task
(Tasks 2, 4, 8 and 11), named in the section.
