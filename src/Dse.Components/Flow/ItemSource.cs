using System.Diagnostics.CodeAnalysis;
using Dse.Core.Contexts;
using Dse.Core.Faults;
using Dse.Core.Flow;
using Dse.Core.Graph;
using Dse.Core.Telemetry;

namespace Dse.Components.Flow;

/// <summary>
/// Where discrete items enter the plant: one item every interval, ids from
/// the simulation's sequence, queued until the outlet takes them. A full queue
/// pauses minting; the interval timer keeps running so cadence resumes cleanly.
/// </summary>
public sealed class ItemSource : FlowComponentBase, IItemProducer, IFaultTarget
{
    /// <summary>The supply runs out: nothing is minted until cleared.</summary>
    public const string Starve = "starve";

    private static readonly FaultDescriptor[] Faults =
    [
        new(Starve, "The supply runs out; nothing is minted until the fault is cleared."),
    ];

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
