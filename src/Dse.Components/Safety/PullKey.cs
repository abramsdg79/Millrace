namespace Dse.Components.Safety;

/// <summary>A pull-wire switch along a conveyor.</summary>
public sealed class PullKey : SafetySwitch
{
    public PullKey(string id)
        : base(id)
    {
    }

    protected override string ActuatedCode => "PULLKEY_PULLED";

    protected override string ReleasedCode => "PULLKEY_RESET";
}
