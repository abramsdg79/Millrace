using System.Globalization;
using Dse.Core.Contexts;
using Dse.Core.Events;
using Dse.Core.Faults;
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
    private readonly FlowGraph _flow;
    private readonly bool _checkConservation;
    private readonly double _conservationTolerance;
    private readonly Dictionary<string, IFaultTarget> _faultTargets;
    private bool _initialized;

    internal Simulation(ISimComponent[] components, FlowGraph flow, SimulationOptions options)
    {
        _components = components;
        _flow = flow;
        _seed = options.Seed;
        _checkConservation = options.CheckConservation;
        _conservationTolerance = options.ConservationTolerance;
        Clock = new SimulationClock(options.StartTime, options.TimeStep);

        _faultTargets = new Dictionary<string, IFaultTarget>(StringComparer.Ordinal);
        foreach (ISimComponent component in components)
        {
            if (component is IFaultTarget target)
            {
                _faultTargets[component.Id] = target;
            }
        }
    }

    public SimulationClock Clock { get; }

    public TelemetryRegistry Telemetry { get; } = new();

    public EventLog Events { get; } = new();

    /// <summary>The item id counter shared by every component in this simulation.</summary>
    public ItemIdSequence Items { get; } = new();

    public IReadOnlyList<ISimComponent> Components => _components;

    /// <summary>The plant-wide mass ledger: sourced, sunk, held and their drift.</summary>
    public MassBalance MassBalance => _flow.Balance();

    /// <summary>Schedules an event at a simulation time measured from the start.</summary>
    public long ScheduleAt(TimeSpan fromStart, ISimEvent simEvent) =>
        _queue.Schedule(fromStart.Ticks / Clock.TimeStep.Ticks, simEvent);

    /// <summary>Schedules an event relative to the current simulation time.</summary>
    public long ScheduleIn(TimeSpan delay, ISimEvent simEvent) =>
        _queue.Schedule(Clock.TickCount + (delay.Ticks / Clock.TimeStep.Ticks), simEvent);

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

        var context = new TickContext(
            Clock.TickCount, Clock.DeltaSeconds, Clock.Now, Events);

        DrainDueEvents();             // phase 1
        EvaluateSignals(in context);  // phase 2
        AdvanceFlow(in context);      // phase 3
        PublishIo();                  // phase 4
        EmitFrame();                  // phase 5

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
    private void EvaluateSignals(in TickContext context)
    {
        foreach (ISimComponent component in _components)
        {
            component.Evaluate(context);
        }

        foreach (ISimComponent component in _components)
        {
            component.Latch();
        }
    }

    /// <summary>
    /// Phase 3. One downstream-first sweep over the material graph — each node
    /// discharges into consumers that have already made room, then advances its
    /// own contents — followed by the conservation audit.
    /// </summary>
    private void AdvanceFlow(in TickContext context)
    {
        _flow.Step(in context);
        if (_checkConservation)
        {
            _flow.AssertConserved(Clock.TickCount, _conservationTolerance);
        }
    }

    /// <summary>Phase 4. The I/O image arrives in plan 4.</summary>
    private static void PublishIo()
    {
    }

    /// <summary>Phase 5. Tick frames arrive in plan 4; the event log is already appended during evaluation.</summary>
    private static void EmitFrame()
    {
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
}
