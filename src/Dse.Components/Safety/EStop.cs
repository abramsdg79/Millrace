using Dse.Core.Catalogue;

namespace Dse.Components.Safety;

/// <summary>An emergency stop button.</summary>
public sealed class EStop : SafetySwitch
{
    public static ComponentDescriptor Descriptor { get; } =
        Describe("e-stop", "A latching emergency-stop button in a safety loop.", id => new EStop(id));

    public EStop(string id)
        : base(id)
    {
    }

    protected override string ActuatedCode => "ESTOP_PRESSED";

    protected override string ReleasedCode => "ESTOP_RELEASED";
}
