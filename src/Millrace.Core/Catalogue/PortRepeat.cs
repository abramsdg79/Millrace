using System.Globalization;

namespace Millrace.Core.Catalogue;

/// <summary>
/// Declares a family of ports (or tags) whose number or names come from a
/// parameter. With no <paramref name="NameChild"/> the parameter is an
/// <see cref="ParameterKind.Int"/> and <c>{n}</c> runs 1..value. With one, the
/// parameter is a <see cref="ParameterKind.GroupList"/> and <c>{n}</c> is each
/// element's string child of that name.
/// </summary>
public sealed record PortRepeat(string Parameter, string NameChild = "")
{
    public bool IsByName => NameChild.Length > 0;

    /// <summary>The concrete names for a pattern, given the count or the names read from the parameter.</summary>
    public IReadOnlyList<string> Expand(string pattern, int count, IReadOnlyList<string> names)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        ArgumentNullException.ThrowIfNull(names);
        var result = new List<string>();
        if (IsByName)
        {
            foreach (string name in names)
            {
                result.Add(pattern.Replace("{n}", name, StringComparison.Ordinal));
            }
        }
        else
        {
            for (int i = 1; i <= count; i++)
            {
                result.Add(pattern.Replace("{n}", i.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal));
            }
        }

        return result;
    }
}
