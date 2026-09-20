using Dse.Core.Catalogue;
using Dse.Core.Flow;
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
        .Parameters("belt-friction", """{ "emptyBeltMassKg": 250 }""")
        // Task 6 — instruments and safety
        .Node(new BulkBelt("BELT", 10.0, 0.5, 2.0, 100.0))
        .Parameters("speed-sensor", """{ "spec": { "unit": "m/s", "rangeLow": 0, "rangeHigh": 5 } }""")
        .Parameters("current-sensor", """{ "spec": { "unit": "A", "rangeLow": 0, "rangeHigh": 20 } }""")
        .Parameters("temperature-sensor", """{ "spec": { "unit": "°C", "rangeLow": 0, "rangeHigh": 300 } }""")
        .Parameters("belt-scale", """{ "belt": "BELT", "positionM": 5, "spec": { "unit": "t/h", "rangeLow": 0, "rangeHigh": 800 } }""")
        .Parameters("pyrometer", """{ "target": "BELT", "positionM": 5, "windowM": 0.5, "spec": { "unit": "°C", "rangeLow": 0, "rangeHigh": 1200 } }""")
        .Parameters("zero-speed-switch", """{ "spec": { "unit": "m/s", "rangeLow": 0, "rangeHigh": 5 }, "thresholdSpeed": 0.02, "delaySeconds": 1 }""")
        .Parameters("part-counter", """{ "belt": "BELT", "positionM": 5, "windowM": 0.2 }""")
        .Parameters("safety-relay", """{ "channels": 3 }""");
}
