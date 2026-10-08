using System.Globalization;

namespace Millrace.Core.Catalogue;

/// <summary>Turns "that name does not exist" into "did you mean". Deterministic: ties break by ordinal order.</summary>
public static class Suggest
{
    /// <summary>The candidate nearest to <paramref name="given"/> ignoring case, or null when none is near enough to be a typo.</summary>
    public static string? Closest(string given, IEnumerable<string> candidates)
    {
        ArgumentNullException.ThrowIfNull(given);
        ArgumentNullException.ThrowIfNull(candidates);

        int limit = Math.Max(2, given.Length / 3);
        string? best = null;
        int bestDistance = int.MaxValue;
        foreach (string candidate in candidates.Order(StringComparer.Ordinal))
        {
            int distance = Distance(given.ToUpperInvariant(), candidate.ToUpperInvariant());
            if (distance <= limit && distance < bestDistance)
            {
                best = candidate;
                bestDistance = distance;
            }
        }

        return best;
    }

    /// <summary>Names sorted and comma-joined; beyond <paramref name="max"/> the rest become a count.</summary>
    public static string List(IEnumerable<string> names, int max = 8)
    {
        ArgumentNullException.ThrowIfNull(names);
        List<string> sorted = names.Order(StringComparer.Ordinal).ToList();
        if (sorted.Count <= max)
        {
            return string.Join(", ", sorted);
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{string.Join(", ", sorted.Take(max))}, … ({sorted.Count} in all)");
    }

    /// <summary>A whole fix sentence. <paramref name="noun"/> is plural: "components".</summary>
    public static string Fix(string given, IEnumerable<string> candidates, string noun)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        List<string> all = candidates.ToList();
        if (all.Count == 0)
        {
            return $"There are no {noun} to choose from; define one first.";
        }

        string? closest = Closest(given, all);
        return closest is null
            ? $"Use one of {List(all)}."
            : $"Use one of {List(all)} — '{closest}' is closest.";
    }

    private static int Distance(string a, string b)
    {
        // Optimal string alignment: Levenshtein plus adjacent transposition.
        int[,] d = new int[a.Length + 1, b.Length + 1];
        for (int i = 0; i <= a.Length; i++)
        {
            d[i, 0] = i;
        }

        for (int j = 0; j <= b.Length; j++)
        {
            d[0, j] = j;
        }

        for (int i = 1; i <= a.Length; i++)
        {
            for (int j = 1; j <= b.Length; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                int best = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
                if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1])
                {
                    best = Math.Min(best, d[i - 2, j - 2] + 1);
                }

                d[i, j] = best;
            }
        }

        return d[a.Length, b.Length];
    }
}
