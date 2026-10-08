using Millrace.Core.Faults;
using Millrace.Core.Graph;

namespace Millrace.Core.Catalogue;

/// <summary>
/// Everything a tool or an agent needs to know about a component type, plus
/// the factory that builds one from parsed parameters. Hand-written, beside
/// the constructor it must match; <c>CatalogueConformance</c> keeps it honest.
/// </summary>
public sealed class ComponentDescriptor
{
    public ComponentDescriptor(
        string type,
        ComponentCategory category,
        string description,
        Func<string, ParameterValues, ISimNode> factory)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ArgumentNullException.ThrowIfNull(factory);
        Type = type;
        Category = category;
        Description = description;
        Factory = factory;
    }

    /// <summary>Kebab-case, unique among components: <c>belt-scale</c>.</summary>
    public string Type { get; }

    public ComponentCategory Category { get; }

    public string Description { get; }

    public Func<string, ParameterValues, ISimNode> Factory { get; }

    public IReadOnlyList<ParameterDescriptor> Parameters { get; init; } = [];

    public IReadOnlyList<PortDescriptor> Ports { get; init; } = [];

    public IReadOnlyList<FlowPortDescriptor> FlowPorts { get; init; } = [];

    public IReadOnlyList<FaultDescriptor> Faults { get; init; } = [];

    public IReadOnlyList<TelemetryKey> Telemetry { get; init; } = [];

    public IReadOnlyList<TagEntry> Tags { get; init; } = [];

    /// <summary>Capabilities an instance supplies to a <see cref="ParameterKind.Reference"/>, directly or as a provider.</summary>
    public IReadOnlyList<Type> Provides { get; init; } = [];
}
