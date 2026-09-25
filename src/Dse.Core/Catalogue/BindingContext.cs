using System.Diagnostics.CodeAnalysis;
using Dse.Core.Graph;
using Dse.Io;

namespace Dse.Core.Catalogue;

/// <summary>
/// What names mean while parameters are bound: the catalogue, the materials in
/// scope (the catalogue's plus any added), and the nodes built so far.
/// </summary>
public sealed class BindingContext
{
    private readonly Dictionary<string, MaterialDescriptor> _materials = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ISimNode> _nodes = new(StringComparer.Ordinal);
    private Dictionary<string, (TagKind Kind, TagAccess Access)>? _tags;

    public BindingContext(ComponentCatalogue catalogue)
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        Catalogue = catalogue;
        foreach (MaterialDescriptor material in catalogue.Materials)
        {
            _materials[material.Material.Name] = material;
        }
    }

    public ComponentCatalogue Catalogue { get; }

    public IReadOnlyList<string> MaterialNames => _materials.Keys.Order(StringComparer.Ordinal).ToList();

    public IReadOnlyList<string> NodeIds => _nodes.Keys.Order(StringComparer.Ordinal).ToList();

    /// <summary>True once <see cref="UseTags"/> was called: tag parameters then resolve in construct mode.</summary>
    public bool ResolvesTags => _tags is not null;

    /// <summary>Every tag name in the table, sorted; empty when there is no table.</summary>
    public IReadOnlyList<string> TagNames =>
        _tags is null ? [] : _tags.Keys.Order(StringComparer.Ordinal).ToList();

    public BindingContext AddMaterial(MaterialDescriptor material)
    {
        ArgumentNullException.ThrowIfNull(material);
        if (!_materials.TryAdd(material.Material.Name, material))
        {
            throw new InvalidOperationException(
                $"Material '{material.Material.Name}' is already defined. Material names are unique across the catalogue and the plant.");
        }

        return this;
    }

    public BindingContext AddNode(ISimNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (!_nodes.TryAdd(node.Id, node))
        {
            throw new InvalidOperationException($"A node with id '{node.Id}' has already been added.");
        }

        return this;
    }

    /// <summary>
    /// The tags a <see cref="ParameterKind.Tag"/> parameter may name. Called once;
    /// a name given twice keeps its first entry — a plant tag before a block's
    /// owned tag — and the builder reports the clash (DSE015).
    /// </summary>
    public BindingContext UseTags(IEnumerable<(string Name, TagKind Kind, TagAccess Access)> tags)
    {
        ArgumentNullException.ThrowIfNull(tags);
        if (_tags is not null)
        {
            throw new InvalidOperationException("This context already has a tag table.");
        }

        _tags = new Dictionary<string, (TagKind Kind, TagAccess Access)>(StringComparer.Ordinal);
        foreach ((string name, TagKind kind, TagAccess access) in tags)
        {
            _tags.TryAdd(name, (kind, access));
        }

        return this;
    }

    public bool TryGetTag(string name, out TagKind kind, out TagAccess access)
    {
        if (_tags is not null && _tags.TryGetValue(name, out var entry))
        {
            (kind, access) = entry;
            return true;
        }

        kind = default;
        access = default;
        return false;
    }

    public bool TryGetMaterial(string name, [NotNullWhen(true)] out MaterialDescriptor? material) =>
        _materials.TryGetValue(name, out material);

    public bool TryGetNode(string id, [NotNullWhen(true)] out ISimNode? node) => _nodes.TryGetValue(id, out node);
}
