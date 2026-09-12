using Dse.Core.Graph;

namespace Dse.Core.Faults;

/// <summary>
/// A component that can be broken on purpose. Faults arrive in phase 1 of the
/// tick they are due, with every declared parameter present, and change the
/// component's state and behaviour only — never its ports or its wiring, so
/// injection cannot perturb evaluation order.
/// </summary>
public interface IFaultTarget : ISimComponent
{
    IReadOnlyList<FaultDescriptor> SupportedFaults { get; }

    /// <summary>Applies or re-applies a fault. Called with arguments resolved against the descriptor.</summary>
    void ApplyFault(string faultId, FaultArguments arguments);

    /// <summary>Removes a fault's effect. Clearing a fault that is not active is a no-op.</summary>
    void ClearFault(string faultId);
}
