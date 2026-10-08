using Millrace.Core.Catalogue;

namespace Millrace.Configuration.Loading;

/// <summary>Parameter schemas for the parts of a plant file that are not component parameters.</summary>
internal static class PlantSchemas
{
    public static readonly string[] TopLevelKeys = ["$schema", "defaults", "materials", "components", "signals", "flows", "tags", "controllers"];

    public static readonly string[] DefaultsKeys = ["seed", "timeStepMs", "startTime"];

    public static readonly string[] ComponentKeys = ["id", "type", "parameters"];

    public static readonly string[] ControllerKeys = ["id", "type", "scanPeriodMs", "claims", "parameters"];

    public static readonly GroupDefinition MaterialProperties = new(
        "MaterialProperties",
        Param.Double("density", "Bulk density.", "kg/m³", @default: 0.0, min: 0.0),
        Param.Double("moisture", "Moisture as a mass fraction.", @default: 0.0, min: 0.0, max: 1.0),
        Param.Double("temperature", "Temperature of new material.", "°C", @default: 20.0));

    public static readonly ParameterDescriptor[] Material =
    [
        Param.String("name", "What components call it. Unique across the plant and the catalogue."),
        Param.Enum("kind", "Whether it flows as bulk mass or as discrete items.", ["bulk", "discrete"]),
        Param.StringList("states", "Names of the per-material state values a transform can accumulate into."),
        Param.Group("properties", "The properties a source gives new material.", MaterialProperties),
        Param.String("description", "For people.", @default: ""),
    ];

    public static readonly ParameterDescriptor[] Link =
    [
        Param.String("from", "The driving port, as <component>.<port>."),
        Param.String("to", "The driven port, as <component>.<port>."),
    ];

    public static readonly ParameterDescriptor[] Tag =
    [
        Param.String("name", "The tag name a client reads or writes."),
        Param.String("port", "The port bound, as <component>.<port>."),
        Param.Enum("access", "Whether a client may write it. Only an input can be written.", ["read", "write"], @default: "read"),
        Param.String("unit", "Engineering unit.", @default: ""),
        Param.Double("rangeLow", "Bottom of the range. Give both ends or neither.", optional: true),
        Param.Double("rangeHigh", "Top of the range. Give both ends or neither.", optional: true),
        Param.String("description", "A sentence fragment; becomes the tag's description.", @default: ""),
    ];
}
