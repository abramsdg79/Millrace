using Millrace.Core.Catalogue;
using Millrace.Core.Flow;

namespace Millrace.Cli.Tests.SampleModule;

/// <summary>What `millrace --assembly` looks for: public, concrete, parameterless.</summary>
public sealed class SampleCatalogueModule : ICatalogueModule
{
    public string Name => "Sample";

    public void Register(CatalogueBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Add(HysteresisSwitch.Descriptor);
        builder.AddBlock(Latch.Descriptor);
        builder.Add(new MaterialDescriptor(
            new MaterialType("sample-ore", PayloadKind.Bulk), new MaterialProperties(1600.0, 0.08, 10.0), "A material that ships with a module."));
    }
}
