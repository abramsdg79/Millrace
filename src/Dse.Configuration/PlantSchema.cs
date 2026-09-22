using System.Text.Json;
using Dse.Configuration.Loading;
using Dse.Core.Catalogue;

namespace Dse.Configuration;

/// <summary>
/// A JSON Schema (draft 2020-12) for plant files, generated from a catalogue.
/// Same catalogue, same bytes. It checks structure; the loader checks meaning.
/// </summary>
public static class PlantSchema
{
    /// <summary>The JSON Schema draft the generated document declares itself against.</summary>
    public const string Dialect = "https://json-schema.org/draft/2020-12/schema";

    private const string LoaderChecks = " The schema cannot check that it exists; the loader does.";

    /// <summary>Generates the plant schema document for <paramref name="catalogue"/>.</summary>
    public static string Generate(ComponentCatalogue catalogue)
    {
        ArgumentNullException.ThrowIfNull(catalogue);

        // Every definition is collected first so that "$defs" can be written sorted.
        var defs = new SortedDictionary<string, Action<Utf8JsonWriter>>(StringComparer.Ordinal);
        foreach (ComponentDescriptor component in catalogue.Components)
        {
            defs[$"component.{component.Type}"] = w => WriteComponent(w, component);
            CollectGroups(component.Parameters, defs);
        }

        foreach (string slot in catalogue.Slots)
        {
            IReadOnlyList<ObjectDescriptor> types = catalogue.ObjectsIn(slot);
            defs[$"object.{slot}"] = w => WriteOneOf(w, types.Select(t => $"object.{slot}.{t.Type}"));
            foreach (ObjectDescriptor type in types)
            {
                defs[$"object.{slot}.{type.Type}"] = w => WriteParameterObject(w, type.Parameters, type.Description, typeConst: type.Type);
                CollectGroups(type.Parameters, defs);
            }
        }

        defs["envelope.material"] = w => WriteParameterObject(w, PlantSchemas.Material, "A material the plant's components can name.", typeConst: null);
        defs["envelope.link"] = w => WriteParameterObject(w, PlantSchemas.Link, "A connection from one port to another.", typeConst: null);
        defs["envelope.tag"] = w => WriteParameterObject(w, PlantSchemas.Tag, "A tag bound to a port, in addition to the tags components declare.", typeConst: null);
        CollectGroups(PlantSchemas.Material, defs);
        CollectGroups(PlantSchemas.Link, defs);
        CollectGroups(PlantSchemas.Tag, defs);

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, CatalogueJson.WriterOptions))
        {
            writer.WriteStartObject();
            writer.WriteString("$schema", Dialect);
            writer.WriteString("title", "DSE plant");
            writer.WriteString("description", $"Generated from the catalogue of modules: {string.Join(", ", catalogue.Modules)}. Do not edit; run `dse schema export`.");
            writer.WriteString("type", "object");
            writer.WriteBoolean("additionalProperties", false);
            writer.WriteStartArray("required");
            writer.WriteStringValue("components");
            writer.WriteEndArray();

            writer.WriteStartObject("properties");
            writer.WriteStartObject("$schema");
            writer.WriteString("type", "string");
            writer.WriteEndObject();
            WriteDefaults(writer);
            WriteArrayOf(writer, "materials", "envelope.material");
            writer.WriteStartObject("components");
            writer.WriteString("type", "array");
            writer.WritePropertyName("items");
            WriteOneOf(writer, catalogue.Components.Select(c => $"component.{c.Type}"));
            writer.WriteEndObject();
            WriteArrayOf(writer, "signals", "envelope.link");
            WriteArrayOf(writer, "flows", "envelope.link");
            WriteArrayOf(writer, "tags", "envelope.tag");
            writer.WriteEndObject();

            writer.WriteStartObject("$defs");
            foreach (KeyValuePair<string, Action<Utf8JsonWriter>> def in defs)
            {
                writer.WritePropertyName(def.Key);
                def.Value(writer);
            }

            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return CatalogueJson.Finish(stream);
    }

    private static void CollectGroups(IReadOnlyList<ParameterDescriptor> parameters, SortedDictionary<string, Action<Utf8JsonWriter>> defs)
    {
        foreach (ParameterDescriptor parameter in parameters)
        {
            if (parameter.GroupName.Length == 0)
            {
                continue;
            }

            string key = $"group.{parameter.GroupName}";
            if (!defs.ContainsKey(key))
            {
                IReadOnlyList<ParameterDescriptor> children = parameter.Children;
                defs[key] = w => WriteParameterObject(w, children, description: null, typeConst: null);
                CollectGroups(children, defs);
            }
        }
    }

    private static void WriteDefaults(Utf8JsonWriter writer)
    {
        writer.WriteStartObject("defaults");
        writer.WriteString("description", "Simulation options used when nothing overrides them. A scenario or the command line may.");
        writer.WriteString("type", "object");
        writer.WriteBoolean("additionalProperties", false);
        writer.WriteStartObject("properties");

        writer.WriteStartObject("seed");
        writer.WriteString("description", "Master seed; every random stream derives from it.");
        writer.WriteString("type", "integer");
        writer.WriteNumber("minimum", 0);
        writer.WriteEndObject();

        writer.WriteStartObject("timeStepMs");
        writer.WriteString("description", "Simulation step in milliseconds.");
        writer.WriteString("type", "number");
        writer.WriteNumber("exclusiveMinimum", 0);
        writer.WriteEndObject();

        writer.WriteStartObject("startTime");
        writer.WriteString("description", "Simulation start, ISO 8601 with an offset.");
        writer.WriteString("type", "string");
        writer.WriteString("format", "date-time");
        writer.WriteEndObject();

        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    private static void WriteArrayOf(Utf8JsonWriter writer, string name, string def)
    {
        writer.WriteStartObject(name);
        writer.WriteString("type", "array");
        writer.WriteStartObject("items");
        writer.WriteString("$ref", $"#/$defs/{def}");
        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    private static void WriteOneOf(Utf8JsonWriter writer, IEnumerable<string> defs)
    {
        writer.WriteStartObject();
        writer.WriteStartArray("oneOf");
        foreach (string def in defs)
        {
            writer.WriteStartObject();
            writer.WriteString("$ref", $"#/$defs/{def}");
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteComponent(Utf8JsonWriter writer, ComponentDescriptor component)
    {
        bool parametersRequired = component.Parameters.Any(p => p.IsRequired);
        writer.WriteStartObject();
        writer.WriteString("description", component.Description);
        writer.WriteString("type", "object");
        writer.WriteBoolean("additionalProperties", false);
        writer.WriteStartArray("required");
        writer.WriteStringValue("id");
        writer.WriteStringValue("type");
        if (parametersRequired)
        {
            writer.WriteStringValue("parameters");
        }

        writer.WriteEndArray();
        writer.WriteStartObject("properties");

        writer.WriteStartObject("id");
        writer.WriteString("description", "Unique in the plant. No dot and no whitespace: a dot separates a component from its port in an address.");
        writer.WriteString("type", "string");
        writer.WriteString("pattern", "^[^.\\s]+$");
        writer.WriteEndObject();

        writer.WriteStartObject("type");
        writer.WriteString("const", component.Type);
        writer.WriteEndObject();

        writer.WritePropertyName("parameters");
        WriteParameterObject(writer, component.Parameters, description: null, typeConst: null);

        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    /// <summary>An object with one property per parameter. With <paramref name="typeConst"/>, also a required <c>"type"</c> pinned to it.</summary>
    private static void WriteParameterObject(Utf8JsonWriter writer, IReadOnlyList<ParameterDescriptor> parameters, string? description, string? typeConst)
    {
        writer.WriteStartObject();
        if (description is not null)
        {
            writer.WriteString("description", description);
        }

        writer.WriteString("type", "object");
        writer.WriteBoolean("additionalProperties", false);

        writer.WriteStartArray("required");
        if (typeConst is not null)
        {
            writer.WriteStringValue("type");
        }

        foreach (ParameterDescriptor parameter in parameters.Where(p => p.IsRequired))
        {
            writer.WriteStringValue(parameter.Name);
        }

        writer.WriteEndArray();

        writer.WriteStartObject("properties");
        if (typeConst is not null)
        {
            writer.WriteStartObject("type");
            writer.WriteString("const", typeConst);
            writer.WriteEndObject();
        }

        foreach (ParameterDescriptor parameter in parameters)
        {
            writer.WritePropertyName(parameter.Name);
            WriteParameter(writer, parameter);
        }

        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    private static void WriteParameter(Utf8JsonWriter writer, ParameterDescriptor parameter)
    {
        writer.WriteStartObject();
        switch (parameter.Kind)
        {
            case ParameterKind.Group:
                // A sibling of "$ref" is allowed in 2020-12, so the use-site description survives.
                writer.WriteString("description", parameter.Description);
                writer.WriteString("$ref", $"#/$defs/group.{parameter.GroupName}");
                break;

            case ParameterKind.Object:
                writer.WriteString("description", parameter.Description);
                writer.WriteString("$ref", $"#/$defs/object.{parameter.Slot}");
                break;

            case ParameterKind.GroupList:
                WriteList(writer, parameter, w => w.WriteString("$ref", $"#/$defs/group.{parameter.GroupName}"));
                break;

            case ParameterKind.ObjectList:
                WriteList(writer, parameter, w => w.WriteString("$ref", $"#/$defs/object.{parameter.Slot}"));
                break;

            case ParameterKind.StringList:
                WriteList(writer, parameter, w => w.WriteString("type", "string"));
                break;

            case ParameterKind.Double:
            case ParameterKind.Int:
                writer.WriteString("description", Describe(parameter));
                writer.WriteString("type", parameter.Kind == ParameterKind.Int ? "integer" : "number");
                WriteBounds(writer, parameter);
                break;

            case ParameterKind.Bool:
                writer.WriteString("description", parameter.Description);
                writer.WriteString("type", "boolean");
                break;

            case ParameterKind.String:
                writer.WriteString("description", parameter.Description);
                writer.WriteString("type", "string");
                break;

            case ParameterKind.Enum:
                writer.WriteString("description", parameter.Description);
                writer.WriteStartArray("enum");
                foreach (string value in parameter.AllowedValues)
                {
                    writer.WriteStringValue(value);
                }

                writer.WriteEndArray();
                break;

            case ParameterKind.Reference:
                writer.WriteString("description", $"{parameter.Description} The id of a component that supplies {parameter.Capability!.Name}.{LoaderChecks}");
                writer.WriteString("type", "string");
                break;

            case ParameterKind.Material:
                writer.WriteString(
                    "description",
                    $"{parameter.Description} The name of a {(parameter.Payload is { } kind ? CatalogueJson.Camel(kind.ToString()) + " " : string.Empty)}material.{LoaderChecks}");
                writer.WriteString("type", "string");
                break;

            case ParameterKind.MaterialState:
                writer.WriteString("description", $"{parameter.Description} The name of a state of the material named by '{parameter.MaterialParameter}'.{LoaderChecks}");
                writer.WriteString("type", "string");
                break;

            default:
                throw new InvalidOperationException($"Parameter kind {parameter.Kind} has no schema.");
        }

        switch (parameter.Default)
        {
            case double number:
                writer.WriteNumber("default", number);
                break;
            case long whole:
                writer.WriteNumber("default", whole);
                break;
            case bool flag:
                writer.WriteBoolean("default", flag);
                break;
            case string text:
                writer.WriteString("default", text);
                break;
        }

        writer.WriteEndObject();
    }

    private static void WriteList(Utf8JsonWriter writer, ParameterDescriptor parameter, Action<Utf8JsonWriter> items)
    {
        writer.WriteString("description", parameter.Description);
        writer.WriteString("type", "array");
        if (parameter.MinCount > 0)
        {
            writer.WriteNumber("minItems", parameter.MinCount);
        }

        writer.WriteStartObject("items");
        items(writer);
        writer.WriteEndObject();
    }

    private static void WriteBounds(Utf8JsonWriter writer, ParameterDescriptor parameter)
    {
        if (parameter.Minimum is { } min)
        {
            writer.WriteNumber(parameter.ExclusiveMinimum ? "exclusiveMinimum" : "minimum", min);
        }

        if (parameter.Maximum is { } max)
        {
            writer.WriteNumber(parameter.ExclusiveMaximum ? "exclusiveMaximum" : "maximum", max);
        }
    }

    private static string Describe(ParameterDescriptor parameter) =>
        parameter.Unit.Length == 0 ? parameter.Description : $"{parameter.Description} Unit: {parameter.Unit}.";
}
