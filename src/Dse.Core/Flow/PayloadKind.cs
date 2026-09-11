namespace Dse.Core.Flow;

/// <summary>What a flow node holds and a flow port carries. A node handles exactly one kind.</summary>
public enum PayloadKind
{
    /// <summary>Mass in kilograms plus blended intensive properties.</summary>
    Bulk,

    /// <summary>Whole <see cref="ItemInstance"/> values that move one at a time.</summary>
    Discrete,
}
