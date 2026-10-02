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
        .Parameters("safety-relay", """{ "channels": 3 }""")
        // Task 7 — flow, transforms, holds
        .Material(new MaterialDescriptor(new MaterialType("test-bulk", PayloadKind.Bulk, "soak"), new MaterialProperties(1000.0, 0.1, 20.0), "A bulk test material."))
        .Material(new MaterialDescriptor(new MaterialType("test-item", PayloadKind.Discrete, "soak"), new MaterialProperties(7800.0, 0.0, 20.0), "A discrete test material."))
        .Parameters("bulk-belt", """{ "lengthM": 10, "cellSizeM": 0.5, "maxSpeedMps": 2, "maxLinearDensityKgPerM": 100 }""")
        .Parameters("discrete-belt", """{ "lengthM": 10, "maxSpeedMps": 2 }""")
        .Parameters("bulk-source", """{ "material": "test-bulk", "rateKgPerS": 20 }""")
        .Parameters("item-source", """{ "material": "test-item", "itemMassKg": 12, "intervalSeconds": 5 }""")
        .Parameters("transfer-chute", """{ "capacityKg": 200 }""")
        .Parameters("former", """{ "input": "test-bulk", "output": "test-item", "pieceMassKg": 0.5, "cycleSeconds": 2, "hopperCapacityKg": 50 }""")
        .Parameters("bulk-process-unit", """
            { "recipe": [ { "inlet": "Flour", "material": "test-bulk", "massKg": 5 }, { "inlet": "Water", "material": "test-bulk", "massKg": 3 } ],
              "hold": { "type": "for-seconds", "seconds": 10 }, "output": "test-bulk" }
            """)
        .Parameters("item-process-unit", """{ "batchSize": 4, "hold": { "type": "for-seconds", "seconds": 10 } }""")
        .Parameters("reject-gate", """{ "dwellSeconds": 2 }""")
        .ObjectParameters(ObjectSlots.Transform, "thermal-transfer", """{ "timeConstantSeconds": 30 }""")
        .ObjectParameters(ObjectSlots.Transform, "moisture-loss", """{ "ratePerDegreeSecond": 0.0001, "thresholdTemperature": 100 }""")
        .ObjectParameters(ObjectSlots.Transform, "residence-accumulator", """{ "material": "test-item", "state": "soak", "thresholdTemperature": 700 }""")
        .ObjectParameters(ObjectSlots.Hold, "for-seconds", """{ "seconds": 10 }""")
        .ObjectParameters(ObjectSlots.Hold, "temperature-at-least", """{ "celsius": 180 }""")
        .ObjectParameters(ObjectSlots.Hold, "temperature-at-most", """{ "celsius": 40 }""")
        .ObjectParameters(ObjectSlots.Hold, "state-at-least", """{ "material": "test-item", "state": "soak", "value": 600 }""")
        .ObjectParameters(ObjectSlots.Hold, "all", """{ "conditions": [ { "type": "for-seconds", "seconds": 10 }, { "type": "temperature-at-least", "celsius": 180 } ] }""")
        // Task 8 — conveyor
        .Parameters("conveyor", """
            { "lengthM": 10, "cellSizeM": 0.5, "beltWidthM": 0.8, "angleOfReposeDeg": 20, "materialDensityKgM3": 2000,
              "emptyBeltMassKg": 250, "frictionCoefficient": 0.04, "pulleyDiameterM": 0.5, "gearRatio": 20,
              "motor": { "ratedPowerW": 750, "ratedSpeedRadPerS": 150, "ratedCurrentA": 2 }, "pullKeys": 3 }
            """);
}
