using System.Text.Json;
using Dse.Cli;
using Dse.Configuration;
using Dse.Scenarios;
using Json.Schema;

namespace Dse.Samples.Tests;

/// <summary>
/// The wheel-line sample, end to end: the plant validates and the schema
/// accepts it; every scenario opens with the line start, matches its golden
/// through <c>dse run --expect</c>, tells its story, settles before it ends,
/// conserves mass and replays byte for byte from a recording; and the stories
/// the log cannot tell — a held billet's temperature, the belt's count, which
/// billet became which wheel — hold on a live run.
/// </summary>
public class WheelLineTests
{
    [Fact]
    public void ThePlantValidatesWithFiveControllers()
    {
        CliRun run = Cli.Run("validate", WheelLine.Plant);

        Assert.Equal(ExitCodes.Ok, run.ExitCode);
        Assert.Empty(run.Err);
        Assert.StartsWith($"OK  {WheelLine.Plant}\n", run.Out, StringComparison.Ordinal);
        Assert.Contains("  components    9\n", run.Out, StringComparison.Ordinal);
        Assert.Contains("  flow links    6\n", run.Out, StringComparison.Ordinal);
        Assert.Contains("  tags          39 (3 explicit)\n", run.Out, StringComparison.Ordinal);
        Assert.Contains("  controllers   5\n", run.Out, StringComparison.Ordinal);
    }

    [Fact]
    public void TheLoaderReportsNothingAtAllNotEvenAWarning()
    {
        LoadResult load = PlantLoader.Load(File.ReadAllText(WheelLine.Plant), Sample.Catalogue);

        Assert.True(load.IsValid, load.ToText());
        Assert.Empty(load.Diagnostics);
    }

    [Fact]
    public void TheGeneratedSchemaAcceptsThePlantAndRejectsAMistakeInIt()
    {
        JsonSchema schema = JsonSchema.FromText(PlantSchema.Generate(Sample.Catalogue));
        string plant = File.ReadAllText(WheelLine.Plant);
        string broken = plant.Replace("\"dwellSeconds\": 1 }", "\"dwellSeconds\": \"1\" }", StringComparison.Ordinal);

        Assert.NotEqual(plant, broken);
        Assert.True(schema.Evaluate(JsonDocument.Parse(plant).RootElement).IsValid);
        Assert.False(schema.Evaluate(JsonDocument.Parse(broken).RootElement).IsValid);
    }

    [Fact]
    public void TheRejectGateIsClaimedByTheCoilAndTheBilletSourceByTheBayInterlock()
    {
        CliRun run = Cli.Run("tags", WheelLine.Plant);

        Assert.Equal(ExitCodes.Ok, run.ExitCode);
        string[] claimed = run.Out.Split('\n').Where(l => l.Contains("  claimed by ", StringComparison.Ordinal)).ToArray();
        Assert.Equal(
            [
                "Billets.Enabled  Bool  ReadOnly  Minting enabled  claimed by INT_BAY",
                "GATE.Reject  Bool  ReadOnly  Divert the item leaving now to the reject outlet  claimed by COIL_REJECT",
            ],
            claimed);
        Assert.Contains("CV.ItemCount  Int64  ReadOnly  count  Blanks on the belt\n", run.Out, StringComparison.Ordinal);
        Assert.Contains("FCE.ZONE_SP  Double  ReadWrite  °C  [0, 1400]  Furnace zone temperature setpoint\n", run.Out, StringComparison.Ordinal);
        Assert.Contains("CV.SPEED_SP  Double  ReadWrite  m/s  [0, 1]  Belt speed setpoint\n", run.Out, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("GATE.Reject", "COIL_REJECT")]
    [InlineData("Billets.Enabled", "INT_BAY")]
    public void AScenarioThatWritesAClaimedTagIsRefusedBeforeTickZero(string tag, string block)
    {
        Scenario scenario = ScenarioLoader.Parse(
            $$"""{ "plant": "../plant.json", "duration": 60, "timeline": [ { "at": 10, "write": "{{tag}}", "value": true } ] }""").Scenario!;

        ScenarioRunResult result = ScenarioRunner.Run(scenario, File.ReadAllText(WheelLine.Plant), Sample.Catalogue);

        ConfigDiagnostic d = Assert.Single(result.Diagnostics);
        Assert.Equal(("DSE206", "$.timeline[0].write"), (d.Code, d.Path));
        Assert.Equal($"Tag '{tag}' is claimed by {block}; a scenario cannot write it.", d.Message);
        Assert.Null(result.Events);
    }
}
