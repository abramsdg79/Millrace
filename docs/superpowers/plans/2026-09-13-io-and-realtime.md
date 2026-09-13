# I/O and Real-Time Layer Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give the engine its front door and its only exit — a printable tag
directory with units, ranges and per-tag quality; a double-buffered I/O image
that any thread may read; a queued write path that lands at phase 1 of the next
tick; an immutable `TickFrame` per tick; and, downstream of that boundary, a
`Dse.Realtime` package (event engine, live state engine, subscriptions with
deadbands, decimation and declared backpressure, a command bus) that cannot
perturb the simulation whether zero or fifty consumers are attached.

**Architecture:** `Dse.Io.Abstractions` gains the whole I/O contract and nothing
else: `TagValue`, `TagQuality`, `TagDescriptor`, `ITagDirectory`, `ITagReader`,
`ITagWriter`, `TickFrame`, `ITickFrameSink`. `Dse.Core` implements it: leaf
components declare `TagBinding`s with relative names and metadata, composites
rename through their existing `Expose` aliases, the builder collects them into a
`TagDirectory` and a `TagImage`, and `Simulation` gains three real phases —
external writes drained at phase 1, the image published at phase 4, a frame
enqueued at phase 5. `Dse.Realtime` references `Dse.Io.Abstractions` only and
consumes frames: a `RealtimeHub` owns the ring buffer and the pump, a `LiveState`
holds current truth for late joiners, `Subscription`s pull deltas under
`Conflate` or `Lossless` policy, and a `CommandBus` validates writes against the
directory before handing them to `ITagWriter`.

**Tech Stack:** .NET 10 (`net10.0`), C#, xUnit. No external runtime dependencies.

**Spec:** `docs/superpowers/specs/2026-09-02-industrial-process-simulation-engine-design.md`
(sections 5.2 phases 1, 4 and 5; 5.4 "the graph is immutable after validation";
9 in full; 10 in full; 17 "Io" and "Realtime"; the I/O and subscription lines of
16).

**Plan sequence:** This is plan 4 of 6. Plans 1, 2 and 3 are merged on
`master` at `c015b7e` (312 tests). Plan 5 adds the catalogue, configuration,
CLI, scenarios and the control blocks; plan 6 the two reference samples.
Nothing in this plan may reference those subsystems: no JSON, no controller, no
alarm table, no scenario recorder. Where plan 5 will need a hook (the write
timeline, command recording, active alarm state) this plan leaves a named seam
and says so.

## Global Constraints

- Target framework `net10.0` for every project. `Dse.Io.Abstractions` references
  nothing. `Dse.Core` references `Dse.Io.Abstractions`. `Dse.Components`
  references `Dse.Core`. **`Dse.Realtime` references `Dse.Io.Abstractions` only
  — never `Dse.Core`, never `Dse.Components`.** The test project
  `Dse.Realtime.Tests` may additionally reference `Dse.Components` for the one
  end-to-end test (Task 13); that is a test-only edge and is documented as such.
  **Zero external runtime package references** in any shipping project. Test
  projects use the same test package versions as
  `tests/Dse.Core.Tests/Dse.Core.Tests.csproj`.
- `Nullable` enabled, `TreatWarningsAsErrors` true, `GenerateDocumentationFile`
  true (a `<see cref>` to a type that does not exist yet is a **build error**;
  reference only types that already exist when the file is compiled),
  deterministic builds. These come from `Directory.Build.props`; a csproj
  repeats only `TargetFramework`, `ImplicitUsings` and `Nullable` (the three the
  existing csproj files already repeat — match them, do not add more).
- **Never use `System.Random`. Never use `string.GetHashCode()`** for anything
  that affects behaviour.
- **Never iterate a `Dictionary` or `HashSet` in per-tick code.** The tag
  directory's name lookup is a dictionary and is used only at resolve time
  (`Handle<T>`, `Read(string)`, `Write(string, …)`, `Subscribe`). Per-tick code
  in `TagImage.Publish`, `TagImage.ApplyPendingWrites`, `RealtimeHub.Pump`,
  `LiveState.Apply` and `Subscription` walks arrays by index only.
- **The simulation thread never blocks on a consumer.** `ITickFrameSink.Publish`
  is one non-blocking enqueue. If the ring is full the frame is dropped and
  counted; the simulation does not wait. `TagImage` reads take no lock; the
  published buffer is immutable after the volatile swap.
- **Frames are immutable.** A `TickFrame` and every array it exposes are never
  written after `Publish`. `TagImage` allocates a fresh values array and a fresh
  dirty mask every tick; pooling is deferred (spec 18).
- **Tag names are ordinal strings** in the form `Component.Sub.Port`; a
  composite's exposed alias replaces the leaf's segment(s) below the composite
  id. The directory is sorted by ordinal name and indices are assigned in that
  order at `Build()`. Indices are stable for the life of one `Simulation` and
  are not stable across builds — a consumer resolves names once per session.
- All formatting and parsing uses `CultureInfo.InvariantCulture`. Messages that
  embed a `double` use `string.Create(CultureInfo.InvariantCulture, $"...")`.
- Event codes are UPPER_SNAKE. Messages are human sentences ending in a full
  stop. Tag descriptions are sentence fragments without a full stop (they become
  OPC UA `Description` later).
- xUnit analyzers run under warnings-as-errors: prefer `Assert.Single`,
  `Assert.Contains`, `Assert.Empty` over `Assert.True(x.Any())` and
  `Assert.Equal(1, x.Count())`.
- Licence: MIT. Namespace root `Dse`. Contract namespace `Dse.Io`. Core
  additions live in `Dse.Core.Io`. Real-time namespace `Dse.Realtime`.
- Commit trailers: every commit message body ends with the two attribution
  lines the session specifies (`Co-Authored-By: …` and `Claude-Session: …`),
  copied verbatim, in the body, never on the subject line: subject, blank line,
  then the two lines.

## Decisions settled here (carry forward as rulings R20–R27)

- **R20 — `InstrumentHealth` is replaced by `Dse.Io.TagQuality`.** Plan 3 kept
  `Dse.Components` free of `Quality` because the contract did not exist. It
  exists now, `Dse.Components` already sees `Dse.Io` transitively through
  `Dse.Core`, and a second enum that "maps onto" quality is a translation table
  nobody needs. `InstrumentBase.Health` becomes `OutputPort<TagQuality>` and the
  `InstrumentHealth` enum is deleted. `Uncertain` is emitted when the unclamped
  reading leaves the instrument's range without a fail fault — the saturated
  transmitter — with detail `OutOfRange`. Fail-high and fail-low are `Bad` with
  detail `SensorFailure`. A frozen instrument stays `Good`: a frozen reading is
  plausible, which is exactly why it is dangerous.
- **R21 — tags are what a plant measures or commands; truth stays telemetry.**
  Instruments, the starter, the safety circuit, sources, sinks, the chute, the
  former and the process units declare tags. The motor, gearbox, pulleys,
  friction element and belts declare none: their outputs are the god view, and a
  SCADA that could read true motor current would not need the current sensor.
  A plant author who wants a truth value on the wire binds it explicitly with
  `SimulationBuilder.Bind`, and the directory shows it.
- **R22 — leaves declare, composites rename, the plant may add.** A leaf
  component implements `ITagProvider` and returns bindings with names relative
  to itself; the builder prefixes them with the component id. A composite's
  existing `Expose(alias, port)` is the rename: an exposed signal port's tag is
  `CompositeId.Alias` instead of `CompositeId.Leaf.Port`. Nested composites are
  applied inside-out, so the outermost alias wins. `SimulationBuilder.Bind(name,
  binding)` adds a binding for a port nothing declared or replaces the declared
  one. One binding per port, always, so the directory has no duplicates.
- **R23 — an input port may be driven from outside.** A writable binding marks
  its `InputPort<T>` as externally driven at `Build()`. The port reads the
  externally driven value when no output is connected. A *declared* writable
  binding on an input that an output already drives degrades to read-only at
  `Build()`: the plant wired a controller there, so the tag observes the
  command instead of issuing it, and the directory says `ReadOnly`. An
  *explicit* `Bind` of a writable binding on a driven input is validation error
  `DSE010`, because the author asked for something the wiring forbids. There is
  no `TagInput` component and no ordering edge; the tag list is the only record
  that the plant has an operator.
- **R24 — writes drain before scheduled events.** Phase 1 applies every write
  queued since the previous phase 1, in enqueue order, then drains due events.
  Each applied write is logged as event code `WRITE` with the tag name as source
  and the value as message, so the write timeline is in the event stream for
  plan 5's scenario recorder. The queue is drained up to the count observed on
  entry, so a producer writing continuously cannot starve the tick.
- **R25 — quality folds into the value tag.** A binding may name an
  `OutputPort<TagQuality>` as its quality source; the tag's value and quality
  are captured together. There is no separate health tag. Tags with no quality
  source are always `Good`.
- **R26 — the ring drops the newest frame on overflow.** The hub's ring buffer
  is single-producer (the simulation thread), single-consumer (the pump). When
  it is full, `Publish` discards the incoming frame, increments `DroppedFrames`
  and raises a gap flag that faults every `Lossless` subscription on the next
  pump, with the reason "ring overflow". `Conflate` subscriptions survive a
  gap because they diff against the values they last delivered, never against
  the frame's dirty mask. Dropping newest rather than oldest keeps the producer
  from ever touching the consumer's index.
- **R27 — consumers pull.** A `Subscription` never invokes a consumer callback
  on the pump thread. It queues deltas under its declared policy and the
  consumer calls `TryRead` or waits on `Available`. A slow consumer therefore
  stalls neither the simulation nor other subscribers; under `Lossless` it is
  faulted at capacity, under `Conflate` it loses intermediate values only.

## Execution rulings (made while running this plan, 2026-09-13)

The code is the authority where it differs from the task text below.

- **R28 — `TagImage.Snapshot()`.** Task 5's threaded test read two tags with two
  independent `Read(int)` calls and could straddle a `Publish` swap; spec 9.3
  guarantees a consistent snapshot *per read*, not across reads. `TagImage`
  gained `public ReadOnlyMemory<TagValue> Snapshot()` (Core only; `ITagReader`
  is unchanged) and the test reads both tags from one snapshot.
- **R29 — a saturated reading was already `Uncertain` in plan 3's tests.**
  `InstrumentBaseTests.ReadingsAreClampedToTheRange` (truth 12 in range 0..10)
  asserts `Uncertain:OutOfRange`, not the mechanical `Good` swap Task 7 listed.
- **R30 — `RealtimeHub.Publish` never signals a disposed hub.** `_disposed` is
  `volatile`, `Publish` returns after the counters when disposed and wraps
  `Set()` in a `catch (ObjectDisposedException)`; a frame published after
  `Dispose` is counted, never signalled, never throws.
- **R31 — writable tags satisfy a required input at validation time.** The
  builder collects tags before the `DSE002` check and skips it for any port a
  `ReadWrite` binding drives; the plan's Task 6 ordering (collect last) made
  R23 unreachable for `required: true` inputs.
- **R32 — a ring gap faults only subscriptions that existed when it happened.**
  `Subscribe` stamps the hub's `DroppedFrames`; `NotifyGap` receives the
  running total and faults a Lossless subscription only when it has advanced
  past the stamp. A subscriber attached after an overflow burst is not
  disconnected on its first pump.
- **Follow-ups parked at the final review (not built):** `SnapshotTick` and
  `Snapshot()` are not read atomically (frame consumers use `TickFrame.Tick`);
  `RealtimeHub.Pending` can read negative from a third thread;
  `DispatcherThread.Dispose` waits up to `idleWait`; a dispatcher whose `Pump`
  throws records `Failure` and goes silent — add observability before a
  protocol adapter exists; `Subscription.Dispose` must not race a waiter on
  `Available`; `AttachFrameSink` is documented "any time" but the field is not
  volatile.
- **Test text corrections:** `Assert.Single(first.Changes)` replaces the
  xUnit2013-violating `Assert.Equal(1, …Count)` in Task 10;
  `state.Get(…).Value.Quality` replaces `state.Get(…).Quality` in Task 13
  (`TagState` carries quality inside `Value`). Task 11 and 12 test counts are
  36 and 49 (one more than stated) because of Task 10's added
  `PublishAfterDisposeDoesNotThrow`.

## Plan-1 to plan-3 facts this plan builds on

- `Simulation.Tick()` already calls `DrainDueEvents`, `EvaluateSignals`,
  `AdvanceFlow`, `PublishIo` (empty) and `EmitFrame` (empty) in that order and
  then `Clock.Advance()`. `Clock.TickCount` during a tick is the index of the
  tick being computed (0 on the first call).
- `Simulation.Initialize()` runs once, before the first tick, and is idempotent.
- `Port.QualifiedName` is `OwnerId.Name`; `ComponentBase.Ports` lists ports in
  declaration order; `CompositeComponent.Expose(alias, port)` stores aliases
  in a private dictionary and `Leaves()` flattens recursively.
- `InputPort<T>.Value` returns the captured value for latched inputs and the
  live source value otherwise; `DefaultValue` applies when nothing is connected.
  `ConnectFrom` throws if a source is already set.
- `EventLog.Records` is an append-only `IReadOnlyList<SimEventRecord>`;
  `SimEventRecord(long Tick, DateTimeOffset SimTime, string Source, string
  Code, string Message)`.
- Validation codes `DSE001`–`DSE008` exist. This plan adds `DSE009`, `DSE010`,
  `DSE011`.
- `InstrumentBase` sets `Value.Value` and `Health.Value` in `Evaluate` after
  calibration, drift, noise, lag, freeze and fail handling; `Spec` carries
  `Unit`, `RangeLow`, `RangeHigh`.
- The `Conveyor` composite exposes `In`, `Out` (flow), `Start`, `Reset`,
  `SafetyReset`, `EStop`, `PullKey1..N` (inputs), `Speed`, `TonnesPerHour`,
  `Current`, `Stopped`, `Contactor`, `Tripped`, `SafetyOk` (outputs).
- Test helpers: `tests/Dse.Core.Tests/Fakes/ConstantSource` (double out),
  `Recorder` (double in, `Samples`), `tests/Dse.Components.Tests/Fakes/Switch`
  (bool out, settable `Value`), `Setpoint` (double out), `TestContexts`.

## File Structure

```
src/Dse.Io.Abstractions/
  Placeholder.cs                         (deleted)
  Quality.cs                             Quality, QualityDetail, TagQuality
  TagKind.cs                             Bool | Double | Int64
  TagAccess.cs                           ReadOnly | ReadWrite
  TagValue.cs                            kind + bits + quality; typed factories and accessors
  TagDescriptor.cs                       index, name, kind, access, unit, range, description
  ITagDirectory.cs                       ordered descriptors, name lookup
  TagHandle.cs                           TagHandle<T>: resolved index + name
  ITagReader.cs                          Directory, SnapshotTick, Read(index), Read(name), Handle<T>
  ITagWriter.cs                          Write(index, value), Write(name, value)
  TagAccessExtensions.cs                 ReadBool/ReadDouble/ReadInt64, WriteBool/…, by name and handle
  DiscreteEvent.cs                       tick, sim time, source, code, message
  DirtyMask.cs                           readonly bitset over ReadOnlyMemory<ulong>
  TickFrame.cs                           the determinism boundary
  ITickFrameSink.cs                      Publish(frame), non-blocking

src/Dse.Core/
  Graph/Port.cs                          (modified: Freeze, IsFrozen)
  Graph/InputPort.cs                     (modified: external driver, frozen ConnectFrom)
  Graph/CompositeComponent.cs            (modified: internal ExposedSignalPorts, Composites())
  Io/ITagProvider.cs                     DescribeTags()
  Io/EnumBits.cs                         enum → long without boxing
  Io/TagBinding.cs                       port + relative name + metadata + capture/apply
  Io/TagDirectory.cs                     ITagDirectory over a sorted TagBinding[]
  Io/TagImage.cs                         ITagReader + ITagWriter: double buffer, dirty mask, write queue
  SimulationBuilder.cs                   (modified: Bind, tag collection, DSE009–011, freeze)
  Simulation.cs                          (modified: IO, AttachFrameSink, phases 1/4/5)

src/Dse.Components/
  Instruments/InstrumentHealth.cs        (deleted — R20)
  Instruments/InstrumentBase.cs          (modified: TagQuality health, Uncertain, ITagProvider)
  Instruments/ZeroSpeedSwitch.cs         (modified: Stopped tag)
  Instruments/PartCounter.cs             (modified: Count, Present tags)
  Mechanical/MotorStarter.cs             (modified: Command, Reset, Contactor, Tripped tags)
  Safety/SafetySwitch.cs                 (modified: Actuated, Ok tags)
  Safety/SafetyRelay.cs                  (modified: Reset, Ok tags)
  Flow/BulkSource.cs, BulkSink.cs, ItemSource.cs, ItemSink.cs,
  Flow/TransferChute.cs, Former.cs, BulkProcessUnit.cs, ItemProcessUnit.cs
                                         (modified: tags per R21)

src/Dse.Realtime/
  Dse.Realtime.csproj                    references Dse.Io.Abstractions only
  TagState.cs                            value + last-change tick and time
  StateSnapshot.cs                       tick, sim time, TagState[], recent events
  LiveState.cs                           current truth; Apply(frame); Snapshot()
  BackpressurePolicy.cs                  Conflate | Lossless
  SubscriptionOptions.cs                 policy, capacity, prefixes, deadbands, decimation
  TagChange.cs                           index + value
  FrameDelta.cs                          tick, sim time, changes, events
  Subscription.cs                        per-subscriber filter, deadband, decimation, queue
  FrameRing.cs                           SPSC ring, drop-newest
  RealtimeHub.cs                         ITickFrameSink; Pump; Subscribe (no-gap)
  DispatcherThread.cs                    background loop calling Pump
  CommandOutcome.cs                      Accepted | UnknownTag | KindMismatch | ReadOnly | OutOfRange
  TagCommand.cs                          name, value, outcome
  ICommandRecorder.cs                    plan 5's seam
  CommandBus.cs                          validate against the directory, forward to ITagWriter

tests/Dse.Io.Abstractions.Tests/
  Dse.Io.Abstractions.Tests.csproj
  TagValueTests.cs, TagQualityTests.cs, DirtyMaskTests.cs,
  TagAccessExtensionsTests.cs, Fakes/ArrayTagReader.cs

tests/Dse.Core.Tests/
  ExternalInputTests.cs, GraphFreezeTests.cs, TagBindingTests.cs,
  TagImageTests.cs, IoIntegrationTests.cs,
  Fakes/Thermostat.cs, Fakes/Pair.cs, Fakes/FrameCollector.cs

tests/Dse.Components.Tests/
  InstrumentBaseTests.cs                 (modified: TagQuality assertions)
  SignalInstrumentTests.cs               (modified)
  InstrumentQualityTests.cs, ComponentTagTests.cs, ConveyorIoTests.cs

tests/Dse.Realtime.Tests/
  Dse.Realtime.Tests.csproj              references Dse.Realtime and Dse.Components (test-only)
  Fakes/Frames.cs                        hand-built directory and frames
  LiveStateTests.cs, RealtimeHubTests.cs, SubscriptionPolicyTests.cs,
  DispatcherThreadTests.cs, CommandBusTests.cs, ConveyorRealtimeTests.cs

docs/architecture.md                     (modified: "The I/O image", "The real-time boundary")
README.md                                (modified: status)
```

---

### Task 1: Quality and TagValue

**Files:**
- Delete: `src/Dse.Io.Abstractions/Placeholder.cs`
- Create: `src/Dse.Io.Abstractions/Quality.cs`
- Create: `src/Dse.Io.Abstractions/TagKind.cs`
- Create: `src/Dse.Io.Abstractions/TagAccess.cs`
- Create: `src/Dse.Io.Abstractions/TagValue.cs`
- Create: `tests/Dse.Io.Abstractions.Tests/Dse.Io.Abstractions.Tests.csproj`
- Test: `tests/Dse.Io.Abstractions.Tests/TagQualityTests.cs`
- Test: `tests/Dse.Io.Abstractions.Tests/TagValueTests.cs`
- Modify: `Dse.sln` (add the test project with `dotnet sln add`)

**Interfaces:**
- Consumes: nothing.
- Produces: `enum Quality { Good, Uncertain, Bad }`, `enum QualityDetail { None,
  SensorFailure, OutOfRange }`, `readonly record struct TagQuality(Quality Code,
  QualityDetail Detail)` with `static readonly TagQuality Good`,
  `static TagQuality Uncertain(QualityDetail)`, `static TagQuality Bad(QualityDetail)`,
  `bool IsGood`; `enum TagKind { Bool, Double, Int64 }`; `enum TagAccess
  { ReadOnly, ReadWrite }`; `readonly record struct TagValue` with
  `TagKind Kind`, `TagQuality Quality`, `static TagValue Bool(bool, TagQuality = default)`,
  `static TagValue Double(double, TagQuality = default)`,
  `static TagValue Int64(long, TagQuality = default)`, `bool AsBool`,
  `double AsDouble`, `long AsInt64`, `TagValue WithQuality(TagQuality)`,
  `bool ValueEquals(TagValue)`, `static TagKind KindOf<T>()`.

`default(TagQuality)` is `(Good, None)` because both enums start at `Good`/`None`
— so every factory's optional quality defaults to Good without a sentinel.
`TagValue` is 16 bytes: kind, 8 bits of payload, quality. Equality (the record
struct's) compares kind, bits and quality, which is exactly what the dirty mask
needs: a quality change alone is a change. Doubles are compared by bit pattern,
so `NaN == NaN` for dirty purposes and `-0.0 != 0.0`; both are what a historian
wants.

- [ ] **Step 1: Create the test project and add it to the solution**

`tests/Dse.Io.Abstractions.Tests/Dse.Io.Abstractions.Tests.csproj`:

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
    <ProjectReference Include="..\..\src\Dse.Io.Abstractions\Dse.Io.Abstractions.csproj" />
  </ItemGroup>

</Project>
```

Run: `dotnet sln Dse.sln add tests/Dse.Io.Abstractions.Tests/Dse.Io.Abstractions.Tests.csproj --solution-folder tests`

Also add `<InternalsVisibleTo Include="Dse.Io.Abstractions.Tests" />` to the
existing `ItemGroup` in `src/Dse.Io.Abstractions/Dse.Io.Abstractions.csproj`
(keep the existing `Dse.Core.Tests` entry).

- [ ] **Step 2: Write the failing tests**

`tests/Dse.Io.Abstractions.Tests/TagQualityTests.cs`:

```csharp
using Dse.Io;

namespace Dse.Io.Abstractions.Tests;

public class TagQualityTests
{
    [Fact]
    public void DefaultIsGoodWithNoDetail()
    {
        TagQuality quality = default;

        Assert.Equal(TagQuality.Good, quality);
        Assert.True(quality.IsGood);
        Assert.Equal(Quality.Good, quality.Code);
        Assert.Equal(QualityDetail.None, quality.Detail);
    }

    [Fact]
    public void BadAndUncertainCarryTheirDetail()
    {
        TagQuality bad = TagQuality.Bad(QualityDetail.SensorFailure);
        TagQuality uncertain = TagQuality.Uncertain(QualityDetail.OutOfRange);

        Assert.False(bad.IsGood);
        Assert.Equal((Quality.Bad, QualityDetail.SensorFailure), (bad.Code, bad.Detail));
        Assert.Equal((Quality.Uncertain, QualityDetail.OutOfRange), (uncertain.Code, uncertain.Detail));
    }

    [Fact]
    public void ToStringNamesCodeAndDetail()
    {
        Assert.Equal("Good", TagQuality.Good.ToString());
        Assert.Equal("Bad:SensorFailure", TagQuality.Bad(QualityDetail.SensorFailure).ToString());
    }
}
```

`tests/Dse.Io.Abstractions.Tests/TagValueTests.cs`:

```csharp
using Dse.Io;

namespace Dse.Io.Abstractions.Tests;

public class TagValueTests
{
    [Fact]
    public void BoolRoundTrips()
    {
        TagValue value = TagValue.Bool(true);

        Assert.Equal(TagKind.Bool, value.Kind);
        Assert.True(value.AsBool);
        Assert.Equal(TagQuality.Good, value.Quality);
    }

    [Fact]
    public void DoubleRoundTripsExactly()
    {
        TagValue value = TagValue.Double(12.3456789012345);

        Assert.Equal(TagKind.Double, value.Kind);
        Assert.Equal(12.3456789012345, value.AsDouble);
    }

    [Fact]
    public void Int64RoundTrips()
    {
        TagValue value = TagValue.Int64(long.MinValue + 7);

        Assert.Equal(TagKind.Int64, value.Kind);
        Assert.Equal(long.MinValue + 7, value.AsInt64);
    }

    [Fact]
    public void AccessorOfTheWrongKindThrows()
    {
        TagValue value = TagValue.Double(1.0);

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => value.AsBool);
        Assert.Contains("Double", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Bool", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void QualityIsPartOfEquality()
    {
        TagValue good = TagValue.Double(1.0);
        TagValue bad = TagValue.Double(1.0, TagQuality.Bad(QualityDetail.SensorFailure));

        Assert.NotEqual(good, bad);
        Assert.True(good.ValueEquals(bad));
        Assert.Equal(good, bad.WithQuality(TagQuality.Good));
    }

    [Fact]
    public void NaNEqualsNaNAndNegativeZeroDiffersFromZero()
    {
        Assert.Equal(TagValue.Double(double.NaN), TagValue.Double(double.NaN));
        Assert.NotEqual(TagValue.Double(-0.0), TagValue.Double(0.0));
    }

    [Fact]
    public void DefaultIsFalseBool()
    {
        TagValue value = default;

        Assert.Equal(TagKind.Bool, value.Kind);
        Assert.False(value.AsBool);
        Assert.True(value.Quality.IsGood);
    }

    [Fact]
    public void KindOfMapsTheThreeSupportedTypes()
    {
        Assert.Equal(TagKind.Bool, TagValue.KindOf<bool>());
        Assert.Equal(TagKind.Double, TagValue.KindOf<double>());
        Assert.Equal(TagKind.Int64, TagValue.KindOf<long>());
        Assert.Throws<NotSupportedException>(() => TagValue.KindOf<int>());
    }

    [Fact]
    public void ToStringIsInvariantAndShowsNonGoodQuality()
    {
        Assert.Equal("true", TagValue.Bool(true).ToString());
        Assert.Equal("2.5", TagValue.Double(2.5).ToString());
        Assert.Equal("42", TagValue.Int64(42).ToString());
        Assert.Equal("2.5 [Bad:SensorFailure]", TagValue.Double(2.5, TagQuality.Bad(QualityDetail.SensorFailure)).ToString());
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/Dse.Io.Abstractions.Tests --filter "FullyQualifiedName~TagValueTests|FullyQualifiedName~TagQualityTests"`
Expected: build FAILS — `TagValue`, `TagQuality`, `Quality` do not exist.

- [ ] **Step 4: Write the types**

Delete `src/Dse.Io.Abstractions/Placeholder.cs`.

`src/Dse.Io.Abstractions/Quality.cs`:

```csharp
namespace Dse.Io;

/// <summary>
/// The quality code carried by every tag value (spec 9.5). Ordered so that
/// <c>default</c> is <see cref="Good"/>.
/// </summary>
public enum Quality
{
    /// <summary>The value is trustworthy.</summary>
    Good,

    /// <summary>The value is present but failed a plausibility check.</summary>
    Uncertain,

    /// <summary>The value must not be used.</summary>
    Bad,
}

/// <summary>The sub-status that explains a non-good quality.</summary>
public enum QualityDetail
{
    /// <summary>No further information.</summary>
    None,

    /// <summary>The transmitter has failed (fail-high, fail-low, disconnected).</summary>
    SensorFailure,

    /// <summary>The reading left the instrument's declared range.</summary>
    OutOfRange,
}

/// <summary>A quality code with its detail. <c>default</c> is Good.</summary>
public readonly record struct TagQuality(Quality Code, QualityDetail Detail)
{
    /// <summary>Good with no detail — the value every healthy tag carries.</summary>
    public static readonly TagQuality Good = new(Quality.Good, QualityDetail.None);

    /// <summary>True when <see cref="Code"/> is <see cref="Quality.Good"/>.</summary>
    public bool IsGood => Code == Quality.Good;

    /// <summary>Uncertain with the given detail.</summary>
    public static TagQuality Uncertain(QualityDetail detail) => new(Quality.Uncertain, detail);

    /// <summary>Bad with the given detail.</summary>
    public static TagQuality Bad(QualityDetail detail) => new(Quality.Bad, detail);

    /// <inheritdoc/>
    public override string ToString() =>
        Detail == QualityDetail.None ? Code.ToString() : $"{Code}:{Detail}";
}
```

`src/Dse.Io.Abstractions/TagKind.cs`:

```csharp
namespace Dse.Io;

/// <summary>The three value kinds a tag can carry. Ordered so <c>default</c> is Bool.</summary>
public enum TagKind
{
    /// <summary>A discrete signal.</summary>
    Bool,

    /// <summary>An analog value in engineering units.</summary>
    Double,

    /// <summary>A counter, an enumeration or any integer.</summary>
    Int64,
}
```

`src/Dse.Io.Abstractions/TagAccess.cs`:

```csharp
namespace Dse.Io;

/// <summary>Whether a tag accepts external writes.</summary>
public enum TagAccess
{
    /// <summary>Published by the plant; writes are rejected.</summary>
    ReadOnly,

    /// <summary>Drives a plant input; the image also reflects the current value.</summary>
    ReadWrite,
}
```

`src/Dse.Io.Abstractions/TagValue.cs`:

```csharp
using System.Globalization;

namespace Dse.Io;

/// <summary>
/// One tag's value with its quality. Sixteen bytes, no references, immutable.
/// Equality compares kind, payload bits and quality, so a quality change alone
/// counts as a change. Doubles compare by bit pattern.
/// </summary>
public readonly record struct TagValue
{
    private readonly long _bits;

    private TagValue(TagKind kind, long bits, TagQuality quality)
    {
        Kind = kind;
        _bits = bits;
        Quality = quality;
    }

    /// <summary>The kind of the payload.</summary>
    public TagKind Kind { get; }

    /// <summary>The quality carried with the value.</summary>
    public TagQuality Quality { get; }

    /// <summary>A discrete value.</summary>
    public static TagValue Bool(bool value, TagQuality quality = default) =>
        new(TagKind.Bool, value ? 1L : 0L, quality);

    /// <summary>An analog value.</summary>
    public static TagValue Double(double value, TagQuality quality = default) =>
        new(TagKind.Double, BitConverter.DoubleToInt64Bits(value), quality);

    /// <summary>An integer value.</summary>
    public static TagValue Int64(long value, TagQuality quality = default) =>
        new(TagKind.Int64, value, quality);

    /// <summary>The payload as a bool. Throws if the kind is not <see cref="TagKind.Bool"/>.</summary>
    public bool AsBool => Expect(TagKind.Bool) != 0L;

    /// <summary>The payload as a double. Throws if the kind is not <see cref="TagKind.Double"/>.</summary>
    public double AsDouble => BitConverter.Int64BitsToDouble(Expect(TagKind.Double));

    /// <summary>The payload as a long. Throws if the kind is not <see cref="TagKind.Int64"/>.</summary>
    public long AsInt64 => Expect(TagKind.Int64);

    /// <summary>The same payload with a different quality.</summary>
    public TagValue WithQuality(TagQuality quality) => new(Kind, _bits, quality);

    /// <summary>True when kind and payload match, ignoring quality.</summary>
    public bool ValueEquals(TagValue other) => Kind == other.Kind && _bits == other._bits;

    /// <summary>
    /// The tag kind for a CLR type. Only <see cref="bool"/>, <see cref="double"/>
    /// and <see cref="long"/> are tag types; anything else throws.
    /// </summary>
    public static TagKind KindOf<T>()
        where T : unmanaged
    {
        if (typeof(T) == typeof(bool))
        {
            return TagKind.Bool;
        }

        if (typeof(T) == typeof(double))
        {
            return TagKind.Double;
        }

        if (typeof(T) == typeof(long))
        {
            return TagKind.Int64;
        }

        throw new NotSupportedException(
            $"'{typeof(T).Name}' is not a tag type. Tags carry bool, double or long.");
    }

    /// <inheritdoc/>
    public override string ToString()
    {
        string payload = Kind switch
        {
            TagKind.Bool => _bits != 0L ? "true" : "false",
            TagKind.Double => BitConverter.Int64BitsToDouble(_bits).ToString("R", CultureInfo.InvariantCulture),
            _ => _bits.ToString(CultureInfo.InvariantCulture),
        };

        return Quality.IsGood ? payload : $"{payload} [{Quality}]";
    }

    private long Expect(TagKind kind)
    {
        if (Kind != kind)
        {
            throw new InvalidOperationException(
                $"This tag value is a {Kind}, not a {kind}.");
        }

        return _bits;
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/Dse.Io.Abstractions.Tests`
Expected: PASS, 12 tests. Then `dotnet build -c Release` — 0 warnings.
`tests/Dse.Core.Tests/ScaffoldingTests.cs` line 11 reads
`typeof(Dse.Io.Placeholder).Assembly`; change it to
`typeof(Dse.Io.TagValue).Assembly` (that test only needs the assembly) and run
`dotnet test tests/Dse.Core.Tests --filter "FullyQualifiedName~ScaffoldingTests"`.

- [ ] **Step 6: Commit**

```bash
git add -A src/Dse.Io.Abstractions tests/Dse.Io.Abstractions.Tests tests/Dse.Core.Tests/ScaffoldingTests.cs Dse.sln
git commit -m "feat(io): add Quality, TagQuality, TagKind, TagAccess and TagValue"
```

---

### Task 2: The rest of the contract — directory, reader, writer, frame

**Files:**
- Create: `src/Dse.Io.Abstractions/TagDescriptor.cs`
- Create: `src/Dse.Io.Abstractions/ITagDirectory.cs`
- Create: `src/Dse.Io.Abstractions/TagHandle.cs`
- Create: `src/Dse.Io.Abstractions/ITagReader.cs`
- Create: `src/Dse.Io.Abstractions/ITagWriter.cs`
- Create: `src/Dse.Io.Abstractions/TagAccessExtensions.cs`
- Create: `src/Dse.Io.Abstractions/DiscreteEvent.cs`
- Create: `src/Dse.Io.Abstractions/DirtyMask.cs`
- Create: `src/Dse.Io.Abstractions/TickFrame.cs`
- Create: `src/Dse.Io.Abstractions/ITickFrameSink.cs`
- Test: `tests/Dse.Io.Abstractions.Tests/Fakes/ArrayTagReader.cs`
- Test: `tests/Dse.Io.Abstractions.Tests/DirtyMaskTests.cs`
- Test: `tests/Dse.Io.Abstractions.Tests/TagAccessExtensionsTests.cs`

**Interfaces:**
- Consumes: Task 1.
- Produces: `sealed record TagDescriptor(int Index, string Name, TagKind Kind,
  TagAccess Access, string Unit, double RangeLow, double RangeHigh, string
  Description)` with `bool HasRange`; `interface ITagDirectory { int Count;
  IReadOnlyList<TagDescriptor> Tags; TagDescriptor this[int]; bool
  TryFind(string, out TagDescriptor); TagDescriptor Find(string); }`;
  `readonly struct TagHandle<T>(int Index, string Name)`; `interface ITagReader
  { ITagDirectory Directory; long SnapshotTick; TagValue Read(int); TagValue
  Read(string); TagHandle<T> Handle<T>(string); }`; `interface ITagWriter
  { ITagDirectory Directory; void Write(int, TagValue); void Write(string,
  TagValue); }`; static `TagAccessExtensions` with `ReadBool/ReadDouble/ReadInt64`
  and `WriteBool/WriteDouble/WriteInt64` by name and by handle;
  `sealed record DiscreteEvent(long Tick, DateTimeOffset SimTime, string Source,
  string Code, string Message)`; `readonly struct DirtyMask` with
  `static DirtyMask Empty(int length)`, `static DirtyMask All(int length)`,
  `static DirtyMask FromBits(ReadOnlyMemory<ulong> bits, int length, int count)`,
  `int Length`, `int Count`, `bool this[int]`, `IEnumerable<int> Indices()`;
  `sealed class TickFrame(long Tick, DateTimeOffset SimTime,
  ReadOnlyMemory<TagValue> Values, DirtyMask Dirty, IReadOnlyList<DiscreteEvent>
  Events)`; `interface ITickFrameSink { void Publish(TickFrame frame); }`.

`TagDescriptor.RangeLow`/`RangeHigh` are `double.NaN` when a tag has no range
(bools, counters without one); `HasRange` tests that. The reader interface is
deliberately small — `Read(int)`, `Read(string)`, `Handle<T>` — and the typed
`ReadBool(...)` family are extension methods, so `Dse.Core`'s `TagImage` and any
test fake implement three members. `Handle<T>` is where kind checking happens:
`Handle<double>("CV001.Start")` on a bool tag throws at resolve time, not on
every scan.

- [ ] **Step 1: Write the failing tests**

`tests/Dse.Io.Abstractions.Tests/Fakes/ArrayTagReader.cs`:

```csharp
using Dse.Io;

namespace Dse.Io.Abstractions.Tests.Fakes;

/// <summary>A reader and writer over two arrays — the smallest possible implementation of the contract.</summary>
public sealed class ArrayTagReader : ITagReader, ITagWriter, ITagDirectory
{
    private readonly TagDescriptor[] _tags;
    private readonly Dictionary<string, int> _byName = new(StringComparer.Ordinal);

    public ArrayTagReader(params TagDescriptor[] tags)
    {
        _tags = tags;
        Values = new TagValue[tags.Length];
        for (int i = 0; i < tags.Length; i++)
        {
            _byName[tags[i].Name] = i;
            Values[i] = tags[i].Kind switch
            {
                TagKind.Bool => TagValue.Bool(false),
                TagKind.Double => TagValue.Double(0.0),
                _ => TagValue.Int64(0L),
            };
        }
    }

    public TagValue[] Values { get; }

    public List<(int Index, TagValue Value)> Writes { get; } = [];

    public ITagDirectory Directory => this;

    public long SnapshotTick { get; set; }

    public int Count => _tags.Length;

    public IReadOnlyList<TagDescriptor> Tags => _tags;

    public TagDescriptor this[int index] => _tags[index];

    public bool TryFind(string name, out TagDescriptor descriptor)
    {
        if (_byName.TryGetValue(name, out int index))
        {
            descriptor = _tags[index];
            return true;
        }

        descriptor = null!;
        return false;
    }

    public TagDescriptor Find(string name) =>
        TryFind(name, out TagDescriptor d) ? d : throw new KeyNotFoundException($"No tag '{name}'.");

    public TagValue Read(int index) => Values[index];

    public TagValue Read(string name) => Values[Find(name).Index];

    public TagHandle<T> Handle<T>(string name)
        where T : unmanaged
    {
        TagDescriptor d = Find(name);
        if (d.Kind != TagValue.KindOf<T>())
        {
            throw new InvalidOperationException($"'{name}' is a {d.Kind} tag, not {typeof(T).Name}.");
        }

        return new TagHandle<T>(d.Index, d.Name);
    }

    public void Write(int index, TagValue value) => Writes.Add((index, value));

    public void Write(string name, TagValue value) => Write(Find(name).Index, value);
}
```

`tests/Dse.Io.Abstractions.Tests/DirtyMaskTests.cs`:

```csharp
using Dse.Io;

namespace Dse.Io.Abstractions.Tests;

public class DirtyMaskTests
{
    [Fact]
    public void EmptyHasNoBits()
    {
        DirtyMask mask = DirtyMask.Empty(130);

        Assert.Equal(130, mask.Length);
        Assert.Equal(0, mask.Count);
        Assert.False(mask[0]);
        Assert.False(mask[129]);
        Assert.Empty(mask.Indices());
    }

    [Fact]
    public void AllSetsEveryBitAndNoMore()
    {
        DirtyMask mask = DirtyMask.All(70);

        Assert.Equal(70, mask.Count);
        Assert.True(mask[0]);
        Assert.True(mask[63]);
        Assert.True(mask[64]);
        Assert.True(mask[69]);
        Assert.Equal(Enumerable.Range(0, 70), mask.Indices());
    }

    [Fact]
    public void FromBitsExposesTheGivenWords()
    {
        ulong[] words = [1UL << 3 | 1UL << 63, 1UL];
        DirtyMask mask = DirtyMask.FromBits(words, length: 65, count: 3);

        Assert.True(mask[3]);
        Assert.True(mask[63]);
        Assert.True(mask[64]);
        Assert.False(mask[4]);
        Assert.Equal(new[] { 3, 63, 64 }, mask.Indices());
        Assert.Equal(3, mask.Count);
    }

    [Fact]
    public void IndexOutOfRangeThrows()
    {
        DirtyMask mask = DirtyMask.Empty(10);

        Assert.Throws<ArgumentOutOfRangeException>(() => mask[10]);
        Assert.Throws<ArgumentOutOfRangeException>(() => mask[-1]);
    }

    [Fact]
    public void ZeroLengthIsValid()
    {
        DirtyMask mask = DirtyMask.All(0);

        Assert.Equal(0, mask.Length);
        Assert.Equal(0, mask.Count);
        Assert.Empty(mask.Indices());
    }
}
```

`tests/Dse.Io.Abstractions.Tests/TagAccessExtensionsTests.cs`:

```csharp
using Dse.Io;
using Dse.Io.Abstractions.Tests.Fakes;

namespace Dse.Io.Abstractions.Tests;

public class TagAccessExtensionsTests
{
    private static ArrayTagReader Reader() => new(
        new TagDescriptor(0, "CV001.Speed", TagKind.Double, TagAccess.ReadOnly, "m/s", 0.0, 3.0, "Belt speed"),
        new TagDescriptor(1, "CV001.Start", TagKind.Bool, TagAccess.ReadWrite, string.Empty, double.NaN, double.NaN, "Start command"),
        new TagDescriptor(2, "Pile.Count", TagKind.Int64, TagAccess.ReadOnly, "count", double.NaN, double.NaN, "Items received"));

    [Fact]
    public void TypedReadsByNameUnwrapTheValue()
    {
        ArrayTagReader reader = Reader();
        reader.Values[0] = TagValue.Double(1.5);
        reader.Values[1] = TagValue.Bool(true);
        reader.Values[2] = TagValue.Int64(9);

        Assert.Equal(1.5, reader.ReadDouble("CV001.Speed"));
        Assert.True(reader.ReadBool("CV001.Start"));
        Assert.Equal(9L, reader.ReadInt64("Pile.Count"));
    }

    [Fact]
    public void TypedReadsByHandleUnwrapTheValue()
    {
        ArrayTagReader reader = Reader();
        reader.Values[0] = TagValue.Double(2.5);
        TagHandle<double> speed = reader.Handle<double>("CV001.Speed");

        Assert.Equal(0, speed.Index);
        Assert.Equal("CV001.Speed", speed.Name);
        Assert.Equal(2.5, reader.ReadDouble(speed));
    }

    [Fact]
    public void TypedWritesWrapTheValue()
    {
        ArrayTagReader writer = Reader();

        writer.WriteBool("CV001.Start", true);
        writer.WriteDouble(writer.Handle<double>("CV001.Speed"), 0.5);
        writer.WriteInt64(2, 4L);

        Assert.Equal(
            new[] { (1, TagValue.Bool(true)), (0, TagValue.Double(0.5)), (2, TagValue.Int64(4L)) },
            writer.Writes);
    }

    [Fact]
    public void DescriptorHasRangeOnlyWhenBothBoundsAreNumbers()
    {
        ArrayTagReader reader = Reader();

        Assert.True(reader.Directory[0].HasRange);
        Assert.False(reader.Directory[1].HasRange);
    }

    [Fact]
    public void TickFrameHoldsWhatItWasGiven()
    {
        var values = new[] { TagValue.Double(1.0), TagValue.Bool(true) };
        var events = new[] { new DiscreteEvent(5, DateTimeOffset.UnixEpoch, "CV001", "AT_SPEED", "At speed.") };
        var frame = new TickFrame(5, DateTimeOffset.UnixEpoch, values, DirtyMask.All(2), events);

        Assert.Equal(5, frame.Tick);
        Assert.Equal(2, frame.Values.Length);
        Assert.Equal(TagValue.Bool(true), frame.Values.Span[1]);
        Assert.Equal(2, frame.Dirty.Count);
        Assert.Single(frame.Events);
    }

    [Fact]
    public void TickFrameRejectsMismatchedMaskLength()
    {
        var values = new[] { TagValue.Double(1.0) };

        Assert.Throws<ArgumentException>(() =>
            new TickFrame(0, DateTimeOffset.UnixEpoch, values, DirtyMask.All(2), []));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Dse.Io.Abstractions.Tests`
Expected: build FAILS — `TagDescriptor`, `ITagReader`, `DirtyMask`, `TickFrame` do not exist.

- [ ] **Step 3: Write the contract**

`src/Dse.Io.Abstractions/TagDescriptor.cs`:

```csharp
namespace Dse.Io;

/// <summary>
/// One entry of the tag directory (spec 9.1): everything a consumer, an HMI
/// generator or a protocol adapter needs to know about a tag without touching
/// the model. <see cref="RangeLow"/> and <see cref="RangeHigh"/> are NaN when
/// the tag declares no range.
/// </summary>
/// <param name="Index">Position in the image and in every frame's value array.</param>
/// <param name="Name">Full ordinal name, e.g. <c>CV001.Scale.Value</c>.</param>
/// <param name="Kind">The value kind.</param>
/// <param name="Access">Whether external writes are accepted.</param>
/// <param name="Unit">Engineering unit; empty for discrete tags.</param>
/// <param name="RangeLow">Lower engineering-range bound, or NaN.</param>
/// <param name="RangeHigh">Upper engineering-range bound, or NaN.</param>
/// <param name="Description">A sentence fragment for humans and for OPC UA's Description.</param>
public sealed record TagDescriptor(
    int Index,
    string Name,
    TagKind Kind,
    TagAccess Access,
    string Unit,
    double RangeLow,
    double RangeHigh,
    string Description)
{
    /// <summary>True when both range bounds are numbers.</summary>
    public bool HasRange => !double.IsNaN(RangeLow) && !double.IsNaN(RangeHigh);
}
```

`src/Dse.Io.Abstractions/ITagDirectory.cs`:

```csharp
namespace Dse.Io;

/// <summary>
/// The printable list of tags (spec 9.1). Ordered by index; names are unique
/// and compared ordinally. Immutable for the life of a simulation.
/// </summary>
public interface ITagDirectory
{
    /// <summary>Number of tags.</summary>
    int Count { get; }

    /// <summary>All descriptors, in index order.</summary>
    IReadOnlyList<TagDescriptor> Tags { get; }

    /// <summary>The descriptor at an index.</summary>
    TagDescriptor this[int index] { get; }

    /// <summary>Looks a tag up by name.</summary>
    bool TryFind(string name, out TagDescriptor descriptor);

    /// <summary>Looks a tag up by name; throws <see cref="KeyNotFoundException"/> if absent.</summary>
    TagDescriptor Find(string name);
}
```

`src/Dse.Io.Abstractions/TagHandle.cs`:

```csharp
namespace Dse.Io;

/// <summary>
/// A tag name resolved once to an index and a checked kind (spec 9.2). Reading
/// through a handle never pays for a dictionary lookup.
/// </summary>
/// <typeparam name="T">bool, double or long, matching the tag's kind.</typeparam>
public readonly struct TagHandle<T>
    where T : unmanaged
{
    /// <summary>Creates a handle. Implementations of <see cref="ITagReader.Handle{T}"/> call this after checking the kind.</summary>
    public TagHandle(int index, string name)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Index = index;
        Name = name;
    }

    /// <summary>Position in the image.</summary>
    public int Index { get; }

    /// <summary>The tag's full name, kept for diagnostics.</summary>
    public string Name { get; }

    /// <inheritdoc/>
    public override string ToString() => Name;
}
```

`src/Dse.Io.Abstractions/ITagReader.cs`:

```csharp
namespace Dse.Io;

/// <summary>
/// Reads the published I/O image (spec 9.3). Every read sees the consistent
/// snapshot published at the most recent tick's phase 4; reads may be issued
/// from any thread and never block the simulation.
/// </summary>
public interface ITagReader
{
    /// <summary>The tag directory this reader indexes.</summary>
    ITagDirectory Directory { get; }

    /// <summary>The tick whose image is currently visible, or -1 before the first tick.</summary>
    long SnapshotTick { get; }

    /// <summary>The value at an index in the current snapshot.</summary>
    TagValue Read(int index);

    /// <summary>The value of a named tag in the current snapshot. Resolves the name every call; prefer a handle in per-scan code.</summary>
    TagValue Read(string name);

    /// <summary>
    /// Resolves a name once to a typed handle. Throws <see cref="KeyNotFoundException"/>
    /// for an unknown tag and <see cref="InvalidOperationException"/> when
    /// <typeparamref name="T"/> does not match the tag's kind.
    /// </summary>
    TagHandle<T> Handle<T>(string name)
        where T : unmanaged;
}
```

`src/Dse.Io.Abstractions/ITagWriter.cs`:

```csharp
namespace Dse.Io;

/// <summary>
/// Queues writes into the simulation (spec 9.3). A write is applied at phase 1
/// of the next tick; it is never applied on the caller's thread. Implementations
/// reject an unknown tag, a kind mismatch and a read-only tag by throwing at
/// the call site, so a caller never queues something that cannot land.
/// </summary>
public interface ITagWriter
{
    /// <summary>The tag directory this writer validates against.</summary>
    ITagDirectory Directory { get; }

    /// <summary>Queues a value for the tag at an index.</summary>
    void Write(int index, TagValue value);

    /// <summary>Queues a value for a named tag.</summary>
    void Write(string name, TagValue value);
}
```

`src/Dse.Io.Abstractions/TagAccessExtensions.cs`:

```csharp
namespace Dse.Io;

/// <summary>The typed front door over <see cref="ITagReader"/> and <see cref="ITagWriter"/> (spec 9.2).</summary>
public static class TagAccessExtensions
{
    /// <summary>Reads a bool tag by name.</summary>
    public static bool ReadBool(this ITagReader reader, string name) => reader.Read(name).AsBool;

    /// <summary>Reads a double tag by name.</summary>
    public static double ReadDouble(this ITagReader reader, string name) => reader.Read(name).AsDouble;

    /// <summary>Reads a long tag by name.</summary>
    public static long ReadInt64(this ITagReader reader, string name) => reader.Read(name).AsInt64;

    /// <summary>Reads a bool tag through a handle.</summary>
    public static bool ReadBool(this ITagReader reader, TagHandle<bool> handle) => reader.Read(handle.Index).AsBool;

    /// <summary>Reads a double tag through a handle.</summary>
    public static double ReadDouble(this ITagReader reader, TagHandle<double> handle) => reader.Read(handle.Index).AsDouble;

    /// <summary>Reads a long tag through a handle.</summary>
    public static long ReadInt64(this ITagReader reader, TagHandle<long> handle) => reader.Read(handle.Index).AsInt64;

    /// <summary>Reads any tag's value with quality through a handle.</summary>
    public static TagValue Read<T>(this ITagReader reader, TagHandle<T> handle)
        where T : unmanaged => reader.Read(handle.Index);

    /// <summary>Queues a bool by name.</summary>
    public static void WriteBool(this ITagWriter writer, string name, bool value) => writer.Write(name, TagValue.Bool(value));

    /// <summary>Queues a double by name.</summary>
    public static void WriteDouble(this ITagWriter writer, string name, double value) => writer.Write(name, TagValue.Double(value));

    /// <summary>Queues a long by name.</summary>
    public static void WriteInt64(this ITagWriter writer, string name, long value) => writer.Write(name, TagValue.Int64(value));

    /// <summary>Queues a bool through a handle.</summary>
    public static void WriteBool(this ITagWriter writer, TagHandle<bool> handle, bool value) => writer.Write(handle.Index, TagValue.Bool(value));

    /// <summary>Queues a double through a handle.</summary>
    public static void WriteDouble(this ITagWriter writer, TagHandle<double> handle, double value) => writer.Write(handle.Index, TagValue.Double(value));

    /// <summary>Queues a long through a handle.</summary>
    public static void WriteInt64(this ITagWriter writer, TagHandle<long> handle, long value) => writer.Write(handle.Index, TagValue.Int64(value));

    /// <summary>Queues a bool by index.</summary>
    public static void WriteBool(this ITagWriter writer, int index, bool value) => writer.Write(index, TagValue.Bool(value));

    /// <summary>Queues a double by index.</summary>
    public static void WriteDouble(this ITagWriter writer, int index, double value) => writer.Write(index, TagValue.Double(value));

    /// <summary>Queues a long by index.</summary>
    public static void WriteInt64(this ITagWriter writer, int index, long value) => writer.Write(index, TagValue.Int64(value));
}
```

`src/Dse.Io.Abstractions/DiscreteEvent.cs`:

```csharp
namespace Dse.Io;

/// <summary>
/// One event-log record as it crosses the determinism boundary. The source is
/// a component id or, for external writes, a tag name.
/// </summary>
public sealed record DiscreteEvent(
    long Tick,
    DateTimeOffset SimTime,
    string Source,
    string Code,
    string Message);
```

`src/Dse.Io.Abstractions/DirtyMask.cs`:

```csharp
namespace Dse.Io;

/// <summary>
/// Which tag indices changed in a frame, as an immutable bitset. A change is
/// any difference in <see cref="TagValue"/> equality — payload or quality.
/// </summary>
public readonly struct DirtyMask
{
    private readonly ReadOnlyMemory<ulong> _bits;

    private DirtyMask(ReadOnlyMemory<ulong> bits, int length, int count)
    {
        _bits = bits;
        Length = length;
        Count = count;
    }

    /// <summary>Number of tags the mask covers.</summary>
    public int Length { get; }

    /// <summary>Number of set bits.</summary>
    public int Count { get; }

    /// <summary>Whether the tag at <paramref name="index"/> changed.</summary>
    public bool this[int index]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Length);
            return (_bits.Span[index >> 6] & (1UL << (index & 63))) != 0UL;
        }
    }

    /// <summary>A mask of the given length with no bits set.</summary>
    public static DirtyMask Empty(int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        return new DirtyMask(new ulong[WordsFor(length)], length, 0);
    }

    /// <summary>A mask of the given length with every bit set — the first frame's mask.</summary>
    public static DirtyMask All(int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        var words = new ulong[WordsFor(length)];
        for (int i = 0; i < length; i++)
        {
            words[i >> 6] |= 1UL << (i & 63);
        }

        return new DirtyMask(words, length, length);
    }

    /// <summary>
    /// Wraps words the caller has already filled. The caller promises never to
    /// write the words again and supplies the popcount it computed while filling.
    /// </summary>
    public static DirtyMask FromBits(ReadOnlyMemory<ulong> bits, int length, int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(count, length);
        if (bits.Length < WordsFor(length))
        {
            throw new ArgumentException(
                $"{bits.Length} words cannot cover {length} bits.", nameof(bits));
        }

        return new DirtyMask(bits, length, count);
    }

    /// <summary>The set indices in ascending order.</summary>
    public IEnumerable<int> Indices()
    {
        for (int i = 0; i < Length; i++)
        {
            if (this[i])
            {
                yield return i;
            }
        }
    }

    /// <summary>How many 64-bit words cover <paramref name="length"/> bits.</summary>
    public static int WordsFor(int length) => (length + 63) >> 6;
}
```

`src/Dse.Io.Abstractions/TickFrame.cs`:

```csharp
namespace Dse.Io;

/// <summary>
/// The only thing that crosses out of the engine (spec 10.1): one tick's
/// complete tag image, which of those tags changed, and the tick's discrete
/// events. Immutable; the arrays behind it are never written after publish,
/// so any number of consumers may hold a frame for any length of time.
/// </summary>
public sealed class TickFrame
{
    /// <summary>Creates a frame. <paramref name="dirty"/> must cover exactly <paramref name="values"/>.</summary>
    public TickFrame(
        long tick,
        DateTimeOffset simTime,
        ReadOnlyMemory<TagValue> values,
        DirtyMask dirty,
        IReadOnlyList<DiscreteEvent> events)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(tick);
        ArgumentNullException.ThrowIfNull(events);
        if (dirty.Length != values.Length)
        {
            throw new ArgumentException(
                $"The dirty mask covers {dirty.Length} tags but the frame carries {values.Length}.",
                nameof(dirty));
        }

        Tick = tick;
        SimTime = simTime;
        Values = values;
        Dirty = dirty;
        Events = events;
    }

    /// <summary>The tick index this frame describes.</summary>
    public long Tick { get; }

    /// <summary>The tick's simulation timestamp — the value components saw as <c>TickContext.SimTime</c>. Never wall-clock (spec 10.8).</summary>
    public DateTimeOffset SimTime { get; }

    /// <summary>Every tag's value, by directory index.</summary>
    public ReadOnlyMemory<TagValue> Values { get; }

    /// <summary>Which tags differ from the previous frame.</summary>
    public DirtyMask Dirty { get; }

    /// <summary>The event-log records this tick produced, in order.</summary>
    public IReadOnlyList<DiscreteEvent> Events { get; }
}
```

`src/Dse.Io.Abstractions/ITickFrameSink.cs`:

```csharp
namespace Dse.Io;

/// <summary>
/// Where the simulation hands each frame at phase 5. Called once per tick on
/// the simulation thread. <b>Must not block and must not throw</b>: the
/// implementation's whole obligation is one enqueue, and it drops rather than
/// waits.
/// </summary>
public interface ITickFrameSink
{
    /// <summary>Accepts a frame without blocking.</summary>
    void Publish(TickFrame frame);
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/Dse.Io.Abstractions.Tests`
Expected: PASS, 23 tests. `dotnet build -c Release` — 0 warnings.

- [ ] **Step 5: Commit**

```bash
git add src/Dse.Io.Abstractions tests/Dse.Io.Abstractions.Tests
git commit -m "feat(io): add the tag directory, reader, writer, dirty mask and tick frame contract"
```

---

### Task 3: External input drivers and the graph freeze

**Files:**
- Modify: `src/Dse.Core/Graph/Port.cs`
- Modify: `src/Dse.Core/Graph/InputPort.cs`
- Modify: `src/Dse.Core/SimulationBuilder.cs` (`_built` flag, `Add` guard, freeze in `Build`)
- Test: `tests/Dse.Core.Tests/ExternalInputTests.cs`
- Test: `tests/Dse.Core.Tests/GraphFreezeTests.cs`

**Interfaces:**
- Consumes: `Port`, `InputPort<T>`, `SimulationBuilder` as they exist.
- Produces: on `Port`: `internal bool IsFrozen { get; }`, `internal void Freeze()`.
  On `InputPort<T>`: `public bool IsExternallyDriven { get; }`,
  `internal void DriveExternally()`, `internal void SetExternal(T value)`,
  `internal T ExternalValue { get; }`. `SimulationBuilder.Add` throws
  `InvalidOperationException` after `Build()`; `Build()` freezes every port of
  every component. `InputPort<T>.ConnectFrom` throws `InvalidOperationException`
  when frozen or externally driven.

An externally driven input reads `ExternalValue` when it has no source; the
value starts at `DefaultValue`. A latched external input still reads through
`Capture`, so a write applied at phase 1 is seen by a latched consumer one
tick later, exactly like a wired source (the tick-timing rule from plan 3 is
unchanged). `IsMissingRequiredConnection` is false for an externally driven
required input: an operator is a valid source.

- [ ] **Step 1: Write the failing tests**

`tests/Dse.Core.Tests/ExternalInputTests.cs`:

```csharp
using Dse.Core.Graph;
using Dse.Core.Tests.Fakes;
using Xunit;

namespace Dse.Core.Tests;

public class ExternalInputTests
{
    [Fact]
    public void UnconnectedInputReadsItsDefaultUntilDriven()
    {
        var port = new InputPort<double>("Setpoint", "T", defaultValue: 4.0, isRequired: false);

        Assert.False(port.IsExternallyDriven);
        port.DriveExternally();

        Assert.True(port.IsExternallyDriven);
        Assert.Equal(4.0, port.Value);

        port.SetExternal(9.5);

        Assert.Equal(9.5, port.Value);
        Assert.Equal(9.5, port.ExternalValue);
    }

    [Fact]
    public void ExternallyDrivenRequiredInputIsNotMissing()
    {
        var port = new InputPort<bool>("Enable", "T", defaultValue: false, isRequired: true);
        Assert.True(port.IsMissingRequiredConnection);

        port.DriveExternally();

        Assert.False(port.IsMissingRequiredConnection);
    }

    [Fact]
    public void LatchedExternalInputReadsOneCaptureLate()
    {
        var port = new InputPort<double>("Demand", "T", defaultValue: 0.0, isRequired: false, isLatched: true);
        port.DriveExternally();

        port.SetExternal(3.0);
        Assert.Equal(0.0, port.Value);

        port.Capture();
        Assert.Equal(3.0, port.Value);
    }

    [Fact]
    public void DrivingAConnectedInputThrows()
    {
        var source = new OutputPort<double>("Out", "S");
        var port = new InputPort<double>("In", "T", defaultValue: 0.0, isRequired: false);
        source.ConnectTo(port);

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(port.DriveExternally);
        Assert.Contains("S.Out", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ConnectingADrivenInputThrows()
    {
        var source = new OutputPort<double>("Out", "S");
        var port = new InputPort<double>("In", "T", defaultValue: 0.0, isRequired: false);
        port.DriveExternally();

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => source.ConnectTo(port));
        Assert.Contains("externally", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ConnectedInputStillReadsItsSourceNotTheExternalSlot()
    {
        var source = new ConstantSource("S", 2.0);
        var recorder = new Recorder("R");
        source.Out.ConnectTo(recorder.In);

        Assert.False(recorder.In.IsExternallyDriven);
    }
}
```

`tests/Dse.Core.Tests/GraphFreezeTests.cs`:

```csharp
using Dse.Core.Graph;
using Dse.Core.Tests.Fakes;
using Dse.Core.Time;
using Xunit;

namespace Dse.Core.Tests;

public class GraphFreezeTests
{
    private static SimulationOptions Options => new() { Seed = 1UL };

    [Fact]
    public void PortsAreFrozenByBuild()
    {
        var source = new ConstantSource("S", 1.0);
        var recorder = new Recorder("R");
        source.Out.ConnectTo(recorder.In);
        var builder = new SimulationBuilder(Options).Add(source).Add(recorder);

        Assert.False(recorder.In.IsFrozen);
        builder.Build();

        Assert.True(recorder.In.IsFrozen);
        Assert.True(source.Out.IsFrozen);
    }

    [Fact]
    public void WiringAfterBuildThrows()
    {
        var source = new ConstantSource("S", 1.0);
        var recorder = new Recorder("R");
        var spare = new Recorder("Spare");
        source.Out.ConnectTo(recorder.In);
        new SimulationBuilder(Options).Add(source).Add(recorder).Add(spare).Build();

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => source.Out.ConnectTo(spare.In));
        Assert.Contains("built", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AddingAfterBuildThrows()
    {
        var builder = new SimulationBuilder(Options).Add(new ConstantSource("S", 1.0));
        builder.Build();

        Assert.Throws<InvalidOperationException>(() => builder.Add(new ConstantSource("T", 2.0)));
    }

    [Fact]
    public void FreezeIsIdempotentAndBuildCanBeCalledOnce()
    {
        var builder = new SimulationBuilder(Options).Add(new ConstantSource("S", 1.0));
        builder.Build();

        Assert.Throws<InvalidOperationException>(() => builder.Build());
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Dse.Core.Tests --filter "FullyQualifiedName~ExternalInputTests|FullyQualifiedName~GraphFreezeTests"`
Expected: build FAILS — `DriveExternally`, `IsFrozen` do not exist.

- [ ] **Step 3: Modify `Port`**

In `src/Dse.Core/Graph/Port.cs`, add after `IsLatchedInput`:

```csharp
    /// <summary>True once the plant containing this port has been built; wiring is then immutable.</summary>
    internal bool IsFrozen { get; private set; }

    /// <summary>Marks the port immutable. Called by <see cref="SimulationBuilder.Build"/> for every port.</summary>
    internal void Freeze() => IsFrozen = true;
```

(The `<see cref="SimulationBuilder.Build"/>` resolves because `SimulationBuilder`
already exists in `Dse.Core`; add `using Dse.Core;` is not needed — same
assembly, but the namespace differs, so write the cref as
`<see cref="Dse.Core.SimulationBuilder.Build"/>`.)

- [ ] **Step 4: Modify `InputPort<T>`**

Replace the body of `src/Dse.Core/Graph/InputPort.cs` so the class reads (keep
the existing XML docs on members that already have them; the new members carry
the docs shown):

```csharp
namespace Dse.Core.Graph;

public sealed class InputPort<T> : Port
    where T : unmanaged
{
    private OutputPort<T>? _source;
    private T _captured;
    private bool _external;
    private T _externalValue;

    public InputPort(string name, string ownerId, T defaultValue, bool isRequired, bool isLatched = false)
        : base(name, ownerId)
    {
        DefaultValue = defaultValue;
        IsRequired = isRequired;
        IsLatched = isLatched;
        _captured = defaultValue;
        _externalValue = defaultValue;
    }

    public T DefaultValue { get; }

    public bool IsRequired { get; }

    public bool IsLatched { get; }

    /// <summary>
    /// True when a writable tag binding drives this input (spec 9.3). The port
    /// then reads the value most recently applied at phase 1, starting from
    /// <see cref="DefaultValue"/>.
    /// </summary>
    public bool IsExternallyDriven => _external;

    public T Value => IsLatched ? _captured : Live;

    private T Live => _source is not null ? _source.Value : _external ? _externalValue : DefaultValue;

    public override bool IsMissingRequiredConnection => IsRequired && _source is null && !_external;

    internal override Port? SourcePort => _source;

    internal override bool CreatesOrderingEdge => !IsLatched && _source is not null;

    internal override bool IsLatchedInput => IsLatched;

    /// <summary>The value the external driver last applied; equals <see cref="DefaultValue"/> until a write lands.</summary>
    internal T ExternalValue => _externalValue;

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

        if (IsFrozen)
        {
            throw new InvalidOperationException(
                $"Cannot connect '{source.QualifiedName}' to '{QualifiedName}': the plant has been built " +
                $"and its wiring is immutable. Wire before calling Build().");
        }

        if (_external)
        {
            throw new InvalidOperationException(
                $"Input '{QualifiedName}' is externally driven by a writable tag; it cannot also be " +
                $"driven by '{source.QualifiedName}'. Remove the tag binding or the connection.");
        }

        if (_source is not null)
        {
            throw new InvalidOperationException(
                $"Input '{QualifiedName}' is already driven by '{_source.QualifiedName}'. " +
                $"An input accepts exactly one source; remove one of the connections.");
        }

        _source = source;
    }

    /// <summary>Marks this input as driven from outside the plant. Called by the builder for writable bindings.</summary>
    internal void DriveExternally()
    {
        if (_source is not null)
        {
            throw new InvalidOperationException(
                $"Input '{QualifiedName}' is driven by '{_source.QualifiedName}'; a writable tag cannot " +
                $"drive it as well.");
        }

        _external = true;
    }

    /// <summary>Applies an external write. Called at phase 1 only.</summary>
    internal void SetExternal(T value) => _externalValue = value;
}
```

- [ ] **Step 5: Modify `SimulationBuilder`**

In `src/Dse.Core/SimulationBuilder.cs`:

Add a field `private bool _built;` after `_options`.

At the top of `Add(ISimNode node)`, before the `switch`:

```csharp
        ThrowIfBuilt();
```

Replace `Build()`:

```csharp
    public Simulation Build()
    {
        ThrowIfBuilt();

        ValidationResult result = Validate();
        if (!result.IsValid)
        {
            throw new SimulationValidationException(result);
        }

        GraphResolver.TryResolve(_components, out ISimComponent[] ordered, out _);
        FlowGraph flow = FlowGraph.Build(FlowNodes());
        FreezePorts();
        _built = true;
        return new Simulation(ordered, flow, _options);
    }

    private void FreezePorts()
    {
        foreach (ISimComponent component in _components)
        {
            foreach (Port port in component.Ports)
            {
                port.Freeze();
            }
        }
    }

    private void ThrowIfBuilt()
    {
        if (_built)
        {
            throw new InvalidOperationException(
                "This builder has already produced a simulation; the plant is immutable after Build(). " +
                "Create a new builder for a different plant.");
        }
    }
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/Dse.Core.Tests`
Expected: PASS — the 221 existing Core tests plus 10 new. `dotnet build -c Release` — 0 warnings.

- [ ] **Step 7: Commit**

```bash
git add src/Dse.Core/Graph/Port.cs src/Dse.Core/Graph/InputPort.cs src/Dse.Core/SimulationBuilder.cs tests/Dse.Core.Tests/ExternalInputTests.cs tests/Dse.Core.Tests/GraphFreezeTests.cs
git commit -m "feat(core): let a writable tag drive an input port and freeze the graph after Build"
```

---

### Task 4: Tag bindings and the provider interface

**Files:**
- Create: `src/Dse.Core/Io/ITagProvider.cs`
- Create: `src/Dse.Core/Io/EnumBits.cs`
- Create: `src/Dse.Core/Io/TagBinding.cs`
- Test: `tests/Dse.Core.Tests/TagBindingTests.cs`

**Interfaces:**
- Consumes: Task 1–3 (`TagValue`, `TagQuality`, `TagKind`, `TagAccess`,
  `InputPort<T>.DriveExternally/SetExternal`).
- Produces: `interface ITagProvider { IEnumerable<TagBinding> DescribeTags(); }`;
  `sealed class TagBinding` with `Port Port`, `string Name`, `TagKind Kind`,
  `TagAccess Access`, `string Unit`, `double RangeLow`, `double RangeHigh`,
  `string Description`; factories `TagBinding.Read(string, OutputPort<bool>,
  string description = "")`, `Read(string, OutputPort<double>, string unit,
  double rangeLow = double.NaN, double rangeHigh = double.NaN, string description = "",
  OutputPort<TagQuality>? quality = null)`, `Read(string, OutputPort<long>,
  string unit = "count", string description = "")`, `Read(string,
  OutputPort<int>, string unit = "count", string description = "")`,
  `ReadEnum<TEnum>(string, OutputPort<TEnum>, string description = "")`,
  `Write(string, InputPort<bool>, string description = "")`, `Write(string,
  InputPort<double>, string unit, double rangeLow = double.NaN, double rangeHigh = double.NaN, string
  description = "")`, `Write(string, InputPort<long>, string unit = "count",
  string description = "")`; internal `TagValue Capture()`, `void
  Apply(TagValue)`, `void BindExternal()`, `TagBinding WithName(string)`,
  `TagBinding AsReadOnly()`. A double range is either both NaN (no range) or
  both finite and ascending.
  `internal static class EnumBits<TEnum>` with `static long ToInt64(TEnum)`.

A binding's `Name` is relative to its declaring component until the builder
qualifies it (Task 6). `Capture` reads the port and, for a double read with a
quality source, the quality port, into one `TagValue`. `Apply` sets the
external slot of a writable input; on a read-only binding it throws. `int`
outputs publish as `Int64` tags; enum outputs publish as `Int64` with the
member names appended to the description, so an HMI can decode `Phase` without
the assembly.

- [ ] **Step 1: Write the failing tests**

`tests/Dse.Core.Tests/TagBindingTests.cs`:

```csharp
using Dse.Core.Graph;
using Dse.Core.Io;
using Dse.Io;
using Xunit;

namespace Dse.Core.Tests;

public class TagBindingTests
{
    private enum Phase
    {
        Idle,
        Filling,
        Done,
    }

    [Fact]
    public void BoolReadCapturesThePortValue()
    {
        var port = new OutputPort<bool>("Ok", "S") { Value = true };
        TagBinding binding = TagBinding.Read("Ok", port, "Loop healthy");

        Assert.Equal(("Ok", TagKind.Bool, TagAccess.ReadOnly, string.Empty, "Loop healthy"),
            (binding.Name, binding.Kind, binding.Access, binding.Unit, binding.Description));
        Assert.True(double.IsNaN(binding.RangeLow));
        Assert.Equal(TagValue.Bool(true), binding.Capture());
    }

    [Fact]
    public void DoubleReadCarriesUnitRangeAndQuality()
    {
        var value = new OutputPort<double>("Value", "TT01") { Value = 42.5 };
        var health = new OutputPort<TagQuality>("Health", "TT01") { Value = TagQuality.Bad(QualityDetail.SensorFailure) };
        TagBinding binding = TagBinding.Read("Value", value, "°C", 0.0, 100.0, "Bearing temperature", health);

        Assert.Equal(("°C", 0.0, 100.0), (binding.Unit, binding.RangeLow, binding.RangeHigh));
        Assert.Equal(TagValue.Double(42.5, TagQuality.Bad(QualityDetail.SensorFailure)), binding.Capture());

        health.Value = TagQuality.Good;
        Assert.Equal(TagValue.Double(42.5), binding.Capture());
    }

    [Fact]
    public void DoubleReadWithoutQualitySourceIsAlwaysGood()
    {
        var value = new OutputPort<double>("Load", "B") { Value = 1.25 };

        Assert.Equal(TagValue.Double(1.25), TagBinding.Read("Load", value, "kg/m", 0.0, 50.0).Capture());
    }

    [Fact]
    public void IntAndLongReadsPublishAsInt64()
    {
        var count = new OutputPort<long>("Count", "PC") { Value = 17L };
        var queued = new OutputPort<int>("Queued", "IS") { Value = 3 };

        Assert.Equal(TagValue.Int64(17L), TagBinding.Read("Count", count).Capture());
        Assert.Equal(TagValue.Int64(3L), TagBinding.Read("Queued", queued).Capture());
        Assert.Equal("count", TagBinding.Read("Queued", queued).Unit);
    }

    [Fact]
    public void EnumReadPublishesAsInt64AndDocumentsTheMembers()
    {
        var phase = new OutputPort<Phase>("Phase", "PU") { Value = Phase.Done };
        TagBinding binding = TagBinding.ReadEnum("Phase", phase, "Unit phase");

        Assert.Equal(TagKind.Int64, binding.Kind);
        Assert.Equal(TagValue.Int64(2L), binding.Capture());
        Assert.Equal("Unit phase (0=Idle, 1=Filling, 2=Done)", binding.Description);
    }

    [Fact]
    public void EnumReadWithoutDescriptionListsMembersOnly()
    {
        var phase = new OutputPort<Phase>("Phase", "PU");

        Assert.Equal("0=Idle, 1=Filling, 2=Done", TagBinding.ReadEnum("Phase", phase).Description);
    }

    [Fact]
    public void WriteBindingDrivesTheInput()
    {
        var input = new InputPort<bool>("Command", "ST", defaultValue: false, isRequired: false);
        TagBinding binding = TagBinding.Write("Command", input, "Run command");

        Assert.Equal((TagKind.Bool, TagAccess.ReadWrite), (binding.Kind, binding.Access));
        binding.BindExternal();
        Assert.True(input.IsExternallyDriven);

        binding.Apply(TagValue.Bool(true));

        Assert.True(input.Value);
        Assert.Equal(TagValue.Bool(true), binding.Capture());
    }

    [Fact]
    public void DoubleWriteBindingCarriesRange()
    {
        var input = new InputPort<double>("Rate", "F", defaultValue: 5.0, isRequired: false);
        TagBinding binding = TagBinding.Write("Rate", input, "kg/s", 0.0, 20.0, "Feed rate");
        binding.BindExternal();

        binding.Apply(TagValue.Double(12.0));

        Assert.Equal(12.0, input.Value);
        Assert.Equal((0.0, 20.0), (binding.RangeLow, binding.RangeHigh));
    }

    [Fact]
    public void ApplyOnAReadOnlyBindingThrows()
    {
        var port = new OutputPort<bool>("Ok", "S");
        TagBinding binding = TagBinding.Read("Ok", port);

        Assert.Throws<InvalidOperationException>(() => binding.Apply(TagValue.Bool(true)));
    }

    [Fact]
    public void ApplyOfTheWrongKindThrows()
    {
        var input = new InputPort<bool>("Command", "ST", defaultValue: false, isRequired: false);
        TagBinding binding = TagBinding.Write("Command", input);
        binding.BindExternal();

        Assert.Throws<InvalidOperationException>(() => binding.Apply(TagValue.Double(1.0)));
    }

    [Fact]
    public void WithNameKeepsEverythingElse()
    {
        var port = new OutputPort<double>("Value", "S") { Value = 2.0 };
        TagBinding binding = TagBinding.Read("Value", port, "A", 0.0, 10.0, "Current");

        TagBinding renamed = binding.WithName("CV001.Current");

        Assert.Equal("CV001.Current", renamed.Name);
        Assert.Same(port, renamed.Port);
        Assert.Equal(("A", 0.0, 10.0, "Current"), (renamed.Unit, renamed.RangeLow, renamed.RangeHigh, renamed.Description));
        Assert.Equal(TagValue.Double(2.0), renamed.Capture());
    }

    [Fact]
    public void DoubleReadWithoutRangeHasNaNBounds()
    {
        var port = new OutputPort<double>("Received", "P") { Value = 1234.5 };
        TagBinding binding = TagBinding.Read("Received", port, "kg");

        Assert.True(double.IsNaN(binding.RangeLow));
        Assert.True(double.IsNaN(binding.RangeHigh));
        Assert.Equal(TagValue.Double(1234.5), binding.Capture());
    }

    [Fact]
    public void AsReadOnlyDropsTheWritePath()
    {
        var input = new InputPort<bool>("Command", "ST", defaultValue: false, isRequired: false);
        TagBinding readOnly = TagBinding.Write("Command", input, "Run command").AsReadOnly();

        Assert.Equal(TagAccess.ReadOnly, readOnly.Access);
        Assert.Equal("Run command", readOnly.Description);
        readOnly.BindExternal();
        Assert.False(input.IsExternallyDriven);
        Assert.Throws<InvalidOperationException>(() => readOnly.Apply(TagValue.Bool(true)));
        Assert.Equal(TagValue.Bool(false), readOnly.Capture());
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(".Speed")]
    [InlineData("Speed.")]
    [InlineData("Belt Speed")]
    public void RejectsMalformedNames(string name)
    {
        var port = new OutputPort<bool>("Ok", "S");

        Assert.Throws<ArgumentException>(() => TagBinding.Read(name, port));
    }

    [Fact]
    public void RejectsAnInvertedOrHalfRange()
    {
        var port = new OutputPort<double>("Value", "S");

        Assert.Throws<ArgumentException>(() => TagBinding.Read("Value", port, "A", 10.0, 0.0));
        Assert.Throws<ArgumentException>(() => TagBinding.Read("Value", port, "A", 0.0, double.NaN));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Dse.Core.Tests --filter "FullyQualifiedName~TagBindingTests"`
Expected: build FAILS — `Dse.Core.Io.TagBinding` does not exist.

- [ ] **Step 3: Write the provider interface and the enum helper**

`src/Dse.Core/Io/ITagProvider.cs`:

```csharp
namespace Dse.Core.Io;

/// <summary>
/// A component that publishes tags (spec 9.1). Names are relative to the
/// component; the builder prefixes them with the component id, and a composite
/// that exposes the same port under an alias renames the tag to
/// <c>CompositeId.Alias</c>. Declare what a plant measures or commands; leave
/// the god view to telemetry.
/// </summary>
public interface ITagProvider
{
    /// <summary>The component's bindings. Called once, at <c>Build()</c>.</summary>
    IEnumerable<TagBinding> DescribeTags();
}
```

`src/Dse.Core/Io/EnumBits.cs`:

```csharp
using System.Runtime.CompilerServices;

namespace Dse.Core.Io;

/// <summary>Reads an enum's underlying integer without boxing, whatever its underlying type.</summary>
internal static class EnumBits<TEnum>
    where TEnum : unmanaged, Enum
{
    private static readonly TypeCode Code = Type.GetTypeCode(Enum.GetUnderlyingType(typeof(TEnum)));

    public static long ToInt64(TEnum value) => Code switch
    {
        TypeCode.SByte => Unsafe.As<TEnum, sbyte>(ref value),
        TypeCode.Byte => Unsafe.As<TEnum, byte>(ref value),
        TypeCode.Int16 => Unsafe.As<TEnum, short>(ref value),
        TypeCode.UInt16 => Unsafe.As<TEnum, ushort>(ref value),
        TypeCode.Int32 => Unsafe.As<TEnum, int>(ref value),
        TypeCode.UInt32 => Unsafe.As<TEnum, uint>(ref value),
        TypeCode.Int64 => Unsafe.As<TEnum, long>(ref value),
        _ => (long)Unsafe.As<TEnum, ulong>(ref value),
    };
}
```

- [ ] **Step 4: Write `TagBinding`**

`src/Dse.Core/Io/TagBinding.cs`:

```csharp
using System.Globalization;
using Dse.Core.Graph;
using Dse.Io;

namespace Dse.Core.Io;

/// <summary>
/// Maps a tag name to a port with the metadata the directory publishes
/// (spec 9.1). Immutable. Created through the static factories; the builder
/// qualifies the name and, for writable bindings, marks the input as
/// externally driven at <c>Build()</c>.
/// </summary>
public sealed class TagBinding
{
    private readonly Func<TagValue> _capture;
    private readonly Action<TagValue>? _apply;
    private readonly Action? _bindExternal;

    private TagBinding(
        Port port,
        string name,
        TagKind kind,
        TagAccess access,
        string unit,
        double rangeLow,
        double rangeHigh,
        string description,
        Func<TagValue> capture,
        Action<TagValue>? apply,
        Action? bindExternal)
    {
        Port = port;
        Name = name;
        Kind = kind;
        Access = access;
        Unit = unit;
        RangeLow = rangeLow;
        RangeHigh = rangeHigh;
        Description = description;
        _capture = capture;
        _apply = apply;
        _bindExternal = bindExternal;
    }

    /// <summary>The bound port.</summary>
    public Port Port { get; }

    /// <summary>Relative to the declaring component until the builder qualifies it; full thereafter.</summary>
    public string Name { get; }

    /// <summary>The published value kind.</summary>
    public TagKind Kind { get; }

    /// <summary>Whether the tag accepts writes.</summary>
    public TagAccess Access { get; }

    /// <summary>Engineering unit; empty for discrete tags.</summary>
    public string Unit { get; }

    /// <summary>Lower range bound or NaN.</summary>
    public double RangeLow { get; }

    /// <summary>Upper range bound or NaN.</summary>
    public double RangeHigh { get; }

    /// <summary>Human description, a sentence fragment.</summary>
    public string Description { get; }

    /// <summary>A read-only discrete tag.</summary>
    public static TagBinding Read(string name, OutputPort<bool> port, string description = "")
    {
        ArgumentNullException.ThrowIfNull(port);
        return new TagBinding(
            port, ValidName(name), TagKind.Bool, TagAccess.ReadOnly, string.Empty, double.NaN, double.NaN,
            description ?? string.Empty, () => TagValue.Bool(port.Value), null, null);
    }

    /// <summary>A read-only analog tag with unit, range and an optional quality source (R25).</summary>
    public static TagBinding Read(
        string name,
        OutputPort<double> port,
        string unit,
        double rangeLow = double.NaN,
        double rangeHigh = double.NaN,
        string description = "",
        OutputPort<TagQuality>? quality = null)
    {
        ArgumentNullException.ThrowIfNull(port);
        ArgumentNullException.ThrowIfNull(unit);
        ValidRange(rangeLow, rangeHigh);

        Func<TagValue> capture = quality is null
            ? () => TagValue.Double(port.Value)
            : () => TagValue.Double(port.Value, quality.Value);

        return new TagBinding(
            port, ValidName(name), TagKind.Double, TagAccess.ReadOnly, unit, rangeLow, rangeHigh,
            description ?? string.Empty, capture, null, null);
    }

    /// <summary>A read-only integer tag.</summary>
    public static TagBinding Read(string name, OutputPort<long> port, string unit = "count", string description = "")
    {
        ArgumentNullException.ThrowIfNull(port);
        ArgumentNullException.ThrowIfNull(unit);
        return new TagBinding(
            port, ValidName(name), TagKind.Int64, TagAccess.ReadOnly, unit, double.NaN, double.NaN,
            description ?? string.Empty, () => TagValue.Int64(port.Value), null, null);
    }

    /// <summary>A read-only integer tag over an <see cref="int"/> port; published as Int64.</summary>
    public static TagBinding Read(string name, OutputPort<int> port, string unit = "count", string description = "")
    {
        ArgumentNullException.ThrowIfNull(port);
        ArgumentNullException.ThrowIfNull(unit);
        return new TagBinding(
            port, ValidName(name), TagKind.Int64, TagAccess.ReadOnly, unit, double.NaN, double.NaN,
            description ?? string.Empty, () => TagValue.Int64(port.Value), null, null);
    }

    /// <summary>
    /// A read-only integer tag over an enum port. The members are appended to
    /// the description as <c>0=Idle, 1=Filling</c> so a consumer can decode it.
    /// </summary>
    public static TagBinding ReadEnum<TEnum>(string name, OutputPort<TEnum> port, string description = "")
        where TEnum : unmanaged, Enum
    {
        ArgumentNullException.ThrowIfNull(port);

        string members = string.Join(", ", Enum.GetValues<TEnum>().Select(v =>
            string.Create(CultureInfo.InvariantCulture, $"{EnumBits<TEnum>.ToInt64(v)}={v}")));
        string full = string.IsNullOrEmpty(description) ? members : $"{description} ({members})";

        return new TagBinding(
            port, ValidName(name), TagKind.Int64, TagAccess.ReadOnly, string.Empty, double.NaN, double.NaN,
            full, () => TagValue.Int64(EnumBits<TEnum>.ToInt64(port.Value)), null, null);
    }

    /// <summary>A writable discrete tag driving an input (R23).</summary>
    public static TagBinding Write(string name, InputPort<bool> port, string description = "")
    {
        ArgumentNullException.ThrowIfNull(port);
        return new TagBinding(
            port, ValidName(name), TagKind.Bool, TagAccess.ReadWrite, string.Empty, double.NaN, double.NaN,
            description ?? string.Empty,
            () => TagValue.Bool(port.Value),
            v => port.SetExternal(v.AsBool),
            port.DriveExternally);
    }

    /// <summary>A writable analog tag driving an input, with unit and range.</summary>
    public static TagBinding Write(
        string name,
        InputPort<double> port,
        string unit,
        double rangeLow = double.NaN,
        double rangeHigh = double.NaN,
        string description = "")
    {
        ArgumentNullException.ThrowIfNull(port);
        ArgumentNullException.ThrowIfNull(unit);
        ValidRange(rangeLow, rangeHigh);
        return new TagBinding(
            port, ValidName(name), TagKind.Double, TagAccess.ReadWrite, unit, rangeLow, rangeHigh,
            description ?? string.Empty,
            () => TagValue.Double(port.Value),
            v => port.SetExternal(v.AsDouble),
            port.DriveExternally);
    }

    /// <summary>A writable integer tag driving an input.</summary>
    public static TagBinding Write(string name, InputPort<long> port, string unit = "count", string description = "")
    {
        ArgumentNullException.ThrowIfNull(port);
        ArgumentNullException.ThrowIfNull(unit);
        return new TagBinding(
            port, ValidName(name), TagKind.Int64, TagAccess.ReadWrite, unit, double.NaN, double.NaN,
            description ?? string.Empty,
            () => TagValue.Int64(port.Value),
            v => port.SetExternal(v.AsInt64),
            port.DriveExternally);
    }

    /// <summary>Reads the port (and quality source) into a value. Phase 4 only.</summary>
    internal TagValue Capture() => _capture();

    /// <summary>Applies an external write to the input. Phase 1 only.</summary>
    internal void Apply(TagValue value)
    {
        if (_apply is null)
        {
            throw new InvalidOperationException($"Tag '{Name}' is read-only.");
        }

        if (value.Kind != Kind)
        {
            throw new InvalidOperationException(
                $"Tag '{Name}' is a {Kind} tag; cannot apply a {value.Kind} value.");
        }

        _apply(value);
    }

    /// <summary>Marks the input externally driven. Called by the builder for writable bindings.</summary>
    internal void BindExternal() => _bindExternal?.Invoke();

    /// <summary>The same binding under a different (usually fully qualified) name.</summary>
    internal TagBinding WithName(string name) => new(
        Port, ValidName(name), Kind, Access, Unit, RangeLow, RangeHigh, Description, _capture, _apply, _bindExternal);

    /// <summary>The same binding with the write path removed (R23: the input is wired, so the tag only observes).</summary>
    internal TagBinding AsReadOnly() => new(
        Port, Name, Kind, TagAccess.ReadOnly, Unit, RangeLow, RangeHigh, Description, _capture, null, null);

    private static string ValidName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name.StartsWith('.') || name.EndsWith('.') || name.Contains("..", StringComparison.Ordinal))
        {
            throw new ArgumentException($"Tag name '{name}' has an empty segment.", nameof(name));
        }

        foreach (char c in name)
        {
            if (char.IsWhiteSpace(c))
            {
                throw new ArgumentException($"Tag name '{name}' contains whitespace.", nameof(name));
            }
        }

        return name;
    }

    private static void ValidRange(double low, double high)
    {
        if (double.IsNaN(low) && double.IsNaN(high))
        {
            return;
        }

        if (!double.IsFinite(low) || !double.IsFinite(high) || high <= low)
        {
            throw new ArgumentException(
                string.Create(CultureInfo.InvariantCulture,
                    $"Range [{low}, {high}] must be both NaN (no range) or finite and ascending."));
        }
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/Dse.Core.Tests --filter "FullyQualifiedName~TagBindingTests"`
Expected: PASS, 19 tests (14 facts + 5 theory rows). `dotnet build -c Release` — 0 warnings.

- [ ] **Step 6: Commit**

```bash
git add src/Dse.Core/Io tests/Dse.Core.Tests/TagBindingTests.cs
git commit -m "feat(core): add TagBinding factories and the ITagProvider interface"
```

---

### Task 5: The tag directory and the I/O image

**Files:**
- Create: `src/Dse.Core/Io/TagDirectory.cs`
- Create: `src/Dse.Core/Io/TagImage.cs`
- Test: `tests/Dse.Core.Tests/TagImageTests.cs`

**Interfaces:**
- Consumes: Task 2 (`ITagDirectory`, `ITagReader`, `ITagWriter`, `DirtyMask`),
  Task 4 (`TagBinding`), `TickContext`.
- Produces: `sealed class TagDirectory : ITagDirectory` with
  `internal TagDirectory(IEnumerable<TagBinding> fullyNamed)` (sorts by ordinal
  name, assigns indices), `internal TagBinding[] Bindings`, `string ToText()`;
  `sealed class TagImage : ITagReader, ITagWriter` with
  `internal TagImage(TagDirectory directory)`, `TagDirectory Directory`,
  `long SnapshotTick`, `int PendingWrites`, `TagValue Read(int)`,
  `TagValue Read(string)`, `TagHandle<T> Handle<T>(string)`,
  `void Write(int, TagValue)`, `void Write(string, TagValue)`,
  `internal void Prime()`, `internal (TagValue[] Values, DirtyMask Dirty)
  Publish(long tick)`, `internal int ApplyPendingWrites(in TickContext ctx)`.

The image is the one place threads meet. `Publish` runs on the simulation
thread: it allocates a fresh `TagValue[]`, captures every binding, diffs
against the previous published array to build the mask, then swaps the
reference with `Volatile.Write`. `Read` on any thread does `Volatile.Read` of
the reference and indexes it; the array is never written again. Writes go into
a `ConcurrentQueue`, whose `Enqueue` is lock-free for producers, and are
validated (index, kind, access) *before* enqueueing so the simulation thread
never sees a bad write. `ApplyPendingWrites` dequeues at most the count
observed on entry (R24).

- [ ] **Step 1: Write the failing tests**

`tests/Dse.Core.Tests/TagImageTests.cs`:

```csharp
using Dse.Core.Graph;
using Dse.Core.Io;
using Dse.Core.Logging;
using Dse.Core.Tests.Fakes;
using Dse.Io;
using Xunit;

namespace Dse.Core.Tests;

public class TagImageTests
{
    private sealed class Ports
    {
        public OutputPort<double> Speed { get; } = new("Speed", "CV001") { Value = 1.0 };

        public OutputPort<TagQuality> SpeedHealth { get; } = new("Health", "CV001");

        public OutputPort<bool> Running { get; } = new("Running", "CV001");

        public InputPort<bool> Start { get; } = new("Start", "CV001", defaultValue: false, isRequired: false);

        public InputPort<double> Rate { get; } = new("Rate", "Feed", defaultValue: 5.0, isRequired: false);

        public TagDirectory Directory() => new(
        [
            TagBinding.Read("CV001.Speed", Speed, "m/s", 0.0, 3.0, "Belt speed", SpeedHealth),
            TagBinding.Read("CV001.Running", Running, "Contactor closed"),
            TagBinding.Write("CV001.Start", Start, "Start command"),
            TagBinding.Write("Feed.Rate", Rate, "kg/s", 0.0, 20.0, "Feed rate"),
        ]);

        public TagImage Image()
        {
            TagDirectory directory = Directory();
            foreach (TagBinding binding in directory.Bindings)
            {
                binding.BindExternal();
            }

            var image = new TagImage(directory);
            image.Prime();
            return image;
        }
    }

    [Fact]
    public void DirectoryIsSortedByOrdinalNameWithIndices()
    {
        TagDirectory directory = new Ports().Directory();

        Assert.Equal(4, directory.Count);
        Assert.Equal(
            new[] { "CV001.Running", "CV001.Speed", "CV001.Start", "Feed.Rate" },
            directory.Tags.Select(t => t.Name));
        Assert.Equal(Enumerable.Range(0, 4), directory.Tags.Select(t => t.Index));
        Assert.Same(directory.Tags[1], directory[1]);
        Assert.True(directory.TryFind("Feed.Rate", out TagDescriptor rate));
        Assert.Equal((TagKind.Double, TagAccess.ReadWrite, "kg/s", 0.0, 20.0), (rate.Kind, rate.Access, rate.Unit, rate.RangeLow, rate.RangeHigh));
        Assert.False(directory.TryFind("Nope", out _));
        Assert.Throws<KeyNotFoundException>(() => directory.Find("Nope"));
    }

    [Fact]
    public void DirectoryRejectsDuplicateNames()
    {
        var a = new OutputPort<bool>("A", "X");
        var b = new OutputPort<bool>("B", "X");

        Assert.Throws<ArgumentException>(() => new TagDirectory([TagBinding.Read("X.Same", a), TagBinding.Read("X.Same", b)]));
    }

    [Fact]
    public void ToTextListsEveryTagOnItsOwnLine()
    {
        string text = new Ports().Directory().ToText();

        string[] lines = text.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(4, lines.Length);
        Assert.Contains("CV001.Speed", lines[1], StringComparison.Ordinal);
        Assert.Contains("m/s", lines[1], StringComparison.Ordinal);
        Assert.Contains("[0, 3]", lines[1], StringComparison.Ordinal);
        Assert.Contains("ReadWrite", lines[2], StringComparison.Ordinal);
        Assert.Contains("Belt speed", lines[1], StringComparison.Ordinal);
    }

    [Fact]
    public void PrimedImageReadsCurrentValuesWithSnapshotTickMinusOne()
    {
        var ports = new Ports();
        TagImage image = ports.Image();

        Assert.Equal(-1L, image.SnapshotTick);
        Assert.Equal(1.0, image.ReadDouble("CV001.Speed"));
        Assert.False(image.ReadBool("CV001.Start"));
        Assert.Equal(5.0, image.ReadDouble("Feed.Rate"));
    }

    [Fact]
    public void ReadsSeeThePublishedSnapshotNotTheLivePort()
    {
        var ports = new Ports();
        TagImage image = ports.Image();
        ports.Speed.Value = 2.0;

        Assert.Equal(1.0, image.ReadDouble("CV001.Speed"));
        image.Publish(0);
        Assert.Equal(2.0, image.ReadDouble("CV001.Speed"));
        Assert.Equal(0L, image.SnapshotTick);

        ports.Speed.Value = 3.0;
        Assert.Equal(2.0, image.ReadDouble("CV001.Speed"));
    }

    [Fact]
    public void FirstPublishIsAllDirtyThenOnlyChanges()
    {
        var ports = new Ports();
        TagImage image = ports.Image();

        (TagValue[] first, DirtyMask firstMask) = image.Publish(0);
        Assert.Equal(4, firstMask.Count);
        Assert.Equal(4, first.Length);

        ports.Running.Value = true;
        (TagValue[] second, DirtyMask secondMask) = image.Publish(1);
        Assert.Equal(new[] { 0 }, secondMask.Indices());
        Assert.True(second[0].AsBool);
        Assert.NotSame(first, second);

        (_, DirtyMask third) = image.Publish(2);
        Assert.Equal(0, third.Count);
    }

    [Fact]
    public void QualityChangeAloneIsDirty()
    {
        var ports = new Ports();
        TagImage image = ports.Image();
        image.Publish(0);

        ports.SpeedHealth.Value = TagQuality.Bad(QualityDetail.SensorFailure);
        (TagValue[] values, DirtyMask mask) = image.Publish(1);

        Assert.Equal(new[] { 1 }, mask.Indices());
        Assert.Equal(TagQuality.Bad(QualityDetail.SensorFailure), values[1].Quality);
        Assert.Equal(TagQuality.Bad(QualityDetail.SensorFailure), image.Read("CV001.Speed").Quality);
    }

    [Fact]
    public void HandlesResolveOnceAndCheckTheKind()
    {
        var ports = new Ports();
        TagImage image = ports.Image();

        TagHandle<double> speed = image.Handle<double>("CV001.Speed");
        Assert.Equal(1, speed.Index);
        Assert.Equal(1.0, image.ReadDouble(speed));

        Assert.Throws<InvalidOperationException>(() => image.Handle<bool>("CV001.Speed"));
        Assert.Throws<KeyNotFoundException>(() => image.Handle<double>("CV001.Nope"));
        Assert.Throws<NotSupportedException>(() => image.Handle<int>("CV001.Speed"));
    }

    [Fact]
    public void WritesAreValidatedBeforeQueueing()
    {
        var ports = new Ports();
        TagImage image = ports.Image();

        Assert.Throws<InvalidOperationException>(() => image.WriteDouble("CV001.Speed", 1.0));
        Assert.Throws<InvalidOperationException>(() => image.WriteDouble("CV001.Start", 1.0));
        Assert.Throws<KeyNotFoundException>(() => image.WriteBool("CV001.Nope", true));
        Assert.Throws<ArgumentOutOfRangeException>(() => image.Write(9, TagValue.Bool(true)));
        Assert.Equal(0, image.PendingWrites);
    }

    [Fact]
    public void PendingWritesLandInOrderAndAreLogged()
    {
        var ports = new Ports();
        TagImage image = ports.Image();
        var log = new EventLog();

        image.WriteBool("CV001.Start", true);
        image.WriteDouble("Feed.Rate", 7.5);
        image.WriteDouble("Feed.Rate", 8.0);
        Assert.Equal(3, image.PendingWrites);
        Assert.False(ports.Start.Value);

        int applied = image.ApplyPendingWrites(TestContexts.Tick(4, log: log));

        Assert.Equal(3, applied);
        Assert.Equal(0, image.PendingWrites);
        Assert.True(ports.Start.Value);
        Assert.Equal(8.0, ports.Rate.Value);
        Assert.Equal(
            new[] { ("CV001.Start", "WRITE", "Set to true."), ("Feed.Rate", "WRITE", "Set to 7.5."), ("Feed.Rate", "WRITE", "Set to 8.") },
            log.Records.Select(r => (r.Source, r.Code, r.Message)));
        Assert.All(log.Records, r => Assert.Equal(4L, r.Tick));
    }

    [Fact]
    public void WrittenValueShowsInTheImageAfterPublish()
    {
        var ports = new Ports();
        TagImage image = ports.Image();

        image.WriteBool("CV001.Start", true);
        image.ApplyPendingWrites(TestContexts.Tick(0));
        Assert.False(image.ReadBool("CV001.Start"));

        image.Publish(0);
        Assert.True(image.ReadBool("CV001.Start"));
    }

    [Fact]
    public void ReadsFromAnotherThreadSeeWholeSnapshots()
    {
        var ports = new Ports();
        TagImage image = ports.Image();
        using var stop = new CancellationTokenSource();
        int torn = 0;

        var reader = new Thread(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                TagValue speed = image.Read(1);
                TagValue running = image.Read(0);
                if (speed.AsDouble >= 100.0 != running.AsBool)
                {
                    Interlocked.Increment(ref torn);
                }
            }
        });
        reader.Start();

        for (int tick = 0; tick < 20_000; tick++)
        {
            bool high = tick % 2 == 1;
            ports.Speed.Value = high ? 100.0 : 1.0;
            ports.Running.Value = high;
            image.Publish(tick);
        }

        stop.Cancel();
        reader.Join();

        Assert.Equal(0, torn);
    }
}
```

Note: `TestContexts` in `Dse.Core.Tests.Fakes` already has `Tick(long tick,
double dt = 0.01, EventLog? log = null)` — check with
`grep -n "public static TickContext Tick" tests/Dse.Core.Tests/Fakes/TestContexts.cs`;
if its signature differs, adapt the two calls above to it rather than changing
the fake.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Dse.Core.Tests --filter "FullyQualifiedName~TagImageTests"`
Expected: build FAILS — `TagDirectory`, `TagImage` do not exist.

- [ ] **Step 3: Write `TagDirectory`**

`src/Dse.Core/Io/TagDirectory.cs`:

```csharp
using System.Globalization;
using System.Text;
using Dse.Io;

namespace Dse.Core.Io;

/// <summary>
/// The plant's tag directory (spec 9.1): bindings sorted by ordinal name, each
/// given the index it keeps for the life of the simulation. Built once by the
/// builder; immutable.
/// </summary>
public sealed class TagDirectory : ITagDirectory
{
    private readonly TagDescriptor[] _descriptors;
    private readonly Dictionary<string, int> _indexByName = new(StringComparer.Ordinal);

    internal TagDirectory(IEnumerable<TagBinding> fullyNamed)
    {
        ArgumentNullException.ThrowIfNull(fullyNamed);

        Bindings = fullyNamed.OrderBy(b => b.Name, StringComparer.Ordinal).ToArray();
        _descriptors = new TagDescriptor[Bindings.Length];
        for (int i = 0; i < Bindings.Length; i++)
        {
            TagBinding b = Bindings[i];
            if (!_indexByName.TryAdd(b.Name, i))
            {
                throw new ArgumentException(
                    $"Tag '{b.Name}' is bound twice ('{Bindings[_indexByName[b.Name]].Port.QualifiedName}' and " +
                    $"'{b.Port.QualifiedName}'). Names must be unique.",
                    nameof(fullyNamed));
            }

            _descriptors[i] = new TagDescriptor(i, b.Name, b.Kind, b.Access, b.Unit, b.RangeLow, b.RangeHigh, b.Description);
        }
    }

    /// <summary>The bindings in index order.</summary>
    internal TagBinding[] Bindings { get; }

    /// <inheritdoc/>
    public int Count => _descriptors.Length;

    /// <inheritdoc/>
    public IReadOnlyList<TagDescriptor> Tags => _descriptors;

    /// <inheritdoc/>
    public TagDescriptor this[int index] => _descriptors[index];

    /// <inheritdoc/>
    public bool TryFind(string name, out TagDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (_indexByName.TryGetValue(name, out int index))
        {
            descriptor = _descriptors[index];
            return true;
        }

        descriptor = null!;
        return false;
    }

    /// <inheritdoc/>
    public TagDescriptor Find(string name)
    {
        if (TryFind(name, out TagDescriptor descriptor))
        {
            return descriptor;
        }

        throw new KeyNotFoundException(
            $"No tag '{name}'. The directory has {Count} tags; call ToText() to list them.");
    }

    /// <summary>One line per tag: name, kind, access, unit, range, description.</summary>
    public string ToText()
    {
        var builder = new StringBuilder();
        foreach (TagDescriptor tag in _descriptors)
        {
            builder.Append(tag.Name)
                   .Append("  ").Append(tag.Kind)
                   .Append("  ").Append(tag.Access);
            if (tag.Unit.Length > 0)
            {
                builder.Append("  ").Append(tag.Unit);
            }

            if (tag.HasRange)
            {
                builder.Append(string.Create(CultureInfo.InvariantCulture, $"  [{tag.RangeLow}, {tag.RangeHigh}]"));
            }

            if (tag.Description.Length > 0)
            {
                builder.Append("  ").Append(tag.Description);
            }

            builder.Append(Environment.NewLine);
        }

        return builder.ToString();
    }
}
```

- [ ] **Step 4: Write `TagImage`**

`src/Dse.Core/Io/TagImage.cs`:

```csharp
using System.Collections.Concurrent;
using Dse.Core.Contexts;
using Dse.Io;

namespace Dse.Core.Io;

/// <summary>
/// The I/O image (spec 9.3): a double-buffered snapshot any thread may read,
/// and a lock-free write queue drained at phase 1. The simulation thread is the
/// only writer of the model; the published array is never mutated after the
/// swap, so readers take no lock and can never see a half-updated plant.
/// </summary>
public sealed class TagImage : ITagReader, ITagWriter
{
    private readonly TagBinding[] _bindings;
    private readonly ConcurrentQueue<PendingWrite> _writes = new();
    private TagValue[] _front;
    private long _snapshotTick = -1;
    private bool _published;

    internal TagImage(TagDirectory directory)
    {
        ArgumentNullException.ThrowIfNull(directory);
        Directory = directory;
        _bindings = directory.Bindings;
        _front = new TagValue[_bindings.Length];
    }

    /// <summary>The directory this image indexes.</summary>
    public TagDirectory Directory { get; }

    ITagDirectory ITagReader.Directory => Directory;

    ITagDirectory ITagWriter.Directory => Directory;

    /// <inheritdoc/>
    public long SnapshotTick => Volatile.Read(ref _snapshotTick);

    /// <summary>Writes queued and not yet applied.</summary>
    public int PendingWrites => _writes.Count;

    /// <inheritdoc/>
    public TagValue Read(int index)
    {
        TagValue[] front = Volatile.Read(ref _front);
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, front.Length);
        return front[index];
    }

    /// <inheritdoc/>
    public TagValue Read(string name) => Read(Directory.Find(name).Index);

    /// <inheritdoc/>
    public TagHandle<T> Handle<T>(string name)
        where T : unmanaged
    {
        TagKind wanted = TagValue.KindOf<T>();
        TagDescriptor tag = Directory.Find(name);
        if (tag.Kind != wanted)
        {
            throw new InvalidOperationException(
                $"Tag '{name}' is a {tag.Kind} tag; a TagHandle<{typeof(T).Name}> needs {wanted}.");
        }

        return new TagHandle<T>(tag.Index, tag.Name);
    }

    /// <inheritdoc/>
    public void Write(int index, TagValue value)
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

        _writes.Enqueue(new PendingWrite(index, value));
    }

    /// <inheritdoc/>
    public void Write(string name, TagValue value) => Write(Directory.Find(name).Index, value);

    /// <summary>Captures the initial values before the first tick. Called from <c>Simulation.Initialize</c>.</summary>
    internal void Prime()
    {
        var values = new TagValue[_bindings.Length];
        for (int i = 0; i < values.Length; i++)
        {
            values[i] = _bindings[i].Capture();
        }

        Volatile.Write(ref _front, values);
    }

    /// <summary>
    /// Phase 4: captures every binding into a fresh array, diffs it against the
    /// previous snapshot, and publishes it. The first publish is wholly dirty.
    /// Returns the published array and mask so phase 5 can wrap them in a frame
    /// without copying.
    /// </summary>
    internal (TagValue[] Values, DirtyMask Dirty) Publish(long tick)
    {
        TagValue[] previous = _front;
        var values = new TagValue[_bindings.Length];
        var words = new ulong[DirtyMask.WordsFor(values.Length)];
        int count = 0;

        for (int i = 0; i < values.Length; i++)
        {
            values[i] = _bindings[i].Capture();
            if (!_published || values[i] != previous[i])
            {
                words[i >> 6] |= 1UL << (i & 63);
                count++;
            }
        }

        Volatile.Write(ref _front, values);
        Volatile.Write(ref _snapshotTick, tick);
        _published = true;
        return (values, DirtyMask.FromBits(words, values.Length, count));
    }

    /// <summary>
    /// Phase 1: applies every write queued before entry, in enqueue order, and
    /// logs each as <c>WRITE</c> from the tag name (R24).
    /// </summary>
    internal int ApplyPendingWrites(in TickContext ctx)
    {
        int budget = _writes.Count;
        int applied = 0;
        while (applied < budget && _writes.TryDequeue(out PendingWrite write))
        {
            TagBinding binding = _bindings[write.Index];
            binding.Apply(write.Value);
            ctx.Log(binding.Name, "WRITE", $"Set to {write.Value}.");
            applied++;
        }

        return applied;
    }

    private readonly record struct PendingWrite(int Index, TagValue Value);
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/Dse.Core.Tests --filter "FullyQualifiedName~TagImageTests"`
Expected: PASS, 12 tests. The thread test must pass ten times in a row:
`for i in $(seq 10); do dotnet test tests/Dse.Core.Tests --filter "FullyQualifiedName~ReadsFromAnotherThreadSeeWholeSnapshots" --no-build || break; done`.
`dotnet build -c Release` — 0 warnings.

- [ ] **Step 6: Commit**

```bash
git add src/Dse.Core/Io/TagDirectory.cs src/Dse.Core/Io/TagImage.cs tests/Dse.Core.Tests/TagImageTests.cs
git commit -m "feat(core): add the tag directory and the double-buffered I/O image with its write queue"
```

---

### Task 6: Tag collection in the builder and the three real phases in `Simulation`

**Files:**
- Modify: `src/Dse.Core/Graph/CompositeComponent.cs` (two internal enumerators)
- Modify: `src/Dse.Core/SimulationBuilder.cs`
- Modify: `src/Dse.Core/Simulation.cs`
- Test: `tests/Dse.Core.Tests/Fakes/Thermostat.cs`
- Test: `tests/Dse.Core.Tests/Fakes/Pair.cs`
- Test: `tests/Dse.Core.Tests/Fakes/FrameCollector.cs`
- Test: `tests/Dse.Core.Tests/IoIntegrationTests.cs`

**Interfaces:**
- Consumes: Tasks 2–5.
- Produces: `SimulationBuilder.Bind(string name, TagBinding binding)`;
  `Simulation.AttachFrameSink(ITickFrameSink sink)` (once, any time; the hub
  needs the directory, which exists only after `Build()`); validation codes
  `DSE009` (duplicate tag name or port bound twice), `DSE010` (explicitly bound
  writable tag on a driven input; a *declared* one degrades to read-only), `DSE011` (binding on a port outside the plant or outside the
  declaring component); `Simulation.IO` (`TagImage`), `Simulation.FrameSink`
  (`ITickFrameSink?`); `Simulation.Tick()` applies writes at phase 1, publishes
  at phase 4, emits a `TickFrame` at phase 5; `Simulation.Initialize()` primes
  the image. On `CompositeComponent`: `internal IEnumerable<CompositeComponent>
  CompositesInsideOut()`, `internal IEnumerable<(string Alias, Port Port)>
  ExposedSignalPorts()`.

Collection order (R22): leaf declarations first, qualified by component id;
then composite renames, inside-out, so an outer alias overrides an inner one;
then explicit `Bind` calls, which add or replace. Duplicate names and explicit
writable binds on driven inputs are validation errors, not exceptions, so a
plant author sees them all at once with the other DSE codes. A declared
writable binding on a driven input degrades to read-only (R23) so a plant that
wires a controller or a test switch into a starter keeps building.

- [ ] **Step 1: Write the fakes and the failing tests**

`tests/Dse.Core.Tests/Fakes/Thermostat.cs`:

```csharp
using Dse.Core.Contexts;
using Dse.Core.Graph;
using Dse.Core.Io;
using Dse.Io;

namespace Dse.Core.Tests.Fakes;

/// <summary>
/// Two writable inputs, one analog output with a quality source. Output is
/// twice the setpoint while enabled, else zero.
/// </summary>
public sealed class Thermostat : ComponentBase, ITagProvider
{
    public Thermostat(string id)
        : base(id)
    {
        Setpoint = AddInput<double>("Setpoint", defaultValue: 20.0);
        Enable = AddInput<bool>("Enable");
        Output = AddOutput<double>("Output");
        Health = AddOutput<TagQuality>("Health");
    }

    public InputPort<double> Setpoint { get; }

    public InputPort<bool> Enable { get; }

    public OutputPort<double> Output { get; }

    public OutputPort<TagQuality> Health { get; }

    /// <summary>What <see cref="Health"/> publishes next tick.</summary>
    public TagQuality Quality { get; set; } = TagQuality.Good;

    public override void Evaluate(in TickContext ctx)
    {
        Output.Value = Enable.Value ? Setpoint.Value * 2.0 : 0.0;
        Health.Value = Quality;
    }

    public IEnumerable<TagBinding> DescribeTags() =>
    [
        TagBinding.Write("Setpoint", Setpoint, "°C", 0.0, 100.0, "Temperature setpoint"),
        TagBinding.Write("Enable", Enable, "Heater enable"),
        TagBinding.Read("Output", Output, "%", 0.0, 200.0, "Heater output", Health),
    ];
}
```

`tests/Dse.Core.Tests/Fakes/Pair.cs`:

```csharp
using Dse.Core.Graph;

namespace Dse.Core.Tests.Fakes;

/// <summary>Two thermostats; exposes A's setpoint as SP and B's output as Out.</summary>
public sealed class Pair : CompositeComponent
{
    public Pair(string id)
        : base(id)
    {
        A = AddChild(new Thermostat("A"));
        B = AddChild(new Thermostat("B"));
        Expose("SP", A.Setpoint);
        Expose("Out", B.Output);
    }

    public Thermostat A { get; }

    public Thermostat B { get; }
}
```

`tests/Dse.Core.Tests/Fakes/FrameCollector.cs`:

```csharp
using Dse.Io;

namespace Dse.Core.Tests.Fakes;

/// <summary>Keeps every frame it is handed.</summary>
public sealed class FrameCollector : ITickFrameSink
{
    public List<TickFrame> Frames { get; } = [];

    public void Publish(TickFrame frame) => Frames.Add(frame);
}
```

`tests/Dse.Core.Tests/IoIntegrationTests.cs`:

```csharp
using Dse.Core.Contexts;
using Dse.Core.Graph;
using Dse.Core.Io;
using Dse.Core.Tests.Fakes;
using Dse.Core.Time;
using Dse.Core.Validation;
using Dse.Io;
using Xunit;

namespace Dse.Core.Tests;

public class IoIntegrationTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 6, 0, 0, TimeSpan.Zero);

    private static SimulationOptions Options => new()
    {
        Seed = 1UL,
        StartTime = Start,
        TimeStep = TimeSpan.FromMilliseconds(10),
    };

    private static (Simulation Sim, Thermostat T, FrameCollector Frames) Plant()
    {
        var t = new Thermostat("T");
        var frames = new FrameCollector();
        Simulation sim = new SimulationBuilder(Options).Add(t).Build();
        sim.AttachFrameSink(frames);
        return (sim, t, frames);
    }

    /// <summary>Declares a tag on a port it does not own.</summary>
    private sealed class Naughty : ComponentBase, ITagProvider
    {
        private readonly OutputPort<double> _foreign;

        public Naughty(string id, OutputPort<double> foreign)
            : base(id) => _foreign = foreign;

        public override void Evaluate(in TickContext ctx)
        {
        }

        public IEnumerable<TagBinding> DescribeTags() => [TagBinding.Read("Stolen", _foreign, "V", 0.0, 1.0)];
    }

    [Fact]
    public void DirectoryListsDeclaredTagsWithFullNames()
    {
        (Simulation sim, _, _) = Plant();

        Assert.Equal(new[] { "T.Enable", "T.Output", "T.Setpoint" }, sim.IO.Directory.Tags.Select(t => t.Name));
        TagDescriptor output = sim.IO.Directory.Find("T.Output");
        Assert.Equal(("%", 0.0, 200.0, TagAccess.ReadOnly, "Heater output"), (output.Unit, output.RangeLow, output.RangeHigh, output.Access, output.Description));
        Assert.Equal(TagAccess.ReadWrite, sim.IO.Directory.Find("T.Setpoint").Access);
    }

    [Fact]
    public void CompositeAliasRenamesTheLeafTag()
    {
        var pair = new Pair("P1");
        Simulation sim = new SimulationBuilder(Options).Add(pair).Build();

        string[] names = sim.IO.Directory.Tags.Select(t => t.Name).ToArray();
        Assert.Equal(new[] { "P1.A.Enable", "P1.A.Output", "P1.B.Enable", "P1.B.Setpoint", "P1.Out", "P1.SP" }, names);
        Assert.Equal(TagAccess.ReadWrite, sim.IO.Directory.Find("P1.SP").Access);
    }

    [Fact]
    public void ExplicitBindAddsATruthTag()
    {
        var source = new ConstantSource("S", 3.0);
        var recorder = new Recorder("R");
        source.Out.ConnectTo(recorder.In);
        Simulation sim = new SimulationBuilder(Options)
            .Add(source).Add(recorder)
            .Bind("Truth.S", TagBinding.Read("Out", source.Out, "V", 0.0, 10.0, "Source truth"))
            .Build();

        sim.Tick();

        Assert.Equal(3.0, sim.IO.ReadDouble("Truth.S"));
        Assert.Equal("Truth.S", Assert.Single(sim.IO.Directory.Tags).Name);
    }

    [Fact]
    public void ExplicitBindReplacesADeclaredTag()
    {
        var t = new Thermostat("T");
        Simulation sim = new SimulationBuilder(Options)
            .Add(t)
            .Bind("Ctrl.SP", TagBinding.Write("x", t.Setpoint, "°C", 0.0, 50.0))
            .Build();

        Assert.Equal(new[] { "Ctrl.SP", "T.Enable", "T.Output" }, sim.IO.Directory.Tags.Select(x => x.Name));
    }

    [Fact]
    public void DuplicateTagNameIsDse009()
    {
        var s1 = new ConstantSource("S1", 1.0);
        var s2 = new ConstantSource("S2", 2.0);
        ValidationResult result = new SimulationBuilder(Options)
            .Add(s1).Add(s2)
            .Bind("Same", TagBinding.Read("Out", s1.Out, "V", 0.0, 1.0))
            .Bind("Same", TagBinding.Read("Out", s2.Out, "V", 0.0, 1.0))
            .Validate();

        ValidationError error = Assert.Single(result.Errors);
        Assert.Equal("DSE009", error.Code);
        Assert.Contains("S1.Out", error.Message, StringComparison.Ordinal);
        Assert.Contains("S2.Out", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DeclaredWritableTagOnADrivenInputBecomesReadOnly()
    {
        var t = new Thermostat("T");
        var source = new ConstantSource("S", 30.0);
        source.Out.ConnectTo(t.Setpoint);
        Simulation sim = new SimulationBuilder(Options).Add(t).Add(source).Build();

        sim.Tick();

        Assert.Equal(TagAccess.ReadOnly, sim.IO.Directory.Find("T.Setpoint").Access);
        Assert.Equal(30.0, sim.IO.ReadDouble("T.Setpoint"));
        Assert.Throws<InvalidOperationException>(() => sim.IO.WriteDouble("T.Setpoint", 1.0));
        Assert.False(t.Setpoint.IsExternallyDriven);
    }

    [Fact]
    public void ExplicitlyBoundWritableTagOnADrivenInputIsDse010()
    {
        var t = new Thermostat("T");
        var source = new ConstantSource("S", 30.0);
        source.Out.ConnectTo(t.Setpoint);

        ValidationResult result = new SimulationBuilder(Options)
            .Add(t).Add(source)
            .Bind("SP", TagBinding.Write("Setpoint", t.Setpoint, "°C", 0.0, 100.0))
            .Validate();

        ValidationError error = Assert.Single(result.Errors);
        Assert.Equal("DSE010", error.Code);
        Assert.Contains("T.Setpoint", error.Message, StringComparison.Ordinal);
        Assert.Contains("S.Out", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void BindOnAPortOutsideThePlantIsDse011()
    {
        var inside = new ConstantSource("In", 1.0);
        var outside = new ConstantSource("Out", 1.0);

        ValidationResult result = new SimulationBuilder(Options)
            .Add(inside)
            .Bind("X", TagBinding.Read("Out", outside.Out, "V", 0.0, 1.0))
            .Validate();

        Assert.Equal("DSE011", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public void DeclaringATagOnAnotherComponentsPortIsDse011()
    {
        var victim = new ConstantSource("V", 1.0);
        var naughty = new Naughty("N", victim.Out);

        ValidationResult result = new SimulationBuilder(Options).Add(victim).Add(naughty).Validate();

        ValidationError error = Assert.Single(result.Errors);
        Assert.Equal("DSE011", error.Code);
        Assert.Contains("N", error.ComponentIds);
    }

    [Fact]
    public void BeforeTheFirstTickTheImageIsPrimed()
    {
        (Simulation sim, _, _) = Plant();
        sim.Initialize();

        Assert.Equal(-1L, sim.IO.SnapshotTick);
        Assert.Equal(20.0, sim.IO.ReadDouble("T.Setpoint"));
        Assert.Equal(0.0, sim.IO.ReadDouble("T.Output"));
    }

    [Fact]
    public void WritesLandAtPhaseOneOfTheNextTick()
    {
        (Simulation sim, _, _) = Plant();
        sim.RunFor(TimeSpan.FromMilliseconds(30));

        sim.IO.WriteDouble("T.Setpoint", 10.0);
        sim.IO.WriteBool("T.Enable", true);
        Assert.Equal(0.0, sim.IO.ReadDouble("T.Output"));
        Assert.Equal(2, sim.IO.PendingWrites);

        sim.Tick();

        Assert.Equal(20.0, sim.IO.ReadDouble("T.Output"));
        Assert.Equal(3L, sim.IO.SnapshotTick);
        Assert.Equal(
            new[] { (3L, "T.Setpoint", "WRITE", "Set to 10."), (3L, "T.Enable", "WRITE", "Set to true.") },
            sim.Events.Records.Select(r => (r.Tick, r.Source, r.Code, r.Message)));
    }

    [Fact]
    public void FramesArriveOncePerTickWithDirtyMasksAndEvents()
    {
        (Simulation sim, _, FrameCollector frames) = Plant();
        sim.RunFor(TimeSpan.FromMilliseconds(20));

        Assert.Equal(2, frames.Frames.Count);
        Assert.Equal(0L, frames.Frames[0].Tick);
        Assert.Equal(3, frames.Frames[0].Dirty.Count);
        Assert.Equal(0, frames.Frames[1].Dirty.Count);
        Assert.Empty(frames.Frames[1].Events);

        sim.IO.WriteBool("T.Enable", true);
        sim.Tick();

        TickFrame frame = frames.Frames[2];
        Assert.Equal(2L, frame.Tick);
        Assert.Equal(Start + TimeSpan.FromMilliseconds(20), frame.SimTime);
        Assert.Equal(new[] { 0, 1 }, frame.Dirty.Indices());
        Assert.True(frame.Values.Span[0].AsBool);
        Assert.Equal(40.0, frame.Values.Span[1].AsDouble);
        DiscreteEvent written = Assert.Single(frame.Events);
        Assert.Equal(("T.Enable", "WRITE", 2L), (written.Source, written.Code, written.Tick));
    }

    [Fact]
    public void FrameValuesAreTheSnapshotTheReaderSees()
    {
        (Simulation sim, _, FrameCollector frames) = Plant();
        sim.IO.WriteBool("T.Enable", true);
        sim.Tick();

        TickFrame frame = frames.Frames[0];
        for (int i = 0; i < sim.IO.Directory.Count; i++)
        {
            Assert.Equal(frame.Values.Span[i], sim.IO.Read(i));
        }
    }

    [Fact]
    public void QualityFlowsFromTheSourcePort()
    {
        (Simulation sim, Thermostat t, FrameCollector frames) = Plant();
        t.Quality = TagQuality.Bad(QualityDetail.SensorFailure);

        sim.Tick();

        Assert.Equal(TagQuality.Bad(QualityDetail.SensorFailure), sim.IO.Read("T.Output").Quality);
        Assert.Equal(TagQuality.Bad(QualityDetail.SensorFailure), frames.Frames[0].Values.Span[1].Quality);
        Assert.True(sim.IO.Read("T.Setpoint").Quality.IsGood);
    }

    [Fact]
    public void HandlesReadWithoutLookups()
    {
        (Simulation sim, _, _) = Plant();
        TagHandle<double> output = sim.IO.Handle<double>("T.Output");
        TagHandle<bool> enable = sim.IO.Handle<bool>("T.Enable");

        sim.IO.WriteBool(enable, true);
        sim.Tick();

        Assert.Equal(40.0, sim.IO.ReadDouble(output));
        Assert.True(sim.IO.ReadBool(enable));
    }

    [Fact]
    public void TwoRunsWithTheSameWritesProduceIdenticalFrames()
    {
        static List<TickFrame> Run()
        {
            (Simulation sim, _, FrameCollector frames) = Plant();
            sim.RunFor(TimeSpan.FromMilliseconds(50));
            sim.IO.WriteBool("T.Enable", true);
            sim.IO.WriteDouble("T.Setpoint", 33.0);
            sim.RunFor(TimeSpan.FromMilliseconds(50));
            return frames.Frames;
        }

        List<TickFrame> a = Run();
        List<TickFrame> b = Run();

        Assert.Equal(a.Count, b.Count);
        for (int i = 0; i < a.Count; i++)
        {
            Assert.Equal(a[i].Tick, b[i].Tick);
            Assert.Equal(a[i].Values.ToArray(), b[i].Values.ToArray());
            Assert.Equal(a[i].Dirty.Indices(), b[i].Dirty.Indices());
            Assert.Equal(a[i].Events.Select(e => (e.Source, e.Code)), b[i].Events.Select(e => (e.Source, e.Code)));
        }
    }

    [Fact]
    public void WithoutASinkNoFrameIsBuilt()
    {
        var t = new Thermostat("T");
        Simulation sim = new SimulationBuilder(Options).Add(t).Build();

        Assert.Null(sim.FrameSink);
        sim.RunFor(TimeSpan.FromMilliseconds(30));

        Assert.Equal(2L, sim.IO.SnapshotTick);
    }

    [Fact]
    public void ASinkAttachedMidRunGetsFramesFromThenOn()
    {
        var t = new Thermostat("T");
        var frames = new FrameCollector();
        Simulation sim = new SimulationBuilder(Options).Add(t).Build();
        sim.RunFor(TimeSpan.FromMilliseconds(30));

        sim.AttachFrameSink(frames);
        sim.Tick();

        TickFrame frame = Assert.Single(frames.Frames);
        Assert.Equal(3L, frame.Tick);
        Assert.Equal(0, frame.Dirty.Count);
        Assert.Equal(3, frame.Values.Length);
    }

    [Fact]
    public void OnlyOneSinkMayBeAttached()
    {
        (Simulation sim, _, _) = Plant();

        Assert.Throws<InvalidOperationException>(() => sim.AttachFrameSink(new FrameCollector()));
    }

    [Fact]
    public void BindAfterBuildThrows()
    {
        var t = new Thermostat("T");
        var builder = new SimulationBuilder(Options).Add(t);
        builder.Build();

        Assert.Throws<InvalidOperationException>(() =>
            builder.Bind("X", TagBinding.Read("Output", t.Output, "%", 0.0, 1.0)));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Dse.Core.Tests --filter "FullyQualifiedName~IoIntegrationTests"`
Expected: build FAILS — `Bind`, `AttachFrameSink`, `Simulation.IO` do not exist.

- [ ] **Step 3: Add the composite enumerators**

In `src/Dse.Core/Graph/CompositeComponent.cs`, add after `Leaves()`:

```csharp
    /// <summary>Every nested composite, children before parents, this one last. Build-time only.</summary>
    internal IEnumerable<CompositeComponent> CompositesInsideOut()
    {
        foreach (ISimNode child in _children)
        {
            if (child is CompositeComponent composite)
            {
                foreach (CompositeComponent nested in composite.CompositesInsideOut())
                {
                    yield return nested;
                }
            }
        }

        yield return this;
    }

    /// <summary>Exposed signal ports (not flow ports) by alias, in ordinal alias order. Build-time only.</summary>
    internal IEnumerable<(string Alias, Port Port)> ExposedSignalPorts() =>
        _aliases
            .Where(pair => pair.Value is not FlowPort)
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => (pair.Key, pair.Value));
```

- [ ] **Step 4: Rewrite `SimulationBuilder`**

Replace `src/Dse.Core/SimulationBuilder.cs` with (existing XML docs kept where the
member is unchanged; the DSE001–DSE008 checks are verbatim from the current file):

```csharp
using Dse.Core.Flow;
using Dse.Core.Graph;
using Dse.Core.Io;
using Dse.Core.Time;
using Dse.Core.Validation;
using Dse.Io;

namespace Dse.Core;

public sealed class SimulationBuilder
{
    private readonly List<ISimComponent> _components = [];
    private readonly List<CompositeComponent> _composites = [];
    private readonly List<(string Name, TagBinding Binding)> _explicitTags = [];
    private readonly SimulationOptions _options;
    private bool _built;

    public SimulationBuilder(SimulationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    public SimulationBuilder Add(ISimNode node)
    {
        ThrowIfBuilt();
        ArgumentNullException.ThrowIfNull(node);

        switch (node)
        {
            case ISimComponent component:
                _components.Add(component);
                break;
            case CompositeComponent composite:
                _components.AddRange(composite.Leaves());
                _composites.Add(composite);
                break;
            default:
                throw new ArgumentException(
                    $"'{node.GetType().Name}' is neither an {nameof(ISimComponent)} nor a " +
                    $"{nameof(CompositeComponent)}.",
                    nameof(node));
        }

        return this;
    }

    /// <summary>
    /// Binds a tag under an explicit full name (R22). Adds a tag for a port
    /// nothing declared, or replaces the declared binding for that port.
    /// </summary>
    public SimulationBuilder Bind(string name, TagBinding binding)
    {
        ThrowIfBuilt();
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(binding);
        _explicitTags.Add((name, binding));
        return this;
    }

    public ValidationResult Validate() => Validate(out _);

    public Simulation Build()
    {
        ThrowIfBuilt();

        ValidationResult result = Validate(out List<TagBinding> tags);
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
        return new Simulation(ordered, flow, _options, image);
    }

    private ValidationResult Validate(out List<TagBinding> tags)
    {
        var errors = new List<ValidationError>();

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (ISimComponent component in _components)
        {
            if (!seen.Add(component.Id))
            {
                errors.Add(new ValidationError(
                    "DSE001",
                    $"Duplicate component id '{component.Id}'. Ids must be unique across the " +
                    $"whole plant; rename one of them or place it inside a composite.",
                    [component.Id]));
            }
        }

        foreach (ISimComponent component in _components)
        {
            foreach (Port port in component.Ports)
            {
                if (port.IsMissingRequiredConnection)
                {
                    errors.Add(new ValidationError(
                        "DSE002",
                        $"Input '{port.QualifiedName}' is required but nothing drives it. " +
                        $"Connect an output to it, or declare the input optional with a default.",
                        [component.Id]));
                }
            }
        }

        foreach (ISimComponent component in _components)
        {
            foreach (Port port in component.Ports)
            {
                if (port.SourcePort is { } source && !seen.Contains(source.OwnerId))
                {
                    errors.Add(new ValidationError(
                        "DSE004",
                        $"Input '{port.QualifiedName}' is driven by '{source.QualifiedName}', but " +
                        $"component '{source.OwnerId}' is not part of the plant. Add it to the " +
                        $"builder, or add the composite that contains it.",
                        [component.Id, source.OwnerId]));
                }
            }
        }

        if (!GraphResolver.TryResolve(_components, out _, out IReadOnlyList<string> cycle))
        {
            string path = string.Join(" -> ", cycle.Append(cycle.Count > 0 ? cycle[0] : string.Empty));
            errors.Add(new ValidationError(
                "DSE003",
                $"Algebraic loop: {path}. Insert a UnitDelay on one connection in the cycle to " +
                $"break it; one tick of lag is physically irrelevant and makes the solve order " +
                $"unambiguous.",
                cycle));
        }

        errors.AddRange(FlowGraph.Validate(FlowNodes(), seen, _options.TimeStep.TotalSeconds));

        tags = CollectTags(seen, errors);

        return ValidationResult.From(errors);
    }

    /// <summary>
    /// R22: leaf declarations qualified by component id, then composite aliases
    /// inside-out, then explicit binds. Every port ends with at most one
    /// binding; every name must be unique; a declared writable binding on a
    /// driven input degrades to read-only and an explicit one is an error (R23).
    /// </summary>
    private List<TagBinding> CollectTags(HashSet<string> componentIds, List<ValidationError> errors)
    {
        var byPort = new Dictionary<Port, TagBinding>(ReferenceEqualityComparer.Instance);
        var explicitPorts = new HashSet<Port>(ReferenceEqualityComparer.Instance);
        var order = new List<Port>();

        foreach (ISimComponent component in _components)
        {
            if (component is not ITagProvider provider)
            {
                continue;
            }

            foreach (TagBinding binding in provider.DescribeTags())
            {
                if (!string.Equals(binding.Port.OwnerId, component.Id, StringComparison.Ordinal))
                {
                    errors.Add(new ValidationError(
                        "DSE011",
                        $"Component '{component.Id}' declares tag '{binding.Name}' on port " +
                        $"'{binding.Port.QualifiedName}', which belongs to '{binding.Port.OwnerId}'. " +
                        $"A component declares tags on its own ports only.",
                        [component.Id, binding.Port.OwnerId]));
                    continue;
                }

                TagBinding qualified = binding.WithName($"{component.Id}.{binding.Name}");
                if (byPort.TryGetValue(binding.Port, out TagBinding? first))
                {
                    errors.Add(new ValidationError(
                        "DSE009",
                        $"Port '{binding.Port.QualifiedName}' is bound to two tags ('{first.Name}' and " +
                        $"'{qualified.Name}') by '{component.Id}'. Bind each port once.",
                        [component.Id]));
                    continue;
                }

                byPort[binding.Port] = qualified;
                order.Add(binding.Port);
            }
        }

        foreach (CompositeComponent top in _composites)
        {
            foreach (CompositeComponent composite in top.CompositesInsideOut())
            {
                foreach ((string alias, Port port) in composite.ExposedSignalPorts())
                {
                    if (byPort.TryGetValue(port, out TagBinding? declared))
                    {
                        byPort[port] = declared.WithName($"{composite.Id}.{alias}");
                    }
                }
            }
        }

        foreach ((string name, TagBinding binding) in _explicitTags)
        {
            if (!componentIds.Contains(binding.Port.OwnerId))
            {
                errors.Add(new ValidationError(
                    "DSE011",
                    $"Tag '{name}' binds port '{binding.Port.QualifiedName}', but component " +
                    $"'{binding.Port.OwnerId}' is not part of the plant. Add it to the builder.",
                    [binding.Port.OwnerId]));
                continue;
            }

            if (!byPort.ContainsKey(binding.Port))
            {
                order.Add(binding.Port);
            }

            byPort[binding.Port] = binding.WithName(name);
            explicitPorts.Add(binding.Port);
        }

        var result = new List<TagBinding>(order.Count);
        var byName = new Dictionary<string, TagBinding>(StringComparer.Ordinal);
        foreach (Port port in order)
        {
            TagBinding binding = byPort[port];
            if (byName.TryGetValue(binding.Name, out TagBinding? other))
            {
                errors.Add(new ValidationError(
                    "DSE009",
                    $"Tag '{binding.Name}' is bound to both '{other.Port.QualifiedName}' and " +
                    $"'{binding.Port.QualifiedName}'. Rename one with Bind or a composite alias.",
                    [other.Port.OwnerId, binding.Port.OwnerId]));
                continue;
            }

            if (binding.Access == TagAccess.ReadWrite && port.SourcePort is { } source)
            {
                if (explicitPorts.Contains(port))
                {
                    errors.Add(new ValidationError(
                        "DSE010",
                        $"Tag '{binding.Name}' would drive input '{port.QualifiedName}', but " +
                        $"'{source.QualifiedName}' already drives it. Remove the connection, or bind a " +
                        $"read-only tag instead.",
                        [port.OwnerId, source.OwnerId]));
                    continue;
                }

                // R23: the plant wired a controller here; the declared tag observes the command.
                binding = binding.AsReadOnly();
            }

            byName[binding.Name] = binding;
            result.Add(binding);
        }

        return result;
    }

    private void FreezePorts()
    {
        foreach (ISimComponent component in _components)
        {
            foreach (Port port in component.Ports)
            {
                port.Freeze();
            }
        }
    }

    private void ThrowIfBuilt()
    {
        if (_built)
        {
            throw new InvalidOperationException(
                "This builder has already produced a simulation; the plant is immutable after Build(). " +
                "Create a new builder for a different plant.");
        }
    }

    private List<IFlowNode> FlowNodes() => _components.OfType<IFlowNode>().ToList();
}
```

- [ ] **Step 5: Wire `Simulation`**

In `src/Dse.Core/Simulation.cs`:

Add `using Dse.Core.Io;` and `using Dse.Io;`.

Change the constructor signature and body:

```csharp
    internal Simulation(
        ISimComponent[] components,
        FlowGraph flow,
        SimulationOptions options,
        TagImage io)
    {
        _components = components;
        _flow = flow;
        _seed = options.Seed;
        _checkConservation = options.CheckConservation;
        _conservationTolerance = options.ConservationTolerance;
        Clock = new SimulationClock(options.StartTime, options.TimeStep);
        IO = io;

        _faultTargets = new Dictionary<string, IFaultTarget>(StringComparer.Ordinal);
        foreach (ISimComponent component in components)
        {
            if (component is IFaultTarget target)
            {
                _faultTargets[component.Id] = target;
            }
        }
    }
```

Add two properties and the attach method after `Clock`:

```csharp
    /// <summary>The I/O image: the string and handle read API, and the queued write API (spec 9).</summary>
    public TagImage IO { get; }

    /// <summary>Where phase 5 hands each frame, or null when nothing is attached.</summary>
    public ITickFrameSink? FrameSink { get; private set; }

    /// <summary>
    /// Attaches the one frame sink (spec 10.1). May be called at any time —
    /// a late-attached consumer sees frames from the next tick on, and the
    /// first frame it receives carries the full image — but only once.
    /// </summary>
    public void AttachFrameSink(ITickFrameSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        if (FrameSink is not null)
        {
            throw new InvalidOperationException(
                "A frame sink is already attached. The simulation publishes to exactly one sink; " +
                "fan-out is the real-time layer's job.");
        }

        FrameSink = sink;
    }
```

At the end of `Initialize()`, before `_initialized = true;`:

```csharp
        IO.Prime();
```

Replace `Tick()` and delete the two static stubs `PublishIo` and `EmitFrame`:

```csharp
    public void Tick()
    {
        Initialize();

        var context = new TickContext(
            Clock.TickCount, Clock.DeltaSeconds, Clock.Now, Events);
        int eventsBefore = Events.Records.Count;

        IO.ApplyPendingWrites(in context);                                   // phase 1: external writes (R24)
        DrainDueEvents();                                                    // phase 1: scheduled events
        EvaluateSignals(in context);                                         // phase 2
        AdvanceFlow(in context);                                             // phase 3
        (TagValue[] values, DirtyMask dirty) = IO.Publish(Clock.TickCount);  // phase 4
        EmitFrame(in context, values, dirty, eventsBefore);                  // phase 5

        Clock.Advance();
    }

    private void EmitFrame(in TickContext context, TagValue[] values, DirtyMask dirty, int eventsBefore)
    {
        if (FrameSink is null)
        {
            return;
        }

        IReadOnlyList<SimEventRecord> records = Events.Records;
        int count = records.Count - eventsBefore;
        DiscreteEvent[] events = count == 0 ? [] : new DiscreteEvent[count];
        for (int i = 0; i < count; i++)
        {
            SimEventRecord record = records[eventsBefore + i];
            events[i] = new DiscreteEvent(record.Tick, record.SimTime, record.Source, record.Code, record.Message);
        }

        FrameSink.Publish(new TickFrame(context.Tick, context.SimTime, values, dirty, events));
    }
```

- [ ] **Step 6: Run the whole Core suite**

Run: `dotnet test tests/Dse.Core.Tests`
Expected: PASS — every existing test plus 20 new in `IoIntegrationTests`.
Then `dotnet test` at the root: `Dse.Components.Tests` must still pass (no
component declares tags yet, so every directory is empty and every sim behaves
as before). `dotnet build -c Release` — 0 warnings.

- [ ] **Step 7: Commit**

```bash
git add src/Dse.Core tests/Dse.Core.Tests
git commit -m "feat(core): collect tag bindings at Build and run the write, publish and frame phases"
```

---

### Task 7: Instrument quality on the contract type, `Uncertain`, and instrument tags

**Files:**
- Delete: `src/Dse.Components/Instruments/InstrumentHealth.cs`
- Modify: `src/Dse.Components/Instruments/InstrumentBase.cs`
- Modify: `src/Dse.Components/Instruments/ZeroSpeedSwitch.cs`
- Modify: `src/Dse.Components/Instruments/PartCounter.cs`
- Modify: `tests/Dse.Components.Tests/InstrumentBaseTests.cs` (assertions only)
- Modify: `tests/Dse.Components.Tests/SignalInstrumentTests.cs` (one assertion)
- Test: `tests/Dse.Components.Tests/InstrumentQualityTests.cs`

**Interfaces:**
- Consumes: `TagQuality`, `QualityDetail` (Task 1), `TagBinding`, `ITagProvider`
  (Task 4), `Simulation.IO` (Task 6).
- Produces: `InstrumentBase.Health` is `OutputPort<TagQuality>`; `InstrumentBase`
  implements `ITagProvider` with `public virtual IEnumerable<TagBinding>
  DescribeTags()` publishing `Value` (unit and range from `Spec`, quality from
  `Health`) and `protected virtual string ValueDescription => "Measured value"`;
  `ZeroSpeedSwitch` adds `Stopped`; `PartCounter` implements `ITagProvider` with
  `Count` and `Present`.

R20 in code. The fail faults pin the range and read `Bad:SensorFailure`; a
reading outside the range with no fail fault is clamped and reads
`Uncertain:OutOfRange` — the transmitter has saturated; a frozen instrument
reads `Good`.

- [ ] **Step 1: Update the existing assertions and write the failing tests**

In `tests/Dse.Components.Tests/InstrumentBaseTests.cs` add `using Dse.Io;` and
replace every `InstrumentHealth.Good` with `TagQuality.Good` and every
`InstrumentHealth.Bad` with `TagQuality.Bad(QualityDetail.SensorFailure)` (six
occurrences at the current lines 47, 56, 117, 130, 135, 139). In
`tests/Dse.Components.Tests/SignalInstrumentTests.cs` add `using Dse.Io;` and
make the same replacement at line 43.

`tests/Dse.Components.Tests/InstrumentQualityTests.cs`:

```csharp
using Dse.Components.Instruments;
using Dse.Components.Tests.Fakes;
using Dse.Core;
using Dse.Core.Faults;
using Dse.Core.Io;
using Dse.Core.Time;
using Dse.Io;
using Xunit;

namespace Dse.Components.Tests;

public class InstrumentQualityTests
{
    private static readonly InstrumentSpec Volts = new("V", 0.0, 10.0);

    private static SimulationOptions Options => new() { Seed = 1UL };

    private static (Simulation Sim, ProbeInstrument Probe, Setpoint Truth) Plant()
    {
        var truth = new Setpoint("Truth", 5.0);
        var probe = new ProbeInstrument("PT", Volts);
        truth.Out.ConnectTo(probe.In);
        Simulation sim = new SimulationBuilder(Options).Add(truth).Add(probe).Build();
        return (sim, probe, truth);
    }

    [Fact]
    public void ReadingOutsideTheRangeIsClampedAndUncertain()
    {
        (Simulation sim, ProbeInstrument probe, Setpoint truth) = Plant();
        truth.Value = 12.0;

        sim.Tick();

        Assert.Equal(10.0, probe.Value.Value);
        Assert.Equal(TagQuality.Uncertain(QualityDetail.OutOfRange), probe.Health.Value);

        truth.Value = -1.0;
        sim.Tick();
        Assert.Equal((0.0, TagQuality.Uncertain(QualityDetail.OutOfRange)), (probe.Value.Value, probe.Health.Value));

        truth.Value = 5.0;
        sim.Tick();
        Assert.Equal((5.0, TagQuality.Good), (probe.Value.Value, probe.Health.Value));
    }

    [Fact]
    public void FailFaultsAreBadNotUncertain()
    {
        (Simulation sim, ProbeInstrument probe, Setpoint truth) = Plant();
        truth.Value = 12.0;
        sim.InjectFaultIn(TimeSpan.Zero, "PT", InstrumentFaults.FailLow);

        sim.Tick();

        Assert.Equal((0.0, TagQuality.Bad(QualityDetail.SensorFailure)), (probe.Value.Value, probe.Health.Value));
    }

    [Fact]
    public void AFrozenReadingStaysGood()
    {
        (Simulation sim, ProbeInstrument probe, Setpoint truth) = Plant();
        sim.Tick();
        sim.InjectFaultIn(TimeSpan.Zero, "PT", InstrumentFaults.Freeze);
        truth.Value = 50.0;

        sim.Tick();

        Assert.Equal((5.0, TagQuality.Good), (probe.Value.Value, probe.Health.Value));
    }

    [Fact]
    public void InstrumentsDeclareTheirValueTagWithQuality()
    {
        (Simulation sim, _, Setpoint truth) = Plant();

        TagDescriptor value = Assert.Single(sim.IO.Directory.Tags);
        Assert.Equal(("PT.Value", TagKind.Double, TagAccess.ReadOnly, "V", 0.0, 10.0, "Measured value"),
            (value.Name, value.Kind, value.Access, value.Unit, value.RangeLow, value.RangeHigh, value.Description));

        truth.Value = 12.0;
        sim.Tick();

        Assert.Equal(TagValue.Double(10.0, TagQuality.Uncertain(QualityDetail.OutOfRange)), sim.IO.Read("PT.Value"));
    }

    [Fact]
    public void TheZeroSpeedSwitchAddsStopped()
    {
        var zss = new ZeroSpeedSwitch("ZS", new InstrumentSpec("m/s", 0.0, 3.0), thresholdSpeed: 0.02, delaySeconds: 1.0);

        Assert.Equal(new[] { ("Value", TagKind.Double), ("Stopped", TagKind.Bool) },
            zss.DescribeTags().Select(t => (t.Name, t.Kind)));
    }

    [Fact]
    public void ThePartCounterDeclaresCountAndPresent()
    {
        var belt = new Dse.Core.Flow.DiscreteBelt("B", length: 2.0, maxSpeed: 1.0);
        var counter = new PartCounter("PC", belt, positionM: 1.0, windowM: 0.1);

        Assert.Equal(new[] { ("Count", TagKind.Int64, "count"), ("Present", TagKind.Bool, string.Empty) },
            counter.DescribeTags().Select(t => (t.Name, t.Kind, t.Unit)));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Dse.Components.Tests --filter "FullyQualifiedName~InstrumentQualityTests"`
Expected: build FAILS — `Health` is `OutputPort<InstrumentHealth>`, `DescribeTags` missing.

- [ ] **Step 3: Change `InstrumentBase`**

Delete `src/Dse.Components/Instruments/InstrumentHealth.cs`.

In `src/Dse.Components/Instruments/InstrumentBase.cs`:

Add `using Dse.Core.Io;` and `using Dse.Io;`. Change the class declaration to
`public abstract class InstrumentBase : ComponentBase, IFaultTarget, ITagProvider`.

Change the port: `Health = AddOutput<TagQuality>("Health");` and
`public OutputPort<TagQuality> Health { get; }` with the doc:

```csharp
    /// <summary>
    /// What the transmitter reports about its own signal, in the I/O layer's
    /// terms: Good; Uncertain:OutOfRange when the reading saturated at a range
    /// limit; Bad:SensorFailure under fail-high or fail-low.
    /// </summary>
```

Replace the health block in `Evaluate` (from `double output = ...` to
`_lastOutput = output;`) with:

```csharp
        double output = _frozen ? _lastOutput : _filtered;
        TagQuality health = TagQuality.Good;
        if (_failHigh)
        {
            output = Spec.RangeHigh;
            health = TagQuality.Bad(QualityDetail.SensorFailure);
        }
        else if (_failLow)
        {
            output = Spec.RangeLow;
            health = TagQuality.Bad(QualityDetail.SensorFailure);
        }
        else if (output < Spec.RangeLow || output > Spec.RangeHigh)
        {
            health = TagQuality.Uncertain(QualityDetail.OutOfRange);
        }

        output = Math.Clamp(output, Spec.RangeLow, Spec.RangeHigh);
        Value.Value = output;
        Health.Value = health;
        _lastOutput = output;
```

Add after `OnEvaluated`:

```csharp
    /// <summary>The description published for the <c>Value</c> tag; override to say what is measured.</summary>
    protected virtual string ValueDescription => "Measured value";

    /// <summary>
    /// Publishes <c>Value</c> with the spec's unit and range and this
    /// instrument's health as quality (R25). Override and concatenate to add tags.
    /// </summary>
    public virtual IEnumerable<TagBinding> DescribeTags() =>
    [
        TagBinding.Read("Value", Value, Spec.Unit, Spec.RangeLow, Spec.RangeHigh, ValueDescription, Health),
    ];
```

- [ ] **Step 4: Add the tags on the two instruments with extra outputs**

`src/Dse.Components/Instruments/ZeroSpeedSwitch.cs`: add `using Dse.Core.Io;`
and, after `DelaySeconds`:

```csharp
    protected override string ValueDescription => "Monitored speed";

    public override IEnumerable<TagBinding> DescribeTags() =>
        base.DescribeTags().Append(TagBinding.Read("Stopped", Stopped, "Below the threshold for the delay"));
```

`src/Dse.Components/Instruments/PartCounter.cs`: add `using Dse.Core.Io;`,
declare `public sealed class PartCounter : ComponentBase, IFaultTarget, ITagProvider`,
and add after `SupportedFaults`:

```csharp
    public IEnumerable<TagBinding> DescribeTags() =>
    [
        TagBinding.Read("Count", Count, "count", "Items counted"),
        TagBinding.Read("Present", Present, "Item in the window"),
    ];
```

- [ ] **Step 5: Run the Components suite**

Run: `dotnet test tests/Dse.Components.Tests`
Expected: PASS — the 91 existing tests (assertions updated) plus 6 new.
`dotnet build -c Release` — 0 warnings. `grep -rn InstrumentHealth src tests
docs/architecture.md` must return nothing (update `docs/architecture.md` if it
names the enum).

- [ ] **Step 6: Commit**

```bash
git add -A src/Dse.Components/Instruments tests/Dse.Components.Tests docs/architecture.md
git commit -m "feat(components): report instrument health as TagQuality with Uncertain on saturation, and declare instrument tags"
```

---

### Task 8: Tags across the library and the conveyor driven through the image

**Files:**
- Modify: `src/Dse.Components/Mechanical/MotorStarter.cs`
- Modify: `src/Dse.Components/Safety/SafetySwitch.cs`
- Modify: `src/Dse.Components/Safety/SafetyRelay.cs`
- Modify: `src/Dse.Components/Flow/BulkSource.cs`, `BulkSink.cs`, `ItemSource.cs`,
  `ItemSink.cs`, `TransferChute.cs`, `Former.cs`, `BulkProcessUnit.cs`,
  `ItemProcessUnit.cs`
- Test: `tests/Dse.Components.Tests/ComponentTagTests.cs`
- Test: `tests/Dse.Components.Tests/ConveyorIoTests.cs`

**Interfaces:**
- Consumes: Tasks 4–7.
- Produces: every component below implements `ITagProvider`. Declared tags
  (relative names; W = ReadWrite):
  - `MotorStarter`: `Command` W, `Reset` W, `Contactor`, `Tripped`
  - `SafetySwitch` (so `EStop`, `PullKey`): `Actuated` W, `Ok`
  - `SafetyRelay`: `Reset` W, `Ok`
  - `BulkSource`: `Enabled` W, `Rate` W (kg/s), `HopperMass` (kg, range
    `[0, HopperCapacityKg]` when the capacity is finite)
  - `BulkSink`: `Received` (kg), `Rate` (kg/s), `Full`
  - `ItemSource`: `Enabled` W, `Queued`
  - `ItemSink`: `Count`, `Full`
  - `TransferChute`: `Level` (fraction, `[0, 1]`), `Full`
  - `Former`: `PiecesFormed`, `HopperLevel` (fraction, `[0, 1]`), `Queued`
  - `BulkProcessUnit`: `Phase` (enum), `BatchMass` (kg), `Progress` (fraction, `[0, 1]`)
  - `ItemProcessUnit`: `Phase` (enum), `ItemCount`, `Progress` (fraction, `[0, 1]`)
  - Nothing on `Motor`, `Gearbox`, `DrivePulley`, `TailPulley`, `BeltFriction`,
    `BulkBelt`, `DiscreteBelt`, `UnitDelay` (R21).

With two pull-keys the `Conveyor` composite therefore publishes exactly these
17 tags, in directory order:

```
CV001.Contactor        Bool    ReadOnly
CV001.Current          Double  ReadOnly   A
CV001.EStop            Bool    ReadWrite
CV001.EStop.Ok         Bool    ReadOnly
CV001.PullKey1         Bool    ReadWrite
CV001.PullKey1.Ok      Bool    ReadOnly
CV001.PullKey2         Bool    ReadWrite
CV001.PullKey2.Ok      Bool    ReadOnly
CV001.Reset            Bool    ReadWrite
CV001.SafetyOk         Bool    ReadOnly
CV001.SafetyReset      Bool    ReadWrite
CV001.Speed            Double  ReadOnly   m/s
CV001.Start            Bool    ReadWrite
CV001.Stopped          Bool    ReadOnly
CV001.TonnesPerHour    Double  ReadOnly   t/h
CV001.Tripped          Bool    ReadOnly
CV001.ZeroSpeed.Value  Double  ReadOnly   m/s
```

The existing `ConveyorTests` wire `Switch` fakes into `Start`, `SafetyReset`
and `PullKey1`; under R23 those three degrade to `ReadOnly` in that plant and
the tests keep passing untouched.

- [ ] **Step 1: Write the failing tests**

`tests/Dse.Components.Tests/ComponentTagTests.cs`:

```csharp
using Dse.Components.Flow;
using Dse.Components.Mechanical;
using Dse.Components.Safety;
using Dse.Core.Flow;
using Dse.Core.Io;
using Dse.Io;
using Xunit;

namespace Dse.Components.Tests;

public class ComponentTagTests
{
    private static readonly MaterialType Ore = new("Ore", PayloadKind.Bulk);

    private static IEnumerable<(string Name, TagKind Kind, TagAccess Access)> Shape(ITagProvider provider) =>
        provider.DescribeTags().Select(t => (t.Name, t.Kind, t.Access));

    [Fact]
    public void StarterPublishesCommandsAndStates()
    {
        Assert.Equal(
            new[]
            {
                ("Command", TagKind.Bool, TagAccess.ReadWrite),
                ("Reset", TagKind.Bool, TagAccess.ReadWrite),
                ("Contactor", TagKind.Bool, TagAccess.ReadOnly),
                ("Tripped", TagKind.Bool, TagAccess.ReadOnly),
            },
            Shape(new MotorStarter("ST")));
    }

    [Fact]
    public void SafetyDevicesPublishActuationAndLoopState()
    {
        (string, TagKind, TagAccess)[] expected =
        [
            ("Actuated", TagKind.Bool, TagAccess.ReadWrite),
            ("Ok", TagKind.Bool, TagAccess.ReadOnly),
        ];

        Assert.Equal(expected, Shape(new EStop("ES")));
        Assert.Equal(expected, Shape(new PullKey("PK")));
        Assert.Equal(
            new[] { ("Reset", TagKind.Bool, TagAccess.ReadWrite), ("Ok", TagKind.Bool, TagAccess.ReadOnly) },
            Shape(new SafetyRelay("SR", 2)));
    }

    [Fact]
    public void BulkSourceAndSinkPublishFeedAndTotals()
    {
        var source = new BulkSource("Feed", Ore, 20.0, hopperCapacityKg: 500.0);
        TagBinding hopper = source.DescribeTags().Single(t => t.Name == "HopperMass");

        Assert.Equal(
            new[]
            {
                ("Enabled", TagKind.Bool, TagAccess.ReadWrite),
                ("Rate", TagKind.Double, TagAccess.ReadWrite),
                ("HopperMass", TagKind.Double, TagAccess.ReadOnly),
            },
            Shape(source));
        Assert.Equal(("kg", 0.0, 500.0), (hopper.Unit, hopper.RangeLow, hopper.RangeHigh));
        Assert.True(double.IsNaN(new BulkSource("F2", Ore, 1.0).DescribeTags().Single(t => t.Name == "HopperMass").RangeHigh));

        Assert.Equal(
            new[]
            {
                ("Received", TagKind.Double, TagAccess.ReadOnly),
                ("Rate", TagKind.Double, TagAccess.ReadOnly),
                ("Full", TagKind.Bool, TagAccess.ReadOnly),
            },
            Shape(new BulkSink("Pile")));
    }

    [Fact]
    public void ItemSourceAndSinkPublishEnableAndCounts()
    {
        var items = new MaterialType("Wheel", PayloadKind.Discrete);

        Assert.Equal(
            new[] { ("Enabled", TagKind.Bool, TagAccess.ReadWrite), ("Queued", TagKind.Int64, TagAccess.ReadOnly) },
            Shape(new ItemSource("IS", items, itemMassKg: 5.0, intervalSeconds: 1.0)));
        Assert.Equal(
            new[] { ("Count", TagKind.Int64, TagAccess.ReadOnly), ("Full", TagKind.Bool, TagAccess.ReadOnly) },
            Shape(new ItemSink("Bin")));
    }

    [Fact]
    public void ChutePublishesLevelAsAFraction()
    {
        TagBinding level = new TransferChute("Chute", 200.0).DescribeTags().Single(t => t.Name == "Level");

        Assert.Equal(("fraction", 0.0, 1.0, TagKind.Double), (level.Unit, level.RangeLow, level.RangeHigh, level.Kind));
        Assert.Contains(new TransferChute("C2", 1.0).DescribeTags(), t => t.Name == "Full" && t.Kind == TagKind.Bool);
    }
}
```

`tests/Dse.Components.Tests/ConveyorIoTests.cs`:

```csharp
using Dse.Components.Conveyors;
using Dse.Components.Flow;
using Dse.Components.Instruments;
using Dse.Components.Mechanical;
using Dse.Core;
using Dse.Core.Flow;
using Dse.Core.Time;
using Dse.Io;
using Xunit;

namespace Dse.Components.Tests;

/// <summary>The conveyor operated the way a SCADA would: through tags only.</summary>
public class ConveyorIoTests
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

    private static SimulationOptions Options => new()
    {
        Seed = 1UL,
        StartTime = new DateTimeOffset(2026, 1, 1, 6, 0, 0, TimeSpan.Zero),
        TimeStep = TimeSpan.FromMilliseconds(10),
    };

    private static Simulation Build()
    {
        var feed = new BulkSource("Feed", Ore, 20.0, new MaterialProperties(2000.0, 0.03, 15.0));
        var conveyor = new Conveyor("CV001", Cv001);
        var chute = new TransferChute("Chute", capacityKg: 200.0);
        var pile = new BulkSink("Pile");

        feed.Out.ConnectTo(conveyor.Inlet("In"));
        conveyor.Outlet("Out").ConnectTo(chute.In);
        chute.Out.ConnectTo(pile.In);

        return new SimulationBuilder(Options).Add(pile).Add(chute).Add(conveyor).Add(feed).Build();
    }

    private static void StartUp(Simulation sim)
    {
        sim.IO.WriteBool("CV001.SafetyReset", true);
        sim.RunFor(TimeSpan.FromMilliseconds(50));
        sim.IO.WriteBool("CV001.SafetyReset", false);
        sim.IO.WriteBool("CV001.Start", true);
    }

    [Fact]
    public void TheConveyorPublishesExactlyItsFace()
    {
        Simulation sim = Build();

        string[] names = sim.IO.Directory.Tags.Select(t => t.Name).Where(n => n.StartsWith("CV001.", StringComparison.Ordinal)).ToArray();

        Assert.Equal(
            new[]
            {
                "CV001.Contactor", "CV001.Current", "CV001.EStop", "CV001.EStop.Ok",
                "CV001.PullKey1", "CV001.PullKey1.Ok", "CV001.PullKey2", "CV001.PullKey2.Ok",
                "CV001.Reset", "CV001.SafetyOk", "CV001.SafetyReset", "CV001.Speed",
                "CV001.Start", "CV001.Stopped", "CV001.TonnesPerHour", "CV001.Tripped",
                "CV001.ZeroSpeed.Value",
            },
            names);
        Assert.Equal(TagAccess.ReadWrite, sim.IO.Directory.Find("CV001.Start").Access);
        Assert.Equal(("t/h", TagAccess.ReadOnly), (sim.IO.Directory.Find("CV001.TonnesPerHour").Unit, sim.IO.Directory.Find("CV001.TonnesPerHour").Access));
        Assert.DoesNotContain(sim.IO.Directory.Tags, t => t.Name.Contains(".Motor.", StringComparison.Ordinal));
    }

    [Fact]
    public void StartsThroughTagsAndReportsThroughTags()
    {
        Simulation sim = Build();
        StartUp(sim);

        sim.RunFor(TimeSpan.FromSeconds(40));

        Assert.True(sim.IO.ReadBool("CV001.Contactor"));
        Assert.False(sim.IO.ReadBool("CV001.Tripped"));
        Assert.True(sim.IO.ReadBool("CV001.SafetyOk"));
        Assert.InRange(sim.IO.ReadDouble("CV001.Speed"), 1.75, 1.95);
        Assert.InRange(sim.IO.ReadDouble("CV001.TonnesPerHour"), 68.0, 76.0);
        Assert.InRange(sim.IO.ReadDouble("CV001.Current"), 1.2, 1.6);
        Assert.True(sim.IO.Read("CV001.Speed").Quality.IsGood);
        Assert.Contains(sim.Events.Records, r => r.Source == "CV001.Start" && r.Code == "WRITE");
    }

    [Fact]
    public void APullKeyWrittenAsATagStopsTheBelt()
    {
        Simulation sim = Build();
        StartUp(sim);
        sim.RunFor(TimeSpan.FromSeconds(30));

        sim.IO.WriteBool("CV001.PullKey1", true);
        sim.RunFor(TimeSpan.FromSeconds(20));

        Assert.False(sim.IO.ReadBool("CV001.PullKey1.Ok"));
        Assert.False(sim.IO.ReadBool("CV001.SafetyOk"));
        Assert.False(sim.IO.ReadBool("CV001.Contactor"));
        Assert.True(sim.IO.ReadBool("CV001.Stopped"));
        Assert.True(sim.IO.ReadDouble("CV001.Speed") < 0.05);
    }

    [Fact]
    public void AFailedSpeedSensorReadsBadOnTheWire()
    {
        Simulation sim = Build();
        StartUp(sim);
        sim.RunFor(TimeSpan.FromSeconds(30));

        sim.InjectFaultIn(TimeSpan.Zero, "CV001.SpeedSensor", InstrumentFaults.FailHigh);
        sim.Tick();

        TagValue speed = sim.IO.Read("CV001.Speed");
        Assert.Equal(TagQuality.Bad(QualityDetail.SensorFailure), speed.Quality);
        Assert.Equal(sim.IO.Directory.Find("CV001.Speed").RangeHigh, speed.AsDouble);
        Assert.True(sim.IO.Read("CV001.Current").Quality.IsGood);
    }

    [Fact]
    public void WritingAReadOnlyTagIsRejectedAtTheCallSite()
    {
        Simulation sim = Build();

        Assert.Throws<InvalidOperationException>(() => sim.IO.WriteBool("CV001.Contactor", true));
        Assert.Throws<InvalidOperationException>(() => sim.IO.WriteDouble("CV001.Start", 1.0));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Dse.Components.Tests --filter "FullyQualifiedName~ComponentTagTests|FullyQualifiedName~ConveyorIoTests"`
Expected: build FAILS — `MotorStarter` is not an `ITagProvider`.

- [ ] **Step 3: Declare the tags**

Each file gains `using Dse.Core.Io;`, adds `ITagProvider` to its base list, and
a `DescribeTags` method placed after `SupportedFaults` (or after the last port
property where there is no fault list).

`src/Dse.Components/Mechanical/MotorStarter.cs`:

```csharp
    public IEnumerable<TagBinding> DescribeTags() =>
    [
        TagBinding.Write("Command", Command, "Run command"),
        TagBinding.Write("Reset", Reset, "Overload reset, rising edge"),
        TagBinding.Read("Contactor", Contactor, "Contactor closed"),
        TagBinding.Read("Tripped", Tripped, "Overload relay tripped"),
    ];
```

`src/Dse.Components/Safety/SafetySwitch.cs` (the abstract base, so both
`EStop` and `PullKey` inherit it):

```csharp
    public IEnumerable<TagBinding> DescribeTags() =>
    [
        TagBinding.Write("Actuated", Actuated, "Actuated by the operator"),
        TagBinding.Read("Ok", Ok, "Safety loop healthy"),
    ];
```

`src/Dse.Components/Safety/SafetyRelay.cs`:

```csharp
    public IEnumerable<TagBinding> DescribeTags() =>
    [
        TagBinding.Write("Reset", Reset, "Safety reset, rising edge"),
        TagBinding.Read("Ok", Ok, "Relay energised"),
    ];
```

`src/Dse.Components/Flow/BulkSource.cs` (the constructor already stores the
capacity in `HopperCapacityKg`):

```csharp
    public IEnumerable<TagBinding> DescribeTags() =>
    [
        TagBinding.Write("Enabled", Enabled, "Feeder enabled"),
        TagBinding.Write("Rate", Rate, "kg/s", description: "Feed rate"),
        double.IsFinite(HopperCapacityKg)
            ? TagBinding.Read("HopperMass", HopperMass, "kg", 0.0, HopperCapacityKg, "Mass in the hopper")
            : TagBinding.Read("HopperMass", HopperMass, "kg", description: "Mass in the hopper"),
    ];
```

`src/Dse.Components/Flow/BulkSink.cs`:

```csharp
    public IEnumerable<TagBinding> DescribeTags() =>
    [
        TagBinding.Read("Received", Received, "kg", description: "Cumulative mass received"),
        TagBinding.Read("Rate", Rate, "kg/s", description: "Receiving rate"),
        TagBinding.Read("Full", Full, "At capacity"),
    ];
```

`src/Dse.Components/Flow/ItemSource.cs`:

```csharp
    public IEnumerable<TagBinding> DescribeTags() =>
    [
        TagBinding.Write("Enabled", Enabled, "Minting enabled"),
        TagBinding.Read("Queued", Queued, "count", "Items waiting at the outlet"),
    ];
```

`src/Dse.Components/Flow/ItemSink.cs`:

```csharp
    public IEnumerable<TagBinding> DescribeTags() =>
    [
        TagBinding.Read("Count", Count, "count", "Items received"),
        TagBinding.Read("Full", Full, "At capacity"),
    ];
```

`src/Dse.Components/Flow/TransferChute.cs`:

```csharp
    public IEnumerable<TagBinding> DescribeTags() =>
    [
        TagBinding.Read("Level", Level, "fraction", 0.0, 1.0, "Held mass over capacity"),
        TagBinding.Read("Full", Full, "At capacity"),
    ];
```

`src/Dse.Components/Flow/Former.cs`:

```csharp
    public IEnumerable<TagBinding> DescribeTags() =>
    [
        TagBinding.Read("PiecesFormed", PiecesFormed, "count", "Pieces formed"),
        TagBinding.Read("HopperLevel", HopperLevel, "fraction", 0.0, 1.0, "Hopper mass over capacity"),
        TagBinding.Read("Queued", Queued, "count", "Pieces waiting at the outlet"),
    ];
```

`src/Dse.Components/Flow/BulkProcessUnit.cs`:

```csharp
    public IEnumerable<TagBinding> DescribeTags() =>
    [
        TagBinding.ReadEnum("Phase", Phase, "Unit phase"),
        TagBinding.Read("BatchMass", BatchMass, "kg", description: "Mass in the unit"),
        TagBinding.Read("Progress", Progress, "fraction", 0.0, 1.0, "Progress through the current phase"),
    ];
```

`src/Dse.Components/Flow/ItemProcessUnit.cs`:

```csharp
    public IEnumerable<TagBinding> DescribeTags() =>
    [
        TagBinding.ReadEnum("Phase", Phase, "Unit phase"),
        TagBinding.Read("ItemCount", ItemCount, "count", "Items in the unit"),
        TagBinding.Read("Progress", Progress, "fraction", 0.0, 1.0, "Progress through the current phase"),
    ];
```

- [ ] **Step 4: Run everything**

Run: `dotnet test`
Expected: PASS across all four test projects; `ConveyorTests` unchanged and
green (its `Switch`-driven inputs degrade to read-only). Report the measured
`Speed`, `TonnesPerHour` and `Current` from `StartsThroughTagsAndReportsThroughTags`;
they must match the windows `ConveyorTests` already uses, because the plant is
the same — if they do not, the write path is wrong, not the window.
`dotnet build -c Release` — 0 warnings.

- [ ] **Step 5: Commit**

```bash
git add src/Dse.Components tests/Dse.Components.Tests
git commit -m "feat(components): declare tags on the starter, safety circuit, sources, sinks and process units"
```

---

### Task 9: The `Dse.Realtime` project and the live state engine

**Files:**
- Create: `src/Dse.Realtime/Dse.Realtime.csproj`
- Create: `src/Dse.Realtime/TagState.cs`
- Create: `src/Dse.Realtime/StateSnapshot.cs`
- Create: `src/Dse.Realtime/LiveState.cs`
- Create: `tests/Dse.Realtime.Tests/Dse.Realtime.Tests.csproj`
- Test: `tests/Dse.Realtime.Tests/Fakes/Frames.cs`
- Test: `tests/Dse.Realtime.Tests/LiveStateTests.cs`
- Modify: `Dse.sln` (add both projects)

**Interfaces:**
- Consumes: `ITagDirectory`, `TagValue`, `TickFrame`, `DiscreteEvent` (Tasks 1–2).
- Produces: `readonly record struct TagState(TagValue Value, long LastChangeTick,
  DateTimeOffset LastChangeTime)`; `sealed class StateSnapshot(long Tick,
  DateTimeOffset SimTime, ReadOnlyMemory<TagState> Tags,
  IReadOnlyList<DiscreteEvent> RecentEvents)`; `sealed class LiveState` with
  `LiveState(ITagDirectory directory, int recentEventCapacity = 256)`,
  `ITagDirectory Directory`, `long Tick` (−1 before the first frame),
  `DateTimeOffset SimTime`, `long FramesApplied`, `TagState this[int]`,
  `TagState Get(string name)`, `StateSnapshot Snapshot()`, `internal void
  Apply(TickFrame frame)`.

The state engine is current truth (spec 10.3). It is written by the hub's pump
and read by anyone, so it takes a private lock on every member: `TagState` is
32 bytes and would tear without one. The first frame it sees is applied in
full regardless of its dirty mask, because a hub may be attached to a
simulation that has been running for an hour. Alarm state waits for plan 5
(brainstorm decision); the engine keeps the newest `recentEventCapacity`
events so a late joiner sees what just happened.

- [ ] **Step 1: Create the projects and add them to the solution**

`src/Dse.Realtime/Dse.Realtime.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\Dse.Io.Abstractions\Dse.Io.Abstractions.csproj" />
  </ItemGroup>

  <ItemGroup>
    <InternalsVisibleTo Include="Dse.Realtime.Tests" />
  </ItemGroup>

</Project>
```

`tests/Dse.Realtime.Tests/Dse.Realtime.Tests.csproj` (the `Dse.Components`
reference is test-only, for Task 13's end-to-end test; the shipping project
never references `Dse.Core`):

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
    <ProjectReference Include="..\..\src\Dse.Realtime\Dse.Realtime.csproj" />
    <!-- Test-only: the end-to-end test drives a real conveyor through the hub. -->
    <ProjectReference Include="..\..\src\Dse.Components\Dse.Components.csproj" />
  </ItemGroup>

</Project>
```

Run:

```bash
dotnet sln Dse.sln add src/Dse.Realtime/Dse.Realtime.csproj --solution-folder src
dotnet sln Dse.sln add tests/Dse.Realtime.Tests/Dse.Realtime.Tests.csproj --solution-folder tests
```

- [ ] **Step 2: Write the frame fakes and the failing tests**

`tests/Dse.Realtime.Tests/Fakes/Frames.cs`:

```csharp
using Dse.Io;

namespace Dse.Realtime.Tests.Fakes;

/// <summary>A three-tag directory and hand-built frames over it: A.Speed (double), A.Run (bool), B.Count (long).</summary>
public static class Frames
{
    public static readonly DateTimeOffset Start = new(2026, 1, 1, 6, 0, 0, TimeSpan.Zero);

    public static readonly TimeSpan Step = TimeSpan.FromMilliseconds(10);

    public const int Speed = 0;

    public const int Run = 1;

    public const int Count = 2;

    public static ArrayDirectory Directory() => new(
        new TagDescriptor(0, "A.Speed", TagKind.Double, TagAccess.ReadOnly, "m/s", 0.0, 2.0, "Belt speed"),
        new TagDescriptor(1, "A.Run", TagKind.Bool, TagAccess.ReadWrite, string.Empty, double.NaN, double.NaN, "Run command"),
        new TagDescriptor(2, "B.Count", TagKind.Int64, TagAccess.ReadOnly, "count", double.NaN, double.NaN, "Items"));

    public static DateTimeOffset TimeOf(long tick) => Start + (Step * tick);

    /// <summary>A frame with the given values; <paramref name="dirty"/> lists the changed indices.</summary>
    public static TickFrame Frame(long tick, double speed, bool run, long count, int[] dirty, params DiscreteEvent[] events)
    {
        TagValue[] values = [TagValue.Double(speed), TagValue.Bool(run), TagValue.Int64(count)];
        var words = new ulong[1];
        foreach (int i in dirty)
        {
            words[0] |= 1UL << i;
        }

        return new TickFrame(tick, TimeOf(tick), values, DirtyMask.FromBits(words, 3, dirty.Length), events);
    }

    /// <summary>A frame with every tag dirty.</summary>
    public static TickFrame Full(long tick, double speed, bool run, long count, params DiscreteEvent[] events) =>
        Frame(tick, speed, run, count, [0, 1, 2], events);

    /// <summary>A frame with nothing dirty and no events.</summary>
    public static TickFrame Quiet(long tick, double speed, bool run, long count) =>
        Frame(tick, speed, run, count, []);

    public static DiscreteEvent Event(long tick, string source, string code) =>
        new(tick, TimeOf(tick), source, code, $"{code} at tick {tick}.");
}

/// <summary>An <see cref="ITagDirectory"/> over an array.</summary>
public sealed class ArrayDirectory : ITagDirectory
{
    private readonly TagDescriptor[] _tags;
    private readonly Dictionary<string, int> _byName = new(StringComparer.Ordinal);

    public ArrayDirectory(params TagDescriptor[] tags)
    {
        _tags = tags;
        for (int i = 0; i < tags.Length; i++)
        {
            _byName[tags[i].Name] = i;
        }
    }

    public int Count => _tags.Length;

    public IReadOnlyList<TagDescriptor> Tags => _tags;

    public TagDescriptor this[int index] => _tags[index];

    public bool TryFind(string name, out TagDescriptor descriptor)
    {
        if (_byName.TryGetValue(name, out int index))
        {
            descriptor = _tags[index];
            return true;
        }

        descriptor = null!;
        return false;
    }

    public TagDescriptor Find(string name) =>
        TryFind(name, out TagDescriptor d) ? d : throw new KeyNotFoundException($"No tag '{name}'.");
}
```

`tests/Dse.Realtime.Tests/LiveStateTests.cs`:

```csharp
using Dse.Io;
using Dse.Realtime.Tests.Fakes;

namespace Dse.Realtime.Tests;

public class LiveStateTests
{
    [Fact]
    public void StartsEmptyAtTickMinusOne()
    {
        var state = new LiveState(Frames.Directory());

        Assert.Equal(-1L, state.Tick);
        Assert.Equal(0L, state.FramesApplied);
        Assert.Equal(TagValue.Double(0.0), state[Frames.Speed].Value);
        Assert.Equal(TagValue.Bool(false), state[Frames.Run].Value);
        Assert.Equal(TagValue.Int64(0L), state[Frames.Count].Value);
        Assert.Equal(-1L, state[Frames.Speed].LastChangeTick);
    }

    [Fact]
    public void TheFirstFrameIsAppliedInFullWhateverItsMask()
    {
        var state = new LiveState(Frames.Directory());

        state.Apply(Frames.Frame(7, 1.5, true, 3, dirty: []));

        Assert.Equal(7L, state.Tick);
        Assert.Equal(Frames.TimeOf(7), state.SimTime);
        Assert.Equal((1.5, true, 3L), (state[0].Value.AsDouble, state[1].Value.AsBool, state[2].Value.AsInt64));
        Assert.All(new[] { state[0], state[1], state[2] }, t => Assert.Equal(7L, t.LastChangeTick));
    }

    [Fact]
    public void LaterFramesUpdateOnlyDirtyTagsAndTheirChangeTick()
    {
        var state = new LiveState(Frames.Directory());
        state.Apply(Frames.Full(0, 1.0, false, 0));

        state.Apply(Frames.Frame(1, 1.2, false, 0, dirty: [Frames.Speed]));
        state.Apply(Frames.Frame(2, 1.2, true, 0, dirty: [Frames.Run]));

        Assert.Equal(2L, state.Tick);
        Assert.Equal((1.2, 1L), (state[Frames.Speed].Value.AsDouble, state[Frames.Speed].LastChangeTick));
        Assert.Equal((true, 2L), (state[Frames.Run].Value.AsBool, state[Frames.Run].LastChangeTick));
        Assert.Equal((0L, 0L), (state[Frames.Count].Value.AsInt64, state[Frames.Count].LastChangeTick));
        Assert.Equal(Frames.TimeOf(2), state[Frames.Run].LastChangeTime);
        Assert.Equal(3L, state.FramesApplied);
    }

    [Fact]
    public void GetByNameResolvesThroughTheDirectory()
    {
        var state = new LiveState(Frames.Directory());
        state.Apply(Frames.Full(0, 0.5, true, 9));

        Assert.Equal(9L, state.Get("B.Count").Value.AsInt64);
        Assert.Throws<KeyNotFoundException>(() => state.Get("Nope"));
    }

    [Fact]
    public void RecentEventsKeepTheNewestN()
    {
        var state = new LiveState(Frames.Directory(), recentEventCapacity: 3);
        state.Apply(Frames.Full(0, 0, false, 0, Frames.Event(0, "A", "E0"), Frames.Event(0, "A", "E1")));
        state.Apply(Frames.Quiet(1, 0, false, 0));
        state.Apply(Frames.Frame(2, 0, false, 0, [], Frames.Event(2, "A", "E2"), Frames.Event(2, "A", "E3")));

        StateSnapshot snapshot = state.Snapshot();

        Assert.Equal(new[] { "E1", "E2", "E3" }, snapshot.RecentEvents.Select(e => e.Code));
    }

    [Fact]
    public void SnapshotIsAnIndependentCopy()
    {
        var state = new LiveState(Frames.Directory());
        state.Apply(Frames.Full(0, 1.0, false, 0));

        StateSnapshot snapshot = state.Snapshot();
        state.Apply(Frames.Frame(1, 2.0, false, 0, dirty: [Frames.Speed]));

        Assert.Equal(0L, snapshot.Tick);
        Assert.Equal(1.0, snapshot.Tags.Span[Frames.Speed].Value.AsDouble);
        Assert.Equal(2.0, state[Frames.Speed].Value.AsDouble);
        Assert.Equal(3, snapshot.Tags.Length);
    }

    [Fact]
    public void RejectsAFrameOfTheWrongWidth()
    {
        var state = new LiveState(Frames.Directory());
        var narrow = new TickFrame(0, Frames.Start, new[] { TagValue.Bool(true) }, DirtyMask.All(1), []);

        Assert.Throws<ArgumentException>(() => state.Apply(narrow));
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/Dse.Realtime.Tests`
Expected: build FAILS — `LiveState` does not exist.

- [ ] **Step 4: Write the state engine**

`src/Dse.Realtime/TagState.cs`:

```csharp
using Dse.Io;

namespace Dse.Realtime;

/// <summary>One tag's current truth (spec 10.3): value with quality, and when it last changed.</summary>
public readonly record struct TagState(TagValue Value, long LastChangeTick, DateTimeOffset LastChangeTime);
```

`src/Dse.Realtime/StateSnapshot.cs`:

```csharp
using Dse.Io;

namespace Dse.Realtime;

/// <summary>An immutable copy of the live state at one tick: what a late joiner receives first.</summary>
public sealed class StateSnapshot
{
    /// <summary>Creates a snapshot; the caller promises never to write the arrays again.</summary>
    public StateSnapshot(long tick, DateTimeOffset simTime, ReadOnlyMemory<TagState> tags, IReadOnlyList<DiscreteEvent> recentEvents)
    {
        ArgumentNullException.ThrowIfNull(recentEvents);
        Tick = tick;
        SimTime = simTime;
        Tags = tags;
        RecentEvents = recentEvents;
    }

    /// <summary>The last tick applied, or -1 if none.</summary>
    public long Tick { get; }

    /// <summary>Simulation time of that tick.</summary>
    public DateTimeOffset SimTime { get; }

    /// <summary>Every tag, by directory index.</summary>
    public ReadOnlyMemory<TagState> Tags { get; }

    /// <summary>The newest events, oldest first.</summary>
    public IReadOnlyList<DiscreteEvent> RecentEvents { get; }
}
```

`src/Dse.Realtime/LiveState.cs`:

```csharp
using Dse.Io;

namespace Dse.Realtime;

/// <summary>
/// The live state engine (spec 10.3): current value, quality and last-change
/// tick per tag, plus the most recent events. Written by the hub's pump, read
/// by anyone; every member takes the private lock because <see cref="TagState"/>
/// is wider than a machine word.
/// </summary>
public sealed class LiveState
{
    private readonly object _sync = new();
    private readonly TagState[] _tags;
    private readonly DiscreteEvent[] _recent;
    private int _recentHead;
    private int _recentCount;
    private long _tick = -1;
    private DateTimeOffset _simTime;
    private long _framesApplied;

    /// <summary>Creates an empty state for <paramref name="directory"/>.</summary>
    public LiveState(ITagDirectory directory, int recentEventCapacity = 256)
    {
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(recentEventCapacity);

        Directory = directory;
        _tags = new TagState[directory.Count];
        for (int i = 0; i < _tags.Length; i++)
        {
            TagValue initial = directory[i].Kind switch
            {
                TagKind.Bool => TagValue.Bool(false),
                TagKind.Double => TagValue.Double(0.0),
                _ => TagValue.Int64(0L),
            };
            _tags[i] = new TagState(initial, -1, default);
        }

        _recent = new DiscreteEvent[recentEventCapacity];
    }

    /// <summary>The directory this state indexes.</summary>
    public ITagDirectory Directory { get; }

    /// <summary>The last tick applied, or -1 before the first frame.</summary>
    public long Tick
    {
        get
        {
            lock (_sync)
            {
                return _tick;
            }
        }
    }

    /// <summary>Simulation time of the last applied frame.</summary>
    public DateTimeOffset SimTime
    {
        get
        {
            lock (_sync)
            {
                return _simTime;
            }
        }
    }

    /// <summary>Frames applied so far.</summary>
    public long FramesApplied
    {
        get
        {
            lock (_sync)
            {
                return _framesApplied;
            }
        }
    }

    /// <summary>The current state of the tag at <paramref name="index"/>.</summary>
    public TagState this[int index]
    {
        get
        {
            lock (_sync)
            {
                return _tags[index];
            }
        }
    }

    /// <summary>The current state of a named tag.</summary>
    public TagState Get(string name) => this[Directory.Find(name).Index];

    /// <summary>An immutable copy of everything, taken atomically.</summary>
    public StateSnapshot Snapshot()
    {
        lock (_sync)
        {
            var tags = new TagState[_tags.Length];
            Array.Copy(_tags, tags, tags.Length);

            var recent = new DiscreteEvent[_recentCount];
            int start = (_recentHead - _recentCount + _recent.Length) % _recent.Length;
            for (int i = 0; i < _recentCount; i++)
            {
                recent[i] = _recent[(start + i) % _recent.Length];
            }

            return new StateSnapshot(_tick, _simTime, tags, recent);
        }
    }

    /// <summary>
    /// Applies a frame. The first frame is applied in full; later frames apply
    /// their dirty tags only. Called by the hub's pump, under the hub's lock.
    /// </summary>
    internal void Apply(TickFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (frame.Values.Length != _tags.Length)
        {
            throw new ArgumentException(
                $"The frame carries {frame.Values.Length} tags; this state has {_tags.Length}.", nameof(frame));
        }

        lock (_sync)
        {
            bool first = _framesApplied == 0;
            ReadOnlySpan<TagValue> values = frame.Values.Span;
            for (int i = 0; i < _tags.Length; i++)
            {
                if (first || frame.Dirty[i])
                {
                    _tags[i] = new TagState(values[i], frame.Tick, frame.SimTime);
                }
            }

            for (int i = 0; i < frame.Events.Count; i++)
            {
                _recent[_recentHead] = frame.Events[i];
                _recentHead = (_recentHead + 1) % _recent.Length;
                if (_recentCount < _recent.Length)
                {
                    _recentCount++;
                }
            }

            _tick = frame.Tick;
            _simTime = frame.SimTime;
            _framesApplied++;
        }
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/Dse.Realtime.Tests`
Expected: PASS, 7 tests. `dotnet build -c Release` — 0 warnings. Confirm the
dependency rule: `grep -n ProjectReference src/Dse.Realtime/Dse.Realtime.csproj`
shows `Dse.Io.Abstractions` only.

- [ ] **Step 6: Commit**

```bash
git add Dse.sln src/Dse.Realtime tests/Dse.Realtime.Tests
git commit -m "feat(realtime): add the Dse.Realtime project with the live state engine"
```

---

### Task 10: The hub — ring, pump, no-gap subscribe, and the two backpressure policies

**Files:**
- Create: `src/Dse.Realtime/FrameRing.cs`
- Create: `src/Dse.Realtime/BackpressurePolicy.cs`
- Create: `src/Dse.Realtime/SubscriptionOptions.cs`
- Create: `src/Dse.Realtime/TagChange.cs`
- Create: `src/Dse.Realtime/FrameDelta.cs`
- Create: `src/Dse.Realtime/DeltaAccumulator.cs`
- Create: `src/Dse.Realtime/Subscription.cs`
- Create: `src/Dse.Realtime/RealtimeHub.cs`
- Test: `tests/Dse.Realtime.Tests/RealtimeHubTests.cs`
- Test: `tests/Dse.Realtime.Tests/SubscriptionPolicyTests.cs`

**Interfaces:**
- Consumes: Task 9.
- Produces: `internal sealed class FrameRing(int capacity)` with `Capacity`,
  `Count`, `bool TryEnqueue(TickFrame)`, `bool TryDequeue(out TickFrame)`;
  `enum BackpressurePolicy { Conflate, Lossless }`; `sealed record
  SubscriptionOptions { BackpressurePolicy Policy = Conflate; int Capacity =
  1024; }`; `readonly record struct TagChange(int Index, TagValue Value)`;
  `sealed record FrameDelta(long Tick, DateTimeOffset SimTime,
  IReadOnlyList<TagChange> Changes, IReadOnlyList<DiscreteEvent> Events)`;
  `internal sealed class DeltaAccumulator(int tagCount, int eventCapacity)`
  with `bool HasContent`, `long DroppedEvents`, `void Add(long tick,
  DateTimeOffset simTime, IReadOnlyList<TagChange> changes,
  IReadOnlyList<DiscreteEvent> events)`, `FrameDelta Flush()`;
  `sealed class Subscription : IDisposable` with `SubscriptionOptions Options`,
  `StateSnapshot Initial`, `WaitHandle Available`, `bool IsFaulted`,
  `string? FaultReason`, `long Delivered`, `long DroppedEvents`, `int Queued`,
  `bool TryRead(out FrameDelta delta)`, `internal void Offer(TickFrame)`,
  `internal void NotifyGap(long dropped)`, `internal void Fault(string)`;
  `sealed class RealtimeHub : ITickFrameSink, IDisposable` with
  `RealtimeHub(ITagDirectory directory, int ringCapacity = 4096, int
  recentEventCapacity = 256)`, `ITagDirectory Directory`, `LiveState State`,
  `long PublishedFrames`, `long DroppedFrames`, `int Pending`,
  `WaitHandle FramesAvailable`, `int SubscriberCount`, `void Publish(TickFrame)`,
  `int Pump()`, `Subscription Subscribe(SubscriptionOptions? options = null)`,
  `internal void Unsubscribe(Subscription)`.

Threading model, stated once: `Publish` runs on the simulation thread and is a
single-producer enqueue into `FrameRing` (R26: full means the incoming frame
is dropped and counted). `Pump` runs on whichever thread calls it — the
`DispatcherThread` of Task 12 or a test — and is the single consumer; it
holds `_pumpLock` while it applies frames to `State` and offers them to each
subscription. `Subscribe` takes the same lock, so the snapshot it captures and
the registration it performs are one atomic step: every frame not yet pumped
at that instant is offered to the new subscription, which is the no-gap
guarantee of spec 10.7. A subscription diffs each frame against the values it
last delivered, not against the frame's mask, so a `Conflate` subscriber
survives a ring gap. Consumers pull with `TryRead` (R27).

- [ ] **Step 1: Write the failing tests**

`tests/Dse.Realtime.Tests/RealtimeHubTests.cs`:

```csharp
using Dse.Io;
using Dse.Realtime.Tests.Fakes;

namespace Dse.Realtime.Tests;

public class RealtimeHubTests
{
    private static SubscriptionOptions Lossless => new() { Policy = BackpressurePolicy.Lossless };

    [Fact]
    public void PublishQueuesAndPumpApplies()
    {
        using var hub = new RealtimeHub(Frames.Directory());

        hub.Publish(Frames.Full(0, 1.0, true, 1));
        hub.Publish(Frames.Frame(1, 1.5, true, 1, dirty: [Frames.Speed]));

        Assert.Equal(2, hub.Pending);
        Assert.Equal(-1L, hub.State.Tick);
        Assert.Equal(2, hub.Pump());
        Assert.Equal(0, hub.Pending);
        Assert.Equal(1L, hub.State.Tick);
        Assert.Equal(1.5, hub.State[Frames.Speed].Value.AsDouble);
        Assert.Equal(2L, hub.PublishedFrames);
        Assert.Equal(0, hub.Pump());
    }

    [Fact]
    public void RingOverflowDropsTheNewestFrameAndCounts()
    {
        using var hub = new RealtimeHub(Frames.Directory(), ringCapacity: 2);

        hub.Publish(Frames.Full(0, 1.0, false, 0));
        hub.Publish(Frames.Full(1, 2.0, false, 0));
        hub.Publish(Frames.Full(2, 3.0, false, 0));

        Assert.Equal(2L, hub.PublishedFrames);
        Assert.Equal(1L, hub.DroppedFrames);
        hub.Pump();
        Assert.Equal(1L, hub.State.Tick);
        Assert.Equal(2.0, hub.State[Frames.Speed].Value.AsDouble);
    }

    [Fact]
    public void PublishNeverBlocksWhenTheRingIsFull()
    {
        using var hub = new RealtimeHub(Frames.Directory(), ringCapacity: 4);

        for (int i = 0; i < 10_000; i++)
        {
            hub.Publish(Frames.Full(i, i, false, 0));
        }

        Assert.Equal(4L, hub.PublishedFrames);
        Assert.Equal(9_996L, hub.DroppedFrames);
    }

    [Fact]
    public void SubscribeCapturesTheSnapshotAndDeliversOnlyLaterFrames()
    {
        using var hub = new RealtimeHub(Frames.Directory());
        hub.Publish(Frames.Full(0, 1.0, false, 0));
        hub.Publish(Frames.Frame(1, 1.1, false, 0, dirty: [Frames.Speed]));
        hub.Pump();

        using Subscription sub = hub.Subscribe(Lossless);

        Assert.Equal(1L, sub.Initial.Tick);
        Assert.Equal(1.1, sub.Initial.Tags.Span[Frames.Speed].Value.AsDouble);
        Assert.False(sub.TryRead(out _));

        hub.Publish(Frames.Frame(2, 1.2, false, 0, dirty: [Frames.Speed]));
        hub.Publish(Frames.Frame(3, 1.2, true, 0, dirty: [Frames.Run]));
        hub.Pump();

        Assert.True(sub.TryRead(out FrameDelta first));
        Assert.True(sub.TryRead(out FrameDelta second));
        Assert.False(sub.TryRead(out _));
        Assert.Equal((2L, Frames.Speed, 1.2), (first.Tick, first.Changes[0].Index, first.Changes[0].Value.AsDouble));
        Assert.Equal((3L, Frames.Run, true), (second.Tick, second.Changes[0].Index, second.Changes[0].Value.AsBool));
    }

    [Fact]
    public void FramesAlreadyInTheRingAtSubscribeTimeAreDelivered()
    {
        using var hub = new RealtimeHub(Frames.Directory());
        hub.Publish(Frames.Full(0, 1.0, false, 0));
        hub.Publish(Frames.Frame(1, 1.1, false, 0, dirty: [Frames.Speed]));

        using Subscription sub = hub.Subscribe(Lossless);
        Assert.Equal(-1L, sub.Initial.Tick);

        hub.Pump();

        Assert.True(sub.TryRead(out FrameDelta first));
        Assert.Equal(0L, first.Tick);
        Assert.Equal(1, first.Changes.Count);
        Assert.True(sub.TryRead(out FrameDelta second));
        Assert.Equal(1L, second.Tick);
    }

    [Fact]
    public void AFrameWithNoChangesAndNoEventsProducesNoDelta()
    {
        using var hub = new RealtimeHub(Frames.Directory());
        hub.Publish(Frames.Full(0, 1.0, false, 0));
        hub.Pump();
        using Subscription sub = hub.Subscribe(Lossless);

        hub.Publish(Frames.Quiet(1, 1.0, false, 0));
        hub.Publish(Frames.Frame(2, 1.0, false, 0, [], Frames.Event(2, "A", "PING")));
        hub.Pump();

        Assert.True(sub.TryRead(out FrameDelta delta));
        Assert.Equal(2L, delta.Tick);
        Assert.Empty(delta.Changes);
        Assert.Equal("PING", Assert.Single(delta.Events).Code);
        Assert.False(sub.TryRead(out _));
    }

    [Fact]
    public void ADirtyBitWithAnUnchangedValueIsNotAChangeForASubscriber()
    {
        using var hub = new RealtimeHub(Frames.Directory());
        hub.Publish(Frames.Full(0, 1.0, false, 0));
        hub.Pump();
        using Subscription sub = hub.Subscribe(Lossless);

        hub.Publish(Frames.Frame(1, 1.0, false, 0, dirty: [Frames.Speed]));
        hub.Pump();

        Assert.False(sub.TryRead(out _));
    }

    [Fact]
    public void LosslessSubscriptionFaultsOnARingGap()
    {
        using var hub = new RealtimeHub(Frames.Directory(), ringCapacity: 1);
        using Subscription lossless = hub.Subscribe(Lossless);
        using Subscription conflate = hub.Subscribe(new SubscriptionOptions { Policy = BackpressurePolicy.Conflate });

        hub.Publish(Frames.Full(0, 1.0, false, 0));
        hub.Publish(Frames.Full(1, 2.0, false, 0));
        hub.Pump();

        Assert.True(lossless.IsFaulted);
        Assert.Contains("ring overflow", lossless.FaultReason, StringComparison.Ordinal);
        Assert.False(lossless.TryRead(out _));
        Assert.True(lossless.Available.WaitOne(0));

        Assert.False(conflate.IsFaulted);
        Assert.True(conflate.TryRead(out FrameDelta delta));
        Assert.Equal(1.0, delta.Changes.Single(c => c.Index == Frames.Speed).Value.AsDouble);
    }

    [Fact]
    public void DisposeUnsubscribes()
    {
        using var hub = new RealtimeHub(Frames.Directory());
        Subscription sub = hub.Subscribe(Lossless);
        Assert.Equal(1, hub.SubscriberCount);

        sub.Dispose();

        Assert.Equal(0, hub.SubscriberCount);
        hub.Publish(Frames.Full(0, 1.0, false, 0));
        Assert.Equal(1, hub.Pump());
    }

    [Fact]
    public void FramesAvailableIsSignalledByPublish()
    {
        using var hub = new RealtimeHub(Frames.Directory());

        Assert.False(hub.FramesAvailable.WaitOne(0));
        hub.Publish(Frames.Full(0, 1.0, false, 0));
        Assert.True(hub.FramesAvailable.WaitOne(0));
    }

    [Fact]
    public void ProducerAndPumpOnDifferentThreadsLoseNothingWhenTheRingIsLargeEnough()
    {
        using var hub = new RealtimeHub(Frames.Directory(), ringCapacity: 1 << 16);
        using Subscription sub = hub.Subscribe(Lossless with { Capacity = 100_000 });
        const int frames = 50_000;
        int pumped = 0;

        var producer = new Thread(() =>
        {
            for (int i = 0; i < frames; i++)
            {
                hub.Publish(Frames.Frame(i, i + 1.0, false, 0, dirty: [Frames.Speed]));
            }
        });
        producer.Start();
        while (producer.IsAlive || hub.Pending > 0)
        {
            pumped += hub.Pump();
        }

        Assert.Equal(frames, pumped);
        Assert.Equal(0L, hub.DroppedFrames);
        long expected = 0;
        while (sub.TryRead(out FrameDelta delta))
        {
            Assert.Equal(expected++, delta.Tick);
        }

        Assert.Equal(frames, expected);
    }
}
```

In `FramesAlreadyInTheRingAtSubscribeTimeAreDelivered` the initial snapshot
holds the defaults (0.0, false, 0), so frame 0 (1.0, false, 0) differs in
`Speed` only — hence one change.

`tests/Dse.Realtime.Tests/SubscriptionPolicyTests.cs`:

```csharp
using Dse.Io;
using Dse.Realtime.Tests.Fakes;

namespace Dse.Realtime.Tests;

public class SubscriptionPolicyTests
{
    private static RealtimeHub Primed()
    {
        var hub = new RealtimeHub(Frames.Directory());
        hub.Publish(Frames.Full(0, 1.0, false, 0));
        hub.Pump();
        return hub;
    }

    [Fact]
    public void LosslessKeepsEveryDeltaInOrder()
    {
        using RealtimeHub hub = Primed();
        using Subscription sub = hub.Subscribe(new SubscriptionOptions { Policy = BackpressurePolicy.Lossless, Capacity = 10 });

        for (int t = 1; t <= 5; t++)
        {
            hub.Publish(Frames.Frame(t, 1.0 + t, false, 0, dirty: [Frames.Speed]));
        }

        hub.Pump();

        Assert.Equal(5, sub.Queued);
        for (int t = 1; t <= 5; t++)
        {
            Assert.True(sub.TryRead(out FrameDelta delta));
            Assert.Equal(1.0 + t, delta.Changes.Single().Value.AsDouble);
        }

        Assert.Equal(5L, sub.Delivered);
    }

    [Fact]
    public void LosslessFaultsAtCapacityInsteadOfDropping()
    {
        using RealtimeHub hub = Primed();
        using Subscription sub = hub.Subscribe(new SubscriptionOptions { Policy = BackpressurePolicy.Lossless, Capacity = 3 });

        for (int t = 1; t <= 4; t++)
        {
            hub.Publish(Frames.Frame(t, 1.0 + t, false, 0, dirty: [Frames.Speed]));
        }

        hub.Pump();

        Assert.True(sub.IsFaulted);
        Assert.Contains("3", sub.FaultReason, StringComparison.Ordinal);
        Assert.False(sub.TryRead(out _));
        Assert.Equal(0, sub.Queued);
    }

    [Fact]
    public void ConflateKeepsOnlyTheLatestValuePerTag()
    {
        using RealtimeHub hub = Primed();
        using Subscription sub = hub.Subscribe(new SubscriptionOptions { Policy = BackpressurePolicy.Conflate });

        hub.Publish(Frames.Frame(1, 2.0, false, 0, dirty: [Frames.Speed]));
        hub.Publish(Frames.Frame(2, 3.0, true, 0, dirty: [Frames.Speed, Frames.Run]));
        hub.Publish(Frames.Frame(3, 4.0, true, 0, dirty: [Frames.Speed]));
        hub.Pump();

        Assert.Equal(1, sub.Queued);
        Assert.True(sub.TryRead(out FrameDelta delta));
        Assert.Equal(3L, delta.Tick);
        Assert.Equal(Frames.TimeOf(3), delta.SimTime);
        Assert.Equal(4.0, delta.Changes.Single(c => c.Index == Frames.Speed).Value.AsDouble);
        Assert.True(delta.Changes.Single(c => c.Index == Frames.Run).Value.AsBool);
        Assert.Equal(2, delta.Changes.Count);
        Assert.False(sub.TryRead(out _));
        Assert.False(sub.IsFaulted);
    }

    [Fact]
    public void ConflateAccumulatesEventsUpToCapacityThenCountsDrops()
    {
        using RealtimeHub hub = Primed();
        using Subscription sub = hub.Subscribe(new SubscriptionOptions { Policy = BackpressurePolicy.Conflate, Capacity = 2 });

        hub.Publish(Frames.Frame(1, 1.0, false, 0, [], Frames.Event(1, "A", "E1")));
        hub.Publish(Frames.Frame(2, 1.0, false, 0, [], Frames.Event(2, "A", "E2"), Frames.Event(2, "A", "E3")));
        hub.Pump();

        Assert.True(sub.TryRead(out FrameDelta delta));
        Assert.Equal(new[] { "E1", "E2" }, delta.Events.Select(e => e.Code));
        Assert.Equal(1L, sub.DroppedEvents);
        Assert.False(sub.IsFaulted);
    }

    [Fact]
    public void AvailableIsSetWhileSomethingIsReadable()
    {
        using RealtimeHub hub = Primed();
        using Subscription sub = hub.Subscribe(new SubscriptionOptions { Policy = BackpressurePolicy.Lossless });

        Assert.False(sub.Available.WaitOne(0));
        hub.Publish(Frames.Frame(1, 2.0, false, 0, dirty: [Frames.Speed]));
        hub.Pump();
        Assert.True(sub.Available.WaitOne(0));

        Assert.True(sub.TryRead(out _));
        Assert.False(sub.Available.WaitOne(0));
    }

    [Fact]
    public void OffersAfterDisposeAreIgnored()
    {
        using RealtimeHub hub = Primed();
        Subscription sub = hub.Subscribe(new SubscriptionOptions { Policy = BackpressurePolicy.Lossless });
        sub.Dispose();

        hub.Publish(Frames.Frame(1, 2.0, false, 0, dirty: [Frames.Speed]));
        hub.Pump();

        Assert.False(sub.TryRead(out _));
        Assert.Equal(0, sub.Queued);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Dse.Realtime.Tests`
Expected: build FAILS — `RealtimeHub`, `Subscription` do not exist.

- [ ] **Step 3: Write the ring and the small types**

`src/Dse.Realtime/FrameRing.cs`:

```csharp
using Dse.Io;

namespace Dse.Realtime;

/// <summary>
/// A single-producer, single-consumer ring of frames. The producer owns
/// <c>_head</c>, the consumer owns <c>_tail</c>; each publishes its index with
/// a volatile write after touching the slot, and reads the other's with a
/// volatile read. Full means the producer's enqueue fails (R26); it never
/// touches the consumer's index.
/// </summary>
internal sealed class FrameRing
{
    private readonly TickFrame?[] _slots;
    private long _head;
    private long _tail;

    public FrameRing(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _slots = new TickFrame?[capacity];
    }

    public int Capacity => _slots.Length;

    public int Count => (int)(Volatile.Read(ref _head) - Volatile.Read(ref _tail));

    /// <summary>Producer side. False when full; the frame is then the caller's to drop.</summary>
    public bool TryEnqueue(TickFrame frame)
    {
        long head = _head;
        if (head - Volatile.Read(ref _tail) >= _slots.Length)
        {
            return false;
        }

        _slots[head % _slots.Length] = frame;
        Volatile.Write(ref _head, head + 1);
        return true;
    }

    /// <summary>Consumer side.</summary>
    public bool TryDequeue(out TickFrame frame)
    {
        long tail = _tail;
        if (tail >= Volatile.Read(ref _head))
        {
            frame = null!;
            return false;
        }

        int slot = (int)(tail % _slots.Length);
        frame = _slots[slot]!;
        _slots[slot] = null;
        Volatile.Write(ref _tail, tail + 1);
        return true;
    }
}
```

`src/Dse.Realtime/BackpressurePolicy.cs`:

```csharp
namespace Dse.Realtime;

/// <summary>What a subscription does when its consumer falls behind (spec 10.5).</summary>
public enum BackpressurePolicy
{
    /// <summary>Keep only the latest value per tag; never fault. Right for analog values feeding an HMI.</summary>
    Conflate,

    /// <summary>Keep every delta up to capacity, then fault the subscription. Mandatory for events and alarms.</summary>
    Lossless,
}
```

`src/Dse.Realtime/SubscriptionOptions.cs`:

```csharp
namespace Dse.Realtime;

/// <summary>What a subscriber declares when it subscribes.</summary>
public sealed record SubscriptionOptions
{
    /// <summary>Backpressure policy; default Conflate.</summary>
    public BackpressurePolicy Policy { get; init; } = BackpressurePolicy.Conflate;

    /// <summary>
    /// Under Lossless, the number of undelivered deltas at which the
    /// subscription faults. Under Conflate, the number of pending events kept
    /// before further events are counted as dropped. Default 1024.
    /// </summary>
    public int Capacity { get; init; } = 1024;
}
```

`src/Dse.Realtime/TagChange.cs`:

```csharp
using Dse.Io;

namespace Dse.Realtime;

/// <summary>One tag's new value in a delta.</summary>
public readonly record struct TagChange(int Index, TagValue Value);
```

`src/Dse.Realtime/FrameDelta.cs`:

```csharp
using Dse.Io;

namespace Dse.Realtime;

/// <summary>
/// What a subscriber reads: the tags that changed since its previous delta
/// (or since its initial snapshot) and the events in between. Under Conflate
/// or decimation a delta may span many frames; <see cref="Tick"/> is the
/// newest.
/// </summary>
public sealed record FrameDelta(
    long Tick,
    DateTimeOffset SimTime,
    IReadOnlyList<TagChange> Changes,
    IReadOnlyList<DiscreteEvent> Events);
```

`src/Dse.Realtime/DeltaAccumulator.cs`:

```csharp
using Dse.Io;

namespace Dse.Realtime;

/// <summary>
/// Merges successive changes into one pending delta: latest value per tag,
/// events appended up to a capacity. Used for the Conflate policy's pending
/// slot and (Task 11) for decimation windows. Not thread-safe; the owning
/// subscription locks.
/// </summary>
internal sealed class DeltaAccumulator
{
    private readonly TagValue[] _values;
    private readonly bool[] _set;
    private readonly List<DiscreteEvent> _events = [];
    private readonly int _eventCapacity;
    private int _count;
    private long _tick;
    private DateTimeOffset _simTime;

    public DeltaAccumulator(int tagCount, int eventCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(tagCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(eventCapacity);
        _values = new TagValue[tagCount];
        _set = new bool[tagCount];
        _eventCapacity = eventCapacity;
    }

    public bool HasContent => _count > 0 || _events.Count > 0;

    public long DroppedEvents { get; private set; }

    public void Add(long tick, DateTimeOffset simTime, IReadOnlyList<TagChange> changes, IReadOnlyList<DiscreteEvent> events)
    {
        for (int i = 0; i < changes.Count; i++)
        {
            TagChange change = changes[i];
            if (!_set[change.Index])
            {
                _set[change.Index] = true;
                _count++;
            }

            _values[change.Index] = change.Value;
        }

        for (int i = 0; i < events.Count; i++)
        {
            if (_events.Count >= _eventCapacity)
            {
                DroppedEvents++;
                continue;
            }

            _events.Add(events[i]);
        }

        _tick = tick;
        _simTime = simTime;
    }

    public FrameDelta Flush()
    {
        var changes = new TagChange[_count];
        int k = 0;
        for (int i = 0; i < _values.Length && k < changes.Length; i++)
        {
            if (_set[i])
            {
                changes[k++] = new TagChange(i, _values[i]);
                _set[i] = false;
            }
        }

        _count = 0;
        DiscreteEvent[] events = _events.ToArray();
        _events.Clear();
        return new FrameDelta(_tick, _simTime, changes, events);
    }
}
```

- [ ] **Step 4: Write `Subscription`**

`src/Dse.Realtime/Subscription.cs`:

```csharp
using Dse.Io;

namespace Dse.Realtime;

/// <summary>
/// One consumer's view of the frame stream (spec 10.2, 10.5, 10.7). The hub
/// offers every frame on the pump thread; the subscription diffs it against the
/// values it last delivered and queues the result under its policy. The
/// consumer pulls with <see cref="TryRead"/> or waits on <see cref="Available"/>
/// (R27). A faulted subscription delivers nothing more and says why.
/// </summary>
public sealed class Subscription : IDisposable
{
    private readonly RealtimeHub _hub;
    private readonly object _sync = new();
    private readonly TagValue[] _lastSent;
    private readonly ManualResetEvent _available = new(false);
    private readonly Queue<FrameDelta>? _queue;
    private readonly DeltaAccumulator? _pending;
    private readonly List<TagChange> _scratch = [];
    private bool _faulted;
    private string? _faultReason;
    private bool _disposed;
    private long _delivered;

    internal Subscription(RealtimeHub hub, SubscriptionOptions options, StateSnapshot initial)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.Capacity);

        _hub = hub;
        Options = options;
        Initial = initial;

        _lastSent = new TagValue[initial.Tags.Length];
        ReadOnlySpan<TagState> tags = initial.Tags.Span;
        for (int i = 0; i < _lastSent.Length; i++)
        {
            _lastSent[i] = tags[i].Value;
        }

        if (options.Policy == BackpressurePolicy.Lossless)
        {
            _queue = new Queue<FrameDelta>();
        }
        else
        {
            _pending = new DeltaAccumulator(_lastSent.Length, options.Capacity);
        }
    }

    /// <summary>What was declared at subscribe time.</summary>
    public SubscriptionOptions Options { get; }

    /// <summary>The state at subscribe time; deltas follow from the next tick (spec 10.7).</summary>
    public StateSnapshot Initial { get; }

    /// <summary>Set while a delta is readable or the subscription is faulted.</summary>
    public WaitHandle Available => _available;

    /// <summary>True once the subscription has been closed by the hub for cause.</summary>
    public bool IsFaulted
    {
        get
        {
            lock (_sync)
            {
                return _faulted;
            }
        }
    }

    /// <summary>Why it faulted, or null.</summary>
    public string? FaultReason
    {
        get
        {
            lock (_sync)
            {
                return _faultReason;
            }
        }
    }

    /// <summary>Deltas handed to the consumer so far.</summary>
    public long Delivered
    {
        get
        {
            lock (_sync)
            {
                return _delivered;
            }
        }
    }

    /// <summary>Under Conflate, events discarded because the pending delta was at capacity.</summary>
    public long DroppedEvents
    {
        get
        {
            lock (_sync)
            {
                return _pending?.DroppedEvents ?? 0L;
            }
        }
    }

    /// <summary>Deltas waiting: the queue length under Lossless, 0 or 1 under Conflate.</summary>
    public int Queued
    {
        get
        {
            lock (_sync)
            {
                return _queue?.Count ?? (_pending!.HasContent ? 1 : 0);
            }
        }
    }

    /// <summary>Takes the next delta if there is one. Never blocks.</summary>
    public bool TryRead(out FrameDelta delta)
    {
        lock (_sync)
        {
            if (_faulted || _disposed)
            {
                delta = null!;
                return false;
            }

            if (_queue is not null)
            {
                if (!_queue.TryDequeue(out FrameDelta? dequeued))
                {
                    delta = null!;
                    return false;
                }

                delta = dequeued;
            }
            else if (_pending!.HasContent)
            {
                delta = _pending.Flush();
            }
            else
            {
                delta = null!;
                return false;
            }

            _delivered++;
            if (_queue is { Count: 0 } || (_queue is null && !_pending!.HasContent))
            {
                _available.Reset();
            }

            return true;
        }
    }

    /// <summary>Pump thread: diffs the frame against the last delivered values and queues the result.</summary>
    internal void Offer(TickFrame frame)
    {
        lock (_sync)
        {
            if (_faulted || _disposed)
            {
                return;
            }

            Collect(frame, _scratch);
            if (_scratch.Count == 0 && frame.Events.Count == 0)
            {
                return;
            }

            Deliver(frame.Tick, frame.SimTime, _scratch, frame.Events);
            _scratch.Clear();
        }
    }

    /// <summary>Pump thread: the ring dropped frames since the last pump.</summary>
    internal void NotifyGap(long dropped)
    {
        lock (_sync)
        {
            if (_queue is not null)
            {
                FaultLocked($"ring overflow: {dropped} frame(s) dropped; a Lossless subscription cannot continue.");
            }
        }
    }

    /// <summary>Closes the subscription for cause.</summary>
    internal void Fault(string reason)
    {
        lock (_sync)
        {
            FaultLocked(reason);
        }
    }

    /// <summary>Unsubscribes. Idempotent.</summary>
    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _queue?.Clear();
        }

        _hub.Unsubscribe(this);
        _available.Dispose();
    }

    private void Collect(TickFrame frame, List<TagChange> into)
    {
        ReadOnlySpan<TagValue> values = frame.Values.Span;
        for (int i = 0; i < _lastSent.Length; i++)
        {
            if (values[i] == _lastSent[i])
            {
                continue;
            }

            into.Add(new TagChange(i, values[i]));
            _lastSent[i] = values[i];
        }
    }

    private void Deliver(long tick, DateTimeOffset simTime, List<TagChange> changes, IReadOnlyList<DiscreteEvent> events)
    {
        if (_queue is not null)
        {
            if (_queue.Count >= Options.Capacity)
            {
                FaultLocked($"lossless queue overflow: {Options.Capacity} undelivered delta(s); the consumer is too slow.");
                return;
            }

            _queue.Enqueue(new FrameDelta(tick, simTime, changes.ToArray(), events));
        }
        else
        {
            _pending!.Add(tick, simTime, changes, events);
        }

        _available.Set();
    }

    private void FaultLocked(string reason)
    {
        if (_faulted || _disposed)
        {
            return;
        }

        _faulted = true;
        _faultReason = reason;
        _queue?.Clear();
        _available.Set();
    }
}
```

- [ ] **Step 5: Write `RealtimeHub`**

`src/Dse.Realtime/RealtimeHub.cs`:

```csharp
using Dse.Io;

namespace Dse.Realtime;

/// <summary>
/// The real-time event engine (spec 10.1, 10.2): owns the ring the simulation
/// publishes into, the pump that fans frames out to the live state and to every
/// subscription, and the no-gap subscribe. <see cref="Publish"/> is the
/// simulation thread's only obligation and never blocks. <see cref="Pump"/> is
/// called by a dispatcher thread in production and directly in tests.
/// </summary>
public sealed class RealtimeHub : ITickFrameSink, IDisposable
{
    private readonly FrameRing _ring;
    private readonly object _pumpLock = new();
    private readonly List<Subscription> _subscriptions = [];
    private readonly AutoResetEvent _framesAvailable = new(false);
    private long _published;
    private long _dropped;
    private long _droppedSinceLastPump;
    private bool _disposed;

    /// <summary>Creates a hub for <paramref name="directory"/> with a ring of <paramref name="ringCapacity"/> frames.</summary>
    public RealtimeHub(ITagDirectory directory, int ringCapacity = 4096, int recentEventCapacity = 256)
    {
        ArgumentNullException.ThrowIfNull(directory);
        Directory = directory;
        _ring = new FrameRing(ringCapacity);
        State = new LiveState(directory, recentEventCapacity);
    }

    /// <summary>The directory frames are indexed by.</summary>
    public ITagDirectory Directory { get; }

    /// <summary>Current truth, maintained by the pump (spec 10.3).</summary>
    public LiveState State { get; }

    /// <summary>Frames accepted into the ring.</summary>
    public long PublishedFrames => Volatile.Read(ref _published);

    /// <summary>Frames dropped because the ring was full (R26).</summary>
    public long DroppedFrames => Volatile.Read(ref _dropped);

    /// <summary>Frames in the ring not yet pumped.</summary>
    public int Pending => _ring.Count;

    /// <summary>Signalled on every publish; what a dispatcher waits on.</summary>
    public WaitHandle FramesAvailable => _framesAvailable;

    /// <summary>Live subscriptions.</summary>
    public int SubscriberCount
    {
        get
        {
            lock (_pumpLock)
            {
                return _subscriptions.Count;
            }
        }
    }

    /// <inheritdoc/>
    public void Publish(TickFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (_ring.TryEnqueue(frame))
        {
            Interlocked.Increment(ref _published);
        }
        else
        {
            Interlocked.Increment(ref _dropped);
            Interlocked.Increment(ref _droppedSinceLastPump);
        }

        if (!_disposed)
        {
            _framesAvailable.Set();
        }
    }

    /// <summary>
    /// Drains the ring: applies each frame to <see cref="State"/>, then offers
    /// it to every subscription. Returns the number of frames processed. Safe
    /// to call from any single thread at a time; concurrent callers serialise.
    /// </summary>
    public int Pump()
    {
        lock (_pumpLock)
        {
            long gap = Interlocked.Exchange(ref _droppedSinceLastPump, 0L);
            if (gap > 0L)
            {
                foreach (Subscription subscription in _subscriptions)
                {
                    subscription.NotifyGap(gap);
                }
            }

            int processed = 0;
            while (_ring.TryDequeue(out TickFrame frame))
            {
                State.Apply(frame);
                foreach (Subscription subscription in _subscriptions)
                {
                    subscription.Offer(frame);
                }

                processed++;
            }

            return processed;
        }
    }

    /// <summary>
    /// Captures the state snapshot and registers the subscription in one step
    /// under the pump lock, so no frame can fall between them (spec 10.7).
    /// </summary>
    public Subscription Subscribe(SubscriptionOptions? options = null)
    {
        lock (_pumpLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var subscription = new Subscription(this, options ?? new SubscriptionOptions(), State.Snapshot());
            _subscriptions.Add(subscription);
            return subscription;
        }
    }

    internal void Unsubscribe(Subscription subscription)
    {
        lock (_pumpLock)
        {
            _subscriptions.Remove(subscription);
        }
    }

    /// <summary>Releases the wait handle. Dispose the dispatcher first.</summary>
    public void Dispose()
    {
        lock (_pumpLock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        _framesAvailable.Dispose();
    }
}
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/Dse.Realtime.Tests`
Expected: PASS, 24 tests. Run the threaded test ten times:
`for i in $(seq 10); do dotnet test tests/Dse.Realtime.Tests --filter "FullyQualifiedName~ProducerAndPumpOnDifferentThreads" --no-build || break; done`.
`dotnet build -c Release` — 0 warnings.

- [ ] **Step 7: Commit**

```bash
git add src/Dse.Realtime tests/Dse.Realtime.Tests
git commit -m "feat(realtime): add the hub with its ring, pump, no-gap subscribe and the Conflate and Lossless policies"
```

---

### Task 11: Tag filters, deadbands and decimation

**Files:**
- Modify: `src/Dse.Realtime/SubscriptionOptions.cs`
- Modify: `src/Dse.Realtime/Subscription.cs`
- Test: `tests/Dse.Realtime.Tests/SubscriptionFilterTests.cs`

**Interfaces:**
- Consumes: Task 10.
- Produces: on `SubscriptionOptions`: `IReadOnlyList<string>? Prefixes`
  (null = every tag; a tag matches when its name equals a prefix or starts
  with the prefix followed by a dot), `IReadOnlyDictionary<string, double>?
  Deadbands` (absolute, per tag name, double tags only), `double
  DeadbandPercentOfRange` (0 = none; applied to double tags that have a range
  and no explicit deadband), `TimeSpan? Decimation` (null = every frame; else
  at most one delta per that much simulation time, the frames in between
  merged). `Subscription` resolves all four against the directory once, at
  subscribe time.

Deadbands live here and not in Core because they are subscriber-specific
(spec 10.2). A deadband compares against the value the subscription *last
delivered*, so a slow drift still gets through once it has moved far enough,
and a quality change always passes. Decimation windows are merged with the
same `DeltaAccumulator` as the Conflate policy, so under Lossless a window
keeps every event and the latest value per tag, and `FrameDelta.Tick` is the
newest frame folded in (spec 10.8).

- [ ] **Step 1: Write the failing tests**

`tests/Dse.Realtime.Tests/SubscriptionFilterTests.cs`:

```csharp
using Dse.Io;
using Dse.Realtime.Tests.Fakes;

namespace Dse.Realtime.Tests;

public class SubscriptionFilterTests
{
    private static RealtimeHub Primed()
    {
        var hub = new RealtimeHub(Frames.Directory());
        hub.Publish(Frames.Full(0, 1.0, false, 0));
        hub.Pump();
        return hub;
    }

    private static SubscriptionOptions Lossless => new() { Policy = BackpressurePolicy.Lossless };

    private static List<FrameDelta> Drain(Subscription sub)
    {
        var deltas = new List<FrameDelta>();
        while (sub.TryRead(out FrameDelta delta))
        {
            deltas.Add(delta);
        }

        return deltas;
    }

    [Fact]
    public void PrefixesSelectWholeComponents()
    {
        using RealtimeHub hub = Primed();
        using Subscription sub = hub.Subscribe(Lossless with { Prefixes = ["A"] });

        hub.Publish(Frames.Frame(1, 2.0, true, 5, dirty: [Frames.Speed, Frames.Run, Frames.Count]));
        hub.Pump();

        FrameDelta delta = Assert.Single(Drain(sub));
        Assert.Equal(new[] { Frames.Speed, Frames.Run }, delta.Changes.Select(c => c.Index));
    }

    [Fact]
    public void APrefixMatchesWholeSegmentsOnly()
    {
        using RealtimeHub hub = Primed();
        using Subscription exact = hub.Subscribe(Lossless with { Prefixes = ["A.Run"] });
        using Subscription partial = hub.Subscribe(Lossless with { Prefixes = ["A.R"] });

        hub.Publish(Frames.Frame(1, 2.0, true, 5, dirty: [Frames.Speed, Frames.Run, Frames.Count]));
        hub.Pump();

        Assert.Equal(Frames.Run, Assert.Single(Drain(exact)).Changes.Single().Index);
        Assert.Empty(Drain(partial));
    }

    [Fact]
    public void FilteredOutTagsStillDeliverEvents()
    {
        using RealtimeHub hub = Primed();
        using Subscription sub = hub.Subscribe(Lossless with { Prefixes = ["B"] });

        hub.Publish(Frames.Frame(1, 2.0, false, 0, [Frames.Speed], Frames.Event(1, "A", "AT_SPEED")));
        hub.Pump();

        FrameDelta delta = Assert.Single(Drain(sub));
        Assert.Empty(delta.Changes);
        Assert.Equal("AT_SPEED", Assert.Single(delta.Events).Code);
    }

    [Fact]
    public void AnAbsoluteDeadbandComparesAgainstTheLastDeliveredValue()
    {
        using RealtimeHub hub = Primed();
        using Subscription sub = hub.Subscribe(Lossless with { Deadbands = new Dictionary<string, double> { ["A.Speed"] = 0.1 } });

        hub.Publish(Frames.Frame(1, 1.05, false, 0, dirty: [Frames.Speed]));
        hub.Publish(Frames.Frame(2, 1.11, false, 0, dirty: [Frames.Speed]));
        hub.Publish(Frames.Frame(3, 1.15, false, 0, dirty: [Frames.Speed]));
        hub.Publish(Frames.Frame(4, 1.30, false, 0, dirty: [Frames.Speed]));
        hub.Pump();

        Assert.Equal(new[] { (2L, 1.11), (4L, 1.30) }, Drain(sub).Select(d => (d.Tick, d.Changes.Single().Value.AsDouble)));
    }

    [Fact]
    public void AQualityChangeAlwaysPassesTheDeadband()
    {
        using RealtimeHub hub = Primed();
        using Subscription sub = hub.Subscribe(Lossless with { Deadbands = new Dictionary<string, double> { ["A.Speed"] = 1.0 } });

        TagValue[] bad = [TagValue.Double(1.0, TagQuality.Bad(QualityDetail.SensorFailure)), TagValue.Bool(false), TagValue.Int64(0)];
        var frame = new TickFrame(1, Frames.TimeOf(1), bad, DirtyMask.FromBits(new ulong[] { 1UL }, 3, 1), []);
        hub.Publish(frame);
        hub.Pump();

        FrameDelta delta = Assert.Single(Drain(sub));
        Assert.Equal(TagQuality.Bad(QualityDetail.SensorFailure), delta.Changes.Single().Value.Quality);
    }

    [Fact]
    public void PercentOfRangeDeadbandUsesTheDirectoryRange()
    {
        using RealtimeHub hub = Primed();
        using Subscription sub = hub.Subscribe(Lossless with { DeadbandPercentOfRange = 5.0 });   // A.Speed range 0..2 → 0.1

        hub.Publish(Frames.Frame(1, 1.09, false, 0, dirty: [Frames.Speed]));
        hub.Publish(Frames.Frame(2, 1.10, false, 0, dirty: [Frames.Speed]));
        hub.Pump();

        Assert.Equal(new[] { 2L }, Drain(sub).Select(d => d.Tick));
    }

    [Fact]
    public void AnExplicitDeadbandOverridesThePercent()
    {
        using RealtimeHub hub = Primed();
        using Subscription sub = hub.Subscribe(Lossless with
        {
            DeadbandPercentOfRange = 50.0,
            Deadbands = new Dictionary<string, double> { ["A.Speed"] = 0.01 },
        });

        hub.Publish(Frames.Frame(1, 1.02, false, 0, dirty: [Frames.Speed]));
        hub.Pump();

        Assert.Single(Drain(sub));
    }

    [Fact]
    public void DeadbandsAreValidatedAtSubscribeTime()
    {
        using RealtimeHub hub = Primed();

        Assert.Throws<ArgumentException>(() => hub.Subscribe(Lossless with { Deadbands = new Dictionary<string, double> { ["Nope"] = 0.1 } }));
        Assert.Throws<ArgumentException>(() => hub.Subscribe(Lossless with { Deadbands = new Dictionary<string, double> { ["A.Run"] = 0.1 } }));
        Assert.Throws<ArgumentOutOfRangeException>(() => hub.Subscribe(Lossless with { Deadbands = new Dictionary<string, double> { ["A.Speed"] = -0.1 } }));
        Assert.Throws<ArgumentOutOfRangeException>(() => hub.Subscribe(Lossless with { DeadbandPercentOfRange = -1.0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => hub.Subscribe(Lossless with { Decimation = TimeSpan.Zero }));
    }

    [Fact]
    public void DecimationEmitsAtMostOncePerIntervalOfSimTime()
    {
        using RealtimeHub hub = Primed();
        using Subscription sub = hub.Subscribe(Lossless with { Decimation = TimeSpan.FromMilliseconds(50) });

        for (int t = 1; t <= 12; t++)
        {
            hub.Publish(Frames.Frame(t, 1.0 + t, false, 0, dirty: [Frames.Speed]));
        }

        hub.Pump();

        List<FrameDelta> deltas = Drain(sub);
        Assert.Equal(new[] { 1L, 6L, 11L }, deltas.Select(d => d.Tick));
        Assert.Equal(new[] { 2.0, 7.0, 12.0 }, deltas.Select(d => d.Changes.Single().Value.AsDouble));
        Assert.Equal(0, sub.Queued);
        Assert.Empty(Drain(sub));
    }

    [Fact]
    public void DecimationKeepsEveryEventInTheWindow()
    {
        using RealtimeHub hub = Primed();
        using Subscription sub = hub.Subscribe(Lossless with { Decimation = TimeSpan.FromMilliseconds(50) });

        hub.Publish(Frames.Frame(1, 1.1, false, 0, dirty: [Frames.Speed]));
        hub.Publish(Frames.Frame(2, 1.2, false, 0, [Frames.Speed], Frames.Event(2, "A", "E2")));
        hub.Publish(Frames.Frame(4, 1.4, false, 0, [Frames.Speed], Frames.Event(4, "A", "E4")));
        hub.Publish(Frames.Frame(6, 1.6, true, 0, dirty: [Frames.Speed, Frames.Run]));
        hub.Pump();

        List<FrameDelta> deltas = Drain(sub);
        Assert.Equal(2, deltas.Count);
        Assert.Equal(new[] { "E2", "E4" }, deltas[1].Events.Select(e => e.Code));
        Assert.Equal(6L, deltas[1].Tick);
        Assert.Equal(2, deltas[1].Changes.Count);
    }

    [Fact]
    public void DecimationAndConflateCompose()
    {
        using RealtimeHub hub = Primed();
        using Subscription sub = hub.Subscribe(new SubscriptionOptions { Policy = BackpressurePolicy.Conflate, Decimation = TimeSpan.FromMilliseconds(50) });

        for (int t = 1; t <= 12; t++)
        {
            hub.Publish(Frames.Frame(t, 1.0 + t, false, 0, dirty: [Frames.Speed]));
        }

        hub.Pump();

        Assert.Equal(1, sub.Queued);
        FrameDelta delta = Assert.Single(Drain(sub));
        Assert.Equal(11L, delta.Tick);
        Assert.Equal(12.0, delta.Changes.Single().Value.AsDouble);
    }
}
```

The last two assertions of `DecimationEmitsAtMostOncePerIntervalOfSimTime`
check that frame 12 is still held in the window and not yet readable.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Dse.Realtime.Tests --filter "FullyQualifiedName~SubscriptionFilterTests"`
Expected: build FAILS — `Prefixes`, `Deadbands`, `Decimation` do not exist.

- [ ] **Step 3: Extend the options**

Replace `src/Dse.Realtime/SubscriptionOptions.cs` with:

```csharp
namespace Dse.Realtime;

/// <summary>What a subscriber declares when it subscribes (spec 10.2, 10.5, 10.8).</summary>
public sealed record SubscriptionOptions
{
    /// <summary>Backpressure policy; default Conflate.</summary>
    public BackpressurePolicy Policy { get; init; } = BackpressurePolicy.Conflate;

    /// <summary>
    /// Under Lossless, the number of undelivered deltas at which the
    /// subscription faults. Under Conflate, the number of pending events kept
    /// before further events are counted as dropped. Default 1024.
    /// </summary>
    public int Capacity { get; init; } = 1024;

    /// <summary>
    /// Tag name prefixes to include; null means every tag. A tag matches when
    /// its name equals a prefix or starts with the prefix followed by a dot,
    /// so <c>CV001</c> selects the whole conveyor and <c>CV001.Speed</c> one tag.
    /// Events are never filtered.
    /// </summary>
    public IReadOnlyList<string>? Prefixes { get; init; }

    /// <summary>Absolute deadband per tag name, in the tag's unit. Double tags only.</summary>
    public IReadOnlyDictionary<string, double>? Deadbands { get; init; }

    /// <summary>
    /// A deadband as a percentage of the directory range, applied to every
    /// double tag that has a range and no entry in <see cref="Deadbands"/>.
    /// 0 (the default) means none.
    /// </summary>
    public double DeadbandPercentOfRange { get; init; }

    /// <summary>
    /// At most one delta per this much simulation time; frames in between are
    /// merged (latest value per tag, every event). Null means every frame.
    /// </summary>
    public TimeSpan? Decimation { get; init; }
}
```

- [ ] **Step 4: Resolve the options in `Subscription`**

In `src/Dse.Realtime/Subscription.cs`:

Add fields after `_scratch`:

```csharp
    private readonly bool[] _included;
    private readonly double[] _deadband;
    private readonly DeltaAccumulator? _window;
    private readonly TimeSpan _decimation;
    private DateTimeOffset? _lastEmit;
```

In the constructor, after the `_lastSent` loop and before the policy branch:

```csharp
        ITagDirectory directory = hub.Directory;
        if (directory.Count != _lastSent.Length)
        {
            throw new ArgumentException("The snapshot does not match the hub's directory.", nameof(initial));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(options.DeadbandPercentOfRange);
        if (options.Decimation is { } decimation)
        {
            if (decimation <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(options), decimation, "Decimation must be positive.");
            }

            _decimation = decimation;
            _window = new DeltaAccumulator(_lastSent.Length, int.MaxValue);
        }

        _included = new bool[_lastSent.Length];
        _deadband = new double[_lastSent.Length];
        for (int i = 0; i < _included.Length; i++)
        {
            TagDescriptor tag = directory[i];
            _included[i] = options.Prefixes is null || Matches(tag.Name, options.Prefixes);
            _deadband[i] = double.NaN;
            if (tag.Kind == TagKind.Double && options.DeadbandPercentOfRange > 0.0 && tag.HasRange)
            {
                _deadband[i] = (tag.RangeHigh - tag.RangeLow) * options.DeadbandPercentOfRange / 100.0;
            }
        }

        if (options.Deadbands is not null)
        {
            foreach ((string name, double band) in options.Deadbands)
            {
                if (!directory.TryFind(name, out TagDescriptor tag))
                {
                    throw new ArgumentException($"Deadband for unknown tag '{name}'.", nameof(options));
                }

                if (tag.Kind != TagKind.Double)
                {
                    throw new ArgumentException($"Deadband on '{name}', a {tag.Kind} tag; deadbands apply to double tags only.", nameof(options));
                }

                ArgumentOutOfRangeException.ThrowIfNegative(band, nameof(options));
                _deadband[tag.Index] = band;
            }
        }
```

Add `using Dse.Io;` is already present. Add the helper at the end of the class:

```csharp
    private static bool Matches(string name, IReadOnlyList<string> prefixes)
    {
        for (int i = 0; i < prefixes.Count; i++)
        {
            string prefix = prefixes[i];
            if (name.Length == prefix.Length)
            {
                if (string.Equals(name, prefix, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            else if (name.Length > prefix.Length
                     && name[prefix.Length] == '.'
                     && name.StartsWith(prefix, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
```

Replace `Collect`:

```csharp
    private void Collect(TickFrame frame, List<TagChange> into)
    {
        ReadOnlySpan<TagValue> values = frame.Values.Span;
        for (int i = 0; i < _lastSent.Length; i++)
        {
            if (!_included[i])
            {
                continue;
            }

            TagValue value = values[i];
            TagValue last = _lastSent[i];
            if (value == last)
            {
                continue;
            }

            double band = _deadband[i];
            if (!double.IsNaN(band)
                && value.Quality == last.Quality
                && Math.Abs(value.AsDouble - last.AsDouble) < band)
            {
                continue;
            }

            into.Add(new TagChange(i, value));
            _lastSent[i] = value;
        }
    }
```

Replace `Offer`:

```csharp
    internal void Offer(TickFrame frame)
    {
        lock (_sync)
        {
            if (_faulted || _disposed)
            {
                return;
            }

            Collect(frame, _scratch);
            bool content = _scratch.Count > 0 || frame.Events.Count > 0;

            if (_window is null)
            {
                if (content)
                {
                    Deliver(frame.Tick, frame.SimTime, _scratch, frame.Events);
                }
            }
            else
            {
                if (content)
                {
                    _window.Add(frame.Tick, frame.SimTime, _scratch, frame.Events);
                }

                if (_lastEmit is null || frame.SimTime - _lastEmit.Value >= _decimation)
                {
                    if (_window.HasContent)
                    {
                        FrameDelta merged = _window.Flush();
                        Deliver(merged.Tick, merged.SimTime, merged.Changes, merged.Events);
                    }

                    _lastEmit = frame.SimTime;
                }
            }

            _scratch.Clear();
        }
    }
```

Change `Deliver`'s signature to take `IReadOnlyList<TagChange> changes` (the
body is unchanged; `changes.ToArray()` works on both a `List<TagChange>` and
the array the window flushes — use `changes as TagChange[] ?? changes.ToArray()`
to avoid copying the flushed array).

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/Dse.Realtime.Tests`
Expected: PASS, 35 tests. `dotnet build -c Release` — 0 warnings.

- [ ] **Step 6: Commit**

```bash
git add src/Dse.Realtime tests/Dse.Realtime.Tests
git commit -m "feat(realtime): add per-subscriber prefix filters, deadbands and decimation"
```

---

### Task 12: The dispatcher thread and the command bus

**Files:**
- Create: `src/Dse.Realtime/DispatcherThread.cs`
- Create: `src/Dse.Realtime/CommandOutcome.cs`
- Create: `src/Dse.Realtime/TagCommand.cs`
- Create: `src/Dse.Realtime/ICommandRecorder.cs`
- Create: `src/Dse.Realtime/CommandBus.cs`
- Test: `tests/Dse.Realtime.Tests/Fakes/RecordingWriter.cs`
- Test: `tests/Dse.Realtime.Tests/DispatcherThreadTests.cs`
- Test: `tests/Dse.Realtime.Tests/CommandBusTests.cs`

**Interfaces:**
- Consumes: Task 10 (`RealtimeHub`), `ITagWriter`, `ITagDirectory`.
- Produces: `sealed class DispatcherThread : IDisposable` with
  `DispatcherThread(RealtimeHub hub, string name = "dse-dispatcher", TimeSpan?
  idleWait = null)`, `void Start()`, `bool IsRunning`, `long PumpedFrames`,
  `Exception? Failure`; `enum CommandOutcome { Accepted, UnknownTag,
  KindMismatch, ReadOnly, OutOfRange }`; `sealed record TagCommand(string Tag,
  TagValue Value, CommandOutcome Outcome)`; `interface ICommandRecorder { void
  Record(TagCommand command); }`; `sealed class CommandBus` with
  `CommandBus(ITagWriter writer, ICommandRecorder? recorder = null)`,
  `ITagDirectory Directory`, `long Accepted`, `long Rejected`,
  `CommandOutcome Write(string tag, TagValue value)`, `WriteBool`,
  `WriteDouble`, `WriteInt64`.

The dispatcher is a background thread that waits on `hub.FramesAvailable`
with a short timeout and pumps; on dispose it stops, pumps once more so
nothing published before the stop is stranded, and joins. The command bus is
spec 10.6: inbound writes on their own channel, validated against the
directory (unknown, read-only, wrong kind, outside the declared range), then
handed to the simulation's `ITagWriter`. Every command, accepted or not, goes
to the optional recorder — plan 5's scenario recorder plugs in there. Returns
an outcome rather than throwing because callers are remote and send junk.

- [ ] **Step 1: Write the fake and the failing tests**

`tests/Dse.Realtime.Tests/Fakes/RecordingWriter.cs`:

```csharp
using Dse.Io;

namespace Dse.Realtime.Tests.Fakes;

/// <summary>An <see cref="ITagWriter"/> that remembers what it was asked to write.</summary>
public sealed class RecordingWriter : ITagWriter
{
    public RecordingWriter(ITagDirectory directory) => Directory = directory;

    public ITagDirectory Directory { get; }

    public List<(int Index, TagValue Value)> Writes { get; } = [];

    public void Write(int index, TagValue value) => Writes.Add((index, value));

    public void Write(string name, TagValue value) => Write(Directory.Find(name).Index, value);
}
```

`tests/Dse.Realtime.Tests/DispatcherThreadTests.cs`:

```csharp
using System.Diagnostics;
using Dse.Realtime.Tests.Fakes;

namespace Dse.Realtime.Tests;

public class DispatcherThreadTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private static void WaitUntil(Func<bool> condition)
    {
        Stopwatch clock = Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(clock.Elapsed < Patience, "Timed out waiting for the dispatcher.");
            Thread.Sleep(1);
        }
    }

    [Fact]
    public void PumpsFramesInTheBackground()
    {
        using var hub = new RealtimeHub(Frames.Directory(), ringCapacity: 1 << 14);
        using Subscription sub = hub.Subscribe(new SubscriptionOptions { Policy = BackpressurePolicy.Lossless, Capacity = 10_000 });
        using var dispatcher = new DispatcherThread(hub);
        dispatcher.Start();

        for (int t = 0; t < 1_000; t++)
        {
            hub.Publish(Frames.Frame(t, t + 1.0, false, 0, dirty: [Frames.Speed]));
        }

        WaitUntil(() => hub.State.Tick == 999);

        Assert.True(dispatcher.IsRunning);
        Assert.Equal(0L, hub.DroppedFrames);
        WaitUntil(() => sub.Queued == 1_000);
        long expected = 0;
        while (sub.TryRead(out FrameDelta delta))
        {
            Assert.Equal(expected++, delta.Tick);
        }

        Assert.Equal(1_000L, expected);
        Assert.Null(dispatcher.Failure);
    }

    [Fact]
    public void DisposeStopsPromptlyWhenIdle()
    {
        using var hub = new RealtimeHub(Frames.Directory());
        var dispatcher = new DispatcherThread(hub, idleWait: TimeSpan.FromMilliseconds(20));
        dispatcher.Start();
        WaitUntil(() => dispatcher.IsRunning);

        Stopwatch clock = Stopwatch.StartNew();
        dispatcher.Dispose();

        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(2));
        Assert.False(dispatcher.IsRunning);
    }

    [Fact]
    public void DisposeDrainsWhatWasPublishedBeforeTheStop()
    {
        using var hub = new RealtimeHub(Frames.Directory());
        var dispatcher = new DispatcherThread(hub, idleWait: TimeSpan.FromSeconds(5));
        dispatcher.Start();
        WaitUntil(() => dispatcher.IsRunning);

        hub.Publish(Frames.Full(0, 1.0, false, 0));
        hub.Publish(Frames.Full(1, 2.0, false, 0));
        dispatcher.Dispose();

        Assert.Equal(1L, hub.State.Tick);
        Assert.Equal(2L, dispatcher.PumpedFrames);
    }

    [Fact]
    public void StartTwiceThrows()
    {
        using var hub = new RealtimeHub(Frames.Directory());
        using var dispatcher = new DispatcherThread(hub);
        dispatcher.Start();

        Assert.Throws<InvalidOperationException>(dispatcher.Start);
    }
}
```

`tests/Dse.Realtime.Tests/CommandBusTests.cs`:

```csharp
using Dse.Io;
using Dse.Realtime.Tests.Fakes;

namespace Dse.Realtime.Tests;

public class CommandBusTests
{
    private sealed class Recorder : ICommandRecorder
    {
        public List<TagCommand> Commands { get; } = [];

        public void Record(TagCommand command) => Commands.Add(command);
    }

    private static ArrayDirectory Directory() => new(
        new TagDescriptor(0, "CV001.Speed", TagKind.Double, TagAccess.ReadOnly, "m/s", 0.0, 2.0, "Belt speed"),
        new TagDescriptor(1, "CV001.Start", TagKind.Bool, TagAccess.ReadWrite, string.Empty, double.NaN, double.NaN, "Start"),
        new TagDescriptor(2, "Feed.Rate", TagKind.Double, TagAccess.ReadWrite, "kg/s", 0.0, 20.0, "Feed rate"),
        new TagDescriptor(3, "Feed.Batches", TagKind.Int64, TagAccess.ReadWrite, "count", double.NaN, double.NaN, "Batches"),
        new TagDescriptor(4, "Feed.Bias", TagKind.Double, TagAccess.ReadWrite, "kg/s", double.NaN, double.NaN, "Unranged"));

    [Fact]
    public void AnAcceptedCommandReachesTheWriter()
    {
        var writer = new RecordingWriter(Directory());
        var bus = new CommandBus(writer);

        Assert.Equal(CommandOutcome.Accepted, bus.WriteBool("CV001.Start", true));
        Assert.Equal(CommandOutcome.Accepted, bus.WriteDouble("Feed.Rate", 12.5));
        Assert.Equal(CommandOutcome.Accepted, bus.WriteInt64("Feed.Batches", 3L));

        Assert.Equal(new[] { (1, TagValue.Bool(true)), (2, TagValue.Double(12.5)), (3, TagValue.Int64(3L)) }, writer.Writes);
        Assert.Equal((3L, 0L), (bus.Accepted, bus.Rejected));
    }

    [Theory]
    [InlineData("Nope", true, CommandOutcome.UnknownTag)]
    [InlineData("CV001.Speed", 1.0, CommandOutcome.ReadOnly)]
    [InlineData("CV001.Start", 1.0, CommandOutcome.KindMismatch)]
    [InlineData("Feed.Rate", 25.0, CommandOutcome.OutOfRange)]
    [InlineData("Feed.Rate", -0.5, CommandOutcome.OutOfRange)]
    [InlineData("Feed.Rate", double.NaN, CommandOutcome.OutOfRange)]
    public void RejectedCommandsNeverReachTheWriter(string tag, object raw, CommandOutcome expected)
    {
        var writer = new RecordingWriter(Directory());
        var bus = new CommandBus(writer);
        TagValue value = raw is bool b ? TagValue.Bool(b) : TagValue.Double((double)raw);

        Assert.Equal(expected, bus.Write(tag, value));

        Assert.Empty(writer.Writes);
        Assert.Equal((0L, 1L), (bus.Accepted, bus.Rejected));
    }

    [Fact]
    public void AnUnrangedDoubleAcceptsAnyFiniteValue()
    {
        var writer = new RecordingWriter(Directory());
        var bus = new CommandBus(writer);

        Assert.Equal(CommandOutcome.Accepted, bus.WriteDouble("Feed.Bias", -1e6));
        Assert.Equal(CommandOutcome.OutOfRange, bus.WriteDouble("Feed.Bias", double.PositiveInfinity));
    }

    [Fact]
    public void TheRecorderSeesEveryCommandWithItsOutcome()
    {
        var recorder = new Recorder();
        var bus = new CommandBus(new RecordingWriter(Directory()), recorder);

        bus.WriteBool("CV001.Start", true);
        bus.WriteDouble("CV001.Speed", 1.0);

        Assert.Equal(
            new[] { ("CV001.Start", CommandOutcome.Accepted), ("CV001.Speed", CommandOutcome.ReadOnly) },
            recorder.Commands.Select(c => (c.Tag, c.Outcome)));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Dse.Realtime.Tests --filter "FullyQualifiedName~DispatcherThreadTests|FullyQualifiedName~CommandBusTests"`
Expected: build FAILS — `DispatcherThread`, `CommandBus` do not exist.

- [ ] **Step 3: Write the dispatcher**

`src/Dse.Realtime/DispatcherThread.cs`:

```csharp
namespace Dse.Realtime;

/// <summary>
/// A background thread that pumps a <see cref="RealtimeHub"/> whenever frames
/// arrive (spec 10.1). Dispose stops it, pumps once more so nothing published
/// before the stop is stranded, and joins. Dispose the dispatcher before the hub.
/// </summary>
public sealed class DispatcherThread : IDisposable
{
    private readonly RealtimeHub _hub;
    private readonly Thread _thread;
    private readonly TimeSpan _idleWait;
    private volatile bool _stop;
    private volatile bool _running;
    private bool _started;
    private long _pumped;
    private Exception? _failure;

    /// <summary>Creates a dispatcher for <paramref name="hub"/>; call <see cref="Start"/> to run it.</summary>
    public DispatcherThread(RealtimeHub hub, string name = "dse-dispatcher", TimeSpan? idleWait = null)
    {
        ArgumentNullException.ThrowIfNull(hub);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        _hub = hub;
        _idleWait = idleWait ?? TimeSpan.FromMilliseconds(100);
        if (_idleWait <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(idleWait), _idleWait, "The idle wait must be positive.");
        }

        _thread = new Thread(Loop) { Name = name, IsBackground = true };
    }

    /// <summary>True while the loop is running.</summary>
    public bool IsRunning => _running;

    /// <summary>Frames pumped by this thread.</summary>
    public long PumpedFrames => Volatile.Read(ref _pumped);

    /// <summary>The exception that ended the loop early, or null.</summary>
    public Exception? Failure => Volatile.Read(ref _failure);

    /// <summary>Starts the thread. Throws if already started.</summary>
    public void Start()
    {
        if (_started)
        {
            throw new InvalidOperationException("The dispatcher has already been started.");
        }

        _started = true;
        _thread.Start();
    }

    /// <summary>Stops the loop, drains the ring once more, and joins.</summary>
    public void Dispose()
    {
        _stop = true;
        if (_started && _thread.IsAlive)
        {
            _thread.Join();
        }
    }

    private void Loop()
    {
        _running = true;
        try
        {
            while (!_stop)
            {
                _hub.FramesAvailable.WaitOne(_idleWait);
                Interlocked.Add(ref _pumped, _hub.Pump());
            }

            Interlocked.Add(ref _pumped, _hub.Pump());
        }
        catch (Exception ex)
        {
            Volatile.Write(ref _failure, ex);
        }
        finally
        {
            _running = false;
        }
    }
}
```

- [ ] **Step 4: Write the command bus**

`src/Dse.Realtime/CommandOutcome.cs`:

```csharp
namespace Dse.Realtime;

/// <summary>What the command bus decided about a write (spec 10.6).</summary>
public enum CommandOutcome
{
    /// <summary>Queued for phase 1 of the next tick.</summary>
    Accepted,

    /// <summary>No tag of that name.</summary>
    UnknownTag,

    /// <summary>The value's kind does not match the tag's.</summary>
    KindMismatch,

    /// <summary>The tag does not accept writes.</summary>
    ReadOnly,

    /// <summary>A double outside the tag's declared range, or not finite.</summary>
    OutOfRange,
}
```

`src/Dse.Realtime/TagCommand.cs`:

```csharp
using Dse.Io;

namespace Dse.Realtime;

/// <summary>One inbound write and what became of it.</summary>
public sealed record TagCommand(string Tag, TagValue Value, CommandOutcome Outcome);
```

`src/Dse.Realtime/ICommandRecorder.cs`:

```csharp
namespace Dse.Realtime;

/// <summary>
/// Receives every command the bus handles, accepted or rejected. Plan 5's
/// scenario recorder attaches here. Called on the writer's thread; must be
/// thread-safe if the bus is shared.
/// </summary>
public interface ICommandRecorder
{
    /// <summary>Records one command.</summary>
    void Record(TagCommand command);
}
```

`src/Dse.Realtime/CommandBus.cs`:

```csharp
using Dse.Io;

namespace Dse.Realtime;

/// <summary>
/// The inbound channel (spec 10.6): validates a write against the tag
/// directory and, if it is sound, queues it through the simulation's
/// <see cref="ITagWriter"/> for phase 1 of the next tick. Never throws on bad
/// input — remote callers send junk — and reports every command to the
/// optional recorder.
/// </summary>
public sealed class CommandBus
{
    private readonly ITagWriter _writer;
    private readonly ICommandRecorder? _recorder;
    private long _accepted;
    private long _rejected;

    /// <summary>Creates a bus over <paramref name="writer"/>, validating against its directory.</summary>
    public CommandBus(ITagWriter writer, ICommandRecorder? recorder = null)
    {
        ArgumentNullException.ThrowIfNull(writer);
        _writer = writer;
        _recorder = recorder;
    }

    /// <summary>The directory commands are validated against.</summary>
    public ITagDirectory Directory => _writer.Directory;

    /// <summary>Commands forwarded to the writer.</summary>
    public long Accepted => Volatile.Read(ref _accepted);

    /// <summary>Commands refused.</summary>
    public long Rejected => Volatile.Read(ref _rejected);

    /// <summary>Validates and forwards one write.</summary>
    public CommandOutcome Write(string tag, TagValue value)
    {
        ArgumentNullException.ThrowIfNull(tag);

        CommandOutcome outcome = Validate(tag, value, out TagDescriptor? descriptor);
        if (outcome == CommandOutcome.Accepted)
        {
            _writer.Write(descriptor!.Index, value);
            Interlocked.Increment(ref _accepted);
        }
        else
        {
            Interlocked.Increment(ref _rejected);
        }

        _recorder?.Record(new TagCommand(tag, value, outcome));
        return outcome;
    }

    /// <summary>Validates and forwards a bool.</summary>
    public CommandOutcome WriteBool(string tag, bool value) => Write(tag, TagValue.Bool(value));

    /// <summary>Validates and forwards a double.</summary>
    public CommandOutcome WriteDouble(string tag, double value) => Write(tag, TagValue.Double(value));

    /// <summary>Validates and forwards a long.</summary>
    public CommandOutcome WriteInt64(string tag, long value) => Write(tag, TagValue.Int64(value));

    private CommandOutcome Validate(string tag, TagValue value, out TagDescriptor? descriptor)
    {
        if (!Directory.TryFind(tag, out TagDescriptor found))
        {
            descriptor = null;
            return CommandOutcome.UnknownTag;
        }

        descriptor = found;
        if (found.Access != TagAccess.ReadWrite)
        {
            return CommandOutcome.ReadOnly;
        }

        if (found.Kind != value.Kind)
        {
            return CommandOutcome.KindMismatch;
        }

        if (found.Kind == TagKind.Double)
        {
            double d = value.AsDouble;
            if (!double.IsFinite(d))
            {
                return CommandOutcome.OutOfRange;
            }

            if (found.HasRange && (d < found.RangeLow || d > found.RangeHigh))
            {
                return CommandOutcome.OutOfRange;
            }
        }

        return CommandOutcome.Accepted;
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/Dse.Realtime.Tests`
Expected: PASS, 48 tests. Run `DispatcherThreadTests` ten times in a row with
`--no-build`; all green. `dotnet build -c Release` — 0 warnings.

- [ ] **Step 6: Commit**

```bash
git add src/Dse.Realtime tests/Dse.Realtime.Tests
git commit -m "feat(realtime): add the dispatcher thread and the validated command bus"
```

---

### Task 13: The conveyor over the wire, and the docs

**Files:**
- Test: `tests/Dse.Realtime.Tests/ConveyorRealtimeTests.cs`
- Modify: `docs/architecture.md` (two new sections)
- Modify: `README.md` (status paragraph)

**Interfaces:**
- Consumes: everything above; `Dse.Components.Conveyors.Conveyor` and the
  plant shape from `ConveyorIoTests` (Task 8).
- Produces: the end-to-end proof that spec 10 holds against a real plant, and
  the documentation the repository standards (spec 19) require.

This is the plan's causal-chain test in the way plan 3's conveyor test was:
an HMI-style Conflate subscriber with a percent deadband and 100 ms
decimation, a historian-style Lossless subscriber, a command bus, and a
conveyor started, run and pull-keyed through them only. Frames are pumped
synchronously here (the pluggable-pump decision); the dispatcher thread has
its own tests in Task 12.

- [ ] **Step 1: Write the failing test**

`tests/Dse.Realtime.Tests/ConveyorRealtimeTests.cs`:

```csharp
using Dse.Components.Conveyors;
using Dse.Components.Flow;
using Dse.Components.Mechanical;
using Dse.Core;
using Dse.Core.Flow;
using Dse.Core.Time;
using Dse.Io;

namespace Dse.Realtime.Tests;

/// <summary>A conveyor operated and observed exclusively through the real-time layer.</summary>
public class ConveyorRealtimeTests
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

    private sealed class Line : IDisposable
    {
        public Line()
        {
            var feed = new BulkSource("Feed", Ore, 20.0, new MaterialProperties(2000.0, 0.03, 15.0));
            var conveyor = new Conveyor("CV001", Cv001);
            var chute = new TransferChute("Chute", capacityKg: 200.0);
            var pile = new BulkSink("Pile");
            feed.Out.ConnectTo(conveyor.Inlet("In"));
            conveyor.Outlet("Out").ConnectTo(chute.In);
            chute.Out.ConnectTo(pile.In);

            Sim = new SimulationBuilder(new SimulationOptions
            {
                Seed = 1UL,
                StartTime = new DateTimeOffset(2026, 1, 1, 6, 0, 0, TimeSpan.Zero),
                TimeStep = TimeSpan.FromMilliseconds(10),
            }).Add(pile).Add(chute).Add(conveyor).Add(feed).Build();

            Hub = new RealtimeHub(Sim.IO.Directory, ringCapacity: 1 << 14);
            Sim.AttachFrameSink(Hub);
            Bus = new CommandBus(Sim.IO);
            Hmi = Hub.Subscribe(new SubscriptionOptions
            {
                Policy = BackpressurePolicy.Conflate,
                Prefixes = ["CV001"],
                DeadbandPercentOfRange = 0.5,
                Decimation = TimeSpan.FromMilliseconds(100),
            });
            Historian = Hub.Subscribe(new SubscriptionOptions { Policy = BackpressurePolicy.Lossless, Capacity = 1 << 16 });
        }

        public Simulation Sim { get; }

        public RealtimeHub Hub { get; }

        public CommandBus Bus { get; }

        public Subscription Hmi { get; }

        public Subscription Historian { get; }

        /// <summary>Runs simulation time, pumping every 100 ms of it as a dispatcher would.</summary>
        public void Run(double seconds)
        {
            int slices = (int)Math.Round(seconds * 10.0);
            for (int i = 0; i < slices; i++)
            {
                Sim.RunFor(TimeSpan.FromMilliseconds(100));
                Hub.Pump();
            }
        }

        public void StartUp()
        {
            Assert.Equal(CommandOutcome.Accepted, Bus.WriteBool("CV001.SafetyReset", true));
            Sim.RunFor(TimeSpan.FromMilliseconds(50));
            Assert.Equal(CommandOutcome.Accepted, Bus.WriteBool("CV001.SafetyReset", false));
            Assert.Equal(CommandOutcome.Accepted, Bus.WriteBool("CV001.Start", true));
        }

        public static List<FrameDelta> Drain(Subscription sub)
        {
            var deltas = new List<FrameDelta>();
            while (sub.TryRead(out FrameDelta delta))
            {
                deltas.Add(delta);
            }

            return deltas;
        }

        public void Dispose()
        {
            Hmi.Dispose();
            Historian.Dispose();
            Hub.Dispose();
        }
    }

    [Fact]
    public void TheLiveStateReportsTheRunningConveyor()
    {
        using var line = new Line();
        line.StartUp();

        line.Run(40.0);

        LiveState state = line.Hub.State;
        Assert.Equal(line.Sim.Clock.TickCount - 1, state.Tick);
        Assert.True(state.Get("CV001.Contactor").Value.AsBool);
        Assert.False(state.Get("CV001.Tripped").Value.AsBool);
        Assert.InRange(state.Get("CV001.Speed").Value.AsDouble, 1.75, 1.95);
        Assert.InRange(state.Get("CV001.TonnesPerHour").Value.AsDouble, 68.0, 76.0);
        Assert.True(state.Get("CV001.Speed").Quality.IsGood);
        Assert.True(state.Get("CV001.Contactor").LastChangeTick > 0);
        Assert.Equal(0L, line.Hub.DroppedFrames);
        Assert.Contains(state.Snapshot().RecentEvents, e => e.Code == "AT_SPEED");
    }

    [Fact]
    public void TheHistorianSeesEveryTickAndEveryEventInOrder()
    {
        using var line = new Line();
        line.StartUp();
        line.Run(40.0);

        List<FrameDelta> deltas = Line.Drain(line.Historian);

        Assert.False(line.Historian.IsFaulted);
        Assert.NotEmpty(deltas);
        for (int i = 1; i < deltas.Count; i++)
        {
            Assert.True(deltas[i].Tick > deltas[i - 1].Tick);
        }

        IEnumerable<DiscreteEvent> events = deltas.SelectMany(d => d.Events);
        Assert.Contains(events, e => e.Source == "CV001.Start" && e.Code == "WRITE");
        Assert.Contains(events, e => e.Source == "CV001.Safety" && e.Code == "SAFETY_RESET");
        Assert.Contains(events, e => e.Source == "CV001.Motor" && e.Code == "AT_SPEED");
        int speedIndex = line.Sim.IO.Directory.Find("CV001.Speed").Index;
        Assert.True(deltas.Count(d => d.Changes.Any(c => c.Index == speedIndex)) > 1000);
    }

    [Fact]
    public void TheHmiGetsADecimatedDeadbandedConveyorOnlyView()
    {
        using var line = new Line();
        line.StartUp();
        line.Run(40.0);

        List<FrameDelta> deltas = Line.Drain(line.Hmi);

        Assert.False(line.Hmi.IsFaulted);
        // Conflate: everything since the last read is one delta.
        FrameDelta delta = Assert.Single(deltas);
        Assert.All(delta.Changes, c => Assert.StartsWith("CV001.", line.Sim.IO.Directory[c.Index].Name, StringComparison.Ordinal));
        int speedIndex = line.Sim.IO.Directory.Find("CV001.Speed").Index;
        Assert.InRange(delta.Changes.Single(c => c.Index == speedIndex).Value.AsDouble, 1.75, 1.95);

        // Read continuously for two more seconds: at most one delta per 100 ms of sim time.
        int reads = 0;
        for (int i = 0; i < 20; i++)
        {
            line.Sim.RunFor(TimeSpan.FromMilliseconds(100));
            line.Hub.Pump();
            reads += Line.Drain(line.Hmi).Count;
        }

        Assert.InRange(reads, 0, 20);
    }

    [Fact]
    public void APullKeyThroughTheBusStopsTheBeltAndTheStateShowsIt()
    {
        using var line = new Line();
        line.StartUp();
        line.Run(30.0);

        Assert.Equal(CommandOutcome.Accepted, line.Bus.WriteBool("CV001.PullKey1", true));
        line.Run(20.0);

        LiveState state = line.Hub.State;
        Assert.False(state.Get("CV001.SafetyOk").Value.AsBool);
        Assert.False(state.Get("CV001.Contactor").Value.AsBool);
        Assert.True(state.Get("CV001.Stopped").Value.AsBool);
        Assert.Contains(Line.Drain(line.Historian).SelectMany(d => d.Events), e => e.Code == "SAFETY_TRIP");
    }

    [Fact]
    public void TheBusRefusesWhatTheDirectoryForbids()
    {
        using var line = new Line();

        Assert.Equal(CommandOutcome.ReadOnly, line.Bus.WriteBool("CV001.Contactor", true));
        Assert.Equal(CommandOutcome.KindMismatch, line.Bus.WriteDouble("CV001.Start", 1.0));
        Assert.Equal(CommandOutcome.UnknownTag, line.Bus.WriteBool("CV001.Nope", true));
        Assert.Equal(0, line.Sim.IO.PendingWrites);
    }

    [Fact]
    public void FiftyConsumersDoNotChangeTheRun()
    {
        static double[] Run(int subscribers)
        {
            using var line = new Line();
            var subs = new List<Subscription>();
            for (int i = 0; i < subscribers; i++)
            {
                subs.Add(line.Hub.Subscribe(new SubscriptionOptions { Policy = BackpressurePolicy.Lossless, Capacity = 1 << 16 }));
            }

            line.StartUp();
            line.Run(20.0);
            double[] result =
            [
                line.Sim.IO.ReadDouble("CV001.Speed"),
                line.Sim.IO.ReadDouble("CV001.TonnesPerHour"),
                line.Sim.IO.ReadDouble("CV001.Current"),
                line.Sim.Telemetry.Read("CV001.Motor.Current"),
            ];
            subs.ForEach(s => s.Dispose());
            return result;
        }

        Assert.Equal(Run(0), Run(50));
    }
}
```

The conveyor plant has no ranged writable tag, so `OutOfRange` is not
exercised here; `CommandBusTests` covers it.

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/Dse.Realtime.Tests --filter "FullyQualifiedName~ConveyorRealtimeTests"`
Expected: FAIL only if an earlier task is incomplete; with Tasks 1–12 merged
this compiles and must pass. If `Sim.AttachFrameSink` does not exist, Task 6
was not finished — stop and report.

- [ ] **Step 3: Run it and record the numbers**

Run: `dotnet test tests/Dse.Realtime.Tests --filter "FullyQualifiedName~ConveyorRealtimeTests"`
Expected: PASS, 6 tests. Report the measured `Speed`, `TonnesPerHour`, the
historian delta count and the HMI `reads` value. Do not widen a window; if one
fails, the layer is wrong.

- [ ] **Step 4: Write the docs**

Append to `docs/architecture.md`, after "Telemetry versus instrumentation":

```markdown
## The I/O image

Tags are the plant's front door. A leaf component that has something a plant
would measure or command implements `ITagProvider` and returns `TagBinding`s
with names relative to itself — a belt scale declares `Value` with the unit and
range from its spec and its `Health` port as the quality source; a starter
declares `Command` and `Reset` as writable and `Contactor` and `Tripped` as
read-only. The builder prefixes each name with the component id. A composite's
`Expose(alias, port)` renames the tag to `CompositeId.Alias`, applied
inside-out so the outermost alias wins, which is how `CV001.Start` and
`CV001.TonnesPerHour` arise. `SimulationBuilder.Bind` adds a tag for a port
nothing declared, or replaces a declared one. Every port has at most one
binding; names are unique; the directory is sorted by name and printable with
`ToText()`.

Truth stays telemetry. The motor, gearbox, pulleys and belts declare no tags: a
SCADA that could read true motor current would not need the current sensor.

A writable tag drives an `InputPort` from outside. If the plant already wires
an output into that input, the declared tag degrades to read-only (the tag
observes the command instead of issuing it); an explicit `Bind` of a writable
tag on a driven input is validation error `DSE010`.

`Simulation.IO` is the image. Reads (`Read`, `ReadDouble`, `Handle<T>` …) see
the snapshot published at the last tick's phase 4, from any thread, without a
lock: the array is written once, swapped with a volatile write and never
touched again. Writes queue lock-free and land at phase 1 of the next tick, in
enqueue order, before scheduled events; each is logged as `WRITE` from the tag
name, so the write timeline is in the event stream. Every tag value carries a
`TagQuality`: instruments report `Uncertain:OutOfRange` when the unclamped
reading leaves their range and `Bad:SensorFailure` under fail-high or fail-low.

Phase 5 wraps the published array, its dirty mask and the tick's event-log
records in an immutable `TickFrame` and hands it to the attached
`ITickFrameSink` in one non-blocking call. Nothing downstream of that call can
affect the run.

## The real-time boundary

`Dse.Realtime` references only `Dse.Io.Abstractions`. It cannot see the model,
which is what makes a protocol adapter a genuine bolt-on.

`RealtimeHub` is the frame sink. `Publish` is a single-producer enqueue into a
ring; when the ring is full the incoming frame is dropped and counted, and the
simulation thread never waits. `Pump` — called by `DispatcherThread` in
production, directly in tests — drains the ring into `LiveState` (current
value, quality and last-change tick per tag, plus recent events) and then
offers each frame to every `Subscription`. `Subscribe` captures the state
snapshot and registers the subscription under the pump lock, so no frame can
fall between them: deltas start at the tick after the snapshot.

A subscription diffs each frame against the values it last delivered, filtered
by tag prefix and deadband (absolute per tag, or a percent of the directory
range), and optionally decimated to one delta per interval of simulation time
with the frames in between merged. Under `Conflate` it keeps the latest value
per tag and never faults; under `Lossless` it queues every delta and faults at
capacity or on a ring gap rather than drop an event. Consumers pull with
`TryRead` or wait on `Available`; a slow consumer stalls nothing.

Inbound writes take their own channel: `CommandBus` validates a command against
the directory — unknown tag, read-only, wrong kind, outside the declared range —
forwards accepted ones to `ITagWriter`, and reports every command to an
optional `ICommandRecorder`, which is where a scenario recorder attaches.
```

In `README.md`, replace the sentence "The I/O and real-time layers,
declarative configuration and the reference samples are planned." with:

```markdown
It also contains the I/O layer (a declared, printable tag directory with
units, ranges and per-tag quality; a lock-free double-buffered image any thread
may read; queued writes that land at phase 1 of the next tick; one immutable
`TickFrame` per tick) and the in-process real-time layer (`Dse.Realtime`: a
ring-buffered hub, a live state engine for late joiners, subscriptions with
prefix filters, deadbands, decimation and declared backpressure, and a
validated command bus), which references the I/O contract only.

Declarative configuration, scenarios and replay, the control blocks, the CLI
and the reference samples are planned.
```

And add `the I/O and real-time layers` to the first paragraph's list where it
says "the first component library" — a clause, not a rewrite.

- [ ] **Step 5: Run everything**

Run: `dotnet build -c Release` then `dotnet test`
Expected: 0 warnings; all five test projects green. Record the total test count.

- [ ] **Step 6: Commit**

```bash
git add tests/Dse.Realtime.Tests/ConveyorRealtimeTests.cs docs/architecture.md README.md
git commit -m "test(realtime): operate the conveyor through the hub and command bus, and document the I/O and real-time layers"
```

---

## Definition of done for this plan

- `dotnet build -c Release` — 0 warnings; `dotnet test` — every project green.
- `Dse.Realtime.csproj` references `Dse.Io.Abstractions` only.
- Spec 9: tag directory with direction, unit, range, description (`ToText()`);
  string and handle APIs; snapshot reads, queued writes at phase 1; per-tag
  quality with `Uncertain` in use; the thread-safety contract holds under the
  two threaded tests.
- Spec 10: `TickFrame` at phase 5 by one non-blocking enqueue; event engine
  with per-subscriber deadbands, decimation and both backpressure policies;
  live state engine with snapshot for late joiners; no-gap subscribe; command
  bus validating against the directory with a recorder seam; frames carry sim
  time only.
- Spec 5.4: the graph is immutable after `Build()`.
- R20–R27 recorded in this file and reflected in `docs/architecture.md`.

## What this plan deliberately does not build

- Alarm state in the live state engine (spec 10.3) — waits for the alarm block
  in plan 5, which defines what an alarm is.
- Scenario recording of writes and commands (spec 10.6 "records into the active
  scenario") — plan 5; the seams are the `WRITE` event records and
  `ICommandRecorder`.
- Any protocol adapter, `Dse.Realtime.Http`, OPC UA, MQTT, Modbus (spec 18).
- Frame and mask pooling. Every tick allocates; at a hundred tags that is well
  under 2 KB per tick and is not worth a lifetime rule yet.
- Raw-count scaling (spec 18).
- `EventLog.ToText()` format changes — parked until plan 5's first golden file.
