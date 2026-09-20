using System.Text.RegularExpressions;

namespace Dse.Core.Catalogue;

/// <summary>Collects descriptors, module by module, and rejects clashes as they are added.</summary>
public sealed partial class CatalogueBuilder
{
    internal const string Direct = "(direct)";

    private readonly Dictionary<string, (ComponentDescriptor Descriptor, string Module)> _components = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Slot, string Type), (ObjectDescriptor Descriptor, string Module)> _objects = [];
    private readonly Dictionary<string, (MaterialDescriptor Descriptor, string Module)> _materials = new(StringComparer.Ordinal);
    private readonly List<string> _modules = [];
    private string _current = Direct;

    public CatalogueBuilder Add<TModule>()
        where TModule : ICatalogueModule, new() => Add(new TModule());

    public CatalogueBuilder Add(ICatalogueModule module)
    {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentException.ThrowIfNullOrWhiteSpace(module.Name, nameof(module));
        if (_modules.Contains(module.Name, StringComparer.Ordinal))
        {
            throw new InvalidOperationException($"Module '{module.Name}' has already been added to this catalogue.");
        }

        _modules.Add(module.Name);
        string previous = _current;
        _current = module.Name;
        try
        {
            module.Register(this);
        }
        finally
        {
            _current = previous;
        }

        return this;
    }

    public CatalogueBuilder Add(ComponentDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        RequireKebabCase(descriptor.Type, "Component type");
        RequireUniqueParameters(descriptor.Parameters, $"component '{descriptor.Type}'");
        RequireMaterialStateNamesSibling(descriptor.Parameters, $"component '{descriptor.Type}'");
        RequireUniquePortNames(descriptor);
        if (_components.TryGetValue(descriptor.Type, out var existing))
        {
            throw Duplicate("Component type", descriptor.Type, existing.Module);
        }

        _components[descriptor.Type] = (descriptor, _current);
        return this;
    }

    public CatalogueBuilder Add(ObjectDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        RequireKebabCase(descriptor.Type, $"Object type in slot '{descriptor.Slot}'");
        RequireUniqueParameters(descriptor.Parameters, $"{descriptor.Slot} '{descriptor.Type}'");
        RequireMaterialStateNamesSibling(descriptor.Parameters, $"{descriptor.Slot} '{descriptor.Type}'");
        if (descriptor.Parameters.Any(p => string.Equals(p.Name, "type", StringComparison.Ordinal)))
        {
            throw new ArgumentException(
                $"{descriptor.Slot} '{descriptor.Type}' declares a parameter named 'type', which is the key that selects it. Rename the parameter.",
                nameof(descriptor));
        }

        if (_objects.TryGetValue((descriptor.Slot, descriptor.Type), out var existing))
        {
            throw Duplicate($"Object type in slot '{descriptor.Slot}'", descriptor.Type, existing.Module);
        }

        _objects[(descriptor.Slot, descriptor.Type)] = (descriptor, _current);
        return this;
    }

    public CatalogueBuilder Add(MaterialDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        string name = descriptor.Material.Name;
        if (_materials.TryGetValue(name, out var existing))
        {
            throw Duplicate("Material", name, existing.Module);
        }

        _materials[name] = (descriptor, _current);
        return this;
    }

    public ComponentCatalogue Build() => new(
        _components.Values.OrderBy(e => e.Descriptor.Type, StringComparer.Ordinal).ToList(),
        _objects.Values
            .OrderBy(e => e.Descriptor.Slot, StringComparer.Ordinal)
            .ThenBy(e => e.Descriptor.Type, StringComparer.Ordinal)
            .ToList(),
        _materials.Values.OrderBy(e => e.Descriptor.Material.Name, StringComparer.Ordinal).ToList(),
        _modules.ToList());

    private InvalidOperationException Duplicate(string what, string name, string firstModule) => new(
        $"{what} '{name}' is registered twice: by module '{firstModule}' and by module '{_current}'. " +
        $"Rename one of them; type names are unique across a catalogue.");

    private static void RequireKebabCase(string type, string what)
    {
        if (!KebabCase().IsMatch(type))
        {
            throw new ArgumentException(
                $"{what} '{type}' must be kebab-case: lowercase letters and digits in groups joined by single hyphens.",
                nameof(type));
        }
    }

    private static void RequireUniqueParameters(IReadOnlyList<ParameterDescriptor> parameters, string owner)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (ParameterDescriptor parameter in parameters)
        {
            if (!seen.Add(parameter.Name))
            {
                throw new ArgumentException($"Parameter '{parameter.Name}' is declared twice on {owner}.", nameof(parameters));
            }

            if (parameter.Children.Count > 0)
            {
                RequireUniqueParameters(parameter.Children, $"{owner}, group '{parameter.Name}'");
            }
        }
    }

    private static void RequireMaterialStateNamesSibling(IReadOnlyList<ParameterDescriptor> parameters, string owner)
    {
        List<string> materials = parameters
            .Where(p => p.Kind == ParameterKind.Material)
            .Select(p => p.Name)
            .ToList();

        foreach (ParameterDescriptor parameter in parameters)
        {
            if (parameter.Kind == ParameterKind.MaterialState
                && !materials.Contains(parameter.MaterialParameter, StringComparer.Ordinal))
            {
                string declared = materials.Count == 0
                    ? "none"
                    : string.Join(", ", materials.Order(StringComparer.Ordinal));
                throw new ArgumentException(
                    $"Parameter '{parameter.Name}' on {owner} is a material state of '{parameter.MaterialParameter}', " +
                    $"but no material parameter of that name is declared beside it. Declared material parameters: {declared}.",
                    nameof(parameters));
            }

            if (parameter.Children.Count > 0)
            {
                RequireMaterialStateNamesSibling(parameter.Children, $"{owner}, group '{parameter.Name}'");
            }
        }
    }

    private static void RequireUniquePortNames(ComponentDescriptor descriptor)
    {
        var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string name in descriptor.Ports.Select(p => p.Name).Concat(descriptor.FlowPorts.Select(p => p.Name)))
        {
            if (seen.TryGetValue(name, out string? first))
            {
                throw new ArgumentException(
                    $"Component type '{descriptor.Type}' declares ports '{first}' and '{name}', which differ only in case. " +
                    $"A plant file matches port names ignoring case, so they must be distinct.",
                    nameof(descriptor));
            }

            seen[name] = name;
        }
    }

    [GeneratedRegex("^[a-z][a-z0-9]*(-[a-z0-9]+)*$")]
    private static partial Regex KebabCase();
}
