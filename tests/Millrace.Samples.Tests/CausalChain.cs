using System.Globalization;
using Millrace.Core.Logging;

namespace Millrace.Samples.Tests;

/// <summary>
/// One event a story expects: an exact source (or any source, when null), an
/// exact code, and a fragment the message must contain. Exact, not prefix: a
/// pattern on <c>CV001</c> never matches <c>CV001.Starter</c>.
/// </summary>
public sealed record EventPattern(string? Source, string Code, string MessageFragment = "")
{
    public bool Matches(SimEventRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return (Source is null || string.Equals(record.Source, Source, StringComparison.Ordinal))
            && string.Equals(record.Code, Code, StringComparison.Ordinal)
            && record.Message.Contains(MessageFragment, StringComparison.Ordinal);
    }

    public override string ToString()
    {
        string source = Source ?? "any source";
        return MessageFragment.Length == 0 ? $"{source} {Code}" : $"{source} {Code} \"{MessageFragment}\"";
    }
}

/// <summary>
/// Something that must not happen: <see cref="Forbidden"/> never occurs after
/// the first match of <see cref="After"/> (the start of the log when null) and
/// before the first match of <see cref="Until"/> that follows it (the end of the
/// log when null).
/// </summary>
public sealed record Absence(EventPattern? After, EventPattern Forbidden, EventPattern? Until = null);

/// <summary>
/// Asserts a scenario's story over its event log. A chain is an ordered
/// subsequence: each pattern must match an event strictly after the event the
/// previous pattern matched, and other events may come between. Both checks
/// return null when they hold and a message naming what was missing when they
/// do not, so a test reads <c>Assert.Null(CausalChain.FindChain(...))</c> and a
/// failure prints the reason.
/// </summary>
public static class CausalChain
{
    public static string? FindChain(IReadOnlyList<SimEventRecord> events, IReadOnlyList<EventPattern> chain)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(chain);

        int next = 0;
        string lastMatched = "the start of the log";
        foreach (EventPattern pattern in chain)
        {
            int found = IndexOf(events, pattern, next);
            if (found < 0)
            {
                return $"The chain breaks at {pattern}: no such event after {lastMatched}.";
            }

            lastMatched = $"{pattern}, matched by '{Line(events[found])}'";
            next = found + 1;
        }

        return null;
    }

    public static string? FindAbsence(IReadOnlyList<SimEventRecord> events, Absence absence)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(absence);

        int start = 0;
        if (absence.After is { } after)
        {
            int found = IndexOf(events, after, 0);
            if (found < 0)
            {
                return $"The absence of {absence.Forbidden} starts at {after}, which never occurs.";
            }

            start = found + 1;
        }

        int end = events.Count;
        if (absence.Until is { } until)
        {
            end = IndexOf(events, until, start);
            if (end < 0)
            {
                return $"The absence of {absence.Forbidden} ends at {until}, which never occurs after its start.";
            }
        }

        for (int i = start; i < end; i++)
        {
            if (absence.Forbidden.Matches(events[i]))
            {
                return $"{absence.Forbidden} must not occur here, but '{Line(events[i])}' does.";
            }
        }

        return null;
    }

    /// <summary>The record as <c>EventLog.ToText()</c> prints it.</summary>
    public static string Line(SimEventRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{record.SimTime:HH:mm:ss.fff}  {record.Source}  {record.Code}  {record.Message}");
    }

    private static int IndexOf(IReadOnlyList<SimEventRecord> events, EventPattern pattern, int from)
    {
        for (int i = from; i < events.Count; i++)
        {
            if (pattern.Matches(events[i]))
            {
                return i;
            }
        }

        return -1;
    }
}
