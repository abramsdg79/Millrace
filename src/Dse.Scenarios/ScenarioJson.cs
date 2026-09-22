using System.Globalization;
using System.Text.Json;
using Dse.Core.Catalogue;
using Dse.Core.Faults;

namespace Dse.Scenarios;

/// <summary>
/// A scenario as deterministic JSON: same scenario, same bytes. The key order
/// is the format's documented order, the timeline is in landing order, and the
/// text is normalised to <c>\n</c> with one trailing newline — so a recording
/// can be committed and diffed.
/// </summary>
public static class ScenarioJson
{
    /// <summary>Renders <paramref name="scenario"/>. Parsing the result yields a scenario that renders identically.</summary>
    public static string Write(Scenario scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        RejectNonFinite(scenario);

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, CatalogueJson.WriterOptions))
        {
            writer.WriteStartObject();
            writer.WriteString("plant", scenario.PlantPath);
            if (scenario.Seed is { } seed)
            {
                writer.WriteNumber("seed", seed);
            }

            if (scenario.StartTime is { } start)
            {
                writer.WriteString("startTime", Iso(start));
            }

            if (scenario.TimeStep is { } step)
            {
                writer.WriteNumber("timeStepMs", step.TotalMilliseconds);
            }

            writer.WriteNumber("duration", ScenarioDiagnostics.Seconds(scenario.Duration));
            writer.WriteStartArray("timeline");
            foreach (ScenarioAction action in scenario.Timeline)
            {
                WriteOne(writer, action);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return CatalogueJson.Finish(stream);
    }

    private static void WriteOne(Utf8JsonWriter writer, ScenarioAction action)
    {
        writer.WriteStartObject();
        writer.WriteNumber("at", ScenarioDiagnostics.Seconds(action.At));
        switch (action)
        {
            case WriteAction write:
                writer.WriteString("write", write.Tag);
                WriteValue(writer, write.Value);
                break;

            case FaultAction fault:
                writer.WriteString("fault", fault.ComponentId);
                writer.WriteString("id", fault.FaultId);
                if (fault.Arguments.Count > 0)
                {
                    writer.WriteStartObject("args");
                    foreach (FaultArgument argument in fault.Arguments)
                    {
                        writer.WriteNumber(argument.Name, argument.Value);
                    }

                    writer.WriteEndObject();
                }

                break;

            case ClearAction clear:
                writer.WriteString("clear", clear.ComponentId);
                writer.WriteString("id", clear.FaultId);
                break;

            default:
                throw new ArgumentException(
                    $"'{action.GetType().Name}' is not an action this format has. A scenario carries writes, faults and clears.",
                    nameof(action));
        }

        writer.WriteEndObject();
    }

    private static void WriteValue(Utf8JsonWriter writer, ScenarioValue value)
    {
        switch (value.Kind)
        {
            case ScenarioValueKind.Bool:
                writer.WriteBoolean("value", value.Boolean);
                break;
            case ScenarioValueKind.Integer:
                writer.WriteNumber("value", value.Integer);
                break;
            default:
                writer.WriteNumber("value", value.Number);
                break;
        }
    }

    /// <summary>
    /// JSON has neither NaN nor infinity. A scenario that carries one cannot be
    /// written, and the message says which action and which number.
    /// </summary>
    private static void RejectNonFinite(Scenario scenario)
    {
        for (int i = 0; i < scenario.Timeline.Count; i++)
        {
            switch (scenario.Timeline[i])
            {
                case WriteAction { Value.Kind: ScenarioValueKind.Number } write when !double.IsFinite(write.Value.Number):
                    throw new ArgumentException(
                        string.Create(CultureInfo.InvariantCulture,
                            $"Action {i} writes {write.Value.Number} to '{write.Tag}', which JSON cannot represent. A scenario carries finite numbers only."),
                        nameof(scenario));

                case FaultAction fault:
                    foreach (FaultArgument argument in fault.Arguments)
                    {
                        if (!double.IsFinite(argument.Value))
                        {
                            throw new ArgumentException(
                                string.Create(CultureInfo.InvariantCulture,
                                    $"Action {i} gives '{fault.FaultId}' the argument {argument.Name}={argument.Value}, which JSON cannot represent. A scenario carries finite numbers only."),
                                nameof(scenario));
                        }
                    }

                    break;
            }
        }
    }

    /// <summary>
    /// Round-trip ISO 8601: a literal <c>Z</c> at offset zero, <c>±hh:mm</c>
    /// otherwise, and a fractional part only when there is one — a bare
    /// <c>'.'</c> would be emitted whatever the value, and would not parse back.
    /// </summary>
    private static string Iso(DateTimeOffset value)
    {
        string fraction = value.Ticks % TimeSpan.TicksPerSecond == 0L ? string.Empty : "'.'FFFFFFF";
        return value.Offset == TimeSpan.Zero
            ? value.ToString($"yyyy'-'MM'-'dd'T'HH':'mm':'ss{fraction}'Z'", CultureInfo.InvariantCulture)
            : value.ToString($"yyyy'-'MM'-'dd'T'HH':'mm':'ss{fraction}zzz", CultureInfo.InvariantCulture);
    }
}
