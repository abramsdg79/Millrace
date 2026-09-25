using System.Globalization;
using System.Text.Json;
using Dse.Core.Catalogue;
using Dse.Core.Flow;

namespace Dse.Configuration.Loading;

/// <summary>Stage 2: everything that can be checked without building anything.</summary>
internal static class StructureStage
{
    /// <summary>A time step or a scan period longer than a day is a mistake, and 1e30 ms overflows <see cref="TimeSpan"/>.</summary>
    private const double MaxTimeStepMs = 86_400_000.0;

    /// <summary>Round-trip ISO 8601, offset given as a literal <c>Z</c> (parsed as UTC).</summary>
    private static readonly string[] StartTimeUtcFormats =
    [
        "yyyy'-'MM'-'dd'T'HH':'mm':'ss'.'FFFFFFF'Z'",
        "yyyy'-'MM'-'dd'T'HH':'mm':'ss'Z'",
    ];

    /// <summary>Round-trip ISO 8601, offset given as <c>±hh:mm</c>. Unlike the <c>K</c> specifier, <c>zzz</c> is not optional.</summary>
    private static readonly string[] StartTimeOffsetFormats =
    [
        "yyyy'-'MM'-'dd'T'HH':'mm':'ss'.'FFFFFFFzzz",
        "yyyy'-'MM'-'dd'T'HH':'mm':'sszzz",
    ];

    public static void Run(LoadState state)
    {
        JsonElement root = state.Root;
        if (root.ValueKind != JsonValueKind.Object)
        {
            state.Error(ConfigDiagnostics.BadParameter, "$", "A plant file is a JSON object.", "Wrap the content in { … } with a \"components\" array.");
            return;
        }

        CheckKeys(state, root, "$", PlantSchemas.TopLevelKeys, "keys");
        if (root.TryGetProperty("$schema", out JsonElement schema) && schema.ValueKind != JsonValueKind.String)
        {
            state.Error(ConfigDiagnostics.BadParameter, "$.$schema", "\"$schema\" must be a string.", "Give the schema's path or URL as a string, or remove the key.");
        }

        ReadDefaults(state, root);
        ReadMaterials(state, root);      // before components: their parameters name materials
        HashSet<string> componentIds = ReadComponents(state, root);
        ReadLinks(state, root, "signals", state.Signals);
        ReadLinks(state, root, "flows", state.Flows);
        ReadTags(state, root);
        ReadControllers(state, root, componentIds);
    }

    private static void CheckKeys(LoadState state, JsonElement element, string path, string[] allowed, string noun)
    {
        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (Array.IndexOf(allowed, property.Name) < 0)
            {
                state.Error(
                    ConfigDiagnostics.UnknownKey,
                    $"{path}.{property.Name}",
                    $"'{property.Name}' is not a key the loader knows here.",
                    Suggest.Fix(property.Name, allowed.Where(k => k[0] != '$'), noun));
            }
        }
    }

    private static void ReadDefaults(LoadState state, JsonElement root)
    {
        if (!root.TryGetProperty("defaults", out JsonElement defaults))
        {
            return;
        }

        if (defaults.ValueKind != JsonValueKind.Object)
        {
            state.Error(ConfigDiagnostics.BadParameter, "$.defaults", "\"defaults\" must be an object.", "Write { \"seed\": …, \"timeStepMs\": …, \"startTime\": … }; every key is optional.");
            return;
        }

        CheckKeys(state, defaults, "$.defaults", PlantSchemas.DefaultsKeys, "keys");

        ulong? seed = null;
        if (defaults.TryGetProperty("seed", out JsonElement seedElement))
        {
            if (seedElement.ValueKind == JsonValueKind.Number && seedElement.TryGetUInt64(out ulong value))
            {
                seed = value;
            }
            else
            {
                state.Error(ConfigDiagnostics.BadParameter, "$.defaults.seed", "\"seed\" must be a whole number, zero or greater.", "Use a non-negative integer such as 1.");
            }
        }

        TimeSpan? step = null;
        if (defaults.TryGetProperty("timeStepMs", out JsonElement stepElement))
        {
            if (stepElement.ValueKind == JsonValueKind.Number && stepElement.TryGetDouble(out double ms) && double.IsFinite(ms) && ms > 0.0)
            {
                if (ms > MaxTimeStepMs)
                {
                    state.Error(
                        ConfigDiagnostics.BadParameter,
                        "$.defaults.timeStepMs",
                        string.Create(CultureInfo.InvariantCulture, $"\"timeStepMs\" is {ms} ms, which is longer than a day."),
                        "Use the simulation step in milliseconds, such as 10.");
                }
                else
                {
                    TimeSpan candidate = TimeSpan.FromMilliseconds(ms);
                    if (candidate.Ticks > 0)
                    {
                        step = candidate;
                    }
                    else
                    {
                        state.Error(
                            ConfigDiagnostics.BadParameter,
                            "$.defaults.timeStepMs",
                            "\"timeStepMs\" must be at least one tick (0.0001 ms).",
                            "Use the simulation step in milliseconds, such as 10.");
                    }
                }
            }
            else
            {
                state.Error(ConfigDiagnostics.BadParameter, "$.defaults.timeStepMs", "\"timeStepMs\" must be a number greater than zero.", "Use the simulation step in milliseconds, such as 10.");
            }
        }

        DateTimeOffset? start = null;
        if (defaults.TryGetProperty("startTime", out JsonElement startElement))
        {
            if (startElement.ValueKind == JsonValueKind.String && TryParseStartTime(startElement.GetString(), out DateTimeOffset parsed))
            {
                start = parsed;
            }
            else
            {
                state.Error(ConfigDiagnostics.BadParameter, "$.defaults.startTime", "\"startTime\" must be an ISO 8601 date and time.", "Write it like \"2026-01-01T06:00:00Z\".");
            }
        }

        state.Defaults = new PlantDefaults(seed, step, start);
    }

    /// <summary>An ISO 8601 date-time WITH an offset: <c>Z</c> parses as UTC; <c>±hh:mm</c> carries its own offset.
    /// An offset-less string (which would otherwise adopt the host's time zone) is rejected.</summary>
    private static bool TryParseStartTime(string? text, out DateTimeOffset parsed) =>
        DateTimeOffset.TryParseExact(text, StartTimeUtcFormats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out parsed)
        || DateTimeOffset.TryParseExact(text, StartTimeOffsetFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed);

    private static void ReadMaterials(LoadState state, JsonElement root)
    {
        ForEachBound(state, root, "materials", PlantSchemas.Material, required: false, (values, path) =>
        {
            string name = values.String("name");
            MaterialType type;
            try
            {
                PayloadKind kind = values.String("kind") == "bulk" ? PayloadKind.Bulk : PayloadKind.Discrete;
                type = new MaterialType(name, kind, values.Strings("states").ToArray());
            }
            catch (ArgumentException ex)
            {
                state.Error(ConfigDiagnostics.BadParameter, $"{path}.states", ex.Message, "Give each state a distinct, non-empty name.");
                return;
            }

            ParameterValues properties = values.Group("properties");
            var descriptor = new MaterialDescriptor(
                type,
                new MaterialProperties(properties.Double("density"), properties.Double("moisture"), properties.Double("temperature")),
                values.String("description"));
            if (state.Context.TryGetMaterial(name, out _))
            {
                state.Error(
                    ConfigDiagnostics.Duplicate,
                    $"{path}.name",
                    $"Material '{name}' is already defined, by the catalogue or earlier in this file.",
                    "Rename this material, or remove it and use the existing one.");
                return;
            }

            state.Context.AddMaterial(descriptor);
        });
    }

    private static HashSet<string> ReadComponents(LoadState state, JsonElement root)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        if (!root.TryGetProperty("components", out JsonElement components) || components.ValueKind != JsonValueKind.Array)
        {
            state.Error(ConfigDiagnostics.BadParameter, "$.components", "A plant needs a \"components\" array.", "Add \"components\": [ { \"id\": …, \"type\": … } ].");
            return seen;
        }

        int index = 0;
        foreach (JsonElement element in components.EnumerateArray())
        {
            ReadComponent(state, element, index, seen);
            index++;
        }

        return seen;
    }

    private static void ReadComponent(LoadState state, JsonElement element, int index, HashSet<string> seen)
    {
        string path = string.Create(CultureInfo.InvariantCulture, $"$.components[{index}]");
        if (element.ValueKind != JsonValueKind.Object)
        {
            state.Error(ConfigDiagnostics.BadParameter, path, "A component is an object.", "Write { \"id\": …, \"type\": …, \"parameters\": { … } }.");
            return;
        }

        CheckKeys(state, element, path, PlantSchemas.ComponentKeys, "keys");

        string? id = ReadId(state, element, path, seen);
        ComponentDescriptor? descriptor = ReadType(state, element, path);

        JsonElement parameters = default;
        if (element.TryGetProperty("parameters", out JsonElement given))
        {
            parameters = given;
        }

        if (descriptor is null)
        {
            return;
        }

        var issues = new List<BindingIssue>();
        ParameterBinder.Bind(descriptor.Parameters, parameters, $"{path}.parameters", state.Context, construct: false, issues);
        state.AddIssues(issues);

        if (id is not null)
        {
            state.Components.Add(new ComponentEntry(index, id, descriptor, parameters));
        }
    }

    private static string? ReadId(LoadState state, JsonElement element, string path, HashSet<string> seen)
    {
        if (!element.TryGetProperty("id", out JsonElement idElement) || idElement.ValueKind != JsonValueKind.String)
        {
            state.Error(ConfigDiagnostics.BadParameter, $"{path}.id", "A component needs a string \"id\".", "Add \"id\": \"…\" with a name unique in this plant.");
            return null;
        }

        string id = idElement.GetString()!;
        if (id.Length == 0 || id.Contains('.', StringComparison.Ordinal) || id.Any(char.IsWhiteSpace))
        {
            state.Error(
                ConfigDiagnostics.BadParameter,
                $"{path}.id",
                $"'{id}' cannot be a component id: an id is non-empty and has no dot and no whitespace.",
                "Dots separate a component from its port in an address; use a hyphen or an underscore instead.");
            return null;
        }

        if (!seen.Add(id))
        {
            state.Error(ConfigDiagnostics.Duplicate, $"{path}.id", $"Another component is already called '{id}'.", "Give this component an id of its own.");
            return null;
        }

        return id;
    }

    private static ComponentDescriptor? ReadType(LoadState state, JsonElement element, string path)
    {
        if (!element.TryGetProperty("type", out JsonElement typeElement) || typeElement.ValueKind != JsonValueKind.String)
        {
            state.Error(ConfigDiagnostics.BadParameter, $"{path}.type", "A component needs a string \"type\".", "Add \"type\": \"…\" naming a catalogue type; `dse catalog export` lists them.");
            return null;
        }

        string type = typeElement.GetString()!;
        if (state.Catalogue.TryGetComponent(type, out ComponentDescriptor? descriptor))
        {
            return descriptor;
        }

        string? closest = Suggest.Closest(type, state.Catalogue.Components.Select(c => c.Type));
        state.Error(
            ConfigDiagnostics.UnknownType,
            $"{path}.type",
            $"'{type}' is not a component type in this catalogue.",
            closest is null
                ? "Run `dse catalog export` to list the types. A custom type needs its assembly loaded with --assembly."
                : $"Use a type that `dse catalog export` lists — '{closest}' is closest. A custom type needs its assembly loaded with --assembly.");
        return null;
    }

    private static void ReadControllers(LoadState state, JsonElement root, HashSet<string> componentIds)
    {
        if (!root.TryGetProperty("controllers", out JsonElement controllers))
        {
            return;
        }

        if (controllers.ValueKind != JsonValueKind.Array)
        {
            state.Error(
                ConfigDiagnostics.BadParameter,
                "$.controllers",
                "\"controllers\" must be an array.",
                "Write \"controllers\": [ { \"id\": …, \"type\": …, \"scanPeriodMs\": … } ].");
            return;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        int index = 0;
        foreach (JsonElement element in controllers.EnumerateArray())
        {
            ReadController(state, element, index, seen, componentIds);
            index++;
        }
    }

    private static void ReadController(LoadState state, JsonElement element, int index, HashSet<string> seen, HashSet<string> componentIds)
    {
        string path = string.Create(CultureInfo.InvariantCulture, $"$.controllers[{index}]");
        if (element.ValueKind != JsonValueKind.Object)
        {
            state.Error(ConfigDiagnostics.BadParameter, path, "A controller is an object.", "Write { \"id\": …, \"type\": …, \"scanPeriodMs\": …, \"parameters\": { … } }.");
            return;
        }

        CheckKeys(state, element, path, PlantSchemas.ControllerKeys, "keys");

        string? id = ReadControllerId(state, element, path, seen, componentIds);
        BlockDescriptor? descriptor = ReadBlockType(state, element, path);
        TimeSpan? period = ReadScanPeriod(state, element, path);

        JsonElement parameters = default;
        if (element.TryGetProperty("parameters", out JsonElement given))
        {
            parameters = given;
        }

        if (descriptor is null)
        {
            return;
        }

        var issues = new List<BindingIssue>();
        ParameterValues? values = ParameterBinder.Bind(descriptor.Parameters, parameters, $"{path}.parameters", state.Context, construct: false, issues);
        state.AddIssues(issues);

        if (id is not null && period is { } scanPeriod && values is not null)
        {
            state.Controllers.Add(new ControllerEntry(index, id, descriptor, scanPeriod, parameters, values));
        }
    }

    /// <summary>R87: the component id rule, because a block id prefixes every tag the block owns.</summary>
    private static string? ReadControllerId(LoadState state, JsonElement element, string path, HashSet<string> seen, HashSet<string> componentIds)
    {
        if (!element.TryGetProperty("id", out JsonElement idElement) || idElement.ValueKind != JsonValueKind.String)
        {
            state.Error(ConfigDiagnostics.BadParameter, $"{path}.id", "A controller needs a string \"id\".", "Add \"id\": \"…\" with a name unique among the plant's components and controllers.");
            return null;
        }

        string id = idElement.GetString()!;
        if (id.Length == 0 || id.Contains('.', StringComparison.Ordinal) || id.Any(char.IsWhiteSpace))
        {
            state.Error(
                ConfigDiagnostics.BadParameter,
                $"{path}.id",
                $"'{id}' cannot be a controller id: an id is non-empty and has no dot and no whitespace.",
                "Use a hyphen or an underscore instead. The id prefixes every tag the block owns, as in INT01.Ok.");
            return null;
        }

        if (componentIds.Contains(id))
        {
            state.Error(ConfigDiagnostics.Duplicate, $"{path}.id", $"A component is already called '{id}'.", "Give this controller an id of its own; components and controllers share one set of ids.");
            return null;
        }

        if (!seen.Add(id))
        {
            state.Error(ConfigDiagnostics.Duplicate, $"{path}.id", $"Another controller is already called '{id}'.", "Give this controller an id of its own.");
            return null;
        }

        return id;
    }

    private static BlockDescriptor? ReadBlockType(LoadState state, JsonElement element, string path)
    {
        if (!element.TryGetProperty("type", out JsonElement typeElement) || typeElement.ValueKind != JsonValueKind.String)
        {
            state.Error(ConfigDiagnostics.BadParameter, $"{path}.type", "A controller needs a string \"type\".", "Add \"type\": \"…\" naming a block type; `dse catalog export` lists them under \"blocks\".");
            return null;
        }

        string type = typeElement.GetString()!;
        if (state.Catalogue.TryGetBlock(type, out BlockDescriptor? descriptor))
        {
            return descriptor;
        }

        string? closest = Suggest.Closest(type, state.Catalogue.Blocks.Select(b => b.Type));
        state.Error(
            ConfigDiagnostics.UnknownType,
            $"{path}.type",
            $"'{type}' is not a block type in this catalogue.",
            closest is null
                ? "Run `dse catalog export` to list the block types. A custom type needs its assembly loaded with --assembly."
                : $"Use a block type that `dse catalog export` lists — '{closest}' is closest. A custom type needs its assembly loaded with --assembly.");
        return null;
    }

    /// <summary>R86: positive, at most a day (checked before TimeSpan conversion), and at least one tick.</summary>
    private static TimeSpan? ReadScanPeriod(LoadState state, JsonElement element, string path)
    {
        const string Fix = "Give the scan period in milliseconds, a whole number of time steps, such as 100.";
        string at = $"{path}.scanPeriodMs";
        if (!element.TryGetProperty("scanPeriodMs", out JsonElement periodElement))
        {
            state.Error(ConfigDiagnostics.BadParameter, at, "A controller needs \"scanPeriodMs\"; there is no default.", Fix);
            return null;
        }

        if (periodElement.ValueKind != JsonValueKind.Number || !periodElement.TryGetDouble(out double ms) || !double.IsFinite(ms) || ms <= 0.0)
        {
            state.Error(ConfigDiagnostics.BadParameter, at, "\"scanPeriodMs\" must be a number greater than zero.", Fix);
            return null;
        }

        if (ms > MaxTimeStepMs)
        {
            state.Error(
                ConfigDiagnostics.BadParameter,
                at,
                string.Create(CultureInfo.InvariantCulture, $"\"scanPeriodMs\" is {ms} ms, which is longer than a day."),
                Fix);
            return null;
        }

        TimeSpan period = TimeSpan.FromMilliseconds(ms);
        if (period.Ticks <= 0L)
        {
            state.Error(ConfigDiagnostics.BadParameter, at, "\"scanPeriodMs\" must be at least one tick (0.0001 ms).", Fix);
            return null;
        }

        return period;
    }

    private static void ReadLinks(LoadState state, JsonElement root, string key, List<LinkEntry> into) =>
        ForEachBound(state, root, key, PlantSchemas.Link, required: false, (values, path) =>
            into.Add(new LinkEntry(values.String("from"), values.String("to"), path)));

    private static void ReadTags(LoadState state, JsonElement root) =>
        ForEachBound(state, root, "tags", PlantSchemas.Tag, required: false, (values, path) =>
        {
            bool hasLow = values.Has("rangeLow");
            bool hasHigh = values.Has("rangeHigh");
            if (hasLow != hasHigh || (hasLow && values.Double("rangeHigh") <= values.Double("rangeLow")))
            {
                state.Error(
                    ConfigDiagnostics.BadParameter,
                    $"{path}.{(hasLow ? "rangeHigh" : "rangeLow")}",
                    "A tag's range needs both ends, with rangeHigh above rangeLow.",
                    "Give both \"rangeLow\" and \"rangeHigh\", or neither.");
                return;
            }

            state.Tags.Add(new TagRequest(
                values.String("name"),
                values.String("port"),
                values.String("access") == "write",
                values.String("unit"),
                values.DoubleOr("rangeLow", double.NaN),
                values.DoubleOr("rangeHigh", double.NaN),
                values.String("description"),
                path));
        });

    private static void ForEachBound(
        LoadState state, JsonElement root, string key, IReadOnlyList<ParameterDescriptor> schema, bool required, Action<ParameterValues, string> use)
    {
        if (!root.TryGetProperty(key, out JsonElement array))
        {
            if (required)
            {
                state.Error(ConfigDiagnostics.BadParameter, $"$.{key}", $"A plant needs a \"{key}\" array.", $"Add \"{key}\": [ … ].");
            }

            return;
        }

        if (array.ValueKind != JsonValueKind.Array)
        {
            state.Error(ConfigDiagnostics.BadParameter, $"$.{key}", $"\"{key}\" must be an array.", $"Write \"{key}\": [ … ].");
            return;
        }

        int index = 0;
        foreach (JsonElement element in array.EnumerateArray())
        {
            string path = string.Create(CultureInfo.InvariantCulture, $"$.{key}[{index}]");
            var issues = new List<BindingIssue>();
            ParameterValues? values = ParameterBinder.Bind(schema, element, path, state.Context, construct: true, issues);
            state.AddIssues(issues);
            if (values is not null)
            {
                use(values, path);
            }

            index++;
        }
    }
}
