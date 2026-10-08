using Millrace.Core.Flow;

namespace Millrace.Core.Tests.Fakes.Flow;

/// <summary>A capacity-limited hold, like a transfer chute. Offers everything it holds.</summary>
public sealed class BulkBuffer : FlowComponentBase, IBulkConsumer, IBulkProducer
{
    private BulkLot _held;

    public BulkBuffer(string id, double capacityKg)
        : base(id)
    {
        CapacityKg = capacityKg;
        In = AddInlet("In", PayloadKind.Bulk);
        Out = AddOutlet("Out", PayloadKind.Bulk);
    }

    public FlowInlet In { get; }

    public FlowOutlet Out { get; }

    public double CapacityKg { get; }

    public BulkLot Contents => _held;

    public override double MassHeld => _held.Mass;

    public double AcceptMass(FlowInlet inlet) => Math.Max(0.0, CapacityKg - _held.Mass);

    public void Deposit(FlowInlet inlet, in BulkLot lot) => _held = _held.Merge(lot);

    public double OfferMass(FlowOutlet outlet) => _held.Mass;

    public BulkLot Withdraw(FlowOutlet outlet, double mass)
    {
        BulkLot taken = _held.Take(mass, out BulkLot remaining);
        _held = remaining;
        return taken;
    }
}
