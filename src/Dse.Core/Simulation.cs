using Dse.Core.Contexts;
using Dse.Core.Control;
using Dse.Core.Events;
using Dse.Core.Faults;
using Dse.Core.Flow;
using Dse.Core.Graph;
using Dse.Core.Io;
using Dse.Core.Logging;
using Dse.Core.Randomness;
using Dse.Core.Telemetry;
using Dse.Core.Time;
using Dse.Io;

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
    private readonly ScanBlockPlan[] _blockPlans;
    private bool _initialized;

    internal Simulation(
        ISimComponent[] components,
        FlowGraph flow,
        SimulationOptions options,
        TagImage io,
        ScanBlockPlan[] blocks)
    {
        _components = components;
        _flow = flow;
        _seed = options.Seed;
        _checkConservation = options.CheckConservation;
        _conservationTolerance = options.ConservationTolerance;
        _blockPlans = blocks;
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

    public SimulationClock Clock { get; }

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

    /// <summary>Where every action that takes effect is reported, or null when nothing is attached.</summary>
    public IActionRecorder? ActionRecorder { get; private set; }

    /// <summary>
    /// Attaches the one action recorder (spec 5b §6.2). May be called at any
    /// time before or during a run, from the thread that ticks — a late-attached
    /// recorder sees actions from then on — but only once, as
    /// <see cref="AttachFrameSink"/> is.
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

    public TelemetryRegistry Telemetry { get; } = new();

    public EventLog Events { get; } = new();

    /// <summary>The item id counter shared by every component in this simulation.</summary>
    public ItemIdSequence Items { get; } = new();

    public IReadOnlyList<ISimComponent> Components => _components;

    /// <summary>How many control blocks are attached (spec 5c §3). Zero for a plant with none.</summary>
    public int ScanBlockCount => _blockPlans.Length;

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

        IO.Prime();
        _initialized = true;
    }

    /// <summary>Advances the plant by exactly one time step, in the five fixed phases.</summary>
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

    /// <summary>Phase 5. Wraps the phase-4 snapshot and this tick's events into a frame, if anyone is listening.</summary>
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
                _simulation.ActionRecorder?.Cleared(clock.TickCount, _target.Id, _faultId);
                return;
            }

            _target.ApplyFault(_faultId, _arguments);
            string detail = _arguments.Count == 0 ? string.Empty : $": {_arguments}";
            _simulation.Events.Record(clock.TickCount, clock.Now, _target.Id, "FAULT", $"{_faultId} injected{detail}.");
            _simulation.ActionRecorder?.Faulted(clock.TickCount, _target.Id, _faultId, _arguments);
        }
    }
}
