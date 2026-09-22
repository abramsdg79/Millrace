using Dse.Core.Faults;
using Dse.Io;

namespace Dse.Core;

/// <summary>
/// Sees every action that took effect, stamped with the tick it took effect on,
/// whichever path it came by: a scenario, a command bus, or test code. This is
/// the seam a recording is made through; what it is called from is the three
/// places an action lands, so nothing can slip past it.
/// </summary>
/// <remarks>
/// Not to be confused with <c>Dse.Realtime.ICommandRecorder</c>, which also sees
/// commands the bus <em>rejected</em>. That is an audit trail; this is a replay.
/// </remarks>
public interface IActionRecorder
{
    /// <summary>A value reached a tag, queued or scheduled.</summary>
    void Wrote(long tick, string tag, TagValue value);

    /// <summary>A fault was applied. <paramref name="arguments"/> is the resolved set: every declared parameter, defaults filled in.</summary>
    void Faulted(long tick, string componentId, string faultId, FaultArguments arguments);

    /// <summary>A fault was cleared.</summary>
    void Cleared(long tick, string componentId, string faultId);
}
