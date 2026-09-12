using Dse.Core.Contexts;

namespace Dse.Core.Graph;

public abstract class ComponentBase : ISimComponent, IQualifiable
{
    private readonly List<Port> _ports = [];
    private readonly List<Port> _latched = [];

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
        if (port.IsLatchedInput)
        {
            _latched.Add(port);
        }

        return port;
    }

    protected InputPort<T> AddInput<T>(string name, T defaultValue = default, bool required = false, bool latched = false)
        where T : unmanaged =>
        AddPort(new InputPort<T>(name, Id, defaultValue, required, latched));

    protected OutputPort<T> AddOutput<T>(string name)
        where T : unmanaged =>
        AddPort(new OutputPort<T>(name, Id));

    public virtual void Initialize(in InitContext ctx)
    {
    }

    public abstract void Evaluate(in TickContext ctx);

    /// <summary>
    /// The latch pass: every latched input captures this tick's source value,
    /// then <see cref="OnLatch"/> runs. Sealed so a subclass cannot forget the
    /// capture; override <see cref="OnLatch"/> for component-specific state.
    /// </summary>
    public void Latch()
    {
        for (int i = 0; i < _latched.Count; i++)
        {
            _latched[i].Capture();
        }

        OnLatch();
    }

    /// <summary>Called once per tick after every component has evaluated and this component's latched inputs have captured.</summary>
    protected virtual void OnLatch()
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
