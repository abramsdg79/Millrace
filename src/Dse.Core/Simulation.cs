using Dse.Core.Contexts;
using Dse.Core.Events;
using Dse.Core.Flow;
using Dse.Core.Graph;
using Dse.Core.Logging;
using Dse.Core.Randomness;
using Dse.Core.Telemetry;
using Dse.Core.Time;

namespace Dse.Core;

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

    /// <summary>The item id counter shared by every component in this simulation.</summary>
    public ItemIdSequence Items { get; } = new();

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
                Items,
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

    /// <summary>
    /// Two passes over the resolved order. First every component evaluates,
    /// reading inputs already written this tick by its producers. Then every
    /// component latches, capturing this tick's inputs for components (such as
    /// <see cref="UnitDelay{T}"/>) that hold state across ticks. Splitting the
    /// passes is what keeps a delay's lag at exactly one tick no matter where
    /// the resolver places it relative to its producer.
    /// </summary>
    private void EvaluateSignals()
    {
        var context = new TickContext(
            Clock.TickCount, Clock.DeltaSeconds, Clock.Now, Events);

        foreach (ISimComponent component in _components)
        {
            component.Evaluate(context);
        }

        foreach (ISimComponent component in _components)
        {
            component.Latch();
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
