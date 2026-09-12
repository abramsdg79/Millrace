namespace Dse.Components.Safety;

/// <summary>An emergency stop button.</summary>
public sealed class EStop : SafetySwitch
{
    public EStop(string id)
        : base(id)
    {
    }

    protected override string ActuatedCode => "ESTOP_PRESSED";

    protected override string ReleasedCode => "ESTOP_RELEASED";
}
