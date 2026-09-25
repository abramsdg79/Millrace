using System.Text.Json;

namespace Dse.Cli.Tests;

public class TagsCommandTests
{
    [Fact]
    public void ListsTheBuiltPlantsTagDirectory()
    {
        CliRun run = Cli.Run("tags", Cli.Plant("minimal.json"));

        Assert.Equal(ExitCodes.Ok, run.ExitCode);
        Assert.Empty(run.Err);
        Assert.Contains("FEED.Rate  Double  ReadWrite  kg/s", run.Out, StringComparison.Ordinal);
        Assert.Contains("PILE.Full  Bool  ReadOnly", run.Out, StringComparison.Ordinal);
    }

    [Fact]
    public void JsonFormatIsAnArrayOfDescriptors()
    {
        CliRun run = Cli.Run("tags", Cli.Plant("minimal.json"), "--format", "json");

        Assert.Equal(ExitCodes.Ok, run.ExitCode);
        using JsonDocument document = JsonDocument.Parse(run.Out);
        JsonElement level = document.RootElement.EnumerateArray().Single(t => t.GetProperty("name").GetString() == "CHUTE.Level");
        Assert.Equal("double", level.GetProperty("kind").GetString());
        Assert.Equal("readOnly", level.GetProperty("access").GetString());
        Assert.Equal("fraction", level.GetProperty("unit").GetString());
        Assert.Equal(0.0, level.GetProperty("rangeLow").GetDouble());
        Assert.Equal(1.0, level.GetProperty("rangeHigh").GetDouble());

        JsonElement full = document.RootElement.EnumerateArray().Single(t => t.GetProperty("name").GetString() == "PILE.Full");
        Assert.False(full.TryGetProperty("rangeLow", out _));
    }

    [Fact]
    public void AnInvalidPlantBehavesAsValidateDoes()
    {
        CliRun run = Cli.Run("tags", Cli.Plant("broken.json"));

        Assert.Equal(ExitCodes.PlantInvalid, run.ExitCode);
        Assert.Empty(run.Out);
        Assert.Contains("DSE102", run.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void AControlledPlantListsItsBlocksTags()
    {
        CliRun run = Cli.Run("tags", Cli.Plant("conveyor-control.json"));

        Assert.Equal(ExitCodes.Ok, run.ExitCode);
        Assert.Empty(run.Err);
        Assert.Contains("INT01.Reset  Bool  ReadWrite", run.Out, StringComparison.Ordinal);
        Assert.Contains("CUR01.HiHi.Active  Bool  ReadOnly", run.Out, StringComparison.Ordinal);
        Assert.Contains("SEQ01.StepTime  Double  ReadOnly  s", run.Out, StringComparison.Ordinal);
        Assert.Equal(49, run.Out.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
    }

    [Fact]
    public void AnEmptyPathIsExitThreeNotACrash()
    {
        CliRun run = Cli.Run("tags", "");

        Assert.Equal(ExitCodes.Unreadable, run.ExitCode);
        Assert.Empty(run.Out);
        Assert.StartsWith("Cannot read '': ", run.Err, StringComparison.Ordinal);
        Assert.DoesNotContain("   at ", run.Err, StringComparison.Ordinal);
    }
}
