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
