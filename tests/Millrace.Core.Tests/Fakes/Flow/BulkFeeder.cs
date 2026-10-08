using Millrace.Core.Contexts;
using Millrace.Core.Flow;

namespace Millrace.Core.Tests.Fakes.Flow;

/// <summary>
/// Creates bulk material at a rate each tick and offers everything it holds.
/// A blocked outlet accumulates, like a feed hopper.
/// </summary>
public sealed class BulkFeeder : FlowComponentBase, IBulkProducer
{
    private readonly MaterialType _type;
    private readonly MaterialProperties _properties;
    private BulkLot _hopper;
    private double _created;

    public BulkFeeder(string id, MaterialType type, double rateKgPerSecond, MaterialProperties properties = default)
        : base(id)
    {
        _type = type;
        _properties = properties;
        RateKgPerSecond = rateKgPerSecond;
        Out = AddOutlet("Out", PayloadKind.Bulk);
    }

    public FlowOutlet Out { get; }

    public double RateKgPerSecond { get; set; }

    public override double MassHeld => _hopper.Mass;

    public override double MassCreated => _created;

    public override void Evaluate(in TickContext ctx)
    {
        double mass = RateKgPerSecond * ctx.Dt;
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
}
