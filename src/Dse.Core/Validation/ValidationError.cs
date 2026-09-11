namespace Dse.Core.Validation;

/// <summary>
/// One reason a plant cannot run. The message must state the fix, not only the
/// symptom — the reader is often an agent that will act on it directly.
/// </summary>
public sealed record ValidationError(
    string Code,
    string Message,
    IReadOnlyList<string> ComponentIds);
