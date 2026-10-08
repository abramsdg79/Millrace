namespace Millrace.Core.Flow;

/// <summary>Where material enters a node. Exactly one source, because mass cannot merge implicitly.</summary>
public sealed class FlowInlet : FlowPort
{
    private FlowOutlet? _source;

    public FlowInlet(string name, string ownerId, PayloadKind kind)
        : base(name, ownerId, kind)
    {
    }

    public bool IsConnected => _source is not null;

    /// <summary>The outlet feeding this inlet, or null. Read by the flow resolver.</summary>
    internal FlowOutlet? Source => _source;

    internal void ConnectFrom(FlowOutlet source)
    {
        if (_source is not null)
        {
            throw new InvalidOperationException(
                $"Inlet '{QualifiedName}' is already fed by '{_source.QualifiedName}'. " +
                $"An inlet has exactly one source; give each stream its own inlet.");
        }

        _source = source;
    }
}
