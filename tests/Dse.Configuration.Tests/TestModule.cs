using Dse.Components;
using Dse.Core.Catalogue;
using Dse.Core.Graph;

namespace Dse.Configuration.Tests;

/// <summary>Two types the shipped catalogue cannot provide: one that can form a reference cycle, one whose factory is defective.</summary>
internal sealed class TestModule : ICatalogueModule
{
    public string Name => "Test";

    public void Register(CatalogueBuilder builder)
    {
        builder.Add(new ComponentDescriptor("echo", ComponentCategory.Signal, "References another echo.", (id, p) => new UnitDelay<bool>(id))
        {
            Parameters = [Param.Reference<ISimComponent>("other", "Another component.", optional: true)],
            Ports = [PortSpec.In<bool>("In"), PortSpec.Out<bool>("Out")],
            Provides = [typeof(ISimComponent)],
        });
        builder.Add(new ComponentDescriptor("broken", ComponentCategory.Signal, "Its factory is wrong.", (id, p) => new UnitDelay<bool>(id, p.Bool("nope"))));
    }
}

internal static class TestPlants
{
    public static ComponentCatalogue Catalogue { get; } = new CatalogueBuilder().Add<ComponentsModule>().Add<TestModule>().Build();

    public static LoadResult Load(string json) => PlantLoader.Load(json, Catalogue);

    public static ConfigDiagnostic Only(string json) => Assert.Single(Load(json).Diagnostics);
}
