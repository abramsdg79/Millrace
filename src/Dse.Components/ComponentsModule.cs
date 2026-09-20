using Dse.Core.Catalogue;

namespace Dse.Components;

/// <summary>
/// Everything a plant can instantiate out of the box: this assembly's
/// components, transforms and hold conditions, and the nodes Core ships.
/// </summary>
public sealed class ComponentsModule : ICatalogueModule
{
    public string Name => "Dse.Components";

    public void Register(CatalogueBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Signal
        builder.Add(CoreDescriptors.UnitDelayBool);
        builder.Add(CoreDescriptors.UnitDelayDouble);
    }
}
