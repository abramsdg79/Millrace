using Dse.Core.Contexts;

namespace Dse.Core.Graph;

public abstract class ComponentBase : ISimComponent, IQualifiable
{
    private readonly List<Port> _ports = [];

    protected ComponentBase(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        Id = id;
    }

    public string Id { get; private set; }

    public IReadOnlyList<Port> Ports => _ports;

    public virtual bool HasDirectFeedthrough => true;

    /// <summary>
    /// Registers a port on this component. Ports are listed in registration
    /// order and their owner id is rewritten when a composite qualifies this
    /// component, so every port — signal or flow — must go through here.
    /// </summary>
    protected TPort AddPort<TPort>(TPort port)
        where TPort : Port
    {
        ArgumentNullException.ThrowIfNull(port);
        if (!string.Equals(port.OwnerId, Id, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Port '{port.QualifiedName}' belongs to '{port.OwnerId}', not to '{Id}'. " +
                $"Construct ports with this component's Id.",
                nameof(port));
        }

        _ports.Add(port);
        return port;
    }

    protected InputPort<T> AddInput<T>(string name, T defaultValue = default, bool required = false)
        where T : unmanaged =>
        AddPort(new InputPort<T>(name, Id, defaultValue, required));

    protected OutputPort<T> AddOutput<T>(string name)
        where T : unmanaged =>
        AddPort(new OutputPort<T>(name, Id));

    public virtual void Initialize(in InitContext ctx)
    {
    }

    public abstract void Evaluate(in TickContext ctx);

    public virtual void Latch()
    {
    }

    void IQualifiable.Qualify(string prefix)
    {
        Id = $"{prefix}.{Id}";
        foreach (Port port in _ports)
        {
            port.OwnerId = Id;
        }
    }
}
