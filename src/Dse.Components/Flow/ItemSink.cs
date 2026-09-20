using Dse.Core.Catalogue;
using Dse.Core.Contexts;
using Dse.Core.Flow;
using Dse.Core.Graph;
using Dse.Core.Io;
using Dse.Core.Telemetry;
using Dse.Io;

namespace Dse.Components.Flow;

/// <summary>
/// Where items leave the plant. Keeps a count, the mass, and the last item
/// (so a test can inspect what arrived) but not the items themselves — a
/// long run must not grow without bound.
/// </summary>
public sealed class ItemSink : FlowComponentBase, IItemConsumer, ITagProvider
{
    public static ComponentDescriptor Descriptor { get; } = new(
        "item-sink",
        ComponentCategory.Flow,
        "Accepts discrete items and destroys them, up to an optional capacity.",
        (id, p) => new ItemSink(id, p.IntOr("capacity", int.MaxValue)))
    {
        Parameters = [Param.Int("capacity", "How many items it will take. Omit for unlimited.", "count", min: 0, optional: true)],
        Ports = [PortSpec.Out<long>("Count", "count"), PortSpec.Out<bool>("Full")],
        FlowPorts = [PortSpec.Inlet("In", PayloadKind.Discrete)],
        Tags = [new TagEntry("Count", TagKind.Int64, TagAccess.ReadOnly, "count"), new TagEntry("Full", TagKind.Bool, TagAccess.ReadOnly)],
        Telemetry = [new TelemetryKey("Received", "count")],
    };

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

    public IEnumerable<TagBinding> DescribeTags() =>
    [
        TagBinding.Read("Count", Count, "count", "Items received"),
        TagBinding.Read("Full", Full, "At capacity"),
    ];

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
