namespace Millrace.Scenarios;

/// <summary>Which JSON shape a write's value had. The tag's kind is not known until scheduling.</summary>
public enum ScenarioValueKind
{
    /// <summary>A JSON <c>true</c> or <c>false</c>.</summary>
    Bool,

    /// <summary>A JSON number that is not an integer literal: <c>1.5</c>, <c>5.0</c>, <c>1e2</c>.</summary>
    Number,

    /// <summary>A JSON integer literal: <c>7</c>.</summary>
    Integer,
}
