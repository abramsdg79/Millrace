namespace Dse.Core.Faults;

/// <summary>
/// What a component says about one way it can break: an id, a sentence, and
/// the parameters an injection may set. Tooling asks "what can break here?"
/// and gets this.
/// </summary>
public sealed record FaultDescriptor(string Id, string Description, IReadOnlyList<FaultParameter> Parameters)
{
    public FaultDescriptor(string id, string description, params FaultParameter[] parameters)
        : this(id, description, (IReadOnlyList<FaultParameter>)parameters.ToArray())
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
    }

    /// <summary>
    /// Every declared parameter in declared order, taking the given value where
    /// there is one and the default otherwise. A name this fault does not
    /// declare is an error naming the ones it does.
    /// </summary>
    public FaultArguments Resolve(FaultArguments given)
    {
        ArgumentNullException.ThrowIfNull(given);

        string[] declared = Parameters.Select(p => p.Name).ToArray();
        for (int i = 0; i < given.Count; i++)
        {
            if (Array.IndexOf(declared, given[i].Name) < 0)
            {
                throw new ArgumentException(
                    $"Fault '{Id}' has no parameter '{given[i].Name}'. Declared: " +
                    $"{(declared.Length == 0 ? "none" : string.Join(", ", declared))}.",
                    nameof(given));
            }
        }

        var resolved = new FaultArgument[Parameters.Count];
        for (int i = 0; i < resolved.Length; i++)
        {
            FaultParameter parameter = Parameters[i];
            resolved[i] = new FaultArgument(
                parameter.Name,
                given.TryGet(parameter.Name, out double value) ? value : parameter.DefaultValue);
        }

        return new FaultArguments(resolved);
    }
}
