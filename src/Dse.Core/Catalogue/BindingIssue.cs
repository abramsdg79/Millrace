namespace Dse.Core.Catalogue;

public enum BindingIssueKind
{
    /// <summary>A key the schema does not declare.</summary>
    UnknownKey,
    /// <summary>An object <c>"type"</c> the slot does not have.</summary>
    UnknownType,
    /// <summary>Missing, wrong JSON type, out of range, not an allowed value, list too short.</summary>
    BadParameter,
    UnknownMaterial,
    UnknownState,
    /// <summary>A reference to a node the context does not hold. Construct mode only.</summary>
    MissingReference,
    /// <summary>The referenced node cannot supply what the parameter needs. Construct mode only.</summary>
    MissingCapability,
    /// <summary>A factory threw <see cref="ArgumentException"/>. Construct mode only.</summary>
    Rejected,
}

/// <summary>One thing wrong with a parameter, where it is, and what to do about it.</summary>
public sealed record BindingIssue(BindingIssueKind Kind, string Path, string Message, string Fix);
