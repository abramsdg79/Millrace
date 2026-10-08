# Material Flow Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add the material layer to the simulation core — material types, bulk
lots and discrete items, typed flow ports, the offer/accept transport protocol
resolved downstream-first, cell-based bulk belts, position-based discrete belts,
residence transforms, and a per-tick mass-conservation audit — so a chain of
feeder → belt → chute → belt → sink backs up, drains and replays bit-identically.

**Architecture:** Flow is a second, separate port graph on the same components.
`FlowInlet`/`FlowOutlet` never create signal-ordering edges; the builder resolves
the material graph on its own and phase 3 walks it downstream-first, each node
discharging into consumers that have already made room and then advancing its
own contents. Mass moves only through that protocol, so the engine can assert
`sourced − sunk − held = 0` every tick. Bulk is mass plus blended intensive
properties in fixed cells; discrete is whole `ItemInstance`s with continuous
positions. Nothing in the engine knows what a conveyor is.

**Tech Stack:** .NET 10 (`net10.0`), C#, xUnit. No external runtime dependencies.

**Spec:** `docs/superpowers/specs/2026-09-02-industrial-process-simulation-engine-design.md`
(section 7 in full, section 8.1 for the transform contract, the conservation
line of section 16, and the flow-related validations of section 5.4)

**Plan sequence:** This is plan 2 of 6. Plan 1 (the simulation core) is merged
on `master` at `2b4f8b8`. Later plans add the component library (plan 3 —
sources, sinks, chutes, the `Former`, `ProcessUnit`, the three shipped
transforms, instruments, the fault channel), I/O and the real-time layer (plan
4), catalogue/config/CLI/scenarios (plan 5) and the reference samples (plan 6).
Nothing in this plan may reference those subsystems.

## Global Constraints

- Target framework `net10.0` for every project.
- `Millrace.Core` and `Millrace.Io.Abstractions` have **zero external runtime package
  references**. Test projects may reference test packages.
- `Nullable` enabled, `TreatWarningsAsErrors` true, `GenerateDocumentationFile`
  true (a `<see cref>` to a type that does not exist yet is a **build error**;
  reference only types that already exist when the file is compiled), deterministic builds.
- **Never use `System.Random`.** Use `DeterministicRandom` (plan 1).
- **Never use `string.GetHashCode()`** for anything that affects behaviour.
- **Never iterate a `Dictionary` or `HashSet` in per-tick code.** Phase 3 walks
  arrays resolved at build time. `Dictionary`/`HashSet`/`SortedSet` are allowed
  in constructors, `Initialize`, validation and `Build`.
- All formatting and parsing uses `CultureInfo.InvariantCulture`. Exception and
  validation messages that embed a `double` use
  `string.Create(CultureInfo.InvariantCulture, $"...")`.
- **Flow ports never create signal-ordering edges.** `FlowPort.SourcePort`
  returns `null` so `GraphResolver` ignores them; transport runs in phase 3.
- **Mass moves only through the transport protocol.** A node that creates mass
  reports it in `MassCreated`; a node that removes mass (sinks, declared losses)
  reports it in `MassDestroyed`. Anything else trips the conservation audit.
- **Bulk cells carry no state array in this plan.** `IMaterialTransform`
  receives `Span<double>.Empty` for bulk; items carry state. Spec 7.3 allows
  bulk state "when a process needs it" — no v1 process needs it, so it is a
  source change deferred until one does.
- Validation codes introduced here: `MR005` material recirculation loop,
  `MR006` CFL violation (`cellSize < maxSpeed·dt`), `MR007` inlet fed by a
  component not in the plant, `MR008` flow contract not implemented (a flow
  port on a non-`IFlowNode`, or a connected port whose owner lacks the
  producer/consumer interface). Every message names the fix.
- Licence: MIT. Namespace root: `Millrace`. Flow types live in `Millrace.Core.Flow`.
- Commit trailers: every commit message body ends with the two attribution
  lines the session specifies (`Co-Authored-By: …` and `Claude-Session: …`),
  in the body, never on the subject line.

## Plan-1 facts this plan builds on

- `ComponentBase` (`Millrace.Core.Graph`): `Id`, `Ports`, protected `AddInput<T>`/
  `AddOutput<T>`, virtual `HasDirectFeedthrough`, `Initialize(in InitContext)`,
  abstract `Evaluate(in TickContext)`, virtual `Latch()`. Ports are qualified
  with the composite path by `IQualifiable.Qualify`, which rewrites every
  `Port.OwnerId` in `_ports` — so any port registered through `_ports` is
  qualified for free.
- `Port` (abstract): `Name`, `OwnerId { get; internal set; }`, `QualifiedName`,
  abstract `IsMissingRequiredConnection`, `internal abstract Port? SourcePort`.
- `InitContext(DeterministicRandom random, TelemetryRegistry telemetry, string
  componentId, DateTimeOffset startTime, double dt)` — Task 3 inserts an
  `ItemIdSequence items` parameter after `telemetry`.
- `TickContext(long tick, double dt, DateTimeOffset simTime, EventLog log)`.
- `GraphResolver.TryResolve(IReadOnlyList<ISimComponent>, out ISimComponent[],
  out IReadOnlyList<string> cycle)` — Kahn's algorithm with a `SortedSet<int>`
  ready set and a backward-walking `FindCycle`. Task 6 extracts the sorting into
  `TopologicalSorter` so the flow graph can reuse it.
- `SimulationBuilder.Validate()` emits `MR001`–`MR004`; `Build()` resolves and
  constructs `Simulation(ISimComponent[] components, SimulationOptions options)`
  (internal constructor).
- `Simulation.Tick()`: `Initialize()`, `DrainDueEvents()`, `EvaluateSignals()`
  (evaluate pass then latch pass), `AdvanceFlow()` (a `private static` no-op
  this plan replaces), `PublishIo()`, `EmitFrame()`, `Clock.Advance()`.
- `SimulationOptions`: `Seed`, `StartTime`, `TimeStep` (default 10 ms).
- Test fakes in `tests/Millrace.Core.Tests/Fakes/`: `ConstantSource`, `Gain`,
  `Integrator`, `NoiseSource`, `Recorder`, `Tripper`, `TwoStage`.
- `OutputPort<T>.Value` is publicly settable, so a unit test can drive a
  component's input by wiring a bare `new OutputPort<double>("Out", "SP")` to it
  and assigning `Value` — no fake needed.

## File Structure

```
src/Millrace.Core/
  Flow/PayloadKind.cs                      Bulk | Discrete
  Flow/MaterialProperties.cs               intensive struct + mass-weighted Blend
  Flow/MaterialType.cs                     name, kind, state schema
  Flow/BulkLot.cs                          mass + properties; Merge, Take
  Flow/ItemIdSequence.cs                   per-simulation monotonic item ids
  Flow/ItemInstance.cs                     id, type, mass, properties, state[]
  Flow/FlowPort.cs                         port base for material connections
  Flow/FlowInlet.cs                        single-source material inlet
  Flow/FlowOutlet.cs                       single-target material outlet
  Flow/IFlowNode.cs                        node contract: inventory, advance, validate
  Flow/IBulkProducer.cs                    offer / withdraw (bulk)
  Flow/IBulkConsumer.cs                    accept / deposit (bulk)
  Flow/IItemProducer.cs                    peek / withdraw (items)
  Flow/IItemConsumer.cs                    can-accept / deposit (items)
  Flow/FlowComponentBase.cs                ComponentBase + IFlowNode defaults + AddInlet/AddOutlet
  Flow/FlowLink.cs                         resolved producer → consumer link
  Flow/FlowGraph.cs                        validate, build, downstream-first Step, Balance
  Flow/MassBalance.cs                      sourced, sunk, held, drift
  Flow/MassConservationException.cs
  Flow/IMaterialTransform.cs               Apply(ref props, state, dt, in ctx)
  Flow/TransformContext.cs                 ambient conditions
  Flow/BulkBelt.cs                         cell-array bulk transport
  Flow/DiscreteBelt.cs                     position-based item transport
  Graph/TopologicalSorter.cs               Kahn + cycle naming, shared by both resolvers
  Graph/GraphResolver.cs                   (modified: delegates to TopologicalSorter)
  Graph/ComponentBase.cs                   (modified: protected AddPort)
  Graph/CompositeComponent.cs              (modified: Inlet/Outlet alias lookup)
  Contexts/InitContext.cs                  (modified: Items)
  Simulation.cs                            (modified: flow graph, phase 3, MassBalance, Items)
  SimulationBuilder.cs                     (modified: flow validation and build)
  Time/SimulationOptions.cs                (modified: CheckConservation, ConservationTolerance)

tests/Millrace.Core.Tests/
  MaterialTests.cs, BulkLotTests.cs, ItemTests.cs, FlowPortTests.cs,
  FlowNodeTests.cs, TopologicalSorterTests.cs, FlowGraphTests.cs,
  ConservationTests.cs, SimulationFlowTests.cs, BulkBeltTests.cs,
  BulkBeltTransformTests.cs, DiscreteBeltTests.cs, FlowIntegrationTests.cs,
  ConservationPropertyTests.cs
  ComponentTests.cs                        (modified: InitContext helper)
  Fakes/TestContexts.cs                    InitContext / TickContext factories
  Fakes/Setpoint.cs                        mutable signal source
  Fakes/Flow/BulkFeeder.cs, BulkSink.cs, BulkBuffer.cs, LeakyBuffer.cs
  Fakes/Flow/ItemFeeder.cs, ItemSink.cs, ItemBuffer.cs
  Fakes/Flow/Heater.cs, ResidenceCounter.cs   test transforms

docs/architecture.md                       (modified: phase 3, new "Material flow" section)
README.md                                  (modified: status)
```

Test-only nodes and transforms live in `tests/`, never in `src/`. Real sources,
sinks, chutes and the shipped transforms arrive in plan 3; this plan ships only
the two belts, because cell advection and item positioning are transport
machinery, not domain components.

---

### Task 1: Material types and properties

**Files:**
- Create: `src/Millrace.Core/Flow/PayloadKind.cs`
- Create: `src/Millrace.Core/Flow/MaterialProperties.cs`
- Create: `src/Millrace.Core/Flow/MaterialType.cs`
- Test: `tests/Millrace.Core.Tests/MaterialTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `enum PayloadKind { Bulk, Discrete }`
  - `readonly record struct MaterialProperties(double Density, double Moisture, double Temperature)`
    with `static MaterialProperties Blend(in MaterialProperties a, double massA, in MaterialProperties b, double massB)`
  - `sealed class MaterialType(string name, PayloadKind kind, params string[] stateSchema)`
    with `string Name`, `PayloadKind Kind`, `IReadOnlyList<string> StateSchema`,
    `int StateIndexOf(string stateName)`, `double[] NewState()`

Spec 7.2: a fixed struct, not a property bag, because a dictionary per cell
puts allocation and non-deterministic iteration into the hottest loop. Spec 7.3:
the state schema is an ordered list of named doubles so the array is
deterministic and costs nothing when empty.

- [ ] **Step 1: Write the failing tests**

`tests/Millrace.Core.Tests/MaterialTests.cs`:

```csharp
using Millrace.Core.Flow;
using Xunit;

namespace Millrace.Core.Tests;

public class MaterialTests
{
    private static readonly MaterialProperties A = new(Density: 1000.0, Moisture: 0.10, Temperature: 20.0);
    private static readonly MaterialProperties B = new(Density: 2000.0, Moisture: 0.30, Temperature: 60.0);

    [Fact]
    public void BlendIsMassWeighted()
    {
        MaterialProperties blended = MaterialProperties.Blend(A, 1.0, B, 3.0);

        Assert.Equal(1750.0, blended.Density, 9);
        Assert.Equal(0.25, blended.Moisture, 9);
        Assert.Equal(50.0, blended.Temperature, 9);
    }

    [Fact]
    public void BlendingWithZeroMassReturnsTheOtherSideExactly()
    {
        Assert.Equal(B, MaterialProperties.Blend(A, 0.0, B, 5.0));
        Assert.Equal(A, MaterialProperties.Blend(A, 5.0, B, 0.0));
    }

    [Fact]
    public void BlendingTwoEmptyMassesReturnsTheFirst()
    {
        Assert.Equal(A, MaterialProperties.Blend(A, 0.0, B, 0.0));
    }

    [Fact]
    public void BlendRejectsNegativeMass()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MaterialProperties.Blend(A, -1.0, B, 1.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => MaterialProperties.Blend(A, 1.0, B, -1.0));
    }

    [Fact]
    public void StateIndexResolvesByName()
    {
        var billet = new MaterialType("Billet", PayloadKind.Discrete, "CoreTemperature", "TimeAbove1150");

        Assert.Equal(0, billet.StateIndexOf("CoreTemperature"));
        Assert.Equal(1, billet.StateIndexOf("TimeAbove1150"));
        Assert.Equal(new[] { "CoreTemperature", "TimeAbove1150" }, billet.StateSchema);
    }

    [Fact]
    public void UnknownStateNamesTheDeclaredOnes()
    {
        var billet = new MaterialType("Billet", PayloadKind.Discrete, "CoreTemperature");

        KeyNotFoundException error =
            Assert.Throws<KeyNotFoundException>(() => billet.StateIndexOf("Nope"));
        Assert.Contains("CoreTemperature", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DuplicateStateNamesAreRejected()
    {
        Assert.Throws<ArgumentException>(
            () => new MaterialType("Billet", PayloadKind.Discrete, "T", "T"));
    }

    [Fact]
    public void NewStateIsSizedBySchema()
    {
        var billet = new MaterialType("Billet", PayloadKind.Discrete, "A", "B");
        var ore = new MaterialType("Ore", PayloadKind.Bulk);

        Assert.Equal(2, billet.NewState().Length);
        Assert.Empty(ore.NewState());
    }

    [Fact]
    public void BlankNameIsRejected()
    {
        Assert.Throws<ArgumentException>(() => new MaterialType(" ", PayloadKind.Bulk));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter FullyQualifiedName~MaterialTests`
Expected: FAIL — `Millrace.Core.Flow` does not exist.

- [ ] **Step 3: Implement the types**

`src/Millrace.Core/Flow/PayloadKind.cs`:

```csharp
namespace Millrace.Core.Flow;

/// <summary>What a flow node holds and a flow port carries. A node handles exactly one kind.</summary>
public enum PayloadKind
{
    /// <summary>Mass in kilograms plus blended intensive properties.</summary>
    Bulk,

    /// <summary>Whole <see cref="ItemInstance"/> values that move one at a time.</summary>
    Discrete,
}
```

> The `<see cref="ItemInstance"/>` above references a type created in Task 3.
> Until then it is a CS1574 build error. Write the doc comment as
> `Whole item instances that move one at a time.` in this task; Task 3 restores
> the `cref` once the type exists.

`src/Millrace.Core/Flow/MaterialProperties.cs`:

```csharp
namespace Millrace.Core.Flow;

/// <summary>
/// The intensive properties every parcel of material carries. A fixed struct
/// rather than a property bag: a dictionary per cell would put allocation and
/// non-deterministic iteration into the hottest loop in the engine. Adding a
/// field is a source change; it blends by the same rule.
/// </summary>
/// <param name="Density">kg/m³.</param>
/// <param name="Moisture">Mass fraction, 0..1.</param>
/// <param name="Temperature">°C.</param>
public readonly record struct MaterialProperties(double Density, double Moisture, double Temperature)
{
    /// <summary>
    /// Mass-weighted average of two streams. A zero-mass side contributes
    /// nothing; two zero masses return <paramref name="a"/> unchanged.
    /// </summary>
    public static MaterialProperties Blend(
        in MaterialProperties a,
        double massA,
        in MaterialProperties b,
        double massB)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(massA);
        ArgumentOutOfRangeException.ThrowIfNegative(massB);

        double total = massA + massB;
        if (total <= 0.0)
        {
            return a;
        }

        double weightA = massA / total;
        double weightB = massB / total;
        return new MaterialProperties(
            (a.Density * weightA) + (b.Density * weightB),
            (a.Moisture * weightA) + (b.Moisture * weightB),
            (a.Temperature * weightA) + (b.Temperature * weightB));
    }
}
```

`src/Millrace.Core/Flow/MaterialType.cs`:

```csharp
namespace Millrace.Core.Flow;

/// <summary>
/// A kind of material — ore, dough, a billet. The state schema names the
/// per-parcel accumulated values (time above a temperature, say) that
/// transforms may write. An empty schema costs nothing.
/// </summary>
public sealed class MaterialType
{
    private readonly string[] _stateSchema;

    public MaterialType(string name, PayloadKind kind, params string[] stateSchema)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(stateSchema);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string state in stateSchema)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(state, nameof(stateSchema));
            if (!seen.Add(state))
            {
                throw new ArgumentException(
                    $"State '{state}' is declared twice on material '{name}'. State names must be unique.",
                    nameof(stateSchema));
            }
        }

        Name = name;
        Kind = kind;
        _stateSchema = stateSchema.ToArray();
    }

    public string Name { get; }

    public PayloadKind Kind { get; }

    /// <summary>Ordered names of the accumulated-state slots.</summary>
    public IReadOnlyList<string> StateSchema => _stateSchema;

    /// <summary>
    /// Index of a named state slot. Resolve it once in Initialize and index the
    /// array by integer per tick; never look up by name in Evaluate.
    /// </summary>
    public int StateIndexOf(string stateName)
    {
        int index = Array.IndexOf(_stateSchema, stateName);
        if (index < 0)
        {
            throw new KeyNotFoundException(
                $"Material '{Name}' declares no state named '{stateName}'. " +
                $"Declared: {string.Join(", ", _stateSchema)}.");
        }

        return index;
    }

    /// <summary>A zeroed state array sized by the schema.</summary>
    public double[] NewState() =>
        _stateSchema.Length == 0 ? Array.Empty<double>() : new double[_stateSchema.Length];

    public override string ToString() => Name;
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter FullyQualifiedName~MaterialTests`
Expected: PASS, 9 tests.

- [ ] **Step 5: Commit**

```bash
git add src/Millrace.Core/Flow tests/Millrace.Core.Tests/MaterialTests.cs
git commit -m "feat(flow): add material types, kinds and blended properties"
```

---

### Task 2: Bulk lots

**Files:**
- Create: `src/Millrace.Core/Flow/BulkLot.cs`
- Test: `tests/Millrace.Core.Tests/BulkLotTests.cs`

**Interfaces:**
- Consumes: `MaterialType`, `MaterialProperties`, `PayloadKind` (Task 1).
- Produces:
  - `readonly record struct BulkLot(MaterialType? Type, double Mass, MaterialProperties Properties)`
    with `static BulkLot Empty`, `bool IsEmpty`,
    `static BulkLot Of(MaterialType type, double mass, MaterialProperties properties)`,
    `BulkLot Merge(in BulkLot other)`, `BulkLot Take(double mass, out BulkLot remaining)`

A lot is the unit of bulk transfer: what a cell holds, what a `Withdraw` returns,
what a `Deposit` receives. `Empty` has no type; merging into it adopts the
incoming lot's type. Merging two different types throws — one cell holds one
material, and type changes happen in an explicit component (plan 3).

- [ ] **Step 1: Write the failing tests**

`tests/Millrace.Core.Tests/BulkLotTests.cs`:

```csharp
using Millrace.Core.Flow;
using Xunit;

namespace Millrace.Core.Tests;

public class BulkLotTests
{
    private static readonly MaterialType Ore = new("Ore", PayloadKind.Bulk);
    private static readonly MaterialType Coal = new("Coal", PayloadKind.Bulk);
    private static readonly MaterialType Billet = new("Billet", PayloadKind.Discrete);
    private static readonly MaterialProperties Wet = new(1600.0, 0.20, 15.0);
    private static readonly MaterialProperties Dry = new(1600.0, 0.00, 35.0);

    [Fact]
    public void MergeAddsMassAndBlendsProperties()
    {
        BulkLot merged = BulkLot.Of(Ore, 1.0, Wet).Merge(BulkLot.Of(Ore, 3.0, Dry));

        Assert.Same(Ore, merged.Type);
        Assert.Equal(4.0, merged.Mass, 9);
        Assert.Equal(0.05, merged.Properties.Moisture, 9);
        Assert.Equal(30.0, merged.Properties.Temperature, 9);
    }

    [Fact]
    public void MergingIntoEmptyAdoptsTheIncomingLot()
    {
        BulkLot incoming = BulkLot.Of(Ore, 2.0, Wet);

        Assert.Equal(incoming, BulkLot.Empty.Merge(incoming));
    }

    [Fact]
    public void MergingEmptyIsANoOp()
    {
        BulkLot lot = BulkLot.Of(Ore, 2.0, Wet);

        Assert.Equal(lot, lot.Merge(BulkLot.Empty));
    }

    [Fact]
    public void MergingDifferentTypesThrows()
    {
        BulkLot ore = BulkLot.Of(Ore, 1.0, Wet);

        InvalidOperationException error =
            Assert.Throws<InvalidOperationException>(() => ore.Merge(BulkLot.Of(Coal, 1.0, Dry)));
        Assert.Contains("Ore", error.Message, StringComparison.Ordinal);
        Assert.Contains("Coal", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TakeSplitsTheMassAndKeepsTheProperties()
    {
        BulkLot taken = BulkLot.Of(Ore, 5.0, Wet).Take(2.0, out BulkLot remaining);

        Assert.Equal(2.0, taken.Mass, 9);
        Assert.Equal(3.0, remaining.Mass, 9);
        Assert.Equal(Wet, taken.Properties);
        Assert.Equal(Wet, remaining.Properties);
        Assert.Same(Ore, remaining.Type);
    }

    [Fact]
    public void TakingMoreThanHeldTakesEverything()
    {
        BulkLot lot = BulkLot.Of(Ore, 5.0, Wet);

        BulkLot taken = lot.Take(9.0, out BulkLot remaining);

        Assert.Equal(lot, taken);
        Assert.True(remaining.IsEmpty);
    }

    [Fact]
    public void TakingZeroReturnsEmptyAndLeavesTheLot()
    {
        BulkLot lot = BulkLot.Of(Ore, 5.0, Wet);

        BulkLot taken = lot.Take(0.0, out BulkLot remaining);

        Assert.True(taken.IsEmpty);
        Assert.Equal(lot, remaining);
    }

    [Fact]
    public void OfRejectsDiscreteTypesAndNegativeMass()
    {
        Assert.Throws<ArgumentException>(() => BulkLot.Of(Billet, 1.0, Wet));
        Assert.Throws<ArgumentOutOfRangeException>(() => BulkLot.Of(Ore, -1.0, Wet));
    }

    [Fact]
    public void EmptyHasNoTypeAndNoMass()
    {
        Assert.True(BulkLot.Empty.IsEmpty);
        Assert.Null(BulkLot.Empty.Type);
        Assert.Equal(0.0, BulkLot.Empty.Mass);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter FullyQualifiedName~BulkLotTests`
Expected: FAIL — `BulkLot` does not exist.

- [ ] **Step 3: Implement the lot**

`src/Millrace.Core/Flow/BulkLot.cs`:

```csharp
namespace Millrace.Core.Flow;

/// <summary>
/// A parcel of bulk material: what a cell holds and what moves across a link.
/// <see cref="Empty"/> has no type and adopts whatever is merged into it.
/// </summary>
public readonly record struct BulkLot(MaterialType? Type, double Mass, MaterialProperties Properties)
{
    public static BulkLot Empty => default;

    public bool IsEmpty => Mass <= 0.0;

    public static BulkLot Of(MaterialType type, double mass, MaterialProperties properties)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (type.Kind != PayloadKind.Bulk)
        {
            throw new ArgumentException(
                $"Material '{type}' is {type.Kind}; a bulk lot needs a Bulk material type.",
                nameof(type));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(mass);
        return new BulkLot(type, mass, properties);
    }

    /// <summary>Combines two lots of the same material, blending properties by mass.</summary>
    public BulkLot Merge(in BulkLot other)
    {
        if (other.IsEmpty)
        {
            return this;
        }

        if (IsEmpty)
        {
            return other;
        }

        if (!ReferenceEquals(Type, other.Type))
        {
            throw new InvalidOperationException(
                $"Cannot merge '{other.Type}' into a lot of '{Type}'. A cell holds one material " +
                $"type; change the type with an explicit process component before mixing.");
        }

        return new BulkLot(
            Type,
            Mass + other.Mass,
            MaterialProperties.Blend(Properties, Mass, other.Properties, other.Mass));
    }

    /// <summary>
    /// Splits off up to <paramref name="mass"/> kilograms. The taken lot and the
    /// remainder share this lot's properties.
    /// </summary>
    public BulkLot Take(double mass, out BulkLot remaining)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(mass);

        if (mass <= 0.0 || IsEmpty)
        {
            remaining = this;
            return Empty;
        }

        if (mass >= Mass)
        {
            remaining = Empty;
            return this;
        }

        remaining = this with { Mass = Mass - mass };
        return this with { Mass = mass };
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter FullyQualifiedName~BulkLotTests`
Expected: PASS, 9 tests.

- [ ] **Step 5: Commit**

```bash
git add src/Millrace.Core/Flow/BulkLot.cs tests/Millrace.Core.Tests/BulkLotTests.cs
git commit -m "feat(flow): add bulk lots with merge and split"
```

---

### Task 3: Items and the per-simulation id sequence

**Files:**
- Create: `src/Millrace.Core/Flow/ItemIdSequence.cs`
- Create: `src/Millrace.Core/Flow/ItemInstance.cs`
- Modify: `src/Millrace.Core/Contexts/InitContext.cs` (new `items` constructor parameter and `Items` property)
- Modify: `src/Millrace.Core/Simulation.cs` (`Items` property, passed into `InitContext`)
- Modify: `src/Millrace.Core/Flow/PayloadKind.cs` (restore the `<see cref="ItemInstance"/>` on `Discrete`)
- Modify: `tests/Millrace.Core.Tests/ComponentTests.cs:96-97` (`NewInitContext` helper gains the sequence argument)
- Create: `tests/Millrace.Core.Tests/Fakes/TestContexts.cs`
- Test: `tests/Millrace.Core.Tests/ItemTests.cs`

**Interfaces:**
- Consumes: `MaterialType`, `MaterialProperties`, `PayloadKind` (Task 1); `InitContext`, `Simulation` (plan 1).
- Produces:
  - `sealed class ItemIdSequence` with `long Next()`, `long Issued`
  - `sealed class ItemInstance(long id, MaterialType type, double mass, MaterialProperties properties)`
    with `long Id`, `MaterialType Type`, `double Mass { get; set; }`,
    `MaterialProperties Properties { get; set; }`, `double[] State`,
    `void ChangeType(MaterialType newType)`
  - `InitContext(DeterministicRandom random, TelemetryRegistry telemetry, ItemIdSequence items, string componentId, DateTimeOffset startTime, double dt)`
    with `ItemIdSequence Items`
  - `Simulation.Items` (`ItemIdSequence`)
  - test helper `TestContexts.Init(...)`, `TestContexts.Tick(...)`

Spec 7.3: the id comes from a per-simulation monotonic counter so it is
deterministic. The counter is handed to components in `InitContext` — the only
place a component may obtain it — so two components in the same simulation
never mint the same id and two runs mint identical ids.

- [ ] **Step 1: Write the failing tests**

`tests/Millrace.Core.Tests/ItemTests.cs`:

```csharp
using Millrace.Core.Contexts;
using Millrace.Core.Flow;
using Millrace.Core.Tests.Fakes;
using Xunit;

namespace Millrace.Core.Tests;

public class ItemTests
{
    private static readonly MaterialType Billet =
        new("Billet", PayloadKind.Discrete, "CoreTemperature", "TimeAbove1150");

    private static readonly MaterialType Wheel = new("Wheel", PayloadKind.Discrete);

    private static readonly MaterialType Ore = new("Ore", PayloadKind.Bulk);

    [Fact]
    public void SequenceStartsAtOneAndIsMonotonic()
    {
        var sequence = new ItemIdSequence();

        Assert.Equal(0, sequence.Issued);
        Assert.Equal(1, sequence.Next());
        Assert.Equal(2, sequence.Next());
        Assert.Equal(2, sequence.Issued);
    }

    [Fact]
    public void ItemStateIsSizedBySchemaAndStartsAtZero()
    {
        var item = new ItemInstance(7, Billet, 12.5, new MaterialProperties(7800.0, 0.0, 25.0));

        Assert.Equal(7, item.Id);
        Assert.Same(Billet, item.Type);
        Assert.Equal(12.5, item.Mass);
        Assert.Equal(new[] { 0.0, 0.0 }, item.State);
    }

    [Fact]
    public void ChangeTypeResetsStateToTheNewSchema()
    {
        var item = new ItemInstance(1, Billet, 12.5, default);
        item.State[1] = 42.0;

        item.ChangeType(Wheel);

        Assert.Same(Wheel, item.Type);
        Assert.Empty(item.State);
    }

    [Fact]
    public void ItemsRejectBulkTypes()
    {
        Assert.Throws<ArgumentException>(() => new ItemInstance(1, Ore, 1.0, default));
        Assert.Throws<ArgumentException>(() => new ItemInstance(1, Wheel, 1.0, default).ChangeType(Ore));
    }

    [Fact]
    public void InitContextHandsEveryComponentTheSameSequence()
    {
        var shared = new ItemIdSequence();
        InitContext first = TestContexts.Init("A", items: shared);
        InitContext second = TestContexts.Init("B", items: shared);

        Assert.Equal(1, first.Items.Next());
        Assert.Equal(2, second.Items.Next());
    }
}
```

`tests/Millrace.Core.Tests/Fakes/TestContexts.cs`:

```csharp
using Millrace.Core.Contexts;
using Millrace.Core.Flow;
using Millrace.Core.Logging;
using Millrace.Core.Randomness;
using Millrace.Core.Telemetry;

namespace Millrace.Core.Tests.Fakes;

/// <summary>Builds the contexts a component needs when a test drives it without a Simulation.</summary>
public static class TestContexts
{
    public static readonly DateTimeOffset Start = new(2026, 1, 1, 6, 0, 0, TimeSpan.Zero);

    public static InitContext Init(
        string componentId,
        double dt = 0.01,
        TelemetryRegistry? telemetry = null,
        ItemIdSequence? items = null) =>
        new(
            new DeterministicRandom(1UL),
            telemetry ?? new TelemetryRegistry(),
            items ?? new ItemIdSequence(),
            componentId,
            Start,
            dt);

    public static TickContext Tick(long tick, double dt = 0.01, EventLog? log = null) =>
        new(tick, dt, Start + TimeSpan.FromSeconds(tick * dt), log ?? new EventLog());
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter FullyQualifiedName~ItemTests`
Expected: FAIL — `ItemIdSequence` does not exist.

- [ ] **Step 3: Implement the sequence and the item**

`src/Millrace.Core/Flow/ItemIdSequence.cs`:

```csharp
namespace Millrace.Core.Flow;

/// <summary>
/// The simulation's item id counter. Ids are issued in creation order, so a
/// replay mints the same ids for the same items.
/// </summary>
public sealed class ItemIdSequence
{
    private long _next = 1;

    /// <summary>How many ids have been issued so far.</summary>
    public long Issued => _next - 1;

    public long Next() => _next++;
}
```

`src/Millrace.Core/Flow/ItemInstance.cs`:

```csharp
namespace Millrace.Core.Flow;

/// <summary>
/// One discrete piece of material. Mutable, because transforms change its
/// properties and state in place as it moves; identity is the id.
/// </summary>
public sealed class ItemInstance
{
    public ItemInstance(long id, MaterialType type, double mass, MaterialProperties properties)
    {
        ArgumentNullException.ThrowIfNull(type);
        RequireDiscrete(type);
        ArgumentOutOfRangeException.ThrowIfNegative(mass);

        Id = id;
        Type = type;
        Mass = mass;
        Properties = properties;
        State = type.NewState();
    }

    public long Id { get; }

    public MaterialType Type { get; private set; }

    public double Mass { get; set; }

    public MaterialProperties Properties { get; set; }

    /// <summary>Accumulated state, indexed by <see cref="MaterialType.StateIndexOf"/> on <see cref="Type"/>.</summary>
    public double[] State { get; private set; }

    /// <summary>
    /// Re-types the item, as a former or a process unit does on discharge. The
    /// accumulated state belonged to the old type and is reset.
    /// </summary>
    public void ChangeType(MaterialType newType)
    {
        ArgumentNullException.ThrowIfNull(newType);
        RequireDiscrete(newType);

        Type = newType;
        State = newType.NewState();
    }

    public override string ToString() => $"{Type}#{Id}";

    private static void RequireDiscrete(MaterialType type)
    {
        if (type.Kind != PayloadKind.Discrete)
        {
            throw new ArgumentException(
                $"Material '{type}' is {type.Kind}; an item needs a Discrete material type.",
                nameof(type));
        }
    }
}
```

- [ ] **Step 4: Thread the sequence through InitContext and Simulation**

Replace `src/Millrace.Core/Contexts/InitContext.cs` with:

```csharp
using Millrace.Core.Flow;
using Millrace.Core.Randomness;
using Millrace.Core.Telemetry;

namespace Millrace.Core.Contexts;

/// <summary>What a component is given once, before the first tick.</summary>
public readonly struct InitContext
{
    private readonly TelemetryRegistry _telemetry;
    private readonly string _componentId;

    public InitContext(
        DeterministicRandom random,
        TelemetryRegistry telemetry,
        ItemIdSequence items,
        string componentId,
        DateTimeOffset startTime,
        double dt)
    {
        ArgumentNullException.ThrowIfNull(random);
        ArgumentNullException.ThrowIfNull(telemetry);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentException.ThrowIfNullOrWhiteSpace(componentId);

        Random = random;
        Items = items;
        StartTime = startTime;
        Dt = dt;
        _telemetry = telemetry;
        _componentId = componentId;
    }

    /// <summary>This component's own random stream, derived from the master seed and its id.</summary>
    public DeterministicRandom Random { get; }

    /// <summary>
    /// The simulation's item id counter. Components that create items keep a
    /// reference and call <see cref="ItemIdSequence.Next"/> per item; it is the
    /// only source of item ids, which is what makes them deterministic.
    /// </summary>
    public ItemIdSequence Items { get; }

    public DateTimeOffset StartTime { get; }

    public double Dt { get; }

    /// <summary>Registers a telemetry channel, prefixed with this component's id.</summary>
    public TelemetryHandle RegisterTelemetry(string name, string unit) =>
        _telemetry.Register($"{_componentId}.{name}", unit);
}
```

In `src/Millrace.Core/Simulation.cs`:

- add `using Millrace.Core.Flow;` to the usings;
- add, after `public EventLog Events { get; } = new();`:

```csharp
    /// <summary>The item id counter shared by every component in this simulation.</summary>
    public ItemIdSequence Items { get; } = new();
```

- in `Initialize()`, change the `InitContext` construction to pass `Items`
  after `Telemetry`:

```csharp
            var context = new InitContext(
                new DeterministicRandom(Hash64.Combine(_seed, component.Id)),
                Telemetry,
                Items,
                component.Id,
                Clock.StartTime,
                Clock.DeltaSeconds);
```

In `tests/Millrace.Core.Tests/ComponentTests.cs`, add `using Millrace.Core.Flow;` and
change the helper at lines 96–97 to:

```csharp
    private static InitContext NewInitContext(string componentId, TelemetryRegistry registry) =>
        new(new DeterministicRandom(1UL), registry, new ItemIdSequence(), componentId, Start, 0.01);
```

In `src/Millrace.Core/Flow/PayloadKind.cs`, restore the doc comment on `Discrete`
to `/// <summary>Whole <see cref="ItemInstance"/> values that move one at a time.</summary>`.

- [ ] **Step 5: Run the affected tests, then the whole suite**

Run: `dotnet test --filter "FullyQualifiedName~ItemTests|FullyQualifiedName~ComponentTests"`
Expected: PASS, 12 tests (5 new + 7 existing).

Run: `dotnet test`
Expected: PASS, all tests (91 from plan 1 + 9 + 9 + 5 = 114).

- [ ] **Step 6: Commit**

```bash
git add src/Millrace.Core/Flow src/Millrace.Core/Contexts/InitContext.cs src/Millrace.Core/Simulation.cs tests/Millrace.Core.Tests/ItemTests.cs tests/Millrace.Core.Tests/Fakes/TestContexts.cs tests/Millrace.Core.Tests/ComponentTests.cs
git commit -m "feat(flow): add item instances and a per-simulation id sequence"
```

---

### Task 4: Flow ports

**Files:**
- Create: `src/Millrace.Core/Flow/FlowPort.cs`
- Create: `src/Millrace.Core/Flow/FlowInlet.cs`
- Create: `src/Millrace.Core/Flow/FlowOutlet.cs`
- Modify: `src/Millrace.Core/Graph/ComponentBase.cs` (protected `AddPort<TPort>`; `AddInput`/`AddOutput` delegate to it)
- Modify: `src/Millrace.Core/Graph/CompositeComponent.cs` (`Inlet(alias)`, `Outlet(alias)`)
- Test: `tests/Millrace.Core.Tests/FlowPortTests.cs`

**Interfaces:**
- Consumes: `Port`, `ComponentBase`, `CompositeComponent`, `GraphResolver` (plan 1); `PayloadKind` (Task 1).
- Produces:
  - `abstract class FlowPort : Port` with `PayloadKind Kind`;
    `IsMissingRequiredConnection => false`; `SourcePort => null`
  - `sealed class FlowInlet(string name, string ownerId, PayloadKind kind) : FlowPort`
    with `bool IsConnected`, `internal FlowOutlet? Source`, `internal void ConnectFrom(FlowOutlet)`
  - `sealed class FlowOutlet(string name, string ownerId, PayloadKind kind) : FlowPort`
    with `bool IsConnected`, `internal FlowInlet? Target`, `void ConnectTo(FlowInlet inlet)`
  - `ComponentBase`: `protected TPort AddPort<TPort>(TPort port) where TPort : Port`
  - `CompositeComponent`: `FlowInlet Inlet(string alias)`, `FlowOutlet Outlet(string alias)`

Spec 3: "Signals fan out freely; mass cannot." An outlet feeds exactly one
inlet and an inlet has exactly one source; both are enforced at wiring time.
Kind mismatch (bulk outlet to discrete inlet) is also a wiring-time error, so
spec 5.4's "flow links joining incompatible payload kinds" is caught with a
stack trace at the mistake rather than a later error list. Flow ports live in
the component's ordinary `Ports` list so composite qualification reaches them,
but `SourcePort` is `null` so the signal resolver never sees a flow edge.

- [ ] **Step 1: Write the failing tests**

`tests/Millrace.Core.Tests/FlowPortTests.cs`:

```csharp
using Millrace.Core.Contexts;
using Millrace.Core.Flow;
using Millrace.Core.Graph;
using Xunit;

namespace Millrace.Core.Tests;

public class FlowPortTests
{
    [Fact]
    public void ConnectLinksBothEnds()
    {
        var outlet = new FlowOutlet("Out", "A", PayloadKind.Bulk);
        var inlet = new FlowInlet("In", "B", PayloadKind.Bulk);

        outlet.ConnectTo(inlet);

        Assert.True(outlet.IsConnected);
        Assert.True(inlet.IsConnected);
        Assert.Same(inlet, outlet.Target);
        Assert.Same(outlet, inlet.Source);
    }

    [Fact]
    public void KindMismatchThrowsNamingBothPorts()
    {
        var outlet = new FlowOutlet("Out", "A", PayloadKind.Bulk);
        var inlet = new FlowInlet("In", "B", PayloadKind.Discrete);

        InvalidOperationException error =
            Assert.Throws<InvalidOperationException>(() => outlet.ConnectTo(inlet));
        Assert.Contains("A.Out", error.Message, StringComparison.Ordinal);
        Assert.Contains("B.In", error.Message, StringComparison.Ordinal);
        Assert.False(outlet.IsConnected);
        Assert.False(inlet.IsConnected);
    }

    [Fact]
    public void AnOutletFeedsExactlyOneInlet()
    {
        var outlet = new FlowOutlet("Out", "A", PayloadKind.Bulk);
        outlet.ConnectTo(new FlowInlet("In", "B", PayloadKind.Bulk));

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => outlet.ConnectTo(new FlowInlet("In", "C", PayloadKind.Bulk)));
        Assert.Contains("A.Out", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnInletHasExactlyOneSource()
    {
        var inlet = new FlowInlet("In", "C", PayloadKind.Bulk);
        new FlowOutlet("Out", "A", PayloadKind.Bulk).ConnectTo(inlet);

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => new FlowOutlet("Out", "B", PayloadKind.Bulk).ConnectTo(inlet));
        Assert.Contains("C.In", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FlowLinksDoNotOrderSignalEvaluation()
    {
        var producer = new BareNode("P");
        var consumer = new BareNode("C");
        producer.Out.ConnectTo(consumer.In);

        // Registered consumer-first; a signal edge would reorder them.
        Assert.True(GraphResolver.TryResolve([consumer, producer], out ISimComponent[] ordered, out _));

        Assert.Equal(new[] { "C", "P" }, ordered.Select(c => c.Id));
        Assert.Null(consumer.In.SourcePort);
        Assert.False(consumer.In.IsMissingRequiredConnection);
    }

    [Fact]
    public void FlowPortsAreQualifiedAndExposedByComposites()
    {
        var composite = new Wrapper("CV001");

        Assert.Equal("CV001.Node.In", composite.Inlet("Feed").QualifiedName);
        Assert.Equal("CV001.Node.Out", composite.Outlet("Discharge").QualifiedName);
        Assert.Throws<InvalidCastException>(() => composite.Input<double>("Feed"));
    }

    private sealed class BareNode : ComponentBase
    {
        public BareNode(string id)
            : base(id)
        {
            In = AddPort(new FlowInlet("In", Id, PayloadKind.Bulk));
            Out = AddPort(new FlowOutlet("Out", Id, PayloadKind.Bulk));
        }

        public FlowInlet In { get; }

        public FlowOutlet Out { get; }

        public override void Evaluate(in TickContext ctx)
        {
        }
    }

    private sealed class Wrapper : CompositeComponent
    {
        public Wrapper(string id)
            : base(id)
        {
            BareNode node = AddChild(new BareNode("Node"));
            Expose("Feed", node.In);
            Expose("Discharge", node.Out);
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter FullyQualifiedName~FlowPortTests`
Expected: FAIL — `FlowOutlet` does not exist.

- [ ] **Step 3: Implement the ports**

`src/Millrace.Core/Flow/FlowPort.cs`:

```csharp
using Millrace.Core.Graph;

namespace Millrace.Core.Flow;

/// <summary>
/// A material connection point. Flow ports never create signal-ordering edges:
/// transport runs in phase 3, after every component has evaluated, so the
/// signal resolver must not see them as dependencies.
/// </summary>
public abstract class FlowPort : Port
{
    protected FlowPort(string name, string ownerId, PayloadKind kind)
        : base(name, ownerId)
    {
        Kind = kind;
    }

    public PayloadKind Kind { get; }

    public override bool IsMissingRequiredConnection => false;

    internal override Port? SourcePort => null;
}
```

`src/Millrace.Core/Flow/FlowInlet.cs`:

```csharp
namespace Millrace.Core.Flow;

/// <summary>Where material enters a node. Exactly one source, because mass cannot merge implicitly.</summary>
public sealed class FlowInlet : FlowPort
{
    private FlowOutlet? _source;

    public FlowInlet(string name, string ownerId, PayloadKind kind)
        : base(name, ownerId, kind)
    {
    }

    public bool IsConnected => _source is not null;

    /// <summary>The outlet feeding this inlet, or null. Read by the flow resolver.</summary>
    internal FlowOutlet? Source => _source;

    internal void ConnectFrom(FlowOutlet source)
    {
        if (_source is not null)
        {
            throw new InvalidOperationException(
                $"Inlet '{QualifiedName}' is already fed by '{_source.QualifiedName}'. " +
                $"An inlet has exactly one source; give each stream its own inlet.");
        }

        _source = source;
    }
}
```

`src/Millrace.Core/Flow/FlowOutlet.cs`:

```csharp
namespace Millrace.Core.Flow;

/// <summary>Where material leaves a node. Feeds exactly one inlet, because mass cannot fan out.</summary>
public sealed class FlowOutlet : FlowPort
{
    private FlowInlet? _target;

    public FlowOutlet(string name, string ownerId, PayloadKind kind)
        : base(name, ownerId, kind)
    {
    }

    public bool IsConnected => _target is not null;

    /// <summary>The inlet this outlet feeds, or null. Read by the flow resolver.</summary>
    internal FlowInlet? Target => _target;

    public void ConnectTo(FlowInlet inlet)
    {
        ArgumentNullException.ThrowIfNull(inlet);

        if (inlet.Kind != Kind)
        {
            throw new InvalidOperationException(
                $"Cannot connect {Kind} outlet '{QualifiedName}' to {inlet.Kind} inlet " +
                $"'{inlet.QualifiedName}'. Material changes kind only through an explicit " +
                $"component such as a former.");
        }

        if (_target is not null)
        {
            throw new InvalidOperationException(
                $"Outlet '{QualifiedName}' already feeds '{_target.QualifiedName}'. " +
                $"An outlet feeds exactly one inlet; mass cannot fan out.");
        }

        inlet.ConnectFrom(this);
        _target = inlet;
    }
}
```

- [ ] **Step 4: Open port registration on ComponentBase and alias lookup on composites**

In `src/Millrace.Core/Graph/ComponentBase.cs`, replace the two `Add…` methods with:

```csharp
    /// <summary>
    /// Registers a port on this component. Ports are listed in registration
    /// order and their owner id is rewritten when a composite qualifies this
    /// component, so every port — signal or flow — must go through here.
    /// </summary>
    protected TPort AddPort<TPort>(TPort port)
        where TPort : Port
    {
        ArgumentNullException.ThrowIfNull(port);
        if (!string.Equals(port.OwnerId, Id, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Port '{port.QualifiedName}' belongs to '{port.OwnerId}', not to '{Id}'. " +
                $"Construct ports with this component's Id.",
                nameof(port));
        }

        _ports.Add(port);
        return port;
    }

    protected InputPort<T> AddInput<T>(string name, T defaultValue = default, bool required = false)
        where T : unmanaged =>
        AddPort(new InputPort<T>(name, Id, defaultValue, required));

    protected OutputPort<T> AddOutput<T>(string name)
        where T : unmanaged =>
        AddPort(new OutputPort<T>(name, Id));
```

In `src/Millrace.Core/Graph/CompositeComponent.cs`, add `using Millrace.Core.Flow;` and,
directly after `Output<T>`:

```csharp
    public FlowInlet Inlet(string alias) => Resolve<FlowInlet>(alias);

    public FlowOutlet Outlet(string alias) => Resolve<FlowOutlet>(alias);
```

- [ ] **Step 5: Run the affected tests, then the whole suite**

Run: `dotnet test --filter "FullyQualifiedName~FlowPortTests|FullyQualifiedName~ComponentTests|FullyQualifiedName~CompositeComponentTests"`
Expected: PASS, 19 tests (6 new + 7 + 6 existing).

Run: `dotnet test`
Expected: PASS, all tests.

- [ ] **Step 6: Commit**

```bash
git add src/Millrace.Core/Flow src/Millrace.Core/Graph/ComponentBase.cs src/Millrace.Core/Graph/CompositeComponent.cs tests/Millrace.Core.Tests/FlowPortTests.cs
git commit -m "feat(flow): add single-source, single-target material ports"
```

---

### Task 5: Flow node contracts and test nodes

**Files:**
- Create: `src/Millrace.Core/Flow/IFlowNode.cs`
- Create: `src/Millrace.Core/Flow/IBulkProducer.cs`, `IBulkConsumer.cs`, `IItemProducer.cs`, `IItemConsumer.cs`
- Create: `src/Millrace.Core/Flow/FlowComponentBase.cs`
- Create: `tests/Millrace.Core.Tests/Fakes/Flow/BulkFeeder.cs`, `BulkSink.cs`, `BulkBuffer.cs`, `ItemFeeder.cs`, `ItemSink.cs`, `ItemBuffer.cs`
- Test: `tests/Millrace.Core.Tests/FlowNodeTests.cs`

**Interfaces:**
- Consumes: `ComponentBase`, `ISimComponent`, `ValidationError` (plan 1); Tasks 1–4.
- Produces:
  - `interface IFlowNode : ISimComponent` with `double MassHeld`, `double MassCreated`,
    `double MassDestroyed`, `void Advance(double dt)`, `IEnumerable<ValidationError> ValidateFlow(double dt)`
  - `interface IBulkProducer` with `double OfferMass(FlowOutlet outlet)`, `BulkLot Withdraw(FlowOutlet outlet, double mass)`
  - `interface IBulkConsumer` with `double AcceptMass(FlowInlet inlet)`, `void Deposit(FlowInlet inlet, in BulkLot lot)`
  - `interface IItemProducer` with `bool TryPeekItem(FlowOutlet outlet, [NotNullWhen(true)] out ItemInstance? item)`, `ItemInstance WithdrawItem(FlowOutlet outlet)`
  - `interface IItemConsumer` with `bool CanAcceptItem(FlowInlet inlet, ItemInstance item)`, `void DepositItem(FlowInlet inlet, ItemInstance item)`
  - `abstract class FlowComponentBase : ComponentBase, IFlowNode` with abstract `MassHeld`,
    virtual `MassCreated`/`MassDestroyed` (0), virtual `Advance` (no-op), virtual
    `ValidateFlow` (none), virtual `Evaluate` (no-op), protected `AddInlet(name, kind)`, `AddOutlet(name, kind)`
  - test nodes: `BulkFeeder`, `BulkSink`, `BulkBuffer`, `ItemFeeder`, `ItemSink`, `ItemBuffer`

The protocol is spec 7.4 made concrete. Bulk: the producer offers a mass, the
consumer states what it can accept, the engine moves the minimum by calling
`Withdraw` then `Deposit`. Discrete: the producer shows its head item, the
consumer says whether it fits, the engine moves whole items until one side says
no. `WithdrawItem` must remove exactly the item last shown by `TryPeekItem`.
`Advance(dt)` is the node's own internal motion, run after its outgoing links
have been resolved.

- [ ] **Step 1: Write the failing tests**

`tests/Millrace.Core.Tests/FlowNodeTests.cs`:

```csharp
using Millrace.Core.Contexts;
using Millrace.Core.Flow;
using Millrace.Core.Tests.Fakes;
using Millrace.Core.Tests.Fakes.Flow;
using Xunit;

namespace Millrace.Core.Tests;

public class FlowNodeTests
{
    private static readonly MaterialType Ore = new("Ore", PayloadKind.Bulk);
    private static readonly MaterialType Wheel = new("Wheel", PayloadKind.Discrete);

    [Fact]
    public void FlowComponentBaseDefaultsToNoCreationNoDestructionAndNoValidationErrors()
    {
        var buffer = new BulkBuffer("B", capacityKg: 10.0);

        Assert.Equal(0.0, buffer.MassCreated);
        Assert.Equal(0.0, buffer.MassDestroyed);
        Assert.Empty(buffer.ValidateFlow(0.01));
        buffer.Advance(0.01);
        buffer.Evaluate(TestContexts.Tick(0));
        Assert.Equal(0.0, buffer.MassHeld);
    }

    [Fact]
    public void FlowPortsAreRegisteredWithTheOwnerId()
    {
        var buffer = new BulkBuffer("B", 10.0);

        Assert.Equal(new[] { "In", "Out" }, buffer.Ports.Select(p => p.Name));
        Assert.All(buffer.Ports, p => Assert.Equal("B", p.OwnerId));
        Assert.Equal(PayloadKind.Bulk, buffer.In.Kind);
    }

    [Fact]
    public void BulkFeederCreatesRateTimesDtEachTickAndCountsIt()
    {
        var feeder = new BulkFeeder("F", Ore, rateKgPerSecond: 10.0);

        for (int tick = 0; tick < 5; tick++)
        {
            feeder.Evaluate(TestContexts.Tick(tick));
        }

        Assert.Equal(0.5, feeder.MassCreated, 9);
        Assert.Equal(0.5, feeder.MassHeld, 9);
        Assert.Equal(0.5, feeder.OfferMass(feeder.Out), 9);
    }

    [Fact]
    public void BulkFeederWithdrawHandsOverMassAndKeepsTheRest()
    {
        var feeder = new BulkFeeder("F", Ore, 100.0);
        feeder.Evaluate(TestContexts.Tick(0));

        BulkLot lot = feeder.Withdraw(feeder.Out, 0.25);

        Assert.Equal(0.25, lot.Mass, 9);
        Assert.Same(Ore, lot.Type);
        Assert.Equal(0.75, feeder.MassHeld, 9);
    }

    [Fact]
    public void BulkBufferAcceptsUpToItsCapacity()
    {
        var buffer = new BulkBuffer("B", 4.0);

        Assert.Equal(4.0, buffer.AcceptMass(buffer.In));
        buffer.Deposit(buffer.In, BulkLot.Of(Ore, 3.0, default));
        Assert.Equal(1.0, buffer.AcceptMass(buffer.In), 9);
        Assert.Equal(3.0, buffer.OfferMass(buffer.Out), 9);
    }

    [Fact]
    public void BulkSinkDestroysEverythingItReceives()
    {
        var sink = new BulkSink("S");

        sink.Deposit(sink.In, BulkLot.Of(Ore, 2.0, new MaterialProperties(1.0, 0.5, 9.0)));
        sink.Deposit(sink.In, BulkLot.Of(Ore, 3.0, default));

        Assert.Equal(double.PositiveInfinity, sink.AcceptMass(sink.In));
        Assert.Equal(5.0, sink.MassDestroyed, 9);
        Assert.Equal(new[] { 2.0, 3.0 }, sink.Received);
        Assert.Equal(0.0, sink.MassHeld);
    }

    [Fact]
    public void ItemFeederMintsIdsFromTheContextSequence()
    {
        var feeder = new ItemFeeder("F", Wheel, itemMassKg: 2.0, intervalSeconds: 0.02);
        feeder.Initialize(TestContexts.Init("F", dt: 0.01));

        for (int tick = 0; tick < 6; tick++)
        {
            feeder.Evaluate(TestContexts.Tick(tick));
        }

        Assert.True(feeder.TryPeekItem(feeder.Out, out ItemInstance? head));
        Assert.Equal(1, head!.Id);
        Assert.Same(head, feeder.WithdrawItem(feeder.Out));
        Assert.True(feeder.TryPeekItem(feeder.Out, out ItemInstance? next));
        Assert.Equal(2, next!.Id);
        Assert.Equal(6.0, feeder.MassCreated, 9);
        Assert.Equal(4.0, feeder.MassHeld, 9);
    }

    [Fact]
    public void ItemBufferAcceptsUpToItsCountAndKeepsOrder()
    {
        var buffer = new ItemBuffer("B", capacity: 2);
        var first = new ItemInstance(1, Wheel, 1.0, default);
        var second = new ItemInstance(2, Wheel, 1.0, default);
        var third = new ItemInstance(3, Wheel, 1.0, default);

        Assert.True(buffer.CanAcceptItem(buffer.In, first));
        buffer.DepositItem(buffer.In, first);
        buffer.DepositItem(buffer.In, second);

        Assert.False(buffer.CanAcceptItem(buffer.In, third));
        Assert.True(buffer.TryPeekItem(buffer.Out, out ItemInstance? head));
        Assert.Same(first, head);
        Assert.Equal(2.0, buffer.MassHeld, 9);
    }

    [Fact]
    public void ItemSinkDestroysItemMass()
    {
        var sink = new ItemSink("S");
        var item = new ItemInstance(1, Wheel, 2.5, default);

        Assert.True(sink.CanAcceptItem(sink.In, item));
        sink.DepositItem(sink.In, item);

        Assert.Equal(2.5, sink.MassDestroyed, 9);
        Assert.Same(item, Assert.Single(sink.Items));
    }
}
```

- [ ] **Step 2: Write the test nodes**

`tests/Millrace.Core.Tests/Fakes/Flow/BulkFeeder.cs`:

```csharp
using Millrace.Core.Contexts;
using Millrace.Core.Flow;

namespace Millrace.Core.Tests.Fakes.Flow;

/// <summary>
/// Creates bulk material at a rate each tick and offers everything it holds.
/// A blocked outlet accumulates, like a feed hopper.
/// </summary>
public sealed class BulkFeeder : FlowComponentBase, IBulkProducer
{
    private readonly MaterialType _type;
    private readonly MaterialProperties _properties;
    private BulkLot _hopper;
    private double _created;

    public BulkFeeder(string id, MaterialType type, double rateKgPerSecond, MaterialProperties properties = default)
        : base(id)
    {
        _type = type;
        _properties = properties;
        RateKgPerSecond = rateKgPerSecond;
        Out = AddOutlet("Out", PayloadKind.Bulk);
    }

    public FlowOutlet Out { get; }

    public double RateKgPerSecond { get; set; }

    public override double MassHeld => _hopper.Mass;

    public override double MassCreated => _created;

    public override void Evaluate(in TickContext ctx)
    {
        double mass = RateKgPerSecond * ctx.Dt;
        if (mass <= 0.0)
        {
            return;
        }

        _hopper = _hopper.Merge(BulkLot.Of(_type, mass, _properties));
        _created += mass;
    }

    public double OfferMass(FlowOutlet outlet) => _hopper.Mass;

    public BulkLot Withdraw(FlowOutlet outlet, double mass)
    {
        BulkLot taken = _hopper.Take(mass, out BulkLot remaining);
        _hopper = remaining;
        return taken;
    }
}
```

`tests/Millrace.Core.Tests/Fakes/Flow/BulkSink.cs`:

```csharp
using Millrace.Core.Flow;

namespace Millrace.Core.Tests.Fakes.Flow;

/// <summary>Accepts everything and removes it from the system, recording each deposit.</summary>
public sealed class BulkSink : FlowComponentBase, IBulkConsumer
{
    private readonly List<double> _received = [];
    private double _sunk;

    public BulkSink(string id)
        : base(id) => In = AddInlet("In", PayloadKind.Bulk);

    public FlowInlet In { get; }

    /// <summary>Mass of each deposit, in order.</summary>
    public IReadOnlyList<double> Received => _received;

    public double TotalReceived => _sunk;

    public MaterialProperties LastProperties { get; private set; }

    public override double MassHeld => 0.0;

    public override double MassDestroyed => _sunk;

    public double AcceptMass(FlowInlet inlet) => double.PositiveInfinity;

    public void Deposit(FlowInlet inlet, in BulkLot lot)
    {
        _sunk += lot.Mass;
        _received.Add(lot.Mass);
        LastProperties = lot.Properties;
    }
}
```

`tests/Millrace.Core.Tests/Fakes/Flow/BulkBuffer.cs`:

```csharp
using Millrace.Core.Flow;

namespace Millrace.Core.Tests.Fakes.Flow;

/// <summary>A capacity-limited hold, like a transfer chute. Offers everything it holds.</summary>
public sealed class BulkBuffer : FlowComponentBase, IBulkConsumer, IBulkProducer
{
    private BulkLot _held;

    public BulkBuffer(string id, double capacityKg)
        : base(id)
    {
        CapacityKg = capacityKg;
        In = AddInlet("In", PayloadKind.Bulk);
        Out = AddOutlet("Out", PayloadKind.Bulk);
    }

    public FlowInlet In { get; }

    public FlowOutlet Out { get; }

    public double CapacityKg { get; }

    public BulkLot Contents => _held;

    public override double MassHeld => _held.Mass;

    public double AcceptMass(FlowInlet inlet) => Math.Max(0.0, CapacityKg - _held.Mass);

    public void Deposit(FlowInlet inlet, in BulkLot lot) => _held = _held.Merge(lot);

    public double OfferMass(FlowOutlet outlet) => _held.Mass;

    public BulkLot Withdraw(FlowOutlet outlet, double mass)
    {
        BulkLot taken = _held.Take(mass, out BulkLot remaining);
        _held = remaining;
        return taken;
    }
}
```

`tests/Millrace.Core.Tests/Fakes/Flow/ItemFeeder.cs`:

```csharp
using System.Diagnostics.CodeAnalysis;
using Millrace.Core.Contexts;
using Millrace.Core.Flow;

namespace Millrace.Core.Tests.Fakes.Flow;

/// <summary>Mints one item every interval, ids from the simulation's sequence, and queues them for discharge.</summary>
public sealed class ItemFeeder : FlowComponentBase, IItemProducer
{
    private readonly Queue<ItemInstance> _ready = new();
    private readonly MaterialType _type;
    private readonly double _itemMass;
    private ItemIdSequence? _ids;
    private double _elapsed;
    private double _created;

    public ItemFeeder(string id, MaterialType type, double itemMassKg, double intervalSeconds)
        : base(id)
    {
        _type = type;
        _itemMass = itemMassKg;
        IntervalSeconds = intervalSeconds;
        Out = AddOutlet("Out", PayloadKind.Discrete);
    }

    public FlowOutlet Out { get; }

    /// <summary>Seconds between items. Zero or negative stops production.</summary>
    public double IntervalSeconds { get; set; }

    public override double MassHeld
    {
        get
        {
            double total = 0.0;
            foreach (ItemInstance item in _ready)
            {
                total += item.Mass;
            }

            return total;
        }
    }

    public override double MassCreated => _created;

    public override void Initialize(in InitContext ctx) => _ids = ctx.Items;

    public override void Evaluate(in TickContext ctx)
    {
        if (IntervalSeconds <= 0.0)
        {
            return;
        }

        _elapsed += ctx.Dt;
        while (_elapsed >= IntervalSeconds - 1e-12)
        {
            _elapsed -= IntervalSeconds;
            _ready.Enqueue(new ItemInstance(_ids!.Next(), _type, _itemMass, default));
            _created += _itemMass;
        }
    }

    public bool TryPeekItem(FlowOutlet outlet, [NotNullWhen(true)] out ItemInstance? item) =>
        _ready.TryPeek(out item);

    public ItemInstance WithdrawItem(FlowOutlet outlet) => _ready.Dequeue();
}
```

`tests/Millrace.Core.Tests/Fakes/Flow/ItemSink.cs`:

```csharp
using Millrace.Core.Flow;

namespace Millrace.Core.Tests.Fakes.Flow;

/// <summary>Accepts every item and removes it from the system.</summary>
public sealed class ItemSink : FlowComponentBase, IItemConsumer
{
    private readonly List<ItemInstance> _items = [];
    private double _sunk;

    public ItemSink(string id)
        : base(id) => In = AddInlet("In", PayloadKind.Discrete);

    public FlowInlet In { get; }

    public IReadOnlyList<ItemInstance> Items => _items;

    public override double MassHeld => 0.0;

    public override double MassDestroyed => _sunk;

    public bool CanAcceptItem(FlowInlet inlet, ItemInstance item) => true;

    public void DepositItem(FlowInlet inlet, ItemInstance item)
    {
        _items.Add(item);
        _sunk += item.Mass;
    }
}
```

`tests/Millrace.Core.Tests/Fakes/Flow/ItemBuffer.cs`:

```csharp
using System.Diagnostics.CodeAnalysis;
using Millrace.Core.Flow;

namespace Millrace.Core.Tests.Fakes.Flow;

/// <summary>A FIFO hold for a fixed number of items.</summary>
public sealed class ItemBuffer : FlowComponentBase, IItemConsumer, IItemProducer
{
    private readonly Queue<ItemInstance> _items = new();

    public ItemBuffer(string id, int capacity)
        : base(id)
    {
        Capacity = capacity;
        In = AddInlet("In", PayloadKind.Discrete);
        Out = AddOutlet("Out", PayloadKind.Discrete);
    }

    public FlowInlet In { get; }

    public FlowOutlet Out { get; }

    public int Capacity { get; }

    public int Count => _items.Count;

    public override double MassHeld
    {
        get
        {
            double total = 0.0;
            foreach (ItemInstance item in _items)
            {
                total += item.Mass;
            }

            return total;
        }
    }

    public bool CanAcceptItem(FlowInlet inlet, ItemInstance item) => _items.Count < Capacity;

    public void DepositItem(FlowInlet inlet, ItemInstance item) => _items.Enqueue(item);

    public bool TryPeekItem(FlowOutlet outlet, [NotNullWhen(true)] out ItemInstance? item) =>
        _items.TryPeek(out item);

    public ItemInstance WithdrawItem(FlowOutlet outlet) => _items.Dequeue();
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test --filter FullyQualifiedName~FlowNodeTests`
Expected: FAIL — `FlowComponentBase` does not exist.

- [ ] **Step 4: Implement the contracts**

`src/Millrace.Core/Flow/IFlowNode.cs`:

```csharp
using Millrace.Core.Graph;
using Millrace.Core.Validation;

namespace Millrace.Core.Flow;

/// <summary>
/// A component that holds material. It reports its inventory so the engine can
/// audit conservation every tick, and it advances its own contents once its
/// outgoing links have been resolved.
/// </summary>
public interface IFlowNode : ISimComponent
{
    /// <summary>Kilograms currently resident in this node.</summary>
    double MassHeld { get; }

    /// <summary>Cumulative kilograms this node has injected from outside the plant (sources).</summary>
    double MassCreated { get; }

    /// <summary>Cumulative kilograms this node has removed from the plant (sinks, declared losses).</summary>
    double MassDestroyed { get; }

    /// <summary>
    /// Internal motion for one time step: cells shift, items travel, transforms
    /// run. Called after this node's outgoing links have transferred and after
    /// every downstream node has already advanced.
    /// </summary>
    void Advance(double dt);

    /// <summary>Flow-specific build-time checks, such as the CFL condition. Empty when valid.</summary>
    IEnumerable<ValidationError> ValidateFlow(double dt);
}
```

`src/Millrace.Core/Flow/IBulkProducer.cs`:

```csharp
namespace Millrace.Core.Flow;

/// <summary>A node that discharges bulk material through one or more outlets.</summary>
public interface IBulkProducer
{
    /// <summary>Kilograms this node wants to push through <paramref name="outlet"/> this tick.</summary>
    double OfferMass(FlowOutlet outlet);

    /// <summary>Removes up to <paramref name="mass"/> kilograms from behind <paramref name="outlet"/> and returns them.</summary>
    BulkLot Withdraw(FlowOutlet outlet, double mass);
}
```

`src/Millrace.Core/Flow/IBulkConsumer.cs`:

```csharp
namespace Millrace.Core.Flow;

/// <summary>A node that receives bulk material through one or more inlets.</summary>
public interface IBulkConsumer
{
    /// <summary>Kilograms this node can take through <paramref name="inlet"/> this tick. Infinity for an unbounded sink.</summary>
    double AcceptMass(FlowInlet inlet);

    void Deposit(FlowInlet inlet, in BulkLot lot);
}
```

`src/Millrace.Core/Flow/IItemProducer.cs`:

```csharp
using System.Diagnostics.CodeAnalysis;

namespace Millrace.Core.Flow;

/// <summary>A node that discharges whole items through one or more outlets.</summary>
public interface IItemProducer
{
    /// <summary>The item ready to leave through <paramref name="outlet"/>, if any. Does not remove it.</summary>
    bool TryPeekItem(FlowOutlet outlet, [NotNullWhen(true)] out ItemInstance? item);

    /// <summary>Removes and returns the item last shown by <see cref="TryPeekItem"/> for this outlet.</summary>
    ItemInstance WithdrawItem(FlowOutlet outlet);
}
```

`src/Millrace.Core/Flow/IItemConsumer.cs`:

```csharp
namespace Millrace.Core.Flow;

/// <summary>A node that receives whole items through one or more inlets.</summary>
public interface IItemConsumer
{
    /// <summary>Whether <paramref name="item"/> fits through <paramref name="inlet"/> right now.</summary>
    bool CanAcceptItem(FlowInlet inlet, ItemInstance item);

    void DepositItem(FlowInlet inlet, ItemInstance item);
}
```

`src/Millrace.Core/Flow/FlowComponentBase.cs`:

```csharp
using Millrace.Core.Contexts;
using Millrace.Core.Graph;
using Millrace.Core.Validation;

namespace Millrace.Core.Flow;

/// <summary>
/// Base for components that hold material. Most flow nodes neither create nor
/// destroy mass, need no validation of their own, and have nothing to do in the
/// signal phase, so those default to nothing; only the inventory is mandatory.
/// </summary>
public abstract class FlowComponentBase : ComponentBase, IFlowNode
{
    protected FlowComponentBase(string id)
        : base(id)
    {
    }

    public abstract double MassHeld { get; }

    public virtual double MassCreated => 0.0;

    public virtual double MassDestroyed => 0.0;

    public override void Evaluate(in TickContext ctx)
    {
    }

    public virtual void Advance(double dt)
    {
    }

    public virtual IEnumerable<ValidationError> ValidateFlow(double dt) => [];

    protected FlowInlet AddInlet(string name, PayloadKind kind) =>
        AddPort(new FlowInlet(name, Id, kind));

    protected FlowOutlet AddOutlet(string name, PayloadKind kind) =>
        AddPort(new FlowOutlet(name, Id, kind));
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test --filter FullyQualifiedName~FlowNodeTests`
Expected: PASS, 9 tests.

- [ ] **Step 6: Commit**

```bash
git add src/Millrace.Core/Flow tests/Millrace.Core.Tests/Fakes/Flow tests/Millrace.Core.Tests/FlowNodeTests.cs
git commit -m "feat(flow): add flow node contracts, offer/accept interfaces and test nodes"
```

---

### Task 6: Shared topological sorter

**Files:**
- Create: `src/Millrace.Core/Graph/TopologicalSorter.cs`
- Modify: `src/Millrace.Core/Graph/GraphResolver.cs` (delegates sorting and cycle naming)
- Test: `tests/Millrace.Core.Tests/TopologicalSorterTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `internal static class TopologicalSorter` with
    `static bool TrySort(List<int>[] dependents, out int[] order, out List<int> cycle)`
  - `GraphResolver.TryResolve` keeps its public signature and behaviour.

The flow graph needs exactly the sort the signal resolver already has — Kahn's
algorithm with registration-index tie-break and a backward-walking cycle
finder — so the algorithm moves to a shared, index-based class and both
resolvers map indices to ids. The existing `GraphResolverTests` are the net
for this refactor: they must pass unchanged.

- [ ] **Step 1: Write the failing tests**

`tests/Millrace.Core.Tests/TopologicalSorterTests.cs`:

```csharp
using Millrace.Core.Graph;
using Xunit;

namespace Millrace.Core.Tests;

public class TopologicalSorterTests
{
    private static List<int>[] Graph(int count, params (int From, int To)[] edges)
    {
        var dependents = new List<int>[count];
        for (int i = 0; i < count; i++)
        {
            dependents[i] = [];
        }

        foreach ((int from, int to) in edges)
        {
            dependents[from].Add(to);
        }

        return dependents;
    }

    [Fact]
    public void PlacesProducersBeforeConsumers()
    {
        Assert.True(TopologicalSorter.TrySort(Graph(3, (2, 1), (1, 0)), out int[] order, out _));

        Assert.Equal(new[] { 2, 1, 0 }, order);
    }

    [Fact]
    public void TiesBreakByIndex()
    {
        Assert.True(TopologicalSorter.TrySort(Graph(3), out int[] independent, out _));
        Assert.True(TopologicalSorter.TrySort(Graph(3, (2, 0)), out int[] oneEdge, out _));

        Assert.Equal(new[] { 0, 1, 2 }, independent);
        Assert.Equal(new[] { 1, 2, 0 }, oneEdge);
    }

    [Fact]
    public void NamesTheCycleInEdgeOrder()
    {
        List<int>[] dependents = Graph(3, (0, 1), (1, 2), (2, 0));

        Assert.False(TopologicalSorter.TrySort(dependents, out int[] order, out List<int> cycle));

        Assert.Empty(order);
        Assert.Equal(3, cycle.Count);
        for (int i = 0; i < cycle.Count; i++)
        {
            int next = cycle[(i + 1) % cycle.Count];
            Assert.Contains(next, dependents[cycle[i]]);
        }
    }

    [Fact]
    public void AnObserverOfACycleIsNotReported()
    {
        // 0 consumes from 1; 1 and 2 feed each other.
        Assert.False(TopologicalSorter.TrySort(Graph(3, (1, 0), (1, 2), (2, 1)), out _, out List<int> cycle));

        Assert.Equal(2, cycle.Count);
        Assert.Contains(1, cycle);
        Assert.Contains(2, cycle);
        Assert.DoesNotContain(0, cycle);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter FullyQualifiedName~TopologicalSorterTests`
Expected: FAIL — `TopologicalSorter` does not exist.

- [ ] **Step 3: Implement the sorter**

`src/Millrace.Core/Graph/TopologicalSorter.cs`:

```csharp
namespace Millrace.Core.Graph;

/// <summary>
/// Kahn's algorithm over an adjacency list, ties broken by index so the order
/// is stable for a given registration order. Shared by the signal resolver and
/// the material-flow resolver; each maps indices to its own ids.
/// </summary>
internal static class TopologicalSorter
{
    /// <summary>
    /// <paramref name="dependents"/>[i] lists the indices that must come after i.
    /// On failure <paramref name="cycle"/> names one loop in edge order.
    /// </summary>
    public static bool TrySort(List<int>[] dependents, out int[] order, out List<int> cycle)
    {
        ArgumentNullException.ThrowIfNull(dependents);

        int count = dependents.Length;
        var inDegree = new int[count];
        for (int i = 0; i < count; i++)
        {
            foreach (int dependent in dependents[i])
            {
                inDegree[dependent]++;
            }
        }

        // Ready set kept sorted by index so ordering is stable.
        var ready = new SortedSet<int>();
        for (int i = 0; i < count; i++)
        {
            if (inDegree[i] == 0)
            {
                ready.Add(i);
            }
        }

        var result = new int[count];
        int placed = 0;
        while (ready.Count > 0)
        {
            int next = ready.Min;
            ready.Remove(next);
            result[placed++] = next;

            foreach (int dependent in dependents[next])
            {
                if (--inDegree[dependent] == 0)
                {
                    ready.Add(dependent);
                }
            }
        }

        if (placed == count)
        {
            order = result;
            cycle = [];
            return true;
        }

        order = [];
        cycle = FindCycle(dependents, inDegree);
        return false;
    }

    /// <summary>
    /// Walks the residual subgraph's predecessors — backward, from a stalled
    /// node toward whatever still owes it an input — to name one loop. A
    /// forward walk over dependents can dead-end on a residual node that merely
    /// consumes from a cycle without being part of it (an observer); every
    /// residual node is guaranteed a residual predecessor, so the backward walk
    /// cannot dead-end.
    /// </summary>
    private static List<int> FindCycle(List<int>[] dependents, int[] inDegree)
    {
        int start = -1;
        for (int i = 0; i < inDegree.Length; i++)
        {
            if (inDegree[i] > 0)
            {
                start = i;
                break;
            }
        }

        if (start < 0)
        {
            return [];
        }

        int count = inDegree.Length;
        var predecessors = new List<int>[count];
        for (int i = 0; i < count; i++)
        {
            predecessors[i] = [];
        }

        for (int producer = 0; producer < count; producer++)
        {
            if (inDegree[producer] <= 0)
            {
                continue;
            }

            foreach (int dependent in dependents[producer])
            {
                if (inDegree[dependent] > 0)
                {
                    predecessors[dependent].Add(producer);
                }
            }
        }

        var path = new List<int>();
        var onPath = new Dictionary<int, int>();
        int current = start;
        while (!onPath.ContainsKey(current))
        {
            onPath[current] = path.Count;
            path.Add(current);
            current = predecessors[current][0];
        }

        List<int> loop = path.Skip(onPath[current]).ToList();
        loop.Reverse();
        return loop;
    }
}
```

- [ ] **Step 4: Make GraphResolver delegate**

Replace `src/Millrace.Core/Graph/GraphResolver.cs` with:

```csharp
namespace Millrace.Core.Graph;

/// <summary>
/// Turns a wired component set into a fixed evaluation order. That order is the
/// determinism guarantee, so it is computed once, at build time, and the graph
/// is immutable afterwards.
/// </summary>
public static class GraphResolver
{
    /// <summary>
    /// Topologically sorts <paramref name="components"/>. On failure
    /// <paramref name="cycle"/> names the components in one unresolvable loop.
    /// </summary>
    public static bool TryResolve(
        IReadOnlyList<ISimComponent> components,
        out ISimComponent[] ordered,
        out IReadOnlyList<string> cycle)
    {
        ArgumentNullException.ThrowIfNull(components);

        int count = components.Count;
        var indexById = new Dictionary<string, int>(count, StringComparer.Ordinal);
        for (int i = 0; i < count; i++)
        {
            indexById[components[i].Id] = i;
        }

        // dependents[i] = indices of components that must run after i.
        var dependents = new List<int>[count];
        for (int i = 0; i < count; i++)
        {
            dependents[i] = [];
        }

        for (int consumer = 0; consumer < count; consumer++)
        {
            ISimComponent component = components[consumer];
            if (!component.HasDirectFeedthrough)
            {
                continue;
            }

            foreach (Port port in component.Ports)
            {
                Port? source = port.SourcePort;
                if (source is null || !indexById.TryGetValue(source.OwnerId, out int producer))
                {
                    continue;
                }

                if (producer == consumer)
                {
                    continue;
                }

                dependents[producer].Add(consumer);
            }
        }

        if (!TopologicalSorter.TrySort(dependents, out int[] order, out List<int> loop))
        {
            ordered = [];
            cycle = loop.Select(i => components[i].Id).ToList();
            return false;
        }

        ordered = new ISimComponent[count];
        for (int i = 0; i < count; i++)
        {
            ordered[i] = components[order[i]];
        }

        cycle = [];
        return true;
    }
}
```

- [ ] **Step 5: Run the sorter and resolver tests, then the whole suite**

Run: `dotnet test --filter "FullyQualifiedName~TopologicalSorterTests|FullyQualifiedName~GraphResolverTests"`
Expected: PASS, 10 tests (4 new + 6 existing, unchanged).

Run: `dotnet test`
Expected: PASS, all tests.

- [ ] **Step 6: Commit**

```bash
git add src/Millrace.Core/Graph/TopologicalSorter.cs src/Millrace.Core/Graph/GraphResolver.cs tests/Millrace.Core.Tests/TopologicalSorterTests.cs
git commit -m "refactor(core): extract the topological sorter shared by both resolvers"
```

---

### Task 7: Flow graph and the downstream-first sweep

**Files:**
- Create: `src/Millrace.Core/Flow/FlowLink.cs`
- Create: `src/Millrace.Core/Flow/FlowGraph.cs`
- Test: `tests/Millrace.Core.Tests/FlowGraphTests.cs`

**Interfaces:**
- Consumes: `IFlowNode`, the four producer/consumer interfaces, `FlowInlet.Source`, `FlowOutlet.Target`, `FlowComponentBase` (Task 5); `TopologicalSorter` (Task 6); `ValidationError` (plan 1).
- Produces:
  - `internal readonly record struct FlowLink(IFlowNode Producer, FlowOutlet Outlet, IFlowNode Consumer, FlowInlet Inlet)`
  - `internal sealed class FlowGraph` with
    `static IReadOnlyList<ValidationError> Validate(IReadOnlyList<IFlowNode> nodes, IReadOnlySet<string> plantIds, double dt)`,
    `static FlowGraph Build(IReadOnlyList<IFlowNode> nodes)`, `static FlowGraph Empty`,
    `IReadOnlyList<IFlowNode> Nodes` (downstream first), `void Step(double dt)`

Spec 7.4: transport resolves downstream first, in reverse topological order;
flow is the minimum of offer and accept; back-pressure is what falls out when a
downstream node is full. `Step` is one sweep: for each node from the most
downstream up, resolve its outgoing links (its consumers have already advanced
and made room), then let it advance its own contents. Nothing else moves mass.

- [ ] **Step 1: Write the failing tests**

`tests/Millrace.Core.Tests/FlowGraphTests.cs`:

```csharp
using System.Diagnostics.CodeAnalysis;
using Millrace.Core.Flow;
using Millrace.Core.Graph;
using Millrace.Core.Tests.Fakes;
using Millrace.Core.Tests.Fakes.Flow;
using Millrace.Core.Validation;
using Xunit;

namespace Millrace.Core.Tests;

public class FlowGraphTests
{
    private static readonly MaterialType Ore = new("Ore", PayloadKind.Bulk);
    private static readonly MaterialType Wheel = new("Wheel", PayloadKind.Discrete);

    private static IReadOnlySet<string> Ids(params IFlowNode[] nodes) =>
        nodes.Select(n => n.Id).ToHashSet(StringComparer.Ordinal);

    private static BulkFeeder FeederWith(double kilograms)
    {
        var feeder = new BulkFeeder("F", Ore, rateKgPerSecond: kilograms);
        feeder.Evaluate(TestContexts.Tick(0, dt: 1.0));
        return feeder;
    }

    [Fact]
    public void BulkMovesTheMinimumOfOfferAndAccept()
    {
        BulkFeeder feeder = FeederWith(10.0);
        var buffer = new BulkBuffer("B", capacityKg: 4.0);
        feeder.Out.ConnectTo(buffer.In);

        FlowGraph.Build([feeder, buffer]).Step(1.0);

        Assert.Equal(4.0, buffer.MassHeld, 9);
        Assert.Equal(6.0, feeder.MassHeld, 9);
    }

    [Fact]
    public void DownstreamNodesResolveFirstWhateverTheRegistrationOrder()
    {
        BulkFeeder feeder = FeederWith(10.0);
        var buffer = new BulkBuffer("B", 4.0);
        var sink = new BulkSink("S");
        feeder.Out.ConnectTo(buffer.In);
        buffer.Out.ConnectTo(sink.In);

        FlowGraph graph = FlowGraph.Build([sink, buffer, feeder]);

        Assert.Equal(new[] { "S", "B", "F" }, graph.Nodes.Select(n => n.Id));

        graph.Step(1.0);
        Assert.Equal(0.0, sink.TotalReceived);
        Assert.Equal(4.0, buffer.MassHeld, 9);

        // The buffer drains into the sink before the feeder refills it.
        graph.Step(1.0);
        Assert.Equal(4.0, sink.TotalReceived, 9);
        Assert.Equal(4.0, buffer.MassHeld, 9);
        Assert.Equal(2.0, feeder.MassHeld, 9);
    }

    [Fact]
    public void ItemsMoveWholeInOrderUntilTheConsumerIsFull()
    {
        var feeder = new ItemFeeder("F", Wheel, itemMassKg: 1.0, intervalSeconds: 1.0);
        feeder.Initialize(TestContexts.Init("F", dt: 1.0));
        for (int tick = 0; tick < 3; tick++)
        {
            feeder.Evaluate(TestContexts.Tick(tick, dt: 1.0));
        }

        var buffer = new ItemBuffer("B", capacity: 2);
        feeder.Out.ConnectTo(buffer.In);

        FlowGraph.Build([feeder, buffer]).Step(1.0);

        Assert.Equal(2, buffer.Count);
        Assert.Equal(1.0, feeder.MassHeld, 9);
        Assert.True(buffer.TryPeekItem(buffer.Out, out ItemInstance? head));
        Assert.Equal(1, head!.Id);
    }

    [Fact]
    public void AProducerThatWithdrawsTheWrongItemIsRejected()
    {
        var cheat = new CheatingProducer("C");
        var sink = new ItemSink("S");
        cheat.Out.ConnectTo(sink.In);

        InvalidOperationException error =
            Assert.Throws<InvalidOperationException>(() => FlowGraph.Build([cheat, sink]).Step(1.0));
        Assert.Contains("C", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EmptyGraphStepsWithoutError()
    {
        FlowGraph graph = FlowGraph.Build([]);

        graph.Step(1.0);

        Assert.Empty(graph.Nodes);
        Assert.Same(FlowGraph.Empty, graph);
    }

    [Fact]
    public void ValidateReportsRecirculation()
    {
        var a = new BulkBuffer("A", 1.0);
        var b = new BulkBuffer("B", 1.0);
        a.Out.ConnectTo(b.In);
        b.Out.ConnectTo(a.In);

        IReadOnlyList<ValidationError> errors = FlowGraph.Validate([a, b], Ids(a, b), 0.01);

        ValidationError error = Assert.Single(errors);
        Assert.Equal("MR005", error.Code);
        Assert.Contains("A", error.ComponentIds);
        Assert.Contains("B", error.ComponentIds);
    }

    [Fact]
    public void ValidateReportsAnInletFedFromOutsideThePlant()
    {
        var feeder = new BulkFeeder("F", Ore, 1.0);
        var buffer = new BulkBuffer("B", 1.0);
        feeder.Out.ConnectTo(buffer.In);

        ValidationError error = Assert.Single(FlowGraph.Validate([buffer], Ids(buffer), 0.01));

        Assert.Equal("MR007", error.Code);
        Assert.Contains("F.Out", error.Message, StringComparison.Ordinal);
        Assert.Contains("B.In", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateReportsAFlowPortOnAComponentThatIsNotAFlowNode()
    {
        var producer = new SignalOnlyProducer("P");
        var buffer = new BulkBuffer("B", 1.0);
        producer.Out.ConnectTo(buffer.In);
        IReadOnlySet<string> plantIds = new HashSet<string>(StringComparer.Ordinal) { "P", "B" };

        ValidationError error = Assert.Single(FlowGraph.Validate([buffer], plantIds, 0.01));

        Assert.Equal("MR008", error.Code);
        Assert.Contains(nameof(IFlowNode), error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateReportsAConnectedPortWithoutItsContract()
    {
        var mute = new MuteNode("M");
        var buffer = new BulkBuffer("B", 1.0);
        mute.Out.ConnectTo(buffer.In);

        ValidationError error = Assert.Single(FlowGraph.Validate([mute, buffer], Ids(mute, buffer), 0.01));

        Assert.Equal("MR008", error.Code);
        Assert.Contains(nameof(IBulkProducer), error.Message, StringComparison.Ordinal);
        Assert.Contains("M.Out", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateCollectsNodeSpecificErrors()
    {
        var picky = new PickyNode("P");

        ValidationError error = Assert.Single(FlowGraph.Validate([picky], Ids(picky), 0.01));

        Assert.Equal("MR999", error.Code);
    }

    [Fact]
    public void ValidPlantHasNoErrors()
    {
        BulkFeeder feeder = FeederWith(1.0);
        var sink = new BulkSink("S");
        feeder.Out.ConnectTo(sink.In);

        Assert.Empty(FlowGraph.Validate([feeder, sink], Ids(feeder, sink), 0.01));
    }

    [Fact]
    public void BuildRefusesAnUnvalidatedLoop()
    {
        var a = new BulkBuffer("A", 1.0);
        var b = new BulkBuffer("B", 1.0);
        a.Out.ConnectTo(b.In);
        b.Out.ConnectTo(a.In);

        Assert.Throws<InvalidOperationException>(() => FlowGraph.Build([a, b]));
    }

    /// <summary>Shows one item but hands over another — the contract violation the engine must catch.</summary>
    private sealed class CheatingProducer : FlowComponentBase, IItemProducer
    {
        private readonly ItemInstance _shown;
        private readonly ItemInstance _given;

        public CheatingProducer(string id)
            : base(id)
        {
            _shown = new ItemInstance(1, Wheel, 1.0, default);
            _given = new ItemInstance(2, Wheel, 1.0, default);
            Out = AddOutlet("Out", PayloadKind.Discrete);
        }

        public FlowOutlet Out { get; }

        public override double MassHeld => 2.0;

        public bool TryPeekItem(FlowOutlet outlet, [NotNullWhen(true)] out ItemInstance? item)
        {
            item = _shown;
            return true;
        }

        public ItemInstance WithdrawItem(FlowOutlet outlet) => _given;
    }

    private sealed class SignalOnlyProducer : ComponentBase
    {
        public SignalOnlyProducer(string id)
            : base(id) => Out = AddPort(new FlowOutlet("Out", Id, PayloadKind.Bulk));

        public FlowOutlet Out { get; }

        public override void Evaluate(in Contexts.TickContext ctx)
        {
        }
    }

    private sealed class MuteNode : FlowComponentBase
    {
        public MuteNode(string id)
            : base(id) => Out = AddOutlet("Out", PayloadKind.Bulk);

        public FlowOutlet Out { get; }

        public override double MassHeld => 0.0;
    }

    private sealed class PickyNode : FlowComponentBase
    {
        public PickyNode(string id)
            : base(id)
        {
        }

        public override double MassHeld => 0.0;

        public override IEnumerable<ValidationError> ValidateFlow(double dt) =>
            [new ValidationError("MR999", "Picky.", [Id])];
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter FullyQualifiedName~FlowGraphTests`
Expected: FAIL — `FlowGraph` does not exist.

- [ ] **Step 3: Implement the link and the graph**

`src/Millrace.Core/Flow/FlowLink.cs`:

```csharp
namespace Millrace.Core.Flow;

/// <summary>One material connection, resolved to its two nodes at build time.</summary>
internal readonly record struct FlowLink(
    IFlowNode Producer,
    FlowOutlet Outlet,
    IFlowNode Consumer,
    FlowInlet Inlet);
```

`src/Millrace.Core/Flow/FlowGraph.cs`:

```csharp
using Millrace.Core.Graph;
using Millrace.Core.Validation;

namespace Millrace.Core.Flow;

/// <summary>
/// The material layer of a built plant: nodes in downstream-first order, each
/// with its outgoing links. Transport is one sweep over that order, so back
/// pressure needs no code of its own — a full consumer simply accepts less.
/// </summary>
internal sealed class FlowGraph
{
    private readonly IFlowNode[] _order;
    private readonly FlowLink[][] _outgoing;

    private FlowGraph(IFlowNode[] order, FlowLink[][] outgoing)
    {
        _order = order;
        _outgoing = outgoing;
    }

    public static FlowGraph Empty { get; } = new([], []);

    /// <summary>Nodes in transport order: the most downstream first.</summary>
    public IReadOnlyList<IFlowNode> Nodes => _order;

    /// <summary>
    /// Every reason the material graph cannot run. <paramref name="plantIds"/>
    /// is the id set of every component in the plant, flow node or not, so a
    /// link from a non-flow component is told apart from a link from nowhere.
    /// </summary>
    public static IReadOnlyList<ValidationError> Validate(
        IReadOnlyList<IFlowNode> nodes,
        IReadOnlySet<string> plantIds,
        double dt)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(plantIds);

        var errors = new List<ValidationError>();
        Dictionary<string, int> byId = IndexById(nodes);

        foreach (IFlowNode node in nodes)
        {
            foreach (Port port in node.Ports)
            {
                switch (port)
                {
                    case FlowInlet { Source: { } source } inlet when !byId.ContainsKey(source.OwnerId):
                        errors.Add(MissingProducer(node, inlet, source, plantIds));
                        break;
                    case FlowInlet { IsConnected: true } inlet when !ImplementsConsumer(node, inlet.Kind):
                        errors.Add(ContractGap(node, inlet, ConsumerContract(inlet.Kind)));
                        break;
                    case FlowOutlet { IsConnected: true } outlet when !ImplementsProducer(node, outlet.Kind):
                        errors.Add(ContractGap(node, outlet, ProducerContract(outlet.Kind)));
                        break;
                }
            }

            errors.AddRange(node.ValidateFlow(dt));
        }

        if (errors.Count == 0 && !TrySort(nodes, byId, out _, out List<string> cycle))
        {
            errors.Add(new ValidationError(
                "MR005",
                $"Material recirculation loop: {string.Join(" -> ", cycle.Append(cycle[0]))}. " +
                $"Recirculation is not supported; break the loop with an explicit sink and source.",
                cycle));
        }

        return errors;
    }

    /// <summary>Builds the transport order. Call <see cref="Validate"/> first; an invalid set throws.</summary>
    public static FlowGraph Build(IReadOnlyList<IFlowNode> nodes)
    {
        ArgumentNullException.ThrowIfNull(nodes);

        if (nodes.Count == 0)
        {
            return Empty;
        }

        Dictionary<string, int> byId = IndexById(nodes);
        if (!TrySort(nodes, byId, out int[] topological, out _))
        {
            throw new InvalidOperationException(
                "The material graph contains a loop. Validate the plant before building it.");
        }

        // Downstream first: reverse the topological order.
        var order = new IFlowNode[nodes.Count];
        for (int i = 0; i < order.Length; i++)
        {
            order[i] = nodes[topological[order.Length - 1 - i]];
        }

        var outgoing = new FlowLink[order.Length][];
        for (int i = 0; i < order.Length; i++)
        {
            var links = new List<FlowLink>();
            foreach (Port port in order[i].Ports)
            {
                if (port is FlowOutlet { Target: { } target } outlet)
                {
                    links.Add(new FlowLink(order[i], outlet, nodes[byId[target.OwnerId]], target));
                }
            }

            outgoing[i] = links.ToArray();
        }

        return new FlowGraph(order, outgoing);
    }

    /// <summary>
    /// Phase 3. For each node, most downstream first: discharge through its
    /// outgoing links into consumers that have already advanced and made room,
    /// then advance its own contents.
    /// </summary>
    public void Step(double dt)
    {
        for (int i = 0; i < _order.Length; i++)
        {
            foreach (FlowLink link in _outgoing[i])
            {
                Transfer(in link);
            }

            _order[i].Advance(dt);
        }
    }

    private static void Transfer(in FlowLink link)
    {
        if (link.Outlet.Kind == PayloadKind.Bulk)
        {
            TransferBulk(in link);
        }
        else
        {
            TransferItems(in link);
        }
    }

    private static void TransferBulk(in FlowLink link)
    {
        var producer = (IBulkProducer)link.Producer;
        var consumer = (IBulkConsumer)link.Consumer;

        double mass = Math.Min(producer.OfferMass(link.Outlet), consumer.AcceptMass(link.Inlet));
        if (mass <= 0.0)
        {
            return;
        }

        BulkLot lot = producer.Withdraw(link.Outlet, mass);
        if (lot.IsEmpty)
        {
            return;
        }

        consumer.Deposit(link.Inlet, in lot);
    }

    private static void TransferItems(in FlowLink link)
    {
        var producer = (IItemProducer)link.Producer;
        var consumer = (IItemConsumer)link.Consumer;

        while (producer.TryPeekItem(link.Outlet, out ItemInstance? item)
            && consumer.CanAcceptItem(link.Inlet, item))
        {
            ItemInstance withdrawn = producer.WithdrawItem(link.Outlet);
            if (!ReferenceEquals(withdrawn, item))
            {
                throw new InvalidOperationException(
                    $"'{link.Producer.Id}' withdrew {withdrawn} after showing {item}. " +
                    $"{nameof(IItemProducer.WithdrawItem)} must remove exactly the item last " +
                    $"returned by {nameof(IItemProducer.TryPeekItem)} for that outlet.");
            }

            consumer.DepositItem(link.Inlet, withdrawn);
        }
    }

    private static Dictionary<string, int> IndexById(IReadOnlyList<IFlowNode> nodes)
    {
        var byId = new Dictionary<string, int>(nodes.Count, StringComparer.Ordinal);
        for (int i = 0; i < nodes.Count; i++)
        {
            byId[nodes[i].Id] = i;
        }

        return byId;
    }

    private static bool TrySort(
        IReadOnlyList<IFlowNode> nodes,
        Dictionary<string, int> byId,
        out int[] order,
        out List<string> cycle)
    {
        var dependents = new List<int>[nodes.Count];
        for (int i = 0; i < dependents.Length; i++)
        {
            dependents[i] = [];
        }

        for (int consumer = 0; consumer < nodes.Count; consumer++)
        {
            foreach (Port port in nodes[consumer].Ports)
            {
                if (port is FlowInlet { Source: { } source }
                    && byId.TryGetValue(source.OwnerId, out int producer)
                    && producer != consumer)
                {
                    dependents[producer].Add(consumer);
                }
            }
        }

        if (TopologicalSorter.TrySort(dependents, out order, out List<int> loop))
        {
            cycle = [];
            return true;
        }

        cycle = loop.Select(i => nodes[i].Id).ToList();
        return false;
    }

    private static bool ImplementsConsumer(IFlowNode node, PayloadKind kind) =>
        kind == PayloadKind.Bulk ? node is IBulkConsumer : node is IItemConsumer;

    private static bool ImplementsProducer(IFlowNode node, PayloadKind kind) =>
        kind == PayloadKind.Bulk ? node is IBulkProducer : node is IItemProducer;

    private static string ConsumerContract(PayloadKind kind) =>
        kind == PayloadKind.Bulk ? nameof(IBulkConsumer) : nameof(IItemConsumer);

    private static string ProducerContract(PayloadKind kind) =>
        kind == PayloadKind.Bulk ? nameof(IBulkProducer) : nameof(IItemProducer);

    private static ValidationError ContractGap(IFlowNode node, FlowPort port, string contract) =>
        new(
            "MR008",
            $"Port '{port.QualifiedName}' is connected, but '{node.Id}' does not implement " +
            $"{contract}. Implement it, or leave the port unconnected.",
            [node.Id]);

    private static ValidationError MissingProducer(
        IFlowNode node,
        FlowInlet inlet,
        FlowOutlet source,
        IReadOnlySet<string> plantIds) =>
        plantIds.Contains(source.OwnerId)
            ? new ValidationError(
                "MR008",
                $"Inlet '{inlet.QualifiedName}' is fed by '{source.QualifiedName}', but " +
                $"'{source.OwnerId}' is not an {nameof(IFlowNode)}. Derive it from " +
                $"{nameof(FlowComponentBase)} or implement {nameof(IFlowNode)}.",
                [node.Id, source.OwnerId])
            : new ValidationError(
                "MR007",
                $"Inlet '{inlet.QualifiedName}' is fed by '{source.QualifiedName}', but " +
                $"component '{source.OwnerId}' is not part of the plant. Add it to the " +
                $"builder, or add the composite that contains it.",
                [node.Id, source.OwnerId]);
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter FullyQualifiedName~FlowGraphTests`
Expected: PASS, 12 tests.

- [ ] **Step 5: Commit**

```bash
git add src/Millrace.Core/Flow/FlowLink.cs src/Millrace.Core/Flow/FlowGraph.cs tests/Millrace.Core.Tests/FlowGraphTests.cs
git commit -m "feat(flow): add the flow graph with downstream-first transport and validation"
```

---

### Task 8: Mass conservation audit

**Files:**
- Create: `src/Millrace.Core/Flow/MassBalance.cs`
- Create: `src/Millrace.Core/Flow/MassConservationException.cs`
- Modify: `src/Millrace.Core/Flow/FlowGraph.cs` (`Balance()`, `AssertConserved(tick, relativeTolerance)`)
- Modify: `src/Millrace.Core/Time/SimulationOptions.cs` (`CheckConservation`, `ConservationTolerance`)
- Create: `tests/Millrace.Core.Tests/Fakes/Flow/LeakyBuffer.cs`
- Test: `tests/Millrace.Core.Tests/ConservationTests.cs`

**Interfaces:**
- Consumes: `FlowGraph`, `IFlowNode` (Tasks 5, 7); `SimulationOptions` (plan 1).
- Produces:
  - `readonly record struct MassBalance(double Created, double Destroyed, double Held)` with `double Drift`
  - `sealed class MassConservationException : Exception` with `long Tick`, `MassBalance Balance`
  - `FlowGraph.Balance()`, `FlowGraph.AssertConserved(long tick, double relativeTolerance)`
  - `SimulationOptions.CheckConservation` (default `true`), `SimulationOptions.ConservationTolerance` (default `1e-9`)
  - test node `LeakyBuffer(id, keepFraction)`

Spec 7.7: the engine asserts each tick that `sourced − sunk − inSystem` is zero
within epsilon; a contributor who breaks conservation finds out immediately.
Spec says debug builds and tests; the check is O(nodes) per tick, so it is on
by default everywhere and switched off per run for throughput, never silently.
The tolerance is relative to the mass sourced so long runs of large plants do
not trip on accumulated rounding.

- [ ] **Step 1: Write the failing tests**

`tests/Millrace.Core.Tests/ConservationTests.cs`:

```csharp
using System.Globalization;
using Millrace.Core.Flow;
using Millrace.Core.Tests.Fakes;
using Millrace.Core.Tests.Fakes.Flow;
using Millrace.Core.Time;
using Xunit;

namespace Millrace.Core.Tests;

public class ConservationTests
{
    private static readonly MaterialType Ore = new("Ore", PayloadKind.Bulk);

    private static BulkFeeder FeederWith(double kilograms)
    {
        var feeder = new BulkFeeder("F", Ore, rateKgPerSecond: kilograms);
        feeder.Evaluate(TestContexts.Tick(0, dt: 1.0));
        return feeder;
    }

    [Fact]
    public void BalanceSumsCreatedDestroyedAndHeld()
    {
        BulkFeeder feeder = FeederWith(10.0);
        var buffer = new BulkBuffer("B", 4.0);
        var sink = new BulkSink("S");
        feeder.Out.ConnectTo(buffer.In);
        buffer.Out.ConnectTo(sink.In);
        FlowGraph graph = FlowGraph.Build([feeder, buffer, sink]);

        graph.Step(1.0);
        Assert.Equal(new MassBalance(10.0, 0.0, 10.0), graph.Balance());

        graph.Step(1.0);
        MassBalance balance = graph.Balance();
        Assert.Equal(10.0, balance.Created, 9);
        Assert.Equal(4.0, balance.Destroyed, 9);
        Assert.Equal(6.0, balance.Held, 9);
        Assert.Equal(0.0, balance.Drift, 9);
    }

    [Fact]
    public void AConservingGraphPassesTheAudit()
    {
        BulkFeeder feeder = FeederWith(10.0);
        var sink = new BulkSink("S");
        feeder.Out.ConnectTo(sink.In);
        FlowGraph graph = FlowGraph.Build([feeder, sink]);

        graph.Step(1.0);
        graph.AssertConserved(tick: 0, relativeTolerance: 1e-9);
    }

    [Fact]
    public void ALeakingNodeTripsTheAudit()
    {
        BulkFeeder feeder = FeederWith(10.0);
        var leaky = new LeakyBuffer("L", keepFraction: 0.5);
        feeder.Out.ConnectTo(leaky.In);
        FlowGraph graph = FlowGraph.Build([feeder, leaky]);

        graph.Step(1.0);
        MassConservationException error = Assert.Throws<MassConservationException>(
            () => graph.AssertConserved(tick: 3, relativeTolerance: 1e-9));

        Assert.Equal(3, error.Tick);
        Assert.Equal(5.0, error.Balance.Drift, 9);
        Assert.Contains("tick 3", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ToleranceScalesWithTheMassSourced()
    {
        BulkFeeder feeder = FeederWith(1_000_000.0);
        var leaky = new LeakyBuffer("L", keepFraction: 1.0 - 1e-6);
        feeder.Out.ConnectTo(leaky.In);
        FlowGraph graph = FlowGraph.Build([feeder, leaky]);
        graph.Step(1.0);

        // Drift is about 1 kg on 1,000,000 kg sourced.
        Assert.Throws<MassConservationException>(() => graph.AssertConserved(0, 1e-9));
        graph.AssertConserved(0, 1e-5);
    }

    [Fact]
    public void ExceptionMessageIsCultureInvariant()
    {
        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            var error = new MassConservationException(1, new MassBalance(2.5, 0.0, 0.0));

            Assert.Contains("2.5", error.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("2,5", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void OptionsDefaultToCheckingWithATightTolerance()
    {
        var options = new SimulationOptions();

        Assert.True(options.CheckConservation);
        Assert.Equal(1e-9, options.ConservationTolerance);
    }
}
```

`tests/Millrace.Core.Tests/Fakes/Flow/LeakyBuffer.cs`:

```csharp
using Millrace.Core.Flow;

namespace Millrace.Core.Tests.Fakes.Flow;

/// <summary>
/// Keeps only a fraction of what it is given and does not report the rest as
/// destroyed — a deliberately broken node for exercising the conservation audit.
/// </summary>
public sealed class LeakyBuffer : FlowComponentBase, IBulkConsumer, IBulkProducer
{
    private BulkLot _held;

    public LeakyBuffer(string id, double keepFraction)
        : base(id)
    {
        KeepFraction = keepFraction;
        In = AddInlet("In", PayloadKind.Bulk);
        Out = AddOutlet("Out", PayloadKind.Bulk);
    }

    public FlowInlet In { get; }

    public FlowOutlet Out { get; }

    public double KeepFraction { get; }

    public override double MassHeld => _held.Mass;

    public double AcceptMass(FlowInlet inlet) => double.PositiveInfinity;

    public void Deposit(FlowInlet inlet, in BulkLot lot) =>
        _held = _held.Merge(lot.Take(lot.Mass * KeepFraction, out _));

    public double OfferMass(FlowOutlet outlet) => _held.Mass;

    public BulkLot Withdraw(FlowOutlet outlet, double mass)
    {
        BulkLot taken = _held.Take(mass, out BulkLot remaining);
        _held = remaining;
        return taken;
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter FullyQualifiedName~ConservationTests`
Expected: FAIL — `MassBalance` does not exist.

- [ ] **Step 3: Implement the balance, the exception and the options**

`src/Millrace.Core/Flow/MassBalance.cs`:

```csharp
namespace Millrace.Core.Flow;

/// <summary>The plant-wide mass ledger at one instant, in kilograms.</summary>
/// <param name="Created">Cumulative mass injected by sources.</param>
/// <param name="Destroyed">Cumulative mass removed by sinks and declared losses.</param>
/// <param name="Held">Mass currently resident in nodes.</param>
public readonly record struct MassBalance(double Created, double Destroyed, double Held)
{
    /// <summary>Sourced − sunk − held. Zero when every transfer went through the protocol.</summary>
    public double Drift => Created - Destroyed - Held;
}
```

`src/Millrace.Core/Flow/MassConservationException.cs`:

```csharp
using System.Globalization;

namespace Millrace.Core.Flow;

/// <summary>Raised by the per-tick audit when mass appeared or vanished outside the transport protocol.</summary>
public sealed class MassConservationException : Exception
{
    public MassConservationException(long tick, MassBalance balance)
        : base(BuildMessage(tick, balance))
    {
        Tick = tick;
        Balance = balance;
    }

    public long Tick { get; }

    public MassBalance Balance { get; }

    private static string BuildMessage(long tick, MassBalance balance) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"Mass is not conserved at tick {tick}: sourced {balance.Created} kg - sunk " +
            $"{balance.Destroyed} kg - held {balance.Held} kg = {balance.Drift} kg. A node is " +
            $"creating or losing mass outside the transfer protocol. Report injected mass in " +
            $"MassCreated and removed mass (sinks, declared losses) in MassDestroyed.");
}
```

Add to `src/Millrace.Core/Flow/FlowGraph.cs`, after `Step`:

```csharp
    /// <summary>Sums every node's ledger. O(nodes); no allocation.</summary>
    public MassBalance Balance()
    {
        double created = 0.0;
        double destroyed = 0.0;
        double held = 0.0;
        foreach (IFlowNode node in _order)
        {
            created += node.MassCreated;
            destroyed += node.MassDestroyed;
            held += node.MassHeld;
        }

        return new MassBalance(created, destroyed, held);
    }

    /// <summary>
    /// Throws when |drift| exceeds <paramref name="relativeTolerance"/> × max(1 kg, mass sourced).
    /// </summary>
    public void AssertConserved(long tick, double relativeTolerance)
    {
        MassBalance balance = Balance();
        double allowed = relativeTolerance * Math.Max(1.0, balance.Created);
        if (Math.Abs(balance.Drift) > allowed)
        {
            throw new MassConservationException(tick, balance);
        }
    }
```

Add to `src/Millrace.Core/Time/SimulationOptions.cs`, after `TimeStep`:

```csharp
    /// <summary>
    /// Audit sourced − sunk − held after every flow phase and throw on drift.
    /// On by default: the check is O(nodes) and a violation is a bug worth
    /// stopping for. Switch it off per run for throughput, never silently.
    /// </summary>
    public bool CheckConservation { get; init; } = true;

    /// <summary>Allowed drift, relative to max(1 kg, total mass sourced).</summary>
    public double ConservationTolerance { get; init; } = 1e-9;
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter FullyQualifiedName~ConservationTests`
Expected: PASS, 6 tests.

- [ ] **Step 5: Commit**

```bash
git add src/Millrace.Core/Flow src/Millrace.Core/Time/SimulationOptions.cs tests/Millrace.Core.Tests/Fakes/Flow/LeakyBuffer.cs tests/Millrace.Core.Tests/ConservationTests.cs
git commit -m "feat(flow): add the per-tick mass conservation audit"
```

---

### Task 9: Builder and Simulation integration

**Files:**
- Modify: `src/Millrace.Core/SimulationBuilder.cs` (flow validation in `Validate`, flow graph in `Build`)
- Modify: `src/Millrace.Core/Simulation.cs` (constructor takes the flow graph; phase 3; `MassBalance`)
- Test: `tests/Millrace.Core.Tests/SimulationFlowTests.cs`

**Interfaces:**
- Consumes: `FlowGraph` (Tasks 7–8), `IFlowNode` (Task 5), `SimulationOptions.CheckConservation`/`ConservationTolerance` (Task 8).
- Produces:
  - `SimulationBuilder.Validate()` additionally emits `MR005`–`MR008` and node `ValidateFlow` errors
  - `internal Simulation(ISimComponent[] components, FlowGraph flow, SimulationOptions options)`
  - `Simulation.MassBalance` (`MassBalance`)
  - `Simulation.Tick()` phase 3 = `FlowGraph.Step(dt)` then the conservation audit when enabled

Spec 5.4: validation resolves order and rejects incompatible flow links and
CFL violations; the graph is immutable after validation. The flow graph is
built once in `Build()` alongside the signal order, and phase 3 becomes an
instance method walking it.

- [ ] **Step 1: Write the failing tests**

`tests/Millrace.Core.Tests/SimulationFlowTests.cs`:

```csharp
using Millrace.Core;
using Millrace.Core.Flow;
using Millrace.Core.Graph;
using Millrace.Core.Tests.Fakes;
using Millrace.Core.Tests.Fakes.Flow;
using Millrace.Core.Time;
using Millrace.Core.Validation;
using Xunit;

namespace Millrace.Core.Tests;

public class SimulationFlowTests
{
    private static readonly MaterialType Ore = new("Ore", PayloadKind.Bulk);

    private static SimulationOptions Options(bool checkConservation = true) => new()
    {
        Seed = 1UL,
        StartTime = new DateTimeOffset(2026, 1, 1, 6, 0, 0, TimeSpan.Zero),
        TimeStep = TimeSpan.FromMilliseconds(10),
        CheckConservation = checkConservation,
    };

    [Fact]
    public void FeederToSinkMovesTheFeedRateEveryTick()
    {
        var feeder = new BulkFeeder("F", Ore, rateKgPerSecond: 10.0);
        var sink = new BulkSink("S");
        feeder.Out.ConnectTo(sink.In);
        Simulation sim = new SimulationBuilder(Options()).Add(sink).Add(feeder).Build();

        sim.RunFor(TimeSpan.FromSeconds(1));

        Assert.Equal(100, sink.Received.Count);
        Assert.All(sink.Received, mass => Assert.Equal(0.1, mass, 9));
        Assert.Equal(10.0, sink.TotalReceived, 9);
        Assert.Equal(0.0, feeder.MassHeld, 9);
    }

    [Fact]
    public void FlowNodesInsideACompositeAreWiredThroughTheFlattenedLeaves()
    {
        var line = new FeedLine("Line", Ore);
        Simulation sim = new SimulationBuilder(Options()).Add(line).Build();

        sim.RunFor(TimeSpan.FromMilliseconds(50));

        Assert.Equal(new[] { "Line.Feeder", "Line.Sink" }, sim.Components.Select(c => c.Id));
        Assert.Equal(0.5, line.Sink.TotalReceived, 9);
    }

    [Fact]
    public void BuildRejectsRecirculation()
    {
        var a = new BulkBuffer("A", 1.0);
        var b = new BulkBuffer("B", 1.0);
        a.Out.ConnectTo(b.In);
        b.Out.ConnectTo(a.In);

        SimulationValidationException error = Assert.Throws<SimulationValidationException>(
            () => new SimulationBuilder(Options()).Add(a).Add(b).Build());

        Assert.Equal("MR005", error.Result.Errors[0].Code);
    }

    [Fact]
    public void ValidateReportsAnInletFedByAComponentThatWasNotAdded()
    {
        var feeder = new BulkFeeder("F", Ore, 1.0);
        var sink = new BulkSink("S");
        feeder.Out.ConnectTo(sink.In);

        ValidationResult result = new SimulationBuilder(Options()).Add(sink).Validate();

        Assert.Equal("MR007", result.Errors[0].Code);
        Assert.Contains("F.Out", result.Errors[0].Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheAuditRunsAfterEveryFlowPhase()
    {
        var feeder = new BulkFeeder("F", Ore, 10.0);
        var leaky = new LeakyBuffer("L", keepFraction: 0.5);
        feeder.Out.ConnectTo(leaky.In);
        Simulation sim = new SimulationBuilder(Options()).Add(feeder).Add(leaky).Build();

        MassConservationException error = Assert.Throws<MassConservationException>(sim.Tick);

        Assert.Equal(0, error.Tick);
        Assert.Equal(0.05, error.Balance.Drift, 9);
    }

    [Fact]
    public void TheAuditCanBeSwitchedOffPerRun()
    {
        var feeder = new BulkFeeder("F", Ore, 10.0);
        var leaky = new LeakyBuffer("L", 0.5);
        feeder.Out.ConnectTo(leaky.In);
        Simulation sim = new SimulationBuilder(Options(checkConservation: false)).Add(feeder).Add(leaky).Build();

        sim.RunFor(TimeSpan.FromMilliseconds(100));

        Assert.Equal(0.5, sim.MassBalance.Drift, 9);
    }

    [Fact]
    public void MassBalanceIsExposed()
    {
        var feeder = new BulkFeeder("F", Ore, 10.0);
        var buffer = new BulkBuffer("B", 0.25);
        feeder.Out.ConnectTo(buffer.In);
        Simulation sim = new SimulationBuilder(Options()).Add(feeder).Add(buffer).Build();

        sim.RunFor(TimeSpan.FromMilliseconds(100));

        MassBalance balance = sim.MassBalance;
        Assert.Equal(1.0, balance.Created, 9);
        Assert.Equal(0.0, balance.Destroyed, 9);
        Assert.Equal(1.0, balance.Held, 9);
        Assert.Equal(0.25, buffer.MassHeld, 9);
        Assert.Equal(0.75, feeder.MassHeld, 9);
    }

    [Fact]
    public void APlantWithoutFlowNodesHasAnEmptyBalance()
    {
        Simulation sim = new SimulationBuilder(Options()).Add(new ConstantSource("S", 1.0)).Build();

        sim.RunFor(TimeSpan.FromMilliseconds(30));

        Assert.Equal(default, sim.MassBalance);
    }

    private sealed class FeedLine : CompositeComponent
    {
        public FeedLine(string id, MaterialType type)
            : base(id)
        {
            BulkFeeder feeder = AddChild(new BulkFeeder("Feeder", type, 10.0));
            Sink = AddChild(new BulkSink("Sink"));
            feeder.Out.ConnectTo(Sink.In);
        }

        public BulkSink Sink { get; }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter FullyQualifiedName~SimulationFlowTests`
Expected: FAIL — `Simulation.MassBalance` does not exist (and `Build` does not
yet validate flow, so the recirculation test fails on its assertion).

- [ ] **Step 3: Extend the builder**

In `src/Millrace.Core/SimulationBuilder.cs`:

- add `using Millrace.Core.Flow;`;
- in `Validate()`, after the `MR003` block and before `return ValidationResult.From(errors);`, add:

```csharp
        errors.AddRange(FlowGraph.Validate(FlowNodes(), seen, _options.TimeStep.TotalSeconds));
```

- replace the body of `Build()` with:

```csharp
        ValidationResult result = Validate();
        if (!result.IsValid)
        {
            throw new SimulationValidationException(result);
        }

        GraphResolver.TryResolve(_components, out ISimComponent[] ordered, out _);
        FlowGraph flow = FlowGraph.Build(FlowNodes());
        return new Simulation(ordered, flow, _options);
```

- add the private helper at the end of the class:

```csharp
    /// <summary>The flow nodes among the added leaves, in registration order.</summary>
    private List<IFlowNode> FlowNodes() => _components.OfType<IFlowNode>().ToList();
```

(`seen` is the `HashSet<string>` of component ids the `MR001` loop already
builds; `HashSet<string>` implements `IReadOnlySet<string>`.)

- [ ] **Step 4: Give Simulation the flow graph and phase 3**

In `src/Millrace.Core/Simulation.cs`:

- add the fields after `_seed`:

```csharp
    private readonly FlowGraph _flow;
    private readonly bool _checkConservation;
    private readonly double _conservationTolerance;
```

- replace the constructor with:

```csharp
    internal Simulation(ISimComponent[] components, FlowGraph flow, SimulationOptions options)
    {
        _components = components;
        _flow = flow;
        _seed = options.Seed;
        _checkConservation = options.CheckConservation;
        _conservationTolerance = options.ConservationTolerance;
        Clock = new SimulationClock(options.StartTime, options.TimeStep);
    }
```

- add after `Components`:

```csharp
    /// <summary>The plant-wide mass ledger: sourced, sunk, held and their drift.</summary>
    public MassBalance MassBalance => _flow.Balance();
```

- replace the `AdvanceFlow` no-op with:

```csharp
    /// <summary>
    /// Phase 3. One downstream-first sweep over the material graph — each node
    /// discharges into consumers that have already made room, then advances its
    /// own contents — followed by the conservation audit.
    /// </summary>
    private void AdvanceFlow()
    {
        _flow.Step(Clock.DeltaSeconds);
        if (_checkConservation)
        {
            _flow.AssertConserved(Clock.TickCount, _conservationTolerance);
        }
    }
```

`Tick()` already calls `AdvanceFlow();` — leave it.

- [ ] **Step 5: Run the affected tests, then the whole suite**

Run: `dotnet test --filter "FullyQualifiedName~SimulationFlowTests|FullyQualifiedName~SimulationTests"`
Expected: PASS, 21 tests (8 new + 13 existing).

Run: `dotnet test`
Expected: PASS, all tests.

- [ ] **Step 6: Commit**

```bash
git add src/Millrace.Core/SimulationBuilder.cs src/Millrace.Core/Simulation.cs tests/Millrace.Core.Tests/SimulationFlowTests.cs
git commit -m "feat(flow): validate, build and step the material graph in the simulation"
```

---

### Task 10: Bulk belt — cells, advection and back-pressure

**Files:**
- Create: `src/Millrace.Core/Flow/BulkBelt.cs`
- Test: `tests/Millrace.Core.Tests/BulkBeltTests.cs`

**Interfaces:**
- Consumes: `FlowComponentBase`, `IBulkProducer`, `IBulkConsumer`, `BulkLot` (Tasks 2, 5); `InputPort<T>`, `OutputPort<T>`, `TelemetryHandle`, `ValidationError` (plan 1).
- Produces:
  - `sealed class BulkBelt(string id, double length, double cellSize, double maxSpeed, double maxLinearDensity) : FlowComponentBase, IBulkProducer, IBulkConsumer`
    with `FlowInlet In`, `FlowOutlet Out`, `InputPort<double> Speed` (m/s, default 0),
    `OutputPort<double> Load` (kg), `OutputPort<double> PeakLinearDensity` (kg/m),
    `double Length`, `CellSize`, `MaxSpeed`, `MaxLinearDensity`, `int CellCount`,
    `ReadOnlySpan<BulkLot> Cells`, `double LinearDensityAt(double position)`,
    `ValidateFlow` emitting `MR006`

Spec 7.5 bulk mode: cells of configured size; each tick a fraction
`v·dt / cellSize` of each cell moves to its neighbour; validation enforces
`cellSize ≥ maxSpeed·dt`. Spec 7.6: cell mass over cell length is linear
density; width and angle of repose give a maximum linear density (plan 3's
belt component derives it; the node takes the number). A cell never holds more
than `maxLinearDensity · cellSize`: the inlet accepts only the room in the
first cell, and internal advection is resolved from the head backwards with
the same room rule, so a blocked discharge builds load along the belt instead
of overfilling one cell. Speed zero freezes the profile exactly.

`OfferMass` needs `dt`, which is captured in `Initialize`; the Simulation
always initialises before the first tick, and unit tests must call
`Initialize` (via `TestContexts.Init`) before transporting.

- [ ] **Step 1: Write the failing tests**

`tests/Millrace.Core.Tests/BulkBeltTests.cs`:

```csharp
using Millrace.Core.Flow;
using Millrace.Core.Graph;
using Millrace.Core.Tests.Fakes;
using Millrace.Core.Validation;
using Xunit;

namespace Millrace.Core.Tests;

public class BulkBeltTests
{
    private const double Dt = 0.5;

    private static readonly MaterialType Ore = new("Ore", PayloadKind.Bulk);

    private static BulkBelt NewBelt(int cells = 5, double maxLinearDensity = 4.0, double maxSpeed = 1.0)
    {
        var belt = new BulkBelt("CV", length: cells * 0.5, cellSize: 0.5, maxSpeed: maxSpeed, maxLinearDensity: maxLinearDensity);
        belt.Initialize(TestContexts.Init(belt.Id, dt: Dt));
        return belt;
    }

    private static OutputPort<double> Drive(BulkBelt belt, double speed)
    {
        var setpoint = new OutputPort<double>("Out", "SP");
        setpoint.ConnectTo(belt.Speed);
        setpoint.Value = speed;
        return setpoint;
    }

    private static double[] Masses(BulkBelt belt)
    {
        var masses = new double[belt.CellCount];
        for (int i = 0; i < masses.Length; i++)
        {
            masses[i] = belt.Cells[i].Mass;
        }

        return masses;
    }

    private static void Deposit(BulkBelt belt, double mass) =>
        belt.Deposit(belt.In, BulkLot.Of(Ore, mass, default));

    [Fact]
    public void RejectsALengthThatIsNotAWholeNumberOfCells()
    {
        Assert.Throws<ArgumentException>(() => new BulkBelt("CV", 1.2, 0.5, 1.0, 4.0));
    }

    [Fact]
    public void RejectsNonPositiveConfiguration()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BulkBelt("CV", 0.0, 0.5, 1.0, 4.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BulkBelt("CV", 1.0, 0.0, 1.0, 4.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BulkBelt("CV", 1.0, 0.5, 0.0, 4.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BulkBelt("CV", 1.0, 0.5, 1.0, 0.0));
    }

    [Fact]
    public void CflViolationIsReportedWithTheFix()
    {
        BulkBelt tooFast = NewBelt(maxSpeed: 2.0);

        ValidationError error = Assert.Single(tooFast.ValidateFlow(Dt));
        Assert.Equal("MR006", error.Code);
        Assert.Equal(new[] { "CV" }, error.ComponentIds);
        Assert.Contains("cell", error.Message, StringComparison.Ordinal);

        Assert.Empty(NewBelt(maxSpeed: 1.0).ValidateFlow(Dt));
    }

    [Fact]
    public void DepositLandsInTheFirstCell()
    {
        BulkBelt belt = NewBelt();

        Deposit(belt, 1.0);

        Assert.Equal(new[] { 1.0, 0.0, 0.0, 0.0, 0.0 }, Masses(belt));
        Assert.Equal(1.0, belt.MassHeld, 9);
        Assert.Same(Ore, belt.Cells[0].Type);
    }

    [Fact]
    public void AtFullSpeedEveryCellShiftsOnePlacePerTick()
    {
        BulkBelt belt = NewBelt();
        Drive(belt, 1.0);
        Deposit(belt, 1.0);

        for (int i = 0; i < 4; i++)
        {
            belt.Advance(Dt);
        }

        Assert.Equal(new[] { 0.0, 0.0, 0.0, 0.0, 1.0 }, Masses(belt));
        Assert.Equal(1.0, belt.OfferMass(belt.Out), 9);
    }

    [Fact]
    public void AtHalfSpeedHalfOfEachCellMoves()
    {
        BulkBelt belt = NewBelt();
        Drive(belt, 0.5);
        Deposit(belt, 1.0);

        belt.Advance(Dt);
        Assert.Equal(new[] { 0.5, 0.5, 0.0, 0.0, 0.0 }, Masses(belt));

        belt.Advance(Dt);
        Assert.Equal(new[] { 0.25, 0.5, 0.25, 0.0, 0.0 }, Masses(belt));
    }

    [Fact]
    public void AStoppedBeltFreezesTheProfile()
    {
        BulkBelt belt = NewBelt();
        OutputPort<double> speed = Drive(belt, 1.0);
        Deposit(belt, 1.0);
        belt.Advance(Dt);
        belt.Advance(Dt);

        speed.Value = 0.0;
        for (int i = 0; i < 3; i++)
        {
            belt.Advance(Dt);
        }

        Assert.Equal(new[] { 0.0, 0.0, 1.0, 0.0, 0.0 }, Masses(belt));
        Assert.Equal(0.0, belt.OfferMass(belt.Out));
    }

    [Fact]
    public void OfferIsTheMovingFractionOfTheLastCellAndWithdrawTakesIt()
    {
        BulkBelt belt = NewBelt();
        Drive(belt, 1.0);
        Deposit(belt, 1.0);
        for (int i = 0; i < 4; i++)
        {
            belt.Advance(Dt);
        }

        BulkLot lot = belt.Withdraw(belt.Out, 0.4);

        Assert.Equal(0.4, lot.Mass, 9);
        Assert.Equal(0.6, belt.Cells[4].Mass, 9);
        Assert.Equal(0.6, belt.OfferMass(belt.Out), 9);
    }

    [Fact]
    public void AcceptIsTheRoomInTheFirstCell()
    {
        BulkBelt belt = NewBelt(maxLinearDensity: 4.0); // 4 kg/m x 0.5 m = 2 kg per cell

        Assert.Equal(2.0, belt.AcceptMass(belt.In), 9);
        Deposit(belt, 1.5);
        Assert.Equal(0.5, belt.AcceptMass(belt.In), 9);
    }

    [Fact]
    public void ABlockedDischargeBuildsLoadBackwardsWithoutExceedingCapacity()
    {
        BulkBelt belt = NewBelt(cells: 5, maxLinearDensity: 4.0);
        Drive(belt, 1.0);

        for (int tick = 0; tick < 20; tick++)
        {
            double room = belt.AcceptMass(belt.In);
            if (room > 0.0)
            {
                Deposit(belt, Math.Min(1.0, room));
            }

            belt.Advance(Dt);
            Assert.All(Masses(belt), mass => Assert.True(mass <= 2.0 + 1e-9));
        }

        Assert.Equal(10.0, belt.MassHeld, 9);
        Assert.Equal(0.0, belt.AcceptMass(belt.In), 9);
        Assert.Equal(new[] { 2.0, 2.0, 2.0, 2.0, 2.0 }, Masses(belt).Select(m => Math.Round(m, 9)));
    }

    [Fact]
    public void SpeedOutsideTheDeclaredRangeThrows()
    {
        BulkBelt belt = NewBelt(maxSpeed: 1.0);
        OutputPort<double> speed = Drive(belt, 1.5);

        Assert.Throws<InvalidOperationException>(() => belt.Advance(Dt));

        speed.Value = -1.0;
        Assert.Throws<InvalidOperationException>(() => belt.OfferMass(belt.Out));
    }

    [Fact]
    public void LinearDensityAtReadsTheCellUnderThePosition()
    {
        BulkBelt belt = NewBelt();
        Deposit(belt, 1.0);

        Assert.Equal(2.0, belt.LinearDensityAt(0.1), 9);
        Assert.Equal(0.0, belt.LinearDensityAt(2.4), 9);
        Assert.Equal(0.0, belt.LinearDensityAt(99.0), 9);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter FullyQualifiedName~BulkBeltTests`
Expected: FAIL — `BulkBelt` does not exist.

- [ ] **Step 3: Implement the belt**

`src/Millrace.Core/Flow/BulkBelt.cs`:

```csharp
using System.Globalization;
using Millrace.Core.Contexts;
using Millrace.Core.Graph;
using Millrace.Core.Telemetry;
using Millrace.Core.Validation;

namespace Millrace.Core.Flow;

/// <summary>
/// Bulk transport as an array of cells. Each tick a fraction v·dt/cellSize of
/// every cell moves to its neighbour (Eulerian advection), resolved from the
/// head backwards so a blocked discharge builds load along the belt instead of
/// overfilling one cell. Stopping the belt freezes the load profile in place;
/// restarting resumes it. Diffusion is tunable by cell size.
/// </summary>
public sealed class BulkBelt : FlowComponentBase, IBulkProducer, IBulkConsumer
{
    private readonly BulkLot[] _cells;
    private readonly double _cellCapacity;
    private double _dt;
    private TelemetryHandle _loadTelemetry;

    public BulkBelt(string id, double length, double cellSize, double maxSpeed, double maxLinearDensity)
        : base(id)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(length);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(cellSize);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxSpeed);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxLinearDensity);

        double cells = length / cellSize;
        int cellCount = (int)Math.Round(cells);
        if (cellCount < 1 || Math.Abs(cells - cellCount) > 1e-9)
        {
            throw new ArgumentException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Length {length} m is not a whole number of {cellSize} m cells."),
                nameof(cellSize));
        }

        Length = length;
        CellSize = cellSize;
        MaxSpeed = maxSpeed;
        MaxLinearDensity = maxLinearDensity;
        _cellCapacity = maxLinearDensity * cellSize;
        _cells = new BulkLot[cellCount];

        In = AddInlet("In", PayloadKind.Bulk);
        Out = AddOutlet("Out", PayloadKind.Bulk);
        Speed = AddInput<double>("Speed");
        Load = AddOutput<double>("Load");
        PeakLinearDensity = AddOutput<double>("PeakLinearDensity");
    }

    public FlowInlet In { get; }

    public FlowOutlet Out { get; }

    /// <summary>Belt speed in m/s. Unconnected reads zero: a belt with no drive does not move.</summary>
    public InputPort<double> Speed { get; }

    /// <summary>Total mass on the belt, kg, as of the last evaluate.</summary>
    public OutputPort<double> Load { get; }

    /// <summary>The fullest cell's linear density, kg/m, as of the last evaluate.</summary>
    public OutputPort<double> PeakLinearDensity { get; }

    public double Length { get; }

    public double CellSize { get; }

    public double MaxSpeed { get; }

    /// <summary>kg/m. A cell never holds more than this times the cell size.</summary>
    public double MaxLinearDensity { get; }

    public int CellCount => _cells.Length;

    /// <summary>The cells from tail (index 0, where material arrives) to head.</summary>
    public ReadOnlySpan<BulkLot> Cells => _cells;

    public override double MassHeld
    {
        get
        {
            double total = 0.0;
            for (int i = 0; i < _cells.Length; i++)
            {
                total += _cells[i].Mass;
            }

            return total;
        }
    }

    /// <summary>Linear density, kg/m, of the cell under <paramref name="position"/> metres from the tail.</summary>
    public double LinearDensityAt(double position)
    {
        int index = Math.Clamp((int)(position / CellSize), 0, _cells.Length - 1);
        return _cells[index].Mass / CellSize;
    }

    public override void Initialize(in InitContext ctx)
    {
        _dt = ctx.Dt;
        _loadTelemetry = ctx.RegisterTelemetry("Load", "kg");
    }

    public override void Evaluate(in TickContext ctx)
    {
        double load = 0.0;
        double peak = 0.0;
        for (int i = 0; i < _cells.Length; i++)
        {
            double mass = _cells[i].Mass;
            load += mass;
            peak = Math.Max(peak, mass);
        }

        Load.Value = load;
        PeakLinearDensity.Value = peak / CellSize;
        _loadTelemetry.Write(load);
    }

    public override IEnumerable<ValidationError> ValidateFlow(double dt)
    {
        double minimumCell = MaxSpeed * dt;
        if (CellSize < minimumCell)
        {
            yield return new ValidationError(
                "MR006",
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Belt '{Id}': cell size {CellSize} m is smaller than maxSpeed × dt = " +
                    $"{minimumCell} m, so a cell could empty in less than one tick. Use cells of " +
                    $"at least {minimumCell} m, lower maxSpeed, or shorten the time step."),
                [Id]);
        }
    }

    public double AcceptMass(FlowInlet inlet) => Math.Max(0.0, _cellCapacity - _cells[0].Mass);

    public void Deposit(FlowInlet inlet, in BulkLot lot) => _cells[0] = _cells[0].Merge(lot);

    public double OfferMass(FlowOutlet outlet) => Fraction(_dt) * _cells[^1].Mass;

    public BulkLot Withdraw(FlowOutlet outlet, double mass)
    {
        BulkLot taken = _cells[^1].Take(mass, out BulkLot remaining);
        _cells[^1] = remaining;
        return taken;
    }

    public override void Advance(double dt)
    {
        double fraction = Fraction(dt);
        if (fraction <= 0.0)
        {
            return;
        }

        // Head first: each cell moves into the room its neighbour has left.
        for (int i = _cells.Length - 2; i >= 0; i--)
        {
            double wanted = fraction * _cells[i].Mass;
            double room = _cellCapacity - _cells[i + 1].Mass;
            double moving = Math.Min(wanted, room);
            if (moving <= 0.0)
            {
                continue;
            }

            BulkLot taken = _cells[i].Take(moving, out BulkLot remaining);
            _cells[i] = remaining;
            _cells[i + 1] = _cells[i + 1].Merge(taken);
        }
    }

    private double Fraction(double dt)
    {
        double speed = Speed.Value;
        if (speed < 0.0)
        {
            throw new InvalidOperationException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Belt '{Id}' speed {speed} m/s is negative. Reversing belts are not modelled."));
        }

        if (speed > MaxSpeed)
        {
            throw new InvalidOperationException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Belt '{Id}' speed {speed} m/s exceeds its declared maxSpeed {MaxSpeed} m/s. " +
                    $"Raise maxSpeed (and re-check the CFL condition) or limit the drive."));
        }

        return speed * dt / CellSize;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter FullyQualifiedName~BulkBeltTests`
Expected: PASS, 12 tests.

- [ ] **Step 5: Commit**

```bash
git add src/Millrace.Core/Flow/BulkBelt.cs tests/Millrace.Core.Tests/BulkBeltTests.cs
git commit -m "feat(flow): add the cell-based bulk belt with capacity back-pressure"
```

---

### Task 11: Residence transforms and belt outputs

**Files:**
- Create: `src/Millrace.Core/Flow/IMaterialTransform.cs`
- Create: `src/Millrace.Core/Flow/TransformContext.cs`
- Modify: `src/Millrace.Core/Flow/BulkBelt.cs` (optional `transforms` parameter, `AmbientTemperature` input, transforms applied in `Advance`)
- Create: `tests/Millrace.Core.Tests/Fakes/Flow/Heater.cs`
- Test: `tests/Millrace.Core.Tests/BulkBeltTransformTests.cs`

**Interfaces:**
- Consumes: `MaterialProperties` (Task 1), `BulkBelt` (Task 10).
- Produces:
  - `readonly record struct TransformContext(double AmbientTemperature)`
  - `interface IMaterialTransform { void Apply(ref MaterialProperties properties, Span<double> state, double dt, in TransformContext context); }`
  - `BulkBelt(string id, double length, double cellSize, double maxSpeed, double maxLinearDensity, IReadOnlyList<IMaterialTransform>? transforms = null)`
    with `InputPort<double> AmbientTemperature` (°C, default 20)
  - test transform `Heater(double ratePerSecond)`

Spec 8.1: a node may hold transforms applied each tick to whatever material is
resident; `TransformContext` exposes ambient conditions taken from the node's
signal inputs; the state span is empty when the material declares no schema,
so one interface serves bulk cells and discrete items. Transforms run before
the cells move and regardless of speed — a stopped oven still bakes. The three
shipped transforms are plan 3; this task ships the contract and proves it
through the belt with a test transform.

- [ ] **Step 1: Write the failing tests**

`tests/Millrace.Core.Tests/Fakes/Flow/Heater.cs`:

```csharp
using Millrace.Core.Flow;

namespace Millrace.Core.Tests.Fakes.Flow;

/// <summary>Moves temperature toward ambient at a fixed fraction per second. Ignores state.</summary>
public sealed class Heater : IMaterialTransform
{
    private readonly double _ratePerSecond;

    public Heater(double ratePerSecond) => _ratePerSecond = ratePerSecond;

    public void Apply(ref MaterialProperties properties, Span<double> state, double dt, in TransformContext context) =>
        properties = properties with
        {
            Temperature = properties.Temperature
                + ((context.AmbientTemperature - properties.Temperature) * _ratePerSecond * dt),
        };
}
```

`tests/Millrace.Core.Tests/BulkBeltTransformTests.cs`:

```csharp
using Millrace.Core.Flow;
using Millrace.Core.Graph;
using Millrace.Core.Telemetry;
using Millrace.Core.Tests.Fakes;
using Millrace.Core.Tests.Fakes.Flow;
using Xunit;

namespace Millrace.Core.Tests;

public class BulkBeltTransformTests
{
    private const double Dt = 0.5;

    private static readonly MaterialType Ore = new("Ore", PayloadKind.Bulk);
    private static readonly MaterialProperties Cold = new(1600.0, 0.1, 0.0);

    private static BulkBelt NewBelt(params IMaterialTransform[] transforms)
    {
        var belt = new BulkBelt("CV", 2.5, 0.5, 1.0, 4.0, transforms);
        belt.Initialize(TestContexts.Init(belt.Id, dt: Dt));
        return belt;
    }

    private static OutputPort<double> Drive(BulkBelt belt, double speed)
    {
        var setpoint = new OutputPort<double>("Out", "SP");
        setpoint.ConnectTo(belt.Speed);
        setpoint.Value = speed;
        return setpoint;
    }

    [Fact]
    public void TransformsApplyToResidentMaterialEveryAdvanceEvenWhenStopped()
    {
        BulkBelt belt = NewBelt(new Heater(ratePerSecond: 1.0));
        belt.Deposit(belt.In, BulkLot.Of(Ore, 1.0, Cold));

        belt.Advance(Dt);
        Assert.Equal(10.0, belt.Cells[0].Properties.Temperature, 9);

        belt.Advance(Dt);
        Assert.Equal(15.0, belt.Cells[0].Properties.Temperature, 9);
    }

    [Fact]
    public void AmbientComesFromTheSignalInput()
    {
        BulkBelt belt = NewBelt(new Heater(1.0));
        var ambient = new OutputPort<double>("Out", "Zone");
        ambient.ConnectTo(belt.AmbientTemperature);
        ambient.Value = 100.0;
        belt.Deposit(belt.In, BulkLot.Of(Ore, 1.0, Cold));

        belt.Advance(Dt);

        Assert.Equal(50.0, belt.Cells[0].Properties.Temperature, 9);
    }

    [Fact]
    public void TransformsRunBeforeTheCellsMove()
    {
        BulkBelt belt = NewBelt(new Heater(1.0));
        Drive(belt, 1.0);
        belt.Deposit(belt.In, BulkLot.Of(Ore, 1.0, Cold));

        belt.Advance(Dt);

        Assert.Equal(0.0, belt.Cells[0].Mass);
        Assert.Equal(10.0, belt.Cells[1].Properties.Temperature, 9);
    }

    [Fact]
    public void EmptyCellsAreNotTransformed()
    {
        var counter = new CountingTransform();
        BulkBelt belt = NewBelt(counter);
        belt.Deposit(belt.In, BulkLot.Of(Ore, 1.0, Cold));

        belt.Advance(Dt);

        Assert.Equal(1, counter.Calls);
        Assert.Equal(0, counter.StateLengthSeen);
    }

    [Fact]
    public void BeltsWithoutTransformsLeavePropertiesAlone()
    {
        BulkBelt belt = NewBelt();
        belt.Deposit(belt.In, BulkLot.Of(Ore, 1.0, Cold));

        belt.Advance(Dt);

        Assert.Equal(Cold, belt.Cells[0].Properties);
    }

    [Fact]
    public void LoadAndPeakDensityOutputsReflectTheCells()
    {
        BulkBelt belt = NewBelt();
        Drive(belt, 1.0);
        belt.Deposit(belt.In, BulkLot.Of(Ore, 1.5, Cold));
        belt.Advance(Dt);
        belt.Deposit(belt.In, BulkLot.Of(Ore, 0.5, Cold));

        belt.Evaluate(TestContexts.Tick(0, Dt));

        Assert.Equal(2.0, belt.Load.Value, 9);
        Assert.Equal(3.0, belt.PeakLinearDensity.Value, 9);
    }

    [Fact]
    public void LoadIsPublishedAsTelemetry()
    {
        var telemetry = new TelemetryRegistry();
        var belt = new BulkBelt("CV001", 2.5, 0.5, 1.0, 4.0);
        belt.Initialize(TestContexts.Init(belt.Id, dt: Dt, telemetry: telemetry));
        belt.Deposit(belt.In, BulkLot.Of(Ore, 1.25, Cold));

        belt.Evaluate(TestContexts.Tick(0, Dt));

        Assert.Equal(1.25, telemetry.Read("CV001.Load"), 9);
        Assert.Equal("kg", Assert.Single(telemetry.Channels).Unit);
    }

    private sealed class CountingTransform : IMaterialTransform
    {
        public int Calls { get; private set; }

        public int StateLengthSeen { get; private set; }

        public void Apply(ref MaterialProperties properties, Span<double> state, double dt, in TransformContext context)
        {
            Calls++;
            StateLengthSeen = state.Length;
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter FullyQualifiedName~BulkBeltTransformTests`
Expected: FAIL — `IMaterialTransform` does not exist.

- [ ] **Step 3: Implement the contract**

`src/Millrace.Core/Flow/TransformContext.cs`:

```csharp
namespace Millrace.Core.Flow;

/// <summary>
/// Ambient conditions a node hands to its transforms, taken from the node's
/// own signal inputs. Add a field when a transform needs one; nodes that do
/// not drive it pass the default.
/// </summary>
/// <param name="AmbientTemperature">°C.</param>
public readonly record struct TransformContext(double AmbientTemperature);
```

`src/Millrace.Core/Flow/IMaterialTransform.cs`:

```csharp
namespace Millrace.Core.Flow;

/// <summary>
/// Something that happens to material while it is resident in a node: heat
/// transfer, moisture loss, time above a threshold. Applied once per tick to
/// every parcel the node holds. <paramref name="state"/> is the parcel's
/// accumulated-state array, sized by its material's schema — empty for bulk
/// in this version and for any material with no schema.
/// </summary>
public interface IMaterialTransform
{
    void Apply(ref MaterialProperties properties, Span<double> state, double dt, in TransformContext context);
}
```

- [ ] **Step 4: Apply transforms in the bulk belt**

In `src/Millrace.Core/Flow/BulkBelt.cs`:

- add the field after `_cellCapacity`:

```csharp
    private readonly IMaterialTransform[] _transforms;
```

- change the constructor signature to
  `public BulkBelt(string id, double length, double cellSize, double maxSpeed, double maxLinearDensity, IReadOnlyList<IMaterialTransform>? transforms = null)`
  and add, after `_cells = new BulkLot[cellCount];`:

```csharp
        _transforms = transforms is null ? [] : transforms.ToArray();
```

- add, after `Speed = AddInput<double>("Speed");`:

```csharp
        AmbientTemperature = AddInput<double>("AmbientTemperature", defaultValue: 20.0);
```

- add the property after `Speed`:

```csharp
    /// <summary>Ambient temperature handed to the transforms, °C. Unconnected reads 20 °C.</summary>
    public InputPort<double> AmbientTemperature { get; }
```

- in `Advance`, insert before `double fraction = Fraction(dt);`:

```csharp
        ApplyTransforms(dt);
```

- add the private method after `Advance`:

```csharp
    private void ApplyTransforms(double dt)
    {
        if (_transforms.Length == 0)
        {
            return;
        }

        var context = new TransformContext(AmbientTemperature.Value);
        for (int i = 0; i < _cells.Length; i++)
        {
            if (_cells[i].IsEmpty)
            {
                continue;
            }

            MaterialProperties properties = _cells[i].Properties;
            foreach (IMaterialTransform transform in _transforms)
            {
                transform.Apply(ref properties, Span<double>.Empty, dt, in context);
            }

            _cells[i] = _cells[i] with { Properties = properties };
        }
    }
```

- update the class doc comment to mention that transforms run every tick
  before the cells move, whatever the speed.

- [ ] **Step 5: Run the belt tests, then the whole suite**

Run: `dotnet test --filter FullyQualifiedName~BulkBelt`
Expected: PASS, 19 tests (7 new + 12 from Task 10).

Run: `dotnet test`
Expected: PASS, all tests.

- [ ] **Step 6: Commit**

```bash
git add src/Millrace.Core/Flow tests/Millrace.Core.Tests/Fakes/Flow/Heater.cs tests/Millrace.Core.Tests/BulkBeltTransformTests.cs
git commit -m "feat(flow): add the residence transform contract and apply it on bulk belts"
```

---

### Task 12: Discrete belt

**Files:**
- Create: `src/Millrace.Core/Flow/DiscreteBelt.cs`
- Create: `tests/Millrace.Core.Tests/Fakes/Flow/ResidenceCounter.cs`
- Test: `tests/Millrace.Core.Tests/DiscreteBeltTests.cs`

**Interfaces:**
- Consumes: `FlowComponentBase`, `IItemProducer`, `IItemConsumer`, `ItemInstance`, `IMaterialTransform`, `TransformContext` (Tasks 3, 5, 11); `InputPort<T>`, `OutputPort<T>` (plan 1).
- Produces:
  - `sealed class DiscreteBelt(string id, double length, double maxSpeed, double minSpacing = 0.0, IReadOnlyList<IMaterialTransform>? transforms = null) : FlowComponentBase, IItemProducer, IItemConsumer`
    with `FlowInlet In`, `FlowOutlet Out`, `InputPort<double> Speed`, `InputPort<double> AmbientTemperature`,
    `OutputPort<int> ItemCount`, `double Length`, `MaxSpeed`, `MinSpacing`,
    `IReadOnlyList<DiscreteBelt.CarriedItem> Items`
  - `readonly record struct DiscreteBelt.CarriedItem(ItemInstance Item, double Position)`
  - test transform `ResidenceCounter(double thresholdTemperature, int stateIndex)`

Spec 7.5 discrete mode: items carry a continuous position advanced by `v·dt`;
no cells, no diffusion, exact transport. Two things the spec leaves to the
implementation, decided here: an item that reaches the head and cannot
discharge waits there, and followers stop behind it at `minSpacing`; the tail
accepts a new item only once the last one has moved `minSpacing` along
(`minSpacing` of zero means always). Transforms run on every item each tick,
with the item's own state span, before positions move.

- [ ] **Step 1: Write the failing tests**

`tests/Millrace.Core.Tests/Fakes/Flow/ResidenceCounter.cs`:

```csharp
using Millrace.Core.Flow;

namespace Millrace.Core.Tests.Fakes.Flow;

/// <summary>Accumulates, in one state slot, the seconds spent above a temperature.</summary>
public sealed class ResidenceCounter : IMaterialTransform
{
    private readonly double _thresholdTemperature;
    private readonly int _stateIndex;

    public ResidenceCounter(double thresholdTemperature, int stateIndex)
    {
        _thresholdTemperature = thresholdTemperature;
        _stateIndex = stateIndex;
    }

    public void Apply(ref MaterialProperties properties, Span<double> state, double dt, in TransformContext context)
    {
        if (properties.Temperature > _thresholdTemperature)
        {
            state[_stateIndex] += dt;
        }
    }
}
```

`tests/Millrace.Core.Tests/DiscreteBeltTests.cs`:

```csharp
using Millrace.Core.Flow;
using Millrace.Core.Graph;
using Millrace.Core.Tests.Fakes;
using Millrace.Core.Tests.Fakes.Flow;
using Xunit;

namespace Millrace.Core.Tests;

public class DiscreteBeltTests
{
    private const double Dt = 0.5;

    private static readonly MaterialType Wheel = new("Wheel", PayloadKind.Discrete);
    private static readonly MaterialType Billet = new("Billet", PayloadKind.Discrete, "TimeAbove100");

    private static DiscreteBelt NewBelt(double minSpacing = 0.0, params IMaterialTransform[] transforms)
    {
        var belt = new DiscreteBelt("CV", length: 2.0, maxSpeed: 1.0, minSpacing, transforms);
        belt.Initialize(TestContexts.Init(belt.Id, dt: Dt));
        return belt;
    }

    private static OutputPort<double> Drive(DiscreteBelt belt, double speed)
    {
        var setpoint = new OutputPort<double>("Out", "SP");
        setpoint.ConnectTo(belt.Speed);
        setpoint.Value = speed;
        return setpoint;
    }

    private static ItemInstance Item(long id, MaterialType? type = null, double mass = 1.0) =>
        new(id, type ?? Wheel, mass, new MaterialProperties(7800.0, 0.0, 20.0));

    private static double[] Positions(DiscreteBelt belt) => belt.Items.Select(c => c.Position).ToArray();

    [Fact]
    public void RejectsInvalidConfiguration()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DiscreteBelt("CV", 0.0, 1.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DiscreteBelt("CV", 2.0, 0.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DiscreteBelt("CV", 2.0, 1.0, minSpacing: -1.0));
        Assert.Throws<ArgumentException>(() => new DiscreteBelt("CV", 2.0, 1.0, minSpacing: 3.0));
    }

    [Fact]
    public void ItemsAdvanceBySpeedTimesDt()
    {
        DiscreteBelt belt = NewBelt();
        Drive(belt, 1.0);
        belt.DepositItem(belt.In, Item(1));

        belt.Advance(Dt);
        Assert.Equal(new[] { 0.5 }, Positions(belt));

        belt.Advance(Dt);
        Assert.Equal(new[] { 1.0 }, Positions(belt));
    }

    [Fact]
    public void TheHeadItemIsOfferedOnlyAtTheEnd()
    {
        DiscreteBelt belt = NewBelt();
        Drive(belt, 1.0);
        ItemInstance item = Item(1);
        belt.DepositItem(belt.In, item);

        for (int i = 0; i < 3; i++)
        {
            belt.Advance(Dt);
            Assert.False(belt.TryPeekItem(belt.Out, out _));
        }

        belt.Advance(Dt);

        Assert.True(belt.TryPeekItem(belt.Out, out ItemInstance? head));
        Assert.Same(item, head);
        Assert.Equal(2.0, belt.Items[0].Position, 9);
    }

    [Fact]
    public void WithdrawRemovesTheHeadAndThrowsWhenNothingHasArrived()
    {
        DiscreteBelt belt = NewBelt();
        Drive(belt, 1.0);
        ItemInstance item = Item(1);
        belt.DepositItem(belt.In, item);
        for (int i = 0; i < 4; i++)
        {
            belt.Advance(Dt);
        }

        Assert.Same(item, belt.WithdrawItem(belt.Out));
        Assert.Empty(belt.Items);
        Assert.Throws<InvalidOperationException>(() => belt.WithdrawItem(belt.Out));
    }

    [Fact]
    public void ItemsQueueBehindABlockedHead()
    {
        DiscreteBelt belt = NewBelt(minSpacing: 0.5);
        Drive(belt, 1.0);
        belt.DepositItem(belt.In, Item(1));
        belt.Advance(Dt);
        belt.DepositItem(belt.In, Item(2));

        for (int i = 0; i < 6; i++)
        {
            belt.Advance(Dt);
        }

        Assert.Equal(new[] { 2.0, 1.5 }, Positions(belt));
        Assert.True(belt.TryPeekItem(belt.Out, out ItemInstance? head));
        Assert.Equal(1, head!.Id);
    }

    [Fact]
    public void MinSpacingBlocksTheTailUntilTheLastItemHasMovedClear()
    {
        DiscreteBelt belt = NewBelt(minSpacing: 0.5);
        Drive(belt, 1.0);
        belt.DepositItem(belt.In, Item(1));

        Assert.False(belt.CanAcceptItem(belt.In, Item(2)));
        belt.Advance(Dt);
        Assert.True(belt.CanAcceptItem(belt.In, Item(2)));
    }

    [Fact]
    public void ZeroSpacingAlwaysAccepts()
    {
        DiscreteBelt belt = NewBelt();
        belt.DepositItem(belt.In, Item(1));

        Assert.True(belt.CanAcceptItem(belt.In, Item(2)));
        belt.DepositItem(belt.In, Item(2));
        Assert.Equal(new[] { 0.0, 0.0 }, Positions(belt));
    }

    [Fact]
    public void AStoppedBeltHoldsItsItems()
    {
        DiscreteBelt belt = NewBelt();
        OutputPort<double> speed = Drive(belt, 1.0);
        belt.DepositItem(belt.In, Item(1));
        belt.Advance(Dt);

        speed.Value = 0.0;
        belt.Advance(Dt);
        belt.Advance(Dt);

        Assert.Equal(new[] { 0.5 }, Positions(belt));
    }

    [Fact]
    public void TransformsAccumulateItemStateUsingTheMaterialSchema()
    {
        int slot = Billet.StateIndexOf("TimeAbove100");
        DiscreteBelt belt = NewBelt(0.0, new Heater(ratePerSecond: 1.0), new ResidenceCounter(100.0, slot));
        var ambient = new OutputPort<double>("Out", "Zone");
        ambient.ConnectTo(belt.AmbientTemperature);
        ambient.Value = 200.0;
        ItemInstance billet = Item(1, Billet, 12.0);
        belt.DepositItem(belt.In, billet);

        belt.Advance(Dt);
        Assert.Equal(110.0, billet.Properties.Temperature, 9);
        Assert.Equal(0.5, billet.State[slot], 9);

        belt.Advance(Dt);
        Assert.Equal(155.0, billet.Properties.Temperature, 9);
        Assert.Equal(1.0, billet.State[slot], 9);
    }

    [Fact]
    public void ItemCountOutputAndMassHeldReflectTheItems()
    {
        DiscreteBelt belt = NewBelt();
        belt.DepositItem(belt.In, Item(1, mass: 1.0));
        belt.DepositItem(belt.In, Item(2, mass: 2.5));

        belt.Evaluate(TestContexts.Tick(0, Dt));

        Assert.Equal(2, belt.ItemCount.Value);
        Assert.Equal(3.5, belt.MassHeld, 9);
    }

    [Fact]
    public void SpeedOutsideTheDeclaredRangeThrows()
    {
        DiscreteBelt belt = NewBelt();
        OutputPort<double> speed = Drive(belt, 1.5);

        Assert.Throws<InvalidOperationException>(() => belt.Advance(Dt));

        speed.Value = -0.5;
        Assert.Throws<InvalidOperationException>(() => belt.Advance(Dt));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter FullyQualifiedName~DiscreteBeltTests`
Expected: FAIL — `DiscreteBelt` does not exist.

- [ ] **Step 3: Implement the belt**

`src/Millrace.Core/Flow/DiscreteBelt.cs`:

```csharp
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Millrace.Core.Contexts;
using Millrace.Core.Graph;

namespace Millrace.Core.Flow;

/// <summary>
/// Discrete transport: each item carries a continuous position advanced by
/// v·dt — no cells, no diffusion, exact. An item that reaches the head and
/// cannot discharge waits there and followers queue behind it at the minimum
/// spacing; the tail accepts a new item once the last one has moved that far.
/// Transforms run on every item each tick, before positions move.
/// </summary>
public sealed class DiscreteBelt : FlowComponentBase, IItemProducer, IItemConsumer
{
    private const double HeadTolerance = 1e-9;

    // Index 0 is nearest the head; items board at the end.
    private readonly List<CarriedItem> _items = [];
    private readonly IMaterialTransform[] _transforms;

    public DiscreteBelt(
        string id,
        double length,
        double maxSpeed,
        double minSpacing = 0.0,
        IReadOnlyList<IMaterialTransform>? transforms = null)
        : base(id)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(length);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxSpeed);
        ArgumentOutOfRangeException.ThrowIfNegative(minSpacing);
        if (minSpacing > length)
        {
            throw new ArgumentException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Minimum spacing {minSpacing} m exceeds the belt length {length} m."),
                nameof(minSpacing));
        }

        Length = length;
        MaxSpeed = maxSpeed;
        MinSpacing = minSpacing;
        _transforms = transforms is null ? [] : transforms.ToArray();

        In = AddInlet("In", PayloadKind.Discrete);
        Out = AddOutlet("Out", PayloadKind.Discrete);
        Speed = AddInput<double>("Speed");
        AmbientTemperature = AddInput<double>("AmbientTemperature", defaultValue: 20.0);
        ItemCount = AddOutput<int>("ItemCount");
    }

    /// <summary>An item on the belt and its distance from the tail, in metres.</summary>
    public readonly record struct CarriedItem(ItemInstance Item, double Position);

    public FlowInlet In { get; }

    public FlowOutlet Out { get; }

    /// <summary>Belt speed in m/s. Unconnected reads zero.</summary>
    public InputPort<double> Speed { get; }

    /// <summary>Ambient temperature handed to the transforms, °C. Unconnected reads 20 °C.</summary>
    public InputPort<double> AmbientTemperature { get; }

    /// <summary>Items on the belt, as of the last evaluate.</summary>
    public OutputPort<int> ItemCount { get; }

    public double Length { get; }

    public double MaxSpeed { get; }

    public double MinSpacing { get; }

    /// <summary>Items from head to tail.</summary>
    public IReadOnlyList<CarriedItem> Items => _items;

    public override double MassHeld
    {
        get
        {
            double total = 0.0;
            for (int i = 0; i < _items.Count; i++)
            {
                total += _items[i].Item.Mass;
            }

            return total;
        }
    }

    public override void Evaluate(in TickContext ctx) => ItemCount.Value = _items.Count;

    public bool CanAcceptItem(FlowInlet inlet, ItemInstance item) =>
        _items.Count == 0 || _items[^1].Position >= MinSpacing;

    public void DepositItem(FlowInlet inlet, ItemInstance item) => _items.Add(new CarriedItem(item, 0.0));

    public bool TryPeekItem(FlowOutlet outlet, [NotNullWhen(true)] out ItemInstance? item)
    {
        if (_items.Count > 0 && _items[0].Position >= Length - HeadTolerance)
        {
            item = _items[0].Item;
            return true;
        }

        item = null;
        return false;
    }

    public ItemInstance WithdrawItem(FlowOutlet outlet)
    {
        if (!TryPeekItem(outlet, out ItemInstance? item))
        {
            throw new InvalidOperationException($"Belt '{Id}' has no item at its head to withdraw.");
        }

        _items.RemoveAt(0);
        return item;
    }

    public override void Advance(double dt)
    {
        double step = SpeedOrThrow() * dt;
        ApplyTransforms(dt);

        for (int i = 0; i < _items.Count; i++)
        {
            double limit = i == 0 ? Length : _items[i - 1].Position - MinSpacing;
            double current = _items[i].Position;
            double next = Math.Min(current + step, Math.Max(current, limit));
            _items[i] = _items[i] with { Position = next };
        }
    }

    private void ApplyTransforms(double dt)
    {
        if (_transforms.Length == 0)
        {
            return;
        }

        var context = new TransformContext(AmbientTemperature.Value);
        for (int i = 0; i < _items.Count; i++)
        {
            ItemInstance item = _items[i].Item;
            MaterialProperties properties = item.Properties;
            foreach (IMaterialTransform transform in _transforms)
            {
                transform.Apply(ref properties, item.State, dt, in context);
            }

            item.Properties = properties;
        }
    }

    private double SpeedOrThrow()
    {
        double speed = Speed.Value;
        if (speed < 0.0)
        {
            throw new InvalidOperationException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Belt '{Id}' speed {speed} m/s is negative. Reversing belts are not modelled."));
        }

        if (speed > MaxSpeed)
        {
            throw new InvalidOperationException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Belt '{Id}' speed {speed} m/s exceeds its declared maxSpeed {MaxSpeed} m/s. " +
                    $"Raise maxSpeed or limit the drive."));
        }

        return speed;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter FullyQualifiedName~DiscreteBeltTests`
Expected: PASS, 11 tests.

- [ ] **Step 5: Commit**

```bash
git add src/Millrace.Core/Flow/DiscreteBelt.cs tests/Millrace.Core.Tests/Fakes/Flow/ResidenceCounter.cs tests/Millrace.Core.Tests/DiscreteBeltTests.cs
git commit -m "feat(flow): add the position-based discrete belt"
```

---

### Task 13: End-to-end back-pressure, conservation property test and docs

**Files:**
- Create: `tests/Millrace.Core.Tests/Fakes/Setpoint.cs`
- Test: `tests/Millrace.Core.Tests/FlowIntegrationTests.cs`
- Test: `tests/Millrace.Core.Tests/ConservationPropertyTests.cs`
- Modify: `docs/architecture.md` (phase 3 bullet; new "Material flow" section)
- Modify: `README.md` (status)

**Interfaces:**
- Consumes: everything above.
- Produces: test fake `Setpoint(string id, double value = 0.0)` with settable `double Value` and `OutputPort<double> Out`; no production code.

This is the plan's acceptance test: spec 7.8's causal chain up to "load builds
along CV002", with the numbers made exact by choosing `cellSize = v·dt` so the
bulk belts behave as shift registers. The property test is spec 16's
"mass-conservation property test over randomly generated plants".

- [ ] **Step 1: Write the setpoint fake and the integration tests**

`tests/Millrace.Core.Tests/Fakes/Setpoint.cs`:

```csharp
using Millrace.Core.Contexts;
using Millrace.Core.Graph;

namespace Millrace.Core.Tests.Fakes;

/// <summary>A signal source a test can change between ticks.</summary>
public sealed class Setpoint : ComponentBase
{
    public Setpoint(string id, double value = 0.0)
        : base(id)
    {
        Value = value;
        Out = AddOutput<double>("Out");
    }

    public double Value { get; set; }

    public OutputPort<double> Out { get; }

    public override void Evaluate(in TickContext ctx) => Out.Value = Value;
}
```

`tests/Millrace.Core.Tests/FlowIntegrationTests.cs`:

```csharp
using Millrace.Core;
using Millrace.Core.Flow;
using Millrace.Core.Tests.Fakes;
using Millrace.Core.Tests.Fakes.Flow;
using Millrace.Core.Time;
using Xunit;

namespace Millrace.Core.Tests;

public class FlowIntegrationTests
{
    private static readonly MaterialType Ore = new("Ore", PayloadKind.Bulk);

    private sealed record Plant(
        Simulation Sim,
        BulkFeeder Feeder,
        BulkBelt Cv1,
        BulkBuffer Chute,
        BulkBelt Cv2,
        BulkSink Sink,
        Setpoint Speed1,
        Setpoint Speed2);

    private static SimulationOptions Options(ulong seed) => new()
    {
        Seed = seed,
        StartTime = new DateTimeOffset(2026, 1, 1, 6, 0, 0, TimeSpan.Zero),
        TimeStep = TimeSpan.FromMilliseconds(500),
    };

    /// <summary>
    /// Feeder → CV001 (5 m, ten 0.5 m cells) → chute (3 kg) → CV002 (2.5 m, five
    /// cells) → stockpile. At 1 m/s and dt = 0.5 s each cell shifts exactly one
    /// place per tick, so travel times are exact. Feed 2 kg/s = 1 kg per tick.
    /// </summary>
    private static Plant Build(ulong seed = 1UL, double feedRate = 2.0)
    {
        var feeder = new BulkFeeder("Feeder", Ore, feedRate, new MaterialProperties(1600.0, 0.08, 15.0));
        var cv1 = new BulkBelt("CV001", length: 5.0, cellSize: 0.5, maxSpeed: 1.0, maxLinearDensity: 4.0);
        var chute = new BulkBuffer("Chute", capacityKg: 3.0);
        var cv2 = new BulkBelt("CV002", length: 2.5, cellSize: 0.5, maxSpeed: 1.0, maxLinearDensity: 4.0);
        var sink = new BulkSink("Stockpile");
        var speed1 = new Setpoint("SP1", 1.0);
        var speed2 = new Setpoint("SP2", 1.0);

        feeder.Out.ConnectTo(cv1.In);
        cv1.Out.ConnectTo(chute.In);
        chute.Out.ConnectTo(cv2.In);
        cv2.Out.ConnectTo(sink.In);
        speed1.Out.ConnectTo(cv1.Speed);
        speed2.Out.ConnectTo(cv2.Speed);

        Simulation sim = new SimulationBuilder(Options(seed))
            .Add(sink).Add(cv2).Add(chute).Add(cv1).Add(feeder).Add(speed1).Add(speed2)
            .Build();

        return new Plant(sim, feeder, cv1, chute, cv2, sink, speed1, speed2);
    }

    private static void Run(Plant plant, int ticks)
    {
        for (int i = 0; i < ticks; i++)
        {
            plant.Sim.Tick();
        }
    }

    [Fact]
    public void SteadyStateThroughputEqualsTheFeedRate()
    {
        Plant plant = Build();

        Run(plant, 60);

        IEnumerable<double> lastTen = plant.Sink.Received.TakeLast(10);
        Assert.All(lastTen, mass => Assert.Equal(1.0, mass, 9));
        Assert.Equal(10.0, plant.Cv1.Load.Value, 9);
        Assert.Equal(0.0, plant.Sim.MassBalance.Drift, 9);
    }

    [Fact]
    public void ASurgeArrivesAfterLengthOverSpeedPlusOneHandOff()
    {
        Plant plant = Build();
        plant.Sim.Tick();                     // tick 0: 1 kg created and placed on CV001
        plant.Feeder.RateKgPerSecond = 0.0;

        int arrival = -1;
        for (int tick = 1; tick < 40; tick++)
        {
            plant.Sim.Tick();
            if (plant.Sink.TotalReceived > 0.0)
            {
                arrival = tick;
                break;
            }
        }

        // 10 ticks along CV001 (5 m at 1 m/s), one tick in the chute, 5 ticks along CV002.
        Assert.Equal(16, arrival);
        Assert.Equal(1.0, plant.Sink.TotalReceived, 9);
    }

    [Fact]
    public void StoppingTheDownstreamBeltBacksUpTheChainAndRestartingDrainsIt()
    {
        Plant plant = Build();
        Run(plant, 40);
        double steadyLoad = plant.Cv1.Load.Value;

        plant.Speed2.Value = 0.0;
        Run(plant, 5);
        int receivedAtStop = plant.Sink.Received.Count;
        Run(plant, 25);

        Assert.Equal(receivedAtStop, plant.Sink.Received.Count);
        Assert.Equal(3.0, plant.Chute.MassHeld, 9);
        Assert.Equal(20.0, plant.Cv1.Load.Value, 9);
        Assert.Equal(4.0, plant.Cv1.PeakLinearDensity.Value, 9);
        Assert.True(plant.Cv1.Load.Value > steadyLoad);
        Assert.True(plant.Feeder.MassHeld > 0.0);
        Assert.Equal(0.0, plant.Sim.MassBalance.Drift, 9);

        plant.Speed2.Value = 1.0;
        Run(plant, 80);   // the backlog (hopper, belt and chute) flushes at +1 kg/tick

        Assert.True(plant.Chute.MassHeld < 3.0);
        Assert.All(plant.Sink.Received.TakeLast(5), mass => Assert.Equal(1.0, mass, 9));
        Assert.Equal(0.0, plant.Sim.MassBalance.Drift, 9);
    }

    [Fact]
    public void TwoRunsAreIndistinguishable()
    {
        Plant first = Build();
        Plant second = Build();

        foreach (Plant plant in new[] { first, second })
        {
            Run(plant, 40);
            plant.Speed2.Value = 0.0;
            Run(plant, 30);
            plant.Speed2.Value = 1.0;
            Run(plant, 80);
        }

        Assert.Equal(first.Sink.Received, second.Sink.Received);
        Assert.Equal(first.Sim.Telemetry.Read("CV001.Load"), second.Sim.Telemetry.Read("CV001.Load"));
        Assert.Equal(first.Sim.MassBalance, second.Sim.MassBalance);
    }

    [Fact]
    public void BeltLoadIsPublishedAsTelemetry()
    {
        Plant plant = Build();

        Run(plant, 20);

        Assert.Equal(plant.Cv1.Load.Value, plant.Sim.Telemetry.Read("CV001.Load"));
        Assert.Equal(plant.Cv2.Load.Value, plant.Sim.Telemetry.Read("CV002.Load"));
    }

    [Fact]
    public void PropertiesTravelWithTheMaterial()
    {
        Plant plant = Build();

        Run(plant, 30);

        Assert.Equal(0.08, plant.Sink.LastProperties.Moisture, 9);
        Assert.Equal(15.0, plant.Sink.LastProperties.Temperature, 9);
    }
}
```

- [ ] **Step 2: Write the conservation property test**

`tests/Millrace.Core.Tests/ConservationPropertyTests.cs`:

```csharp
using Millrace.Core;
using Millrace.Core.Flow;
using Millrace.Core.Graph;
using Millrace.Core.Randomness;
using Millrace.Core.Tests.Fakes;
using Millrace.Core.Tests.Fakes.Flow;
using Millrace.Core.Time;
using Xunit;

namespace Millrace.Core.Tests;

/// <summary>
/// Spec section 16: mass conservation over randomly generated plants. Each
/// seed builds a different chain and drives it with random speeds and feed
/// rates; the per-tick audit inside the simulation is the assertion.
/// </summary>
public class ConservationPropertyTests
{
    private const int Seeds = 20;
    private const int Ticks = 300;

    private static readonly MaterialType Ore = new("Ore", PayloadKind.Bulk);
    private static readonly MaterialType Wheel = new("Wheel", PayloadKind.Discrete);

    private static SimulationOptions Options(ulong seed) => new()
    {
        Seed = seed,
        StartTime = new DateTimeOffset(2026, 1, 1, 6, 0, 0, TimeSpan.Zero),
        TimeStep = TimeSpan.FromMilliseconds(500),
    };

    [Fact]
    public void RandomBulkChainsConserveMass()
    {
        for (ulong seed = 0; seed < Seeds; seed++)
        {
            var random = new DeterministicRandom(seed);
            var builder = new SimulationBuilder(Options(seed));
            var feeder = new BulkFeeder("Feeder", Ore, 1.0);
            var sink = new BulkSink("Sink");
            var speeds = new List<Setpoint>();
            builder.Add(feeder).Add(sink);

            FlowOutlet upstream = feeder.Out;
            int stages = 1 + (int)(random.NextDouble() * 4);
            for (int i = 0; i < stages; i++)
            {
                if (random.NextDouble() < 0.6)
                {
                    int cells = 1 + (int)(random.NextDouble() * 8);
                    double density = 1.0 + (random.NextDouble() * 9.0);
                    var belt = new BulkBelt($"Belt{i}", cells * 0.5, 0.5, 1.0, density);
                    var speed = new Setpoint($"Speed{i}");
                    speed.Out.ConnectTo(belt.Speed);
                    upstream.ConnectTo(belt.In);
                    upstream = belt.Out;
                    speeds.Add(speed);
                    builder.Add(belt).Add(speed);
                }
                else
                {
                    var buffer = new BulkBuffer($"Hold{i}", 0.5 + (random.NextDouble() * 9.5));
                    upstream.ConnectTo(buffer.In);
                    upstream = buffer.Out;
                    builder.Add(buffer);
                }
            }

            upstream.ConnectTo(sink.In);
            Simulation sim = builder.Build();

            for (int tick = 0; tick < Ticks; tick++)
            {
                foreach (Setpoint speed in speeds)
                {
                    speed.Value = random.NextDouble();
                }

                feeder.RateKgPerSecond = random.NextDouble() < 0.2 ? 0.0 : random.NextDouble() * 5.0;

                try
                {
                    sim.Tick();
                }
                catch (MassConservationException error)
                {
                    Assert.Fail($"Seed {seed}, tick {tick}: {error.Message}");
                }
            }

            MassBalance balance = sim.MassBalance;
            Assert.True(Math.Abs(balance.Drift) <= 1e-9 * Math.Max(1.0, balance.Created), $"Seed {seed}: drift {balance.Drift}");
            Assert.True(balance.Held >= 0.0, $"Seed {seed}: negative inventory");
            Assert.True(sink.TotalReceived <= balance.Created + 1e-9, $"Seed {seed}: sink received more than was sourced");
        }
    }

    [Fact]
    public void RandomDiscreteChainsConserveMassAndKeepItemOrder()
    {
        for (ulong seed = 0; seed < Seeds; seed++)
        {
            var random = new DeterministicRandom(seed);
            var builder = new SimulationBuilder(Options(seed));
            var feeder = new ItemFeeder("Feeder", Wheel, 2.0, 0.5 + (random.NextDouble() * 1.5));
            var sink = new ItemSink("Sink");
            var speeds = new List<Setpoint>();
            builder.Add(feeder).Add(sink);

            FlowOutlet upstream = feeder.Out;
            int belts = 1 + (int)(random.NextDouble() * 3);
            for (int i = 0; i < belts; i++)
            {
                double length = 0.5 + (random.NextDouble() * 4.5);
                var belt = new DiscreteBelt($"Belt{i}", length, 1.0, minSpacing: random.NextDouble() * 0.5);
                var speed = new Setpoint($"Speed{i}");
                speed.Out.ConnectTo(belt.Speed);
                upstream.ConnectTo(belt.In);
                upstream = belt.Out;
                speeds.Add(speed);
                builder.Add(belt).Add(speed);
            }

            var buffer = new ItemBuffer("Hold", 1 + (int)(random.NextDouble() * 5));
            upstream.ConnectTo(buffer.In);
            buffer.Out.ConnectTo(sink.In);
            builder.Add(buffer);
            Simulation sim = builder.Build();

            for (int tick = 0; tick < Ticks; tick++)
            {
                foreach (Setpoint speed in speeds)
                {
                    speed.Value = random.NextDouble();
                }

                try
                {
                    sim.Tick();
                }
                catch (MassConservationException error)
                {
                    Assert.Fail($"Seed {seed}, tick {tick}: {error.Message}");
                }
            }

            MassBalance balance = sim.MassBalance;
            Assert.True(Math.Abs(balance.Drift) <= 1e-9 * Math.Max(1.0, balance.Created), $"Seed {seed}: drift {balance.Drift}");
            long previous = 0;
            foreach (ItemInstance item in sink.Items)
            {
                Assert.True(item.Id > previous, $"Seed {seed}: item {item.Id} arrived after {previous}");
                previous = item.Id;
            }
        }
    }
}
```

- [ ] **Step 3: Run the new tests, then the whole suite**

Run: `dotnet test --filter "FullyQualifiedName~FlowIntegrationTests|FullyQualifiedName~ConservationPropertyTests"`
Expected: PASS, 8 tests.

Run: `dotnet test && dotnet build --configuration Release`
Expected: all tests PASS; Release build succeeds with 0 warnings.

- [ ] **Step 4: Update the architecture document**

In `docs/architecture.md`, replace the phase 3 bullet under "The tick" with:

```markdown
3. **Advance flow** — one downstream-first sweep over the material graph: each
   node discharges through its outgoing links into consumers that have already
   advanced and made room, then advances its own contents. The conservation
   audit runs after the sweep.
```

Append this section after "Feedback loops" (before "Determinism rules"):

```markdown
## Material flow

Mass travels on a second port graph, separate from signals. A `FlowOutlet`
feeds exactly one `FlowInlet` and an inlet has exactly one source — mass cannot
fan out or merge implicitly — and both ends must carry the same `PayloadKind`.
Flow ports never create signal-ordering edges: transport is phase 3, after
every component has evaluated, so a belt whose speed comes from a controller
that reads the belt's load is not an algebraic loop.

A component that holds material implements `IFlowNode` (usually by deriving
from `FlowComponentBase`) and, per port, one side of the transport protocol:

- **Bulk** — `IBulkProducer.OfferMass` says how much the producer wants to push,
  `IBulkConsumer.AcceptMass` how much the consumer can take, and the engine
  moves the minimum with `Withdraw` then `Deposit`. Back pressure is not a
  feature of any node; it is what a full consumer's `AcceptMass` returns.
- **Discrete** — `IItemProducer.TryPeekItem` shows the head item,
  `IItemConsumer.CanAcceptItem` says whether it fits, and the engine moves whole
  items until one side says no. `WithdrawItem` must return exactly the item
  last shown.

Bulk is a `BulkLot`: mass, one `MaterialType`, and `MaterialProperties`
(density, moisture, temperature) that blend by mass-weighted average when lots
merge. Discrete is an `ItemInstance` with an id from the simulation's
`ItemIdSequence` (so replays mint the same ids), a mass, properties, and a
state array sized by its material's schema.

`BulkBelt` is an array of cells. Each tick a fraction `v·dt/cellSize` of every
cell moves to its neighbour, resolved from the head backwards so a blocked
discharge builds load along the belt; validation refuses `cellSize <
maxSpeed·dt` (`MR006`). A cell never exceeds `maxLinearDensity·cellSize`, so
the inlet accepts only the room in the first cell. Speed zero freezes the load
profile exactly. `DiscreteBelt` carries items at continuous positions with no
diffusion; items queue behind a blocked head at the minimum spacing.

Transforms (`IMaterialTransform`) run on resident material every tick, before
it moves and whatever the speed, with ambient conditions taken from the node's
signal inputs. Bulk cells pass an empty state span in this version; items pass
their own.

Every tick the engine sums each node's `MassHeld`, `MassCreated` and
`MassDestroyed` and throws `MassConservationException` if
`created − destroyed − held` drifts beyond `SimulationOptions.ConservationTolerance`
(relative to the mass sourced). A node that injects mass reports it in
`MassCreated`; a node that removes it — a sink, a declared loss — reports it in
`MassDestroyed`. Anything else is a bug, and the audit finds it on the tick it
happens. Validation also rejects recirculation loops (`MR005`), inlets fed
from outside the plant (`MR007`) and flow ports whose owner lacks the
producer/consumer contract (`MR008`).
```

- [ ] **Step 5: Update the README status**

In `README.md`, replace the two paragraphs under `## Status` with:

```markdown
Under construction. This repository currently contains the simulation core —
deterministic clock, per-component random streams, typed signal ports,
composite components, topological resolution with algebraic-loop detection,
validation, telemetry, an ordered event log, a runner with real-time and scaled
execution — and the material layer: bulk and discrete payloads, typed flow
ports, offer/accept transport resolved downstream-first, cell-based bulk belts
and position-based discrete belts, residence transforms, and a per-tick mass
conservation audit.

The industrial component library, the I/O and real-time layers, declarative
configuration and the reference samples are planned. See
`docs/superpowers/specs/` for the design, `docs/superpowers/plans/` for the
implementation plans, and `docs/architecture.md` for how the engine works.
```

- [ ] **Step 6: Commit**

```bash
git add tests/Millrace.Core.Tests/Fakes/Setpoint.cs tests/Millrace.Core.Tests/FlowIntegrationTests.cs tests/Millrace.Core.Tests/ConservationPropertyTests.cs docs/architecture.md README.md
git commit -m "test(flow): add back-pressure acceptance tests and the conservation property test"
```

---

## Definition of done for this plan

- `dotnet test` passes with every test above (91 from plan 1 plus 106 new).
- `dotnet build --configuration Release` produces zero warnings.
- `Millrace.Core` and `Millrace.Io.Abstractions` still have no external package references.
- A feeder → belt → chute → belt → sink chain reaches steady state at the feed
  rate; a pulse arrives after exactly length-over-speed per belt plus one
  hand-off tick; stopping the downstream belt fills the chute and builds load
  along the upstream belt to its capacity without exceeding it; restarting
  drains it; two runs are indistinguishable.
- Twenty random bulk chains and twenty random discrete chains run 300 ticks
  each with the conservation audit on and never trip it.
- Validation reports recirculation, CFL violations, inlets fed from outside the
  plant and missing flow contracts, each message naming the fix.
- A `UnitDelay`-style test drives nothing here: the material graph has no
  algebraic loops by construction (recirculation is rejected), so plan 1's
  ordering guarantees are untouched.
- `docs/architecture.md` documents the material layer and the rules a flow-node
  author must follow.

## What this plan deliberately does not build

Real sources, sinks, chutes and transfer points, the `Former` (bulk → discrete),
`ProcessUnit`, the three shipped transforms, the belt scale and every other
instrument, motors and the belt composite that derives `maxLinearDensity` from
width and angle of repose — all plan 3, which also introduces the fault channel
alongside its first `IFaultTarget`s. Splitters, mergers, recirculation and the
`Disintegrator` are deferred (spec 18). Bulk cells carry no state array until a
process needs one. Nothing in this plan touches phases 4 and 5.
