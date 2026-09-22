using System.Globalization;
using System.Text.Json;
using Dse.Configuration;
using Dse.Core;
using Dse.Core.Catalogue;

namespace Dse.Cli.Commands;

/// <summary>Reads, loads and builds the plant a command was given; reports failure the same way for every command.</summary>
internal static class PlantFile
{
    /// <summary>Exit code 0 with a loaded plant and its built simulation, or a non-zero code after reporting why.</summary>
    public static int TryBuild(CliContext context, out LoadResult? result, out Simulation? simulation)
    {
        result = null;
        simulation = null;
        string path = context.CommandLine.Argument!;
        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            context.Err.Write($"Cannot read '{path}': {ex.Message}\n");
            return ExitCodes.Unreadable;
        }

        result = PlantLoader.Load(json, context.Catalogue, new LoadOptions { TimeStep = context.TimeStep });
        if (!result.IsValid)
        {
            ReportInvalid(context, path, result);
            return ExitCodes.PlantInvalid;
        }

        simulation = result.Builder!.Build();
        return ExitCodes.Ok;
    }

    private static void ReportInvalid(CliContext context, string path, LoadResult result)
    {
        if (context.Json)
        {
            context.Out.Write(ValidationJson(path, result, summary: null));
            return;
        }

        int errors = result.Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error);
        context.Err.Write(result.ToText());
        context.Err.Write(string.Create(
            CultureInfo.InvariantCulture, $"\n{errors} error{(errors == 1 ? string.Empty : "s")} in {Path.GetFileName(path)}\n"));
    }

    /// <summary>The <c>validate --format json</c> document. <paramref name="summary"/> writes the summary object's members, or is null.</summary>
    public static string ValidationJson(string path, LoadResult result, Action<Utf8JsonWriter>? summary)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, CatalogueJson.WriterOptions))
        {
            writer.WriteStartObject();
            writer.WriteBoolean("ok", result.IsValid);
            writer.WriteString("file", path);
            if (summary is null)
            {
                writer.WriteNull("summary");
            }
            else
            {
                writer.WriteStartObject("summary");
                summary(writer);
                writer.WriteEndObject();
            }

            writer.WriteStartArray("diagnostics");
            foreach (ConfigDiagnostic diagnostic in result.Diagnostics)
            {
                writer.WriteStartObject();
                writer.WriteString("code", diagnostic.Code);
                writer.WriteString("severity", CatalogueJson.Camel(diagnostic.Severity.ToString()));
                writer.WriteString("path", diagnostic.Path);
                writer.WriteString("message", diagnostic.Message);
                writer.WriteString("fix", diagnostic.Fix);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return CatalogueJson.Finish(stream);
    }
}
