using System.Text.Json;

namespace Dse.Cli.Tests;

public class RunCommandTests
{
    private static readonly string Sample = Cli.Built("Dse.Cli.Tests.SampleModule");

    private static string TempPath(string extension) =>
        Path.Combine(Path.GetTempPath(), $"dse-run-{Guid.NewGuid():N}{extension}");

    [Fact]
    public void AScenarioRunsAndPrintsItsEventLog()
    {
        CliRun run = Cli.Run("run", Cli.Scenario("minimal.json"));

        Assert.Equal(ExitCodes.Ok, run.ExitCode);
        Assert.Empty(run.Err);
        Assert.Contains("FEED.Enabled  WRITE  Set to false.", run.Out, StringComparison.Ordinal);
        Assert.DoesNotContain('\r', run.Out);
    }

    [Fact]
    public void OutTakesTheLogAndStandardOutputStaysEmpty()
    {
        string file = TempPath(".log");
        try
        {
            CliRun run = Cli.Run("run", Cli.Scenario("minimal.json"), "--out", file);

            Assert.Equal(ExitCodes.Ok, run.ExitCode);
            Assert.Empty(run.Out);
            Assert.Empty(run.Err);
            Assert.Contains("FEED.Enabled  WRITE  Set to false.", File.ReadAllText(file), StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void AnExpectedLogThatMatchesPrintsTheMatchLine()
    {
        string golden = TempPath(".log");
        try
        {
            Assert.Equal(ExitCodes.Ok, Cli.Run("run", Cli.Scenario("minimal.json"), "--out", golden).ExitCode);

            CliRun run = Cli.Run("run", Cli.Scenario("minimal.json"), "--expect", golden);

            Assert.Equal(ExitCodes.Ok, run.ExitCode);
            Assert.Empty(run.Err);
            Assert.StartsWith($"Matched {golden} (", run.Out, StringComparison.Ordinal);
            Assert.EndsWith(" events).\n", run.Out, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(golden);
        }
    }

    [Fact]
    public void AnExpectedLogThatDiffersIsExitFourAndWritesTheActualBesideIt()
    {
        string golden = TempPath(".log");
        File.WriteAllText(golden, "06:00:00.000  NOPE  WRONG  Not this.\n");
        try
        {
            CliRun run = Cli.Run("run", Cli.Scenario("minimal.json"), "--expect", golden);

            Assert.Equal(ExitCodes.LogMismatch, run.ExitCode);
            Assert.Empty(run.Out);
            Assert.Contains("The logs differ at line 1.", run.Err, StringComparison.Ordinal);
            Assert.Contains("expected (1 lines):", run.Err, StringComparison.Ordinal);
            Assert.Contains($"The actual log is at '{golden}.actual'.", run.Err, StringComparison.Ordinal);
            Assert.Contains("FEED.Enabled  WRITE  Set to false.", File.ReadAllText(golden + ".actual"), StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(golden);
            File.Delete(golden + ".actual");
        }
    }

    [Fact]
    public void AnInvalidScenarioIsExitOneWithItsDiagnosticsOnStandardError()
    {
        CliRun run = Cli.Run("run", Cli.Scenario("no-duration.json"));

        Assert.Equal(ExitCodes.PlantInvalid, run.ExitCode);
        Assert.Empty(run.Out);
        Assert.Contains("DSE202 $.duration", run.Err, StringComparison.Ordinal);
        Assert.Contains("  Fix: ", run.Err, StringComparison.Ordinal);
        Assert.EndsWith("1 error in no-duration.json\n", run.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void AnInvalidPlantIsExitOneAndLeadsWithDse205()
    {
        CliRun run = Cli.Run("run", Cli.Scenario("broken-plant.json"));

        Assert.Equal(ExitCodes.PlantInvalid, run.ExitCode);
        Assert.Empty(run.Out);
        Assert.StartsWith("DSE205 $.plant", run.Err, StringComparison.Ordinal);
        Assert.Contains("DSE102", run.Err, StringComparison.Ordinal);
        Assert.EndsWith("3 errors in broken-plant.json\n", run.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void AMissingScenarioFileIsExitThree()
    {
        CliRun run = Cli.Run("run", Cli.Scenario("no-such-scenario.json"));

        Assert.Equal(ExitCodes.Unreadable, run.ExitCode);
        Assert.Empty(run.Out);
        Assert.Contains("Cannot read", run.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void AMissingPlantFileIsExitThree()
    {
        CliRun run = Cli.Run("run", Cli.Scenario("missing-plant.json"));

        Assert.Equal(ExitCodes.Unreadable, run.ExitCode);
        Assert.Contains("no-such-plant.json", run.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void AMissingGoldenFileIsExitThree()
    {
        CliRun run = Cli.Run("run", Cli.Scenario("minimal.json"), "--expect", TempPath(".log"));

        Assert.Equal(ExitCodes.Unreadable, run.ExitCode);
        Assert.Empty(run.Out);
        Assert.Contains("Cannot read", run.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnwritableOutIsExitThree()
    {
        string file = Path.Combine(Path.GetTempPath(), $"dse-run-{Guid.NewGuid():N}", "log.txt");

        CliRun run = Cli.Run("run", Cli.Scenario("minimal.json"), "--out", file);

        Assert.Equal(ExitCodes.Unreadable, run.ExitCode);
        Assert.StartsWith("Cannot write '", run.Err, StringComparison.Ordinal);
        Assert.DoesNotContain("   at ", run.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEmptyPathIsExitThreeNotACrash()
    {
        CliRun run = Cli.Run("run", "");

        Assert.Equal(ExitCodes.Unreadable, run.ExitCode);
        Assert.Empty(run.Out);
        Assert.StartsWith("Cannot read '': ", run.Err, StringComparison.Ordinal);
        Assert.DoesNotContain("   at ", run.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void JsonFormatCarriesTheEventsAndTheTicks()
    {
        CliRun run = Cli.Run("run", Cli.Scenario("minimal.json"), "--format", "json");

        Assert.Equal(ExitCodes.Ok, run.ExitCode);
        Assert.Empty(run.Err);
        using JsonDocument document = JsonDocument.Parse(run.Out);
        JsonElement root = document.RootElement;
        Assert.True(root.GetProperty("ok").GetBoolean());
        Assert.EndsWith("minimal.json", root.GetProperty("scenario").GetString(), StringComparison.Ordinal);
        Assert.EndsWith("minimal.json", root.GetProperty("plant").GetString(), StringComparison.Ordinal);
        Assert.Equal(500L, root.GetProperty("ticks").GetInt64());
        Assert.False(root.TryGetProperty("match", out _));
        Assert.Empty(root.GetProperty("diagnostics").EnumerateArray());
        JsonElement written = root.GetProperty("events").EnumerateArray()
            .Single(e => e.GetProperty("source").GetString() == "FEED.Enabled");
        Assert.Equal(["tick", "time", "source", "code", "message"], written.EnumerateObject().Select(p => p.Name));
        Assert.Equal(200L, written.GetProperty("tick").GetInt64());
        Assert.Equal("WRITE", written.GetProperty("code").GetString());
    }

    [Fact]
    public void JsonFormatOfAnInvalidScenarioIsOkFalseOnStandardOutput()
    {
        CliRun run = Cli.Run("run", Cli.Scenario("no-duration.json"), "--format", "json");

        Assert.Equal(ExitCodes.PlantInvalid, run.ExitCode);
        Assert.Empty(run.Err);
        using JsonDocument document = JsonDocument.Parse(run.Out);
        JsonElement root = document.RootElement;
        Assert.False(root.GetProperty("ok").GetBoolean());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("plant").ValueKind);
        Assert.Equal(0L, root.GetProperty("ticks").GetInt64());
        Assert.Empty(root.GetProperty("events").EnumerateArray());
        JsonElement diagnostic = Assert.Single(root.GetProperty("diagnostics").EnumerateArray());
        Assert.Equal("DSE202", diagnostic.GetProperty("code").GetString());
        Assert.Equal("error", diagnostic.GetProperty("severity").GetString());
    }

    [Fact]
    public void JsonFormatWithExpectCarriesTheMatch()
    {
        string golden = TempPath(".log");
        File.WriteAllText(golden, "06:00:00.000  NOPE  WRONG  Not this.\n");
        try
        {
            CliRun run = Cli.Run("run", Cli.Scenario("minimal.json"), "--expect", golden, "--format", "json");

            Assert.Equal(ExitCodes.LogMismatch, run.ExitCode);
            Assert.Empty(run.Err);
            using JsonDocument document = JsonDocument.Parse(run.Out);
            JsonElement root = document.RootElement;
            Assert.True(root.GetProperty("ok").GetBoolean());
            Assert.False(root.GetProperty("match").GetBoolean());
            Assert.Equal(1, root.GetProperty("firstDifferentLine").GetInt32());
            Assert.Equal(golden + ".actual", root.GetProperty("actual").GetString());
        }
        finally
        {
            File.Delete(golden);
            File.Delete(golden + ".actual");
        }
    }

    [Fact]
    public void TheWorkedExampleRunsFromItsFilesAndMatchesTheControlGolden()
    {
        string golden = Cli.Golden("conveyor-control.log");

        CliRun run = Cli.Run("run", Cli.Scenario("conveyor-control.json"), "--expect", golden);

        Assert.Equal(ExitCodes.Ok, run.ExitCode);
        Assert.Empty(run.Err);
        Assert.Equal($"Matched {golden} (40 events).\n", run.Out);
    }

    [Fact]
    public void APlantThatNeedsAPluginRunsWithAssembly()
    {
        CliRun with = Cli.Run("run", Cli.Scenario("sample.json"), "--assembly", Sample);
        CliRun without = Cli.Run("run", Cli.Scenario("sample.json"));

        Assert.Equal(ExitCodes.Ok, with.ExitCode);
        Assert.Equal(ExitCodes.PlantInvalid, without.ExitCode);
        Assert.Contains("DSE205", without.Err, StringComparison.Ordinal);
        Assert.Contains("DSE102", without.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void TheHelpListsRunAndItsExitCode()
    {
        CliRun run = Cli.Run("help");

        Assert.Equal(ExitCodes.Ok, run.ExitCode);
        Assert.Contains("run <scenario.json>", run.Out, StringComparison.Ordinal);
        Assert.Contains("4 the event log differs", run.Out, StringComparison.Ordinal);
    }

    [Fact]
    public void TheCommandsHelpListsItsOptions()
    {
        CliRun run = Cli.Run("run", "--help");

        Assert.Equal(ExitCodes.Ok, run.ExitCode);
        Assert.Contains("--expect <golden.log>", run.Out, StringComparison.Ordinal);
        Assert.Contains("--out <file>", run.Out, StringComparison.Ordinal);
        Assert.Contains("--assembly <path>", run.Out, StringComparison.Ordinal);
        Assert.DoesNotContain("--time-step", run.Out, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("run")]
    [InlineData("run a.json b.json")]
    [InlineData("run a.json --expect")]
    [InlineData("run a.json --time-step 5")]
    public void MalformedInvocationsAreUsageErrors(string line)
    {
        CliRun run = Cli.Run(line.Split(' '));

        Assert.Equal(ExitCodes.Usage, run.ExitCode);
        Assert.Empty(run.Out);
        Assert.NotEmpty(run.Err);
    }

    [Fact]
    public void AnEnormousTimeStepExitsOneInsteadOfCrashing()
    {
        CliRun run = Cli.Run("run", Cli.Scenario("huge-step.json"));

        Assert.Equal(ExitCodes.PlantInvalid, run.ExitCode);
        Assert.Empty(run.Out);
        Assert.Contains("DSE202 $.timeStepMs", run.Err, StringComparison.Ordinal);
        Assert.Contains("longer than a day", run.Err, StringComparison.Ordinal);
        Assert.DoesNotContain("   at ", run.Err, StringComparison.Ordinal);
    }
}
