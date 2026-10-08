using Millrace.Core.Graph;

namespace Millrace.Core.Catalogue;

/// <summary>Finds what a reference needs on a node: the node itself, or something it provides.</summary>
public static class Capabilities
{
    public static bool TryGet(ISimNode node, Type capability, out object? instance)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(capability);
        if (capability.IsInstanceOfType(node))
        {
            instance = node;
            return true;
        }

        if (node is ICapabilityProvider provider && provider.TryGetCapability(capability, out instance) && instance is not null)
        {
            return true;
        }

        instance = null;
        return false;
    }
}
