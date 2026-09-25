using Dse.Core.Catalogue;
using Dse.Core.Testing;
using Dse.Io;

namespace Dse.Control.Catalogue.Tests;

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
    public void EveryPublicScanBlockInDseControlHasADescriptor()
    {
        List<Type> blocks = typeof(Timer).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false, IsPublic: true } && typeof(IScanBlock).IsAssignableFrom(t))
            .ToList();
        var built = new HashSet<Type>(Report.BuiltTypes);

        Assert.Equal(5, blocks.Count);
        Assert.Empty(blocks.Where(t => !built.Contains(t)).Select(t => t.FullName));
    }

    [Fact]
    public void TheModuleRegistersFiveBlocksAndTwoTransitions()
    {
        ComponentCatalogue catalogue = ControlFixtures.Catalogue;

        Assert.Equal(["Dse.Control"], catalogue.Modules);
        Assert.Empty(catalogue.Components);
        Assert.Equal(["alarm", "interlock", "permissive", "sequencer", "timer"], catalogue.Blocks.Select(b => b.Type));
        Assert.Equal(
            new[] { ("transition", "after"), ("transition", "when") },
            catalogue.Objects.Select(o => (o.Slot, o.Type)));
    }
}
