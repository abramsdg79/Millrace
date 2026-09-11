namespace Dse.Core.Graph;

/// <summary>
/// A container of components. Composites never evaluate: at build time the tree
/// is flattened to leaves, so a conveyor is genuinely a composition rather than
/// a special case in the engine.
/// </summary>
public abstract class CompositeComponent : ISimNode, IQualifiable
{
    private readonly List<ISimNode> _children = [];
    private readonly Dictionary<string, Port> _aliases = new(StringComparer.Ordinal);

    protected CompositeComponent(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        Id = id;
    }

    public string Id { get; private set; }

    /// <summary>Adds a child and prefixes its id (and its descendants' ids) with this composite's id.</summary>
    protected TChild AddChild<TChild>(TChild child)
        where TChild : ISimNode
    {
        ArgumentNullException.ThrowIfNull(child);

        ((IQualifiable)child).Qualify(Id);
        _children.Add(child);
        return child;
    }

    /// <summary>Publishes a child's port under a name on this composite.</summary>
    protected void Expose(string alias, Port port)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(alias);
        ArgumentNullException.ThrowIfNull(port);

        if (!_aliases.TryAdd(alias, port))
        {
            throw new InvalidOperationException(
                $"Composite '{Id}' already exposes a port named '{alias}'.");
        }
    }

    public InputPort<T> Input<T>(string alias)
        where T : unmanaged => Resolve<InputPort<T>>(alias);

    public OutputPort<T> Output<T>(string alias)
        where T : unmanaged => Resolve<OutputPort<T>>(alias);

    /// <summary>Every evaluatable component beneath this one, depth-first in declaration order.</summary>
    internal IEnumerable<ISimComponent> Leaves()
    {
        foreach (ISimNode child in _children)
        {
            switch (child)
            {
                case ISimComponent component:
                    yield return component;
                    break;
                case CompositeComponent composite:
                    foreach (ISimComponent leaf in composite.Leaves())
                    {
                        yield return leaf;
                    }

                    break;
            }
        }
    }

    private TPort Resolve<TPort>(string alias)
        where TPort : Port
    {
        if (!_aliases.TryGetValue(alias, out Port? port))
        {
            string available = string.Join(", ", _aliases.Keys.Order(StringComparer.Ordinal));
            throw new KeyNotFoundException(
                $"Composite '{Id}' exposes no port named '{alias}'. Available: {available}.");
        }

        if (port is not TPort typed)
        {
            throw new InvalidCastException(
                $"Port '{alias}' on composite '{Id}' is a {port.GetType().Name}, not a {typeof(TPort).Name}.");
        }

        return typed;
    }

    void IQualifiable.Qualify(string prefix)
    {
        Id = $"{prefix}.{Id}";
        foreach (ISimNode child in _children)
        {
            ((IQualifiable)child).Qualify(prefix);
        }
    }
}
