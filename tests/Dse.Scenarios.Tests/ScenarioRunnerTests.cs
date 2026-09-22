using Dse.Configuration;

namespace Dse.Scenarios.Tests;

public class ScenarioRunnerTests
{
    private static ScenarioRunResult Run(string plant, string body) =>
        Corpus.Run($$"""{ "plant": "{{plant}}", {{body}} }""", plant);

    private static ConfigDiagnostic Only(string plant, string body) => Assert.Single(Run(plant, body).Diagnostics);

    [Fact]
    public void TheConveyorScenarioRunsAndLogsEveryAction()
    {
        ScenarioRunResult result = Run("conveyor-line.json", """
            "seed": 42, "duration": 120,
            "timeline": [
              { "at": 5,    "write": "CV001.Start", "value": true },
              { "at": 30,   "fault": "CV001.Motor", "id": "thermal-bias", "args": { "amount": 0.8 } },
              { "at": 60.5, "clear": "CV001.Motor", "id": "thermal-bias" }
            ]
            """);

        Assert.Empty(result.Diagnostics);
        Assert.True(result.IsValid);
        Assert.Equal(new RunSummary(12000L, result.Summary!.Events, 3), result.Summary);
        string log = result.Events!.ToText().ReplaceLineEndings("\n");
        Assert.Contains("06:00:05.000  CV001.Start  WRITE  Set to true.\n", log, StringComparison.Ordinal);
        Assert.Contains("06:00:30.000  CV001.Motor  FAULT  thermal-bias injected: amount=0.8.\n", log, StringComparison.Ordinal);
        Assert.Contains("06:01:00.500  CV001.Motor  FAULT_CLEARED  thermal-bias cleared.\n", log, StringComparison.Ordinal);
    }

    [Fact]
    public void TwoRunsOfOneScenarioAreByteIdentical()
    {
        const string Body = """
            "duration": 30,
            "timeline": [ { "at": 10, "write": "FEED.Rate", "value": 5 } ]
            """;

        Assert.Equal(
            Run("minimal.json", Body).Events!.ToText(),
            Run("minimal.json", Body).Events!.ToText());
    }

    [Fact]
    public void AScenarioWithNoTimelineStillRuns()
    {
        ScenarioRunResult result = Run("minimal.json", "\"duration\": 2");

        Assert.Empty(result.Diagnostics);
        Assert.Equal(200L, result.Summary!.Ticks);
        Assert.Equal(0, result.Summary.ActionsScheduled);
    }

    [Fact]
    public void AScenarioOverrideBeatsThePlantsDefaults()
    {
        ScenarioRunResult result = Run("minimal.json", """
            "startTime": "2026-05-04T12:00:00Z", "timeStepMs": 100, "duration": 2,
            "timeline": [ { "at": 1, "write": "FEED.Enabled", "value": false } ]
            """);

        Assert.Empty(result.Diagnostics);
        Assert.Equal(20L, result.Summary!.Ticks);
        Assert.StartsWith("12:00:01.000  FEED.Enabled  WRITE  Set to false.", result.Events!.ToText(), StringComparison.Ordinal);
    }

    [Fact]
    public void ASubTickStepFromThePlantsDefaultsIsDse202BeforeBuilding()
    {
        string plantJson = File.ReadAllText(Corpus.PlantPath("minimal.json"))
            .Replace("\"timeStepMs\": 10", "\"timeStepMs\": 1e-9", StringComparison.Ordinal);
        var scenario = new Scenario("minimal.json", null, null, null, TimeSpan.FromSeconds(1), []);

        ScenarioRunResult result = ScenarioRunner.Run(scenario, plantJson, Corpus.Catalogue);

        Assert.False(result.IsValid);
        Assert.Null(result.Events);
        Assert.Null(result.Summary);
        ConfigDiagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(("DSE202", "$.timeStepMs"), (diagnostic.Code, diagnostic.Path));
    }

    [Fact]
    public void AnInvalidPlantIsDse205ThenThePlantsOwnDiagnostics()
    {
        ScenarioRunResult result = Corpus.Run("""{ "plant": "broken.json", "duration": 1 }""", "broken.json");

        Assert.False(result.IsValid);
        Assert.Null(result.Events);
        Assert.Null(result.Summary);
        ConfigDiagnostic first = result.Diagnostics[0];
        Assert.Equal(("DSE205", "$.plant"), (first.Code, first.Path));
        Assert.Equal("The plant 'broken.json' has 2 errors of its own; they follow.", first.Message);
        Assert.StartsWith("Fix the plant file", first.Fix, StringComparison.Ordinal);
        Assert.Equal(new[] { "DSE103", "DSE102" }, result.Diagnostics.Skip(1).Select(d => d.Code));
    }

    [Fact]
    public void TheRunnerChecksTheTimesOfAScenarioBuiltInCode()
    {
        var scenario = new Scenario("minimal.json", null, null, null, TimeSpan.FromSeconds(10),
        [
            new WriteAction(TimeSpan.FromSeconds(10), "FEED.Enabled", ScenarioValue.OfBool(false)),
            new WriteAction(TimeSpan.FromMilliseconds(1005), "FEED.Enabled", ScenarioValue.OfBool(false)),
            new WriteAction(TimeSpan.FromSeconds(-1), "FEED.Enabled", ScenarioValue.OfBool(false)),
        ]);

        ScenarioRunResult result = ScenarioRunner.Run(
            scenario, File.ReadAllText(Corpus.PlantPath("minimal.json")), Corpus.Catalogue);

        Assert.Equal(new[] { "DSE203", "DSE203", "DSE202" }, result.Diagnostics.Select(d => d.Code));
        Assert.Equal(
            new[]
            {
                "An action at 10 s is not before the end of the run at 10 s.",
                "The action time 1.005 s is not a whole number of 10 ms steps.",
                "An action at -1 s is before the start of the run.",
            },
            result.Diagnostics.Select(d => d.Message));
        Assert.Equal("$.timeline[2].at", result.Diagnostics[2].Path);
    }

    [Fact]
    public void AnOffTickActionOnThePlantsOwnStepIsDse203()
    {
        ConfigDiagnostic diagnostic = Only("minimal.json", """
            "duration": 10,
            "timeline": [ { "at": 1.005, "write": "FEED.Enabled", "value": false } ]
            """);

        Assert.Equal(("DSE203", "$.timeline[0].at"), (diagnostic.Code, diagnostic.Path));
        Assert.Equal("The action time 1.005 s is not a whole number of 10 ms steps.", diagnostic.Message);
    }

    [Fact]
    public void AnOffTickDurationOnThePlantsOwnStepIsDse203()
    {
        ConfigDiagnostic diagnostic = Only("minimal.json", "\"duration\": 10.005");

        Assert.Equal(("DSE203", "$.duration"), (diagnostic.Code, diagnostic.Path));
        Assert.Equal("The duration 10.005 s is not a whole number of 10 ms steps.", diagnostic.Message);
    }

    [Fact]
    public void AnUnknownTagIsDse206AndSuggestsTheNearest()
    {
        ConfigDiagnostic diagnostic = Only("minimal.json", """
            "duration": 10,
            "timeline": [ { "at": 1, "write": "FEED.Ratte", "value": 5 } ]
            """);

        Assert.Equal(("DSE206", "$.timeline[0].write"), (diagnostic.Code, diagnostic.Path));
        Assert.Equal("There is no tag 'FEED.Ratte' in this plant.", diagnostic.Message);
        Assert.Contains("'FEED.Rate' is closest.", diagnostic.Fix, StringComparison.Ordinal);
    }

    [Fact]
    public void AReadOnlyTagIsDse206()
    {
        ConfigDiagnostic diagnostic = Only("minimal.json", """
            "duration": 10,
            "timeline": [ { "at": 1, "write": "PILE.Full", "value": true } ]
            """);

        Assert.Equal(("DSE206", "$.timeline[0].write"), (diagnostic.Code, diagnostic.Path));
        Assert.Equal("Tag 'PILE.Full' is read-only; a scenario cannot write it.", diagnostic.Message);
        Assert.StartsWith("Write a tag whose access is ReadWrite;", diagnostic.Fix, StringComparison.Ordinal);
    }

    [Fact]
    public void AValueOfTheWrongKindIsDse206()
    {
        ConfigDiagnostic diagnostic = Only("minimal.json", """
            "duration": 10,
            "timeline": [ { "at": 1, "write": "FEED.Enabled", "value": 5 } ]
            """);

        Assert.Equal(("DSE206", "$.timeline[0].value"), (diagnostic.Code, diagnostic.Path));
        Assert.Equal("Tag 'FEED.Enabled' is a Bool tag; 5 is not a Bool value.", diagnostic.Message);
        Assert.Equal("Write true or false.", diagnostic.Fix);
    }

    [Fact]
    public void AFractionOnAnIntegerTagIsDse206()
    {
        ConfigDiagnostic diagnostic = Only("item-line.json", """
            "duration": 10,
            "timeline": [ { "at": 1, "write": "BIN.Count", "value": 5.5 } ]
            """);

        // BIN.Count is read-only, so the access check fires first: that is the order the runner promises.
        Assert.Equal(("DSE206", "$.timeline[0].write"), (diagnostic.Code, diagnostic.Path));
        Assert.Equal("Tag 'BIN.Count' is read-only; a scenario cannot write it.", diagnostic.Message);
    }

    [Fact]
    public void AnUnknownComponentIsDse206AtTheShapeKey()
    {
        ConfigDiagnostic diagnostic = Only("minimal.json", """
            "duration": 10,
            "timeline": [ { "at": 1, "fault": "NOPE", "id": "blockage" } ]
            """);

        Assert.Equal(("DSE206", "$.timeline[0].fault"), (diagnostic.Code, diagnostic.Path));
        Assert.StartsWith("No component 'NOPE' in the plant.", diagnostic.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("(Parameter", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AComponentWithNoFaultsIsDse206()
    {
        ConfigDiagnostic diagnostic = Only("minimal.json", """
            "duration": 10,
            "timeline": [ { "at": 1, "fault": "PILE", "id": "blockage" } ]
            """);

        Assert.Equal(("DSE206", "$.timeline[0].fault"), (diagnostic.Code, diagnostic.Path));
        Assert.StartsWith("Component 'PILE' is not an IFaultTarget", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnknownFaultIdIsDse206AtTheIdPath()
    {
        ConfigDiagnostic diagnostic = Only("minimal.json", """
            "duration": 10,
            "timeline": [ { "at": 1, "fault": "CHUTE", "id": "blokage" } ]
            """);

        Assert.Equal(("DSE206", "$.timeline[0].id"), (diagnostic.Code, diagnostic.Path));
        Assert.Equal("'CHUTE' supports no fault 'blokage'. Supported: blockage.", diagnostic.Message);
    }

    [Fact]
    public void AnUndeclaredFaultArgumentIsDse206AtTheArgsPath()
    {
        ConfigDiagnostic diagnostic = Only("minimal.json", """
            "duration": 10,
            "timeline": [ { "at": 1, "fault": "CHUTE", "id": "blockage", "args": { "amount": 1 } } ]
            """);

        Assert.Equal(("DSE206", "$.timeline[0].args"), (diagnostic.Code, diagnostic.Path));
        Assert.Equal("Fault 'blockage' has no parameter 'amount'. Declared: none.", diagnostic.Message);
    }

    [Fact]
    public void ADuplicateFaultArgumentIsDse206AtTheArgsPath()
    {
        ConfigDiagnostic diagnostic = Only("conveyor-line.json", """
            "duration": 10,
            "timeline": [ { "at": 1, "fault": "CV001.Motor", "id": "thermal-bias", "args": { "amount": 0.8, "amount": 0.9 } } ]
            """);

        Assert.Equal(("DSE206", "$.timeline[0].args"), (diagnostic.Code, diagnostic.Path));
        Assert.Equal("Argument 'amount' is given twice.", diagnostic.Message);
    }

    [Fact]
    public void AClearOfAnUnknownFaultIsDse206AtTheClearPath()
    {
        ConfigDiagnostic diagnostic = Only("minimal.json", """
            "duration": 10,
            "timeline": [ { "at": 1, "clear": "NOPE", "id": "blockage" } ]
            """);

        Assert.Equal(("DSE206", "$.timeline[0].clear"), (diagnostic.Code, diagnostic.Path));
    }

    [Fact]
    public void EveryProblemIsCollectedAndNothingRuns()
    {
        ScenarioRunResult result = Run("minimal.json", """
            "duration": 10,
            "timeline": [
              { "at": 1, "write": "FEED.Ratte", "value": 5 },
              { "at": 2.003, "write": "FEED.Enabled", "value": false },
              { "at": 3, "fault": "CHUTE", "id": "blokage" }
            ]
            """);

        Assert.Equal(new[] { "DSE206", "DSE203", "DSE206" }, result.Diagnostics.Select(d => d.Code));
        Assert.Equal(
            new[] { "$.timeline[0].write", "$.timeline[1].at", "$.timeline[2].id" },
            result.Diagnostics.Select(d => d.Path));
        Assert.Null(result.Events);
        Assert.Null(result.Summary);
        Assert.EndsWith("\n", result.ToText(), StringComparison.Ordinal);
    }
}
