using System.Diagnostics.CodeAnalysis;
using Dse.Core.Contexts;
using Dse.Core.Flow;

namespace Dse.Core.Tests.Fakes.Flow;

/// <summary>Mints one item every interval, ids from the simulation's sequence, and queues them for discharge.</summary>
public sealed class ItemFeeder : FlowComponentBase, IItemProducer
{
    private readonly Queue<ItemInstance> _ready = new();
    private readonly MaterialType _type;
    private readonly double _itemMass;
    private ItemIdSequence? _ids;
    private double _elapsed;
    private double _created;

    public ItemFeeder(string id, MaterialType type, double itemMassKg, double intervalSeconds)
        : base(id)
    {
        _type = type;
        _itemMass = itemMassKg;
        IntervalSeconds = intervalSeconds;
        Out = AddOutlet("Out", PayloadKind.Discrete);
    }

    public FlowOutlet Out { get; }

    /// <summary>Seconds between items. Zero or negative stops production.</summary>
    public double IntervalSeconds { get; set; }

    public override double MassHeld
    {
        get
        {
            double total = 0.0;
            foreach (ItemInstance item in _ready)
            {
                total += item.Mass;
            }

            return total;
        }
    }

    public override double MassCreated => _created;

    public override void Initialize(in InitContext ctx) => _ids = ctx.Items;

    public override void Evaluate(in TickContext ctx)
    {
        if (IntervalSeconds <= 0.0)
        {
            return;
        }

        _elapsed += ctx.Dt;
        while (_elapsed >= IntervalSeconds - 1e-12)
        {
            _elapsed -= IntervalSeconds;
            _ready.Enqueue(new ItemInstance(_ids!.Next(), _type, _itemMass, default));
            _created += _itemMass;
        }
    }

    public bool TryPeekItem(FlowOutlet outlet, [NotNullWhen(true)] out ItemInstance? item) =>
        _ready.TryPeek(out item);

    public ItemInstance WithdrawItem(FlowOutlet outlet) => _ready.Dequeue();
}
