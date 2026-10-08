namespace Millrace.Realtime;

/// <summary>What the command bus decided about a write (spec 10.6).</summary>
public enum CommandOutcome
{
    /// <summary>Queued for phase 1 of the next tick.</summary>
    Accepted,

    /// <summary>No tag of that name.</summary>
    UnknownTag,

    /// <summary>The value's kind does not match the tag's.</summary>
    KindMismatch,

    /// <summary>The tag does not accept writes.</summary>
    ReadOnly,

    /// <summary>A double outside the tag's declared range, or not finite.</summary>
    OutOfRange,
}
