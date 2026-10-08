using System.Globalization;
using System.Text;

namespace Millrace.Scenarios;

/// <summary>
/// Judges a run's event log against a committed one. The command line and the
/// tests share this, so "the behaviour changed" means the same in both.
/// </summary>
public static class GoldenLog
{
    private const int Context = 3;

    /// <summary><c>\n</c> line endings and exactly one trailing newline; an empty log stays empty.</summary>
    public static string Normalise(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        string normalised = text.ReplaceLineEndings("\n").TrimEnd('\n');
        return normalised.Length == 0 ? string.Empty : normalised + "\n";
    }

    /// <summary>Compares two logs after normalising both.</summary>
    public static LogComparison Compare(string expected, string actual)
    {
        string left = Normalise(expected);
        string right = Normalise(actual);
        string[] expectedLines = Lines(left);
        string[] actualLines = Lines(right);

        if (string.Equals(left, right, StringComparison.Ordinal))
        {
            return new LogComparison(
                true,
                0,
                expectedLines.Length,
                actualLines.Length,
                string.Create(CultureInfo.InvariantCulture, $"The logs match ({actualLines.Length} lines).\n"));
        }

        int index = 0;
        while (index < expectedLines.Length
               && index < actualLines.Length
               && string.Equals(expectedLines[index], actualLines[index], StringComparison.Ordinal))
        {
            index++;
        }

        var report = new StringBuilder();
        report.Append(string.Create(CultureInfo.InvariantCulture, $"The logs differ at line {index + 1}.\n"));
        Block(report, "expected", expectedLines, index);
        Block(report, "actual", actualLines, index);
        return new LogComparison(false, index + 1, expectedLines.Length, actualLines.Length, report.ToString());
    }

    private static string[] Lines(string normalised) =>
        normalised.Length == 0 ? [] : normalised.TrimEnd('\n').Split('\n');

    private static void Block(StringBuilder report, string label, string[] lines, int index)
    {
        report.Append(string.Create(CultureInfo.InvariantCulture, $"{label} ({lines.Length} lines):\n"));
        int from = Math.Max(0, index - Context);
        int to = Math.Min(lines.Length - 1, index + Context);
        if (to < from)
        {
            report.Append("  (no lines)\n");
            return;
        }

        for (int i = from; i <= to; i++)
        {
            report.Append(string.Create(
                CultureInfo.InvariantCulture, $"{(i == index ? '>' : ' ')} {i + 1,5} | {lines[i]}\n"));
        }
    }
}
