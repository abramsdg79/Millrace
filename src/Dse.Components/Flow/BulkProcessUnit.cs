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
