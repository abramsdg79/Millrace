using System.Text.Json;

namespace Dse.Cli.Tests;

public class ValidateCommandTests
{
    [Fact]
    public void AValidPlantPrintsASummary()
    {
        CliRun run = Cli.Run("validate", Cli.Plant("minimal.json"));

        Assert.Equal(ExitCodes.Ok, run.ExitCode);
        Assert.Empty(run.Err);
        Assert.StartsWith("OK  ", run.Out, StringComparison.Ordinal);
        Assert.Contains("minimal.json", run.Out, StringComparison.Ordinal);
        Assert.Matches(@"components\s+3\n", run.Out);
        Assert.Matches(@"leaves\s+3\n", run.Out);
        Assert.Matches(@"flow links\s+2\n", run.Out);
        Assert.Matches(@"time step\s+10 ms\n", run.Out);
    }

    [Fact]
    public void TimeStepOverridesTheFile()
    {
        CliRun run = Cli.Run("validate", Cli.Plant("minimal.json"), "--time-step", "2.5");

        Assert.Equal(ExitCodes.Ok, run.ExitCode);
        Assert.Matches(@"time step\s+2\.5 ms\n", run.Out);
    }

    [Fact]
    public void AnInvalidPlantPrintsEveryDiagnosticToStderrAndExitsOne()
    {
        CliRun run = Cli.Run("validate", Cli.Plant("broken.json"));

        Assert.Equal(ExitCodes.PlantInvalid, run.ExitCode);
        Assert.Empty(run.Out);
        Assert.Contains("DSE102 $.components[2].type", run.Err, StringComparison.Ordinal);
        Assert.Contains("DSE103 $.components[1].parameters.capacityKg", run.Err, StringComparison.Ordinal);
        Assert.Contains("  Fix: ", run.Err, StringComparison.Ordinal);
        Assert.EndsWith("2 errors in broken.json\n", run.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void JsonFormatPutsTheDiagnosticsOnStdout()
    {
        CliRun run = Cli.Run("validate", Cli.Plant("broken.json"), "--format", "json");

        Assert.Equal(ExitCodes.PlantInvalid, run.ExitCode);
        Assert.Empty(run.Err);
        using JsonDocument document = JsonDocument.Parse(run.Out);
        JsonElement root = document.RootElement;
        Assert.False(root.GetProperty("ok").GetBoolean());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("summary").ValueKind);
        Assert.Equal(2, root.GetProperty("diagnostics").GetArrayLength());
        JsonElement first = root.GetProperty("diagnostics")[0];
        Assert.Equal(["code", "severity", "path", "message", "fix"], first.EnumerateObject().Select(p => p.Name));
        Assert.Equal("error", first.GetProperty("severity").GetString());
    }

    [Fact]
    public void JsonFormatOfAValidPlantCarriesTheSummary()
    {
        CliRun run = Cli.Run("validate", Cli.Plant("minimal.json"), "--format", "json");

        Assert.Equal(ExitCodes.Ok, run.ExitCode);
        using JsonDocument document = JsonDocument.Parse(run.Out);
        JsonElement summary = document.RootElement.GetProperty("summary");
        Assert.True(document.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal(3, summary.GetProperty("components").GetInt32());
        Assert.Equal(3, summary.GetProperty("leaves").GetInt32());
        Assert.Equal(2, summary.GetProperty("flowLinks").GetInt32());
        Assert.Equal(10.0, summary.GetProperty("timeStepMs").GetDouble());
        Assert.True(summary.GetProperty("tags").GetInt32() > 0);
        Assert.Empty(document.RootElement.GetProperty("diagnostics").EnumerateArray());
    }

    [Fact]
    public void AMissingFileIsExitThree()
    {
        CliRun run = Cli.Run("validate", Cli.Plant("nope.json"));

        Assert.Equal(ExitCodes.Unreadable, run.ExitCode);
        Assert.Empty(run.Out);
        Assert.Contains("Cannot read", run.Err, StringComparison.Ordinal);
        Assert.Contains("nope.json", run.Err, StringComparison.Ordinal);
    }
}
