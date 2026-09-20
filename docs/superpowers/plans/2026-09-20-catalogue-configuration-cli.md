# Catalogue, Configuration and CLI Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the engine discoverable and drivable without writing C#: a
catalogue in which every component, transform and hold condition describes
itself and can be built from parsed parameters; a JSON plant file with a loader
whose every error names its fix; a JSON Schema generated from the catalogue; and
a `dse` command line with `catalog export`, `schema export`, `validate` and
`tags`, extensible by `--assembly`.

**Architecture:** `Dse.Core/Catalogue` holds the model — `ComponentDescriptor`,
`ObjectDescriptor` (transforms and holds, by slot), `MaterialDescriptor`, a
parameter tree, and a `ParameterBinder` that turns a `JsonElement` into
`ParameterValues` while collecting issues. Each component type carries a
hand-written `static Descriptor` beside its constructor; `ComponentsModule`
registers them; `Dse.Core.Testing.CatalogueConformance` builds every type and
compares descriptor with instance so the two cannot drift. `Dse.Configuration`
is a six-stage loader (parse, structure, references, instantiate, wire, build)
producing `DSE1xx` diagnostics with JSON paths and fixes, plus a deterministic
draft 2020-12 schema generator. `Dse.Cli` is a dependency-free shell around
both, testable in-process through `CliApp.Run`.

**Tech Stack:** .NET 10 (`net10.0`), C#, `System.Text.Json`, xUnit. No external
runtime dependencies. One test-only package: JsonSchema.Net.

**Spec:** `docs/superpowers/specs/2026-09-20-catalogue-configuration-cli-design.md`
(all of it), which refines sections 4, 14, 16 and 17 of
`docs/superpowers/specs/2026-09-02-industrial-process-simulation-engine-design.md`.

**Plan sequence:** This is plan 5a. Plans 1–4 are merged on `master` at
`f31d6d5` (471 tests: 23 Io.Abstractions, 284 Core, 108 Components, 56
Realtime). Plan 5b adds `Dse.Control`, `Dse.Scenarios` and `dse run`; plan 6 the
two reference samples. Nothing in this plan may reference those subsystems: no
scenario, no controller, no alarm. `EventLog.ToText()` is used as it stands and
is not changed.

## Global Constraints

- Target framework `net10.0` for every project. `Dse.Configuration` references
  `Dse.Core` and `Dse.Components`. `Dse.Cli` references `Dse.Core`,
  `Dse.Components` and `Dse.Configuration`. `Dse.Core` still references only
  `Dse.Io.Abstractions`. **Zero external runtime package references** in any
  shipping project. Test projects use the same test package versions as
  `tests/Dse.Core.Tests/Dse.Core.Tests.csproj`; `Dse.Configuration.Tests` adds
  `JsonSchema.Net` and nothing else.
- `Nullable` enabled, `TreatWarningsAsErrors` true, `GenerateDocumentationFile`
  true (a `<see cref>` to a type that does not exist yet is a **build error**;
  reference only types that already exist when the file is compiled),
  deterministic builds. These come from `Directory.Build.props`; a csproj
  repeats only `TargetFramework`, `ImplicitUsings` and `Nullable`.
- **Never use `System.Random`. Never use `string.GetHashCode()`** for anything
  that affects behaviour. **Never let a `Dictionary` or `HashSet` iteration
  order reach an output**: every exported list is sorted with
  `StringComparer.Ordinal` first.
- All formatting and parsing uses `CultureInfo.InvariantCulture`. Messages that
  embed a number use `string.Create(CultureInfo.InvariantCulture, $"...")`.
- **JSON output is deterministic**: `Utf8JsonWriter` with `Indented = true`,
  properties written in the order the code writes them, arrays pre-sorted, and
  the final text normalised to `\n` line endings with one trailing `\n`.
- **Names.** Catalogue type names are kebab-case (`belt-scale`). JSON parameter
  names are camelCase (`ratedPowerW`) and match ordinally. Port, alias, tag and
  fault names are exactly what the code declares today (`Start`, `Channel1`,
  `bearing-friction`); in a plant file a port name matches ignoring case, a
  component id matches ordinally.
- **The instance is the truth.** Where a descriptor written in this plan
  disagrees with the component it describes (a port name, a tag's access, a
  telemetry key), the conformance test will say so. Fix the descriptor to match
  the component, never the component to match the descriptor, and **report the
  mismatch** in the task report. Likewise for any expected value stated in a
  test here: report the measurement, never widen a window.
- Diagnostic messages are human sentences ending in a full stop. A `Fix` is an
  imperative sentence ending in a full stop. Descriptions in descriptors are
  sentences ending in a full stop; tag descriptions stay fragments (plan 4).
- xUnit analyzers run under warnings-as-errors: prefer `Assert.Single`,
  `Assert.Contains`, `Assert.Empty` over `Assert.True(x.Any())` and
  `Assert.Equal(1, x.Count())`.
- Licence: MIT. Namespaces: `Dse.Core.Catalogue`, `Dse.Core.Testing`,
  `Dse.Configuration`, `Dse.Cli`.
- Commit trailers: every commit message ends with the attribution line(s) the
  session specifies, copied verbatim, in the body, never on the subject line:
  subject, blank line, then the line(s). The trailer identifies the session,
  not the model that happens to be typing.
- Build and test commands, from the repository root:
  `dotnet build Dse.sln -c Release --nologo` (expect `0 Warning(s)`, `0
  Error(s)`) and `dotnet test Dse.sln --nologo`.

## Decisions settled here (carry forward as rulings R33–R49)

These refine the spec where writing real code against the real APIs forced a
choice. Where one differs from the spec's wording, this plan wins and says why.

- **R33 — a factory is `(string id, ParameterValues p) → ISimNode`.** The spec
  gave it a third `IReferenceResolver` argument. The binder resolves references,
  materials, state names and nested objects *before* the factory runs and stores
  the results in `ParameterValues`, so the factory reads `p.Reference<IMaterialObservable>("belt")`
  and never sees a resolver. One mechanism instead of two.
- **R34 — an object that needs a material state carries its own `material`
  parameter.** The spec let a transform or hold inherit "the enclosing
  component's output material". A transform on a belt has no enclosing material
  — a belt carries whatever arrives — so the inheritance rule cannot be uniform.
  `residence-accumulator` and `state-at-least` take `material` and `state`
  side by side. More verbose, never ambiguous.
- **R35 — no materials ship.** `Dse.Components` defines no `MaterialType` today,
  so `ComponentsModule` registers none and plants define their own under
  `materials`. `MaterialDescriptor` and its catalogue slot exist for third-party
  modules and are exercised by the CLI sample module.
- **R36 — the loader resolves ports against live instances, not descriptors.**
  A descriptor's port list is documentation and conformance input. Ports whose
  number or names depend on parameters (`Channel{n}` on the safety relay,
  `PullKey{n}` on the conveyor, one inlet per recipe line on the bulk process
  unit) are declared once with a `PortRepeat`.
- **R37 — Core gains five small public seams**, because the loader wires without
  knowing `T`: `PortConnector.TryConnect(Port, Port, out string)`;
  `CompositeComponent.ExposedPorts` and `.LeafComponents`;
  `TagBinding.ForPort(...)`; and `ICapabilityProvider`, whose method takes a
  `Type` (the spec's generic form is an extension method over it).
- **R38 — a descriptor lists what it `Provides`.** `belt-scale.belt` needs an
  `IMaterialObservable`; the loader checks that statically from `Provides` so
  `DSE105` can list every component in the plant that *would* satisfy it.
  Conformance verifies `Provides` against the instance.
- **R39 — the descriptor sweep has a shrinking `Pending` list.** "Every concrete
  node has a descriptor" cannot be green until the last rollout task, and no
  commit may leave a red test. The sweep exempts types named in a `Pending`
  array; every rollout task deletes its types from it; Task 8 deletes the array.
  The test also fails if `Pending` names a type that *has* a descriptor, so the
  list cannot rot.
- **R40 — Core validation errors pass through with their text split.** Every
  `DSE001`–`DSE011` message is already "symptom. fix." The loader puts the first
  sentence in `Message` and the rest in `Fix`, with the JSON path of the first
  involved top-level component.
- **R41 — "unlimited" is an omitted parameter.** JSON has no infinity.
  `BulkSink.capacityKg`, `BulkSource.hopperCapacityKg` and the queue capacities
  are optional parameters with no default; the factory passes the constructor's
  own default when they are absent, and the description says "Omit for
  unlimited."
- **R42 — golden files update on request.** `Golden.Assert(relativePath, actual)`
  compares with a committed file located from `[CallerFilePath]`. With the
  environment variable `DSE_UPDATE_GOLDEN=1` it writes the file instead. A
  golden file is generated by running the test once with the variable set,
  **read by the implementer**, and committed.
- **R43 — `--time-step` and `defaults.timeStepMs` are milliseconds as a JSON
  number**, fractional allowed (`0.5`). `defaults.seed` is a non-negative
  integer. `defaults.startTime` is an ISO 8601 string with an offset.

- **R45 — the port factory class is `PortSpec`, and descriptor properties avoid
  the names of the types they build.** Every component inherits an instance
  property `Ports`, which would shadow a static class of that name inside a
  descriptor initialiser. For the same reason `CoreDescriptors` has `BeltBulk`
  and `BeltDiscrete`, and `TransformDescriptors` has `Thermal`, `Moisture` and
  `Residence`.
- **R46 — one parameter kind beyond the spec's list: `StringList`.** A plant-local
  material declares its state names as an array of strings, and the loader
  checks its own envelope with the same binder that checks component
  parameters.
- **R47 — the diagnostic table gains `DSE112` (a tag cannot bind that port) and
  there is no `DSE012`.** The spec marked its table provisional and, in 3.4,
  explained why the recipe/material check stays parked.
- **R48 — one load context per `--assembly`.** The spec said one shared context;
  an `AssemblyDependencyResolver` is per plugin, so the context is too. Each
  declines any assembly the host ships beside `dse`, which is what keeps
  `ICatalogueModule` a single type.
- **R49 — conformance returns a report, not a list.** `CatalogueConformance.Check`
  returns `ConformanceReport(Mismatches, BuiltTypes)`; the sweep needs to know
  which CLR types the catalogue can build.
- **R44 — the catalogue class is `ComponentCatalogue`.** The spec calls it
  `Catalogue`, but a class of that name inside namespace `Dse.Core.Catalogue` is
  shadowed by the namespace from every `Dse.Core.*` namespace (`CS0118`). The
  builder stays `CatalogueBuilder`; the module interface stays
  `ICatalogueModule`.

## File structure

```
src/Dse.Core/
  Graph/PortConnector.cs                 new — untyped signal and flow connection
  Graph/ICapabilityProvider.cs           new
  Graph/CompositeComponent.cs            + ExposedPorts, LeafComponents
  Graph/Port.cs, InputPort.cs, OutputPort.cs   + internal ValueType, IsInput, IsRequiredInput, TryConnectFrom
  Io/TagBinding.cs                       + ForPort
  Catalogue/
    ComponentCategory.cs  ParameterKind.cs  PortDirection.cs  ObjectSlots.cs
    ParameterDescriptor.cs  GroupDefinition.cs  Param.cs
    PortRepeat.cs  PortDescriptor.cs  FlowPortDescriptor.cs  PortSpec.cs
    TagEntry.cs  TelemetryKey.cs
    ComponentDescriptor.cs  ObjectDescriptor.cs  MaterialDescriptor.cs
    ICatalogueModule.cs  CatalogueBuilder.cs  ComponentCatalogue.cs
    Suggest.cs  Capabilities.cs  BindingIssue.cs  BindingContext.cs
    ParameterValues.cs  ParameterBinder.cs
    CatalogueJson.cs
    CoreDescriptors.cs                   unit-delay-bool, unit-delay-double, bulk-belt, discrete-belt
  Testing/ConformanceFixtures.cs  ConformanceReport.cs  CatalogueConformance.cs
src/Dse.Components/
  ComponentsModule.cs                    new
  <every component file>                 + public static Descriptor
  Mechanical/MotorRatingGroup.cs         new — shared group + reader
  Instruments/InstrumentCatalogue.cs     new — shared spec group, outputs, tag, telemetry
  Transforms/TransformDescriptors.cs     new
  Flow/HoldDescriptors.cs                new
src/Dse.Configuration/                   new project
  DiagnosticSeverity.cs  ConfigDiagnostic.cs  DiagnosticInfo.cs  ConfigDiagnostics.cs
  LoadOptions.cs  LoadResult.cs  PlantSummary.cs  PlantLoader.cs
  PlantSchema.cs  DiagnosticsReference.cs
  Loading/LoadState.cs  PlantSchemas.cs  ParseStage.cs  StructureStage.cs
          ReferenceStage.cs  InstantiateStage.cs  WireStage.cs  BuildStage.cs
src/Dse.Cli/                             new project (assembly name: dse)
  Program.cs  CliApp.cs  ExitCodes.cs  CommandLine.cs  CommandTable.cs  CliContext.cs  ModuleLoader.cs
  Commands/CatalogExport.cs  SchemaExport.cs  Validate.cs  Tags.cs  PlantFile.cs
tests/
  Shared/Golden.cs                       linked into the projects that pin golden files
  Dse.Core.Tests/Catalogue/*             model, suggest, binder, conformance, export tests
  Dse.Core.Tests/PortConnectorTests.cs, TagBindingForPortTests.cs
  Dse.Components.Tests/Catalogue/*       fixtures, conformance + sweep, factory tests, export golden
  Dse.Configuration.Tests/               new — loader stages, corpus (Plants/), schema, agreement, round trip
  Dse.Cli.Tests/                         new — in-process CLI tests, plugin tests
  Dse.Cli.Tests.SampleModule/            new — the recipe's worked example, loaded as a stranger
  Dse.Cli.Tests.ClashModule/             new — a deliberate duplicate type name
docs/
  authoring-a-component.md  configuration-diagnostics.md (generated)
  architecture.md (+ section)            README.md (+ status, CLI quick start)
```

## Task map

| # | Task | Deliverable |
|---|---|---|
| 1 | Core seams | connect, enumerate and tag ports without knowing `T` |
| 2 | Catalogue model | descriptors, parameters, builder, `ComponentCatalogue` |
| 3 | Parameter binding | JSON → `ParameterValues`, every issue collected |
| 4 | Conformance | descriptor-versus-instance check, module, sweep with `Pending` |
| 5–8 | Descriptor rollout | mechanical · instruments + safety · flow + transforms + holds · conveyor |
| 9 | Export | deterministic catalogue JSON, golden helper |
| 10–12 | Loader | parse + structure · references + instantiate · wire + build, corpus, round trip |
| 13 | Schema | generated draft 2020-12 schema, agreement with the loader |
| 14–15 | CLI | four commands · `--assembly` and the sample module |
| 16 | Documentation | authoring recipe, generated diagnostics reference, architecture, README |

Tasks are sequential. Suggested models, following plans 3 and 4: Tasks 5–8 are
transcription plus conformance-driven correction and suit the smaller model;
Tasks 1–4, 9–15 edit or create logic and want the larger one; every reviewer,
and Task 16's prose check against the source, the larger one.

---
### Task 1: Core seams for untyped wiring

The loader holds a `Port` and does not know its `T`. Today nothing public can
connect two `Port`s, list a composite's aliases or leaves, bind a tag to an
untyped port, or ask a composite for its belt. This task adds exactly those
seams (R37) and nothing else.

**Files:**
- Create: `src/Dse.Core/Graph/PortConnector.cs`
- Create: `src/Dse.Core/Graph/ICapabilityProvider.cs`
- Modify: `src/Dse.Core/Graph/Port.cs` (four internal virtuals)
- Modify: `src/Dse.Core/Graph/InputPort.cs`, `src/Dse.Core/Graph/OutputPort.cs` (override them)
- Modify: `src/Dse.Core/Graph/CompositeComponent.cs` (two public properties)
- Modify: `src/Dse.Core/Io/TagBinding.cs` (one public factory)
- Modify: `src/Dse.Components/Conveyors/Conveyor.cs` (implement `ICapabilityProvider`)
- Test: `tests/Dse.Core.Tests/PortConnectorTests.cs`
- Test: `tests/Dse.Core.Tests/TagBindingForPortTests.cs`
- Test: `tests/Dse.Components.Tests/ConveyorCapabilityTests.cs`

**Interfaces:**
- Consumes: `Port`, `InputPort<T>`, `OutputPort<T>`, `FlowInlet`, `FlowOutlet`,
  `CompositeComponent`, `TagBinding`, `UnitDelay<T>` — all existing.
- Produces:
  - `public static class PortConnector { public static bool TryConnect(Port from, Port to, out string problem); }`
    — `problem` is empty on success, otherwise a sentence ending in a full stop.
  - `public interface ICapabilityProvider { bool TryGetCapability(Type capability, out object? instance); }`
  - `public IReadOnlyList<KeyValuePair<string, Port>> CompositeComponent.ExposedPorts` — sorted by alias, ordinal; signal and flow ports both.
  - `public IReadOnlyList<ISimComponent> CompositeComponent.LeafComponents` — flattened, in child order.
  - `public static TagBinding TagBinding.ForPort(string name, Port port, TagAccess access, string unit = "", double rangeLow = double.NaN, double rangeHigh = double.NaN, string description = "")`
    — throws `ArgumentException` naming the supported port types when the port's type has no tag kind, or when `access` is `ReadWrite` on an output.

- [ ] **Step 1: Write the failing connector tests**

Create `tests/Dse.Core.Tests/PortConnectorTests.cs`:

```csharp
using Dse.Core.Flow;
using Dse.Core.Graph;

namespace Dse.Core.Tests;

public class PortConnectorTests
{
    [Fact]
    public void ConnectsAnOutputToAnInputOfTheSameType()
    {
        var a = new UnitDelay<bool>("A");
        var b = new UnitDelay<bool>("B");

        bool ok = PortConnector.TryConnect(a.Out, b.In, out string problem);

        Assert.True(ok, problem);
        Assert.Empty(problem);
        a.Out.Value = true;
        Assert.True(b.In.Value);
    }

    [Fact]
    public void RefusesMismatchedValueTypesAndNamesBoth()
    {
        var a = new UnitDelay<bool>("A");
        var b = new UnitDelay<double>("B");

        bool ok = PortConnector.TryConnect(a.Out, b.In, out string problem);

        Assert.False(ok);
        Assert.Contains("A.Out", problem, StringComparison.Ordinal);
        Assert.Contains("B.In", problem, StringComparison.Ordinal);
        Assert.Contains("Boolean", problem, StringComparison.Ordinal);
        Assert.Contains("Double", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void RefusesAnInputAsTheSource()
    {
        var a = new UnitDelay<bool>("A");
        var b = new UnitDelay<bool>("B");

        bool ok = PortConnector.TryConnect(a.In, b.In, out string problem);

        Assert.False(ok);
        Assert.Contains("'A.In' is an input", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void RefusesAnOutputAsTheTarget()
    {
        var a = new UnitDelay<bool>("A");
        var b = new UnitDelay<bool>("B");

        bool ok = PortConnector.TryConnect(a.Out, b.Out, out string problem);

        Assert.False(ok);
        Assert.Contains("'B.Out' is an output", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void ReportsASecondDriverInsteadOfThrowing()
    {
        var a = new UnitDelay<bool>("A");
        var b = new UnitDelay<bool>("B");
        var c = new UnitDelay<bool>("C");
        Assert.True(PortConnector.TryConnect(a.Out, c.In, out _));

        bool ok = PortConnector.TryConnect(b.Out, c.In, out string problem);

        Assert.False(ok);
        Assert.Contains("already driven by 'A.Out'", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void ConnectsAnOutletToAnInletOfTheSameKind()
    {
        var outlet = new FlowOutlet("Out", "Up", PayloadKind.Bulk);
        var inlet = new FlowInlet("In", "Down", PayloadKind.Bulk);

        bool ok = PortConnector.TryConnect(outlet, inlet, out string problem);

        Assert.True(ok, problem);
        Assert.True(outlet.IsConnected);
        Assert.True(inlet.IsConnected);
    }

    [Fact]
    public void ReportsAPayloadKindMismatch()
    {
        var outlet = new FlowOutlet("Out", "Up", PayloadKind.Bulk);
        var inlet = new FlowInlet("In", "Down", PayloadKind.Discrete);

        bool ok = PortConnector.TryConnect(outlet, inlet, out string problem);

        Assert.False(ok);
        Assert.Contains("Bulk outlet", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void RefusesASignalPortOnAFlowLink()
    {
        var outlet = new FlowOutlet("Out", "Up", PayloadKind.Bulk);
        var b = new UnitDelay<bool>("B");

        bool ok = PortConnector.TryConnect(outlet, b.In, out string problem);

        Assert.False(ok);
        Assert.Contains("material", problem, StringComparison.Ordinal);
    }

    private sealed class Pair : CompositeComponent
    {
        public Pair(string id)
            : base(id)
        {
            First = AddChild(new UnitDelay<bool>("First"));
            Second = AddChild(new UnitDelay<bool>("Second"));
            First.Out.ConnectTo(Second.In);
            Expose("Zed", Second.Out);
            Expose("Alpha", First.In);
        }

        public UnitDelay<bool> First { get; }

        public UnitDelay<bool> Second { get; }
    }

    [Fact]
    public void ACompositeListsItsAliasesSortedAndItsLeavesInChildOrder()
    {
        var pair = new Pair("P");

        Assert.Equal(["Alpha", "Zed"], pair.ExposedPorts.Select(e => e.Key));
        Assert.Same(pair.First.In, pair.ExposedPorts[0].Value);
        Assert.Equal(["P.First", "P.Second"], pair.LeafComponents.Select(l => l.Id));
    }
}
```

- [ ] **Step 2: Run them and see them fail to compile**

Run: `dotnet test tests/Dse.Core.Tests --nologo --filter FullyQualifiedName~PortConnectorTests`
Expected: build FAILS — `PortConnector`, `ExposedPorts` and `LeafComponents` do not exist.

- [ ] **Step 3: Add the internal hooks on `Port`, `InputPort<T>` and `OutputPort<T>`**

In `src/Dse.Core/Graph/Port.cs`, add beside the other internal members:

```csharp
    /// <summary>
    /// Connects <paramref name="source"/> to this port when this is an input of
    /// the same value type. False means "not compatible"; an incompatible
    /// <em>state</em> (already driven, frozen) throws as <c>ConnectFrom</c> does.
    /// </summary>
    internal virtual bool TryConnectFrom(Port source) => false;

    /// <summary>The CLR type this port carries, or null for a flow port.</summary>
    internal virtual Type? ValueType => null;

    /// <summary>True for an <c>InputPort&lt;T&gt;</c>. Flow ports answer false; ask their type instead.</summary>
    internal virtual bool IsInput => false;

    /// <summary>True for an input declared <c>required</c>. Read by catalogue conformance.</summary>
    internal virtual bool IsRequiredInput => false;
```

In `src/Dse.Core/Graph/InputPort.cs`, add:

```csharp
    internal override Type? ValueType => typeof(T);

    internal override bool IsInput => true;

    internal override bool IsRequiredInput => IsRequired;

    internal override bool TryConnectFrom(Port source)
    {
        if (source is not OutputPort<T> typed)
        {
            return false;
        }

        ConnectFrom(typed);
        return true;
    }
```

In `src/Dse.Core/Graph/OutputPort.cs`, add:

```csharp
    internal override Type? ValueType => typeof(T);
```

- [ ] **Step 4: Write `PortConnector`**

Create `src/Dse.Core/Graph/PortConnector.cs`:

```csharp
using Dse.Core.Flow;

namespace Dse.Core.Graph;

/// <summary>
/// Connects two ports without knowing their value type. Configuration loaders
/// hold a <see cref="Port"/>, not an <c>OutputPort&lt;T&gt;</c>; this is their way in.
/// Code that knows its types keeps using <c>ConnectTo</c>.
/// </summary>
public static class PortConnector
{
    /// <summary>
    /// Connects <paramref name="from"/> to <paramref name="to"/>. Returns false
    /// with a sentence in <paramref name="problem"/> when the two cannot be
    /// connected; never throws for a wiring mistake.
    /// </summary>
    public static bool TryConnect(Port from, Port to, out string problem)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);

        bool fromIsFlow = from is FlowPort;
        bool toIsFlow = to is FlowPort;
        if (fromIsFlow != toIsFlow)
        {
            Port flow = fromIsFlow ? from : to;
            Port signal = fromIsFlow ? to : from;
            problem =
                $"'{flow.QualifiedName}' carries material and '{signal.QualifiedName}' carries a signal; " +
                $"a link joins two ports of the same layer.";
            return false;
        }

        try
        {
            return fromIsFlow ? ConnectFlow(from, to, out problem) : ConnectSignal(from, to, out problem);
        }
        catch (InvalidOperationException ex)
        {
            problem = ex.Message;
            return false;
        }
    }

    private static bool ConnectFlow(Port from, Port to, out string problem)
    {
        if (from is not FlowOutlet outlet)
        {
            problem = $"'{from.QualifiedName}' is an inlet; a flow link starts at an outlet.";
            return false;
        }

        if (to is not FlowInlet inlet)
        {
            problem = $"'{to.QualifiedName}' is an outlet; a flow link ends at an inlet.";
            return false;
        }

        outlet.ConnectTo(inlet);
        problem = string.Empty;
        return true;
    }

    private static bool ConnectSignal(Port from, Port to, out string problem)
    {
        if (from.IsInput)
        {
            problem = $"'{from.QualifiedName}' is an input; a signal link starts at an output.";
            return false;
        }

        if (!to.IsInput)
        {
            problem = $"'{to.QualifiedName}' is an output; a signal link ends at an input.";
            return false;
        }

        if (to.TryConnectFrom(from))
        {
            problem = string.Empty;
            return true;
        }

        problem =
            $"'{from.QualifiedName}' carries {from.ValueType?.Name} and '{to.QualifiedName}' expects " +
            $"{to.ValueType?.Name}; a signal link joins ports of one value type.";
        return false;
    }
}
```

- [ ] **Step 5: Expose a composite's aliases and leaves**

In `src/Dse.Core/Graph/CompositeComponent.cs`, add after `Outlet(string alias)`:

```csharp
    /// <summary>Every exposed port, signal and flow, sorted by alias (ordinal).</summary>
    public IReadOnlyList<KeyValuePair<string, Port>> ExposedPorts =>
        _aliases.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToList();

    /// <summary>The leaves this composite flattens to, nested composites included, in child order.</summary>
    public IReadOnlyList<ISimComponent> LeafComponents => Leaves().ToList();
```

- [ ] **Step 6: Run the connector tests**

Run: `dotnet test tests/Dse.Core.Tests --nologo --filter FullyQualifiedName~PortConnectorTests`
Expected: PASS, 9 tests.

- [ ] **Step 7: Write the failing `ForPort` tests**

Create `tests/Dse.Core.Tests/TagBindingForPortTests.cs`:

```csharp
using Dse.Core.Graph;
using Dse.Core.Io;
using Dse.Io;

namespace Dse.Core.Tests;

public class TagBindingForPortTests
{
    [Fact]
    public void BindsABoolOutputReadOnly()
    {
        var delay = new UnitDelay<bool>("D");

        TagBinding tag = TagBinding.ForPort("D.OUT", delay.Out, TagAccess.ReadOnly, description: "Held value");

        Assert.Equal(TagKind.Bool, tag.Kind);
        Assert.Equal(TagAccess.ReadOnly, tag.Access);
        Assert.Same(delay.Out, tag.Port);
        Assert.Equal("Held value", tag.Description);
    }

    [Fact]
    public void BindsADoubleInputWritableWithUnitAndRange()
    {
        var delay = new UnitDelay<double>("D");

        TagBinding tag = TagBinding.ForPort("D.SP", delay.In, TagAccess.ReadWrite, "m/s", 0.0, 5.0);

        Assert.Equal(TagKind.Double, tag.Kind);
        Assert.Equal(TagAccess.ReadWrite, tag.Access);
        Assert.Equal("m/s", tag.Unit);
        Assert.Equal(5.0, tag.RangeHigh);
    }

    [Fact]
    public void BindsAnInputReadOnlyToObserveACommand()
    {
        var delay = new UnitDelay<bool>("D");

        TagBinding tag = TagBinding.ForPort("D.CMD", delay.In, TagAccess.ReadOnly);

        Assert.Equal(TagAccess.ReadOnly, tag.Access);
    }

    [Fact]
    public void BindsLongAndIntOutputsAsInt64()
    {
        Assert.Equal(TagKind.Int64, TagBinding.ForPort("L", new UnitDelay<long>("L").Out, TagAccess.ReadOnly).Kind);
        Assert.Equal(TagKind.Int64, TagBinding.ForPort("I", new UnitDelay<int>("I").Out, TagAccess.ReadOnly).Kind);
    }

    [Fact]
    public void RefusesToWriteAnOutput()
    {
        var delay = new UnitDelay<bool>("D");

        var ex = Assert.Throws<ArgumentException>(
            () => TagBinding.ForPort("D.OUT", delay.Out, TagAccess.ReadWrite));

        Assert.Contains("is an output", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RefusesAPortTypeWithNoTagKindAndListsTheSupportedOnes()
    {
        var delay = new UnitDelay<float>("D");

        var ex = Assert.Throws<ArgumentException>(
            () => TagBinding.ForPort("D.OUT", delay.Out, TagAccess.ReadOnly));

        Assert.Contains("Single", ex.Message, StringComparison.Ordinal);
        Assert.Contains("bool, double, int, long", ex.Message, StringComparison.Ordinal);
    }
}
```

- [ ] **Step 8: Run them and see them fail to compile**

Run: `dotnet test tests/Dse.Core.Tests --nologo --filter FullyQualifiedName~TagBindingForPortTests`
Expected: build FAILS — `TagBinding.ForPort` does not exist.

- [ ] **Step 9: Write `TagBinding.ForPort`**

In `src/Dse.Core/Io/TagBinding.cs`, add after the last `Write(...)` factory. A
read-only binding on an *input* needs a capture the typed factories do not
offer, so those three cases construct the binding directly:

```csharp
    /// <summary>
    /// Binds a port whose value type is not known at compile time — the way a
    /// configuration file binds tags. Supports bool, double, int and long ports.
    /// An input may be bound <see cref="TagAccess.ReadOnly"/> to observe a command.
    /// </summary>
    public static TagBinding ForPort(
        string name,
        Port port,
        TagAccess access,
        string unit = "",
        double rangeLow = double.NaN,
        double rangeHigh = double.NaN,
        string description = "")
    {
        ArgumentNullException.ThrowIfNull(port);
        ArgumentNullException.ThrowIfNull(unit);
        description ??= string.Empty;
        bool writable = access == TagAccess.ReadWrite;

        switch (port)
        {
            case OutputPort<bool> o when !writable:
                return Read(name, o, description);
            case OutputPort<double> o when !writable:
                return Read(name, o, unit, rangeLow, rangeHigh, description);
            case OutputPort<long> o when !writable:
                return Read(name, o, unit.Length == 0 ? "count" : unit, description);
            case OutputPort<int> o when !writable:
                return Read(name, o, unit.Length == 0 ? "count" : unit, description);
            case InputPort<bool> i:
                return writable ? Write(name, i, description) : Write(name, i, description).AsReadOnly();
            case InputPort<double> i:
                return writable
                    ? Write(name, i, unit, rangeLow, rangeHigh, description)
                    : Write(name, i, unit, rangeLow, rangeHigh, description).AsReadOnly();
            case InputPort<long> i:
                return writable
                    ? Write(name, i, unit.Length == 0 ? "count" : unit, description)
                    : Write(name, i, unit.Length == 0 ? "count" : unit, description).AsReadOnly();
        }

        if (writable && IsOutput(port))
        {
            throw new ArgumentException(
                $"Port '{port.QualifiedName}' is an output; a tag can read it but not write it. " +
                $"Bind it read-only, or bind the input it drives.",
                nameof(access));
        }

        throw new ArgumentException(
            $"Port '{port.QualifiedName}' carries {port.ValueType?.Name ?? "material"}, which has no tag kind. " +
            $"A tag binds a bool, double, int, long port (an int input is read-only).",
            nameof(port));
    }

    private static bool IsOutput(Port port) => port.ValueType is not null && !port.IsInput;
```

The final message must contain the literal `bool, double, int, long` (a test
looks for it).

- [ ] **Step 10: Run the `ForPort` tests**

Run: `dotnet test tests/Dse.Core.Tests --nologo --filter FullyQualifiedName~TagBindingForPortTests`
Expected: PASS, 6 tests.

- [ ] **Step 11: Write `ICapabilityProvider`**

Create `src/Dse.Core/Graph/ICapabilityProvider.cs`:

```csharp
namespace Dse.Core.Graph;

/// <summary>
/// A node that can hand out something it does not itself implement — a conveyor
/// offering its belt as an <c>IMaterialObservable</c>. Lets a reference name the
/// composite instead of an inner leaf.
/// </summary>
public interface ICapabilityProvider
{
    /// <summary>True, with the instance, when this node can supply <paramref name="capability"/>.</summary>
    bool TryGetCapability(Type capability, out object? instance);
}
```

- [ ] **Step 12: Write the failing conveyor test**

Create `tests/Dse.Components.Tests/ConveyorCapabilityTests.cs`:

```csharp
using Dse.Components.Conveyors;
using Dse.Components.Mechanical;
using Dse.Core.Flow;
using Dse.Core.Graph;

namespace Dse.Components.Tests;

public class ConveyorCapabilityTests
{
    private static readonly ConveyorOptions Options = new(
        LengthM: 10.0,
        CellSizeM: 0.5,
        BeltWidthM: 0.8,
        AngleOfReposeDeg: 20.0,
        MaterialDensityKgM3: 2000.0,
        EmptyBeltMassKg: 250.0,
        FrictionCoefficient: 0.04,
        PulleyDiameterM: 0.5,
        GearRatio: 20.0,
        Motor: new MotorRating(750.0, 150.0, 2.0));

    [Fact]
    public void AConveyorOffersItsBeltAsAnObservable()
    {
        var conveyor = new Conveyor("CV001", Options);

        bool ok = ((ICapabilityProvider)conveyor).TryGetCapability(typeof(IMaterialObservable), out object? instance);

        Assert.True(ok);
        Assert.Same(conveyor.Belt, instance);
    }

    [Fact]
    public void AConveyorOffersNothingElse()
    {
        var conveyor = new Conveyor("CV001", Options);

        bool ok = ((ICapabilityProvider)conveyor).TryGetCapability(typeof(IDisposable), out object? instance);

        Assert.False(ok);
        Assert.Null(instance);
    }
}
```

- [ ] **Step 13: Implement it on `Conveyor`**

In `src/Dse.Components/Conveyors/Conveyor.cs`, change the class line to
`public sealed class Conveyor : CompositeComponent, ICapabilityProvider` and add:

```csharp
    /// <summary>The belt stands in for the conveyor wherever something observes material.</summary>
    public bool TryGetCapability(Type capability, out object? instance)
    {
        ArgumentNullException.ThrowIfNull(capability);
        instance = capability.IsInstanceOfType(Belt) ? Belt : null;
        return instance is not null;
    }
```

`IDisposable` is not implemented by `BulkBelt`, so the second test sees false.

- [ ] **Step 14: Run everything**

Run: `dotnet build Dse.sln -c Release --nologo` — expect `0 Warning(s)`, `0 Error(s)`.
Run: `dotnet test Dse.sln --nologo`
Expected: PASS, 471 + 17 = 488 tests.

- [ ] **Step 15: Commit**

```bash
git add src/Dse.Core src/Dse.Components/Conveyors/Conveyor.cs tests/Dse.Core.Tests/PortConnectorTests.cs tests/Dse.Core.Tests/TagBindingForPortTests.cs tests/Dse.Components.Tests/ConveyorCapabilityTests.cs
git commit -m "feat(core): connect, enumerate and tag ports without knowing their value type"
```

---
### Task 2: The catalogue model

Pure data and one builder. No JSON, no binding, no component knows about it yet.

**Files:**
- Create, all under `src/Dse.Core/Catalogue/`: `ComponentCategory.cs`,
  `ParameterKind.cs`, `PortDirection.cs`, `ObjectSlots.cs`,
  `ParameterDescriptor.cs`, `GroupDefinition.cs`, `Param.cs`, `PortRepeat.cs`,
  `PortDescriptor.cs`, `FlowPortDescriptor.cs`, `PortSpec.cs`, `TagEntry.cs`,
  `TelemetryKey.cs`, `ComponentDescriptor.cs`, `ObjectDescriptor.cs`,
  `MaterialDescriptor.cs`, `ICatalogueModule.cs`, `CatalogueBuilder.cs`,
  `ComponentCatalogue.cs`, `ParameterValues.cs` (a stub completed in Task 3)
- Test: `tests/Dse.Core.Tests/Catalogue/CatalogueModelTests.cs`

**Interfaces:**
- Consumes: `ISimNode`, `FaultDescriptor`, `MaterialType`, `MaterialProperties`,
  `PayloadKind`, `Dse.Io.TagKind`, `Dse.Io.TagAccess`.
- Produces (namespace `Dse.Core.Catalogue`) — later tasks use these names exactly:
  - `enum ComponentCategory { Signal, Mechanical, Instrumentation, Safety, Flow, Conveyor }`
  - `enum ParameterKind { Double, Int, Bool, String, StringList, Enum, Group, GroupList, Reference, Material, MaterialState, Object, ObjectList }`
  - `enum PortDirection { In, Out }`
  - `static class ObjectSlots { const string Transform = "transform"; const string Hold = "hold"; }`
  - `sealed class ParameterDescriptor` with `Name, Kind, Description, Unit, Default (object?), IsOptional, Minimum, Maximum (double?), ExclusiveMinimum, ExclusiveMaximum, AllowedValues, GroupName, Children, Capability (Type?), Payload (PayloadKind?), MaterialParameter, Slot, MinCount, IsRequired`
  - `sealed class GroupDefinition(string name, params ParameterDescriptor[] parameters)` with `Name`, `Parameters`
  - `static class Param` — `Double, Int, Bool, String, StringList, Enum, Group, GroupList, Reference<T>, Material, MaterialState, Object, ObjectList` (signatures in Step 3)
  - `sealed record PortRepeat(string Parameter, string NameChild = "")`
  - `sealed record PortDescriptor(string Name, PortDirection Direction, string ValueType, string Unit, string Description, bool Required, PortRepeat? Repeat)`
  - `sealed record FlowPortDescriptor(string Name, PortDirection Direction, PayloadKind Payload, string Description, PortRepeat? Repeat)`
  - `static class PortSpec` — `In<T>`, `Out<T>`, `Inlet`, `Outlet`, `ValueTypeName(Type)`
  - `sealed record TagEntry(string Name, TagKind Kind, TagAccess Access, string Unit = "", PortRepeat? Repeat = null)`
  - `sealed record TelemetryKey(string Name, string Unit = "")`
  - `sealed class ComponentDescriptor`, `sealed class ObjectDescriptor`, `sealed record MaterialDescriptor(MaterialType Material, MaterialProperties Properties, string Description)`
  - `interface ICatalogueModule { string Name { get; } void Register(CatalogueBuilder builder); }`
  - `sealed class CatalogueBuilder` — `Add<TModule>()`, `Add(ICatalogueModule)`, `Add(ComponentDescriptor)`, `Add(ObjectDescriptor)`, `Add(MaterialDescriptor)`, `Build()`
  - `sealed class ComponentCatalogue` (R44) — `Components`, `Objects`, `Materials`, `Modules`, `TryGetComponent`, `TryGetObject`, `ObjectsIn(slot)`, `Slots`, `TryGetMaterial`, `ModuleOf(...)`

- [ ] **Step 1: Write the failing tests**

Create `tests/Dse.Core.Tests/Catalogue/CatalogueModelTests.cs`:

```csharp
using Dse.Core.Catalogue;
using Dse.Core.Flow;
using Dse.Core.Graph;

namespace Dse.Core.Tests.Catalogue;

public class CatalogueModelTests
{
    private static ComponentDescriptor Delay(string type) =>
        new(type, ComponentCategory.Signal, "Holds a value for one tick.", (id, p) => new UnitDelay<bool>(id))
        {
            Ports = [PortSpec.In<bool>("In"), PortSpec.Out<bool>("Out")],
        };

    private sealed class ModuleA : ICatalogueModule
    {
        public string Name => "A";

        public void Register(CatalogueBuilder builder) => builder.Add(Delay("delay"));
    }

    private sealed class ModuleB : ICatalogueModule
    {
        public string Name => "B";

        public void Register(CatalogueBuilder builder) => builder.Add(Delay("delay"));
    }

    [Fact]
    public void AScalarWithoutADefaultIsRequiredUnlessOptional()
    {
        Assert.True(Param.Double("ratio", "Reduction ratio.").IsRequired);
        Assert.False(Param.Double("efficiency", "Efficiency.", @default: 0.95).IsRequired);
        Assert.False(Param.Double("capacityKg", "Omit for unlimited.", optional: true).IsRequired);
    }

    [Fact]
    public void AGroupIsRequiredOnlyWhenOneOfItsChildrenIs()
    {
        var allDefaulted = new GroupDefinition("G1", Param.Double("a", "A.", @default: 1.0));
        var oneRequired = new GroupDefinition("G2", Param.Double("a", "A."), Param.Double("b", "B.", @default: 1.0));

        Assert.False(Param.Group("g", "G.", allDefaulted).IsRequired);
        Assert.True(Param.Group("g", "G.", oneRequired).IsRequired);
    }

    [Fact]
    public void AListIsRequiredWhenItsMinimumCountIsPositive()
    {
        var line = new GroupDefinition("Line", Param.String("inlet", "Inlet name."));

        Assert.True(Param.GroupList("recipe", "Recipe.", line, minCount: 1).IsRequired);
        Assert.False(Param.ObjectList("transforms", "Transforms.", ObjectSlots.Transform).IsRequired);
        Assert.False(Param.StringList("states", "States.").IsRequired);
    }

    [Fact]
    public void AReferenceRecordsItsCapability()
    {
        ParameterDescriptor belt = Param.Reference<IMaterialObservable>("belt", "The belt weighed.");

        Assert.Equal(ParameterKind.Reference, belt.Kind);
        Assert.Equal(typeof(IMaterialObservable), belt.Capability);
        Assert.True(belt.IsRequired);
    }

    [Theory]
    [InlineData("RatedPower")]
    [InlineData("rated-power")]
    [InlineData("rated power")]
    [InlineData("")]
    public void AParameterNameMustBeCamelCase(string name)
    {
        Assert.Throws<ArgumentException>(() => Param.Double(name, "X."));
    }

    [Fact]
    public void PortValueTypesHaveStableNames()
    {
        Assert.Equal("bool", PortSpec.ValueTypeName(typeof(bool)));
        Assert.Equal("double", PortSpec.ValueTypeName(typeof(double)));
        Assert.Equal("int", PortSpec.ValueTypeName(typeof(int)));
        Assert.Equal("long", PortSpec.ValueTypeName(typeof(long)));
        Assert.Equal("enum:PayloadKind", PortSpec.ValueTypeName(typeof(PayloadKind)));
    }

    [Theory]
    [InlineData("Delay")]
    [InlineData("unit_delay")]
    [InlineData("-delay")]
    [InlineData("delay-")]
    public void ATypeNameMustBeKebabCase(string type)
    {
        var ex = Assert.Throws<ArgumentException>(() => new CatalogueBuilder().Add(Delay(type)));
        Assert.Contains("kebab-case", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PortNamesMustBeUniqueIgnoringCase()
    {
        var descriptor = new ComponentDescriptor(
            "clash", ComponentCategory.Signal, "Two ports, one name.", (id, p) => new UnitDelay<bool>(id))
        {
            Ports = [PortSpec.In<bool>("Run"), PortSpec.Out<bool>("RUN")],
        };

        var ex = Assert.Throws<ArgumentException>(() => new CatalogueBuilder().Add(descriptor));
        Assert.Contains("'Run' and 'RUN'", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ADuplicateTypeNamesBothModules()
    {
        CatalogueBuilder builder = new CatalogueBuilder().Add<ModuleA>();

        var ex = Assert.Throws<InvalidOperationException>(() => builder.Add<ModuleB>());

        Assert.Contains("'delay'", ex.Message, StringComparison.Ordinal);
        Assert.Contains("module 'A'", ex.Message, StringComparison.Ordinal);
        Assert.Contains("module 'B'", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheSameObjectTypeMayExistInTwoSlots()
    {
        ComponentCatalogue catalogue = new CatalogueBuilder()
            .Add(new ObjectDescriptor(ObjectSlots.Transform, "none", "Does nothing.", p => new object()))
            .Add(new ObjectDescriptor(ObjectSlots.Hold, "none", "Holds nothing.", p => new object()))
            .Build();

        Assert.True(catalogue.TryGetObject(ObjectSlots.Transform, "none", out _));
        Assert.True(catalogue.TryGetObject(ObjectSlots.Hold, "none", out _));
        Assert.Equal(["hold", "transform"], catalogue.Slots);
    }

    [Fact]
    public void EntriesComeBackSortedByTypeName()
    {
        ComponentCatalogue catalogue = new CatalogueBuilder()
            .Add(Delay("zeta"))
            .Add(Delay("alpha"))
            .Add(new MaterialDescriptor(new MaterialType("ore", PayloadKind.Bulk), default, "Run-of-mine ore."))
            .Build();

        Assert.Equal(["alpha", "zeta"], catalogue.Components.Select(c => c.Type));
        Assert.True(catalogue.TryGetComponent("alpha", out ComponentDescriptor? alpha));
        Assert.Equal("(direct)", catalogue.ModuleOf(alpha!));
        Assert.True(catalogue.TryGetMaterial("ore", out _));
        Assert.False(catalogue.TryGetComponent("Alpha", out _));
    }

    [Fact]
    public void ARepeatByCountExpandsFromOne()
    {
        var repeat = new PortRepeat("channels");

        Assert.Equal(["Channel1", "Channel2", "Channel3"], repeat.Expand("Channel{n}", count: 3, names: []));
    }

    [Fact]
    public void ARepeatByNameExpandsFromTheNames()
    {
        var repeat = new PortRepeat("recipe", "inlet");

        Assert.Equal(["Flour", "Water"], repeat.Expand("{n}", count: 0, names: ["Flour", "Water"]));
    }
}
```

- [ ] **Step 2: Run them and see them fail to compile**

Run: `dotnet test tests/Dse.Core.Tests --nologo --filter FullyQualifiedName~CatalogueModelTests`
Expected: build FAILS — namespace `Dse.Core.Catalogue` does not exist.

- [ ] **Step 3: Write the enums, slots and parameter model**

`src/Dse.Core/Catalogue/ComponentCategory.cs`:

```csharp
namespace Dse.Core.Catalogue;

/// <summary>Where a component sits in a palette. Has no effect on behaviour.</summary>
public enum ComponentCategory
{
    Signal,
    Mechanical,
    Instrumentation,
    Safety,
    Flow,
    Conveyor,
}
```

`src/Dse.Core/Catalogue/ParameterKind.cs`:

```csharp
namespace Dse.Core.Catalogue;

/// <summary>What a parameter holds, and therefore what JSON it accepts.</summary>
public enum ParameterKind
{
    Double,
    Int,
    Bool,
    String,
    /// <summary>An array of strings — a material's state names.</summary>
    StringList,
    Enum,
    /// <summary>A nested object with its own parameters — a rating, an instrument spec.</summary>
    Group,
    /// <summary>An array of such objects — recipe lines.</summary>
    GroupList,
    /// <summary>The id of another component that supplies a capability.</summary>
    Reference,
    /// <summary>The name of a material.</summary>
    Material,
    /// <summary>The name of a state of a sibling material parameter.</summary>
    MaterialState,
    /// <summary>One <c>{ "type": … }</c> object from a slot — a hold condition.</summary>
    Object,
    /// <summary>An array of them — a transform chain.</summary>
    ObjectList,
}
```

`src/Dse.Core/Catalogue/PortDirection.cs`:

```csharp
namespace Dse.Core.Catalogue;

public enum PortDirection
{
    In,
    Out,
}
```

`src/Dse.Core/Catalogue/ObjectSlots.cs`:

```csharp
namespace Dse.Core.Catalogue;

/// <summary>The object slots Core knows about. A slot is only a name; a module may introduce more.</summary>
public static class ObjectSlots
{
    /// <summary>An <c>IMaterialTransform</c>.</summary>
    public const string Transform = "transform";

    /// <summary>A hold condition of a process unit.</summary>
    public const string Hold = "hold";
}
```

`src/Dse.Core/Catalogue/GroupDefinition.cs`:

```csharp
namespace Dse.Core.Catalogue;

/// <summary>
/// A named, reusable set of parameters — one per nested record type. Declared
/// once and shared, so the schema defines it once under <c>$defs</c>.
/// </summary>
public sealed class GroupDefinition
{
    public GroupDefinition(string name, params ParameterDescriptor[] parameters)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(parameters);
        Name = name;
        Parameters = parameters.ToArray();
    }

    /// <summary>PascalCase, unique across a catalogue: <c>MotorRating</c>.</summary>
    public string Name { get; }

    public IReadOnlyList<ParameterDescriptor> Parameters { get; }
}
```

`src/Dse.Core/Catalogue/ParameterDescriptor.cs`:

```csharp
using Dse.Core.Flow;

namespace Dse.Core.Catalogue;

/// <summary>One parameter of a component, object or group. Built with <see cref="Param"/>.</summary>
public sealed class ParameterDescriptor
{
    internal ParameterDescriptor(string name, ParameterKind kind, string description)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        if (!IsCamelCase(name))
        {
            throw new ArgumentException(
                $"Parameter name '{name}' must be camelCase: a lowercase letter, then letters and digits.",
                nameof(name));
        }

        Name = name;
        Kind = kind;
        Description = description;
    }

    public string Name { get; }

    public ParameterKind Kind { get; }

    public string Description { get; }

    public string Unit { get; init; } = string.Empty;

    /// <summary>A <see cref="double"/>, <see cref="long"/>, <see cref="bool"/> or <see cref="string"/>; null when there is none.</summary>
    public object? Default { get; init; }

    /// <summary>May be omitted although it has no default; the factory decides what absence means.</summary>
    public bool IsOptional { get; init; }

    public double? Minimum { get; init; }

    public double? Maximum { get; init; }

    public bool ExclusiveMinimum { get; init; }

    public bool ExclusiveMaximum { get; init; }

    public IReadOnlyList<string> AllowedValues { get; init; } = [];

    /// <summary>For <see cref="ParameterKind.Group"/> and <see cref="ParameterKind.GroupList"/>: the shared definition's name.</summary>
    public string GroupName { get; init; } = string.Empty;

    public IReadOnlyList<ParameterDescriptor> Children { get; init; } = [];

    /// <summary>For <see cref="ParameterKind.Reference"/>: what the referenced component must supply.</summary>
    public Type? Capability { get; init; }

    /// <summary>For <see cref="ParameterKind.Material"/>: the payload kind required, or null for either.</summary>
    public PayloadKind? Payload { get; init; }

    /// <summary>For <see cref="ParameterKind.MaterialState"/>: the sibling material parameter it indexes.</summary>
    public string MaterialParameter { get; init; } = string.Empty;

    /// <summary>For <see cref="ParameterKind.Object"/> and <see cref="ParameterKind.ObjectList"/>.</summary>
    public string Slot { get; init; } = string.Empty;

    /// <summary>For the three list kinds.</summary>
    public int MinCount { get; init; }

    /// <summary>True when a plant file must supply it.</summary>
    public bool IsRequired => Kind switch
    {
        ParameterKind.Group => Children.Any(child => child.IsRequired),
        ParameterKind.GroupList or ParameterKind.ObjectList or ParameterKind.StringList => MinCount > 0,
        ParameterKind.Reference or ParameterKind.Material or ParameterKind.MaterialState or ParameterKind.Object => !IsOptional,
        _ => Default is null && !IsOptional,
    };

    private static bool IsCamelCase(string name)
    {
        if (name.Length == 0 || !char.IsAsciiLetterLower(name[0]))
        {
            return false;
        }

        foreach (char c in name)
        {
            if (!char.IsAsciiLetterOrDigit(c))
            {
                return false;
            }
        }

        return true;
    }
}
```

`src/Dse.Core/Catalogue/Param.cs`:

```csharp
using Dse.Core.Flow;

namespace Dse.Core.Catalogue;

/// <summary>The only way to make a <see cref="ParameterDescriptor"/>.</summary>
public static class Param
{
    public static ParameterDescriptor Double(
        string name,
        string description,
        string unit = "",
        double? @default = null,
        double? min = null,
        double? max = null,
        bool exclusiveMin = false,
        bool exclusiveMax = false,
        bool optional = false)
    {
        if (@default is { } value && !double.IsFinite(value))
        {
            throw new ArgumentException(
                $"Parameter '{name}' has a non-finite default. JSON cannot carry one; make the parameter optional instead.",
                nameof(@default));
        }

        return new ParameterDescriptor(name, ParameterKind.Double, description)
        {
            Unit = unit,
            Default = @default,
            Minimum = min,
            Maximum = max,
            ExclusiveMinimum = exclusiveMin,
            ExclusiveMaximum = exclusiveMax,
            IsOptional = optional,
        };
    }

    public static ParameterDescriptor Int(
        string name,
        string description,
        string unit = "",
        long? @default = null,
        double? min = null,
        double? max = null,
        bool optional = false) =>
        new(name, ParameterKind.Int, description)
        {
            Unit = unit,
            Default = @default,
            Minimum = min,
            Maximum = max,
            IsOptional = optional,
        };

    public static ParameterDescriptor Bool(string name, string description, bool? @default = null) =>
        new(name, ParameterKind.Bool, description) { Default = @default };

    public static ParameterDescriptor String(string name, string description, string? @default = null) =>
        new(name, ParameterKind.String, description) { Default = @default };

    public static ParameterDescriptor StringList(string name, string description, int minCount = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(minCount);
        return new ParameterDescriptor(name, ParameterKind.StringList, description) { MinCount = minCount };
    }

    public static ParameterDescriptor Enum(string name, string description, IReadOnlyList<string> values, string? @default = null)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count == 0)
        {
            throw new ArgumentException($"Enum parameter '{name}' needs at least one allowed value.", nameof(values));
        }

        if (@default is not null && !values.Contains(@default, StringComparer.Ordinal))
        {
            throw new ArgumentException($"Enum parameter '{name}' defaults to '{@default}', which it does not allow.", nameof(@default));
        }

        return new ParameterDescriptor(name, ParameterKind.Enum, description)
        {
            AllowedValues = values.ToArray(),
            Default = @default,
        };
    }

    public static ParameterDescriptor Group(string name, string description, GroupDefinition group)
    {
        ArgumentNullException.ThrowIfNull(group);
        return new ParameterDescriptor(name, ParameterKind.Group, description)
        {
            GroupName = group.Name,
            Children = group.Parameters,
        };
    }

    public static ParameterDescriptor GroupList(string name, string description, GroupDefinition group, int minCount = 0)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentOutOfRangeException.ThrowIfNegative(minCount);
        return new ParameterDescriptor(name, ParameterKind.GroupList, description)
        {
            GroupName = group.Name,
            Children = group.Parameters,
            MinCount = minCount,
        };
    }

    public static ParameterDescriptor Reference<TCapability>(string name, string description, bool optional = false)
        where TCapability : class =>
        new(name, ParameterKind.Reference, description)
        {
            Capability = typeof(TCapability),
            IsOptional = optional,
        };

    public static ParameterDescriptor Material(string name, string description, PayloadKind? kind = null, bool optional = false) =>
        new(name, ParameterKind.Material, description)
        {
            Payload = kind,
            IsOptional = optional,
        };

    public static ParameterDescriptor MaterialState(string name, string description, string materialParameter)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(materialParameter);
        return new ParameterDescriptor(name, ParameterKind.MaterialState, description)
        {
            MaterialParameter = materialParameter,
        };
    }

    public static ParameterDescriptor Object(string name, string description, string slot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slot);
        return new ParameterDescriptor(name, ParameterKind.Object, description) { Slot = slot };
    }

    public static ParameterDescriptor ObjectList(string name, string description, string slot, int minCount = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slot);
        ArgumentOutOfRangeException.ThrowIfNegative(minCount);
        return new ParameterDescriptor(name, ParameterKind.ObjectList, description)
        {
            Slot = slot,
            MinCount = minCount,
        };
    }
}
```

- [ ] **Step 4: Write the port, tag and telemetry records**

`src/Dse.Core/Catalogue/PortRepeat.cs`:

```csharp
using System.Globalization;

namespace Dse.Core.Catalogue;

/// <summary>
/// Declares a family of ports (or tags) whose number or names come from a
/// parameter. With no <paramref name="NameChild"/> the parameter is an
/// <see cref="ParameterKind.Int"/> and <c>{n}</c> runs 1..value. With one, the
/// parameter is a <see cref="ParameterKind.GroupList"/> and <c>{n}</c> is each
/// element's string child of that name.
/// </summary>
public sealed record PortRepeat(string Parameter, string NameChild = "")
{
    public bool IsByName => NameChild.Length > 0;

    /// <summary>The concrete names for a pattern, given the count or the names read from the parameter.</summary>
    public IReadOnlyList<string> Expand(string pattern, int count, IReadOnlyList<string> names)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        ArgumentNullException.ThrowIfNull(names);
        var result = new List<string>();
        if (IsByName)
        {
            foreach (string name in names)
            {
                result.Add(pattern.Replace("{n}", name, StringComparison.Ordinal));
            }
        }
        else
        {
            for (int i = 1; i <= count; i++)
            {
                result.Add(pattern.Replace("{n}", i.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal));
            }
        }

        return result;
    }
}
```

`src/Dse.Core/Catalogue/PortDescriptor.cs`:

```csharp
namespace Dse.Core.Catalogue;

/// <summary>A signal port as the catalogue describes it. Build with <see cref="PortSpec"/>.</summary>
public sealed record PortDescriptor(
    string Name,
    PortDirection Direction,
    string ValueType,
    string Unit,
    string Description,
    bool Required,
    PortRepeat? Repeat);
```

`src/Dse.Core/Catalogue/FlowPortDescriptor.cs`:

```csharp
using Dse.Core.Flow;

namespace Dse.Core.Catalogue;

/// <summary>A material inlet or outlet as the catalogue describes it.</summary>
public sealed record FlowPortDescriptor(
    string Name,
    PortDirection Direction,
    PayloadKind Payload,
    string Description,
    PortRepeat? Repeat);
```

`src/Dse.Core/Catalogue/PortSpec.cs`:

```csharp
using Dse.Core.Flow;

namespace Dse.Core.Catalogue;

/// <summary>
/// Factories for port descriptors. Not called <c>Ports</c>: every component inherits an
/// instance property of that name, which would shadow it inside a descriptor initialiser.
/// </summary>
public static class PortSpec
{
    public static PortDescriptor In<T>(string name, string unit = "", string description = "", bool required = false, PortRepeat? repeat = null)
        where T : unmanaged =>
        new(Valid(name), PortDirection.In, ValueTypeName(typeof(T)), unit, description, required, repeat);

    public static PortDescriptor Out<T>(string name, string unit = "", string description = "", PortRepeat? repeat = null)
        where T : unmanaged =>
        new(Valid(name), PortDirection.Out, ValueTypeName(typeof(T)), unit, description, false, repeat);

    public static FlowPortDescriptor Inlet(string name, PayloadKind payload, string description = "", PortRepeat? repeat = null) =>
        new(Valid(name), PortDirection.In, payload, description, repeat);

    public static FlowPortDescriptor Outlet(string name, PayloadKind payload, string description = "", PortRepeat? repeat = null) =>
        new(Valid(name), PortDirection.Out, payload, description, repeat);

    /// <summary><c>bool</c>, <c>double</c>, <c>int</c>, <c>long</c>, or <c>enum:TypeName</c>.</summary>
    public static string ValueTypeName(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (type == typeof(bool))
        {
            return "bool";
        }

        if (type == typeof(double))
        {
            return "double";
        }

        if (type == typeof(int))
        {
            return "int";
        }

        if (type == typeof(long))
        {
            return "long";
        }

        return type.IsEnum ? $"enum:{type.Name}" : type.Name;
    }

    private static string Valid(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return name;
    }
}
```

`src/Dse.Core/Catalogue/TagEntry.cs`:

```csharp
using Dse.Io;

namespace Dse.Core.Catalogue;

/// <summary>
/// A tag a type declares, named relative to the component (for a composite, the
/// alias or <c>Leaf.Tag</c>). Units that depend on a parameter are left empty.
/// </summary>
public sealed record TagEntry(string Name, TagKind Kind, TagAccess Access, string Unit = "", PortRepeat? Repeat = null);
```

`src/Dse.Core/Catalogue/TelemetryKey.cs`:

```csharp
namespace Dse.Core.Catalogue;

/// <summary>A telemetry channel a type registers, named relative to the component.</summary>
public sealed record TelemetryKey(string Name, string Unit = "");
```

- [ ] **Step 5: Write the three descriptors**

`src/Dse.Core/Catalogue/ParameterValues.cs` — a stub so the factory delegates
compile; Task 3 replaces this file:

```csharp
namespace Dse.Core.Catalogue;

/// <summary>The parsed, defaulted, resolved parameters a factory reads.</summary>
public sealed class ParameterValues
{
    internal ParameterValues()
    {
    }
}
```

`src/Dse.Core/Catalogue/ComponentDescriptor.cs`:

```csharp
using Dse.Core.Faults;
using Dse.Core.Graph;

namespace Dse.Core.Catalogue;

/// <summary>
/// Everything a tool or an agent needs to know about a component type, plus
/// the factory that builds one from parsed parameters. Hand-written, beside
/// the constructor it must match; <c>CatalogueConformance</c> keeps it honest.
/// </summary>
public sealed class ComponentDescriptor
{
    public ComponentDescriptor(
        string type,
        ComponentCategory category,
        string description,
        Func<string, ParameterValues, ISimNode> factory)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ArgumentNullException.ThrowIfNull(factory);
        Type = type;
        Category = category;
        Description = description;
        Factory = factory;
    }

    /// <summary>Kebab-case, unique among components: <c>belt-scale</c>.</summary>
    public string Type { get; }

    public ComponentCategory Category { get; }

    public string Description { get; }

    public Func<string, ParameterValues, ISimNode> Factory { get; }

    public IReadOnlyList<ParameterDescriptor> Parameters { get; init; } = [];

    public IReadOnlyList<PortDescriptor> Ports { get; init; } = [];

    public IReadOnlyList<FlowPortDescriptor> FlowPorts { get; init; } = [];

    public IReadOnlyList<FaultDescriptor> Faults { get; init; } = [];

    public IReadOnlyList<TelemetryKey> Telemetry { get; init; } = [];

    public IReadOnlyList<TagEntry> Tags { get; init; } = [];

    /// <summary>Capabilities an instance supplies to a <see cref="ParameterKind.Reference"/>, directly or as a provider.</summary>
    public IReadOnlyList<Type> Provides { get; init; } = [];
}
```

`src/Dse.Core/Catalogue/ObjectDescriptor.cs`:

```csharp
namespace Dse.Core.Catalogue;

/// <summary>A nested, typed value a component takes — a transform, a hold condition.</summary>
public sealed class ObjectDescriptor
{
    public ObjectDescriptor(string slot, string type, string description, Func<ParameterValues, object> factory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slot);
        ArgumentNullException.ThrowIfNull(type);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ArgumentNullException.ThrowIfNull(factory);
        Slot = slot;
        Type = type;
        Description = description;
        Factory = factory;
    }

    public string Slot { get; }

    /// <summary>Kebab-case, unique within the slot.</summary>
    public string Type { get; }

    public string Description { get; }

    public Func<ParameterValues, object> Factory { get; }

    public IReadOnlyList<ParameterDescriptor> Parameters { get; init; } = [];
}
```

`src/Dse.Core/Catalogue/MaterialDescriptor.cs`:

```csharp
using Dse.Core.Flow;

namespace Dse.Core.Catalogue;

/// <summary>A material a plant can name, with the properties a source gives it by default.</summary>
public sealed record MaterialDescriptor(MaterialType Material, MaterialProperties Properties, string Description);
```

- [ ] **Step 6: Write the module interface, the builder and the catalogue**

`src/Dse.Core/Catalogue/ICatalogueModule.cs`:

```csharp
namespace Dse.Core.Catalogue;

/// <summary>One assembly's contribution to a catalogue. Needs a public parameterless constructor to be loaded by the CLI.</summary>
public interface ICatalogueModule
{
    /// <summary>Shown in duplicate-name errors and in the export.</summary>
    string Name { get; }

    void Register(CatalogueBuilder builder);
}
```

`src/Dse.Core/Catalogue/CatalogueBuilder.cs`:

```csharp
using System.Text.RegularExpressions;

namespace Dse.Core.Catalogue;

/// <summary>Collects descriptors, module by module, and rejects clashes as they are added.</summary>
public sealed partial class CatalogueBuilder
{
    internal const string Direct = "(direct)";

    private readonly Dictionary<string, (ComponentDescriptor Descriptor, string Module)> _components = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Slot, string Type), (ObjectDescriptor Descriptor, string Module)> _objects = [];
    private readonly Dictionary<string, (MaterialDescriptor Descriptor, string Module)> _materials = new(StringComparer.Ordinal);
    private readonly List<string> _modules = [];
    private string _current = Direct;

    public CatalogueBuilder Add<TModule>()
        where TModule : ICatalogueModule, new() => Add(new TModule());

    public CatalogueBuilder Add(ICatalogueModule module)
    {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentException.ThrowIfNullOrWhiteSpace(module.Name, nameof(module));
        if (_modules.Contains(module.Name, StringComparer.Ordinal))
        {
            throw new InvalidOperationException($"Module '{module.Name}' has already been added to this catalogue.");
        }

        _modules.Add(module.Name);
        string previous = _current;
        _current = module.Name;
        try
        {
            module.Register(this);
        }
        finally
        {
            _current = previous;
        }

        return this;
    }

    public CatalogueBuilder Add(ComponentDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        RequireKebabCase(descriptor.Type, "Component type");
        RequireUniqueParameters(descriptor.Parameters, $"component '{descriptor.Type}'");
        RequireUniquePortNames(descriptor);
        if (_components.TryGetValue(descriptor.Type, out var existing))
        {
            throw Duplicate("Component type", descriptor.Type, existing.Module);
        }

        _components[descriptor.Type] = (descriptor, _current);
        return this;
    }

    public CatalogueBuilder Add(ObjectDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        RequireKebabCase(descriptor.Type, $"Object type in slot '{descriptor.Slot}'");
        RequireUniqueParameters(descriptor.Parameters, $"{descriptor.Slot} '{descriptor.Type}'");
        if (descriptor.Parameters.Any(p => string.Equals(p.Name, "type", StringComparison.Ordinal)))
        {
            throw new ArgumentException(
                $"{descriptor.Slot} '{descriptor.Type}' declares a parameter named 'type', which is the key that selects it. Rename the parameter.",
                nameof(descriptor));
        }

        if (_objects.TryGetValue((descriptor.Slot, descriptor.Type), out var existing))
        {
            throw Duplicate($"Object type in slot '{descriptor.Slot}'", descriptor.Type, existing.Module);
        }

        _objects[(descriptor.Slot, descriptor.Type)] = (descriptor, _current);
        return this;
    }

    public CatalogueBuilder Add(MaterialDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        string name = descriptor.Material.Name;
        if (_materials.TryGetValue(name, out var existing))
        {
            throw Duplicate("Material", name, existing.Module);
        }

        _materials[name] = (descriptor, _current);
        return this;
    }

    public ComponentCatalogue Build() => new(
        _components.Values.OrderBy(e => e.Descriptor.Type, StringComparer.Ordinal).ToList(),
        _objects.Values
            .OrderBy(e => e.Descriptor.Slot, StringComparer.Ordinal)
            .ThenBy(e => e.Descriptor.Type, StringComparer.Ordinal)
            .ToList(),
        _materials.Values.OrderBy(e => e.Descriptor.Material.Name, StringComparer.Ordinal).ToList(),
        _modules.ToList());

    private InvalidOperationException Duplicate(string what, string name, string firstModule) => new(
        $"{what} '{name}' is registered twice: by module '{firstModule}' and by module '{_current}'. " +
        $"Rename one of them; type names are unique across a catalogue.");

    private static void RequireKebabCase(string type, string what)
    {
        if (!KebabCase().IsMatch(type))
        {
            throw new ArgumentException(
                $"{what} '{type}' must be kebab-case: lowercase letters and digits in groups joined by single hyphens.",
                nameof(type));
        }
    }

    private static void RequireUniqueParameters(IReadOnlyList<ParameterDescriptor> parameters, string owner)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (ParameterDescriptor parameter in parameters)
        {
            if (!seen.Add(parameter.Name))
            {
                throw new ArgumentException($"Parameter '{parameter.Name}' is declared twice on {owner}.", nameof(parameters));
            }

            if (parameter.Children.Count > 0)
            {
                RequireUniqueParameters(parameter.Children, $"{owner}, group '{parameter.Name}'");
            }
        }
    }

    private static void RequireUniquePortNames(ComponentDescriptor descriptor)
    {
        var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string name in descriptor.Ports.Select(p => p.Name).Concat(descriptor.FlowPorts.Select(p => p.Name)))
        {
            if (seen.TryGetValue(name, out string? first))
            {
                throw new ArgumentException(
                    $"Component type '{descriptor.Type}' declares ports '{first}' and '{name}', which differ only in case. " +
                    $"A plant file matches port names ignoring case, so they must be distinct.",
                    nameof(descriptor));
            }

            seen[name] = name;
        }
    }

    [GeneratedRegex("^[a-z][a-z0-9]*(-[a-z0-9]+)*$")]
    private static partial Regex KebabCase();
}
```

`src/Dse.Core/Catalogue/ComponentCatalogue.cs`:

```csharp
using System.Diagnostics.CodeAnalysis;

namespace Dse.Core.Catalogue;

/// <summary>An immutable set of descriptors. Every list is sorted, so anything generated from it is deterministic.</summary>
public sealed class ComponentCatalogue
{
    private readonly Dictionary<string, (ComponentDescriptor Descriptor, string Module)> _components;
    private readonly Dictionary<(string Slot, string Type), (ObjectDescriptor Descriptor, string Module)> _objects;
    private readonly Dictionary<string, (MaterialDescriptor Descriptor, string Module)> _materials;

    internal ComponentCatalogue(
        List<(ComponentDescriptor Descriptor, string Module)> components,
        List<(ObjectDescriptor Descriptor, string Module)> objects,
        List<(MaterialDescriptor Descriptor, string Module)> materials,
        List<string> modules)
    {
        Components = components.Select(e => e.Descriptor).ToList();
        Objects = objects.Select(e => e.Descriptor).ToList();
        Materials = materials.Select(e => e.Descriptor).ToList();
        Modules = modules;
        Slots = Objects.Select(o => o.Slot).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        _components = components.ToDictionary(e => e.Descriptor.Type, StringComparer.Ordinal);
        _objects = objects.ToDictionary(e => (e.Descriptor.Slot, e.Descriptor.Type));
        _materials = materials.ToDictionary(e => e.Descriptor.Material.Name, StringComparer.Ordinal);
    }

    /// <summary>Sorted by type name.</summary>
    public IReadOnlyList<ComponentDescriptor> Components { get; }

    /// <summary>Sorted by slot, then type name.</summary>
    public IReadOnlyList<ObjectDescriptor> Objects { get; }

    /// <summary>Sorted by material name.</summary>
    public IReadOnlyList<MaterialDescriptor> Materials { get; }

    /// <summary>In the order they were added.</summary>
    public IReadOnlyList<string> Modules { get; }

    /// <summary>Every slot that has at least one object, sorted.</summary>
    public IReadOnlyList<string> Slots { get; }

    public bool TryGetComponent(string type, [NotNullWhen(true)] out ComponentDescriptor? descriptor)
    {
        bool found = _components.TryGetValue(type, out var entry);
        descriptor = found ? entry.Descriptor : null;
        return found;
    }

    public bool TryGetObject(string slot, string type, [NotNullWhen(true)] out ObjectDescriptor? descriptor)
    {
        bool found = _objects.TryGetValue((slot, type), out var entry);
        descriptor = found ? entry.Descriptor : null;
        return found;
    }

    public IReadOnlyList<ObjectDescriptor> ObjectsIn(string slot) =>
        Objects.Where(o => string.Equals(o.Slot, slot, StringComparison.Ordinal)).ToList();

    public bool TryGetMaterial(string name, [NotNullWhen(true)] out MaterialDescriptor? descriptor)
    {
        bool found = _materials.TryGetValue(name, out var entry);
        descriptor = found ? entry.Descriptor : null;
        return found;
    }

    /// <summary>The module that registered a descriptor, or <c>(direct)</c>.</summary>
    public string ModuleOf(ComponentDescriptor descriptor) => _components[descriptor.Type].Module;

    /// <inheritdoc cref="ModuleOf(ComponentDescriptor)"/>
    public string ModuleOf(ObjectDescriptor descriptor) => _objects[(descriptor.Slot, descriptor.Type)].Module;

    /// <inheritdoc cref="ModuleOf(ComponentDescriptor)"/>
    public string ModuleOf(MaterialDescriptor descriptor) => _materials[descriptor.Material.Name].Module;
}
```

- [ ] **Step 7: Run the tests**

Run: `dotnet test tests/Dse.Core.Tests --nologo --filter FullyQualifiedName~CatalogueModelTests`
Expected: PASS, 19 test cases (13 methods; the two theories contribute 4 each).

- [ ] **Step 8: Build Release and commit**

Run: `dotnet build Dse.sln -c Release --nologo` — expect `0 Warning(s)`, `0 Error(s)`.

```bash
git add src/Dse.Core/Catalogue tests/Dse.Core.Tests/Catalogue
git commit -m "feat(catalogue): add descriptors, parameters and the catalogue builder"
```

---
### Task 3: Parameter binding

One walker turns a `JsonElement` into `ParameterValues` against a parameter
schema, collecting every issue at that level instead of stopping at the first.
The loader runs it twice per component: once with `construct: false` to check
structure, once with `construct: true` to resolve references and build nested
objects (R33). Conformance (Task 4) runs it to build probe instances.

**Files:**
- Create: `src/Dse.Core/Catalogue/Suggest.cs`
- Create: `src/Dse.Core/Catalogue/Capabilities.cs`
- Create: `src/Dse.Core/Catalogue/BindingIssue.cs`
- Create: `src/Dse.Core/Catalogue/BindingContext.cs`
- Replace: `src/Dse.Core/Catalogue/ParameterValues.cs` (Task 2's stub)
- Create: `src/Dse.Core/Catalogue/ParameterBinder.cs`
- Test: `tests/Dse.Core.Tests/Catalogue/SuggestTests.cs`
- Test: `tests/Dse.Core.Tests/Catalogue/ParameterBinderTests.cs`

**Interfaces:**
- Consumes: Task 2's model; `ICapabilityProvider` from Task 1.
- Produces:
  - `static class Suggest { string? Closest(string given, IEnumerable<string> candidates); string List(IEnumerable<string> names, int max = 8); string Fix(string given, IEnumerable<string> candidates, string noun); }`
  - `static class Capabilities { bool TryGet(ISimNode node, Type capability, out object? instance); }`
  - `enum BindingIssueKind { UnknownKey, UnknownType, BadParameter, UnknownMaterial, UnknownState, MissingReference, MissingCapability, Rejected }`
  - `sealed record BindingIssue(BindingIssueKind Kind, string Path, string Message, string Fix)`
  - `sealed class BindingContext(ComponentCatalogue catalogue)` — `Catalogue`, `AddMaterial(MaterialDescriptor)`, `AddNode(ISimNode)`, `TryGetMaterial`, `MaterialNames`, `TryGetNode`, `NodeIds`
  - `sealed class ParameterValues` — `Has`, `Double`, `DoubleOr`, `Int`, `IntOr`, `Bool`, `String`, `Strings`, `Group`, `Groups`, `Reference<T>`, `Material`, `MaterialOrNull`, `MaterialProperties`, `StateIndex`, `Object<T>`, `Objects<T>`
  - `static class ParameterBinder { ParameterValues? Bind(IReadOnlyList<ParameterDescriptor> schema, JsonElement json, string path, BindingContext context, bool construct, List<BindingIssue> issues); }`
    — returns null exactly when it added an issue. `json` may be `default` (an absent `parameters` object), which binds as `{}`.

- [ ] **Step 1: Write the failing `Suggest` tests**

Create `tests/Dse.Core.Tests/Catalogue/SuggestTests.cs`:

```csharp
using Dse.Core.Catalogue;

namespace Dse.Core.Tests.Catalogue;

public class SuggestTests
{
    [Fact]
    public void FindsATransposition()
    {
        Assert.Equal("Start", Suggest.Closest("Strat", ["Reset", "Start", "Speed"]));
    }

    [Fact]
    public void IgnoresCase()
    {
        Assert.Equal("CV001", Suggest.Closest("cv001", ["CV001", "CV002"]));
    }

    [Fact]
    public void GivesUpWhenNothingIsNear()
    {
        Assert.Null(Suggest.Closest("hopper", ["Start", "Reset"]));
    }

    [Fact]
    public void BreaksATieByOrdinalOrder()
    {
        Assert.Equal("CV001", Suggest.Closest("CV00", ["CV002", "CV001"]));
    }

    [Fact]
    public void ListsSortedAndTruncates()
    {
        Assert.Equal("a, b, c", Suggest.List(["c", "a", "b"]));
        Assert.Equal("a, b, … (4 in all)", Suggest.List(["d", "c", "a", "b"], max: 2));
    }

    [Fact]
    public void WritesAFixSentence()
    {
        Assert.Equal("Use one of CV001, CV002 — 'CV001' is closest.", Suggest.Fix("CV01", ["CV002", "CV001"], "components"));
        Assert.Equal("Use one of Reset, Start.", Suggest.Fix("hopper", ["Start", "Reset"], "ports"));
        Assert.Equal("There are no materials to choose from; define one first.", Suggest.Fix("ore", [], "materials"));
    }
}
```

- [ ] **Step 2: Run and see them fail to compile**

Run: `dotnet test tests/Dse.Core.Tests --nologo --filter FullyQualifiedName~SuggestTests`
Expected: build FAILS — `Suggest` does not exist.

- [ ] **Step 3: Write `Suggest`**

Create `src/Dse.Core/Catalogue/Suggest.cs`:

```csharp
using System.Globalization;

namespace Dse.Core.Catalogue;

/// <summary>Turns "that name does not exist" into "did you mean". Deterministic: ties break by ordinal order.</summary>
public static class Suggest
{
    /// <summary>The candidate nearest to <paramref name="given"/> ignoring case, or null when none is near enough to be a typo.</summary>
    public static string? Closest(string given, IEnumerable<string> candidates)
    {
        ArgumentNullException.ThrowIfNull(given);
        ArgumentNullException.ThrowIfNull(candidates);

        int limit = Math.Max(2, given.Length / 3);
        string? best = null;
        int bestDistance = int.MaxValue;
        foreach (string candidate in candidates.Order(StringComparer.Ordinal))
        {
            int distance = Distance(given.ToUpperInvariant(), candidate.ToUpperInvariant());
            if (distance <= limit && distance < bestDistance)
            {
                best = candidate;
                bestDistance = distance;
            }
        }

        return best;
    }

    /// <summary>Names sorted and comma-joined; beyond <paramref name="max"/> the rest become a count.</summary>
    public static string List(IEnumerable<string> names, int max = 8)
    {
        ArgumentNullException.ThrowIfNull(names);
        List<string> sorted = names.Order(StringComparer.Ordinal).ToList();
        if (sorted.Count <= max)
        {
            return string.Join(", ", sorted);
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{string.Join(", ", sorted.Take(max))}, … ({sorted.Count} in all)");
    }

    /// <summary>A whole fix sentence. <paramref name="noun"/> is plural: "components".</summary>
    public static string Fix(string given, IEnumerable<string> candidates, string noun)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        List<string> all = candidates.ToList();
        if (all.Count == 0)
        {
            return $"There are no {noun} to choose from; define one first.";
        }

        string? closest = Closest(given, all);
        return closest is null
            ? $"Use one of {List(all)}."
            : $"Use one of {List(all)} — '{closest}' is closest.";
    }

    private static int Distance(string a, string b)
    {
        // Optimal string alignment: Levenshtein plus adjacent transposition.
        int[,] d = new int[a.Length + 1, b.Length + 1];
        for (int i = 0; i <= a.Length; i++)
        {
            d[i, 0] = i;
        }

        for (int j = 0; j <= b.Length; j++)
        {
            d[0, j] = j;
        }

        for (int i = 1; i <= a.Length; i++)
        {
            for (int j = 1; j <= b.Length; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                int best = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
                if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1])
                {
                    best = Math.Min(best, d[i - 2, j - 2] + 1);
                }

                d[i, j] = best;
            }
        }

        return d[a.Length, b.Length];
    }
}
```

Run: `dotnet test tests/Dse.Core.Tests --nologo --filter FullyQualifiedName~SuggestTests`
Expected: PASS, 6 tests.

- [ ] **Step 4: Write the failing binder tests**

Create `tests/Dse.Core.Tests/Catalogue/ParameterBinderTests.cs`:

```csharp
using System.Text.Json;
using Dse.Core.Catalogue;
using Dse.Core.Flow;
using Dse.Core.Graph;

namespace Dse.Core.Tests.Catalogue;

public class ParameterBinderTests
{
    private static readonly GroupDefinition Rating = new(
        "Rating",
        Param.Double("powerW", "Nameplate power.", "W", min: 0.0, exclusiveMin: true),
        Param.Double("droop", "Speed droop.", @default: 0.03, min: 0.0, max: 1.0));

    private static readonly GroupDefinition Line = new(
        "Line",
        Param.String("inlet", "Inlet name."),
        Param.Double("massKg", "Mass.", "kg", min: 0.0, exclusiveMin: true));

    private static readonly MaterialType Dough = new("dough", PayloadKind.Bulk, "proof", "bake");
    private static readonly MaterialType Loaf = new("loaf", PayloadKind.Discrete);

    private sealed record Timer(double Seconds);

    private sealed record StateFloor(int Index, double Value);

    private sealed record All(IReadOnlyList<object> Conditions);

    private static BindingContext Context()
    {
        ComponentCatalogue catalogue = new CatalogueBuilder()
            .Add(new ObjectDescriptor(ObjectSlots.Hold, "for-seconds", "Waits.", p => new Timer(p.Double("seconds")))
            {
                Parameters = [Param.Double("seconds", "How long.", "s", min: 0.0)],
            })
            .Add(new ObjectDescriptor(ObjectSlots.Hold, "state-at-least", "Waits for a state.", p => new StateFloor(p.StateIndex("state"), p.Double("value")))
            {
                Parameters =
                [
                    Param.Material("material", "Whose state."),
                    Param.MaterialState("state", "Which state.", "material"),
                    Param.Double("value", "Threshold."),
                ],
            })
            .Add(new ObjectDescriptor(ObjectSlots.Hold, "all", "Every condition.", p => new All(p.Objects<object>("conditions")))
            {
                Parameters = [Param.ObjectList("conditions", "The conditions.", ObjectSlots.Hold, minCount: 1)],
            })
            .Build();

        return new BindingContext(catalogue)
            .AddMaterial(new MaterialDescriptor(Dough, new MaterialProperties(1100.0, 0.4, 25.0), "Dough."))
            .AddMaterial(new MaterialDescriptor(Loaf, default, "A loaf."))
            .AddNode(new BulkBelt("BELT", 10.0, 0.5, 2.0, 100.0))
            .AddNode(new UnitDelay<bool>("DELAY"));
    }

    private static (ParameterValues? Values, List<BindingIssue> Issues) Bind(
        IReadOnlyList<ParameterDescriptor> schema, string json, bool construct = true)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        var issues = new List<BindingIssue>();
        ParameterValues? values = ParameterBinder.Bind(schema, document.RootElement, "$.p", Context(), construct, issues);
        return (values, issues);
    }

    [Fact]
    public void AppliesDefaultsAndReadsScalars()
    {
        ParameterDescriptor[] schema =
        [
            Param.Double("ratio", "Ratio."),
            Param.Double("efficiency", "Efficiency.", @default: 0.95),
            Param.Int("channels", "Channels.", @default: 2),
            Param.Bool("enabled", "Enabled.", @default: true),
            Param.String("unit", "Unit.", @default: "A"),
            Param.Enum("mode", "Mode.", ["fast", "slow"], @default: "slow"),
            Param.Double("capacityKg", "Omit for unlimited.", optional: true),
        ];

        var (values, issues) = Bind(schema, """{ "ratio": 20, "channels": 3 }""");

        Assert.Empty(issues);
        Assert.NotNull(values);
        Assert.Equal(20.0, values.Double("ratio"));
        Assert.Equal(0.95, values.Double("efficiency"));
        Assert.Equal(3, values.Int("channels"));
        Assert.True(values.Bool("enabled"));
        Assert.Equal("A", values.String("unit"));
        Assert.Equal("slow", values.String("mode"));
        Assert.False(values.Has("capacityKg"));
        Assert.Equal(double.PositiveInfinity, values.DoubleOr("capacityKg", double.PositiveInfinity));
    }

    [Fact]
    public void AnAbsentParametersObjectBindsAsEmpty()
    {
        var issues = new List<BindingIssue>();

        ParameterValues? values = ParameterBinder.Bind(
            [Param.Double("efficiency", "Efficiency.", @default: 0.95)], default, "$.p", Context(), true, issues);

        Assert.Empty(issues);
        Assert.Equal(0.95, values!.Double("efficiency"));
    }

    [Fact]
    public void CollectsEveryIssueAtOnce()
    {
        ParameterDescriptor[] schema =
        [
            Param.Double("ratio", "Ratio.", min: 1.0),
            Param.Int("channels", "Channels.", min: 1),
            Param.Bool("enabled", "Enabled."),
        ];

        var (values, issues) = Bind(schema, """{ "ratio": 0.5, "channels": 1.5, "enabeld": true }""");

        Assert.Null(values);
        Assert.Equal(4, issues.Count);
        Assert.Contains(issues, i => i.Kind == BindingIssueKind.UnknownKey && i.Path == "$.p.enabeld" && i.Fix.Contains("'enabled' is closest", StringComparison.Ordinal));
        Assert.Contains(issues, i => i.Kind == BindingIssueKind.BadParameter && i.Path == "$.p.ratio" && i.Message.Contains("0.5", StringComparison.Ordinal));
        Assert.Contains(issues, i => i.Kind == BindingIssueKind.BadParameter && i.Path == "$.p.channels" && i.Message.Contains("whole number", StringComparison.Ordinal));
        Assert.Contains(issues, i => i.Kind == BindingIssueKind.BadParameter && i.Path == "$.p.enabled" && i.Message.Contains("required", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("""{ "v": 0 }""", false)]
    [InlineData("""{ "v": 0.001 }""", true)]
    [InlineData("""{ "v": 1 }""", true)]
    [InlineData("""{ "v": 1.001 }""", false)]
    public void HonoursExclusiveAndInclusiveBounds(string json, bool ok)
    {
        var (_, issues) = Bind([Param.Double("v", "Yield.", min: 0.0, max: 1.0, exclusiveMin: true)], json);

        Assert.Equal(ok, issues.Count == 0);
    }

    [Fact]
    public void RejectsAWrongJsonTypeAndSaysWhatItWanted()
    {
        var (_, issues) = Bind([Param.Double("ratio", "Ratio.")], """{ "ratio": "20" }""");

        BindingIssue issue = Assert.Single(issues);
        Assert.Equal(BindingIssueKind.BadParameter, issue.Kind);
        Assert.Contains("a number", issue.Message, StringComparison.Ordinal);
        Assert.Contains("a string", issue.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsAnEnumValueItDoesNotAllow()
    {
        var (_, issues) = Bind([Param.Enum("mode", "Mode.", ["fast", "slow"])], """{ "mode": "fats" }""");

        BindingIssue issue = Assert.Single(issues);
        Assert.Contains("'fast' is closest", issue.Fix, StringComparison.Ordinal);
    }

    [Fact]
    public void BindsAGroupAndFillsAnOmittedOneWithDefaults()
    {
        var optionalGroup = new GroupDefinition("Opt", Param.Double("a", "A.", @default: 7.0));
        ParameterDescriptor[] schema = [Param.Group("motor", "Rating.", Rating), Param.Group("extra", "Extra.", optionalGroup)];

        var (values, issues) = Bind(schema, """{ "motor": { "powerW": 750 } }""");

        Assert.Empty(issues);
        Assert.Equal(750.0, values!.Group("motor").Double("powerW"));
        Assert.Equal(0.03, values.Group("motor").Double("droop"));
        Assert.Equal(7.0, values.Group("extra").Double("a"));
    }

    [Fact]
    public void PathsReachIntoGroupsAndLists()
    {
        ParameterDescriptor[] schema = [Param.Group("motor", "Rating.", Rating), Param.GroupList("recipe", "Recipe.", Line, minCount: 1)];

        var (_, issues) = Bind(schema, """{ "motor": { "powerW": 0 }, "recipe": [ { "inlet": "Flour", "massKg": 5 }, { "inlet": "Water" } ] }""");

        Assert.Equal(2, issues.Count);
        Assert.Contains(issues, i => i.Path == "$.p.motor.powerW");
        Assert.Contains(issues, i => i.Path == "$.p.recipe[1].massKg");
    }

    [Fact]
    public void BindsAStringListAndRejectsANonStringEntry()
    {
        var (values, issues) = Bind([Param.StringList("states", "States.")], """{ "states": [ "proof", "bake" ] }""");
        var (_, bad) = Bind([Param.StringList("states", "States.")], """{ "states": [ "proof", 2 ] }""");
        var (absent, _) = Bind([Param.StringList("states", "States.")], "{}");

        Assert.Empty(issues);
        Assert.Equal(["proof", "bake"], values!.Strings("states"));
        Assert.Equal("$.p.states[1]", Assert.Single(bad).Path);
        Assert.Empty(absent!.Strings("states"));
    }

    [Fact]
    public void AListShorterThanItsMinimumIsAnIssue()
    {
        var (_, issues) = Bind([Param.GroupList("recipe", "Recipe.", Line, minCount: 1)], """{ "recipe": [] }""");

        BindingIssue issue = Assert.Single(issues);
        Assert.Contains("at least 1", issue.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolvesAMaterialItsPropertiesAndAState()
    {
        ParameterDescriptor[] schema =
        [
            Param.Material("material", "What.", PayloadKind.Bulk),
            Param.MaterialState("state", "Which.", "material"),
            Param.Material("output", "Optional.", optional: true),
        ];

        var (values, issues) = Bind(schema, """{ "state": "bake", "material": "dough" }""");

        Assert.Empty(issues);
        Assert.Same(Dough, values!.Material("material"));
        Assert.Equal(0.4, values.MaterialProperties("material").Moisture);
        Assert.Equal(1, values.StateIndex("state"));
        Assert.Null(values.MaterialOrNull("output"));
    }

    [Fact]
    public void ReportsUnknownMaterialWrongKindAndUnknownState()
    {
        ParameterDescriptor[] schema =
        [
            Param.Material("a", "A."),
            Param.Material("b", "B.", PayloadKind.Discrete),
            Param.Material("c", "C."),
            Param.MaterialState("state", "Which.", "c"),
        ];

        var (_, issues) = Bind(schema, """{ "a": "duogh", "b": "dough", "c": "dough", "state": "cool" }""");

        Assert.Equal(3, issues.Count);
        Assert.Contains(issues, i => i.Kind == BindingIssueKind.UnknownMaterial && i.Fix.Contains("'dough' is closest", StringComparison.Ordinal));
        Assert.Contains(issues, i => i.Kind == BindingIssueKind.BadParameter && i.Path == "$.p.b" && i.Message.Contains("Bulk", StringComparison.Ordinal));
        Assert.Contains(issues, i => i.Kind == BindingIssueKind.UnknownState && i.Fix.Contains("bake, proof", StringComparison.Ordinal));
    }

    [Fact]
    public void ResolvesAReferenceToItsCapability()
    {
        var (values, issues) = Bind([Param.Reference<IMaterialObservable>("belt", "Belt.")], """{ "belt": "BELT" }""");

        Assert.Empty(issues);
        Assert.IsType<BulkBelt>(values!.Reference<IMaterialObservable>("belt"));
    }

    [Fact]
    public void ReportsAMissingReferenceAndAMissingCapability()
    {
        ParameterDescriptor[] schema = [Param.Reference<IMaterialObservable>("a", "A."), Param.Reference<IMaterialObservable>("b", "B.")];

        var (_, issues) = Bind(schema, """{ "a": "BLET", "b": "DELAY" }""");

        Assert.Equal(2, issues.Count);
        Assert.Contains(issues, i => i.Kind == BindingIssueKind.MissingReference && i.Fix.Contains("'BELT' is closest", StringComparison.Ordinal));
        Assert.Contains(issues, i => i.Kind == BindingIssueKind.MissingCapability && i.Message.Contains("IMaterialObservable", StringComparison.Ordinal));
    }

    [Fact]
    public void InCheckModeAReferenceNeedsOnlyToBeAString()
    {
        var (values, issues) = Bind([Param.Reference<IMaterialObservable>("belt", "Belt.")], """{ "belt": "NOT-BUILT-YET" }""", construct: false);

        Assert.Empty(issues);
        Assert.NotNull(values);
    }

    [Fact]
    public void BuildsNestedObjectsRecursively()
    {
        const string Json = """
            { "hold": { "type": "all", "conditions": [
                { "type": "for-seconds", "seconds": 900 },
                { "type": "state-at-least", "material": "dough", "state": "bake", "value": 1 } ] } }
            """;

        var (values, issues) = Bind([Param.Object("hold", "Hold.", ObjectSlots.Hold)], Json);

        Assert.Empty(issues);
        All all = values!.Object<All>("hold");
        Assert.Equal(new Timer(900.0), all.Conditions[0]);
        Assert.Equal(new StateFloor(1, 1.0), all.Conditions[1]);
    }

    [Fact]
    public void ReportsAnUnknownObjectTypeAndAMissingTypeKey()
    {
        ParameterDescriptor[] schema = [Param.Object("a", "A.", ObjectSlots.Hold), Param.Object("b", "B.", ObjectSlots.Hold)];

        var (_, issues) = Bind(schema, """{ "a": { "type": "for-secnods", "seconds": 1 }, "b": { "seconds": 1 } }""");

        Assert.Equal(2, issues.Count);
        Assert.Contains(issues, i => i.Kind == BindingIssueKind.UnknownType && i.Path == "$.p.a.type" && i.Fix.Contains("'for-seconds' is closest", StringComparison.Ordinal));
        Assert.Contains(issues, i => i.Kind == BindingIssueKind.BadParameter && i.Path == "$.p.b" && i.Message.Contains("\"type\"", StringComparison.Ordinal));
    }

    [Fact]
    public void AnObjectFactoryThatThrowsBecomesARejectedIssue()
    {
        ComponentCatalogue catalogue = new CatalogueBuilder()
            .Add(new ObjectDescriptor(ObjectSlots.Hold, "fussy", "Rejects everything.", p => throw new ArgumentException("Seconds must be even."))
            {
                Parameters = [Param.Double("seconds", "How long.")],
            })
            .Build();
        using JsonDocument document = JsonDocument.Parse("""{ "hold": { "type": "fussy", "seconds": 3 } }""");
        var issues = new List<BindingIssue>();

        ParameterValues? values = ParameterBinder.Bind(
            [Param.Object("hold", "Hold.", ObjectSlots.Hold)], document.RootElement, "$.p", new BindingContext(catalogue), true, issues);

        Assert.Null(values);
        BindingIssue issue = Assert.Single(issues);
        Assert.Equal(BindingIssueKind.Rejected, issue.Kind);
        Assert.Equal("$.p.hold", issue.Path);
        Assert.Contains("Seconds must be even.", issue.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AskingForAnUndeclaredParameterIsAFactoryBug()
    {
        var (values, _) = Bind([Param.Double("ratio", "Ratio.")], """{ "ratio": 2 }""");

        var ex = Assert.Throws<KeyNotFoundException>(() => values!.Double("ration"));
        Assert.Contains("'ration'", ex.Message, StringComparison.Ordinal);
        Assert.Contains("ratio", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AddingAMaterialTwiceIsAnError()
    {
        BindingContext context = Context();

        var ex = Assert.Throws<InvalidOperationException>(
            () => context.AddMaterial(new MaterialDescriptor(new MaterialType("dough", PayloadKind.Bulk), default, "Again.")));
        Assert.Contains("'dough'", ex.Message, StringComparison.Ordinal);
    }
}
```

- [ ] **Step 5: Run and see them fail to compile**

Run: `dotnet test tests/Dse.Core.Tests --nologo --filter FullyQualifiedName~ParameterBinderTests`
Expected: build FAILS — `BindingContext`, `ParameterBinder`, `BindingIssue` do not exist.

- [ ] **Step 6: Write `Capabilities`, `BindingIssue` and `BindingContext`**

`src/Dse.Core/Catalogue/Capabilities.cs`:

```csharp
using Dse.Core.Graph;

namespace Dse.Core.Catalogue;

/// <summary>Finds what a reference needs on a node: the node itself, or something it provides.</summary>
public static class Capabilities
{
    public static bool TryGet(ISimNode node, Type capability, out object? instance)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(capability);
        if (capability.IsInstanceOfType(node))
        {
            instance = node;
            return true;
        }

        if (node is ICapabilityProvider provider && provider.TryGetCapability(capability, out instance) && instance is not null)
        {
            return true;
        }

        instance = null;
        return false;
    }
}
```

`src/Dse.Core/Catalogue/BindingIssue.cs`:

```csharp
namespace Dse.Core.Catalogue;

public enum BindingIssueKind
{
    /// <summary>A key the schema does not declare.</summary>
    UnknownKey,
    /// <summary>An object <c>"type"</c> the slot does not have.</summary>
    UnknownType,
    /// <summary>Missing, wrong JSON type, out of range, not an allowed value, list too short.</summary>
    BadParameter,
    UnknownMaterial,
    UnknownState,
    /// <summary>A reference to a node the context does not hold. Construct mode only.</summary>
    MissingReference,
    /// <summary>The referenced node cannot supply what the parameter needs. Construct mode only.</summary>
    MissingCapability,
    /// <summary>A factory threw <see cref="ArgumentException"/>. Construct mode only.</summary>
    Rejected,
}

/// <summary>One thing wrong with a parameter, where it is, and what to do about it.</summary>
public sealed record BindingIssue(BindingIssueKind Kind, string Path, string Message, string Fix);
```

`src/Dse.Core/Catalogue/BindingContext.cs`:

```csharp
using System.Diagnostics.CodeAnalysis;
using Dse.Core.Graph;

namespace Dse.Core.Catalogue;

/// <summary>
/// What names mean while parameters are bound: the catalogue, the materials in
/// scope (the catalogue's plus any added), and the nodes built so far.
/// </summary>
public sealed class BindingContext
{
    private readonly Dictionary<string, MaterialDescriptor> _materials = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ISimNode> _nodes = new(StringComparer.Ordinal);

    public BindingContext(ComponentCatalogue catalogue)
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        Catalogue = catalogue;
        foreach (MaterialDescriptor material in catalogue.Materials)
        {
            _materials[material.Material.Name] = material;
        }
    }

    public ComponentCatalogue Catalogue { get; }

    public IReadOnlyList<string> MaterialNames => _materials.Keys.Order(StringComparer.Ordinal).ToList();

    public IReadOnlyList<string> NodeIds => _nodes.Keys.Order(StringComparer.Ordinal).ToList();

    public BindingContext AddMaterial(MaterialDescriptor material)
    {
        ArgumentNullException.ThrowIfNull(material);
        if (!_materials.TryAdd(material.Material.Name, material))
        {
            throw new InvalidOperationException(
                $"Material '{material.Material.Name}' is already defined. Material names are unique across the catalogue and the plant.");
        }

        return this;
    }

    public BindingContext AddNode(ISimNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (!_nodes.TryAdd(node.Id, node))
        {
            throw new InvalidOperationException($"A node with id '{node.Id}' has already been added.");
        }

        return this;
    }

    public bool TryGetMaterial(string name, [NotNullWhen(true)] out MaterialDescriptor? material) =>
        _materials.TryGetValue(name, out material);

    public bool TryGetNode(string id, [NotNullWhen(true)] out ISimNode? node) => _nodes.TryGetValue(id, out node);
}
```

- [ ] **Step 7: Replace `ParameterValues`**

Overwrite `src/Dse.Core/Catalogue/ParameterValues.cs`:

```csharp
using Dse.Core.Flow;

namespace Dse.Core.Catalogue;

/// <summary>
/// The parsed, defaulted, range-checked, resolved parameters a factory reads.
/// Asking for a name the descriptor does not declare throws: that is a bug in
/// the factory, and the conformance test finds it.
/// </summary>
public sealed class ParameterValues
{
    private readonly Dictionary<string, object?> _values;
    private readonly string[] _declared;

    internal ParameterValues(Dictionary<string, object?> values, IEnumerable<string> declared)
    {
        _values = values;
        _declared = declared.ToArray();
    }

    /// <summary>True when the parameter was given or defaulted; false for an optional one left out.</summary>
    public bool Has(string name)
    {
        RequireDeclared(name);
        return _values.ContainsKey(name);
    }

    public double Double(string name) => Get<double>(name);

    public double DoubleOr(string name, double fallback) => Has(name) ? Get<double>(name) : fallback;

    public int Int(string name) => Get<int>(name);

    public int IntOr(string name, int fallback) => Has(name) ? Get<int>(name) : fallback;

    public bool Bool(string name) => Get<bool>(name);

    /// <summary>A <see cref="ParameterKind.String"/> or <see cref="ParameterKind.Enum"/> value.</summary>
    public string String(string name) => Get<string>(name);

    public IReadOnlyList<string> Strings(string name) => Get<IReadOnlyList<string>>(name);

    public ParameterValues Group(string name) => Get<ParameterValues>(name);

    public IReadOnlyList<ParameterValues> Groups(string name) => Get<IReadOnlyList<ParameterValues>>(name);

    /// <summary>The capability the referenced component supplied.</summary>
    public T Reference<T>(string name)
        where T : class => Get<T>(name);

    public MaterialType Material(string name) => Get<MaterialDescriptor>(name).Material;

    public MaterialType? MaterialOrNull(string name) => Has(name) ? Material(name) : null;

    /// <summary>The default properties the named material was defined with.</summary>
    public MaterialProperties MaterialProperties(string name) => Get<MaterialDescriptor>(name).Properties;

    /// <summary>The index of a <see cref="ParameterKind.MaterialState"/> in its material's state schema.</summary>
    public int StateIndex(string name) => Get<int>(name);

    public T Object<T>(string name)
        where T : class => Get<T>(name);

    public IReadOnlyList<T> Objects<T>(string name)
        where T : class => Get<IReadOnlyList<object>>(name).Cast<T>().ToList();

    private T Get<T>(string name)
    {
        RequireDeclared(name);
        if (!_values.TryGetValue(name, out object? value))
        {
            throw new KeyNotFoundException(
                $"Parameter '{name}' is optional and was left out. Test for it with Has, or read it with an ...Or method.");
        }

        if (value is not T typed)
        {
            throw new InvalidCastException(
                $"Parameter '{name}' holds {value?.GetType().Name ?? "nothing"}, not {typeof(T).Name}. " +
                $"Read it with the accessor that matches its declared kind.");
        }

        return typed;
    }

    private void RequireDeclared(string name)
    {
        if (Array.IndexOf(_declared, name) < 0)
        {
            throw new KeyNotFoundException(
                $"The factory asked for parameter '{name}', which its descriptor does not declare. " +
                $"Declared: {string.Join(", ", _declared)}.");
        }
    }
}
```

Task 2's tests construct no `ParameterValues`, so replacing the stub's
constructor breaks nothing.

- [ ] **Step 8: Write `ParameterBinder`**

Create `src/Dse.Core/Catalogue/ParameterBinder.cs`:

```csharp
using System.Globalization;
using System.Text.Json;
using Dse.Core.Graph;

namespace Dse.Core.Catalogue;

/// <summary>
/// Binds JSON to a parameter schema. Every issue found at a level is reported
/// before the binder gives up on that level; nothing here throws for bad input.
/// </summary>
public static class ParameterBinder
{
    private const string TypeKey = "type";

    /// <summary>
    /// Returns the bound values, or null exactly when it added to <paramref name="issues"/>.
    /// With <paramref name="construct"/> false, references are only checked to be
    /// strings and object factories are not called — the result is for checking,
    /// not for a factory.
    /// </summary>
    public static ParameterValues? Bind(
        IReadOnlyList<ParameterDescriptor> schema,
        JsonElement json,
        string path,
        BindingContext context,
        bool construct,
        List<BindingIssue> issues)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(issues);
        return BindObject(schema, json, path, context, construct, issues, allowTypeKey: false);
    }

    private static ParameterValues? BindObject(
        IReadOnlyList<ParameterDescriptor> schema,
        JsonElement json,
        string path,
        BindingContext context,
        bool construct,
        List<BindingIssue> issues,
        bool allowTypeKey)
    {
        int before = issues.Count;
        bool absent = json.ValueKind == JsonValueKind.Undefined;
        if (!absent && json.ValueKind != JsonValueKind.Object)
        {
            issues.Add(Bad(path, $"This must be an object, but it is {Describe(json)}.", "Write it as { … } with one key per parameter."));
            return null;
        }

        if (!absent)
        {
            foreach (JsonProperty property in json.EnumerateObject())
            {
                bool declared = schema.Any(p => string.Equals(p.Name, property.Name, StringComparison.Ordinal));
                if (!declared && !(allowTypeKey && string.Equals(property.Name, TypeKey, StringComparison.Ordinal)))
                {
                    issues.Add(new BindingIssue(
                        BindingIssueKind.UnknownKey,
                        $"{path}.{property.Name}",
                        $"'{property.Name}' is not a parameter here.",
                        Suggest.Fix(property.Name, schema.Select(p => p.Name), "parameters")));
                }
            }
        }

        var values = new Dictionary<string, object?>(StringComparer.Ordinal);

        // Material states index a sibling material, so they bind last.
        foreach (ParameterDescriptor parameter in schema.OrderBy(p => p.Kind == ParameterKind.MaterialState ? 1 : 0))
        {
            string childPath = $"{path}.{parameter.Name}";
            bool present = !absent && json.TryGetProperty(parameter.Name, out _);
            if (!present)
            {
                BindAbsent(parameter, childPath, context, construct, issues, values);
                continue;
            }

            JsonElement element = json.GetProperty(parameter.Name);
            if (TryBindValue(parameter, element, childPath, context, construct, issues, values, out object? bound))
            {
                values[parameter.Name] = bound;
            }
        }

        return issues.Count == before ? new ParameterValues(values, schema.Select(p => p.Name)) : null;
    }

    private static void BindAbsent(
        ParameterDescriptor parameter,
        string path,
        BindingContext context,
        bool construct,
        List<BindingIssue> issues,
        Dictionary<string, object?> values)
    {
        if (parameter.IsRequired)
        {
            issues.Add(Bad(path, $"'{parameter.Name}' is required and has no default.", $"Add \"{parameter.Name}\": {Example(parameter)}."));
            return;
        }

        switch (parameter.Kind)
        {
            case ParameterKind.Group:
                values[parameter.Name] = BindObject(parameter.Children, default, path, context, construct, issues, allowTypeKey: false);
                break;
            case ParameterKind.GroupList:
                values[parameter.Name] = (IReadOnlyList<ParameterValues>)[];
                break;
            case ParameterKind.ObjectList:
                values[parameter.Name] = (IReadOnlyList<object>)[];
                break;
            case ParameterKind.StringList:
                values[parameter.Name] = (IReadOnlyList<string>)[];
                break;
            case ParameterKind.Int when parameter.Default is long whole:
                values[parameter.Name] = (int)whole;
                break;
            default:
                if (parameter.Default is not null)
                {
                    values[parameter.Name] = parameter.Default;
                }

                break;
        }
    }

    private static bool TryBindValue(
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
        switch (parameter.Kind)
        {
            case ParameterKind.Double:
                if (element.ValueKind != JsonValueKind.Number || !element.TryGetDouble(out double number) || !double.IsFinite(number))
                {
                    issues.Add(WrongType(path, parameter, "a number", element));
                    return false;
                }

                bound = number;
                return InRange(parameter, number, path, issues);

            case ParameterKind.Int:
                if (element.ValueKind != JsonValueKind.Number)
                {
                    issues.Add(WrongType(path, parameter, "a whole number", element));
                    return false;
                }

                if (!element.TryGetInt64(out long whole) || whole < int.MinValue || whole > int.MaxValue)
                {
                    issues.Add(Bad(path, $"'{parameter.Name}' must be a whole number, but it is {element.GetRawText()}.", "Remove the fraction, or pick a value that fits in 32 bits."));
                    return false;
                }

                bound = (int)whole;
                return InRange(parameter, whole, path, issues);

            case ParameterKind.Bool:
                if (element.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                {
                    issues.Add(WrongType(path, parameter, "true or false", element));
                    return false;
                }

                bound = element.GetBoolean();
                return true;

            case ParameterKind.String:
                if (element.ValueKind != JsonValueKind.String)
                {
                    issues.Add(WrongType(path, parameter, "a string", element));
                    return false;
                }

                bound = element.GetString()!;
                return true;

            case ParameterKind.StringList:
                return TryBindList(
                    parameter, element, path, issues,
                    (item, itemPath) =>
                    {
                        if (item.ValueKind == JsonValueKind.String)
                        {
                            return item.GetString()!;
                        }

                        issues.Add(Bad(itemPath, $"Every entry of '{parameter.Name}' must be a string, but this is {Describe(item)}.", "Write it as \"…\"."));
                        return null;
                    },
                    items => (IReadOnlyList<string>)items.Cast<string>().ToList(),
                    out bound);

            case ParameterKind.Enum:
                if (element.ValueKind != JsonValueKind.String)
                {
                    issues.Add(WrongType(path, parameter, "a string", element));
                    return false;
                }

                string choice = element.GetString()!;
                if (!parameter.AllowedValues.Contains(choice, StringComparer.Ordinal))
                {
                    issues.Add(Bad(path, $"'{choice}' is not a value '{parameter.Name}' allows.", Suggest.Fix(choice, parameter.AllowedValues, "values")));
                    return false;
                }

                bound = choice;
                return true;

            case ParameterKind.Group:
                bound = BindObject(parameter.Children, element, path, context, construct, issues, allowTypeKey: false);
                return bound is not null;

            case ParameterKind.GroupList:
                return TryBindList(
                    parameter, element, path, issues,
                    (item, itemPath) => BindObject(parameter.Children, item, itemPath, context, construct, issues, allowTypeKey: false),
                    items => (IReadOnlyList<ParameterValues>)items.Cast<ParameterValues>().ToList(),
                    out bound);

            case ParameterKind.ObjectList:
                return TryBindList(
                    parameter, element, path, issues,
                    (item, itemPath) => BindSlotObject(parameter.Slot, item, itemPath, context, construct, issues),
                    items => (IReadOnlyList<object>)items,
                    out bound);

            case ParameterKind.Object:
                bound = BindSlotObject(parameter.Slot, element, path, context, construct, issues);
                return bound is not null;

            case ParameterKind.Reference:
                return TryBindReference(parameter, element, path, context, construct, issues, out bound);

            case ParameterKind.Material:
                return TryBindMaterial(parameter, element, path, context, issues, out bound);

            case ParameterKind.MaterialState:
                return TryBindState(parameter, element, path, issues, siblings, out bound);

            default:
                throw new InvalidOperationException($"Parameter kind {parameter.Kind} has no binder.");
        }
    }

    private static bool TryBindList(
        ParameterDescriptor parameter,
        JsonElement element,
        string path,
        List<BindingIssue> issues,
        Func<JsonElement, string, object?> bindItem,
        Func<List<object>, object> wrap,
        out object? bound)
    {
        bound = null;
        if (element.ValueKind != JsonValueKind.Array)
        {
            issues.Add(WrongType(path, parameter, "an array", element));
            return false;
        }

        int length = element.GetArrayLength();
        if (length < parameter.MinCount)
        {
            issues.Add(Bad(
                path,
                string.Create(CultureInfo.InvariantCulture, $"'{parameter.Name}' needs at least {parameter.MinCount} entries, but it has {length}."),
                "Add the missing entries."));
            return false;
        }

        var items = new List<object>(length);
        bool ok = true;
        int index = 0;
        foreach (JsonElement item in element.EnumerateArray())
        {
            object? value = bindItem(item, string.Create(CultureInfo.InvariantCulture, $"{path}[{index}]"));
            if (value is null)
            {
                ok = false;
            }
            else
            {
                items.Add(value);
            }

            index++;
        }

        if (ok)
        {
            bound = wrap(items);
        }

        return ok;
    }

    /// <summary>Returns the constructed object; in check mode, a placeholder; null when it added an issue.</summary>
    private static object? BindSlotObject(
        string slot,
        JsonElement element,
        string path,
        BindingContext context,
        bool construct,
        List<BindingIssue> issues)
    {
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty(TypeKey, out JsonElement typeElement)
            || typeElement.ValueKind != JsonValueKind.String)
        {
            issues.Add(Bad(
                path,
                $"A {slot} is an object with a string \"type\" key, but this is {Describe(element)} without one.",
                $"Write {{ \"type\": \"…\", … }} using one of {Suggest.List(context.Catalogue.ObjectsIn(slot).Select(o => o.Type))}."));
            return null;
        }

        string type = typeElement.GetString()!;
        if (!context.Catalogue.TryGetObject(slot, type, out ObjectDescriptor? descriptor))
        {
            issues.Add(new BindingIssue(
                BindingIssueKind.UnknownType,
                $"{path}.{TypeKey}",
                $"'{type}' is not a {slot} in this catalogue.",
                Suggest.Fix(type, context.Catalogue.ObjectsIn(slot).Select(o => o.Type), $"{slot}s")));
            return null;
        }

        ParameterValues? values = BindObject(descriptor.Parameters, element, path, context, construct, issues, allowTypeKey: true);
        if (values is null)
        {
            return null;
        }

        if (!construct)
        {
            return descriptor;
        }

        try
        {
            return descriptor.Factory(values);
        }
        catch (ArgumentException ex)
        {
            issues.Add(new BindingIssue(
                BindingIssueKind.Rejected,
                path,
                $"The {slot} '{type}' rejected its parameters: {ex.Message}",
                "Change the parameter the message names."));
            return null;
        }
    }

    private static bool TryBindReference(
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
            issues.Add(WrongType(path, parameter, "a component id (a string)", element));
            return false;
        }

        string id = element.GetString()!;
        if (!construct)
        {
            bound = id;
            return true;
        }

        if (!context.TryGetNode(id, out ISimNode? node))
        {
            issues.Add(new BindingIssue(
                BindingIssueKind.MissingReference,
                path,
                $"'{id}' is not a component in this plant.",
                Suggest.Fix(id, context.NodeIds, "components")));
            return false;
        }

        if (!Capabilities.TryGet(node, parameter.Capability!, out bound))
        {
            issues.Add(new BindingIssue(
                BindingIssueKind.MissingCapability,
                path,
                $"'{id}' cannot supply {parameter.Capability!.Name}, which '{parameter.Name}' needs.",
                "Reference a component that can."));
            return false;
        }

        return true;
    }

    private static bool TryBindMaterial(
        ParameterDescriptor parameter,
        JsonElement element,
        string path,
        BindingContext context,
        List<BindingIssue> issues,
        out object? bound)
    {
        bound = null;
        if (element.ValueKind != JsonValueKind.String)
        {
            issues.Add(WrongType(path, parameter, "a material name (a string)", element));
            return false;
        }

        string name = element.GetString()!;
        if (!context.TryGetMaterial(name, out MaterialDescriptor? material))
        {
            issues.Add(new BindingIssue(
                BindingIssueKind.UnknownMaterial,
                path,
                $"'{name}' is not a material in this plant or its catalogue.",
                Suggest.Fix(name, context.MaterialNames, "materials")));
            return false;
        }

        if (parameter.Payload is { } required && material.Material.Kind != required)
        {
            issues.Add(Bad(
                path,
                $"'{name}' is a {material.Material.Kind} material, but '{parameter.Name}' needs a {required} one.",
                $"Name a {required} material."));
            return false;
        }

        bound = material;
        return true;
    }

    private static bool TryBindState(
        ParameterDescriptor parameter,
        JsonElement element,
        string path,
        List<BindingIssue> issues,
        Dictionary<string, object?> siblings,
        out object? bound)
    {
        bound = null;
        if (element.ValueKind != JsonValueKind.String)
        {
            issues.Add(WrongType(path, parameter, "a state name (a string)", element));
            return false;
        }

        if (!siblings.TryGetValue(parameter.MaterialParameter, out object? sibling) || sibling is not MaterialDescriptor material)
        {
            // The material itself failed to bind and has been reported; a second issue here is noise.
            return false;
        }

        string state = element.GetString()!;
        int index = -1;
        for (int i = 0; i < material.Material.StateSchema.Count; i++)
        {
            if (string.Equals(material.Material.StateSchema[i], state, StringComparison.Ordinal))
            {
                index = i;
            }
        }

        if (index < 0)
        {
            issues.Add(new BindingIssue(
                BindingIssueKind.UnknownState,
                path,
                $"Material '{material.Material.Name}' has no state named '{state}'.",
                Suggest.Fix(state, material.Material.StateSchema, "states")));
            return false;
        }

        bound = index;
        return true;
    }

    private static bool InRange(ParameterDescriptor parameter, double value, string path, List<BindingIssue> issues)
    {
        bool below = parameter.Minimum is { } min && (parameter.ExclusiveMinimum ? value <= min : value < min);
        bool above = parameter.Maximum is { } max && (parameter.ExclusiveMaximum ? value >= max : value > max);
        if (!below && !above)
        {
            return true;
        }

        issues.Add(Bad(
            path,
            string.Create(CultureInfo.InvariantCulture, $"'{parameter.Name}' is {value}, which is outside {Range(parameter)}."),
            $"Use a value in {Range(parameter)}."));
        return false;
    }

    /// <summary>Interval notation: <c>(0, 1]</c>, <c>[0, ∞)</c>.</summary>
    internal static string Range(ParameterDescriptor parameter)
    {
        string low = parameter.Minimum is { } min
            ? string.Create(CultureInfo.InvariantCulture, $"{(parameter.ExclusiveMinimum ? '(' : '[')}{min}")
            : "(-∞";
        string high = parameter.Maximum is { } max
            ? string.Create(CultureInfo.InvariantCulture, $"{max}{(parameter.ExclusiveMaximum ? ')' : ']')}")
            : "∞)";
        return $"{low}, {high}";
    }

    private static BindingIssue Bad(string path, string message, string fix) =>
        new(BindingIssueKind.BadParameter, path, message, fix);

    private static BindingIssue WrongType(string path, ParameterDescriptor parameter, string wanted, JsonElement actual) =>
        Bad(path, $"'{parameter.Name}' must be {wanted}, but it is {Describe(actual)}.", $"Write {Example(parameter)}.");

    private static string Describe(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => "an object",
        JsonValueKind.Array => "an array",
        JsonValueKind.String => "a string",
        JsonValueKind.Number => "a number",
        JsonValueKind.True or JsonValueKind.False => "a boolean",
        JsonValueKind.Null => "null",
        _ => "nothing",
    };

    private static string Example(ParameterDescriptor parameter) => parameter.Kind switch
    {
        ParameterKind.Double => parameter.Unit.Length > 0 ? $"a number in {parameter.Unit}" : "a number",
        ParameterKind.Int => "a whole number",
        ParameterKind.Bool => "true or false",
        ParameterKind.String => "\"…\"",
        ParameterKind.Enum => $"one of {Suggest.List(parameter.AllowedValues)}",
        ParameterKind.Group => "{ … }",
        ParameterKind.GroupList or ParameterKind.ObjectList => "[ … ]",
        ParameterKind.StringList => "[ \"…\" ]",
        ParameterKind.Reference => "the id of another component",
        ParameterKind.Material => "the name of a material",
        ParameterKind.MaterialState => "the name of one of the material's states",
        ParameterKind.Object => "{ \"type\": \"…\", … }",
        _ => "a value",
    };
}
```

- [ ] **Step 9: Run the binder tests**

Run: `dotnet test tests/Dse.Core.Tests --nologo --filter FullyQualifiedName~ParameterBinderTests`
Expected: PASS, 23 test cases (20 methods; the bounds theory contributes 4).

`CollectsEveryIssueAtOnce` expects exactly four issues: the unknown key
`enabeld`, `ratio` out of range, `channels` not whole, and `enabled` missing. If
it reports a different count, read the issues and report them; do not change
the expected number.

- [ ] **Step 10: Build Release, run everything, commit**

Run: `dotnet build Dse.sln -c Release --nologo` — expect `0 Warning(s)`, `0 Error(s)`.
Run: `dotnet test Dse.sln --nologo` — expect all green.

```bash
git add src/Dse.Core/Catalogue tests/Dse.Core.Tests/Catalogue
git commit -m "feat(catalogue): bind JSON parameters to descriptors, collecting every issue"
```

---
### Task 4: Conformance, the module, and the first descriptors

Hand-written descriptors drift. This task builds the check that stops them —
before any real descriptor exists — then proves it on the two `UnitDelay`
registrations, and sets up the shrinking `Pending` list (R39) the rollout tasks
work through.

**Files:**
- Create: `src/Dse.Core/Testing/ConformanceFixtures.cs`
- Create: `src/Dse.Core/Testing/ConformanceReport.cs`
- Create: `src/Dse.Core/Testing/CatalogueConformance.cs`
- Create: `src/Dse.Core/Catalogue/CoreDescriptors.cs`
- Create: `src/Dse.Components/ComponentsModule.cs`
- Test: `tests/Dse.Core.Tests/Catalogue/CatalogueConformanceTests.cs`
- Test: `tests/Dse.Components.Tests/Catalogue/ComponentsFixtures.cs`
- Test: `tests/Dse.Components.Tests/Catalogue/ComponentsCatalogueTests.cs`

**Interfaces:**
- Consumes: Tasks 1–3.
- Produces:
  - `sealed class ConformanceFixtures` — fluent `Parameters(string componentType, string json)`, `ObjectParameters(string slot, string type, string json)`, `Material(MaterialDescriptor)`, `Node(ISimNode)`
  - `sealed record ConformanceReport(IReadOnlyList<string> Mismatches, IReadOnlyList<Type> BuiltTypes)`
  - `static class CatalogueConformance { ConformanceReport Check(ComponentCatalogue catalogue, ConformanceFixtures fixtures); const string ProbeId = "probe"; }`
    (the spec's `Check` returned the mismatch list alone; the sweep also needs to know which CLR types the catalogue can build, so it returns both)
  - `static class CoreDescriptors` — `UnitDelayBool`, `UnitDelayDouble` (Task 7 adds the belts)
  - `sealed class ComponentsModule : ICatalogueModule` — `Name` is `"Dse.Components"`
  - In tests: `ComponentsFixtures.Create()` and `ComponentsFixtures.Catalogue`, which every rollout task extends.

**What conformance compares**, per component type, between the descriptor and
an instance built with id `probe` from the type's fixture JSON (or `{}`):

| Aspect | From the descriptor | From the instance |
|---|---|---|
| Signal ports | `Ports`, repeats expanded | leaf: `Ports` that are not `FlowPort`; composite: non-flow `ExposedPorts`, by alias |
| — compared on | name, direction, value type, `required` | |
| Flow ports | `FlowPorts`, repeats expanded | `FlowInlet` / `FlowOutlet`: name, direction, `Kind` |
| Faults | id and ordered parameter names | `IFaultTarget.SupportedFaults`; a composite has none |
| Tags | `Tags`, repeats expanded: name, kind, access | leaf: `DescribeTags()`; composite: every leaf's, renamed `Leaf.Tag` or by alias, degraded to read-only when the input is wired (plan 4 R22, R23) |
| Telemetry | `Telemetry` names; unit too when the descriptor states one | each leaf `Initialize`d against a scratch registry; keys with the `probe.` prefix removed |
| Provides | each listed type must be obtainable through `Capabilities.TryGet` | and every capability some `Reference` parameter in the catalogue asks for, if the instance can supply it, must be listed |

Every object descriptor is bound and built once from its fixture JSON (or `{}`).

- [ ] **Step 1: Write the failing helper tests**

Create `tests/Dse.Core.Tests/Catalogue/CatalogueConformanceTests.cs`:

```csharp
using Dse.Core.Catalogue;
using Dse.Core.Contexts;
using Dse.Core.Faults;
using Dse.Core.Flow;
using Dse.Core.Graph;
using Dse.Core.Io;
using Dse.Core.Testing;
using Dse.Io;

namespace Dse.Core.Tests.Catalogue;

public class CatalogueConformanceTests
{
    /// <summary>One of everything conformance looks at.</summary>
    private sealed class Widget : FlowComponentBase, IFaultTarget, ITagProvider
    {
        private static readonly FaultDescriptor[] Faults =
        [
            new("jam", "Stops.", new FaultParameter("seconds", "s", 1.0, "How long.")),
        ];

        public Widget(string id, int channels)
            : base(id)
        {
            for (int i = 1; i <= channels; i++)
            {
                AddInput<bool>($"Channel{i}", defaultValue: true);
            }

            Enable = AddInput<bool>("Enable", required: true);
            Level = AddOutput<double>("Level");
            In = AddInlet("In", PayloadKind.Bulk);
        }

        public InputPort<bool> Enable { get; }

        public OutputPort<double> Level { get; }

        public FlowInlet In { get; }

        public override double MassHeld => 0.0;

        public IReadOnlyList<FaultDescriptor> SupportedFaults => Faults;

        public void ApplyFault(string faultId, FaultArguments arguments)
        {
        }

        public void ClearFault(string faultId)
        {
        }

        public IEnumerable<TagBinding> DescribeTags() =>
        [
            TagBinding.Write("Enable", Enable, "Enabled"),
            TagBinding.Read("Level", Level, "fraction", 0.0, 1.0, "Level"),
        ];

        public override void Initialize(in InitContext ctx) => ctx.RegisterTelemetry("Held", "kg");
    }

    private static ComponentDescriptor Honest() =>
        new("widget", ComponentCategory.Flow, "A test widget.", (id, p) => new Widget(id, p.Int("channels")))
        {
            Parameters = [Param.Int("channels", "How many channels.", @default: 2, min: 1)],
            Ports =
            [
                PortSpec.In<bool>("Channel{n}", repeat: new PortRepeat("channels")),
                PortSpec.In<bool>("Enable", required: true),
                PortSpec.Out<double>("Level", "fraction"),
            ],
            FlowPorts = [PortSpec.Inlet("In", PayloadKind.Bulk)],
            Faults = [new FaultDescriptor("jam", "Stops.", new FaultParameter("seconds", "s", 1.0, "How long."))],
            Tags =
            [
                new TagEntry("Enable", TagKind.Bool, TagAccess.ReadWrite),
                new TagEntry("Level", TagKind.Double, TagAccess.ReadOnly, "fraction"),
            ],
            Telemetry = [new TelemetryKey("Held", "kg")],
        };

    private static ConformanceReport Check(ComponentDescriptor descriptor) =>
        CatalogueConformance.Check(new CatalogueBuilder().Add(descriptor).Build(), new ConformanceFixtures());

    [Fact]
    public void AnHonestDescriptorHasNoMismatches()
    {
        ConformanceReport report = Check(Honest());

        Assert.Empty(report.Mismatches);
        Assert.Contains(typeof(Widget), report.BuiltTypes);
    }

    [Fact]
    public void AFixtureChangesTheProbeAndTheExpansion()
    {
        ComponentCatalogue catalogue = new CatalogueBuilder().Add(Honest()).Build();

        ConformanceReport report = CatalogueConformance.Check(
            catalogue, new ConformanceFixtures().Parameters("widget", """{ "channels": 4 }"""));

        Assert.Empty(report.Mismatches);
    }

    [Fact]
    public void ReportsAPortTheDescriptorForgot()
    {
        ComponentDescriptor honest = Honest();
        var forgetful = new ComponentDescriptor(honest.Type, honest.Category, honest.Description, honest.Factory)
        {
            Parameters = honest.Parameters,
            Ports = honest.Ports.Where(p => p.Name != "Level").ToList(),
            FlowPorts = honest.FlowPorts,
            Faults = honest.Faults,
            Tags = honest.Tags,
            Telemetry = honest.Telemetry,
        };

        string mismatch = Assert.Single(Check(forgetful).Mismatches);

        Assert.Equal("widget: signal port 'Level' (Out double) is on the instance but not in the descriptor.", mismatch);
    }

    [Fact]
    public void ReportsAPortTheDescriptorInvented()
    {
        ComponentDescriptor honest = Honest();
        var inventive = new ComponentDescriptor(honest.Type, honest.Category, honest.Description, honest.Factory)
        {
            Parameters = honest.Parameters,
            Ports = [.. honest.Ports, PortSpec.Out<bool>("Ghost")],
            FlowPorts = honest.FlowPorts,
            Faults = honest.Faults,
            Tags = honest.Tags,
            Telemetry = honest.Telemetry,
        };

        string mismatch = Assert.Single(Check(inventive).Mismatches);

        Assert.Equal("widget: signal port 'Ghost' (Out bool) is in the descriptor but not on the instance.", mismatch);
    }

    [Fact]
    public void ReportsEveryOtherAspect()
    {
        ComponentDescriptor honest = Honest();
        var wrong = new ComponentDescriptor(honest.Type, honest.Category, honest.Description, honest.Factory)
        {
            Parameters = honest.Parameters,
            Ports = honest.Ports,
            FlowPorts = [PortSpec.Inlet("In", PayloadKind.Discrete)],
            Faults = [new FaultDescriptor("jam", "Stops.")],
            Tags = [new TagEntry("Enable", TagKind.Bool, TagAccess.ReadOnly), new TagEntry("Level", TagKind.Double, TagAccess.ReadOnly)],
            Telemetry = [new TelemetryKey("Held", "t")],
            Provides = [typeof(IMaterialObservable)],
        };

        IReadOnlyList<string> mismatches = Check(wrong).Mismatches;

        Assert.Contains(mismatches, m => m.Contains("flow port 'In' (In Discrete) is in the descriptor", StringComparison.Ordinal));
        Assert.Contains(mismatches, m => m.Contains("flow port 'In' (In Bulk) is on the instance", StringComparison.Ordinal));
        Assert.Contains(mismatches, m => m.Contains("fault 'jam()' is in the descriptor", StringComparison.Ordinal));
        Assert.Contains(mismatches, m => m.Contains("fault 'jam(seconds)' is on the instance", StringComparison.Ordinal));
        Assert.Contains(mismatches, m => m.Contains("tag 'Enable' (Bool ReadOnly) is in the descriptor", StringComparison.Ordinal));
        Assert.Contains(mismatches, m => m.Contains("telemetry 'Held' is 'kg' on the instance but 't' in the descriptor", StringComparison.Ordinal));
        Assert.Contains(mismatches, m => m.Contains("provides IMaterialObservable in the descriptor, but the instance cannot supply it", StringComparison.Ordinal));
    }

    [Fact]
    public void ReportsAFixtureThatDoesNotBind()
    {
        ComponentCatalogue catalogue = new CatalogueBuilder().Add(Honest()).Build();

        ConformanceReport report = CatalogueConformance.Check(
            catalogue, new ConformanceFixtures().Parameters("widget", """{ "channels": 0 }"""));

        string mismatch = Assert.Single(report.Mismatches);
        Assert.StartsWith("widget: the fixture does not bind — $.channels:", mismatch, StringComparison.Ordinal);
    }

    [Fact]
    public void ReportsAFactoryThatThrows()
    {
        var broken = new ComponentDescriptor("broken", ComponentCategory.Signal, "Asks for the wrong name.", (id, p) => new UnitDelay<bool>(id, p.Bool("nope")));

        string mismatch = Assert.Single(Check(broken).Mismatches);

        Assert.StartsWith("broken: the factory threw KeyNotFoundException", mismatch, StringComparison.Ordinal);
    }

    [Fact]
    public void ReportsAnUnlistedCapabilityThatSomeReferenceNeeds()
    {
        ComponentCatalogue catalogue = new CatalogueBuilder()
            .Add(new ComponentDescriptor("belt", ComponentCategory.Flow, "A belt.", (id, p) => new BulkBelt(id, 10.0, 0.5, 2.0, 100.0))
            {
                Ports =
                [
                    PortSpec.In<double>("Speed"), PortSpec.In<double>("AmbientTemperature"),
                    PortSpec.Out<double>("Load"), PortSpec.Out<double>("PeakLinearDensity"),
                ],
                FlowPorts = [PortSpec.Inlet("In", PayloadKind.Bulk), PortSpec.Outlet("Out", PayloadKind.Bulk)],
                Telemetry = [new TelemetryKey("Load", "kg")],
            })
            .Add(new ComponentDescriptor("watcher", ComponentCategory.Signal, "Watches a belt.", (id, p) => new UnitDelay<bool>(id))
            {
                Parameters = [Param.Reference<IMaterialObservable>("belt", "What to watch.")],
                Ports = [PortSpec.In<bool>("In"), PortSpec.Out<bool>("Out")],
            })
            .Build();
        ConformanceFixtures fixtures = new ConformanceFixtures()
            .Node(new BulkBelt("STANDIN", 10.0, 0.5, 2.0, 100.0))
            .Parameters("watcher", """{ "belt": "STANDIN" }""");

        string mismatch = Assert.Single(CatalogueConformance.Check(catalogue, fixtures).Mismatches);

        Assert.Equal("belt: the instance can supply IMaterialObservable, which a reference parameter in this catalogue needs, but the descriptor does not list it under Provides.", mismatch);
    }

    [Fact]
    public void BuildsEveryObjectDescriptor()
    {
        ComponentCatalogue catalogue = new CatalogueBuilder()
            .Add(new ObjectDescriptor(ObjectSlots.Hold, "for-seconds", "Waits.", p => Tuple.Create(p.Double("seconds")))
            {
                Parameters = [Param.Double("seconds", "How long.")],
            })
            .Build();

        ConformanceReport missing = CatalogueConformance.Check(catalogue, new ConformanceFixtures());
        ConformanceReport given = CatalogueConformance.Check(
            catalogue, new ConformanceFixtures().ObjectParameters(ObjectSlots.Hold, "for-seconds", """{ "seconds": 5 }"""));

        Assert.StartsWith("hold 'for-seconds': the fixture does not bind — $.seconds:", Assert.Single(missing.Mismatches), StringComparison.Ordinal);
        Assert.Empty(given.Mismatches);
        Assert.Contains(typeof(Tuple<double>), given.BuiltTypes);
    }
}
```

- [ ] **Step 2: Run and see them fail to compile**

Run: `dotnet test tests/Dse.Core.Tests --nologo --filter FullyQualifiedName~CatalogueConformanceTests`
Expected: build FAILS — namespace `Dse.Core.Testing` does not exist.

- [ ] **Step 3: Write the fixtures and the report**

`src/Dse.Core/Testing/ConformanceFixtures.cs`:

```csharp
using Dse.Core.Catalogue;
using Dse.Core.Graph;

namespace Dse.Core.Testing;

/// <summary>
/// What conformance needs beyond defaults to build a probe of each type: JSON for
/// required parameters, materials to name, and stand-in nodes to reference.
/// </summary>
public sealed class ConformanceFixtures
{
    private readonly Dictionary<string, string> _components = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Slot, string Type), string> _objects = [];
    private readonly List<MaterialDescriptor> _materials = [];
    private readonly List<ISimNode> _nodes = [];

    /// <summary>The <c>parameters</c> object for a probe of <paramref name="componentType"/>.</summary>
    public ConformanceFixtures Parameters(string componentType, string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(componentType);
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        _components[componentType] = json;
        return this;
    }

    /// <summary>The parameters (without <c>"type"</c>) for a probe of an object.</summary>
    public ConformanceFixtures ObjectParameters(string slot, string type, string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slot);
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        _objects[(slot, type)] = json;
        return this;
    }

    public ConformanceFixtures Material(MaterialDescriptor material)
    {
        ArgumentNullException.ThrowIfNull(material);
        _materials.Add(material);
        return this;
    }

    /// <summary>A stand-in a fixture may reference by its id.</summary>
    public ConformanceFixtures Node(ISimNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        _nodes.Add(node);
        return this;
    }

    internal string ParametersFor(string componentType) =>
        _components.TryGetValue(componentType, out string? json) ? json : "{}";

    internal string ParametersFor(string slot, string type) =>
        _objects.TryGetValue((slot, type), out string? json) ? json : "{}";

    internal BindingContext NewContext(ComponentCatalogue catalogue)
    {
        var context = new BindingContext(catalogue);
        foreach (MaterialDescriptor material in _materials)
        {
            context.AddMaterial(material);
        }

        foreach (ISimNode node in _nodes)
        {
            context.AddNode(node);
        }

        return context;
    }
}
```

`src/Dse.Core/Testing/ConformanceReport.cs`:

```csharp
namespace Dse.Core.Testing;

/// <summary>
/// What conformance found. <paramref name="Mismatches"/> is empty when every
/// descriptor matches what it builds; <paramref name="BuiltTypes"/> is every CLR
/// type a factory produced, for a "does everything have a descriptor" sweep.
/// </summary>
public sealed record ConformanceReport(IReadOnlyList<string> Mismatches, IReadOnlyList<Type> BuiltTypes);
```

- [ ] **Step 4: Write `CatalogueConformance`**

`src/Dse.Core/Testing/CatalogueConformance.cs`:

```csharp
using System.Text.Json;
using Dse.Core.Catalogue;
using Dse.Core.Contexts;
using Dse.Core.Faults;
using Dse.Core.Flow;
using Dse.Core.Graph;
using Dse.Core.Io;
using Dse.Core.Randomness;
using Dse.Core.Telemetry;
using Dse.Io;

namespace Dse.Core.Testing;

/// <summary>
/// Builds one instance of every catalogue entry and reports every difference
/// between a descriptor and what it built. No test-framework dependency: a
/// module's tests assert that <see cref="ConformanceReport.Mismatches"/> is empty.
/// </summary>
public static class CatalogueConformance
{
    /// <summary>The id every probe is built with.</summary>
    public const string ProbeId = "probe";

    public static ConformanceReport Check(ComponentCatalogue catalogue, ConformanceFixtures fixtures)
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        ArgumentNullException.ThrowIfNull(fixtures);

        var mismatches = new List<string>();
        var built = new List<Type>();
        Type[] wanted = catalogue.Components
            .SelectMany(c => Flatten(c.Parameters))
            .Concat(catalogue.Objects.SelectMany(o => Flatten(o.Parameters)))
            .Where(p => p.Kind == ParameterKind.Reference)
            .Select(p => p.Capability!)
            .Distinct()
            .ToArray();

        foreach (ObjectDescriptor descriptor in catalogue.Objects)
        {
            string label = $"{descriptor.Slot} '{descriptor.Type}'";
            ParameterValues? values = BindFixture(
                descriptor.Parameters, fixtures.ParametersFor(descriptor.Slot, descriptor.Type), fixtures.NewContext(catalogue), label, mismatches);
            if (values is not null && TryBuild(() => descriptor.Factory(values), label, mismatches, out object? made))
            {
                built.Add(made.GetType());
            }
        }

        foreach (ComponentDescriptor descriptor in catalogue.Components)
        {
            ParameterValues? values = BindFixture(
                descriptor.Parameters, fixtures.ParametersFor(descriptor.Type), fixtures.NewContext(catalogue), descriptor.Type, mismatches);
            if (values is null || !TryBuild(() => descriptor.Factory(ProbeId, values), descriptor.Type, mismatches, out object? made))
            {
                continue;
            }

            var node = (ISimNode)made;
            built.Add(node.GetType());
            Compare(descriptor, values, node, wanted, mismatches);
        }

        return new ConformanceReport(mismatches, built.Distinct().ToList());
    }

    private static IEnumerable<ParameterDescriptor> Flatten(IEnumerable<ParameterDescriptor> parameters) =>
        parameters.SelectMany(p => Flatten(p.Children).Prepend(p));

    private static ParameterValues? BindFixture(
        IReadOnlyList<ParameterDescriptor> schema, string json, BindingContext context, string label, List<string> mismatches)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        var issues = new List<BindingIssue>();
        ParameterValues? values = ParameterBinder.Bind(schema, document.RootElement, "$", context, construct: true, issues);
        foreach (BindingIssue issue in issues)
        {
            mismatches.Add($"{label}: the fixture does not bind — {issue.Path}: {issue.Message} {issue.Fix}");
        }

        return values;
    }

    private static bool TryBuild(Func<object> build, string label, List<string> mismatches, out object made)
    {
        try
        {
            made = build();
            return true;
        }
#pragma warning disable CA1031 // Any exception from a factory is a finding to report, not a crash.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            mismatches.Add($"{label}: the factory threw {ex.GetType().Name}: {ex.Message}");
            made = new object();
            return false;
        }
    }

    private static void Compare(ComponentDescriptor descriptor, ParameterValues values, ISimNode node, Type[] wanted, List<string> mismatches)
    {
        IReadOnlyList<ISimComponent> leaves = node switch
        {
            CompositeComponent composite => composite.LeafComponents,
            ISimComponent leaf => [leaf],
            _ => [],
        };
        List<(string Name, Port Port)> ports = node switch
        {
            CompositeComponent composite => composite.ExposedPorts.Select(e => (e.Key, e.Value)).ToList(),
            ISimComponent leaf => leaf.Ports.Select(p => (p.Name, p)).ToList(),
            _ => [],
        };

        Diff(
            descriptor.Type, "signal port",
            descriptor.Ports.SelectMany(p => Names(p.Name, p.Repeat, values)
                .Select(n => $"'{n}' ({p.Direction} {p.ValueType}{(p.Required ? " required" : string.Empty)})")),
            ports.Where(p => p.Port is not FlowPort)
                .Select(p => $"'{p.Name}' ({(p.Port.IsInput ? PortDirection.In : PortDirection.Out)} {PortSpec.ValueTypeName(p.Port.ValueType!)}{(p.Port.IsRequiredInput ? " required" : string.Empty)})"),
            mismatches);

        Diff(
            descriptor.Type, "flow port",
            descriptor.FlowPorts.SelectMany(p => Names(p.Name, p.Repeat, values).Select(n => $"'{n}' ({p.Direction} {p.Payload})")),
            ports.Where(p => p.Port is FlowPort)
                .Select(p => $"'{p.Name}' ({(p.Port is FlowInlet ? PortDirection.In : PortDirection.Out)} {((FlowPort)p.Port).Kind})"),
            mismatches);

        Diff(
            descriptor.Type, "fault",
            descriptor.Faults.Select(Signature),
            node is IFaultTarget target ? target.SupportedFaults.Select(Signature) : [],
            mismatches);

        Diff(
            descriptor.Type, "tag",
            descriptor.Tags.SelectMany(t => Names(t.Name, t.Repeat, values).Select(n => $"'{n}' ({t.Kind} {t.Access})")),
            ActualTags(node, leaves).Select(t => $"'{t.Name}' ({t.Kind} {t.Access})"),
            mismatches);

        CompareTelemetry(descriptor, leaves, mismatches);

        foreach (Type capability in descriptor.Provides)
        {
            if (!Capabilities.TryGet(node, capability, out _))
            {
                mismatches.Add($"{descriptor.Type}: provides {capability.Name} in the descriptor, but the instance cannot supply it.");
            }
        }

        foreach (Type capability in wanted)
        {
            if (Capabilities.TryGet(node, capability, out _) && !descriptor.Provides.Contains(capability))
            {
                mismatches.Add(
                    $"{descriptor.Type}: the instance can supply {capability.Name}, which a reference parameter in this catalogue needs, " +
                    $"but the descriptor does not list it under Provides.");
            }
        }
    }

    private static IReadOnlyList<string> Names(string pattern, PortRepeat? repeat, ParameterValues values)
    {
        if (repeat is null)
        {
            return [pattern];
        }

        return repeat.IsByName
            ? repeat.Expand(pattern, 0, values.Groups(repeat.Parameter).Select(g => g.String(repeat.NameChild)).ToList())
            : repeat.Expand(pattern, values.Int(repeat.Parameter), []);
    }

    private static string Signature(FaultDescriptor fault) =>
        $"'{fault.Id}({string.Join(", ", fault.Parameters.Select(p => p.Name))})'";

    /// <summary>The tags a plant would see, named as <c>SimulationBuilder</c> names them, relative to the probe.</summary>
    private static List<(string Name, TagKind Kind, TagAccess Access)> ActualTags(ISimNode node, IReadOnlyList<ISimComponent> leaves)
    {
        var aliases = new Dictionary<Port, string>(ReferenceEqualityComparer.Instance);
        if (node is CompositeComponent composite)
        {
            foreach (KeyValuePair<string, Port> exposed in composite.ExposedPorts)
            {
                aliases[exposed.Value] = exposed.Key;
            }
        }

        var tags = new List<(string, TagKind, TagAccess)>();
        foreach (ISimComponent leaf in leaves)
        {
            if (leaf is not ITagProvider provider)
            {
                continue;
            }

            string prefix = leaf.Id.Length > node.Id.Length ? leaf.Id[(node.Id.Length + 1)..] + "." : string.Empty;
            foreach (TagBinding binding in provider.DescribeTags())
            {
                string name = aliases.TryGetValue(binding.Port, out string? alias) ? alias : prefix + binding.Name;
                TagAccess access = binding.Access == TagAccess.ReadWrite && binding.Port.SourcePort is not null
                    ? TagAccess.ReadOnly
                    : binding.Access;
                tags.Add((name, binding.Kind, access));
            }
        }

        return tags;
    }

    private static void CompareTelemetry(ComponentDescriptor descriptor, IReadOnlyList<ISimComponent> leaves, List<string> mismatches)
    {
        var registry = new TelemetryRegistry();
        var items = new ItemIdSequence();
        foreach (ISimComponent leaf in leaves)
        {
            try
            {
                leaf.Initialize(new InitContext(new DeterministicRandom(1UL), registry, items, leaf.Id, DateTimeOffset.UnixEpoch, 0.01));
            }
#pragma warning disable CA1031 // Reported, not rethrown: one broken Initialize must not hide the other findings.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                mismatches.Add($"{descriptor.Type}: Initialize threw {ex.GetType().Name} on '{leaf.Id}': {ex.Message}");
            }
        }

        var actual = registry.Channels.ToDictionary(c => c.Key[(ProbeId.Length + 1)..], c => c.Unit, StringComparer.Ordinal);
        Diff(
            descriptor.Type, "telemetry",
            descriptor.Telemetry.Select(t => $"'{t.Name}'"),
            actual.Keys.Select(k => $"'{k}'"),
            mismatches);

        foreach (TelemetryKey key in descriptor.Telemetry)
        {
            if (key.Unit.Length > 0 && actual.TryGetValue(key.Name, out string? unit) && !string.Equals(unit, key.Unit, StringComparison.Ordinal))
            {
                mismatches.Add($"{descriptor.Type}: telemetry '{key.Name}' is '{unit}' on the instance but '{key.Unit}' in the descriptor.");
            }
        }
    }

    private static void Diff(string type, string what, IEnumerable<string> declared, IEnumerable<string> actual, List<string> mismatches)
    {
        List<string> declaredList = declared.ToList();
        List<string> actualList = actual.ToList();
        foreach (string item in declaredList.Except(actualList, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            mismatches.Add($"{type}: {what} {item} is in the descriptor but not on the instance.");
        }

        foreach (string item in actualList.Except(declaredList, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            mismatches.Add($"{type}: {what} {item} is on the instance but not in the descriptor.");
        }
    }
}
```

`InitContext`'s constructor is public and takes
`(DeterministicRandom, TelemetryRegistry, ItemIdSequence, string, DateTimeOffset, double)`.
If `TelemetryRegistry` or `ItemIdSequence` has no public parameterless
constructor the build will say so; both are in this assembly, so an `internal`
one is reachable. The `#pragma` lines are harmless when `CA1031` is not enabled;
leave them.

- [ ] **Step 5: Run the helper tests**

Run: `dotnet test tests/Dse.Core.Tests --nologo --filter FullyQualifiedName~CatalogueConformanceTests`
Expected: PASS, 9 tests.

`ReportsEveryOtherAspect` names seven findings. If the run produces a finding
the test does not look for, that is fine (it uses `Contains`); if one the test
looks for is absent, report which.

- [ ] **Step 6: Write the two Core descriptors**

`src/Dse.Core/Catalogue/CoreDescriptors.cs`:

```csharp
using Dse.Core.Graph;

namespace Dse.Core.Catalogue;

/// <summary>
/// Descriptors for the nodes Core itself ships. A generic node is registered
/// once per closed type a plant can name.
/// </summary>
public static class CoreDescriptors
{
    public static ComponentDescriptor UnitDelayBool { get; } = new(
        "unit-delay-bool",
        ComponentCategory.Signal,
        "Holds a boolean for one tick. Put one on a connection to break an algebraic loop.",
        (id, p) => new UnitDelay<bool>(id, p.Bool("initialValue")))
    {
        Parameters = [Param.Bool("initialValue", "The output on the first tick.", @default: false)],
        Ports =
        [
            PortSpec.In<bool>("In", description: "The value to delay."),
            PortSpec.Out<bool>("Out", description: "The input as it was one tick ago."),
        ],
    };

    public static ComponentDescriptor UnitDelayDouble { get; } = new(
        "unit-delay-double",
        ComponentCategory.Signal,
        "Holds a number for one tick. Put one on a connection to break an algebraic loop.",
        (id, p) => new UnitDelay<double>(id, p.Double("initialValue")))
    {
        Parameters = [Param.Double("initialValue", "The output on the first tick.", @default: 0.0)],
        Ports =
        [
            PortSpec.In<double>("In", description: "The value to delay."),
            PortSpec.Out<double>("Out", description: "The input as it was one tick ago."),
        ],
    };
}
```

- [ ] **Step 7: Write the module**

`src/Dse.Components/ComponentsModule.cs`:

```csharp
using Dse.Core.Catalogue;

namespace Dse.Components;

/// <summary>
/// Everything a plant can instantiate out of the box: this assembly's
/// components, transforms and hold conditions, and the nodes Core ships.
/// </summary>
public sealed class ComponentsModule : ICatalogueModule
{
    public string Name => "Dse.Components";

    public void Register(CatalogueBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Signal
        builder.Add(CoreDescriptors.UnitDelayBool);
        builder.Add(CoreDescriptors.UnitDelayDouble);
    }
}
```

- [ ] **Step 8: Write the fixtures and the two catalogue tests**

`tests/Dse.Components.Tests/Catalogue/ComponentsFixtures.cs`:

```csharp
using Dse.Core.Catalogue;
using Dse.Core.Testing;

namespace Dse.Components.Tests.Catalogue;

/// <summary>
/// The catalogue under test and what conformance needs to build a probe of each
/// type. Every descriptor rollout task adds the entries its types need.
/// </summary>
internal static class ComponentsFixtures
{
    public static ComponentCatalogue Catalogue { get; } = new CatalogueBuilder().Add<ComponentsModule>().Build();

    public static ConformanceFixtures Create() => new();
}
```

`tests/Dse.Components.Tests/Catalogue/ComponentsCatalogueTests.cs`:

```csharp
using System.Reflection;
using Dse.Components.Conveyors;
using Dse.Components.Flow;
using Dse.Components.Instruments;
using Dse.Components.Mechanical;
using Dse.Components.Safety;
using Dse.Components.Transforms;
using Dse.Core.Flow;
using Dse.Core.Graph;
using Dse.Core.Testing;

namespace Dse.Components.Tests.Catalogue;

public class ComponentsCatalogueTests
{
    /// <summary>
    /// Types that have no descriptor YET. Each rollout task deletes the types it
    /// covers; Task 8 deletes this array. Never add to it.
    /// </summary>
    private static readonly Type[] Pending =
    [
        // Task 5 — mechanical
        typeof(Motor), typeof(Gearbox), typeof(DrivePulley), typeof(TailPulley), typeof(BeltFriction), typeof(MotorStarter),
        // Task 6 — instruments and safety
        typeof(SpeedSensor), typeof(CurrentSensor), typeof(TemperatureSensor), typeof(BeltScale), typeof(Pyrometer),
        typeof(ZeroSpeedSwitch), typeof(PartCounter), typeof(EStop), typeof(PullKey), typeof(SafetyRelay),
        // Task 7 — flow, transforms, holds
        typeof(BulkBelt), typeof(DiscreteBelt), typeof(BulkSource), typeof(BulkSink), typeof(ItemSource), typeof(ItemSink),
        typeof(TransferChute), typeof(Former), typeof(BulkProcessUnit), typeof(ItemProcessUnit),
        typeof(ThermalTransfer), typeof(MoistureLoss), typeof(ResidenceAccumulator),
        // Task 8 — conveyor
        typeof(Conveyor),
    ];

    /// <summary>The hold conditions are private classes behind <see cref="Hold"/>; Task 7 empties this.</summary>
    private const int PendingHolds = 5;

    private static readonly ConformanceReport Report =
        CatalogueConformance.Check(ComponentsFixtures.Catalogue, ComponentsFixtures.Create());

    [Fact]
    public void EveryDescriptorMatchesWhatItBuilds()
    {
        Assert.Empty(Report.Mismatches);
    }

    [Fact]
    public void EveryConcreteNodeTransformAndHoldHasADescriptor()
    {
        Assembly[] assemblies = [typeof(ComponentsModule).Assembly, typeof(UnitDelay<>).Assembly];
        List<Type> concrete = assemblies
            .SelectMany(a => a.GetTypes())
            .Where(t => t is { IsClass: true, IsAbstract: false })
            .Where(t => typeof(ISimNode).IsAssignableFrom(t)
                     || typeof(IMaterialTransform).IsAssignableFrom(t)
                     || typeof(IHoldCondition).IsAssignableFrom(t))
            .ToList();

        var built = new HashSet<Type>(Report.BuiltTypes.Select(Definition));
        List<string> missing = concrete
            .Where(t => !built.Contains(Definition(t)) && !Pending.Contains(t))
            .Select(t => t.FullName!)
            .Order(StringComparer.Ordinal)
            .ToList();
        List<string> stale = Pending.Where(t => built.Contains(Definition(t))).Select(t => t.Name).ToList();
        int holdsMissing = missing.Count(name => name.StartsWith(typeof(Hold).FullName + "+", StringComparison.Ordinal));

        Assert.Empty(stale);
        Assert.Equal(PendingHolds, holdsMissing);
        Assert.Empty(missing.Where(name => !name.StartsWith(typeof(Hold).FullName + "+", StringComparison.Ordinal)));
    }

    private static Type Definition(Type type) => type.IsGenericType ? type.GetGenericTypeDefinition() : type;
}
```

`UnitDelay<>` is covered because `unit-delay-bool` builds a `UnitDelay<bool>`,
whose generic definition matches.

- [ ] **Step 9: Run them**

Run: `dotnet test tests/Dse.Components.Tests --nologo --filter FullyQualifiedName~ComponentsCatalogueTests`
Expected: PASS, 2 tests.

If the sweep finds a concrete type this plan did not anticipate (not in
`Pending`, not built), do **not** add it to `Pending`: report its name. It needs
a descriptor and a home in one of Tasks 5–8.

- [ ] **Step 10: Build Release, run everything, commit**

Run: `dotnet build Dse.sln -c Release --nologo` — expect `0 Warning(s)`, `0 Error(s)`.
Run: `dotnet test Dse.sln --nologo` — expect all green.

```bash
git add src/Dse.Core/Testing src/Dse.Core/Catalogue/CoreDescriptors.cs src/Dse.Components/ComponentsModule.cs tests/Dse.Core.Tests/Catalogue/CatalogueConformanceTests.cs tests/Dse.Components.Tests/Catalogue
git commit -m "feat(catalogue): check descriptors against the instances they build"
```

---
### Task 5: Descriptors — mechanical

Six types. The conformance and sweep tests from Task 4 are the tests; this task
makes them fail by shrinking `Pending`, then makes them pass.

**Rules for every descriptor in Tasks 5–8:**
- The descriptor is `public static ComponentDescriptor Descriptor { get; } = …`
  inside the component's own class. **Place it after the class's
  `private static readonly FaultDescriptor[] Faults` field** (static
  initialisers run in textual order; a descriptor placed above `Faults` would
  capture `null`). Where a class has no `Faults` field, place it after the
  constants, before the instance fields.
- Inside a component class the simple name `Ports` means the inherited instance
  property, which is why the factory class is `PortSpec`. On the left of an
  object initialiser `Ports = [...]` is the descriptor's property and is fine.
- Units on ports, and every description, are documentation: conformance does
  not check them. Names, directions, value types, `required`, fault signatures,
  tag kinds and access, and telemetry names **are** checked. If conformance
  disagrees with a list below, the component is right: fix the descriptor and
  report the difference.
- Ranges on parameters mirror what the constructor enforces. Do not invent a
  tighter range than the constructor has; a missing range is only a worse error
  message (`DSE111` instead of `DSE103`), a wrong one rejects a valid plant.

**Files:**
- Create: `src/Dse.Components/Mechanical/MotorRatingGroup.cs`
- Modify: `src/Dse.Components/Mechanical/Motor.cs`, `Gearbox.cs`, `DrivePulley.cs`, `TailPulley.cs`, `BeltFriction.cs`, `MotorStarter.cs`
- Modify: `src/Dse.Components/ComponentsModule.cs`
- Modify: `tests/Dse.Components.Tests/Catalogue/ComponentsFixtures.cs`
- Modify: `tests/Dse.Components.Tests/Catalogue/ComponentsCatalogueTests.cs`
- Test: `tests/Dse.Components.Tests/Catalogue/MechanicalFactoryTests.cs`

**Interfaces:**
- Consumes: `Param`, `PortSpec`, `GroupDefinition`, `ComponentDescriptor`, `TagEntry`, `TelemetryKey`, `ParameterValues` (Tasks 2–3).
- Produces: `MotorRatingGroup.Definition` (`GroupDefinition` named `MotorRating`) and `MotorRatingGroup.Read(ParameterValues) → MotorRating`, reused by Task 8; `Motor.Descriptor`, `Gearbox.Descriptor`, `DrivePulley.Descriptor`, `TailPulley.Descriptor`, `BeltFriction.Descriptor`, `MotorStarter.Descriptor`. Type names: `motor`, `gearbox`, `drive-pulley`, `tail-pulley`, `belt-friction`, `motor-starter`.

- [ ] **Step 1: Shrink `Pending` and add the fixtures**

In `ComponentsCatalogueTests.cs` delete the line under `// Task 5 — mechanical`
(and the comment).

In `ComponentsFixtures.cs` replace `Create()`:

```csharp
    public static ConformanceFixtures Create() => new ConformanceFixtures()
        // Task 5 — mechanical
        .Parameters("motor", """{ "rating": { "ratedPowerW": 750, "ratedSpeedRadPerS": 150, "ratedCurrentA": 2 } }""")
        .Parameters("gearbox", """{ "ratio": 20 }""")
        .Parameters("drive-pulley", """{ "diameterM": 0.5 }""")
        .Parameters("tail-pulley", """{ "bearingDragN": 80 }""")
        .Parameters("belt-friction", """{ "emptyBeltMassKg": 250 }""");
```

- [ ] **Step 2: Run and see the sweep fail**

Run: `dotnet test tests/Dse.Components.Tests --nologo --filter FullyQualifiedName~ComponentsCatalogueTests`
Expected: `EveryConcreteNodeTransformAndHoldHasADescriptor` FAILS listing
`Dse.Components.Mechanical.BeltFriction`, `…DrivePulley`, `…Gearbox`, `…Motor`,
`…MotorStarter`, `…TailPulley`.

- [ ] **Step 3: Write the shared rating group**

`src/Dse.Components/Mechanical/MotorRatingGroup.cs`:

```csharp
using Dse.Core.Catalogue;

namespace Dse.Components.Mechanical;

/// <summary><see cref="MotorRating"/> as catalogue parameters, shared by the motor and the conveyor.</summary>
public static class MotorRatingGroup
{
    public static GroupDefinition Definition { get; } = new(
        "MotorRating",
        Param.Double("ratedPowerW", "Nameplate power.", "W", min: 0.0, exclusiveMin: true),
        Param.Double("ratedSpeedRadPerS", "Shaft speed at rated load.", "rad/s", min: 0.0, exclusiveMin: true),
        Param.Double("ratedCurrentA", "Current at rated load.", "A", min: 0.0, exclusiveMin: true),
        Param.Double("noLoadCurrentFraction", "Current at zero torque, as a fraction of rated.", @default: 0.3, min: 0.0),
        Param.Double("lockedRotorCurrentMultiple", "Starting current, as a multiple of rated.", @default: 6.0, min: 0.0),
        Param.Double("breakdownTorqueMultiple", "Torque above which the motor stalls, as a multiple of rated.", @default: 2.5, min: 0.0),
        Param.Double("accelerationTimeConstantS", "First-order approach to target speed when energised.", "s", @default: 1.0, min: 0.0),
        Param.Double("coastTimeConstantS", "First-order decay to rest when de-energised.", "s", @default: 3.0, min: 0.0),
        Param.Double("thermalTimeConstantS", "Time constant of the I²t thermal state.", "s", @default: 60.0, min: 0.0),
        Param.Double("speedDroopFraction", "Speed lost at rated torque, as a fraction of rated speed.", @default: 0.03, min: 0.0));

    public static MotorRating Read(ParameterValues p)
    {
        ArgumentNullException.ThrowIfNull(p);
        return new MotorRating(
            p.Double("ratedPowerW"),
            p.Double("ratedSpeedRadPerS"),
            p.Double("ratedCurrentA"),
            p.Double("noLoadCurrentFraction"),
            p.Double("lockedRotorCurrentMultiple"),
            p.Double("breakdownTorqueMultiple"),
            p.Double("accelerationTimeConstantS"),
            p.Double("coastTimeConstantS"),
            p.Double("thermalTimeConstantS"),
            p.Double("speedDroopFraction"));
    }
}
```

The defaults are `MotorRating`'s own; open `MotorRating.cs` and confirm each
one. If the `Motor` constructor rejects a zero that a `min: 0.0` above allows,
leave the range as it is: the constructor's message will surface as `DSE111`.

- [ ] **Step 4: Write the six descriptors**

Add `using Dse.Core.Catalogue;` to each file.

`Motor.cs`, after `Faults`:

```csharp
    public static ComponentDescriptor Descriptor { get; } = new(
        "motor",
        ComponentCategory.Mechanical,
        "An induction motor: first-order speed response, current from torque, an I²t thermal state and a breakdown stall.",
        (id, p) => new Motor(id, MotorRatingGroup.Read(p.Group("rating"))))
    {
        Parameters = [Param.Group("rating", "The nameplate and model constants.", MotorRatingGroup.Definition)],
        Ports =
        [
            PortSpec.In<bool>("Energised", description: "The contactor feeding the motor is closed."),
            PortSpec.In<double>("TorqueDemand", "N·m", "Load torque reflected to the shaft. Latched: read as it stood at the end of the previous tick."),
            PortSpec.Out<double>("Speed", "rad/s"),
            PortSpec.Out<double>("Torque", "N·m"),
            PortSpec.Out<double>("Current", "A"),
            PortSpec.Out<double>("ThermalState", description: "I²t state; 1.0 is the rated continuous limit."),
            PortSpec.Out<bool>("AtSpeed"),
        ],
        Faults = Faults,
        Telemetry = [new TelemetryKey("Speed", "rad/s"), new TelemetryKey("Current", "A"), new TelemetryKey("ThermalState")],
    };
```

`Gearbox.cs`, before the instance members:

```csharp
    public static ComponentDescriptor Descriptor { get; } = new(
        "gearbox",
        ComponentCategory.Mechanical,
        "A fixed-ratio reducer: divides speed, multiplies torque demand back towards the motor, loses a fixed efficiency.",
        (id, p) => new Gearbox(id, p.Double("ratio"), p.Double("efficiency")))
    {
        Parameters =
        [
            Param.Double("ratio", "Input speed over output speed.", min: 0.0, exclusiveMin: true),
            Param.Double("efficiency", "Fraction of power transmitted.", @default: 0.95, min: 0.0, max: 1.0, exclusiveMin: true),
        ],
        Ports =
        [
            PortSpec.In<double>("InputSpeed", "rad/s"),
            PortSpec.In<double>("OutputTorqueDemand", "N·m", "Latched."),
            PortSpec.Out<double>("OutputSpeed", "rad/s"),
            PortSpec.Out<double>("InputTorqueDemand", "N·m"),
        ],
    };
```

`DrivePulley.cs`, after `Faults`:

```csharp
    public static ComponentDescriptor Descriptor { get; } = new(
        "drive-pulley",
        ComponentCategory.Mechanical,
        "Turns shaft speed into belt speed and belt force into a torque demand.",
        (id, p) => new DrivePulley(id, p.Double("diameterM"), p.Double("bearingDragN")))
    {
        Parameters =
        [
            Param.Double("diameterM", "Pulley diameter.", "m", min: 0.0, exclusiveMin: true),
            Param.Double("bearingDragN", "Constant drag from the bearings.", "N", @default: 0.0, min: 0.0),
        ],
        Ports =
        [
            PortSpec.In<double>("ShaftSpeed", "rad/s"),
            PortSpec.In<double>("BeltForce", "N"),
            PortSpec.Out<double>("BeltSpeed", "m/s"),
            PortSpec.Out<double>("TorqueDemand", "N·m"),
        ],
        Faults = Faults,
    };
```

`TailPulley.cs`, after `Faults`:

```csharp
    public static ComponentDescriptor Descriptor { get; } = new(
        "tail-pulley",
        ComponentCategory.Mechanical,
        "The idle pulley at the tail: contributes bearing drag.",
        (id, p) => new TailPulley(id, p.Double("bearingDragN")))
    {
        Parameters = [Param.Double("bearingDragN", "Constant drag from the bearings.", "N", min: 0.0)],
        Ports = [PortSpec.Out<double>("Drag", "N")],
        Faults = Faults,
    };
```

`BeltFriction.cs`, before the instance members:

```csharp
    public static ComponentDescriptor Descriptor { get; } = new(
        "belt-friction",
        ComponentCategory.Mechanical,
        "Rolling resistance of a loaded belt: force from carried mass, belt mass and added drag.",
        (id, p) => new BeltFriction(id, p.Double("emptyBeltMassKg"), p.Double("frictionCoefficient")))
    {
        Parameters =
        [
            Param.Double("emptyBeltMassKg", "Mass of the moving belt and idlers with no load.", "kg", min: 0.0),
            Param.Double("frictionCoefficient", "Rolling resistance coefficient.", @default: 0.03, min: 0.0),
        ],
        Ports =
        [
            PortSpec.In<double>("Load", "kg", "Mass on the belt."),
            PortSpec.In<double>("Drag", "N", "Additional drag, such as the tail pulley's."),
            PortSpec.Out<double>("Force", "N"),
        ],
    };
```

`MotorStarter.cs`, after `Faults` (add `using Dse.Io;` if it is not there):

```csharp
    public static ComponentDescriptor Descriptor { get; } = new(
        "motor-starter",
        ComponentCategory.Mechanical,
        "A direct-on-line starter with a thermal overload relay: closes on command when safe, trips on thermal state, resets on a rising edge.",
        (id, p) => new MotorStarter(id, p.Double("tripLevel"), p.Double("resetLevel")))
    {
        Parameters =
        [
            Param.Double("tripLevel", "Thermal state at which the overload relay trips.", @default: 1.1, min: 0.0, exclusiveMin: true),
            Param.Double("resetLevel", "Thermal state below which a reset is accepted. Must be below tripLevel.", @default: 0.9),
        ],
        Ports =
        [
            PortSpec.In<bool>("Command", description: "Run command."),
            PortSpec.In<bool>("SafetyOk", description: "Safety circuit healthy; defaults to true when unwired."),
            PortSpec.In<double>("ThermalState", description: "The motor's thermal state."),
            PortSpec.In<bool>("Reset", description: "Overload reset, rising edge."),
            PortSpec.Out<bool>("Contactor"),
            PortSpec.Out<bool>("Tripped"),
        ],
        Faults = Faults,
        Tags =
        [
            new TagEntry("Command", TagKind.Bool, TagAccess.ReadWrite),
            new TagEntry("Reset", TagKind.Bool, TagAccess.ReadWrite),
            new TagEntry("Contactor", TagKind.Bool, TagAccess.ReadOnly),
            new TagEntry("Tripped", TagKind.Bool, TagAccess.ReadOnly),
        ],
    };
```

- [ ] **Step 5: Register them**

In `ComponentsModule.Register`, add `using Dse.Components.Mechanical;` and, after
the signal block:

```csharp
        // Mechanical
        builder.Add(Motor.Descriptor);
        builder.Add(Gearbox.Descriptor);
        builder.Add(DrivePulley.Descriptor);
        builder.Add(TailPulley.Descriptor);
        builder.Add(BeltFriction.Descriptor);
        builder.Add(MotorStarter.Descriptor);
```

- [ ] **Step 6: Run conformance and the sweep**

Run: `dotnet test tests/Dse.Components.Tests --nologo --filter FullyQualifiedName~ComponentsCatalogueTests`
Expected: PASS, 2 tests. If `EveryDescriptorMatchesWhatItBuilds` lists
mismatches, fix the descriptors to match and note each one in the report.

- [ ] **Step 7: Prove a factory passes its parameters through**

Conformance proves shape, not values. One test per task proves the factory
wires parameters to the right constructor arguments.

Create `tests/Dse.Components.Tests/Catalogue/MechanicalFactoryTests.cs`:

```csharp
using System.Text.Json;
using Dse.Components.Mechanical;
using Dse.Core.Catalogue;

namespace Dse.Components.Tests.Catalogue;

public class MechanicalFactoryTests
{
    internal static T Build<T>(ComponentDescriptor descriptor, string json, BindingContext? context = null)
        where T : class
    {
        using JsonDocument document = JsonDocument.Parse(json);
        var issues = new List<BindingIssue>();
        ParameterValues? values = ParameterBinder.Bind(
            descriptor.Parameters, document.RootElement, "$", context ?? new BindingContext(ComponentsFixtures.Catalogue), construct: true, issues);
        Assert.Empty(issues);
        return Assert.IsType<T>(descriptor.Factory("X", values!));
    }

    [Fact]
    public void TheMotorFactoryReadsTheWholeRating()
    {
        Motor motor = Build<Motor>(Motor.Descriptor, """
            { "rating": { "ratedPowerW": 750, "ratedSpeedRadPerS": 150, "ratedCurrentA": 2,
                          "noLoadCurrentFraction": 0.31, "lockedRotorCurrentMultiple": 6.1, "breakdownTorqueMultiple": 2.6,
                          "accelerationTimeConstantS": 1.1, "coastTimeConstantS": 3.1, "thermalTimeConstantS": 61,
                          "speedDroopFraction": 0.04 } }
            """);

        Assert.Equal(new MotorRating(750, 150, 2, 0.31, 6.1, 2.6, 1.1, 3.1, 61, 0.04), motor.Rating);
        Assert.Equal("X", motor.Id);
    }

    [Fact]
    public void TheMotorFactoryAppliesTheRatingDefaults()
    {
        Motor motor = Build<Motor>(Motor.Descriptor, """{ "rating": { "ratedPowerW": 750, "ratedSpeedRadPerS": 150, "ratedCurrentA": 2 } }""");

        Assert.Equal(new MotorRating(750, 150, 2), motor.Rating);
    }

    [Fact]
    public void TheGearboxAndStarterFactoriesPassTheirArguments()
    {
        Gearbox gearbox = Build<Gearbox>(Gearbox.Descriptor, """{ "ratio": 20, "efficiency": 0.9 }""");
        MotorStarter starter = Build<MotorStarter>(MotorStarter.Descriptor, """{ "tripLevel": 1.2, "resetLevel": 0.8 }""");

        Assert.Equal(20.0, gearbox.Ratio);
        Assert.Equal(0.9, gearbox.Efficiency);
        Assert.Equal(1.2, starter.TripLevel);
        Assert.Equal(0.8, starter.ResetLevel);
    }
}
```

`TheMotorFactoryAppliesTheRatingDefaults` is the guard on Step 3's defaults: it
fails if any default in `MotorRatingGroup` differs from `MotorRating`'s.

Run: `dotnet test tests/Dse.Components.Tests --nologo --filter FullyQualifiedName~MechanicalFactoryTests`
Expected: PASS, 3 tests.

- [ ] **Step 8: Build Release, run everything, commit**

Run: `dotnet build Dse.sln -c Release --nologo` — expect `0 Warning(s)`, `0 Error(s)`.
Run: `dotnet test Dse.sln --nologo` — expect all green.

```bash
git add src/Dse.Components tests/Dse.Components.Tests/Catalogue
git commit -m "feat(catalogue): describe the mechanical components"
```

---
### Task 6: Descriptors — instruments and safety

Ten types. Seven instruments share a spec group, two outputs, a tag and a
telemetry key; two safety switches share everything but their name.

The rules at the top of Task 5 apply.

**Files:**
- Create: `src/Dse.Components/Instruments/InstrumentCatalogue.cs`
- Modify: `src/Dse.Components/Instruments/SpeedSensor.cs`, `CurrentSensor.cs`, `TemperatureSensor.cs`, `BeltScale.cs`, `Pyrometer.cs`, `ZeroSpeedSwitch.cs`, `PartCounter.cs`
- Modify: `src/Dse.Components/Safety/SafetySwitch.cs`, `EStop.cs`, `PullKey.cs`, `SafetyRelay.cs`
- Modify: `src/Dse.Components/ComponentsModule.cs`
- Modify: `tests/Dse.Components.Tests/Catalogue/ComponentsFixtures.cs`, `ComponentsCatalogueTests.cs`
- Test: `tests/Dse.Components.Tests/Catalogue/InstrumentFactoryTests.cs`

**Interfaces:**
- Consumes: Tasks 2–4; `MechanicalFactoryTests.Build<T>` (Task 5) as the test helper.
- Produces: `InstrumentCatalogue.Spec` (`GroupDefinition` named `InstrumentSpec`), `InstrumentCatalogue.ReadSpec(ParameterValues) → InstrumentSpec`, `InstrumentCatalogue.Outputs`, `.ValueTag`, `.Truth`; a `Descriptor` on each of the ten classes. Type names: `speed-sensor`, `current-sensor`, `temperature-sensor`, `belt-scale`, `pyrometer`, `zero-speed-switch`, `part-counter`, `e-stop`, `pull-key`, `safety-relay`.

- [ ] **Step 1: Shrink `Pending` and add the fixtures**

In `ComponentsCatalogueTests.cs` delete the two lines under
`// Task 6 — instruments and safety` (and the comment).

In `ComponentsFixtures.cs`, add `using Dse.Core.Flow;` and append to the chain
in `Create()` (before the final `;`):

```csharp
        // Task 6 — instruments and safety
        .Node(new BulkBelt("BELT", 10.0, 0.5, 2.0, 100.0))
        .Parameters("speed-sensor", """{ "spec": { "unit": "m/s", "rangeLow": 0, "rangeHigh": 5 } }""")
        .Parameters("current-sensor", """{ "spec": { "unit": "A", "rangeLow": 0, "rangeHigh": 20 } }""")
        .Parameters("temperature-sensor", """{ "spec": { "unit": "°C", "rangeLow": 0, "rangeHigh": 300 } }""")
        .Parameters("belt-scale", """{ "belt": "BELT", "positionM": 5, "spec": { "unit": "t/h", "rangeLow": 0, "rangeHigh": 800 } }""")
        .Parameters("pyrometer", """{ "target": "BELT", "positionM": 5, "windowM": 0.5, "spec": { "unit": "°C", "rangeLow": 0, "rangeHigh": 1200 } }""")
        .Parameters("zero-speed-switch", """{ "spec": { "unit": "m/s", "rangeLow": 0, "rangeHigh": 5 }, "thresholdSpeed": 0.02, "delaySeconds": 1 }""")
        .Parameters("part-counter", """{ "belt": "BELT", "positionM": 5, "windowM": 0.2 }""")
        .Parameters("safety-relay", """{ "channels": 3 }""")
```

- [ ] **Step 2: Run and see the sweep fail**

Run: `dotnet test tests/Dse.Components.Tests --nologo --filter FullyQualifiedName~ComponentsCatalogueTests`
Expected: the sweep FAILS listing the ten types.

- [ ] **Step 3: Write the shared instrument pieces**

`src/Dse.Components/Instruments/InstrumentCatalogue.cs`:

```csharp
using Dse.Core.Catalogue;
using Dse.Io;

namespace Dse.Components.Instruments;

/// <summary>What every <see cref="InstrumentBase"/> contributes to its descriptor.</summary>
public static class InstrumentCatalogue
{
    /// <summary><see cref="InstrumentSpec"/> as catalogue parameters.</summary>
    public static GroupDefinition Spec { get; } = new(
        "InstrumentSpec",
        Param.String("unit", "Engineering unit of the reading; becomes the tag's unit."),
        Param.Double("rangeLow", "Bottom of the calibrated range. The reading clamps here."),
        Param.Double("rangeHigh", "Top of the calibrated range; must exceed rangeLow."),
        Param.Double("noiseSigma", "Standard deviation of Gaussian noise per tick, in the reading's unit.", @default: 0.0, min: 0.0),
        Param.Double("lagSeconds", "First-order response time constant.", "s", @default: 0.0, min: 0.0));

    /// <summary>The two outputs every instrument has.</summary>
    public static IReadOnlyList<PortDescriptor> Outputs { get; } =
    [
        PortSpec.Out<double>("Value", description: "The reading, in the spec's unit, clamped to its range."),
        PortSpec.Out<TagQuality>("Health", description: "Good, Uncertain when saturated, Bad on a fail fault."),
    ];

    /// <summary>The reading's tag. Its unit and range come from the spec, so none is stated here.</summary>
    public static TagEntry ValueTag { get; } = new("Value", TagKind.Double, TagAccess.ReadOnly);

    /// <summary>The true value before the instrument touched it. Unit from the spec.</summary>
    public static TelemetryKey Truth { get; } = new("Truth");

    public static InstrumentSpec ReadSpec(ParameterValues p)
    {
        ArgumentNullException.ThrowIfNull(p);
        return new InstrumentSpec(
            p.String("unit"), p.Double("rangeLow"), p.Double("rangeHigh"), p.Double("noiseSigma"), p.Double("lagSeconds"));
    }
}
```

- [ ] **Step 4: Write the seven instrument descriptors**

Add `using Dse.Core.Catalogue;` (and `using Dse.Core.Flow;`, `using Dse.Io;`
where the code below needs them) to each file. None of the six
`InstrumentBase` subclasses has a `Faults` field; place `Descriptor` first in
the class.

`SpeedSensor.cs`:

```csharp
    public static ComponentDescriptor Descriptor { get; } = new(
        "speed-sensor",
        ComponentCategory.Instrumentation,
        "Measures a speed signal.",
        (id, p) => new SpeedSensor(id, InstrumentCatalogue.ReadSpec(p.Group("spec"))))
    {
        Parameters = [Param.Group("spec", "Unit, range, noise and lag.", InstrumentCatalogue.Spec)],
        Ports = [PortSpec.In<double>("Speed", description: "The true speed."), .. InstrumentCatalogue.Outputs],
        Faults = InstrumentFaults.All,
        Tags = [InstrumentCatalogue.ValueTag],
        Telemetry = [InstrumentCatalogue.Truth],
    };
```

`CurrentSensor.cs`:

```csharp
    public static ComponentDescriptor Descriptor { get; } = new(
        "current-sensor",
        ComponentCategory.Instrumentation,
        "Measures a current signal.",
        (id, p) => new CurrentSensor(id, InstrumentCatalogue.ReadSpec(p.Group("spec"))))
    {
        Parameters = [Param.Group("spec", "Unit, range, noise and lag.", InstrumentCatalogue.Spec)],
        Ports = [PortSpec.In<double>("Current", "A", "The true current."), .. InstrumentCatalogue.Outputs],
        Faults = InstrumentFaults.All,
        Tags = [InstrumentCatalogue.ValueTag],
        Telemetry = [InstrumentCatalogue.Truth],
    };
```

`TemperatureSensor.cs`:

```csharp
    public static ComponentDescriptor Descriptor { get; } = new(
        "temperature-sensor",
        ComponentCategory.Instrumentation,
        "Measures a temperature signal.",
        (id, p) => new TemperatureSensor(id, InstrumentCatalogue.ReadSpec(p.Group("spec"))))
    {
        Parameters = [Param.Group("spec", "Unit, range, noise and lag.", InstrumentCatalogue.Spec)],
        Ports = [PortSpec.In<double>("Temperature", "°C", "The true temperature."), .. InstrumentCatalogue.Outputs],
        Faults = InstrumentFaults.All,
        Tags = [InstrumentCatalogue.ValueTag],
        Telemetry = [InstrumentCatalogue.Truth],
    };
```

`BeltScale.cs`:

```csharp
    public static ComponentDescriptor Descriptor { get; } = new(
        "belt-scale",
        ComponentCategory.Instrumentation,
        "Weighs the material passing one point of a belt and reports a mass flow from the load there and the belt speed.",
        (id, p) => new BeltScale(id, p.Reference<IMaterialObservable>("belt"), p.Double("positionM"), InstrumentCatalogue.ReadSpec(p.Group("spec"))))
    {
        Parameters =
        [
            Param.Reference<IMaterialObservable>("belt", "The belt weighed: a belt, or a conveyor that has one."),
            Param.Double("positionM", "Distance of the weigh frame from the tail.", "m", min: 0.0),
            Param.Group("spec", "Unit, range, noise and lag.", InstrumentCatalogue.Spec),
        ],
        Ports = [PortSpec.In<double>("Speed", "m/s", "Belt speed."), .. InstrumentCatalogue.Outputs],
        Faults = InstrumentFaults.All,
        Tags = [InstrumentCatalogue.ValueTag],
        Telemetry = [InstrumentCatalogue.Truth],
    };
```

`Pyrometer.cs`:

```csharp
    public static ComponentDescriptor Descriptor { get; } = new(
        "pyrometer",
        ComponentCategory.Instrumentation,
        "Reads the temperature of whatever is in its window; reads the background when nothing is.",
        (id, p) => new Pyrometer(
            id, p.Reference<IMaterialObservable>("target"), p.Double("positionM"), p.Double("windowM"), InstrumentCatalogue.ReadSpec(p.Group("spec"))))
    {
        Parameters =
        [
            Param.Reference<IMaterialObservable>("target", "What it looks at: a belt, a conveyor, a chute or a process unit."),
            Param.Double("positionM", "Centre of the window, from the tail.", "m", min: 0.0),
            Param.Double("windowM", "Length of the window.", "m", min: 0.0),
            Param.Group("spec", "Unit, range, noise and lag.", InstrumentCatalogue.Spec),
        ],
        Ports = [PortSpec.In<double>("Background", "°C", "Read when the window is empty; defaults to 20."), .. InstrumentCatalogue.Outputs],
        Faults = InstrumentFaults.All,
        Tags = [InstrumentCatalogue.ValueTag],
        Telemetry = [InstrumentCatalogue.Truth],
    };
```

`ZeroSpeedSwitch.cs`:

```csharp
    public static ComponentDescriptor Descriptor { get; } = new(
        "zero-speed-switch",
        ComponentCategory.Instrumentation,
        "Asserts Stopped when the measured speed has stayed below a threshold for a delay.",
        (id, p) => new ZeroSpeedSwitch(id, InstrumentCatalogue.ReadSpec(p.Group("spec")), p.Double("thresholdSpeed"), p.Double("delaySeconds")))
    {
        Parameters =
        [
            Param.Group("spec", "Unit, range, noise and lag.", InstrumentCatalogue.Spec),
            Param.Double("thresholdSpeed", "Speed below which the belt counts as stopped, in the spec's unit.", min: 0.0),
            Param.Double("delaySeconds", "How long the speed must stay below the threshold.", "s", min: 0.0),
        ],
        Ports = [PortSpec.In<double>("Speed", description: "The true speed."), .. InstrumentCatalogue.Outputs, PortSpec.Out<bool>("Stopped")],
        Faults = InstrumentFaults.All,
        Tags = [InstrumentCatalogue.ValueTag, new TagEntry("Stopped", TagKind.Bool, TagAccess.ReadOnly)],
        Telemetry = [InstrumentCatalogue.Truth],
    };
```

`PartCounter.cs`, after its `Faults` field:

```csharp
    public static ComponentDescriptor Descriptor { get; } = new(
        "part-counter",
        ComponentCategory.Instrumentation,
        "Counts items entering a window on a belt and reports whether one is in it now.",
        (id, p) => new PartCounter(id, p.Reference<IMaterialObservable>("belt"), p.Double("positionM"), p.Double("windowM")))
    {
        Parameters =
        [
            Param.Reference<IMaterialObservable>("belt", "The belt watched: a discrete belt, or a composite that has one."),
            Param.Double("positionM", "Centre of the window, from the tail.", "m", min: 0.0),
            Param.Double("windowM", "Length of the window.", "m", min: 0.0),
        ],
        Ports = [PortSpec.Out<long>("Count", "count"), PortSpec.Out<bool>("Present")],
        Faults = Faults,
        Tags = [new TagEntry("Count", TagKind.Int64, TagAccess.ReadOnly, "count"), new TagEntry("Present", TagKind.Bool, TagAccess.ReadOnly)],
        Telemetry = [new TelemetryKey("Count", "count")],
    };
```

- [ ] **Step 5: Write the three safety descriptors**

`EStop` and `PullKey` cannot see `SafetySwitch`'s private `Faults`, so the base
class builds the descriptor for them. In `SafetySwitch.cs`, after `Faults`
(add `using Dse.Core.Catalogue;`, `using Dse.Core.Graph;`, `using Dse.Io;`):

```csharp
    /// <summary>The descriptor of a concrete switch; all of them share ports, faults and tags.</summary>
    protected static ComponentDescriptor Describe(string type, string description, Func<string, ISimNode> create) =>
        new(type, ComponentCategory.Safety, description, (id, p) => create(id))
        {
            Ports =
            [
                PortSpec.In<bool>("Actuated", description: "The operator has pressed or pulled it."),
                PortSpec.Out<bool>("Ok", description: "The safety loop through this switch is healthy."),
            ],
            Faults = Faults,
            Tags =
            [
                new TagEntry("Actuated", TagKind.Bool, TagAccess.ReadWrite),
                new TagEntry("Ok", TagKind.Bool, TagAccess.ReadOnly),
            ],
        };
```

`EStop.cs` (add `using Dse.Core.Catalogue;`):

```csharp
    public static ComponentDescriptor Descriptor { get; } =
        Describe("e-stop", "A latching emergency-stop button in a safety loop.", id => new EStop(id));
```

`PullKey.cs` (add `using Dse.Core.Catalogue;`):

```csharp
    public static ComponentDescriptor Descriptor { get; } =
        Describe("pull-key", "A pull-wire switch along a conveyor, in a safety loop.", id => new PullKey(id));
```

`SafetyRelay.cs`, after `Faults` (add `using Dse.Core.Catalogue;`, `using Dse.Io;`):

```csharp
    public static ComponentDescriptor Descriptor { get; } = new(
        "safety-relay",
        ComponentCategory.Safety,
        "Energises only while every channel is healthy; once dropped, stays dropped until a reset edge.",
        (id, p) => new SafetyRelay(id, p.Int("channels")))
    {
        Parameters = [Param.Int("channels", "Number of monitored channels.", min: 1)],
        Ports =
        [
            PortSpec.In<bool>("Channel{n}", description: "One monitored loop; healthy when unwired.", repeat: new PortRepeat("channels")),
            PortSpec.In<bool>("Reset", description: "Safety reset, rising edge."),
            PortSpec.Out<bool>("Ok"),
        ],
        Faults = Faults,
        Tags = [new TagEntry("Reset", TagKind.Bool, TagAccess.ReadWrite), new TagEntry("Ok", TagKind.Bool, TagAccess.ReadOnly)],
    };
```

- [ ] **Step 6: Register them**

In `ComponentsModule.Register`, add `using Dse.Components.Instruments;` and
`using Dse.Components.Safety;`, then:

```csharp
        // Instrumentation
        builder.Add(SpeedSensor.Descriptor);
        builder.Add(CurrentSensor.Descriptor);
        builder.Add(TemperatureSensor.Descriptor);
        builder.Add(BeltScale.Descriptor);
        builder.Add(Pyrometer.Descriptor);
        builder.Add(ZeroSpeedSwitch.Descriptor);
        builder.Add(PartCounter.Descriptor);

        // Safety
        builder.Add(EStop.Descriptor);
        builder.Add(PullKey.Descriptor);
        builder.Add(SafetyRelay.Descriptor);
```

- [ ] **Step 7: Run conformance and the sweep**

Run: `dotnet test tests/Dse.Components.Tests --nologo --filter FullyQualifiedName~ComponentsCatalogueTests`
Expected: PASS, 2 tests.

- [ ] **Step 8: Prove the factories pass references and specs through**

Create `tests/Dse.Components.Tests/Catalogue/InstrumentFactoryTests.cs`:

```csharp
using Dse.Components.Instruments;
using Dse.Components.Safety;
using Dse.Core.Catalogue;
using Dse.Core.Flow;

namespace Dse.Components.Tests.Catalogue;

public class InstrumentFactoryTests
{
    [Fact]
    public void TheBeltScaleFactoryResolvesItsBeltAndReadsItsSpec()
    {
        var belt = new BulkBelt("CV", 10.0, 0.5, 2.0, 100.0);
        BindingContext context = new BindingContext(ComponentsFixtures.Catalogue).AddNode(belt);

        BeltScale scale = MechanicalFactoryTests.Build<BeltScale>(
            BeltScale.Descriptor,
            """{ "belt": "CV", "positionM": 4, "spec": { "unit": "t/h", "rangeLow": 0, "rangeHigh": 800, "noiseSigma": 0.5, "lagSeconds": 2 } }""",
            context);

        Assert.Equal(4.0, scale.PositionM);
        Assert.Equal(new InstrumentSpec("t/h", 0.0, 800.0, 0.5, 2.0), scale.Spec);
    }

    [Fact]
    public void AnInvertedRangeIsTheConstructorsToReject()
    {
        using var document = System.Text.Json.JsonDocument.Parse("""{ "spec": { "unit": "A", "rangeLow": 5, "rangeHigh": 1 } }""");
        var issues = new List<BindingIssue>();
        ParameterValues? values = ParameterBinder.Bind(
            CurrentSensor.Descriptor.Parameters, document.RootElement, "$", new BindingContext(ComponentsFixtures.Catalogue), true, issues);

        Assert.Empty(issues);
        Assert.Throws<ArgumentException>(() => CurrentSensor.Descriptor.Factory("X", values!));
    }

    [Fact]
    public void TheRelayFactoryMakesTheChannelsAsked()
    {
        SafetyRelay relay = MechanicalFactoryTests.Build<SafetyRelay>(SafetyRelay.Descriptor, """{ "channels": 4 }""");

        Assert.Contains(relay.Ports, p => p.Name == "Channel4");
        Assert.DoesNotContain(relay.Ports, p => p.Name == "Channel5");
    }
}
```

The second test documents a deliberate gap: a cross-parameter rule (`rangeHigh`
above `rangeLow`) is not expressible as a parameter range, so the constructor
catches it and the loader reports `DSE111`.

Run: `dotnet test tests/Dse.Components.Tests --nologo --filter FullyQualifiedName~InstrumentFactoryTests`
Expected: PASS, 3 tests.

- [ ] **Step 9: Build Release, run everything, commit**

Run: `dotnet build Dse.sln -c Release --nologo` — expect `0 Warning(s)`, `0 Error(s)`.
Run: `dotnet test Dse.sln --nologo` — expect all green.

```bash
git add src/Dse.Components tests/Dse.Components.Tests/Catalogue
git commit -m "feat(catalogue): describe the instruments and the safety circuit"
```

---
### Task 7: Descriptors — flow, transforms and holds

Ten flow nodes (two of them in Core), three transforms, five hold conditions.
This is where `Material`, `MaterialState`, `GroupList`, `Object`, `ObjectList`,
a by-name `PortRepeat` and `Provides` are first used for real.

The rules at the top of Task 5 apply.

**Files:**
- Modify: `src/Dse.Core/Catalogue/CoreDescriptors.cs` (+ `BulkBelt`, `DiscreteBelt`)
- Create: `src/Dse.Components/Transforms/TransformDescriptors.cs`
- Create: `src/Dse.Components/Flow/HoldDescriptors.cs`
- Modify: `src/Dse.Components/Flow/BulkSource.cs`, `BulkSink.cs`, `ItemSource.cs`, `ItemSink.cs`, `TransferChute.cs`, `Former.cs`, `BulkProcessUnit.cs`, `ItemProcessUnit.cs`
- Modify: `src/Dse.Components/ComponentsModule.cs`
- Modify: `tests/Dse.Components.Tests/Catalogue/ComponentsFixtures.cs`, `ComponentsCatalogueTests.cs`
- Test: `tests/Dse.Components.Tests/Catalogue/FlowFactoryTests.cs`

**Interfaces:**
- Consumes: Tasks 2–4; `MechanicalFactoryTests.Build<T>`.
- Produces: `CoreDescriptors.BeltBulk`, `CoreDescriptors.BeltDiscrete`; `TransformDescriptors.Thermal`, `.Moisture`, `.Residence`; `HoldDescriptors.All` (an `IReadOnlyList<ObjectDescriptor>` of five); a `Descriptor` on each of the eight flow classes. Type names: `bulk-belt`, `discrete-belt`, `bulk-source`, `bulk-sink`, `item-source`, `item-sink`, `transfer-chute`, `former`, `bulk-process-unit`, `item-process-unit`; transforms `thermal-transfer`, `moisture-loss`, `residence-accumulator`; holds `for-seconds`, `temperature-at-least`, `temperature-at-most`, `state-at-least`, `all`.

- [ ] **Step 1: Shrink `Pending`, retire `PendingHolds`, add the fixtures**

In `ComponentsCatalogueTests.cs`:
- delete the three lines under `// Task 7 — flow, transforms, holds` (and the comment);
- delete the `PendingHolds` constant and its doc comment;
- in `EveryConcreteNodeTransformAndHoldHasADescriptor`, delete the
  `holdsMissing` line and replace the three assertions with:

```csharp
        Assert.Empty(stale);
        Assert.Empty(missing);
```

In `ComponentsFixtures.cs`, add `using Dse.Core.Catalogue;` if absent and append
to the chain in `Create()`:

```csharp
        // Task 7 — flow, transforms, holds
        .Material(new MaterialDescriptor(new MaterialType("test-bulk", PayloadKind.Bulk, "soak"), new MaterialProperties(1000.0, 0.1, 20.0), "A bulk test material."))
        .Material(new MaterialDescriptor(new MaterialType("test-item", PayloadKind.Discrete, "soak"), new MaterialProperties(7800.0, 0.0, 20.0), "A discrete test material."))
        .Parameters("bulk-belt", """{ "lengthM": 10, "cellSizeM": 0.5, "maxSpeedMps": 2, "maxLinearDensityKgPerM": 100 }""")
        .Parameters("discrete-belt", """{ "lengthM": 10, "maxSpeedMps": 2 }""")
        .Parameters("bulk-source", """{ "material": "test-bulk", "rateKgPerS": 20 }""")
        .Parameters("item-source", """{ "material": "test-item", "itemMassKg": 12, "intervalSeconds": 5 }""")
        .Parameters("transfer-chute", """{ "capacityKg": 200 }""")
        .Parameters("former", """{ "input": "test-bulk", "output": "test-item", "pieceMassKg": 0.5, "cycleSeconds": 2, "hopperCapacityKg": 50 }""")
        .Parameters("bulk-process-unit", """
            { "recipe": [ { "inlet": "Flour", "material": "test-bulk", "massKg": 5 }, { "inlet": "Water", "material": "test-bulk", "massKg": 3 } ],
              "hold": { "type": "for-seconds", "seconds": 10 }, "output": "test-bulk" }
            """)
        .Parameters("item-process-unit", """{ "batchSize": 4, "hold": { "type": "for-seconds", "seconds": 10 } }""")
        .ObjectParameters(ObjectSlots.Transform, "thermal-transfer", """{ "timeConstantSeconds": 30 }""")
        .ObjectParameters(ObjectSlots.Transform, "moisture-loss", """{ "ratePerDegreeSecond": 0.0001, "thresholdTemperature": 100 }""")
        .ObjectParameters(ObjectSlots.Transform, "residence-accumulator", """{ "material": "test-item", "state": "soak", "thresholdTemperature": 700 }""")
        .ObjectParameters(ObjectSlots.Hold, "for-seconds", """{ "seconds": 10 }""")
        .ObjectParameters(ObjectSlots.Hold, "temperature-at-least", """{ "celsius": 180 }""")
        .ObjectParameters(ObjectSlots.Hold, "temperature-at-most", """{ "celsius": 40 }""")
        .ObjectParameters(ObjectSlots.Hold, "state-at-least", """{ "material": "test-item", "state": "soak", "value": 600 }""")
        .ObjectParameters(ObjectSlots.Hold, "all", """{ "conditions": [ { "type": "for-seconds", "seconds": 10 }, { "type": "temperature-at-least", "celsius": 180 } ] }""")
```

- [ ] **Step 2: Run and see the sweep fail**

Run: `dotnet test tests/Dse.Components.Tests --nologo --filter FullyQualifiedName~ComponentsCatalogueTests`
Expected: the sweep FAILS listing the ten flow types, the three transforms and
five `Dse.Components.Flow.Hold+…` classes.

- [ ] **Step 3: Write the transform and hold descriptors**

`src/Dse.Components/Transforms/TransformDescriptors.cs`:

```csharp
using Dse.Core.Catalogue;

namespace Dse.Components.Transforms;

/// <summary>The transforms a belt or process unit can apply to what it carries.</summary>
public static class TransformDescriptors
{
    public static ObjectDescriptor Thermal { get; } = new(
        ObjectSlots.Transform,
        "thermal-transfer",
        "Moves the material's temperature towards ambient with a first-order time constant.",
        p => new ThermalTransfer(p.Double("timeConstantSeconds")))
    {
        Parameters = [Param.Double("timeConstantSeconds", "Time to close 63% of the gap to ambient.", "s", min: 0.0, exclusiveMin: true)],
    };

    public static ObjectDescriptor Moisture { get; } = new(
        ObjectSlots.Transform,
        "moisture-loss",
        "Dries the material while it is hotter than a threshold, in proportion to the excess.",
        p => new MoistureLoss(p.Double("ratePerDegreeSecond"), p.Double("thresholdTemperature")))
    {
        Parameters =
        [
            Param.Double("ratePerDegreeSecond", "Moisture fraction lost per degree above the threshold, per second.", "1/(°C·s)", min: 0.0),
            Param.Double("thresholdTemperature", "Temperature above which drying happens.", "°C"),
        ],
    };

    public static ObjectDescriptor Residence { get; } = new(
        ObjectSlots.Transform,
        "residence-accumulator",
        "Accumulates, in one of the material's states, the seconds spent above a threshold temperature.",
        p => new ResidenceAccumulator(p.StateIndex("state"), p.Double("thresholdTemperature")))
    {
        Parameters =
        [
            Param.Material("material", "The material whose state is accumulated."),
            Param.MaterialState("state", "The state that accumulates.", "material"),
            Param.Double("thresholdTemperature", "Temperature above which time counts.", "°C"),
        ],
    };
}
```

`src/Dse.Components/Flow/HoldDescriptors.cs`:

```csharp
using Dse.Core.Catalogue;

namespace Dse.Components.Flow;

/// <summary>The conditions a process unit can hold a batch for. See <see cref="Hold"/>.</summary>
public static class HoldDescriptors
{
    public static IReadOnlyList<ObjectDescriptor> All { get; } =
    [
        new(ObjectSlots.Hold, "for-seconds", "Holds the batch for a fixed time.", p => Hold.ForSeconds(p.Double("seconds")))
        {
            Parameters = [Param.Double("seconds", "How long to hold.", "s", min: 0.0)],
        },
        new(ObjectSlots.Hold, "temperature-at-least", "Holds until the batch is at least this hot.", p => Hold.TemperatureAtLeast(p.Double("celsius")))
        {
            Parameters = [Param.Double("celsius", "The temperature to reach.", "°C")],
        },
        new(ObjectSlots.Hold, "temperature-at-most", "Holds until the batch has cooled to this.", p => Hold.TemperatureAtMost(p.Double("celsius")))
        {
            Parameters = [Param.Double("celsius", "The temperature to fall to.", "°C")],
        },
        new(ObjectSlots.Hold, "state-at-least", "Holds until one of the material's states reaches a value.", p => Hold.StateAtLeast(p.StateIndex("state"), p.Double("value")))
        {
            Parameters =
            [
                Param.Material("material", "The material whose state is watched."),
                Param.MaterialState("state", "The state watched.", "material"),
                Param.Double("value", "The value to reach."),
            ],
        },
        new(ObjectSlots.Hold, "all", "Holds until every listed condition is satisfied.", p => Hold.All(p.Objects<IHoldCondition>("conditions").ToArray()))
        {
            Parameters = [Param.ObjectList("conditions", "The conditions, all of which must hold.", ObjectSlots.Hold, minCount: 1)],
        },
    ];
}
```

- [ ] **Step 4: Write the two belt descriptors in Core**

Append to `CoreDescriptors` (add `using Dse.Core.Flow;`):

```csharp
    private static readonly ParameterDescriptor Transforms = Param.ObjectList(
        "transforms", "Applied, in order, every tick to everything the belt carries.", ObjectSlots.Transform);

    public static ComponentDescriptor BeltBulk { get; } = new(
        "bulk-belt",
        ComponentCategory.Flow,
        "A spatially discretised belt for bulk material: what is loaded travels, and stops where it is when the belt stops.",
        (id, p) => new BulkBelt(
            id, p.Double("lengthM"), p.Double("cellSizeM"), p.Double("maxSpeedMps"), p.Double("maxLinearDensityKgPerM"),
            p.Objects<IMaterialTransform>("transforms")))
    {
        Parameters =
        [
            Param.Double("lengthM", "Belt length; must be a whole number of cells.", "m", min: 0.0, exclusiveMin: true),
            Param.Double("cellSizeM", "Length of one cell. The belt may not advance more than one cell per tick (DSE006).", "m", min: 0.0, exclusiveMin: true),
            Param.Double("maxSpeedMps", "The fastest the belt will ever be driven.", "m/s", min: 0.0, exclusiveMin: true),
            Param.Double("maxLinearDensityKgPerM", "The most the belt can carry per metre.", "kg/m", min: 0.0, exclusiveMin: true),
            Transforms,
        ],
        Ports =
        [
            PortSpec.In<double>("Speed", "m/s"),
            PortSpec.In<double>("AmbientTemperature", "°C", "Defaults to 20."),
            PortSpec.Out<double>("Load", "kg", "Total mass on the belt."),
            PortSpec.Out<double>("PeakLinearDensity", "kg/m"),
        ],
        FlowPorts = [PortSpec.Inlet("In", PayloadKind.Bulk, "The tail."), PortSpec.Outlet("Out", PayloadKind.Bulk, "The head.")],
        Telemetry = [new TelemetryKey("Load", "kg")],
        Provides = [typeof(IMaterialObservable)],
    };

    public static ComponentDescriptor BeltDiscrete { get; } = new(
        "discrete-belt",
        ComponentCategory.Flow,
        "A belt carrying individual items at tracked positions, with an optional minimum spacing.",
        (id, p) => new DiscreteBelt(
            id, p.Double("lengthM"), p.Double("maxSpeedMps"), p.Double("minSpacingM"), p.Objects<IMaterialTransform>("transforms")))
    {
        Parameters =
        [
            Param.Double("lengthM", "Belt length.", "m", min: 0.0, exclusiveMin: true),
            Param.Double("maxSpeedMps", "The fastest the belt will ever be driven.", "m/s", min: 0.0, exclusiveMin: true),
            Param.Double("minSpacingM", "Closest two items may sit; must not exceed the length.", "m", @default: 0.0, min: 0.0),
            Transforms,
        ],
        Ports =
        [
            PortSpec.In<double>("Speed", "m/s"),
            PortSpec.In<double>("AmbientTemperature", "°C", "Defaults to 20."),
            PortSpec.Out<int>("ItemCount", "count"),
        ],
        FlowPorts = [PortSpec.Inlet("In", PayloadKind.Discrete, "The tail."), PortSpec.Outlet("Out", PayloadKind.Discrete, "The head.")],
        Provides = [typeof(IMaterialObservable)],
    };
```

`Transforms` must be declared above the two properties that use it. The
properties are not named `BulkBelt` and `DiscreteBelt` because, inside this
class, those names would then hide the types the factories construct; the same
is why `TransformDescriptors`' properties are `Thermal`, `Moisture`, `Residence`.

- [ ] **Step 5: Write the eight flow descriptors**

Add `using Dse.Core.Catalogue;` and `using Dse.Io;` to each file. Each goes
after the class's `Faults` field; `BulkSink` and `ItemSink` have none, so there
it goes first in the class.

`BulkSource.cs`:

```csharp
    public static ComponentDescriptor Descriptor { get; } = new(
        "bulk-source",
        ComponentCategory.Flow,
        "Creates bulk material at a commanded rate, from an optional finite hopper.",
        (id, p) => new BulkSource(
            id, p.Material("material"), p.Double("rateKgPerS"), p.MaterialProperties("material"),
            p.DoubleOr("hopperCapacityKg", double.PositiveInfinity)))
    {
        Parameters =
        [
            Param.Material("material", "What it feeds; new material takes this material's defined properties.", PayloadKind.Bulk),
            Param.Double("rateKgPerS", "Feed rate when nothing drives the Rate input.", "kg/s", min: 0.0),
            Param.Double("hopperCapacityKg", "Hopper size. Omit for unlimited.", "kg", min: 0.0, exclusiveMin: true, optional: true),
        ],
        Ports =
        [
            PortSpec.In<double>("Rate", "kg/s", "Defaults to rateKgPerS."),
            PortSpec.In<bool>("Enabled", description: "Defaults to true."),
            PortSpec.Out<double>("HopperMass", "kg"),
        ],
        FlowPorts = [PortSpec.Outlet("Out", PayloadKind.Bulk)],
        Faults = Faults,
        Tags =
        [
            new TagEntry("Enabled", TagKind.Bool, TagAccess.ReadWrite),
            new TagEntry("Rate", TagKind.Double, TagAccess.ReadWrite, "kg/s"),
            new TagEntry("HopperMass", TagKind.Double, TagAccess.ReadOnly, "kg"),
        ],
        Telemetry = [new TelemetryKey("Hopper", "kg"), new TelemetryKey("Sourced", "kg")],
    };
```

`BulkSink.cs`:

```csharp
    public static ComponentDescriptor Descriptor { get; } = new(
        "bulk-sink",
        ComponentCategory.Flow,
        "Accepts bulk material and destroys it, up to an optional capacity.",
        (id, p) => new BulkSink(id, p.DoubleOr("capacityKg", double.PositiveInfinity)))
    {
        Parameters = [Param.Double("capacityKg", "How much it will take. Omit for unlimited.", "kg", min: 0.0, exclusiveMin: true, optional: true)],
        Ports = [PortSpec.Out<double>("Received", "kg"), PortSpec.Out<double>("Rate", "kg/s"), PortSpec.Out<bool>("Full")],
        FlowPorts = [PortSpec.Inlet("In", PayloadKind.Bulk)],
        Tags =
        [
            new TagEntry("Received", TagKind.Double, TagAccess.ReadOnly, "kg"),
            new TagEntry("Rate", TagKind.Double, TagAccess.ReadOnly, "kg/s"),
            new TagEntry("Full", TagKind.Bool, TagAccess.ReadOnly),
        ],
        Telemetry = [new TelemetryKey("Received", "kg")],
    };
```

`ItemSource.cs`:

```csharp
    public static ComponentDescriptor Descriptor { get; } = new(
        "item-source",
        ComponentCategory.Flow,
        "Mints one discrete item at a fixed interval and queues it at the outlet.",
        (id, p) => new ItemSource(
            id, p.Material("material"), p.Double("itemMassKg"), p.Double("intervalSeconds"), p.MaterialProperties("material"),
            p.IntOr("queueCapacity", int.MaxValue)))
    {
        Parameters =
        [
            Param.Material("material", "What each item is; new items take this material's defined properties.", PayloadKind.Discrete),
            Param.Double("itemMassKg", "Mass of one item.", "kg", min: 0.0, exclusiveMin: true),
            Param.Double("intervalSeconds", "Time between items.", "s", min: 0.0, exclusiveMin: true),
            Param.Int("queueCapacity", "Items that may wait at the outlet. Omit for unlimited.", "count", min: 1, optional: true),
        ],
        Ports = [PortSpec.In<bool>("Enabled", description: "Defaults to true."), PortSpec.Out<int>("Queued", "count")],
        FlowPorts = [PortSpec.Outlet("Out", PayloadKind.Discrete)],
        Faults = Faults,
        Tags = [new TagEntry("Enabled", TagKind.Bool, TagAccess.ReadWrite), new TagEntry("Queued", TagKind.Int64, TagAccess.ReadOnly, "count")],
        Telemetry = [new TelemetryKey("Sourced", "count")],
    };
```

`ItemSink.cs`:

```csharp
    public static ComponentDescriptor Descriptor { get; } = new(
        "item-sink",
        ComponentCategory.Flow,
        "Accepts discrete items and destroys them, up to an optional capacity.",
        (id, p) => new ItemSink(id, p.IntOr("capacity", int.MaxValue)))
    {
        Parameters = [Param.Int("capacity", "How many items it will take. Omit for unlimited.", "count", min: 1, optional: true)],
        Ports = [PortSpec.Out<long>("Count", "count"), PortSpec.Out<bool>("Full")],
        FlowPorts = [PortSpec.Inlet("In", PayloadKind.Discrete)],
        Tags = [new TagEntry("Count", TagKind.Int64, TagAccess.ReadOnly, "count"), new TagEntry("Full", TagKind.Bool, TagAccess.ReadOnly)],
        Telemetry = [new TelemetryKey("Received", "count")],
    };
```

`TransferChute.cs`:

```csharp
    public static ComponentDescriptor Descriptor { get; } = new(
        "transfer-chute",
        ComponentCategory.Flow,
        "A small buffer between two belts; backs material up when it is full or blocked.",
        (id, p) => new TransferChute(id, p.Double("capacityKg")))
    {
        Parameters = [Param.Double("capacityKg", "Mass the chute can hold.", "kg", min: 0.0, exclusiveMin: true)],
        Ports = [PortSpec.Out<double>("Level", "fraction"), PortSpec.Out<bool>("Full")],
        FlowPorts = [PortSpec.Inlet("In", PayloadKind.Bulk), PortSpec.Outlet("Out", PayloadKind.Bulk)],
        Faults = Faults,
        Tags = [new TagEntry("Level", TagKind.Double, TagAccess.ReadOnly, "fraction"), new TagEntry("Full", TagKind.Bool, TagAccess.ReadOnly)],
        Telemetry = [new TelemetryKey("Held", "kg")],
        Provides = [typeof(IMaterialObservable)],
    };
```

`Former.cs`:

```csharp
    public static ComponentDescriptor Descriptor { get; } = new(
        "former",
        ComponentCategory.Flow,
        "Turns bulk material into discrete pieces of a fixed mass, one per cycle. The only place material changes kind.",
        (id, p) => new Former(
            id, p.Material("input"), p.Material("output"), p.Double("pieceMassKg"), p.Double("cycleSeconds"), p.Double("hopperCapacityKg"),
            p.IntOr("outputQueueCapacity", int.MaxValue)))
    {
        Parameters =
        [
            Param.Material("input", "The bulk material consumed.", PayloadKind.Bulk),
            Param.Material("output", "The discrete material produced.", PayloadKind.Discrete),
            Param.Double("pieceMassKg", "Mass of one piece.", "kg", min: 0.0, exclusiveMin: true),
            Param.Double("cycleSeconds", "Time to form one piece.", "s", min: 0.0, exclusiveMin: true),
            Param.Double("hopperCapacityKg", "Bulk material the former can hold.", "kg", min: 0.0, exclusiveMin: true),
            Param.Int("outputQueueCapacity", "Pieces that may wait at the outlet. Omit for unlimited.", "count", min: 1, optional: true),
        ],
        Ports = [PortSpec.Out<long>("PiecesFormed", "count"), PortSpec.Out<double>("HopperLevel", "fraction"), PortSpec.Out<int>("Queued", "count")],
        FlowPorts = [PortSpec.Inlet("In", PayloadKind.Bulk), PortSpec.Outlet("Out", PayloadKind.Discrete)],
        Faults = Faults,
        Tags =
        [
            new TagEntry("PiecesFormed", TagKind.Int64, TagAccess.ReadOnly, "count"),
            new TagEntry("HopperLevel", TagKind.Double, TagAccess.ReadOnly, "fraction"),
            new TagEntry("Queued", TagKind.Int64, TagAccess.ReadOnly, "count"),
        ],
        Telemetry = [new TelemetryKey("Hopper", "kg"), new TelemetryKey("Formed", "count")],
        Provides = [typeof(IMaterialObservable)],
    };
```

`BulkProcessUnit.cs`:

```csharp
    private static readonly GroupDefinition RecipeLineGroup = new(
        "RecipeLine",
        Param.String("inlet", "Name of the inlet this ingredient arrives at; the unit gets one inlet per line."),
        Param.Material("material", "The ingredient.", PayloadKind.Bulk),
        Param.Double("massKg", "Mass of it per batch.", "kg", min: 0.0, exclusiveMin: true));

    public static ComponentDescriptor Descriptor { get; } = new(
        "bulk-process-unit",
        ComponentCategory.Flow,
        "A batch unit for bulk material: fills to a recipe, holds until a condition is met while applying transforms, then discharges.",
        (id, p) => new BulkProcessUnit(
            id,
            p.Groups("recipe").Select(line => new RecipeLine(line.String("inlet"), line.Material("material"), line.Double("massKg"))).ToList(),
            p.Object<IHoldCondition>("hold"),
            p.Material("output"),
            p.Double("yield"),
            p.Objects<IMaterialTransform>("transforms")))
    {
        Parameters =
        [
            Param.GroupList("recipe", "What one batch is made of.", RecipeLineGroup, minCount: 1),
            Param.Object("hold", "When the batch is done.", ObjectSlots.Hold),
            Param.Material("output", "What the batch becomes.", PayloadKind.Bulk),
            Param.Double("yield", "Fraction of the batch mass that comes out.", @default: 1.0, min: 0.0, max: 1.0, exclusiveMin: true),
            Param.ObjectList("transforms", "Applied, in order, every tick while holding.", ObjectSlots.Transform),
        ],
        Ports =
        [
            PortSpec.In<double>("AmbientTemperature", "°C", "Defaults to 20."),
            PortSpec.Out<ProcessPhase>("Phase"),
            PortSpec.Out<double>("BatchMass", "kg"),
            PortSpec.Out<double>("Progress", "fraction"),
        ],
        FlowPorts =
        [
            PortSpec.Inlet("{n}", PayloadKind.Bulk, "One per recipe line, named by the line's inlet.", new PortRepeat("recipe", "inlet")),
            PortSpec.Outlet("Out", PayloadKind.Bulk),
        ],
        Faults = Faults,
        Tags =
        [
            new TagEntry("Phase", TagKind.Int64, TagAccess.ReadOnly),
            new TagEntry("BatchMass", TagKind.Double, TagAccess.ReadOnly, "kg"),
            new TagEntry("Progress", TagKind.Double, TagAccess.ReadOnly, "fraction"),
        ],
        Telemetry = [new TelemetryKey("Batch", "kg"), new TelemetryKey("Lost", "kg"), new TelemetryKey("Cycles", "count")],
        Provides = [typeof(IMaterialObservable)],
    };
```

`ItemProcessUnit.cs`:

```csharp
    public static ComponentDescriptor Descriptor { get; } = new(
        "item-process-unit",
        ComponentCategory.Flow,
        "A batch unit for discrete items — a furnace, a press: takes a batch, holds it while applying transforms, discharges, optionally as a new material.",
        (id, p) => new ItemProcessUnit(
            id,
            p.Int("batchSize"),
            p.Object<IHoldCondition>("hold"),
            p.MaterialOrNull("output"),
            p.Double("yield"),
            p.Objects<IMaterialTransform>("transforms")))
    {
        Parameters =
        [
            Param.Int("batchSize", "Items per batch.", "count", min: 1),
            Param.Object("hold", "When the batch is done.", ObjectSlots.Hold),
            Param.Material("output", "What each item becomes. Omit to keep the material.", PayloadKind.Discrete, optional: true),
            Param.Double("yield", "Fraction of each item's mass that comes out.", @default: 1.0, min: 0.0, max: 1.0, exclusiveMin: true),
            Param.ObjectList("transforms", "Applied, in order, every tick while holding.", ObjectSlots.Transform),
        ],
        Ports =
        [
            PortSpec.In<double>("AmbientTemperature", "°C", "Defaults to 20."),
            PortSpec.Out<ProcessPhase>("Phase"),
            PortSpec.Out<int>("ItemCount", "count"),
            PortSpec.Out<double>("Progress", "fraction"),
        ],
        FlowPorts = [PortSpec.Inlet("In", PayloadKind.Discrete), PortSpec.Outlet("Out", PayloadKind.Discrete)],
        Faults = Faults,
        Tags =
        [
            new TagEntry("Phase", TagKind.Int64, TagAccess.ReadOnly),
            new TagEntry("ItemCount", TagKind.Int64, TagAccess.ReadOnly, "count"),
            new TagEntry("Progress", TagKind.Double, TagAccess.ReadOnly, "fraction"),
        ],
        Telemetry = [new TelemetryKey("Items", "count"), new TelemetryKey("Lost", "kg"), new TelemetryKey("Cycles", "count")],
        Provides = [typeof(IMaterialObservable)],
    };
```

- [ ] **Step 6: Register them**

In `ComponentsModule.Register`, add `using Dse.Components.Flow;` and
`using Dse.Components.Transforms;`, then:

```csharp
        // Flow
        builder.Add(CoreDescriptors.BeltBulk);
        builder.Add(CoreDescriptors.BeltDiscrete);
        builder.Add(BulkSource.Descriptor);
        builder.Add(BulkSink.Descriptor);
        builder.Add(ItemSource.Descriptor);
        builder.Add(ItemSink.Descriptor);
        builder.Add(TransferChute.Descriptor);
        builder.Add(Former.Descriptor);
        builder.Add(BulkProcessUnit.Descriptor);
        builder.Add(ItemProcessUnit.Descriptor);

        // Transforms
        builder.Add(TransformDescriptors.Thermal);
        builder.Add(TransformDescriptors.Moisture);
        builder.Add(TransformDescriptors.Residence);

        // Hold conditions
        foreach (ObjectDescriptor hold in HoldDescriptors.All)
        {
            builder.Add(hold);
        }
```

- [ ] **Step 7: Run conformance and the sweep**

Run: `dotnet test tests/Dse.Components.Tests --nologo --filter FullyQualifiedName~ComponentsCatalogueTests`
Expected: PASS, 2 tests.

Two things to look for if it does not:
- "the instance can supply IMaterialObservable … does not list it under
  Provides" on a type not given `Provides` above — add it and report it.
- a telemetry or tag mismatch — the lists above were read from the source on
  2026-09-20; the source wins.

- [ ] **Step 8: Prove the factories**

Create `tests/Dse.Components.Tests/Catalogue/FlowFactoryTests.cs`:

```csharp
using Dse.Components.Flow;
using Dse.Core.Catalogue;
using Dse.Core.Flow;

namespace Dse.Components.Tests.Catalogue;

public class FlowFactoryTests
{
    private static BindingContext Context() => new BindingContext(ComponentsFixtures.Catalogue)
        .AddMaterial(new MaterialDescriptor(new MaterialType("flour", PayloadKind.Bulk), new MaterialProperties(600.0, 0.12, 18.0), "Flour."))
        .AddMaterial(new MaterialDescriptor(new MaterialType("dough", PayloadKind.Bulk, "proof"), new MaterialProperties(1100.0, 0.4, 25.0), "Dough."))
        .AddMaterial(new MaterialDescriptor(new MaterialType("billet", PayloadKind.Discrete, "soak"), new MaterialProperties(7800.0, 0.0, 20.0), "A billet."));

    [Fact]
    public void ASourceWithNoCapacityIsUnlimitedAndOneWithACapacityIsNot()
    {
        BulkSource open = MechanicalFactoryTests.Build<BulkSource>(BulkSource.Descriptor, """{ "material": "flour", "rateKgPerS": 2 }""", Context());
        BulkSource bounded = MechanicalFactoryTests.Build<BulkSource>(
            BulkSource.Descriptor, """{ "material": "flour", "rateKgPerS": 2, "hopperCapacityKg": 500 }""", Context());

        Assert.Equal(double.PositiveInfinity, open.HopperCapacityKg);
        Assert.Equal(500.0, bounded.HopperCapacityKg);
    }

    [Fact]
    public void AProcessUnitGetsOneInletPerRecipeLineAndANestedHold()
    {
        BulkProcessUnit mixer = MechanicalFactoryTests.Build<BulkProcessUnit>(
            BulkProcessUnit.Descriptor,
            """
            { "recipe": [ { "inlet": "Flour", "material": "flour", "massKg": 50 }, { "inlet": "Water", "material": "flour", "massKg": 30 } ],
              "hold": { "type": "all", "conditions": [ { "type": "for-seconds", "seconds": 600 },
                                                        { "type": "state-at-least", "material": "dough", "state": "proof", "value": 1 } ] },
              "output": "dough", "yield": 0.98,
              "transforms": [ { "type": "thermal-transfer", "timeConstantSeconds": 900 } ] }
            """,
            Context());

        Assert.Contains(mixer.Ports, p => p is FlowInlet && p.Name == "Flour");
        Assert.Contains(mixer.Ports, p => p is FlowInlet && p.Name == "Water");
        Assert.Equal(3, mixer.Ports.Count(p => p is FlowPort));
    }

    [Fact]
    public void AnItemUnitMayKeepItsMaterial()
    {
        ItemProcessUnit furnace = MechanicalFactoryTests.Build<ItemProcessUnit>(
            ItemProcessUnit.Descriptor,
            """
            { "batchSize": 6, "hold": { "type": "state-at-least", "material": "billet", "state": "soak", "value": 1200 },
              "transforms": [ { "type": "residence-accumulator", "material": "billet", "state": "soak", "thresholdTemperature": 700 } ] }
            """,
            Context());

        Assert.Equal("X", furnace.Id);
    }

    [Fact]
    public void ABulkMaterialCannotFeedAnItemSource()
    {
        using var document = System.Text.Json.JsonDocument.Parse("""{ "material": "flour", "itemMassKg": 1, "intervalSeconds": 1 }""");
        var issues = new List<BindingIssue>();

        ParameterBinder.Bind(ItemSource.Descriptor.Parameters, document.RootElement, "$", Context(), true, issues);

        BindingIssue issue = Assert.Single(issues);
        Assert.Equal("$.material", issue.Path);
        Assert.Contains("Discrete", issue.Message, StringComparison.Ordinal);
    }
}
```

Run: `dotnet test tests/Dse.Components.Tests --nologo --filter FullyQualifiedName~FlowFactoryTests`
Expected: PASS, 4 tests.

- [ ] **Step 9: Build Release, run everything, commit**

Run: `dotnet build Dse.sln -c Release --nologo` — expect `0 Warning(s)`, `0 Error(s)`.
Run: `dotnet test Dse.sln --nologo` — expect all green.

```bash
git add src/Dse.Core/Catalogue/CoreDescriptors.cs src/Dse.Components tests/Dse.Components.Tests/Catalogue
git commit -m "feat(catalogue): describe the flow nodes, transforms and hold conditions"
```

---
### Task 8: Descriptors — the conveyor, and the end of `Pending`

The one shipped composite. Its descriptor describes the *exposed* surface —
aliases, not leaves — plus the tags and telemetry of the leaves inside, named
the way `SimulationBuilder` names them. When it is green, `Pending` is deleted
and the sweep becomes unconditional.

The rules at the top of Task 5 apply.

**Files:**
- Modify: `src/Dse.Components/Conveyors/Conveyor.cs`
- Modify: `src/Dse.Components/ComponentsModule.cs`
- Modify: `tests/Dse.Components.Tests/Catalogue/ComponentsFixtures.cs`, `ComponentsCatalogueTests.cs`
- Test: `tests/Dse.Components.Tests/Catalogue/ConveyorFactoryTests.cs`

**Interfaces:**
- Consumes: `MotorRatingGroup` (Task 5); `ICapabilityProvider` on `Conveyor` (Task 1).
- Produces: `Conveyor.Descriptor`, type name `conveyor`. After this task `ComponentsModule` registers 29 components, 3 transforms and 5 holds, and no materials (R35).

- [ ] **Step 1: Delete `Pending` and add the fixture**

In `ComponentsCatalogueTests.cs` delete the `Pending` array with its doc
comment, delete the `stale` line and its assertion, and make the `missing`
query unconditional:

```csharp
        List<string> missing = concrete
            .Where(t => !built.Contains(Definition(t)))
            .Select(t => t.FullName!)
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Empty(missing);
```

Remove any `using` the file no longer needs.

In `ComponentsFixtures.cs` append to the chain in `Create()`:

```csharp
        // Task 8 — conveyor
        .Parameters("conveyor", """
            { "lengthM": 10, "cellSizeM": 0.5, "beltWidthM": 0.8, "angleOfReposeDeg": 20, "materialDensityKgM3": 2000,
              "emptyBeltMassKg": 250, "frictionCoefficient": 0.04, "pulleyDiameterM": 0.5, "gearRatio": 20,
              "motor": { "ratedPowerW": 750, "ratedSpeedRadPerS": 150, "ratedCurrentA": 2 }, "pullKeys": 3 }
            """)
```

- [ ] **Step 2: Run and see the sweep fail**

Run: `dotnet test tests/Dse.Components.Tests --nologo --filter FullyQualifiedName~ComponentsCatalogueTests`
Expected: the sweep FAILS listing `Dse.Components.Conveyors.Conveyor` only.

- [ ] **Step 3: Write the descriptor**

In `Conveyor.cs` add `using Dse.Core.Catalogue;`, `using Dse.Core.Flow;`,
`using Dse.Io;` as needed, and place first in the class:

```csharp
    private static readonly PortRepeat PerPullKey = new("pullKeys");

    public static ComponentDescriptor Descriptor { get; } = new(
        "conveyor",
        ComponentCategory.Conveyor,
        "A complete belt conveyor: motor, gearbox, drive and tail pulleys, belt, speed sensor, belt scale, current sensor, " +
        "zero-speed switch, e-stop, pull-keys, safety relay and starter, wired and ready. Faults are injected on the leaves " +
        "inside it, addressed as <id>.Motor, <id>.Belt, <id>.Scale and so on.",
        (id, p) => new Conveyor(id, new ConveyorOptions(
            LengthM: p.Double("lengthM"),
            CellSizeM: p.Double("cellSizeM"),
            BeltWidthM: p.Double("beltWidthM"),
            AngleOfReposeDeg: p.Double("angleOfReposeDeg"),
            MaterialDensityKgM3: p.Double("materialDensityKgM3"),
            EmptyBeltMassKg: p.Double("emptyBeltMassKg"),
            FrictionCoefficient: p.Double("frictionCoefficient"),
            PulleyDiameterM: p.Double("pulleyDiameterM"),
            GearRatio: p.Double("gearRatio"),
            Motor: MotorRatingGroup.Read(p.Group("motor")),
            TailDragN: p.Double("tailDragN"),
            PullKeys: p.Int("pullKeys"),
            SpeedMarginFraction: p.Double("speedMarginFraction"))))
    {
        Parameters =
        [
            Param.Double("lengthM", "Belt length; must be a whole number of cells.", "m", min: 0.0, exclusiveMin: true),
            Param.Double("cellSizeM", "Length of one belt cell. The belt may not advance more than one cell per tick (DSE006).", "m", min: 0.0, exclusiveMin: true),
            Param.Double("beltWidthM", "Belt width; with the angle of repose and density it sets how much a metre of belt can carry.", "m", min: 0.0, exclusiveMin: true),
            Param.Double("angleOfReposeDeg", "Surcharge angle of the material on the belt.", "°", min: 0.0),
            Param.Double("materialDensityKgM3", "Bulk density used to size the belt's capacity.", "kg/m³", min: 0.0, exclusiveMin: true),
            Param.Double("emptyBeltMassKg", "Mass of the moving belt and idlers with no load.", "kg", min: 0.0),
            Param.Double("frictionCoefficient", "Rolling resistance coefficient.", min: 0.0),
            Param.Double("pulleyDiameterM", "Drive pulley diameter.", "m", min: 0.0, exclusiveMin: true),
            Param.Double("gearRatio", "Motor speed over pulley speed.", min: 0.0, exclusiveMin: true),
            Param.Group("motor", "The drive motor's rating.", MotorRatingGroup.Definition),
            Param.Double("tailDragN", "Tail pulley bearing drag.", "N", @default: 50.0, min: 0.0),
            Param.Int("pullKeys", "Number of pull-wire switches along the belt.", "count", @default: 2, min: 0),
            Param.Double("speedMarginFraction", "How far the belt's declared maximum speed exceeds the no-load speed.", @default: 0.1, min: 0.0),
        ],
        Ports =
        [
            PortSpec.In<bool>("Start", description: "Run command to the starter."),
            PortSpec.In<bool>("Reset", description: "Overload reset, rising edge."),
            PortSpec.In<bool>("SafetyReset", description: "Safety relay reset, rising edge."),
            PortSpec.In<bool>("EStop", description: "The e-stop is pressed."),
            PortSpec.In<bool>("PullKey{n}", description: "A pull-key is pulled.", repeat: PerPullKey),
            PortSpec.Out<double>("Speed", "m/s", "Measured belt speed."),
            PortSpec.Out<double>("TonnesPerHour", "t/h", "Measured mass flow at the scale."),
            PortSpec.Out<double>("Current", "A", "Measured motor current."),
            PortSpec.Out<bool>("Stopped", description: "Zero-speed switch."),
            PortSpec.Out<bool>("Contactor"),
            PortSpec.Out<bool>("Tripped"),
            PortSpec.Out<bool>("SafetyOk"),
        ],
        FlowPorts = [PortSpec.Inlet("In", PayloadKind.Bulk, "The tail."), PortSpec.Outlet("Out", PayloadKind.Bulk, "The head.")],
        Tags =
        [
            new TagEntry("Start", TagKind.Bool, TagAccess.ReadWrite),
            new TagEntry("Reset", TagKind.Bool, TagAccess.ReadWrite),
            new TagEntry("SafetyReset", TagKind.Bool, TagAccess.ReadWrite),
            new TagEntry("EStop", TagKind.Bool, TagAccess.ReadWrite),
            new TagEntry("PullKey{n}", TagKind.Bool, TagAccess.ReadWrite, Repeat: PerPullKey),
            new TagEntry("Speed", TagKind.Double, TagAccess.ReadOnly, "m/s"),
            new TagEntry("TonnesPerHour", TagKind.Double, TagAccess.ReadOnly, "t/h"),
            new TagEntry("Current", TagKind.Double, TagAccess.ReadOnly, "A"),
            new TagEntry("Stopped", TagKind.Bool, TagAccess.ReadOnly),
            new TagEntry("Contactor", TagKind.Bool, TagAccess.ReadOnly),
            new TagEntry("Tripped", TagKind.Bool, TagAccess.ReadOnly),
            new TagEntry("SafetyOk", TagKind.Bool, TagAccess.ReadOnly),
            new TagEntry("ZeroSpeed.Value", TagKind.Double, TagAccess.ReadOnly, "m/s"),
            new TagEntry("EStop.Ok", TagKind.Bool, TagAccess.ReadOnly),
            new TagEntry("PullKey{n}.Ok", TagKind.Bool, TagAccess.ReadOnly, Repeat: PerPullKey),
        ],
        Telemetry =
        [
            new TelemetryKey("Motor.Speed", "rad/s"),
            new TelemetryKey("Motor.Current", "A"),
            new TelemetryKey("Motor.ThermalState"),
            new TelemetryKey("Belt.Load", "kg"),
            new TelemetryKey("SpeedSensor.Truth"),
            new TelemetryKey("Scale.Truth"),
            new TelemetryKey("CurrentSensor.Truth"),
            new TelemetryKey("ZeroSpeed.Truth"),
        ],
        Provides = [typeof(IMaterialObservable)],
    };
```

`PerPullKey` must be declared above `Descriptor`. Inside `Conveyor`, `Motor`,
`Belt`, `EStop` and the rest are instance properties; the descriptor above uses
none of them as a bare name (`Motor:` is a named argument, which is fine).

- [ ] **Step 4: Register it**

In `ComponentsModule.Register`, add `using Dse.Components.Conveyors;` and:

```csharp
        // Conveyor
        builder.Add(Conveyor.Descriptor);
```

- [ ] **Step 5: Run conformance and the sweep**

Run: `dotnet test tests/Dse.Components.Tests --nologo --filter FullyQualifiedName~ComponentsCatalogueTests`
Expected: PASS, 2 tests.

This is the descriptor most likely to disagree with its instance, because the
tag and telemetry lists above were derived by reading `Conveyor`'s wiring, not
by running it. Every disagreement is a finding for the report; the instance is
right.

- [ ] **Step 6: Prove the JSON conveyor is the code conveyor**

Create `tests/Dse.Components.Tests/Catalogue/ConveyorFactoryTests.cs`:

```csharp
using Dse.Components.Conveyors;
using Dse.Components.Mechanical;

namespace Dse.Components.Tests.Catalogue;

public class ConveyorFactoryTests
{
    [Fact]
    public void TheFactoryBuildsWhatTheConstructorBuilds()
    {
        var byHand = new Conveyor("X", new ConveyorOptions(
            LengthM: 10.0, CellSizeM: 0.5, BeltWidthM: 0.8, AngleOfReposeDeg: 20.0, MaterialDensityKgM3: 2000.0,
            EmptyBeltMassKg: 250.0, FrictionCoefficient: 0.04, PulleyDiameterM: 0.5, GearRatio: 20.0,
            Motor: new MotorRating(750.0, 150.0, 2.0), TailDragN: 80.0, PullKeys: 3));

        Conveyor fromJson = MechanicalFactoryTests.Build<Conveyor>(Conveyor.Descriptor, """
            { "lengthM": 10, "cellSizeM": 0.5, "beltWidthM": 0.8, "angleOfReposeDeg": 20, "materialDensityKgM3": 2000,
              "emptyBeltMassKg": 250, "frictionCoefficient": 0.04, "pulleyDiameterM": 0.5, "gearRatio": 20,
              "motor": { "ratedPowerW": 750, "ratedSpeedRadPerS": 150, "ratedCurrentA": 2 }, "tailDragN": 80, "pullKeys": 3 }
            """);

        Assert.Equal(byHand.LeafComponents.Select(l => l.Id), fromJson.LeafComponents.Select(l => l.Id));
        Assert.Equal(byHand.ExposedPorts.Select(e => e.Key), fromJson.ExposedPorts.Select(e => e.Key));
        Assert.Equal(byHand.Belt.MaxSpeed, fromJson.Belt.MaxSpeed);
        Assert.Equal(byHand.Belt.MaxLinearDensity, fromJson.Belt.MaxLinearDensity);
        Assert.Equal(byHand.Motor.Rating, fromJson.Motor.Rating);
    }

    [Fact]
    public void TheDefaultsAreTheOptionsRecordsDefaults()
    {
        Conveyor fromJson = MechanicalFactoryTests.Build<Conveyor>(Conveyor.Descriptor, """
            { "lengthM": 10, "cellSizeM": 0.5, "beltWidthM": 0.8, "angleOfReposeDeg": 20, "materialDensityKgM3": 2000,
              "emptyBeltMassKg": 250, "frictionCoefficient": 0.04, "pulleyDiameterM": 0.5, "gearRatio": 20,
              "motor": { "ratedPowerW": 750, "ratedSpeedRadPerS": 150, "ratedCurrentA": 2 } }
            """);
        var byHand = new Conveyor("X", new ConveyorOptions(
            10.0, 0.5, 0.8, 20.0, 2000.0, 250.0, 0.04, 0.5, 20.0, new MotorRating(750.0, 150.0, 2.0)));

        Assert.Equal(byHand.LeafComponents.Count, fromJson.LeafComponents.Count);
        Assert.Equal(byHand.Belt.MaxSpeed, fromJson.Belt.MaxSpeed);
        Assert.Equal(byHand.Tail.BearingDragN, fromJson.Tail.BearingDragN);
    }
}
```

Run: `dotnet test tests/Dse.Components.Tests --nologo --filter FullyQualifiedName~ConveyorFactoryTests`
Expected: PASS, 2 tests.

- [ ] **Step 7: Build Release, run everything, commit**

Run: `dotnet build Dse.sln -c Release --nologo` — expect `0 Warning(s)`, `0 Error(s)`.
Run: `dotnet test Dse.sln --nologo` — expect all green.

```bash
git add src/Dse.Components tests/Dse.Components.Tests/Catalogue
git commit -m "feat(catalogue): describe the conveyor and require a descriptor for every node"
```

---

### Task 9: Catalogue export and the golden-file helper

**Files:**
- Create: `src/Dse.Core/Catalogue/CatalogueJson.cs`
- Create: `tests/Shared/Golden.cs` (linked into test projects, not a project)
- Modify: `tests/Dse.Components.Tests/Dse.Components.Tests.csproj` (link `Golden.cs`)
- Test: `tests/Dse.Core.Tests/Catalogue/CatalogueJsonTests.cs`
- Test: `tests/Dse.Components.Tests/Catalogue/ComponentsExportTests.cs`
- Create (generated, then read, then committed): `tests/Dse.Components.Tests/Catalogue/Golden/components-catalogue.json`

**Interfaces:**
- Consumes: Task 2's model.
- Produces:
  - `public static class CatalogueJson { public const int FormatVersion = 1; public static string Export(ComponentCatalogue catalogue); public static JsonWriterOptions WriterOptions { get; } public static string Finish(MemoryStream stream); public static string Camel(string pascal); }` — `WriterOptions` and `Finish` are public because Task 13's schema generator, in another assembly, must emit JSON the same way
  - `internal static class Golden { public static void Assert(string relativePath, string actual, [CallerFilePath] string callerFile = ""); }` (namespace `Dse.Tests.Shared`)
  - `JsonText.Normalise(string)` is **not** introduced; `Export` returns text already normalised to `\n` with one trailing `\n`.

**Export format** (property order is exactly as listed; absent optional values are omitted, never `null`):

```
{ "formatVersion": 1,
  "modules": [ "Dse.Components" ],
  "components": [ { "type", "module", "category", "description",
                    "parameters": [ <parameter> ], "ports": [ <port> ], "flowPorts": [ <flowPort> ],
                    "faults": [ { "id", "description", "parameters": [ { "name", "unit", "default", "description" } ] } ],
                    "tags": [ { "name", "kind", "access", "unit"?, "repeat"? } ],
                    "telemetry": [ { "name", "unit"? } ],
                    "provides": [ "IMaterialObservable" ] } ],
  "objects":    [ { "slot", "type", "module", "description", "parameters": [ <parameter> ] } ],
  "materials":  [ { "name", "module", "kind", "states": [], "properties": { "density", "moisture", "temperature" }, "description" } ] }

<parameter> = { "name", "kind", "required", "description", "unit"?, "default"?, "optional"?,
                "minimum"?, "exclusiveMinimum"?, "maximum"?, "exclusiveMaximum"?, "allowedValues"?,
                "group"?, "parameters"? (children), "capability"?, "payload"?, "materialParameter"?, "slot"?, "minCount"? }
<port>      = { "name", "direction", "valueType", "unit"?, "required"?, "description"?, "repeat"? }
<flowPort>  = { "name", "direction", "payload", "description"?, "repeat"? }
"repeat"    = { "parameter", "nameChild"? }
```

Enum values are camelCase (`groupList`, `readWrite`, `in`, `bulk`). `"required"`,
`"optional"`, `"exclusiveMinimum"` and `"exclusiveMaximum"` are written only
when true, except `"required"` on a parameter, which is always written.

- [ ] **Step 1: Write the golden helper and link it**

`tests/Shared/Golden.cs`:

```csharp
using System.Runtime.CompilerServices;

namespace Dse.Tests.Shared;

/// <summary>
/// Compares text with a committed file beside the calling test. Set the
/// environment variable DSE_UPDATE_GOLDEN=1 to write the file instead (R42);
/// read what it wrote before committing it.
/// </summary>
internal static class Golden
{
    public static void Assert(string relativePath, string actual, [CallerFilePath] string callerFile = "")
    {
        string path = Path.Combine(Path.GetDirectoryName(callerFile)!, relativePath);
        string normalised = actual.ReplaceLineEndings("\n");

        if (Environment.GetEnvironmentVariable("DSE_UPDATE_GOLDEN") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, normalised);
            return;
        }

        Xunit.Assert.True(File.Exists(path), $"Golden file '{path}' does not exist. Run the test once with DSE_UPDATE_GOLDEN=1, read the file, commit it.");
        string expected = File.ReadAllText(path).ReplaceLineEndings("\n");
        if (!string.Equals(expected, normalised, StringComparison.Ordinal))
        {
            File.WriteAllText(path + ".actual", normalised);
            Xunit.Assert.Fail($"Output differs from golden file '{path}'. The new output is beside it as '.actual'; diff the two.");
        }
    }
}
```

In `Dse.Components.Tests.csproj`, add:

```xml
  <ItemGroup>
    <Compile Include="..\Shared\Golden.cs" Link="Shared\Golden.cs" />
  </ItemGroup>
```

Add `*.actual` to `.gitignore` at the repository root (create the line; keep
what is there).

- [ ] **Step 2: Write the failing export tests**

`tests/Dse.Core.Tests/Catalogue/CatalogueJsonTests.cs`:

```csharp
using System.Text.Json;
using Dse.Core.Catalogue;
using Dse.Core.Faults;
using Dse.Core.Flow;
using Dse.Core.Graph;
using Dse.Io;

namespace Dse.Core.Tests.Catalogue;

public class CatalogueJsonTests
{
    private sealed class Module : ICatalogueModule
    {
        public string Name => "Test";

        public void Register(CatalogueBuilder builder)
        {
            var rating = new GroupDefinition("Rating", Param.Double("powerW", "Power.", "W", min: 0.0, exclusiveMin: true));
            builder.Add(new ComponentDescriptor("zeta", ComponentCategory.Signal, "Last.", (id, p) => new UnitDelay<bool>(id)));
            builder.Add(new ComponentDescriptor("alpha", ComponentCategory.Flow, "First.", (id, p) => new UnitDelay<bool>(id))
            {
                Parameters =
                [
                    Param.Group("rating", "Rating.", rating),
                    Param.Int("channels", "Channels.", "count", @default: 2, min: 1),
                    Param.Reference<IMaterialObservable>("belt", "Belt."),
                    Param.Double("capacityKg", "Omit for unlimited.", "kg", optional: true),
                ],
                Ports = [PortSpec.In<bool>("Channel{n}", required: true, repeat: new PortRepeat("channels")), PortSpec.Out<double>("Level", "fraction")],
                FlowPorts = [PortSpec.Inlet("In", PayloadKind.Bulk)],
                Faults = [new FaultDescriptor("jam", "Stops.", new FaultParameter("seconds", "s", 1.5, "How long."))],
                Tags = [new TagEntry("Level", TagKind.Double, TagAccess.ReadOnly, "fraction")],
                Telemetry = [new TelemetryKey("Held", "kg")],
                Provides = [typeof(IMaterialObservable)],
            });
            builder.Add(new ObjectDescriptor(ObjectSlots.Hold, "for-seconds", "Waits.", p => new object())
            {
                Parameters = [Param.Double("seconds", "How long.", "s", min: 0.0)],
            });
            builder.Add(new MaterialDescriptor(new MaterialType("ore", PayloadKind.Bulk, "wet"), new MaterialProperties(2000.0, 0.03, 15.0), "Ore."));
        }
    }

    private static readonly string Json = CatalogueJson.Export(new CatalogueBuilder().Add<Module>().Build());

    [Fact]
    public void IsValidJsonWithAVersionAndSortedComponents()
    {
        using JsonDocument document = JsonDocument.Parse(Json);
        JsonElement root = document.RootElement;

        Assert.Equal(1, root.GetProperty("formatVersion").GetInt32());
        Assert.Equal("Test", root.GetProperty("modules")[0].GetString());
        Assert.Equal(["alpha", "zeta"], root.GetProperty("components").EnumerateArray().Select(c => c.GetProperty("type").GetString()));
    }

    [Fact]
    public void WritesEveryAspectOfAComponent()
    {
        using JsonDocument document = JsonDocument.Parse(Json);
        JsonElement alpha = document.RootElement.GetProperty("components")[0];

        Assert.Equal("flow", alpha.GetProperty("category").GetString());
        Assert.Equal("Test", alpha.GetProperty("module").GetString());

        JsonElement rating = alpha.GetProperty("parameters")[0];
        Assert.Equal("group", rating.GetProperty("kind").GetString());
        Assert.Equal("Rating", rating.GetProperty("group").GetString());
        Assert.True(rating.GetProperty("required").GetBoolean());
        JsonElement power = rating.GetProperty("parameters")[0];
        Assert.Equal(0.0, power.GetProperty("minimum").GetDouble());
        Assert.True(power.GetProperty("exclusiveMinimum").GetBoolean());
        Assert.False(power.TryGetProperty("maximum", out _));

        JsonElement channels = alpha.GetProperty("parameters")[1];
        Assert.Equal(2, channels.GetProperty("default").GetInt32());
        Assert.False(channels.GetProperty("required").GetBoolean());

        Assert.Equal("IMaterialObservable", alpha.GetProperty("parameters")[2].GetProperty("capability").GetString());
        Assert.True(alpha.GetProperty("parameters")[3].GetProperty("optional").GetBoolean());

        JsonElement port = alpha.GetProperty("ports")[0];
        Assert.Equal("in", port.GetProperty("direction").GetString());
        Assert.Equal("bool", port.GetProperty("valueType").GetString());
        Assert.True(port.GetProperty("required").GetBoolean());
        Assert.Equal("channels", port.GetProperty("repeat").GetProperty("parameter").GetString());

        Assert.Equal("bulk", alpha.GetProperty("flowPorts")[0].GetProperty("payload").GetString());
        Assert.Equal(1.5, alpha.GetProperty("faults")[0].GetProperty("parameters")[0].GetProperty("default").GetDouble());
        Assert.Equal("readOnly", alpha.GetProperty("tags")[0].GetProperty("access").GetString());
        Assert.Equal("kg", alpha.GetProperty("telemetry")[0].GetProperty("unit").GetString());
        Assert.Equal("IMaterialObservable", alpha.GetProperty("provides")[0].GetString());
    }

    [Fact]
    public void WritesObjectsAndMaterials()
    {
        using JsonDocument document = JsonDocument.Parse(Json);
        JsonElement hold = document.RootElement.GetProperty("objects")[0];
        JsonElement ore = document.RootElement.GetProperty("materials")[0];

        Assert.Equal("hold", hold.GetProperty("slot").GetString());
        Assert.Equal("for-seconds", hold.GetProperty("type").GetString());
        Assert.Equal("bulk", ore.GetProperty("kind").GetString());
        Assert.Equal("wet", ore.GetProperty("states")[0].GetString());
        Assert.Equal(0.03, ore.GetProperty("properties").GetProperty("moisture").GetDouble());
    }

    [Fact]
    public void IsByteIdenticalAcrossRunsAndUsesUnixLineEndings()
    {
        string again = CatalogueJson.Export(new CatalogueBuilder().Add<Module>().Build());

        Assert.Equal(Json, again);
        Assert.DoesNotContain('\r', Json);
        Assert.EndsWith("}\n", Json, StringComparison.Ordinal);
        Assert.DoesNotContain("\\u", Json, StringComparison.Ordinal);
    }
}
```

Run: `dotnet test tests/Dse.Core.Tests --nologo --filter FullyQualifiedName~CatalogueJsonTests`
Expected: build FAILS — `CatalogueJson` does not exist.

- [ ] **Step 3: Write `CatalogueJson`**

`src/Dse.Core/Catalogue/CatalogueJson.cs`:

```csharp
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Dse.Core.Faults;

namespace Dse.Core.Catalogue;

/// <summary>The catalogue as deterministic JSON: same catalogue, same bytes.</summary>
public static class CatalogueJson
{
    /// <summary>Bumped when a consumer of the export would have to change.</summary>
    public const int FormatVersion = 1;

    /// <summary>Indented, with readable non-ASCII. Every JSON document this project emits uses these.</summary>
    public static JsonWriterOptions WriterOptions { get; } = new()
    {
        Indented = true,
        // Units such as "°C" and "N·m" stay readable instead of becoming \u escapes.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string Export(ComponentCatalogue catalogue)
    {
        ArgumentNullException.ThrowIfNull(catalogue);

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, WriterOptions))
        {
            writer.WriteStartObject();
            writer.WriteNumber("formatVersion", FormatVersion);

            writer.WriteStartArray("modules");
            foreach (string module in catalogue.Modules)
            {
                writer.WriteStringValue(module);
            }

            writer.WriteEndArray();

            writer.WriteStartArray("components");
            foreach (ComponentDescriptor component in catalogue.Components)
            {
                WriteComponent(writer, component, catalogue.ModuleOf(component));
            }

            writer.WriteEndArray();

            writer.WriteStartArray("objects");
            foreach (ObjectDescriptor descriptor in catalogue.Objects)
            {
                writer.WriteStartObject();
                writer.WriteString("slot", descriptor.Slot);
                writer.WriteString("type", descriptor.Type);
                writer.WriteString("module", catalogue.ModuleOf(descriptor));
                writer.WriteString("description", descriptor.Description);
                writer.WritePropertyName("parameters");
                WriteParameters(writer, descriptor.Parameters);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();

            writer.WriteStartArray("materials");
            foreach (MaterialDescriptor material in catalogue.Materials)
            {
                writer.WriteStartObject();
                writer.WriteString("name", material.Material.Name);
                writer.WriteString("module", catalogue.ModuleOf(material));
                writer.WriteString("kind", Camel(material.Material.Kind.ToString()));
                writer.WriteStartArray("states");
                foreach (string state in material.Material.StateSchema)
                {
                    writer.WriteStringValue(state);
                }

                writer.WriteEndArray();
                writer.WriteStartObject("properties");
                writer.WriteNumber("density", material.Properties.Density);
                writer.WriteNumber("moisture", material.Properties.Moisture);
                writer.WriteNumber("temperature", material.Properties.Temperature);
                writer.WriteEndObject();
                writer.WriteString("description", material.Description);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Finish(stream);
    }

    /// <summary>UTF-8 to text, <c>\n</c> line endings, exactly one trailing newline. Shared with the schema generator.</summary>
    public static string Finish(MemoryStream stream) =>
        Encoding.UTF8.GetString(stream.ToArray()).ReplaceLineEndings("\n").TrimEnd('\n') + "\n";

    /// <summary><c>GroupList</c> → <c>groupList</c>.</summary>
    public static string Camel(string pascal)
    {
        ArgumentException.ThrowIfNullOrEmpty(pascal);
        return char.ToLowerInvariant(pascal[0]) + pascal[1..];
    }

    internal static void WriteParameters(Utf8JsonWriter writer, IReadOnlyList<ParameterDescriptor> parameters)
    {
        writer.WriteStartArray();
        foreach (ParameterDescriptor parameter in parameters)
        {
            writer.WriteStartObject();
            writer.WriteString("name", parameter.Name);
            writer.WriteString("kind", Camel(parameter.Kind.ToString()));
            writer.WriteBoolean("required", parameter.IsRequired);
            writer.WriteString("description", parameter.Description);
            if (parameter.Unit.Length > 0)
            {
                writer.WriteString("unit", parameter.Unit);
            }

            switch (parameter.Default)
            {
                case double number:
                    writer.WriteNumber("default", number);
                    break;
                case long whole:
                    writer.WriteNumber("default", whole);
                    break;
                case bool flag:
                    writer.WriteBoolean("default", flag);
                    break;
                case string text:
                    writer.WriteString("default", text);
                    break;
            }

            if (parameter.IsOptional)
            {
                writer.WriteBoolean("optional", true);
            }

            if (parameter.Minimum is { } minimum)
            {
                writer.WriteNumber("minimum", minimum);
            }

            if (parameter.ExclusiveMinimum)
            {
                writer.WriteBoolean("exclusiveMinimum", true);
            }

            if (parameter.Maximum is { } maximum)
            {
                writer.WriteNumber("maximum", maximum);
            }

            if (parameter.ExclusiveMaximum)
            {
                writer.WriteBoolean("exclusiveMaximum", true);
            }

            if (parameter.AllowedValues.Count > 0)
            {
                writer.WriteStartArray("allowedValues");
                foreach (string value in parameter.AllowedValues)
                {
                    writer.WriteStringValue(value);
                }

                writer.WriteEndArray();
            }

            if (parameter.GroupName.Length > 0)
            {
                writer.WriteString("group", parameter.GroupName);
                writer.WritePropertyName("parameters");
                WriteParameters(writer, parameter.Children);
            }

            if (parameter.Capability is not null)
            {
                writer.WriteString("capability", parameter.Capability.Name);
            }

            if (parameter.Payload is { } payload)
            {
                writer.WriteString("payload", Camel(payload.ToString()));
            }

            if (parameter.MaterialParameter.Length > 0)
            {
                writer.WriteString("materialParameter", parameter.MaterialParameter);
            }

            if (parameter.Slot.Length > 0)
            {
                writer.WriteString("slot", parameter.Slot);
            }

            if (parameter.MinCount > 0)
            {
                writer.WriteNumber("minCount", parameter.MinCount);
            }

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteComponent(Utf8JsonWriter writer, ComponentDescriptor component, string module)
    {
        writer.WriteStartObject();
        writer.WriteString("type", component.Type);
        writer.WriteString("module", module);
        writer.WriteString("category", Camel(component.Category.ToString()));
        writer.WriteString("description", component.Description);
        writer.WritePropertyName("parameters");
        WriteParameters(writer, component.Parameters);

        writer.WriteStartArray("ports");
        foreach (PortDescriptor port in component.Ports)
        {
            writer.WriteStartObject();
            writer.WriteString("name", port.Name);
            writer.WriteString("direction", Camel(port.Direction.ToString()));
            writer.WriteString("valueType", port.ValueType);
            WriteOptional(writer, "unit", port.Unit);
            if (port.Required)
            {
                writer.WriteBoolean("required", true);
            }

            WriteOptional(writer, "description", port.Description);
            WriteRepeat(writer, port.Repeat);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();

        writer.WriteStartArray("flowPorts");
        foreach (FlowPortDescriptor port in component.FlowPorts)
        {
            writer.WriteStartObject();
            writer.WriteString("name", port.Name);
            writer.WriteString("direction", Camel(port.Direction.ToString()));
            writer.WriteString("payload", Camel(port.Payload.ToString()));
            WriteOptional(writer, "description", port.Description);
            WriteRepeat(writer, port.Repeat);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();

        writer.WriteStartArray("faults");
        foreach (FaultDescriptor fault in component.Faults)
        {
            writer.WriteStartObject();
            writer.WriteString("id", fault.Id);
            writer.WriteString("description", fault.Description);
            writer.WriteStartArray("parameters");
            foreach (FaultParameter parameter in fault.Parameters)
            {
                writer.WriteStartObject();
                writer.WriteString("name", parameter.Name);
                writer.WriteString("unit", parameter.Unit);
                writer.WriteNumber("default", parameter.DefaultValue);
                writer.WriteString("description", parameter.Description);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        writer.WriteEndArray();

        writer.WriteStartArray("tags");
        foreach (TagEntry tag in component.Tags)
        {
            writer.WriteStartObject();
            writer.WriteString("name", tag.Name);
            writer.WriteString("kind", Camel(tag.Kind.ToString()));
            writer.WriteString("access", Camel(tag.Access.ToString()));
            WriteOptional(writer, "unit", tag.Unit);
            WriteRepeat(writer, tag.Repeat);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();

        writer.WriteStartArray("telemetry");
        foreach (TelemetryKey key in component.Telemetry)
        {
            writer.WriteStartObject();
            writer.WriteString("name", key.Name);
            WriteOptional(writer, "unit", key.Unit);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();

        writer.WriteStartArray("provides");
        foreach (string capability in component.Provides.Select(t => t.Name).Order(StringComparer.Ordinal))
        {
            writer.WriteStringValue(capability);
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteOptional(Utf8JsonWriter writer, string name, string value)
    {
        if (value.Length > 0)
        {
            writer.WriteString(name, value);
        }
    }

    private static void WriteRepeat(Utf8JsonWriter writer, PortRepeat? repeat)
    {
        if (repeat is null)
        {
            return;
        }

        writer.WriteStartObject("repeat");
        writer.WriteString("parameter", repeat.Parameter);
        WriteOptional(writer, "nameChild", repeat.NameChild);
        writer.WriteEndObject();
    }
}
```

Run: `dotnet test tests/Dse.Core.Tests --nologo --filter FullyQualifiedName~CatalogueJsonTests`
Expected: PASS, 4 tests.

- [ ] **Step 4: Pin the shipped catalogue with a golden file**

`tests/Dse.Components.Tests/Catalogue/ComponentsExportTests.cs`:

```csharp
using System.Text.Json;
using Dse.Core.Catalogue;
using Dse.Tests.Shared;

namespace Dse.Components.Tests.Catalogue;

public class ComponentsExportTests
{
    [Fact]
    public void TheShippedCatalogueExportsExactlyTheGoldenFile()
    {
        Golden.Assert("Golden/components-catalogue.json", CatalogueJson.Export(ComponentsFixtures.Catalogue));
    }

    [Fact]
    public void TheShippedCatalogueHasTheExpectedCounts()
    {
        using JsonDocument document = JsonDocument.Parse(CatalogueJson.Export(ComponentsFixtures.Catalogue));

        Assert.Equal(29, document.RootElement.GetProperty("components").GetArrayLength());
        Assert.Equal(8, document.RootElement.GetProperty("objects").GetArrayLength());
        Assert.Equal(0, document.RootElement.GetProperty("materials").GetArrayLength());
    }
}
```

The 29: 2 unit delays, 6 mechanical, 7 instruments, 3 safety, 10 flow, 1
conveyor. If the count differs, list the registered type names in the report.

Generate the golden file, then **read it**:

```bash
DSE_UPDATE_GOLDEN=1 dotnet test tests/Dse.Components.Tests --nologo --filter FullyQualifiedName~ComponentsExportTests
```

Open `tests/Dse.Components.Tests/Catalogue/Golden/components-catalogue.json`
and check, at least: `conveyor` has 13 parameters and its `motor` group has 10
children; `safety-relay`'s `Channel{n}` port carries a `repeat`; units such as
`°C` and `N·m` are readable, not `°`; the file ends with a single newline.

Run without the variable:
`dotnet test tests/Dse.Components.Tests --nologo --filter FullyQualifiedName~ComponentsExportTests`
Expected: PASS, 2 tests.

- [ ] **Step 5: Build Release, run everything, commit**

Run: `dotnet build Dse.sln -c Release --nologo` — expect `0 Warning(s)`, `0 Error(s)`.
Run: `dotnet test Dse.sln --nologo` — expect all green.

```bash
git add .gitignore src/Dse.Core/Catalogue/CatalogueJson.cs tests/Shared tests/Dse.Core.Tests/Catalogue tests/Dse.Components.Tests
git commit -m "feat(catalogue): export the catalogue as deterministic JSON and pin it with a golden file"
```

---
### Task 10: `Dse.Configuration` — diagnostics, parse and structure

The new project, the diagnostic model, and the first two loader stages. After
this task `PlantLoader.Load` finds every syntax and structural error in a plant
file; for a structurally valid file it returns no diagnostics and — until Task
12 — no builder.

**The pipeline** is a list of stages over one `LoadState`. A stage runs only if
no earlier stage produced an error; within a stage every error is collected.
Tasks 11 and 12 append stages to the list.

**Files:**
- Create: `src/Dse.Configuration/Dse.Configuration.csproj`
- Create: `src/Dse.Configuration/DiagnosticSeverity.cs`, `ConfigDiagnostic.cs`, `DiagnosticInfo.cs`, `ConfigDiagnostics.cs`
- Create: `src/Dse.Configuration/LoadOptions.cs`, `LoadResult.cs`, `PlantSummary.cs`, `PlantLoader.cs`
- Create: `src/Dse.Configuration/Loading/LoadState.cs`, `Loading/ParseStage.cs`, `Loading/StructureStage.cs`, `Loading/PlantSchemas.cs`
- Create: `tests/Dse.Configuration.Tests/Dse.Configuration.Tests.csproj`
- Test: `tests/Dse.Configuration.Tests/Plants.cs`, `ParseAndStructureTests.cs`, `ConfigDiagnosticTests.cs`
- Modify: `Dse.sln`

**Interfaces:**
- Consumes: `ComponentCatalogue`, `BindingContext`, `ParameterBinder`, `BindingIssue`, `Suggest`, `Param` (Tasks 2–3); `ComponentsModule` (Tasks 4–8).
- Produces (namespace `Dse.Configuration`):
  - `enum DiagnosticSeverity { Error, Warning }`
  - `sealed record ConfigDiagnostic(string Code, DiagnosticSeverity Severity, string Path, string Message, string Fix)` with `ToText()`; the constructor rejects an empty `Fix`
  - `sealed record DiagnosticInfo(string Code, string Title, string Explanation)`
  - `static class ConfigDiagnostics` — constants `Syntax = "DSE100"` … `TagCannotBind = "DSE112"`, `All`, and `internal` factories `Error(...)`, `From(BindingIssue)`
  - `sealed class LoadOptions { TimeSpan? TimeStep; ulong? Seed; DateTimeOffset? StartTime; }`
  - `sealed record PlantSummary(int Components, int SignalLinks, int FlowLinks, int ExplicitTags)`
  - `sealed class LoadResult` — `Diagnostics`, `IsValid`, `Builder` (`SimulationBuilder?`), `Options` (`SimulationOptions?`), `Summary` (`PlantSummary?`), `Nodes` (`IReadOnlyDictionary<string, ISimNode>`), `ToText()`
  - `static class PlantLoader { LoadResult Load(string json, ComponentCatalogue catalogue, LoadOptions? options = null); }`
  - internal: `LoadState`, `ComponentEntry`, `LinkEntry`, `TagRequest`, `PlantDefaults`, `ILoadStage`-free static stages `ParseStage.Run`, `StructureStage.Run`

**Diagnostic codes** (final; the spec's table was provisional and gains `DSE112`):

| Code | Title |
|---|---|
| DSE100 | The file is not valid JSON |
| DSE101 | Unknown key |
| DSE102 | Unknown component, object or material type |
| DSE103 | Parameter missing, of the wrong type, or out of range |
| DSE104 | Reference to a component that does not exist |
| DSE105 | Referenced component lacks the required capability |
| DSE106 | Reference cycle |
| DSE107 | Duplicate component id or material name |
| DSE108 | Unknown component or port in an address |
| DSE109 | The two ports cannot be connected |
| DSE110 | Unknown material state |
| DSE111 | A constructor rejected its parameters |
| DSE112 | A tag cannot bind that port |

- [ ] **Step 1: Create the two projects and add them to the solution**

`src/Dse.Configuration/Dse.Configuration.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\Dse.Core\Dse.Core.csproj" />
    <ProjectReference Include="..\Dse.Components\Dse.Components.csproj" />
  </ItemGroup>
  <ItemGroup>
    <InternalsVisibleTo Include="Dse.Configuration.Tests" />
  </ItemGroup>
</Project>
```

`tests/Dse.Configuration.Tests/Dse.Configuration.Tests.csproj` — copy the
package versions from `tests/Dse.Core.Tests/Dse.Core.Tests.csproj` if they
differ from these:

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
    <ProjectReference Include="..\..\src\Dse.Configuration\Dse.Configuration.csproj" />
  </ItemGroup>

</Project>
```

```bash
dotnet sln Dse.sln add src/Dse.Configuration/Dse.Configuration.csproj --solution-folder src
dotnet sln Dse.sln add tests/Dse.Configuration.Tests/Dse.Configuration.Tests.csproj --solution-folder tests
```

- [ ] **Step 2: Write the failing diagnostic tests**

`tests/Dse.Configuration.Tests/ConfigDiagnosticTests.cs`:

```csharp
namespace Dse.Configuration.Tests;

public class ConfigDiagnosticTests
{
    [Fact]
    public void FormatsAsCodePathMessageFix()
    {
        var diagnostic = new ConfigDiagnostic(
            "DSE104", DiagnosticSeverity.Error, "$.components[2].parameters.belt",
            "'CV01' is not a component in this plant.", "Use one of CV001, CV002 — 'CV001' is closest.");

        Assert.Equal(
            "DSE104 $.components[2].parameters.belt\n  'CV01' is not a component in this plant.\n  Fix: Use one of CV001, CV002 — 'CV001' is closest.",
            diagnostic.ToText());
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void ADiagnosticWithoutAFixCannotBeMade(string fix)
    {
        Assert.Throws<ArgumentException>(
            () => new ConfigDiagnostic("DSE103", DiagnosticSeverity.Error, "$", "Something is wrong.", fix));
    }

    [Fact]
    public void TheTableListsEveryCodeOnceInOrder()
    {
        string[] codes = ConfigDiagnostics.All.Select(d => d.Code).ToArray();

        Assert.Equal(13, codes.Length);
        Assert.Equal("DSE100", codes[0]);
        Assert.Equal("DSE112", codes[^1]);
        Assert.Equal(codes.Order(StringComparer.Ordinal), codes);
        Assert.Equal(codes.Length, codes.Distinct().Count());
        Assert.All(ConfigDiagnostics.All, d =>
        {
            Assert.EndsWith(".", d.Explanation, StringComparison.Ordinal);
            Assert.False(d.Title.EndsWith('.'));
        });
    }
}
```

Run: `dotnet test tests/Dse.Configuration.Tests --nologo`
Expected: build FAILS — `ConfigDiagnostic` does not exist.

- [ ] **Step 3: Write the diagnostic model**

`src/Dse.Configuration/DiagnosticSeverity.cs`:

```csharp
namespace Dse.Configuration;

public enum DiagnosticSeverity
{
    Error,
    Warning,
}
```

`src/Dse.Configuration/ConfigDiagnostic.cs`:

```csharp
namespace Dse.Configuration;

/// <summary>
/// One thing wrong with a plant file: a code, where it is (a JSON path), what
/// is wrong, and what to do. The fix is not optional — an error that does not
/// name its fix cannot be constructed.
/// </summary>
public sealed record ConfigDiagnostic
{
    public ConfigDiagnostic(string code, DiagnosticSeverity severity, string path, string message, string fix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        ArgumentException.ThrowIfNullOrWhiteSpace(fix);
        Code = code;
        Severity = severity;
        Path = path;
        Message = message;
        Fix = fix;
    }

    public string Code { get; }

    public DiagnosticSeverity Severity { get; }

    /// <summary>A JSON path from the document root: <c>$.components[3].parameters.motor.ratedPowerW</c>.</summary>
    public string Path { get; }

    public string Message { get; }

    public string Fix { get; }

    /// <summary>Three lines, <c>\n</c>-separated, no trailing newline.</summary>
    public string ToText() => $"{Code} {Path}\n  {Message}\n  Fix: {Fix}";
}
```

`src/Dse.Configuration/DiagnosticInfo.cs`:

```csharp
namespace Dse.Configuration;

/// <summary>What a diagnostic code means, for the reference page and <c>--help</c>.</summary>
public sealed record DiagnosticInfo(string Code, string Title, string Explanation);
```

`src/Dse.Configuration/ConfigDiagnostics.cs`:

```csharp
using Dse.Core.Catalogue;

namespace Dse.Configuration;

/// <summary>Every configuration diagnostic code. The reference page in docs/ is generated from <see cref="All"/>.</summary>
public static class ConfigDiagnostics
{
    public const string Syntax = "DSE100";
    public const string UnknownKey = "DSE101";
    public const string UnknownType = "DSE102";
    public const string BadParameter = "DSE103";
    public const string MissingReference = "DSE104";
    public const string MissingCapability = "DSE105";
    public const string ReferenceCycle = "DSE106";
    public const string Duplicate = "DSE107";
    public const string UnknownAddress = "DSE108";
    public const string CannotConnect = "DSE109";
    public const string UnknownState = "DSE110";
    public const string Rejected = "DSE111";
    public const string TagCannotBind = "DSE112";

    public static IReadOnlyList<DiagnosticInfo> All { get; } =
    [
        new(Syntax, "The file is not valid JSON",
            "The text could not be parsed. Comments and trailing commas are allowed; everything else must be strict JSON. The message gives the line and column."),
        new(UnknownKey, "Unknown key",
            "An object has a key the loader does not know. Keys match exactly, including case. Nothing is ignored silently, so a misspelt optional parameter cannot quietly fall back to its default."),
        new(UnknownType, "Unknown component, object or material type",
            "A \"type\" names something that is not in the catalogue, or a material name is not defined in the plant or the catalogue. Custom types need their assembly loaded with --assembly."),
        new(BadParameter, "Parameter missing, of the wrong type, or out of range",
            "A required parameter is absent, a value has the wrong JSON type, a number is outside its declared range, an enum value is not allowed, or a list is shorter than its minimum."),
        new(MissingReference, "Reference to a component that does not exist",
            "A reference parameter holds an id that no component in the plant has. Ids match exactly."),
        new(MissingCapability, "Referenced component lacks the required capability",
            "The referenced component exists but cannot supply what the parameter needs — a belt scale pointed at a motor. The fix lists the components that can."),
        new(ReferenceCycle, "Reference cycle",
            "Components reference each other in a loop, so none of them can be built first."),
        new(Duplicate, "Duplicate component id or material name",
            "Two components share an id, or a material is defined twice (the catalogue's materials and the plant's share one namespace)."),
        new(UnknownAddress, "Unknown component or port in an address",
            "A signal, flow or tag address is not of the form <component>.<port>, or names a component or port that does not exist. Port names match ignoring case."),
        new(CannotConnect, "The two ports cannot be connected",
            "A link joins ports that cannot be joined: two outputs, different value types, a signal port under \"flows\", bulk into discrete, an input that is already driven."),
        new(UnknownState, "Unknown material state",
            "A state name is not one of the states the named material declares."),
        new(Rejected, "A constructor rejected its parameters",
            "Every parameter was individually valid but the component or object refused the combination — a reset level above the trip level, a belt length that is not a whole number of cells. The message is the constructor's own."),
        new(TagCannotBind, "A tag cannot bind that port",
            "A tag names a port that has no tag kind (a flow port, an enum output), or asks to write an output."),
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
            _ => throw new InvalidOperationException($"Binding issue kind {issue.Kind} has no diagnostic code."),
        },
        issue.Path,
        issue.Message,
        issue.Fix);
}
```

Run: `dotnet test tests/Dse.Configuration.Tests --nologo --filter FullyQualifiedName~ConfigDiagnosticTests`
Expected: PASS, 4 test cases.

- [ ] **Step 4: Write the failing loader tests**

`tests/Dse.Configuration.Tests/Plants.cs`:

```csharp
using Dse.Components;
using Dse.Core.Catalogue;

namespace Dse.Configuration.Tests;

/// <summary>The catalogue and a smallest-useful plant that loader tests mutate with <c>Replace</c>.</summary>
internal static class Plants
{
    public static ComponentCatalogue Catalogue { get; } = new CatalogueBuilder().Add<ComponentsModule>().Build();

    /// <summary>Feed → chute → pile. Valid.</summary>
    public const string Minimal = """
        {
          "defaults": { "seed": 1, "timeStepMs": 10, "startTime": "2026-01-01T06:00:00Z" },
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
        """;

    public static LoadResult Load(string json, LoadOptions? options = null) => PlantLoader.Load(json, Catalogue, options);

    public static ConfigDiagnostic Only(string json) => Assert.Single(Load(json).Diagnostics);
}
```

`tests/Dse.Configuration.Tests/ParseAndStructureTests.cs`:

```csharp
namespace Dse.Configuration.Tests;

public class ParseAndStructureTests
{
    [Fact]
    public void TheMinimalPlantHasNoDiagnostics()
    {
        LoadResult result = Plants.Load(Plants.Minimal);

        Assert.Empty(result.Diagnostics);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void CommentsAndTrailingCommasAreAllowed()
    {
        string json = Plants.Minimal
            .Replace("\"components\": [", "// the line\n  \"components\": [", StringComparison.Ordinal)
            .Replace("{ \"id\": \"PILE\", \"type\": \"bulk-sink\" }", "{ \"id\": \"PILE\", \"type\": \"bulk-sink\" },", StringComparison.Ordinal);

        Assert.Empty(Plants.Load(json).Diagnostics);
    }

    [Fact]
    public void ASyntaxErrorGivesLineAndColumn()
    {
        ConfigDiagnostic d = Plants.Only("{\n  \"components\": [ { \"id\": }\n}");

        Assert.Equal("DSE100", d.Code);
        Assert.Equal("$", d.Path);
        Assert.Contains("line 2", d.Message, StringComparison.Ordinal);
        Assert.Contains("column", d.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheRootMustBeAnObject()
    {
        ConfigDiagnostic d = Plants.Only("[]");

        Assert.Equal("DSE103", d.Code);
        Assert.Equal("$", d.Path);
    }

    [Fact]
    public void AnUnknownTopLevelKeySuggestsTheNearest()
    {
        ConfigDiagnostic d = Plants.Only(Plants.Minimal.Replace("\"flows\":", "\"flow\":", StringComparison.Ordinal));

        Assert.Equal("DSE101", d.Code);
        Assert.Equal("$.flow", d.Path);
        Assert.Contains("'flows' is closest", d.Fix, StringComparison.Ordinal);
    }

    [Fact]
    public void ASchemaKeyIsAllowed()
    {
        string json = Plants.Minimal.Replace("\"defaults\":", "\"$schema\": \"./dse-plant.schema.json\",\n  \"defaults\":", StringComparison.Ordinal);

        Assert.Empty(Plants.Load(json).Diagnostics);
    }

    [Fact]
    public void ComponentsAreRequired()
    {
        ConfigDiagnostic d = Plants.Only("{}");

        Assert.Equal("DSE103", d.Code);
        Assert.Equal("$.components", d.Path);
    }

    [Theory]
    [InlineData("\"seed\": 1", "\"seed\": -1", "$.defaults.seed")]
    [InlineData("\"seed\": 1", "\"seed\": 1.5", "$.defaults.seed")]
    [InlineData("\"timeStepMs\": 10", "\"timeStepMs\": 0", "$.defaults.timeStepMs")]
    [InlineData("\"timeStepMs\": 10", "\"timeStepMs\": \"10\"", "$.defaults.timeStepMs")]
    [InlineData("\"startTime\": \"2026-01-01T06:00:00Z\"", "\"startTime\": \"yesterday\"", "$.defaults.startTime")]
    public void BadDefaultsAreParameterErrors(string from, string to, string path)
    {
        ConfigDiagnostic d = Plants.Only(Plants.Minimal.Replace(from, to, StringComparison.Ordinal));

        Assert.Equal("DSE103", d.Code);
        Assert.Equal(path, d.Path);
    }

    [Fact]
    public void AnUnknownDefaultsKeyIsReported()
    {
        ConfigDiagnostic d = Plants.Only(Plants.Minimal.Replace("\"seed\": 1", "\"sead\": 1", StringComparison.Ordinal));

        Assert.Equal("DSE101", d.Code);
        Assert.Equal("$.defaults.sead", d.Path);
    }

    [Fact]
    public void AnUnknownComponentTypeSuggestsTheNearest()
    {
        ConfigDiagnostic d = Plants.Only(Plants.Minimal.Replace("\"bulk-sink\"", "\"bulk-snik\"", StringComparison.Ordinal));

        Assert.Equal("DSE102", d.Code);
        Assert.Equal("$.components[2].type", d.Path);
        Assert.Contains("'bulk-sink' is closest", d.Fix, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnknownTypeMentionsAssemblies()
    {
        ConfigDiagnostic d = Plants.Only(Plants.Minimal.Replace("\"bulk-sink\"", "\"furnace\"", StringComparison.Ordinal));

        Assert.Contains("--assembly", d.Fix, StringComparison.Ordinal);
    }

    [Fact]
    public void AParameterErrorCarriesTheFullPath()
    {
        ConfigDiagnostic d = Plants.Only(Plants.Minimal.Replace("\"capacityKg\": 200", "\"capacityKg\": -1", StringComparison.Ordinal));

        Assert.Equal("DSE103", d.Code);
        Assert.Equal("$.components[1].parameters.capacityKg", d.Path);
    }

    [Fact]
    public void AMissingParametersObjectReportsTheRequiredParameters()
    {
        ConfigDiagnostic d = Plants.Only(Plants.Minimal.Replace(", \"parameters\": { \"capacityKg\": 200 }", string.Empty, StringComparison.Ordinal));

        Assert.Equal("DSE103", d.Code);
        Assert.Equal("$.components[1].parameters.capacityKg", d.Path);
    }

    [Fact]
    public void AnUnknownMaterialIsAnUnknownType()
    {
        ConfigDiagnostic d = Plants.Only(Plants.Minimal.Replace("\"material\": \"ore\"", "\"material\": \"oar\"", StringComparison.Ordinal));

        Assert.Equal("DSE102", d.Code);
        Assert.Equal("$.components[0].parameters.material", d.Path);
        Assert.Contains("'ore' is closest", d.Fix, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("\"id\": \"CHUTE\"", "\"id\": \"FEED\"", "DSE107", "$.components[1].id")]
    [InlineData("\"id\": \"CHUTE\"", "\"id\": \"CH.UTE\"", "DSE103", "$.components[1].id")]
    [InlineData("\"id\": \"CHUTE\"", "\"id\": \"CH UTE\"", "DSE103", "$.components[1].id")]
    [InlineData("\"id\": \"CHUTE\"", "\"id\": 7", "DSE103", "$.components[1].id")]
    [InlineData("\"id\": \"CHUTE\", ", "", "DSE103", "$.components[1].id")]
    [InlineData("\"id\": \"CHUTE\"", "\"id\": \"CHUTE\", \"colour\": \"red\"", "DSE101", "$.components[1].colour")]
    public void ComponentEnvelopeErrors(string from, string to, string code, string path)
    {
        // The flows still name CHUTE, but the wire stage never runs once structure has failed.
        ConfigDiagnostic d = Plants.Only(Plants.Minimal.Replace(from, to, StringComparison.Ordinal));

        Assert.Equal(code, d.Code);
        Assert.Equal(path, d.Path);
    }

    [Fact]
    public void AMaterialDefinedTwiceIsADuplicate()
    {
        string json = Plants.Minimal.Replace(
            "\"materials\": [", "\"materials\": [\n    { \"name\": \"ore\", \"kind\": \"bulk\" },", StringComparison.Ordinal);

        ConfigDiagnostic d = Plants.Only(json);

        Assert.Equal("DSE107", d.Code);
        Assert.Equal("$.materials[1].name", d.Path);
    }

    [Fact]
    public void AMaterialMayDeclareStatesAndOmitProperties()
    {
        string json = Plants.Minimal.Replace(
            "\"materials\": [", "\"materials\": [\n    { \"name\": \"dough\", \"kind\": \"bulk\", \"states\": [ \"proof\", \"bake\" ] },", StringComparison.Ordinal);

        Assert.Empty(Plants.Load(json).Diagnostics);
    }

    [Fact]
    public void ARepeatedStateNameIsAParameterError()
    {
        string json = Plants.Minimal.Replace(
            "\"materials\": [", "\"materials\": [\n    { \"name\": \"dough\", \"kind\": \"bulk\", \"states\": [ \"proof\", \"proof\" ] },", StringComparison.Ordinal);

        ConfigDiagnostic d = Plants.Only(json);

        Assert.Equal("DSE103", d.Code);
        Assert.Equal("$.materials[0].states", d.Path);
    }

    [Fact]
    public void ALinkNeedsFromAndTo()
    {
        ConfigDiagnostic d = Plants.Only(Plants.Minimal.Replace("{ \"from\": \"CHUTE.Out\", \"to\": \"PILE.In\" }", "{ \"from\": \"CHUTE.Out\" }", StringComparison.Ordinal));

        Assert.Equal("DSE103", d.Code);
        Assert.Equal("$.flows[1].to", d.Path);
    }

    [Fact]
    public void ATagAccessMustBeReadOrWrite()
    {
        string json = Plants.Minimal.Replace(
            "\"flows\": [", "\"tags\": [ { \"name\": \"FEED.RUN\", \"port\": \"FEED.Enabled\", \"access\": \"rw\" } ],\n  \"flows\": [", StringComparison.Ordinal);

        ConfigDiagnostic d = Plants.Only(json);

        Assert.Equal("DSE103", d.Code);
        Assert.Equal("$.tags[0].access", d.Path);
        Assert.Contains("read, write", d.Fix, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryStructuralErrorIsReportedTogether()
    {
        string json = Plants.Minimal
            .Replace("\"bulk-sink\"", "\"bulk-snik\"", StringComparison.Ordinal)
            .Replace("\"capacityKg\": 200", "\"capacityKg\": -1", StringComparison.Ordinal)
            .Replace("\"rateKgPerS\": 20", "\"rateKgPerSec\": 20", StringComparison.Ordinal);

        LoadResult result = Plants.Load(json);

        Assert.False(result.IsValid);
        Assert.Null(result.Builder);
        Assert.Equal(
            ["DSE101", "DSE102", "DSE103", "DSE103"],
            result.Diagnostics.Select(d => d.Code).Order(StringComparer.Ordinal));
    }
}
```

The last test expects four: the unknown key `rateKgPerSec`, the now-missing
required `rateKgPerS`, the negative capacity, and the unknown type.

Run: `dotnet test tests/Dse.Configuration.Tests --nologo --filter FullyQualifiedName~ParseAndStructureTests`
Expected: build FAILS — `PlantLoader`, `LoadResult` do not exist.

- [ ] **Step 5: Write the options, the result and the state**

`src/Dse.Configuration/LoadOptions.cs`:

```csharp
namespace Dse.Configuration;

/// <summary>Overrides for a plant's <c>defaults</c> block. A scenario or a command line sets these.</summary>
public sealed class LoadOptions
{
    public TimeSpan? TimeStep { get; init; }

    public ulong? Seed { get; init; }

    public DateTimeOffset? StartTime { get; init; }
}
```

`src/Dse.Configuration/PlantSummary.cs`:

```csharp
namespace Dse.Configuration;

/// <summary>What a valid plant file declared. Counts are of entries in the file, not of flattened leaves.</summary>
public sealed record PlantSummary(int Components, int SignalLinks, int FlowLinks, int ExplicitTags);
```

`src/Dse.Configuration/LoadResult.cs`:

```csharp
using Dse.Core;
using Dse.Core.Graph;
using Dse.Core.Time;

namespace Dse.Configuration;

/// <summary>
/// The outcome of loading a plant file. When <see cref="IsValid"/>, the
/// <see cref="Builder"/> has every component added, wired and validated and is
/// ready for <c>Build()</c>; it is returned unbuilt so a caller can still decide.
/// </summary>
public sealed class LoadResult
{
    internal LoadResult(
        IReadOnlyList<ConfigDiagnostic> diagnostics,
        SimulationBuilder? builder,
        SimulationOptions? options,
        PlantSummary? summary,
        IReadOnlyDictionary<string, ISimNode> nodes)
    {
        Diagnostics = diagnostics;
        Builder = builder;
        Options = options;
        Summary = summary;
        Nodes = nodes;
    }

    public IReadOnlyList<ConfigDiagnostic> Diagnostics { get; }

    public bool IsValid => Diagnostics.All(d => d.Severity != DiagnosticSeverity.Error);

    public SimulationBuilder? Builder { get; }

    /// <summary>The options the builder was made with: the file's defaults under any <see cref="LoadOptions"/>.</summary>
    public SimulationOptions? Options { get; }

    public PlantSummary? Summary { get; }

    /// <summary>The top-level components by id. Empty unless the plant is valid.</summary>
    public IReadOnlyDictionary<string, ISimNode> Nodes { get; }

    /// <summary>Every diagnostic, blank-line separated, ending in a newline; empty when there are none.</summary>
    public string ToText() =>
        Diagnostics.Count == 0 ? string.Empty : string.Join("\n\n", Diagnostics.Select(d => d.ToText())) + "\n";
}
```

`src/Dse.Configuration/Loading/LoadState.cs`:

```csharp
using System.Text.Json;
using Dse.Core;
using Dse.Core.Catalogue;
using Dse.Core.Graph;
using Dse.Core.Time;

namespace Dse.Configuration.Loading;

internal sealed class ComponentEntry(int index, string id, ComponentDescriptor descriptor, JsonElement parameters)
{
    public int Index { get; } = index;

    public string Id { get; } = id;

    public ComponentDescriptor Descriptor { get; } = descriptor;

    /// <summary>The <c>parameters</c> object, or <c>default</c> when the entry has none.</summary>
    public JsonElement Parameters { get; } = parameters;

    public string Path => $"$.components[{Index}]";

    public string ParametersPath => $"{Path}.parameters";

    public ISimNode? Node { get; set; }
}

internal sealed record LinkEntry(string From, string To, string Path);

internal sealed record TagRequest(
    string Name, string Port, bool Writable, string Unit, double RangeLow, double RangeHigh, string Description, string Path);

internal sealed record PlantDefaults(ulong? Seed, TimeSpan? TimeStep, DateTimeOffset? StartTime);

/// <summary>Everything the stages share. One per <c>Load</c>.</summary>
internal sealed class LoadState(ComponentCatalogue catalogue, LoadOptions options)
{
    public ComponentCatalogue Catalogue { get; } = catalogue;

    public LoadOptions Options { get; } = options;

    public BindingContext Context { get; } = new(catalogue);

    public List<ConfigDiagnostic> Diagnostics { get; } = [];

    public JsonElement Root { get; set; }

    public PlantDefaults Defaults { get; set; } = new(null, null, null);

    public List<ComponentEntry> Components { get; } = [];

    /// <summary>Set by the reference stage: <see cref="Components"/> in an order that builds referents first.</summary>
    public List<ComponentEntry> BuildOrder { get; set; } = [];

    public List<LinkEntry> Signals { get; } = [];

    public List<LinkEntry> Flows { get; } = [];

    public List<TagRequest> Tags { get; } = [];

    public SimulationBuilder? Builder { get; set; }

    public SimulationOptions? SimulationOptions { get; set; }

    public bool HasErrors => Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);

    public void Error(string code, string path, string message, string fix) =>
        Diagnostics.Add(ConfigDiagnostics.Error(code, path, message, fix));

    public void AddIssues(IEnumerable<BindingIssue> issues) => Diagnostics.AddRange(issues.Select(ConfigDiagnostics.From));
}
```

- [ ] **Step 6: Write the parse stage**

`src/Dse.Configuration/Loading/ParseStage.cs`:

```csharp
using System.Globalization;
using System.Text.Json;

namespace Dse.Configuration.Loading;

internal static class ParseStage
{
    private static readonly JsonDocumentOptions Options = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>The parsed document, which the caller disposes; null after reporting DSE100.</summary>
    public static JsonDocument? Run(string json, LoadState state)
    {
        try
        {
            JsonDocument document = JsonDocument.Parse(json, Options);
            state.Root = document.RootElement;
            return document;
        }
        catch (JsonException ex)
        {
            long line = (ex.LineNumber ?? 0) + 1;
            long column = (ex.BytePositionInLine ?? 0) + 1;
            state.Error(
                ConfigDiagnostics.Syntax,
                "$",
                string.Create(CultureInfo.InvariantCulture, $"The file is not valid JSON at line {line}, column {column}: {FirstSentence(ex.Message)}"),
                "Correct the JSON at that position. Comments and trailing commas are allowed; everything else must be strict JSON.");
            return null;
        }
    }

    // System.Text.Json appends "LineNumber: n | BytePositionInLine: m." to its messages; that is already in ours.
    private static string FirstSentence(string message)
    {
        int cut = message.IndexOf(" LineNumber:", StringComparison.Ordinal);
        string text = (cut < 0 ? message : message[..cut]).TrimEnd();
        return text.EndsWith('.') ? text : text + ".";
    }
}
```

- [ ] **Step 7: Write the envelope schemas**

The loader checks its own envelope — materials, links, tags — with the same
binder that checks component parameters, so their errors read the same way.

`src/Dse.Configuration/Loading/PlantSchemas.cs`:

```csharp
using Dse.Core.Catalogue;

namespace Dse.Configuration.Loading;

/// <summary>Parameter schemas for the parts of a plant file that are not component parameters.</summary>
internal static class PlantSchemas
{
    public static readonly string[] TopLevelKeys = ["$schema", "defaults", "materials", "components", "signals", "flows", "tags"];

    public static readonly string[] DefaultsKeys = ["seed", "timeStepMs", "startTime"];

    public static readonly string[] ComponentKeys = ["id", "type", "parameters"];

    public static readonly GroupDefinition MaterialProperties = new(
        "MaterialProperties",
        Param.Double("density", "Bulk density.", "kg/m³", @default: 0.0, min: 0.0),
        Param.Double("moisture", "Moisture as a mass fraction.", @default: 0.0, min: 0.0, max: 1.0),
        Param.Double("temperature", "Temperature of new material.", "°C", @default: 20.0));

    public static readonly ParameterDescriptor[] Material =
    [
        Param.String("name", "What components call it. Unique across the plant and the catalogue."),
        Param.Enum("kind", "Whether it flows as bulk mass or as discrete items.", ["bulk", "discrete"]),
        Param.StringList("states", "Names of the per-material state values a transform can accumulate into."),
        Param.Group("properties", "The properties a source gives new material.", MaterialProperties),
        Param.String("description", "For people.", @default: ""),
    ];

    public static readonly ParameterDescriptor[] Link =
    [
        Param.String("from", "The driving port, as <component>.<port>."),
        Param.String("to", "The driven port, as <component>.<port>."),
    ];

    public static readonly ParameterDescriptor[] Tag =
    [
        Param.String("name", "The tag name a client reads or writes."),
        Param.String("port", "The port bound, as <component>.<port>."),
        Param.Enum("access", "Whether a client may write it. Only an input can be written.", ["read", "write"], @default: "read"),
        Param.String("unit", "Engineering unit.", @default: ""),
        Param.Double("rangeLow", "Bottom of the range. Give both ends or neither.", optional: true),
        Param.Double("rangeHigh", "Top of the range. Give both ends or neither.", optional: true),
        Param.String("description", "A sentence fragment; becomes the tag's description.", @default: ""),
    ];
}
```

- [ ] **Step 8: Write the structure stage**

`src/Dse.Configuration/Loading/StructureStage.cs`:

```csharp
using System.Globalization;
using System.Text.Json;
using Dse.Core.Catalogue;
using Dse.Core.Flow;

namespace Dse.Configuration.Loading;

/// <summary>Stage 2: everything that can be checked without building anything.</summary>
internal static class StructureStage
{
    public static void Run(LoadState state)
    {
        JsonElement root = state.Root;
        if (root.ValueKind != JsonValueKind.Object)
        {
            state.Error(ConfigDiagnostics.BadParameter, "$", "A plant file is a JSON object.", "Wrap the content in { … } with a \"components\" array.");
            return;
        }

        CheckKeys(state, root, "$", PlantSchemas.TopLevelKeys, "keys");
        if (root.TryGetProperty("$schema", out JsonElement schema) && schema.ValueKind != JsonValueKind.String)
        {
            state.Error(ConfigDiagnostics.BadParameter, "$.$schema", "\"$schema\" must be a string.", "Give the schema's path or URL as a string, or remove the key.");
        }

        ReadDefaults(state, root);
        ReadMaterials(state, root);      // before components: their parameters name materials
        ReadComponents(state, root);
        ReadLinks(state, root, "signals", state.Signals);
        ReadLinks(state, root, "flows", state.Flows);
        ReadTags(state, root);
    }

    private static void CheckKeys(LoadState state, JsonElement element, string path, string[] allowed, string noun)
    {
        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (Array.IndexOf(allowed, property.Name) < 0)
            {
                state.Error(
                    ConfigDiagnostics.UnknownKey,
                    $"{path}.{property.Name}",
                    $"'{property.Name}' is not a key the loader knows here.",
                    Suggest.Fix(property.Name, allowed.Where(k => k[0] != '$'), noun));
            }
        }
    }

    private static void ReadDefaults(LoadState state, JsonElement root)
    {
        if (!root.TryGetProperty("defaults", out JsonElement defaults))
        {
            return;
        }

        if (defaults.ValueKind != JsonValueKind.Object)
        {
            state.Error(ConfigDiagnostics.BadParameter, "$.defaults", "\"defaults\" must be an object.", "Write { \"seed\": …, \"timeStepMs\": …, \"startTime\": … }; every key is optional.");
            return;
        }

        CheckKeys(state, defaults, "$.defaults", PlantSchemas.DefaultsKeys, "keys");

        ulong? seed = null;
        if (defaults.TryGetProperty("seed", out JsonElement seedElement))
        {
            if (seedElement.ValueKind == JsonValueKind.Number && seedElement.TryGetUInt64(out ulong value))
            {
                seed = value;
            }
            else
            {
                state.Error(ConfigDiagnostics.BadParameter, "$.defaults.seed", "\"seed\" must be a whole number, zero or greater.", "Use a non-negative integer such as 1.");
            }
        }

        TimeSpan? step = null;
        if (defaults.TryGetProperty("timeStepMs", out JsonElement stepElement))
        {
            if (stepElement.ValueKind == JsonValueKind.Number && stepElement.TryGetDouble(out double ms) && double.IsFinite(ms) && ms > 0.0)
            {
                step = TimeSpan.FromMilliseconds(ms);
            }
            else
            {
                state.Error(ConfigDiagnostics.BadParameter, "$.defaults.timeStepMs", "\"timeStepMs\" must be a number greater than zero.", "Use the simulation step in milliseconds, such as 10.");
            }
        }

        DateTimeOffset? start = null;
        if (defaults.TryGetProperty("startTime", out JsonElement startElement))
        {
            if (startElement.ValueKind == JsonValueKind.String
                && DateTimeOffset.TryParse(startElement.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset parsed))
            {
                start = parsed;
            }
            else
            {
                state.Error(ConfigDiagnostics.BadParameter, "$.defaults.startTime", "\"startTime\" must be an ISO 8601 date and time.", "Write it like \"2026-01-01T06:00:00Z\".");
            }
        }

        state.Defaults = new PlantDefaults(seed, step, start);
    }

    private static void ReadMaterials(LoadState state, JsonElement root)
    {
        ForEachBound(state, root, "materials", PlantSchemas.Material, required: false, (values, path) =>
        {
            string name = values.String("name");
            MaterialType type;
            try
            {
                PayloadKind kind = values.String("kind") == "bulk" ? PayloadKind.Bulk : PayloadKind.Discrete;
                type = new MaterialType(name, kind, values.Strings("states").ToArray());
            }
            catch (ArgumentException ex)
            {
                state.Error(ConfigDiagnostics.BadParameter, $"{path}.states", ex.Message, "Give each state a distinct, non-empty name.");
                return;
            }

            ParameterValues properties = values.Group("properties");
            var descriptor = new MaterialDescriptor(
                type,
                new MaterialProperties(properties.Double("density"), properties.Double("moisture"), properties.Double("temperature")),
                values.String("description"));
            if (state.Context.TryGetMaterial(name, out _))
            {
                state.Error(
                    ConfigDiagnostics.Duplicate,
                    $"{path}.name",
                    $"Material '{name}' is already defined, by the catalogue or earlier in this file.",
                    "Rename this material, or remove it and use the existing one.");
                return;
            }

            state.Context.AddMaterial(descriptor);
        });
    }

    private static void ReadComponents(LoadState state, JsonElement root)
    {
        if (!root.TryGetProperty("components", out JsonElement components) || components.ValueKind != JsonValueKind.Array)
        {
            state.Error(ConfigDiagnostics.BadParameter, "$.components", "A plant needs a \"components\" array.", "Add \"components\": [ { \"id\": …, \"type\": … } ].");
            return;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        int index = 0;
        foreach (JsonElement element in components.EnumerateArray())
        {
            ReadComponent(state, element, index, seen);
            index++;
        }
    }

    private static void ReadComponent(LoadState state, JsonElement element, int index, HashSet<string> seen)
    {
        string path = string.Create(CultureInfo.InvariantCulture, $"$.components[{index}]");
        if (element.ValueKind != JsonValueKind.Object)
        {
            state.Error(ConfigDiagnostics.BadParameter, path, "A component is an object.", "Write { \"id\": …, \"type\": …, \"parameters\": { … } }.");
            return;
        }

        CheckKeys(state, element, path, PlantSchemas.ComponentKeys, "keys");

        string? id = ReadId(state, element, path, seen);
        ComponentDescriptor? descriptor = ReadType(state, element, path);

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
        ParameterBinder.Bind(descriptor.Parameters, parameters, $"{path}.parameters", state.Context, construct: false, issues);
        state.AddIssues(issues);

        if (id is not null)
        {
            state.Components.Add(new ComponentEntry(index, id, descriptor, parameters));
        }
    }

    private static string? ReadId(LoadState state, JsonElement element, string path, HashSet<string> seen)
    {
        if (!element.TryGetProperty("id", out JsonElement idElement) || idElement.ValueKind != JsonValueKind.String)
        {
            state.Error(ConfigDiagnostics.BadParameter, $"{path}.id", "A component needs a string \"id\".", "Add \"id\": \"…\" with a name unique in this plant.");
            return null;
        }

        string id = idElement.GetString()!;
        if (id.Length == 0 || id.Contains('.', StringComparison.Ordinal) || id.Any(char.IsWhiteSpace))
        {
            state.Error(
                ConfigDiagnostics.BadParameter,
                $"{path}.id",
                $"'{id}' cannot be a component id: an id is non-empty and has no dot and no whitespace.",
                "Dots separate a component from its port in an address; use a hyphen or an underscore instead.");
            return null;
        }

        if (!seen.Add(id))
        {
            state.Error(ConfigDiagnostics.Duplicate, $"{path}.id", $"Another component is already called '{id}'.", "Give this component an id of its own.");
            return null;
        }

        return id;
    }

    private static ComponentDescriptor? ReadType(LoadState state, JsonElement element, string path)
    {
        if (!element.TryGetProperty("type", out JsonElement typeElement) || typeElement.ValueKind != JsonValueKind.String)
        {
            state.Error(ConfigDiagnostics.BadParameter, $"{path}.type", "A component needs a string \"type\".", "Add \"type\": \"…\" naming a catalogue type; `dse catalog export` lists them.");
            return null;
        }

        string type = typeElement.GetString()!;
        if (state.Catalogue.TryGetComponent(type, out ComponentDescriptor? descriptor))
        {
            return descriptor;
        }

        string? closest = Suggest.Closest(type, state.Catalogue.Components.Select(c => c.Type));
        state.Error(
            ConfigDiagnostics.UnknownType,
            $"{path}.type",
            $"'{type}' is not a component type in this catalogue.",
            closest is null
                ? "Run `dse catalog export` to list the types. A custom type needs its assembly loaded with --assembly."
                : $"Use a type that `dse catalog export` lists — '{closest}' is closest. A custom type needs its assembly loaded with --assembly.");
        return null;
    }

    private static void ReadLinks(LoadState state, JsonElement root, string key, List<LinkEntry> into) =>
        ForEachBound(state, root, key, PlantSchemas.Link, required: false, (values, path) =>
            into.Add(new LinkEntry(values.String("from"), values.String("to"), path)));

    private static void ReadTags(LoadState state, JsonElement root) =>
        ForEachBound(state, root, "tags", PlantSchemas.Tag, required: false, (values, path) =>
        {
            bool hasLow = values.Has("rangeLow");
            bool hasHigh = values.Has("rangeHigh");
            if (hasLow != hasHigh || (hasLow && values.Double("rangeHigh") <= values.Double("rangeLow")))
            {
                state.Error(
                    ConfigDiagnostics.BadParameter,
                    $"{path}.{(hasLow ? "rangeHigh" : "rangeLow")}",
                    "A tag's range needs both ends, with rangeHigh above rangeLow.",
                    "Give both \"rangeLow\" and \"rangeHigh\", or neither.");
                return;
            }

            state.Tags.Add(new TagRequest(
                values.String("name"),
                values.String("port"),
                values.String("access") == "write",
                values.String("unit"),
                values.DoubleOr("rangeLow", double.NaN),
                values.DoubleOr("rangeHigh", double.NaN),
                values.String("description"),
                path));
        });

    private static void ForEachBound(
        LoadState state, JsonElement root, string key, IReadOnlyList<ParameterDescriptor> schema, bool required, Action<ParameterValues, string> use)
    {
        if (!root.TryGetProperty(key, out JsonElement array))
        {
            if (required)
            {
                state.Error(ConfigDiagnostics.BadParameter, $"$.{key}", $"A plant needs a \"{key}\" array.", $"Add \"{key}\": [ … ].");
            }

            return;
        }

        if (array.ValueKind != JsonValueKind.Array)
        {
            state.Error(ConfigDiagnostics.BadParameter, $"$.{key}", $"\"{key}\" must be an array.", $"Write \"{key}\": [ … ].");
            return;
        }

        int index = 0;
        foreach (JsonElement element in array.EnumerateArray())
        {
            string path = string.Create(CultureInfo.InvariantCulture, $"$.{key}[{index}]");
            var issues = new List<BindingIssue>();
            ParameterValues? values = ParameterBinder.Bind(schema, element, path, state.Context, construct: true, issues);
            state.AddIssues(issues);
            if (values is not null)
            {
                use(values, path);
            }

            index++;
        }
    }
}
```

- [ ] **Step 9: Write `PlantLoader`**

`src/Dse.Configuration/PlantLoader.cs`:

```csharp
using System.Text.Json;
using Dse.Configuration.Loading;
using Dse.Core.Catalogue;
using Dse.Core.Graph;

namespace Dse.Configuration;

/// <summary>
/// Loads a declarative plant. Stages run in order; a stage runs only if no
/// earlier one reported an error, and reports every error it can find.
/// </summary>
public static class PlantLoader
{
    private static readonly Action<LoadState>[] Stages =
    [
        StructureStage.Run,
    ];

    public static LoadResult Load(string json, ComponentCatalogue catalogue, LoadOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentNullException.ThrowIfNull(catalogue);

        var state = new LoadState(catalogue, options ?? new LoadOptions());
        using JsonDocument? document = ParseStage.Run(json, state);
        if (document is not null)
        {
            foreach (Action<LoadState> stage in Stages)
            {
                stage(state);
                if (state.HasErrors)
                {
                    break;
                }
            }
        }

        bool valid = !state.HasErrors && state.Builder is not null;
        return new LoadResult(
            state.Diagnostics,
            valid ? state.Builder : null,
            valid ? state.SimulationOptions : null,
            valid ? new PlantSummary(state.Components.Count, state.Signals.Count, state.Flows.Count, state.Tags.Count) : null,
            valid
                ? state.Components.ToDictionary(c => c.Id, c => c.Node!, StringComparer.Ordinal)
                : new Dictionary<string, ISimNode>(StringComparer.Ordinal));
    }
}
```

- [ ] **Step 10: Run the loader tests**

Run: `dotnet test tests/Dse.Configuration.Tests --nologo`
Expected: PASS — 4 diagnostic cases and 30 parse-and-structure cases (21
methods; the two theories contribute 5 and 6).

- [ ] **Step 11: Build Release, run everything, commit**

Run: `dotnet build Dse.sln -c Release --nologo` — expect `0 Warning(s)`, `0 Error(s)`.
Run: `dotnet test Dse.sln --nologo` — expect all green.

```bash
git add Dse.sln src/Dse.Configuration tests/Dse.Configuration.Tests
git commit -m "feat(configuration): parse a plant file and report every structural error with its fix"
```

---
### Task 11: Loader — references and instantiation

Stage 3 resolves every `Reference` parameter statically — does the id exist,
can that type supply the capability (R38), is there a cycle — and orders the
components so that referents are built first. Stage 4 builds them.

**Files:**
- Create: `src/Dse.Configuration/Loading/ReferenceStage.cs`, `Loading/InstantiateStage.cs`
- Modify: `src/Dse.Configuration/Loading/LoadState.cs` (`ComponentEntry.ReferencedIds`)
- Modify: `src/Dse.Configuration/PlantLoader.cs` (two more stages)
- Test: `tests/Dse.Configuration.Tests/TestModule.cs`, `ReferenceAndInstantiateTests.cs`

**Interfaces:**
- Consumes: `LoadState`, `ComponentEntry` (Task 10); `ParameterBinder`, `Suggest` (Task 3); `ComponentDescriptor.Provides` (Task 2).
- Produces: `ReferenceStage.Run(LoadState)` — fills `ComponentEntry.ReferencedIds` and `LoadState.BuildOrder`; `InstantiateStage.Run(LoadState)` — fills `ComponentEntry.Node` and adds each node to `LoadState.Context`. In tests: `TestModule` with types `echo` (optional reference `other`, provides `ISimComponent`) and `broken` (a factory that asks for an undeclared parameter), and `TestPlants.Catalogue`.

- [ ] **Step 1: Write the test module and the failing tests**

`tests/Dse.Configuration.Tests/TestModule.cs`:

```csharp
using Dse.Components;
using Dse.Core.Catalogue;
using Dse.Core.Graph;

namespace Dse.Configuration.Tests;

/// <summary>Two types the shipped catalogue cannot provide: one that can form a reference cycle, one whose factory is defective.</summary>
internal sealed class TestModule : ICatalogueModule
{
    public string Name => "Test";

    public void Register(CatalogueBuilder builder)
    {
        builder.Add(new ComponentDescriptor("echo", ComponentCategory.Signal, "References another echo.", (id, p) => new UnitDelay<bool>(id))
        {
            Parameters = [Param.Reference<ISimComponent>("other", "Another component.", optional: true)],
            Ports = [PortSpec.In<bool>("In"), PortSpec.Out<bool>("Out")],
            Provides = [typeof(ISimComponent)],
        });
        builder.Add(new ComponentDescriptor("broken", ComponentCategory.Signal, "Its factory is wrong.", (id, p) => new UnitDelay<bool>(id, p.Bool("nope"))));
    }
}

internal static class TestPlants
{
    public static ComponentCatalogue Catalogue { get; } = new CatalogueBuilder().Add<ComponentsModule>().Add<TestModule>().Build();

    public static LoadResult Load(string json) => PlantLoader.Load(json, Catalogue);

    public static ConfigDiagnostic Only(string json) => Assert.Single(Load(json).Diagnostics);
}
```

`tests/Dse.Configuration.Tests/ReferenceAndInstantiateTests.cs`:

```csharp
namespace Dse.Configuration.Tests;

public class ReferenceAndInstantiateTests
{
    /// <summary>The scale is listed before the belt it references, on purpose.</summary>
    private const string Weighed = """
        {
          "materials": [ { "name": "ore", "kind": "bulk" } ],
          "components": [
            { "id": "WT", "type": "belt-scale",
              "parameters": { "belt": "BELT", "positionM": 5, "spec": { "unit": "t/h", "rangeLow": 0, "rangeHigh": 800 } } },
            { "id": "FEED", "type": "bulk-source", "parameters": { "material": "ore", "rateKgPerS": 20 } },
            { "id": "BELT", "type": "bulk-belt",
              "parameters": { "lengthM": 10, "cellSizeM": 0.5, "maxSpeedMps": 2, "maxLinearDensityKgPerM": 100 } },
            { "id": "PILE", "type": "bulk-sink" }
          ],
          "flows": [ { "from": "FEED.Out", "to": "BELT.In" }, { "from": "BELT.Out", "to": "PILE.In" } ]
        }
        """;

    [Fact]
    public void AForwardReferenceIsFine()
    {
        Assert.Empty(TestPlants.Load(Weighed).Diagnostics);
    }

    [Fact]
    public void AMissingReferenceSuggestsTheNearestId()
    {
        ConfigDiagnostic d = TestPlants.Only(Weighed.Replace("\"belt\": \"BELT\"", "\"belt\": \"BLET\"", StringComparison.Ordinal));

        Assert.Equal("DSE104", d.Code);
        Assert.Equal("$.components[0].parameters.belt", d.Path);
        Assert.Contains("'BELT' is closest", d.Fix, StringComparison.Ordinal);
    }

    [Fact]
    public void AReferenceToTheWrongKindOfThingListsTheRightOnes()
    {
        ConfigDiagnostic d = TestPlants.Only(Weighed.Replace("\"belt\": \"BELT\"", "\"belt\": \"FEED\"", StringComparison.Ordinal));

        Assert.Equal("DSE105", d.Code);
        Assert.Equal("$.components[0].parameters.belt", d.Path);
        Assert.Contains("bulk-source", d.Message, StringComparison.Ordinal);
        Assert.Contains("IMaterialObservable", d.Message, StringComparison.Ordinal);
        Assert.Equal("Reference one of BELT.", d.Fix);
    }

    [Fact]
    public void ASelfReferenceIsACycleOfOne()
    {
        ConfigDiagnostic d = TestPlants.Only(Weighed.Replace("\"belt\": \"BELT\"", "\"belt\": \"WT\"", StringComparison.Ordinal));

        Assert.Equal("DSE106", d.Code);
        Assert.Contains("references itself", d.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ACycleIsReportedWithItsPath()
    {
        const string Json = """
            { "components": [
                { "id": "A", "type": "echo", "parameters": { "other": "B" } },
                { "id": "B", "type": "echo", "parameters": { "other": "C" } },
                { "id": "C", "type": "echo", "parameters": { "other": "A" } },
                { "id": "D", "type": "echo" } ] }
            """;

        ConfigDiagnostic d = TestPlants.Only(Json);

        Assert.Equal("DSE106", d.Code);
        Assert.Equal("$.components[0].parameters", d.Path);
        Assert.Contains("A -> B -> C -> A", d.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AChainListedBackwardsStillBuilds()
    {
        const string Json = """
            { "components": [
                { "id": "C", "type": "echo", "parameters": { "other": "B" } },
                { "id": "B", "type": "echo", "parameters": { "other": "A" } },
                { "id": "A", "type": "echo" } ] }
            """;

        Assert.Empty(TestPlants.Load(Json).Diagnostics);
    }

    [Fact]
    public void AConstructorsRefusalBecomesDse111WithItsMessage()
    {
        const string Json = """
            { "components": [ { "id": "MS", "type": "motor-starter", "parameters": { "tripLevel": 1.1, "resetLevel": 1.5 } } ] }
            """;

        ConfigDiagnostic d = TestPlants.Only(Json);

        Assert.Equal("DSE111", d.Code);
        Assert.Equal("$.components[0]", d.Path);
        Assert.Contains("reset level", d.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("'MS'", d.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ADefectiveFactoryNamesItsModuleInsteadOfCrashing()
    {
        ConfigDiagnostic d = TestPlants.Only("""{ "components": [ { "id": "X", "type": "broken" } ] }""");

        Assert.Equal("DSE111", d.Code);
        Assert.Contains("defect", d.Fix, StringComparison.Ordinal);
        Assert.Contains("'Test'", d.Fix, StringComparison.Ordinal);
    }

    [Fact]
    public void ADependentOfAFailedComponentIsSkippedQuietly()
    {
        // 10 m is not a whole number of 3 m cells, so the belt's constructor refuses; the scale that references it says nothing.
        ConfigDiagnostic d = TestPlants.Only(Weighed.Replace("\"cellSizeM\": 0.5", "\"cellSizeM\": 3", StringComparison.Ordinal));

        Assert.Equal("DSE111", d.Code);
        Assert.Equal("$.components[2]", d.Path);
    }

    [Fact]
    public void AnUnknownStateInsideANestedObjectIsDse110()
    {
        const string Json = """
            { "materials": [ { "name": "billet", "kind": "discrete", "states": [ "soak" ] } ],
              "components": [ { "id": "FCE", "type": "item-process-unit", "parameters": {
                  "batchSize": 4, "hold": { "type": "state-at-least", "material": "billet", "state": "sok", "value": 600 } } } ] }
            """;

        ConfigDiagnostic d = TestPlants.Only(Json);

        Assert.Equal("DSE110", d.Code);
        Assert.Equal("$.components[0].parameters.hold.state", d.Path);
        Assert.Contains("'soak' is closest", d.Fix, StringComparison.Ordinal);
    }
}
```

Run: `dotnet test tests/Dse.Configuration.Tests --nologo --filter FullyQualifiedName~ReferenceAndInstantiateTests`
Expected: FAIL — the reference tests report no diagnostics, because the stages do not exist.
(`AnUnknownStateInsideANestedObjectIsDse110` already passes: that is stage 2's work. It lives here because it needs no new fixture.)

- [ ] **Step 2: Give `ComponentEntry` its referents**

In `LoadState.cs`, add to `ComponentEntry`:

```csharp
    /// <summary>Ids this component's reference parameters name. Filled by the reference stage.</summary>
    public List<string> ReferencedIds { get; } = [];
```

- [ ] **Step 3: Write the reference stage**

`src/Dse.Configuration/Loading/ReferenceStage.cs`:

```csharp
using System.Globalization;
using System.Text.Json;
using Dse.Core.Catalogue;

namespace Dse.Configuration.Loading;

/// <summary>Stage 3: every reference names a real component of a suitable type, and there is an order to build them in.</summary>
internal static class ReferenceStage
{
    public static void Run(LoadState state)
    {
        var byId = state.Components.ToDictionary(c => c.Id, StringComparer.Ordinal);
        foreach (ComponentEntry entry in state.Components)
        {
            foreach ((string path, string id, ParameterDescriptor parameter) in References(entry.Descriptor.Parameters, entry.Parameters, entry.ParametersPath))
            {
                if (!byId.TryGetValue(id, out ComponentEntry? target))
                {
                    state.Error(ConfigDiagnostics.MissingReference, path, $"'{id}' is not a component in this plant.", Suggest.Fix(id, byId.Keys, "components"));
                }
                else if (ReferenceEquals(target, entry))
                {
                    state.Error(ConfigDiagnostics.ReferenceCycle, path, $"'{id}' references itself.", "Reference a different component.");
                }
                else if (!Supplies(target.Descriptor, parameter.Capability!))
                {
                    List<string> able = state.Components.Where(c => Supplies(c.Descriptor, parameter.Capability!)).Select(c => c.Id).ToList();
                    state.Error(
                        ConfigDiagnostics.MissingCapability,
                        path,
                        $"'{id}' is a {target.Descriptor.Type}, which cannot supply {parameter.Capability!.Name}; '{parameter.Name}' needs one.",
                        able.Count == 0
                            ? $"No component in this plant supplies {parameter.Capability.Name}; add one that does, then reference it."
                            : $"Reference one of {Suggest.List(able)}.");
                }
                else
                {
                    entry.ReferencedIds.Add(id);
                }
            }
        }

        if (!state.HasErrors)
        {
            Order(state, byId);
        }
    }

    /// <summary>Every reference in a parameters object, through groups and group lists, with its JSON path.</summary>
    private static IEnumerable<(string Path, string Id, ParameterDescriptor Parameter)> References(
        IReadOnlyList<ParameterDescriptor> schema, JsonElement json, string path)
    {
        if (json.ValueKind != JsonValueKind.Object)
        {
            yield break;
        }

        foreach (ParameterDescriptor parameter in schema)
        {
            if (!json.TryGetProperty(parameter.Name, out JsonElement value))
            {
                continue;
            }

            string childPath = $"{path}.{parameter.Name}";
            switch (parameter.Kind)
            {
                case ParameterKind.Reference when value.ValueKind == JsonValueKind.String:
                    yield return (childPath, value.GetString()!, parameter);
                    break;

                case ParameterKind.Group:
                    foreach (var found in References(parameter.Children, value, childPath))
                    {
                        yield return found;
                    }

                    break;

                case ParameterKind.GroupList when value.ValueKind == JsonValueKind.Array:
                    int index = 0;
                    foreach (JsonElement item in value.EnumerateArray())
                    {
                        foreach (var found in References(parameter.Children, item, string.Create(CultureInfo.InvariantCulture, $"{childPath}[{index}]")))
                        {
                            yield return found;
                        }

                        index++;
                    }

                    break;
            }
        }
    }

    private static bool Supplies(ComponentDescriptor descriptor, Type capability) =>
        descriptor.Provides.Any(capability.IsAssignableFrom);

    /// <summary>File order, except that a component waits for what it references. Deterministic.</summary>
    private static void Order(LoadState state, Dictionary<string, ComponentEntry> byId)
    {
        var placed = new HashSet<string>(StringComparer.Ordinal);
        var order = new List<ComponentEntry>(state.Components.Count);
        while (order.Count < state.Components.Count)
        {
            ComponentEntry? next = state.Components.FirstOrDefault(c => !placed.Contains(c.Id) && c.ReferencedIds.All(placed.Contains));
            if (next is null)
            {
                ReportCycle(state, byId, placed);
                return;
            }

            placed.Add(next.Id);
            order.Add(next);
        }

        state.BuildOrder = order;
    }

    private static void ReportCycle(LoadState state, Dictionary<string, ComponentEntry> byId, HashSet<string> placed)
    {
        // Every unplaced component waits on an unplaced one, so walking "first unplaced referent" must revisit something.
        ComponentEntry start = state.Components.First(c => !placed.Contains(c.Id));
        var walk = new List<ComponentEntry> { start };
        ComponentEntry current = start;
        while (true)
        {
            current = byId[current.ReferencedIds.First(id => !placed.Contains(id))];
            int seenAt = walk.IndexOf(current);
            if (seenAt >= 0)
            {
                List<ComponentEntry> cycle = walk.GetRange(seenAt, walk.Count - seenAt);
                string path = string.Join(" -> ", cycle.Select(c => c.Id).Append(cycle[0].Id));
                state.Error(
                    ConfigDiagnostics.ReferenceCycle,
                    cycle[0].ParametersPath,
                    $"These components reference each other in a loop: {path}.",
                    "Remove one of the references; a component cannot be built before the thing it references.");
                return;
            }

            walk.Add(current);
        }
    }
}
```

- [ ] **Step 4: Write the instantiate stage**

`src/Dse.Configuration/Loading/InstantiateStage.cs`:

```csharp
using Dse.Core.Catalogue;
using Dse.Core.Graph;

namespace Dse.Configuration.Loading;

/// <summary>Stage 4: build every component, referents first. A failure skips its dependents without further noise.</summary>
internal static class InstantiateStage
{
    public static void Run(LoadState state)
    {
        var failed = new HashSet<string>(StringComparer.Ordinal);
        foreach (ComponentEntry entry in state.BuildOrder)
        {
            if (entry.ReferencedIds.Any(failed.Contains) || !TryBuild(state, entry))
            {
                failed.Add(entry.Id);
            }
        }
    }

    private static bool TryBuild(LoadState state, ComponentEntry entry)
    {
        var issues = new List<BindingIssue>();
        ParameterValues? values = ParameterBinder.Bind(
            entry.Descriptor.Parameters, entry.Parameters, entry.ParametersPath, state.Context, construct: true, issues);
        state.AddIssues(issues);
        if (values is null)
        {
            return false;
        }

        ISimNode node;
        try
        {
            node = entry.Descriptor.Factory(entry.Id, values);
        }
        catch (ArgumentException ex)
        {
            state.Error(
                ConfigDiagnostics.Rejected,
                entry.Path,
                $"'{entry.Id}' ({entry.Descriptor.Type}) rejected its parameters: {WithoutParameterSuffix(ex.Message)}",
                "Change the parameter the message names; each value is valid alone, the combination is not.");
            return false;
        }
#pragma warning disable CA1031 // A defective third-party factory must become a diagnostic, not a crash.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            state.Error(
                ConfigDiagnostics.Rejected,
                entry.Path,
                $"The factory for type '{entry.Descriptor.Type}' failed with {ex.GetType().Name}: {ex.Message}",
                $"This is a defect in module '{state.Catalogue.ModuleOf(entry.Descriptor)}', not in the plant file; report it with this file. " +
                "The module's conformance test should have caught it.");
            return false;
        }

        entry.Node = node;
        state.Context.AddNode(node);
        return true;
    }

    // ArgumentException appends " (Parameter 'x')" naming a C# parameter, which means nothing to a plant author.
    private static string WithoutParameterSuffix(string message)
    {
        int cut = message.IndexOf(" (Parameter '", StringComparison.Ordinal);
        string text = cut < 0 ? message : message[..cut];
        return text.EndsWith('.') ? text : text + ".";
    }
}
```

- [ ] **Step 5: Add the stages to the pipeline**

In `PlantLoader.cs`:

```csharp
    private static readonly Action<LoadState>[] Stages =
    [
        StructureStage.Run,
        ReferenceStage.Run,
        InstantiateStage.Run,
    ];
```

- [ ] **Step 6: Run the tests**

Run: `dotnet test tests/Dse.Configuration.Tests --nologo`
Expected: PASS — Task 10's 34 cases and these 10.

`AConstructorsRefusalBecomesDse111WithItsMessage` looks for "reset level"
ignoring case, which is what `MotorStarter`'s constructor says today ("The reset
level must be below the trip level."). If the text differs, report the text.

- [ ] **Step 7: Build Release, run everything, commit**

Run: `dotnet build Dse.sln -c Release --nologo` — expect `0 Warning(s)`, `0 Error(s)`.
Run: `dotnet test Dse.sln --nologo` — expect all green.

```bash
git add src/Dse.Configuration tests/Dse.Configuration.Tests
git commit -m "feat(configuration): resolve references, order construction and build the components"
```

---
### Task 12: Loader — wire, build, the fixture corpus and the round trip

Stage 5 turns addresses into ports and connects them; stage 6 hands everything
to `SimulationBuilder` and passes Core's own validation through (R40). Then the
two proofs the spec asks for: a corpus in which every diagnostic code has a file
that produces exactly it, and a JSON plant whose event log is byte-identical to
its hand-built twin.

**Files:**
- Create: `src/Dse.Configuration/Loading/WireStage.cs`, `Loading/BuildStage.cs`
- Modify: `src/Dse.Configuration/Loading/LoadState.cs` (`Bindings`)
- Modify: `src/Dse.Configuration/PlantLoader.cs` (two more stages)
- Modify: `tests/Dse.Configuration.Tests/Dse.Configuration.Tests.csproj` (copy the corpus to the output)
- Create: `tests/Dse.Configuration.Tests/Plants/valid/*.json` (4), `Plants/invalid/*.json` (14)
- Test: `tests/Dse.Configuration.Tests/WireAndBuildTests.cs`, `CorpusTests.cs`, `RoundTripTests.cs`

**Interfaces:**
- Consumes: `PortConnector`, `CompositeComponent.ExposedPorts` / `.LeafComponents`, `TagBinding.ForPort` (Task 1); `LoadState` (Tasks 10–11).
- Produces: a complete `PlantLoader.Load` — for a valid plant `LoadResult.Builder`, `.Options`, `.Summary` and `.Nodes` are set. In tests: `Corpus.Valid()` and `Corpus.Invalid()` (`IEnumerable<object[]>` of file names) and `Corpus.Read(kind, name)`, reused by Task 13.

**Address rules** (spec 3.1): `<component>.<port>`, split on the **last** dot.
The component part matches ordinally against every top-level id and every
flattened leaf id (`CV001.Motor`). The port part matches ignoring case — against
`Ports` for a leaf, against `ExposedPorts` aliases for a composite.

- [ ] **Step 1: Write the failing wire-and-build tests**

`tests/Dse.Configuration.Tests/WireAndBuildTests.cs`:

```csharp
using Dse.Core;
using Dse.Io;

namespace Dse.Configuration.Tests;

public class WireAndBuildTests
{
    [Fact]
    public void AValidPlantComesBackReadyToBuild()
    {
        LoadResult result = Plants.Load(Plants.Minimal);

        Assert.True(result.IsValid);
        Assert.NotNull(result.Builder);
        Assert.Equal(new PlantSummary(3, 0, 2, 0), result.Summary);
        Assert.Equal(["CHUTE", "FEED", "PILE"], result.Nodes.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(1UL, result.Options!.Seed);
        Assert.Equal(TimeSpan.FromMilliseconds(10), result.Options.TimeStep);
        Assert.Equal(new DateTimeOffset(2026, 1, 1, 6, 0, 0, TimeSpan.Zero), result.Options.StartTime);

        Simulation simulation = result.Builder.Build();
        simulation.RunFor(TimeSpan.FromSeconds(5));
        Assert.True(simulation.IO.ReadDouble("PILE.Received") > 0.0);
    }

    [Fact]
    public void LoadOptionsOverrideTheFilesDefaults()
    {
        LoadResult result = Plants.Load(Plants.Minimal, new LoadOptions { TimeStep = TimeSpan.FromMilliseconds(5), Seed = 9UL });

        Assert.Equal(TimeSpan.FromMilliseconds(5), result.Options!.TimeStep);
        Assert.Equal(9UL, result.Options.Seed);
        Assert.Equal(new DateTimeOffset(2026, 1, 1, 6, 0, 0, TimeSpan.Zero), result.Options.StartTime);
    }

    [Fact]
    public void PortNamesMatchIgnoringCase()
    {
        string json = Plants.Minimal.Replace("\"FEED.Out\"", "\"FEED.out\"", StringComparison.Ordinal);

        Assert.True(Plants.Load(json).IsValid);
    }

    [Fact]
    public void ComponentIdsDoNot()
    {
        ConfigDiagnostic d = Plants.Only(Plants.Minimal.Replace("\"FEED.Out\"", "\"feed.Out\"", StringComparison.Ordinal));

        Assert.Equal("DSE108", d.Code);
        Assert.Equal("$.flows[0].from", d.Path);
        Assert.Contains("'FEED' is closest", d.Fix, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnknownPortListsThePortsThereAre()
    {
        ConfigDiagnostic d = Plants.Only(Plants.Minimal.Replace("\"CHUTE.In\"", "\"CHUTE.Inn\"", StringComparison.Ordinal));

        Assert.Equal("DSE108", d.Code);
        Assert.Equal("$.flows[0].to", d.Path);
        Assert.Contains("Full, In, Level, Out", d.Fix, StringComparison.Ordinal);
        Assert.Contains("'In' is closest", d.Fix, StringComparison.Ordinal);
    }

    [Fact]
    public void AnAddressWithoutADotIsNotAnAddress()
    {
        ConfigDiagnostic d = Plants.Only(Plants.Minimal.Replace("\"PILE.In\"", "\"PILE\"", StringComparison.Ordinal));

        Assert.Equal("DSE108", d.Code);
        Assert.Contains("<component>.<port>", d.Fix, StringComparison.Ordinal);
    }

    [Fact]
    public void BothEndsOfABadLinkAreReported()
    {
        string json = Plants.Minimal.Replace(
            "{ \"from\": \"CHUTE.Out\", \"to\": \"PILE.In\" }", "{ \"from\": \"CHUT.Out\", \"to\": \"PILE.Inn\" }", StringComparison.Ordinal);

        LoadResult result = Plants.Load(json);

        Assert.Equal(["$.flows[1].from", "$.flows[1].to"], result.Diagnostics.Select(d => d.Path));
    }

    [Fact]
    public void ASignalLinkCannotJoinDifferentValueTypes()
    {
        string json = Plants.Minimal.Replace(
            "\"flows\": [", "\"signals\": [ { \"from\": \"CHUTE.Full\", \"to\": \"FEED.Rate\" } ],\n  \"flows\": [", StringComparison.Ordinal);

        ConfigDiagnostic d = Plants.Only(json);

        Assert.Equal("DSE109", d.Code);
        Assert.Equal("$.signals[0]", d.Path);
        Assert.Contains("Boolean", d.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AMaterialLinkUnderSignalsIsSentToFlows()
    {
        string json = Plants.Minimal
            .Replace("{ \"from\": \"FEED.Out\", \"to\": \"CHUTE.In\" },", string.Empty, StringComparison.Ordinal)
            .Replace("\"flows\": [", "\"signals\": [ { \"from\": \"FEED.Out\", \"to\": \"CHUTE.In\" } ],\n  \"flows\": [", StringComparison.Ordinal);

        ConfigDiagnostic d = Assert.Single(Plants.Load(json).Diagnostics, x => x.Code == "DSE109");

        Assert.Contains("\"flows\"", d.Fix, StringComparison.Ordinal);
    }

    [Fact]
    public void ASignalLinkDrivesAnInput()
    {
        string json = Plants.Minimal.Replace(
            "\"flows\": [", "\"signals\": [ { \"from\": \"PILE.Full\", \"to\": \"FEED.Enabled\" } ],\n  \"flows\": [", StringComparison.Ordinal);

        // Wired the wrong way round on purpose — a full pile *enables* the feed — so the effect is unmistakable.
        Simulation simulation = Plants.Load(json).Builder!.Build();
        simulation.RunFor(TimeSpan.FromSeconds(5));

        Assert.Equal(0.0, simulation.IO.ReadDouble("PILE.Received"));
    }

    [Fact]
    public void AnExplicitTagBindsAndCanBeWritten()
    {
        string json = Plants.Minimal.Replace(
            "\"flows\": [",
            "\"tags\": [ { \"name\": \"AREA1.FEED_SP\", \"port\": \"FEED.Rate\", \"access\": \"write\", \"unit\": \"kg/s\", \"rangeLow\": 0, \"rangeHigh\": 50, \"description\": \"Feed rate setpoint\" } ],\n  \"flows\": [",
            StringComparison.Ordinal);

        LoadResult result = Plants.Load(json);
        Simulation simulation = result.Builder!.Build();
        TagDescriptor tag = simulation.IO.Directory.Find("AREA1.FEED_SP");

        Assert.Equal(1, result.Summary!.ExplicitTags);
        Assert.Equal(TagAccess.ReadWrite, tag.Access);
        Assert.Equal("kg/s", tag.Unit);
        Assert.Equal(50.0, tag.RangeHigh);
        Assert.Equal("Feed rate setpoint", tag.Description);
    }

    [Theory]
    [InlineData("FEED.Out", "read")]
    [InlineData("FEED.HopperMass", "write")]
    public void ATagThatCannotBindIsDse112(string port, string access)
    {
        string json = Plants.Minimal.Replace(
            "\"flows\": [", $"\"tags\": [ {{ \"name\": \"T\", \"port\": \"{port}\", \"access\": \"{access}\" }} ],\n  \"flows\": [", StringComparison.Ordinal);

        ConfigDiagnostic d = Plants.Only(json);

        Assert.Equal("DSE112", d.Code);
        Assert.Equal("$.tags[0]", d.Path);
    }

    [Fact]
    public void ALeafInsideACompositeCanBeAddressed()
    {
        string json = Corpus.Read("valid", "conveyor-line.json").Replace(
            "\"flows\": [", "\"tags\": [ { \"name\": \"CV001.MOTOR_AMPS_TRUE\", \"port\": \"CV001.Motor.Current\", \"unit\": \"A\" } ],\n  \"flows\": [", StringComparison.Ordinal);

        Simulation simulation = Plants.Load(json).Builder!.Build();

        Assert.Equal(TagAccess.ReadOnly, simulation.IO.Directory.Find("CV001.MOTOR_AMPS_TRUE").Access);
    }

    [Fact]
    public void CoreValidationPassesThroughSplitIntoMessageAndFix()
    {
        // 100 m/s with 0.5 m cells is 1 m per 10 ms tick: two cells per tick, which the belt's CFL check refuses.
        string json = Corpus.Read("invalid", "DSE006-belt-too-fast-for-its-cells.json");

        ConfigDiagnostic d = Plants.Only(json);

        Assert.Equal("DSE006", d.Code);
        Assert.Equal("$.components[2]", d.Path);
        Assert.EndsWith(".", d.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(d.Fix, d.Message, StringComparison.Ordinal);
        Assert.False(string.IsNullOrWhiteSpace(d.Fix));
    }
}
```

Run: `dotnet test tests/Dse.Configuration.Tests --nologo --filter FullyQualifiedName~WireAndBuildTests`
Expected: build FAILS — `Corpus` does not exist. (Step 5 creates it.)

- [ ] **Step 2: Add `Bindings` to the state**

In `LoadState.cs` add `using Dse.Core.Io;` and, to `LoadState`:

```csharp
    /// <summary>Explicit tags from the file, resolved to bindings by the wire stage.</summary>
    public List<(string Name, TagBinding Binding)> Bindings { get; } = [];
```

- [ ] **Step 3: Write the wire stage**

`src/Dse.Configuration/Loading/WireStage.cs`:

```csharp
using Dse.Core.Catalogue;
using Dse.Core.Flow;
using Dse.Core.Graph;
using Dse.Core.Io;
using Dse.Io;

namespace Dse.Configuration.Loading;

/// <summary>Stage 5: addresses become ports; ports get connected; tags get bound.</summary>
internal static class WireStage
{
    public static void Run(LoadState state)
    {
        var nodes = new Dictionary<string, ISimNode>(StringComparer.Ordinal);
        foreach (ComponentEntry entry in state.Components)
        {
            nodes[entry.Id] = entry.Node!;
            if (entry.Node is CompositeComponent composite)
            {
                foreach (ISimComponent leaf in composite.LeafComponents)
                {
                    nodes[leaf.Id] = leaf;
                }
            }
        }

        foreach (LinkEntry link in state.Signals)
        {
            Connect(state, nodes, link, flows: false);
        }

        foreach (LinkEntry link in state.Flows)
        {
            Connect(state, nodes, link, flows: true);
        }

        foreach (TagRequest tag in state.Tags)
        {
            Bind(state, nodes, tag);
        }
    }

    private static void Connect(LoadState state, Dictionary<string, ISimNode> nodes, LinkEntry link, bool flows)
    {
        Port? from = Resolve(state, nodes, link.From, $"{link.Path}.from");
        Port? to = Resolve(state, nodes, link.To, $"{link.Path}.to");
        if (from is null || to is null)
        {
            return;
        }

        Port? misplaced = new[] { from, to }.FirstOrDefault(p => p is FlowPort != flows);
        if (misplaced is not null)
        {
            state.Error(
                ConfigDiagnostics.CannotConnect,
                link.Path,
                flows
                    ? $"'{misplaced.QualifiedName}' carries a signal, but this link is under \"flows\"."
                    : $"'{misplaced.QualifiedName}' carries material, but this link is under \"signals\".",
                flows ? "Move the link to \"signals\", or name an inlet and an outlet." : "Move the link to \"flows\", or name an output and an input.");
            return;
        }

        if (!PortConnector.TryConnect(from, to, out string problem))
        {
            state.Error(
                ConfigDiagnostics.CannotConnect,
                link.Path,
                problem,
                flows
                    ? "A flow link runs from an outlet to an inlet of the same payload kind, and each end takes one link."
                    : "A signal link runs from an output to an input of the same value type, and an input takes one link.");
        }
    }

    private static void Bind(LoadState state, Dictionary<string, ISimNode> nodes, TagRequest tag)
    {
        Port? port = Resolve(state, nodes, tag.Port, $"{tag.Path}.port");
        if (port is null)
        {
            return;
        }

        try
        {
            TagBinding binding = TagBinding.ForPort(
                tag.Name, port, tag.Writable ? TagAccess.ReadWrite : TagAccess.ReadOnly, tag.Unit, tag.RangeLow, tag.RangeHigh, tag.Description);
            state.Bindings.Add((tag.Name, binding));
        }
        catch (ArgumentException ex)
        {
            int cut = ex.Message.IndexOf(" (Parameter '", StringComparison.Ordinal);
            state.Error(
                ConfigDiagnostics.TagCannotBind,
                tag.Path,
                cut < 0 ? ex.Message : ex.Message[..cut],
                "Bind a bool, double, int or long signal port, and ask for \"write\" only on an input.");
        }
    }

    private static Port? Resolve(LoadState state, Dictionary<string, ISimNode> nodes, string address, string path)
    {
        int dot = address.LastIndexOf('.');
        if (dot <= 0 || dot == address.Length - 1)
        {
            state.Error(
                ConfigDiagnostics.UnknownAddress,
                path,
                $"'{address}' is not a port address.",
                "Write it as <component>.<port>, such as CV001.Start; for a leaf inside a composite, CV001.Motor.Current.");
            return null;
        }

        string nodeId = address[..dot];
        string portName = address[(dot + 1)..];
        if (!nodes.TryGetValue(nodeId, out ISimNode? node))
        {
            state.Error(
                ConfigDiagnostics.UnknownAddress,
                path,
                $"'{nodeId}' is not a component in this plant (in the address '{address}').",
                Suggest.Fix(nodeId, state.Components.Select(c => c.Id), "components"));
            return null;
        }

        List<KeyValuePair<string, Port>> ports = node switch
        {
            CompositeComponent composite => composite.ExposedPorts.ToList(),
            ISimComponent leaf => leaf.Ports.Select(p => KeyValuePair.Create(p.Name, p)).ToList(),
            _ => [],
        };

        foreach (KeyValuePair<string, Port> candidate in ports)
        {
            if (string.Equals(candidate.Key, portName, StringComparison.OrdinalIgnoreCase))
            {
                return candidate.Value;
            }
        }

        state.Error(
            ConfigDiagnostics.UnknownAddress,
            path,
            $"'{nodeId}' has no port named '{portName}'.",
            Suggest.Fix(portName, ports.Select(p => p.Key), "ports"));
        return null;
    }
}
```

- [ ] **Step 4: Write the build stage**

`src/Dse.Configuration/Loading/BuildStage.cs`:

```csharp
using Dse.Core;
using Dse.Core.Io;
using Dse.Core.Time;
using Dse.Core.Validation;

namespace Dse.Configuration.Loading;

/// <summary>Stage 6: hand the plant to Core and pass its verdict through.</summary>
internal static class BuildStage
{
    public static void Run(LoadState state)
    {
        var fallback = new SimulationOptions();
        var options = new SimulationOptions
        {
            Seed = state.Options.Seed ?? state.Defaults.Seed ?? fallback.Seed,
            TimeStep = state.Options.TimeStep ?? state.Defaults.TimeStep ?? fallback.TimeStep,
            StartTime = state.Options.StartTime ?? state.Defaults.StartTime ?? fallback.StartTime,
        };

        var builder = new SimulationBuilder(options);
        foreach (ComponentEntry entry in state.Components)
        {
            // File order, not build order: the order a plant is added in is part of what makes a run reproducible.
            builder.Add(entry.Node!);
        }

        foreach ((string name, TagBinding binding) in state.Bindings)
        {
            builder.Bind(name, binding);
        }

        foreach (ValidationError error in builder.Validate().Errors)
        {
            (string message, string fix) = Split(error.Message);
            state.Error(error.Code, PathOf(state, error), message, fix);
        }

        state.Builder = builder;
        state.SimulationOptions = options;
    }

    /// <summary>Core messages read "symptom. fix." (R40).</summary>
    private static (string Message, string Fix) Split(string text)
    {
        int cut = text.IndexOf(". ", StringComparison.Ordinal);
        return cut < 0
            ? (text, "Correct the plant so that this check passes.")
            : (text[..(cut + 1)], text[(cut + 2)..]);
    }

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
        }

        return "$";
    }
}
```

In `PlantLoader.cs`:

```csharp
    private static readonly Action<LoadState>[] Stages =
    [
        StructureStage.Run,
        ReferenceStage.Run,
        InstantiateStage.Run,
        WireStage.Run,
        BuildStage.Run,
    ];
```

- [ ] **Step 5: Write the corpus**

Add to `Dse.Configuration.Tests.csproj`:

```xml
  <ItemGroup>
    <None Include="Plants\**\*.json" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
```

`tests/Dse.Configuration.Tests/CorpusTests.cs`:

```csharp
namespace Dse.Configuration.Tests;

/// <summary>The plant files under Plants/, copied beside the test assembly. Public because xUnit's MemberData reads it.</summary>
public static class Corpus
{
    private static string Root => Path.Combine(AppContext.BaseDirectory, "Plants");

    public static IEnumerable<object[]> Valid() => Names("valid");

    public static IEnumerable<object[]> Invalid() => Names("invalid");

    public static string Read(string kind, string name) => File.ReadAllText(Path.Combine(Root, kind, name));

    private static IEnumerable<object[]> Names(string kind) =>
        Directory.EnumerateFiles(Path.Combine(Root, kind), "*.json")
            .Select(Path.GetFileName)
            .Order(StringComparer.Ordinal)
            .Select(name => new object[] { name! });
}

public class CorpusTests
{
    [Theory]
    [MemberData(nameof(Corpus.Valid), MemberType = typeof(Corpus))]
    public void EveryValidPlantLoadsCleanAndBuilds(string name)
    {
        LoadResult result = Plants.Load(Corpus.Read("valid", name));

        Assert.True(result.IsValid, result.ToText());
        Assert.Empty(result.Diagnostics);
        result.Builder!.Build().RunFor(TimeSpan.FromSeconds(2));
    }

    [Theory]
    [MemberData(nameof(Corpus.Invalid), MemberType = typeof(Corpus))]
    public void EveryInvalidPlantYieldsExactlyTheCodeInItsName(string name)
    {
        string expected = name[..name.IndexOf('-', StringComparison.Ordinal)];

        LoadResult result = Plants.Load(Corpus.Read("invalid", name));

        Assert.False(result.IsValid);
        Assert.NotEmpty(result.Diagnostics);
        Assert.All(result.Diagnostics, d => Assert.Equal(expected, d.Code));
        Assert.Null(result.Builder);
    }

    [Fact]
    public void EveryConfigurationCodeHasAnInvalidPlant()
    {
        var covered = Corpus.Invalid().Select(row => ((string)row[0])[..6]).ToHashSet(StringComparer.Ordinal);

        Assert.Empty(ConfigDiagnostics.All.Select(d => d.Code).Where(code => !covered.Contains(code)));
    }
}
```

**Valid plants.** Strict JSON, no comments (Task 13 feeds these to a schema
validator that does not accept them).

`Plants/valid/minimal.json` — exactly the text of `Plants.Minimal` from Task 10.

`Plants/valid/conveyor-line.json` — the plant of `ConveyorRealtimeTests`, in the order that test adds it:

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
  ]
}
```

`Plants/valid/instrumented-belt.json` — a forward reference, a leaf belt, a writable setpoint tag:

```json
{
  "defaults": { "timeStepMs": 10 },
  "materials": [ { "name": "ore", "kind": "bulk", "properties": { "density": 1800, "moisture": 0.05, "temperature": 12 } } ],
  "components": [
    { "id": "WT", "type": "belt-scale",
      "parameters": { "belt": "BELT", "positionM": 5, "spec": { "unit": "kg/m", "rangeLow": 0, "rangeHigh": 100, "noiseSigma": 0.1 } } },
    { "id": "FEED", "type": "bulk-source", "parameters": { "material": "ore", "rateKgPerS": 20, "hopperCapacityKg": 5000 } },
    { "id": "BELT", "type": "bulk-belt",
      "parameters": { "lengthM": 10, "cellSizeM": 0.5, "maxSpeedMps": 2, "maxLinearDensityKgPerM": 100,
                      "transforms": [ { "type": "thermal-transfer", "timeConstantSeconds": 600 } ] } },
    { "id": "PILE", "type": "bulk-sink", "parameters": { "capacityKg": 100000 } }
  ],
  "flows": [ { "from": "FEED.Out", "to": "BELT.In" }, { "from": "BELT.Out", "to": "PILE.In" } ],
  "tags": [
    { "name": "BELT.SPEED_SP", "port": "BELT.Speed", "access": "write", "unit": "m/s", "rangeLow": 0, "rangeHigh": 2,
      "description": "Belt speed setpoint" }
  ]
}
```

`Plants/valid/item-line.json` — the discrete side: states, a nested hold, a transform that names a state, two instruments on two kinds of observable:

```json
{
  "defaults": { "timeStepMs": 10 },
  "materials": [
    { "name": "billet", "kind": "discrete", "states": [ "soak" ],
      "properties": { "density": 7800, "moisture": 0, "temperature": 20 }, "description": "A steel billet." }
  ],
  "components": [
    { "id": "SRC", "type": "item-source", "parameters": { "material": "billet", "itemMassKg": 12, "intervalSeconds": 5, "queueCapacity": 4 } },
    { "id": "RB", "type": "discrete-belt", "parameters": { "lengthM": 6, "maxSpeedMps": 1, "minSpacingM": 0.5 } },
    { "id": "FCE", "type": "item-process-unit", "parameters": {
        "batchSize": 4,
        "hold": { "type": "all", "conditions": [
          { "type": "for-seconds", "seconds": 60 },
          { "type": "state-at-least", "material": "billet", "state": "soak", "value": 30 } ] },
        "transforms": [
          { "type": "thermal-transfer", "timeConstantSeconds": 120 },
          { "type": "residence-accumulator", "material": "billet", "state": "soak", "thresholdTemperature": 700 } ] } },
    { "id": "BIN", "type": "item-sink" },
    { "id": "PC", "type": "part-counter", "parameters": { "belt": "RB", "positionM": 3, "windowM": 0.2 } },
    { "id": "PY", "type": "pyrometer",
      "parameters": { "target": "FCE", "positionM": 0, "windowM": 1, "spec": { "unit": "°C", "rangeLow": 0, "rangeHigh": 1300 } } }
  ],
  "flows": [ { "from": "SRC.Out", "to": "RB.In" }, { "from": "RB.Out", "to": "FCE.In" }, { "from": "FCE.Out", "to": "BIN.In" } ],
  "tags": [ { "name": "RB.SPEED_SP", "port": "RB.Speed", "access": "write", "unit": "m/s", "rangeLow": 0, "rangeHigh": 1 } ]
}
```

If Core validation rejects `item-line.json` or `instrumented-belt.json` for a
reason this plan did not foresee, **report the diagnostic**; then make the
smallest change to the file that respects the rule. Do not weaken the test.

**Invalid plants.** Each is `minimal.json` with one thing wrong, unless shown.
Create each by copying `minimal.json` and making the change:

| File | The change |
|---|---|
| `DSE100-unterminated-array.json` | the whole file is `{ "components": [ }` |
| `DSE101-unknown-component-key.json` | the `PILE` entry gains `"colour": "red"` |
| `DSE102-unknown-component-type.json` | `"bulk-sink"` → `"bulk-snik"` |
| `DSE103-capacity-out-of-range.json` | `"capacityKg": 200` → `"capacityKg": -1` |
| `DSE107-duplicate-id.json` | `"id": "CHUTE"` → `"id": "FEED"` |
| `DSE108-unknown-port.json` | `"FEED.Out"` → `"FEED.Otu"` |
| `DSE109-bool-into-double.json` | add `"signals": [ { "from": "CHUTE.Full", "to": "FEED.Rate" } ]` |
| `DSE112-tag-on-a-flow-port.json` | add `"tags": [ { "name": "T", "port": "FEED.Out" } ]` |

And four that need their own components — written out in full:

`DSE104-reference-to-nothing.json`, `DSE105-reference-to-a-source.json`, `DSE106-self-reference.json` — each is `instrumented-belt.json` with `"belt": "BELT"` changed to `"belt": "BLET"`, `"belt": "FEED"` and `"belt": "WT"` respectively.

`DSE110-unknown-state.json` — `item-line.json` with the hold's `"state": "soak"` changed to `"state": "sok"` (the hold's only; leave the transform's).

`DSE111-reset-above-trip.json`:

```json
{ "components": [ { "id": "MS", "type": "motor-starter", "parameters": { "tripLevel": 1.1, "resetLevel": 1.5 } } ] }
```

`DSE006-belt-too-fast-for-its-cells.json` — `instrumented-belt.json` with `"maxSpeedMps": 2` → `"maxSpeedMps": 100` and the tag's `"rangeHigh": 2` → `"rangeHigh": 100`.

That is 14 files and all 13 `DSE1xx` codes plus one pass-through.

- [ ] **Step 6: Run the wire, build and corpus tests**

Run: `dotnet test tests/Dse.Configuration.Tests --nologo`
Expected: PASS — 44 earlier cases, 15 wire-and-build cases (14 methods; the tag theory contributes 2), and 19 corpus cases (4 valid, 14 invalid, 1 coverage).

Two expectations to report rather than bend if they fail:
- `AnUnknownPortListsThePortsThereAre` expects the chute's ports to be exactly `Full, In, Level, Out`.
- `CoreValidationPassesThroughSplitIntoMessageAndFix` expects the CFL rule to carry code `DSE006` and its `ValidationError.ComponentIds` to name the belt, which is the third entry (`$.components[2]`).

- [ ] **Step 7: Write the round-trip test**

`tests/Dse.Configuration.Tests/RoundTripTests.cs`:

```csharp
using Dse.Components.Conveyors;
using Dse.Components.Flow;
using Dse.Components.Mechanical;
using Dse.Core;
using Dse.Core.Flow;
using Dse.Core.Time;
using Dse.Io;

namespace Dse.Configuration.Tests;

/// <summary>
/// The spec's proof that factories build what constructors build: the same
/// plant, once from JSON and once by hand, driven identically, must write the
/// same event log byte for byte.
/// </summary>
public class RoundTripTests
{
    private static Simulation ByHand()
    {
        var ore = new MaterialType("Ore", PayloadKind.Bulk);
        var feed = new BulkSource("Feed", ore, 20.0, new MaterialProperties(2000.0, 0.03, 15.0));
        var conveyor = new Conveyor("CV001", new ConveyorOptions(
            LengthM: 10.0, CellSizeM: 0.5, BeltWidthM: 0.8, AngleOfReposeDeg: 20.0, MaterialDensityKgM3: 2000.0,
            EmptyBeltMassKg: 250.0, FrictionCoefficient: 0.04, PulleyDiameterM: 0.5, GearRatio: 20.0,
            Motor: new MotorRating(750.0, 150.0, 2.0), TailDragN: 80.0));
        var chute = new TransferChute("Chute", capacityKg: 200.0);
        var pile = new BulkSink("Pile");
        feed.Out.ConnectTo(conveyor.Inlet("In"));
        conveyor.Outlet("Out").ConnectTo(chute.In);
        chute.Out.ConnectTo(pile.In);

        return new SimulationBuilder(new SimulationOptions
        {
            Seed = 1UL,
            StartTime = new DateTimeOffset(2026, 1, 1, 6, 0, 0, TimeSpan.Zero),
            TimeStep = TimeSpan.FromMilliseconds(10),
        }).Add(pile).Add(chute).Add(conveyor).Add(feed).Build();
    }

    private static Simulation FromJson() => Plants.Load(Corpus.Read("valid", "conveyor-line.json")).Builder!.Build();

    /// <summary>Reset the safety relay, start, run loaded, pull a key, run on.</summary>
    private static void Operate(Simulation simulation)
    {
        simulation.IO.WriteBool("CV001.SafetyReset", true);
        simulation.RunFor(TimeSpan.FromMilliseconds(500));
        simulation.IO.WriteBool("CV001.SafetyReset", false);
        simulation.IO.WriteBool("CV001.Start", true);
        simulation.RunFor(TimeSpan.FromSeconds(30));
        simulation.IO.WriteBool("CV001.PullKey1", true);
        simulation.RunFor(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void TheTagDirectoriesAreIdentical()
    {
        Assert.Equal(ByHand().IO.Directory.ToText(), FromJson().IO.Directory.ToText());
    }

    [Fact]
    public void TheEventLogsAreByteIdentical()
    {
        Simulation byHand = ByHand();
        Simulation fromJson = FromJson();

        Operate(byHand);
        Operate(fromJson);

        string expected = byHand.Events.ToText();
        Assert.Contains("WRITE", expected, StringComparison.Ordinal);
        Assert.True(expected.Split('\n').Length > 5, "The scenario should produce a real event log, not a near-empty one.");
        Assert.Equal(expected, fromJson.Events.ToText());
        Assert.Equal(byHand.IO.ReadDouble("Pile.Received"), fromJson.IO.ReadDouble("Pile.Received"));
        Assert.Equal(byHand.Telemetry.Read("CV001.Motor.ThermalState"), fromJson.Telemetry.Read("CV001.Motor.ThermalState"));
    }
}
```

Run: `dotnet test tests/Dse.Configuration.Tests --nologo --filter FullyQualifiedName~RoundTripTests`
Expected: PASS, 2 tests. A difference here is a real finding: a factory passing
the wrong argument, or a default that differs. Report the first differing line
of the two logs.

- [ ] **Step 8: Build Release, run everything, commit**

Run: `dotnet build Dse.sln -c Release --nologo` — expect `0 Warning(s)`, `0 Error(s)`.
Run: `dotnet test Dse.sln --nologo` — expect all green.

```bash
git add src/Dse.Configuration tests/Dse.Configuration.Tests
git commit -m "feat(configuration): wire, bind tags and build, with a fixture corpus and a byte-identical round trip"
```

---
### Task 13: The generated JSON Schema, and its agreement with the loader

The schema is for tools the loader never meets — an editor's completion, an
agent validating with an off-the-shelf library. It is generated from the same
descriptors and the same envelope parameter lists (`PlantSchemas`) the loader
binds with, and a test-only validator proves the two agree where they can and
documents where they cannot.

**Files:**
- Create: `src/Dse.Configuration/PlantSchema.cs`
- Modify: `tests/Dse.Configuration.Tests/Dse.Configuration.Tests.csproj` (+ JsonSchema.Net)
- Test: `tests/Dse.Configuration.Tests/PlantSchemaTests.cs`, `SchemaAgreementTests.cs`
- Create (generated, read, committed): `tests/Dse.Configuration.Tests/Golden/plant.schema.json`

**Interfaces:**
- Consumes: `ComponentCatalogue`, `ParameterDescriptor` (Task 2); `CatalogueJson.WriterOptions`, `CatalogueJson.Finish`, `CatalogueJson.Camel` (Task 9); `PlantSchemas` (Task 10); `Corpus`, `Golden` (Tasks 9, 12).
- Produces: `public static class PlantSchema { public const string Dialect = "https://json-schema.org/draft/2020-12/schema"; public static string Generate(ComponentCatalogue catalogue); }`

**Shape of the schema:**

```
{ "$schema": <Dialect>, "title": "DSE plant", "type": "object", "additionalProperties": false,
  "required": [ "components" ],
  "properties": { "$schema", "defaults", "materials", "components", "signals", "flows", "tags" },
  "$defs": {                                      — keys sorted ordinally
    "component.<type>":      { id, type: { const }, parameters }   — "parameters" required iff a parameter is
    "group.<Name>":          an object of parameters, declared once however often it is used
    "object.<slot>":         { "oneOf": [ { "$ref": "#/$defs/object.<slot>.<type>" } … ] }
    "object.<slot>.<type>":  { type: { const }, …parameters }
    "envelope.material" | "envelope.link" | "envelope.tag"
  } }
```

`components.items` is `{ "oneOf": [ { "$ref": "#/$defs/component.<type>" } … ] }`.
A `"type"` that names nothing matches no branch, so the instance is invalid —
the structural half of `DSE102`.

**What the schema cannot check**, by design (spec 3.5): that a reference names a
component, a material name a material, a state a state; duplicate ids;
addresses; anything a constructor decides. Those properties are plain strings
whose `description` says the loader checks them.

- [ ] **Step 1: Add the validator package and write the failing tests**

```bash
dotnet add tests/Dse.Configuration.Tests package JsonSchema.Net
```

This is the one test-only dependency the spec allows. Confirm the restored
package's licence is MIT and note its version in the report. It must not appear
in any project under `src/`.

`tests/Dse.Configuration.Tests/PlantSchemaTests.cs`:

```csharp
using System.Text.Json;
using Dse.Tests.Shared;

namespace Dse.Configuration.Tests;

public class PlantSchemaTests
{
    private static readonly string Text = PlantSchema.Generate(Plants.Catalogue);

    private static JsonElement Defs(JsonDocument document) => document.RootElement.GetProperty("$defs");

    [Fact]
    public void DeclaresItsDialectAndClosesTheRoot()
    {
        using JsonDocument document = JsonDocument.Parse(Text);
        JsonElement root = document.RootElement;

        Assert.Equal(PlantSchema.Dialect, root.GetProperty("$schema").GetString());
        Assert.False(root.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal("components", Assert.Single(root.GetProperty("required").EnumerateArray()).GetString());
        Assert.Equal(
            ["$schema", "defaults", "materials", "components", "signals", "flows", "tags"],
            root.GetProperty("properties").EnumerateObject().Select(p => p.Name));
    }

    [Fact]
    public void HasOneBranchPerComponentType()
    {
        using JsonDocument document = JsonDocument.Parse(Text);
        string[] branches = document.RootElement
            .GetProperty("properties").GetProperty("components").GetProperty("items").GetProperty("oneOf")
            .EnumerateArray().Select(b => b.GetProperty("$ref").GetString()!).ToArray();

        Assert.Equal(Plants.Catalogue.Components.Select(c => $"#/$defs/component.{c.Type}"), branches);
    }

    [Fact]
    public void AComponentBranchPinsItsTypeAndClosesItsParameters()
    {
        using JsonDocument document = JsonDocument.Parse(Text);
        JsonElement gearbox = Defs(document).GetProperty("component.gearbox");
        JsonElement parameters = gearbox.GetProperty("properties").GetProperty("parameters");

        Assert.Equal("gearbox", gearbox.GetProperty("properties").GetProperty("type").GetProperty("const").GetString());
        Assert.Equal(["id", "type", "parameters"], gearbox.GetProperty("required").EnumerateArray().Select(r => r.GetString()));
        Assert.False(gearbox.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal("ratio", Assert.Single(parameters.GetProperty("required").EnumerateArray()).GetString());
        Assert.False(parameters.GetProperty("additionalProperties").GetBoolean());

        JsonElement efficiency = parameters.GetProperty("properties").GetProperty("efficiency");
        Assert.Equal("number", efficiency.GetProperty("type").GetString());
        Assert.Equal(0.0, efficiency.GetProperty("exclusiveMinimum").GetDouble());
        Assert.Equal(1.0, efficiency.GetProperty("maximum").GetDouble());
        Assert.Equal(0.95, efficiency.GetProperty("default").GetDouble());
        Assert.False(efficiency.TryGetProperty("minimum", out _));
    }

    [Fact]
    public void AComponentWithNoRequiredParameterDoesNotRequireTheObject()
    {
        using JsonDocument document = JsonDocument.Parse(Text);
        JsonElement sink = Defs(document).GetProperty("component.bulk-sink");

        Assert.Equal(["id", "type"], sink.GetProperty("required").EnumerateArray().Select(r => r.GetString()));
    }

    [Fact]
    public void ASharedGroupIsDefinedOnceAndReferenced()
    {
        using JsonDocument document = JsonDocument.Parse(Text);
        JsonElement defs = Defs(document);

        string motorRef = defs.GetProperty("component.motor").GetProperty("properties").GetProperty("parameters")
            .GetProperty("properties").GetProperty("rating").GetProperty("$ref").GetString()!;
        string conveyorRef = defs.GetProperty("component.conveyor").GetProperty("properties").GetProperty("parameters")
            .GetProperty("properties").GetProperty("motor").GetProperty("$ref").GetString()!;

        Assert.Equal("#/$defs/group.MotorRating", motorRef);
        Assert.Equal(motorRef, conveyorRef);
        Assert.Equal(3, defs.GetProperty("group.MotorRating").GetProperty("required").GetArrayLength());
    }

    [Fact]
    public void ASlotIsAOneOfOverItsTypesAndATypePinsItsName()
    {
        using JsonDocument document = JsonDocument.Parse(Text);
        JsonElement defs = Defs(document);

        Assert.Equal(5, defs.GetProperty("object.hold").GetProperty("oneOf").GetArrayLength());
        Assert.Equal(3, defs.GetProperty("object.transform").GetProperty("oneOf").GetArrayLength());

        JsonElement all = defs.GetProperty("object.hold.all");
        Assert.Equal("all", all.GetProperty("properties").GetProperty("type").GetProperty("const").GetString());
        Assert.Equal(["type", "conditions"], all.GetProperty("required").EnumerateArray().Select(r => r.GetString()));
        JsonElement conditions = all.GetProperty("properties").GetProperty("conditions");
        Assert.Equal(1, conditions.GetProperty("minItems").GetInt32());
        Assert.Equal("#/$defs/object.hold", conditions.GetProperty("items").GetProperty("$ref").GetString());
    }

    [Fact]
    public void AReferenceIsAStringThatSaysTheLoaderChecksIt()
    {
        using JsonDocument document = JsonDocument.Parse(Text);
        JsonElement belt = Defs(document).GetProperty("component.belt-scale").GetProperty("properties").GetProperty("parameters")
            .GetProperty("properties").GetProperty("belt");

        Assert.Equal("string", belt.GetProperty("type").GetString());
        Assert.Contains("IMaterialObservable", belt.GetProperty("description").GetString(), StringComparison.Ordinal);
        Assert.Contains("loader", belt.GetProperty("description").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void DefsAreSortedAndTheTextIsDeterministic()
    {
        using JsonDocument document = JsonDocument.Parse(Text);
        string[] keys = Defs(document).EnumerateObject().Select(p => p.Name).ToArray();

        Assert.Equal(keys.Order(StringComparer.Ordinal), keys);
        Assert.Equal(Text, PlantSchema.Generate(Plants.Catalogue));
        Assert.DoesNotContain('\r', Text);
        Assert.EndsWith("}\n", Text, StringComparison.Ordinal);
    }

    [Fact]
    public void MatchesTheGoldenFile()
    {
        Golden.Assert("Golden/plant.schema.json", Text);
    }
}
```

`tests/Dse.Configuration.Tests/SchemaAgreementTests.cs`:

```csharp
using System.Text.Json.Nodes;
using Json.Schema;

namespace Dse.Configuration.Tests;

/// <summary>
/// The schema and the loader are two validators. For structure they must agree;
/// for meaning only the loader can speak. Both halves are asserted, so the
/// boundary between them is documented by a test rather than by hope.
/// </summary>
public class SchemaAgreementTests
{
    /// <summary>What an off-the-shelf validator can see: unknown keys, unknown component types, bad parameters.</summary>
    private static readonly string[] Structural = ["DSE101", "DSE102", "DSE103"];

    private static readonly JsonSchema Schema = JsonSchema.FromText(PlantSchema.Generate(Plants.Catalogue));

    private static bool Accepts(string plantJson) => Schema.Evaluate(JsonNode.Parse(plantJson)).IsValid;

    [Fact]
    public void TheSchemaIsItselfValidDraft202012()
    {
        JsonNode? schemaAsData = JsonNode.Parse(PlantSchema.Generate(Plants.Catalogue));

        Assert.True(MetaSchemas.Draft202012.Evaluate(schemaAsData).IsValid);
    }

    [Theory]
    [MemberData(nameof(Corpus.Valid), MemberType = typeof(Corpus))]
    public void TheSchemaAcceptsEveryPlantTheLoaderAccepts(string name)
    {
        Assert.True(Accepts(Corpus.Read("valid", name)), $"The schema rejected valid plant '{name}'.");
    }

    [Theory]
    [MemberData(nameof(Corpus.Invalid), MemberType = typeof(Corpus))]
    public void TheSchemaRejectsStructuralErrorsAndOnlyThose(string name)
    {
        string code = name[..6];
        if (code == "DSE100")
        {
            return; // Not JSON at all; there is nothing to hand a schema validator.
        }

        bool accepted = Accepts(Corpus.Read("invalid", name));

        if (Structural.Contains(code))
        {
            Assert.False(accepted, $"'{name}' has a structural error the schema should catch, but the schema accepted it.");
        }
        else
        {
            Assert.True(accepted, $"'{name}' has a semantic error only the loader can see, but the schema rejected it — it is stricter than the loader somewhere.");
        }
    }

    [Theory]
    [InlineData("\"ratio\": 20", "\"ratio\": \"20\"")]
    [InlineData("\"ratio\": 20", "\"ratio\": 0")]
    [InlineData("\"ratio\": 20", "\"ratoi\": 20")]
    [InlineData("\"type\": \"gearbox\"", "\"type\": \"gearbocks\"")]
    [InlineData("\"id\": \"GB\"", "\"id\": \"G B\"")]
    [InlineData("\"id\": \"GB\"", "\"id\": \"G.B\"")]
    [InlineData("\"id\": \"GB\", ", "")]
    public void BothValidatorsRejectTheSameStructuralMistakes(string from, string to)
    {
        const string Good = """{ "components": [ { "id": "GB", "type": "gearbox", "parameters": { "ratio": 20 } } ] }""";
        string bad = Good.Replace(from, to, StringComparison.Ordinal);

        Assert.True(Accepts(Good));
        Assert.True(Plants.Load(Good).IsValid);
        Assert.False(Accepts(bad));
        Assert.False(Plants.Load(bad).IsValid);
    }
}
```

If the restored JsonSchema.Net's `Evaluate` takes a `JsonElement` rather than a
`JsonNode` (its major versions differ), pass
`JsonDocument.Parse(text).RootElement` in `Accepts` and in the meta-schema test;
change nothing else.

Run: `dotnet test tests/Dse.Configuration.Tests --nologo --filter "FullyQualifiedName~PlantSchemaTests|FullyQualifiedName~SchemaAgreementTests"`
Expected: build FAILS — `PlantSchema` does not exist.

- [ ] **Step 2: Write `PlantSchema`**

`src/Dse.Configuration/PlantSchema.cs`:

```csharp
using System.Text.Json;
using Dse.Configuration.Loading;
using Dse.Core.Catalogue;

namespace Dse.Configuration;

/// <summary>
/// A JSON Schema (draft 2020-12) for plant files, generated from a catalogue.
/// Same catalogue, same bytes. It checks structure; the loader checks meaning.
/// </summary>
public static class PlantSchema
{
    public const string Dialect = "https://json-schema.org/draft/2020-12/schema";

    private const string LoaderChecks = " The schema cannot check that it exists; the loader does.";

    public static string Generate(ComponentCatalogue catalogue)
    {
        ArgumentNullException.ThrowIfNull(catalogue);

        // Every definition is collected first so that "$defs" can be written sorted.
        var defs = new SortedDictionary<string, Action<Utf8JsonWriter>>(StringComparer.Ordinal);
        foreach (ComponentDescriptor component in catalogue.Components)
        {
            defs[$"component.{component.Type}"] = w => WriteComponent(w, component);
            CollectGroups(component.Parameters, defs);
        }

        foreach (string slot in catalogue.Slots)
        {
            IReadOnlyList<ObjectDescriptor> types = catalogue.ObjectsIn(slot);
            defs[$"object.{slot}"] = w => WriteOneOf(w, types.Select(t => $"object.{slot}.{t.Type}"));
            foreach (ObjectDescriptor type in types)
            {
                defs[$"object.{slot}.{type.Type}"] = w => WriteParameterObject(w, type.Parameters, type.Description, typeConst: type.Type);
                CollectGroups(type.Parameters, defs);
            }
        }

        defs["envelope.material"] = w => WriteParameterObject(w, PlantSchemas.Material, "A material the plant's components can name.", typeConst: null);
        defs["envelope.link"] = w => WriteParameterObject(w, PlantSchemas.Link, "A connection from one port to another.", typeConst: null);
        defs["envelope.tag"] = w => WriteParameterObject(w, PlantSchemas.Tag, "A tag bound to a port, in addition to the tags components declare.", typeConst: null);
        CollectGroups(PlantSchemas.Material, defs);

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, CatalogueJson.WriterOptions))
        {
            writer.WriteStartObject();
            writer.WriteString("$schema", Dialect);
            writer.WriteString("title", "DSE plant");
            writer.WriteString("description", $"Generated from the catalogue of modules: {string.Join(", ", catalogue.Modules)}. Do not edit; run `dse schema export`.");
            writer.WriteString("type", "object");
            writer.WriteBoolean("additionalProperties", false);
            writer.WriteStartArray("required");
            writer.WriteStringValue("components");
            writer.WriteEndArray();

            writer.WriteStartObject("properties");
            writer.WriteStartObject("$schema");
            writer.WriteString("type", "string");
            writer.WriteEndObject();
            WriteDefaults(writer);
            WriteArrayOf(writer, "materials", "envelope.material");
            writer.WriteStartObject("components");
            writer.WriteString("type", "array");
            writer.WritePropertyName("items");
            WriteOneOf(writer, catalogue.Components.Select(c => $"component.{c.Type}"));
            writer.WriteEndObject();
            WriteArrayOf(writer, "signals", "envelope.link");
            WriteArrayOf(writer, "flows", "envelope.link");
            WriteArrayOf(writer, "tags", "envelope.tag");
            writer.WriteEndObject();

            writer.WriteStartObject("$defs");
            foreach (KeyValuePair<string, Action<Utf8JsonWriter>> def in defs)
            {
                writer.WritePropertyName(def.Key);
                def.Value(writer);
            }

            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return CatalogueJson.Finish(stream);
    }

    private static void CollectGroups(IReadOnlyList<ParameterDescriptor> parameters, SortedDictionary<string, Action<Utf8JsonWriter>> defs)
    {
        foreach (ParameterDescriptor parameter in parameters)
        {
            if (parameter.GroupName.Length == 0)
            {
                continue;
            }

            string key = $"group.{parameter.GroupName}";
            if (!defs.ContainsKey(key))
            {
                IReadOnlyList<ParameterDescriptor> children = parameter.Children;
                defs[key] = w => WriteParameterObject(w, children, description: null, typeConst: null);
                CollectGroups(children, defs);
            }
        }
    }

    private static void WriteDefaults(Utf8JsonWriter writer)
    {
        writer.WriteStartObject("defaults");
        writer.WriteString("description", "Simulation options used when nothing overrides them. A scenario or the command line may.");
        writer.WriteString("type", "object");
        writer.WriteBoolean("additionalProperties", false);
        writer.WriteStartObject("properties");

        writer.WriteStartObject("seed");
        writer.WriteString("description", "Master seed; every random stream derives from it.");
        writer.WriteString("type", "integer");
        writer.WriteNumber("minimum", 0);
        writer.WriteEndObject();

        writer.WriteStartObject("timeStepMs");
        writer.WriteString("description", "Simulation step in milliseconds.");
        writer.WriteString("type", "number");
        writer.WriteNumber("exclusiveMinimum", 0);
        writer.WriteEndObject();

        writer.WriteStartObject("startTime");
        writer.WriteString("description", "Simulation start, ISO 8601 with an offset.");
        writer.WriteString("type", "string");
        writer.WriteString("format", "date-time");
        writer.WriteEndObject();

        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    private static void WriteArrayOf(Utf8JsonWriter writer, string name, string def)
    {
        writer.WriteStartObject(name);
        writer.WriteString("type", "array");
        writer.WriteStartObject("items");
        writer.WriteString("$ref", $"#/$defs/{def}");
        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    private static void WriteOneOf(Utf8JsonWriter writer, IEnumerable<string> defs)
    {
        writer.WriteStartObject();
        writer.WriteStartArray("oneOf");
        foreach (string def in defs)
        {
            writer.WriteStartObject();
            writer.WriteString("$ref", $"#/$defs/{def}");
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteComponent(Utf8JsonWriter writer, ComponentDescriptor component)
    {
        bool parametersRequired = component.Parameters.Any(p => p.IsRequired);
        writer.WriteStartObject();
        writer.WriteString("description", component.Description);
        writer.WriteString("type", "object");
        writer.WriteBoolean("additionalProperties", false);
        writer.WriteStartArray("required");
        writer.WriteStringValue("id");
        writer.WriteStringValue("type");
        if (parametersRequired)
        {
            writer.WriteStringValue("parameters");
        }

        writer.WriteEndArray();
        writer.WriteStartObject("properties");

        writer.WriteStartObject("id");
        writer.WriteString("description", "Unique in the plant. No dot and no whitespace: a dot separates a component from its port in an address.");
        writer.WriteString("type", "string");
        writer.WriteString("pattern", "^[^.\\s]+$");
        writer.WriteEndObject();

        writer.WriteStartObject("type");
        writer.WriteString("const", component.Type);
        writer.WriteEndObject();

        writer.WritePropertyName("parameters");
        WriteParameterObject(writer, component.Parameters, description: null, typeConst: null);

        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    /// <summary>An object with one property per parameter. With <paramref name="typeConst"/>, also a required <c>"type"</c> pinned to it.</summary>
    private static void WriteParameterObject(Utf8JsonWriter writer, IReadOnlyList<ParameterDescriptor> parameters, string? description, string? typeConst)
    {
        writer.WriteStartObject();
        if (description is not null)
        {
            writer.WriteString("description", description);
        }

        writer.WriteString("type", "object");
        writer.WriteBoolean("additionalProperties", false);

        writer.WriteStartArray("required");
        if (typeConst is not null)
        {
            writer.WriteStringValue("type");
        }

        foreach (ParameterDescriptor parameter in parameters.Where(p => p.IsRequired))
        {
            writer.WriteStringValue(parameter.Name);
        }

        writer.WriteEndArray();

        writer.WriteStartObject("properties");
        if (typeConst is not null)
        {
            writer.WriteStartObject("type");
            writer.WriteString("const", typeConst);
            writer.WriteEndObject();
        }

        foreach (ParameterDescriptor parameter in parameters)
        {
            writer.WritePropertyName(parameter.Name);
            WriteParameter(writer, parameter);
        }

        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    private static void WriteParameter(Utf8JsonWriter writer, ParameterDescriptor parameter)
    {
        writer.WriteStartObject();
        switch (parameter.Kind)
        {
            case ParameterKind.Group:
                // A sibling of "$ref" is allowed in 2020-12, so the use-site description survives.
                writer.WriteString("description", parameter.Description);
                writer.WriteString("$ref", $"#/$defs/group.{parameter.GroupName}");
                break;

            case ParameterKind.Object:
                writer.WriteString("description", parameter.Description);
                writer.WriteString("$ref", $"#/$defs/object.{parameter.Slot}");
                break;

            case ParameterKind.GroupList:
                WriteList(writer, parameter, w => w.WriteString("$ref", $"#/$defs/group.{parameter.GroupName}"));
                break;

            case ParameterKind.ObjectList:
                WriteList(writer, parameter, w => w.WriteString("$ref", $"#/$defs/object.{parameter.Slot}"));
                break;

            case ParameterKind.StringList:
                WriteList(writer, parameter, w => w.WriteString("type", "string"));
                break;

            case ParameterKind.Double:
            case ParameterKind.Int:
                writer.WriteString("description", Describe(parameter));
                writer.WriteString("type", parameter.Kind == ParameterKind.Int ? "integer" : "number");
                WriteBounds(writer, parameter);
                break;

            case ParameterKind.Bool:
                writer.WriteString("description", parameter.Description);
                writer.WriteString("type", "boolean");
                break;

            case ParameterKind.String:
                writer.WriteString("description", parameter.Description);
                writer.WriteString("type", "string");
                break;

            case ParameterKind.Enum:
                writer.WriteString("description", parameter.Description);
                writer.WriteStartArray("enum");
                foreach (string value in parameter.AllowedValues)
                {
                    writer.WriteStringValue(value);
                }

                writer.WriteEndArray();
                break;

            case ParameterKind.Reference:
                writer.WriteString("description", $"{parameter.Description} The id of a component that supplies {parameter.Capability!.Name}.{LoaderChecks}");
                writer.WriteString("type", "string");
                break;

            case ParameterKind.Material:
                writer.WriteString(
                    "description",
                    $"{parameter.Description} The name of a {(parameter.Payload is { } kind ? CatalogueJson.Camel(kind.ToString()) + " " : string.Empty)}material.{LoaderChecks}");
                writer.WriteString("type", "string");
                break;

            case ParameterKind.MaterialState:
                writer.WriteString("description", $"{parameter.Description} The name of a state of the material named by '{parameter.MaterialParameter}'.{LoaderChecks}");
                writer.WriteString("type", "string");
                break;

            default:
                throw new InvalidOperationException($"Parameter kind {parameter.Kind} has no schema.");
        }

        switch (parameter.Default)
        {
            case double number:
                writer.WriteNumber("default", number);
                break;
            case long whole:
                writer.WriteNumber("default", whole);
                break;
            case bool flag:
                writer.WriteBoolean("default", flag);
                break;
            case string text:
                writer.WriteString("default", text);
                break;
        }

        writer.WriteEndObject();
    }

    private static void WriteList(Utf8JsonWriter writer, ParameterDescriptor parameter, Action<Utf8JsonWriter> items)
    {
        writer.WriteString("description", parameter.Description);
        writer.WriteString("type", "array");
        if (parameter.MinCount > 0)
        {
            writer.WriteNumber("minItems", parameter.MinCount);
        }

        writer.WriteStartObject("items");
        items(writer);
        writer.WriteEndObject();
    }

    private static void WriteBounds(Utf8JsonWriter writer, ParameterDescriptor parameter)
    {
        if (parameter.Minimum is { } min)
        {
            writer.WriteNumber(parameter.ExclusiveMinimum ? "exclusiveMinimum" : "minimum", min);
        }

        if (parameter.Maximum is { } max)
        {
            writer.WriteNumber(parameter.ExclusiveMaximum ? "exclusiveMaximum" : "maximum", max);
        }
    }

    private static string Describe(ParameterDescriptor parameter) =>
        parameter.Unit.Length == 0 ? parameter.Description : $"{parameter.Description} Unit: {parameter.Unit}.";
}
```

One place where the two validators would otherwise disagree: the loader's `Int`
is 32-bit and the schema's `integer` is unbounded. A value beyond 2³¹ is
rejected by the loader (`DSE103`) and accepted by the schema. That is the
semantic side of the boundary and needs no code; it is recorded here so nobody
"fixes" it with a `maximum` that makes every integer parameter noisy.

- [ ] **Step 3: Run the schema tests, then generate and read the golden file**

```bash
DSE_UPDATE_GOLDEN=1 dotnet test tests/Dse.Configuration.Tests --nologo --filter FullyQualifiedName~PlantSchemaTests.MatchesTheGoldenFile
```

Open `tests/Dse.Configuration.Tests/Golden/plant.schema.json` and check: the
`oneOf` under `components` has 29 branches; `$defs` holds `group.MotorRating`,
`group.InstrumentSpec`, `group.RecipeLine` and `group.MaterialProperties`
exactly once each; `component.safety-relay`'s `channels` is an `integer` with
`minimum` 1; no `\u` escapes.

Run: `dotnet test tests/Dse.Configuration.Tests --nologo`
Expected: PASS — 80 earlier cases; 9 schema cases; and agreement cases: 1
meta-schema, 4 valid, 14 invalid, 7 structural pairs.

If `TheSchemaRejectsStructuralErrorsAndOnlyThose` fails on a semantic file, the
schema is stricter than the loader: find the keyword that rejected it (evaluate
with `new EvaluationOptions { OutputFormat = OutputFormat.List }` and print the
failing entries) and report it. If it fails on a structural file, the schema is
looser: report the keyword that is missing. Either way fix the generator, not
the test.

- [ ] **Step 4: Build Release, run everything, commit**

Run: `dotnet build Dse.sln -c Release --nologo` — expect `0 Warning(s)`, `0 Error(s)`.
Run: `dotnet test Dse.sln --nologo` — expect all green.
Run: `grep -rn "JsonSchema" src/` — expect no output.

```bash
git add src/Dse.Configuration/PlantSchema.cs tests/Dse.Configuration.Tests
git commit -m "feat(configuration): generate the plant JSON Schema and prove it agrees with the loader"
```

---
### Task 14: `Dse.Cli` — four commands

A thin shell. Everything it does is one call into `Dse.Core` or
`Dse.Configuration`; what it owns is argument parsing, exit codes, which stream
gets what, and the text and JSON renderings.

**Files:**
- Create: `src/Dse.Cli/Dse.Cli.csproj`, `Program.cs`, `CliApp.cs`, `ExitCodes.cs`, `CommandLine.cs`, `CommandTable.cs`, `CliContext.cs`
- Create: `src/Dse.Cli/Commands/CatalogExport.cs`, `SchemaExport.cs`, `Validate.cs`, `Tags.cs`, `PlantFile.cs`
- Create: `tests/Dse.Cli.Tests/Dse.Cli.Tests.csproj`
- Test: `tests/Dse.Cli.Tests/Cli.cs`, `CommandLineTests.cs`, `ExportCommandTests.cs`, `ValidateCommandTests.cs`, `TagsCommandTests.cs`
- Create: `tests/Dse.Cli.Tests/Plants/minimal.json`, `Plants/broken.json`
- Modify: `Dse.sln`

**Interfaces:**
- Consumes: `CatalogueJson.Export`, `CatalogueJson.WriterOptions`, `CatalogueJson.Finish`, `CatalogueJson.Camel` (Task 9); `PlantSchema.Generate` (Task 13); `PlantLoader.Load`, `LoadOptions`, `LoadResult`, `ConfigDiagnostic` (Tasks 10–12); `ComponentsModule`.
- Produces:
  - `public static class CliApp { public static int Run(string[] args, TextWriter stdout, TextWriter stderr); }`
  - `public static class ExitCodes { Ok = 0, PlantInvalid = 1, Usage = 2, Unreadable = 3 }` (`const int`)
  - internal `ParsedCommandLine`, `CommandLine.Parse`, `CommandSpec`, `CommandTable.All`, `CliContext` — Task 15 extends `CliContext` construction to load assemblies.

**Behaviour** (spec 4):

| | stdout | stderr | exit |
|---|---|---|---|
| `dse`, `dse help`, `dse --help` | general help | | 0 |
| `dse <command> --help` | that command's help | | 0 |
| unknown command / option, missing or extra argument, bad option value | | the problem, then a pointer to `--help` | 2 |
| `catalog export`, `schema export` | the JSON (or nothing, with `--out`) | | 0 |
| `validate`, valid plant | summary (text or JSON) | | 0 |
| `validate`, invalid plant, `--format text` | | diagnostics, then a count line | 1 |
| `validate`, invalid plant, `--format json` | the JSON, `ok: false` | | 1 |
| `tags`, valid plant | the tag table (text or JSON) | | 0 |
| `tags`, invalid plant | exactly as `validate` | | 1 |
| plant file or `--out` path cannot be read / written | | `Cannot read '…': …` | 3 |

Options take `--name value` or `--name=value`. An option may appear once,
except `--assembly` (Task 15).

- [ ] **Step 1: Create the projects**

`src/Dse.Cli/Dse.Cli.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <AssemblyName>dse</AssemblyName>
    <RootNamespace>Dse.Cli</RootNamespace>
    <PackAsTool>true</PackAsTool>
    <ToolCommandName>dse</ToolCommandName>
    <PackageId>Dse.Cli</PackageId>
    <Version>0.1.0</Version>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\Dse.Core\Dse.Core.csproj" />
    <ProjectReference Include="..\Dse.Components\Dse.Components.csproj" />
    <ProjectReference Include="..\Dse.Configuration\Dse.Configuration.csproj" />
  </ItemGroup>
  <ItemGroup>
    <InternalsVisibleTo Include="Dse.Cli.Tests" />
  </ItemGroup>
</Project>
```

`tests/Dse.Cli.Tests/Dse.Cli.Tests.csproj` — as `Dse.Configuration.Tests.csproj`
from Task 10 (same packages, **without** JsonSchema.Net and without the
`Golden.cs` link), with this project reference and this content item:

```xml
  <ItemGroup>
    <None Include="Plants\**\*.json" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\Dse.Cli\Dse.Cli.csproj" />
  </ItemGroup>
```

```bash
dotnet sln Dse.sln add src/Dse.Cli/Dse.Cli.csproj --solution-folder src
dotnet sln Dse.sln add tests/Dse.Cli.Tests/Dse.Cli.Tests.csproj --solution-folder tests
```

`tests/Dse.Cli.Tests/Plants/minimal.json` — the text of
`tests/Dse.Configuration.Tests/Plants/valid/minimal.json`.

`tests/Dse.Cli.Tests/Plants/broken.json` — the same with `"bulk-sink"` →
`"bulk-snik"` and `"capacityKg": 200` → `"capacityKg": -1` (two errors).

- [ ] **Step 2: Write the failing tests**

`tests/Dse.Cli.Tests/Cli.cs`:

```csharp
namespace Dse.Cli.Tests;

/// <summary>Runs the CLI in-process and captures both streams.</summary>
internal sealed record CliRun(int ExitCode, string Out, string Err);

internal static class Cli
{
    public static string Plant(string name) => Path.Combine(AppContext.BaseDirectory, "Plants", name);

    public static CliRun Run(params string[] args)
    {
        using var stdout = new StringWriter { NewLine = "\n" };
        using var stderr = new StringWriter { NewLine = "\n" };
        int exit = CliApp.Run(args, stdout, stderr);
        return new CliRun(exit, stdout.ToString(), stderr.ToString());
    }
}
```

`tests/Dse.Cli.Tests/CommandLineTests.cs`:

```csharp
namespace Dse.Cli.Tests;

public class CommandLineTests
{
    [Fact]
    public void NoArgumentsPrintsGeneralHelp()
    {
        CliRun run = Cli.Run();

        Assert.Equal(ExitCodes.Ok, run.ExitCode);
        Assert.Contains("Commands:", run.Out, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("help")]
    [InlineData("--help")]
    [InlineData("-h")]
    public void HelpPrintsGeneralHelpAndSucceeds(string word)
    {
        CliRun run = Cli.Run(word);

        Assert.Equal(ExitCodes.Ok, run.ExitCode);
        Assert.Empty(run.Err);
        Assert.Contains("catalog export", run.Out, StringComparison.Ordinal);
        Assert.Contains("schema export", run.Out, StringComparison.Ordinal);
        Assert.Contains("validate <plant.json>", run.Out, StringComparison.Ordinal);
        Assert.Contains("tags <plant.json>", run.Out, StringComparison.Ordinal);
        Assert.Contains("Exit codes", run.Out, StringComparison.Ordinal);
    }

    [Fact]
    public void ACommandsHelpListsItsOptions()
    {
        CliRun run = Cli.Run("validate", "--help");

        Assert.Equal(ExitCodes.Ok, run.ExitCode);
        Assert.Contains("--format", run.Out, StringComparison.Ordinal);
        Assert.Contains("--time-step", run.Out, StringComparison.Ordinal);
        Assert.DoesNotContain("--out", run.Out, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("frobnicate")]
    [InlineData("catalog")]
    [InlineData("catalog import")]
    public void AnUnknownCommandIsAUsageError(string line)
    {
        CliRun run = Cli.Run(line.Split(' '));

        Assert.Equal(ExitCodes.Usage, run.ExitCode);
        Assert.Empty(run.Out);
        Assert.Contains("dse help", run.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnknownOptionNamesTheOnesTheCommandHas()
    {
        CliRun run = Cli.Run("validate", Cli.Plant("minimal.json"), "--fromat", "json");

        Assert.Equal(ExitCodes.Usage, run.ExitCode);
        Assert.Contains("--fromat", run.Err, StringComparison.Ordinal);
        Assert.Contains("--format", run.Err, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("validate")]
    [InlineData("validate a.json b.json")]
    [InlineData("catalog export extra")]
    [InlineData("validate a.json --format")]
    [InlineData("validate a.json --format xml")]
    [InlineData("validate a.json --format json --format text")]
    [InlineData("validate a.json --time-step fast")]
    [InlineData("validate a.json --time-step 0")]
    public void MalformedInvocationsAreUsageErrors(string line)
    {
        CliRun run = Cli.Run(line.Split(' '));

        Assert.Equal(ExitCodes.Usage, run.ExitCode);
        Assert.Empty(run.Out);
        Assert.NotEmpty(run.Err);
    }

    [Fact]
    public void AnOptionMayUseAnEqualsSign()
    {
        CliRun run = Cli.Run("validate", Cli.Plant("minimal.json"), "--format=json");

        Assert.Equal(ExitCodes.Ok, run.ExitCode);
        Assert.StartsWith("{", run.Out, StringComparison.Ordinal);
    }
}
```

`tests/Dse.Cli.Tests/ExportCommandTests.cs`:

```csharp
using System.Text.Json;
using Dse.Components;
using Dse.Configuration;
using Dse.Core.Catalogue;

namespace Dse.Cli.Tests;

public class ExportCommandTests
{
    private static readonly ComponentCatalogue Shipped = new CatalogueBuilder().Add<ComponentsModule>().Build();

    [Fact]
    public void CatalogExportPrintsExactlyTheLibrarysExport()
    {
        CliRun run = Cli.Run("catalog", "export");

        Assert.Equal(ExitCodes.Ok, run.ExitCode);
        Assert.Empty(run.Err);
        Assert.Equal(CatalogueJson.Export(Shipped), run.Out);
    }

    [Fact]
    public void SchemaExportPrintsExactlyTheLibrarysSchema()
    {
        CliRun run = Cli.Run("schema", "export");

        Assert.Equal(ExitCodes.Ok, run.ExitCode);
        Assert.Equal(PlantSchema.Generate(Shipped), run.Out);
    }

    [Fact]
    public void OutWritesTheFileAndKeepsStdoutEmpty()
    {
        string path = Path.Combine(Path.GetTempPath(), $"dse-cli-{Guid.NewGuid():N}.json");
        try
        {
            CliRun run = Cli.Run("schema", "export", "--out", path);

            Assert.Equal(ExitCodes.Ok, run.ExitCode);
            Assert.Empty(run.Out);
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
            Assert.Equal(PlantSchema.Dialect, document.RootElement.GetProperty("$schema").GetString());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void AnUnwritableOutPathIsExitThree()
    {
        CliRun run = Cli.Run("catalog", "export", "--out", Path.Combine(Path.GetTempPath(), "no-such-dir-dse", "x", "c.json"));

        Assert.Equal(ExitCodes.Unreadable, run.ExitCode);
        Assert.Contains("Cannot write", run.Err, StringComparison.Ordinal);
    }
}
```

`tests/Dse.Cli.Tests/ValidateCommandTests.cs`:

```csharp
using System.Text.Json;

namespace Dse.Cli.Tests;

public class ValidateCommandTests
{
    [Fact]
    public void AValidPlantPrintsASummary()
    {
        CliRun run = Cli.Run("validate", Cli.Plant("minimal.json"));

        Assert.Equal(ExitCodes.Ok, run.ExitCode);
        Assert.Empty(run.Err);
        Assert.StartsWith("OK  ", run.Out, StringComparison.Ordinal);
        Assert.Contains("minimal.json", run.Out, StringComparison.Ordinal);
        Assert.Matches(@"components\s+3\n", run.Out);
        Assert.Matches(@"leaves\s+3\n", run.Out);
        Assert.Matches(@"flow links\s+2\n", run.Out);
        Assert.Matches(@"time step\s+10 ms\n", run.Out);
    }

    [Fact]
    public void TimeStepOverridesTheFile()
    {
        CliRun run = Cli.Run("validate", Cli.Plant("minimal.json"), "--time-step", "2.5");

        Assert.Equal(ExitCodes.Ok, run.ExitCode);
        Assert.Matches(@"time step\s+2\.5 ms\n", run.Out);
    }

    [Fact]
    public void AnInvalidPlantPrintsEveryDiagnosticToStderrAndExitsOne()
    {
        CliRun run = Cli.Run("validate", Cli.Plant("broken.json"));

        Assert.Equal(ExitCodes.PlantInvalid, run.ExitCode);
        Assert.Empty(run.Out);
        Assert.Contains("DSE102 $.components[2].type", run.Err, StringComparison.Ordinal);
        Assert.Contains("DSE103 $.components[1].parameters.capacityKg", run.Err, StringComparison.Ordinal);
        Assert.Contains("  Fix: ", run.Err, StringComparison.Ordinal);
        Assert.EndsWith("2 errors in broken.json\n", run.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void JsonFormatPutsTheDiagnosticsOnStdout()
    {
        CliRun run = Cli.Run("validate", Cli.Plant("broken.json"), "--format", "json");

        Assert.Equal(ExitCodes.PlantInvalid, run.ExitCode);
        Assert.Empty(run.Err);
        using JsonDocument document = JsonDocument.Parse(run.Out);
        JsonElement root = document.RootElement;
        Assert.False(root.GetProperty("ok").GetBoolean());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("summary").ValueKind);
        Assert.Equal(2, root.GetProperty("diagnostics").GetArrayLength());
        JsonElement first = root.GetProperty("diagnostics")[0];
        Assert.Equal(["code", "severity", "path", "message", "fix"], first.EnumerateObject().Select(p => p.Name));
        Assert.Equal("error", first.GetProperty("severity").GetString());
    }

    [Fact]
    public void JsonFormatOfAValidPlantCarriesTheSummary()
    {
        CliRun run = Cli.Run("validate", Cli.Plant("minimal.json"), "--format", "json");

        Assert.Equal(ExitCodes.Ok, run.ExitCode);
        using JsonDocument document = JsonDocument.Parse(run.Out);
        JsonElement summary = document.RootElement.GetProperty("summary");
        Assert.True(document.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal(3, summary.GetProperty("components").GetInt32());
        Assert.Equal(3, summary.GetProperty("leaves").GetInt32());
        Assert.Equal(2, summary.GetProperty("flowLinks").GetInt32());
        Assert.Equal(10.0, summary.GetProperty("timeStepMs").GetDouble());
        Assert.True(summary.GetProperty("tags").GetInt32() > 0);
        Assert.Empty(document.RootElement.GetProperty("diagnostics").EnumerateArray());
    }

    [Fact]
    public void AMissingFileIsExitThree()
    {
        CliRun run = Cli.Run("validate", Cli.Plant("nope.json"));

        Assert.Equal(ExitCodes.Unreadable, run.ExitCode);
        Assert.Empty(run.Out);
        Assert.Contains("Cannot read", run.Err, StringComparison.Ordinal);
        Assert.Contains("nope.json", run.Err, StringComparison.Ordinal);
    }
}
```

`tests/Dse.Cli.Tests/TagsCommandTests.cs`:

```csharp
using System.Text.Json;

namespace Dse.Cli.Tests;

public class TagsCommandTests
{
    [Fact]
    public void ListsTheBuiltPlantsTagDirectory()
    {
        CliRun run = Cli.Run("tags", Cli.Plant("minimal.json"));

        Assert.Equal(ExitCodes.Ok, run.ExitCode);
        Assert.Empty(run.Err);
        Assert.Contains("FEED.Rate  Double  ReadWrite  kg/s", run.Out, StringComparison.Ordinal);
        Assert.Contains("PILE.Full  Bool  ReadOnly", run.Out, StringComparison.Ordinal);
    }

    [Fact]
    public void JsonFormatIsAnArrayOfDescriptors()
    {
        CliRun run = Cli.Run("tags", Cli.Plant("minimal.json"), "--format", "json");

        using JsonDocument document = JsonDocument.Parse(run.Out);
        JsonElement level = document.RootElement.EnumerateArray().Single(t => t.GetProperty("name").GetString() == "CHUTE.Level");
        Assert.Equal("double", level.GetProperty("kind").GetString());
        Assert.Equal("readOnly", level.GetProperty("access").GetString());
        Assert.Equal("fraction", level.GetProperty("unit").GetString());
        Assert.Equal(0.0, level.GetProperty("rangeLow").GetDouble());
        Assert.Equal(1.0, level.GetProperty("rangeHigh").GetDouble());

        JsonElement full = document.RootElement.EnumerateArray().Single(t => t.GetProperty("name").GetString() == "PILE.Full");
        Assert.False(full.TryGetProperty("rangeLow", out _));
    }

    [Fact]
    public void AnInvalidPlantBehavesAsValidateDoes()
    {
        CliRun run = Cli.Run("tags", Cli.Plant("broken.json"));

        Assert.Equal(ExitCodes.PlantInvalid, run.ExitCode);
        Assert.Empty(run.Out);
        Assert.Contains("DSE102", run.Err, StringComparison.Ordinal);
    }
}
```

Run: `dotnet test tests/Dse.Cli.Tests --nologo`
Expected: build FAILS — `CliApp`, `ExitCodes` do not exist.

- [ ] **Step 3: Write exit codes, the command table and the parser**

`src/Dse.Cli/ExitCodes.cs`:

```csharp
namespace Dse.Cli;

public static class ExitCodes
{
    public const int Ok = 0;

    /// <summary>The plant was read and has errors.</summary>
    public const int PlantInvalid = 1;

    /// <summary>The command line itself is wrong.</summary>
    public const int Usage = 2;

    /// <summary>A file or an assembly could not be read, written or loaded.</summary>
    public const int Unreadable = 3;
}
```

`src/Dse.Cli/CommandTable.cs`:

```csharp
namespace Dse.Cli;

/// <summary>An option a command accepts. Every option takes a value.</summary>
internal sealed record OptionSpec(string Name, string ValueName, string Help, bool Repeatable = false);

/// <summary>One command: how it is invoked, what it accepts, what runs it.</summary>
internal sealed record CommandSpec(
    string[] Words,
    string? Argument,
    string Summary,
    OptionSpec[] Options,
    Func<CliContext, int> Run)
{
    public string Invocation => string.Join(' ', Words) + (Argument is null ? string.Empty : $" <{Argument}>");
}

/// <summary>The single source of commands, options and help text.</summary>
internal static class CommandTable
{
    public static readonly OptionSpec Out = new("--out", "file", "Write the output to this file instead of standard output.");
    public static readonly OptionSpec Format = new("--format", "text|json", "How to print results. Default: text.");
    public static readonly OptionSpec TimeStep = new("--time-step", "ms", "Simulation step in milliseconds, overriding the plant's defaults.");

    public static IReadOnlyList<CommandSpec> All { get; } =
    [
        new(["catalog", "export"], null, "Print every component, transform, hold and material type as JSON.", [Out], Commands.CatalogExport.Run),
        new(["schema", "export"], null, "Print the JSON Schema for plant files, generated from the catalogue.", [Out], Commands.SchemaExport.Run),
        new(["validate"], "plant.json", "Load a plant and report every error, each with its fix.", [Format, TimeStep], Commands.Validate.Run),
        new(["tags"], "plant.json", "Load and build a plant, then list its tags: name, kind, access, unit, range.", [Format, TimeStep], Commands.Tags.Run),
    ];

    public static string GeneralHelp()
    {
        var lines = new List<string> { "dse — deterministic industrial process simulation engine", string.Empty, "Commands:" };
        int width = All.Max(c => c.Invocation.Length);
        foreach (CommandSpec command in All)
        {
            lines.Add($"  {command.Invocation.PadRight(width)}  {command.Summary}");
        }

        lines.Add(string.Empty);
        lines.Add("Run `dse <command> --help` for a command's options.");
        lines.Add(string.Empty);
        lines.Add("Exit codes: 0 success; 1 the plant has errors; 2 usage error; 3 a file or assembly could not be read.");
        return string.Join('\n', lines) + "\n";
    }

    public static string HelpFor(CommandSpec command)
    {
        var lines = new List<string> { $"dse {command.Invocation} [options]", string.Empty, command.Summary, string.Empty, "Options:" };
        int width = command.Options.Max(o => o.Name.Length + o.ValueName.Length + 3);
        foreach (OptionSpec option in command.Options)
        {
            lines.Add($"  {$"{option.Name} <{option.ValueName}>".PadRight(width)}  {option.Help}{(option.Repeatable ? " May be repeated." : string.Empty)}");
        }

        return string.Join('\n', lines) + "\n";
    }
}
```

`src/Dse.Cli/CommandLine.cs`:

```csharp
namespace Dse.Cli;

/// <summary>What a command line came to: a command with its argument and options, a request for help, or a usage error.</summary>
internal sealed class ParsedCommandLine
{
    public CommandSpec? Command { get; init; }

    public bool HelpRequested { get; init; }

    public string? Argument { get; init; }

    public Dictionary<string, List<string>> Options { get; } = new(StringComparer.Ordinal);

    /// <summary>Non-null means a usage error; nothing else is meaningful.</summary>
    public string? Error { get; init; }

    public string? Single(OptionSpec option) => Options.TryGetValue(option.Name, out List<string>? values) ? values[0] : null;

    public IReadOnlyList<string> All(OptionSpec option) => Options.TryGetValue(option.Name, out List<string>? values) ? values : [];
}

internal static class CommandLine
{
    private static readonly string[] HelpWords = ["help", "--help", "-h"];

    public static ParsedCommandLine Parse(string[] args)
    {
        if (args.Length == 0 || (args.Length == 1 && HelpWords.Contains(args[0], StringComparer.Ordinal)))
        {
            return new ParsedCommandLine { HelpRequested = true };
        }

        CommandSpec? command = CommandTable.All
            .Where(c => args.Length >= c.Words.Length && c.Words.SequenceEqual(args.Take(c.Words.Length), StringComparer.Ordinal))
            .OrderByDescending(c => c.Words.Length)
            .FirstOrDefault();
        if (command is null)
        {
            return Fail($"'{string.Join(' ', args.TakeWhile(a => !a.StartsWith('-')))}' is not a dse command.");
        }

        var parsed = new List<(string Name, string Value)>();
        var positionals = new List<string>();
        for (int i = command.Words.Length; i < args.Length; i++)
        {
            string arg = args[i];
            if (arg is "--help" or "-h")
            {
                return new ParsedCommandLine { Command = command, HelpRequested = true };
            }

            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                positionals.Add(arg);
                continue;
            }

            int equals = arg.IndexOf('=', StringComparison.Ordinal);
            string name = equals < 0 ? arg : arg[..equals];
            OptionSpec? option = command.Options.FirstOrDefault(o => string.Equals(o.Name, name, StringComparison.Ordinal));
            if (option is null)
            {
                return Fail($"'{name}' is not an option of `dse {string.Join(' ', command.Words)}`. It accepts: {string.Join(", ", command.Options.Select(o => o.Name))}.", command);
            }

            string value;
            if (equals >= 0)
            {
                value = arg[(equals + 1)..];
            }
            else if (i + 1 < args.Length)
            {
                value = args[++i];
            }
            else
            {
                return Fail($"'{name}' needs a value: {name} <{option.ValueName}>.", command);
            }

            if (!option.Repeatable && parsed.Any(p => p.Name == name))
            {
                return Fail($"'{name}' was given twice.", command);
            }

            parsed.Add((name, value));
        }

        int expected = command.Argument is null ? 0 : 1;
        if (positionals.Count != expected)
        {
            return Fail(
                expected == 0
                    ? $"`dse {string.Join(' ', command.Words)}` takes no argument, but got '{positionals[0]}'."
                    : positionals.Count == 0
                        ? $"`dse {string.Join(' ', command.Words)}` needs a <{command.Argument}>."
                        : $"`dse {string.Join(' ', command.Words)}` takes one <{command.Argument}>, but got {positionals.Count}.",
                command);
        }

        var result = new ParsedCommandLine { Command = command, Argument = positionals.FirstOrDefault() };
        foreach ((string name, string value) in parsed)
        {
            if (!result.Options.TryGetValue(name, out List<string>? values))
            {
                result.Options[name] = values = [];
            }

            values.Add(value);
        }

        return result;
    }

    private static ParsedCommandLine Fail(string problem, CommandSpec? command = null) => new()
    {
        Error = command is null
            ? $"{problem}\nRun `dse help` to list the commands."
            : $"{problem}\nRun `dse {string.Join(' ', command.Words)} --help` for its options.",
    };
}
```

- [ ] **Step 4: Write the context, the app and the entry point**

`src/Dse.Cli/CliContext.cs`:

```csharp
using System.Globalization;
using Dse.Core.Catalogue;

namespace Dse.Cli;

/// <summary>What a command runs with.</summary>
internal sealed class CliContext(ParsedCommandLine commandLine, ComponentCatalogue catalogue, TextWriter stdout, TextWriter stderr)
{
    public ParsedCommandLine CommandLine { get; } = commandLine;

    public ComponentCatalogue Catalogue { get; } = catalogue;

    public TextWriter Out { get; } = stdout;

    public TextWriter Err { get; } = stderr;

    public bool Json => CommandLine.Single(CommandTable.Format) == "json";

    public TimeSpan? TimeStep =>
        CommandLine.Single(CommandTable.TimeStep) is { } text
            ? TimeSpan.FromMilliseconds(double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture))
            : null;

    /// <summary>Writes a payload to <c>--out</c> or to standard output. Returns an exit code.</summary>
    public int Emit(string payload)
    {
        string? path = CommandLine.Single(CommandTable.Out);
        if (path is null)
        {
            Out.Write(payload);
            return ExitCodes.Ok;
        }

        try
        {
            File.WriteAllText(path, payload);
            return ExitCodes.Ok;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            Err.Write($"Cannot write '{path}': {ex.Message}\n");
            return ExitCodes.Unreadable;
        }
    }
}
```

`src/Dse.Cli/CliApp.cs`:

```csharp
using System.Globalization;
using Dse.Components;
using Dse.Core.Catalogue;

namespace Dse.Cli;

/// <summary>The whole CLI as a function of its arguments and two streams, so tests run it in-process.</summary>
public static class CliApp
{
    public static int Run(string[] args, TextWriter stdout, TextWriter stderr)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(stdout);
        ArgumentNullException.ThrowIfNull(stderr);

        ParsedCommandLine parsed = CommandLine.Parse(args);
        if (parsed.Error is not null)
        {
            stderr.Write(parsed.Error + "\n");
            return ExitCodes.Usage;
        }

        if (parsed.HelpRequested)
        {
            stdout.Write(parsed.Command is null ? CommandTable.GeneralHelp() : CommandTable.HelpFor(parsed.Command));
            return ExitCodes.Ok;
        }

        if (OptionValueProblem(parsed) is { } problem)
        {
            stderr.Write($"{problem}\nRun `dse {string.Join(' ', parsed.Command!.Words)} --help` for its options.\n");
            return ExitCodes.Usage;
        }

        ComponentCatalogue catalogue = new CatalogueBuilder().Add<ComponentsModule>().Build();
        return parsed.Command!.Run(new CliContext(parsed, catalogue, stdout, stderr));
    }

    private static string? OptionValueProblem(ParsedCommandLine parsed)
    {
        if (parsed.Single(CommandTable.Format) is { } format && format is not ("text" or "json"))
        {
            return $"'--format {format}' is not a format. Use text or json.";
        }

        if (parsed.Single(CommandTable.TimeStep) is { } step
            && !(double.TryParse(step, NumberStyles.Float, CultureInfo.InvariantCulture, out double ms) && double.IsFinite(ms) && ms > 0.0))
        {
            return $"'--time-step {step}' is not a step. Give milliseconds greater than zero, such as 10 or 0.5.";
        }

        return null;
    }
}
```

`src/Dse.Cli/Program.cs`:

```csharp
return Dse.Cli.CliApp.Run(args, Console.Out, Console.Error);
```

- [ ] **Step 5: Write the commands**

`src/Dse.Cli/Commands/CatalogExport.cs`:

```csharp
using Dse.Core.Catalogue;

namespace Dse.Cli.Commands;

internal static class CatalogExport
{
    public static int Run(CliContext context) => context.Emit(CatalogueJson.Export(context.Catalogue));
}
```

`src/Dse.Cli/Commands/SchemaExport.cs`:

```csharp
using Dse.Configuration;

namespace Dse.Cli.Commands;

internal static class SchemaExport
{
    public static int Run(CliContext context) => context.Emit(PlantSchema.Generate(context.Catalogue));
}
```

`src/Dse.Cli/Commands/PlantFile.cs` — what `validate` and `tags` share:

```csharp
using System.Globalization;
using System.Text.Json;
using Dse.Configuration;
using Dse.Core;
using Dse.Core.Catalogue;

namespace Dse.Cli.Commands;

/// <summary>Reads, loads and builds the plant a command was given; reports failure the same way for every command.</summary>
internal static class PlantFile
{
    /// <summary>Exit code 0 with a loaded plant and its built simulation, or a non-zero code after reporting why.</summary>
    public static int TryBuild(CliContext context, out LoadResult? result, out Simulation? simulation)
    {
        result = null;
        simulation = null;
        string path = context.CommandLine.Argument!;
        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            context.Err.Write($"Cannot read '{path}': {ex.Message}\n");
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

    private static void ReportInvalid(CliContext context, string path, LoadResult result)
    {
        if (context.Json)
        {
            context.Out.Write(ValidationJson(path, result, summary: null));
            return;
        }

        int errors = result.Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error);
        context.Err.Write(result.ToText());
        context.Err.Write(string.Create(
            CultureInfo.InvariantCulture, $"\n{errors} error{(errors == 1 ? string.Empty : "s")} in {Path.GetFileName(path)}\n"));
    }

    /// <summary>The <c>validate --format json</c> document. <paramref name="summary"/> writes the summary object's members, or is null.</summary>
    public static string ValidationJson(string path, LoadResult result, Action<Utf8JsonWriter>? summary)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, CatalogueJson.WriterOptions))
        {
            writer.WriteStartObject();
            writer.WriteBoolean("ok", result.IsValid);
            writer.WriteString("file", path);
            if (summary is null)
            {
                writer.WriteNull("summary");
            }
            else
            {
                writer.WriteStartObject("summary");
                summary(writer);
                writer.WriteEndObject();
            }

            writer.WriteStartArray("diagnostics");
            foreach (ConfigDiagnostic diagnostic in result.Diagnostics)
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

`src/Dse.Cli/Commands/Validate.cs`:

```csharp
using System.Globalization;
using Dse.Configuration;
using Dse.Core;

namespace Dse.Cli.Commands;

internal static class Validate
{
    public static int Run(CliContext context)
    {
        int exit = PlantFile.TryBuild(context, out LoadResult? result, out Simulation? simulation);
        if (exit != ExitCodes.Ok)
        {
            return exit;
        }

        PlantSummary summary = result!.Summary!;
        int leaves = simulation!.Components.Count;
        int tags = simulation.IO.Directory.Count;
        double stepMs = result.Options!.TimeStep.TotalMilliseconds;
        string path = context.CommandLine.Argument!;

        if (context.Json)
        {
            context.Out.Write(PlantFile.ValidationJson(path, result, w =>
            {
                w.WriteNumber("components", summary.Components);
                w.WriteNumber("leaves", leaves);
                w.WriteNumber("signalLinks", summary.SignalLinks);
                w.WriteNumber("flowLinks", summary.FlowLinks);
                w.WriteNumber("tags", tags);
                w.WriteNumber("explicitTags", summary.ExplicitTags);
                w.WriteNumber("timeStepMs", stepMs);
            }));
            return ExitCodes.Ok;
        }

        context.Out.Write(string.Create(CultureInfo.InvariantCulture, $"""
            OK  {path}
              components    {summary.Components}
              leaves        {leaves}
              signal links  {summary.SignalLinks}
              flow links    {summary.FlowLinks}
              tags          {tags} ({summary.ExplicitTags} explicit)
              time step     {stepMs} ms

            """).ReplaceLineEndings("\n"));
        return ExitCodes.Ok;
    }
}
```

`src/Dse.Cli/Commands/Tags.cs`:

```csharp
using System.Text.Json;
using Dse.Configuration;
using Dse.Core;
using Dse.Core.Catalogue;
using Dse.Io;

namespace Dse.Cli.Commands;

internal static class Tags
{
    public static int Run(CliContext context)
    {
        int exit = PlantFile.TryBuild(context, out LoadResult? _, out Simulation? simulation);
        if (exit != ExitCodes.Ok)
        {
            return exit;
        }

        if (!context.Json)
        {
            context.Out.Write(simulation!.IO.Directory.ToText().ReplaceLineEndings("\n"));
            return ExitCodes.Ok;
        }

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, CatalogueJson.WriterOptions))
        {
            writer.WriteStartArray();
            foreach (TagDescriptor tag in simulation!.IO.Directory.Tags)
            {
                writer.WriteStartObject();
                writer.WriteString("name", tag.Name);
                writer.WriteString("kind", CatalogueJson.Camel(tag.Kind.ToString()));
                writer.WriteString("access", CatalogueJson.Camel(tag.Access.ToString()));
                writer.WriteString("unit", tag.Unit);
                if (tag.HasRange)
                {
                    writer.WriteNumber("rangeLow", tag.RangeLow);
                    writer.WriteNumber("rangeHigh", tag.RangeHigh);
                }

                writer.WriteString("description", tag.Description);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        context.Out.Write(CatalogueJson.Finish(stream));
        return ExitCodes.Ok;
    }
}
```

- [ ] **Step 6: Run the tests**

Run: `dotnet test tests/Dse.Cli.Tests --nologo`
Expected: PASS — 18 command-line cases (7 methods: theories of 3, 3 and 8), 4 export, 6 validate, 3 tags.

Things to report rather than bend:
- `leaves 3` for the minimal plant assumes `Simulation.Components` lists flattened leaves and the minimal plant has three; say what it has.
- `ListsTheBuiltPlantsTagDirectory` expects the exact `TagDirectory.ToText()` column layout (two spaces between columns). If the declared tag on `FEED.Rate` prints differently, quote the line.

- [ ] **Step 7: Run it for real**

```bash
dotnet run --project src/Dse.Cli -- help
dotnet run --project src/Dse.Cli -- validate tests/Dse.Cli.Tests/Plants/minimal.json
dotnet run --project src/Dse.Cli -- validate tests/Dse.Cli.Tests/Plants/broken.json; echo "exit $?"
dotnet run --project src/Dse.Cli -- tags tests/Dse.Configuration.Tests/Plants/valid/conveyor-line.json
```

Expected: help text; an `OK` block; two diagnostics with fixes and `exit 1`; the
conveyor's tag table, `CV001.Start` first among the `CV001.` writable ones.
Paste the third command's output into the report.

- [ ] **Step 8: Build Release, run everything, commit**

Run: `dotnet build Dse.sln -c Release --nologo` — expect `0 Warning(s)`, `0 Error(s)`.
Run: `dotnet test Dse.sln --nologo` — expect all green.

```bash
git add Dse.sln src/Dse.Cli tests/Dse.Cli.Tests
git commit -m "feat(cli): add dse with catalog export, schema export, validate and tags"
```

---
### Task 15: `--assembly` and the sample module

A catalogue nobody can extend from the command line is a catalogue of exactly
the shipped types. `--assembly <path>` loads more modules; a fixture project
proves it with a real plugin DLL that the test project does **not** reference.

**Files:**
- Create: `src/Dse.Cli/ModuleLoader.cs`
- Modify: `src/Dse.Cli/CommandTable.cs` (the option, on every command), `src/Dse.Cli/CliApp.cs` (build the catalogue through the loader)
- Create: `tests/Dse.Cli.Tests.SampleModule/Dse.Cli.Tests.SampleModule.csproj`, `HysteresisSwitch.cs`, `SampleCatalogueModule.cs`
- Create: `tests/Dse.Cli.Tests.ClashModule/Dse.Cli.Tests.ClashModule.csproj`, `ClashCatalogueModule.cs`
- Modify: `tests/Dse.Cli.Tests/Dse.Cli.Tests.csproj` (build-order-only references)
- Create: `tests/Dse.Cli.Tests/Plants/sample.json`
- Test: `tests/Dse.Cli.Tests/PluginTests.cs`
- Modify: `Dse.sln`

**Interfaces:**
- Consumes: `CatalogueBuilder`, `ICatalogueModule` (Task 2); `CatalogueConformance` (Task 4); `CliApp`, `CommandTable`, `ExitCodes` (Task 14).
- Produces:
  - `internal static class ModuleLoader { public static bool TryLoad(IReadOnlyList<string> paths, CatalogueBuilder builder, out string problem); }`
  - `CommandTable.Assembly` — `new OptionSpec("--assembly", "path", "…", Repeatable: true)`, accepted by all four commands
  - the fixture plugin: module name `Sample`, component type `hysteresis-switch`, material `sample-ore`. Task 16's authoring recipe links to `HysteresisSwitch.cs` as its worked example, so write it as an example: complete, commented where a newcomer would ask why.

**Loading rules** (spec 4.3, with one refinement): each `--assembly` gets its own
non-collectible `AssemblyLoadContext` with an `AssemblyDependencyResolver` for
that plugin's directory (the spec said one shared context; a resolver is
per-plugin, so the context is too). The context declines any assembly the host
already ships beside `dse` — every `Dse.*` assembly above all — so that
`ICatalogueModule` is one type on both sides. Every public, concrete
`ICatalogueModule` with a public parameterless constructor is added. All
failures are exit code 3 with one line on stderr:

| Failure | Message starts |
|---|---|
| file missing | `Cannot load assembly '…': the file does not exist.` |
| not a .NET assembly, or unloadable | `Cannot load assembly '…': ` + the runtime's message |
| no module inside | `Assembly '…' contains no catalogue module.` |
| a module's registration throws (duplicate type, bad name) | `Module '…' from '…' could not be registered: ` + the catalogue's message |

- [ ] **Step 1: Write the two fixture projects**

`tests/Dse.Cli.Tests.SampleModule/Dse.Cli.Tests.SampleModule.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\Dse.Core\Dse.Core.csproj" />
  </ItemGroup>
</Project>
```

`tests/Dse.Cli.Tests.SampleModule/HysteresisSwitch.cs`:

```csharp
using Dse.Core.Catalogue;
using Dse.Core.Contexts;
using Dse.Core.Graph;
using Dse.Core.Io;
using Dse.Io;

namespace Dse.Cli.Tests.SampleModule;

/// <summary>
/// A switch with hysteresis: on above one level, off below a lower one, holding
/// in between. The worked example of docs/authoring-a-component.md — every
/// section of that recipe points at a part of this file.
/// </summary>
public sealed class HysteresisSwitch : ComponentBase, ITagProvider
{
    // 1. The descriptor sits beside the constructor it must match. The catalogue
    //    conformance test builds one of these from it and compares the two.
    public static ComponentDescriptor Descriptor { get; } = new(
        "hysteresis-switch",
        ComponentCategory.Signal,
        "Switches on above one level and off below a lower one, holding its state in between.",
        (id, p) => new HysteresisSwitch(id, p.Double("onAbove"), p.Double("offBelow")))
    {
        Parameters =
        [
            Param.Double("onAbove", "The output turns on when the value rises above this."),
            Param.Double("offBelow", "The output turns off when the value falls below this. Must be below onAbove."),
        ],
        Ports =
        [
            PortSpec.In<double>("Value", description: "The value watched."),
            PortSpec.Out<bool>("On"),
        ],
        Tags = [new TagEntry("On", TagKind.Bool, TagAccess.ReadOnly)],
    };

    private readonly double _onAbove;
    private readonly double _offBelow;

    // 2. All state is instance state. No statics, no clock, no System.Random:
    //    two simulations in one process must not see each other.
    private bool _on;

    public HysteresisSwitch(string id, double onAbove, double offBelow)
        : base(id)
    {
        // 3. A rule across two parameters cannot be a parameter range, so the
        //    constructor enforces it. A plant file that breaks it gets DSE111
        //    carrying this message — so the message must say what to change.
        if (offBelow >= onAbove)
        {
            throw new ArgumentException("offBelow must be below onAbove; otherwise the switch has no band to hold in.", nameof(offBelow));
        }

        _onAbove = onAbove;
        _offBelow = offBelow;

        // 4. Ports are declared once, in the constructor. The graph is immutable after Build().
        Value = AddInput<double>("Value");
        On = AddOutput<bool>("On");
    }

    public InputPort<double> Value { get; }

    public OutputPort<bool> On { get; }

    // 5. Evaluate reads inputs and writes outputs for one tick. It never reaches
    //    for another component: everything it knows arrives on a port.
    public override void Evaluate(in TickContext ctx)
    {
        if (Value.Value > _onAbove)
        {
            _on = true;
        }
        else if (Value.Value < _offBelow)
        {
            _on = false;
        }

        On.Value = _on;
    }

    // 6. Tags are what a plant would really have on the wire. Names are relative; the builder prefixes the id.
    public IEnumerable<TagBinding> DescribeTags() => [TagBinding.Read("On", On, "Switch state")];
}
```

`tests/Dse.Cli.Tests.SampleModule/SampleCatalogueModule.cs` (the class is not called `SampleModule`: a type named like its own namespace invites `CS0118`):

```csharp
using Dse.Core.Catalogue;
using Dse.Core.Flow;

namespace Dse.Cli.Tests.SampleModule;

/// <summary>What `dse --assembly` looks for: public, concrete, parameterless.</summary>
public sealed class SampleCatalogueModule : ICatalogueModule
{
    public string Name => "Sample";

    public void Register(CatalogueBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Add(HysteresisSwitch.Descriptor);
        builder.Add(new MaterialDescriptor(
            new MaterialType("sample-ore", PayloadKind.Bulk), new MaterialProperties(1600.0, 0.08, 10.0), "A material that ships with a module."));
    }
}
```

`tests/Dse.Cli.Tests.ClashModule/Dse.Cli.Tests.ClashModule.csproj` — identical
to the sample module's csproj.

`tests/Dse.Cli.Tests.ClashModule/ClashCatalogueModule.cs`:

```csharp
using Dse.Core.Catalogue;
using Dse.Core.Graph;

namespace Dse.Cli.Tests.ClashModule;

/// <summary>Registers a type name the shipped catalogue already has, to prove the clash is reported and not swallowed.</summary>
public sealed class ClashCatalogueModule : ICatalogueModule
{
    public string Name => "Clash";

    public void Register(CatalogueBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Add(new ComponentDescriptor("gearbox", ComponentCategory.Mechanical, "Not the real one.", (id, p) => new UnitDelay<bool>(id)));
    }
}
```

In `tests/Dse.Cli.Tests/Dse.Cli.Tests.csproj` add references that order the
build but do **not** reference the assemblies — the point is to load them as
strangers:

```xml
  <ItemGroup>
    <ProjectReference Include="..\Dse.Cli.Tests.SampleModule\Dse.Cli.Tests.SampleModule.csproj" ReferenceOutputAssembly="false" Private="false" />
    <ProjectReference Include="..\Dse.Cli.Tests.ClashModule\Dse.Cli.Tests.ClashModule.csproj" ReferenceOutputAssembly="false" Private="false" />
  </ItemGroup>
```

```bash
dotnet sln Dse.sln add tests/Dse.Cli.Tests.SampleModule/Dse.Cli.Tests.SampleModule.csproj --solution-folder tests
dotnet sln Dse.sln add tests/Dse.Cli.Tests.ClashModule/Dse.Cli.Tests.ClashModule.csproj --solution-folder tests
```

`tests/Dse.Cli.Tests/Plants/sample.json`:

```json
{
  "components": [
    { "id": "HS", "type": "hysteresis-switch", "parameters": { "onAbove": 80, "offBelow": 60 } },
    { "id": "SRC", "type": "bulk-source", "parameters": { "material": "sample-ore", "rateKgPerS": 1 } },
    { "id": "PILE", "type": "bulk-sink" }
  ],
  "flows": [ { "from": "SRC.Out", "to": "PILE.In" } ]
}
```

- [ ] **Step 2: Write the failing tests**

`tests/Dse.Cli.Tests/PluginTests.cs`:

```csharp
using System.Text.Json;
using Dse.Components;
using Dse.Core.Catalogue;
using Dse.Core.Testing;

namespace Dse.Cli.Tests;

public class PluginTests
{
    /// <summary>tests/&lt;project&gt;/bin/&lt;configuration&gt;/&lt;tfm&gt;/&lt;project&gt;.dll, found from this assembly's own output directory.</summary>
    private static string Built(string project)
    {
        var output = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        string tfm = output.Name;
        string configuration = output.Parent!.Name;
        string tests = output.Parent.Parent!.Parent!.Parent!.FullName;
        return Path.Combine(tests, project, "bin", configuration, tfm, project + ".dll");
    }

    private static readonly string Sample = Built("Dse.Cli.Tests.SampleModule");
    private static readonly string Clash = Built("Dse.Cli.Tests.ClashModule");

    [Fact]
    public void TheFixturesWereBuilt()
    {
        Assert.True(File.Exists(Sample), $"Expected the sample module at '{Sample}'.");
        Assert.True(File.Exists(Clash), $"Expected the clash module at '{Clash}'.");
    }

    [Fact]
    public void APluginsTypesAppearInTheCatalogueExport()
    {
        CliRun run = Cli.Run("catalog", "export", "--assembly", Sample);

        Assert.Equal(ExitCodes.Ok, run.ExitCode);
        using JsonDocument document = JsonDocument.Parse(run.Out);
        Assert.Equal(["Dse.Components", "Sample"], document.RootElement.GetProperty("modules").EnumerateArray().Select(m => m.GetString()));
        JsonElement sw = document.RootElement.GetProperty("components").EnumerateArray().Single(c => c.GetProperty("type").GetString() == "hysteresis-switch");
        Assert.Equal("Sample", sw.GetProperty("module").GetString());
        Assert.Equal("sample-ore", document.RootElement.GetProperty("materials")[0].GetProperty("name").GetString());
    }

    [Fact]
    public void APluginsTypesAppearInTheSchema()
    {
        CliRun run = Cli.Run("schema", "export", "--assembly=" + Sample);

        Assert.Equal(ExitCodes.Ok, run.ExitCode);
        using JsonDocument document = JsonDocument.Parse(run.Out);
        Assert.True(document.RootElement.GetProperty("$defs").TryGetProperty("component.hysteresis-switch", out _));
    }

    [Fact]
    public void APlantUsingAPluginValidatesWithItAndNotWithout()
    {
        CliRun with = Cli.Run("validate", Cli.Plant("sample.json"), "--assembly", Sample);
        CliRun without = Cli.Run("validate", Cli.Plant("sample.json"));

        Assert.Equal(ExitCodes.Ok, with.ExitCode);
        Assert.Equal(ExitCodes.PlantInvalid, without.ExitCode);
        Assert.Contains("DSE102", without.Err, StringComparison.Ordinal);
        Assert.Contains("--assembly", without.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void APluginsConstructorMessageReachesTheUser()
    {
        string path = Path.Combine(Path.GetTempPath(), $"dse-cli-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, """{ "components": [ { "id": "HS", "type": "hysteresis-switch", "parameters": { "onAbove": 60, "offBelow": 80 } } ] }""");
        try
        {
            CliRun run = Cli.Run("validate", path, "--assembly", Sample);

            Assert.Equal(ExitCodes.PlantInvalid, run.ExitCode);
            Assert.Contains("DSE111", run.Err, StringComparison.Ordinal);
            Assert.Contains("offBelow must be below onAbove", run.Err, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void AMissingAssemblyIsExitThree()
    {
        CliRun run = Cli.Run("catalog", "export", "--assembly", Path.Combine(Path.GetTempPath(), "no-such-plugin.dll"));

        Assert.Equal(ExitCodes.Unreadable, run.ExitCode);
        Assert.Empty(run.Out);
        Assert.Contains("the file does not exist", run.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void AFileThatIsNotAnAssemblyIsExitThree()
    {
        CliRun run = Cli.Run("catalog", "export", "--assembly", Cli.Plant("minimal.json"));

        Assert.Equal(ExitCodes.Unreadable, run.ExitCode);
        Assert.StartsWith("Cannot load assembly", run.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void AnAssemblyWithNoModuleSaysSo()
    {
        string noModule = Path.Combine(AppContext.BaseDirectory, "Dse.Io.Abstractions.dll");

        CliRun run = Cli.Run("catalog", "export", "--assembly", noModule);

        Assert.Equal(ExitCodes.Unreadable, run.ExitCode);
        Assert.Contains("contains no catalogue module", run.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void ADuplicateTypeNameNamesBothModules()
    {
        CliRun run = Cli.Run("catalog", "export", "--assembly", Clash);

        Assert.Equal(ExitCodes.Unreadable, run.ExitCode);
        Assert.Contains("'gearbox'", run.Err, StringComparison.Ordinal);
        Assert.Contains("module 'Dse.Components'", run.Err, StringComparison.Ordinal);
        Assert.Contains("module 'Clash'", run.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void TwoPluginsMayBeLoadedTogether()
    {
        CliRun run = Cli.Run("catalog", "export", "--assembly", Sample, "--assembly", Clash);

        // Both load; the clash is still a clash. What matters is that --assembly repeats without a usage error.
        Assert.Equal(ExitCodes.Unreadable, run.ExitCode);
        Assert.Contains("module 'Clash'", run.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void ThePluginPassesCatalogueConformance()
    {
        CatalogueBuilder builder = new CatalogueBuilder().Add<ComponentsModule>();
        Assert.True(ModuleLoader.TryLoad([Sample], builder, out string problem), problem);

        ConformanceReport report = CatalogueConformance.Check(
            builder.Build(),
            new ConformanceFixtures().Parameters("hysteresis-switch", """{ "onAbove": 80, "offBelow": 60 }"""));

        // The shipped types have no fixtures here, so only the plugin's findings are of interest.
        Assert.DoesNotContain(report.Mismatches, m => m.StartsWith("hysteresis-switch:", StringComparison.Ordinal));
        Assert.Contains(report.BuiltTypes, t => t.Name == "HysteresisSwitch");
    }
}
```

Run: `dotnet test tests/Dse.Cli.Tests --nologo --filter FullyQualifiedName~PluginTests`
Expected: build FAILS — `ModuleLoader` does not exist.

- [ ] **Step 3: Write `ModuleLoader`**

`src/Dse.Cli/ModuleLoader.cs`:

```csharp
using System.Reflection;
using System.Runtime.Loader;
using Dse.Core.Catalogue;

namespace Dse.Cli;

/// <summary>Loads catalogue modules from assemblies named on the command line.</summary>
internal static class ModuleLoader
{
    /// <summary>Adds every module found in <paramref name="paths"/>, in order. False, with one line in <paramref name="problem"/>, on the first failure.</summary>
    public static bool TryLoad(IReadOnlyList<string> paths, CatalogueBuilder builder, out string problem)
    {
        foreach (string given in paths)
        {
            string path = Path.GetFullPath(given);
            if (!File.Exists(path))
            {
                problem = $"Cannot load assembly '{given}': the file does not exist.";
                return false;
            }

            List<Type> modules;
            try
            {
                Assembly assembly = new PluginLoadContext(path).LoadFromAssemblyPath(path);
                modules = assembly.GetExportedTypes()
                    .Where(t => t is { IsClass: true, IsAbstract: false }
                             && typeof(ICatalogueModule).IsAssignableFrom(t)
                             && t.GetConstructor(Type.EmptyTypes) is not null)
                    .OrderBy(t => t.FullName, StringComparer.Ordinal)
                    .ToList();
            }
            catch (Exception ex) when (ex is BadImageFormatException or FileLoadException or FileNotFoundException or ReflectionTypeLoadException or TypeLoadException)
            {
                problem = $"Cannot load assembly '{given}': {ex.Message}";
                return false;
            }

            if (modules.Count == 0)
            {
                problem =
                    $"Assembly '{given}' contains no catalogue module. A module is a public, non-abstract class that implements " +
                    $"{nameof(ICatalogueModule)} and has a public parameterless constructor.";
                return false;
            }

            foreach (Type type in modules)
            {
                var module = (ICatalogueModule)Activator.CreateInstance(type)!;
                try
                {
                    builder.Add(module);
                }
                catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
                {
                    problem = $"Module '{module.Name}' from '{given}' could not be registered: {ex.Message}";
                    return false;
                }
            }
        }

        problem = string.Empty;
        return true;
    }

    /// <summary>
    /// Resolves a plugin's private dependencies from beside the plugin, and
    /// declines anything the host ships: the host's copy of Dse.Core is what makes
    /// the plugin's ICatalogueModule the same type as ours.
    /// </summary>
    private sealed class PluginLoadContext(string pluginPath) : AssemblyLoadContext(isCollectible: false)
    {
        private readonly AssemblyDependencyResolver _resolver = new(pluginPath);

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            if (assemblyName.Name is { } name && File.Exists(Path.Combine(AppContext.BaseDirectory, name + ".dll")))
            {
                return null; // Fall through to the default context: the host's copy.
            }

            string? path = _resolver.ResolveAssemblyToPath(assemblyName);
            return path is null ? null : LoadFromAssemblyPath(path);
        }
    }
}
```

- [ ] **Step 4: Add the option and use the loader**

In `CommandTable.cs` add the option and put it on every command:

```csharp
    public static readonly OptionSpec Assembly = new(
        "--assembly", "path", "Load catalogue modules from this assembly, in addition to the shipped components.", Repeatable: true);
```

```csharp
        new(["catalog", "export"], null, "Print every component, transform, hold and material type as JSON.", [Out, Assembly], Commands.CatalogExport.Run),
        new(["schema", "export"], null, "Print the JSON Schema for plant files, generated from the catalogue.", [Out, Assembly], Commands.SchemaExport.Run),
        new(["validate"], "plant.json", "Load a plant and report every error, each with its fix.", [Format, TimeStep, Assembly], Commands.Validate.Run),
        new(["tags"], "plant.json", "Load and build a plant, then list its tags: name, kind, access, unit, range.", [Format, TimeStep, Assembly], Commands.Tags.Run),
```

`Assembly` must be declared above `All`.

In `CliApp.Run`, replace the line that builds the catalogue:

```csharp
        CatalogueBuilder builder = new CatalogueBuilder().Add<ComponentsModule>();
        if (!ModuleLoader.TryLoad(parsed.All(CommandTable.Assembly), builder, out string loadProblem))
        {
            stderr.Write(loadProblem + "\n");
            return ExitCodes.Unreadable;
        }

        return parsed.Command!.Run(new CliContext(parsed, builder.Build(), stdout, stderr));
```

- [ ] **Step 5: Run the tests**

Run: `dotnet test tests/Dse.Cli.Tests --nologo`
Expected: PASS — Task 14's 31 cases and these 11.

`ACommandsHelpListsItsOptions` (Task 14) still passes: `validate --help` now also
lists `--assembly`, and still not `--out`.

If `APluginsTypesAppearInTheCatalogueExport` fails with an `InvalidCastException`
or "no catalogue module" although the DLL is right, the plugin got its own copy
of `Dse.Core` — check that `Dse.Core.dll` is in `AppContext.BaseDirectory` of
the test run and that `PluginLoadContext.Load` returned null for it; report
what you find.

- [ ] **Step 6: Run it for real**

```bash
dotnet build tests/Dse.Cli.Tests.SampleModule -c Debug --nologo
dotnet run --project src/Dse.Cli -- validate tests/Dse.Cli.Tests/Plants/sample.json --assembly tests/Dse.Cli.Tests.SampleModule/bin/Debug/net10.0/Dse.Cli.Tests.SampleModule.dll
dotnet run --project src/Dse.Cli -- validate tests/Dse.Cli.Tests/Plants/sample.json; echo "exit $?"
```

Expected: an `OK` block with 3 components; then two `DSE102`s — the type
`hysteresis-switch`, whose fix mentions `--assembly`, and the material
`sample-ore` — and `exit 1`.

- [ ] **Step 7: Build Release, run everything, commit**

Run: `dotnet build Dse.sln -c Release --nologo` — expect `0 Warning(s)`, `0 Error(s)`.
Run: `dotnet test Dse.sln --nologo` — expect all green.

```bash
git add Dse.sln src/Dse.Cli tests/Dse.Cli.Tests tests/Dse.Cli.Tests.SampleModule tests/Dse.Cli.Tests.ClashModule
git commit -m "feat(cli): load catalogue modules from assemblies named on the command line"
```

---
### Task 16: The authoring recipe, the diagnostics reference, and the documentation

Spec 14 asks for "a written recipe for authoring a new component". It is
validated by having been followed: `HysteresisSwitch` (Task 15) is its worked
example, and the recipe's last step is the conformance check that already
passes for it.

**Files:**
- Create: `src/Dse.Configuration/DiagnosticsReference.cs`
- Test: `tests/Dse.Configuration.Tests/DiagnosticsReferenceTests.cs`
- Create (generated, read, committed): `docs/configuration-diagnostics.md`
- Create: `docs/authoring-a-component.md`
- Modify: `docs/architecture.md` (one new section), `README.md` (status paragraph, CLI quick start)

**Interfaces:**
- Consumes: `ConfigDiagnostics.All` (Task 10); `Golden` (Task 9).
- Produces: `public static class DiagnosticsReference { public static string Render(); }`

- [ ] **Step 1: Write the failing reference test**

`tests/Dse.Configuration.Tests/DiagnosticsReferenceTests.cs`:

```csharp
using Dse.Tests.Shared;

namespace Dse.Configuration.Tests;

public class DiagnosticsReferenceTests
{
    [Fact]
    public void TheCommittedReferencePageIsCurrent()
    {
        // Two levels up from this file is the repository root. Regenerate with DSE_UPDATE_GOLDEN=1.
        Golden.Assert("../../docs/configuration-diagnostics.md", DiagnosticsReference.Render());
    }

    [Fact]
    public void EveryCodeHasASection()
    {
        string page = DiagnosticsReference.Render();

        Assert.All(ConfigDiagnostics.All, d => Assert.Contains($"## {d.Code} — {d.Title}\n", page, StringComparison.Ordinal));
        Assert.StartsWith("# Configuration diagnostics\n", page, StringComparison.Ordinal);
        Assert.EndsWith("\n", page, StringComparison.Ordinal);
        Assert.DoesNotContain('\r', page);
    }
}
```

Run: `dotnet test tests/Dse.Configuration.Tests --nologo --filter FullyQualifiedName~DiagnosticsReferenceTests`
Expected: build FAILS — `DiagnosticsReference` does not exist.

- [ ] **Step 2: Write the renderer**

`src/Dse.Configuration/DiagnosticsReference.cs`:

```csharp
using System.Text;

namespace Dse.Configuration;

/// <summary>Renders docs/configuration-diagnostics.md from the code table, so the page cannot drift from the codes.</summary>
public static class DiagnosticsReference
{
    public static string Render()
    {
        var page = new StringBuilder();
        page.Append("# Configuration diagnostics\n\n");
        page.Append("<!-- Generated from ConfigDiagnostics.All by DiagnosticsReference.Render(). Do not edit by hand:\n");
        page.Append("     run the Dse.Configuration tests with DSE_UPDATE_GOLDEN=1, read the result, commit it. -->\n\n");
        page.Append("`dse validate` and `PlantLoader.Load` report every problem in a plant file as a diagnostic with four\n");
        page.Append("parts: a **code**, a **JSON path** into the file (`$.components[3].parameters.motor.ratedPowerW`), a\n");
        page.Append("**message** saying what is wrong, and a **fix** saying what to do. A diagnostic without a fix cannot be\n");
        page.Append("constructed.\n\n");
        page.Append("The loader works in stages — parse, structure, references, instantiate, wire, build — and stops at the\n");
        page.Append("end of the first stage that found an error, having reported *every* error that stage could find. Fixing\n");
        page.Append("what is reported may therefore reveal errors from a later stage.\n\n");
        page.Append("| Code | Meaning |\n|---|---|\n");
        foreach (DiagnosticInfo info in ConfigDiagnostics.All)
        {
            page.Append("| ").Append(info.Code).Append(" | ").Append(info.Title).Append(" |\n");
        }

        page.Append('\n');
        foreach (DiagnosticInfo info in ConfigDiagnostics.All)
        {
            page.Append("## ").Append(info.Code).Append(" — ").Append(info.Title).Append("\n\n");
            page.Append(info.Explanation).Append("\n\n");
        }

        page.Append("## DSE001–DSE011 — plant validation\n\n");
        page.Append("Codes below DSE100 come from `SimulationBuilder.Validate()` and mean the same for a plant built in code:\n");
        page.Append("duplicate ids, unconnected required inputs, algebraic loops, belts too fast for their cells, incompatible\n");
        page.Append("flow links, tag conflicts. The loader passes them through with the path of the first component involved;\n");
        page.Append("their message is split at its first sentence into message and fix. See `docs/architecture.md`.\n");
        return page.ToString();
    }
}
```

- [ ] **Step 3: Generate the page, read it, and run the tests**

```bash
DSE_UPDATE_GOLDEN=1 dotnet test tests/Dse.Configuration.Tests --nologo --filter FullyQualifiedName~DiagnosticsReferenceTests
```

Read `docs/configuration-diagnostics.md`: thirteen sections in code order, the
table, the closing pass-through section, no `.actual` file left behind.

Run: `dotnet test tests/Dse.Configuration.Tests --nologo --filter FullyQualifiedName~DiagnosticsReferenceTests`
Expected: PASS, 2 tests.

- [ ] **Step 4: Write the authoring recipe**

Create `docs/authoring-a-component.md` with exactly this content:

````markdown
# Authoring a component

The framework's job is to make the plumbing free, so that the physics of your
domain is the only real work. This is the plumbing, in the order you will meet
it. The worked example is
[`HysteresisSwitch`](../tests/Dse.Cli.Tests.SampleModule/HysteresisSwitch.cs);
its numbered comments match the sections below. For a component with faults,
telemetry and material, read [`TransferChute`](../src/Dse.Components/Flow/TransferChute.cs).

## 1. Decide what it is

| It… | Derive from | You get |
|---|---|---|
| computes signals from signals | `ComponentBase` | `AddInput<T>`, `AddOutput<T>`, `Evaluate` |
| holds or moves material | `FlowComponentBase` | the above, plus `AddInlet`, `AddOutlet`, `Advance`, `MassHeld` |
| measures something | `InstrumentBase` | range, noise, lag, quality and the whole sensor-fault vocabulary, already working |
| is several components wired together | `CompositeComponent` | `AddChild`, `Expose`; it is flattened away at build time |

A component cannot reach a sibling. Everything it knows arrives on a port or
through a constructor argument; that is what makes it testable alone.

## 2. Keep all state in the instance

No static fields. No `DateTime.Now`, no `Stopwatch`, no `Environment.TickCount`:
time is `ctx.SimTime` and the step is `ctx.Dt`, both on the `TickContext` that
`Evaluate` receives. No `System.Random` and no `Guid.NewGuid()`: randomness is
the `DeterministicRandom` that `Initialize(in InitContext ctx)` hands you as
`ctx.Random` — keep the reference. It is seeded from the master seed and *your
component's id*, so adding a component to a plant does not disturb anyone else's
random stream. Never iterate a
`Dictionary` or `HashSet` where the order can reach an output, and never use
`string.GetHashCode()` for anything that matters: both vary between runs.

Two simulations in one process must not be able to see each other. If yours
passes that test, it is deterministic.

## 3. Let the constructor say no

Validate every argument in the constructor and throw `ArgumentException` (or
`ArgumentOutOfRangeException`) with a message that says what to change. For a
plant loaded from JSON that message is shown to the author as `DSE111`, so write
it for someone who has never seen your source.

## 4. Declare ports once, in the constructor

```csharp
Value = AddInput<double>("Value");                       // optional, defaults to 0
Permit = AddInput<bool>("Permit", defaultValue: true);   // optional, defaults to true
Feed = AddInput<double>("Feed", required: true);         // DSE002 if nothing drives it
Load = AddInput<double>("Load", latched: true);          // reads last tick's value; creates no ordering edge
On = AddOutput<bool>("On");
```

The graph is immutable after `Build()`. If your component reads an input and
that input depends, this same tick, on your own output, you have an algebraic
loop (`DSE003`). Break it where the physics allows a tick of delay: declare the
input `latched`, or return `false` from `HasDirectFeedthrough` if *none* of your
outputs depend on this tick's inputs.

## 5. Evaluate

`Evaluate(in TickContext ctx)` runs once per tick, after everything that drives
your inputs. Read inputs, update state, write outputs. It must not allocate in
the steady state and must not throw for a physical condition — a stalled motor
is an output, not an exception. Discrete happenings worth a line in the incident
timeline go to `ctx.Log(Id, code, message)`: codes are `UPPER_SNAKE`, messages
are sentences.

A flow component also overrides `Advance(in TickContext ctx)`, where material
actually moves, and reports `MassHeld` (and `MassCreated` / `MassDestroyed` if
it is a source, a sink, or lossy). The engine audits conservation every tick;
you cannot forget to.

## 6. Tags are what the plant really has on the wire

Implement `ITagProvider` and return bindings for the signals a real installation
would wire to its control system: a sensor's reading, a starter's command and
feedback. Do **not** tag internal truth — true motor current is telemetry
(`ctx.RegisterTelemetry` in `Initialize`), because a SCADA that could read it
would not need the current sensor. Names are relative (`"On"`); the builder
prefixes the component id. Tag descriptions are fragments without a full stop.

## 7. Faults change behaviour, never wiring

Implement `IFaultTarget`: publish a `FaultDescriptor` per fault, with its
parameters and their defaults, and change *state* in `ApplyFault` /
`ClearFault`. Faults arrive through the event queue at a tick boundary. An
instrument gets calibration, noise, drift, lag, freeze and fail-high/low from
`InstrumentBase` for free.

## 8. Describe it

Beside the constructor, write the descriptor — and **below** any
`private static readonly FaultDescriptor[] Faults`, because static initialisers
run in textual order:

```csharp
public static ComponentDescriptor Descriptor { get; } = new(
    "hysteresis-switch",                      // kebab-case, unique in the catalogue
    ComponentCategory.Signal,
    "Switches on above one level and off below a lower one, holding its state in between.",
    (id, p) => new HysteresisSwitch(id, p.Double("onAbove"), p.Double("offBelow")))
{
    Parameters = [ Param.Double("onAbove", "…"), Param.Double("offBelow", "…") ],
    Ports      = [ PortSpec.In<double>("Value"), PortSpec.Out<bool>("On") ],
    Tags       = [ new TagEntry("On", TagKind.Bool, TagAccess.ReadOnly) ],
};
```

- **Parameters** are camelCase and carry units, defaults, ranges and a
  description that ends in a full stop. Give a range only where the constructor
  enforces one. "Unlimited" is an *optional* parameter with no default
  (`optional: true`, read with `p.DoubleOr(…)`): JSON has no infinity.
- A nested record is a `GroupDefinition`, declared once and shared
  (`MotorRatingGroup`, `InstrumentCatalogue.Spec`).
- Another component is a `Param.Reference<TCapability>`; the factory reads
  `p.Reference<TCapability>(name)` and receives the instance. List under
  `Provides` every capability *your* component can supply.
- A material is `Param.Material`; a state of it is `Param.MaterialState`, named,
  never indexed. A transform or hold condition is an `ObjectDescriptor` in a
  slot, taken with `Param.Object` / `Param.ObjectList`.
- Ports whose number or names depend on a parameter are declared once with a
  `PortRepeat` (`"Channel{n}"` repeated by `channels`).
- Use `PortSpec`, not `Ports`: inside a component class `Ports` is the inherited
  instance property.

## 9. Register it

```csharp
public sealed class MyModule : ICatalogueModule
{
    public string Name => "MyCompany.Bakery";
    public void Register(CatalogueBuilder builder) => builder.Add(Oven.Descriptor);
}
```

Public, concrete, parameterless: that is what `dse --assembly` looks for. In
code, compose catalogues explicitly:
`new CatalogueBuilder().Add<ComponentsModule>().Add<MyModule>().Build()`.

## 10. Prove it

```csharp
[Fact]
public void EveryDescriptorMatchesWhatItBuilds()
{
    ComponentCatalogue catalogue = new CatalogueBuilder().Add<ComponentsModule>().Add<MyModule>().Build();
    ConformanceFixtures fixtures = new ConformanceFixtures()
        .Parameters("oven", """{ "zones": 3, "spec": { "unit": "°C", "rangeLow": 0, "rangeHigh": 400 } }""");

    ConformanceReport report = CatalogueConformance.Check(catalogue, fixtures);

    Assert.DoesNotContain(report.Mismatches, m => m.StartsWith("oven:", StringComparison.Ordinal));
}
```

`CatalogueConformance` builds one of everything from its descriptor and reports
every difference between what the descriptor says and what the instance has:
ports, flow ports, faults, tags, telemetry, capabilities. A fixture supplies
required parameters, materials to name and stand-in nodes to reference. When it
is quiet, `dse catalog export --assembly yours.dll` describes your component
truthfully, `dse schema export` validates plants that use it, and
`dse validate` loads them.

Then test the physics — that part is yours.
````

The member names above were checked against the source on 2026-09-20:
`TickContext` has `Tick`, `Dt`, `SimTime` and `Log(source, code, message)`;
`InitContext` has `Random`, `Items`, `StartTime`, `Dt` and
`RegisterTelemetry(name, unit)`; `FlowComponentBase` has `MassHeld`,
`MassCreated`, `MassDestroyed`, `Advance` and `ValidateFlow`. Check them again as
you paste — where one has changed, **use the real name** and say so in the
report. A recipe that does not compile in the reader's head is worse than none.

- [ ] **Step 5: Extend the architecture document and the README**

Append to `docs/architecture.md`:

```markdown
## Catalogue, schema and loader

Three things are generated from one source, the descriptors, and therefore agree:

    ComponentDescriptor ──► CatalogueJson.Export   what exists, for a person or an agent
           │            ──► PlantSchema.Generate   what a plant file may say, for an editor or a validator
           └── Factory  ──► PlantLoader.Load       a SimulationBuilder, or diagnostics

A **descriptor** is hand-written beside the constructor it describes and carries
the factory that builds the component from parsed parameters. It cannot drift:
`CatalogueConformance` builds one instance of every type and compares the
descriptor with it, and a sweep fails the build if any concrete node, transform
or hold condition has no descriptor.

A **catalogue** is an immutable value composed from modules
(`new CatalogueBuilder().Add<ComponentsModule>()…`). There is no static registry
and no assembly scanning; the CLI's `--assembly` is the only place a module is
discovered rather than named.

The **loader** runs six stages — parse, structure, references, instantiate,
wire, build — and stops at the end of the first stage that reported an error,
having collected every error of that stage. It resolves ports against the live
instances, not against descriptors, so what it wires is what exists. The last
stage is `SimulationBuilder.Validate()`: a plant loaded from JSON passes exactly
the checks a plant built in code passes, and `DSE001`–`DSE011` mean the same in
both. The loader returns the builder unbuilt, so a caller can still set a frame
sink or decide not to build.

The **schema** checks structure; the loader checks meaning. That a reference
names a component, a material a material, an address a port — no schema can
know. The boundary is asserted by `SchemaAgreementTests`, which requires the
schema to reject every structural fixture and to *accept* every semantic one.

Determinism is unaffected: the loader adds components in file order, the
catalogue and every export are sorted, and the round-trip test holds a JSON plant
and its hand-built twin to byte-identical event logs.
```

In `README.md`, add to the end of the **Status** section's first paragraph the
sentence:

```markdown
A component catalogue, declarative JSON plants with a generated JSON Schema, and
a `dse` command line sit on top.
```

and add this section after **Status**:

````markdown
## Command line

```bash
dotnet run --project src/Dse.Cli -- catalog export            # every component, transform, hold and material, as JSON
dotnet run --project src/Dse.Cli -- schema export --out dse-plant.schema.json
dotnet run --project src/Dse.Cli -- validate plant.json       # every error, each with its fix; exit 1 if any
dotnet run --project src/Dse.Cli -- tags plant.json           # the tag directory a SCADA would see
```

Add `--assembly path/to/YourModule.dll` to any command to include your own
components; add `--format json` to `validate` and `tags` for machine-readable
output. A plant file looks like this:

```json
{
  "defaults": { "seed": 1, "timeStepMs": 10 },
  "materials": [ { "name": "ore", "kind": "bulk", "properties": { "density": 2000, "moisture": 0.03, "temperature": 15 } } ],
  "components": [
    { "id": "FEED",  "type": "bulk-source", "parameters": { "material": "ore", "rateKgPerS": 20 } },
    { "id": "CV001", "type": "conveyor",    "parameters": { "lengthM": 10, "cellSizeM": 0.5, "beltWidthM": 0.8,
        "angleOfReposeDeg": 20, "materialDensityKgM3": 2000, "emptyBeltMassKg": 250, "frictionCoefficient": 0.04,
        "pulleyDiameterM": 0.5, "gearRatio": 20, "motor": { "ratedPowerW": 750, "ratedSpeedRadPerS": 150, "ratedCurrentA": 2 } } },
    { "id": "PILE",  "type": "bulk-sink" }
  ],
  "flows": [ { "from": "FEED.Out", "to": "CV001.In" }, { "from": "CV001.Out", "to": "PILE.In" } ]
}
```

See [authoring a component](docs/authoring-a-component.md) and the
[configuration diagnostics](docs/configuration-diagnostics.md).
````

Save that example as a file and run `dse validate` on it; it must print `OK`.
If it does not, fix the example, not the loader.

- [ ] **Step 6: Build Release, run everything, commit**

Run: `dotnet build Dse.sln -c Release --nologo` — expect `0 Warning(s)`, `0 Error(s)`.
Run: `dotnet test Dse.sln --nologo` — expect all green.

```bash
git add src/Dse.Configuration/DiagnosticsReference.cs tests/Dse.Configuration.Tests/DiagnosticsReferenceTests.cs docs README.md
git commit -m "docs: add the component authoring recipe, the diagnostics reference and the CLI quick start"
```

---

## Completion check

Before the final whole-branch review, confirm each of the spec's success
criteria (section 7) with a command, and put the output in the report:

| Criterion | How |
|---|---|
| `catalog export` and `schema export` reproduce their golden files | `dotnet test --filter "FullyQualifiedName~ComponentsExportTests|FullyQualifiedName~PlantSchemaTests.MatchesTheGoldenFile"` |
| every concrete node, transform and hold has a descriptor; conformance is clean | `dotnet test --filter FullyQualifiedName~ComponentsCatalogueTests` |
| the JSON conveyor and the code-built one write byte-identical logs | `dotnet test --filter FullyQualifiedName~RoundTripTests` |
| every `DSE1xx` code has a fixture that produces exactly it; every diagnostic has a fix | `dotnet test --filter "FullyQualifiedName~CorpusTests|FullyQualifiedName~ConfigDiagnosticTests"` |
| the schema and the loader agree, boundary asserted | `dotnet test --filter FullyQualifiedName~SchemaAgreementTests` |
| a plugin's types appear in both exports and load through `validate` | `dotnet test --filter FullyQualifiedName~PluginTests` |
| Release build, zero warnings; the original 471 tests still pass | `dotnet build Dse.sln -c Release --nologo` and `dotnet test Dse.sln --nologo` |
| no runtime dependency crept in | `grep -rn "PackageReference" src/` prints nothing |

Record, as plans 1–4 did, a **"Rulings made during execution"** section at the
end of this file for every place the code had to differ from the plan, and a
**"Parked follow-ups"** list from the final review.
