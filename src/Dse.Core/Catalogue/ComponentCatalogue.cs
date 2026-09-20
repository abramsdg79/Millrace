using System.Diagnostics.CodeAnalysis;

namespace Dse.Core.Catalogue;

/// <summary>An immutable set of descriptors. Every list is sorted, so anything generated from it is deterministic.</summary>
public sealed class ComponentCatalogue
{
    private readonly Dictionary<string, (ComponentDescriptor Descriptor, string Module)> _components;
    private readonly Dictionary<(string Slot, string Type), (ObjectDescriptor Descriptor, string Module)> _objects;
    private readonly Dictionary<string, (MaterialDescriptor Descriptor, string Module)> _materials;

    internal ComponentCatalogue(
        List<(ComponentDescriptor Descriptor, string Module)> components,
        List<(ObjectDescriptor Descriptor, string Module)> objects,
        List<(MaterialDescriptor Descriptor, string Module)> materials,
        List<string> modules)
    {
        Components = components.Select(e => e.Descriptor).ToList();
        Objects = objects.Select(e => e.Descriptor).ToList();
        Materials = materials.Select(e => e.Descriptor).ToList();
        Modules = modules;
        Slots = Objects.Select(o => o.Slot).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        _components = components.ToDictionary(e => e.Descriptor.Type, StringComparer.Ordinal);
        _objects = objects.ToDictionary(e => (e.Descriptor.Slot, e.Descriptor.Type));
        _materials = materials.ToDictionary(e => e.Descriptor.Material.Name, StringComparer.Ordinal);
    }

    /// <summary>Sorted by type name.</summary>
    public IReadOnlyList<ComponentDescriptor> Components { get; }

    /// <summary>Sorted by slot, then type name.</summary>
    public IReadOnlyList<ObjectDescriptor> Objects { get; }

    /// <summary>Sorted by material name.</summary>
    public IReadOnlyList<MaterialDescriptor> Materials { get; }

    /// <summary>In the order they were added.</summary>
    public IReadOnlyList<string> Modules { get; }

    /// <summary>Every slot that has at least one object, sorted.</summary>
    public IReadOnlyList<string> Slots { get; }

    public bool TryGetComponent(string type, [NotNullWhen(true)] out ComponentDescriptor? descriptor)
    {
        bool found = _components.TryGetValue(type, out var entry);
        descriptor = found ? entry.Descriptor : null;
        return found;
    }

    public bool TryGetObject(string slot, string type, [NotNullWhen(true)] out ObjectDescriptor? descriptor)
    {
        bool found = _objects.TryGetValue((slot, type), out var entry);
        descriptor = found ? entry.Descriptor : null;
        return found;
    }

    public IReadOnlyList<ObjectDescriptor> ObjectsIn(string slot) =>
        Objects.Where(o => string.Equals(o.Slot, slot, StringComparison.Ordinal)).ToList();

    public bool TryGetMaterial(string name, [NotNullWhen(true)] out MaterialDescriptor? descriptor)
    {
        bool found = _materials.TryGetValue(name, out var entry);
        descriptor = found ? entry.Descriptor : null;
        return found;
    }

    /// <summary>The module that registered a descriptor, or <c>(direct)</c>.</summary>
    public string ModuleOf(ComponentDescriptor descriptor) => _components[descriptor.Type].Module;

    /// <inheritdoc cref="ModuleOf(ComponentDescriptor)"/>
    public string ModuleOf(ObjectDescriptor descriptor) => _objects[(descriptor.Slot, descriptor.Type)].Module;

    /// <inheritdoc cref="ModuleOf(ComponentDescriptor)"/>
    public string ModuleOf(MaterialDescriptor descriptor) => _materials[descriptor.Material.Name].Module;
}
