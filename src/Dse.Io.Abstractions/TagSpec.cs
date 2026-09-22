namespace Dse.Io;

/// <summary>
/// A tag a scan block owns, named relative to the block: the host publishes it
/// as <c>&lt;block id&gt;.&lt;name&gt;</c>. An output is read-only to everyone
/// else; a command is read-write, so a command bus, a scenario write and a
/// test's <c>WriteAt</c> all reach it.
/// </summary>
/// <param name="Name">The pin name, such as <c>Ok</c> or <c>Hi.Active</c>.</param>
/// <param name="Kind">The value kind.</param>
/// <param name="Unit">Engineering unit; empty for discrete tags.</param>
/// <param name="Description">A sentence fragment for humans.</param>
public sealed record TagSpec(string Name, TagKind Kind, string Unit = "", string Description = "")
{
    /// <summary>The pin name, such as <c>Ok</c> or <c>Hi.Active</c>.</summary>
    public string Name { get; init; } = TagNameRules.Check(Name, nameof(Name));
}
