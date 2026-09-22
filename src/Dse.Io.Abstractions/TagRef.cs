namespace Dse.Io;

/// <summary>
/// A plant tag a scan block reads or commands, by the full name
/// <c>dse tags</c> prints. The kind must match the tag's; the host reports a
/// mismatch as DSE014 rather than converting.
/// </summary>
/// <param name="Name">The tag's full name, such as <c>CV001.Start</c>.</param>
/// <param name="Kind">The kind the block expects.</param>
public sealed record TagRef(string Name, TagKind Kind)
{
    /// <summary>The tag's full name, such as <c>CV001.Start</c>.</summary>
    public string Name { get; init; } = TagNameRules.Check(Name, nameof(Name));
}
