using System.Globalization;
using System.Text.Json;
using Dse.Configuration;
using Dse.Core.Catalogue;
using Dse.Core.Logging;
using Dse.Scenarios;

namespace Dse.Cli.Commands;

/// <summary>
/// Runs a scenario against the plant it names. The command is a shell:
/// <c>ScenarioRunner</c> decides what happened and <c>GoldenLog</c> decides
/// whether it matched, so a test and the command line agree by construction.
/// </summary>
internal static class RunScenario
{
    public static int Run(CliContext context)
    {
        string path = context.CommandLine.Argument!;
        if (!PlantFile.TryRead(context, path, out string json))
        {
            return ExitCodes.Unreadable;
        }

        ScenarioParseResult parsed = ScenarioLoader.Parse(json);
        if (parsed.Scenario is null)
        {
            return Invalid(context, path, null, parsed.Diagnostics, parsed.ToText());
        }

        string plantPath = parsed.Scenario.ResolvePlantPath(path);
        if (!PlantFile.TryRead(context, plantPath, out string plantJson))
        {
            return ExitCodes.Unreadable;
        }

        ScenarioRunResult result = ScenarioRunner.Run(parsed.Scenario, plantJson, context.Catalogue);
        return result.IsValid
            ? Deliver(context, path, plantPath, result)
            : Invalid(context, path, plantPath, result.Diagnostics, result.ToText());
    }

    private static int Deliver(CliContext context, string path, string plantPath, ScenarioRunResult result)
    {
        string log = GoldenLog.Normalise(result.Events!.ToText());
        string? expect = context.CommandLine.Single(CommandTable.Expect);
        LogComparison? comparison = null;
        string? actualPath = null;

        if (expect is not null)
        {
            if (!PlantFile.TryRead(context, expect, out string golden))
            {
                return ExitCodes.Unreadable;
            }

            comparison = GoldenLog.Compare(golden, log);
            if (!comparison.Matched)
            {
                actualPath = expect + ".actual";
                if (!TryWrite(context, actualPath, log))
                {
                    return ExitCodes.Unreadable;
                }
            }
        }

        string payload = context.Json
            ? Json(path, plantPath, result, comparison, actualPath, result.Diagnostics)
            : log;
        string? outPath = context.CommandLine.Single(CommandTable.Out);
        if (outPath is not null)
        {
            if (!TryWrite(context, outPath, payload))
            {
                return ExitCodes.Unreadable;
            }
        }
        else if (context.Json || expect is null)
        {
            context.Out.Write(payload);
        }

        if (comparison is null)
        {
            return ExitCodes.Ok;
        }

        if (comparison.Matched)
        {
            if (!context.Json)
            {
                context.Out.Write(string.Create(
                    CultureInfo.InvariantCulture, $"Matched {expect} ({result.Summary!.Events} events).\n"));
            }

            return ExitCodes.Ok;
        }

        if (!context.Json)
        {
            context.Err.Write(comparison.Report);
            context.Err.Write($"The actual log is at '{actualPath}'.\n");
        }

        return ExitCodes.LogMismatch;
    }

    private static int Invalid(
        CliContext context, string path, string? plantPath, IReadOnlyList<ConfigDiagnostic> diagnostics, string text)
    {
        if (context.Json)
        {
            context.Out.Write(Json(path, plantPath, null, null, null, diagnostics));
            return ExitCodes.PlantInvalid;
        }

        PlantFile.ReportDiagnostics(context, path, text, diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error));
        return ExitCodes.PlantInvalid;
    }

    private static bool TryWrite(CliContext context, string path, string payload)
    {
        try
        {
            File.WriteAllText(path, payload);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            context.Err.Write($"Cannot write '{path}': {ex.Message}\n");
            return false;
        }
    }

    /// <summary><c>ok</c> is "the configuration is sound"; <c>match</c> is "the behaviour is unchanged".</summary>
    private static string Json(
        string scenarioPath,
        string? plantPath,
        ScenarioRunResult? result,
        LogComparison? comparison,
        string? actualPath,
        IReadOnlyList<ConfigDiagnostic> diagnostics)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, CatalogueJson.WriterOptions))
        {
            writer.WriteStartObject();
            writer.WriteBoolean("ok", result is not null);
            writer.WriteString("scenario", scenarioPath);
            if (plantPath is null)
            {
                writer.WriteNull("plant");
            }
            else
            {
                writer.WriteString("plant", plantPath);
            }

            writer.WriteNumber("ticks", result?.Summary?.Ticks ?? 0L);

            writer.WriteStartArray("events");
            if (result?.Events is { } log)
            {
                foreach (SimEventRecord record in log.Records)
                {
                    writer.WriteStartObject();
                    writer.WriteNumber("tick", record.Tick);
                    writer.WriteString("time", record.SimTime.ToString("o", CultureInfo.InvariantCulture));
                    writer.WriteString("source", record.Source);
                    writer.WriteString("code", record.Code);
                    writer.WriteString("message", record.Message);
                    writer.WriteEndObject();
                }
            }

            writer.WriteEndArray();

            if (comparison is not null)
            {
                writer.WriteBoolean("match", comparison.Matched);
                if (!comparison.Matched)
                {
                    writer.WriteNumber("firstDifferentLine", comparison.FirstDifferentLine);
                    writer.WriteString("actual", actualPath!);
                }
            }

            writer.WriteStartArray("diagnostics");
            foreach (ConfigDiagnostic diagnostic in diagnostics)
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
