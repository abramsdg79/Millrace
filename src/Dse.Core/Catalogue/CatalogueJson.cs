using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Dse.Core.Faults;

namespace Dse.Core.Catalogue;

/// <summary>The catalogue as deterministic JSON: same catalogue, same bytes.</summary>
public static class CatalogueJson
{
    /// <summary>Bumped when a consumer of the export would have to change.</summary>
    public const int FormatVersion = 1;

    /// <summary>Indented, with readable non-ASCII. Every JSON document this project emits uses these.</summary>
    public static JsonWriterOptions WriterOptions { get; } = new()
    {
        Indented = true,
        // Units such as "°C" and "N·m" stay readable instead of becoming \u escapes.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string Export(ComponentCatalogue catalogue)
    {
        ArgumentNullException.ThrowIfNull(catalogue);

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, WriterOptions))
        {
            writer.WriteStartObject();
            writer.WriteNumber("formatVersion", FormatVersion);

            writer.WriteStartArray("modules");
            foreach (string module in catalogue.Modules)
            {
                writer.WriteStringValue(module);
            }

            writer.WriteEndArray();

            writer.WriteStartArray("components");
            foreach (ComponentDescriptor component in catalogue.Components)
            {
                WriteComponent(writer, component, catalogue.ModuleOf(component));
            }

            writer.WriteEndArray();

            writer.WriteStartArray("blocks");
            foreach (BlockDescriptor block in catalogue.Blocks)
            {
                writer.WriteStartObject();
                writer.WriteString("type", block.Type);
                writer.WriteString("module", catalogue.ModuleOf(block));
                writer.WriteString("description", block.Description);
                writer.WritePropertyName("parameters");
                WriteParameters(writer, block.Parameters);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();

            writer.WriteStartArray("objects");
            foreach (ObjectDescriptor descriptor in catalogue.Objects)
            {
                writer.WriteStartObject();
                writer.WriteString("slot", descriptor.Slot);
                writer.WriteString("type", descriptor.Type);
                writer.WriteString("module", catalogue.ModuleOf(descriptor));
                writer.WriteString("description", descriptor.Description);
                writer.WritePropertyName("parameters");
                WriteParameters(writer, descriptor.Parameters);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();

            writer.WriteStartArray("materials");
            foreach (MaterialDescriptor material in catalogue.Materials)
            {
                writer.WriteStartObject();
                writer.WriteString("name", material.Material.Name);
                writer.WriteString("module", catalogue.ModuleOf(material));
                writer.WriteString("kind", Camel(material.Material.Kind.ToString()));
                writer.WriteStartArray("states");
                foreach (string state in material.Material.StateSchema)
                {
                    writer.WriteStringValue(state);
                }

                writer.WriteEndArray();
                writer.WriteStartObject("properties");
                writer.WriteNumber("density", material.Properties.Density);
                writer.WriteNumber("moisture", material.Properties.Moisture);
                writer.WriteNumber("temperature", material.Properties.Temperature);
                writer.WriteEndObject();
                writer.WriteString("description", material.Description);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Finish(stream);
    }

    /// <summary>UTF-8 to text, <c>\n</c> line endings, exactly one trailing newline. Shared with the schema generator.</summary>
    public static string Finish(MemoryStream stream) =>
        Encoding.UTF8.GetString(stream.ToArray()).ReplaceLineEndings("\n").TrimEnd('\n') + "\n";

    /// <summary><c>GroupList</c> → <c>groupList</c>.</summary>
    public static string Camel(string pascal)
    {
        ArgumentException.ThrowIfNullOrEmpty(pascal);
        return char.ToLowerInvariant(pascal[0]) + pascal[1..];
    }

    internal static void WriteParameters(Utf8JsonWriter writer, IReadOnlyList<ParameterDescriptor> parameters)
    {
        writer.WriteStartArray();
        foreach (ParameterDescriptor parameter in parameters)
        {
            writer.WriteStartObject();
            writer.WriteString("name", parameter.Name);
            writer.WriteString("kind", Camel(parameter.Kind.ToString()));
            writer.WriteBoolean("required", parameter.IsRequired);
            writer.WriteString("description", parameter.Description);
            if (parameter.Unit.Length > 0)
            {
                writer.WriteString("unit", parameter.Unit);
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

            if (parameter.IsOptional)
            {
                writer.WriteBoolean("optional", true);
            }

            if (parameter.Minimum is { } minimum)
            {
                writer.WriteNumber("minimum", minimum);
            }

            if (parameter.ExclusiveMinimum)
            {
                writer.WriteBoolean("exclusiveMinimum", true);
            }

            if (parameter.Maximum is { } maximum)
            {
                writer.WriteNumber("maximum", maximum);
            }

            if (parameter.ExclusiveMaximum)
            {
                writer.WriteBoolean("exclusiveMaximum", true);
            }

            if (parameter.AllowedValues.Count > 0)
            {
                writer.WriteStartArray("allowedValues");
                foreach (string value in parameter.AllowedValues)
                {
                    writer.WriteStringValue(value);
                }

                writer.WriteEndArray();
            }

            if (parameter.GroupName.Length > 0)
            {
                writer.WriteString("group", parameter.GroupName);
                writer.WritePropertyName("parameters");
                WriteParameters(writer, parameter.Children);
            }

            if (parameter.Capability is not null)
            {
                writer.WriteString("capability", parameter.Capability.Name);
            }

            if (parameter.Payload is { } payload)
            {
                writer.WriteString("payload", Camel(payload.ToString()));
            }

            if (parameter.MaterialParameter.Length > 0)
            {
                writer.WriteString("materialParameter", parameter.MaterialParameter);
            }

            if (parameter.Slot.Length > 0)
            {
                writer.WriteString("slot", parameter.Slot);
            }

            if (parameter.MinCount > 0)
            {
                writer.WriteNumber("minCount", parameter.MinCount);
            }

            if (parameter.RequiredKind is { } tagKind)
            {
                writer.WriteString("tagKind", Camel(tagKind.ToString()));
            }

            if (parameter.IsWriteTarget)
            {
                writer.WriteBoolean("writes", true);
            }

            if (parameter.TagParameter.Length > 0)
            {
                writer.WriteString("tagParameter", parameter.TagParameter);
            }

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteComponent(Utf8JsonWriter writer, ComponentDescriptor component, string module)
    {
        writer.WriteStartObject();
        writer.WriteString("type", component.Type);
        writer.WriteString("module", module);
        writer.WriteString("category", Camel(component.Category.ToString()));
        writer.WriteString("description", component.Description);
        writer.WritePropertyName("parameters");
        WriteParameters(writer, component.Parameters);

        writer.WriteStartArray("ports");
        foreach (PortDescriptor port in component.Ports)
        {
            writer.WriteStartObject();
            writer.WriteString("name", port.Name);
            writer.WriteString("direction", Camel(port.Direction.ToString()));
            writer.WriteString("valueType", port.ValueType);
            WriteOptional(writer, "unit", port.Unit);
            if (port.Required)
            {
                writer.WriteBoolean("required", true);
            }

            WriteOptional(writer, "description", port.Description);
            WriteRepeat(writer, port.Repeat);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();

        writer.WriteStartArray("flowPorts");
        foreach (FlowPortDescriptor port in component.FlowPorts)
        {
            writer.WriteStartObject();
            writer.WriteString("name", port.Name);
            writer.WriteString("direction", Camel(port.Direction.ToString()));
            writer.WriteString("payload", Camel(port.Payload.ToString()));
            WriteOptional(writer, "description", port.Description);
            WriteRepeat(writer, port.Repeat);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();

        writer.WriteStartArray("faults");
        foreach (FaultDescriptor fault in component.Faults)
        {
            writer.WriteStartObject();
            writer.WriteString("id", fault.Id);
            writer.WriteString("description", fault.Description);
            writer.WriteStartArray("parameters");
            foreach (FaultParameter parameter in fault.Parameters)
            {
                writer.WriteStartObject();
                writer.WriteString("name", parameter.Name);
                writer.WriteString("unit", parameter.Unit);
                writer.WriteNumber("default", parameter.DefaultValue);
                writer.WriteString("description", parameter.Description);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        writer.WriteEndArray();

        writer.WriteStartArray("tags");
        foreach (TagEntry tag in component.Tags)
        {
            writer.WriteStartObject();
            writer.WriteString("name", tag.Name);
            writer.WriteString("kind", Camel(tag.Kind.ToString()));
            writer.WriteString("access", Camel(tag.Access.ToString()));
            WriteOptional(writer, "unit", tag.Unit);
            WriteRepeat(writer, tag.Repeat);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();

        writer.WriteStartArray("telemetry");
        foreach (TelemetryKey key in component.Telemetry)
        {
            writer.WriteStartObject();
            writer.WriteString("name", key.Name);
            WriteOptional(writer, "unit", key.Unit);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();

        writer.WriteStartArray("provides");
        foreach (string capability in component.Provides.Select(t => t.Name).Order(StringComparer.Ordinal))
        {
            writer.WriteStringValue(capability);
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteOptional(Utf8JsonWriter writer, string name, string value)
    {
        if (value.Length > 0)
        {
            writer.WriteString(name, value);
        }
    }

    private static void WriteRepeat(Utf8JsonWriter writer, PortRepeat? repeat)
    {
        if (repeat is null)
        {
            return;
        }

        writer.WriteStartObject("repeat");
        writer.WriteString("parameter", repeat.Parameter);
        WriteOptional(writer, "nameChild", repeat.NameChild);
        writer.WriteEndObject();
    }
}
