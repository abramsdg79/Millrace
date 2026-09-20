using System.Text.Json;
using Dse.Core.Catalogue;
using Dse.Tests.Shared;

namespace Dse.Components.Tests.Catalogue;

public class ComponentsExportTests
{
    [Fact]
    public void TheShippedCatalogueExportsExactlyTheGoldenFile()
    {
        Golden.Assert("Golden/components-catalogue.json", CatalogueJson.Export(ComponentsFixtures.Catalogue));
    }

    [Fact]
    public void TheShippedCatalogueHasTheExpectedCounts()
    {
        using JsonDocument document = JsonDocument.Parse(CatalogueJson.Export(ComponentsFixtures.Catalogue));

        Assert.Equal(29, document.RootElement.GetProperty("components").GetArrayLength());
        Assert.Equal(8, document.RootElement.GetProperty("objects").GetArrayLength());
        Assert.Equal(0, document.RootElement.GetProperty("materials").GetArrayLength());
    }
}
