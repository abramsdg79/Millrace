using Millrace.Core.Faults;
using Millrace.Io;

namespace Millrace.Core;

/// <summary>
/// Sees every external action that took effect, stamped with the tick it took
/// effect on, whichever path it came by: a scenario, a command bus, or test code.
/// A write a control block issued is not one: replaying the external actions
/// re-runs the block, which issues it again. This is the seam a recording is made
/// through; what it is called from is the three places an action lands, so no
/// external action can slip past it.
/// </summary>
/// <remarks>
/// Not to be confused with <c>Millrace.Realtime.ICommandRecorder</c>, which also sees
/// commands the bus <em>rejected</em>. That is an audit trail; this is a replay.
/// Called on the tick thread only, during phase 1: not safe to call from another thread.
/// </remarks>
public interface IActionRecorder
{
    /// <summary>An external value reached a tag, queued or scheduled. Never called for a block's own write.</summary>
    void Wrote(long tick, string tag, TagValue value);

    /// <summary>A fault was applied. <paramref name="arguments"/> is the resolved set: every declared parameter, defaults filled in.</summary>
    void Faulted(long tick, string componentId, string faultId, FaultArguments arguments);

    /// <summary>A fault was cleared.</summary>
    void Cleared(long tick, string componentId, string faultId);
}
