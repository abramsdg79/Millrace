using Millrace.Core.Catalogue;

namespace Millrace.Components.Safety;

/// <summary>An emergency stop button.</summary>
public sealed class EStop : SafetySwitch
{
    public static ComponentDescriptor Descriptor { get; } =
        Describe(
            "e-stop",
            "An emergency-stop button in a safety loop: not OK while it is held actuated. The button itself does not latch; the safety relay it feeds does.",
            id => new EStop(id));

    public EStop(string id)
        : base(id)
    {
    }

    protected override string ActuatedCode => "ESTOP_PRESSED";

    protected override string ReleasedCode => "ESTOP_RELEASED";
}
