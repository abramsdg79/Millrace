using System.Diagnostics.CodeAnalysis;
using Millrace.Core.Catalogue;
using Millrace.Core.Contexts;
using Millrace.Core.Faults;
using Millrace.Core.Flow;
using Millrace.Core.Graph;
using Millrace.Core.Io;
using Millrace.Core.Telemetry;
using Millrace.Io;

namespace Millrace.Components.Flow;

/// <summary>
/// Where discrete items enter the plant: one item every interval, ids from
/// the simulation's sequence, queued until the outlet takes them. A full queue
/// pauses minting; the interval timer keeps running so cadence resumes cleanly.
/// </summary>
public sealed class ItemSource : FlowComponentBase, IItemProducer, IFaultTarget, ITagProvider
{
    /// <summary>The supply runs out: nothing is minted until cleared.</summary>
    public const string Starve = "starve";

    private static readonly FaultDescriptor[] Faults =
    [
        new(Starve, "The supply runs out; nothing is minted until the fault is cleared."),
    ];

    public static ComponentDescriptor Descriptor { get; } = new(
        "item-source",
        ComponentCategory.Flow,
        "Mints one discrete item at a fixed interval and queues it at the outlet.",
        (id, p) => new ItemSource(
            id, p.Material("material"), p.Double("itemMassKg"), p.Double("intervalSeconds"), p.MaterialProperties("material"),
            p.IntOr("queueCapacity", int.MaxValue)))
    {
        Parameters =
        [
            Param.Material("material", "What each item is; new items take this material's defined properties.", PayloadKind.Discrete),
            Param.Double("itemMassKg", "Mass of one item.", "kg", min: 0.0, exclusiveMin: true),
            Param.Double("intervalSeconds", "Time between items.", "s", min: 0.0, exclusiveMin: true),
            Param.Int("queueCapacity", "Items that may wait at the outlet. Omit for unlimited.", "count", min: 1, optional: true),
        ],
        Ports = [PortSpec.In<bool>("Enabled", description: "Defaults to true."), PortSpec.Out<int>("Queued", "count")],
        FlowPorts = [PortSpec.Outlet("Out", PayloadKind.Discrete)],
        Faults = Faults,
        Tags = [new TagEntry("Enabled", TagKind.Bool, TagAccess.ReadWrite), new TagEntry("Queued", TagKind.Int64, TagAccess.ReadOnly, "count")],
        Telemetry = [new TelemetryKey("Sourced", "count")],
    };

    private readonly Queue<ItemInstance> _ready = new();
    private readonly MaterialType _type;
    private readonly MaterialProperties _properties;
    private ItemIdSequence? _ids;
    private double _elapsed;
    private double _created;
    private long _minted;
    private bool _starved;
    private TelemetryHandle _sourcedTelemetry;

    public ItemSource(
        string id,
        MaterialType type,
        double itemMassKg,
        double intervalSeconds,
        MaterialProperties properties = default,
        int queueCapacity = int.MaxValue)
        : base(id)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(itemMassKg);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(intervalSeconds);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(queueCapacity);

        _type = type;
        _properties = properties;
        ItemMassKg = itemMassKg;
        IntervalSeconds = intervalSeconds;
        QueueCapacity = queueCapacity;

        Out = AddOutlet("Out", PayloadKind.Discrete);
        Enabled = AddInput<bool>("Enabled", defaultValue: true);
        Queued = AddOutput<int>("Queued");
    }

    public FlowOutlet Out { get; }

    /// <summary>False pauses minting. Unconnected reads true.</summary>
    public InputPort<bool> Enabled { get; }

    /// <summary>Items waiting at the outlet, as of the last evaluate.</summary>
    public OutputPort<int> Queued { get; }

    /// <summary>kg per item.</summary>
    public double ItemMassKg { get; }

    /// <summary>Seconds between items.</summary>
    public double IntervalSeconds { get; }

    public int QueueCapacity { get; }

    public override double MassHeld => _ready.Count * ItemMassKg;

    public override double MassCreated => _created;

    public IReadOnlyList<FaultDescriptor> SupportedFaults => Faults;

    public IEnumerable<TagBinding> DescribeTags() =>
    [
        TagBinding.Write("Enabled", Enabled, "Minting enabled"),
        TagBinding.Read("Queued", Queued, "count", "Items waiting at the outlet"),
    ];

    public override void Initialize(in InitContext ctx)
    {
        _ids = ctx.Items;
        _sourcedTelemetry = ctx.RegisterTelemetry("Sourced", "count");
    }

    public override void Evaluate(in TickContext ctx)
    {
        Queued.Value = _ready.Count;
        _sourcedTelemetry.Write(_minted);
    }

    public override void Advance(in TickContext ctx)
    {
        if (_starved || !Enabled.Value)
        {
            return;
        }

        _elapsed += ctx.Dt;
        while (_elapsed >= IntervalSeconds - 1e-12)
        {
            _elapsed -= IntervalSeconds;
            if (_ready.Count >= QueueCapacity)
            {
                continue;
            }

            _ready.Enqueue(new ItemInstance(_ids!.Next(), _type, ItemMassKg, _properties));
            _created += ItemMassKg;
            _minted++;
        }
    }

    public bool TryPeekItem(FlowOutlet outlet, [NotNullWhen(true)] out ItemInstance? item) =>
        _ready.TryPeek(out item);

    public ItemInstance WithdrawItem(FlowOutlet outlet) => _ready.Dequeue();

    public void ApplyFault(string faultId, FaultArguments arguments) => _starved = true;

    public void ClearFault(string faultId) => _starved = false;
}
