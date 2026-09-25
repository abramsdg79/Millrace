using Dse.Core.Catalogue;
using Dse.Core.Testing;

namespace Dse.Control.Catalogue.Tests;

/// <summary>The module under test and what conformance needs to build a probe of each type.</summary>
internal static class ControlFixtures
{
    public static ComponentCatalogue Catalogue { get; } = new CatalogueBuilder().Add<ControlModule>().Build();

    public static ConformanceFixtures Create() => new ConformanceFixtures()
        .ObjectParameters(ControlCatalogue.TransitionSlot, "when", """{ "tag": "V1.Level", "op": ">=", "value": 80 }""")
        .ObjectParameters(ControlCatalogue.TransitionSlot, "after", """{ "delayS": 5 }""")
        .BlockParameters("timer", """{ "mode": "on-delay", "input": "V1.Running", "presetS": 2 }""")
        .BlockParameters("permissive", """{ "conditions": [ { "tag": "V1.Tripped", "normal": false } ] }""")
        .BlockParameters(
            "interlock",
            """{ "conditions": [ { "tag": "V1.Tripped", "normal": false } ], "trip": [ { "tag": "V1.Fill", "value": false } ] }""");
}
