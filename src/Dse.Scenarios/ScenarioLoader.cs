using System.Globalization;
using System.Text.Json;
using Dse.Configuration;
using Dse.Core.Catalogue;
using Dse.Core.Faults;

namespace Dse.Scenarios;

/// <summary>
/// Turns scenario text into a <see cref="Scenario"/>, or into every structural
/// reason it cannot be one. Pure: no file system, no catalogue, no plant.
/// Whatever needs the plant — does that tag exist, is that component a fault
/// target — is <c>ScenarioRunner</c>'s job.
/// </summary>
public static class ScenarioLoader
{
    /// <summary>The largest time a scenario may name, in seconds: about 31 years, and far inside <see cref="TimeSpan"/>.</summary>
    internal const double MaxSeconds = 1.0e9;

    /// <summary>A time step longer than a day is a mistake, and 1e30 ms overflows <see cref="TimeSpan"/>.</summary>
    private const double MaxTimeStepMs = 86_400_000.0;

    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static readonly string[] TopLevelKeys = ["plant", "seed", "startTime", "timeStepMs", "duration", "timeline"];

    private static readonly string[] ActionKeys = ["at", "write", "value", "fault", "clear", "id", "args"];

    // The same forms the plant loader's defaults.startTime accepts; see
    // Dse.Configuration.Loading.StructureStage.TryParseStartTime (R54). If one
    // changes, change both.
    private static readonly string[] StartTimeUtcFormats =
    [
        "yyyy'-'MM'-'dd'T'HH':'mm':'ss'.'FFFFFFF'Z'",
        "yyyy'-'MM'-'dd'T'HH':'mm':'ss'Z'",
    ];

    private static readonly string[] StartTimeOffsetFormats =
    [
        "yyyy'-'MM'-'dd'T'HH':'mm':'ss'.'FFFFFFFzzz",
        "yyyy'-'MM'-'dd'T'HH':'mm':'sszzz",
    ];

    /// <summary>Parses scenario text, collecting every structural problem rather than stopping at the first.</summary>
    public static ScenarioParseResult Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        var diagnostics = new List<ConfigDiagnostic>();
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json, DocumentOptions);
        }
        catch (JsonException ex)
        {
            long line = (ex.LineNumber ?? 0) + 1;
            long column = (ex.BytePositionInLine ?? 0) + 1;
            diagnostics.Add(ScenarioDiagnostics.Error(
                ScenarioDiagnostics.Syntax,
                "$",
                string.Create(CultureInfo.InvariantCulture, $"The scenario is not valid JSON at line {line}, column {column}: {FirstSentence(ex.Message)}"),
                "Correct the JSON at that position. Comments and trailing commas are allowed; everything else must be strict JSON."));
            return new ScenarioParseResult(diagnostics, null);
        }

        using (document)
        {
            Scenario? scenario = Read(document.RootElement, diagnostics);
            return new ScenarioParseResult(diagnostics, diagnostics.Count == 0 ? scenario : null);
        }
    }

    // System.Text.Json appends "LineNumber: n | BytePositionInLine: m." to its messages; that is already in ours.
    private static string FirstSentence(string message)
    {
        int cut = message.IndexOf(" LineNumber:", StringComparison.Ordinal);
        string text = (cut < 0 ? message : message[..cut]).TrimEnd();
        return text.EndsWith('.') ? text : text + ".";
    }

    private static Scenario? Read(JsonElement root, List<ConfigDiagnostic> diagnostics)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            diagnostics.Add(ScenarioDiagnostics.Error(
                ScenarioDiagnostics.BadValue,
                "$",
                "A scenario file is a JSON object.",
                "Wrap the content in { … } with a \"plant\" and a \"duration\"."));
            return null;
        }

        CheckKeys(root, "$", TopLevelKeys, "a scenario", diagnostics);

        string? plant = ReadPlant(root, diagnostics);
        ulong? seed = ReadSeed(root, diagnostics);
        DateTimeOffset? startTime = ReadStartTime(root, diagnostics);
        TimeSpan? step = ReadTimeStep(root, diagnostics);
        TimeSpan? duration = ReadDuration(root, step, diagnostics);
        List<ScenarioAction> timeline = ReadTimeline(root, duration, step, diagnostics);

        return plant is null || duration is null
            ? null
            : new Scenario(plant, seed, startTime, step, duration.Value, timeline);
    }

    private static void CheckKeys(JsonElement element, string path, string[] allowed, string owner, List<ConfigDiagnostic> diagnostics)
    {
        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (Array.IndexOf(allowed, property.Name) < 0)
            {
                diagnostics.Add(ScenarioDiagnostics.Error(
                    ScenarioDiagnostics.UnknownKey,
                    path == "$" ? $"$.{property.Name}" : $"{path}.{property.Name}",
                    $"'{property.Name}' is not a key {owner} has.",
                    Suggest.Fix(property.Name, allowed, "keys")));
            }
        }
    }

    private static string? ReadPlant(JsonElement root, List<ConfigDiagnostic> diagnostics)
    {
        if (root.TryGetProperty("plant", out JsonElement element)
            && element.ValueKind == JsonValueKind.String
            && element.GetString() is { Length: > 0 } path)
        {
            return path;
        }

        diagnostics.Add(ScenarioDiagnostics.Error(
            ScenarioDiagnostics.BadValue,
            "$.plant",
            "A scenario needs a \"plant\": the path of the plant file, relative to this scenario.",
            "Add \"plant\": \"conveyor-line.json\", naming a plant file beside this one."));
        return null;
    }

    private static ulong? ReadSeed(JsonElement root, List<ConfigDiagnostic> diagnostics)
    {
        if (!root.TryGetProperty("seed", out JsonElement element))
        {
            return null;
        }

        if (element.ValueKind == JsonValueKind.Number && element.TryGetUInt64(out ulong seed))
        {
            return seed;
        }

        diagnostics.Add(ScenarioDiagnostics.Error(
            ScenarioDiagnostics.BadValue,
            "$.seed",
            "\"seed\" must be a whole number, zero or greater.",
            "Use a non-negative integer such as 1."));
        return null;
    }

    private static DateTimeOffset? ReadStartTime(JsonElement root, List<ConfigDiagnostic> diagnostics)
    {
        if (!root.TryGetProperty("startTime", out JsonElement element))
        {
            return null;
        }

        if (element.ValueKind == JsonValueKind.String && TryParseStartTime(element.GetString(), out DateTimeOffset parsed))
        {
            return parsed;
        }

        diagnostics.Add(ScenarioDiagnostics.Error(
            ScenarioDiagnostics.BadValue,
            "$.startTime",
            "\"startTime\" must be an ISO 8601 date and time with an offset.",
            "Write it like \"2026-01-01T06:00:00Z\" or \"2026-03-01T08:00:00+02:00\"."));
        return null;
    }

    /// <summary>An ISO 8601 date-time WITH an offset. An offset-less string would adopt the host's time zone, so it is rejected.</summary>
    private static bool TryParseStartTime(string? text, out DateTimeOffset parsed) =>
        DateTimeOffset.TryParseExact(text, StartTimeUtcFormats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out parsed)
        || DateTimeOffset.TryParseExact(text, StartTimeOffsetFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed);

    private static TimeSpan? ReadTimeStep(JsonElement root, List<ConfigDiagnostic> diagnostics)
    {
        if (!root.TryGetProperty("timeStepMs", out JsonElement element))
        {
            return null;
        }

        if (element.ValueKind == JsonValueKind.Number && element.TryGetDouble(out double ms) && double.IsFinite(ms) && ms > 0.0)
        {
            if (ms > MaxTimeStepMs)
            {
                diagnostics.Add(ScenarioDiagnostics.Error(
                    ScenarioDiagnostics.BadValue,
                    "$.timeStepMs",
                    string.Create(CultureInfo.InvariantCulture, $"\"timeStepMs\" is {ms} ms, which is longer than a day."),
                    "Use the simulation step in milliseconds, such as 10."));
                return null;
            }

            TimeSpan step = TimeSpan.FromMilliseconds(ms);
            if (step.Ticks > 0)
            {
                return step;
            }

            diagnostics.Add(ScenarioDiagnostics.Error(
                ScenarioDiagnostics.BadValue,
                "$.timeStepMs",
                "\"timeStepMs\" must be at least one tick (0.0001 ms).",
                "Use the simulation step in milliseconds, such as 10."));
            return null;
        }

        diagnostics.Add(ScenarioDiagnostics.Error(
            ScenarioDiagnostics.BadValue,
            "$.timeStepMs",
            "\"timeStepMs\" must be a number greater than zero.",
            "Use the simulation step in milliseconds, such as 10."));
        return null;
    }

    private static TimeSpan? ReadDuration(JsonElement root, TimeSpan? step, List<ConfigDiagnostic> diagnostics)
    {
        if (!root.TryGetProperty("duration", out JsonElement element))
        {
            diagnostics.Add(ScenarioDiagnostics.Error(
                ScenarioDiagnostics.BadValue,
                "$.duration",
                "A scenario needs a \"duration\": how many seconds to run.",
                "Add \"duration\": 120."));
            return null;
        }

        if (!TryReadSeconds(element, out TimeSpan duration) || duration <= TimeSpan.Zero)
        {
            diagnostics.Add(ScenarioDiagnostics.Error(
                ScenarioDiagnostics.BadValue,
                "$.duration",
                "\"duration\" must be a number of seconds greater than zero and at most 1000000000.",
                "Use the number of seconds to run, such as 120."));
            return null;
        }

        if (step is { } declared && duration.Ticks % declared.Ticks != 0L)
        {
            diagnostics.Add(ScenarioDiagnostics.OffTick("$.duration", "The duration", duration, declared));
        }

        return duration;
    }

    /// <summary>Seconds to exact ticks (R52). False for anything that is not a finite number in [0, <see cref="MaxSeconds"/>].</summary>
    private static bool TryReadSeconds(JsonElement element, out TimeSpan value)
    {
        value = TimeSpan.Zero;
        if (element.ValueKind != JsonValueKind.Number
            || !element.TryGetDouble(out double seconds)
            || !double.IsFinite(seconds)
            || seconds < 0.0
            || seconds > MaxSeconds)
        {
            return false;
        }

        value = TimeSpan.FromTicks((long)Math.Round(seconds * TimeSpan.TicksPerSecond, MidpointRounding.AwayFromZero));
        return true;
    }

    private static List<ScenarioAction> ReadTimeline(JsonElement root, TimeSpan? duration, TimeSpan? step, List<ConfigDiagnostic> diagnostics)
    {
        var timeline = new List<ScenarioAction>();
        if (!root.TryGetProperty("timeline", out JsonElement array))
        {
            return timeline;
        }

        if (array.ValueKind != JsonValueKind.Array)
        {
            diagnostics.Add(ScenarioDiagnostics.Error(
                ScenarioDiagnostics.BadValue,
                "$.timeline",
                "\"timeline\" must be an array of actions.",
                "Write \"timeline\": [ { \"at\": 5, \"write\": \"CV001.Start\", \"value\": true } ], or remove the key."));
            return timeline;
        }

        int index = 0;
        foreach (JsonElement element in array.EnumerateArray())
        {
            string path = string.Create(CultureInfo.InvariantCulture, $"$.timeline[{index}]");
            if (ReadAction(element, path, duration, step, diagnostics) is { } action)
            {
                timeline.Add(action);
            }

            index++;
        }

        return timeline;
    }

    private static ScenarioAction? ReadAction(
        JsonElement element, string path, TimeSpan? duration, TimeSpan? step, List<ConfigDiagnostic> diagnostics)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            diagnostics.Add(ScenarioDiagnostics.Error(
                ScenarioDiagnostics.BadAction,
                path,
                "A timeline entry is an object.",
                "Write { \"at\": 5, \"write\": \"CV001.Start\", \"value\": true }."));
            return null;
        }

        CheckKeys(element, path, ActionKeys, "an action", diagnostics);
        TimeSpan? at = ReadAt(element, path, duration, step, diagnostics);

        bool hasWrite = element.TryGetProperty("write", out JsonElement write);
        bool hasFault = element.TryGetProperty("fault", out JsonElement fault);
        bool hasClear = element.TryGetProperty("clear", out JsonElement clear);
        int shapes = (hasWrite ? 1 : 0) + (hasFault ? 1 : 0) + (hasClear ? 1 : 0);
        if (shapes != 1)
        {
            diagnostics.Add(ScenarioDiagnostics.Error(
                ScenarioDiagnostics.BadAction,
                path,
                shapes == 0
                    ? "An action needs exactly one of \"write\", \"fault\" or \"clear\"; this one has none."
                    : string.Create(CultureInfo.InvariantCulture, $"An action needs exactly one of \"write\", \"fault\" or \"clear\"; this one has {shapes}."),
                shapes == 0
                    ? "Add \"write\": \"<tag>\" with a \"value\", or \"fault\" or \"clear\": \"<component>\" with an \"id\"."
                    : "Keep one of them and give each of the others an action of its own."));
            return null;
        }

        if (hasWrite)
        {
            return ReadWrite(element, path, at, write, diagnostics);
        }

        return hasFault
            ? ReadFault(element, path, at, fault, diagnostics)
            : ReadClear(element, path, at, clear, diagnostics);
    }

    private static TimeSpan? ReadAt(
        JsonElement element, string path, TimeSpan? duration, TimeSpan? step, List<ConfigDiagnostic> diagnostics)
    {
        if (!element.TryGetProperty("at", out JsonElement value))
        {
            diagnostics.Add(ScenarioDiagnostics.Error(
                ScenarioDiagnostics.BadValue,
                $"{path}.at",
                "An action needs an \"at\": its time in seconds from the start of the run.",
                "Add \"at\": 5."));
            return null;
        }

        if (!TryReadSeconds(value, out TimeSpan at))
        {
            diagnostics.Add(ScenarioDiagnostics.Error(
                ScenarioDiagnostics.BadValue,
                $"{path}.at",
                "\"at\" must be a number of seconds, zero or greater and at most 1000000000.",
                "Give the time from the start of the run in seconds, such as 5."));
            return null;
        }

        if (step is { } declared && at.Ticks % declared.Ticks != 0L)
        {
            diagnostics.Add(ScenarioDiagnostics.OffTick($"{path}.at", "The action time", at, declared));
        }

        if (duration is { } end && at >= end)
        {
            diagnostics.Add(ScenarioDiagnostics.NotBeforeTheEnd($"{path}.at", at, end));
        }

        return at;
    }

    private static ScenarioAction? ReadWrite(
        JsonElement element, string path, TimeSpan? at, JsonElement tag, List<ConfigDiagnostic> diagnostics)
    {
        string? name = ReadName(tag, $"{path}.write", "write", "a tag", "\"CV001.Start\"", diagnostics);
        Reject(element, path, "id", "\"id\" belongs to a fault or a clear, not to a write.", diagnostics);
        Reject(element, path, "args", "\"args\" belongs to a fault, not to a write.", diagnostics);

        if (!element.TryGetProperty("value", out JsonElement value))
        {
            diagnostics.Add(ScenarioDiagnostics.Error(
                ScenarioDiagnostics.BadAction,
                path,
                "A write action needs a \"value\".",
                "Add \"value\": true for a Bool tag, or a number for a Double or an Int64 tag."));
            return null;
        }

        ScenarioValue? parsed = ReadValue(value);
        if (parsed is null)
        {
            diagnostics.Add(ScenarioDiagnostics.Error(
                ScenarioDiagnostics.BadValue,
                $"{path}.value",
                "\"value\" must be true, false or a number.",
                "Write true or false for a Bool tag, a number for a Double tag, or a whole number for an Int64 tag."));
            return null;
        }

        return name is null || at is null ? null : new WriteAction(at.Value, name, parsed);
    }

    private static ScenarioAction? ReadFault(
        JsonElement element, string path, TimeSpan? at, JsonElement component, List<ConfigDiagnostic> diagnostics)
    {
        string? target = ReadName(component, $"{path}.fault", "fault", "a component", "\"CV001.Motor\"", diagnostics);
        string? faultId = ReadFaultId(element, path, "A fault action needs an \"id\" naming the fault.", diagnostics);
        List<FaultArgument>? arguments = ReadArguments(element, path, diagnostics);

        return target is null || faultId is null || arguments is null || at is null
            ? null
            : new FaultAction(at.Value, target, faultId, arguments);
    }

    private static ScenarioAction? ReadClear(
        JsonElement element, string path, TimeSpan? at, JsonElement component, List<ConfigDiagnostic> diagnostics)
    {
        string? target = ReadName(component, $"{path}.clear", "clear", "a component", "\"CV001.Motor\"", diagnostics);
        string? faultId = ReadFaultId(element, path, "A clear action needs an \"id\" naming the fault.", diagnostics);
        if (element.TryGetProperty("args", out _))
        {
            diagnostics.Add(ScenarioDiagnostics.Error(
                ScenarioDiagnostics.BadAction,
                $"{path}.args",
                "A clear action takes no \"args\".",
                "Remove \"args\"; clearing a fault takes no arguments."));
            return null;
        }

        return target is null || faultId is null || at is null ? null : new ClearAction(at.Value, target, faultId);
    }

    private static string? ReadFaultId(JsonElement element, string path, string missing, List<ConfigDiagnostic> diagnostics)
    {
        if (!element.TryGetProperty("id", out JsonElement id))
        {
            diagnostics.Add(ScenarioDiagnostics.Error(
                ScenarioDiagnostics.BadAction,
                path,
                missing,
                "Add \"id\": \"thermal-bias\"; `dse catalog export` lists each component's fault ids."));
            return null;
        }

        return ReadName(id, $"{path}.id", "id", "a fault", "\"thermal-bias\"", diagnostics);
    }

    private static List<FaultArgument>? ReadArguments(JsonElement element, string path, List<ConfigDiagnostic> diagnostics)
    {
        var arguments = new List<FaultArgument>();
        if (!element.TryGetProperty("args", out JsonElement args))
        {
            return arguments;
        }

        if (args.ValueKind != JsonValueKind.Object)
        {
            diagnostics.Add(ScenarioDiagnostics.Error(
                ScenarioDiagnostics.BadValue,
                $"{path}.args",
                "\"args\" must be an object of numbers.",
                "Write \"args\": { \"amount\": 0.8 }, or remove the key."));
            return null;
        }

        bool sound = true;
        foreach (JsonProperty property in args.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.Number
                && property.Value.TryGetDouble(out double number)
                && double.IsFinite(number))
            {
                arguments.Add(new FaultArgument(property.Name, number));
                continue;
            }

            sound = false;
            diagnostics.Add(ScenarioDiagnostics.Error(
                ScenarioDiagnostics.BadValue,
                $"{path}.args.{property.Name}",
                $"Fault argument '{property.Name}' must be a number.",
                "Give a number, such as 0.8; every fault argument is numeric."));
        }

        return sound ? arguments : null;
    }

    private static string? ReadName(
        JsonElement element, string path, string key, string what, string example, List<ConfigDiagnostic> diagnostics)
    {
        if (element.ValueKind == JsonValueKind.String && element.GetString() is { Length: > 0 } name)
        {
            return name;
        }

        diagnostics.Add(ScenarioDiagnostics.Error(
            ScenarioDiagnostics.BadValue,
            path,
            $"\"{key}\" must be the name of {what}.",
            $"Give a name such as {example}."));
        return null;
    }

    private static void Reject(JsonElement element, string path, string key, string message, List<ConfigDiagnostic> diagnostics)
    {
        if (element.TryGetProperty(key, out _))
        {
            diagnostics.Add(ScenarioDiagnostics.Error(
                ScenarioDiagnostics.BadAction,
                $"{path}.{key}",
                message,
                "Remove the key, or make this an action of the kind it belongs to."));
        }
    }

    /// <summary>A JSON number is an Int64 value only when it is written as an integer literal (R56).</summary>
    private static ScenarioValue? ReadValue(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.True => ScenarioValue.OfBool(true),
        JsonValueKind.False => ScenarioValue.OfBool(false),
        JsonValueKind.Number when element.TryGetInt64(out long whole) => ScenarioValue.OfInteger(whole),
        JsonValueKind.Number when element.TryGetDouble(out double number) && double.IsFinite(number) => ScenarioValue.OfNumber(number),
        _ => null,
    };
}
