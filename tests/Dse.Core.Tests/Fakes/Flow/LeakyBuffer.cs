using Dse.Core.Flow;

namespace Dse.Core.Tests.Fakes.Flow;

/// <summary>
/// Keeps only a fraction of what it is given and does not report the rest as
/// destroyed — a deliberately broken node for exercising the conservation audit.
/// </summary>
public sealed class LeakyBuffer : FlowComponentBase, IBulkConsumer, IBulkProducer
{
    private BulkLot _held;

    public LeakyBuffer(string id, double keepFraction)
        : base(id)
    {
        KeepFraction = keepFraction;
        In = AddInlet("In", PayloadKind.Bulk);
        Out = AddOutlet("Out", PayloadKind.Bulk);
    }

    public FlowInlet In { get; }

    public FlowOutlet Out { get; }

    public double KeepFraction { get; }

    public override double MassHeld => _held.Mass;

    public double AcceptMass(FlowInlet inlet) => double.PositiveInfinity;

    public void Deposit(FlowInlet inlet, in BulkLot lot) =>
        _held = _held.Merge(lot.Take(lot.Mass * KeepFraction, out _));

    public double OfferMass(FlowOutlet outlet) => _held.Mass;

    public BulkLot Withdraw(FlowOutlet outlet, double mass)
    {
        BulkLot taken = _held.Take(mass, out BulkLot remaining);
        _held = remaining;
        return taken;
    }
}
