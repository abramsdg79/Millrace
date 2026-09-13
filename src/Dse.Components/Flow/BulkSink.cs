using Dse.Core.Contexts;
using Dse.Core.Flow;
using Dse.Core.Graph;
using Dse.Core.Io;
using Dse.Core.Telemetry;

namespace Dse.Components.Flow;

/// <summary>
/// Where bulk material leaves the plant: a stockpile, a truck, a declared loss.
/// Everything deposited is removed from the ledger. With a capacity it fills,
/// says so once, and then accepts nothing, so the plant behind it backs up.
/// </summary>
public sealed class BulkSink : FlowComponentBase, IBulkConsumer, ITagProvider
{
    private double _received;
    private double _sinceEvaluate;
    private bool _full;
    private TelemetryHandle _receivedTelemetry;

    public BulkSink(string id, double capacityKg = double.PositiveInfinity)
        : base(id)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(capacityKg);
        CapacityKg = capacityKg;

        In = AddInlet("In", PayloadKind.Bulk);
        Received = AddOutput<double>("Received");
        Rate = AddOutput<double>("Rate");
        Full = AddOutput<bool>("Full");
    }

    public FlowInlet In { get; }

    /// <summary>Cumulative mass received, kg.</summary>
    public OutputPort<double> Received { get; }

    /// <summary>Mass received during the previous tick divided by dt, kg/s.</summary>
    public OutputPort<double> Rate { get; }

    public OutputPort<bool> Full { get; }

    /// <summary>kg. Infinite by default.</summary>
    public double CapacityKg { get; }

    /// <summary>Properties of the last lot deposited.</summary>
    public MaterialProperties LastProperties { get; private set; }

    /// <summary>Material type of the last lot deposited, or null before any.</summary>
    public MaterialType? LastType { get; private set; }

    public override double MassHeld => 0.0;

    public override double MassDestroyed => _received;

    public IEnumerable<TagBinding> DescribeTags() =>
    [
        TagBinding.Read("Received", Received, "kg", description: "Cumulative mass received"),
        TagBinding.Read("Rate", Rate, "kg/s", description: "Receiving rate"),
        TagBinding.Read("Full", Full, "At capacity"),
    ];

    public override void Initialize(in InitContext ctx) =>
        _receivedTelemetry = ctx.RegisterTelemetry("Received", "kg");

    public override void Evaluate(in TickContext ctx)
    {
        Received.Value = _received;
        Rate.Value = _sinceEvaluate / ctx.Dt;
        _sinceEvaluate = 0.0;
        _receivedTelemetry.Write(_received);

        bool full = _received >= CapacityKg;
        if (full && !_full)
        {
            ctx.Log(Id, "FULL", "Capacity reached; accepting nothing more.");
        }

        _full = full;
        Full.Value = full;
    }

    public double AcceptMass(FlowInlet inlet) => Math.Max(0.0, CapacityKg - _received);

    public void Deposit(FlowInlet inlet, in BulkLot lot)
    {
        _received += lot.Mass;
        _sinceEvaluate += lot.Mass;
        LastProperties = lot.Properties;
        LastType = lot.Type;
    }
}
