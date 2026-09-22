using Dse.Core.Catalogue;
using Dse.Core.Flow;

namespace Dse.Cli.Tests.SampleModule;

/// <summary>What `dse --assembly` looks for: public, concrete, parameterless.</summary>
public sealed class SampleCatalogueModule : ICatalogueModule
{
    public string Name => "Sample";

    public void Register(CatalogueBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Add(HysteresisSwitch.Descriptor);
        builder.Add(new MaterialDescriptor(
            new MaterialType("sample-ore", PayloadKind.Bulk), new MaterialProperties(1600.0, 0.08, 10.0), "A material that ships with a module."));
    }
}
