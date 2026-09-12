# Component Library Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship `Dse.Components` — the industrial object library — on top of the
simulation core and material layer, plus the three Core seams it needs (latched
inputs, material observation, the fault channel), so that a conveyor composed
of a motor, gearbox, pulleys, belt, instruments, safety circuit and starter trips
its own overload when the belt downstream of it blocks, with no code anywhere
that says "if downstream stops, trip upstream".

**Architecture:** Three small additions to `Dse.Core` come first: an input port
may be *latched* (read one tick late, creating no ordering edge) so a reflected
torque path needs no `UnitDelay`; a flow node may be *observable* so an
instrument mounted on it can read its material during phase 2, when material is
frozen; and a component may be a *fault target*, receiving faults through the
event queue at phase 1 that change its state only. `Dse.Components` then adds
sources, sinks, a chute, a former, two process units, three transforms, an
instrument base with the full sensor-fault vocabulary, six instruments, the
motor with its I²t thermal model, the drivetrain, the safety circuit, the
starter, and the `Conveyor` composite that proves spec 7.8 and 8.3 end to end.

**Tech Stack:** .NET 10 (`net10.0`), C#, xUnit. No external runtime dependencies.

**Spec:** `docs/superpowers/specs/2026-09-02-industrial-process-simulation-engine-design.md`
(sections 6.4, 6.5, 7.6, 8 in full, 9.4, 9.5 as far as instrument health, 12 in
full, 17 "Components", and the per-component and integration lines of 16).

**Plan sequence:** This is plan 3 of 6. Plans 1 and 2 are merged on `master` at
`59ca777` (204 tests). Plan 4 adds I/O bindings, the tag image and quality, and
the real-time layer; plan 5 the catalogue, configuration, CLI and scenarios;
plan 6 the two reference samples. Nothing in this plan may reference those
subsystems: no tag names, no `Quality`, no JSON, no controller. Where plan 4
will need something (instrument health, writable operator inputs) this plan
exposes it as ordinary ports.

## Global Constraints

- Target framework `net10.0` for every project. `Dse.Components` references
  `Dse.Core` only. **Zero external runtime package references** in any shipping
  project. Test projects may reference test packages (same versions as
  `tests/Dse.Core.Tests/Dse.Core.Tests.csproj`).
- `Nullable` enabled, `TreatWarningsAsErrors` true, `GenerateDocumentationFile`
  true (a `<see cref>` to a type that does not exist yet is a **build error**;
  reference only types that already exist when the file is compiled),
  deterministic builds. These come from `Directory.Build.props`; do not repeat
  them in a csproj.
- **Never use `System.Random`.** Instruments take `ctx.Random` in `Initialize`.
  A sensor with zero noise must never draw from its stream.
- **Never use `string.GetHashCode()`** for anything that affects behaviour.
- **Never iterate a `Dictionary` or `HashSet` in per-tick code.** `List<T>` and
  arrays are fine. Dictionaries are allowed in constructors, `Initialize`,
  validation, `Build` and at fault-scheduling time.
- All formatting and parsing uses `CultureInfo.InvariantCulture`. Messages that
  embed a `double` use `string.Create(CultureInfo.InvariantCulture, $"...")`.
- **Flow components change their material only in `Advance` (phase 3).**
  `Evaluate` publishes outputs from the frozen state and reads signal inputs.
  This is what makes `IMaterialObservable` safe: during phase 2 no node moves,
  creates, destroys or re-types material. Sources create mass in `Advance`.
- **A component never reads another component except through its ports or
  through `IMaterialObservable`.** An instrument may hold a reference to the
  node it is mounted on and call `TryObserve` in `Evaluate`; it may not call
  anything else on it.
- **Reflected-torque inputs are latched.** Every input that carries a torque
  demand back toward the motor (`Gearbox.OutputTorqueDemand`,
  `Motor.TorqueDemand`) is declared `latched: true`. Speed inputs are live.
  A unit test that drives a component with a latched input by hand must call
  `Latch()` after every `Evaluate`, as `Simulation` does.
- **Faults change state and behaviour only.** `IFaultTarget.ApplyFault` and
  `ClearFault` never add, remove or rewire ports or components. They run in
  phase 1 with resolved arguments (every declared parameter present).
- Fault ids are lower-kebab-case string constants (`"bearing-friction"`).
  Event codes are UPPER_SNAKE (`AT_SPEED`). Event messages are human sentences
  ending in a full stop, numbers formatted invariant.
- Every port and every constructor parameter that carries a physical quantity
  states its unit in its XML doc: m, m/s, rad/s, N, N·m, A, kg, kg/s, kg/m, °C, s.
- Licence: MIT. Namespace root `Dse`. Core additions live in `Dse.Core.Faults`,
  `Dse.Core.Flow` and `Dse.Core.Graph`. Library namespaces:
  `Dse.Components.Flow`, `.Transforms`, `.Instruments`, `.Mechanical`,
  `.Safety`, `.Conveyors`.
- Commit trailers: every commit message body ends with the two attribution
  lines the session specifies (`Co-Authored-By: …` and `Claude-Session: …`),
  copied verbatim, in the body, never on the subject line.

## Decisions settled here (carry forward as rulings R13–R19)

- **R13 — belts have no direct feedthrough.** `BulkBelt.Load` and
  `DiscreteBelt.ItemCount` are computed from cells and items that only change
  in phase 3, so neither belt's outputs depend on its inputs within a tick.
  Both declare `HasDirectFeedthrough => false`. Without this, belt → friction
  → pulley → belt is a false algebraic loop.
- **R14 — latched inputs replace `UnitDelay` inside a component.** A
  bidirectional mechanical element (speed forward, torque back) adjacent to
  another forms a two-node cycle; a chain of three would need two `UnitDelay`s.
  An input declared latched reads the value captured in the latch pass of the
  previous tick and creates no ordering edge. `UnitDelay` stays for wiring-time
  loop breaking; the port-level form is for components that know an input is
  a reflection.
- **R15 — `ProcessUnit` is two classes.** A node handles one payload kind
  (spec 7.1), so the state machine ships as `BulkProcessUnit` (recipe by mass,
  one inlet per line) and `ItemProcessUnit` (batch by count). Mixer, prover,
  furnace and press remain configurations, not subclasses.
- **R16 — yield loss is a declared loss on the unit.** A process unit that
  changes type with yield < 1 reports the lost mass in `MassDestroyed` and
  publishes it as telemetry `Lost`, rather than requiring a wired loss sink.
  The audit is unchanged; a plant that wants to measure loss reads telemetry.
- **R17 — `MoistureLoss` changes the moisture fraction only.** Transforms have
  no mass authority (`IMaterialTransform.Apply` carries none), so evaporated
  water does not leave the ledger in v1. A dryer that must lose mass is a
  process unit with yield < 1. Documented on the transform.
- **R18 — `BulkBelt` and `DiscreteBelt` keep their names.** The composite that
  derives capacity from width and angle of repose is `Conveyor`; the belts are
  belts.
- **R19 — `Dse.Core` is not widened for the components tests.**
  `Dse.Components.Tests` uses the public API (`SimulationBuilder`,
  `Simulation.MassBalance`, `.Telemetry`, `.Events`) and its own fakes. The
  real sources and sinks in this plan replace the `Fakes.Flow` nodes it cannot
  see. `Dse.Components` grants `InternalsVisibleTo` to `Dse.Components.Tests`.

## Plan-1 and plan-2 facts this plan builds on

- `ComponentBase` (`Dse.Core.Graph`): `Id`, `Ports`, protected `AddPort`,
  `AddInput<T>(name, defaultValue = default, required = false)`,
  `AddOutput<T>(name)`, virtual `HasDirectFeedthrough` (true), virtual
  `Initialize(in InitContext)`, abstract `Evaluate(in TickContext)`, virtual
  `Latch()`. Task 1 changes `Latch` to non-virtual and adds `OnLatch`.
- `InputPort<T>(name, ownerId, defaultValue, isRequired)`: `Value` reads the
  source or the default; `ConnectFrom(OutputPort<T>)`. `OutputPort<T>.Value`
  is publicly settable, so a unit test drives an input by wiring a bare
  `new OutputPort<double>("Out", "SP")` to it. Inside a `Simulation` that is
  rejected (`DSE004`), so simulation-level tests use the `Setpoint` and
  `Switch` fakes this plan adds.
- `Port`: `Name`, `OwnerId`, `QualifiedName`, abstract
  `IsMissingRequiredConnection`, `internal abstract Port? SourcePort`.
- `GraphResolver.TryResolve` adds an edge producer → consumer for every port
  with a non-null `SourcePort` on a consumer whose `HasDirectFeedthrough` is
  true. `TopologicalSorter` is shared with `FlowGraph`.
- `CompositeComponent`: `AddChild<T>(child)` qualifies ids as
  `"{CompositeId}.{ChildId}"`; `Expose(alias, port)`; `Input<T>(alias)`,
  `Output<T>(alias)`, `Inlet(alias)`, `Outlet(alias)`. Composites never
  evaluate; `SimulationBuilder.Add` flattens them.
- `InitContext(DeterministicRandom random, TelemetryRegistry telemetry,
  ItemIdSequence items, string componentId, DateTimeOffset startTime, double dt)`
  with `Random`, `Items`, `StartTime`, `Dt`, `RegisterTelemetry(name, unit)`
  (key `"{componentId}.{name}"`).
- `TickContext(long tick, double dt, DateTimeOffset simTime, EventLog log)`
  with `Tick`, `Dt`, `SimTime`, `Log(source, code, message)`.
- `Simulation`: `Clock`, `Telemetry`, `Events`, `Items`, `Components`,
  `MassBalance`, `ScheduleAt(TimeSpan, ISimEvent)`, `ScheduleIn`, `Initialize()`,
  `Tick()`, `RunFor(TimeSpan)`. Internal constructor
  `Simulation(ISimComponent[] components, FlowGraph flow, SimulationOptions options)`.
  Phases: `DrainDueEvents`, `EvaluateSignals` (evaluate pass, latch pass),
  `AdvanceFlow` (`FlowGraph.Step(in ctx)` then the audit), `PublishIo`,
  `EmitFrame`, `Clock.Advance()`.
- `ISimEvent.Apply()` takes nothing; `CallbackEvent(Action)` exists.
  `EventQueue.Schedule(dueTick, payload)` returns a sequence number.
- `EventLog.Record(tick, simTime, source, code, message)`; `ToText()` is the
  golden-file format (`HH:mm:ss.fff  Source  CODE  Message`).
- `FlowComponentBase : ComponentBase, IFlowNode`: abstract `MassHeld`, virtual
  `MassCreated`/`MassDestroyed` (0), `Evaluate` (no-op), `Advance(in TickContext)`
  (no-op), `ValidateFlow(double dt)` (empty), `AddInlet(name, kind)`,
  `AddOutlet(name, kind)`.
- Transport protocol: `IBulkProducer.OfferMass(outlet)` / `Withdraw(outlet, mass)`;
  `IBulkConsumer.AcceptMass(inlet)` / `Deposit(inlet, in lot)`;
  `IItemProducer.TryPeekItem(outlet, out item)` / `WithdrawItem(outlet)`;
  `IItemConsumer.CanAcceptItem(inlet, item)` / `DepositItem(inlet, item)`.
  Phase 3 visits nodes most-downstream first; for each, links transfer, then
  `Advance` runs. Material deposited during a tick moves on the next.
- `BulkLot(MaterialType? Type, double Mass, MaterialProperties Properties)`:
  `Empty`, `IsEmpty`, `Of(type, mass, props)`, `Merge(in other)` (same type
  only, blends), `Take(mass, out remaining)`.
- `ItemInstance(long id, MaterialType type, double mass, MaterialProperties
  properties)`: `Id`, `Type`, settable `Mass` and `Properties`, `double[] State`,
  `ChangeType(MaterialType)` (resets state). `MaterialType(name, kind, params
  string[] stateSchema)`: `StateIndexOf(name)`, `NewState()`.
- `MaterialProperties(Density, Moisture, Temperature)` with static
  `Blend(in a, massA, in b, massB)`.
- `IMaterialTransform.Apply(ref MaterialProperties properties, Span<double> state,
  double dt, in TransformContext context)`; `TransformContext(double AmbientTemperature)`.
  Bulk cells pass `Span<double>.Empty`.
- `BulkBelt(id, length, cellSize, maxSpeed, maxLinearDensity, transforms = null)`:
  `In`, `Out`, `Speed` (m/s, default 0), `AmbientTemperature` (default 20),
  `Load` (kg), `PeakLinearDensity` (kg/m), `CellSize`, `CellCount`, `Cells`,
  `LinearDensityAt(position)`. Cells never exceed `maxLinearDensity · cellSize`;
  the inlet accepts only the room in cell 0.
- `DiscreteBelt(id, length, maxSpeed, minSpacing = 0, transforms = null)`:
  `In`, `Out`, `Speed`, `AmbientTemperature`, `ItemCount`, `Length`,
  `Items` (`IReadOnlyList<CarriedItem>`, head first; `CarriedItem(Item, Position)`,
  position measured from the tail).
- `SimulationOptions`: `Seed`, `StartTime`, `TimeStep` (10 ms),
  `CheckConservation` (true), `ConservationTolerance` (1e-9).
- `ValidationError(Code, Message, ComponentIds)`; codes `DSE001`–`DSE008` taken.
- Core test fakes (not visible to `Dse.Components.Tests`): `ConstantSource`,
  `Gain`, `Integrator`, `NoiseSource`, `Recorder`, `Setpoint`, `Tripper`,
  `TwoStage`, `Fakes/Flow/*`, `TestContexts.Init(id, dt = 0.01, …)`,
  `TestContexts.Tick(tick, dt = 0.01, log = null)`.

## File Structure

```
src/Dse.Core/
  Graph/Port.cs                          (modified: CreatesOrderingEdge, Capture)
  Graph/InputPort.cs                     (modified: latched inputs)
  Graph/ComponentBase.cs                 (modified: latched list, Latch/OnLatch, AddInput latched:)
  Graph/UnitDelay.cs                     (modified: OnLatch)
  Graph/GraphResolver.cs                 (modified: honours CreatesOrderingEdge)
  Flow/IMaterialObservable.cs            what an instrument may see of a node's material
  Flow/MaterialObservation.cs            mass, linear density, properties, item id
  Flow/BulkBelt.cs                       (modified: observable, no feedthrough)
  Flow/DiscreteBelt.cs                   (modified: observable, no feedthrough)
  Faults/FaultArgument.cs                name + value
  Faults/FaultArguments.cs               ordered, duplicate-free argument set
  Faults/FaultParameter.cs               name, unit, default, description
  Faults/FaultDescriptor.cs              id, description, parameters; Resolve
  Faults/IFaultTarget.cs                 SupportedFaults, ApplyFault, ClearFault
  Simulation.cs                          (modified: InjectFaultAt/In, ClearFaultAt/In, FaultsOf)

src/Dse.Components/
  Dse.Components.csproj
  Flow/BulkSource.cs                     rate-driven bulk source with hopper
  Flow/BulkSink.cs                       stockpile / loss sink, optional capacity
  Flow/ItemSource.cs                     interval-driven item source
  Flow/ItemSink.cs                       item sink, optional capacity
  Flow/TransferChute.cs                  capacity-limited bulk hold, blockage fault
  Flow/Former.cs                         bulk → discrete divider
  Flow/ProcessPhase.cs                   Idle | Filling | Processing | Discharging
  Flow/IHoldCondition.cs                 when a batch is done
  Flow/Hold.cs                           ForSeconds, TemperatureAtLeast/AtMost, StateAtLeast, All
  Flow/RecipeLine.cs                     inlet name, material, kg
  Flow/BulkProcessUnit.cs                recipe-filled batch unit
  Flow/ItemProcessUnit.cs                count-filled batch unit
  Transforms/ThermalTransfer.cs          lumped capacitance toward ambient
  Transforms/MoistureLoss.cs             rate as a function of temperature
  Transforms/ResidenceAccumulator.cs     time above a threshold, into a state slot
  Instruments/InstrumentHealth.cs        Good | Uncertain | Bad
  Instruments/InstrumentSpec.cs          unit, range, noise, lag
  Instruments/InstrumentFaults.cs        the shared fault vocabulary
  Instruments/InstrumentBase.cs          measure → calibrate → drift → noise → lag → fail
  Instruments/SpeedSensor.cs
  Instruments/CurrentSensor.cs
  Instruments/TemperatureSensor.cs
  Instruments/ZeroSpeedSwitch.cs
  Instruments/BeltScale.cs               linear density × speed → t/h
  Instruments/Pyrometer.cs               temperature of observed material
  Instruments/PartCounter.cs             new item ids seen in a window
  Mechanical/MotorRating.cs
  Mechanical/Motor.cs                    speed, torque, current, I²t thermal state
  Mechanical/Gearbox.cs
  Mechanical/DrivePulley.cs
  Mechanical/TailPulley.cs
  Mechanical/BeltFriction.cs             load → resistive force
  Mechanical/BeltGeometry.cs             width + angle of repose → max kg/m
  Mechanical/MotorStarter.cs             contactor + overload relay
  Safety/SafetySwitch.cs                 base for e-stop and pull-key
  Safety/EStop.cs
  Safety/PullKey.cs
  Safety/SafetyRelay.cs                  N channels, latching, reset
  Conveyors/ConveyorOptions.cs
  Conveyors/Conveyor.cs                  the composite

tests/Dse.Core.Tests/
  LatchedInputTests.cs, ObservationTests.cs, FaultChannelTests.cs,
  Fakes/Reflector.cs, Fakes/Fuse.cs

tests/Dse.Components.Tests/
  Dse.Components.Tests.csproj
  Fakes/TestContexts.cs, Fakes/Setpoint.cs, Fakes/Switch.cs, Fakes/ProbeInstrument.cs
  SourceSinkTests.cs, TransferChuteTests.cs, TransformTests.cs, FormerTests.cs,
  BulkProcessUnitTests.cs, ItemProcessUnitTests.cs, InstrumentBaseTests.cs,
  SignalInstrumentTests.cs, MaterialInstrumentTests.cs, MotorTests.cs,
  DrivetrainTests.cs, SafetyTests.cs, MotorStarterTests.cs, ConveyorTests.cs
```

---

### Task 1: Latched inputs

**Files:**
- Modify: `src/Dse.Core/Graph/Port.cs`
- Modify: `src/Dse.Core/Graph/InputPort.cs`
- Modify: `src/Dse.Core/Graph/ComponentBase.cs`
- Modify: `src/Dse.Core/Graph/UnitDelay.cs`
- Modify: `src/Dse.Core/Graph/GraphResolver.cs:71-85`
- Create: `tests/Dse.Core.Tests/Fakes/Reflector.cs`
- Test: `tests/Dse.Core.Tests/LatchedInputTests.cs`

**Interfaces:**
- Consumes: plan-1 port model.
- Produces:
  - `Port`: `internal virtual bool CreatesOrderingEdge => SourcePort is not null;`
    and `internal virtual void Capture() { }`.
  - `InputPort<T>(string name, string ownerId, T defaultValue, bool isRequired, bool isLatched = false)`
    with `bool IsLatched`; `Value` returns the captured value when latched.
  - `ComponentBase.AddInput<T>(string name, T defaultValue = default, bool required = false, bool latched = false)`;
    `public void Latch()` (no longer virtual) captures every latched input then
    calls `protected virtual void OnLatch()`.
  - `UnitDelay<T>` overrides `OnLatch`.
  - Test fake `Reflector(string id, double gain)` — a bidirectional element:
    live `Forward` in → `ForwardOut = Forward × gain`; latched `Back` in →
    `BackOut = Back / gain`.

R14. A latched input is the port-level form of `UnitDelay`: it reads the value
captured at the end of the previous tick and creates no ordering edge, so a
component can declare "this input is a reflection, one tick late is correct"
without the plant author wiring a delay. `ISimComponent.Latch()` is already
called for every component after the evaluate pass; `ComponentBase` now uses
it to capture, and subclasses that need the hook override `OnLatch`.

- [ ] **Step 1: Write the fake and the failing tests**

`tests/Dse.Core.Tests/Fakes/Reflector.cs`:

```csharp
using Dse.Core.Contexts;
using Dse.Core.Graph;

namespace Dse.Core.Tests.Fakes;

/// <summary>
/// A bidirectional element: a value passes forward live, and a value coming
/// back is read one tick late. Two of these in a row would be an algebraic
/// loop if the back path were live.
/// </summary>
public sealed class Reflector : ComponentBase
{
    private readonly double _gain;

    public Reflector(string id, double gain)
        : base(id)
    {
        _gain = gain;
        Forward = AddInput<double>("Forward");
        Back = AddInput<double>("Back", latched: true);
        ForwardOut = AddOutput<double>("ForwardOut");
        BackOut = AddOutput<double>("BackOut");
    }

    public InputPort<double> Forward { get; }

    public InputPort<double> Back { get; }

    public OutputPort<double> ForwardOut { get; }

    public OutputPort<double> BackOut { get; }

    public override void Evaluate(in TickContext ctx)
    {
        ForwardOut.Value = Forward.Value * _gain;
        BackOut.Value = Back.Value / _gain;
    }
}
```

`tests/Dse.Core.Tests/LatchedInputTests.cs`:

```csharp
using Dse.Core;
using Dse.Core.Graph;
using Dse.Core.Tests.Fakes;
using Dse.Core.Time;
using Dse.Core.Validation;
using Xunit;

namespace Dse.Core.Tests;

public class LatchedInputTests
{
    private static SimulationOptions Options() => new()
    {
        Seed = 1UL,
        StartTime = new DateTimeOffset(2026, 1, 1, 6, 0, 0, TimeSpan.Zero),
        TimeStep = TimeSpan.FromMilliseconds(10),
    };

    [Fact]
    public void ALatchedInputReadsItsDefaultUntilTheFirstLatch()
    {
        var reflector = new Reflector("R", 2.0);
        var driver = new OutputPort<double>("Out", "SP");
        driver.ConnectTo(reflector.Back);
        driver.Value = 8.0;

        reflector.Evaluate(TestContexts.Tick(0));
        Assert.Equal(0.0, reflector.BackOut.Value);

        reflector.Latch();
        reflector.Evaluate(TestContexts.Tick(1));
        Assert.Equal(4.0, reflector.BackOut.Value);
    }

    [Fact]
    public void ALatchedInputLagsItsProducerByExactlyOneTick()
    {
        var reflector = new Reflector("R", 2.0);
        var driver = new OutputPort<double>("Out", "SP");
        driver.ConnectTo(reflector.Back);

        var seen = new List<double>();
        for (int tick = 0; tick < 4; tick++)
        {
            driver.Value = tick + 1;
            reflector.Evaluate(TestContexts.Tick(tick));
            seen.Add(reflector.Back.Value);
            reflector.Latch();
        }

        Assert.Equal([0.0, 1.0, 2.0, 3.0], seen);
    }

    [Fact]
    public void ALatchedInputCreatesNoOrderingEdge()
    {
        var a = new Reflector("A", 2.0);
        var b = new Reflector("B", 3.0);
        a.ForwardOut.ConnectTo(b.Forward);   // live: A before B
        b.BackOut.ConnectTo(a.Back);         // latched: no edge B -> A

        ValidationResult result = new SimulationBuilder(Options()).Add(b).Add(a).Validate();

        Assert.True(result.IsValid, result.ToText());
        Simulation sim = new SimulationBuilder(Options()).Add(b).Add(a).Build();
        Assert.Equal(["A", "B"], sim.Components.Select(c => c.Id));
    }

    [Fact]
    public void ALiveBackPathIsStillAnAlgebraicLoop()
    {
        var a = new Reflector("A", 2.0);
        var b = new Reflector("B", 3.0);
        a.ForwardOut.ConnectTo(b.Forward);
        b.BackOut.ConnectTo(a.Forward);      // live both ways

        ValidationResult result = new SimulationBuilder(Options()).Add(a).Add(b).Validate();

        Assert.Contains(result.Errors, e => e.Code == "DSE003");
    }

    [Fact]
    public void AChainOfReflectorsSettlesInASimulation()
    {
        var source = new ConstantSource("S", 1.0);
        var a = new Reflector("A", 2.0);
        var b = new Reflector("B", 3.0);
        var load = new Gain("L", 5.0);
        source.Out.ConnectTo(a.Forward);
        a.ForwardOut.ConnectTo(b.Forward);
        b.ForwardOut.ConnectTo(load.In);
        load.Out.ConnectTo(b.Back);
        b.BackOut.ConnectTo(a.Back);

        Simulation sim = new SimulationBuilder(Options())
            .Add(load).Add(b).Add(a).Add(source).Build();
        sim.RunFor(TimeSpan.FromMilliseconds(50));

        // Forward: 1 -> 2 -> 6 -> load 30. Back: 30 / 3 = 10 (one tick late), 10 / 2 = 5 (two ticks late).
        Assert.Equal(30.0, load.Out.Value);
        Assert.Equal(10.0, b.BackOut.Value);
        Assert.Equal(5.0, a.BackOut.Value);
    }

    [Fact]
    public void ALatchedInputFromOutsideThePlantIsStillRejected()
    {
        var a = new Reflector("A", 2.0);
        var stray = new Reflector("Stray", 1.0);
        stray.BackOut.ConnectTo(a.Back);

        ValidationResult result = new SimulationBuilder(Options()).Add(a).Validate();

        Assert.Contains(result.Errors, e => e.Code == "DSE004");
    }

    [Fact]
    public void UnitDelayStillLagsByOneTick()
    {
        var source = new ConstantSource("S", 7.0);
        var delay = new UnitDelay<double>("D");
        var recorder = new Recorder("R");
        source.Out.ConnectTo(delay.In);
        delay.Out.ConnectTo(recorder.In);

        Simulation sim = new SimulationBuilder(Options()).Add(recorder).Add(delay).Add(source).Build();
        sim.RunFor(TimeSpan.FromMilliseconds(30));

        Assert.Equal([0.0, 7.0, 7.0], recorder.Samples);
    }
}
```

`Gain` exists in `tests/Dse.Core.Tests/Fakes/Gain.cs` (`Gain(string id, double factor)`,
`In`, `Out`). Check its constructor before relying on the name of the factor
parameter; only the port names matter here.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Dse.Core.Tests --filter "FullyQualifiedName~LatchedInputTests"`
Expected: build error — `AddInput` has no `latched` parameter.

- [ ] **Step 3: Implement**

`src/Dse.Core/Graph/Port.cs` — add after `SourcePort`:

```csharp
    /// <summary>
    /// Whether the resolver should order this port's owner after the port's
    /// source. False for outputs, for flow ports, and for latched inputs, which
    /// read one tick late by design.
    /// </summary>
    internal virtual bool CreatesOrderingEdge => SourcePort is not null;

    /// <summary>Called in the latch pass. Latched inputs capture their source here.</summary>
    internal virtual void Capture()
    {
    }
```

`src/Dse.Core/Graph/InputPort.cs` — replace the class body:

```csharp
/// <summary>
/// An input. Exactly one source, because two sources is an undefined value.
/// Unconnected inputs read their declared default so partial plants still run.
/// A latched input reads the value captured at the end of the previous tick
/// and creates no ordering edge — the port-level way to declare that one tick
/// of lag on a reflected quantity is physically correct.
/// </summary>
public sealed class InputPort<T> : Port
    where T : unmanaged
{
    private OutputPort<T>? _source;
    private T _captured;

    public InputPort(string name, string ownerId, T defaultValue, bool isRequired, bool isLatched = false)
        : base(name, ownerId)
    {
        DefaultValue = defaultValue;
        IsRequired = isRequired;
        IsLatched = isLatched;
        _captured = defaultValue;
    }

    public T DefaultValue { get; }

    public bool IsRequired { get; }

    /// <summary>True when <see cref="Value"/> is the value latched at the end of the previous tick.</summary>
    public bool IsLatched { get; }

    public T Value => IsLatched ? _captured : Live;

    private T Live => _source is null ? DefaultValue : _source.Value;

    public override bool IsMissingRequiredConnection => IsRequired && _source is null;

    internal override Port? SourcePort => _source;

    internal override bool CreatesOrderingEdge => !IsLatched && _source is not null;

    internal override void Capture()
    {
        if (IsLatched)
        {
            _captured = Live;
        }
    }

    public void ConnectFrom(OutputPort<T> source)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (_source is not null)
        {
            throw new InvalidOperationException(
                $"Input '{QualifiedName}' is already driven by '{_source.QualifiedName}'. " +
                $"An input accepts exactly one source; remove one of the connections.");
        }

        _source = source;
    }
}
```

`src/Dse.Core/Graph/ComponentBase.cs` — add a field, extend `AddInput`, replace `Latch`:

```csharp
    private readonly List<Port> _ports = [];
    private readonly List<Port> _latched = [];
```

A latched input must be discoverable without reflection (spec 14: no attribute magic), so `Port` carries a marker. Add to `Port.cs`:

```csharp
    /// <summary>True for inputs that capture in the latch pass.</summary>
    internal virtual bool IsLatchedInput => false;
```

and to `InputPort<T>`:

```csharp
    internal override bool IsLatchedInput => IsLatched;
```

Then in `ComponentBase.AddPort`, after `_ports.Add(port);`:

```csharp
        if (port.IsLatchedInput)
        {
            _latched.Add(port);
        }
```

Replace `AddInput` and `Latch`:

```csharp
    protected InputPort<T> AddInput<T>(string name, T defaultValue = default, bool required = false, bool latched = false)
        where T : unmanaged =>
        AddPort(new InputPort<T>(name, Id, defaultValue, required, latched));

    /// <summary>
    /// The latch pass: every latched input captures this tick's source value,
    /// then <see cref="OnLatch"/> runs. Sealed so a subclass cannot forget the
    /// capture; override <see cref="OnLatch"/> for component-specific state.
    /// </summary>
    public void Latch()
    {
        for (int i = 0; i < _latched.Count; i++)
        {
            _latched[i].Capture();
        }

        OnLatch();
    }

    /// <summary>Called once per tick after every component has evaluated and this component's latched inputs have captured.</summary>
    protected virtual void OnLatch()
    {
    }
```

`src/Dse.Core/Graph/UnitDelay.cs` — replace the `Latch` override:

```csharp
    protected override void OnLatch() => _held = In.Value;
```

`src/Dse.Core/Graph/GraphResolver.cs` — in the port loop replace

```csharp
                Port? source = port.SourcePort;
                if (source is null || !indexById.TryGetValue(source.OwnerId, out int producer))
```

with

```csharp
                if (!port.CreatesOrderingEdge)
                {
                    continue;
                }

                Port? source = port.SourcePort;
                if (source is null || !indexById.TryGetValue(source.OwnerId, out int producer))
```

`FlowPort.SourcePort` already returns null, so flow ports keep creating no edge.

- [ ] **Step 4: Run the whole Core suite**

Run: `dotnet test tests/Dse.Core.Tests`
Expected: 204 existing tests still pass plus 7 new — 211. `UnitDelayTests`
still calls `delay.Latch()` directly; that is now the sealed public method and
behaves the same.

- [ ] **Step 5: Commit**

```bash
git add src/Dse.Core/Graph tests/Dse.Core.Tests/Fakes/Reflector.cs tests/Dse.Core.Tests/LatchedInputTests.cs
git commit -m "feat(core): add latched inputs that read one tick late and create no ordering edge"
```

---

### Task 2: Material observation and belt feedthrough

**Files:**
- Create: `src/Dse.Core/Flow/IMaterialObservable.cs`
- Create: `src/Dse.Core/Flow/MaterialObservation.cs`
- Modify: `src/Dse.Core/Flow/BulkBelt.cs`
- Modify: `src/Dse.Core/Flow/DiscreteBelt.cs`
- Test: `tests/Dse.Core.Tests/ObservationTests.cs`

**Interfaces:**
- Consumes: `BulkBelt`, `DiscreteBelt`, `BulkLot`, `ItemInstance` (plan 2).
- Produces:
  - `readonly record struct MaterialObservation(double Mass, double LinearDensity, MaterialProperties Properties, long ItemId)`
    — `LinearDensity` is kg/m for bulk and 0 for an item; `ItemId` is 0 for bulk.
  - `interface IMaterialObservable { bool TryObserve(double position, double window, out MaterialObservation observation); }`
  - `BulkBelt : IMaterialObservable` (the cell under `position`; `window` ignored),
    `DiscreteBelt : IMaterialObservable` (the head-most item within `window`
    metres of `position`). Both now `HasDirectFeedthrough => false` (R13).

Spec 7.6: "the belt scale reads linear density at its position multiplied by
belt speed". Spec 15.2: "a pyrometer reading item temperature". Neither can be
a signal port on the belt without the belt knowing where every instrument is
mounted, and neither may reach into a sibling. The rule that makes a direct
read safe: material changes only in phase 3, so during phase 2 every node's
contents are frozen and the read is independent of evaluation order.

- [ ] **Step 1: Write the failing tests**

`tests/Dse.Core.Tests/ObservationTests.cs`:

```csharp
using Dse.Core.Flow;
using Dse.Core.Graph;
using Dse.Core.Tests.Fakes;
using Xunit;

namespace Dse.Core.Tests;

public class ObservationTests
{
    private static readonly MaterialType Ore = new("Ore", PayloadKind.Bulk);
    private static readonly MaterialType Billet = new("Billet", PayloadKind.Discrete);

    [Fact]
    public void ABulkBeltReportsTheCellUnderThePosition()
    {
        var belt = new BulkBelt("CV", length: 2.0, cellSize: 0.5, maxSpeed: 1.0, maxLinearDensity: 10.0);
        belt.Initialize(TestContexts.Init(belt.Id, dt: 0.5));
        belt.Deposit(belt.In, BulkLot.Of(Ore, 2.0, new MaterialProperties(1600.0, 0.1, 30.0)));

        Assert.True(belt.TryObserve(0.25, 0.0, out MaterialObservation tail));
        Assert.Equal(2.0, tail.Mass);
        Assert.Equal(4.0, tail.LinearDensity);
        Assert.Equal(30.0, tail.Properties.Temperature);
        Assert.Equal(0L, tail.ItemId);

        Assert.False(belt.TryObserve(1.75, 0.0, out _));
    }

    [Fact]
    public void ADiscreteBeltReportsTheItemWithinTheWindow()
    {
        var belt = new DiscreteBelt("CV", length: 4.0, maxSpeed: 1.0);
        belt.Initialize(TestContexts.Init(belt.Id, dt: 0.5));
        var speed = new OutputPort<double>("Out", "SP");
        speed.ConnectTo(belt.Speed);
        speed.Value = 1.0;

        var first = new ItemInstance(1, Billet, 5.0, new MaterialProperties(7800.0, 0.0, 900.0));
        belt.DepositItem(belt.In, first);
        belt.Advance(TestContexts.Tick(0, dt: 0.5));   // first at 0.5
        belt.Advance(TestContexts.Tick(1, dt: 0.5));   // first at 1.0
        belt.DepositItem(belt.In, new ItemInstance(2, Billet, 5.0, default));

        Assert.True(belt.TryObserve(1.0, 0.1, out MaterialObservation seen));
        Assert.Equal(1L, seen.ItemId);
        Assert.Equal(900.0, seen.Properties.Temperature);
        Assert.Equal(5.0, seen.Mass);
        Assert.Equal(0.0, seen.LinearDensity);

        Assert.True(belt.TryObserve(0.05, 0.1, out MaterialObservation tail));
        Assert.Equal(2L, tail.ItemId);
        Assert.False(belt.TryObserve(2.0, 0.1, out _));
    }

    [Fact]
    public void BeltsDeclareNoDirectFeedthrough()
    {
        Assert.False(new BulkBelt("B", 1.0, 0.5, 1.0, 1.0).HasDirectFeedthrough);
        Assert.False(new DiscreteBelt("D", 1.0, 1.0).HasDirectFeedthrough);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Dse.Core.Tests --filter "FullyQualifiedName~ObservationTests"`
Expected: build error — `MaterialObservation` does not exist.

- [ ] **Step 3: Implement**

`src/Dse.Core/Flow/MaterialObservation.cs`:

```csharp
namespace Dse.Core.Flow;

/// <summary>
/// What an instrument sees when it looks at one point on a node. For bulk,
/// the mass and linear density of the cell there; for an item, its mass and
/// id with <see cref="LinearDensity"/> zero.
/// </summary>
/// <param name="Mass">kg.</param>
/// <param name="LinearDensity">kg/m; zero for discrete items.</param>
/// <param name="Properties">The material's intensive properties.</param>
/// <param name="ItemId">The item's id, or zero for bulk.</param>
public readonly record struct MaterialObservation(
    double Mass,
    double LinearDensity,
    MaterialProperties Properties,
    long ItemId);
```

`src/Dse.Core/Flow/IMaterialObservable.cs`:

```csharp
namespace Dse.Core.Flow;

/// <summary>
/// A node an instrument can be mounted on. Safe to call during phase 2 only:
/// material changes exclusively in phase 3, so what an instrument reads in
/// its Evaluate is the same whatever the evaluation order. An instrument
/// holds a reference to the node and calls nothing else on it.
/// </summary>
public interface IMaterialObservable
{
    /// <summary>
    /// The material at <paramref name="position"/> metres from the node's
    /// inlet end, looking <paramref name="window"/> metres either side. Nodes
    /// without a length (a chute, a process unit) ignore both and report their
    /// contents. False when nothing is there.
    /// </summary>
    bool TryObserve(double position, double window, out MaterialObservation observation);
}
```

`src/Dse.Core/Flow/BulkBelt.cs` — declare the interface, add the override and
the method:

```csharp
public sealed class BulkBelt : FlowComponentBase, IBulkProducer, IBulkConsumer, IMaterialObservable
```

```csharp
    /// <summary>
    /// Load and peak density come from the cells, which move only in phase 3,
    /// so nothing this belt outputs depends on its inputs within a tick.
    /// </summary>
    public override bool HasDirectFeedthrough => false;

    public bool TryObserve(double position, double window, out MaterialObservation observation)
    {
        int index = Math.Clamp((int)(position / CellSize), 0, _cells.Length - 1);
        BulkLot cell = _cells[index];
        if (cell.IsEmpty)
        {
            observation = default;
            return false;
        }

        observation = new MaterialObservation(cell.Mass, cell.Mass / CellSize, cell.Properties, 0L);
        return true;
    }
```

`src/Dse.Core/Flow/DiscreteBelt.cs` — the same shape:

```csharp
public sealed class DiscreteBelt : FlowComponentBase, IItemProducer, IItemConsumer, IMaterialObservable
```

```csharp
    /// <summary>Item count comes from items that move only in phase 3; see <see cref="BulkBelt.HasDirectFeedthrough"/>.</summary>
    public override bool HasDirectFeedthrough => false;

    /// <summary>The head-most item within the window; a photo-eye sees the first thing to reach it.</summary>
    public bool TryObserve(double position, double window, out MaterialObservation observation)
    {
        for (int i = 0; i < _items.Count; i++)
        {
            if (Math.Abs(_items[i].Position - position) <= window)
            {
                ItemInstance item = _items[i].Item;
                observation = new MaterialObservation(item.Mass, 0.0, item.Properties, item.Id);
                return true;
            }
        }

        observation = default;
        return false;
    }
```

- [ ] **Step 4: Run the whole Core suite**

Run: `dotnet test tests/Dse.Core.Tests`
Expected: 214 pass. `FlowIntegrationTests` and `DiscreteBeltTests` still pass:
belts read `Speed` in `Advance`, after the whole evaluate pass, so their
placement in phase 2 never mattered.

- [ ] **Step 5: Commit**

```bash
git add src/Dse.Core/Flow tests/Dse.Core.Tests/ObservationTests.cs
git commit -m "feat(core): let instruments observe a node's material and drop belt feedthrough"
```

---

### Task 3: The fault channel

**Files:**
- Create: `src/Dse.Core/Faults/FaultArgument.cs`
- Create: `src/Dse.Core/Faults/FaultArguments.cs`
- Create: `src/Dse.Core/Faults/FaultParameter.cs`
- Create: `src/Dse.Core/Faults/FaultDescriptor.cs`
- Create: `src/Dse.Core/Faults/IFaultTarget.cs`
- Modify: `src/Dse.Core/Simulation.cs`
- Create: `tests/Dse.Core.Tests/Fakes/Fuse.cs`
- Test: `tests/Dse.Core.Tests/FaultChannelTests.cs`

**Interfaces:**
- Consumes: `Simulation`, `EventQueue`, `ISimEvent`, `EventLog` (plan 1).
- Produces:
  - `readonly record struct FaultArgument(string Name, double Value)`
  - `sealed class FaultArguments` — `static FaultArguments None`,
    `FaultArguments(params FaultArgument[] arguments)` (duplicate names throw),
    `int Count`, `FaultArgument this[int index]`, `double Get(string name)`
    (throws `KeyNotFoundException` naming what is present),
    `bool TryGet(string name, out double value)`, `override string ToString()`
    → `"name=value, name=value"` invariant, or `""`.
  - `sealed record FaultParameter(string Name, string Unit, double DefaultValue, string Description)`
  - `sealed record FaultDescriptor(string Id, string Description, IReadOnlyList<FaultParameter> Parameters)`
    with ctor `FaultDescriptor(string id, string description, params FaultParameter[] parameters)`
    and `FaultArguments Resolve(FaultArguments given)` — fills defaults in
    declared order; an unknown name throws `ArgumentException` listing the
    declared names.
  - `interface IFaultTarget : ISimComponent { IReadOnlyList<FaultDescriptor> SupportedFaults { get; } void ApplyFault(string faultId, FaultArguments arguments); void ClearFault(string faultId); }`
  - `Simulation`: `IReadOnlyList<FaultDescriptor> FaultsOf(string componentId)`,
    `long InjectFaultAt(TimeSpan fromStart, string componentId, string faultId, FaultArguments? arguments = null)`,
    `long InjectFaultIn(TimeSpan delay, …)`, `long ClearFaultAt(TimeSpan fromStart, string componentId, string faultId)`,
    `long ClearFaultIn(TimeSpan delay, string componentId, string faultId)`.
    Injection logs `FAULT` "`{faultId}` injected: `{arguments}`." and clearing
    logs `FAULT_CLEARED` "`{faultId}` cleared." with the component as source.

Spec 12: a component publishes the faults it supports and their parameter
schema; faults arrive through the event queue at a tick and change state only.
Resolution — component id to target, fault id to descriptor, arguments to a
full set — happens when the fault is *scheduled*, so a typo fails at the call
site with a message naming what exists, not inside phase 1 of some later tick.

- [ ] **Step 1: Write the fake and the failing tests**

`tests/Dse.Core.Tests/Fakes/Fuse.cs`:

```csharp
using Dse.Core.Contexts;
using Dse.Core.Faults;
using Dse.Core.Graph;

namespace Dse.Core.Tests.Fakes;

/// <summary>Conducts until blown. The one fault has one parameter so resolution can be tested.</summary>
public sealed class Fuse : ComponentBase, IFaultTarget
{
    public const string Blow = "blow";

    private static readonly FaultDescriptor[] Faults =
    [
        new(Blow, "Opens the fuse.", new FaultParameter("resistance", "ohm", 1.0e6, "Resistance once open.")),
    ];

    private bool _blown;

    public Fuse(string id)
        : base(id)
    {
        Ok = AddOutput<bool>("Ok");
        Resistance = AddOutput<double>("Resistance");
    }

    public OutputPort<bool> Ok { get; }

    public OutputPort<double> Resistance { get; }

    public IReadOnlyList<FaultDescriptor> SupportedFaults => Faults;

    public FaultArguments? LastArguments { get; private set; }

    public void ApplyFault(string faultId, FaultArguments arguments)
    {
        _blown = true;
        LastArguments = arguments;
        Resistance.Value = arguments.Get("resistance");
    }

    public void ClearFault(string faultId)
    {
        _blown = false;
        Resistance.Value = 0.0;
    }

    public override void Evaluate(in TickContext ctx) => Ok.Value = !_blown;
}
```

`tests/Dse.Core.Tests/FaultChannelTests.cs`:

```csharp
using Dse.Core;
using Dse.Core.Faults;
using Dse.Core.Tests.Fakes;
using Dse.Core.Time;
using Xunit;

namespace Dse.Core.Tests;

public class FaultChannelTests
{
    private static SimulationOptions Options() => new()
    {
        Seed = 1UL,
        StartTime = new DateTimeOffset(2026, 1, 1, 6, 0, 0, TimeSpan.Zero),
        TimeStep = TimeSpan.FromMilliseconds(10),
    };

    [Fact]
    public void ArgumentsRejectDuplicateNamesAndFormatInvariantly()
    {
        Assert.Throws<ArgumentException>(() => new FaultArguments(new("a", 1.0), new("a", 2.0)));

        var args = new FaultArguments(new("gain", 1.5), new("offset", -0.25));
        Assert.Equal(2, args.Count);
        Assert.Equal(1.5, args.Get("gain"));
        Assert.True(args.TryGet("offset", out double offset));
        Assert.Equal(-0.25, offset);
        Assert.False(args.TryGet("missing", out _));
        Assert.Equal("gain=1.5, offset=-0.25", args.ToString());
        Assert.Equal("", FaultArguments.None.ToString());
    }

    [Fact]
    public void ADescriptorFillsDefaultsAndRejectsUnknownNames()
    {
        var descriptor = new FaultDescriptor(
            "calibration",
            "Gain and offset error.",
            new FaultParameter("gain", "", 1.0, "Multiplier."),
            new FaultParameter("offset", "unit", 0.0, "Added."));

        FaultArguments resolved = descriptor.Resolve(new FaultArguments(new("offset", 2.0)));
        Assert.Equal("gain=1, offset=2", resolved.ToString());

        var ex = Assert.Throws<ArgumentException>(() => descriptor.Resolve(new FaultArguments(new("gian", 2.0))));
        Assert.Contains("gian", ex.Message);
        Assert.Contains("gain, offset", ex.Message);
    }

    [Fact]
    public void AFaultIsAppliedInPhaseOneOfItsTick()
    {
        var fuse = new Fuse("F1");
        Simulation sim = new SimulationBuilder(Options()).Add(fuse).Build();

        sim.InjectFaultAt(TimeSpan.FromMilliseconds(30), "F1", Fuse.Blow);
        sim.RunFor(TimeSpan.FromMilliseconds(30));
        Assert.True(fuse.Ok.Value);

        sim.Tick();   // tick 3: the fault lands before Evaluate

        Assert.False(fuse.Ok.Value);
        Assert.Equal(1.0e6, fuse.Resistance.Value);
        Assert.Equal("resistance=1000000", fuse.LastArguments!.ToString());
        var record = Assert.Single(sim.Events.Records);
        Assert.Equal((3L, "F1", "FAULT", "blow injected: resistance=1000000."), (record.Tick, record.Source, record.Code, record.Message));
    }

    [Fact]
    public void ArgumentsOverrideDefaults()
    {
        var fuse = new Fuse("F1");
        Simulation sim = new SimulationBuilder(Options()).Add(fuse).Build();

        sim.InjectFaultIn(TimeSpan.Zero, "F1", Fuse.Blow, new FaultArguments(new("resistance", 50.0)));
        sim.Tick();

        Assert.Equal(50.0, fuse.Resistance.Value);
    }

    [Fact]
    public void ClearingRestoresTheComponentAndLogs()
    {
        var fuse = new Fuse("F1");
        Simulation sim = new SimulationBuilder(Options()).Add(fuse).Build();

        sim.InjectFaultAt(TimeSpan.Zero, "F1", Fuse.Blow);
        sim.ClearFaultAt(TimeSpan.FromMilliseconds(20), "F1", Fuse.Blow);
        sim.RunFor(TimeSpan.FromMilliseconds(30));

        Assert.True(fuse.Ok.Value);
        Assert.Equal(["FAULT", "FAULT_CLEARED"], sim.Events.Records.Select(r => r.Code));
        Assert.Equal("blow cleared.", sim.Events.Records[1].Message);
    }

    [Fact]
    public void SchedulingNamesWhatExistsWhenTheTargetOrFaultIsUnknown()
    {
        var fuse = new Fuse("F1");
        var plain = new ConstantSource("C1", 1.0);
        Simulation sim = new SimulationBuilder(Options()).Add(fuse).Add(plain).Build();

        var missing = Assert.Throws<KeyNotFoundException>(() => sim.InjectFaultAt(TimeSpan.Zero, "F2", Fuse.Blow));
        Assert.Contains("F1", missing.Message);

        var notATarget = Assert.Throws<KeyNotFoundException>(() => sim.InjectFaultAt(TimeSpan.Zero, "C1", Fuse.Blow));
        Assert.Contains("IFaultTarget", notATarget.Message);

        var unknownFault = Assert.Throws<ArgumentException>(() => sim.InjectFaultAt(TimeSpan.Zero, "F1", "melt"));
        Assert.Contains("blow", unknownFault.Message);

        Assert.Equal(["blow"], sim.FaultsOf("F1").Select(f => f.Id));
    }

    [Fact]
    public void TwoFaultsOnTheSameTickApplyInScheduleOrder()
    {
        var fuse = new Fuse("F1");
        Simulation sim = new SimulationBuilder(Options()).Add(fuse).Build();

        sim.InjectFaultAt(TimeSpan.Zero, "F1", Fuse.Blow, new FaultArguments(new("resistance", 1.0)));
        sim.InjectFaultAt(TimeSpan.Zero, "F1", Fuse.Blow, new FaultArguments(new("resistance", 2.0)));
        sim.Tick();

        Assert.Equal(2.0, fuse.Resistance.Value);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Dse.Core.Tests --filter "FullyQualifiedName~FaultChannelTests"`
Expected: build error — namespace `Dse.Core.Faults` does not exist.

- [ ] **Step 3: Implement the fault types**

`src/Dse.Core/Faults/FaultArgument.cs`:

```csharp
namespace Dse.Core.Faults;

/// <summary>One named numeric argument to a fault.</summary>
public readonly record struct FaultArgument(string Name, double Value);
```

`src/Dse.Core/Faults/FaultArguments.cs`:

```csharp
using System.Globalization;
using System.Text;

namespace Dse.Core.Faults;

/// <summary>
/// The arguments a fault is injected with. Small and ordered — a fault has a
/// handful of parameters — so lookup is a linear scan and iteration is stable.
/// </summary>
public sealed class FaultArguments
{
    private readonly FaultArgument[] _arguments;

    public static FaultArguments None { get; } = new();

    public FaultArguments(params FaultArgument[] arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        for (int i = 0; i < arguments.Length; i++)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(arguments[i].Name, nameof(arguments));
            for (int j = 0; j < i; j++)
            {
                if (string.Equals(arguments[i].Name, arguments[j].Name, StringComparison.Ordinal))
                {
                    throw new ArgumentException(
                        $"Argument '{arguments[i].Name}' is given twice.", nameof(arguments));
                }
            }
        }

        _arguments = arguments.ToArray();
    }

    public int Count => _arguments.Length;

    public FaultArgument this[int index] => _arguments[index];

    public bool TryGet(string name, out double value)
    {
        for (int i = 0; i < _arguments.Length; i++)
        {
            if (string.Equals(_arguments[i].Name, name, StringComparison.Ordinal))
            {
                value = _arguments[i].Value;
                return true;
            }
        }

        value = 0.0;
        return false;
    }

    public double Get(string name)
    {
        if (TryGet(name, out double value))
        {
            return value;
        }

        throw new KeyNotFoundException(
            $"No fault argument named '{name}'. Present: {string.Join(", ", _arguments.Select(a => a.Name))}.");
    }

    /// <summary><c>name=value, name=value</c>, invariant. Empty for no arguments.</summary>
    public override string ToString()
    {
        var builder = new StringBuilder();
        for (int i = 0; i < _arguments.Length; i++)
        {
            if (i > 0)
            {
                builder.Append(", ");
            }

            builder.Append(_arguments[i].Name)
                   .Append('=')
                   .Append(_arguments[i].Value.ToString(CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }
}
```

`src/Dse.Core/Faults/FaultParameter.cs`:

```csharp
namespace Dse.Core.Faults;

/// <summary>One parameter of a fault, with the default used when an injection omits it.</summary>
public sealed record FaultParameter(string Name, string Unit, double DefaultValue, string Description);
```

`src/Dse.Core/Faults/FaultDescriptor.cs`:

```csharp
namespace Dse.Core.Faults;

/// <summary>
/// What a component says about one way it can break: an id, a sentence, and
/// the parameters an injection may set. Tooling asks "what can break here?"
/// and gets this.
/// </summary>
public sealed record FaultDescriptor(string Id, string Description, IReadOnlyList<FaultParameter> Parameters)
{
    public FaultDescriptor(string id, string description, params FaultParameter[] parameters)
        : this(id, description, (IReadOnlyList<FaultParameter>)parameters.ToArray())
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
    }

    /// <summary>
    /// Every declared parameter in declared order, taking the given value where
    /// there is one and the default otherwise. A name this fault does not
    /// declare is an error naming the ones it does.
    /// </summary>
    public FaultArguments Resolve(FaultArguments given)
    {
        ArgumentNullException.ThrowIfNull(given);

        string[] declared = Parameters.Select(p => p.Name).ToArray();
        for (int i = 0; i < given.Count; i++)
        {
            if (Array.IndexOf(declared, given[i].Name) < 0)
            {
                throw new ArgumentException(
                    $"Fault '{Id}' has no parameter '{given[i].Name}'. Declared: " +
                    $"{(declared.Length == 0 ? "none" : string.Join(", ", declared))}.",
                    nameof(given));
            }
        }

        var resolved = new FaultArgument[Parameters.Count];
        for (int i = 0; i < resolved.Length; i++)
        {
            FaultParameter parameter = Parameters[i];
            resolved[i] = new FaultArgument(
                parameter.Name,
                given.TryGet(parameter.Name, out double value) ? value : parameter.DefaultValue);
        }

        return new FaultArguments(resolved);
    }
}
```

`src/Dse.Core/Faults/IFaultTarget.cs`:

```csharp
using Dse.Core.Graph;

namespace Dse.Core.Faults;

/// <summary>
/// A component that can be broken on purpose. Faults arrive in phase 1 of the
/// tick they are due, with every declared parameter present, and change the
/// component's state and behaviour only — never its ports or its wiring, so
/// injection cannot perturb evaluation order.
/// </summary>
public interface IFaultTarget : ISimComponent
{
    IReadOnlyList<FaultDescriptor> SupportedFaults { get; }

    /// <summary>Applies or re-applies a fault. Called with arguments resolved against the descriptor.</summary>
    void ApplyFault(string faultId, FaultArguments arguments);

    /// <summary>Removes a fault's effect. Clearing a fault that is not active is a no-op.</summary>
    void ClearFault(string faultId);
}
```

- [ ] **Step 4: Wire the simulation**

`src/Dse.Core/Simulation.cs` — add `using Dse.Core.Faults;` and `using System.Globalization;`,
a field, constructor work, the public methods and the nested event:

```csharp
    private readonly Dictionary<string, IFaultTarget> _faultTargets;
```

In the constructor, after `Clock = …;`:

```csharp
        _faultTargets = new Dictionary<string, IFaultTarget>(StringComparer.Ordinal);
        foreach (ISimComponent component in components)
        {
            if (component is IFaultTarget target)
            {
                _faultTargets[component.Id] = target;
            }
        }
```

Public members, after `ScheduleIn`:

```csharp
    /// <summary>The faults a component supports. Tooling's "what can break here?".</summary>
    public IReadOnlyList<FaultDescriptor> FaultsOf(string componentId) =>
        FaultTarget(componentId).SupportedFaults;

    /// <summary>
    /// Schedules a fault for phase 1 of the tick at <paramref name="fromStart"/>.
    /// The target, the fault id and the arguments are resolved now, so a
    /// mistake fails here with a message naming what exists.
    /// </summary>
    public long InjectFaultAt(TimeSpan fromStart, string componentId, string faultId, FaultArguments? arguments = null) =>
        ScheduleAt(fromStart, FaultEvent.Inject(this, componentId, faultId, arguments ?? FaultArguments.None));

    public long InjectFaultIn(TimeSpan delay, string componentId, string faultId, FaultArguments? arguments = null) =>
        ScheduleIn(delay, FaultEvent.Inject(this, componentId, faultId, arguments ?? FaultArguments.None));

    public long ClearFaultAt(TimeSpan fromStart, string componentId, string faultId) =>
        ScheduleAt(fromStart, FaultEvent.Clear(this, componentId, faultId));

    public long ClearFaultIn(TimeSpan delay, string componentId, string faultId) =>
        ScheduleIn(delay, FaultEvent.Clear(this, componentId, faultId));

    private IFaultTarget FaultTarget(string componentId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(componentId);

        if (_faultTargets.TryGetValue(componentId, out IFaultTarget? target))
        {
            return target;
        }

        bool exists = Array.Exists(_components, c => string.Equals(c.Id, componentId, StringComparison.Ordinal));
        string targets = _faultTargets.Count == 0
            ? "none"
            : string.Join(", ", _faultTargets.Keys.Order(StringComparer.Ordinal));
        throw new KeyNotFoundException(exists
            ? $"Component '{componentId}' is not an {nameof(IFaultTarget)}; it declares no faults. Fault targets: {targets}."
            : $"No component '{componentId}' in the plant. Fault targets: {targets}.");
    }

    private static FaultDescriptor Descriptor(IFaultTarget target, string faultId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(faultId);

        foreach (FaultDescriptor descriptor in target.SupportedFaults)
        {
            if (string.Equals(descriptor.Id, faultId, StringComparison.Ordinal))
            {
                return descriptor;
            }
        }

        throw new ArgumentException(
            $"'{target.Id}' supports no fault '{faultId}'. Supported: " +
            $"{string.Join(", ", target.SupportedFaults.Select(f => f.Id))}.",
            nameof(faultId));
    }

    /// <summary>A resolved fault injection or clearance, logged when it lands.</summary>
    private sealed class FaultEvent : ISimEvent
    {
        private readonly Simulation _simulation;
        private readonly IFaultTarget _target;
        private readonly string _faultId;
        private readonly FaultArguments? _arguments;

        private FaultEvent(Simulation simulation, IFaultTarget target, string faultId, FaultArguments? arguments)
        {
            _simulation = simulation;
            _target = target;
            _faultId = faultId;
            _arguments = arguments;
        }

        public static FaultEvent Inject(Simulation simulation, string componentId, string faultId, FaultArguments arguments)
        {
            IFaultTarget target = simulation.FaultTarget(componentId);
            FaultDescriptor descriptor = Descriptor(target, faultId);
            return new FaultEvent(simulation, target, faultId, descriptor.Resolve(arguments));
        }

        public static FaultEvent Clear(Simulation simulation, string componentId, string faultId)
        {
            IFaultTarget target = simulation.FaultTarget(componentId);
            Descriptor(target, faultId);
            return new FaultEvent(simulation, target, faultId, null);
        }

        public void Apply()
        {
            SimulationClock clock = _simulation.Clock;
            if (_arguments is null)
            {
                _target.ClearFault(_faultId);
                _simulation.Events.Record(clock.TickCount, clock.Now, _target.Id, "FAULT_CLEARED", $"{_faultId} cleared.");
                return;
            }

            _target.ApplyFault(_faultId, _arguments);
            string detail = _arguments.Count == 0 ? string.Empty : $": {_arguments}";
            _simulation.Events.Record(clock.TickCount, clock.Now, _target.Id, "FAULT", $"{_faultId} injected{detail}.");
        }
    }
```

- [ ] **Step 5: Run the whole Core suite**

Run: `dotnet test tests/Dse.Core.Tests`
Expected: 221 pass.

- [ ] **Step 6: Commit**

```bash
git add src/Dse.Core/Faults src/Dse.Core/Simulation.cs tests/Dse.Core.Tests/Fakes/Fuse.cs tests/Dse.Core.Tests/FaultChannelTests.cs
git commit -m "feat(core): add the fault channel with descriptors, resolved arguments and scheduled injection"
```

---

### Task 4: The components project, sources and sinks

**Files:**
- Create: `src/Dse.Components/Dse.Components.csproj`
- Create: `src/Dse.Components/Flow/BulkSource.cs`
- Create: `src/Dse.Components/Flow/BulkSink.cs`
- Create: `src/Dse.Components/Flow/ItemSource.cs`
- Create: `src/Dse.Components/Flow/ItemSink.cs`
- Create: `tests/Dse.Components.Tests/Dse.Components.Tests.csproj`
- Create: `tests/Dse.Components.Tests/Fakes/TestContexts.cs`
- Create: `tests/Dse.Components.Tests/Fakes/Setpoint.cs`
- Create: `tests/Dse.Components.Tests/Fakes/Switch.cs`
- Modify: `Dse.sln`
- Test: `tests/Dse.Components.Tests/SourceSinkTests.cs`

**Interfaces:**
- Consumes: `FlowComponentBase`, transport interfaces, `BulkLot`, `ItemInstance`,
  `ItemIdSequence` (plan 2); `IFaultTarget`, `FaultDescriptor` (Task 3).
- Produces:
  - `BulkSource(string id, MaterialType type, double rateKgPerSecond, MaterialProperties properties = default, double hopperCapacityKg = double.PositiveInfinity)`
    : `FlowComponentBase, IBulkProducer, IFaultTarget`. Ports: `FlowOutlet Out`,
    `InputPort<double> Rate` (kg/s, default = ctor rate), `InputPort<bool> Enabled`
    (default true), `OutputPort<double> HopperMass` (kg). Telemetry `Hopper` (kg),
    `Sourced` (kg). Fault `"starve"` (no parameters). Creates mass in `Advance`.
  - `BulkSink(string id, double capacityKg = double.PositiveInfinity)`
    : `FlowComponentBase, IBulkConsumer`. Ports: `FlowInlet In`,
    `OutputPort<double> Received` (kg, cumulative), `OutputPort<double> Rate`
    (kg/s over the previous tick), `OutputPort<bool> Full`. Telemetry `Received`.
    Event `FULL` once when capacity is reached. `MaterialProperties LastProperties`.
  - `ItemSource(string id, MaterialType type, double itemMassKg, double intervalSeconds, MaterialProperties properties = default, int queueCapacity = int.MaxValue)`
    : `FlowComponentBase, IItemProducer, IFaultTarget`. Ports: `FlowOutlet Out`,
    `InputPort<bool> Enabled` (default true), `OutputPort<int> Queued`.
    Telemetry `Sourced` (count). Fault `"starve"`. Mints in `Advance`.
  - `ItemSink(string id, int capacity = int.MaxValue)`
    : `FlowComponentBase, IItemConsumer`. Ports: `FlowInlet In`,
    `OutputPort<long> Count`, `OutputPort<bool> Full`. `ItemInstance? LastItem`,
    `double MassReceived`. Telemetry `Received` (count). Event `FULL`.
  - Test fakes: `Setpoint(string id, double value = 0.0)` with settable `Value`
    and `OutputPort<double> Out`; `Switch(string id, bool value = false)` with
    settable `Value` and `OutputPort<bool> Out`; `TestContexts` identical to
    the Core one.

Spec 17 "bulk material source and sink, discrete item source". These are the
plant's boundaries and the only nodes that report `MassCreated` and
`MassDestroyed`. A source with a finite hopper stops creating when the hopper
is full — the bin is full, the feeder waits — so back-pressure reaches all the
way to the boundary and the ledger stays exact.

- [ ] **Step 1: Create the projects**

```bash
cd /home/keeper/Work/Github/POCs/DSE
dotnet new classlib -n Dse.Components -o src/Dse.Components --framework net10.0
rm src/Dse.Components/Class1.cs
dotnet add src/Dse.Components/Dse.Components.csproj reference src/Dse.Core/Dse.Core.csproj
dotnet new xunit -n Dse.Components.Tests -o tests/Dse.Components.Tests --framework net10.0
rm tests/Dse.Components.Tests/UnitTest1.cs
dotnet add tests/Dse.Components.Tests/Dse.Components.Tests.csproj reference src/Dse.Components/Dse.Components.csproj
dotnet sln Dse.sln add src/Dse.Components/Dse.Components.csproj --solution-folder src
dotnet sln Dse.sln add tests/Dse.Components.Tests/Dse.Components.Tests.csproj --solution-folder tests
```

Then edit `src/Dse.Components/Dse.Components.csproj` to exactly:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\Dse.Core\Dse.Core.csproj" />
  </ItemGroup>
  <ItemGroup>
    <InternalsVisibleTo Include="Dse.Components.Tests" />
  </ItemGroup>
</Project>
```

and `tests/Dse.Components.Tests/Dse.Components.Tests.csproj` to match
`tests/Dse.Core.Tests/Dse.Core.Tests.csproj` exactly (same four package
versions, `<Using Include="Xunit" />`, `IsPackable` false) with the project
reference pointing at `..\..\src\Dse.Components\Dse.Components.csproj`. The
template may pull newer package versions; pin them to the Core test project's.

- [ ] **Step 2: Write the fakes and the failing tests**

`tests/Dse.Components.Tests/Fakes/TestContexts.cs` — copy
`tests/Dse.Core.Tests/Fakes/TestContexts.cs` verbatim, changing only the
namespace to `Dse.Components.Tests.Fakes`.

`tests/Dse.Components.Tests/Fakes/Setpoint.cs`:

```csharp
using Dse.Core.Contexts;
using Dse.Core.Graph;

namespace Dse.Components.Tests.Fakes;

/// <summary>An analog signal a test can change between ticks.</summary>
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

`tests/Dse.Components.Tests/Fakes/Switch.cs`:

```csharp
using Dse.Core.Contexts;
using Dse.Core.Graph;

namespace Dse.Components.Tests.Fakes;

/// <summary>A discrete signal a test can flip between ticks.</summary>
public sealed class Switch : ComponentBase
{
    public Switch(string id, bool value = false)
        : base(id)
    {
        Value = value;
        Out = AddOutput<bool>("Out");
    }

    public bool Value { get; set; }

    public OutputPort<bool> Out { get; }

    public override void Evaluate(in TickContext ctx) => Out.Value = Value;
}
```

`tests/Dse.Components.Tests/SourceSinkTests.cs`:

```csharp
using Dse.Components.Flow;
using Dse.Components.Tests.Fakes;
using Dse.Core;
using Dse.Core.Flow;
using Dse.Core.Time;
using Xunit;

namespace Dse.Components.Tests;

public class SourceSinkTests
{
    private static readonly MaterialType Ore = new("Ore", PayloadKind.Bulk);
    private static readonly MaterialType Billet = new("Billet", PayloadKind.Discrete);

    private static SimulationOptions Options(double stepSeconds = 0.5) => new()
    {
        Seed = 1UL,
        StartTime = new DateTimeOffset(2026, 1, 1, 6, 0, 0, TimeSpan.Zero),
        TimeStep = TimeSpan.FromSeconds(stepSeconds),
    };

    [Fact]
    public void ABulkSourceFeedsASinkAtItsRateAndTheLedgerBalances()
    {
        var source = new BulkSource("Feed", Ore, rateKgPerSecond: 2.0, new MaterialProperties(1600.0, 0.05, 15.0));
        var sink = new BulkSink("Pile");
        source.Out.ConnectTo(sink.In);
        Simulation sim = new SimulationBuilder(Options()).Add(sink).Add(source).Build();

        sim.RunFor(TimeSpan.FromSeconds(5));   // 10 ticks; mass created on tick N moves on tick N+1

        // Outputs and telemetry are written in phase 2, so they show the state
        // after the *previous* tick's transport: one tick behind the ledger.
        Assert.Equal(9.0, sink.MassDestroyed, 9);
        Assert.Equal(8.0, sink.Received.Value, 9);
        Assert.Equal(1.0, source.MassHeld, 9);
        Assert.Equal(2.0, sink.Rate.Value, 9);
        Assert.Equal(15.0, sink.LastProperties.Temperature);
        Assert.Equal(10.0, sim.MassBalance.Created, 9);
        Assert.Equal(0.0, sim.MassBalance.Drift, 9);
        Assert.Equal(9.0, sim.Telemetry.Read("Feed.Sourced"), 9);
        Assert.Equal(8.0, sim.Telemetry.Read("Pile.Received"), 9);
    }

    [Fact]
    public void TheRateInputOverridesTheConfiguredRate()
    {
        var source = new BulkSource("Feed", Ore, rateKgPerSecond: 2.0);
        var sink = new BulkSink("Pile");
        var rate = new Setpoint("Rate", 4.0);
        source.Out.ConnectTo(sink.In);
        rate.Out.ConnectTo(source.Rate);
        Simulation sim = new SimulationBuilder(Options()).Add(sink).Add(source).Add(rate).Build();

        sim.RunFor(TimeSpan.FromSeconds(2));

        Assert.Equal(8.0, sim.MassBalance.Created, 9);
    }

    [Fact]
    public void AFullHopperStopsCreatingMass()
    {
        var source = new BulkSource("Feed", Ore, rateKgPerSecond: 2.0, hopperCapacityKg: 3.0);
        var sink = new BulkSink("Pile", capacityKg: 0.0);   // accepts nothing
        source.Out.ConnectTo(sink.In);
        Simulation sim = new SimulationBuilder(Options()).Add(sink).Add(source).Build();

        sim.RunFor(TimeSpan.FromSeconds(10));

        Assert.Equal(3.0, source.MassHeld, 9);
        Assert.Equal(3.0, source.HopperMass.Value, 9);
        Assert.Equal(3.0, sim.MassBalance.Created, 9);
        Assert.Equal(0.0, sim.MassBalance.Drift, 9);
    }

    [Fact]
    public void DisablingAndStarvingBothStopTheFeed()
    {
        var source = new BulkSource("Feed", Ore, rateKgPerSecond: 2.0);
        var sink = new BulkSink("Pile");
        var enabled = new Switch("Run", true);
        source.Out.ConnectTo(sink.In);
        enabled.Out.ConnectTo(source.Enabled);
        Simulation sim = new SimulationBuilder(Options()).Add(sink).Add(source).Add(enabled).Build();

        sim.RunFor(TimeSpan.FromSeconds(1));
        enabled.Value = false;
        sim.RunFor(TimeSpan.FromSeconds(1));
        Assert.Equal(2.0, sim.MassBalance.Created, 9);

        enabled.Value = true;
        sim.InjectFaultIn(TimeSpan.Zero, "Feed", BulkSource.Starve);
        sim.RunFor(TimeSpan.FromSeconds(1));
        Assert.Equal(2.0, sim.MassBalance.Created, 9);

        sim.ClearFaultIn(TimeSpan.Zero, "Feed", BulkSource.Starve);
        sim.RunFor(TimeSpan.FromSeconds(1));
        Assert.Equal(4.0, sim.MassBalance.Created, 9);
    }

    [Fact]
    public void ASinkWithCapacityFillsOnceAndSaysSo()
    {
        var source = new BulkSource("Feed", Ore, rateKgPerSecond: 2.0);
        var sink = new BulkSink("Pile", capacityKg: 2.5);
        source.Out.ConnectTo(sink.In);
        Simulation sim = new SimulationBuilder(Options()).Add(sink).Add(source).Build();

        sim.RunFor(TimeSpan.FromSeconds(5));

        Assert.Equal(2.5, sink.Received.Value, 9);
        Assert.True(sink.Full.Value);
        var full = Assert.Single(sim.Events.Records, r => r.Code == "FULL");
        Assert.Equal("Pile", full.Source);
        Assert.Equal(0.0, sim.MassBalance.Drift, 9);
    }

    [Fact]
    public void AnItemSourceMintsOnAnIntervalWithSequentialIds()
    {
        var source = new ItemSource("Billets", Billet, itemMassKg: 20.0, intervalSeconds: 1.0, new MaterialProperties(7800.0, 0.0, 25.0));
        var sink = new ItemSink("Scrap");
        source.Out.ConnectTo(sink.In);
        Simulation sim = new SimulationBuilder(Options()).Add(sink).Add(source).Build();

        sim.RunFor(TimeSpan.FromSeconds(5.5));   // 11 ticks: items at ticks 1,3,5,7,9 (elapsed reaches 1.0); the tick-9 item moves on tick 10

        Assert.Equal(5L, sink.LastItem!.Id);
        Assert.Equal(100.0, sink.MassReceived, 9);
        Assert.Equal(4L, sink.Count.Value);   // published in phase 2 of tick 10, before the fifth deposit
        Assert.Equal(25.0, sink.LastItem.Properties.Temperature);
        Assert.Equal(0.0, sim.MassBalance.Drift, 9);
        Assert.Equal(5.0, sim.Telemetry.Read("Billets.Sourced"));
    }

    [Fact]
    public void AnItemSourceQueuesWhenBlockedAndStopsAtItsQueueCapacity()
    {
        var source = new ItemSource("Billets", Billet, 20.0, 0.5, queueCapacity: 3);
        var sink = new ItemSink("Scrap", capacity: 0);
        source.Out.ConnectTo(sink.In);
        Simulation sim = new SimulationBuilder(Options()).Add(sink).Add(source).Build();

        sim.RunFor(TimeSpan.FromSeconds(10));

        Assert.Equal(3, source.Queued.Value);
        Assert.Equal(60.0, source.MassHeld, 9);
        Assert.Equal(3L, sim.Items.Issued);
        Assert.Equal(0.0, sim.MassBalance.Drift, 9);
    }

    [Fact]
    public void AnItemSinkWithCapacityFillsOnce()
    {
        var source = new ItemSource("Billets", Billet, 20.0, 0.5);
        var sink = new ItemSink("Scrap", capacity: 2);
        source.Out.ConnectTo(sink.In);
        Simulation sim = new SimulationBuilder(Options()).Add(sink).Add(source).Build();

        sim.RunFor(TimeSpan.FromSeconds(5));

        Assert.Equal(2L, sink.Count.Value);
        Assert.True(sink.Full.Value);
        Assert.Single(sim.Events.Records, r => r.Code == "FULL");
    }

    [Fact]
    public void StarvingAnItemSourceStopsMinting()
    {
        var source = new ItemSource("Billets", Billet, 20.0, 0.5);
        var sink = new ItemSink("Scrap");
        source.Out.ConnectTo(sink.In);
        Simulation sim = new SimulationBuilder(Options()).Add(sink).Add(source).Build();

        sim.InjectFaultAt(TimeSpan.FromSeconds(2), "Billets", ItemSource.Starve);
        sim.RunFor(TimeSpan.FromSeconds(6));

        Assert.Equal(4L, sim.Items.Issued);
        Assert.Contains(sim.Events.Records, r => r.Code == "FAULT" && r.Source == "Billets");
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/Dse.Components.Tests`
Expected: build error — `Dse.Components.Flow` does not exist.

- [ ] **Step 4: Implement the four components**

`src/Dse.Components/Flow/BulkSource.cs`:

```csharp
using Dse.Core.Contexts;
using Dse.Core.Faults;
using Dse.Core.Flow;
using Dse.Core.Graph;
using Dse.Core.Telemetry;

namespace Dse.Components.Flow;

/// <summary>
/// Where bulk material enters the plant: a feeder drawing from an unbounded
/// supply into a hopper, at a rate a signal may set. The hopper is what a
/// blocked outlet fills; once it is full the feeder waits, so nothing is
/// created that the plant cannot take. Mass is created in <see cref="Advance"/>.
/// </summary>
public sealed class BulkSource : FlowComponentBase, IBulkProducer, IFaultTarget
{
    /// <summary>The supply runs out: nothing is created until cleared.</summary>
    public const string Starve = "starve";

    private static readonly FaultDescriptor[] Faults =
    [
        new(Starve, "The supply runs out; the feeder creates nothing until the fault is cleared."),
    ];

    private readonly MaterialType _type;
    private readonly MaterialProperties _properties;
    private BulkLot _hopper;
    private double _created;
    private bool _starved;
    private TelemetryHandle _hopperTelemetry;
    private TelemetryHandle _sourcedTelemetry;

    public BulkSource(
        string id,
        MaterialType type,
        double rateKgPerSecond,
        MaterialProperties properties = default,
        double hopperCapacityKg = double.PositiveInfinity)
        : base(id)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentOutOfRangeException.ThrowIfNegative(rateKgPerSecond);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(hopperCapacityKg);

        _type = type;
        _properties = properties;
        HopperCapacityKg = hopperCapacityKg;

        Out = AddOutlet("Out", PayloadKind.Bulk);
        Rate = AddInput<double>("Rate", defaultValue: rateKgPerSecond);
        Enabled = AddInput<bool>("Enabled", defaultValue: true);
        HopperMass = AddOutput<double>("HopperMass");
    }

    public FlowOutlet Out { get; }

    /// <summary>Feed rate, kg/s. Unconnected reads the rate given at construction.</summary>
    public InputPort<double> Rate { get; }

    /// <summary>False stops the feeder. Unconnected reads true.</summary>
    public InputPort<bool> Enabled { get; }

    /// <summary>Mass waiting in the hopper, kg, as of the last evaluate.</summary>
    public OutputPort<double> HopperMass { get; }

    /// <summary>kg. Infinite by default.</summary>
    public double HopperCapacityKg { get; }

    public override double MassHeld => _hopper.Mass;

    public override double MassCreated => _created;

    public IReadOnlyList<FaultDescriptor> SupportedFaults => Faults;

    public override void Initialize(in InitContext ctx)
    {
        _hopperTelemetry = ctx.RegisterTelemetry("Hopper", "kg");
        _sourcedTelemetry = ctx.RegisterTelemetry("Sourced", "kg");
    }

    public override void Evaluate(in TickContext ctx)
    {
        HopperMass.Value = _hopper.Mass;
        _hopperTelemetry.Write(_hopper.Mass);
        _sourcedTelemetry.Write(_created);
    }

    public override void Advance(in TickContext ctx)
    {
        if (_starved || !Enabled.Value)
        {
            return;
        }

        double rate = Rate.Value;
        if (!double.IsFinite(rate) || rate <= 0.0)
        {
            return;
        }

        double mass = Math.Min(rate * ctx.Dt, HopperCapacityKg - _hopper.Mass);
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

    public void ApplyFault(string faultId, FaultArguments arguments) => _starved = true;

    public void ClearFault(string faultId) => _starved = false;
}
```

`src/Dse.Components/Flow/BulkSink.cs`:

```csharp
using Dse.Core.Contexts;
using Dse.Core.Flow;
using Dse.Core.Graph;
using Dse.Core.Telemetry;

namespace Dse.Components.Flow;

/// <summary>
/// Where bulk material leaves the plant: a stockpile, a truck, a declared loss.
/// Everything deposited is removed from the ledger. With a capacity it fills,
/// says so once, and then accepts nothing, so the plant behind it backs up.
/// </summary>
public sealed class BulkSink : FlowComponentBase, IBulkConsumer
{
    private double _received;
    private double _sinceEvaluate;
    private bool _full;
    private TelemetryHandle _receivedTelemetry;

    public BulkSink(string id, double capacityKg = double.PositiveInfinity)
        : base(id)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(capacityKg);
        CapacityKg = capacityKg;

        In = AddInlet("In", PayloadKind.Bulk);
        Received = AddOutput<double>("Received");
        Rate = AddOutput<double>("Rate");
        Full = AddOutput<bool>("Full");
    }

    public FlowInlet In { get; }

    /// <summary>Cumulative mass received, kg.</summary>
    public OutputPort<double> Received { get; }

    /// <summary>Mass received during the previous tick divided by dt, kg/s.</summary>
    public OutputPort<double> Rate { get; }

    public OutputPort<bool> Full { get; }

    /// <summary>kg. Infinite by default.</summary>
    public double CapacityKg { get; }

    /// <summary>Properties of the last lot deposited.</summary>
    public MaterialProperties LastProperties { get; private set; }

    public override double MassHeld => 0.0;

    public override double MassDestroyed => _received;

    public override void Initialize(in InitContext ctx) =>
        _receivedTelemetry = ctx.RegisterTelemetry("Received", "kg");

    public override void Evaluate(in TickContext ctx)
    {
        Received.Value = _received;
        Rate.Value = _sinceEvaluate / ctx.Dt;
        _sinceEvaluate = 0.0;
        _receivedTelemetry.Write(_received);

        bool full = _received >= CapacityKg;
        if (full && !_full)
        {
            ctx.Log(Id, "FULL", "Capacity reached; accepting nothing more.");
        }

        _full = full;
        Full.Value = full;
    }

    public double AcceptMass(FlowInlet inlet) => Math.Max(0.0, CapacityKg - _received);

    public void Deposit(FlowInlet inlet, in BulkLot lot)
    {
        _received += lot.Mass;
        _sinceEvaluate += lot.Mass;
        LastProperties = lot.Properties;
    }
}
```

`src/Dse.Components/Flow/ItemSource.cs`:

```csharp
using System.Diagnostics.CodeAnalysis;
using Dse.Core.Contexts;
using Dse.Core.Faults;
using Dse.Core.Flow;
using Dse.Core.Graph;
using Dse.Core.Telemetry;

namespace Dse.Components.Flow;

/// <summary>
/// Where discrete items enter the plant: one item every interval, ids from
/// the simulation's sequence, queued until the outlet takes them. A full queue
/// pauses minting; the interval timer keeps running so cadence resumes cleanly.
/// </summary>
public sealed class ItemSource : FlowComponentBase, IItemProducer, IFaultTarget
{
    /// <summary>The supply runs out: nothing is minted until cleared.</summary>
    public const string Starve = "starve";

    private static readonly FaultDescriptor[] Faults =
    [
        new(Starve, "The supply runs out; nothing is minted until the fault is cleared."),
    ];

    private readonly Queue<ItemInstance> _ready = new();
    private readonly MaterialType _type;
    private readonly MaterialProperties _properties;
    private ItemIdSequence? _ids;
    private double _elapsed;
    private double _created;
    private long _minted;
    private bool _starved;
    private TelemetryHandle _sourcedTelemetry;

    public ItemSource(
        string id,
        MaterialType type,
        double itemMassKg,
        double intervalSeconds,
        MaterialProperties properties = default,
        int queueCapacity = int.MaxValue)
        : base(id)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(itemMassKg);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(intervalSeconds);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(queueCapacity);

        _type = type;
        _properties = properties;
        ItemMassKg = itemMassKg;
        IntervalSeconds = intervalSeconds;
        QueueCapacity = queueCapacity;

        Out = AddOutlet("Out", PayloadKind.Discrete);
        Enabled = AddInput<bool>("Enabled", defaultValue: true);
        Queued = AddOutput<int>("Queued");
    }

    public FlowOutlet Out { get; }

    /// <summary>False pauses minting. Unconnected reads true.</summary>
    public InputPort<bool> Enabled { get; }

    /// <summary>Items waiting at the outlet, as of the last evaluate.</summary>
    public OutputPort<int> Queued { get; }

    /// <summary>kg per item.</summary>
    public double ItemMassKg { get; }

    /// <summary>Seconds between items.</summary>
    public double IntervalSeconds { get; }

    public int QueueCapacity { get; }

    public override double MassHeld => _ready.Count * ItemMassKg;

    public override double MassCreated => _created;

    public IReadOnlyList<FaultDescriptor> SupportedFaults => Faults;

    public override void Initialize(in InitContext ctx)
    {
        _ids = ctx.Items;
        _sourcedTelemetry = ctx.RegisterTelemetry("Sourced", "count");
    }

    public override void Evaluate(in TickContext ctx)
    {
        Queued.Value = _ready.Count;
        _sourcedTelemetry.Write(_minted);
    }

    public override void Advance(in TickContext ctx)
    {
        if (_starved || !Enabled.Value)
        {
            return;
        }

        _elapsed += ctx.Dt;
        while (_elapsed >= IntervalSeconds - 1e-12)
        {
            _elapsed -= IntervalSeconds;
            if (_ready.Count >= QueueCapacity)
            {
                continue;
            }

            _ready.Enqueue(new ItemInstance(_ids!.Next(), _type, ItemMassKg, _properties));
            _created += ItemMassKg;
            _minted++;
        }
    }

    public bool TryPeekItem(FlowOutlet outlet, [NotNullWhen(true)] out ItemInstance? item) =>
        _ready.TryPeek(out item);

    public ItemInstance WithdrawItem(FlowOutlet outlet) => _ready.Dequeue();

    public void ApplyFault(string faultId, FaultArguments arguments) => _starved = true;

    public void ClearFault(string faultId) => _starved = false;
}
```

`_ids` is set in `Initialize`; `Simulation` always initialises before the first
tick, and a hand-driven unit test must call `Initialize` itself.

`src/Dse.Components/Flow/ItemSink.cs`:

```csharp
using Dse.Core.Contexts;
using Dse.Core.Flow;
using Dse.Core.Graph;
using Dse.Core.Telemetry;

namespace Dse.Components.Flow;

/// <summary>
/// Where items leave the plant. Keeps a count, the mass, and the last item
/// (so a test can inspect what arrived) but not the items themselves — a
/// long run must not grow without bound.
/// </summary>
public sealed class ItemSink : FlowComponentBase, IItemConsumer
{
    private long _count;
    private double _mass;
    private bool _full;
    private TelemetryHandle _receivedTelemetry;

    public ItemSink(string id, int capacity = int.MaxValue)
        : base(id)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(capacity);
        Capacity = capacity;

        In = AddInlet("In", PayloadKind.Discrete);
        Count = AddOutput<long>("Count");
        Full = AddOutput<bool>("Full");
    }

    public FlowInlet In { get; }

    /// <summary>Items received, cumulative.</summary>
    public OutputPort<long> Count { get; }

    public OutputPort<bool> Full { get; }

    public int Capacity { get; }

    public ItemInstance? LastItem { get; private set; }

    /// <summary>kg, cumulative.</summary>
    public double MassReceived => _mass;

    public override double MassHeld => 0.0;

    public override double MassDestroyed => _mass;

    public override void Initialize(in InitContext ctx) =>
        _receivedTelemetry = ctx.RegisterTelemetry("Received", "count");

    public override void Evaluate(in TickContext ctx)
    {
        Count.Value = _count;
        _receivedTelemetry.Write(_count);

        bool full = _count >= Capacity;
        if (full && !_full)
        {
            ctx.Log(Id, "FULL", "Capacity reached; accepting nothing more.");
        }

        _full = full;
        Full.Value = full;
    }

    public bool CanAcceptItem(FlowInlet inlet, ItemInstance item) => _count < Capacity;

    public void DepositItem(FlowInlet inlet, ItemInstance item)
    {
        _count++;
        _mass += item.Mass;
        LastItem = item;
    }
}
```

- [ ] **Step 5: Run the tests**

Run: `dotnet test tests/Dse.Components.Tests`
Expected: 9 pass. If the item cadence test is off by one, check the ordering
argument in its comment before touching the source: with dt 0.5 and interval
1.0 the first item is minted in `Advance` of tick 1 (elapsed reaches 1.0),
transfers on tick 2, so after 11 ticks (0–10) items minted on ticks 1, 3, 5,
7, 9 have all moved.

- [ ] **Step 6: Commit**

```bash
git add Dse.sln src/Dse.Components tests/Dse.Components.Tests
git commit -m "feat(components): add the component library project with bulk and item sources and sinks"
```

---

### Task 5: Transfer chute

**Files:**
- Create: `src/Dse.Components/Flow/TransferChute.cs`
- Test: `tests/Dse.Components.Tests/TransferChuteTests.cs`

**Interfaces:**
- Consumes: `FlowComponentBase`, `IBulkConsumer`, `IBulkProducer`, `BulkLot`,
  `IMaterialObservable` (Task 2), `IFaultTarget` (Task 3), `BulkSource`/`BulkSink` (Task 4).
- Produces: `TransferChute(string id, double capacityKg)`
  : `FlowComponentBase, IBulkConsumer, IBulkProducer, IMaterialObservable, IFaultTarget`.
  Ports: `FlowInlet In`, `FlowOutlet Out`, `OutputPort<double> Level` (0..1),
  `OutputPort<bool> Full`. Telemetry `Held` (kg). Events `FULL` / `CLEARED`
  (edges). Fault `"blockage"` (no parameters): discharge stops. `BulkLot Contents`.

Spec 7.8: "the transfer chute from CV002 cannot discharge → the chute fills →
CV002's last cell cannot discharge". A chute is a capacity-limited hold that
offers everything it has. Its blockage fault is the plant-level way to start
that chain in a test without stopping a belt.

- [ ] **Step 1: Write the failing tests**

`tests/Dse.Components.Tests/TransferChuteTests.cs`:

```csharp
using Dse.Components.Flow;
using Dse.Core;
using Dse.Core.Flow;
using Dse.Core.Time;
using Xunit;

namespace Dse.Components.Tests;

public class TransferChuteTests
{
    private static readonly MaterialType Ore = new("Ore", PayloadKind.Bulk);

    private static SimulationOptions Options() => new()
    {
        Seed = 1UL,
        StartTime = new DateTimeOffset(2026, 1, 1, 6, 0, 0, TimeSpan.Zero),
        TimeStep = TimeSpan.FromSeconds(0.5),
    };

    private static (Simulation Sim, BulkSource Feed, TransferChute Chute, BulkSink Pile) Build(double sinkCapacity = double.PositiveInfinity)
    {
        var feed = new BulkSource("Feed", Ore, 2.0, new MaterialProperties(1600.0, 0.05, 15.0));
        var chute = new TransferChute("Chute", capacityKg: 3.0);
        var pile = new BulkSink("Pile", sinkCapacity);
        feed.Out.ConnectTo(chute.In);
        chute.Out.ConnectTo(pile.In);
        Simulation sim = new SimulationBuilder(Options()).Add(pile).Add(chute).Add(feed).Build();
        return (sim, feed, chute, pile);
    }

    [Fact]
    public void PassesMaterialThroughWithOneTickOfResidence()
    {
        var plant = Build();

        plant.Sim.RunFor(TimeSpan.FromSeconds(5));

        Assert.Equal(8.0, plant.Pile.MassDestroyed, 9);      // created tick 0, chute tick 1, pile tick 2 …
        Assert.Equal(1.0, plant.Chute.MassHeld, 9);
        Assert.Equal(0.0, plant.Sim.MassBalance.Drift, 9);
    }

    [Fact]
    public void FillsToCapacityWhenBlockedDownstreamAndSaysSoOnce()
    {
        var plant = Build(sinkCapacity: 0.0);

        plant.Sim.RunFor(TimeSpan.FromSeconds(6));

        Assert.Equal(3.0, plant.Chute.MassHeld, 9);
        Assert.Equal(1.0, plant.Chute.Level.Value, 9);
        Assert.True(plant.Chute.Full.Value);
        Assert.Single(plant.Sim.Events.Records, r => r.Code == "FULL" && r.Source == "Chute");
        Assert.True(plant.Feed.MassHeld > 0.0);
        Assert.Equal(0.0, plant.Sim.MassBalance.Drift, 9);
    }

    [Fact]
    public void ABlockageStopsDischargeAndClearingResumesIt()
    {
        var plant = Build();
        plant.Sim.RunFor(TimeSpan.FromSeconds(3));
        double sunkBefore = plant.Pile.MassDestroyed;

        plant.Sim.InjectFaultIn(TimeSpan.Zero, "Chute", TransferChute.Blockage);
        plant.Sim.RunFor(TimeSpan.FromSeconds(4));

        Assert.Equal(sunkBefore, plant.Pile.MassDestroyed, 9);
        Assert.Equal(3.0, plant.Chute.MassHeld, 9);
        Assert.Contains(plant.Sim.Events.Records, r => r.Code == "FULL");

        plant.Sim.ClearFaultIn(TimeSpan.Zero, "Chute", TransferChute.Blockage);
        plant.Sim.RunFor(TimeSpan.FromSeconds(3));

        Assert.True(plant.Pile.MassDestroyed > sunkBefore + 2.0);
        Assert.Contains(plant.Sim.Events.Records, r => r.Code == "CLEARED");
        Assert.Equal(0.0, plant.Sim.MassBalance.Drift, 9);
    }

    [Fact]
    public void ObservationReportsTheContents()
    {
        var plant = Build(sinkCapacity: 0.0);
        plant.Sim.RunFor(TimeSpan.FromSeconds(2));

        Assert.True(plant.Chute.TryObserve(0.0, 0.0, out MaterialObservation seen));
        Assert.Equal(plant.Chute.MassHeld, seen.Mass, 9);
        Assert.Equal(15.0, seen.Properties.Temperature);
        Assert.Equal(0L, seen.ItemId);

        var empty = new TransferChute("E", 1.0);
        Assert.False(empty.TryObserve(0.0, 0.0, out _));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Dse.Components.Tests --filter "FullyQualifiedName~TransferChuteTests"`
Expected: build error — `TransferChute` does not exist.

- [ ] **Step 3: Implement**

`src/Dse.Components/Flow/TransferChute.cs`:

```csharp
using Dse.Core.Contexts;
using Dse.Core.Faults;
using Dse.Core.Flow;
using Dse.Core.Graph;
using Dse.Core.Telemetry;

namespace Dse.Components.Flow;

/// <summary>
/// A capacity-limited hold between two transports. It accepts the room it has
/// and offers everything it holds, so a blocked outlet fills it and a full
/// chute stops the belt feeding it — spec 7.8's chain, with no code of its own.
/// </summary>
public sealed class TransferChute : FlowComponentBase, IBulkConsumer, IBulkProducer, IMaterialObservable, IFaultTarget
{
    /// <summary>Material bridges in the chute: nothing discharges until cleared.</summary>
    public const string Blockage = "blockage";

    private static readonly FaultDescriptor[] Faults =
    [
        new(Blockage, "Material bridges in the chute; nothing discharges until the fault is cleared."),
    ];

    private BulkLot _held;
    private bool _blocked;
    private bool _full;
    private TelemetryHandle _heldTelemetry;

    public TransferChute(string id, double capacityKg)
        : base(id)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacityKg);
        CapacityKg = capacityKg;

        In = AddInlet("In", PayloadKind.Bulk);
        Out = AddOutlet("Out", PayloadKind.Bulk);
        Level = AddOutput<double>("Level");
        Full = AddOutput<bool>("Full");
    }

    public FlowInlet In { get; }

    public FlowOutlet Out { get; }

    /// <summary>Held mass over capacity, 0..1, as of the last evaluate.</summary>
    public OutputPort<double> Level { get; }

    public OutputPort<bool> Full { get; }

    /// <summary>kg.</summary>
    public double CapacityKg { get; }

    public BulkLot Contents => _held;

    public override double MassHeld => _held.Mass;

    public IReadOnlyList<FaultDescriptor> SupportedFaults => Faults;

    public override void Initialize(in InitContext ctx) =>
        _heldTelemetry = ctx.RegisterTelemetry("Held", "kg");

    public override void Evaluate(in TickContext ctx)
    {
        double level = _held.Mass / CapacityKg;
        Level.Value = level;
        _heldTelemetry.Write(_held.Mass);

        bool full = level >= 1.0 - 1e-9;
        if (full != _full)
        {
            ctx.Log(Id, full ? "FULL" : "CLEARED", full ? "Chute is full." : "Chute has room again.");
        }

        _full = full;
        Full.Value = full;
    }

    public double AcceptMass(FlowInlet inlet) => Math.Max(0.0, CapacityKg - _held.Mass);

    public void Deposit(FlowInlet inlet, in BulkLot lot) => _held = _held.Merge(lot);

    public double OfferMass(FlowOutlet outlet) => _blocked ? 0.0 : _held.Mass;

    public BulkLot Withdraw(FlowOutlet outlet, double mass)
    {
        BulkLot taken = _held.Take(mass, out BulkLot remaining);
        _held = remaining;
        return taken;
    }

    /// <summary>The contents; position and window are ignored, a chute has no length.</summary>
    public bool TryObserve(double position, double window, out MaterialObservation observation)
    {
        if (_held.IsEmpty)
        {
            observation = default;
            return false;
        }

        observation = new MaterialObservation(_held.Mass, 0.0, _held.Properties, 0L);
        return true;
    }

    public void ApplyFault(string faultId, FaultArguments arguments) => _blocked = true;

    public void ClearFault(string faultId) => _blocked = false;
}
```

- [ ] **Step 4: Run the tests**

Run: `dotnet test tests/Dse.Components.Tests`
Expected: 13 pass.

- [ ] **Step 5: Commit**

```bash
git add src/Dse.Components/Flow/TransferChute.cs tests/Dse.Components.Tests/TransferChuteTests.cs
git commit -m "feat(components): add the transfer chute with capacity back-pressure and a blockage fault"
```

---

### Task 6: The three shipped transforms

**Files:**
- Create: `src/Dse.Components/Transforms/ThermalTransfer.cs`
- Create: `src/Dse.Components/Transforms/MoistureLoss.cs`
- Create: `src/Dse.Components/Transforms/ResidenceAccumulator.cs`
- Test: `tests/Dse.Components.Tests/TransformTests.cs`

**Interfaces:**
- Consumes: `IMaterialTransform`, `TransformContext`, `MaterialProperties`,
  `MaterialType` (plan 2).
- Produces:
  - `ThermalTransfer(double timeConstantSeconds)` — `T += (Tamb − T) · min(1, dt/τ)`.
  - `MoistureLoss(double ratePerDegreeSecond, double thresholdTemperature)` —
    `Moisture −= rate · max(0, T − threshold) · dt`, clamped at 0. R17: mass unchanged.
  - `ResidenceAccumulator(int stateIndex, double thresholdTemperature)` with
    `static ResidenceAccumulator For(MaterialType type, string stateName, double thresholdTemperature)`
    — `state[index] += dt` while `T ≥ threshold`; no-op when the span is too short (bulk).

Spec 8.1 names all three. They are pure functions of the material and the
context; the tests drive them directly and then once through a `BulkBelt` and
a `DiscreteBelt` to prove the wiring.

- [ ] **Step 1: Write the failing tests**

`tests/Dse.Components.Tests/TransformTests.cs`:

```csharp
using Dse.Components.Tests.Fakes;
using Dse.Components.Transforms;
using Dse.Core.Flow;
using Dse.Core.Graph;
using Xunit;

namespace Dse.Components.Tests;

public class TransformTests
{
    private static readonly MaterialType Dough = new("Dough", PayloadKind.Bulk);
    private static readonly MaterialType Loaf = new("Loaf", PayloadKind.Discrete, "BakeTimeAbove200C", "CoreTemperature");

    [Fact]
    public void ThermalTransferMovesTowardAmbientWithTheTimeConstant()
    {
        var transform = new ThermalTransfer(timeConstantSeconds: 10.0);
        var props = new MaterialProperties(1000.0, 0.4, 20.0);
        var ctx = new TransformContext(220.0);

        for (int i = 0; i < 100; i++)
        {
            transform.Apply(ref props, Span<double>.Empty, 0.1, in ctx);   // 10 s = one time constant
        }

        Assert.InRange(props.Temperature, 145.0, 148.0);   // exact 146.4; forward Euler lands within a degree
        Assert.Equal(0.4, props.Moisture);
        Assert.Equal(1000.0, props.Density);
    }

    [Fact]
    public void ThermalTransferWithAZeroTimeConstantSnapsToAmbient()
    {
        var transform = new ThermalTransfer(0.0);
        var props = new MaterialProperties(1000.0, 0.4, 20.0);
        transform.Apply(ref props, Span<double>.Empty, 0.01, new TransformContext(180.0));
        Assert.Equal(180.0, props.Temperature);
    }

    [Fact]
    public void MoistureLossOnlyHappensAboveTheThresholdAndNeverGoesNegative()
    {
        var transform = new MoistureLoss(ratePerDegreeSecond: 0.001, thresholdTemperature: 100.0);
        var cold = new MaterialProperties(1000.0, 0.4, 60.0);
        var hot = new MaterialProperties(1000.0, 0.4, 150.0);
        var ctx = new TransformContext(200.0);

        transform.Apply(ref cold, Span<double>.Empty, 1.0, in ctx);
        transform.Apply(ref hot, Span<double>.Empty, 1.0, in ctx);
        Assert.Equal(0.4, cold.Moisture);
        Assert.Equal(0.35, hot.Moisture, 9);   // 0.001 × 50 °C × 1 s

        for (int i = 0; i < 20; i++)
        {
            transform.Apply(ref hot, Span<double>.Empty, 1.0, in ctx);
        }

        Assert.Equal(0.0, hot.Moisture);
    }

    [Fact]
    public void ResidenceAccumulatorCountsTimeAboveTheThresholdIntoItsSlot()
    {
        ResidenceAccumulator transform = ResidenceAccumulator.For(Loaf, "BakeTimeAbove200C", 200.0);
        double[] state = Loaf.NewState();
        var below = new MaterialProperties(600.0, 0.3, 150.0);
        var above = new MaterialProperties(600.0, 0.3, 210.0);
        var ctx = new TransformContext(230.0);

        transform.Apply(ref below, state, 0.5, in ctx);
        transform.Apply(ref above, state, 0.5, in ctx);
        transform.Apply(ref above, state, 0.5, in ctx);

        Assert.Equal([1.0, 0.0], state);
        Assert.Equal(150.0, below.Temperature);

        transform.Apply(ref above, Span<double>.Empty, 0.5, in ctx);   // bulk: no slot, no-op, no throw
        Assert.Throws<KeyNotFoundException>(() => ResidenceAccumulator.For(Loaf, "Crust", 200.0));
    }

    [Fact]
    public void ABeltWithTransformsBakesWhatItCarries()
    {
        var oven = new DiscreteBelt(
            "Oven",
            length: 2.0,
            maxSpeed: 1.0,
            transforms: [new ThermalTransfer(1.0), ResidenceAccumulator.For(Loaf, "BakeTimeAbove200C", 200.0)]);
        oven.Initialize(TestContexts.Init(oven.Id, dt: 0.1));
        var speed = new OutputPort<double>("Out", "SP");
        speed.ConnectTo(oven.Speed);
        speed.Value = 0.5;
        var zone = new OutputPort<double>("Out", "Zone");
        zone.ConnectTo(oven.AmbientTemperature);
        zone.Value = 230.0;

        var loaf = new ItemInstance(1, Loaf, 0.8, new MaterialProperties(600.0, 0.3, 25.0));
        oven.DepositItem(oven.In, loaf);
        for (int tick = 0; tick < 40; tick++)
        {
            oven.Advance(TestContexts.Tick(tick, dt: 0.1));   // 4 s at 0.5 m/s: reaches the head
        }

        Assert.True(loaf.Properties.Temperature > 200.0);
        Assert.True(loaf.State[0] > 0.5 && loaf.State[0] < 4.0);
        Assert.True(oven.TryPeekItem(oven.Out, out _));
    }

    [Fact]
    public void ABulkBeltWithMoistureLossDriesWhatItCarries()
    {
        var dryer = new BulkBelt(
            "Dryer",
            length: 1.0,
            cellSize: 0.5,
            maxSpeed: 1.0,
            maxLinearDensity: 10.0,
            transforms: [new ThermalTransfer(0.0), new MoistureLoss(0.001, 100.0)]);
        dryer.Initialize(TestContexts.Init(dryer.Id, dt: 0.1));
        var zone = new OutputPort<double>("Out", "Zone");
        zone.ConnectTo(dryer.AmbientTemperature);
        zone.Value = 150.0;
        dryer.Deposit(dryer.In, BulkLot.Of(Dough, 1.0, new MaterialProperties(1000.0, 0.4, 20.0)));

        for (int tick = 0; tick < 10; tick++)
        {
            dryer.Advance(TestContexts.Tick(tick, dt: 0.1));   // speed 0: nothing moves, everything dries
        }

        Assert.Equal(1.0, dryer.MassHeld, 9);
        Assert.Equal(0.4 - (0.001 * 50.0 * 1.0), dryer.Cells[0].Properties.Moisture, 9);   // 0.35
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Dse.Components.Tests --filter "FullyQualifiedName~TransformTests"`
Expected: build error — `Dse.Components.Transforms` does not exist.

- [ ] **Step 3: Implement**

`src/Dse.Components/Transforms/ThermalTransfer.cs`:

```csharp
using Dse.Core.Flow;

namespace Dse.Components.Transforms;

/// <summary>
/// Lumped-capacitance heat transfer toward the ambient temperature with one
/// time constant. Zero snaps the material to ambient. This is the transform
/// that turns a belt into a band oven or a cooling conveyor.
/// </summary>
public sealed class ThermalTransfer : IMaterialTransform
{
    public ThermalTransfer(double timeConstantSeconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(timeConstantSeconds);
        TimeConstantSeconds = timeConstantSeconds;
    }

    /// <summary>s.</summary>
    public double TimeConstantSeconds { get; }

    public void Apply(ref MaterialProperties properties, Span<double> state, double dt, in TransformContext context)
    {
        double fraction = TimeConstantSeconds <= 0.0 ? 1.0 : Math.Min(1.0, dt / TimeConstantSeconds);
        properties = properties with
        {
            Temperature = properties.Temperature + ((context.AmbientTemperature - properties.Temperature) * fraction),
        };
    }
}
```

`src/Dse.Components/Transforms/MoistureLoss.cs`:

```csharp
using Dse.Core.Flow;

namespace Dse.Components.Transforms;

/// <summary>
/// Moisture leaves at a rate proportional to how far the material is above a
/// threshold temperature. Changes the moisture fraction only: transforms have
/// no mass authority, so the evaporated water stays in the mass ledger. A
/// dryer that must lose mass is a process unit with a yield below one.
/// </summary>
public sealed class MoistureLoss : IMaterialTransform
{
    public MoistureLoss(double ratePerDegreeSecond, double thresholdTemperature)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(ratePerDegreeSecond);
        RatePerDegreeSecond = ratePerDegreeSecond;
        ThresholdTemperature = thresholdTemperature;
    }

    /// <summary>Moisture fraction lost per °C above the threshold per second.</summary>
    public double RatePerDegreeSecond { get; }

    /// <summary>°C.</summary>
    public double ThresholdTemperature { get; }

    public void Apply(ref MaterialProperties properties, Span<double> state, double dt, in TransformContext context)
    {
        double excess = properties.Temperature - ThresholdTemperature;
        if (excess <= 0.0)
        {
            return;
        }

        double moisture = Math.Max(0.0, properties.Moisture - (RatePerDegreeSecond * excess * dt));
        properties = properties with { Moisture = moisture };
    }
}
```

`src/Dse.Components/Transforms/ResidenceAccumulator.cs`:

```csharp
using Dse.Core.Flow;

namespace Dse.Components.Transforms;

/// <summary>
/// Accumulates the time material spends at or above a temperature into one
/// slot of its state array. Resolve the slot once with <see cref="For"/>;
/// material with no such slot (bulk, or a schema without it) is left alone.
/// </summary>
public sealed class ResidenceAccumulator : IMaterialTransform
{
    public ResidenceAccumulator(int stateIndex, double thresholdTemperature)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(stateIndex);
        StateIndex = stateIndex;
        ThresholdTemperature = thresholdTemperature;
    }

    public int StateIndex { get; }

    /// <summary>°C.</summary>
    public double ThresholdTemperature { get; }

    /// <summary>Resolves the slot by name; throws if the material declares no such state.</summary>
    public static ResidenceAccumulator For(MaterialType type, string stateName, double thresholdTemperature)
    {
        ArgumentNullException.ThrowIfNull(type);
        return new ResidenceAccumulator(type.StateIndexOf(stateName), thresholdTemperature);
    }

    public void Apply(ref MaterialProperties properties, Span<double> state, double dt, in TransformContext context)
    {
        if (StateIndex >= state.Length || properties.Temperature < ThresholdTemperature)
        {
            return;
        }

        state[StateIndex] += dt;
    }
}
```

- [ ] **Step 4: Run the tests**

Run: `dotnet test tests/Dse.Components.Tests`
Expected: 19 pass.

- [ ] **Step 5: Commit**

```bash
git add src/Dse.Components/Transforms tests/Dse.Components.Tests/TransformTests.cs
git commit -m "feat(components): add the thermal, moisture-loss and residence transforms"
```

---

### Task 7: Former (bulk → discrete)

**Files:**
- Create: `src/Dse.Components/Flow/Former.cs`
- Test: `tests/Dse.Components.Tests/FormerTests.cs`

**Interfaces:**
- Consumes: `FlowComponentBase`, `IBulkConsumer`, `IItemProducer`, `BulkLot`,
  `ItemInstance`, `ItemIdSequence`, `IMaterialObservable`, `IFaultTarget`.
- Produces: `Former(string id, MaterialType input, MaterialType output, double pieceMassKg, double cycleSeconds, double hopperCapacityKg, int outputQueueCapacity = int.MaxValue)`
  : `FlowComponentBase, IBulkConsumer, IItemProducer, IMaterialObservable, IFaultTarget`.
  Ports: `FlowInlet In` (Bulk), `FlowOutlet Out` (Discrete),
  `OutputPort<long> PiecesFormed`, `OutputPort<double> HopperLevel` (0..1),
  `OutputPort<int> Queued`. Telemetry `Hopper` (kg), `Formed` (count).
  Fault `"jam"`: no pieces are formed until cleared. `BulkLot Hopper`.

Spec 7.1: crossing from bulk to discrete is an explicit component — a dough
divider. Every `cycleSeconds`, if the hopper holds at least one piece's mass
and the output queue has room, one piece is cut off carrying the hopper's
blended properties. Mass is neither created nor destroyed; `MassHeld` is the
hopper plus the queue.

- [ ] **Step 1: Write the failing tests**

`tests/Dse.Components.Tests/FormerTests.cs`:

```csharp
using Dse.Components.Flow;
using Dse.Core;
using Dse.Core.Flow;
using Dse.Core.Time;
using Xunit;

namespace Dse.Components.Tests;

public class FormerTests
{
    private static readonly MaterialType Dough = new("Dough", PayloadKind.Bulk);
    private static readonly MaterialType Piece = new("DoughPiece", PayloadKind.Discrete, "ProofTime");

    private static SimulationOptions Options() => new()
    {
        Seed = 1UL,
        StartTime = new DateTimeOffset(2026, 1, 1, 6, 0, 0, TimeSpan.Zero),
        TimeStep = TimeSpan.FromSeconds(0.5),
    };

    private static (Simulation Sim, BulkSource Feed, Former Divider, ItemSink Tray) Build(double feedRate = 2.0, int trayCapacity = int.MaxValue)
    {
        var feed = new BulkSource("Feed", Dough, feedRate, new MaterialProperties(1050.0, 0.45, 26.0));
        var divider = new Former("Divider", Dough, Piece, pieceMassKg: 0.8, cycleSeconds: 1.0, hopperCapacityKg: 5.0);
        var tray = new ItemSink("Tray", trayCapacity);
        feed.Out.ConnectTo(divider.In);
        divider.Out.ConnectTo(tray.In);
        Simulation sim = new SimulationBuilder(Options()).Add(tray).Add(divider).Add(feed).Build();
        return (sim, feed, divider, tray);
    }

    [Fact]
    public void CutsOnePieceEveryCycleCarryingTheHoppersProperties()
    {
        var plant = Build();

        plant.Sim.RunFor(TimeSpan.FromSeconds(20));

        Assert.True(plant.Tray.LastItem is not null);
        Assert.Equal(Piece, plant.Tray.LastItem!.Type);
        Assert.Equal(0.8, plant.Tray.LastItem.Mass, 9);
        Assert.Equal(26.0, plant.Tray.LastItem.Properties.Temperature, 9);
        Assert.Equal(1, plant.Tray.LastItem.State.Length);
        Assert.InRange(plant.Tray.MassReceived, 0.8 * 16, 0.8 * 20);   // one per second (every second tick) minus fill and hand-off
        Assert.Equal(0.0, plant.Sim.MassBalance.Drift, 9);
    }

    [Fact]
    public void WaitsForEnoughDoughAndFillsTheHopperWhenStarvedOfCycles()
    {
        var plant = Build(feedRate: 0.2);   // 0.1 kg per tick: a piece every 4 s, on the next cycle boundary

        plant.Sim.RunFor(TimeSpan.FromSeconds(30));

        Assert.InRange(plant.Sim.Items.Issued, 6L, 7L);   // 6 kg fed; the last piece may be one tick short
        Assert.True(plant.Divider.Hopper.Mass < 0.9);
        Assert.Equal(0.0, plant.Sim.MassBalance.Drift, 9);
    }

    [Fact]
    public void ABlockedOutletFillsTheQueueThenTheHopperThenTheFeed()
    {
        var plant = Build(trayCapacity: 0);

        plant.Sim.RunFor(TimeSpan.FromSeconds(30));

        Assert.Equal(1.0, plant.Divider.HopperLevel.Value, 9);
        Assert.True(plant.Divider.Queued.Value > 0);
        Assert.True(plant.Feed.MassHeld > 0.0);
        Assert.Equal(0.0, plant.Sim.MassBalance.Drift, 9);
    }

    [Fact]
    public void AJamStopsFormingUntilCleared()
    {
        var plant = Build();
        plant.Sim.InjectFaultAt(TimeSpan.FromSeconds(5), "Divider", Former.Jam);
        plant.Sim.ClearFaultAt(TimeSpan.FromSeconds(15), "Divider", Former.Jam);

        plant.Sim.RunFor(TimeSpan.FromSeconds(10));
        long duringJam = plant.Sim.Items.Issued;
        plant.Sim.RunFor(TimeSpan.FromSeconds(5));
        Assert.Equal(duringJam, plant.Sim.Items.Issued);

        plant.Sim.RunFor(TimeSpan.FromSeconds(5));
        Assert.True(plant.Sim.Items.Issued > duringJam);
        Assert.Equal(0.0, plant.Sim.MassBalance.Drift, 9);
    }

    [Fact]
    public void RejectsMismatchedKinds()
    {
        Assert.Throws<ArgumentException>(() => new Former("F", Piece, Piece, 1.0, 1.0, 5.0));
        Assert.Throws<ArgumentException>(() => new Former("F", Dough, Dough, 1.0, 1.0, 5.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Former("F", Dough, Piece, 6.0, 1.0, 5.0));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Dse.Components.Tests --filter "FullyQualifiedName~FormerTests"`
Expected: build error — `Former` does not exist.

- [ ] **Step 3: Implement**

`src/Dse.Components/Flow/Former.cs`:

```csharp
using System.Diagnostics.CodeAnalysis;
using Dse.Core.Contexts;
using Dse.Core.Faults;
using Dse.Core.Flow;
using Dse.Core.Graph;
using Dse.Core.Telemetry;

namespace Dse.Components.Flow;

/// <summary>
/// Turns bulk into discrete items — a dough divider, a billet shear. Bulk
/// collects in a hopper; every cycle one piece of fixed mass is cut off,
/// carrying the hopper's blended properties, and queued for the outlet. The
/// crossing is explicit because real plants contain exactly this machine.
/// </summary>
public sealed class Former : FlowComponentBase, IBulkConsumer, IItemProducer, IMaterialObservable, IFaultTarget
{
    /// <summary>The cutter jams: nothing is formed until cleared. Bulk still accumulates.</summary>
    public const string Jam = "jam";

    private static readonly FaultDescriptor[] Faults =
    [
        new(Jam, "The cutter jams; no pieces are formed until the fault is cleared."),
    ];

    private readonly Queue<ItemInstance> _ready = new();
    private readonly MaterialType _output;
    private BulkLot _hopper;
    private ItemIdSequence? _ids;
    private double _elapsed;
    private long _formed;
    private bool _jammed;
    private TelemetryHandle _hopperTelemetry;
    private TelemetryHandle _formedTelemetry;

    public Former(
        string id,
        MaterialType input,
        MaterialType output,
        double pieceMassKg,
        double cycleSeconds,
        double hopperCapacityKg,
        int outputQueueCapacity = int.MaxValue)
        : base(id)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);
        if (input.Kind != PayloadKind.Bulk)
        {
            throw new ArgumentException($"Former input '{input}' must be a Bulk material.", nameof(input));
        }

        if (output.Kind != PayloadKind.Discrete)
        {
            throw new ArgumentException($"Former output '{output}' must be a Discrete material.", nameof(output));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pieceMassKg);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(cycleSeconds);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(hopperCapacityKg);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(pieceMassKg, hopperCapacityKg);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(outputQueueCapacity);

        _output = output;
        PieceMassKg = pieceMassKg;
        CycleSeconds = cycleSeconds;
        HopperCapacityKg = hopperCapacityKg;
        OutputQueueCapacity = outputQueueCapacity;

        In = AddInlet("In", PayloadKind.Bulk);
        Out = AddOutlet("Out", PayloadKind.Discrete);
        PiecesFormed = AddOutput<long>("PiecesFormed");
        HopperLevel = AddOutput<double>("HopperLevel");
        Queued = AddOutput<int>("Queued");
    }

    public FlowInlet In { get; }

    public FlowOutlet Out { get; }

    public OutputPort<long> PiecesFormed { get; }

    /// <summary>Hopper mass over capacity, 0..1.</summary>
    public OutputPort<double> HopperLevel { get; }

    public OutputPort<int> Queued { get; }

    /// <summary>kg.</summary>
    public double PieceMassKg { get; }

    /// <summary>s between pieces.</summary>
    public double CycleSeconds { get; }

    /// <summary>kg.</summary>
    public double HopperCapacityKg { get; }

    public int OutputQueueCapacity { get; }

    public BulkLot Hopper => _hopper;

    public override double MassHeld => _hopper.Mass + (_ready.Count * PieceMassKg);

    public IReadOnlyList<FaultDescriptor> SupportedFaults => Faults;

    public override void Initialize(in InitContext ctx)
    {
        _ids = ctx.Items;
        _hopperTelemetry = ctx.RegisterTelemetry("Hopper", "kg");
        _formedTelemetry = ctx.RegisterTelemetry("Formed", "count");
    }

    public override void Evaluate(in TickContext ctx)
    {
        PiecesFormed.Value = _formed;
        HopperLevel.Value = _hopper.Mass / HopperCapacityKg;
        Queued.Value = _ready.Count;
        _hopperTelemetry.Write(_hopper.Mass);
        _formedTelemetry.Write(_formed);
    }

    public override void Advance(in TickContext ctx)
    {
        _elapsed += ctx.Dt;
        if (_elapsed < CycleSeconds - 1e-12)
        {
            return;
        }

        _elapsed -= CycleSeconds;
        if (_jammed || _ready.Count >= OutputQueueCapacity || _hopper.Mass < PieceMassKg - 1e-12)
        {
            return;
        }

        BulkLot piece = _hopper.Take(PieceMassKg, out BulkLot remaining);
        _hopper = remaining;
        _ready.Enqueue(new ItemInstance(_ids!.Next(), _output, piece.Mass, piece.Properties));
        _formed++;
    }

    public double AcceptMass(FlowInlet inlet) => Math.Max(0.0, HopperCapacityKg - _hopper.Mass);

    public void Deposit(FlowInlet inlet, in BulkLot lot) => _hopper = _hopper.Merge(lot);

    public bool TryPeekItem(FlowOutlet outlet, [NotNullWhen(true)] out ItemInstance? item) =>
        _ready.TryPeek(out item);

    public ItemInstance WithdrawItem(FlowOutlet outlet) => _ready.Dequeue();

    /// <summary>The hopper's contents; position and window are ignored.</summary>
    public bool TryObserve(double position, double window, out MaterialObservation observation)
    {
        if (_hopper.IsEmpty)
        {
            observation = default;
            return false;
        }

        observation = new MaterialObservation(_hopper.Mass, 0.0, _hopper.Properties, 0L);
        return true;
    }

    public void ApplyFault(string faultId, FaultArguments arguments) => _jammed = true;

    public void ClearFault(string faultId) => _jammed = false;
}
```

`BulkLot.Take` never returns more than asked, and the hopper holds at least a
piece, so `piece.Mass == PieceMassKg` — `MassHeld` may use the constant.

- [ ] **Step 4: Run the tests**

Run: `dotnet test tests/Dse.Components.Tests`
Expected: 24 pass.

- [ ] **Step 5: Commit**

```bash
git add src/Dse.Components/Flow/Former.cs tests/Dse.Components.Tests/FormerTests.cs
git commit -m "feat(components): add the former that cuts bulk into discrete pieces"
```

---

### Task 8: Hold conditions and the bulk process unit

**Files:**
- Create: `src/Dse.Components/Flow/ProcessPhase.cs`
- Create: `src/Dse.Components/Flow/IHoldCondition.cs`
- Create: `src/Dse.Components/Flow/Hold.cs`
- Create: `src/Dse.Components/Flow/RecipeLine.cs`
- Create: `src/Dse.Components/Flow/BulkProcessUnit.cs`
- Test: `tests/Dse.Components.Tests/BulkProcessUnitTests.cs`

**Interfaces:**
- Consumes: transport interfaces, `BulkLot`, `MaterialProperties.Blend`,
  `IMaterialTransform`, `TransformContext`, `IMaterialObservable`, `IFaultTarget`.
- Produces:
  - `enum ProcessPhase { Idle, Filling, Processing, Discharging }`
  - `interface IHoldCondition { bool IsSatisfied(double elapsedSeconds, in MaterialProperties properties, ReadOnlySpan<double> state); }`
  - `static class Hold`: `ForSeconds(double)`, `TemperatureAtLeast(double)`,
    `TemperatureAtMost(double)`, `StateAtLeast(int stateIndex, double value)`,
    `StateAtLeast(MaterialType, string stateName, double value)`, `All(params IHoldCondition[])`.
  - `sealed record RecipeLine(string InletName, MaterialType Material, double MassKg)`.
  - `BulkProcessUnit(string id, IReadOnlyList<RecipeLine> recipe, IHoldCondition hold, MaterialType output, double yield = 1.0, IReadOnlyList<IMaterialTransform>? transforms = null)`
    : `FlowComponentBase, IBulkConsumer, IBulkProducer, IMaterialObservable, IFaultTarget`.
    Ports: one `FlowInlet` per recipe line (named by the line; `Inlet(string name)`
    looks one up), `FlowOutlet Out`, `InputPort<double> AmbientTemperature`
    (default 20), `OutputPort<ProcessPhase> Phase`, `OutputPort<double> BatchMass` (kg),
    `OutputPort<double> Progress` (filling: mass fraction received; processing:
    elapsed / hold seconds when timed, else 0 or 1; discharging: fraction left).
    Telemetry `Batch` (kg), `Lost` (kg), `Cycles` (count). Events `FILLING`,
    `PROCESSING`, `DISCHARGING`, `IDLE` on each transition. Faults
    `"discharge-jam"` and `"yield-loss"` (parameter `fraction`, default 0.05:
    extra loss on top of the configured yield).

Spec 8.2. `Idle` accepts the first deposit and becomes `Filling` (a unit is
"idle" when empty and ready). `Filling` accepts, per inlet, only what the
recipe still needs, so an over-supplied feed backs up. When every line is
satisfied the lines merge into one batch of the output type — this is where
flour and water become dough — properties blended by mass. `Processing`
applies the transforms every tick with the ambient input and tests the hold
each tick; on satisfaction the yield is applied and the loss is booked (R16).
`Discharging` offers the batch; when it is empty the unit is `Idle` again. All
state changes are in `Advance`.

- [ ] **Step 1: Write the failing tests**

`tests/Dse.Components.Tests/BulkProcessUnitTests.cs`:

```csharp
using Dse.Components.Flow;
using Dse.Components.Tests.Fakes;
using Dse.Components.Transforms;
using Dse.Core;
using Dse.Core.Faults;
using Dse.Core.Flow;
using Dse.Core.Time;
using Xunit;

namespace Dse.Components.Tests;

public class BulkProcessUnitTests
{
    private static readonly MaterialType Flour = new("Flour", PayloadKind.Bulk);
    private static readonly MaterialType Water = new("Water", PayloadKind.Bulk);
    private static readonly MaterialType Dough = new("Dough", PayloadKind.Bulk);

    private static SimulationOptions Options() => new()
    {
        Seed = 1UL,
        StartTime = new DateTimeOffset(2026, 1, 1, 6, 0, 0, TimeSpan.Zero),
        TimeStep = TimeSpan.FromSeconds(0.5),
    };

    private sealed record Plant(Simulation Sim, BulkSource Flour, BulkSource Water, BulkProcessUnit Mixer, BulkSink Out, Setpoint Ambient);

    private static Plant Build(IHoldCondition hold, double yield = 1.0, IReadOnlyList<IMaterialTransform>? transforms = null)
    {
        var flour = new BulkSource("FlourFeed", Flour, 4.0, new MaterialProperties(600.0, 0.12, 20.0));
        var water = new BulkSource("WaterFeed", Water, 2.0, new MaterialProperties(1000.0, 1.0, 10.0));
        var mixer = new BulkProcessUnit(
            "Mixer",
            [new RecipeLine("Flour", Flour, 6.0), new RecipeLine("Water", Water, 4.0)],
            hold,
            Dough,
            yield,
            transforms);
        var sink = new BulkSink("Out");
        var ambient = new Setpoint("Ambient", 30.0);
        flour.Out.ConnectTo(mixer.Inlet("Flour"));
        water.Out.ConnectTo(mixer.Inlet("Water"));
        mixer.Out.ConnectTo(sink.In);
        ambient.Out.ConnectTo(mixer.AmbientTemperature);
        Simulation sim = new SimulationBuilder(Options()).Add(sink).Add(mixer).Add(flour).Add(water).Add(ambient).Build();
        return new Plant(sim, flour, water, mixer, sink, ambient);
    }

    private static IEnumerable<string> Phases(Simulation sim) =>
        sim.Events.Records.Where(r => r.Source == "Mixer").Select(r => r.Code);

    [Fact]
    public void FillsByRecipeMixesHoldsAndDischargesABlendedBatch()
    {
        Plant plant = Build(Hold.ForSeconds(3.0));

        plant.Sim.RunFor(TimeSpan.FromSeconds(4));    // flour 2 kg/tick needs 3 ticks, water 1 kg/tick needs 4
        Assert.Equal(ProcessPhase.Processing, plant.Mixer.Phase.Value);
        Assert.Equal(10.0, plant.Mixer.BatchMass.Value, 9);
        Assert.Equal(["FILLING", "PROCESSING"], Phases(plant.Sim));

        plant.Sim.RunFor(TimeSpan.FromSeconds(4));
        Assert.Equal(10.0, plant.Out.MassDestroyed, 9);
        Assert.Same(Dough, plant.Out.LastType);
        Assert.Equal(0.6 * 0.12 + 0.4 * 1.0, plant.Out.LastProperties.Moisture, 9);
        Assert.Equal(0.6 * 20.0 + 0.4 * 10.0, plant.Out.LastProperties.Temperature, 9);
        Assert.Equal(["FILLING", "PROCESSING", "DISCHARGING", "IDLE", "FILLING"], Phases(plant.Sim).Take(5));
        Assert.Equal(0.0, plant.Sim.MassBalance.Drift, 9);
    }

    [Fact]
    public void OverSuppliedLinesBackUpDuringProcessing()
    {
        Plant plant = Build(Hold.ForSeconds(10.0));

        plant.Sim.RunFor(TimeSpan.FromSeconds(8));

        Assert.Equal(ProcessPhase.Processing, plant.Mixer.Phase.Value);
        Assert.True(plant.Flour.MassHeld > 0.0);
        Assert.True(plant.Water.MassHeld > 0.0);
        Assert.Equal(10.0, plant.Mixer.MassHeld, 9);
    }

    [Fact]
    public void APropertyHoldReleasesWhenTheBatchGetsThere()
    {
        Plant plant = Build(Hold.TemperatureAtLeast(29.0), transforms: [new ThermalTransfer(2.0)]);

        plant.Sim.RunFor(TimeSpan.FromSeconds(30));

        Assert.True(plant.Out.MassDestroyed >= 10.0 - 1e-9);
        Assert.True(plant.Out.LastProperties.Temperature >= 29.0);
        Assert.Contains("DISCHARGING", Phases(plant.Sim));
    }

    [Fact]
    public void YieldLossIsBookedAsADeclaredLoss()
    {
        Plant plant = Build(Hold.ForSeconds(1.0), yield: 0.9);

        plant.Sim.RunFor(TimeSpan.FromSeconds(8));

        Assert.Equal(9.0, plant.Out.MassDestroyed, 9);
        Assert.Equal(1.0, plant.Mixer.MassDestroyed, 9);
        Assert.Equal(1.0, plant.Sim.Telemetry.Read("Mixer.Lost"), 9);
        Assert.Equal(0.0, plant.Sim.MassBalance.Drift, 9);
    }

    [Fact]
    public void FaultsJamTheDischargeAndAddLoss()
    {
        Plant plant = Build(Hold.ForSeconds(1.0));
        plant.Sim.InjectFaultAt(TimeSpan.Zero, "Mixer", BulkProcessUnit.DischargeJam);
        plant.Sim.InjectFaultAt(TimeSpan.Zero, "Mixer", BulkProcessUnit.YieldLoss, new FaultArguments(new("fraction", 0.2)));

        plant.Sim.RunFor(TimeSpan.FromSeconds(8));
        Assert.Equal(ProcessPhase.Discharging, plant.Mixer.Phase.Value);
        Assert.Equal(0.0, plant.Out.MassDestroyed, 9);
        Assert.Equal(2.0, plant.Mixer.MassDestroyed, 9);

        plant.Sim.ClearFaultIn(TimeSpan.Zero, "Mixer", BulkProcessUnit.DischargeJam);
        plant.Sim.RunFor(TimeSpan.FromSeconds(2));
        Assert.Equal(8.0, plant.Out.MassDestroyed, 9);
        Assert.Equal(0.0, plant.Sim.MassBalance.Drift, 9);
    }

    [Fact]
    public void HoldConditionsCompose()
    {
        var props = new MaterialProperties(1.0, 0.1, 50.0);
        IHoldCondition both = Hold.All(Hold.ForSeconds(5.0), Hold.TemperatureAtLeast(40.0));
        Assert.False(both.IsSatisfied(4.0, in props, ReadOnlySpan<double>.Empty));
        Assert.True(both.IsSatisfied(5.0, in props, ReadOnlySpan<double>.Empty));
        Assert.False(Hold.TemperatureAtMost(40.0).IsSatisfied(99.0, in props, ReadOnlySpan<double>.Empty));

        var type = new MaterialType("T", PayloadKind.Discrete, "Soak");
        IHoldCondition soak = Hold.StateAtLeast(type, "Soak", 2.0);
        Assert.False(soak.IsSatisfied(0.0, in props, [1.0]));
        Assert.True(soak.IsSatisfied(0.0, in props, [2.0]));
        Assert.False(soak.IsSatisfied(0.0, in props, ReadOnlySpan<double>.Empty));
    }

    [Fact]
    public void RejectsBadRecipes()
    {
        Assert.Throws<ArgumentException>(() => new BulkProcessUnit("M", [], Hold.ForSeconds(1.0), Dough));
        Assert.Throws<ArgumentException>(() => new BulkProcessUnit(
            "M", [new RecipeLine("A", Flour, 1.0), new RecipeLine("A", Water, 1.0)], Hold.ForSeconds(1.0), Dough));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BulkProcessUnit(
            "M", [new RecipeLine("A", Flour, 1.0)], Hold.ForSeconds(1.0), Dough, yield: 1.5));
    }
}
```

`BulkSink` records only the last lot's properties, so add the type alongside.
In `src/Dse.Components/Flow/BulkSink.cs` add

```csharp
    /// <summary>Material type of the last lot deposited, or null before any.</summary>
    public MaterialType? LastType { get; private set; }
```

and in `Deposit`, after `LastProperties = lot.Properties;`, add `LastType = lot.Type;`.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Dse.Components.Tests --filter "FullyQualifiedName~BulkProcessUnitTests"`
Expected: build error — `BulkProcessUnit` does not exist.

- [ ] **Step 3: Implement the small types**

`src/Dse.Components/Flow/ProcessPhase.cs`:

```csharp
namespace Dse.Components.Flow;

/// <summary>Where a batch unit is in its cycle.</summary>
public enum ProcessPhase
{
    /// <summary>Empty and ready; the first deposit starts filling.</summary>
    Idle,
    Filling,
    Processing,
    Discharging,
}
```

`src/Dse.Components/Flow/IHoldCondition.cs`:

```csharp
using Dse.Core.Flow;

namespace Dse.Components.Flow;

/// <summary>
/// When a batch is done: a fixed time, a property threshold, an accumulated
/// state, or a combination. Evaluated once per tick against the batch (bulk)
/// or against every item (discrete).
/// </summary>
public interface IHoldCondition
{
    bool IsSatisfied(double elapsedSeconds, in MaterialProperties properties, ReadOnlySpan<double> state);
}
```

`src/Dse.Components/Flow/Hold.cs`:

```csharp
using Dse.Core.Flow;

namespace Dse.Components.Flow;

/// <summary>The shipped hold conditions.</summary>
public static class Hold
{
    public static IHoldCondition ForSeconds(double seconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(seconds);
        return new Timed(seconds);
    }

    public static IHoldCondition TemperatureAtLeast(double celsius) => new TemperatureFloor(celsius);

    public static IHoldCondition TemperatureAtMost(double celsius) => new TemperatureCeiling(celsius);

    /// <summary>A state slot at or above a value. Unsatisfiable for material without that slot.</summary>
    public static IHoldCondition StateAtLeast(int stateIndex, double value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(stateIndex);
        return new StateFloor(stateIndex, value);
    }

    public static IHoldCondition StateAtLeast(MaterialType type, string stateName, double value)
    {
        ArgumentNullException.ThrowIfNull(type);
        return StateAtLeast(type.StateIndexOf(stateName), value);
    }

    public static IHoldCondition All(params IHoldCondition[] conditions)
    {
        ArgumentNullException.ThrowIfNull(conditions);
        ArgumentOutOfRangeException.ThrowIfZero(conditions.Length);
        return new Conjunction(conditions.ToArray());
    }

    /// <summary>The seconds a timed hold waits for, or null. Used by units to report progress.</summary>
    internal static double? TimedSeconds(IHoldCondition condition) => condition switch
    {
        Timed timed => timed.Seconds,
        Conjunction all => all.Conditions.Select(TimedSeconds).FirstOrDefault(s => s is not null),
        _ => null,
    };

    private sealed class Timed(double seconds) : IHoldCondition
    {
        public double Seconds { get; } = seconds;

        public bool IsSatisfied(double elapsedSeconds, in MaterialProperties properties, ReadOnlySpan<double> state) =>
            elapsedSeconds >= Seconds - 1e-12;
    }

    private sealed class TemperatureFloor(double celsius) : IHoldCondition
    {
        public bool IsSatisfied(double elapsedSeconds, in MaterialProperties properties, ReadOnlySpan<double> state) =>
            properties.Temperature >= celsius;
    }

    private sealed class TemperatureCeiling(double celsius) : IHoldCondition
    {
        public bool IsSatisfied(double elapsedSeconds, in MaterialProperties properties, ReadOnlySpan<double> state) =>
            properties.Temperature <= celsius;
    }

    private sealed class StateFloor(int index, double value) : IHoldCondition
    {
        public bool IsSatisfied(double elapsedSeconds, in MaterialProperties properties, ReadOnlySpan<double> state) =>
            index < state.Length && state[index] >= value;
    }

    private sealed class Conjunction(IHoldCondition[] conditions) : IHoldCondition
    {
        public IHoldCondition[] Conditions { get; } = conditions;

        public bool IsSatisfied(double elapsedSeconds, in MaterialProperties properties, ReadOnlySpan<double> state)
        {
            for (int i = 0; i < Conditions.Length; i++)
            {
                if (!Conditions[i].IsSatisfied(elapsedSeconds, in properties, state))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
```

`src/Dse.Components/Flow/RecipeLine.cs`:

```csharp
using Dse.Core.Flow;

namespace Dse.Components.Flow;

/// <summary>One ingredient of a bulk recipe: the inlet it arrives on, what it is, how much.</summary>
/// <param name="InletName">The inlet's port name on the unit.</param>
/// <param name="Material">Must be a Bulk material.</param>
/// <param name="MassKg">kg per batch.</param>
public sealed record RecipeLine(string InletName, MaterialType Material, double MassKg);
```

- [ ] **Step 4: Implement the unit**

`src/Dse.Components/Flow/BulkProcessUnit.cs`:

```csharp
using System.Globalization;
using Dse.Core.Contexts;
using Dse.Core.Faults;
using Dse.Core.Flow;
using Dse.Core.Graph;
using Dse.Core.Telemetry;

namespace Dse.Components.Flow;

/// <summary>
/// A batch unit for bulk material: Idle → Filling → Processing → Discharging.
/// Filling accepts, per inlet, only what the recipe still needs; the lines
/// then merge into one batch of the output type with mass-blended properties.
/// Processing applies the transforms and tests the hold every tick; on
/// release the yield is applied and the loss booked as a declared loss.
/// A mixer, a prover and a furnace are configurations of this class.
/// </summary>
public sealed class BulkProcessUnit : FlowComponentBase, IBulkConsumer, IBulkProducer, IMaterialObservable, IFaultTarget
{
    /// <summary>The discharge valve fails to open: nothing leaves until cleared.</summary>
    public const string DischargeJam = "discharge-jam";

    /// <summary>Extra loss on top of the configured yield, as a fraction of each batch.</summary>
    public const string YieldLoss = "yield-loss";

    private static readonly FaultDescriptor[] Faults =
    [
        new(DischargeJam, "The discharge fails to open; the batch stays in the unit until the fault is cleared."),
        new(YieldLoss, "Extra loss on every batch released while active.",
            new FaultParameter("fraction", "", 0.05, "Fraction of the batch lost, on top of the configured yield.")),
    ];

    private readonly RecipeLine[] _recipe;
    private readonly FlowInlet[] _inlets;
    private readonly BulkLot[] _received;
    private readonly IHoldCondition _hold;
    private readonly double? _holdSeconds;
    private readonly MaterialType _output;
    private readonly IMaterialTransform[] _transforms;
    private BulkLot _batch;
    private ProcessPhase _phase;
    private double _elapsed;
    private double _lost;
    private long _cycles;
    private bool _jammed;
    private bool _pendingFill;
    private double _extraLoss;
    private TelemetryHandle _batchTelemetry;
    private TelemetryHandle _lostTelemetry;
    private TelemetryHandle _cyclesTelemetry;

    public BulkProcessUnit(
        string id,
        IReadOnlyList<RecipeLine> recipe,
        IHoldCondition hold,
        MaterialType output,
        double yield = 1.0,
        IReadOnlyList<IMaterialTransform>? transforms = null)
        : base(id)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        ArgumentNullException.ThrowIfNull(hold);
        ArgumentNullException.ThrowIfNull(output);
        if (recipe.Count == 0)
        {
            throw new ArgumentException("A recipe needs at least one line.", nameof(recipe));
        }

        if (output.Kind != PayloadKind.Bulk)
        {
            throw new ArgumentException($"Output '{output}' must be a Bulk material.", nameof(output));
        }

        if (yield <= 0.0 || yield > 1.0)
        {
            throw new ArgumentOutOfRangeException(nameof(yield), yield, "Yield must be in (0, 1].");
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (RecipeLine line in recipe)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(line.InletName, nameof(recipe));
            if (line.Material.Kind != PayloadKind.Bulk)
            {
                throw new ArgumentException($"Recipe line '{line.InletName}' material '{line.Material}' must be Bulk.", nameof(recipe));
            }

            if (line.MassKg <= 0.0)
            {
                throw new ArgumentException($"Recipe line '{line.InletName}' needs a positive mass.", nameof(recipe));
            }

            if (!names.Add(line.InletName))
            {
                throw new ArgumentException($"Recipe line '{line.InletName}' is declared twice.", nameof(recipe));
            }
        }

        _recipe = recipe.ToArray();
        _inlets = new FlowInlet[_recipe.Length];
        _received = new BulkLot[_recipe.Length];
        for (int i = 0; i < _recipe.Length; i++)
        {
            _inlets[i] = AddInlet(_recipe[i].InletName, PayloadKind.Bulk);
        }

        _hold = hold;
        _holdSeconds = Hold.TimedSeconds(hold);
        _output = output;
        Yield = yield;
        _transforms = transforms is null ? [] : transforms.ToArray();

        Out = AddOutlet("Out", PayloadKind.Bulk);
        AmbientTemperature = AddInput<double>("AmbientTemperature", defaultValue: 20.0);
        Phase = AddOutput<ProcessPhase>("Phase");
        BatchMass = AddOutput<double>("BatchMass");
        Progress = AddOutput<double>("Progress");
    }

    public FlowOutlet Out { get; }

    /// <summary>Zone temperature handed to the transforms, °C. Unconnected reads 20 °C.</summary>
    public InputPort<double> AmbientTemperature { get; }

    public OutputPort<ProcessPhase> Phase { get; }

    /// <summary>Mass in the unit, kg: the lines while filling, the batch afterwards.</summary>
    public OutputPort<double> BatchMass { get; }

    /// <summary>0..1 through the current phase where that is meaningful.</summary>
    public OutputPort<double> Progress { get; }

    /// <summary>Fraction of the batch that survives release; the rest is a declared loss.</summary>
    public double Yield { get; }

    public IReadOnlyList<RecipeLine> Recipe => _recipe;

    public ProcessPhase CurrentPhase => _phase;

    public override double MassHeld
    {
        get
        {
            double total = _batch.Mass;
            for (int i = 0; i < _received.Length; i++)
            {
                total += _received[i].Mass;
            }

            return total;
        }
    }

    public override double MassDestroyed => _lost;

    public IReadOnlyList<FaultDescriptor> SupportedFaults => Faults;

    /// <summary>The inlet for a recipe line, by its name.</summary>
    public FlowInlet Inlet(string name)
    {
        int index = LineIndex(name);
        if (index < 0)
        {
            throw new KeyNotFoundException(
                $"'{Id}' has no recipe line '{name}'. Lines: {string.Join(", ", _recipe.Select(l => l.InletName))}.");
        }

        return _inlets[index];
    }

    public override void Initialize(in InitContext ctx)
    {
        _batchTelemetry = ctx.RegisterTelemetry("Batch", "kg");
        _lostTelemetry = ctx.RegisterTelemetry("Lost", "kg");
        _cyclesTelemetry = ctx.RegisterTelemetry("Cycles", "count");
    }

    public override void Evaluate(in TickContext ctx)
    {
        Phase.Value = _phase;
        BatchMass.Value = MassHeld;
        Progress.Value = _phase switch
        {
            ProcessPhase.Filling => ReceivedFraction(),
            ProcessPhase.Processing => _holdSeconds is { } seconds && seconds > 0.0 ? Math.Min(1.0, _elapsed / seconds) : 0.0,
            ProcessPhase.Discharging => 1.0 - (_batch.Mass / Math.Max(_batch.Mass, BatchTarget())),
            _ => 0.0,
        };
        _batchTelemetry.Write(MassHeld);
        _lostTelemetry.Write(_lost);
        _cyclesTelemetry.Write(_cycles);
    }

    public override void Advance(in TickContext ctx)
    {
        switch (_phase)
        {
            case ProcessPhase.Idle when _pendingFill:
                _pendingFill = false;
                Transition(ProcessPhase.Filling, "First material received; filling.", in ctx);
                if (AllLinesComplete())
                {
                    StartProcessing(in ctx);
                }

                break;

            case ProcessPhase.Filling when AllLinesComplete():
                StartProcessing(in ctx);
                break;

            case ProcessPhase.Processing:
                ApplyTransforms(ctx.Dt);
                _elapsed += ctx.Dt;
                if (_hold.IsSatisfied(_elapsed, _batch.Properties, ReadOnlySpan<double>.Empty))
                {
                    Release(in ctx);
                }

                break;

            case ProcessPhase.Discharging when _batch.IsEmpty:
                Transition(ProcessPhase.Idle, "Batch discharged; ready for the next.", in ctx);
                _cycles++;
                break;
        }
    }

    public double AcceptMass(FlowInlet inlet)
    {
        if (_phase is not (ProcessPhase.Idle or ProcessPhase.Filling))
        {
            return 0.0;
        }

        int index = Array.IndexOf(_inlets, inlet);
        return Math.Max(0.0, _recipe[index].MassKg - _received[index].Mass);
    }

    public void Deposit(FlowInlet inlet, in BulkLot lot)
    {
        int index = Array.IndexOf(_inlets, inlet);
        if (!ReferenceEquals(lot.Type, _recipe[index].Material))
        {
            throw new InvalidOperationException(
                $"'{Id}' inlet '{inlet.Name}' received '{lot.Type}' but the recipe calls for '{_recipe[index].Material}'.");
        }

        _received[index] = _received[index].Merge(lot);
        _pendingFill = _phase == ProcessPhase.Idle;
    }

    public double OfferMass(FlowOutlet outlet) =>
        _phase == ProcessPhase.Discharging && !_jammed ? _batch.Mass : 0.0;

    public BulkLot Withdraw(FlowOutlet outlet, double mass)
    {
        BulkLot taken = _batch.Take(mass, out BulkLot remaining);
        _batch = remaining;
        return taken;
    }

    /// <summary>The batch when there is one, else the blended lines; position and window ignored.</summary>
    public bool TryObserve(double position, double window, out MaterialObservation observation)
    {
        if (!_batch.IsEmpty)
        {
            observation = new MaterialObservation(_batch.Mass, 0.0, _batch.Properties, 0L);
            return true;
        }

        BulkLot blended = BlendLines(out double mass);
        if (mass <= 0.0)
        {
            observation = default;
            return false;
        }

        observation = new MaterialObservation(mass, 0.0, blended.Properties, 0L);
        return true;
    }

    public void ApplyFault(string faultId, FaultArguments arguments)
    {
        switch (faultId)
        {
            case DischargeJam:
                _jammed = true;
                break;
            case YieldLoss:
                _extraLoss = Math.Clamp(arguments.Get("fraction"), 0.0, 1.0);
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
            case YieldLoss:
                _extraLoss = 0.0;
                break;
        }
    }

    private void StartProcessing(in TickContext ctx)
    {
        BulkLot blended = BlendLines(out double mass);
        _batch = BulkLot.Of(_output, mass, blended.Properties);
        Array.Clear(_received);
        _elapsed = 0.0;
        Transition(ProcessPhase.Processing, "Recipe complete; processing.", in ctx);
    }

    private void Release(in TickContext ctx)
    {
        double lossFraction = Math.Min(1.0, (1.0 - Yield) + _extraLoss);
        double loss = _batch.Mass * lossFraction;
        if (loss > 0.0)
        {
            _batch = _batch with { Mass = _batch.Mass - loss };
            _lost += loss;
        }

        Transition(
            ProcessPhase.Discharging,
            string.Create(CultureInfo.InvariantCulture, $"Hold satisfied after {_elapsed} s; discharging {_batch.Mass} kg of {_output}."),
            in ctx);
    }

    private void Transition(ProcessPhase next, string message, in TickContext ctx)
    {
        _phase = next;
        ctx.Log(Id, next.ToString().ToUpperInvariant(), message);
    }

    private bool AllLinesComplete()
    {
        for (int i = 0; i < _recipe.Length; i++)
        {
            if (_received[i].Mass < _recipe[i].MassKg - 1e-9)
            {
                return false;
            }
        }

        return true;
    }

    private double ReceivedFraction()
    {
        double received = 0.0;
        for (int i = 0; i < _received.Length; i++)
        {
            received += _received[i].Mass;
        }

        return received / BatchTarget();
    }

    private double BatchTarget()
    {
        double total = 0.0;
        for (int i = 0; i < _recipe.Length; i++)
        {
            total += _recipe[i].MassKg;
        }

        return total;
    }

    private BulkLot BlendLines(out double mass)
    {
        mass = 0.0;
        MaterialProperties properties = default;
        for (int i = 0; i < _received.Length; i++)
        {
            if (_received[i].IsEmpty)
            {
                continue;
            }

            properties = MaterialProperties.Blend(in properties, mass, _received[i].Properties, _received[i].Mass);
            mass += _received[i].Mass;
        }

        return new BulkLot(null, mass, properties);
    }

    private void ApplyTransforms(double dt)
    {
        if (_transforms.Length == 0 || _batch.IsEmpty)
        {
            return;
        }

        var context = new TransformContext(AmbientTemperature.Value);
        MaterialProperties properties = _batch.Properties;
        foreach (IMaterialTransform transform in _transforms)
        {
            transform.Apply(ref properties, Span<double>.Empty, dt, in context);
        }

        _batch = _batch with { Properties = properties };
    }

    private int LineIndex(string name)
    {
        for (int i = 0; i < _recipe.Length; i++)
        {
            if (string.Equals(_recipe[i].InletName, name, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }
}
```

`Deposit` has no tick context to log with, so it only flags `_pendingFill`;
`Advance` performs the `Idle → Filling` transition and logs it.

- [ ] **Step 5: Run the tests**

Run: `dotnet test tests/Dse.Components.Tests`
Expected: 31 pass. Walk the first test by hand if a phase code is off. The
unit is downstream of its sources, so in the phase-3 sweep its `Advance` runs
*before* the sources' links deposit into it; a deposit on tick N is acted on
in `Advance` of tick N+1. With dt 0.5: flour (2 kg/tick) and water (1 kg/tick)
first land on tick 1, `FILLING` is logged on tick 2, water completes on tick 4,
`PROCESSING` starts on tick 5, the 3 s hold (six ticks of elapsed time)
releases on tick 11, the batch leaves on tick 12 and `IDLE` is logged the
same tick, then `FILLING` again on tick 13.

- [ ] **Step 6: Commit**

```bash
git add src/Dse.Components/Flow tests/Dse.Components.Tests/BulkProcessUnitTests.cs
git commit -m "feat(components): add hold conditions and the bulk process unit"
```

---

### Task 9: Item process unit

**Files:**
- Create: `src/Dse.Components/Flow/ItemProcessUnit.cs`
- Test: `tests/Dse.Components.Tests/ItemProcessUnitTests.cs`

**Interfaces:**
- Consumes: `ProcessPhase`, `IHoldCondition`, `Hold` (Task 8); `IItemConsumer`,
  `IItemProducer`, `ItemInstance`, `IMaterialObservable`, `IFaultTarget`.
- Produces: `ItemProcessUnit(string id, int batchSize, IHoldCondition hold, MaterialType? output = null, double yield = 1.0, IReadOnlyList<IMaterialTransform>? transforms = null)`
  : `FlowComponentBase, IItemConsumer, IItemProducer, IMaterialObservable, IFaultTarget`.
  Ports: `FlowInlet In`, `FlowOutlet Out`, `InputPort<double> AmbientTemperature`
  (default 20), `OutputPort<ProcessPhase> Phase`, `OutputPort<int> ItemCount`,
  `OutputPort<double> Progress`. Telemetry `Items` (count), `Lost` (kg),
  `Cycles` (count). Events as the bulk unit. Fault `"discharge-jam"`.
  `IReadOnlyList<ItemInstance> Items`.

Spec 8.2 and 15.2: a furnace holds a batch of billets to a soak condition; a
press takes one blank, holds for its cycle time, and discharges a wheel — a
type change with a yield (flash). The hold is satisfied when *every* item in
the batch satisfies it. On release each item is re-typed when an output type
is given (its accumulated state resets, as `ItemInstance.ChangeType` does)
and loses `(1 − yield)` of its mass to the declared loss.

- [ ] **Step 1: Write the failing tests**

`tests/Dse.Components.Tests/ItemProcessUnitTests.cs`:

```csharp
using Dse.Components.Flow;
using Dse.Components.Tests.Fakes;
using Dse.Components.Transforms;
using Dse.Core;
using Dse.Core.Flow;
using Dse.Core.Time;
using Xunit;

namespace Dse.Components.Tests;

public class ItemProcessUnitTests
{
    private static readonly MaterialType Billet = new("Billet", PayloadKind.Discrete, "SoakTime");
    private static readonly MaterialType Wheel = new("Wheel", PayloadKind.Discrete);

    private static SimulationOptions Options() => new()
    {
        Seed = 1UL,
        StartTime = new DateTimeOffset(2026, 1, 1, 6, 0, 0, TimeSpan.Zero),
        TimeStep = TimeSpan.FromSeconds(0.5),
    };

    private sealed record Plant(Simulation Sim, ItemSource Source, ItemProcessUnit Unit, ItemSink Sink, Setpoint Zone);

    private static Plant Build(int batchSize, IHoldCondition hold, MaterialType? output = null, double yield = 1.0, IReadOnlyList<IMaterialTransform>? transforms = null, double interval = 1.0)
    {
        var source = new ItemSource("Billets", Billet, 20.0, interval, new MaterialProperties(7800.0, 0.0, 25.0));
        var unit = new ItemProcessUnit("Furnace", batchSize, hold, output, yield, transforms);
        var sink = new ItemSink("Out");
        var zone = new Setpoint("Zone", 1200.0);
        source.Out.ConnectTo(unit.In);
        unit.Out.ConnectTo(sink.In);
        zone.Out.ConnectTo(unit.AmbientTemperature);
        Simulation sim = new SimulationBuilder(Options()).Add(sink).Add(unit).Add(source).Add(zone).Build();
        return new Plant(sim, source, unit, sink, zone);
    }

    private static IEnumerable<string> Phases(Simulation sim) =>
        sim.Events.Records.Where(r => r.Source == "Furnace").Select(r => r.Code);

    [Fact]
    public void FillsToTheBatchSizeHoldsAndDischargesInOrder()
    {
        Plant plant = Build(batchSize: 3, Hold.ForSeconds(2.0));

        // Items land on ticks 2, 4, 6; PROCESSING on tick 7; the 2 s hold releases
        // on tick 11; all three leave on tick 12 and IDLE is logged the same tick.
        plant.Sim.RunFor(TimeSpan.FromSeconds(7));

        Assert.Equal(3L, plant.Sink.Count.Value);
        Assert.Equal(3L, plant.Sink.LastItem!.Id);
        Assert.Equal(["FILLING", "PROCESSING", "DISCHARGING", "IDLE"], Phases(plant.Sim).Take(4));
        Assert.Equal(0.0, plant.Sim.MassBalance.Drift, 9);
    }

    [Fact]
    public void ASoakConditionOnStateReleasesWhenEveryItemHasSoaked()
    {
        Plant plant = Build(
            batchSize: 2,
            Hold.StateAtLeast(Billet, "SoakTime", 3.0),
            transforms: [new ThermalTransfer(1.0), ResidenceAccumulator.For(Billet, "SoakTime", 1100.0)]);

        plant.Sim.RunFor(TimeSpan.FromSeconds(40));

        Assert.True(plant.Sink.Count.Value >= 2);
        Assert.True(plant.Sink.LastItem!.State[0] >= 3.0);
        Assert.True(plant.Sink.LastItem.Properties.Temperature > 1100.0);
    }

    [Fact]
    public void APressChangesTypeAndLosesFlash()
    {
        Plant plant = Build(batchSize: 1, Hold.ForSeconds(1.0), output: Wheel, yield: 0.95);

        plant.Sim.RunFor(TimeSpan.FromSeconds(10));

        Assert.True(plant.Sink.Count.Value >= 1);
        Assert.Same(Wheel, plant.Sink.LastItem!.Type);
        Assert.Equal(19.0, plant.Sink.LastItem.Mass, 9);
        Assert.Empty(plant.Sink.LastItem.State);
        // Loss is booked at release, one tick before discharge, so it may run one item ahead of the sink.
        Assert.InRange(plant.Unit.MassDestroyed, plant.Sink.Count.Value * 1.0, (plant.Sink.Count.Value + 2) * 1.0);
        Assert.Equal(0.0, plant.Sim.MassBalance.Drift, 9);
    }

    [Fact]
    public void AFullUnitBacksUpItsFeed()
    {
        Plant plant = Build(batchSize: 2, Hold.ForSeconds(30.0), interval: 0.5);

        plant.Sim.RunFor(TimeSpan.FromSeconds(10));

        Assert.Equal(ProcessPhase.Processing, plant.Unit.Phase.Value);
        Assert.Equal(2, plant.Unit.ItemCount.Value);
        Assert.True(plant.Source.Queued.Value > 5);
    }

    [Fact]
    public void ADischargeJamHoldsTheBatch()
    {
        Plant plant = Build(batchSize: 1, Hold.ForSeconds(1.0));
        plant.Sim.InjectFaultAt(TimeSpan.Zero, "Furnace", ItemProcessUnit.DischargeJam);

        plant.Sim.RunFor(TimeSpan.FromSeconds(10));
        Assert.Equal(0L, plant.Sink.Count.Value);
        Assert.Equal(ProcessPhase.Discharging, plant.Unit.Phase.Value);

        plant.Sim.ClearFaultIn(TimeSpan.Zero, "Furnace", ItemProcessUnit.DischargeJam);
        plant.Sim.RunFor(TimeSpan.FromSeconds(2));
        Assert.True(plant.Sink.MassReceived > 0.0);
    }

    [Fact]
    public void ObservationShowsTheHeadItem()
    {
        Plant plant = Build(batchSize: 2, Hold.ForSeconds(30.0));
        plant.Sim.RunFor(TimeSpan.FromSeconds(5));

        Assert.True(plant.Unit.TryObserve(0.0, 0.0, out MaterialObservation seen));
        Assert.Equal(1L, seen.ItemId);
        Assert.Equal(20.0, seen.Mass);
    }

    [Fact]
    public void RejectsBadArguments()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ItemProcessUnit("U", 0, Hold.ForSeconds(1.0)));
        Assert.Throws<ArgumentException>(() => new ItemProcessUnit("U", 1, Hold.ForSeconds(1.0), new MaterialType("B", PayloadKind.Bulk)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ItemProcessUnit("U", 1, Hold.ForSeconds(1.0), yield: 0.0));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Dse.Components.Tests --filter "FullyQualifiedName~ItemProcessUnitTests"`
Expected: build error — `ItemProcessUnit` does not exist.

- [ ] **Step 3: Implement**

`src/Dse.Components/Flow/ItemProcessUnit.cs`:

```csharp
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Dse.Core.Contexts;
using Dse.Core.Faults;
using Dse.Core.Flow;
using Dse.Core.Graph;
using Dse.Core.Telemetry;

namespace Dse.Components.Flow;

/// <summary>
/// A batch unit for discrete items: Idle → Filling → Processing → Discharging.
/// Fills to a fixed count, applies the transforms to every item each tick
/// while the hold is unsatisfied for any of them, then re-types (optional),
/// applies the yield, and discharges in arrival order. A furnace and a press
/// are configurations of this class.
/// </summary>
public sealed class ItemProcessUnit : FlowComponentBase, IItemConsumer, IItemProducer, IMaterialObservable, IFaultTarget
{
    /// <summary>The discharge fails to open: nothing leaves until cleared.</summary>
    public const string DischargeJam = "discharge-jam";

    private static readonly FaultDescriptor[] Faults =
    [
        new(DischargeJam, "The discharge fails to open; the batch stays in the unit until the fault is cleared."),
    ];

    private readonly List<ItemInstance> _items = [];
    private readonly IHoldCondition _hold;
    private readonly double? _holdSeconds;
    private readonly MaterialType? _output;
    private readonly IMaterialTransform[] _transforms;
    private ProcessPhase _phase;
    private double _elapsed;
    private double _lost;
    private long _cycles;
    private bool _jammed;
    private bool _pendingFill;
    private TelemetryHandle _itemsTelemetry;
    private TelemetryHandle _lostTelemetry;
    private TelemetryHandle _cyclesTelemetry;

    public ItemProcessUnit(
        string id,
        int batchSize,
        IHoldCondition hold,
        MaterialType? output = null,
        double yield = 1.0,
        IReadOnlyList<IMaterialTransform>? transforms = null)
        : base(id)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);
        ArgumentNullException.ThrowIfNull(hold);
        if (output is not null && output.Kind != PayloadKind.Discrete)
        {
            throw new ArgumentException($"Output '{output}' must be a Discrete material.", nameof(output));
        }

        if (yield <= 0.0 || yield > 1.0)
        {
            throw new ArgumentOutOfRangeException(nameof(yield), yield, "Yield must be in (0, 1].");
        }

        BatchSize = batchSize;
        _hold = hold;
        _holdSeconds = Hold.TimedSeconds(hold);
        _output = output;
        Yield = yield;
        _transforms = transforms is null ? [] : transforms.ToArray();

        In = AddInlet("In", PayloadKind.Discrete);
        Out = AddOutlet("Out", PayloadKind.Discrete);
        AmbientTemperature = AddInput<double>("AmbientTemperature", defaultValue: 20.0);
        Phase = AddOutput<ProcessPhase>("Phase");
        ItemCount = AddOutput<int>("ItemCount");
        Progress = AddOutput<double>("Progress");
    }

    public FlowInlet In { get; }

    public FlowOutlet Out { get; }

    /// <summary>Zone temperature handed to the transforms, °C. Unconnected reads 20 °C.</summary>
    public InputPort<double> AmbientTemperature { get; }

    public OutputPort<ProcessPhase> Phase { get; }

    public OutputPort<int> ItemCount { get; }

    /// <summary>0..1 through the current phase where that is meaningful.</summary>
    public OutputPort<double> Progress { get; }

    public int BatchSize { get; }

    /// <summary>Fraction of each item's mass that survives release.</summary>
    public double Yield { get; }

    public ProcessPhase CurrentPhase => _phase;

    /// <summary>Items in the unit, in arrival order.</summary>
    public IReadOnlyList<ItemInstance> Items => _items;

    public override double MassHeld
    {
        get
        {
            double total = 0.0;
            for (int i = 0; i < _items.Count; i++)
            {
                total += _items[i].Mass;
            }

            return total;
        }
    }

    public override double MassDestroyed => _lost;

    public IReadOnlyList<FaultDescriptor> SupportedFaults => Faults;

    public override void Initialize(in InitContext ctx)
    {
        _itemsTelemetry = ctx.RegisterTelemetry("Items", "count");
        _lostTelemetry = ctx.RegisterTelemetry("Lost", "kg");
        _cyclesTelemetry = ctx.RegisterTelemetry("Cycles", "count");
    }

    public override void Evaluate(in TickContext ctx)
    {
        Phase.Value = _phase;
        ItemCount.Value = _items.Count;
        Progress.Value = _phase switch
        {
            ProcessPhase.Filling => (double)_items.Count / BatchSize,
            ProcessPhase.Processing => _holdSeconds is { } seconds && seconds > 0.0 ? Math.Min(1.0, _elapsed / seconds) : 0.0,
            ProcessPhase.Discharging => 1.0 - ((double)_items.Count / BatchSize),
            _ => 0.0,
        };
        _itemsTelemetry.Write(_items.Count);
        _lostTelemetry.Write(_lost);
        _cyclesTelemetry.Write(_cycles);
    }

    public override void Advance(in TickContext ctx)
    {
        switch (_phase)
        {
            case ProcessPhase.Idle when _pendingFill:
                _pendingFill = false;
                Transition(ProcessPhase.Filling, "First item received; filling.", in ctx);
                if (_items.Count >= BatchSize)
                {
                    StartProcessing(in ctx);
                }

                break;

            case ProcessPhase.Filling when _items.Count >= BatchSize:
                StartProcessing(in ctx);
                break;

            case ProcessPhase.Processing:
                ApplyTransforms(ctx.Dt);
                _elapsed += ctx.Dt;
                if (AllSatisfied())
                {
                    Release(in ctx);
                }

                break;

            case ProcessPhase.Discharging when _items.Count == 0:
                Transition(ProcessPhase.Idle, "Batch discharged; ready for the next.", in ctx);
                _cycles++;
                break;
        }
    }

    public bool CanAcceptItem(FlowInlet inlet, ItemInstance item) =>
        _phase is ProcessPhase.Idle or ProcessPhase.Filling && _items.Count < BatchSize;

    public void DepositItem(FlowInlet inlet, ItemInstance item)
    {
        _items.Add(item);
        _pendingFill |= _phase == ProcessPhase.Idle;
    }

    public bool TryPeekItem(FlowOutlet outlet, [NotNullWhen(true)] out ItemInstance? item)
    {
        if (_phase == ProcessPhase.Discharging && !_jammed && _items.Count > 0)
        {
            item = _items[0];
            return true;
        }

        item = null;
        return false;
    }

    public ItemInstance WithdrawItem(FlowOutlet outlet)
    {
        if (!TryPeekItem(outlet, out ItemInstance? item))
        {
            throw new InvalidOperationException($"'{Id}' has nothing to discharge.");
        }

        _items.RemoveAt(0);
        return item;
    }

    /// <summary>The head item; position and window are ignored.</summary>
    public bool TryObserve(double position, double window, out MaterialObservation observation)
    {
        if (_items.Count == 0)
        {
            observation = default;
            return false;
        }

        ItemInstance head = _items[0];
        observation = new MaterialObservation(head.Mass, 0.0, head.Properties, head.Id);
        return true;
    }

    public void ApplyFault(string faultId, FaultArguments arguments) => _jammed = true;

    public void ClearFault(string faultId) => _jammed = false;

    private void StartProcessing(in TickContext ctx)
    {
        _elapsed = 0.0;
        Transition(ProcessPhase.Processing, "Batch complete; processing.", in ctx);
    }

    private void Release(in TickContext ctx)
    {
        for (int i = 0; i < _items.Count; i++)
        {
            ItemInstance item = _items[i];
            if (_output is not null)
            {
                item.ChangeType(_output);
            }

            double loss = item.Mass * (1.0 - Yield);
            if (loss > 0.0)
            {
                item.Mass -= loss;
                _lost += loss;
            }
        }

        Transition(
            ProcessPhase.Discharging,
            string.Create(CultureInfo.InvariantCulture, $"Hold satisfied after {_elapsed} s; discharging {_items.Count} items."),
            in ctx);
    }

    private void Transition(ProcessPhase next, string message, in TickContext ctx)
    {
        _phase = next;
        ctx.Log(Id, next.ToString().ToUpperInvariant(), message);
    }

    private bool AllSatisfied()
    {
        for (int i = 0; i < _items.Count; i++)
        {
            ItemInstance item = _items[i];
            if (!_hold.IsSatisfied(_elapsed, item.Properties, item.State))
            {
                return false;
            }
        }

        return true;
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
            ItemInstance item = _items[i];
            MaterialProperties properties = item.Properties;
            foreach (IMaterialTransform transform in _transforms)
            {
                transform.Apply(ref properties, item.State, dt, in context);
            }

            item.Properties = properties;
        }
    }
}
```

- [ ] **Step 4: Run the tests**

Run: `dotnet test tests/Dse.Components.Tests`
Expected: 38 pass.

- [ ] **Step 5: Commit**

```bash
git add src/Dse.Components/Flow/ItemProcessUnit.cs tests/Dse.Components.Tests/ItemProcessUnitTests.cs
git commit -m "feat(components): add the item process unit with type change and yield"
```

---

### Task 10: Instrument base and the sensor-fault vocabulary

**Files:**
- Create: `src/Dse.Components/Instruments/InstrumentHealth.cs`
- Create: `src/Dse.Components/Instruments/InstrumentSpec.cs`
- Create: `src/Dse.Components/Instruments/InstrumentFaults.cs`
- Create: `src/Dse.Components/Instruments/InstrumentBase.cs`
- Create: `tests/Dse.Components.Tests/Fakes/ProbeInstrument.cs`
- Test: `tests/Dse.Components.Tests/InstrumentBaseTests.cs`

**Interfaces:**
- Consumes: `ComponentBase`, `InitContext.Random`, `DeterministicRandom.NextGaussian`,
  `IFaultTarget`, `FaultDescriptor`, `FaultParameter`.
- Produces:
  - `enum InstrumentHealth { Good, Uncertain, Bad }` — this plan emits `Good`
    and `Bad`; `Uncertain` is reserved for plan 4's plausibility checks.
  - `readonly record struct InstrumentSpec(string Unit, double RangeLow, double RangeHigh, double NoiseSigma = 0.0, double LagSeconds = 0.0)`.
  - `static class InstrumentFaults`: `Calibration` ("calibration": `gain` 1,
    `offset` 0), `Noise` ("noise": `sigma`), `Drift` ("drift": `rate` per
    second), `Lag` ("lag": `seconds`), `Freeze` ("freeze"), `FailHigh`
    ("fail-high"), `FailLow` ("fail-low"); `IReadOnlyList<FaultDescriptor> All`.
  - `abstract class InstrumentBase : ComponentBase, IFaultTarget` —
    `InstrumentBase(string id, InstrumentSpec spec)`; `InstrumentSpec Spec`;
    `OutputPort<double> Value`; `OutputPort<InstrumentHealth> Health`;
    `protected abstract double Measure(in TickContext ctx)`;
    `protected virtual void OnInitialize(in InitContext ctx)`; `Initialize` and
    `Evaluate` are sealed; telemetry `Truth` (the unfaulted measurement).

Spec 12: every instrument derives from `InstrumentBase`, which provides
calibration, noise, drift, lag, freeze, fail-high and fail-low, so a new sensor
arrives with the whole fault vocabulary working. Spec 6.5: instrumentation can
be lied to. The pipeline per tick:

```
truth   = Measure(ctx)                                  → telemetry Truth
reading = truth × gain + offset                         calibration fault
drift  += rate × dt;  reading += drift                  drift fault
reading += sigma × Gaussian()          if sigma > 0     spec noise or noise fault
filtered += (reading − filtered) × min(1, dt / lag)     spec lag or lag fault; lag 0 → filtered = reading
output  = frozen ? last output : filtered               freeze fault
output  = RangeHigh / RangeLow, Health = Bad            fail-high / fail-low
Value   = clamp(output, RangeLow, RangeHigh)
```

- [ ] **Step 1: Write the fake and the failing tests**

`tests/Dse.Components.Tests/Fakes/ProbeInstrument.cs`:

```csharp
using Dse.Components.Instruments;
using Dse.Core.Contexts;
using Dse.Core.Graph;

namespace Dse.Components.Tests.Fakes;

/// <summary>Measures whatever is on its input, so the base pipeline can be tested alone.</summary>
public sealed class ProbeInstrument : InstrumentBase
{
    public ProbeInstrument(string id, InstrumentSpec spec)
        : base(id, spec) => In = AddInput<double>("In");

    public InputPort<double> In { get; }

    protected override double Measure(in TickContext ctx) => In.Value;
}
```

`tests/Dse.Components.Tests/InstrumentBaseTests.cs`:

```csharp
using Dse.Components.Instruments;
using Dse.Components.Tests.Fakes;
using Dse.Core;
using Dse.Core.Faults;
using Dse.Core.Graph;
using Dse.Core.Time;
using Xunit;

namespace Dse.Components.Tests;

public class InstrumentBaseTests
{
    private static readonly InstrumentSpec Clean = new("m/s", 0.0, 10.0);

    private static SimulationOptions Options(ulong seed = 7UL) => new()
    {
        Seed = seed,
        StartTime = new DateTimeOffset(2026, 1, 1, 6, 0, 0, TimeSpan.Zero),
        TimeStep = TimeSpan.FromMilliseconds(100),
    };

    private static (ProbeInstrument Probe, OutputPort<double> Truth) Probe(InstrumentSpec spec, double truth)
    {
        var probe = new ProbeInstrument("S", spec);
        probe.Initialize(TestContexts.Init(probe.Id, dt: 0.1));
        var source = new OutputPort<double>("Out", "SP");
        source.ConnectTo(probe.In);
        source.Value = truth;
        return (probe, source);
    }

    private static void Run(ProbeInstrument probe, int ticks)
    {
        for (int i = 0; i < ticks; i++)
        {
            probe.Evaluate(TestContexts.Tick(i, dt: 0.1));
            probe.Latch();
        }
    }

    [Fact]
    public void ACleanInstrumentReadsTheTruth()
    {
        var (probe, _) = Probe(Clean, 3.5);
        Run(probe, 3);
        Assert.Equal(3.5, probe.Value.Value);
        Assert.Equal(InstrumentHealth.Good, probe.Health.Value);
    }

    [Fact]
    public void ReadingsAreClampedToTheRange()
    {
        var (probe, _) = Probe(Clean, 12.0);
        Run(probe, 1);
        Assert.Equal(10.0, probe.Value.Value);
        Assert.Equal(InstrumentHealth.Good, probe.Health.Value);
    }

    [Fact]
    public void CalibrationAppliesGainAndOffset()
    {
        var (probe, _) = Probe(Clean, 2.0);
        probe.ApplyFault(InstrumentFaults.Calibration, new FaultArguments(new("gain", 1.5), new("offset", 0.25)));
        Run(probe, 1);
        Assert.Equal(3.25, probe.Value.Value, 9);

        probe.ClearFault(InstrumentFaults.Calibration);
        Run(probe, 1);
        Assert.Equal(2.0, probe.Value.Value, 9);
    }

    [Fact]
    public void DriftAccumulatesAndClearingResetsIt()
    {
        var (probe, _) = Probe(Clean, 2.0);
        probe.ApplyFault(InstrumentFaults.Drift, new FaultArguments(new("rate", 0.5)));
        Run(probe, 10);   // 1 s at 0.5 /s
        Assert.Equal(2.5, probe.Value.Value, 9);

        probe.ClearFault(InstrumentFaults.Drift);
        Run(probe, 1);
        Assert.Equal(2.0, probe.Value.Value, 9);
    }

    [Fact]
    public void LagIsFirstOrder()
    {
        var (probe, truth) = Probe(Clean with { LagSeconds = 1.0 }, 0.0);
        Run(probe, 1);           // the first reading primes the filter at the truth
        truth.Value = 4.0;
        Run(probe, 10);          // one time constant
        Assert.InRange(probe.Value.Value, 4.0 * 0.62, 4.0 * 0.68);   // 1 − e⁻¹ = 0.632; Euler gives 0.651
        Run(probe, 100);
        Assert.Equal(4.0, probe.Value.Value, 3);
    }

    [Fact]
    public void ALagFaultOverridesTheSpecLag()
    {
        var (probe, truth) = Probe(Clean, 0.0);
        Run(probe, 1);
        probe.ApplyFault(InstrumentFaults.Lag, new FaultArguments(new("seconds", 5.0)));
        truth.Value = 4.0;
        Run(probe, 10);
        Assert.True(probe.Value.Value < 1.0);   // 4 × (1 − 0.98¹⁰) ≈ 0.73
    }

    [Fact]
    public void FreezeHoldsTheLastOutput()
    {
        var (probe, truth) = Probe(Clean, 2.0);
        Run(probe, 1);
        probe.ApplyFault(InstrumentFaults.Freeze, FaultArguments.None);
        truth.Value = 6.0;
        Run(probe, 5);
        Assert.Equal(2.0, probe.Value.Value);
        Assert.Equal(InstrumentHealth.Good, probe.Health.Value);

        probe.ClearFault(InstrumentFaults.Freeze);
        Run(probe, 1);
        Assert.Equal(6.0, probe.Value.Value);
    }

    [Fact]
    public void FailHighAndFailLowPinTheRangeAndReportBad()
    {
        var (probe, _) = Probe(Clean, 2.0);
        probe.ApplyFault(InstrumentFaults.FailHigh, FaultArguments.None);
        Run(probe, 1);
        Assert.Equal((10.0, InstrumentHealth.Bad), (probe.Value.Value, probe.Health.Value));

        probe.ClearFault(InstrumentFaults.FailHigh);
        probe.ApplyFault(InstrumentFaults.FailLow, FaultArguments.None);
        Run(probe, 1);
        Assert.Equal((0.0, InstrumentHealth.Bad), (probe.Value.Value, probe.Health.Value));

        probe.ClearFault(InstrumentFaults.FailLow);
        Run(probe, 1);
        Assert.Equal((2.0, InstrumentHealth.Good), (probe.Value.Value, probe.Health.Value));
    }

    [Fact]
    public void NoiseIsDeterministicPerSeedAndAbsentWhenSigmaIsZero()
    {
        static List<double> Sample(ulong seed, double sigma)
        {
            var probe = new ProbeInstrument("S", Clean with { NoiseSigma = sigma });
            var source = new Setpoint("SP", 5.0);
            source.Out.ConnectTo(probe.In);
            Simulation sim = new SimulationBuilder(Options(seed)).Add(probe).Add(source).Build();
            var samples = new List<double>();
            for (int i = 0; i < 50; i++)
            {
                sim.Tick();
                samples.Add(probe.Value.Value);
            }

            return samples;
        }

        Assert.All(Sample(1UL, 0.0), v => Assert.Equal(5.0, v));
        List<double> a = Sample(1UL, 0.1);
        Assert.Equal(a, Sample(1UL, 0.1));
        Assert.NotEqual(a, Sample(2UL, 0.1));
        Assert.True(a.Any(v => Math.Abs(v - 5.0) > 0.01));
        Assert.InRange(a.Average(), 4.9, 5.1);
    }

    [Fact]
    public void ANoiseFaultAddsNoiseToACleanInstrument()
    {
        var probe = new ProbeInstrument("S", Clean);
        var source = new Setpoint("SP", 5.0);
        source.Out.ConnectTo(probe.In);
        Simulation sim = new SimulationBuilder(Options()).Add(probe).Add(source).Build();
        sim.InjectFaultAt(TimeSpan.Zero, "S", InstrumentFaults.Noise, new FaultArguments(new("sigma", 0.5)));

        var samples = new List<double>();
        for (int i = 0; i < 20; i++)
        {
            sim.Tick();
            samples.Add(probe.Value.Value);
        }

        Assert.True(samples.Any(v => Math.Abs(v - 5.0) > 0.05));
    }

    [Fact]
    public void TruthTelemetryIsUnaffectedByFaults()
    {
        var probe = new ProbeInstrument("S", Clean);
        var source = new Setpoint("SP", 5.0);
        source.Out.ConnectTo(probe.In);
        Simulation sim = new SimulationBuilder(Options()).Add(probe).Add(source).Build();
        sim.InjectFaultAt(TimeSpan.Zero, "S", InstrumentFaults.FailLow);
        sim.Tick();

        Assert.Equal(0.0, probe.Value.Value);
        Assert.Equal(5.0, sim.Telemetry.Read("S.Truth"));
    }

    [Fact]
    public void EveryInstrumentPublishesTheSharedVocabulary()
    {
        var probe = new ProbeInstrument("S", Clean);
        Assert.Equal(
            ["calibration", "noise", "drift", "lag", "freeze", "fail-high", "fail-low"],
            probe.SupportedFaults.Select(f => f.Id));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Dse.Components.Tests --filter "FullyQualifiedName~InstrumentBaseTests"`
Expected: build error — `Dse.Components.Instruments` does not exist.

- [ ] **Step 3: Implement**

`src/Dse.Components/Instruments/InstrumentHealth.cs`:

```csharp
namespace Dse.Components.Instruments;

/// <summary>
/// What a transmitter reports about its own signal. Maps onto the I/O
/// layer's quality code. This library emits Good and Bad; Uncertain is
/// reserved for plausibility checks that arrive with the I/O layer.
/// </summary>
public enum InstrumentHealth
{
    Good,
    Uncertain,
    Bad,
}
```

`src/Dse.Components/Instruments/InstrumentSpec.cs`:

```csharp
namespace Dse.Components.Instruments;

/// <summary>What an instrument's datasheet says.</summary>
/// <param name="Unit">Engineering unit of <c>Value</c>.</param>
/// <param name="RangeLow">Lowest value the transmitter can report.</param>
/// <param name="RangeHigh">Highest value the transmitter can report.</param>
/// <param name="NoiseSigma">Standard deviation of per-tick Gaussian noise, in the unit. Zero for none.</param>
/// <param name="LagSeconds">First-order time constant of the reading. Zero for instantaneous.</param>
public readonly record struct InstrumentSpec(
    string Unit,
    double RangeLow,
    double RangeHigh,
    double NoiseSigma = 0.0,
    double LagSeconds = 0.0);
```

`src/Dse.Components/Instruments/InstrumentFaults.cs`:

```csharp
using Dse.Core.Faults;

namespace Dse.Components.Instruments;

/// <summary>The fault vocabulary every instrument supports.</summary>
public static class InstrumentFaults
{
    public const string Calibration = "calibration";
    public const string Noise = "noise";
    public const string Drift = "drift";
    public const string Lag = "lag";
    public const string Freeze = "freeze";
    public const string FailHigh = "fail-high";
    public const string FailLow = "fail-low";

    public static IReadOnlyList<FaultDescriptor> All { get; } =
    [
        new(Calibration, "Gain and offset error on the reading.",
            new FaultParameter("gain", "", 1.0, "Multiplier applied to the true value."),
            new FaultParameter("offset", "unit", 0.0, "Added after the gain.")),
        new(Noise, "Gaussian noise on the reading, replacing the datasheet noise.",
            new FaultParameter("sigma", "unit", 0.0, "Standard deviation per tick.")),
        new(Drift, "The reading walks away from the truth at a constant rate.",
            new FaultParameter("rate", "unit/s", 0.0, "Drift rate; clearing resets the accumulated drift.")),
        new(Lag, "A slower response than the datasheet.",
            new FaultParameter("seconds", "s", 0.0, "First-order time constant.")),
        new(Freeze, "The reading stops updating and holds its last value."),
        new(FailHigh, "The signal pins at the top of the range and health reads Bad."),
        new(FailLow, "The signal pins at the bottom of the range and health reads Bad."),
    ];
}
```

`src/Dse.Components/Instruments/InstrumentBase.cs`:

```csharp
using Dse.Core.Contexts;
using Dse.Core.Faults;
using Dse.Core.Graph;
using Dse.Core.Randomness;
using Dse.Core.Telemetry;

namespace Dse.Components.Instruments;

/// <summary>
/// Every sensor. A subclass says what it measures; this class says how a
/// real transmitter corrupts it — calibration, drift, noise, lag, freeze and
/// failure — so a new instrument arrives with the whole fault vocabulary
/// working. Telemetry <c>Truth</c> is the unfaulted measurement, for tests.
/// </summary>
public abstract class InstrumentBase : ComponentBase, IFaultTarget
{
    private DeterministicRandom? _random;
    private TelemetryHandle _truthTelemetry;
    private double _gain = 1.0;
    private double _offset;
    private double _driftRate;
    private double _drift;
    private double? _noiseOverride;
    private double? _lagOverride;
    private bool _frozen;
    private bool _failHigh;
    private bool _failLow;
    private double _filtered;
    private bool _primed;
    private double _lastOutput;

    protected InstrumentBase(string id, InstrumentSpec spec)
        : base(id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(spec.Unit, nameof(spec));
        if (spec.RangeHigh <= spec.RangeLow)
        {
            throw new ArgumentException("RangeHigh must exceed RangeLow.", nameof(spec));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(spec.NoiseSigma, nameof(spec));
        ArgumentOutOfRangeException.ThrowIfNegative(spec.LagSeconds, nameof(spec));

        Spec = spec;
        Value = AddOutput<double>("Value");
        Health = AddOutput<InstrumentHealth>("Health");
    }

    public InstrumentSpec Spec { get; }

    /// <summary>The reading, in <see cref="InstrumentSpec.Unit"/>, clamped to the range.</summary>
    public OutputPort<double> Value { get; }

    public OutputPort<InstrumentHealth> Health { get; }

    public IReadOnlyList<FaultDescriptor> SupportedFaults => InstrumentFaults.All;

    public sealed override void Initialize(in InitContext ctx)
    {
        _random = ctx.Random;
        _truthTelemetry = ctx.RegisterTelemetry("Truth", Spec.Unit);
        OnInitialize(in ctx);
    }

    /// <summary>Subclass initialisation: register telemetry, capture dt.</summary>
    protected virtual void OnInitialize(in InitContext ctx)
    {
    }

    /// <summary>The true value of the measured quantity this tick.</summary>
    protected abstract double Measure(in TickContext ctx);

    public sealed override void Evaluate(in TickContext ctx)
    {
        double truth = Measure(in ctx);
        _truthTelemetry.Write(truth);

        double reading = (truth * _gain) + _offset;
        _drift += _driftRate * ctx.Dt;
        reading += _drift;

        double sigma = _noiseOverride ?? Spec.NoiseSigma;
        if (sigma > 0.0)
        {
            reading += sigma * (_random ?? throw new InvalidOperationException(
                $"Instrument '{Id}' was not initialised; call Initialize before Evaluate.")).NextGaussian();
        }

        double lag = _lagOverride ?? Spec.LagSeconds;
        if (!_primed || lag <= 0.0)
        {
            _filtered = reading;
            _primed = true;
        }
        else
        {
            _filtered += (reading - _filtered) * Math.Min(1.0, ctx.Dt / lag);
        }

        double output = _frozen ? _lastOutput : _filtered;
        InstrumentHealth health = InstrumentHealth.Good;
        if (_failHigh)
        {
            output = Spec.RangeHigh;
            health = InstrumentHealth.Bad;
        }
        else if (_failLow)
        {
            output = Spec.RangeLow;
            health = InstrumentHealth.Bad;
        }

        output = Math.Clamp(output, Spec.RangeLow, Spec.RangeHigh);
        Value.Value = output;
        Health.Value = health;
        _lastOutput = output;
        OnEvaluated(output, in ctx);
    }

    /// <summary>Called after the reading is published; discrete instruments derive their switch state here.</summary>
    protected virtual void OnEvaluated(double reading, in TickContext ctx)
    {
    }

    public void ApplyFault(string faultId, FaultArguments arguments)
    {
        switch (faultId)
        {
            case InstrumentFaults.Calibration:
                _gain = arguments.Get("gain");
                _offset = arguments.Get("offset");
                break;
            case InstrumentFaults.Noise:
                _noiseOverride = Math.Max(0.0, arguments.Get("sigma"));
                break;
            case InstrumentFaults.Drift:
                _driftRate = arguments.Get("rate");
                break;
            case InstrumentFaults.Lag:
                _lagOverride = Math.Max(0.0, arguments.Get("seconds"));
                break;
            case InstrumentFaults.Freeze:
                _frozen = true;
                break;
            case InstrumentFaults.FailHigh:
                _failHigh = true;
                break;
            case InstrumentFaults.FailLow:
                _failLow = true;
                break;
            default:
                throw new ArgumentException($"'{Id}' supports no fault '{faultId}'.", nameof(faultId));
        }
    }

    public void ClearFault(string faultId)
    {
        switch (faultId)
        {
            case InstrumentFaults.Calibration:
                _gain = 1.0;
                _offset = 0.0;
                break;
            case InstrumentFaults.Noise:
                _noiseOverride = null;
                break;
            case InstrumentFaults.Drift:
                _driftRate = 0.0;
                _drift = 0.0;
                break;
            case InstrumentFaults.Lag:
                _lagOverride = null;
                break;
            case InstrumentFaults.Freeze:
                _frozen = false;
                break;
            case InstrumentFaults.FailHigh:
                _failHigh = false;
                break;
            case InstrumentFaults.FailLow:
                _failLow = false;
                break;
        }
    }
}
```

The `OnEvaluated` hook is what `ZeroSpeedSwitch` (Task 11) uses to derive a
discrete output from the *faulted* reading, so a frozen or failed switch lies
the same way its analog value does.

- [ ] **Step 4: Run the tests**

Run: `dotnet test tests/Dse.Components.Tests`
Expected: 50 pass.

- [ ] **Step 5: Commit**

```bash
git add src/Dse.Components/Instruments tests/Dse.Components.Tests/Fakes/ProbeInstrument.cs tests/Dse.Components.Tests/InstrumentBaseTests.cs
git commit -m "feat(components): add the instrument base with the shared sensor-fault vocabulary"
```

---

### Task 11: Signal instruments

**Files:**
- Create: `src/Dse.Components/Instruments/SpeedSensor.cs`
- Create: `src/Dse.Components/Instruments/CurrentSensor.cs`
- Create: `src/Dse.Components/Instruments/TemperatureSensor.cs`
- Create: `src/Dse.Components/Instruments/ZeroSpeedSwitch.cs`
- Test: `tests/Dse.Components.Tests/SignalInstrumentTests.cs`

**Interfaces:**
- Consumes: `InstrumentBase`, `InstrumentSpec` (Task 10).
- Produces:
  - `SpeedSensor(string id, InstrumentSpec spec)` — `InputPort<double> Speed` (m/s); measures it.
  - `CurrentSensor(string id, InstrumentSpec spec)` — `InputPort<double> Current` (A).
  - `TemperatureSensor(string id, InstrumentSpec spec)` — `InputPort<double> Temperature` (°C).
  - `ZeroSpeedSwitch(string id, InstrumentSpec spec, double thresholdSpeed, double delaySeconds)`
    — `InputPort<double> Speed`; `OutputPort<bool> Stopped` true once the
    *reading* has been below the threshold for the delay. Events
    `ZERO_SPEED` / `MOTION` on edges.

Each sensor is a thin `Measure`; the value of the task is proving that the
same fault vocabulary works on every one and that the switch derives its
discrete state from the corrupted reading, not the truth.

- [ ] **Step 1: Write the failing tests**

`tests/Dse.Components.Tests/SignalInstrumentTests.cs`:

```csharp
using Dse.Components.Instruments;
using Dse.Components.Tests.Fakes;
using Dse.Core;
using Dse.Core.Faults;
using Dse.Core.Time;
using Xunit;

namespace Dse.Components.Tests;

public class SignalInstrumentTests
{
    private static SimulationOptions Options() => new()
    {
        Seed = 1UL,
        StartTime = new DateTimeOffset(2026, 1, 1, 6, 0, 0, TimeSpan.Zero),
        TimeStep = TimeSpan.FromMilliseconds(100),
    };

    [Fact]
    public void EachSensorMeasuresItsInputAndCanBeLiedTo()
    {
        var speed = new SpeedSensor("ST", new InstrumentSpec("m/s", 0.0, 5.0));
        var current = new CurrentSensor("IT", new InstrumentSpec("A", 0.0, 50.0));
        var temperature = new TemperatureSensor("TT", new InstrumentSpec("degC", -20.0, 400.0));
        var v = new Setpoint("V", 1.8);
        var i = new Setpoint("I", 12.5);
        var t = new Setpoint("T", 180.0);
        v.Out.ConnectTo(speed.Speed);
        i.Out.ConnectTo(current.Current);
        t.Out.ConnectTo(temperature.Temperature);
        Simulation sim = new SimulationBuilder(Options())
            .Add(speed).Add(current).Add(temperature).Add(v).Add(i).Add(t).Build();

        sim.Tick();
        Assert.Equal((1.8, 12.5, 180.0), (speed.Value.Value, current.Value.Value, temperature.Value.Value));

        sim.InjectFaultIn(TimeSpan.Zero, "ST", InstrumentFaults.FailHigh);
        sim.InjectFaultIn(TimeSpan.Zero, "IT", InstrumentFaults.Calibration, new FaultArguments(new("gain", 2.0)));
        sim.InjectFaultIn(TimeSpan.Zero, "TT", InstrumentFaults.Freeze);
        t.Value = 250.0;
        sim.Tick();

        Assert.Equal((5.0, InstrumentHealth.Bad), (speed.Value.Value, speed.Health.Value));
        Assert.Equal(25.0, current.Value.Value);
        Assert.Equal(180.0, temperature.Value.Value);
        Assert.Equal(250.0, sim.Telemetry.Read("TT.Truth"));
    }

    [Fact]
    public void TheZeroSpeedSwitchTripsAfterTheDelayAndResetsOnMotion()
    {
        var zss = new ZeroSpeedSwitch("ZSS", new InstrumentSpec("m/s", 0.0, 5.0), thresholdSpeed: 0.1, delaySeconds: 0.5);
        var v = new Setpoint("V", 1.5);
        v.Out.ConnectTo(zss.Speed);
        Simulation sim = new SimulationBuilder(Options()).Add(zss).Add(v).Build();

        sim.RunFor(TimeSpan.FromSeconds(1));
        Assert.False(zss.Stopped.Value);

        v.Value = 0.0;
        sim.RunFor(TimeSpan.FromMilliseconds(400));
        Assert.False(zss.Stopped.Value);
        sim.RunFor(TimeSpan.FromMilliseconds(200));
        Assert.True(zss.Stopped.Value);
        Assert.Single(sim.Events.Records, r => r.Code == "ZERO_SPEED");

        v.Value = 1.0;
        sim.Tick();
        Assert.False(zss.Stopped.Value);
        Assert.Single(sim.Events.Records, r => r.Code == "MOTION");
    }

    [Fact]
    public void AFrozenZeroSpeedSwitchNeverNoticesTheBeltStop()
    {
        var zss = new ZeroSpeedSwitch("ZSS", new InstrumentSpec("m/s", 0.0, 5.0), 0.1, 0.5);
        var v = new Setpoint("V", 1.5);
        v.Out.ConnectTo(zss.Speed);
        Simulation sim = new SimulationBuilder(Options()).Add(zss).Add(v).Build();
        sim.RunFor(TimeSpan.FromSeconds(1));

        sim.InjectFaultIn(TimeSpan.Zero, "ZSS", InstrumentFaults.Freeze);
        v.Value = 0.0;
        sim.RunFor(TimeSpan.FromSeconds(5));

        Assert.False(zss.Stopped.Value);
        Assert.Equal(1.5, zss.Value.Value);
        Assert.Equal(0.0, sim.Telemetry.Read("ZSS.Truth"));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Dse.Components.Tests --filter "FullyQualifiedName~SignalInstrumentTests"`
Expected: build error — `SpeedSensor` does not exist.

- [ ] **Step 3: Implement**

`src/Dse.Components/Instruments/SpeedSensor.cs`:

```csharp
using Dse.Core.Contexts;
using Dse.Core.Graph;

namespace Dse.Components.Instruments;

/// <summary>A tachometer or encoder on a pulley: reports the speed on its input, m/s.</summary>
public sealed class SpeedSensor : InstrumentBase
{
    public SpeedSensor(string id, InstrumentSpec spec)
        : base(id, spec) => Speed = AddInput<double>("Speed");

    /// <summary>True speed, m/s.</summary>
    public InputPort<double> Speed { get; }

    protected override double Measure(in TickContext ctx) => Speed.Value;
}
```

`src/Dse.Components/Instruments/CurrentSensor.cs`:

```csharp
using Dse.Core.Contexts;
using Dse.Core.Graph;

namespace Dse.Components.Instruments;

/// <summary>A current transformer on a motor feed: reports the current on its input, A.</summary>
public sealed class CurrentSensor : InstrumentBase
{
    public CurrentSensor(string id, InstrumentSpec spec)
        : base(id, spec) => Current = AddInput<double>("Current");

    /// <summary>True current, A.</summary>
    public InputPort<double> Current { get; }

    protected override double Measure(in TickContext ctx) => Current.Value;
}
```

`src/Dse.Components/Instruments/TemperatureSensor.cs`:

```csharp
using Dse.Core.Contexts;
using Dse.Core.Graph;

namespace Dse.Components.Instruments;

/// <summary>A thermocouple or RTD on a signal: reports the temperature on its input, °C.</summary>
public sealed class TemperatureSensor : InstrumentBase
{
    public TemperatureSensor(string id, InstrumentSpec spec)
        : base(id, spec) => Temperature = AddInput<double>("Temperature");

    /// <summary>True temperature, °C.</summary>
    public InputPort<double> Temperature { get; }

    protected override double Measure(in TickContext ctx) => Temperature.Value;
}
```

`src/Dse.Components/Instruments/ZeroSpeedSwitch.cs`:

```csharp
using System.Globalization;
using Dse.Core.Contexts;
using Dse.Core.Graph;

namespace Dse.Components.Instruments;

/// <summary>
/// A speed switch: <see cref="Stopped"/> goes true once the *reading* has
/// been below the threshold for the delay, and false the moment it is not.
/// Derived from the faulted reading, so a frozen switch never notices a stop.
/// </summary>
public sealed class ZeroSpeedSwitch : InstrumentBase
{
    private double _belowFor;
    private bool _stopped;

    public ZeroSpeedSwitch(string id, InstrumentSpec spec, double thresholdSpeed, double delaySeconds)
        : base(id, spec)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(thresholdSpeed);
        ArgumentOutOfRangeException.ThrowIfNegative(delaySeconds);
        ThresholdSpeed = thresholdSpeed;
        DelaySeconds = delaySeconds;

        Speed = AddInput<double>("Speed");
        Stopped = AddOutput<bool>("Stopped");
    }

    /// <summary>True speed, m/s.</summary>
    public InputPort<double> Speed { get; }

    public OutputPort<bool> Stopped { get; }

    /// <summary>m/s.</summary>
    public double ThresholdSpeed { get; }

    /// <summary>s the reading must stay below the threshold.</summary>
    public double DelaySeconds { get; }

    protected override double Measure(in TickContext ctx) => Speed.Value;

    protected override void OnEvaluated(double reading, in TickContext ctx)
    {
        bool below = reading < ThresholdSpeed;
        _belowFor = below ? _belowFor + ctx.Dt : 0.0;
        bool stopped = below && _belowFor >= DelaySeconds - 1e-12;

        if (stopped != _stopped)
        {
            ctx.Log(
                Id,
                stopped ? "ZERO_SPEED" : "MOTION",
                stopped
                    ? string.Create(CultureInfo.InvariantCulture, $"Speed below {ThresholdSpeed} m/s for {DelaySeconds} s.")
                    : "Motion detected.");
        }

        _stopped = stopped;
        Stopped.Value = stopped;
    }
}
```

- [ ] **Step 4: Run the tests**

Run: `dotnet test tests/Dse.Components.Tests`
Expected: 53 pass. In the delay test, with dt 0.1 the reading is below the
threshold from the first tick after the setpoint change; `_belowFor` reaches
0.5 on the fifth such tick, so 400 ms (4 ticks) is not enough and 600 ms is.

- [ ] **Step 5: Commit**

```bash
git add src/Dse.Components/Instruments tests/Dse.Components.Tests/SignalInstrumentTests.cs
git commit -m "feat(components): add speed, current and temperature sensors and the zero-speed switch"
```

---

### Task 12: Material instruments — belt scale, pyrometer, part counter

**Files:**
- Create: `src/Dse.Components/Instruments/BeltScale.cs`
- Create: `src/Dse.Components/Instruments/Pyrometer.cs`
- Create: `src/Dse.Components/Instruments/PartCounter.cs`
- Test: `tests/Dse.Components.Tests/MaterialInstrumentTests.cs`

**Interfaces:**
- Consumes: `IMaterialObservable`, `MaterialObservation` (Task 2), `InstrumentBase`,
  `BulkBelt`, `DiscreteBelt`, sources and sinks.
- Produces:
  - `BeltScale(string id, IMaterialObservable belt, double positionM, InstrumentSpec spec)`
    — `InputPort<double> Speed` (m/s); measures `LinearDensity × Speed × 3.6`
    (t/h). Use `spec.LagSeconds` as the integration window.
  - `Pyrometer(string id, IMaterialObservable target, double positionM, double windowM, InstrumentSpec spec)`
    — `InputPort<double> Background` (°C, default 20); measures the observed
    material's temperature, or the background when nothing is there.
  - `PartCounter(string id, IMaterialObservable belt, double positionM, double windowM)`
    : `ComponentBase, IFaultTarget` — `OutputPort<long> Count`,
    `OutputPort<bool> Present`; counts each new item id seen in the window.
    Fault `"blinded"`: sees nothing. Telemetry `Count`.

Spec 7.6 for the scale; spec 15.2 for the pyrometer; spec 17 for the counter.
All three read the node they are mounted on through `IMaterialObservable` in
`Evaluate`, which is safe because material moves only in phase 3.

- [ ] **Step 1: Write the failing tests**

`tests/Dse.Components.Tests/MaterialInstrumentTests.cs`:

```csharp
using Dse.Components.Flow;
using Dse.Components.Instruments;
using Dse.Components.Tests.Fakes;
using Dse.Core;
using Dse.Core.Flow;
using Dse.Core.Time;
using Xunit;

namespace Dse.Components.Tests;

public class MaterialInstrumentTests
{
    private static readonly MaterialType Ore = new("Ore", PayloadKind.Bulk);
    private static readonly MaterialType Billet = new("Billet", PayloadKind.Discrete);

    private static SimulationOptions Options() => new()
    {
        Seed = 1UL,
        StartTime = new DateTimeOffset(2026, 1, 1, 6, 0, 0, TimeSpan.Zero),
        TimeStep = TimeSpan.FromSeconds(0.5),
    };

    [Fact]
    public void TheBeltScaleReadsTonnesPerHourFromDensityAndSpeed()
    {
        var feed = new BulkSource("Feed", Ore, 2.0);
        var belt = new BulkBelt("CV", length: 5.0, cellSize: 0.5, maxSpeed: 1.0, maxLinearDensity: 10.0);
        var pile = new BulkSink("Pile");
        var speed = new Setpoint("SP", 1.0);
        var scale = new BeltScale("WT", belt, positionM: 2.5, new InstrumentSpec("t/h", 0.0, 100.0));
        feed.Out.ConnectTo(belt.In);
        belt.Out.ConnectTo(pile.In);
        speed.Out.ConnectTo(belt.Speed);
        speed.Out.ConnectTo(scale.Speed);
        Simulation sim = new SimulationBuilder(Options()).Add(pile).Add(belt).Add(feed).Add(speed).Add(scale).Build();

        sim.RunFor(TimeSpan.FromSeconds(15));

        // 1 kg per 0.5 m cell = 2 kg/m × 1 m/s × 3.6 = 7.2 t/h, the feed rate.
        Assert.Equal(7.2, scale.Value.Value, 6);
        Assert.Equal(7.2, sim.Telemetry.Read("WT.Truth"), 6);

        speed.Value = 0.0;
        sim.Tick();
        Assert.Equal(0.0, scale.Value.Value, 9);
    }

    [Fact]
    public void ThePyrometerReadsTheItemInFrontOfItOrTheBackground()
    {
        var source = new ItemSource("Billets", Billet, 20.0, 2.0, new MaterialProperties(7800.0, 0.0, 950.0));
        var belt = new DiscreteBelt("CV", length: 4.0, maxSpeed: 1.0);
        var sink = new ItemSink("Out");
        var speed = new Setpoint("SP", 1.0);
        var pyro = new Pyrometer("TT", belt, positionM: 2.0, windowM: 0.3, new InstrumentSpec("degC", 0.0, 1500.0));
        source.Out.ConnectTo(belt.In);
        belt.Out.ConnectTo(sink.In);
        speed.Out.ConnectTo(belt.Speed);
        Simulation sim = new SimulationBuilder(Options()).Add(sink).Add(belt).Add(source).Add(speed).Add(pyro).Build();

        var readings = new List<double>();
        for (int i = 0; i < 40; i++)
        {
            sim.Tick();
            readings.Add(pyro.Value.Value);
        }

        Assert.Contains(950.0, readings);
        Assert.Contains(20.0, readings);
        Assert.All(readings, r => Assert.True(r == 950.0 || r == 20.0));
    }

    [Fact]
    public void ThePartCounterCountsEachItemOnceAndCanBeBlinded()
    {
        var source = new ItemSource("Billets", Billet, 20.0, 1.0);
        var belt = new DiscreteBelt("CV", length: 4.0, maxSpeed: 1.0);
        var sink = new ItemSink("Out");
        var speed = new Setpoint("SP", 1.0);
        var counter = new PartCounter("PC", belt, positionM: 3.0, windowM: 0.3);
        source.Out.ConnectTo(belt.In);
        belt.Out.ConnectTo(sink.In);
        speed.Out.ConnectTo(belt.Speed);
        Simulation sim = new SimulationBuilder(Options()).Add(sink).Add(belt).Add(source).Add(speed).Add(counter).Build();

        sim.RunFor(TimeSpan.FromSeconds(20));
        long seen = counter.Count.Value;
        Assert.InRange(seen, 13L, 16L);
        Assert.Equal((double)seen, sim.Telemetry.Read("PC.Count"));

        sim.InjectFaultIn(TimeSpan.Zero, "PC", PartCounter.Blinded);
        sim.RunFor(TimeSpan.FromSeconds(5));
        Assert.Equal(seen, counter.Count.Value);
        Assert.False(counter.Present.Value);
    }

    [Fact]
    public void AStoppedItemInTheWindowIsCountedOnce()
    {
        var source = new ItemSource("Billets", Billet, 20.0, 1.0);
        var belt = new DiscreteBelt("CV", length: 4.0, maxSpeed: 1.0);
        var sink = new ItemSink("Out", capacity: 0);
        var speed = new Setpoint("SP", 1.0);
        var counter = new PartCounter("PC", belt, positionM: 4.0, windowM: 0.1);   // at the head, where items queue
        source.Out.ConnectTo(belt.In);
        belt.Out.ConnectTo(sink.In);
        speed.Out.ConnectTo(belt.Speed);
        Simulation sim = new SimulationBuilder(Options()).Add(sink).Add(belt).Add(source).Add(speed).Add(counter).Build();

        sim.RunFor(TimeSpan.FromSeconds(30));

        Assert.Equal(1L, counter.Count.Value);
        Assert.True(counter.Present.Value);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Dse.Components.Tests --filter "FullyQualifiedName~MaterialInstrumentTests"`
Expected: build error — `BeltScale` does not exist.

- [ ] **Step 3: Implement**

`src/Dse.Components/Instruments/BeltScale.cs`:

```csharp
using Dse.Core.Contexts;
using Dse.Core.Flow;
using Dse.Core.Graph;

namespace Dse.Components.Instruments;

/// <summary>
/// A belt weigher: linear density under the weigh idler times belt speed,
/// reported in t/h. Mounted on a bulk belt at a position from its tail; reads
/// the belt's material in phase 2, when nothing moves. The datasheet lag is
/// the scale's integration window.
/// </summary>
public sealed class BeltScale : InstrumentBase
{
    private const double KgPerSecondToTonnesPerHour = 3.6;

    private readonly IMaterialObservable _belt;

    public BeltScale(string id, IMaterialObservable belt, double positionM, InstrumentSpec spec)
        : base(id, spec)
    {
        ArgumentNullException.ThrowIfNull(belt);
        ArgumentOutOfRangeException.ThrowIfNegative(positionM);
        _belt = belt;
        PositionM = positionM;
        Speed = AddInput<double>("Speed");
    }

    /// <summary>Belt speed at the weigh idler, m/s.</summary>
    public InputPort<double> Speed { get; }

    /// <summary>Metres from the belt's tail.</summary>
    public double PositionM { get; }

    protected override double Measure(in TickContext ctx) =>
        _belt.TryObserve(PositionM, 0.0, out MaterialObservation seen)
            ? seen.LinearDensity * Speed.Value * KgPerSecondToTonnesPerHour
            : 0.0;
}
```

`src/Dse.Components/Instruments/Pyrometer.cs`:

```csharp
using Dse.Core.Contexts;
using Dse.Core.Flow;
using Dse.Core.Graph;

namespace Dse.Components.Instruments;

/// <summary>
/// A non-contact temperature sensor aimed at one point on a node. Reads the
/// temperature of whatever material is within its window, and the background
/// when nothing is.
/// </summary>
public sealed class Pyrometer : InstrumentBase
{
    private readonly IMaterialObservable _target;

    public Pyrometer(string id, IMaterialObservable target, double positionM, double windowM, InstrumentSpec spec)
        : base(id, spec)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentOutOfRangeException.ThrowIfNegative(positionM);
        ArgumentOutOfRangeException.ThrowIfNegative(windowM);
        _target = target;
        PositionM = positionM;
        WindowM = windowM;
        Background = AddInput<double>("Background", defaultValue: 20.0);
    }

    /// <summary>What the sensor sees when no material is in view, °C. Unconnected reads 20 °C.</summary>
    public InputPort<double> Background { get; }

    public double PositionM { get; }

    public double WindowM { get; }

    protected override double Measure(in TickContext ctx) =>
        _target.TryObserve(PositionM, WindowM, out MaterialObservation seen)
            ? seen.Properties.Temperature
            : Background.Value;
}
```

`src/Dse.Components/Instruments/PartCounter.cs`:

```csharp
using Dse.Core.Contexts;
using Dse.Core.Faults;
using Dse.Core.Flow;
using Dse.Core.Graph;
using Dse.Core.Telemetry;

namespace Dse.Components.Instruments;

/// <summary>
/// A photo-eye across a discrete belt. Counts every new item id it sees in
/// its window; an item that stops in front of it is counted once. Two items
/// passing through the window within one tick are one count — exactly what
/// a real photo-eye misses.
/// </summary>
public sealed class PartCounter : ComponentBase, IFaultTarget
{
    /// <summary>The eye is obscured: it sees nothing until cleared.</summary>
    public const string Blinded = "blinded";

    private static readonly FaultDescriptor[] Faults =
    [
        new(Blinded, "The photo-eye is obscured and sees nothing until the fault is cleared."),
    ];

    private readonly IMaterialObservable _belt;
    private long _count;
    private long _lastId;
    private bool _blinded;
    private TelemetryHandle _countTelemetry;

    public PartCounter(string id, IMaterialObservable belt, double positionM, double windowM)
        : base(id)
    {
        ArgumentNullException.ThrowIfNull(belt);
        ArgumentOutOfRangeException.ThrowIfNegative(positionM);
        ArgumentOutOfRangeException.ThrowIfNegative(windowM);
        _belt = belt;
        PositionM = positionM;
        WindowM = windowM;
        Count = AddOutput<long>("Count");
        Present = AddOutput<bool>("Present");
    }

    public OutputPort<long> Count { get; }

    /// <summary>True while an item is in the window.</summary>
    public OutputPort<bool> Present { get; }

    public double PositionM { get; }

    public double WindowM { get; }

    public IReadOnlyList<FaultDescriptor> SupportedFaults => Faults;

    public override void Initialize(in InitContext ctx) =>
        _countTelemetry = ctx.RegisterTelemetry("Count", "count");

    public override void Evaluate(in TickContext ctx)
    {
        MaterialObservation seen = default;
        bool present = !_blinded && _belt.TryObserve(PositionM, WindowM, out seen);
        if (present && seen.ItemId != _lastId)
        {
            _count++;
            _lastId = seen.ItemId;
        }

        Present.Value = present;
        Count.Value = _count;
        _countTelemetry.Write(_count);
    }

    public void ApplyFault(string faultId, FaultArguments arguments) => _blinded = true;

    public void ClearFault(string faultId) => _blinded = false;
}
```

- [ ] **Step 4: Run the tests**

Run: `dotnet test tests/Dse.Components.Tests`
Expected: 57 pass.

- [ ] **Step 5: Commit**

```bash
git add src/Dse.Components/Instruments tests/Dse.Components.Tests/MaterialInstrumentTests.cs
git commit -m "feat(components): add the belt scale, pyrometer and part counter mounted on flow nodes"
```

---

### Task 13: Motor with the I²t thermal model

**Files:**
- Create: `src/Dse.Components/Mechanical/MotorRating.cs`
- Create: `src/Dse.Components/Mechanical/Motor.cs`
- Test: `tests/Dse.Components.Tests/MotorTests.cs`

**Interfaces:**
- Consumes: `ComponentBase`, latched inputs (Task 1), `IFaultTarget` (Task 3).
- Produces:
  - `sealed record MotorRating(double RatedPowerW, double RatedSpeedRadPerS, double RatedCurrentA, double NoLoadCurrentFraction = 0.3, double LockedRotorCurrentMultiple = 6.0, double BreakdownTorqueMultiple = 2.5, double AccelerationTimeConstantS = 1.0, double CoastTimeConstantS = 3.0, double ThermalTimeConstantS = 60.0, double SpeedDroopFraction = 0.03)`
    with `double RatedTorque => RatedPowerW / RatedSpeedRadPerS`.
  - `Motor(string id, MotorRating rating)` : `ComponentBase, IFaultTarget`.
    Ports: `InputPort<bool> Energised` (default false),
    `InputPort<double> TorqueDemand` (N·m, **latched**),
    `OutputPort<double> Speed` (rad/s), `OutputPort<double> Torque` (N·m delivered),
    `OutputPort<double> Current` (A), `OutputPort<double> ThermalState`
    (1.0 = equilibrium at rated current), `OutputPort<bool> AtSpeed`.
    Telemetry `Speed`, `Current`, `ThermalState`. Events `ENERGISED`,
    `DE_ENERGISED`, `AT_SPEED`, `STALLED`, `STOPPED`. Faults
    `"bearing-friction"` (`torque` N·m, default 0), `"thermal-bias"` (`amount`,
    default 0.5, one-shot added to the thermal state).

Spec 8.3: torque demand gives current; current drives an I²t thermal state;
"inject motor overload" is a bias on the thermal state or the load, and the
trip that follows (Task 16, in the starter) is the same trip accumulation
would cause. The model, per tick, with `Tr = RatedTorque`, `ωr = RatedSpeed`,
`Ir = RatedCurrent`:

```
demand   = TorqueDemand + frictionFault                      (N·m; TorqueDemand is last tick's)
load     = min(demand / Tr, Breakdown)
if energised:
    stalled = demand > Breakdown × Tr
    target  = stalled ? 0 : ωr × (1 − Droop × load)
    ω      += (target − ω) × min(1, dt / τaccel)
    running = Ir × (NoLoad + (1 − NoLoad) × load)
    start   = target > 0 ? max(0, 1 − ω / target) : 1        (1 at standstill or stall, 0 at speed)
    I       = max(running, LockedRotor × Ir × start)
    Torque  = stalled ? 0 : demand
else:
    ω      += (0 − ω) × min(1, dt / τcoast);  I = 0;  Torque = 0
θ        += ((I / Ir)² − θ) × min(1, dt / τthermal)           first-order toward (I/Ir)²
```

At rated current θ tends to 1 and never crosses it; at 1.2× rated it tends to
1.44 and crosses any trip level below that on a curve — a Class 10-shaped
overload characteristic with one time constant.

- [ ] **Step 1: Write the failing tests**

`tests/Dse.Components.Tests/MotorTests.cs`:

```csharp
using Dse.Components.Mechanical;
using Dse.Components.Tests.Fakes;
using Dse.Core.Faults;
using Dse.Core.Graph;
using Dse.Core.Logging;
using Xunit;

namespace Dse.Components.Tests;

public class MotorTests
{
    // 750 W at 150 rad/s: rated torque 5 N·m, rated current 2 A.
    private static readonly MotorRating Rating = new(750.0, 150.0, 2.0);

    private sealed class Rig
    {
        public Rig(double dt = 0.1)
        {
            Dt = dt;
            Motor = new Motor("M", Rating);
            Motor.Initialize(TestContexts.Init(Motor.Id, dt: dt));
            Energised = new OutputPort<bool>("Out", "Run");
            Energised.ConnectTo(Motor.Energised);
            Demand = new OutputPort<double>("Out", "Load");
            Demand.ConnectTo(Motor.TorqueDemand);
        }

        public double Dt { get; }

        public Motor Motor { get; }

        public OutputPort<bool> Energised { get; }

        public OutputPort<double> Demand { get; }

        public EventLog Log { get; } = new();

        public int Tick { get; private set; }

        public void Run(double seconds)
        {
            int ticks = (int)Math.Round(seconds / Dt);
            for (int i = 0; i < ticks; i++)
            {
                Motor.Evaluate(TestContexts.Tick(Tick++, Dt, Log));
                Motor.Latch();
            }
        }

        public IEnumerable<string> Codes => Log.Records.Select(r => r.Code);
    }

    [Fact]
    public void RatingDerivesTorque()
    {
        Assert.Equal(5.0, Rating.RatedTorque, 9);
    }

    [Fact]
    public void AnUnloadedMotorReachesRatedSpeedAndDrawsNoLoadCurrent()
    {
        var rig = new Rig();
        rig.Energised.Value = true;

        rig.Run(10.0);

        Assert.Equal(150.0, rig.Motor.Speed.Value, 1);
        Assert.Equal(0.6, rig.Motor.Current.Value, 2);
        Assert.True(rig.Motor.AtSpeed.Value);
        Assert.Equal(["ENERGISED", "AT_SPEED"], rig.Codes);
    }

    [Fact]
    public void StartingCurrentIsLockedRotorAndDecaysWithSpeed()
    {
        var rig = new Rig(dt: 0.01);
        rig.Energised.Value = true;

        rig.Run(0.01);
        Assert.InRange(rig.Motor.Current.Value, 11.5, 12.0);   // 6 × 2 A at standstill
        rig.Run(3.0);
        Assert.True(rig.Motor.Current.Value < 1.0);
    }

    [Fact]
    public void RatedTorqueDrawsRatedCurrentAndSettlesTheThermalStateAtOne()
    {
        var rig = new Rig();
        rig.Energised.Value = true;
        rig.Demand.Value = 5.0;

        rig.Run(600.0);   // ten thermal time constants

        Assert.Equal(2.0, rig.Motor.Current.Value, 2);
        Assert.Equal(145.5, rig.Motor.Speed.Value, 1);   // 3 % droop
        Assert.Equal(1.0, rig.Motor.ThermalState.Value, 2);
        Assert.Equal(5.0, rig.Motor.Torque.Value, 9);
    }

    [Fact]
    public void TwiceRatedTorqueCrossesTheTripLevelOnACurve()
    {
        var rig = new Rig();
        rig.Energised.Value = true;
        rig.Run(5.0);
        rig.Demand.Value = 10.0;   // I = 2 × (0.3 + 0.7 × 2) = 3.4 A; (I/Ir)² = 2.89

        double crossedAt = double.NaN;
        for (double t = 0.0; t < 120.0; t += rig.Dt)
        {
            rig.Run(rig.Dt);
            if (rig.Motor.ThermalState.Value >= 1.1)
            {
                crossedAt = t;
                break;
            }
        }

        // The direct-on-line start (12 A decaying over ~3 s) leaves θ ≈ 0.3; from there
        // θ → 2.89 with τ = 60 s crosses 1.1 near 60 × ln((2.89 − 0.3) / (2.89 − 1.1)) ≈ 23 s.
        Assert.InRange(crossedAt, 18.0, 30.0);
        Assert.Equal(3.4, rig.Motor.Current.Value, 2);
    }

    [Fact]
    public void BeyondBreakdownTorqueTheMotorStallsAtLockedRotorCurrent()
    {
        var rig = new Rig();
        rig.Energised.Value = true;
        rig.Run(5.0);
        rig.Demand.Value = 13.0;   // 2.6 × rated > 2.5 breakdown

        rig.Run(10.0);

        Assert.True(rig.Motor.Speed.Value < 1.0);
        Assert.Equal(12.0, rig.Motor.Current.Value, 1);
        Assert.Equal(0.0, rig.Motor.Torque.Value);
        Assert.Contains("STALLED", rig.Codes);
        Assert.False(rig.Motor.AtSpeed.Value);
    }

    [Fact]
    public void DeEnergisingCoastsToAStop()
    {
        var rig = new Rig();
        rig.Energised.Value = true;
        rig.Run(10.0);
        rig.Energised.Value = false;

        rig.Run(3.0);
        Assert.InRange(rig.Motor.Speed.Value, 50.0, 60.0);   // one coast time constant: 150 × e⁻¹ ≈ 55
        Assert.Equal(0.0, rig.Motor.Current.Value);
        rig.Run(30.0);
        Assert.True(rig.Motor.Speed.Value < 0.75);
        Assert.Equal(["ENERGISED", "AT_SPEED", "DE_ENERGISED", "STOPPED"], rig.Codes);
    }

    [Fact]
    public void BearingFrictionRaisesCurrentWithNoExternalLoad()
    {
        var rig = new Rig();
        rig.Energised.Value = true;
        rig.Run(10.0);
        double before = rig.Motor.Current.Value;

        rig.Motor.ApplyFault(Motor.BearingFriction, new FaultArguments(new("torque", 2.5)));
        rig.Run(10.0);

        Assert.Equal(1.3, rig.Motor.Current.Value, 2);   // 2 × (0.3 + 0.7 × 0.5)
        Assert.True(rig.Motor.Current.Value > before);

        rig.Motor.ClearFault(Motor.BearingFriction);
        rig.Run(10.0);
        Assert.Equal(before, rig.Motor.Current.Value, 2);
    }

    [Fact]
    public void AThermalBiasIsAOneShotStepInTheThermalState()
    {
        var rig = new Rig();
        rig.Energised.Value = true;
        rig.Run(10.0);
        double before = rig.Motor.ThermalState.Value;

        rig.Motor.ApplyFault(Motor.ThermalBias, new FaultArguments(new("amount", 0.7)));
        rig.Run(rig.Dt);

        Assert.InRange(rig.Motor.ThermalState.Value, before + 0.65, before + 0.71);
    }

    [Fact]
    public void TorqueDemandIsLatched()
    {
        var motor = new Motor("M", Rating);
        Assert.True(motor.TorqueDemand.IsLatched);
        Assert.False(motor.Energised.IsLatched);
        Assert.Equal(["bearing-friction", "thermal-bias"], motor.SupportedFaults.Select(f => f.Id));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Dse.Components.Tests --filter "FullyQualifiedName~MotorTests"`
Expected: build error — `Dse.Components.Mechanical` does not exist.

- [ ] **Step 3: Implement**

`src/Dse.Components/Mechanical/MotorRating.cs`:

```csharp
namespace Dse.Components.Mechanical;

/// <summary>An induction motor's nameplate plus the handful of constants the model needs.</summary>
/// <param name="RatedPowerW">W.</param>
/// <param name="RatedSpeedRadPerS">rad/s at rated load.</param>
/// <param name="RatedCurrentA">A at rated load.</param>
/// <param name="NoLoadCurrentFraction">Current at zero torque as a fraction of rated.</param>
/// <param name="LockedRotorCurrentMultiple">Starting current as a multiple of rated.</param>
/// <param name="BreakdownTorqueMultiple">Torque above which the motor stalls, as a multiple of rated.</param>
/// <param name="AccelerationTimeConstantS">s; first-order approach to target speed when energised.</param>
/// <param name="CoastTimeConstantS">s; first-order decay to rest when de-energised.</param>
/// <param name="ThermalTimeConstantS">s; the I²t thermal state's time constant.</param>
/// <param name="SpeedDroopFraction">Speed lost at rated torque as a fraction of rated speed.</param>
public sealed record MotorRating(
    double RatedPowerW,
    double RatedSpeedRadPerS,
    double RatedCurrentA,
    double NoLoadCurrentFraction = 0.3,
    double LockedRotorCurrentMultiple = 6.0,
    double BreakdownTorqueMultiple = 2.5,
    double AccelerationTimeConstantS = 1.0,
    double CoastTimeConstantS = 3.0,
    double ThermalTimeConstantS = 60.0,
    double SpeedDroopFraction = 0.03)
{
    /// <summary>N·m at rated power and speed.</summary>
    public double RatedTorque => RatedPowerW / RatedSpeedRadPerS;
}
```

`src/Dse.Components/Mechanical/Motor.cs`:

```csharp
using System.Globalization;
using Dse.Core.Contexts;
using Dse.Core.Faults;
using Dse.Core.Graph;
using Dse.Core.Telemetry;

namespace Dse.Components.Mechanical;

/// <summary>
/// An induction motor as a causal model: torque demand sets current, current
/// heats an I²t thermal state, and the starter's overload relay trips on that
/// state. "Inject motor overload" is a bias on the thermal state or the load;
/// the trip that follows is the same trip that material accumulation causes
/// unaided. The torque input is latched — it is a reflection of the load one
/// tick ago — so a drivetrain needs no explicit delay to be solvable.
/// </summary>
public sealed class Motor : ComponentBase, IFaultTarget
{
    /// <summary>Extra torque the motor must overcome, N·m.</summary>
    public const string BearingFriction = "bearing-friction";

    /// <summary>A one-shot step added to the thermal state.</summary>
    public const string ThermalBias = "thermal-bias";

    private static readonly FaultDescriptor[] Faults =
    [
        new(BearingFriction, "Extra torque the motor must overcome, as a worn bearing adds.",
            new FaultParameter("torque", "N·m", 0.0, "Added to the torque demand while active.")),
        new(ThermalBias, "A step in the thermal state, as a hot start or a blocked fan gives.",
            new FaultParameter("amount", "", 0.5, "Added once, on injection; clearing has no effect, the state decays on its own.")),
    ];

    private readonly MotorRating _rating;
    private double _speed;
    private double _thermal;
    private double _friction;
    private bool _wasEnergised;
    private bool _atSpeed;
    private bool _stalled;
    private bool _stopped = true;
    private TelemetryHandle _speedTelemetry;
    private TelemetryHandle _currentTelemetry;
    private TelemetryHandle _thermalTelemetry;

    public Motor(string id, MotorRating rating)
        : base(id)
    {
        ArgumentNullException.ThrowIfNull(rating);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rating.RatedPowerW, nameof(rating));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rating.RatedSpeedRadPerS, nameof(rating));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rating.RatedCurrentA, nameof(rating));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rating.ThermalTimeConstantS, nameof(rating));
        _rating = rating;

        Energised = AddInput<bool>("Energised");
        TorqueDemand = AddInput<double>("TorqueDemand", latched: true);
        Speed = AddOutput<double>("Speed");
        Torque = AddOutput<double>("Torque");
        Current = AddOutput<double>("Current");
        ThermalState = AddOutput<double>("ThermalState");
        AtSpeed = AddOutput<bool>("AtSpeed");
    }

    /// <summary>Contactor closed. Unconnected reads false.</summary>
    public InputPort<bool> Energised { get; }

    /// <summary>Torque the load reflects onto the shaft, N·m. Latched: read one tick late.</summary>
    public InputPort<double> TorqueDemand { get; }

    /// <summary>Shaft speed, rad/s.</summary>
    public OutputPort<double> Speed { get; }

    /// <summary>Torque delivered, N·m. Zero when stalled or de-energised.</summary>
    public OutputPort<double> Torque { get; }

    /// <summary>Line current, A.</summary>
    public OutputPort<double> Current { get; }

    /// <summary>I²t thermal state; 1.0 is equilibrium at rated current.</summary>
    public OutputPort<double> ThermalState { get; }

    /// <summary>True once within 5 % of the target speed after energising.</summary>
    public OutputPort<bool> AtSpeed { get; }

    public MotorRating Rating => _rating;

    public IReadOnlyList<FaultDescriptor> SupportedFaults => Faults;

    public override void Initialize(in InitContext ctx)
    {
        _speedTelemetry = ctx.RegisterTelemetry("Speed", "rad/s");
        _currentTelemetry = ctx.RegisterTelemetry("Current", "A");
        _thermalTelemetry = ctx.RegisterTelemetry("ThermalState", "");
    }

    public override void Evaluate(in TickContext ctx)
    {
        double dt = ctx.Dt;
        bool energised = Energised.Value;
        double demand = Math.Max(0.0, TorqueDemand.Value) + _friction;
        double ratedTorque = _rating.RatedTorque;
        double load = Math.Min(demand / ratedTorque, _rating.BreakdownTorqueMultiple);
        double ratedCurrent = _rating.RatedCurrentA;

        double current;
        double torque;
        if (energised)
        {
            if (!_wasEnergised)
            {
                ctx.Log(Id, "ENERGISED", "Contactor closed.");
                _atSpeed = false;
                _stopped = false;
            }

            bool stalled = demand > _rating.BreakdownTorqueMultiple * ratedTorque;
            double target = stalled ? 0.0 : _rating.RatedSpeedRadPerS * (1.0 - (_rating.SpeedDroopFraction * load));
            _speed += (target - _speed) * Math.Min(1.0, dt / _rating.AccelerationTimeConstantS);

            double running = ratedCurrent * (_rating.NoLoadCurrentFraction + ((1.0 - _rating.NoLoadCurrentFraction) * load));
            double start = target > 0.0 ? Math.Max(0.0, 1.0 - (_speed / target)) : 1.0;
            current = Math.Max(running, _rating.LockedRotorCurrentMultiple * ratedCurrent * start);
            torque = stalled ? 0.0 : demand;

            if (stalled && !_stalled)
            {
                ctx.Log(Id, "STALLED", string.Create(CultureInfo.InvariantCulture,
                    $"Torque demand {demand} N·m exceeds breakdown torque {_rating.BreakdownTorqueMultiple * ratedTorque} N·m."));
            }

            _stalled = stalled;
            if (!_atSpeed && target > 0.0 && _speed >= 0.95 * target)
            {
                _atSpeed = true;
                ctx.Log(Id, "AT_SPEED", string.Create(CultureInfo.InvariantCulture, $"Reached {_speed} rad/s."));
            }

            if (stalled)
            {
                _atSpeed = false;
            }
        }
        else
        {
            if (_wasEnergised)
            {
                ctx.Log(Id, "DE_ENERGISED", "Contactor open; coasting.");
            }

            _speed += (0.0 - _speed) * Math.Min(1.0, dt / _rating.CoastTimeConstantS);
            current = 0.0;
            torque = 0.0;
            _atSpeed = false;
            _stalled = false;

            if (!_stopped && _speed < 0.005 * _rating.RatedSpeedRadPerS)
            {
                _stopped = true;
                ctx.Log(Id, "STOPPED", "Shaft at rest.");
            }
        }

        double ratio = current / ratedCurrent;
        _thermal += ((ratio * ratio) - _thermal) * Math.Min(1.0, dt / _rating.ThermalTimeConstantS);

        _wasEnergised = energised;
        Speed.Value = _speed;
        Torque.Value = torque;
        Current.Value = current;
        ThermalState.Value = _thermal;
        AtSpeed.Value = _atSpeed;
        _speedTelemetry.Write(_speed);
        _currentTelemetry.Write(current);
        _thermalTelemetry.Write(_thermal);
    }

    public void ApplyFault(string faultId, FaultArguments arguments)
    {
        switch (faultId)
        {
            case BearingFriction:
                _friction = Math.Max(0.0, arguments.Get("torque"));
                break;
            case ThermalBias:
                _thermal += arguments.Get("amount");
                break;
        }
    }

    public void ClearFault(string faultId)
    {
        if (faultId == BearingFriction)
        {
            _friction = 0.0;
        }
    }
}
```

`ThermalBias` is one-shot, so clearing it is a no-op: the state decays on its
own, and the descriptor says so. `IFaultTarget.ClearFault` permits that.

- [ ] **Step 4: Run the tests**

Run: `dotnet test tests/Dse.Components.Tests`
Expected: 67 pass. The thermal-crossing window (18–30 s) is deliberately
wide: the locked-rotor current during the start heats the state to about 0.3
before the demand step, and forward Euler at dt 0.1 is a fraction of a
percent fast. If the crossing lands outside the window, print θ after the
5 s warm-up before touching the model.

- [ ] **Step 5: Commit**

```bash
git add src/Dse.Components/Mechanical tests/Dse.Components.Tests/MotorTests.cs
git commit -m "feat(components): add the motor with speed, current and I²t thermal model"
```

---

### Task 14: Drivetrain — gearbox, pulleys, belt friction and belt geometry

**Files:**
- Create: `src/Dse.Components/Mechanical/Gearbox.cs`
- Create: `src/Dse.Components/Mechanical/DrivePulley.cs`
- Create: `src/Dse.Components/Mechanical/TailPulley.cs`
- Create: `src/Dse.Components/Mechanical/BeltFriction.cs`
- Create: `src/Dse.Components/Mechanical/BeltGeometry.cs`
- Test: `tests/Dse.Components.Tests/DrivetrainTests.cs`

**Interfaces:**
- Consumes: `ComponentBase`, latched inputs, `IFaultTarget`, `Motor` (Task 13),
  `BulkBelt` (R13: no feedthrough).
- Produces:
  - `Gearbox(string id, double ratio, double efficiency = 0.95)` — inputs
    `InputSpeed` (rad/s, live), `OutputTorqueDemand` (N·m, **latched**);
    outputs `OutputSpeed = InputSpeed / ratio`,
    `InputTorqueDemand = OutputTorqueDemand / (ratio × efficiency)`.
  - `DrivePulley(string id, double diameterM, double bearingDragN = 0.0)`
    : `ComponentBase, IFaultTarget` — inputs `ShaftSpeed` (rad/s), `BeltForce`
    (N); outputs `BeltSpeed = ShaftSpeed × r × (1 − slip)` (m/s),
    `TorqueDemand = (BeltForce + bearingDrag + faultDrag) × r` (N·m). Faults
    `"bearing-friction"` (`drag` N) and `"belt-slip"` (`fraction`, default 0.1).
  - `TailPulley(string id, double bearingDragN)` : `ComponentBase, IFaultTarget`
    — output `Drag` (N). Fault `"bearing-friction"` (`drag` N, added).
  - `BeltFriction(string id, double emptyBeltMassKg, double frictionCoefficient = 0.03)`
    — inputs `Load` (kg), `Drag` (N, default 0); output
    `Force = μ × g × (emptyBeltMass + Load) + Drag` (N), `g = 9.80665`.
  - `static class BeltGeometry` — `double MaxLinearDensity(double beltWidthM, double angleOfReposeDeg, double bulkDensityKgM3)`:
    CEMA-style effective width `b = 0.9 × W − 0.05` (m), triangular surcharge
    area `b² × tan(θ) / 4`, times density. Throws when `b ≤ 0`.

Spec 7.6: belt width and angle of repose give a maximum linear density. Spec
7.8: load → torque demand → current. Together with the motor this is the
whole reflected-torque chain; two latched inputs (one here, one on the motor)
are what make it solvable without a `UnitDelay`, and the chain test proves it
validates and settles.

- [ ] **Step 1: Write the failing tests**

`tests/Dse.Components.Tests/DrivetrainTests.cs`:

```csharp
using Dse.Components.Flow;
using Dse.Components.Mechanical;
using Dse.Components.Tests.Fakes;
using Dse.Core;
using Dse.Core.Faults;
using Dse.Core.Flow;
using Dse.Core.Graph;
using Dse.Core.Time;
using Xunit;

namespace Dse.Components.Tests;

public class DrivetrainTests
{
    private static readonly MaterialType Ore = new("Ore", PayloadKind.Bulk);

    private static SimulationOptions Options(double stepSeconds = 0.01) => new()
    {
        Seed = 1UL,
        StartTime = new DateTimeOffset(2026, 1, 1, 6, 0, 0, TimeSpan.Zero),
        TimeStep = TimeSpan.FromSeconds(stepSeconds),
    };

    [Fact]
    public void GearboxScalesSpeedDownAndTorqueUp()
    {
        var gearbox = new Gearbox("GB", ratio: 20.0, efficiency: 0.95);
        var speed = new OutputPort<double>("Out", "W");
        var torque = new OutputPort<double>("Out", "T");
        speed.ConnectTo(gearbox.InputSpeed);
        torque.ConnectTo(gearbox.OutputTorqueDemand);
        speed.Value = 150.0;
        torque.Value = 190.0;

        gearbox.Evaluate(TestContexts.Tick(0));
        Assert.Equal(7.5, gearbox.OutputSpeed.Value, 9);
        Assert.Equal(0.0, gearbox.InputTorqueDemand.Value);   // latched: last tick's demand
        gearbox.Latch();
        gearbox.Evaluate(TestContexts.Tick(1));
        Assert.Equal(10.0, gearbox.InputTorqueDemand.Value, 9);
        Assert.True(gearbox.OutputTorqueDemand.IsLatched);
    }

    [Fact]
    public void DrivePulleyConvertsSpeedAndForceAndCanSlip()
    {
        var pulley = new DrivePulley("DP", diameterM: 0.5, bearingDragN: 10.0);
        var shaft = new OutputPort<double>("Out", "W");
        var force = new OutputPort<double>("Out", "F");
        shaft.ConnectTo(pulley.ShaftSpeed);
        force.ConnectTo(pulley.BeltForce);
        shaft.Value = 8.0;
        force.Value = 190.0;

        pulley.Evaluate(TestContexts.Tick(0));
        Assert.Equal(2.0, pulley.BeltSpeed.Value, 9);
        Assert.Equal(50.0, pulley.TorqueDemand.Value, 9);   // (190 + 10) × 0.25

        pulley.ApplyFault(DrivePulley.BeltSlip, new FaultArguments(new("fraction", 0.25)));
        pulley.ApplyFault(DrivePulley.BearingFriction, new FaultArguments(new("drag", 40.0)));
        pulley.Evaluate(TestContexts.Tick(1));
        Assert.Equal(1.5, pulley.BeltSpeed.Value, 9);
        Assert.Equal(60.0, pulley.TorqueDemand.Value, 9);
    }

    [Fact]
    public void TailPulleyDragAddsToBeltForce()
    {
        var tail = new TailPulley("TP", bearingDragN: 30.0);
        var friction = new BeltFriction("BF", emptyBeltMassKg: 100.0, frictionCoefficient: 0.05);
        var load = new OutputPort<double>("Out", "L");
        load.ConnectTo(friction.Load);
        tail.Drag.ConnectTo(friction.Drag);
        load.Value = 300.0;

        tail.Evaluate(TestContexts.Tick(0));
        friction.Evaluate(TestContexts.Tick(0));
        Assert.Equal((0.05 * 9.80665 * 400.0) + 30.0, friction.Force.Value, 9);

        tail.ApplyFault(TailPulley.BearingFriction, new FaultArguments(new("drag", 20.0)));
        tail.Evaluate(TestContexts.Tick(1));
        friction.Evaluate(TestContexts.Tick(1));
        Assert.Equal((0.05 * 9.80665 * 400.0) + 50.0, friction.Force.Value, 9);
    }

    [Fact]
    public void BeltGeometryGivesAMaximumLinearDensity()
    {
        // b = 0.9 × 0.8 − 0.05 = 0.67 m; area = 0.67² × tan 20° / 4 = 0.04085 m²; × 2000 kg/m³.
        Assert.Equal(81.7, BeltGeometry.MaxLinearDensity(0.8, 20.0, 2000.0), 0);
        Assert.Throws<ArgumentOutOfRangeException>(() => BeltGeometry.MaxLinearDensity(0.05, 20.0, 2000.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => BeltGeometry.MaxLinearDensity(0.8, 0.0, 2000.0));
    }

    [Fact]
    public void TheReflectedTorqueChainValidatesAndLoadsTheMotor()
    {
        // Motor → gearbox → drive pulley → belt speed; belt load → friction → force → pulley torque → gearbox → motor.
        var motor = new Motor("M", new MotorRating(750.0, 150.0, 2.0));
        var gearbox = new Gearbox("GB", 20.0);
        var pulley = new DrivePulley("DP", 0.5);
        var tail = new TailPulley("TP", 20.0);
        var friction = new BeltFriction("BF", 250.0, 0.04);
        var belt = new BulkBelt("Belt", length: 10.0, cellSize: 0.5, maxSpeed: 2.0, maxLinearDensity: 80.0);
        var feed = new BulkSource("Feed", Ore, 40.0);
        var pile = new BulkSink("Pile");
        var run = new Switch("Run", true);

        run.Out.ConnectTo(motor.Energised);
        motor.Speed.ConnectTo(gearbox.InputSpeed);
        gearbox.OutputSpeed.ConnectTo(pulley.ShaftSpeed);
        pulley.BeltSpeed.ConnectTo(belt.Speed);
        belt.Load.ConnectTo(friction.Load);
        tail.Drag.ConnectTo(friction.Drag);
        friction.Force.ConnectTo(pulley.BeltForce);
        pulley.TorqueDemand.ConnectTo(gearbox.OutputTorqueDemand);
        gearbox.InputTorqueDemand.ConnectTo(motor.TorqueDemand);
        feed.Out.ConnectTo(belt.In);
        belt.Out.ConnectTo(pile.In);

        Simulation sim = new SimulationBuilder(Options())
            .Add(pile).Add(belt).Add(feed).Add(run).Add(motor).Add(gearbox).Add(pulley).Add(tail).Add(friction)
            .Build();

        sim.RunFor(TimeSpan.FromSeconds(5));
        double emptyCurrent = motor.Current.Value;
        Assert.InRange(pulley.BeltSpeed.Value, 1.8, 1.9);   // 150 / 20 × 0.25 less droop

        sim.RunFor(TimeSpan.FromSeconds(30));               // belt fills to ~215 kg steady load

        // Empty: 118 N → 1.55 N·m → 1.03 A. Loaded: 202 N → 2.66 N·m → 1.35 A.
        Assert.True(belt.Load.Value > 180.0);
        Assert.True(motor.Current.Value > emptyCurrent + 0.2);
        Assert.True(motor.TorqueDemand.Value > 2.0);
        Assert.Equal(0.0, sim.MassBalance.Drift, 6);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Dse.Components.Tests --filter "FullyQualifiedName~DrivetrainTests"`
Expected: build error — `Gearbox` does not exist.

- [ ] **Step 3: Implement**

`src/Dse.Components/Mechanical/Gearbox.cs`:

```csharp
using Dse.Core.Contexts;
using Dse.Core.Graph;

namespace Dse.Components.Mechanical;

/// <summary>
/// A fixed-ratio reducer. Speed passes forward live; the torque the load
/// reflects back is read one tick late (latched), which is what keeps a
/// motor–gearbox–pulley chain free of algebraic loops.
/// </summary>
public sealed class Gearbox : ComponentBase
{
    public Gearbox(string id, double ratio, double efficiency = 0.95)
        : base(id)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(ratio);
        if (efficiency <= 0.0 || efficiency > 1.0)
        {
            throw new ArgumentOutOfRangeException(nameof(efficiency), efficiency, "Efficiency must be in (0, 1].");
        }

        Ratio = ratio;
        Efficiency = efficiency;
        InputSpeed = AddInput<double>("InputSpeed");
        OutputTorqueDemand = AddInput<double>("OutputTorqueDemand", latched: true);
        OutputSpeed = AddOutput<double>("OutputSpeed");
        InputTorqueDemand = AddOutput<double>("InputTorqueDemand");
    }

    /// <summary>Input turns per output turn.</summary>
    public double Ratio { get; }

    public double Efficiency { get; }

    /// <summary>rad/s at the input shaft.</summary>
    public InputPort<double> InputSpeed { get; }

    /// <summary>N·m demanded at the output shaft. Latched.</summary>
    public InputPort<double> OutputTorqueDemand { get; }

    /// <summary>rad/s at the output shaft.</summary>
    public OutputPort<double> OutputSpeed { get; }

    /// <summary>N·m reflected to the input shaft.</summary>
    public OutputPort<double> InputTorqueDemand { get; }

    public override void Evaluate(in TickContext ctx)
    {
        OutputSpeed.Value = InputSpeed.Value / Ratio;
        InputTorqueDemand.Value = OutputTorqueDemand.Value / (Ratio * Efficiency);
    }
}
```

`src/Dse.Components/Mechanical/DrivePulley.cs`:

```csharp
using Dse.Core.Contexts;
using Dse.Core.Faults;
using Dse.Core.Graph;

namespace Dse.Components.Mechanical;

/// <summary>
/// The pulley the drive turns: shaft speed becomes belt speed, and the force
/// the belt resists with becomes torque at the shaft. Bearing drag and belt
/// slip are its own faults, because they are genuinely its own.
/// </summary>
public sealed class DrivePulley : ComponentBase, IFaultTarget
{
    /// <summary>Extra drag at the bearing, N.</summary>
    public const string BearingFriction = "bearing-friction";

    /// <summary>The belt slips on the pulley: belt speed falls short of the surface speed.</summary>
    public const string BeltSlip = "belt-slip";

    private static readonly FaultDescriptor[] Faults =
    [
        new(BearingFriction, "Extra drag at the pulley bearing.",
            new FaultParameter("drag", "N", 0.0, "Added to the belt force while active.")),
        new(BeltSlip, "The belt slips on the pulley; belt speed is reduced, torque is not.",
            new FaultParameter("fraction", "", 0.1, "Fraction of surface speed lost, 0..1.")),
    ];

    private double _faultDrag;
    private double _slip;

    public DrivePulley(string id, double diameterM, double bearingDragN = 0.0)
        : base(id)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(diameterM);
        ArgumentOutOfRangeException.ThrowIfNegative(bearingDragN);
        DiameterM = diameterM;
        BearingDragN = bearingDragN;

        ShaftSpeed = AddInput<double>("ShaftSpeed");
        BeltForce = AddInput<double>("BeltForce");
        BeltSpeed = AddOutput<double>("BeltSpeed");
        TorqueDemand = AddOutput<double>("TorqueDemand");
    }

    /// <summary>m.</summary>
    public double DiameterM { get; }

    /// <summary>N.</summary>
    public double BearingDragN { get; }

    /// <summary>rad/s.</summary>
    public InputPort<double> ShaftSpeed { get; }

    /// <summary>N, the belt's total resistance.</summary>
    public InputPort<double> BeltForce { get; }

    /// <summary>m/s.</summary>
    public OutputPort<double> BeltSpeed { get; }

    /// <summary>N·m at the shaft.</summary>
    public OutputPort<double> TorqueDemand { get; }

    public IReadOnlyList<FaultDescriptor> SupportedFaults => Faults;

    public override void Evaluate(in TickContext ctx)
    {
        double radius = DiameterM / 2.0;
        BeltSpeed.Value = ShaftSpeed.Value * radius * (1.0 - _slip);
        TorqueDemand.Value = (Math.Max(0.0, BeltForce.Value) + BearingDragN + _faultDrag) * radius;
    }

    public void ApplyFault(string faultId, FaultArguments arguments)
    {
        switch (faultId)
        {
            case BearingFriction:
                _faultDrag = Math.Max(0.0, arguments.Get("drag"));
                break;
            case BeltSlip:
                _slip = Math.Clamp(arguments.Get("fraction"), 0.0, 1.0);
                break;
        }
    }

    public void ClearFault(string faultId)
    {
        switch (faultId)
        {
            case BearingFriction:
                _faultDrag = 0.0;
                break;
            case BeltSlip:
                _slip = 0.0;
                break;
        }
    }
}
```

`src/Dse.Components/Mechanical/TailPulley.cs`:

```csharp
using Dse.Core.Contexts;
using Dse.Core.Faults;
using Dse.Core.Graph;

namespace Dse.Components.Mechanical;

/// <summary>An idler pulley. What it contributes is drag, and a worn bearing adds to it.</summary>
public sealed class TailPulley : ComponentBase, IFaultTarget
{
    /// <summary>Extra drag at the bearing, N.</summary>
    public const string BearingFriction = "bearing-friction";

    private static readonly FaultDescriptor[] Faults =
    [
        new(BearingFriction, "Extra drag at the pulley bearing.",
            new FaultParameter("drag", "N", 0.0, "Added to the pulley's drag while active.")),
    ];

    private double _faultDrag;

    public TailPulley(string id, double bearingDragN)
        : base(id)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bearingDragN);
        BearingDragN = bearingDragN;
        Drag = AddOutput<double>("Drag");
    }

    /// <summary>N.</summary>
    public double BearingDragN { get; }

    /// <summary>N, the drag this pulley adds to the belt.</summary>
    public OutputPort<double> Drag { get; }

    public IReadOnlyList<FaultDescriptor> SupportedFaults => Faults;

    public override void Evaluate(in TickContext ctx) => Drag.Value = BearingDragN + _faultDrag;

    public void ApplyFault(string faultId, FaultArguments arguments) =>
        _faultDrag = Math.Max(0.0, arguments.Get("drag"));

    public void ClearFault(string faultId) => _faultDrag = 0.0;
}
```

`src/Dse.Components/Mechanical/BeltFriction.cs`:

```csharp
using Dse.Core.Contexts;
using Dse.Core.Graph;

namespace Dse.Components.Mechanical;

/// <summary>
/// The force it takes to move a loaded belt: rolling friction on the belt's
/// own mass plus the load, plus whatever drag the idlers add. This is the
/// link from "material accumulated" to "motor works harder".
/// </summary>
public sealed class BeltFriction : ComponentBase
{
    private const double Gravity = 9.80665;

    public BeltFriction(string id, double emptyBeltMassKg, double frictionCoefficient = 0.03)
        : base(id)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(emptyBeltMassKg);
        ArgumentOutOfRangeException.ThrowIfNegative(frictionCoefficient);
        EmptyBeltMassKg = emptyBeltMassKg;
        FrictionCoefficient = frictionCoefficient;

        Load = AddInput<double>("Load");
        Drag = AddInput<double>("Drag");
        Force = AddOutput<double>("Force");
    }

    /// <summary>kg, belt and moving parts with no material on it.</summary>
    public double EmptyBeltMassKg { get; }

    /// <summary>Rolling friction coefficient, dimensionless.</summary>
    public double FrictionCoefficient { get; }

    /// <summary>kg of material on the belt.</summary>
    public InputPort<double> Load { get; }

    /// <summary>N of extra drag, from idlers. Unconnected reads zero.</summary>
    public InputPort<double> Drag { get; }

    /// <summary>N.</summary>
    public OutputPort<double> Force { get; }

    public override void Evaluate(in TickContext ctx) =>
        Force.Value = (FrictionCoefficient * Gravity * (EmptyBeltMassKg + Math.Max(0.0, Load.Value))) + Math.Max(0.0, Drag.Value);
}
```

`src/Dse.Components/Mechanical/BeltGeometry.cs`:

```csharp
namespace Dse.Components.Mechanical;

/// <summary>
/// Belt width and the material's angle of repose give the most a belt can
/// carry per metre. Exceeding it is what defines overload and spillage, so
/// the number is derived, never asserted.
/// </summary>
public static class BeltGeometry
{
    /// <summary>
    /// kg/m for a flat belt carrying a triangular surcharge: effective width
    /// <c>b = 0.9 W − 0.05</c> (the CEMA edge allowance), cross-section
    /// <c>b² tan θ / 4</c>, times bulk density.
    /// </summary>
    public static double MaxLinearDensity(double beltWidthM, double angleOfReposeDeg, double bulkDensityKgM3)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(beltWidthM);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bulkDensityKgM3);
        if (angleOfReposeDeg <= 0.0 || angleOfReposeDeg >= 90.0)
        {
            throw new ArgumentOutOfRangeException(nameof(angleOfReposeDeg), angleOfReposeDeg, "Angle of repose must be in (0, 90) degrees.");
        }

        double effectiveWidth = (0.9 * beltWidthM) - 0.05;
        if (effectiveWidth <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(beltWidthM), beltWidthM, "Belt is too narrow to carry a surcharge.");
        }

        double area = effectiveWidth * effectiveWidth * Math.Tan(angleOfReposeDeg * Math.PI / 180.0) / 4.0;
        return area * bulkDensityKgM3;
    }
}
```

- [ ] **Step 4: Run the tests**

Run: `dotnet test tests/Dse.Components.Tests`
Expected: 72 pass. The chain test's `Validate` must pass with no `DSE003`:
the only cycles are Motor⇄Gearbox and Gearbox⇄DrivePulley, each broken by
a latched torque input, and DrivePulley→Belt→BeltFriction→DrivePulley, which
is not a cycle because the belt has no feedthrough (R13). If `DSE003`
appears, the message names the loop; check the latched flags before anything
else.

- [ ] **Step 5: Commit**

```bash
git add src/Dse.Components/Mechanical tests/Dse.Components.Tests/DrivetrainTests.cs
git commit -m "feat(components): add the gearbox, pulleys, belt friction and belt geometry"
```

---

### Task 15: Safety circuit — e-stop, pull-key, safety relay

**Files:**
- Create: `src/Dse.Components/Safety/SafetySwitch.cs`
- Create: `src/Dse.Components/Safety/EStop.cs`
- Create: `src/Dse.Components/Safety/PullKey.cs`
- Create: `src/Dse.Components/Safety/SafetyRelay.cs`
- Test: `tests/Dse.Components.Tests/SafetyTests.cs`

**Interfaces:**
- Consumes: `ComponentBase`, `IFaultTarget`.
- Produces:
  - `abstract class SafetySwitch : ComponentBase, IFaultTarget` —
    `InputPort<bool> Actuated` (default false); `OutputPort<bool> Ok`
    (normally-closed contact: true when healthy and not actuated). Faults
    `"wiring-open"` (`Ok` false regardless — fail-safe) and `"contact-welded"`
    (`Ok` true regardless — the dangerous one). Subclasses supply the event
    codes via `protected abstract string ActuatedCode { get; }` and
    `ReleasedCode`.
  - `EStop(string id)` — `ESTOP_PRESSED` / `ESTOP_RELEASED`.
  - `PullKey(string id)` — `PULLKEY_PULLED` / `PULLKEY_RESET`.
  - `SafetyRelay(string id, int channels)` : `ComponentBase, IFaultTarget` —
    inputs `Channel1..ChannelN` (bool, default true; `Channel(int index)`
    1-based lookup), `Reset` (bool); output `Ok`. Any channel false → `Ok`
    false, latched; `Ok` returns true only when every channel is true and
    `Reset` rises. Events `SAFETY_TRIP` (naming the channel) / `SAFETY_RESET`.
    Faults `"stuck-energised"` (`Ok` stays true) and `"coil-failure"` (`Ok` false).

Spec 9.4: e-stops and pull-keys are physical components producing discrete
signals, and the safety relay de-energises the contactor directly, with no
controller. `Actuated` is an input port rather than a method so that a test,
a scenario, or (plan 4) an operator writing a tag can act on it through the
same door.

- [ ] **Step 1: Write the failing tests**

`tests/Dse.Components.Tests/SafetyTests.cs`:

```csharp
using Dse.Components.Safety;
using Dse.Components.Tests.Fakes;
using Dse.Core;
using Dse.Core.Time;
using Xunit;

namespace Dse.Components.Tests;

public class SafetyTests
{
    private static SimulationOptions Options() => new()
    {
        Seed = 1UL,
        StartTime = new DateTimeOffset(2026, 1, 1, 6, 0, 0, TimeSpan.Zero),
        TimeStep = TimeSpan.FromMilliseconds(10),
    };

    [Fact]
    public void SwitchesAreNormallyClosedAndLogTheirOwnCodes()
    {
        var estop = new EStop("ES1");
        var key = new PullKey("PK1");
        var press = new Switch("Press");
        var pull = new Switch("Pull");
        press.Out.ConnectTo(estop.Actuated);
        pull.Out.ConnectTo(key.Actuated);
        Simulation sim = new SimulationBuilder(Options()).Add(estop).Add(key).Add(press).Add(pull).Build();

        sim.Tick();
        Assert.True(estop.Ok.Value);
        Assert.True(key.Ok.Value);

        press.Value = true;
        pull.Value = true;
        sim.Tick();
        Assert.False(estop.Ok.Value);
        Assert.False(key.Ok.Value);

        press.Value = false;
        pull.Value = false;
        sim.Tick();
        Assert.Equal(
            ["ESTOP_PRESSED", "PULLKEY_PULLED", "ESTOP_RELEASED", "PULLKEY_RESET"],
            sim.Events.Records.Select(r => r.Code));
    }

    [Fact]
    public void WiringOpenFailsSafeAndAWeldedContactFailsDangerous()
    {
        var estop = new EStop("ES1");
        var press = new Switch("Press");
        press.Out.ConnectTo(estop.Actuated);
        Simulation sim = new SimulationBuilder(Options()).Add(estop).Add(press).Build();

        sim.InjectFaultIn(TimeSpan.Zero, "ES1", SafetySwitch.WiringOpen);
        sim.Tick();
        Assert.False(estop.Ok.Value);

        sim.ClearFaultIn(TimeSpan.Zero, "ES1", SafetySwitch.WiringOpen);
        sim.InjectFaultIn(TimeSpan.Zero, "ES1", SafetySwitch.ContactWelded);
        press.Value = true;
        sim.Tick();
        Assert.True(estop.Ok.Value);
    }

    [Fact]
    public void TheRelayLatchesOnAnyOpenChannelAndResetsOnlyWhenHealthyAndReset()
    {
        var relay = new SafetyRelay("SR", channels: 2);
        var ch1 = new Switch("C1", true);
        var ch2 = new Switch("C2", true);
        var reset = new Switch("Reset");
        ch1.Out.ConnectTo(relay.Channel(1));
        ch2.Out.ConnectTo(relay.Channel(2));
        reset.Out.ConnectTo(relay.Reset);
        Simulation sim = new SimulationBuilder(Options()).Add(relay).Add(ch1).Add(ch2).Add(reset).Build();

        sim.Tick();
        Assert.False(relay.Ok.Value);          // needs a reset after power-up, like a real relay
        reset.Value = true;
        sim.Tick();
        Assert.True(relay.Ok.Value);
        reset.Value = false;
        sim.Tick();

        ch2.Value = false;
        sim.Tick();
        Assert.False(relay.Ok.Value);
        ch2.Value = true;
        sim.RunFor(TimeSpan.FromMilliseconds(50));
        Assert.False(relay.Ok.Value);          // latched

        reset.Value = true;
        sim.Tick();
        Assert.True(relay.Ok.Value);
        reset.Value = false;
        sim.Tick();
        Assert.True(relay.Ok.Value);           // reset is edge-triggered; holding it is not required

        ch1.Value = false;
        reset.Value = true;                    // held reset must not override an open channel
        sim.RunFor(TimeSpan.FromMilliseconds(30));
        Assert.False(relay.Ok.Value);

        var trips = sim.Events.Records.Where(r => r.Code == "SAFETY_TRIP").ToList();
        Assert.Equal(2, trips.Count);
        Assert.Contains("Channel2", trips[0].Message);
        Assert.Contains("Channel1", trips[1].Message);
        Assert.Equal(2, sim.Events.Records.Count(r => r.Code == "SAFETY_RESET"));
    }

    [Fact]
    public void RelayFaults()
    {
        var relay = new SafetyRelay("SR", 1);
        var ch1 = new Switch("C1", true);
        var reset = new Switch("Reset", true);
        ch1.Out.ConnectTo(relay.Channel(1));
        reset.Out.ConnectTo(relay.Reset);
        Simulation sim = new SimulationBuilder(Options()).Add(relay).Add(ch1).Add(reset).Build();
        sim.Tick();
        Assert.True(relay.Ok.Value);

        sim.InjectFaultIn(TimeSpan.Zero, "SR", SafetyRelay.CoilFailure);
        sim.Tick();
        Assert.False(relay.Ok.Value);

        sim.ClearFaultIn(TimeSpan.Zero, "SR", SafetyRelay.CoilFailure);
        sim.InjectFaultIn(TimeSpan.Zero, "SR", SafetyRelay.StuckEnergised);
        ch1.Value = false;
        sim.Tick();
        Assert.True(relay.Ok.Value);
    }

    [Fact]
    public void RelayRejectsBadChannelCounts()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SafetyRelay("SR", 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SafetyRelay("SR", 2).Channel(3));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Dse.Components.Tests --filter "FullyQualifiedName~SafetyTests"`
Expected: build error — `Dse.Components.Safety` does not exist.

- [ ] **Step 3: Implement**

`src/Dse.Components/Safety/SafetySwitch.cs`:

```csharp
using Dse.Core.Contexts;
using Dse.Core.Faults;
using Dse.Core.Graph;

namespace Dse.Components.Safety;

/// <summary>
/// A normally-closed safety contact: <see cref="Ok"/> is true until someone
/// actuates it. Wiring that opens fails safe; a contact that welds fails
/// dangerous. Both are real, and both are faults here.
/// </summary>
public abstract class SafetySwitch : ComponentBase, IFaultTarget
{
    /// <summary>The loop is open: reads not-OK whatever the operator does.</summary>
    public const string WiringOpen = "wiring-open";

    /// <summary>The contact is welded: reads OK whatever the operator does.</summary>
    public const string ContactWelded = "contact-welded";

    private static readonly FaultDescriptor[] Faults =
    [
        new(WiringOpen, "The safety loop is open; the switch reads not-OK regardless of actuation (fail-safe)."),
        new(ContactWelded, "The contact is welded closed; the switch reads OK even when actuated."),
    ];

    private bool _open;
    private bool _welded;
    private bool _wasActuated;

    protected SafetySwitch(string id)
        : base(id)
    {
        Actuated = AddInput<bool>("Actuated");
        Ok = AddOutput<bool>("Ok");
    }

    /// <summary>True while the operator holds the device actuated. Unconnected reads false.</summary>
    public InputPort<bool> Actuated { get; }

    /// <summary>The contact: true when the loop is healthy and the device is not actuated.</summary>
    public OutputPort<bool> Ok { get; }

    public IReadOnlyList<FaultDescriptor> SupportedFaults => Faults;

    protected abstract string ActuatedCode { get; }

    protected abstract string ReleasedCode { get; }

    public override void Evaluate(in TickContext ctx)
    {
        bool actuated = Actuated.Value;
        if (actuated != _wasActuated)
        {
            ctx.Log(Id, actuated ? ActuatedCode : ReleasedCode, actuated ? "Actuated by the operator." : "Released.");
        }

        _wasActuated = actuated;
        Ok.Value = _welded || (!_open && !actuated);
    }

    public void ApplyFault(string faultId, FaultArguments arguments)
    {
        switch (faultId)
        {
            case WiringOpen:
                _open = true;
                break;
            case ContactWelded:
                _welded = true;
                break;
        }
    }

    public void ClearFault(string faultId)
    {
        switch (faultId)
        {
            case WiringOpen:
                _open = false;
                break;
            case ContactWelded:
                _welded = false;
                break;
        }
    }
}
```

`src/Dse.Components/Safety/EStop.cs`:

```csharp
namespace Dse.Components.Safety;

/// <summary>An emergency stop button.</summary>
public sealed class EStop : SafetySwitch
{
    public EStop(string id)
        : base(id)
    {
    }

    protected override string ActuatedCode => "ESTOP_PRESSED";

    protected override string ReleasedCode => "ESTOP_RELEASED";
}
```

`src/Dse.Components/Safety/PullKey.cs`:

```csharp
namespace Dse.Components.Safety;

/// <summary>A pull-wire switch along a conveyor.</summary>
public sealed class PullKey : SafetySwitch
{
    public PullKey(string id)
        : base(id)
    {
    }

    protected override string ActuatedCode => "PULLKEY_PULLED";

    protected override string ReleasedCode => "PULLKEY_RESET";
}
```

`src/Dse.Components/Safety/SafetyRelay.cs`:

```csharp
using System.Globalization;
using Dse.Core.Contexts;
using Dse.Core.Faults;
using Dse.Core.Graph;

namespace Dse.Components.Safety;

/// <summary>
/// A latching safety relay. Any open channel drops it and it stays dropped
/// until every channel is healthy and a reset edge arrives — starting from
/// power-up, when it needs its first reset. Its output goes straight to the
/// starter's safety input, so the circuit works with no controller at all.
/// </summary>
public sealed class SafetyRelay : ComponentBase, IFaultTarget
{
    /// <summary>The relay stays energised whatever the channels say.</summary>
    public const string StuckEnergised = "stuck-energised";

    /// <summary>The coil is open: the relay cannot energise.</summary>
    public const string CoilFailure = "coil-failure";

    private static readonly FaultDescriptor[] Faults =
    [
        new(StuckEnergised, "The relay contacts are welded; it stays energised whatever the channels say."),
        new(CoilFailure, "The coil is open; the relay cannot energise until the fault is cleared."),
    ];

    private readonly InputPort<bool>[] _channels;
    private bool _energised;
    private bool _wasReset;
    private bool _stuck;
    private bool _coilFailed;

    public SafetyRelay(string id, int channels)
        : base(id)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(channels);

        _channels = new InputPort<bool>[channels];
        for (int i = 0; i < channels; i++)
        {
            _channels[i] = AddInput<bool>(
                string.Create(CultureInfo.InvariantCulture, $"Channel{i + 1}"),
                defaultValue: true);
        }

        Reset = AddInput<bool>("Reset");
        Ok = AddOutput<bool>("Ok");
    }

    public int ChannelCount => _channels.Length;

    /// <summary>A safety channel, 1-based. Unconnected reads true (healthy).</summary>
    public InputPort<bool> Channel(int index)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(index, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(index, _channels.Length);
        return _channels[index - 1];
    }

    /// <summary>Rising edge re-energises the relay if every channel is healthy.</summary>
    public InputPort<bool> Reset { get; }

    /// <summary>True while energised: the contactor may close.</summary>
    public OutputPort<bool> Ok { get; }

    public IReadOnlyList<FaultDescriptor> SupportedFaults => Faults;

    public override void Evaluate(in TickContext ctx)
    {
        int open = -1;
        for (int i = 0; i < _channels.Length; i++)
        {
            if (!_channels[i].Value)
            {
                open = i;
                break;
            }
        }

        bool reset = Reset.Value;
        bool resetEdge = reset && !_wasReset;
        _wasReset = reset;

        if (open >= 0)
        {
            if (_energised)
            {
                ctx.Log(Id, "SAFETY_TRIP", $"{_channels[open].Name} open; relay de-energised.");
            }

            _energised = false;
        }
        else if (!_energised && resetEdge && !_coilFailed)
        {
            _energised = true;
            ctx.Log(Id, "SAFETY_RESET", "All channels healthy; relay energised.");
        }

        Ok.Value = _stuck || (_energised && !_coilFailed);
    }

    public void ApplyFault(string faultId, FaultArguments arguments)
    {
        switch (faultId)
        {
            case StuckEnergised:
                _stuck = true;
                break;
            case CoilFailure:
                _coilFailed = true;
                _energised = false;
                break;
        }
    }

    public void ClearFault(string faultId)
    {
        switch (faultId)
        {
            case StuckEnergised:
                _stuck = false;
                break;
            case CoilFailure:
                _coilFailed = false;
                break;
        }
    }
}
```

- [ ] **Step 4: Run the tests**

Run: `dotnet test tests/Dse.Components.Tests`
Expected: 77 pass. In the relay test the first tick reads `Reset` false, so the
relay stays de-energised from power-up; the reset edge on the second tick
energises it. In `RelayFaults` the reset switch is true from the first tick,
which is a rising edge from the relay's initial `_wasReset == false`.

- [ ] **Step 5: Commit**

```bash
git add src/Dse.Components/Safety tests/Dse.Components.Tests/SafetyTests.cs
git commit -m "feat(components): add the e-stop, pull-key and latching safety relay"
```

---

### Task 16: Motor starter with overload relay

**Files:**
- Create: `src/Dse.Components/Mechanical/MotorStarter.cs`
- Test: `tests/Dse.Components.Tests/MotorStarterTests.cs`

**Interfaces:**
- Consumes: `ComponentBase`, `IFaultTarget`, `Motor` (Task 13), `SafetyRelay` (Task 15).
- Produces: `MotorStarter(string id, double tripLevel = 1.1, double resetLevel = 0.9)`
  : `ComponentBase, IFaultTarget`. Inputs `Command` (bool), `SafetyOk` (bool,
  default true), `ThermalState` (double), `Reset` (bool). Outputs `Contactor`
  (bool), `Tripped` (bool). Events `CONTACTOR_CLOSED`, `CONTACTOR_OPENED`,
  `OVERLOAD_TRIP`, `OVERLOAD_RESET`. Faults `"contactor-welded"` (closed
  regardless) and `"contactor-open"` (cannot close).

Spec 8.3: "an overload relay trips on a curve" — the curve is the motor's
thermal state; the relay trips when it crosses the trip level and can be
reset only once it has cooled below the reset level and a reset edge arrives.
Spec 9.4: the safety relay de-energises the contactor directly through
`SafetyOk`. `Contactor = (Command ∧ SafetyOk ∧ ¬Tripped ∧ ¬open) ∨ welded`.

- [ ] **Step 1: Write the failing tests**

`tests/Dse.Components.Tests/MotorStarterTests.cs`:

```csharp
using Dse.Components.Mechanical;
using Dse.Components.Tests.Fakes;
using Dse.Core;
using Dse.Core.Time;
using Xunit;

namespace Dse.Components.Tests;

public class MotorStarterTests
{
    private static SimulationOptions Options() => new()
    {
        Seed = 1UL,
        StartTime = new DateTimeOffset(2026, 1, 1, 6, 0, 0, TimeSpan.Zero),
        TimeStep = TimeSpan.FromMilliseconds(10),
    };

    private sealed record Rig(Simulation Sim, MotorStarter Starter, Switch Command, Switch Safety, Setpoint Thermal, Switch Reset);

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

    [Fact]
    public void ClosesOnCommandAndOpensWhenSafetyDrops()
    {
        Rig rig = Build();
        rig.Sim.Tick();
        Assert.False(rig.Starter.Contactor.Value);

        rig.Command.Value = true;
        rig.Sim.Tick();
        Assert.True(rig.Starter.Contactor.Value);

        rig.Safety.Value = false;
        rig.Sim.Tick();
        Assert.False(rig.Starter.Contactor.Value);
        Assert.False(rig.Starter.Tripped.Value);

        rig.Safety.Value = true;
        rig.Sim.Tick();
        Assert.True(rig.Starter.Contactor.Value);   // command still held: no trip, so it closes again
        Assert.Equal(["CONTACTOR_CLOSED", "CONTACTOR_OPENED", "CONTACTOR_CLOSED"], rig.Sim.Events.Records.Select(r => r.Code));
    }

    [Fact]
    public void TripsOnTheThermalStateAndResetsOnlyAfterCooling()
    {
        Rig rig = Build();
        rig.Command.Value = true;
        rig.Sim.Tick();

        rig.Thermal.Value = 1.1;
        rig.Sim.Tick();
        Assert.True(rig.Starter.Tripped.Value);
        Assert.False(rig.Starter.Contactor.Value);

        rig.Reset.Value = true;                    // still hot
        rig.Sim.Tick();
        Assert.True(rig.Starter.Tripped.Value);
        rig.Reset.Value = false;

        rig.Thermal.Value = 0.8;
        rig.Sim.Tick();
        Assert.True(rig.Starter.Tripped.Value);    // cooled, but no reset edge yet

        rig.Reset.Value = true;
        rig.Sim.Tick();
        Assert.False(rig.Starter.Tripped.Value);
        Assert.True(rig.Starter.Contactor.Value);
        Assert.Equal(
            ["CONTACTOR_CLOSED", "OVERLOAD_TRIP", "CONTACTOR_OPENED", "OVERLOAD_RESET", "CONTACTOR_CLOSED"],
            rig.Sim.Events.Records.Select(r => r.Code));
        Assert.Contains("1.1", rig.Sim.Events.Records[1].Message);
    }

    [Fact]
    public void ContactorFaults()
    {
        Rig rig = Build();
        rig.Sim.InjectFaultIn(TimeSpan.Zero, "K1", MotorStarter.ContactorWelded);
        rig.Sim.Tick();
        Assert.True(rig.Starter.Contactor.Value);   // no command, yet closed

        rig.Sim.ClearFaultIn(TimeSpan.Zero, "K1", MotorStarter.ContactorWelded);
        rig.Sim.InjectFaultIn(TimeSpan.Zero, "K1", MotorStarter.ContactorOpen);
        rig.Command.Value = true;
        rig.Sim.Tick();
        Assert.False(rig.Starter.Contactor.Value);
    }

    [Fact]
    public void RejectsAResetLevelAboveTheTripLevel()
    {
        Assert.Throws<ArgumentException>(() => new MotorStarter("K1", tripLevel: 1.0, resetLevel: 1.0));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Dse.Components.Tests --filter "FullyQualifiedName~MotorStarterTests"`
Expected: build error — `MotorStarter` does not exist.

- [ ] **Step 3: Implement**

`src/Dse.Components/Mechanical/MotorStarter.cs`:

```csharp
using System.Globalization;
using Dse.Core.Contexts;
using Dse.Core.Faults;
using Dse.Core.Graph;

namespace Dse.Components.Mechanical;

/// <summary>
/// A direct-on-line starter: a contactor and a thermal overload relay. The
/// contactor closes on command when the safety circuit allows and the relay
/// is not tripped. The relay trips when the motor's thermal state crosses
/// the trip level and resets on a reset edge once it has cooled below the
/// reset level. The safety input bypasses everything: a dropped safety relay
/// opens the contactor with no controller involved.
/// </summary>
public sealed class MotorStarter : ComponentBase, IFaultTarget
{
    /// <summary>The contactor is welded closed.</summary>
    public const string ContactorWelded = "contactor-welded";

    /// <summary>The contactor cannot close.</summary>
    public const string ContactorOpen = "contactor-open";

    private static readonly FaultDescriptor[] Faults =
    [
        new(ContactorWelded, "The contactor is welded closed; the motor stays energised whatever the logic says."),
        new(ContactorOpen, "The contactor coil or contacts have failed; the motor cannot be energised."),
    ];

    private bool _tripped;
    private bool _closed;
    private bool _wasReset;
    private bool _welded;
    private bool _open;

    public MotorStarter(string id, double tripLevel = 1.1, double resetLevel = 0.9)
        : base(id)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tripLevel);
        if (resetLevel >= tripLevel)
        {
            throw new ArgumentException("The reset level must be below the trip level.", nameof(resetLevel));
        }

        TripLevel = tripLevel;
        ResetLevel = resetLevel;

        Command = AddInput<bool>("Command");
        SafetyOk = AddInput<bool>("SafetyOk", defaultValue: true);
        ThermalState = AddInput<double>("ThermalState");
        Reset = AddInput<bool>("Reset");
        Contactor = AddOutput<bool>("Contactor");
        Tripped = AddOutput<bool>("Tripped");
    }

    /// <summary>Thermal state at which the overload relay trips.</summary>
    public double TripLevel { get; }

    /// <summary>Thermal state below which a reset is accepted.</summary>
    public double ResetLevel { get; }

    /// <summary>Run command from a controller or an operator. Unconnected reads false.</summary>
    public InputPort<bool> Command { get; }

    /// <summary>From the safety relay. Unconnected reads true.</summary>
    public InputPort<bool> SafetyOk { get; }

    /// <summary>The motor's thermal state.</summary>
    public InputPort<double> ThermalState { get; }

    /// <summary>Rising edge resets the overload relay if cooled.</summary>
    public InputPort<bool> Reset { get; }

    /// <summary>True energises the motor.</summary>
    public OutputPort<bool> Contactor { get; }

    public OutputPort<bool> Tripped { get; }

    public IReadOnlyList<FaultDescriptor> SupportedFaults => Faults;

    public override void Evaluate(in TickContext ctx)
    {
        double thermal = ThermalState.Value;
        bool reset = Reset.Value;
        bool resetEdge = reset && !_wasReset;
        _wasReset = reset;

        if (!_tripped && thermal >= TripLevel)
        {
            _tripped = true;
            ctx.Log(Id, "OVERLOAD_TRIP", string.Create(CultureInfo.InvariantCulture,
                $"Thermal state {thermal} reached the trip level {TripLevel}."));
        }
        else if (_tripped && resetEdge && thermal < ResetLevel)
        {
            _tripped = false;
            ctx.Log(Id, "OVERLOAD_RESET", "Overload relay reset.");
        }

        bool closed = _welded || (Command.Value && SafetyOk.Value && !_tripped && !_open);
        if (closed != _closed)
        {
            ctx.Log(Id, closed ? "CONTACTOR_CLOSED" : "CONTACTOR_OPENED", closed ? "Motor energised." : "Motor de-energised.");
        }

        _closed = closed;
        Contactor.Value = closed;
        Tripped.Value = _tripped;
    }

    public void ApplyFault(string faultId, FaultArguments arguments)
    {
        switch (faultId)
        {
            case ContactorWelded:
                _welded = true;
                break;
            case ContactorOpen:
                _open = true;
                break;
        }
    }

    public void ClearFault(string faultId)
    {
        switch (faultId)
        {
            case ContactorWelded:
                _welded = false;
                break;
            case ContactorOpen:
                _open = false;
                break;
        }
    }
}
```

- [ ] **Step 4: Run the tests**

Run: `dotnet test tests/Dse.Components.Tests`
Expected: 81 pass.

- [ ] **Step 5: Commit**

```bash
git add src/Dse.Components/Mechanical/MotorStarter.cs tests/Dse.Components.Tests/MotorStarterTests.cs
git commit -m "feat(components): add the motor starter with contactor and thermal overload relay"
```

---

### Task 17: The conveyor composite, the causal chain, and docs

**Files:**
- Create: `src/Dse.Components/Conveyors/ConveyorOptions.cs`
- Create: `src/Dse.Components/Conveyors/Conveyor.cs`
- Test: `tests/Dse.Components.Tests/ConveyorTests.cs`
- Modify: `docs/architecture.md`
- Modify: `README.md`

**Interfaces:**
- Consumes: everything above; `CompositeComponent` (plan 1).
- Produces:
  - `sealed record ConveyorOptions(double LengthM, double CellSizeM, double BeltWidthM, double AngleOfReposeDeg, double MaterialDensityKgM3, double EmptyBeltMassKg, double FrictionCoefficient, double PulleyDiameterM, double GearRatio, MotorRating Motor, double TailDragN = 50.0, int PullKeys = 2, double SpeedMarginFraction = 0.1)`.
  - `Conveyor(string id, ConveyorOptions options) : CompositeComponent` with
    children `Motor`, `Gearbox`, `Drive`, `Tail`, `Friction`, `Belt`,
    `SpeedSensor`, `Scale`, `CurrentSensor`, `ZeroSpeed`, `PullKey1..N`,
    `EStop`, `Safety`, `Starter` (ids qualified `"{id}.{child}"`). Exposed:
    `Inlet("In")`, `Outlet("Out")`; inputs `Start`, `Reset`, `SafetyReset`,
    `PullKey1..N`, `EStop` (all `bool`); outputs `Speed`, `TonnesPerHour`,
    `Current` (`double`), `Stopped`, `Contactor`, `Tripped`, `SafetyOk` (`bool`).
    Public `BulkBelt Belt`, `Motor Motor`, `MotorStarter Starter`,
    `SafetyRelay Safety`.

Spec 6.3: a conveyor is a composition, and there is no hard-coded conveyor in
the engine. Spec 7.8 and 8.3 are this task's acceptance tests: a blocked
chute fills, the belt behind it loads up, torque demand rises, current
rises, the thermal state crosses the trip level, the starter opens the
contactor, the motor coasts, the zero-speed switch fires, the scale reads
zero — and none of it is conveyor-specific code. The pull-key test proves
spec 9.4; the thermal-bias test proves spec 8.3's "the same trip"; the
replay test proves spec 16's determinism line for the library.

- [ ] **Step 1: Write the failing tests**

`tests/Dse.Components.Tests/ConveyorTests.cs`:

```csharp
using Dse.Components.Conveyors;
using Dse.Components.Flow;
using Dse.Components.Mechanical;
using Dse.Components.Tests.Fakes;
using Dse.Core;
using Dse.Core.Faults;
using Dse.Core.Flow;
using Dse.Core.Logging;
using Dse.Core.Time;
using Xunit;

namespace Dse.Components.Tests;

public class ConveyorTests
{
    private static readonly MaterialType Ore = new("Ore", PayloadKind.Bulk);

    private static readonly ConveyorOptions Cv001 = new(
        LengthM: 10.0,
        CellSizeM: 0.5,
        BeltWidthM: 0.8,
        AngleOfReposeDeg: 20.0,
        MaterialDensityKgM3: 2000.0,
        EmptyBeltMassKg: 250.0,
        FrictionCoefficient: 0.04,
        PulleyDiameterM: 0.5,
        GearRatio: 20.0,
        Motor: new MotorRating(750.0, 150.0, 2.0),
        TailDragN: 80.0);

    private static SimulationOptions Options(ulong seed = 1UL) => new()
    {
        Seed = seed,
        StartTime = new DateTimeOffset(2026, 1, 1, 6, 0, 0, TimeSpan.Zero),
        TimeStep = TimeSpan.FromMilliseconds(10),
    };

    private sealed record Plant(
        Simulation Sim,
        BulkSource Feed,
        Conveyor Conveyor,
        TransferChute Chute,
        BulkSink Pile,
        Switch Start,
        Switch SafetyReset,
        Switch PullKey1)
    {
        public void Run(double seconds) => Sim.RunFor(TimeSpan.FromSeconds(seconds));

        public double Seconds => Sim.Clock.Elapsed.TotalSeconds;

        public IEnumerable<SimEventRecord> Events(string source) => Sim.Events.Records.Where(r => r.Source == source);

        /// <summary>Pulses the safety reset and holds the start command: the operator's start sequence.</summary>
        public void StartUp()
        {
            SafetyReset.Value = true;
            Run(0.05);
            SafetyReset.Value = false;
            Start.Value = true;
        }
    }

    private static Plant Build(ulong seed = 1UL, double feedRate = 20.0)
    {
        var feed = new BulkSource("Feed", Ore, feedRate, new MaterialProperties(2000.0, 0.03, 15.0));
        var conveyor = new Conveyor("CV001", Cv001);
        var chute = new TransferChute("Chute", capacityKg: 200.0);
        var pile = new BulkSink("Pile");
        var start = new Switch("Start");
        var safetyReset = new Switch("SafetyReset");
        var pullKey1 = new Switch("Key1");

        feed.Out.ConnectTo(conveyor.Inlet("In"));
        conveyor.Outlet("Out").ConnectTo(chute.In);
        chute.Out.ConnectTo(pile.In);
        start.Out.ConnectTo(conveyor.Input<bool>("Start"));
        safetyReset.Out.ConnectTo(conveyor.Input<bool>("SafetyReset"));
        pullKey1.Out.ConnectTo(conveyor.Input<bool>("PullKey1"));

        Simulation sim = new SimulationBuilder(Options(seed))
            .Add(pile).Add(chute).Add(conveyor).Add(feed).Add(start).Add(safetyReset).Add(pullKey1)
            .Build();
        return new Plant(sim, feed, conveyor, chute, pile, start, safetyReset, pullKey1);
    }

    [Fact]
    public void TheCompositeFlattensToQualifiedLeavesWithNoAlgebraicLoop()
    {
        Plant plant = Build();

        string[] ids = plant.Sim.Components.Select(c => c.Id).ToArray();
        Assert.Contains("CV001.Motor", ids);
        Assert.Contains("CV001.Belt", ids);
        Assert.Contains("CV001.PullKey2", ids);
        Assert.Contains("CV001.Starter", ids);
        Assert.Equal(15, ids.Count(id => id.StartsWith("CV001.", StringComparison.Ordinal)));   // 13 fixed children + 2 pull-keys
        Assert.Equal(["bearing-friction", "thermal-bias"], plant.Sim.FaultsOf("CV001.Motor").Select(f => f.Id));
    }

    [Fact]
    public void StartsReachesSpeedAndConveysAtTheFeedRate()
    {
        Plant plant = Build();
        plant.StartUp();

        plant.Run(40.0);

        Assert.Contains(plant.Events("CV001.Motor"), r => r.Code == "AT_SPEED");
        Assert.True(plant.Conveyor.Output<bool>("Contactor").Value);
        Assert.False(plant.Conveyor.Output<bool>("Tripped").Value);
        Assert.InRange(plant.Conveyor.Output<double>("Speed").Value, 1.75, 1.95);        // 150 / 20 × 0.25 m less droop
        Assert.InRange(plant.Conveyor.Output<double>("TonnesPerHour").Value, 68.0, 76.0); // 20 kg/s = 72 t/h
        Assert.InRange(plant.Conveyor.Output<double>("Current").Value, 1.2, 1.6);
        Assert.InRange(plant.Sim.Telemetry.Read("CV001.Belt.Load"), 95.0, 120.0);
        Assert.True(plant.Pile.MassDestroyed > 500.0);
        Assert.Equal(0.0, plant.Sim.MassBalance.Drift, 6);
    }

    [Fact]
    public void ABlockedChuteLoadsTheBeltRaisesCurrentAndTripsTheOverload()
    {
        Plant plant = Build();
        plant.StartUp();
        plant.Run(30.0);
        double steadyLoad = plant.Sim.Telemetry.Read("CV001.Belt.Load");
        double steadyCurrent = plant.Sim.Telemetry.Read("CV001.Motor.Current");

        plant.Sim.InjectFaultIn(TimeSpan.Zero, "Chute", TransferChute.Blockage);

        double loadAtTrip = double.NaN;
        double currentAtTrip = double.NaN;
        double tripTime = double.NaN;
        while (plant.Seconds < 250.0)
        {
            plant.Sim.Tick();
            if (plant.Conveyor.Output<bool>("Tripped").Value)
            {
                tripTime = plant.Seconds;
                loadAtTrip = plant.Sim.Telemetry.Read("CV001.Belt.Load");
                currentAtTrip = plant.Sim.Telemetry.Read("CV001.Motor.Current");
                break;
            }
        }

        // The chain: chute full → belt loads → torque → current → thermal state → trip.
        Assert.False(double.IsNaN(tripTime));
        Assert.True(plant.Chute.Full.Value);
        Assert.True(loadAtTrip > 4.0 * steadyLoad);
        Assert.True(loadAtTrip > 500.0);
        Assert.True(currentAtTrip > 2.0);                       // above rated
        Assert.True(currentAtTrip > steadyCurrent + 0.5);
        Assert.Contains(plant.Events("CV001.Starter"), r => r.Code == "OVERLOAD_TRIP");
        long atSpeedTick = plant.Events("CV001.Motor").First(r => r.Code == "AT_SPEED").Tick;
        long tripTick = plant.Events("CV001.Starter").First(r => r.Code == "OVERLOAD_TRIP").Tick;
        Assert.True(atSpeedTick < tripTick);

        // Consequences: contactor open, motor coasts, belt stops, scale reads zero.
        plant.Run(20.0);
        Assert.False(plant.Conveyor.Output<bool>("Contactor").Value);
        Assert.True(plant.Conveyor.Output<bool>("Stopped").Value);
        Assert.True(plant.Conveyor.Output<double>("Speed").Value < 0.05);
        Assert.True(plant.Conveyor.Output<double>("TonnesPerHour").Value < 1.0);
        Assert.Contains(plant.Events("CV001.ZeroSpeed"), r => r.Code == "ZERO_SPEED");
        Assert.Contains(plant.Events("CV001.Motor"), r => r.Code == "DE_ENERGISED");
        // Frozen where it stopped: the feed can only add what room was left before zero speed.
        Assert.InRange(plant.Sim.Telemetry.Read("CV001.Belt.Load"), loadAtTrip - 1e-6, 820.0);
        Assert.Equal(0.0, plant.Sim.MassBalance.Drift, 6);
    }

    [Fact]
    public void APullKeyStopsTheBeltWithNoControllerAndTheLineRestartsAfterReset()
    {
        Plant plant = Build();
        plant.StartUp();
        plant.Run(30.0);

        plant.PullKey1.Value = true;
        plant.Run(15.0);

        Assert.False(plant.Conveyor.Output<bool>("SafetyOk").Value);
        Assert.False(plant.Conveyor.Output<bool>("Contactor").Value);
        Assert.False(plant.Conveyor.Output<bool>("Tripped").Value);
        Assert.True(plant.Conveyor.Output<double>("Speed").Value < 0.05);
        string[] codes = plant.Sim.Events.Records
            .Where(r => r.Tick >= 3000)
            .Select(r => $"{r.Source} {r.Code}")
            .Take(4)
            .ToArray();
        Assert.Equal(
            ["CV001.PullKey1 PULLKEY_PULLED", "CV001.Safety SAFETY_TRIP", "CV001.Starter CONTACTOR_OPENED", "CV001.Motor DE_ENERGISED"],
            codes);

        plant.PullKey1.Value = false;
        plant.Run(1.0);
        Assert.False(plant.Conveyor.Output<bool>("Contactor").Value);   // latched until reset
        plant.SafetyReset.Value = true;
        plant.Run(0.05);
        plant.SafetyReset.Value = false;
        plant.Run(10.0);
        Assert.True(plant.Conveyor.Output<bool>("Contactor").Value);
        Assert.Equal(2, plant.Events("CV001.Motor").Count(r => r.Code == "AT_SPEED"));
    }

    [Fact]
    public void AnInjectedThermalBiasTripsTheSameRelayTheSameWay()
    {
        Plant plant = Build();
        plant.StartUp();
        plant.Sim.InjectFaultAt(TimeSpan.FromSeconds(40), "CV001.Motor", Motor.ThermalBias, new FaultArguments(new("amount", 0.7)));

        plant.Run(41.0);

        SimEventRecord trip = Assert.Single(plant.Events("CV001.Starter"), r => r.Code == "OVERLOAD_TRIP");
        Assert.Equal(4000L, trip.Tick);
        Assert.Contains(plant.Sim.Events.Records, r => r.Code == "FAULT" && r.Source == "CV001.Motor" && r.Tick == 4000L);
        Assert.False(plant.Conveyor.Output<bool>("Contactor").Value);
    }

    [Fact]
    public void TwoRunsOfTheBlockedChuteScenarioAreByteIdentical()
    {
        static (string Log, double Load, double Current) Scenario()
        {
            Plant plant = Build();
            plant.StartUp();
            plant.Sim.InjectFaultAt(TimeSpan.FromSeconds(30), "Chute", TransferChute.Blockage);
            plant.Run(200.0);
            return (plant.Sim.Events.ToText(), plant.Sim.Telemetry.Read("CV001.Belt.Load"), plant.Conveyor.Output<double>("Current").Value);
        }

        var first = Scenario();
        var second = Scenario();

        Assert.Equal(first.Log, second.Log);
        Assert.Equal(first.Load, second.Load);
        Assert.Equal(first.Current, second.Current);
        Assert.Contains("OVERLOAD_TRIP", first.Log);
    }

    [Fact]
    public void ADifferentSeedChangesSensorNoiseButNotTheEventLog()
    {
        static (string Log, double Current) Scenario(ulong seed)
        {
            Plant plant = Build(seed);
            plant.StartUp();
            plant.Run(30.0);
            return (plant.Sim.Events.ToText(), plant.Conveyor.Output<double>("Current").Value);
        }

        var a = Scenario(1UL);
        var b = Scenario(2UL);

        Assert.Equal(a.Log, b.Log);
        Assert.NotEqual(a.Current, b.Current);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Dse.Components.Tests --filter "FullyQualifiedName~ConveyorTests"`
Expected: build error — `Dse.Components.Conveyors` does not exist.

- [ ] **Step 3: Implement**

`src/Dse.Components/Conveyors/ConveyorOptions.cs`:

```csharp
using Dse.Components.Mechanical;

namespace Dse.Components.Conveyors;

/// <summary>Everything a bulk conveyor is built from.</summary>
/// <param name="LengthM">m.</param>
/// <param name="CellSizeM">m; must divide the length and satisfy the CFL condition.</param>
/// <param name="BeltWidthM">m.</param>
/// <param name="AngleOfReposeDeg">Degrees, of the material carried.</param>
/// <param name="MaterialDensityKgM3">kg/m³, of the material carried.</param>
/// <param name="EmptyBeltMassKg">kg of belt and moving parts.</param>
/// <param name="FrictionCoefficient">Rolling friction, dimensionless.</param>
/// <param name="PulleyDiameterM">m, drive pulley.</param>
/// <param name="GearRatio">Motor turns per pulley turn.</param>
/// <param name="Motor">The motor's rating.</param>
/// <param name="TailDragN">N, tail pulley bearing drag.</param>
/// <param name="PullKeys">Number of pull-wire switches along the belt.</param>
/// <param name="SpeedMarginFraction">How far the belt's declared max speed exceeds the no-load speed.</param>
public sealed record ConveyorOptions(
    double LengthM,
    double CellSizeM,
    double BeltWidthM,
    double AngleOfReposeDeg,
    double MaterialDensityKgM3,
    double EmptyBeltMassKg,
    double FrictionCoefficient,
    double PulleyDiameterM,
    double GearRatio,
    MotorRating Motor,
    double TailDragN = 50.0,
    int PullKeys = 2,
    double SpeedMarginFraction = 0.1);
```

`src/Dse.Components/Conveyors/Conveyor.cs`:

```csharp
using System.Globalization;
using Dse.Components.Instruments;
using Dse.Components.Mechanical;
using Dse.Components.Safety;
using Dse.Core.Flow;
using Dse.Core.Graph;

namespace Dse.Components.Conveyors;

/// <summary>
/// A bulk conveyor: motor, gearbox, drive and tail pulleys, belt, speed
/// sensor, belt scale, current sensor, zero-speed switch, pull-keys, e-stop,
/// safety relay and starter, wired the way a real one is. Nothing here is
/// special to the engine; it is a composition, and it flattens to leaves at
/// build time. The causal chain — a blocked discharge loads the belt, raises
/// torque, raises current, heats the motor, trips the overload — is a
/// consequence of the wiring, not a rule written anywhere.
/// </summary>
public sealed class Conveyor : CompositeComponent
{
    private const double KgPerSecondToTonnesPerHour = 3.6;

    public Conveyor(string id, ConveyorOptions options)
        : base(id)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.PullKeys, nameof(options));
        ArgumentOutOfRangeException.ThrowIfNegative(options.SpeedMarginFraction, nameof(options));

        double radius = options.PulleyDiameterM / 2.0;
        double noLoadSpeed = options.Motor.RatedSpeedRadPerS / options.GearRatio * radius;
        double maxSpeed = noLoadSpeed * (1.0 + options.SpeedMarginFraction);
        double maxLinearDensity = BeltGeometry.MaxLinearDensity(
            options.BeltWidthM, options.AngleOfReposeDeg, options.MaterialDensityKgM3);

        Motor = AddChild(new Motor("Motor", options.Motor));
        Gearbox = AddChild(new Gearbox("Gearbox", options.GearRatio));
        Drive = AddChild(new DrivePulley("Drive", options.PulleyDiameterM));
        Tail = AddChild(new TailPulley("Tail", options.TailDragN));
        Friction = AddChild(new BeltFriction("Friction", options.EmptyBeltMassKg, options.FrictionCoefficient));
        Belt = AddChild(new BulkBelt("Belt", options.LengthM, options.CellSizeM, maxSpeed, maxLinearDensity));
        SpeedSensor = AddChild(new SpeedSensor("SpeedSensor", new InstrumentSpec("m/s", 0.0, maxSpeed * 1.2, NoiseSigma: 0.002)));
        Scale = AddChild(new BeltScale(
            "Scale",
            Belt,
            options.LengthM / 2.0,
            new InstrumentSpec("t/h", 0.0, maxLinearDensity * maxSpeed * KgPerSecondToTonnesPerHour, NoiseSigma: 0.3, LagSeconds: 1.0)));
        CurrentSensor = AddChild(new CurrentSensor("CurrentSensor", new InstrumentSpec("A", 0.0, options.Motor.RatedCurrentA * 8.0, NoiseSigma: 0.01)));
        ZeroSpeed = AddChild(new ZeroSpeedSwitch("ZeroSpeed", new InstrumentSpec("m/s", 0.0, maxSpeed * 1.2), thresholdSpeed: 0.02, delaySeconds: 1.0));
        EStop = AddChild(new EStop("EStop"));
        Safety = AddChild(new SafetyRelay("Safety", options.PullKeys + 1));
        Starter = AddChild(new MotorStarter("Starter"));

        var keys = new PullKey[options.PullKeys];
        for (int i = 0; i < keys.Length; i++)
        {
            keys[i] = AddChild(new PullKey(string.Create(CultureInfo.InvariantCulture, $"PullKey{i + 1}")));
        }

        PullKeys = keys;

        // Power and drive.
        Starter.Contactor.ConnectTo(Motor.Energised);
        Motor.Speed.ConnectTo(Gearbox.InputSpeed);
        Gearbox.OutputSpeed.ConnectTo(Drive.ShaftSpeed);
        Drive.BeltSpeed.ConnectTo(Belt.Speed);

        // Reflected load, back to the motor (latched at the gearbox and the motor).
        Belt.Load.ConnectTo(Friction.Load);
        Tail.Drag.ConnectTo(Friction.Drag);
        Friction.Force.ConnectTo(Drive.BeltForce);
        Drive.TorqueDemand.ConnectTo(Gearbox.OutputTorqueDemand);
        Gearbox.InputTorqueDemand.ConnectTo(Motor.TorqueDemand);

        // Protection.
        Motor.ThermalState.ConnectTo(Starter.ThermalState);
        Safety.Ok.ConnectTo(Starter.SafetyOk);
        for (int i = 0; i < keys.Length; i++)
        {
            keys[i].Ok.ConnectTo(Safety.Channel(i + 1));
        }

        EStop.Ok.ConnectTo(Safety.Channel(keys.Length + 1));

        // Instruments.
        Drive.BeltSpeed.ConnectTo(SpeedSensor.Speed);
        Drive.BeltSpeed.ConnectTo(Scale.Speed);
        Drive.BeltSpeed.ConnectTo(ZeroSpeed.Speed);
        Motor.Current.ConnectTo(CurrentSensor.Current);

        // The composite's face.
        Expose("In", Belt.In);
        Expose("Out", Belt.Out);
        Expose("Start", Starter.Command);
        Expose("Reset", Starter.Reset);
        Expose("SafetyReset", Safety.Reset);
        Expose("EStop", EStop.Actuated);
        for (int i = 0; i < keys.Length; i++)
        {
            Expose(string.Create(CultureInfo.InvariantCulture, $"PullKey{i + 1}"), keys[i].Actuated);
        }

        Expose("Speed", SpeedSensor.Value);
        Expose("TonnesPerHour", Scale.Value);
        Expose("Current", CurrentSensor.Value);
        Expose("Stopped", ZeroSpeed.Stopped);
        Expose("Contactor", Starter.Contactor);
        Expose("Tripped", Starter.Tripped);
        Expose("SafetyOk", Safety.Ok);
    }

    public Motor Motor { get; }

    public Gearbox Gearbox { get; }

    public DrivePulley Drive { get; }

    public TailPulley Tail { get; }

    public BeltFriction Friction { get; }

    public BulkBelt Belt { get; }

    public SpeedSensor SpeedSensor { get; }

    public BeltScale Scale { get; }

    public CurrentSensor CurrentSensor { get; }

    public ZeroSpeedSwitch ZeroSpeed { get; }

    public EStop EStop { get; }

    public IReadOnlyList<PullKey> PullKeys { get; }

    public SafetyRelay Safety { get; }

    public MotorStarter Starter { get; }
}
```

The children are qualified when `AddChild` runs, so every port connected
afterwards already carries the `"{id}.{child}"` owner id, and a leaf added
later (the pull-keys) is qualified the same way. Ports are wired
child-to-child inside the composite, which is allowed: wiring is not
evaluation, and the flattened leaves still never touch each other at run time.

- [ ] **Step 4: Run the tests**

Run: `dotnet test tests/Dse.Components.Tests`
Expected: 88 pass. Expected numbers, for when one is off:

| Quantity | Value |
|---|---|
| No-load belt speed | 150 / 20 × 0.25 = 1.875 m/s; ≈1.86 with droop |
| Max linear density | 81.7 kg/m; belt capacity 817 kg |
| Steady load at 20 kg/s | ≈108 kg; force ≈220 N; motor torque ≈2.9 N·m; current ≈1.41 A; θ → 0.5 |
| Full belt | force ≈499 N; motor torque ≈6.6 N·m (1.3 × rated); current ≈2.44 A; θ → 1.48 |
| Time from blockage to trip | ≈35 s to fill plus ≈45 s of heating: 70–110 s |

If the trip never comes, print `CV001.Motor.ThermalState` telemetry every
10 s; if it comes before the belt is more than half full, check the tail drag
and friction coefficient against the table.

- [ ] **Step 5: Document**

Append to `docs/architecture.md`, after "Feedback loops":

```markdown
## Latched inputs

A component that knows one of its inputs is a *reflection* — the torque a
load pushes back up a shaft — can declare that input `latched: true`. A
latched input reads the value captured in the latch pass of the previous
tick and creates no ordering edge, so a chain of bidirectional mechanical
elements (motor ⇄ gearbox ⇄ drive pulley) resolves without a `UnitDelay`.
`UnitDelay` remains the wiring-time tool for loops between components that
did not anticipate them. Belts declare no direct feedthrough at all: their
outputs (load, item count) come from material that moves only in phase 3.
```

Append to the "Material flow" section:

```markdown
Two more rules for flow-node authors, both consequences of the phase
structure. **Material changes only in `Advance`.** A source creates mass, a
former cuts pieces, a process unit fills, holds and releases — all in phase
3, never in `Evaluate`. `Evaluate` only publishes outputs from the frozen
state. **Instruments read a node through `IMaterialObservable`.** Because
nothing moves in phase 2, a belt scale or a pyrometer may hold a reference
to the node it is mounted on and call `TryObserve` in its `Evaluate`,
whatever the evaluation order. It may call nothing else on the node.
```

Add a new section before "Determinism rules":

```markdown
## Faults

A component that can be broken implements `IFaultTarget`: it publishes
`FaultDescriptor`s (id, description, parameters with defaults) and accepts
`ApplyFault` / `ClearFault`. `Simulation.InjectFaultAt` resolves the target,
the fault and the arguments when the fault is *scheduled*, so a mistake fails
at the call site naming what exists, and delivers it through the event queue
in phase 1 of the due tick, logging `FAULT` / `FAULT_CLEARED`. Faults change
state and behaviour only; the graph is immutable, so injection cannot
perturb evaluation order.

Every instrument derives from `InstrumentBase` and gets calibration, noise,
drift, lag, freeze, fail-high and fail-low for free; its `Truth` telemetry
is what it should have read. Physical faults — bearing friction, belt slip,
a welded contactor, a blocked chute — are declared per component.
```

Replace the "Status" paragraph of `README.md` with:

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
sensor-fault vocabulary, six instruments, a motor with an I²t thermal model,
a drivetrain, a safety circuit, a starter, and a `Conveyor` composite that
trips its own overload when the belt downstream of it blocks.

The I/O and real-time layers, declarative configuration and the reference
samples are planned.
```

- [ ] **Step 6: Run everything, Release build, commit**

```bash
dotnet build --configuration Release
dotnet test
```

Expected: zero warnings; 221 Core tests and 88 Components tests pass (309).

```bash
git add src/Dse.Components/Conveyors tests/Dse.Components.Tests/ConveyorTests.cs docs/architecture.md README.md
git commit -m "feat(components): add the conveyor composite and prove the overload causal chain end to end"
```

---

## Definition of done for this plan

- `dotnet test` passes: 204 from plans 1 and 2, 17 new in `Dse.Core.Tests`,
  88 in `Dse.Components.Tests` — 309 in all.
- `dotnet build --configuration Release` produces zero warnings.
- `Dse.Core`, `Dse.Io.Abstractions` and `Dse.Components` have no external
  package references; `Dse.Components` references `Dse.Core` only.
- A conveyor built purely by composition, fed at a steady rate, trips its own
  overload after the chute downstream of it blocks — belt load, torque
  demand, current and thermal state all rise first, in that order — then
  coasts to zero speed, the zero-speed switch fires and the scale reads zero.
  No component in the chain knows about any other.
- A pull-key stops the same conveyor with no controller connected, and the
  line restarts only after a safety reset.
- An injected thermal bias trips the same relay through the same path on the
  tick it lands.
- Two runs of the blocked-chute scenario produce byte-identical event logs;
  a different seed changes sensor noise and nothing in the log.
- Every instrument answers the whole sensor-fault vocabulary; every fault
  target publishes descriptors, and a wrong component id, fault id or
  parameter name fails at scheduling time naming what exists.
- Bulk and item process units fill by recipe or count, hold on time or on a
  property or an accumulated state, re-type on release, and book yield loss
  as a declared loss; the conservation audit holds through all of it.
- `docs/architecture.md` documents latched inputs, the two flow-node rules,
  material observation and the fault channel.

## What this plan deliberately does not build

Tag bindings, the I/O image, `Quality`, tick frames and everything in the
real-time layer (plan 4) — `InstrumentHealth` is the hook plan 4 maps to
quality. The component catalogue, JSON configuration, the CLI and scenario
files (plan 5) — `FaultDescriptor` is what the catalogue will export. The
mine-conveyor and wheel-line samples and any controller (plan 6 and
`Dse.Control`) — the conveyor test here uses a hand-held start switch, not a
sequencer. A discrete-mode `Conveyor` composite: the wheel line composes one
from `DiscreteBelt` and the same drivetrain in plan 6. The `Disintegrator`,
splitters, mergers, and every component in spec 18. Plausibility checks
that would emit `InstrumentHealth.Uncertain`. Mass change by transforms
(R17). Bulk-cell state arrays, still deferred until a process needs one.
