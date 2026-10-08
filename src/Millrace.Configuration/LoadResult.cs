using Millrace.Core;
using Millrace.Core.Graph;
using Millrace.Core.Time;

namespace Millrace.Configuration;

/// <summary>
/// The outcome of loading a plant file. When <see cref="IsValid"/>, the
/// <see cref="Builder"/> has every component added, wired and validated and is
/// ready for <c>Build()</c>; it is returned unbuilt so a caller can still decide.
/// </summary>
public sealed class LoadResult
{
    internal LoadResult(
        IReadOnlyList<ConfigDiagnostic> diagnostics,
        SimulationBuilder? builder,
        SimulationOptions? options,
        PlantSummary? summary,
        IReadOnlyDictionary<string, ISimNode> nodes)
    {
        Diagnostics = diagnostics;
        Builder = builder;
        Options = options;
        Summary = summary;
        Nodes = nodes;
    }

    public IReadOnlyList<ConfigDiagnostic> Diagnostics { get; }

    public bool IsValid => Diagnostics.All(d => d.Severity != DiagnosticSeverity.Error);

    public SimulationBuilder? Builder { get; }

    /// <summary>The options the builder was made with: the file's defaults under any <see cref="LoadOptions"/>.</summary>
    public SimulationOptions? Options { get; }

    public PlantSummary? Summary { get; }

    /// <summary>The top-level components by id. Empty unless the plant is valid.</summary>
    public IReadOnlyDictionary<string, ISimNode> Nodes { get; }

    /// <summary>Every diagnostic, blank-line separated, ending in a newline; empty when there are none.</summary>
    public string ToText() =>
        Diagnostics.Count == 0 ? string.Empty : string.Join("\n\n", Diagnostics.Select(d => d.ToText())) + "\n";
}
