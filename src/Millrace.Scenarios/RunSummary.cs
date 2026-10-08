namespace Millrace.Scenarios;

/// <summary>What a run came to, in three numbers.</summary>
/// <param name="Ticks">Ticks executed: the duration divided by the time step.</param>
/// <param name="Events">Records in the event log.</param>
/// <param name="ActionsScheduled">Actions the scenario declared, all of which bound.</param>
public sealed record RunSummary(long Ticks, int Events, int ActionsScheduled);
