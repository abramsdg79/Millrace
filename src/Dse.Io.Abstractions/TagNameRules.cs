namespace Dse.Io;

/// <summary>
/// The one place a tag or pin name is checked. The rules match
/// <c>TagBinding.ValidName</c> in <c>Dse.Core</c>, which cannot be referenced
/// from here; if one changes, both change.
/// </summary>
internal static class TagNameRules
{
    /// <summary>Returns the name, or throws <see cref="ArgumentException"/> naming <paramref name="parameter"/>.</summary>
    internal static string Check(string name, string parameter)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name, parameter);

        if (name.StartsWith('.') || name.EndsWith('.') || name.Contains("..", StringComparison.Ordinal))
        {
            throw new ArgumentException($"Name '{name}' has an empty segment.", parameter);
        }

        foreach (char c in name)
        {
            if (char.IsWhiteSpace(c))
            {
                throw new ArgumentException($"Name '{name}' contains whitespace.", parameter);
            }
        }

        return name;
    }
}
