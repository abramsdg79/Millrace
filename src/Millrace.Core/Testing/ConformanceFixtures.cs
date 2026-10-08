using Millrace.Core.Catalogue;
using Millrace.Core.Graph;

namespace Millrace.Core.Testing;

/// <summary>
/// What conformance needs beyond defaults to build a probe of each type: JSON for
/// required parameters, materials to name, and stand-in nodes to reference.
/// </summary>
public sealed class ConformanceFixtures
{
    private readonly Dictionary<string, string> _components = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Slot, string Type), string> _objects = [];
    private readonly Dictionary<string, List<string>> _blocks = new(StringComparer.Ordinal);
    private readonly List<MaterialDescriptor> _materials = [];
    private readonly List<ISimNode> _nodes = [];

    /// <summary>The <c>parameters</c> object for a probe of <paramref name="componentType"/>.</summary>
    public ConformanceFixtures Parameters(string componentType, string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(componentType);
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        _components[componentType] = json;
        return this;
    }

    /// <summary>The parameters (without <c>"type"</c>) for a probe of an object.</summary>
    public ConformanceFixtures ObjectParameters(string slot, string type, string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slot);
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        _objects[(slot, type)] = json;
        return this;
    }

    /// <summary>
    /// A <c>parameters</c> object for a probe of a block type. Call it again for
    /// the same type to check it with several — an alarm with one limit and with four.
    /// </summary>
    public ConformanceFixtures BlockParameters(string blockType, string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(blockType);
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        if (!_blocks.TryGetValue(blockType, out List<string>? list))
        {
            list = [];
            _blocks[blockType] = list;
        }

        list.Add(json);
        return this;
    }

    public ConformanceFixtures Material(MaterialDescriptor material)
    {
        ArgumentNullException.ThrowIfNull(material);
        _materials.Add(material);
        return this;
    }

    /// <summary>A stand-in a fixture may reference by its id.</summary>
    public ConformanceFixtures Node(ISimNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        _nodes.Add(node);
        return this;
    }

    internal string ParametersFor(string componentType) =>
        _components.TryGetValue(componentType, out string? json) ? json : "{}";

    internal string ParametersFor(string slot, string type) =>
        _objects.TryGetValue((slot, type), out string? json) ? json : "{}";

    internal IReadOnlyList<string> BlockParametersFor(string blockType) =>
        _blocks.TryGetValue(blockType, out List<string>? list) ? list : ["{}"];

    internal BindingContext NewContext(ComponentCatalogue catalogue)
    {
        var context = new BindingContext(catalogue);
        foreach (MaterialDescriptor material in _materials)
        {
            context.AddMaterial(material);
        }

        foreach (ISimNode node in _nodes)
        {
            context.AddNode(node);
        }

        return context;
    }
}
