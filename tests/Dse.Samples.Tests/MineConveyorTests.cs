using System.Text.Json;
using Dse.Cli;
using Dse.Configuration;
using Json.Schema;

namespace Dse.Samples.Tests;

/// <summary>The mine-conveyor sample's plant: it validates, and the schema accepts it.</summary>
public class MineConveyorTests
{
    [Fact]
    public void ThePlantValidatesWithTwelveControllers()
    {
        CliRun run = Cli.Run("validate", Sample.Plant);

        Assert.Equal(ExitCodes.Ok, run.ExitCode);
        Assert.Empty(run.Err);
        Assert.StartsWith($"OK  {Sample.Plant}\n", run.Out, StringComparison.Ordinal);
        Assert.Contains("  controllers   12\n", run.Out, StringComparison.Ordinal);
    }

    [Fact]
    public void TheLoaderReportsNothingAtAllNotEvenAWarning()
    {
        LoadResult load = PlantLoader.Load(File.ReadAllText(Sample.Plant), Sample.Catalogue);

        Assert.True(load.IsValid, load.ToText());
        Assert.Empty(load.Diagnostics);
    }

    [Fact]
    public void TheGeneratedSchemaAcceptsThePlantAndRejectsAMistakeInIt()
    {
        JsonSchema schema = JsonSchema.FromText(PlantSchema.Generate(Sample.Catalogue));
        string plant = File.ReadAllText(Sample.Plant);
        string broken = plant.Replace("\"lengthM\": 60", "\"lengthM\": \"60\"", StringComparison.Ordinal);

        Assert.NotEqual(plant, broken);
        Assert.True(schema.Evaluate(JsonDocument.Parse(plant).RootElement).IsValid);
        Assert.False(schema.Evaluate(JsonDocument.Parse(broken).RootElement).IsValid);
    }
}
