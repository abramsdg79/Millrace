using Dse.Core.Testing;

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
}
