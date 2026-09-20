using System.Text.Json;
using Dse.Core;
using Dse.Core.Catalogue;
using Dse.Core.Graph;
using Dse.Core.Time;

namespace Dse.Configuration.Loading;

internal sealed class ComponentEntry(int index, string id, ComponentDescriptor descriptor, JsonElement parameters)
{
    public int Index { get; } = index;

    public string Id { get; } = id;

    public ComponentDescriptor Descriptor { get; } = descriptor;

    /// <summary>The <c>parameters</c> object, or <c>default</c> when the entry has none.</summary>
    public JsonElement Parameters { get; } = parameters;

    public string Path => $"$.components[{Index}]";

    public string ParametersPath => $"{Path}.parameters";

    public ISimNode? Node { get; set; }
}

internal sealed record LinkEntry(string From, string To, string Path);

internal sealed record TagRequest(
    string Name, string Port, bool Writable, string Unit, double RangeLow, double RangeHigh, string Description, string Path);

internal sealed record PlantDefaults(ulong? Seed, TimeSpan? TimeStep, DateTimeOffset? StartTime);

/// <summary>Everything the stages share. One per <c>Load</c>.</summary>
internal sealed class LoadState(ComponentCatalogue catalogue, LoadOptions options)
{
    public ComponentCatalogue Catalogue { get; } = catalogue;

    public LoadOptions Options { get; } = options;

    public BindingContext Context { get; } = new(catalogue);

    public List<ConfigDiagnostic> Diagnostics { get; } = [];

    public JsonElement Root { get; set; }

    public PlantDefaults Defaults { get; set; } = new(null, null, null);

    public List<ComponentEntry> Components { get; } = [];

    /// <summary>Set by the reference stage: <see cref="Components"/> in an order that builds referents first.</summary>
    public List<ComponentEntry> BuildOrder { get; set; } = [];

    public List<LinkEntry> Signals { get; } = [];

    public List<LinkEntry> Flows { get; } = [];

    public List<TagRequest> Tags { get; } = [];

    public SimulationBuilder? Builder { get; set; }

    public SimulationOptions? SimulationOptions { get; set; }

    public bool HasErrors => Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);

    public void Error(string code, string path, string message, string fix) =>
        Diagnostics.Add(ConfigDiagnostics.Error(code, path, message, fix));

    public void AddIssues(IEnumerable<BindingIssue> issues) => Diagnostics.AddRange(issues.Select(ConfigDiagnostics.From));
}
