using System.Text.Json;
using Millrace.Core.Catalogue;
using Millrace.Tests.Shared;

namespace Millrace.Components.Tests.Catalogue;

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

        Assert.Equal(30, document.RootElement.GetProperty("components").GetArrayLength());
        Assert.Equal(8, document.RootElement.GetProperty("objects").GetArrayLength());
        Assert.Equal(0, document.RootElement.GetProperty("materials").GetArrayLength());
    }
}
