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

    protected InputPort<T> AddInput<T>(string name, T defaultValue = default, bool required = false)
        where T : unmanaged
    {
        var port = new InputPort<T>(name, Id, defaultValue, required);
        _ports.Add(port);
        return port;
    }

    protected OutputPort<T> AddOutput<T>(string name)
        where T : unmanaged
    {
        var port = new OutputPort<T>(name, Id);
        _ports.Add(port);
        return port;
    }

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
