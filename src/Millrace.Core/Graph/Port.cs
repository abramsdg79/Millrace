namespace Millrace.Core.Graph;

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

    /// <summary>
    /// Whether the resolver should order this port's owner after the port's
    /// source. False for outputs, for flow ports, and for latched inputs, which
    /// read one tick late by design.
    /// </summary>
    internal virtual bool CreatesOrderingEdge => SourcePort is not null;

    /// <summary>Called in the latch pass. Latched inputs capture their source here.</summary>
    internal virtual void Capture()
    {
    }

    /// <summary>True for inputs that capture in the latch pass.</summary>
    internal virtual bool IsLatchedInput => false;

    /// <summary>True once the plant containing this port has been built; wiring is then immutable.</summary>
    internal bool IsFrozen { get; private set; }

    /// <summary>Marks the port immutable. Called by <see cref="Millrace.Core.SimulationBuilder.Build"/> for every port.</summary>
    internal void Freeze() => IsFrozen = true;

    /// <summary>
    /// Connects <paramref name="source"/> to this port when this is an input of
    /// the same value type. False means "not compatible"; an incompatible
    /// <em>state</em> (already driven, frozen) throws as <c>ConnectFrom</c> does.
    /// </summary>
    internal virtual bool TryConnectFrom(Port source) => false;

    /// <summary>The CLR type this port carries, or null for a flow port.</summary>
    internal virtual Type? ValueType => null;

    /// <summary>True for an <c>InputPort&lt;T&gt;</c>. Flow ports answer false; ask their type instead.</summary>
    internal virtual bool IsInput => false;

    /// <summary>True for an input declared <c>required</c>. Read by catalogue conformance.</summary>
    internal virtual bool IsRequiredInput => false;
}
