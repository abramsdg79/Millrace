namespace Millrace.Core.Graph;

/// <summary>
/// A node that can hand out something it does not itself implement — a conveyor
/// offering its belt as an <c>IMaterialObservable</c>. Lets a reference name the
/// composite instead of an inner leaf.
/// </summary>
public interface ICapabilityProvider
{
    /// <summary>True, with the instance, when this node can supply <paramref name="capability"/>.</summary>
    bool TryGetCapability(Type capability, out object? instance);
}
