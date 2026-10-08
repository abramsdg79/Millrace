using Millrace.Core.Flow;

namespace Millrace.Core.Tests.Fakes.Flow;

/// <summary>Accepts every item and removes it from the system.</summary>
public sealed class ItemSink : FlowComponentBase, IItemConsumer
{
    private readonly List<ItemInstance> _items = [];
    private double _sunk;

    public ItemSink(string id)
        : base(id) => In = AddInlet("In", PayloadKind.Discrete);

    public FlowInlet In { get; }

    public IReadOnlyList<ItemInstance> Items => _items;

    public override double MassHeld => 0.0;

    public override double MassDestroyed => _sunk;

    public bool CanAcceptItem(FlowInlet inlet, ItemInstance item) => true;

    public void DepositItem(FlowInlet inlet, ItemInstance item)
    {
        _items.Add(item);
        _sunk += item.Mass;
    }
}
