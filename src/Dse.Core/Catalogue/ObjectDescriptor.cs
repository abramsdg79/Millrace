namespace Dse.Core.Catalogue;

/// <summary>A nested, typed value a component takes — a transform, a hold condition.</summary>
public sealed class ObjectDescriptor
{
    public ObjectDescriptor(string slot, string type, string description, Func<ParameterValues, object> factory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slot);
        ArgumentNullException.ThrowIfNull(type);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ArgumentNullException.ThrowIfNull(factory);
        Slot = slot;
        Type = type;
        Description = description;
        Factory = factory;
    }

    public string Slot { get; }

    /// <summary>Kebab-case, unique within the slot.</summary>
    public string Type { get; }

    public string Description { get; }

    public Func<ParameterValues, object> Factory { get; }

    public IReadOnlyList<ParameterDescriptor> Parameters { get; init; } = [];
}
