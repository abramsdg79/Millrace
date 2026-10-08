using System.Text.Json;
using Millrace.Components;
using Millrace.Configuration;
using Millrace.Control.Catalogue;
using Millrace.Core.Catalogue;

namespace Millrace.Cli.Tests;

public class ExportCommandTests
{
    private static readonly ComponentCatalogue Shipped =
        new CatalogueBuilder().Add<ComponentsModule>().Add<ControlModule>().Build();

    [Fact]
    public void CatalogExportPrintsExactlyTheLibrarysExport()
    {
        CliRun run = Cli.Run("catalog", "export");

        Assert.Equal(ExitCodes.Ok, run.ExitCode);
        Assert.Empty(run.Err);
        Assert.Equal(CatalogueJson.Export(Shipped), run.Out);
    }

    [Fact]
    public void SchemaExportPrintsExactlyTheLibrarysSchema()
    {
        CliRun run = Cli.Run("schema", "export");

        Assert.Equal(ExitCodes.Ok, run.ExitCode);
        Assert.Equal(PlantSchema.Generate(Shipped), run.Out);
    }

    [Fact]
    public void OutWritesTheFileAndKeepsStdoutEmpty()
    {
        string path = Path.Combine(Path.GetTempPath(), $"millrace-cli-{Guid.NewGuid():N}.json");
        try
        {
            CliRun run = Cli.Run("schema", "export", "--out", path);

            Assert.Equal(ExitCodes.Ok, run.ExitCode);
            Assert.Empty(run.Out);
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
            Assert.Equal(PlantSchema.Dialect, document.RootElement.GetProperty("$schema").GetString());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void AnUnwritableOutPathIsExitThree()
    {
        CliRun run = Cli.Run("catalog", "export", "--out", Path.Combine(Path.GetTempPath(), "no-such-dir-millrace", "x", "c.json"));

        Assert.Equal(ExitCodes.Unreadable, run.ExitCode);
        Assert.Contains("Cannot write", run.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEmptyOutPathIsExitThreeNotACrash()
    {
        CliRun run = Cli.Run("catalog", "export", "--out", "");

        Assert.Equal(ExitCodes.Unreadable, run.ExitCode);
        Assert.StartsWith("Cannot write '': ", run.Err, StringComparison.Ordinal);
        Assert.DoesNotContain("   at ", run.Err, StringComparison.Ordinal);
    }
}
