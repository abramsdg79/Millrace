using System.Text.Json;
using Millrace.Core.Catalogue;
using Millrace.Core.Testing;
using Millrace.Io;
using Millrace.Tests.Shared;

namespace Millrace.Control.Catalogue.Tests;

public class ControlCatalogueTests
{
    private static readonly ConformanceReport Report =
        CatalogueConformance.Check(ControlFixtures.Catalogue, ControlFixtures.Create());

    [Fact]
    public void EveryDescriptorMatchesWhatItBuilds()
    {
        Assert.Empty(Report.Mismatches);
    }

    [Fact]
    public void EveryPublicScanBlockInMillraceControlHasADescriptor()
    {
        List<Type> blocks = typeof(Timer).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false, IsPublic: true } && typeof(IScanBlock).IsAssignableFrom(t))
            .ToList();
        var built = new HashSet<Type>(Report.BuiltTypes);

        Assert.Equal(6, blocks.Count);
        Assert.Empty(blocks.Where(t => !built.Contains(t)).Select(t => t.FullName));
    }

    [Fact]
    public void TheModuleRegistersSixBlocksAndTwoTransitions()
    {
        ComponentCatalogue catalogue = ControlFixtures.Catalogue;

        Assert.Equal(["Millrace.Control"], catalogue.Modules);
        Assert.Empty(catalogue.Components);
        Assert.Equal(["alarm", "coil", "interlock", "permissive", "sequencer", "timer"], catalogue.Blocks.Select(b => b.Type));
        Assert.Equal(
            new[] { ("transition", "after"), ("transition", "when") },
            catalogue.Objects.Select(o => (o.Slot, o.Type)));
    }

    [Fact]
    public void TheControlCatalogueExportsExactlyTheGoldenFile()
    {
        Golden.Assert("Golden/control-catalogue.json", CatalogueJson.Export(ControlFixtures.Catalogue));
    }

    [Fact]
    public void TheControlCatalogueHasTheExpectedCounts()
    {
        using JsonDocument document = JsonDocument.Parse(CatalogueJson.Export(ControlFixtures.Catalogue));
        JsonElement root = document.RootElement;

        Assert.Empty(root.GetProperty("components").EnumerateArray());
        Assert.Equal(6, root.GetProperty("blocks").GetArrayLength());
        Assert.Equal(2, root.GetProperty("objects").GetArrayLength());
        Assert.Empty(root.GetProperty("materials").EnumerateArray());
    }
}
