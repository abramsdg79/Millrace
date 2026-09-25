using System.Text.Json;
using Dse.Components;
using Dse.Core.Catalogue;
using Dse.Core.Testing;

namespace Dse.Cli.Tests;

public class PluginTests
{
    private static readonly string Sample = Cli.Built("Dse.Cli.Tests.SampleModule");
    private static readonly string Clash = Cli.Built("Dse.Cli.Tests.ClashModule");

    [Fact]
    public void TheFixturesWereBuilt()
    {
        Assert.True(File.Exists(Sample), $"Expected the sample module at '{Sample}'.");
        Assert.True(File.Exists(Clash), $"Expected the clash module at '{Clash}'.");
    }

    [Fact]
    public void APluginsTypesAppearInTheCatalogueExport()
    {
        CliRun run = Cli.Run("catalog", "export", "--assembly", Sample);

        Assert.Equal(ExitCodes.Ok, run.ExitCode);
        using JsonDocument document = JsonDocument.Parse(run.Out);
        Assert.Equal(["Dse.Components", "Dse.Control", "Sample"], document.RootElement.GetProperty("modules").EnumerateArray().Select(m => m.GetString()));
        JsonElement sw = document.RootElement.GetProperty("components").EnumerateArray().Single(c => c.GetProperty("type").GetString() == "hysteresis-switch");
        Assert.Equal("Sample", sw.GetProperty("module").GetString());
        Assert.Single(document.RootElement.GetProperty("materials").EnumerateArray(), m => m.GetProperty("name").GetString() == "sample-ore");
    }

    [Fact]
    public void APluginsTypesAppearInTheSchema()
    {
        CliRun run = Cli.Run("schema", "export", "--assembly=" + Sample);

        Assert.Equal(ExitCodes.Ok, run.ExitCode);
        using JsonDocument document = JsonDocument.Parse(run.Out);
        Assert.True(document.RootElement.GetProperty("$defs").TryGetProperty("component.hysteresis-switch", out _));
    }

    [Fact]
    public void APlantUsingAPluginValidatesWithItAndNotWithout()
    {
        CliRun with = Cli.Run("validate", Cli.Plant("sample.json"), "--assembly", Sample);
        CliRun without = Cli.Run("validate", Cli.Plant("sample.json"));

        Assert.Equal(ExitCodes.Ok, with.ExitCode);
        Assert.Equal(ExitCodes.PlantInvalid, without.ExitCode);
        Assert.Contains("DSE102", without.Err, StringComparison.Ordinal);
        Assert.Contains("--assembly", without.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void APluginsConstructorMessageReachesTheUser()
    {
        string path = Path.Combine(Path.GetTempPath(), $"dse-cli-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, """{ "components": [ { "id": "HS", "type": "hysteresis-switch", "parameters": { "onAbove": 60, "offBelow": 80 } } ] }""");
        try
        {
            CliRun run = Cli.Run("validate", path, "--assembly", Sample);

            Assert.Equal(ExitCodes.PlantInvalid, run.ExitCode);
            Assert.Contains("DSE111", run.Err, StringComparison.Ordinal);
            Assert.Contains("offBelow must be below onAbove", run.Err, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void AMissingAssemblyIsExitThree()
    {
        CliRun run = Cli.Run("catalog", "export", "--assembly", Path.Combine(Path.GetTempPath(), "no-such-plugin.dll"));

        Assert.Equal(ExitCodes.Unreadable, run.ExitCode);
        Assert.Empty(run.Out);
        Assert.Contains("the file does not exist", run.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEmptyAssemblyValueIsExitThreeNotACrash()
    {
        CliRun run = Cli.Run("catalog", "export", "--assembly=");

        Assert.Equal(ExitCodes.Unreadable, run.ExitCode);
        Assert.StartsWith("Cannot load assembly '': ", run.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void AFileThatIsNotAnAssemblyIsExitThree()
    {
        CliRun run = Cli.Run("catalog", "export", "--assembly", Cli.Plant("minimal.json"));

        Assert.Equal(ExitCodes.Unreadable, run.ExitCode);
        Assert.StartsWith("Cannot load assembly", run.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void AnAssemblyWithNoModuleSaysSo()
    {
        string noModule = Path.Combine(AppContext.BaseDirectory, "Dse.Io.Abstractions.dll");

        CliRun run = Cli.Run("catalog", "export", "--assembly", noModule);

        Assert.Equal(ExitCodes.Unreadable, run.ExitCode);
        Assert.Contains("contains no catalogue module", run.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void ADuplicateTypeNameNamesBothModules()
    {
        CliRun run = Cli.Run("catalog", "export", "--assembly", Clash);

        Assert.Equal(ExitCodes.Unreadable, run.ExitCode);
        Assert.Contains("'gearbox'", run.Err, StringComparison.Ordinal);
        Assert.Contains("module 'Dse.Components'", run.Err, StringComparison.Ordinal);
        Assert.Contains("module 'Clash'", run.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void TwoPluginsMayBeLoadedTogether()
    {
        CliRun run = Cli.Run("catalog", "export", "--assembly", Sample, "--assembly", Clash);

        // Both load; the clash is still a clash. What matters is that --assembly repeats without a usage error.
        Assert.Equal(ExitCodes.Unreadable, run.ExitCode);
        Assert.Contains("module 'Clash'", run.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void ThePluginPassesCatalogueConformance()
    {
        CatalogueBuilder builder = new CatalogueBuilder().Add<ComponentsModule>();
        Assert.True(ModuleLoader.TryLoad([Sample], builder, out string problem), problem);

        ConformanceReport report = CatalogueConformance.Check(
            builder.Build(),
            new ConformanceFixtures()
                .Parameters("hysteresis-switch", """{ "onAbove": 80, "offBelow": 60 }""")
                .BlockParameters("latch", """{ "set": "X.Set", "reset": "X.Reset" }"""));

        // The shipped types have no fixtures here, so only the plugin's findings are of interest.
        Assert.DoesNotContain(report.Mismatches, m => m.StartsWith("hysteresis-switch:", StringComparison.Ordinal));
        Assert.Contains(report.BuiltTypes, t => t.Name == "HysteresisSwitch");
        Assert.DoesNotContain(report.Mismatches, m => m.StartsWith("latch", StringComparison.Ordinal));
        Assert.Contains(report.BuiltTypes, t => t.Name == "Latch");
    }

    [Fact]
    public void APluginsBlockTypeAppearsInTheCatalogueExport()
    {
        CliRun run = Cli.Run("catalog", "export", "--assembly", Sample);

        Assert.Equal(ExitCodes.Ok, run.ExitCode);
        using JsonDocument document = JsonDocument.Parse(run.Out);
        JsonElement latch = document.RootElement.GetProperty("blocks").EnumerateArray().Single(b => b.GetProperty("type").GetString() == "latch");
        Assert.Equal("Sample", latch.GetProperty("module").GetString());
        Assert.Equal(["set", "reset"], latch.GetProperty("parameters").EnumerateArray().Select(p => p.GetProperty("name").GetString()));
    }

    [Fact]
    public void APluginsBlockTypeAppearsInTheSchema()
    {
        CliRun run = Cli.Run("schema", "export", "--assembly", Sample);

        Assert.Equal(ExitCodes.Ok, run.ExitCode);
        using JsonDocument document = JsonDocument.Parse(run.Out);
        Assert.True(document.RootElement.GetProperty("$defs").TryGetProperty("block.latch", out _));
        Assert.Contains(
            document.RootElement.GetProperty("properties").GetProperty("controllers").GetProperty("items").GetProperty("oneOf").EnumerateArray(),
            b => b.GetProperty("$ref").GetString() == "#/$defs/block.latch");
    }

    [Fact]
    public void APlantUsingAPluginBlockValidatesWithItAndNotWithout()
    {
        CliRun with = Cli.Run("validate", Cli.Plant("sample-block.json"), "--assembly", Sample);
        CliRun without = Cli.Run("validate", Cli.Plant("sample-block.json"));

        Assert.Equal(ExitCodes.Ok, with.ExitCode);
        Assert.Matches(@"controllers\s+1\n", with.Out);
        Assert.Equal(ExitCodes.PlantInvalid, without.ExitCode);
        Assert.Contains("DSE102 $.controllers[0].type", without.Err, StringComparison.Ordinal);
        Assert.Contains("--assembly", without.Err, StringComparison.Ordinal);
    }
}
