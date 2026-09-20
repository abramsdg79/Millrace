namespace Dse.Core.Graph;

/// <summary>An output. May drive any number of inputs.</summary>
public sealed class OutputPort<T> : Port
    where T : unmanaged
{
    public OutputPort(string name, string ownerId)
        : base(name, ownerId)
    {
    }

    public T Value { get; set; }

    public override bool IsMissingRequiredConnection => false;

    internal override Port? SourcePort => null;

    public void ConnectTo(InputPort<T> input)
    {
        ArgumentNullException.ThrowIfNull(input);
        input.ConnectFrom(this);
    }

    internal override Type? ValueType => typeof(T);
}
