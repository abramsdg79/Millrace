using Dse.Core.Contexts;
using Dse.Core.Faults;
using Dse.Core.Flow;
using Dse.Core.Graph;
using Dse.Core.Io;
using Dse.Core.Telemetry;

namespace Dse.Components.Flow;

/// <summary>
/// Where bulk material enters the plant: a feeder drawing from an unbounded
/// supply into a hopper, at a rate a signal may set. The hopper is what a
/// blocked outlet fills; once it is full the feeder waits, so nothing is
/// created that the plant cannot take. Mass is created in <see cref="Advance"/>.
/// </summary>
public sealed class BulkSource : FlowComponentBase, IBulkProducer, IFaultTarget, ITagProvider
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

    public IEnumerable<TagBinding> DescribeTags() =>
    [
        TagBinding.Write("Enabled", Enabled, "Feeder enabled"),
        TagBinding.Write("Rate", Rate, "kg/s", description: "Feed rate"),
        double.IsFinite(HopperCapacityKg)
            ? TagBinding.Read("HopperMass", HopperMass, "kg", 0.0, HopperCapacityKg, "Mass in the hopper")
            : TagBinding.Read("HopperMass", HopperMass, "kg", description: "Mass in the hopper"),
    ];

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
