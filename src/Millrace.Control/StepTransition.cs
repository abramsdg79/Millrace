using Millrace.Io;

namespace Millrace.Control;

/// <summary>
/// What ends a step: a comparison against a plant tag, or a delay on the step
/// clock. There is no third kind.
/// </summary>
public sealed class StepTransition
{
    private StepTransition(string? tag, PredicateOperator op, TagValue value, TimeSpan delay)
    {
        Tag = tag;
        Operator = op;
        Value = value;
        Delay = delay;
    }

    /// <summary>The tag compared, or null for a timed transition.</summary>
    public string? Tag { get; }

    /// <summary>How the tag is compared. Meaningless for a timed transition.</summary>
    public PredicateOperator Operator { get; }

    /// <summary>What the tag is compared with. Its kind is the kind the pin declares.</summary>
    public TagValue Value { get; }

    /// <summary>How long the step runs. Meaningless for a predicate transition.</summary>
    public TimeSpan Delay { get; }

    /// <summary>True when this is a delay rather than a comparison.</summary>
    public bool IsTimed => Tag is null;

    /// <summary>The step ends when <paramref name="tag"/> compares as asked.</summary>
    public static StepTransition When(string tag, PredicateOperator op, TagValue value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);

        if (!Enum.IsDefined(op))
        {
            throw new ArgumentOutOfRangeException(nameof(op), op, "That is not a comparison operator.");
        }

        if (value.Kind == TagKind.Bool && op is not (PredicateOperator.Equal or PredicateOperator.NotEqual))
        {
            throw new ArgumentException(
                $"A Bool tag cannot be compared with {op}. Use Equal or NotEqual.", nameof(op));
        }

        return new StepTransition(tag, op, value, TimeSpan.Zero);
    }

    /// <summary>The step ends when its clock reaches <paramref name="delay"/>.</summary>
    public static StepTransition After(TimeSpan delay)
    {
        if (delay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(delay), delay, "A delay must not be negative.");
        }

        return new StepTransition(null, PredicateOperator.Equal, TagValue.Bool(false), delay);
    }
}
