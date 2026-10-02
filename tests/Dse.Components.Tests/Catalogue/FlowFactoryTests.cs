using Dse.Components.Flow;
using Dse.Core.Catalogue;
using Dse.Core.Flow;

namespace Dse.Components.Tests.Catalogue;

public class FlowFactoryTests
{
    private static BindingContext Context() => new BindingContext(ComponentsFixtures.Catalogue)
        .AddMaterial(new MaterialDescriptor(new MaterialType("flour", PayloadKind.Bulk), new MaterialProperties(600.0, 0.12, 18.0), "Flour."))
        .AddMaterial(new MaterialDescriptor(new MaterialType("dough", PayloadKind.Bulk, "proof"), new MaterialProperties(1100.0, 0.4, 25.0), "Dough."))
        .AddMaterial(new MaterialDescriptor(new MaterialType("billet", PayloadKind.Discrete, "soak"), new MaterialProperties(7800.0, 0.0, 20.0), "A billet."));

    [Fact]
    public void ASourceWithNoCapacityIsUnlimitedAndOneWithACapacityIsNot()
    {
        BulkSource open = MechanicalFactoryTests.Build<BulkSource>(BulkSource.Descriptor, """{ "material": "flour", "rateKgPerS": 2 }""", Context());
        BulkSource bounded = MechanicalFactoryTests.Build<BulkSource>(
            BulkSource.Descriptor, """{ "material": "flour", "rateKgPerS": 2, "hopperCapacityKg": 500 }""", Context());

        Assert.Equal(double.PositiveInfinity, open.HopperCapacityKg);
        Assert.Equal(500.0, bounded.HopperCapacityKg);
    }

    [Fact]
    public void ASourceIsEnabledAtPowerUpUnlessTheFileSaysOtherwise()
    {
        BulkSource running = MechanicalFactoryTests.Build<BulkSource>(BulkSource.Descriptor, """{ "material": "flour", "rateKgPerS": 2 }""", Context());
        BulkSource held = MechanicalFactoryTests.Build<BulkSource>(
            BulkSource.Descriptor, """{ "material": "flour", "rateKgPerS": 2, "enabled": false }""", Context());

        Assert.True(running.Enabled.Value);
        Assert.False(held.Enabled.Value);
    }

    [Fact]
    public void AProcessUnitGetsOneInletPerRecipeLineAndANestedHold()
    {
        BulkProcessUnit mixer = MechanicalFactoryTests.Build<BulkProcessUnit>(
            BulkProcessUnit.Descriptor,
            """
            { "recipe": [ { "inlet": "Flour", "material": "flour", "massKg": 50 }, { "inlet": "Water", "material": "flour", "massKg": 30 } ],
              "hold": { "type": "all", "conditions": [ { "type": "for-seconds", "seconds": 600 },
                                                        { "type": "state-at-least", "material": "dough", "state": "proof", "value": 1 } ] },
              "output": "dough", "yield": 0.98,
              "transforms": [ { "type": "thermal-transfer", "timeConstantSeconds": 900 } ] }
            """,
            Context());

        Assert.Contains(mixer.Ports, p => p is FlowInlet && p.Name == "Flour");
        Assert.Contains(mixer.Ports, p => p is FlowInlet && p.Name == "Water");
        Assert.Equal(3, mixer.Ports.Count(p => p is FlowPort));
    }

    [Fact]
    public void AnItemUnitMayKeepItsMaterial()
    {
        ItemProcessUnit furnace = MechanicalFactoryTests.Build<ItemProcessUnit>(
            ItemProcessUnit.Descriptor,
            """
            { "batchSize": 6, "hold": { "type": "state-at-least", "material": "billet", "state": "soak", "value": 1200 },
              "transforms": [ { "type": "residence-accumulator", "material": "billet", "state": "soak", "thresholdTemperature": 700 } ] }
            """,
            Context());

        Assert.Equal("X", furnace.Id);
    }

    [Fact]
    public void AnItemUnitHeatsWhileHeldOnlyWhenTheFileSaysSo()
    {
        const string Batch = """ "batchSize": 1, "hold": { "type": "for-seconds", "seconds": 5 } """;
        ItemProcessUnit plain = MechanicalFactoryTests.Build<ItemProcessUnit>(ItemProcessUnit.Descriptor, $$"""{ {{Batch}} }""", Context());
        ItemProcessUnit furnace = MechanicalFactoryTests.Build<ItemProcessUnit>(
            ItemProcessUnit.Descriptor, $$"""{ {{Batch}}, "heatWhileHeld": true }""", Context());

        Assert.False(plain.HeatWhileHeld);
        Assert.True(furnace.HeatWhileHeld);
    }

    [Fact]
    public void ARejectGateReadsItsDwellAndHasTwoOutlets()
    {
        RejectGate gate = MechanicalFactoryTests.Build<RejectGate>(RejectGate.Descriptor, """{ "dwellSeconds": 1.5 }""");

        Assert.Equal(1.5, gate.DwellSeconds);
        Assert.Equal(["Out", "RejectOut"], gate.Ports.OfType<FlowOutlet>().Select(p => p.Name));
    }

    [Fact]
    public void ABulkMaterialCannotFeedAnItemSource()
    {
        using var document = System.Text.Json.JsonDocument.Parse("""{ "material": "flour", "itemMassKg": 1, "intervalSeconds": 1 }""");
        var issues = new List<BindingIssue>();

        ParameterBinder.Bind(ItemSource.Descriptor.Parameters, document.RootElement, "$", Context(), true, issues);

        BindingIssue issue = Assert.Single(issues);
        Assert.Equal("$.material", issue.Path);
        Assert.Contains("Discrete", issue.Message, StringComparison.Ordinal);
    }
}
