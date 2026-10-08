using Millrace.Core.Catalogue;
using Millrace.Core.Testing;

namespace Millrace.Control.Catalogue.Tests;

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
            """{ "conditions": [ { "tag": "V1.Tripped", "normal": false } ], "trip": [ { "tag": "V1.Fill", "value": false } ] }""")
        .BlockParameters("alarm", """{ "input": "V1.Level", "limits": [ { "kind": "hi", "value": 80 } ] }""")
        .BlockParameters("coil", """{ "condition": { "tag": "V1.Tripped", "normal": true }, "output": "V1.Fill" }""")
        .BlockParameters(
            "alarm",
            """
            { "input": "V1.Level", "limits": [
                { "kind": "hi-hi", "value": 95, "deadband": 2, "onDelayS": 1 },
                { "kind": "lo-lo", "value": 5 },
                { "kind": "hi", "value": 80 },
                { "kind": "lo", "value": 20 } ] }
            """)
        .BlockParameters(
            "sequencer",
            """
            { "steps": [
                { "name": "Fill", "writes": [ { "tag": "V1.Fill", "value": true } ],
                  "transition": { "type": "when", "tag": "V1.Level", "op": ">=", "value": 80 }, "timeoutS": 30 },
                { "name": "Settle", "transition": { "type": "after", "delayS": 5 } } ],
              "abort": [ { "tag": "V1.Fill", "value": false } ] }
            """);
}
