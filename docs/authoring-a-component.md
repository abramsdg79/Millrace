# Authoring a component

The framework's job is to make the plumbing free, so that the physics of your
domain is the only real work. This is the plumbing, in the order you will meet
it. The worked example is
[`HysteresisSwitch`](../tests/Millrace.Cli.Tests.SampleModule/HysteresisSwitch.cs);
its numbered comments illustrate the sections below. For a component with faults,
telemetry and material, read [`TransferChute`](../src/Millrace.Components/Flow/TransferChute.cs).

## 1. Decide what it is

| It… | Derive from | You get |
|---|---|---|
| computes signals from signals | `ComponentBase` | `AddInput<T>`, `AddOutput<T>`, `Evaluate` |
| holds or moves material | `FlowComponentBase` | the above, plus `AddInlet`, `AddOutlet`, `Advance`, `MassHeld` |
| measures something | `InstrumentBase` | range, noise, lag, quality and the whole sensor-fault vocabulary, already working |
| is several components wired together | `CompositeComponent` | `AddChild`, `Expose`; it is flattened away at build time |

A component cannot reach a sibling. Everything it knows arrives on a port or
through a constructor argument; that is what makes it testable alone.

`HysteresisSwitch` computes a signal (`On`) from a signal (`Value`) and touches
no material and measures nothing of its own — it derives from `ComponentBase`,
and its `ComponentCategory` (below, at the descriptor) is `Signal`, not `Flow`
or `Instrumentation`, for the same reason.

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
plant loaded from JSON that message is shown to the author as `MR111`, so write
it for someone who has never seen your source.

## 4. Declare ports once, in the constructor

```csharp
Value = AddInput<double>("Value");                       // optional, defaults to 0
Permit = AddInput<bool>("Permit", defaultValue: true);   // optional, defaults to true
Feed = AddInput<double>("Feed", required: true);         // MR002 if nothing drives it
Load = AddInput<double>("Load", latched: true);          // reads last tick's value; creates no ordering edge
On = AddOutput<bool>("On");
```

The graph is immutable after `Build()`. If your component reads an input and
that input depends, this same tick, on your own output, you have an algebraic
loop (`MR003`). Break it where the physics allows a tick of delay: declare the
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
`ClearFault`, switching on the fault id: a component's faults are independent
and may be active together, as `item-process-unit`'s `discharge-jam` and
`slow-cycle` are. Faults arrive through the event queue at a tick boundary. An
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

`Descriptor` is a `static` property, not an instance member: it describes the
*type* — one row in the catalogue, one schema entry — not any particular
switch, so one instance shared by every caller is correct, and it must exist
before any `HysteresisSwitch` does, since `catalog export` and `schema export`
read it without building a plant.

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

Public, concrete, parameterless: that is what `millrace --assembly` looks for. In
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
is quiet, `millrace catalog export --assembly yours.dll` describes your component
truthfully, `millrace schema export` validates plants that use it, and
`millrace validate` loads them.

Then test the physics — that part is yours.

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
- **The factory** takes the id, the scan period and the resolved parameters,
  and must build a block with exactly that id and scan period. The tags in
  `OwnedTags` must match the block's `Outputs` (read-only) and `Commands`
  (read-write), or the loader reports MR111 naming the module.
- **`Param.Tag(name, description, kind, writes)`** is a tag name the loader
  resolves: give `kind` when the block needs one, and `writes: true` when the
  block commands the tag. **`Param.Value(name, description, tagParameter)`** is
  a value the loader converts to the kind of its sibling tag parameter. A
  component may not declare either.
- Register it with `builder.AddBlock(Latch.Descriptor)`. Block and component
  types share one namespace.
- Prove it with `new ConformanceFixtures().BlockParameters("latch", """{ … }""")`
  — call it again to check the type with several fixtures. Conformance compares
  `OwnedTags` with the instance's `Outputs` and `Commands` by name, kind,
  access, unit and description.
