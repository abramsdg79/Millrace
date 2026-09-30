namespace Dse.Core.Validation;

/// <summary>
/// One reason a plant cannot run. The message must state the fix, not only the
/// symptom — the reader is often an agent that will act on it directly.
/// </summary>
public sealed record ValidationError(
    string Code,
    string Message,
    IReadOnlyList<string> ComponentIds)
{
    /// <summary>
    /// The claimed tag a DSE016 is about, exactly as the claim spelled it, so a
    /// plant file can report it at the claim; empty for every other error.
    /// </summary>
    public string Tag { get; init; } = "";

    /// <summary>
    /// For a DSE016, the position of the claim it is about in the list its
    /// block was added with — the block's own claim, or, when another block
    /// commands the tag, the claimant's; -1 for every other error.
    /// </summary>
    public int ClaimIndex { get; init; } = -1;
}
