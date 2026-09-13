namespace Dse.Core.Graph;

/// <summary>
/// An input. Exactly one source, because two sources is an undefined value.
/// Unconnected inputs read their declared default so partial plants still run.
/// A latched input reads the value captured at the end of the previous tick
/// and creates no ordering edge — the port-level way to declare that one tick
/// of lag on a reflected quantity is physically correct.
/// </summary>
public sealed class InputPort<T> : Port
    where T : unmanaged
{
    private OutputPort<T>? _source;
    private T _captured;
    private bool _external;
    private T _externalValue;

    public InputPort(string name, string ownerId, T defaultValue, bool isRequired, bool isLatched = false)
        : base(name, ownerId)
    {
        DefaultValue = defaultValue;
        IsRequired = isRequired;
        IsLatched = isLatched;
        _captured = defaultValue;
        _externalValue = defaultValue;
    }

    public T DefaultValue { get; }

    public bool IsRequired { get; }

    /// <summary>True when <see cref="Value"/> is the value latched at the end of the previous tick.</summary>
    public bool IsLatched { get; }

    /// <summary>
    /// True when a writable tag binding drives this input (spec 9.3). The port
    /// then reads the value most recently applied at phase 1, starting from
    /// <see cref="DefaultValue"/>.
    /// </summary>
    public bool IsExternallyDriven => _external;

    public T Value => IsLatched ? _captured : Live;

    private T Live => _source is not null ? _source.Value : _external ? _externalValue : DefaultValue;

    public override bool IsMissingRequiredConnection => IsRequired && _source is null && !_external;

    internal override Port? SourcePort => _source;

    internal override bool CreatesOrderingEdge => !IsLatched && _source is not null;

    internal override bool IsLatchedInput => IsLatched;

    /// <summary>The value the external driver last applied; equals <see cref="DefaultValue"/> until a write lands.</summary>
    internal T ExternalValue => _externalValue;

    internal override void Capture()
    {
        if (IsLatched)
        {
            _captured = Live;
        }
    }

    public void ConnectFrom(OutputPort<T> source)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (IsFrozen)
        {
            throw new InvalidOperationException(
                $"Cannot connect '{source.QualifiedName}' to '{QualifiedName}': the plant has been built " +
                $"and its wiring is immutable. Wire before calling Build().");
        }

        if (_external)
        {
            throw new InvalidOperationException(
                $"Input '{QualifiedName}' is externally driven by a writable tag; it cannot also be " +
                $"driven by '{source.QualifiedName}'. Remove the tag binding or the connection.");
        }

        if (_source is not null)
        {
            throw new InvalidOperationException(
                $"Input '{QualifiedName}' is already driven by '{_source.QualifiedName}'. " +
                $"An input accepts exactly one source; remove one of the connections.");
        }

        _source = source;
    }

    /// <summary>Marks this input as driven from outside the plant. Called by the builder for writable bindings.</summary>
    internal void DriveExternally()
    {
        if (_source is not null)
        {
            throw new InvalidOperationException(
                $"Input '{QualifiedName}' is driven by '{_source.QualifiedName}'; a writable tag cannot " +
                $"drive it as well.");
        }

        _external = true;
    }

    /// <summary>Applies an external write. Called at phase 1 only.</summary>
    internal void SetExternal(T value) => _externalValue = value;
}
