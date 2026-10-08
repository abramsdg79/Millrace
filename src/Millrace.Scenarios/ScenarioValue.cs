using System.Globalization;
using Millrace.Io;

namespace Millrace.Scenarios;

/// <summary>
/// A write's value exactly as the file gave it. It stays in this form until the
/// tag's kind is known, at scheduling: a scenario is checked against a plant,
/// not guessed at while parsing.
/// </summary>
public sealed record ScenarioValue
{
    private ScenarioValue(ScenarioValueKind kind, bool boolean, double number, long integer)
    {
        Kind = kind;
        Boolean = boolean;
        Number = number;
        Integer = integer;
    }

    /// <summary>Which of the three payloads is meaningful.</summary>
    public ScenarioValueKind Kind { get; }

    /// <summary>The payload of a <see cref="ScenarioValueKind.Bool"/> value.</summary>
    public bool Boolean { get; }

    /// <summary>The payload of a numeric value; for an integer, the same number as a double.</summary>
    public double Number { get; }

    /// <summary>The payload of a <see cref="ScenarioValueKind.Integer"/> value.</summary>
    public long Integer { get; }

    /// <summary>A JSON <c>true</c> or <c>false</c>.</summary>
    public static ScenarioValue OfBool(bool value) => new(ScenarioValueKind.Bool, value, 0.0, 0L);

    /// <summary>A JSON number that is not an integer literal.</summary>
    public static ScenarioValue OfNumber(double value) => new(ScenarioValueKind.Number, false, value, 0L);

    /// <summary>A JSON integer literal, which may drive an Int64 tag or a Double one.</summary>
    public static ScenarioValue OfInteger(long value) => new(ScenarioValueKind.Integer, false, value, value);

    /// <summary>The tag value for a tag of <paramref name="kind"/>, or null when this value cannot be one.</summary>
    public TagValue? ToTagValue(TagKind kind) => (kind, Kind) switch
    {
        (TagKind.Bool, ScenarioValueKind.Bool) => TagValue.Bool(Boolean),
        (TagKind.Double, ScenarioValueKind.Number or ScenarioValueKind.Integer) => TagValue.Double(Number),
        (TagKind.Int64, ScenarioValueKind.Integer) => TagValue.Int64(Integer),
        _ => null,
    };

    /// <summary>What the file said, for a diagnostic: <c>true</c>, <c>1.5</c>, <c>7</c>.</summary>
    public override string ToString() => Kind switch
    {
        ScenarioValueKind.Bool => Boolean ? "true" : "false",
        ScenarioValueKind.Integer => Integer.ToString(CultureInfo.InvariantCulture),
        _ => Number.ToString("R", CultureInfo.InvariantCulture),
    };
}
