using Dse.Configuration;
using Dse.Core;

namespace Dse.Scenarios;

/// <summary>
/// A scenario bound to its plant and not yet run: the built simulation with
/// every action scheduled, or every reason it could not be. Never both.
/// </summary>
public sealed class ScenarioBinding
{
    internal ScenarioBinding(IReadOnlyList<ConfigDiagnostic> diagnostics, Simulation? simulation)
    {
        Diagnostics = diagnostics;
        Simulation = simulation;
    }

    /// <summary>Every problem found, in the order <see cref="ScenarioRunner.Run"/> reports them.</summary>
    public IReadOnlyList<ConfigDiagnostic> Diagnostics { get; }

    /// <summary>The simulation at tick 0 with the timeline scheduled, or null when the scenario did not bind.</summary>
    public Simulation? Simulation { get; }

    /// <summary>True when nothing of error severity was reported.</summary>
    public bool IsValid => Diagnostics.All(d => d.Severity != DiagnosticSeverity.Error);

    /// <summary>Every diagnostic, blank-line separated, ending in a newline; empty when there are none.</summary>
    public string ToText() =>
        Diagnostics.Count == 0 ? string.Empty : string.Join("\n\n", Diagnostics.Select(d => d.ToText())) + "\n";
}
