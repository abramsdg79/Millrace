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

    public InputPort(string name, string ownerId, T defaultValue, bool isRequired, bool isLatched = false)
        : base(name, ownerId)
    {
        DefaultValue = defaultValue;
        IsRequired = isRequired;
        IsLatched = isLatched;
        _captured = defaultValue;
    }

    public T DefaultValue { get; }

    public bool IsRequired { get; }

    /// <summary>True when <see cref="Value"/> is the value latched at the end of the previous tick.</summary>
    public bool IsLatched { get; }

    public T Value => IsLatched ? _captured : Live;

    private T Live => _source is null ? DefaultValue : _source.Value;

    public override bool IsMissingRequiredConnection => IsRequired && _source is null;

    internal override Port? SourcePort => _source;

    internal override bool CreatesOrderingEdge => !IsLatched && _source is not null;

    internal override bool IsLatchedInput => IsLatched;

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

        if (_source is not null)
        {
            throw new InvalidOperationException(
                $"Input '{QualifiedName}' is already driven by '{_source.QualifiedName}'. " +
                $"An input accepts exactly one source; remove one of the connections.");
        }

        _source = source;
    }
}
