# Simulation Core Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the deterministic simulation core — clock, RNG, event queue, port
graph, composites, resolver, validation, lifecycle, telemetry, event log and
runner — so that a small plant of toy components runs and replays bit-identically.

**Architecture:** Components are leaves with typed ports that read inputs, update
their own state, and write outputs, never touching a sibling. A builder flattens
composites into one ordered array, topologically sorted at validation time; the
graph is immutable afterwards. A fixed-step clock derives simulation time by
multiplication, and each component draws from its own RNG stream so that editing
a plant never shifts another component's randomness.

**Tech Stack:** .NET 10 (`net10.0`), C#, xUnit. No external runtime dependencies.

**Spec:** `docs/superpowers/specs/2026-09-02-industrial-process-simulation-engine-design.md`
(sections 4, 5, 6; plus the event log from section 13 and repository standards
from section 19)

**Plan sequence:** This is plan 1 of 6. Later plans add flow and materials, the
component library, I/O and the real-time layer, catalogue/config/CLI, and the
reference samples. Nothing in this plan may reference those subsystems.

## Global Constraints

- Target framework `net10.0` for every project.
- `Millrace.Core` and `Millrace.Io.Abstractions` have **zero external runtime package
  references**. Test projects may reference test packages.
- `Nullable` enabled, `TreatWarningsAsErrors` true, deterministic builds.
- **Never use `System.Random`** — its algorithm is not contractually stable
  across .NET versions. Use `DeterministicRandom` from Task 2.
- **Never use `string.GetHashCode()`** for anything that affects simulation
  behaviour — it is randomised per process. Use `Hash64.OfString`.
- **Never iterate a `Dictionary` or `HashSet` in per-tick code.** Enumeration
  order is not part of the contract. Resolve to arrays at build time.
- All formatting and parsing uses `CultureInfo.InvariantCulture`.
- Licence: MIT.
- Namespace root: `Millrace`.

## File Structure

```
Directory.Build.props                       shared build settings for all projects
Millrace.sln
LICENSE                                     MIT
.github/workflows/ci.yml                    build + test on push

src/Millrace.Io.Abstractions/
  Placeholder.cs                            (populated by plan 4; project exists now
                                             so the dependency graph is fixed early)

src/Millrace.Core/
  Randomness/Hash64.cs                      stable 64-bit string hash + mixer
  Randomness/DeterministicRandom.cs         SplitMix64 stream, uniform + gaussian
  Time/SimulationClock.cs                   tick count, derived simulation time
  Time/SimulationOptions.cs                 seed, start time, time step
  Events/ISimEvent.cs                       event payload contract + CallbackEvent
  Events/EventQueue.cs                      (dueTick, sequence) ordered min-heap
  Telemetry/TelemetryRegistry.cs            named channels, flat array storage
  Telemetry/TelemetryHandle.cs              write handle, no lookup per tick
  Logging/SimEventRecord.cs                 one discrete event
  Logging/EventLog.cs                       ordered log + stable text format
  Graph/Port.cs                             port base: name, owner, connection state
  Graph/InputPort.cs                        single-source input with default
  Graph/OutputPort.cs                       fan-out output
  Graph/ISimNode.cs                         anything the builder accepts
  Graph/ISimComponent.cs                    evaluatable leaf
  Graph/ComponentBase.cs                    port registration + id qualification
  Graph/CompositeComponent.cs               children, aliases, flattening
  Graph/IQualifiable.cs                     internal id-prefixing contract
  Graph/UnitDelay.cs                        one-tick delay, breaks algebraic loops
  Graph/GraphResolver.cs                    topological sort + cycle reporting
  Validation/ValidationError.cs             code, message, component ids
  Validation/ValidationResult.cs            errors + IsValid
  Validation/SimulationValidationException.cs
  Contexts/InitContext.cs                   per-component RNG + telemetry registration
  Contexts/TickContext.cs                   tick, dt, sim time, event logging
  Simulation.cs                             lifecycle + the five tick phases
  SimulationBuilder.cs                      add, validate, build
  SimulationRunner.cs                       execution modes and pacing

tests/Millrace.Core.Tests/
  (one test file per source area, named <Area>Tests.cs)
  Fakes/ConstantSource.cs                   test-only components
  Fakes/Gain.cs
  Fakes/Integrator.cs
  Fakes/NoiseSource.cs
  Fakes/Recorder.cs
```

Test-only components live in `tests/`, never in `src/`. `Millrace.Core` ships no
industrial or example components — those arrive in plan 3.

---

### Task 1: Repository scaffolding

**Files:**
- Create: `Directory.Build.props`
- Create: `Millrace.sln`
- Create: `src/Millrace.Io.Abstractions/Millrace.Io.Abstractions.csproj`, `src/Millrace.Io.Abstractions/Placeholder.cs`
- Create: `src/Millrace.Core/Millrace.Core.csproj`
- Create: `tests/Millrace.Core.Tests/Millrace.Core.Tests.csproj`
- Create: `LICENSE`, `.github/workflows/ci.yml`
- Test: `tests/Millrace.Core.Tests/ScaffoldingTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: a solution where `dotnet test` runs. Later tasks add files to
  `src/Millrace.Core` and `tests/Millrace.Core.Tests` without touching project files.

- [ ] **Step 1: Create the projects with the SDK templates**

Use the templates rather than hand-written csproj files so package versions match
the installed SDK.

```bash
# from the repository root
dotnet new sln -n Millrace
dotnet new classlib -n Millrace.Io.Abstractions -o src/Millrace.Io.Abstractions -f net10.0
dotnet new classlib -n Millrace.Core -o src/Millrace.Core -f net10.0
dotnet new xunit -n Millrace.Core.Tests -o tests/Millrace.Core.Tests -f net10.0
rm -f src/Millrace.Io.Abstractions/Class1.cs src/Millrace.Core/Class1.cs
dotnet sln add src/Millrace.Io.Abstractions src/Millrace.Core tests/Millrace.Core.Tests
dotnet add src/Millrace.Core reference src/Millrace.Io.Abstractions
dotnet add tests/Millrace.Core.Tests reference src/Millrace.Core
```

- [ ] **Step 2: Add shared build settings**

`Directory.Build.props`:

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
    <Deterministic>true</Deterministic>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
    <!-- Public XML docs are encouraged but must not block a red-green cycle. -->
    <NoWarn>$(NoWarn);CS1591</NoWarn>
  </PropertyGroup>
</Project>
```

The template csproj files each set `TargetFramework` themselves; that is
harmless duplication and can be left alone.

- [ ] **Step 3: Add the placeholder so the empty project compiles**

`src/Millrace.Io.Abstractions/Placeholder.cs`:

```csharp
namespace Millrace.Io;

/// <summary>
/// Reserved. The I/O contract lands in plan 4; this project exists now so the
/// dependency direction (Core depends on Io.Abstractions, never the reverse) is
/// fixed from the first commit.
/// </summary>
internal static class Placeholder
{
}
```

- [ ] **Step 4: Write the scaffolding test**

`tests/Millrace.Core.Tests/ScaffoldingTests.cs`:

```csharp
using System.Reflection;
using Xunit;

namespace Millrace.Core.Tests;

public class ScaffoldingTests
{
    [Fact]
    public void CoreAssemblyIsReferenceable()
    {
        Assembly core = typeof(Millrace.Io.Placeholder).Assembly;
        Assert.Equal("Millrace.Io.Abstractions", core.GetName().Name);
    }
}
```

`Placeholder` is `internal`, so add `InternalsVisibleTo` to
`src/Millrace.Io.Abstractions/Millrace.Io.Abstractions.csproj` inside a new `ItemGroup`:

```xml
  <ItemGroup>
    <InternalsVisibleTo Include="Millrace.Core.Tests" />
  </ItemGroup>
```

- [ ] **Step 5: Run the test**

Run: `dotnet test`
Expected: PASS, 1 test.

- [ ] **Step 6: Add the licence and CI workflow**

`LICENSE`: the standard MIT licence text, copyright holder `Millrace contributors`,
year `2026`.

`.github/workflows/ci.yml`:

```yaml
name: ci
on:
  push:
  pull_request:
jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'
      - run: dotnet build --configuration Release
      - run: dotnet test --configuration Release --no-build
```

- [ ] **Step 7: Verify the release build is clean**

Run: `dotnet build --configuration Release`
Expected: Build succeeded, 0 warnings, 0 errors.

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -m "chore: scaffold solution, projects, CI and licence"
```

---

### Task 2: Deterministic hashing and RNG

**Files:**
- Create: `src/Millrace.Core/Randomness/Hash64.cs`
- Create: `src/Millrace.Core/Randomness/DeterministicRandom.cs`
- Test: `tests/Millrace.Core.Tests/RandomnessTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `static ulong Hash64.OfString(string value)`
  - `static ulong Hash64.Mix(ulong z)`
  - `static ulong Hash64.Combine(ulong seed, string id)`
  - `DeterministicRandom(ulong seed)`, `ulong NextUInt64()`,
    `double NextDouble()`, `double NextGaussian()`

The expected values below are FNV-1a-64 and SplitMix64 outputs, computed
independently. They are the regression guard proving nobody swapped in
`string.GetHashCode()` or `System.Random`.

- [ ] **Step 1: Write the failing tests**

`tests/Millrace.Core.Tests/RandomnessTests.cs`:

```csharp
using Millrace.Core.Randomness;
using Xunit;

namespace Millrace.Core.Tests;

public class RandomnessTests
{
    [Fact]
    public void StringHashMatchesKnownFnv1a64Vectors()
    {
        Assert.Equal(18295790033315820519UL, Hash64.OfString("CV001"));
        Assert.Equal(18295791132827448730UL, Hash64.OfString("CV002"));
    }

    [Fact]
    public void CombineMatchesKnownVector()
    {
        Assert.Equal(10340090026681282474UL, Hash64.Combine(492781UL, "CV001"));
    }

    [Fact]
    public void StreamMatchesKnownSplitMix64Vectors()
    {
        var random = new DeterministicRandom(0UL);

        Assert.Equal(16294208416658607535UL, random.NextUInt64());
        Assert.Equal(7960286522194355700UL, random.NextUInt64());
        Assert.Equal(487617019471545679UL, random.NextUInt64());
    }

    [Fact]
    public void NextDoubleMatchesKnownVectorAndStaysInRange()
    {
        Assert.Equal(0.8833108082136426, new DeterministicRandom(0UL).NextDouble(), 15);

        var random = new DeterministicRandom(12345UL);
        for (int i = 0; i < 10_000; i++)
        {
            double value = random.NextDouble();
            Assert.InRange(value, 0.0, 0.9999999999999999);
        }
    }

    [Fact]
    public void SameSeedProducesSameSequence()
    {
        var a = new DeterministicRandom(99UL);
        var b = new DeterministicRandom(99UL);

        for (int i = 0; i < 100; i++)
        {
            Assert.Equal(a.NextUInt64(), b.NextUInt64());
        }
    }

    [Fact]
    public void GaussianIsDeterministicAndRoughlyStandardNormal()
    {
        var a = new DeterministicRandom(7UL);
        var b = new DeterministicRandom(7UL);
        Assert.Equal(a.NextGaussian(), b.NextGaussian());

        var random = new DeterministicRandom(7UL);
        double sum = 0.0;
        double sumOfSquares = 0.0;
        const int n = 100_000;
        for (int i = 0; i < n; i++)
        {
            double value = random.NextGaussian();
            sum += value;
            sumOfSquares += value * value;
        }

        double mean = sum / n;
        double variance = (sumOfSquares / n) - (mean * mean);
        Assert.InRange(mean, -0.02, 0.02);
        Assert.InRange(variance, 0.95, 1.05);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter FullyQualifiedName~RandomnessTests`
Expected: FAIL — `Millrace.Core.Randomness` does not exist.

- [ ] **Step 3: Implement the hash**

`src/Millrace.Core/Randomness/Hash64.cs`:

```csharp
using System.Text;

namespace Millrace.Core.Randomness;

/// <summary>
/// Stable 64-bit hashing. Unlike <see cref="string.GetHashCode()"/> these values
/// are identical in every process and every run, which is what makes
/// per-component seed derivation reproducible.
/// </summary>
public static class Hash64
{
    private const ulong FnvOffsetBasis = 14695981039346656037UL;
    private const ulong FnvPrime = 1099511628211UL;

    /// <summary>FNV-1a over the UTF-8 bytes of <paramref name="value"/>.</summary>
    public static ulong OfString(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        ulong hash = FnvOffsetBasis;
        foreach (byte b in Encoding.UTF8.GetBytes(value))
        {
            unchecked
            {
                hash ^= b;
                hash *= FnvPrime;
            }
        }

        return hash;
    }

    /// <summary>The SplitMix64 finaliser. Avalanches a counter into a usable value.</summary>
    public static ulong Mix(ulong z)
    {
        unchecked
        {
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
    }

    /// <summary>Derives a component's stream seed from the master seed and its id.</summary>
    public static ulong Combine(ulong seed, string id) => Mix(seed ^ OfString(id));
}
```

- [ ] **Step 4: Implement the generator**

`src/Millrace.Core/Randomness/DeterministicRandom.cs`:

```csharp
namespace Millrace.Core.Randomness;

/// <summary>
/// SplitMix64. Small, fast, and — unlike <see cref="System.Random"/> — a fixed
/// algorithm whose output will not change with a runtime upgrade.
/// </summary>
public sealed class DeterministicRandom
{
    private const ulong GoldenGamma = 0x9E3779B97F4A7C15UL;

    private ulong _state;
    private double _spareGaussian;
    private bool _hasSpareGaussian;

    public DeterministicRandom(ulong seed) => _state = seed;

    public ulong NextUInt64()
    {
        unchecked
        {
            _state += GoldenGamma;
        }

        return Hash64.Mix(_state);
    }

    /// <summary>Uniform in [0, 1). Uses the top 53 bits, the full mantissa of a double.</summary>
    public double NextDouble() => (NextUInt64() >> 11) * (1.0 / 9007199254740992.0);

    /// <summary>Standard normal, via the polar Box-Muller method.</summary>
    public double NextGaussian()
    {
        if (_hasSpareGaussian)
        {
            _hasSpareGaussian = false;
            return _spareGaussian;
        }

        double u, v, s;
        do
        {
            u = (2.0 * NextDouble()) - 1.0;
            v = (2.0 * NextDouble()) - 1.0;
            s = (u * u) + (v * v);
        }
        while (s >= 1.0 || s == 0.0);

        double scale = Math.Sqrt(-2.0 * Math.Log(s) / s);
        _spareGaussian = v * scale;
        _hasSpareGaussian = true;
        return u * scale;
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test --filter FullyQualifiedName~RandomnessTests`
Expected: PASS, 6 tests.

- [ ] **Step 6: Commit**

```bash
git add src/Millrace.Core/Randomness tests/Millrace.Core.Tests/RandomnessTests.cs
git commit -m "feat(core): add deterministic hashing and SplitMix64 generator"
```

---

### Task 3: Simulation clock

**Files:**
- Create: `src/Millrace.Core/Time/SimulationClock.cs`
- Create: `src/Millrace.Core/Time/SimulationOptions.cs`
- Test: `tests/Millrace.Core.Tests/SimulationClockTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `SimulationClock(DateTimeOffset startTime, TimeSpan timeStep)` with
    `long TickCount`, `TimeSpan TimeStep`, `double DeltaSeconds`,
    `DateTimeOffset StartTime`, `DateTimeOffset Now`, `TimeSpan Elapsed`,
    `void Advance()`
  - `SimulationOptions` with `ulong Seed`, `DateTimeOffset StartTime`,
    `TimeSpan TimeStep` (default 10 ms)

- [ ] **Step 1: Write the failing tests**

`tests/Millrace.Core.Tests/SimulationClockTests.cs`:

```csharp
using Millrace.Core.Time;
using Xunit;

namespace Millrace.Core.Tests;

public class SimulationClockTests
{
    private static readonly DateTimeOffset Start =
        new(2026, 1, 1, 6, 0, 0, TimeSpan.Zero);

    [Fact]
    public void StartsAtTickZero()
    {
        var clock = new SimulationClock(Start, TimeSpan.FromMilliseconds(10));

        Assert.Equal(0, clock.TickCount);
        Assert.Equal(Start, clock.Now);
        Assert.Equal(TimeSpan.Zero, clock.Elapsed);
    }

    [Fact]
    public void ExposesDeltaInSeconds()
    {
        var clock = new SimulationClock(Start, TimeSpan.FromMilliseconds(10));

        Assert.Equal(0.01, clock.DeltaSeconds, 12);
    }

    [Fact]
    public void OneHundredTicksOfTenMillisecondsIsExactlyOneSecond()
    {
        var clock = new SimulationClock(Start, TimeSpan.FromMilliseconds(10));

        for (int i = 0; i < 100; i++)
        {
            clock.Advance();
        }

        Assert.Equal(Start.AddSeconds(1), clock.Now);
    }

    [Fact]
    public void DoesNotDriftOverAMillionTicks()
    {
        var clock = new SimulationClock(Start, TimeSpan.FromMilliseconds(10));

        for (int i = 0; i < 1_000_000; i++)
        {
            clock.Advance();
        }

        // 1,000,000 x 10 ms = 10,000 s exactly, with no accumulated error.
        Assert.Equal(Start.AddSeconds(10_000), clock.Now);
        Assert.Equal(TimeSpan.FromSeconds(10_000), clock.Elapsed);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void RejectsNonPositiveTimeStep(int milliseconds)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new SimulationClock(Start, TimeSpan.FromMilliseconds(milliseconds)));
    }

    [Fact]
    public void OptionsDefaultToTenMillisecondStep()
    {
        var options = new SimulationOptions();

        Assert.Equal(TimeSpan.FromMilliseconds(10), options.TimeStep);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter FullyQualifiedName~SimulationClockTests`
Expected: FAIL — `Millrace.Core.Time` does not exist.

- [ ] **Step 3: Implement the clock and options**

`src/Millrace.Core/Time/SimulationClock.cs`:

```csharp
namespace Millrace.Core.Time;

/// <summary>
/// Fixed-step simulation clock. Time is derived from the tick count by
/// multiplication and never accumulated, because accumulating a step drifts and
/// drift breaks replay.
/// </summary>
public sealed class SimulationClock
{
    public SimulationClock(DateTimeOffset startTime, TimeSpan timeStep)
    {
        if (timeStep <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeStep), timeStep, "The time step must be positive.");
        }

        StartTime = startTime;
        TimeStep = timeStep;
        DeltaSeconds = timeStep.TotalSeconds;
    }

    public DateTimeOffset StartTime { get; }

    public TimeSpan TimeStep { get; }

    /// <summary>The time step in seconds, precomputed for use inside Evaluate.</summary>
    public double DeltaSeconds { get; }

    public long TickCount { get; private set; }

    public TimeSpan Elapsed => TimeSpan.FromTicks(TimeStep.Ticks * TickCount);

    public DateTimeOffset Now => StartTime + Elapsed;

    public void Advance() => TickCount++;
}
```

`src/Millrace.Core/Time/SimulationOptions.cs`:

```csharp
namespace Millrace.Core.Time;

/// <summary>The complete set of inputs that determine a simulation's results.</summary>
public sealed class SimulationOptions
{
    /// <summary>Master seed. Every component's stream is derived from this.</summary>
    public ulong Seed { get; init; }

    public DateTimeOffset StartTime { get; init; } =
        new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public TimeSpan TimeStep { get; init; } = TimeSpan.FromMilliseconds(10);
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter FullyQualifiedName~SimulationClockTests`
Expected: PASS, 7 tests (the theory counts twice).

- [ ] **Step 5: Commit**

```bash
git add src/Millrace.Core/Time tests/Millrace.Core.Tests/SimulationClockTests.cs
git commit -m "feat(core): add fixed-step simulation clock and options"
```

---

### Task 4: Event queue

**Files:**
- Create: `src/Millrace.Core/Events/ISimEvent.cs`
- Create: `src/Millrace.Core/Events/EventQueue.cs`
- Test: `tests/Millrace.Core.Tests/EventQueueTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `interface ISimEvent { void Apply(); }`
  - `sealed class CallbackEvent(Action callback) : ISimEvent`
  - `readonly record struct ScheduledEvent(long DueTick, long Sequence, ISimEvent Payload)`
  - `EventQueue` with `long Schedule(long dueTick, ISimEvent payload)`,
    `bool TryDequeueDue(long tick, out ScheduledEvent scheduled)`, `int Count`

`Apply()` takes no argument in this plan. Plan 5 replaces `CallbackEvent` with
serialisable scenario actions; `ISimEvent` is the seam that makes that additive.

- [ ] **Step 1: Write the failing tests**

`tests/Millrace.Core.Tests/EventQueueTests.cs`:

```csharp
using Millrace.Core.Events;
using Xunit;

namespace Millrace.Core.Tests;

public class EventQueueTests
{
    private static CallbackEvent Named(List<string> sink, string name) =>
        new(() => sink.Add(name));

    [Fact]
    public void DequeuesInDueTickOrderRegardlessOfScheduleOrder()
    {
        var fired = new List<string>();
        var queue = new EventQueue();

        queue.Schedule(30, Named(fired, "third"));
        queue.Schedule(10, Named(fired, "first"));
        queue.Schedule(20, Named(fired, "second"));

        DrainThrough(queue, 30);

        Assert.Equal(new[] { "first", "second", "third" }, fired);
    }

    [Fact]
    public void EventsOnTheSameTickFireInScheduleOrder()
    {
        var fired = new List<string>();
        var queue = new EventQueue();

        queue.Schedule(5, Named(fired, "a"));
        queue.Schedule(5, Named(fired, "b"));
        queue.Schedule(5, Named(fired, "c"));

        DrainThrough(queue, 5);

        Assert.Equal(new[] { "a", "b", "c" }, fired);
    }

    [Fact]
    public void OverdueEventsFireOnTheNextDrain()
    {
        var fired = new List<string>();
        var queue = new EventQueue();
        queue.Schedule(3, Named(fired, "missed"));

        // Nothing drained at tick 3; drain at tick 9 must still deliver it.
        DrainThrough(queue, 9);

        Assert.Equal(new[] { "missed" }, fired);
    }

    [Fact]
    public void DoesNotDequeueEventsThatAreNotYetDue()
    {
        var queue = new EventQueue();
        queue.Schedule(100, new CallbackEvent(() => { }));

        Assert.False(queue.TryDequeueDue(99, out _));
        Assert.Equal(1, queue.Count);
    }

    [Fact]
    public void SequenceNumbersAreMonotonic()
    {
        var queue = new EventQueue();

        long first = queue.Schedule(1, new CallbackEvent(() => { }));
        long second = queue.Schedule(1, new CallbackEvent(() => { }));

        Assert.True(second > first);
    }

    [Fact]
    public void RejectsNegativeDueTick()
    {
        var queue = new EventQueue();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => queue.Schedule(-1, new CallbackEvent(() => { })));
    }

    private static void DrainThrough(EventQueue queue, long tick)
    {
        while (queue.TryDequeueDue(tick, out ScheduledEvent scheduled))
        {
            scheduled.Payload.Apply();
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter FullyQualifiedName~EventQueueTests`
Expected: FAIL — `Millrace.Core.Events` does not exist.

- [ ] **Step 3: Implement the event contract**

`src/Millrace.Core/Events/ISimEvent.cs`:

```csharp
namespace Millrace.Core.Events;

/// <summary>Something that happens at a specific tick.</summary>
public interface ISimEvent
{
    void Apply();
}

/// <summary>An event that runs a delegate. Not serialisable; scenarios (plan 5) supply their own.</summary>
public sealed class CallbackEvent : ISimEvent
{
    private readonly Action _callback;

    public CallbackEvent(Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        _callback = callback;
    }

    public void Apply() => _callback();
}

/// <summary>An event with its ordering key. Sequence breaks ties within a tick.</summary>
public readonly record struct ScheduledEvent(long DueTick, long Sequence, ISimEvent Payload);
```

- [ ] **Step 4: Implement the queue**

`src/Millrace.Core/Events/EventQueue.cs`:

```csharp
namespace Millrace.Core.Events;

/// <summary>
/// A binary min-heap ordered by (due tick, sequence). The sequence number is
/// what makes simultaneous events reproducible: two events due on the same tick
/// always fire in the order they were scheduled.
/// </summary>
public sealed class EventQueue
{
    private readonly List<ScheduledEvent> _heap = [];
    private long _nextSequence;

    public int Count => _heap.Count;

    /// <summary>Schedules an event and returns its sequence number.</summary>
    public long Schedule(long dueTick, ISimEvent payload)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(dueTick);
        ArgumentNullException.ThrowIfNull(payload);

        var scheduled = new ScheduledEvent(dueTick, _nextSequence++, payload);
        _heap.Add(scheduled);
        SiftUp(_heap.Count - 1);
        return scheduled.Sequence;
    }

    /// <summary>
    /// Removes and returns the earliest event due at or before <paramref name="tick"/>.
    /// Events that became due while nothing was draining are still delivered.
    /// </summary>
    public bool TryDequeueDue(long tick, out ScheduledEvent scheduled)
    {
        if (_heap.Count == 0 || _heap[0].DueTick > tick)
        {
            scheduled = default;
            return false;
        }

        scheduled = _heap[0];
        int last = _heap.Count - 1;
        _heap[0] = _heap[last];
        _heap.RemoveAt(last);
        if (_heap.Count > 0)
        {
            SiftDown(0);
        }

        return true;
    }

    private static bool IsBefore(in ScheduledEvent a, in ScheduledEvent b) =>
        a.DueTick != b.DueTick ? a.DueTick < b.DueTick : a.Sequence < b.Sequence;

    private void SiftUp(int index)
    {
        while (index > 0)
        {
            int parent = (index - 1) / 2;
            if (!IsBefore(_heap[index], _heap[parent]))
            {
                return;
            }

            (_heap[index], _heap[parent]) = (_heap[parent], _heap[index]);
            index = parent;
        }
    }

    private void SiftDown(int index)
    {
        while (true)
        {
            int left = (2 * index) + 1;
            int right = left + 1;
            int smallest = index;

            if (left < _heap.Count && IsBefore(_heap[left], _heap[smallest]))
            {
                smallest = left;
            }

            if (right < _heap.Count && IsBefore(_heap[right], _heap[smallest]))
            {
                smallest = right;
            }

            if (smallest == index)
            {
                return;
            }

            (_heap[index], _heap[smallest]) = (_heap[smallest], _heap[index]);
            index = smallest;
        }
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test --filter FullyQualifiedName~EventQueueTests`
Expected: PASS, 6 tests.

- [ ] **Step 6: Commit**

```bash
git add src/Millrace.Core/Events tests/Millrace.Core.Tests/EventQueueTests.cs
git commit -m "feat(core): add deterministically ordered event queue"
```

---

### Task 5: Telemetry registry

**Files:**
- Create: `src/Millrace.Core/Telemetry/TelemetryRegistry.cs`
- Create: `src/Millrace.Core/Telemetry/TelemetryHandle.cs`
- Test: `tests/Millrace.Core.Tests/TelemetryTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `readonly struct TelemetryHandle` with `void Write(double value)`
  - `TelemetryRegistry` with `TelemetryHandle Register(string key, string unit)`,
    `double Read(string key)`, `bool TryRead(string key, out double value)`,
    `IReadOnlyList<TelemetryChannel> Channels`
  - `sealed record TelemetryChannel(string Key, string Unit)`

Telemetry is the god view described in spec section 6.5: the true internal value,
free of sensor noise. Instruments come in plan 3 and are a different thing.

- [ ] **Step 1: Write the failing tests**

`tests/Millrace.Core.Tests/TelemetryTests.cs`:

```csharp
using Millrace.Core.Telemetry;
using Xunit;

namespace Millrace.Core.Tests;

public class TelemetryTests
{
    [Fact]
    public void WritesAreReadableByKey()
    {
        var registry = new TelemetryRegistry();
        TelemetryHandle speed = registry.Register("CV001.Belt.Speed", "m/s");

        speed.Write(3.2);

        Assert.Equal(3.2, registry.Read("CV001.Belt.Speed"));
    }

    [Fact]
    public void ChannelsStartAtZero()
    {
        var registry = new TelemetryRegistry();
        registry.Register("CV001.Belt.Speed", "m/s");

        Assert.Equal(0.0, registry.Read("CV001.Belt.Speed"));
    }

    [Fact]
    public void ChannelsAreListedInRegistrationOrderWithUnits()
    {
        var registry = new TelemetryRegistry();
        registry.Register("B", "kg");
        registry.Register("A", "m/s");

        Assert.Equal(new[] { "B", "A" }, registry.Channels.Select(c => c.Key));
        Assert.Equal(new[] { "kg", "m/s" }, registry.Channels.Select(c => c.Unit));
    }

    [Fact]
    public void DuplicateKeysAreRejected()
    {
        var registry = new TelemetryRegistry();
        registry.Register("A", "m/s");

        InvalidOperationException error =
            Assert.Throws<InvalidOperationException>(() => registry.Register("A", "m/s"));
        Assert.Contains("A", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadingAnUnknownKeyThrowsAndTryReadReportsFalse()
    {
        var registry = new TelemetryRegistry();

        Assert.Throws<KeyNotFoundException>(() => registry.Read("nope"));
        Assert.False(registry.TryRead("nope", out double value));
        Assert.Equal(0.0, value);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter FullyQualifiedName~TelemetryTests`
Expected: FAIL — `Millrace.Core.Telemetry` does not exist.

- [ ] **Step 3: Implement the handle**

`src/Millrace.Core/Telemetry/TelemetryHandle.cs`:

```csharp
namespace Millrace.Core.Telemetry;

/// <summary>
/// A write handle for one telemetry channel. Resolved once during Initialize so
/// that writing during a tick costs an array store and no dictionary lookup.
/// </summary>
public readonly struct TelemetryHandle
{
    private readonly TelemetryRegistry? _registry;
    private readonly int _index;

    internal TelemetryHandle(TelemetryRegistry registry, int index)
    {
        _registry = registry;
        _index = index;
    }

    public void Write(double value) => _registry?.WriteAt(_index, value);
}
```

A `default` handle writes nowhere. That lets a component hold an unassigned
handle without a null check on every tick.

- [ ] **Step 4: Implement the registry**

`src/Millrace.Core/Telemetry/TelemetryRegistry.cs`:

```csharp
namespace Millrace.Core.Telemetry;

public sealed record TelemetryChannel(string Key, string Unit);

/// <summary>
/// The god view: true internal values published by components, unaffected by
/// sensor noise, drift or failure. Tests assert on these; controllers must not.
/// </summary>
public sealed class TelemetryRegistry
{
    private readonly List<TelemetryChannel> _channels = [];
    private readonly Dictionary<string, int> _indexByKey = new(StringComparer.Ordinal);
    private double[] _values = new double[16];

    public IReadOnlyList<TelemetryChannel> Channels => _channels;

    public TelemetryHandle Register(string key, string unit)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(unit);

        if (_indexByKey.ContainsKey(key))
        {
            throw new InvalidOperationException(
                $"Telemetry key '{key}' is already registered. Keys must be unique.");
        }

        int index = _channels.Count;
        _channels.Add(new TelemetryChannel(key, unit));
        _indexByKey[key] = index;

        if (index >= _values.Length)
        {
            Array.Resize(ref _values, _values.Length * 2);
        }

        return new TelemetryHandle(this, index);
    }

    public double Read(string key)
    {
        if (!_indexByKey.TryGetValue(key, out int index))
        {
            throw new KeyNotFoundException($"No telemetry channel named '{key}'.");
        }

        return _values[index];
    }

    public bool TryRead(string key, out double value)
    {
        if (_indexByKey.TryGetValue(key, out int index))
        {
            value = _values[index];
            return true;
        }

        value = 0.0;
        return false;
    }

    internal void WriteAt(int index, double value) => _values[index] = value;
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test --filter FullyQualifiedName~TelemetryTests`
Expected: PASS, 5 tests.

- [ ] **Step 6: Commit**

```bash
git add src/Millrace.Core/Telemetry tests/Millrace.Core.Tests/TelemetryTests.cs
git commit -m "feat(core): add telemetry registry and write handles"
```

---

### Task 6: Event log

**Files:**
- Create: `src/Millrace.Core/Logging/SimEventRecord.cs`
- Create: `src/Millrace.Core/Logging/EventLog.cs`
- Test: `tests/Millrace.Core.Tests/EventLogTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `sealed record SimEventRecord(long Tick, DateTimeOffset SimTime, string Source, string Code, string Message)`
  - `EventLog` with
    `void Record(long tick, DateTimeOffset simTime, string source, string code, string message)`,
    `IReadOnlyList<SimEventRecord> Records`, `string ToText()`

`ToText()` is the golden-file format for regression tests. Its shape is a
contract: changing it invalidates every committed golden file.

- [ ] **Step 1: Write the failing tests**

`tests/Millrace.Core.Tests/EventLogTests.cs`:

```csharp
using Millrace.Core.Logging;
using Xunit;

namespace Millrace.Core.Tests;

public class EventLogTests
{
    private static readonly DateTimeOffset Start =
        new(2026, 1, 1, 6, 32, 11, TimeSpan.Zero);

    [Fact]
    public void RecordsArePreservedInOrder()
    {
        var log = new EventLog();

        log.Record(1, Start, "CV001", "STARTED", "Conveyor started");
        log.Record(2, Start.AddSeconds(7), "CV001", "AT_SPEED", "Reached speed");

        Assert.Equal(2, log.Records.Count);
        Assert.Equal("STARTED", log.Records[0].Code);
        Assert.Equal("AT_SPEED", log.Records[1].Code);
        Assert.Equal(2, log.Records[1].Tick);
    }

    [Fact]
    public void TextFormatIsStableAndCultureInvariant()
    {
        var log = new EventLog();
        log.Record(1, Start, "CV001", "STARTED", "Conveyor started");
        log.Record(760, Start.AddSeconds(7.5), "CV001", "AT_SPEED", "Reached speed");

        string expected =
            "06:32:11.000  CV001  STARTED  Conveyor started" + Environment.NewLine +
            "06:32:18.500  CV001  AT_SPEED  Reached speed" + Environment.NewLine;

        Assert.Equal(expected, log.ToText());
    }

    [Fact]
    public void EmptyLogProducesEmptyText()
    {
        Assert.Equal(string.Empty, new EventLog().ToText());
    }

    [Fact]
    public void RejectsBlankSourceOrCode()
    {
        var log = new EventLog();

        Assert.Throws<ArgumentException>(() => log.Record(1, Start, " ", "CODE", "m"));
        Assert.Throws<ArgumentException>(() => log.Record(1, Start, "SRC", "", "m"));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter FullyQualifiedName~EventLogTests`
Expected: FAIL — `Millrace.Core.Logging` does not exist.

- [ ] **Step 3: Implement the record and log**

`src/Millrace.Core/Logging/SimEventRecord.cs`:

```csharp
namespace Millrace.Core.Logging;

/// <summary>One discrete thing that happened, at one tick.</summary>
public sealed record SimEventRecord(
    long Tick,
    DateTimeOffset SimTime,
    string Source,
    string Code,
    string Message);
```

`src/Millrace.Core/Logging/EventLog.cs`:

```csharp
using System.Globalization;
using System.Text;

namespace Millrace.Core.Logging;

/// <summary>
/// The ordered log of discrete events. This is the primary regression artifact:
/// two runs of the same scenario must produce byte-identical text.
/// </summary>
public sealed class EventLog
{
    private readonly List<SimEventRecord> _records = [];

    public IReadOnlyList<SimEventRecord> Records => _records;

    public void Record(long tick, DateTimeOffset simTime, string source, string code, string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentNullException.ThrowIfNull(message);

        _records.Add(new SimEventRecord(tick, simTime, source, code, message));
    }

    /// <summary>
    /// The golden-file format. Stable by contract — changing it invalidates every
    /// committed expected-output file.
    /// </summary>
    public string ToText()
    {
        var builder = new StringBuilder();
        foreach (SimEventRecord record in _records)
        {
            builder.Append(record.SimTime.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture))
                   .Append("  ").Append(record.Source)
                   .Append("  ").Append(record.Code)
                   .Append("  ").Append(record.Message)
                   .Append(Environment.NewLine);
        }

        return builder.ToString();
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter FullyQualifiedName~EventLogTests`
Expected: PASS, 4 tests.

- [ ] **Step 5: Commit**

```bash
git add src/Millrace.Core/Logging tests/Millrace.Core.Tests/EventLogTests.cs
git commit -m "feat(core): add ordered event log with stable text format"
```

---

### Task 7: Ports

**Files:**
- Create: `src/Millrace.Core/Graph/Port.cs`
- Create: `src/Millrace.Core/Graph/OutputPort.cs`
- Create: `src/Millrace.Core/Graph/InputPort.cs`
- Test: `tests/Millrace.Core.Tests/PortTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `abstract class Port` with `string Name`, `string OwnerId`,
    `bool IsMissingRequiredConnection`, `internal Port? SourcePort`
  - `sealed class OutputPort<T> : Port where T : unmanaged` with `T Value { get; set; }`,
    `void ConnectTo(InputPort<T> input)`
  - `sealed class InputPort<T> : Port where T : unmanaged` with `T Value { get; }`,
    `T DefaultValue`, `bool IsRequired`, `void ConnectFrom(OutputPort<T> source)`

An input has exactly one source. Connecting a second one throws immediately at
wiring time rather than deferring to validation — the stack trace at the point of
the mistake is worth far more than a later error list.

- [ ] **Step 1: Write the failing tests**

`tests/Millrace.Core.Tests/PortTests.cs`:

```csharp
using Millrace.Core.Graph;
using Xunit;

namespace Millrace.Core.Tests;

public class PortTests
{
    [Fact]
    public void UnconnectedInputReturnsItsDefault()
    {
        var input = new InputPort<double>("Speed", "M1", defaultValue: 1.5, isRequired: false);

        Assert.Equal(1.5, input.Value);
    }

    [Fact]
    public void ConnectedInputTracksItsSource()
    {
        var output = new OutputPort<double>("Out", "A");
        var input = new InputPort<double>("In", "B", defaultValue: 0.0, isRequired: false);

        output.ConnectTo(input);
        output.Value = 42.0;

        Assert.Equal(42.0, input.Value);
    }

    [Fact]
    public void OneOutputMayDriveManyInputs()
    {
        var output = new OutputPort<bool>("Out", "A");
        var first = new InputPort<bool>("In", "B", defaultValue: false, isRequired: false);
        var second = new InputPort<bool>("In", "C", defaultValue: false, isRequired: false);

        output.ConnectTo(first);
        output.ConnectTo(second);
        output.Value = true;

        Assert.True(first.Value);
        Assert.True(second.Value);
    }

    [Fact]
    public void ConnectingASecondSourceToAnInputThrows()
    {
        var first = new OutputPort<double>("Out", "A");
        var second = new OutputPort<double>("Out", "B");
        var input = new InputPort<double>("In", "C", defaultValue: 0.0, isRequired: false);

        first.ConnectTo(input);

        InvalidOperationException error =
            Assert.Throws<InvalidOperationException>(() => second.ConnectTo(input));
        Assert.Contains("C.In", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RequiredUnconnectedInputIsFlagged()
    {
        var required = new InputPort<double>("In", "A", defaultValue: 0.0, isRequired: true);
        var optional = new InputPort<double>("In", "B", defaultValue: 0.0, isRequired: false);
        var output = new OutputPort<double>("Out", "C");

        Assert.True(required.IsMissingRequiredConnection);
        Assert.False(optional.IsMissingRequiredConnection);
        Assert.False(output.IsMissingRequiredConnection);

        output.ConnectTo(required);
        Assert.False(required.IsMissingRequiredConnection);
    }

    [Fact]
    public void SourcePortExposesTheUpstreamPortForTheResolver()
    {
        var output = new OutputPort<double>("Out", "A");
        var input = new InputPort<double>("In", "B", defaultValue: 0.0, isRequired: false);

        Assert.Null(input.SourcePort);
        output.ConnectTo(input);
        Assert.Same(output, input.SourcePort);
    }
}
```

`SourcePort` is `internal`; add `InternalsVisibleTo` for the test project in
Step 3.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter FullyQualifiedName~PortTests`
Expected: FAIL — `Millrace.Core.Graph` does not exist.

- [ ] **Step 3: Implement the port base**

`src/Millrace.Core/Graph/Port.cs`:

```csharp
namespace Millrace.Core.Graph;

/// <summary>A named connection point on a component.</summary>
public abstract class Port
{
    protected Port(string name, string ownerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);

        Name = name;
        OwnerId = ownerId;
    }

    public string Name { get; }

    /// <summary>The owning component's id. Reassigned when a composite qualifies its children.</summary>
    public string OwnerId { get; internal set; }

    /// <summary>Fully qualified port name, used in validation messages.</summary>
    public string QualifiedName => $"{OwnerId}.{Name}";

    /// <summary>True when this is a required input with nothing driving it.</summary>
    public abstract bool IsMissingRequiredConnection { get; }

    /// <summary>The upstream port, or null. Used by the resolver to build edges.</summary>
    internal abstract Port? SourcePort { get; }
}
```

Add `InternalsVisibleTo` to `src/Millrace.Core/Millrace.Core.csproj`:

```xml
  <ItemGroup>
    <InternalsVisibleTo Include="Millrace.Core.Tests" />
  </ItemGroup>
```

- [ ] **Step 4: Implement the output port**

`src/Millrace.Core/Graph/OutputPort.cs`:

```csharp
namespace Millrace.Core.Graph;

/// <summary>An output. May drive any number of inputs.</summary>
public sealed class OutputPort<T> : Port
    where T : unmanaged
{
    public OutputPort(string name, string ownerId)
        : base(name, ownerId)
    {
    }

    public T Value { get; set; }

    public override bool IsMissingRequiredConnection => false;

    internal override Port? SourcePort => null;

    public void ConnectTo(InputPort<T> input)
    {
        ArgumentNullException.ThrowIfNull(input);
        input.ConnectFrom(this);
    }
}
```

- [ ] **Step 5: Implement the input port**

`src/Millrace.Core/Graph/InputPort.cs`:

```csharp
namespace Millrace.Core.Graph;

/// <summary>
/// An input. Exactly one source, because two sources is an undefined value.
/// Unconnected inputs read their declared default so partial plants still run.
/// </summary>
public sealed class InputPort<T> : Port
    where T : unmanaged
{
    private OutputPort<T>? _source;

    public InputPort(string name, string ownerId, T defaultValue, bool isRequired)
        : base(name, ownerId)
    {
        DefaultValue = defaultValue;
        IsRequired = isRequired;
    }

    public T DefaultValue { get; }

    public bool IsRequired { get; }

    public T Value => _source is null ? DefaultValue : _source.Value;

    public override bool IsMissingRequiredConnection => IsRequired && _source is null;

    internal override Port? SourcePort => _source;

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

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test --filter FullyQualifiedName~PortTests`
Expected: PASS, 6 tests.

- [ ] **Step 7: Commit**

```bash
git add src/Millrace.Core/Graph src/Millrace.Core/Millrace.Core.csproj tests/Millrace.Core.Tests/PortTests.cs
git commit -m "feat(core): add typed signal ports with single-source inputs"
```

---

### Task 8: Components and contexts

**Files:**
- Create: `src/Millrace.Core/Graph/ISimNode.cs`
- Create: `src/Millrace.Core/Graph/ISimComponent.cs`
- Create: `src/Millrace.Core/Graph/IQualifiable.cs`
- Create: `src/Millrace.Core/Graph/ComponentBase.cs`
- Create: `src/Millrace.Core/Contexts/InitContext.cs`
- Create: `src/Millrace.Core/Contexts/TickContext.cs`
- Create: `tests/Millrace.Core.Tests/Fakes/ConstantSource.cs`, `Gain.cs`, `Integrator.cs`, `Recorder.cs`
- Test: `tests/Millrace.Core.Tests/ComponentTests.cs`

**Interfaces:**
- Consumes: `Port`, `InputPort<T>`, `OutputPort<T>` (Task 7),
  `DeterministicRandom` (Task 2), `TelemetryRegistry`/`TelemetryHandle` (Task 5),
  `EventLog` (Task 6).
- Produces:
  - `interface ISimNode { string Id { get; } }`
  - `interface ISimComponent : ISimNode` with `IReadOnlyList<Port> Ports`,
    `bool HasDirectFeedthrough`, `void Initialize(in InitContext ctx)`,
    `void Evaluate(in TickContext ctx)`
  - `abstract class ComponentBase : ISimComponent` with protected
    `InputPort<T> AddInput<T>(string name, T defaultValue = default, bool required = false)`
    and `OutputPort<T> AddOutput<T>(string name)`
  - `readonly struct InitContext` with `DeterministicRandom Random`,
    `DateTimeOffset StartTime`, `double Dt`,
    `TelemetryHandle RegisterTelemetry(string name, string unit)`
  - `readonly struct TickContext` with `long Tick`, `double Dt`,
    `DateTimeOffset SimTime`, `void Log(string source, string code, string message)`
  - test fakes `ConstantSource`, `Gain`, `Integrator`, `Recorder`

`RegisterTelemetry` prefixes the component id automatically, so a `Gain` with id
`CV001.Gain` registering `"Out"` produces the key `CV001.Gain.Out`.

- [ ] **Step 1: Write the failing tests**

`tests/Millrace.Core.Tests/ComponentTests.cs`:

```csharp
using Millrace.Core.Contexts;
using Millrace.Core.Graph;
using Millrace.Core.Logging;
using Millrace.Core.Randomness;
using Millrace.Core.Telemetry;
using Millrace.Core.Tests.Fakes;
using Xunit;

namespace Millrace.Core.Tests;

public class ComponentTests
{
    private static readonly DateTimeOffset Start =
        new(2026, 1, 1, 6, 0, 0, TimeSpan.Zero);

    [Fact]
    public void PortsAreRegisteredInDeclarationOrderWithTheOwnerId()
    {
        var gain = new Gain("G1", factor: 2.0);

        Assert.Equal(new[] { "In", "Out" }, gain.Ports.Select(p => p.Name));
        Assert.All(gain.Ports, p => Assert.Equal("G1", p.OwnerId));
    }

    [Fact]
    public void ComponentsDefaultToDirectFeedthrough()
    {
        Assert.True(new Gain("G1", 2.0).HasDirectFeedthrough);
    }

    [Fact]
    public void EvaluateReadsInputsAndWritesOutputs()
    {
        var source = new ConstantSource("S1", 21.0);
        var gain = new Gain("G1", factor: 2.0);
        source.Out.ConnectTo(gain.In);

        TickContext tick = NewTickContext();
        source.Evaluate(tick);
        gain.Evaluate(tick);

        Assert.Equal(42.0, gain.Out.Value);
    }

    [Fact]
    public void IntegratorAccumulatesUsingTheTimeStep()
    {
        var source = new ConstantSource("S1", 2.0);
        var integrator = new Integrator("I1");
        source.Out.ConnectTo(integrator.In);

        for (long tick = 0; tick < 100; tick++)
        {
            TickContext context = NewTickContext(tick);
            source.Evaluate(context);
            integrator.Evaluate(context);
        }

        // 2.0 units/second integrated for 100 x 10 ms = 1 s.
        Assert.Equal(2.0, integrator.Out.Value, 9);
    }

    [Fact]
    public void RegisterTelemetryPrefixesTheComponentId()
    {
        var registry = new TelemetryRegistry();
        var gain = new Gain("CV001.Gain", factor: 3.0);

        gain.Initialize(NewInitContext(gain.Id, registry));

        Assert.Equal(new[] { "CV001.Gain.Out" }, registry.Channels.Select(c => c.Key));
    }

    [Fact]
    public void LoggingThroughTheTickContextLandsInTheEventLog()
    {
        var log = new EventLog();
        var context = new TickContext(5, 0.01, Start.AddSeconds(0.05), log);

        context.Log("G1", "NOTE", "hello");

        Assert.Single(log.Records);
        Assert.Equal("G1", log.Records[0].Source);
        Assert.Equal(5, log.Records[0].Tick);
    }

    [Fact]
    public void BlankComponentIdIsRejected()
    {
        Assert.Throws<ArgumentException>(() => new Gain(" ", 1.0));
    }

    private static TickContext NewTickContext(long tick = 0) =>
        new(tick, 0.01, Start + TimeSpan.FromMilliseconds(10 * tick), new EventLog());

    private static InitContext NewInitContext(string componentId, TelemetryRegistry registry) =>
        new(new DeterministicRandom(1UL), registry, componentId, Start, 0.01);
}
```

- [ ] **Step 2: Write the test fakes**

`tests/Millrace.Core.Tests/Fakes/ConstantSource.cs`:

```csharp
using Millrace.Core.Contexts;
using Millrace.Core.Graph;

namespace Millrace.Core.Tests.Fakes;

/// <summary>Emits a fixed value every tick.</summary>
public sealed class ConstantSource : ComponentBase
{
    private readonly double _value;

    public ConstantSource(string id, double value)
        : base(id)
    {
        _value = value;
        Out = AddOutput<double>("Out");
    }

    public OutputPort<double> Out { get; }

    public override void Evaluate(in TickContext ctx) => Out.Value = _value;
}
```

`tests/Millrace.Core.Tests/Fakes/Gain.cs`:

```csharp
using Millrace.Core.Contexts;
using Millrace.Core.Graph;
using Millrace.Core.Telemetry;

namespace Millrace.Core.Tests.Fakes;

/// <summary>Multiplies its input by a constant and publishes the result as telemetry.</summary>
public sealed class Gain : ComponentBase
{
    private readonly double _factor;
    private TelemetryHandle _outTelemetry;

    public Gain(string id, double factor)
        : base(id)
    {
        _factor = factor;
        In = AddInput<double>("In");
        Out = AddOutput<double>("Out");
    }

    public InputPort<double> In { get; }

    public OutputPort<double> Out { get; }

    public override void Initialize(in InitContext ctx) =>
        _outTelemetry = ctx.RegisterTelemetry("Out", "1");

    public override void Evaluate(in TickContext ctx)
    {
        Out.Value = In.Value * _factor;
        _outTelemetry.Write(Out.Value);
    }
}
```

`tests/Millrace.Core.Tests/Fakes/Integrator.cs`:

```csharp
using Millrace.Core.Contexts;
using Millrace.Core.Graph;

namespace Millrace.Core.Tests.Fakes;

/// <summary>Rectangular integration of its input over simulation time.</summary>
public sealed class Integrator : ComponentBase
{
    private double _accumulated;

    public Integrator(string id)
        : base(id)
    {
        In = AddInput<double>("In");
        Out = AddOutput<double>("Out");
    }

    public InputPort<double> In { get; }

    public OutputPort<double> Out { get; }

    public override void Evaluate(in TickContext ctx)
    {
        _accumulated += In.Value * ctx.Dt;
        Out.Value = _accumulated;
    }
}
```

`tests/Millrace.Core.Tests/Fakes/Recorder.cs`:

```csharp
using Millrace.Core.Contexts;
using Millrace.Core.Graph;

namespace Millrace.Core.Tests.Fakes;

/// <summary>Captures every value it sees, so a test can compare whole runs.</summary>
public sealed class Recorder : ComponentBase
{
    private readonly List<double> _samples = [];

    public Recorder(string id)
        : base(id) => In = AddInput<double>("In");

    public InputPort<double> In { get; }

    public IReadOnlyList<double> Samples => _samples;

    public override void Evaluate(in TickContext ctx) => _samples.Add(In.Value);
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test --filter FullyQualifiedName~ComponentTests`
Expected: FAIL — `ComponentBase` does not exist.

- [ ] **Step 4: Implement the node contracts**

`src/Millrace.Core/Graph/ISimNode.cs`:

```csharp
namespace Millrace.Core.Graph;

/// <summary>Anything the builder accepts: a leaf component or a composite.</summary>
public interface ISimNode
{
    string Id { get; }
}
```

`src/Millrace.Core/Graph/ISimComponent.cs`:

```csharp
using Millrace.Core.Contexts;

namespace Millrace.Core.Graph;

/// <summary>
/// An evaluatable leaf. Reads its inputs, updates its own state, writes its
/// outputs — and never touches another component. That restriction is what makes
/// topological ordering meaningful and components independently testable.
/// </summary>
public interface ISimComponent : ISimNode
{
    IReadOnlyList<Port> Ports { get; }

    /// <summary>
    /// True when outputs depend on inputs within the same tick. Components that
    /// return false (such as <see cref="UnitDelay{T}"/>) create no ordering edge
    /// and so may sit inside a feedback loop.
    /// </summary>
    bool HasDirectFeedthrough { get; }

    void Initialize(in InitContext ctx);

    void Evaluate(in TickContext ctx);
}
```

`src/Millrace.Core/Graph/IQualifiable.cs`:

```csharp
namespace Millrace.Core.Graph;

/// <summary>Lets a composite prepend its id to everything it contains.</summary>
internal interface IQualifiable
{
    void Qualify(string prefix);
}
```

- [ ] **Step 5: Implement ComponentBase**

`src/Millrace.Core/Graph/ComponentBase.cs`:

```csharp
using Millrace.Core.Contexts;

namespace Millrace.Core.Graph;

public abstract class ComponentBase : ISimComponent, IQualifiable
{
    private readonly List<Port> _ports = [];

    protected ComponentBase(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        Id = id;
    }

    public string Id { get; private set; }

    public IReadOnlyList<Port> Ports => _ports;

    public virtual bool HasDirectFeedthrough => true;

    protected InputPort<T> AddInput<T>(string name, T defaultValue = default, bool required = false)
        where T : unmanaged
    {
        var port = new InputPort<T>(name, Id, defaultValue, required);
        _ports.Add(port);
        return port;
    }

    protected OutputPort<T> AddOutput<T>(string name)
        where T : unmanaged
    {
        var port = new OutputPort<T>(name, Id);
        _ports.Add(port);
        return port;
    }

    public virtual void Initialize(in InitContext ctx)
    {
    }

    public abstract void Evaluate(in TickContext ctx);

    void IQualifiable.Qualify(string prefix)
    {
        Id = $"{prefix}.{Id}";
        foreach (Port port in _ports)
        {
            port.OwnerId = Id;
        }
    }
}
```

- [ ] **Step 6: Implement the contexts**

`src/Millrace.Core/Contexts/InitContext.cs`:

```csharp
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
        string componentId,
        DateTimeOffset startTime,
        double dt)
    {
        ArgumentNullException.ThrowIfNull(random);
        ArgumentNullException.ThrowIfNull(telemetry);
        ArgumentException.ThrowIfNullOrWhiteSpace(componentId);

        Random = random;
        StartTime = startTime;
        Dt = dt;
        _telemetry = telemetry;
        _componentId = componentId;
    }

    /// <summary>This component's own random stream, derived from the master seed and its id.</summary>
    public DeterministicRandom Random { get; }

    public DateTimeOffset StartTime { get; }

    public double Dt { get; }

    /// <summary>Registers a telemetry channel, prefixed with this component's id.</summary>
    public TelemetryHandle RegisterTelemetry(string name, string unit) =>
        _telemetry.Register($"{_componentId}.{name}", unit);
}
```

`src/Millrace.Core/Contexts/TickContext.cs`:

```csharp
using Millrace.Core.Logging;

namespace Millrace.Core.Contexts;

/// <summary>What a component is given on every tick.</summary>
public readonly struct TickContext
{
    private readonly EventLog _log;

    public TickContext(long tick, double dt, DateTimeOffset simTime, EventLog log)
    {
        ArgumentNullException.ThrowIfNull(log);

        Tick = tick;
        Dt = dt;
        SimTime = simTime;
        _log = log;
    }

    public long Tick { get; }

    /// <summary>The time step in seconds.</summary>
    public double Dt { get; }

    public DateTimeOffset SimTime { get; }

    /// <summary>Records a discrete event. Pass the component's own id as the source.</summary>
    public void Log(string source, string code, string message) =>
        _log.Record(Tick, SimTime, source, code, message);
}
```

- [ ] **Step 7: Run tests to verify they pass**

Run: `dotnet test --filter FullyQualifiedName~ComponentTests`
Expected: PASS, 7 tests.

- [ ] **Step 8: Commit**

```bash
git add src/Millrace.Core/Graph src/Millrace.Core/Contexts tests/Millrace.Core.Tests
git commit -m "feat(core): add component base, node contracts and tick contexts"
```

---

### Task 9: Composite components

**Files:**
- Create: `src/Millrace.Core/Graph/CompositeComponent.cs`
- Create: `tests/Millrace.Core.Tests/Fakes/TwoStage.cs`
- Test: `tests/Millrace.Core.Tests/CompositeComponentTests.cs`

**Interfaces:**
- Consumes: `ISimNode`, `ISimComponent`, `IQualifiable`, `ComponentBase`, `Port` (Tasks 7-8).
- Produces:
  - `abstract class CompositeComponent : ISimNode, IQualifiable` with protected
    `TChild AddChild<TChild>(TChild child) where TChild : ISimNode`,
    protected `void Expose(string alias, Port port)`,
    public `InputPort<T> Input<T>(string alias)`, `OutputPort<T> Output<T>(string alias)`,
    internal `IEnumerable<ISimComponent> Leaves()`
  - test fake `TwoStage`

A composite never evaluates. It owns children and exposes aliases of their ports,
so flattening collapses the tree to leaves and there is no hard-coded conveyor
anywhere in the engine.

- [ ] **Step 1: Write the failing tests**

`tests/Millrace.Core.Tests/CompositeComponentTests.cs`:

```csharp
using Millrace.Core.Graph;
using Millrace.Core.Tests.Fakes;
using Xunit;

namespace Millrace.Core.Tests;

public class CompositeComponentTests
{
    [Fact]
    public void ChildIdsAreQualifiedWithTheCompositeId()
    {
        var stage = new TwoStage("CV001", firstFactor: 2.0, secondFactor: 3.0);

        Assert.Equal(
            new[] { "CV001.First", "CV001.Second" },
            stage.Leaves().Select(c => c.Id));
    }

    [Fact]
    public void QualificationReachesPortOwnerIds()
    {
        var stage = new TwoStage("CV001", 2.0, 3.0);

        ISimComponent first = stage.Leaves().First();
        Assert.All(first.Ports, p => Assert.Equal("CV001.First", p.OwnerId));
    }

    [Fact]
    public void NestingQualifiesEveryDescendant()
    {
        var outer = new NestingComposite("Plant", new TwoStage("CV001", 2.0, 3.0));

        Assert.Equal(
            new[] { "Plant.CV001.First", "Plant.CV001.Second" },
            outer.Leaves().Select(c => c.Id));
    }

    [Fact]
    public void ExposedAliasesResolveToTheUnderlyingChildPorts()
    {
        var stage = new TwoStage("CV001", 2.0, 3.0);

        InputPort<double> input = stage.Input<double>("In");
        OutputPort<double> output = stage.Output<double>("Out");

        Assert.Equal("CV001.First.In", input.QualifiedName);
        Assert.Equal("CV001.Second.Out", output.QualifiedName);
    }

    [Fact]
    public void UnknownAliasThrowsWithTheAvailableNames()
    {
        var stage = new TwoStage("CV001", 2.0, 3.0);

        KeyNotFoundException error =
            Assert.Throws<KeyNotFoundException>(() => stage.Input<double>("Nope"));
        Assert.Contains("In", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AliasOfTheWrongTypeThrows()
    {
        var stage = new TwoStage("CV001", 2.0, 3.0);

        Assert.Throws<InvalidCastException>(() => stage.Input<bool>("In"));
    }

    private sealed class NestingComposite : CompositeComponent
    {
        public NestingComposite(string id, TwoStage inner)
            : base(id) => AddChild(inner);
    }
}
```

`tests/Millrace.Core.Tests/Fakes/TwoStage.cs`:

```csharp
using Millrace.Core.Graph;

namespace Millrace.Core.Tests.Fakes;

/// <summary>Two gains in series, exposed as a single composite.</summary>
public sealed class TwoStage : CompositeComponent
{
    public TwoStage(string id, double firstFactor, double secondFactor)
        : base(id)
    {
        Gain first = AddChild(new Gain("First", firstFactor));
        Gain second = AddChild(new Gain("Second", secondFactor));

        first.Out.ConnectTo(second.In);

        Expose("In", first.In);
        Expose("Out", second.Out);
    }
}
```

`Leaves()` is `internal`; the test project already has `InternalsVisibleTo`.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter FullyQualifiedName~CompositeComponentTests`
Expected: FAIL — `CompositeComponent` does not exist.

- [ ] **Step 3: Implement the composite**

`src/Millrace.Core/Graph/CompositeComponent.cs`:

```csharp
namespace Millrace.Core.Graph;

/// <summary>
/// A container of components. Composites never evaluate: at build time the tree
/// is flattened to leaves, so a conveyor is genuinely a composition rather than
/// a special case in the engine.
/// </summary>
public abstract class CompositeComponent : ISimNode, IQualifiable
{
    private readonly List<ISimNode> _children = [];
    private readonly Dictionary<string, Port> _aliases = new(StringComparer.Ordinal);

    protected CompositeComponent(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        Id = id;
    }

    public string Id { get; private set; }

    /// <summary>Adds a child and prefixes its id (and its descendants' ids) with this composite's id.</summary>
    protected TChild AddChild<TChild>(TChild child)
        where TChild : ISimNode
    {
        ArgumentNullException.ThrowIfNull(child);

        ((IQualifiable)child).Qualify(Id);
        _children.Add(child);
        return child;
    }

    /// <summary>Publishes a child's port under a name on this composite.</summary>
    protected void Expose(string alias, Port port)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(alias);
        ArgumentNullException.ThrowIfNull(port);

        if (!_aliases.TryAdd(alias, port))
        {
            throw new InvalidOperationException(
                $"Composite '{Id}' already exposes a port named '{alias}'.");
        }
    }

    public InputPort<T> Input<T>(string alias)
        where T : unmanaged => Resolve<InputPort<T>>(alias);

    public OutputPort<T> Output<T>(string alias)
        where T : unmanaged => Resolve<OutputPort<T>>(alias);

    /// <summary>Every evaluatable component beneath this one, depth-first in declaration order.</summary>
    internal IEnumerable<ISimComponent> Leaves()
    {
        foreach (ISimNode child in _children)
        {
            switch (child)
            {
                case ISimComponent component:
                    yield return component;
                    break;
                case CompositeComponent composite:
                    foreach (ISimComponent leaf in composite.Leaves())
                    {
                        yield return leaf;
                    }

                    break;
            }
        }
    }

    private TPort Resolve<TPort>(string alias)
        where TPort : Port
    {
        if (!_aliases.TryGetValue(alias, out Port? port))
        {
            string available = string.Join(", ", _aliases.Keys.Order(StringComparer.Ordinal));
            throw new KeyNotFoundException(
                $"Composite '{Id}' exposes no port named '{alias}'. Available: {available}.");
        }

        if (port is not TPort typed)
        {
            throw new InvalidCastException(
                $"Port '{alias}' on composite '{Id}' is a {port.GetType().Name}, not a {typeof(TPort).Name}.");
        }

        return typed;
    }

    void IQualifiable.Qualify(string prefix)
    {
        Id = $"{prefix}.{Id}";
        foreach (ISimNode child in _children)
        {
            ((IQualifiable)child).Qualify(prefix);
        }
    }
}
```

Note the qualification rule: a child's id already contains this composite's
old id, so prepending only the new prefix produces the right full path at any
depth.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter FullyQualifiedName~CompositeComponentTests`
Expected: PASS, 6 tests.

- [ ] **Step 5: Commit**

```bash
git add src/Millrace.Core/Graph/CompositeComponent.cs tests/Millrace.Core.Tests
git commit -m "feat(core): add composite components with port aliases and flattening"
```

---

### Task 10: Unit delay

**Files:**
- Create: `src/Millrace.Core/Graph/UnitDelay.cs`
- Test: `tests/Millrace.Core.Tests/UnitDelayTests.cs`

**Interfaces:**
- Consumes: `ComponentBase`, `InputPort<T>`, `OutputPort<T>`, `TickContext`.
- Produces:
  - `sealed class UnitDelay<T> : ComponentBase where T : unmanaged` with
    `InputPort<T> In`, `OutputPort<T> Out`,
    `UnitDelay(string id, T initialValue = default)`,
    `HasDirectFeedthrough => false`

- [ ] **Step 1: Write the failing tests**

`tests/Millrace.Core.Tests/UnitDelayTests.cs`:

```csharp
using Millrace.Core.Contexts;
using Millrace.Core.Graph;
using Millrace.Core.Logging;
using Millrace.Core.Tests.Fakes;
using Xunit;

namespace Millrace.Core.Tests;

public class UnitDelayTests
{
    [Fact]
    public void DoesNotHaveDirectFeedthrough()
    {
        Assert.False(new UnitDelay<double>("D1").HasDirectFeedthrough);
    }

    [Fact]
    public void EmitsTheInitialValueOnTheFirstTick()
    {
        var delay = new UnitDelay<double>("D1", initialValue: 7.0);
        var source = new ConstantSource("S1", 100.0);
        source.Out.ConnectTo(delay.In);

        source.Evaluate(NewTickContext(0));
        delay.Evaluate(NewTickContext(0));

        Assert.Equal(7.0, delay.Out.Value);
    }

    [Fact]
    public void EmitsThePreviousTicksInputThereafter()
    {
        var delay = new UnitDelay<double>("D1");
        var source = new ConstantSource("S1", 5.0);
        source.Out.ConnectTo(delay.In);

        source.Evaluate(NewTickContext(0));
        delay.Evaluate(NewTickContext(0));
        Assert.Equal(0.0, delay.Out.Value);

        source.Evaluate(NewTickContext(1));
        delay.Evaluate(NewTickContext(1));
        Assert.Equal(5.0, delay.Out.Value);
    }

    [Fact]
    public void WorksForBooleans()
    {
        var delay = new UnitDelay<bool>("D1", initialValue: true);

        delay.Evaluate(NewTickContext(0));

        Assert.True(delay.Out.Value);
    }

    private static TickContext NewTickContext(long tick) =>
        new(tick, 0.01, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), new EventLog());
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter FullyQualifiedName~UnitDelayTests`
Expected: FAIL — `UnitDelay` does not exist.

- [ ] **Step 3: Implement the delay**

`src/Millrace.Core/Graph/UnitDelay.cs`:

```csharp
using Millrace.Core.Contexts;

namespace Millrace.Core.Graph;

/// <summary>
/// Emits the previous tick's input. Because it declares no direct feedthrough it
/// creates no ordering edge, which is how a genuine feedback loop — belt load
/// raising torque demand, lowering speed, changing belt load — is made solvable.
/// </summary>
public sealed class UnitDelay<T> : ComponentBase
    where T : unmanaged
{
    private T _held;

    public UnitDelay(string id, T initialValue = default)
        : base(id)
    {
        _held = initialValue;
        In = AddInput<T>("In");
        Out = AddOutput<T>("Out");
    }

    public InputPort<T> In { get; }

    public OutputPort<T> Out { get; }

    public override bool HasDirectFeedthrough => false;

    public override void Evaluate(in TickContext ctx)
    {
        Out.Value = _held;
        _held = In.Value;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter FullyQualifiedName~UnitDelayTests`
Expected: PASS, 4 tests.

- [ ] **Step 5: Commit**

```bash
git add src/Millrace.Core/Graph/UnitDelay.cs tests/Millrace.Core.Tests/UnitDelayTests.cs
git commit -m "feat(core): add unit delay for breaking algebraic loops"
```

---

### Task 11: Graph resolver

**Files:**
- Create: `src/Millrace.Core/Graph/GraphResolver.cs`
- Test: `tests/Millrace.Core.Tests/GraphResolverTests.cs`

**Interfaces:**
- Consumes: `ISimComponent`, `Port`, `InputPort<T>` (via `Port.SourcePort`).
- Produces:
  - `static class GraphResolver` with
    `static bool TryResolve(IReadOnlyList<ISimComponent> components, out ISimComponent[] ordered, out IReadOnlyList<string> cycle)`

Kahn's algorithm over edges from each connected input's owning component to the
consumer, skipping components without direct feedthrough. Ties break by
registration index, so ordering is stable for a given plant.

- [ ] **Step 1: Write the failing tests**

`tests/Millrace.Core.Tests/GraphResolverTests.cs`:

```csharp
using Millrace.Core.Graph;
using Millrace.Core.Tests.Fakes;
using Xunit;

namespace Millrace.Core.Tests;

public class GraphResolverTests
{
    [Fact]
    public void OrdersProducersBeforeConsumers()
    {
        var source = new ConstantSource("S", 1.0);
        var gain = new Gain("G", 2.0);
        var recorder = new Recorder("R");
        source.Out.ConnectTo(gain.In);
        gain.Out.ConnectTo(recorder.In);

        // Deliberately registered in reverse dependency order.
        bool resolved = GraphResolver.TryResolve(
            [recorder, gain, source], out ISimComponent[] ordered, out _);

        Assert.True(resolved);
        Assert.Equal(new[] { "S", "G", "R" }, ordered.Select(c => c.Id));
    }

    [Fact]
    public void IndependentComponentsKeepRegistrationOrder()
    {
        var first = new ConstantSource("A", 1.0);
        var second = new ConstantSource("B", 2.0);

        Assert.True(GraphResolver.TryResolve([first, second], out ISimComponent[] ordered, out _));

        Assert.Equal(new[] { "A", "B" }, ordered.Select(c => c.Id));
    }

    [Fact]
    public void ReportsACycleWhenNothingBreaksTheLoop()
    {
        var left = new Gain("L", 1.0);
        var right = new Gain("R", 1.0);
        left.Out.ConnectTo(right.In);
        right.Out.ConnectTo(left.In);

        bool resolved = GraphResolver.TryResolve(
            [left, right], out _, out IReadOnlyList<string> cycle);

        Assert.False(resolved);
        Assert.Contains("L", cycle);
        Assert.Contains("R", cycle);
    }

    [Fact]
    public void AUnitDelayBreaksTheLoop()
    {
        var gain = new Gain("G", 0.5);
        var delay = new UnitDelay<double>("D");
        gain.Out.ConnectTo(delay.In);
        delay.Out.ConnectTo(gain.In);

        bool resolved = GraphResolver.TryResolve(
            [gain, delay], out ISimComponent[] ordered, out _);

        Assert.True(resolved);
        Assert.Equal(2, ordered.Length);
    }

    [Fact]
    public void ResolvesADiamond()
    {
        var source = new ConstantSource("S", 1.0);
        var left = new Gain("L", 2.0);
        var right = new Gain("R", 3.0);
        var sink = new Recorder("K");
        source.Out.ConnectTo(left.In);
        source.Out.ConnectTo(right.In);
        left.Out.ConnectTo(sink.In);

        Assert.True(GraphResolver.TryResolve(
            [sink, left, right, source], out ISimComponent[] ordered, out _));

        List<string> ids = ordered.Select(c => c.Id).ToList();
        Assert.True(ids.IndexOf("S") < ids.IndexOf("L"));
        Assert.True(ids.IndexOf("S") < ids.IndexOf("R"));
        Assert.True(ids.IndexOf("L") < ids.IndexOf("K"));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter FullyQualifiedName~GraphResolverTests`
Expected: FAIL — `GraphResolver` does not exist.

- [ ] **Step 3: Implement the resolver**

`src/Millrace.Core/Graph/GraphResolver.cs`:

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
        var inDegree = new int[count];
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
                inDegree[consumer]++;
            }
        }

        // Ready set kept sorted by registration index so ordering is stable.
        var ready = new SortedSet<int>();
        for (int i = 0; i < count; i++)
        {
            if (inDegree[i] == 0)
            {
                ready.Add(i);
            }
        }

        var result = new ISimComponent[count];
        int placed = 0;
        while (ready.Count > 0)
        {
            int next = ready.Min;
            ready.Remove(next);
            result[placed++] = components[next];

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
            ordered = result;
            cycle = [];
            return true;
        }

        ordered = [];
        cycle = FindCycle(components, dependents, inDegree);
        return false;
    }

    /// <summary>Walks the components that never reached in-degree zero to name one loop.</summary>
    private static List<string> FindCycle(
        IReadOnlyList<ISimComponent> components,
        List<int>[] dependents,
        int[] inDegree)
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

        var path = new List<int>();
        var onPath = new Dictionary<int, int>();
        int current = start;
        while (!onPath.ContainsKey(current))
        {
            onPath[current] = path.Count;
            path.Add(current);

            int next = -1;
            foreach (int dependent in dependents[current])
            {
                if (inDegree[dependent] > 0)
                {
                    next = dependent;
                    break;
                }
            }

            if (next < 0)
            {
                return path.Select(i => components[i].Id).ToList();
            }

            current = next;
        }

        return path.Skip(onPath[current]).Select(i => components[i].Id).ToList();
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter FullyQualifiedName~GraphResolverTests`
Expected: PASS, 5 tests.

- [ ] **Step 5: Commit**

```bash
git add src/Millrace.Core/Graph/GraphResolver.cs tests/Millrace.Core.Tests/GraphResolverTests.cs
git commit -m "feat(core): add topological graph resolver with cycle reporting"
```

---

### Task 12: Validation

**Files:**
- Create: `src/Millrace.Core/Validation/ValidationError.cs`
- Create: `src/Millrace.Core/Validation/ValidationResult.cs`
- Create: `src/Millrace.Core/Validation/SimulationValidationException.cs`
- Test: `tests/Millrace.Core.Tests/ValidationTests.cs`

**Interfaces:**
- Consumes: nothing beyond BCL types.
- Produces:
  - `sealed record ValidationError(string Code, string Message, IReadOnlyList<string> ComponentIds)`
  - `sealed class ValidationResult` with `IReadOnlyList<ValidationError> Errors`,
    `bool IsValid`, `static ValidationResult Ok()`,
    `static ValidationResult From(IEnumerable<ValidationError> errors)`, `string ToText()`
  - `sealed class SimulationValidationException : Exception` with `ValidationResult Result`

Error codes used by Task 13: `MR001` duplicate component id, `MR002` required
input unconnected, `MR003` algebraic loop. Every message names the fix, not just
the symptom.

- [ ] **Step 1: Write the failing tests**

`tests/Millrace.Core.Tests/ValidationTests.cs`:

```csharp
using Millrace.Core.Validation;
using Xunit;

namespace Millrace.Core.Tests;

public class ValidationTests
{
    [Fact]
    public void EmptyResultIsValid()
    {
        Assert.True(ValidationResult.Ok().IsValid);
        Assert.Empty(ValidationResult.Ok().Errors);
    }

    [Fact]
    public void ResultWithErrorsIsInvalid()
    {
        var result = ValidationResult.From(
        [
            new ValidationError("MR002", "Input 'G.In' is required but unconnected.", ["G"]),
        ]);

        Assert.False(result.IsValid);
        Assert.Single(result.Errors);
    }

    [Fact]
    public void TextListsEveryErrorOnItsOwnLine()
    {
        var result = ValidationResult.From(
        [
            new ValidationError("MR001", "first", ["A"]),
            new ValidationError("MR002", "second", ["B"]),
        ]);

        string expected =
            "MR001: first" + Environment.NewLine +
            "MR002: second" + Environment.NewLine;

        Assert.Equal(expected, result.ToText());
    }

    [Fact]
    public void ExceptionCarriesTheResultAndSummarisesItInTheMessage()
    {
        var result = ValidationResult.From(
        [
            new ValidationError("MR003", "Algebraic loop: A -> B -> A.", ["A", "B"]),
        ]);

        var exception = new SimulationValidationException(result);

        Assert.Same(result, exception.Result);
        Assert.Contains("MR003", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ExceptionRejectsAValidResult()
    {
        Assert.Throws<ArgumentException>(
            () => new SimulationValidationException(ValidationResult.Ok()));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter FullyQualifiedName~ValidationTests`
Expected: FAIL — `Millrace.Core.Validation` does not exist.

- [ ] **Step 3: Implement the validation types**

`src/Millrace.Core/Validation/ValidationError.cs`:

```csharp
namespace Millrace.Core.Validation;

/// <summary>
/// One reason a plant cannot run. The message must state the fix, not only the
/// symptom — the reader is often an agent that will act on it directly.
/// </summary>
public sealed record ValidationError(
    string Code,
    string Message,
    IReadOnlyList<string> ComponentIds);
```

`src/Millrace.Core/Validation/ValidationResult.cs`:

```csharp
using System.Text;

namespace Millrace.Core.Validation;

public sealed class ValidationResult
{
    private static readonly ValidationResult Valid = new([]);

    private ValidationResult(IReadOnlyList<ValidationError> errors) => Errors = errors;

    public IReadOnlyList<ValidationError> Errors { get; }

    public bool IsValid => Errors.Count == 0;

    public static ValidationResult Ok() => Valid;

    public static ValidationResult From(IEnumerable<ValidationError> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);

        List<ValidationError> list = errors.ToList();
        return list.Count == 0 ? Valid : new ValidationResult(list);
    }

    public string ToText()
    {
        var builder = new StringBuilder();
        foreach (ValidationError error in Errors)
        {
            builder.Append(error.Code).Append(": ").Append(error.Message)
                   .Append(Environment.NewLine);
        }

        return builder.ToString();
    }
}
```

`src/Millrace.Core/Validation/SimulationValidationException.cs`:

```csharp
namespace Millrace.Core.Validation;

public sealed class SimulationValidationException : Exception
{
    public SimulationValidationException(ValidationResult result)
        : base(BuildMessage(result))
    {
        Result = result;
    }

    public ValidationResult Result { get; }

    private static string BuildMessage(ValidationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.IsValid)
        {
            throw new ArgumentException(
                "A validation exception requires at least one error.", nameof(result));
        }

        return $"The plant failed validation:{Environment.NewLine}{result.ToText()}";
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter FullyQualifiedName~ValidationTests`
Expected: PASS, 5 tests.

- [ ] **Step 5: Commit**

```bash
git add src/Millrace.Core/Validation tests/Millrace.Core.Tests/ValidationTests.cs
git commit -m "feat(core): add validation results and exception"
```

---

### Task 13: Simulation and builder

**Files:**
- Create: `src/Millrace.Core/Simulation.cs`
- Create: `src/Millrace.Core/SimulationBuilder.cs`
- Test: `tests/Millrace.Core.Tests/SimulationTests.cs`

**Interfaces:**
- Consumes: everything from Tasks 2-12.
- Produces:
  - `sealed class SimulationBuilder(SimulationOptions options)` with
    `SimulationBuilder Add(ISimNode node)`, `ValidationResult Validate()`,
    `Simulation Build()`
  - `sealed class Simulation` with `SimulationClock Clock`,
    `TelemetryRegistry Telemetry`, `EventLog Events`,
    `IReadOnlyList<ISimComponent> Components`,
    `long ScheduleAt(TimeSpan fromStart, ISimEvent e)`,
    `long ScheduleIn(TimeSpan delay, ISimEvent e)`, `void Tick()`,
    `void RunFor(TimeSpan duration)`

`Tick()` runs the five phases from spec section 5.2. Phases 3 and 4 are private
no-op methods in this plan; plans 2 and 4 fill them in without restructuring.

- [ ] **Step 1: Write the failing tests**

`tests/Millrace.Core.Tests/SimulationTests.cs`:

```csharp
using Millrace.Core;
using Millrace.Core.Events;
using Millrace.Core.Graph;
using Millrace.Core.Time;
using Millrace.Core.Tests.Fakes;
using Millrace.Core.Validation;
using Xunit;

namespace Millrace.Core.Tests;

public class SimulationTests
{
    private static SimulationOptions Options => new()
    {
        Seed = 492781UL,
        StartTime = new DateTimeOffset(2026, 1, 1, 6, 0, 0, TimeSpan.Zero),
        TimeStep = TimeSpan.FromMilliseconds(10),
    };

    [Fact]
    public void BuildFlattensCompositesAndOrdersComponents()
    {
        var builder = new SimulationBuilder(Options);
        builder.Add(new TwoStage("CV001", 2.0, 3.0));

        Simulation sim = builder.Build();

        Assert.Equal(
            new[] { "CV001.First", "CV001.Second" },
            sim.Components.Select(c => c.Id));
    }

    [Fact]
    public void TickEvaluatesTheWholeChainInOneTick()
    {
        var source = new ConstantSource("S", 21.0);
        var gain = new Gain("G", 2.0);
        var recorder = new Recorder("R");
        source.Out.ConnectTo(gain.In);
        gain.Out.ConnectTo(recorder.In);

        Simulation sim = new SimulationBuilder(Options)
            .Add(recorder).Add(gain).Add(source)
            .Build();

        sim.Tick();

        Assert.Equal(new[] { 42.0 }, recorder.Samples);
    }

    [Fact]
    public void TickAdvancesTheClockAfterEvaluation()
    {
        Simulation sim = new SimulationBuilder(Options).Add(new ConstantSource("S", 1.0)).Build();

        Assert.Equal(0, sim.Clock.TickCount);
        sim.Tick();
        Assert.Equal(1, sim.Clock.TickCount);
        Assert.Equal(Options.StartTime.AddMilliseconds(10), sim.Clock.Now);
    }

    [Fact]
    public void RunForExecutesTheExpectedNumberOfTicks()
    {
        var recorder = new Recorder("R");
        Simulation sim = new SimulationBuilder(Options).Add(recorder).Build();

        sim.RunFor(TimeSpan.FromSeconds(1));

        Assert.Equal(100, recorder.Samples.Count);
        Assert.Equal(100, sim.Clock.TickCount);
    }

    [Fact]
    public void ScheduledEventsFireOnTheirTickBeforeEvaluation()
    {
        var order = new List<string>();
        var probe = new CallbackProbe("P", order);
        Simulation sim = new SimulationBuilder(Options).Add(probe).Build();

        sim.ScheduleAt(TimeSpan.FromMilliseconds(20), new CallbackEvent(() => order.Add("event")));

        sim.RunFor(TimeSpan.FromMilliseconds(40));

        // Tick 2 is the 20 ms mark: the event must precede that tick's evaluation.
        int eventIndex = order.IndexOf("event");
        Assert.Equal("evaluate@2", order[eventIndex + 1]);
    }

    [Fact]
    public void ValidateReportsDuplicateComponentIds()
    {
        var builder = new SimulationBuilder(Options)
            .Add(new ConstantSource("S", 1.0))
            .Add(new ConstantSource("S", 2.0));

        ValidationResult result = builder.Validate();

        Assert.False(result.IsValid);
        Assert.Equal("MR001", result.Errors[0].Code);
    }

    [Fact]
    public void ValidateReportsUnconnectedRequiredInputs()
    {
        var builder = new SimulationBuilder(Options).Add(new NeedsInput("N"));

        ValidationResult result = builder.Validate();

        Assert.Equal("MR002", result.Errors[0].Code);
        Assert.Contains("N.In", result.Errors[0].Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateReportsAnAlgebraicLoopAndNamesTheFix()
    {
        var left = new Gain("L", 1.0);
        var right = new Gain("R", 1.0);
        left.Out.ConnectTo(right.In);
        right.Out.ConnectTo(left.In);

        ValidationResult result = new SimulationBuilder(Options).Add(left).Add(right).Validate();

        Assert.Equal("MR003", result.Errors[0].Code);
        Assert.Contains("UnitDelay", result.Errors[0].Message, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildThrowsWhenValidationFails()
    {
        var builder = new SimulationBuilder(Options).Add(new NeedsInput("N"));

        SimulationValidationException error =
            Assert.Throws<SimulationValidationException>(() => builder.Build());
        Assert.Equal("MR002", error.Result.Errors[0].Code);
    }

    [Fact]
    public void InitializeRunsOnceBeforeTheFirstTick()
    {
        var counter = new InitCounter("I");
        Simulation sim = new SimulationBuilder(Options).Add(counter).Build();

        sim.Tick();
        sim.Tick();

        Assert.Equal(1, counter.InitializeCount);
    }

    private sealed class NeedsInput : ComponentBase
    {
        public NeedsInput(string id)
            : base(id) => AddInput<double>("In", defaultValue: 0.0, required: true);

        public override void Evaluate(in Contexts.TickContext ctx)
        {
        }
    }

    private sealed class InitCounter : ComponentBase
    {
        public InitCounter(string id)
            : base(id)
        {
        }

        public int InitializeCount { get; private set; }

        public override void Initialize(in Contexts.InitContext ctx) => InitializeCount++;

        public override void Evaluate(in Contexts.TickContext ctx)
        {
        }
    }

    private sealed class CallbackProbe : ComponentBase
    {
        private readonly List<string> _order;

        public CallbackProbe(string id, List<string> order)
            : base(id) => _order = order;

        public override void Evaluate(in Contexts.TickContext ctx) =>
            _order.Add($"evaluate@{ctx.Tick}");
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter FullyQualifiedName~SimulationTests`
Expected: FAIL — `SimulationBuilder` does not exist.

- [ ] **Step 3: Implement the simulation**

`src/Millrace.Core/Simulation.cs`:

```csharp
using Millrace.Core.Contexts;
using Millrace.Core.Events;
using Millrace.Core.Graph;
using Millrace.Core.Logging;
using Millrace.Core.Randomness;
using Millrace.Core.Telemetry;
using Millrace.Core.Time;

namespace Millrace.Core;

/// <summary>
/// A validated, immutable plant plus its running state. The graph cannot change
/// after construction, which is what keeps evaluation order — and therefore
/// results — reproducible.
/// </summary>
public sealed class Simulation
{
    private readonly ISimComponent[] _components;
    private readonly EventQueue _queue = new();
    private readonly ulong _seed;
    private bool _initialized;

    internal Simulation(ISimComponent[] components, SimulationOptions options)
    {
        _components = components;
        _seed = options.Seed;
        Clock = new SimulationClock(options.StartTime, options.TimeStep);
    }

    public SimulationClock Clock { get; }

    public TelemetryRegistry Telemetry { get; } = new();

    public EventLog Events { get; } = new();

    public IReadOnlyList<ISimComponent> Components => _components;

    /// <summary>Schedules an event at a simulation time measured from the start.</summary>
    public long ScheduleAt(TimeSpan fromStart, ISimEvent simEvent) =>
        _queue.Schedule(fromStart.Ticks / Clock.TimeStep.Ticks, simEvent);

    /// <summary>Schedules an event relative to the current simulation time.</summary>
    public long ScheduleIn(TimeSpan delay, ISimEvent simEvent) =>
        _queue.Schedule(Clock.TickCount + (delay.Ticks / Clock.TimeStep.Ticks), simEvent);

    /// <summary>Runs Initialize on every component. Called automatically by the first tick.</summary>
    public void Initialize()
    {
        if (_initialized)
        {
            return;
        }

        foreach (ISimComponent component in _components)
        {
            var context = new InitContext(
                new DeterministicRandom(Hash64.Combine(_seed, component.Id)),
                Telemetry,
                component.Id,
                Clock.StartTime,
                Clock.DeltaSeconds);

            component.Initialize(context);
        }

        _initialized = true;
    }

    /// <summary>Advances the plant by exactly one time step, in the five fixed phases.</summary>
    public void Tick()
    {
        Initialize();

        DrainDueEvents();       // phase 1
        EvaluateSignals();      // phase 2
        AdvanceFlow();          // phase 3
        PublishIo();            // phase 4
        EmitFrame();            // phase 5

        Clock.Advance();
    }

    public void RunFor(TimeSpan duration)
    {
        long ticks = duration.Ticks / Clock.TimeStep.Ticks;
        for (long i = 0; i < ticks; i++)
        {
            Tick();
        }
    }

    private void DrainDueEvents()
    {
        while (_queue.TryDequeueDue(Clock.TickCount, out ScheduledEvent scheduled))
        {
            scheduled.Payload.Apply();
        }
    }

    private void EvaluateSignals()
    {
        var context = new TickContext(
            Clock.TickCount, Clock.DeltaSeconds, Clock.Now, Events);

        foreach (ISimComponent component in _components)
        {
            component.Evaluate(context);
        }
    }

    /// <summary>Phase 3. Material transport arrives in plan 2.</summary>
    private static void AdvanceFlow()
    {
    }

    /// <summary>Phase 4. The I/O image arrives in plan 4.</summary>
    private static void PublishIo()
    {
    }

    /// <summary>Phase 5. Tick frames arrive in plan 4; the event log is already appended during evaluation.</summary>
    private static void EmitFrame()
    {
    }
}
```

- [ ] **Step 4: Implement the builder**

`src/Millrace.Core/SimulationBuilder.cs`:

```csharp
using Millrace.Core.Graph;
using Millrace.Core.Time;
using Millrace.Core.Validation;

namespace Millrace.Core;

/// <summary>Collects nodes, flattens composites, validates, and produces a Simulation.</summary>
public sealed class SimulationBuilder
{
    private readonly List<ISimComponent> _components = [];
    private readonly SimulationOptions _options;

    public SimulationBuilder(SimulationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    /// <summary>Adds a leaf component, or every leaf beneath a composite.</summary>
    public SimulationBuilder Add(ISimNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        switch (node)
        {
            case ISimComponent component:
                _components.Add(component);
                break;
            case CompositeComponent composite:
                _components.AddRange(composite.Leaves());
                break;
            default:
                throw new ArgumentException(
                    $"'{node.GetType().Name}' is neither an {nameof(ISimComponent)} nor a " +
                    $"{nameof(CompositeComponent)}.",
                    nameof(node));
        }

        return this;
    }

    /// <summary>Checks the plant without building it. Used by tooling and by Build.</summary>
    public ValidationResult Validate()
    {
        var errors = new List<ValidationError>();

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (ISimComponent component in _components)
        {
            if (!seen.Add(component.Id))
            {
                errors.Add(new ValidationError(
                    "MR001",
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
                        "MR002",
                        $"Input '{port.QualifiedName}' is required but nothing drives it. " +
                        $"Connect an output to it, or declare the input optional with a default.",
                        [component.Id]));
                }
            }
        }

        if (!GraphResolver.TryResolve(_components, out _, out IReadOnlyList<string> cycle))
        {
            string path = string.Join(" -> ", cycle.Append(cycle.Count > 0 ? cycle[0] : string.Empty));
            errors.Add(new ValidationError(
                "MR003",
                $"Algebraic loop: {path}. Insert a UnitDelay on one connection in the cycle to " +
                $"break it; one tick of lag is physically irrelevant and makes the solve order " +
                $"unambiguous.",
                cycle));
        }

        return ValidationResult.From(errors);
    }

    /// <summary>Validates and constructs the simulation. Throws if the plant is invalid.</summary>
    public Simulation Build()
    {
        ValidationResult result = Validate();
        if (!result.IsValid)
        {
            throw new SimulationValidationException(result);
        }

        GraphResolver.TryResolve(_components, out ISimComponent[] ordered, out _);
        return new Simulation(ordered, _options);
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test --filter FullyQualifiedName~SimulationTests`
Expected: PASS, 10 tests.

- [ ] **Step 6: Run the whole suite**

Run: `dotnet test`
Expected: PASS, all tests.

- [ ] **Step 7: Commit**

```bash
git add src/Millrace.Core/Simulation.cs src/Millrace.Core/SimulationBuilder.cs tests/Millrace.Core.Tests/SimulationTests.cs
git commit -m "feat(core): add simulation lifecycle, tick phases and builder validation"
```

---

### Task 14: Per-component seeding is stable across plant edits

**Files:**
- Create: `tests/Millrace.Core.Tests/Fakes/NoiseSource.cs`
- Test: `tests/Millrace.Core.Tests/SeedStabilityTests.cs`

**Interfaces:**
- Consumes: `Simulation`, `SimulationBuilder`, `InitContext.Random`.
- Produces: test fake `NoiseSource` with `IReadOnlyList<double> Samples`.

This task adds no production code. Task 13 already derives each component's seed
with `Hash64.Combine(masterSeed, componentId)`; this locks that property in.
Spec section 5.3 calls it out because it is easy to regress — swapping to a
single shared stream still passes every other test in this plan, and silently
breaks every saved scenario the moment a plant is edited.

- [ ] **Step 1: Write the noise fake**

`tests/Millrace.Core.Tests/Fakes/NoiseSource.cs`:

```csharp
using Millrace.Core.Contexts;
using Millrace.Core.Graph;
using Millrace.Core.Randomness;

namespace Millrace.Core.Tests.Fakes;

/// <summary>Emits one draw from its own random stream per tick.</summary>
public sealed class NoiseSource : ComponentBase
{
    private readonly List<double> _samples = [];
    private DeterministicRandom? _random;

    public NoiseSource(string id)
        : base(id) => Out = AddOutput<double>("Out");

    public OutputPort<double> Out { get; }

    public IReadOnlyList<double> Samples => _samples;

    public override void Initialize(in InitContext ctx) => _random = ctx.Random;

    public override void Evaluate(in TickContext ctx)
    {
        double value = _random!.NextDouble();
        Out.Value = value;
        _samples.Add(value);
    }
}
```

- [ ] **Step 2: Write the failing tests**

`tests/Millrace.Core.Tests/SeedStabilityTests.cs`:

```csharp
using Millrace.Core;
using Millrace.Core.Time;
using Millrace.Core.Tests.Fakes;
using Xunit;

namespace Millrace.Core.Tests;

public class SeedStabilityTests
{
    private static SimulationOptions Options(ulong seed = 492781UL) => new()
    {
        Seed = seed,
        StartTime = new DateTimeOffset(2026, 1, 1, 6, 0, 0, TimeSpan.Zero),
        TimeStep = TimeSpan.FromMilliseconds(10),
    };

    [Fact]
    public void AddingAComponentDoesNotShiftAnotherComponentsStream()
    {
        var smallX = new NoiseSource("X");
        Simulation small = new SimulationBuilder(Options()).Add(smallX).Build();
        small.RunFor(TimeSpan.FromMilliseconds(100));

        var largeX = new NoiseSource("X");
        Simulation large = new SimulationBuilder(Options())
            .Add(new NoiseSource("W"))
            .Add(largeX)
            .Add(new NoiseSource("Z"))
            .Build();
        large.RunFor(TimeSpan.FromMilliseconds(100));

        Assert.Equal(smallX.Samples, largeX.Samples);
    }

    [Fact]
    public void DifferentComponentsGetDifferentStreams()
    {
        var first = new NoiseSource("A");
        var second = new NoiseSource("B");
        Simulation sim = new SimulationBuilder(Options()).Add(first).Add(second).Build();

        sim.RunFor(TimeSpan.FromMilliseconds(100));

        Assert.NotEqual(first.Samples, second.Samples);
    }

    [Fact]
    public void ADifferentMasterSeedChangesEveryStream()
    {
        var first = new NoiseSource("A");
        new SimulationBuilder(Options(1UL)).Add(first).Build()
            .RunFor(TimeSpan.FromMilliseconds(100));

        var second = new NoiseSource("A");
        new SimulationBuilder(Options(2UL)).Add(second).Build()
            .RunFor(TimeSpan.FromMilliseconds(100));

        Assert.NotEqual(first.Samples, second.Samples);
    }

    [Fact]
    public void RenamingAComponentChangesItsStream()
    {
        var original = new NoiseSource("A");
        new SimulationBuilder(Options()).Add(original).Build()
            .RunFor(TimeSpan.FromMilliseconds(100));

        var renamed = new NoiseSource("A2");
        new SimulationBuilder(Options()).Add(renamed).Build()
            .RunFor(TimeSpan.FromMilliseconds(100));

        Assert.NotEqual(original.Samples, renamed.Samples);
    }
}
```

- [ ] **Step 3: Run the tests**

Run: `dotnet test --filter FullyQualifiedName~SeedStabilityTests`
Expected: PASS, 4 tests. If `AddingAComponentDoesNotShiftAnotherComponentsStream`
fails, `Simulation.Initialize` is handing out draws from one shared stream
instead of deriving per component — fix it there, not in the test.

- [ ] **Step 4: Commit**

```bash
git add tests/Millrace.Core.Tests/Fakes/NoiseSource.cs tests/Millrace.Core.Tests/SeedStabilityTests.cs
git commit -m "test(core): lock in per-component seed stability across plant edits"
```

---

### Task 15: Runner and execution modes

**Files:**
- Create: `src/Millrace.Core/SimulationRunner.cs`
- Test: `tests/Millrace.Core.Tests/SimulationRunnerTests.cs`

**Interfaces:**
- Consumes: `Simulation`, `SimulationClock`.
- Produces:
  - `enum ExecutionMode { AsFastAsPossible, RealTime, Scaled }`
  - `sealed class SimulationRunner(Simulation simulation, ExecutionMode mode = ExecutionMode.AsFastAsPossible, double speedFactor = 1.0)`
    with `void RunFor(TimeSpan simDuration, CancellationToken cancellationToken = default)`
    and `void Step(int ticks = 1)`

Pacing lives here and nowhere else, so execution mode cannot affect results.
Paused and step-by-step are `Step` plus the caller's own loop rather than modes.

- [ ] **Step 1: Write the failing tests**

`tests/Millrace.Core.Tests/SimulationRunnerTests.cs`:

```csharp
using System.Diagnostics;
using Millrace.Core;
using Millrace.Core.Time;
using Millrace.Core.Tests.Fakes;
using Xunit;

namespace Millrace.Core.Tests;

public class SimulationRunnerTests
{
    private static SimulationOptions Options => new()
    {
        Seed = 1UL,
        StartTime = new DateTimeOffset(2026, 1, 1, 6, 0, 0, TimeSpan.Zero),
        TimeStep = TimeSpan.FromMilliseconds(10),
    };

    private static (Simulation Sim, Recorder Recorder) NewPlant()
    {
        var source = new ConstantSource("S", 3.0);
        var recorder = new Recorder("R");
        source.Out.ConnectTo(recorder.In);

        Simulation sim = new SimulationBuilder(Options).Add(source).Add(recorder).Build();
        return (sim, recorder);
    }

    [Fact]
    public void AsFastAsPossibleRunsEveryTick()
    {
        (Simulation sim, Recorder recorder) = NewPlant();

        new SimulationRunner(sim).RunFor(TimeSpan.FromSeconds(1));

        Assert.Equal(100, recorder.Samples.Count);
    }

    [Fact]
    public void StepAdvancesExactlyTheRequestedTicks()
    {
        (Simulation sim, Recorder recorder) = NewPlant();
        var runner = new SimulationRunner(sim);

        runner.Step();
        runner.Step(4);

        Assert.Equal(5, recorder.Samples.Count);
        Assert.Equal(5, sim.Clock.TickCount);
    }

    [Fact]
    public void ExecutionModeDoesNotChangeResults()
    {
        (Simulation fast, Recorder fastRecorder) = NewPlant();
        (Simulation scaled, Recorder scaledRecorder) = NewPlant();

        new SimulationRunner(fast).RunFor(TimeSpan.FromSeconds(1));
        new SimulationRunner(scaled, ExecutionMode.Scaled, speedFactor: 1000.0)
            .RunFor(TimeSpan.FromSeconds(1));

        Assert.Equal(fastRecorder.Samples, scaledRecorder.Samples);
        Assert.Equal(fast.Events.ToText(), scaled.Events.ToText());
    }

    [Fact]
    public void ScaledModeTakesRoughlyTheExpectedWallClockTime()
    {
        (Simulation sim, _) = NewPlant();
        var stopwatch = Stopwatch.StartNew();

        // 2 simulated seconds at 20x is about 100 ms of wall clock.
        new SimulationRunner(sim, ExecutionMode.Scaled, speedFactor: 20.0)
            .RunFor(TimeSpan.FromSeconds(2));

        stopwatch.Stop();
        Assert.InRange(stopwatch.Elapsed.TotalMilliseconds, 50.0, 600.0);
    }

    [Fact]
    public void CancellationStopsTheRunEarly()
    {
        (Simulation sim, Recorder recorder) = NewPlant();
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(50));

        new SimulationRunner(sim, ExecutionMode.Scaled, speedFactor: 1.0)
            .RunFor(TimeSpan.FromSeconds(30), cts.Token);

        Assert.True(recorder.Samples.Count < 3000);
    }

    [Fact]
    public void RejectsANonPositiveSpeedFactor()
    {
        (Simulation sim, _) = NewPlant();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => new SimulationRunner(sim, ExecutionMode.Scaled, speedFactor: 0.0));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter FullyQualifiedName~SimulationRunnerTests`
Expected: FAIL — `SimulationRunner` does not exist.

- [ ] **Step 3: Implement the runner**

`src/Millrace.Core/SimulationRunner.cs`:

```csharp
using System.Diagnostics;

namespace Millrace.Core;

public enum ExecutionMode
{
    /// <summary>No pacing. An eight-hour scenario finishes in seconds.</summary>
    AsFastAsPossible,

    /// <summary>One second of simulation per second of wall clock.</summary>
    RealTime,

    /// <summary>Real time multiplied by a speed factor.</summary>
    Scaled,
}

/// <summary>
/// Drives a simulation. All pacing lives here, so switching execution mode cannot
/// change results — only how long the run takes.
/// </summary>
public sealed class SimulationRunner
{
    private readonly Simulation _simulation;
    private readonly ExecutionMode _mode;
    private readonly double _speedFactor;

    public SimulationRunner(
        Simulation simulation,
        ExecutionMode mode = ExecutionMode.AsFastAsPossible,
        double speedFactor = 1.0)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        if (speedFactor <= 0.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(speedFactor), speedFactor, "The speed factor must be positive.");
        }

        _simulation = simulation;
        _mode = mode;
        _speedFactor = mode == ExecutionMode.RealTime ? 1.0 : speedFactor;
    }

    /// <summary>Advances by a fixed number of ticks, ignoring pacing. Use for step-by-step control.</summary>
    public void Step(int ticks = 1)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(ticks);

        for (int i = 0; i < ticks; i++)
        {
            _simulation.Tick();
        }
    }

    public void RunFor(TimeSpan simDuration, CancellationToken cancellationToken = default)
    {
        long stepTicks = _simulation.Clock.TimeStep.Ticks;
        long tickCount = simDuration.Ticks / stepTicks;
        bool paced = _mode is ExecutionMode.RealTime or ExecutionMode.Scaled;
        double stepMilliseconds = _simulation.Clock.TimeStep.TotalMilliseconds / _speedFactor;

        Stopwatch? stopwatch = paced ? Stopwatch.StartNew() : null;

        for (long i = 0; i < tickCount; i++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            _simulation.Tick();

            if (stopwatch is null)
            {
                continue;
            }

            double targetMilliseconds = (i + 1) * stepMilliseconds;
            double waitMilliseconds = targetMilliseconds - stopwatch.Elapsed.TotalMilliseconds;
            if (waitMilliseconds >= 1.0)
            {
                cancellationToken.WaitHandle.WaitOne((int)waitMilliseconds);
            }
        }
    }
}
```

`WaitHandle.WaitOne` is used rather than `Thread.Sleep` so that a cancelled run
stops promptly instead of sleeping out the remaining interval.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter FullyQualifiedName~SimulationRunnerTests`
Expected: PASS, 6 tests.

- [ ] **Step 5: Commit**

```bash
git add src/Millrace.Core/SimulationRunner.cs tests/Millrace.Core.Tests/SimulationRunnerTests.cs
git commit -m "feat(core): add simulation runner with real-time and scaled pacing"
```

---

### Task 16: End-to-end determinism

**Files:**
- Create: `tests/Millrace.Core.Tests/Fakes/Tripper.cs`
- Create: `docs/architecture.md`
- Test: `tests/Millrace.Core.Tests/DeterminismTests.cs`
- Modify: `README.md` (create if absent)

**Interfaces:**
- Consumes: everything above.
- Produces: test fake `Tripper`; no production code.

This is the plan's acceptance test. It builds a plant with a composite, a
feedback loop broken by a `UnitDelay`, randomness and logged events, runs it
twice, and requires the two runs to be indistinguishable.

- [ ] **Step 1: Write the failing tests**

Write both files below, then run them before `Tripper` exists — the suite must
fail to compile, which is this task's red state.

- [ ] **Step 2: Write the tripper fake**

`tests/Millrace.Core.Tests/Fakes/Tripper.cs`:

```csharp
using Millrace.Core.Contexts;
using Millrace.Core.Graph;

namespace Millrace.Core.Tests.Fakes;

/// <summary>Logs a one-shot event the first time its input exceeds a threshold.</summary>
public sealed class Tripper : ComponentBase
{
    private readonly double _threshold;
    private bool _tripped;

    public Tripper(string id, double threshold)
        : base(id)
    {
        _threshold = threshold;
        In = AddInput<double>("In", defaultValue: 0.0, required: true);
        Tripped = AddOutput<bool>("Tripped");
    }

    public InputPort<double> In { get; }

    public OutputPort<bool> Tripped { get; }

    public override void Evaluate(in TickContext ctx)
    {
        if (!_tripped && In.Value > _threshold)
        {
            _tripped = true;
            ctx.Log(Id, "TRIP", $"Input exceeded {_threshold}.");
        }

        Tripped.Value = _tripped;
    }
}
```

The test file, written first:

`tests/Millrace.Core.Tests/DeterminismTests.cs`:

```csharp
using Millrace.Core;
using Millrace.Core.Events;
using Millrace.Core.Graph;
using Millrace.Core.Time;
using Millrace.Core.Tests.Fakes;
using Xunit;

namespace Millrace.Core.Tests;

public class DeterminismTests
{
    private sealed record Run(
        Simulation Simulation,
        Recorder Recorder,
        NoiseSource Noise);

    private static SimulationOptions Options(ulong seed) => new()
    {
        Seed = seed,
        StartTime = new DateTimeOffset(2026, 1, 1, 6, 32, 11, TimeSpan.Zero),
        TimeStep = TimeSpan.FromMilliseconds(10),
    };

    /// <summary>
    /// Noise feeds a two-stage composite; the composite's output is integrated
    /// through a delayed feedback loop; a tripper logs an event when the integral
    /// passes a threshold. Exercises composites, loops, randomness and logging.
    /// </summary>
    private static Run BuildPlant(ulong seed)
    {
        var noise = new NoiseSource("Noise");
        var stage = new TwoStage("CV001", firstFactor: 0.5, secondFactor: 0.5);
        var integrator = new Integrator("Integrator");
        var delay = new UnitDelay<double>("Delay");
        var damping = new Gain("Damping", -0.05);
        var sum = new Sum("Sum");
        var recorder = new Recorder("Recorder");
        var tripper = new Tripper("Tripper", threshold: 0.20);

        noise.Out.ConnectTo(stage.Input<double>("In"));
        stage.Output<double>("Out").ConnectTo(sum.A);
        delay.Out.ConnectTo(damping.In);
        damping.Out.ConnectTo(sum.B);
        sum.Out.ConnectTo(integrator.In);
        integrator.Out.ConnectTo(delay.In);
        integrator.Out.ConnectTo(recorder.In);
        integrator.Out.ConnectTo(tripper.In);

        Simulation sim = new SimulationBuilder(Options(seed))
            .Add(noise).Add(stage).Add(sum).Add(integrator)
            .Add(delay).Add(damping).Add(recorder).Add(tripper)
            .Build();

        return new Run(sim, recorder, noise);
    }

    [Fact]
    public void ThePlantValidatesDespiteTheFeedbackLoop()
    {
        Run run = BuildPlant(492781UL);

        Assert.Equal(9, run.Simulation.Components.Count);
    }

    [Fact]
    public void TwoRunsWithTheSameSeedAreIndistinguishable()
    {
        Run first = BuildPlant(492781UL);
        Run second = BuildPlant(492781UL);

        first.Simulation.RunFor(TimeSpan.FromMinutes(1));
        second.Simulation.RunFor(TimeSpan.FromMinutes(1));

        Assert.Equal(first.Recorder.Samples, second.Recorder.Samples);
        Assert.Equal(first.Noise.Samples, second.Noise.Samples);
        Assert.Equal(first.Simulation.Events.ToText(), second.Simulation.Events.ToText());
        Assert.Equal(
            first.Simulation.Telemetry.Read("CV001.Second.Out"),
            second.Simulation.Telemetry.Read("CV001.Second.Out"));
    }

    [Fact]
    public void ADifferentSeedProducesADifferentRun()
    {
        Run first = BuildPlant(492781UL);
        Run second = BuildPlant(492782UL);

        first.Simulation.RunFor(TimeSpan.FromMinutes(1));
        second.Simulation.RunFor(TimeSpan.FromMinutes(1));

        Assert.NotEqual(first.Recorder.Samples, second.Recorder.Samples);
    }

    [Fact]
    public void TheRunProducesLoggedEvents()
    {
        Run run = BuildPlant(492781UL);

        run.Simulation.RunFor(TimeSpan.FromMinutes(1));

        Assert.Contains(run.Simulation.Events.Records, r => r.Code == "TRIP");
    }

    [Fact]
    public void InjectedEventsAreReproducible()
    {
        Run first = BuildPlant(492781UL);
        Run second = BuildPlant(492781UL);

        foreach (Run run in new[] { first, second })
        {
            Simulation sim = run.Simulation;
            sim.ScheduleAt(
                TimeSpan.FromSeconds(30),
                new CallbackEvent(() => sim.Events.Record(
                    sim.Clock.TickCount, sim.Clock.Now, "TEST", "INJECTED", "fault injected")));
        }

        first.Simulation.RunFor(TimeSpan.FromMinutes(1));
        second.Simulation.RunFor(TimeSpan.FromMinutes(1));

        Assert.Equal(first.Simulation.Events.ToText(), second.Simulation.Events.ToText());
        Assert.Contains(first.Simulation.Events.Records, r => r.Code == "INJECTED");
    }

    /// <summary>Adds two inputs. Needed to close the feedback loop.</summary>
    private sealed class Sum : ComponentBase
    {
        public Sum(string id)
            : base(id)
        {
            A = AddInput<double>("A");
            B = AddInput<double>("B");
            Out = AddOutput<double>("Out");
        }

        public InputPort<double> A { get; }

        public InputPort<double> B { get; }

        public OutputPort<double> Out { get; }

        public override void Evaluate(in Contexts.TickContext ctx) =>
            Out.Value = A.Value + B.Value;
    }
}
```

- [ ] **Step 3: Run tests to verify they pass**

Run: `dotnet test --filter FullyQualifiedName~DeterminismTests`
Expected: PASS, 5 tests.

The trip is guaranteed rather than lucky, which is why the assertion is
unconditional. `NoiseSource` is uniform on [0, 1) with mean 0.5; the two-stage
composite scales it by 0.25, so the integrator sees a mean input of 0.125 minus
0.05 times its own delayed output. That is `dI/dt = 0.125 - 0.05 I`, which rises
toward a steady state of 2.5 and crosses the 0.20 threshold after about 1.7
simulated seconds — comfortably inside the one-minute run.

- [ ] **Step 4: Write the architecture document**

Spec section 19 requires this: a contributor who does not understand evaluation
order will write a component that breaks determinism.

`docs/architecture.md`:

```markdown
# Architecture

## The tick

`Simulation.Tick()` runs five phases in a fixed order, and that order is the
determinism guarantee:

1. **Drain events** due at or before this tick, ordered by `(dueTick, sequence)`.
   The sequence number is monotonic, so events due on the same tick always fire
   in the order they were scheduled.
2. **Evaluate the signal graph** in resolved topological order.
3. **Advance flow** — material transport. Not yet implemented.
4. **Publish the I/O image** — the snapshot external readers see. Not yet
   implemented.
5. **Emit the tick frame.** Not yet implemented; the event log is already
   appended during evaluation.

The clock advances after phase 5, so a component evaluating on tick N sees tick
N's simulation time.

## The port model

A component reads its inputs, updates its own state, and writes its outputs. It
never touches another component. That single restriction is what makes
topological ordering meaningful and lets every component be unit-tested alone.

An output may drive many inputs. An input has exactly one source — two sources
is an undefined value, so the second connection throws at wiring time rather
than being reported later. An unconnected input reads its declared default, so a
partial plant still runs.

Composites never evaluate. `CompositeComponent` owns children and exposes
aliases of their ports; at build time the tree is flattened to leaves and their
ids are qualified with the composite path. There is no hard-coded conveyor
anywhere in the engine.

## Feedback loops

Some loops are real physics, not mistakes: belt load raises motor torque demand,
which lowers speed, which changes belt load. Validation detects the cycle, names
the components in it, and refuses to run until a `UnitDelay` breaks it.
`UnitDelay` declares `HasDirectFeedthrough => false`, so it creates no ordering
edge. One tick of lag at 10 ms is physically irrelevant.

## Determinism rules

Rules a component author must follow, all of them easy to break by accident:

- Never use `System.Random`. Its algorithm is not contractually stable across
  .NET versions. Use the `DeterministicRandom` handed to you in `InitContext`.
- Never use `string.GetHashCode()` in anything affecting behaviour. It is
  randomised per process. Use `Hash64.OfString`.
- Never iterate a `Dictionary` or `HashSet` during a tick. Resolve lookups to
  arrays during `Initialize`.
- Never read wall-clock time. `TickContext.SimTime` is the only clock.
- Keep all state in the component. Static mutable state is shared across
  simulations and destroys reproducibility.

Each component's random stream is seeded from `hash(masterSeed, componentId)`,
not drawn from one shared generator. This is deliberate: it means adding a
component to a plant does not shift any other component's randomness, so a saved
scenario survives plant edits. `SeedStabilityTests` guards it.

## Telemetry versus instrumentation

Telemetry is the god view — true internal values, published by components, read
freely by tests. Instrumentation is what the plant can actually measure, and
arrives with the component library: sensors are ordinary components subject to
noise, drift, lag and failure. A test asserting true belt speed uses telemetry.
A controller must go through a sensor, and can be lied to.
```

- [ ] **Step 5: Write the README**

`README.md`:

```markdown
# Millrace — Deterministic Industrial Process Simulation Engine

A deterministic, composable simulation engine for real-world industrial
processes, written in .NET. Build virtual plants from reusable machines,
instrumentation and process components, then run, test, break and replay them
deterministically.

The engine simulates behaviour, not tags: belt speed, motor current and tonnes
per hour are derived from a physical model, so a change anywhere propagates
causally through the plant.

## Status

Under construction. This repository currently contains the simulation core:
deterministic clock, per-component random streams, typed signal ports,
composite components, topological resolution with algebraic-loop detection,
validation, telemetry, an ordered event log, and a runner with real-time and
scaled execution.

Material flow, the industrial component library, the I/O and real-time layers,
declarative configuration and the reference samples are planned. See
`docs/superpowers/specs/` for the design and `docs/superpowers/plans/` for the
implementation plans.

## Build and test

```bash
dotnet build
dotnet test
```

## Licence

MIT.
```

- [ ] **Step 6: Run the whole suite and a release build**

Run: `dotnet test && dotnet build --configuration Release`
Expected: all tests PASS; Release build succeeds with 0 warnings.

- [ ] **Step 7: Commit**

```bash
git add tests/Millrace.Core.Tests docs/architecture.md README.md
git commit -m "test(core): add determinism acceptance test, architecture doc and README"
```

---

## Definition of done for this plan

- `dotnet test` passes with every test above.
- `dotnet build --configuration Release` produces zero warnings.
- `Millrace.Core` and `Millrace.Io.Abstractions` have no external package references.
- A plant containing a composite, a feedback loop, randomness and logged events
  runs twice with the same seed and produces identical samples, telemetry and
  event-log text.
- Adding a component to a plant does not change any other component's random
  stream.
- Validation reports duplicate ids, unconnected required inputs and algebraic
  loops, and every message names the fix.
- `docs/architecture.md` documents the tick phases, the port model and the
  determinism rules a component author must follow.

## What this plan deliberately does not build

Material flow and belts (plan 2), industrial components (plan 3), the I/O
boundary and the real-time event and state engines (plan 4), the catalogue,
declarative configuration, CLI and scenarios (plan 5), and the reference samples
(plan 6). `Simulation.Tick` already calls the phase 3 and phase 4 hooks so those
plans slot in without restructuring the tick loop.
