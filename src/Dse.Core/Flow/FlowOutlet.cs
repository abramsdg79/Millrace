namespace Dse.Core.Flow;

/// <summary>Where material leaves a node. Feeds exactly one inlet, because mass cannot fan out.</summary>
public sealed class FlowOutlet : FlowPort
{
    private FlowInlet? _target;

    public FlowOutlet(string name, string ownerId, PayloadKind kind)
        : base(name, ownerId, kind)
    {
    }

    public bool IsConnected => _target is not null;

    /// <summary>The inlet this outlet feeds, or null. Read by the flow resolver.</summary>
    internal FlowInlet? Target => _target;

    public void ConnectTo(FlowInlet inlet)
    {
        ArgumentNullException.ThrowIfNull(inlet);

        if (inlet.Kind != Kind)
        {
            throw new InvalidOperationException(
                $"Cannot connect {Kind} outlet '{QualifiedName}' to {inlet.Kind} inlet " +
                $"'{inlet.QualifiedName}'. Material changes kind only through an explicit " +
                $"component such as a former.");
        }

        if (_target is not null)
        {
            throw new InvalidOperationException(
                $"Outlet '{QualifiedName}' already feeds '{_target.QualifiedName}'. " +
                $"An outlet feeds exactly one inlet; mass cannot fan out.");
        }

        inlet.ConnectFrom(this);
        _target = inlet;
    }
}
