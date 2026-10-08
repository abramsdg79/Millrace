using System.Globalization;

namespace Millrace.Components.Mechanical;

/// <summary>
/// How a component's event message prints a measured value next to the
/// threshold it crossed, so the text never reads on the wrong side of it
/// (plan 7, R187). The arithmetic is the alarm's (6e R145): format with
/// <c>F&lt;n&gt;</c>, parse the text back, and step it one unit in the last
/// place, in <c>decimal</c>, only when it lies on the wrong side — never
/// scale. <c>Millrace.Control</c>'s alarm keeps its own copy: Components cannot
/// reference Control.
/// </summary>
internal static class ThresholdText
{
    /// <summary>
    /// The decimals of a level's shortest round-trip form, as the alarm counts
    /// them: <c>1.1</c> has 1, <c>1.125</c> 3, <c>2</c> 0, <c>1E-07</c> 7.
    /// </summary>
    public static int Decimals(double level)
    {
        string shortest = level.ToString("R", CultureInfo.InvariantCulture);
        int e = shortest.IndexOf('E', StringComparison.Ordinal);
        int exponent = e < 0 ? 0 : int.Parse(shortest.AsSpan(e + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
        string mantissa = e < 0 ? shortest : shortest[..e];
        int dot = mantissa.IndexOf('.', StringComparison.Ordinal);
        return Math.Max(0, (dot < 0 ? 0 : mantissa.Length - dot - 1) - exponent);
    }

    /// <summary>
    /// <paramref name="value"/>, which has reached <paramref name="level"/>,
    /// to <paramref name="decimals"/> places: the nearest text, stepped up
    /// once only if it reads below the level. A value on the level reads on it.
    /// </summary>
    public static string NotBelow(double value, double level, int decimals)
    {
        string text = Format(value, decimals);
        return double.Parse(text, CultureInfo.InvariantCulture) < level ? StepUp(text, decimals) : text;
    }

    /// <summary>
    /// <paramref name="value"/>, which exceeds a limit printed as
    /// <paramref name="limitText"/>, to <paramref name="decimals"/> places:
    /// rounded up (the nearest text, stepped once when it lies below the
    /// value), and one step above the limit's text if that still does not
    /// exceed it. A non-finite value, or one beyond <c>decimal</c>'s range,
    /// prints as its plain text.
    /// </summary>
    public static string Exceeding(double value, string limitText, int decimals)
    {
        if (!double.IsFinite(value) || Math.Abs(value) >= 7.9e28)
        {
            return Format(value, decimals);   // beyond decimal's range: the plain text, as before
        }

        string text = Format(value, decimals);
        if (double.Parse(text, CultureInfo.InvariantCulture) < value)
        {
            text = StepUp(text, decimals);
        }

        return decimal.Parse(text, CultureInfo.InvariantCulture) > decimal.Parse(limitText, CultureInfo.InvariantCulture)
            ? text
            : StepUp(limitText, decimals);
    }

    private static string Format(double value, int decimals) =>
        value.ToString("F" + decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

    private static string StepUp(string text, int decimals) =>
        (decimal.Parse(text, CultureInfo.InvariantCulture) + new decimal(1, 0, 0, false, (byte)decimals))
            .ToString("F" + decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
}
