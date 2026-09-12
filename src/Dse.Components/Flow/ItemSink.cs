using Dse.Core.Contexts;
using Dse.Core.Flow;
using Dse.Core.Graph;
using Dse.Core.Telemetry;

namespace Dse.Components.Flow;

/// <summary>
/// Where items leave the plant. Keeps a count, the mass, and the last item
/// (so a test can inspect what arrived) but not the items themselves — a
/// long run must not grow without bound.
/// </summary>
public sealed class ItemSink : FlowComponentBase, IItemConsumer
{
    private long _count;
    private double _mass;
    private bool _full;
    private TelemetryHandle _receivedTelemetry;

    public ItemSink(string id, int capacity = int.MaxValue)
        : base(id)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(capacity);
        Capacity = capacity;

        In = AddInlet("In", PayloadKind.Discrete);
        Count = AddOutput<long>("Count");
        Full = AddOutput<bool>("Full");
    }

    public FlowInlet In { get; }

    /// <summary>Items received, cumulative.</summary>
    public OutputPort<long> Count { get; }

    public OutputPort<bool> Full { get; }

    public int Capacity { get; }

    public ItemInstance? LastItem { get; private set; }

    /// <summary>kg, cumulative.</summary>
    public double MassReceived => _mass;

    public override double MassHeld => 0.0;

    public override double MassDestroyed => _mass;

    public override void Initialize(in InitContext ctx) =>
        _receivedTelemetry = ctx.RegisterTelemetry("Received", "count");

    public override void Evaluate(in TickContext ctx)
    {
        Count.Value = _count;
        _receivedTelemetry.Write(_count);

        bool full = _count >= Capacity;
        if (full && !_full)
        {
            ctx.Log(Id, "FULL", "Capacity reached; accepting nothing more.");
        }

        _full = full;
        Full.Value = full;
    }

    public bool CanAcceptItem(FlowInlet inlet, ItemInstance item) => _count < Capacity;

    public void DepositItem(FlowInlet inlet, ItemInstance item)
    {
        _count++;
        _mass += item.Mass;
        LastItem = item;
    }
}
