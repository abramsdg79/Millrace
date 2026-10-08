using Millrace.Components;
using Millrace.Configuration;
using Millrace.Control.Catalogue;
using Millrace.Core;
using Millrace.Core.Catalogue;
using Millrace.Io;

namespace Millrace.Scenarios.Tests;

/// <summary>Spec 6d §4: a scenario cannot write a tag a block claims, and is told which block and what to write instead.</summary>
public class ClaimedTagScenarioTests
{
    private static readonly ComponentCatalogue Catalogue =
        new CatalogueBuilder().Add<ComponentsModule>().Add<ControlModule>().Build();

    /// <summary>A feeder whose interlock takes its permit away when the chute fills; <c>CLAIMS</c> is its claims entry.</summary>
    private const string Plant = """
        {
          "defaults": { "seed": 1, "timeStepMs": 10, "startTime": "2026-01-01T06:00:00Z" },
          "materials": [ { "name": "ore", "kind": "bulk" } ],
          "components": [
            { "id": "FEED", "type": "bulk-source", "parameters": { "material": "ore", "rateKgPerS": 20 } },
            { "id": "CHUTE", "type": "transfer-chute", "parameters": { "capacityKg": 200 } },
            { "id": "PILE", "type": "bulk-sink" }
          ],
          "flows": [ { "from": "FEED.Out", "to": "CHUTE.In" }, { "from": "CHUTE.Out", "to": "PILE.In" } ],
          "controllers": [
            { "id": "INT01", "type": "interlock", "scanPeriodMs": 100, CLAIMS
              "parameters": {
                "conditions": [ { "tag": "CHUTE.Full", "normal": false } ],
                "trip": [ { "tag": "FEED.Permit", "value": false } ],
                "reset": [ { "tag": "FEED.Permit", "value": true } ] } }
          ]
        }
        """;

    private static string Claimed => Plant.Replace("CLAIMS", "\"claims\": [ \"FEED.Permit\" ],", StringComparison.Ordinal);

    private static string Unclaimed => Plant.Replace("CLAIMS", string.Empty, StringComparison.Ordinal);

    private static ScenarioRunResult Run(string plantJson, string timeline)
    {
        ScenarioParseResult parsed = ScenarioLoader.Parse(
            $$"""{ "plant": "plant.json", "duration": 10, "timeline": [ {{timeline}} ] }""");
        Assert.NotNull(parsed.Scenario);
        return ScenarioRunner.Run(parsed.Scenario, plantJson, Catalogue);
    }

    [Fact]
    public void WritingAClaimedTagDoesNotBindAndNamesTheClaimant()
    {
        ScenarioRunResult result = Run(Claimed, """{ "at": 1, "write": "FEED.Permit", "value": true }""");

        ConfigDiagnostic d = Assert.Single(result.Diagnostics);
        Assert.Equal(("MR206", "$.timeline[0].write"), (d.Code, d.Path));
        Assert.Equal("Tag 'FEED.Permit' is claimed by INT01; a scenario cannot write it.", d.Message);
        Assert.Equal("Write the inputs of the block that claims it instead; `millrace tags` names it.", d.Fix);
        Assert.Null(result.Events);
        Assert.Null(result.Summary);
    }

    [Fact]
    public void TheSameWriteBindsOnThePlantWithoutTheClaim()
    {
        ScenarioRunResult result = Run(Unclaimed, """{ "at": 1, "write": "FEED.Permit", "value": true }""");

        Assert.True(result.IsValid, result.ToText());
        Assert.Contains("FEED.Permit  WRITE  Set to true.", result.Events!.ToText(), StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnclaimedTagBesideAClaimedOneStillBinds()
    {
        ScenarioRunResult result = Run(Claimed, """{ "at": 1, "write": "FEED.Enabled", "value": false }""");

        Assert.True(result.IsValid, result.ToText());
    }

    [Fact]
    public void ARecordingThatWroteATagNowClaimedDoesNotReplay()
    {
        LoadResult load = PlantLoader.Load(Unclaimed, Catalogue);
        Simulation live = load.Builder!.Build();
        var recorder = new ScenarioRecorder();
        live.AttachActionRecorder(recorder);
        live.WriteAt(TimeSpan.FromSeconds(1), "FEED.Permit", TagValue.Bool(true));
        live.RunFor(TimeSpan.FromSeconds(10));
        Scenario recorded = recorder.ToScenario("plant.json", load.Options!, TimeSpan.FromSeconds(10));

        ScenarioRunResult replay = ScenarioRunner.Run(recorded, Claimed, Catalogue);

        ConfigDiagnostic d = Assert.Single(replay.Diagnostics);
        Assert.Equal("Tag 'FEED.Permit' is claimed by INT01; a scenario cannot write it.", d.Message);
        Assert.Null(replay.Events);
    }
}
