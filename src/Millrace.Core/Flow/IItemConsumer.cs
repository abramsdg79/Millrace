namespace Millrace.Core.Flow;

/// <summary>A node that receives whole items through one or more inlets.</summary>
public interface IItemConsumer
{
    /// <summary>Whether <paramref name="item"/> fits through <paramref name="inlet"/> right now.</summary>
    bool CanAcceptItem(FlowInlet inlet, ItemInstance item);

    void DepositItem(FlowInlet inlet, ItemInstance item);
}
