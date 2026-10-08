using Millrace.Core.Faults;

namespace Millrace.Scenarios;

/// <summary>
/// One thing a scenario does, and when. <c>At</c> is the tick at which the plant
/// sees it, for every shape alike — that is the whole point of
/// <c>Simulation.WriteAt</c>.
/// </summary>
/// <param name="At">Time from the start of the run.</param>
public abstract record ScenarioAction(TimeSpan At);

/// <summary>A value reaches a tag.</summary>
public sealed record WriteAction(TimeSpan At, string Tag, ScenarioValue Value) : ScenarioAction(At);

/// <summary>A fault is injected into a component. <paramref name="Arguments"/> may omit any the descriptor defaults.</summary>
public sealed record FaultAction(
    TimeSpan At,
    string ComponentId,
    string FaultId,
    IReadOnlyList<FaultArgument> Arguments) : ScenarioAction(At);

/// <summary>A fault is cleared.</summary>
public sealed record ClearAction(TimeSpan At, string ComponentId, string FaultId) : ScenarioAction(At);
