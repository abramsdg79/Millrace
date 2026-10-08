namespace Millrace.Core.Catalogue;

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
    /// <summary>A tag name the context's tag table does not hold. Construct mode with a tag table only.</summary>
    UnknownTag,
    /// <summary>A tag of the wrong kind, or a value that does not fit its tag's kind. Construct mode with a tag table only.</summary>
    WrongTagKind,
    /// <summary>A block commands a read-only tag. Construct mode with a tag table only.</summary>
    ReadOnlyTag,
}

/// <summary>One thing wrong with a parameter, where it is, and what to do about it.</summary>
public sealed record BindingIssue(BindingIssueKind Kind, string Path, string Message, string Fix);
