using Dse.Core.Catalogue;
using Dse.Core.Testing;

namespace Dse.Components.Tests.Catalogue;

/// <summary>
/// The catalogue under test and what conformance needs to build a probe of each
/// type. Every descriptor rollout task extends the entries its types need.
/// </summary>
internal static class ComponentsFixtures
{
    public static ComponentCatalogue Catalogue { get; } = new CatalogueBuilder().Add<ComponentsModule>().Build();

    public static ConformanceFixtures Create() => new();
}
