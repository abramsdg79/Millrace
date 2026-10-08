using Millrace.Core.Flow;

namespace Millrace.Components.Flow;

/// <summary>
/// When a batch is done: a fixed time, a property threshold, an accumulated
/// state, or a combination. Evaluated once per tick against the batch (bulk)
/// or against every item (discrete).
/// </summary>
public interface IHoldCondition
{
    bool IsSatisfied(double elapsedSeconds, in MaterialProperties properties, ReadOnlySpan<double> state);
}
