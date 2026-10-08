using System.Diagnostics.CodeAnalysis;

namespace Millrace.Core.Flow;

/// <summary>A node that discharges whole items through one or more outlets.</summary>
public interface IItemProducer
{
    /// <summary>The item ready to leave through <paramref name="outlet"/>, if any. Does not remove it.</summary>
    bool TryPeekItem(FlowOutlet outlet, [NotNullWhen(true)] out ItemInstance? item);

    /// <summary>Removes and returns the item last shown by <see cref="TryPeekItem"/> for this outlet.</summary>
    ItemInstance WithdrawItem(FlowOutlet outlet);
}
