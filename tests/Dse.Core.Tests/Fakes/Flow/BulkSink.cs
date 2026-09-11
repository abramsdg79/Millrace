using Dse.Core.Flow;

namespace Dse.Core.Tests.Fakes.Flow;

/// <summary>Accepts everything and removes it from the system, recording each deposit.</summary>
public sealed class BulkSink : FlowComponentBase, IBulkConsumer
{
    private readonly List<double> _received = [];
    private double _sunk;

    public BulkSink(string id)
        : base(id) => In = AddInlet("In", PayloadKind.Bulk);

    public FlowInlet In { get; }

    /// <summary>Mass of each deposit, in order.</summary>
    public IReadOnlyList<double> Received => _received;

    public double TotalReceived => _sunk;

    public MaterialProperties LastProperties { get; private set; }

    public override double MassHeld => 0.0;

    public override double MassDestroyed => _sunk;

    public double AcceptMass(FlowInlet inlet) => double.PositiveInfinity;

    public void Deposit(FlowInlet inlet, in BulkLot lot)
    {
        _sunk += lot.Mass;
        _received.Add(lot.Mass);
        LastProperties = lot.Properties;
    }
}
