namespace Dse.Core.Flow;

/// <summary>A node that receives bulk material through one or more inlets.</summary>
public interface IBulkConsumer
{
    /// <summary>Kilograms this node can take through <paramref name="inlet"/> this tick. Infinity for an unbounded sink.</summary>
    double AcceptMass(FlowInlet inlet);

    void Deposit(FlowInlet inlet, in BulkLot lot);
}
