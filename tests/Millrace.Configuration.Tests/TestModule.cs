using Millrace.Components;
using Millrace.Control;
using Millrace.Core.Catalogue;
using Millrace.Core.Graph;
using Millrace.Io;

namespace Millrace.Configuration.Tests;

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
        builder.AddBlock(new BlockDescriptor(
            "broken-block",
            "Its factory is wrong.",
            (id, p) => [],
            (id, period, p) => throw new InvalidOperationException("bad wiring")));
        builder.AddBlock(new BlockDescriptor(
            "tagless",
            "Its owned-tag function is wrong.",
            (id, p) => throw new InvalidOperationException("no tags today"),
            (id, period, p) => throw new InvalidOperationException("never reached")));
        builder.AddBlock(new BlockDescriptor(
            "null-tags",
            "Its owned-tag function returns nothing at all.",
            (id, p) => null!,
            (id, period, p) => throw new InvalidOperationException("never reached")));
        builder.AddBlock(new BlockDescriptor(
            "null-block",
            "Its factory returns nothing at all.",
            (id, p) => [],
            (id, period, p) => null!));
        builder.AddBlock(new BlockDescriptor(
            "wrong-id",
            "Its factory ignores the id it is given.",
            (id, p) => [],
            (id, period, p) => new Permissive("OTHER", [new Condition("PILE.Full", false)], period)));
        builder.AddBlock(new BlockDescriptor(
            "throwing-outputs",
            "Its block's Outputs getter throws.",
            (id, p) => [],
            (id, period, p) => new DefectiveBlock(id, period) { OutputsOverride = () => throw new InvalidOperationException("no outputs today") }));
        builder.AddBlock(new BlockDescriptor(
            "null-inputs",
            "Its block's Inputs getter returns null.",
            (id, p) => [],
            (id, period, p) => new DefectiveBlock(id, period) { InputsOverride = () => null! }));
        builder.AddBlock(new BlockDescriptor(
            "null-command",
            "Its block's Commands list holds a null.",
            (id, p) => [],
            (id, period, p) => new DefectiveBlock(id, period) { CommandsOverride = () => [null!] }));
        builder.AddBlock(new BlockDescriptor(
            "misdeclared",
            "Declares its output read-write; the block publishes it read-only.",
            (id, p) => [(new TagSpec($"{id}.Ok", TagKind.Bool), TagAccess.ReadWrite)],
            (id, period, p) => new DefectiveBlock(id, period) { OutputsOverride = () => [new TagSpec("Ok", TagKind.Bool)] }));
    }
}

/// <summary>A block with no pins unless an override says otherwise; the overrides model a defective module.</summary>
internal sealed class DefectiveBlock(string id, TimeSpan scanPeriod) : IScanBlock
{
    public Func<IReadOnlyList<TagRef>> InputsOverride { get; init; } = () => [];

    public Func<IReadOnlyList<TagRef>> WritesOverride { get; init; } = () => [];

    public Func<IReadOnlyList<TagSpec>> OutputsOverride { get; init; } = () => [];

    public Func<IReadOnlyList<TagSpec>> CommandsOverride { get; init; } = () => [];

    public string Id { get; } = id;

    public TimeSpan ScanPeriod { get; } = scanPeriod;

    public IReadOnlyList<TagRef> Inputs => InputsOverride();

    public IReadOnlyList<TagRef> Writes => WritesOverride();

    public IReadOnlyList<TagSpec> Outputs => OutputsOverride();

    public IReadOnlyList<TagSpec> Commands => CommandsOverride();

    public void Scan(in ScanInputs inputs, ref ScanOutputs outputs)
    {
    }
}

internal static class TestPlants
{
    public static ComponentCatalogue Catalogue { get; } = new CatalogueBuilder().Add<ComponentsModule>().Add<TestModule>().Build();

    public static LoadResult Load(string json) => PlantLoader.Load(json, Catalogue);

    public static ConfigDiagnostic Only(string json) => Assert.Single(Load(json).Diagnostics);
}
