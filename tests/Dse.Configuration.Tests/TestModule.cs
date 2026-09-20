using Dse.Components;
using Dse.Core.Catalogue;
using Dse.Core.Graph;

namespace Dse.Configuration.Tests;

/// <summary>Types the shipped catalogue cannot provide: reference cycles, a nested reference, a defective factory.</summary>
internal sealed class TestModule : ICatalogueModule
{
    /// <summary>Shared by <c>link</c> and <c>links</c> on <c>nested-echo</c>.</summary>
    private static readonly GroupDefinition LinkGroup = new("Link", Param.Reference<ISimComponent>("target", "The target.", optional: true));

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
        builder.Add(new ComponentDescriptor("silent", ComponentCategory.Signal, "Its factory throws a message with no full stop.", (id, p) => throw new InvalidOperationException("bad wiring")));
        builder.Add(new ComponentDescriptor("nested-echo", ComponentCategory.Signal, "References other echoes through a group and a group list.", (id, p) => new UnitDelay<bool>(id))
        {
            Parameters = [Param.Group("link", "A grouped reference.", LinkGroup), Param.GroupList("links", "Several.", LinkGroup)],
            Ports = [PortSpec.In<bool>("In"), PortSpec.Out<bool>("Out")],
            Provides = [typeof(ISimComponent)],
        });
        builder.Add(new ObjectDescriptor("probe", "watch", "Watches a component.", p => new object())
        {
            Parameters = [Param.Reference<ISimComponent>("target", "What it watches.")],
        });
        builder.Add(new ComponentDescriptor("object-echo", ComponentCategory.Signal, "References other echoes through objects in a slot.", (id, p) => new UnitDelay<bool>(id))
        {
            Parameters = [Param.ObjectList("probes", "Probes.", "probe")],
            Ports = [PortSpec.In<bool>("In"), PortSpec.Out<bool>("Out")],
            Provides = [typeof(ISimComponent)],
        });
    }
}

internal static class TestPlants
{
    public static ComponentCatalogue Catalogue { get; } = new CatalogueBuilder().Add<ComponentsModule>().Add<TestModule>().Build();

    public static LoadResult Load(string json) => PlantLoader.Load(json, Catalogue);

    public static ConfigDiagnostic Only(string json) => Assert.Single(Load(json).Diagnostics);
}
