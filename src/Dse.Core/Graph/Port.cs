namespace Dse.Core.Graph;

/// <summary>A named connection point on a component.</summary>
public abstract class Port
{
    protected Port(string name, string ownerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);

        Name = name;
        OwnerId = ownerId;
    }

    public string Name { get; }

    /// <summary>The owning component's id. Reassigned when a composite qualifies its children.</summary>
    public string OwnerId { get; internal set; }

    /// <summary>Fully qualified port name, used in validation messages.</summary>
    public string QualifiedName => $"{OwnerId}.{Name}";

    /// <summary>True when this is a required input with nothing driving it.</summary>
    public abstract bool IsMissingRequiredConnection { get; }

    /// <summary>The upstream port, or null. Used by the resolver to build edges.</summary>
    internal abstract Port? SourcePort { get; }
}
