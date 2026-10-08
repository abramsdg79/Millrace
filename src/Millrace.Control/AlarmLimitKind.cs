namespace Millrace.Control;

/// <summary>
/// The four limits an analog alarm may carry, declared in ascending order so
/// that sorting by this enum sorts by value: a configured set must satisfy
/// <c>LoLo &lt; Lo &lt; Hi &lt; HiHi</c>.
/// </summary>
public enum AlarmLimitKind
{
    /// <summary>The lower trip.</summary>
    LoLo,

    /// <summary>The lower warning.</summary>
    Lo,

    /// <summary>The upper warning.</summary>
    Hi,

    /// <summary>The upper trip.</summary>
    HiHi,
}
