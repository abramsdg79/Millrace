using System.Diagnostics.CodeAnalysis;
using Dse.Core.Graph;

namespace Dse.Core.Catalogue;

/// <summary>
/// What names mean while parameters are bound: the catalogue, the materials in
/// scope (the catalogue's plus any added), and the nodes built so far.
/// </summary>
public sealed class BindingContext
{
    private readonly Dictionary<string, MaterialDescriptor> _materials = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ISimNode> _nodes = new(StringComparer.Ordinal);

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

    public bool TryGetMaterial(string name, [NotNullWhen(true)] out MaterialDescriptor? material) =>
        _materials.TryGetValue(name, out material);

    public bool TryGetNode(string id, [NotNullWhen(true)] out ISimNode? node) => _nodes.TryGetValue(id, out node);
}
