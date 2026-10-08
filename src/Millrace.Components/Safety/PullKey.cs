using Millrace.Core.Catalogue;

namespace Millrace.Components.Safety;

/// <summary>A pull-wire switch along a conveyor.</summary>
public sealed class PullKey : SafetySwitch
{
    public static ComponentDescriptor Descriptor { get; } =
        Describe("pull-key", "A pull-wire switch along a conveyor, in a safety loop.", id => new PullKey(id));

    public PullKey(string id)
        : base(id)
    {
    }

    protected override string ActuatedCode => "PULLKEY_PULLED";

    protected override string ReleasedCode => "PULLKEY_RESET";
}
