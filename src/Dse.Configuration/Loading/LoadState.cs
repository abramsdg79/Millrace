using System.Text.Json;
using Dse.Core;
using Dse.Core.Catalogue;
using Dse.Core.Graph;
using Dse.Core.Io;
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

    /// <summary>Ids this component's reference parameters name. Filled by the reference stage.</summary>
    public List<string> ReferencedIds { get; } = [];
}

internal sealed class ControllerEntry(
    int index,
    string id,
    BlockDescriptor descriptor,
    TimeSpan scanPeriod,
    JsonElement parameters,
    ParameterValues checkedValues,
    IReadOnlyList<string> claims)
{
    public int Index { get; } = index;

    public string Id { get; } = id;

    public BlockDescriptor Descriptor { get; } = descriptor;

    public TimeSpan ScanPeriod { get; } = scanPeriod;

    /// <summary>The <c>parameters</c> object, or <c>default</c> when the entry has none.</summary>
    public JsonElement Parameters { get; } = parameters;

    /// <summary>The parameters as the structure stage bound them, for <see cref="BlockDescriptor.OwnedTags"/> (R82).</summary>
    public ParameterValues CheckedValues { get; } = checkedValues;

    /// <summary>The <c>claims</c> array, in file order; empty when the entry has none.</summary>
    public IReadOnlyList<string> Claims { get; } = claims;

    public string Path => $"$.controllers[{Index}]";

    public string ParametersPath => $"{Path}.parameters";
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

    /// <summary>In file order, which is scan order.</summary>
    public List<ControllerEntry> Controllers { get; } = [];

    public List<LinkEntry> Signals { get; } = [];

    public List<LinkEntry> Flows { get; } = [];

    public List<TagRequest> Tags { get; } = [];

    /// <summary>Explicit tags from the file, resolved to bindings by the wire stage.</summary>
    public List<(string Name, TagBinding Binding)> Bindings { get; } = [];

    public SimulationBuilder? Builder { get; set; }

    public SimulationOptions? SimulationOptions { get; set; }

    public bool HasErrors => Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);

    public void Error(string code, string path, string message, string fix) =>
        Diagnostics.Add(ConfigDiagnostics.Error(code, path, message, fix));

    public void AddIssues(IEnumerable<BindingIssue> issues) => Diagnostics.AddRange(issues.Select(ConfigDiagnostics.From));
}
