namespace Dse.Configuration.Tests;

public class ReferenceAndInstantiateTests
{
    /// <summary>The scale is listed before the belt it references, on purpose.</summary>
    private const string Weighed = """
        {
          "materials": [ { "name": "ore", "kind": "bulk" } ],
          "components": [
            { "id": "WT", "type": "belt-scale",
              "parameters": { "belt": "BELT", "positionM": 5, "spec": { "unit": "t/h", "rangeLow": 0, "rangeHigh": 800 } } },
            { "id": "FEED", "type": "bulk-source", "parameters": { "material": "ore", "rateKgPerS": 20 } },
            { "id": "BELT", "type": "bulk-belt",
              "parameters": { "lengthM": 10, "cellSizeM": 0.5, "maxSpeedMps": 2, "maxLinearDensityKgPerM": 100 } },
            { "id": "PILE", "type": "bulk-sink" }
          ],
          "flows": [ { "from": "FEED.Out", "to": "BELT.In" }, { "from": "BELT.Out", "to": "PILE.In" } ]
        }
        """;

    [Fact]
    public void AForwardReferenceIsFine()
    {
        Assert.Empty(TestPlants.Load(Weighed).Diagnostics);
    }

    [Fact]
    public void AMissingReferenceSuggestsTheNearestId()
    {
        ConfigDiagnostic d = TestPlants.Only(Weighed.Replace("\"belt\": \"BELT\"", "\"belt\": \"BLET\"", StringComparison.Ordinal));

        Assert.Equal("DSE104", d.Code);
        Assert.Equal("$.components[0].parameters.belt", d.Path);
        Assert.Contains("'BELT' is closest", d.Fix, StringComparison.Ordinal);
    }

    [Fact]
    public void AReferenceToTheWrongKindOfThingListsTheRightOnes()
    {
        ConfigDiagnostic d = TestPlants.Only(Weighed.Replace("\"belt\": \"BELT\"", "\"belt\": \"FEED\"", StringComparison.Ordinal));

        Assert.Equal("DSE105", d.Code);
        Assert.Equal("$.components[0].parameters.belt", d.Path);
        Assert.Contains("bulk-source", d.Message, StringComparison.Ordinal);
        Assert.Contains("IMaterialObservable", d.Message, StringComparison.Ordinal);
        Assert.Equal("Reference one of BELT.", d.Fix);
    }

    [Fact]
    public void ASelfReferenceIsACycleOfOne()
    {
        ConfigDiagnostic d = TestPlants.Only(Weighed.Replace("\"belt\": \"BELT\"", "\"belt\": \"WT\"", StringComparison.Ordinal));

        Assert.Equal("DSE106", d.Code);
        Assert.Contains("references itself", d.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ACycleIsReportedWithItsPath()
    {
        const string Json = """
            { "components": [
                { "id": "A", "type": "echo", "parameters": { "other": "B" } },
                { "id": "B", "type": "echo", "parameters": { "other": "C" } },
                { "id": "C", "type": "echo", "parameters": { "other": "A" } },
                { "id": "D", "type": "echo" } ] }
            """;

        ConfigDiagnostic d = TestPlants.Only(Json);

        Assert.Equal("DSE106", d.Code);
        Assert.Equal("$.components[0].parameters", d.Path);
        Assert.Contains("A -> B -> C -> A", d.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AChainListedBackwardsStillBuilds()
    {
        const string Json = """
            { "components": [
                { "id": "C", "type": "echo", "parameters": { "other": "B" } },
                { "id": "B", "type": "echo", "parameters": { "other": "A" } },
                { "id": "A", "type": "echo" } ] }
            """;

        Assert.Empty(TestPlants.Load(Json).Diagnostics);
    }

    [Fact]
    public void AConstructorsRefusalBecomesDse111WithItsMessage()
    {
        const string Json = """
            { "components": [ { "id": "MS", "type": "motor-starter", "parameters": { "tripLevel": 1.1, "resetLevel": 1.5 } } ] }
            """;

        ConfigDiagnostic d = TestPlants.Only(Json);

        Assert.Equal("DSE111", d.Code);
        Assert.Equal("$.components[0]", d.Path);
        Assert.Contains("reset level", d.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("'MS'", d.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ADefectiveFactoryNamesItsModuleInsteadOfCrashing()
    {
        ConfigDiagnostic d = TestPlants.Only("""{ "components": [ { "id": "X", "type": "broken" } ] }""");

        Assert.Equal("DSE111", d.Code);
        Assert.Contains("defect", d.Fix, StringComparison.Ordinal);
        Assert.Contains("'Test'", d.Fix, StringComparison.Ordinal);
        Assert.StartsWith("Report this", d.Fix, StringComparison.Ordinal);
    }

    [Fact]
    public void TheCatchAllMessageIsANormalisedSentence()
    {
        ConfigDiagnostic d = TestPlants.Only("""{ "components": [ { "id": "X", "type": "silent" } ] }""");

        Assert.EndsWith("bad wiring.", d.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("..", d.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NoComponentSuppliesTheCapabilityAtAllSaysSo()
    {
        const string Json = """
            { "materials": [ { "name": "ore", "kind": "bulk" } ],
              "components": [
                { "id": "WT", "type": "belt-scale",
                  "parameters": { "belt": "FEED", "positionM": 5, "spec": { "unit": "t/h", "rangeLow": 0, "rangeHigh": 800 } } },
                { "id": "FEED", "type": "bulk-source", "parameters": { "material": "ore", "rateKgPerS": 20 } },
                { "id": "PILE", "type": "bulk-sink" } ] }
            """;

        ConfigDiagnostic d = TestPlants.Only(Json);

        Assert.Equal("DSE105", d.Code);
        Assert.Equal("Add a component that supplies IMaterialObservable, then reference it.", d.Fix);
    }

    [Fact]
    public void ADependentOfAFailedComponentIsSkippedQuietly()
    {
        // 10 m is not a whole number of 3 m cells, so the belt's constructor refuses; the scale that references it says nothing.
        ConfigDiagnostic d = TestPlants.Only(Weighed.Replace("\"cellSizeM\": 0.5", "\"cellSizeM\": 3", StringComparison.Ordinal));

        Assert.Equal("DSE111", d.Code);
        Assert.Equal("$.components[2]", d.Path);
    }

    [Fact]
    public void AnUnknownStateInsideANestedObjectIsDse110()
    {
        const string Json = """
            { "materials": [ { "name": "billet", "kind": "discrete", "states": [ "soak" ] } ],
              "components": [ { "id": "FCE", "type": "item-process-unit", "parameters": {
                  "batchSize": 4, "hold": { "type": "state-at-least", "material": "billet", "state": "sok", "value": 600 } } } ] }
            """;

        ConfigDiagnostic d = TestPlants.Only(Json);

        Assert.Equal("DSE110", d.Code);
        Assert.Equal("$.components[0].parameters.hold.state", d.Path);
        Assert.Contains("'soak' is closest", d.Fix, StringComparison.Ordinal);
    }

    [Fact]
    public void AReferenceInsideAGroupIsFoundAndOrdersConstruction()
    {
        const string Json = """
            { "components": [
                { "id": "N", "type": "nested-echo", "parameters": { "link": { "target": "Z" } } },
                { "id": "Z", "type": "echo" } ] }
            """;

        Assert.Empty(TestPlants.Load(Json).Diagnostics);
    }

    [Fact]
    public void AMissingReferenceInsideAGroupListHasAnIndexedPath()
    {
        const string Json = """
            { "components": [
                { "id": "N", "type": "nested-echo", "parameters": { "links": [ { "target": "Z" }, { "target": "ZZ" } ] } },
                { "id": "Z", "type": "echo" } ] }
            """;

        ConfigDiagnostic d = TestPlants.Only(Json);

        Assert.Equal("DSE104", d.Code);
        Assert.Equal("$.components[0].parameters.links[1].target", d.Path);
    }

    [Fact]
    public void AReferenceInsideAnObjectIsFoundAndOrdersConstruction()
    {
        // Before ReferenceStage understood object references this raised a spurious DSE104: OE is listed
        // before Z, but Z must be built first because OE's "probes" reference it.
        const string Json = """
            { "components": [
                { "id": "OE", "type": "object-echo", "parameters": { "probes": [ { "type": "watch", "target": "Z" } ] } },
                { "id": "Z", "type": "echo" } ] }
            """;

        Assert.Empty(TestPlants.Load(Json).Diagnostics);
    }

    [Fact]
    public void AMissingReferenceInsideAnObjectHasItsPath()
    {
        const string Json = """
            { "components": [
                { "id": "OE", "type": "object-echo", "parameters": { "probes": [ { "type": "watch", "target": "ZZ" } ] } },
                { "id": "Z", "type": "echo" } ] }
            """;

        ConfigDiagnostic d = TestPlants.Only(Json);

        Assert.Equal("DSE104", d.Code);
        Assert.Equal("$.components[0].parameters.probes[0].target", d.Path);
    }

    [Fact]
    public void ACycleThroughAnObjectReferenceIsReported()
    {
        const string Json = """
            { "components": [
                { "id": "A", "type": "object-echo", "parameters": { "probes": [ { "type": "watch", "target": "B" } ] } },
                { "id": "B", "type": "object-echo", "parameters": { "probes": [ { "type": "watch", "target": "A" } ] } } ] }
            """;

        ConfigDiagnostic d = TestPlants.Only(Json);

        Assert.Equal("DSE106", d.Code);
        Assert.Contains("A -> B -> A", d.Message, StringComparison.Ordinal);
    }
}
