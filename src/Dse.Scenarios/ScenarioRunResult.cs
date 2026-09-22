using Dse.Configuration;
using Dse.Core.Logging;

namespace Dse.Scenarios;

/// <summary>
/// The outcome of running a scenario: either a log and a summary, or every
/// reason the scenario and the plant do not fit together. Never both — a
/// scenario that did not bind produces no partial log.
/// </summary>
public sealed class ScenarioRunResult
{
    internal ScenarioRunResult(IReadOnlyList<ConfigDiagnostic> diagnostics, EventLog? events, RunSummary? summary)
    {
        Diagnostics = diagnostics;
        Events = events;
        Summary = summary;
    }

    /// <summary>Every problem found, in order: the plant's, then the duration's, then each action's.</summary>
    public IReadOnlyList<ConfigDiagnostic> Diagnostics { get; }

    /// <summary>True when nothing of error severity was reported, as <c>LoadResult.IsValid</c> is.</summary>
    public bool IsValid => Diagnostics.All(d => d.Severity != DiagnosticSeverity.Error);

    /// <summary>The run's event log — the regression artifact — or null when nothing ran.</summary>
    public EventLog? Events { get; }

    /// <summary>Null when nothing ran.</summary>
    public RunSummary? Summary { get; }

    /// <summary>Every diagnostic, blank-line separated, ending in a newline; empty when there are none.</summary>
    public string ToText() =>
        Diagnostics.Count == 0 ? string.Empty : string.Join("\n\n", Diagnostics.Select(d => d.ToText())) + "\n";
}
