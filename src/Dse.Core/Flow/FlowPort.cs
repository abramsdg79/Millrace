using Dse.Core.Graph;

namespace Dse.Core.Flow;

/// <summary>
/// A material connection point. Flow ports never create signal-ordering edges:
/// transport runs in phase 3, after every component has evaluated, so the
/// signal resolver must not see them as dependencies.
/// </summary>
public abstract class FlowPort : Port
{
    protected FlowPort(string name, string ownerId, PayloadKind kind)
        : base(name, ownerId)
    {
        Kind = kind;
    }

    public PayloadKind Kind { get; }

    public override bool IsMissingRequiredConnection => false;

    internal override Port? SourcePort => null;
}
