using System.Diagnostics.CodeAnalysis;
using Millrace.Core.Flow;

namespace Millrace.Core.Tests.Fakes.Flow;

/// <summary>A FIFO hold for a fixed number of items.</summary>
public sealed class ItemBuffer : FlowComponentBase, IItemConsumer, IItemProducer
{
    private readonly Queue<ItemInstance> _items = new();

    public ItemBuffer(string id, int capacity)
        : base(id)
    {
        Capacity = capacity;
        In = AddInlet("In", PayloadKind.Discrete);
        Out = AddOutlet("Out", PayloadKind.Discrete);
    }

    public FlowInlet In { get; }

    public FlowOutlet Out { get; }

    public int Capacity { get; }

    public int Count => _items.Count;

    public override double MassHeld
    {
        get
        {
            double total = 0.0;
            foreach (ItemInstance item in _items)
            {
                total += item.Mass;
            }

            return total;
        }
    }

    public bool CanAcceptItem(FlowInlet inlet, ItemInstance item) => _items.Count < Capacity;

    public void DepositItem(FlowInlet inlet, ItemInstance item) => _items.Enqueue(item);

    public bool TryPeekItem(FlowOutlet outlet, [NotNullWhen(true)] out ItemInstance? item) =>
        _items.TryPeek(out item);

    public ItemInstance WithdrawItem(FlowOutlet outlet) => _items.Dequeue();
}
