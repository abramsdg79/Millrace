using Millrace.Core.Catalogue;
using Millrace.Core.Graph;

namespace Millrace.Cli.Tests.ClashModule;

/// <summary>Registers a type name the shipped catalogue already has, to prove the clash is reported and not swallowed.</summary>
public sealed class ClashCatalogueModule : ICatalogueModule
{
    public string Name => "Clash";

    public void Register(CatalogueBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Add(new ComponentDescriptor("gearbox", ComponentCategory.Mechanical, "Not the real one.", (id, p) => new UnitDelay<bool>(id)));
    }
}
