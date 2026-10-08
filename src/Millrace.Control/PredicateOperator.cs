namespace Millrace.Control;

/// <summary>How a step's predicate compares a tag with a value.</summary>
public enum PredicateOperator
{
    /// <summary><c>==</c>. The only operator, with <see cref="NotEqual"/>, that a Bool tag allows.</summary>
    Equal,

    /// <summary><c>!=</c>.</summary>
    NotEqual,

    /// <summary><c>&lt;</c>.</summary>
    Less,

    /// <summary><c>&lt;=</c>.</summary>
    LessOrEqual,

    /// <summary><c>&gt;</c>.</summary>
    Greater,

    /// <summary><c>&gt;=</c>.</summary>
    GreaterOrEqual,
}
