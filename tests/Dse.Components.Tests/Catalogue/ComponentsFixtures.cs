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

    public static ConformanceFixtures Create() => new ConformanceFixtures()
        // Task 5 — mechanical
        .Parameters("motor", """{ "rating": { "ratedPowerW": 750, "ratedSpeedRadPerS": 150, "ratedCurrentA": 2 } }""")
        .Parameters("gearbox", """{ "ratio": 20 }""")
        .Parameters("drive-pulley", """{ "diameterM": 0.5 }""")
        .Parameters("tail-pulley", """{ "bearingDragN": 80 }""")
        .Parameters("belt-friction", """{ "emptyBeltMassKg": 250 }""");
}
