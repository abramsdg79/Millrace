namespace Dse.Core.Graph;

/// <summary>
/// An input. Exactly one source, because two sources is an undefined value.
/// Unconnected inputs read their declared default so partial plants still run.
/// </summary>
public sealed class InputPort<T> : Port
    where T : unmanaged
{
    private OutputPort<T>? _source;

    public InputPort(string name, string ownerId, T defaultValue, bool isRequired)
        : base(name, ownerId)
    {
        DefaultValue = defaultValue;
        IsRequired = isRequired;
    }

    public T DefaultValue { get; }

    public bool IsRequired { get; }

    public T Value => _source is null ? DefaultValue : _source.Value;

    public override bool IsMissingRequiredConnection => IsRequired && _source is null;

    internal override Port? SourcePort => _source;

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
