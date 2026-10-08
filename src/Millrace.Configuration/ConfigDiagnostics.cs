using Millrace.Core.Catalogue;

namespace Millrace.Configuration;

/// <summary>Every configuration diagnostic code. The reference page in docs/ is generated from <see cref="All"/>.</summary>
public static class ConfigDiagnostics
{
    public const string Syntax = "MR100";
    public const string UnknownKey = "MR101";
    public const string UnknownType = "MR102";
    public const string BadParameter = "MR103";
    public const string MissingReference = "MR104";
    public const string MissingCapability = "MR105";
    public const string ReferenceCycle = "MR106";
    public const string Duplicate = "MR107";
    public const string UnknownAddress = "MR108";
    public const string CannotConnect = "MR109";
    public const string UnknownState = "MR110";
    public const string Rejected = "MR111";
    public const string TagCannotBind = "MR112";
    public const string UnknownTag = "MR113";
    public const string WrongKind = "MR114";
    public const string ReadOnlyTag = "MR115";

    public static IReadOnlyList<DiagnosticInfo> All { get; } =
    [
        new(Syntax, "The file is not valid JSON",
            "The text could not be parsed. Comments and trailing commas are allowed; everything else must be strict JSON. The message gives the line and column."),
        new(UnknownKey, "Unknown key",
            "An object has a key the loader does not know. Keys match exactly, including case. Nothing is ignored silently, so a misspelt optional parameter cannot quietly fall back to its default."),
        new(UnknownType, "Unknown component, block, object or material type",
            "A \"type\" names something that is not in the catalogue — a component, a controller's block, an object in a slot — or a material name is not defined in the plant or the catalogue. Custom types need their assembly loaded with --assembly."),
        new(BadParameter, "Parameter missing, of the wrong type, or out of range",
            "A required parameter is absent, a value has the wrong JSON type, a number is outside its declared range, an enum value is not allowed, or a list is shorter than its minimum. A controller's scanPeriodMs is required: a number above zero, at least one tick and at most a day."),
        new(MissingReference, "Reference to a component that does not exist",
            "A reference parameter holds an id that no component in the plant has. Ids match exactly."),
        new(MissingCapability, "Referenced component lacks the required capability",
            "The referenced component exists but cannot supply what the parameter needs — a belt scale pointed at a motor. The fix lists the components that can."),
        new(ReferenceCycle, "Reference cycle",
            "Components reference each other in a loop, so none of them can be built first."),
        new(Duplicate, "Duplicate id or material name",
            "Two components, two controllers, or a component and a controller share an id — they share one set of ids, because a block's id prefixes its tags — or a material is defined twice (the catalogue's materials and the plant's share one namespace)."),
        new(UnknownAddress, "Unknown component or port in an address",
            "A signal, flow or tag address is not of the form <component>.<port>, or names a component or port that does not exist. Port names match ignoring case."),
        new(CannotConnect, "The two ports cannot be connected",
            "A link joins ports that cannot be joined: two outputs, different value types, a signal port under \"flows\", bulk into discrete, an input that is already driven."),
        new(UnknownState, "Unknown material state",
            "A state name is not one of the states the named material declares."),
        new(Rejected, "A constructor rejected its parameters",
            "Every parameter was individually valid but the component, block or object refused the combination — a reset level above the trip level, a belt length that is not a whole number of cells, alarm limits that do not ascend. The message is the constructor's own. A factory that fails any other way is a defect in its module, and the fix names the module."),
        new(TagCannotBind, "A tag cannot bind that port",
            "A tag names a port that has no tag kind (a flow port, an enum output), or asks to write an output."),
        new(UnknownTag, "A controller names a tag the plant does not have",
            "A controller's tag parameter names no component tag, composite exposure or \"tags\" bind of the plant, and no tag a controller owns. Names match exactly, including case; the fix suggests the nearest, and `millrace tags` lists them all."),
        new(WrongKind, "A controller's tag or value is of the wrong kind",
            "A controller's value does not fit the kind of the tag it is for — 1.5 for an Int64 tag, true for a Double one — or a controller names a tag of a kind the block cannot use, such as a Double tag as an interlock condition. Integer-valued numbers fit Int64 and Double tags; only true and false fit a Bool tag."),
        new(ReadOnlyTag, "A controller commands a read-only tag",
            "A controller writes a tag that does not accept writes: a measured value, another block's output, or a command input a signal link already drives, whose tag the plant publishes read-only."),
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
            BindingIssueKind.UnknownTag => UnknownTag,
            BindingIssueKind.WrongTagKind => WrongKind,
            BindingIssueKind.ReadOnlyTag => ReadOnlyTag,
            _ => throw new InvalidOperationException($"Binding issue kind {issue.Kind} has no diagnostic code."),
        },
        issue.Path,
        issue.Message,
        issue.Fix);
}
