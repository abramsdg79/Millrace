using Dse.Configuration;

namespace Dse.Scenarios;

/// <summary>
/// What a scenario file came to: the scenario, or every structural reason it is
/// not one. The scenario is null whenever there is an error, so a caller cannot
/// half-run a broken file.
/// </summary>
public sealed class ScenarioParseResult
{
    internal ScenarioParseResult(IReadOnlyList<ConfigDiagnostic> diagnostics, Scenario? scenario)
    {
        Diagnostics = diagnostics;
        Scenario = scenario;
    }

    /// <summary>Every problem found, in a fixed order: unknown keys, then the header, then the timeline.</summary>
    public IReadOnlyList<ConfigDiagnostic> Diagnostics { get; }

    public bool IsValid => Diagnostics.All(d => d.Severity != DiagnosticSeverity.Error);

    /// <summary>The parsed scenario, or null when anything is wrong.</summary>
    public Scenario? Scenario { get; }

    /// <summary>Every diagnostic, blank-line separated, ending in a newline; empty when there are none.</summary>
    public string ToText() =>
        Diagnostics.Count == 0 ? string.Empty : string.Join("\n\n", Diagnostics.Select(d => d.ToText())) + "\n";
}
