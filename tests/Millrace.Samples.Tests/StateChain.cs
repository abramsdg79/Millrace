using System.Globalization;

namespace Millrace.Samples.Tests;

/// <summary>One sampled value of a tag, at a time from the start of the run.</summary>
public sealed record TagSample(TimeSpan Time, double Value);

/// <summary>
/// Asserts a story told by state rather than by events: a set of sampled tags
/// that fall, one after another, in a given order. Like <see cref="CausalChain"/>,
/// a check returns null when it holds and a message when it does not.
/// </summary>
public static class StateChain
{
    /// <summary>
    /// The first sample time at or after <paramref name="from"/> from which the
    /// series stays at or below <paramref name="ceiling"/> to its end; null if it
    /// never settles there.
    /// </summary>
    public static TimeSpan? SettlesAtOrBelow(IReadOnlyList<TagSample> series, double ceiling, TimeSpan from)
    {
        ArgumentNullException.ThrowIfNull(series);

        TimeSpan? since = null;
        foreach (TagSample sample in series)
        {
            if (sample.Time < from)
            {
                continue;
            }

            since = sample.Value <= ceiling ? since ?? sample.Time : null;
        }

        return since;
    }

    /// <summary>
    /// Null when every tag in <paramref name="order"/> was above
    /// <paramref name="floor"/> at <paramref name="from"/> — so its fall means
    /// something — and then settles at or below <paramref name="ceiling"/>, each
    /// strictly later than the tag before it. Otherwise a message naming the first
    /// tag that does not.
    /// </summary>
    public static string? FindFallInOrder(
        IReadOnlyDictionary<string, IReadOnlyList<TagSample>> traces,
        IReadOnlyList<string> order,
        TimeSpan from,
        double floor,
        double ceiling)
    {
        ArgumentNullException.ThrowIfNull(traces);
        ArgumentNullException.ThrowIfNull(order);

        TimeSpan? previous = null;
        string previousTag = string.Empty;
        foreach (string tag in order)
        {
            if (!traces.TryGetValue(tag, out IReadOnlyList<TagSample>? series))
            {
                return $"There is no trace of {tag}.";
            }

            TagSample? start = series.FirstOrDefault(s => s.Time >= from);
            if (start is null || start.Value <= floor)
            {
                return string.Create(CultureInfo.InvariantCulture,
                    $"{tag} was not above {floor} at {from.TotalSeconds} s, so its fall proves nothing.");
            }

            TimeSpan? settled = SettlesAtOrBelow(series, ceiling, from);
            if (settled is not { } at)
            {
                return string.Create(CultureInfo.InvariantCulture, $"{tag} never settles at or below {ceiling}.");
            }

            if (previous is { } before && at <= before)
            {
                return string.Create(CultureInfo.InvariantCulture,
                    $"{tag} settles at or below {ceiling} at {at.TotalSeconds} s, not after {previousTag} at {before.TotalSeconds} s.");
            }

            previous = at;
            previousTag = tag;
        }

        return null;
    }
}
