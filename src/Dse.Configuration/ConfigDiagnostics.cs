using Dse.Core.Catalogue;

namespace Dse.Configuration;

/// <summary>Every configuration diagnostic code. The reference page in docs/ is generated from <see cref="All"/>.</summary>
public static class ConfigDiagnostics
{
    public const string Syntax = "DSE100";
    public const string UnknownKey = "DSE101";
    public const string UnknownType = "DSE102";
    public const string BadParameter = "DSE103";
    public const string MissingReference = "DSE104";
    public const string MissingCapability = "DSE105";
    public const string ReferenceCycle = "DSE106";
    public const string Duplicate = "DSE107";
    public const string UnknownAddress = "DSE108";
    public const string CannotConnect = "DSE109";
    public const string UnknownState = "DSE110";
    public const string Rejected = "DSE111";
    public const string TagCannotBind = "DSE112";

    public static IReadOnlyList<DiagnosticInfo> All { get; } =
    [
        new(Syntax, "The file is not valid JSON",
            "The text could not be parsed. Comments and trailing commas are allowed; everything else must be strict JSON. The message gives the line and column."),
        new(UnknownKey, "Unknown key",
            "An object has a key the loader does not know. Keys match exactly, including case. Nothing is ignored silently, so a misspelt optional parameter cannot quietly fall back to its default."),
        new(UnknownType, "Unknown component, object or material type",
            "A \"type\" names something that is not in the catalogue, or a material name is not defined in the plant or the catalogue. Custom types need their assembly loaded with --assembly."),
        new(BadParameter, "Parameter missing, of the wrong type, or out of range",
            "A required parameter is absent, a value has the wrong JSON type, a number is outside its declared range, an enum value is not allowed, or a list is shorter than its minimum."),
        new(MissingReference, "Reference to a component that does not exist",
            "A reference parameter holds an id that no component in the plant has. Ids match exactly."),
        new(MissingCapability, "Referenced component lacks the required capability",
            "The referenced component exists but cannot supply what the parameter needs — a belt scale pointed at a motor. The fix lists the components that can."),
        new(ReferenceCycle, "Reference cycle",
            "Components reference each other in a loop, so none of them can be built first."),
        new(Duplicate, "Duplicate component id or material name",
            "Two components share an id, or a material is defined twice (the catalogue's materials and the plant's share one namespace)."),
        new(UnknownAddress, "Unknown component or port in an address",
            "A signal, flow or tag address is not of the form <component>.<port>, or names a component or port that does not exist. Port names match ignoring case."),
        new(CannotConnect, "The two ports cannot be connected",
            "A link joins ports that cannot be joined: two outputs, different value types, a signal port under \"flows\", bulk into discrete, an input that is already driven."),
        new(UnknownState, "Unknown material state",
            "A state name is not one of the states the named material declares."),
        new(Rejected, "A constructor rejected its parameters",
            "Every parameter was individually valid but the component or object refused the combination — a reset level above the trip level, a belt length that is not a whole number of cells. The message is the constructor's own."),
        new(TagCannotBind, "A tag cannot bind that port",
            "A tag names a port that has no tag kind (a flow port, an enum output), or asks to write an output."),
    ];

    internal static ConfigDiagnostic Error(string code, string path, string message, string fix) =>
        new(code, DiagnosticSeverity.Error, path, message, fix);

    internal static ConfigDiagnostic From(BindingIssue issue) => Error(
        issue.Kind switch
        {
            BindingIssueKind.UnknownKey => UnknownKey,
            BindingIssueKind.UnknownType or BindingIssueKind.UnknownMaterial => UnknownType,
            BindingIssueKind.BadParameter => BadParameter,
            BindingIssueKind.UnknownState => UnknownState,
            BindingIssueKind.MissingReference => MissingReference,
            BindingIssueKind.MissingCapability => MissingCapability,
            BindingIssueKind.Rejected => Rejected,
            _ => throw new InvalidOperationException($"Binding issue kind {issue.Kind} has no diagnostic code."),
        },
        issue.Path,
        issue.Message,
        issue.Fix);
}
