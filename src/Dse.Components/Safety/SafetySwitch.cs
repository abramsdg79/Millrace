using Dse.Core.Contexts;
using Dse.Core.Faults;
using Dse.Core.Graph;

namespace Dse.Components.Safety;

/// <summary>
/// A normally-closed safety contact: <see cref="Ok"/> is true until someone
/// actuates it. Wiring that opens fails safe; a contact that welds fails
/// dangerous. Both are real, and both are faults here.
/// </summary>
public abstract class SafetySwitch : ComponentBase, IFaultTarget
{
    /// <summary>The loop is open: reads not-OK whatever the operator does.</summary>
    public const string WiringOpen = "wiring-open";

    /// <summary>The contact is welded: reads OK whatever the operator does.</summary>
    public const string ContactWelded = "contact-welded";

    private static readonly FaultDescriptor[] Faults =
    [
        new(WiringOpen, "The safety loop is open; the switch reads not-OK regardless of actuation (fail-safe)."),
        new(ContactWelded, "The contact is welded closed; the switch reads OK even when actuated."),
    ];

    private bool _open;
    private bool _welded;
    private bool _wasActuated;

    protected SafetySwitch(string id)
        : base(id)
    {
        Actuated = AddInput<bool>("Actuated");
        Ok = AddOutput<bool>("Ok");
    }

    /// <summary>True while the operator holds the device actuated. Unconnected reads false.</summary>
    public InputPort<bool> Actuated { get; }

    /// <summary>The contact: true when the loop is healthy and the device is not actuated.</summary>
    public OutputPort<bool> Ok { get; }

    public IReadOnlyList<FaultDescriptor> SupportedFaults => Faults;

    protected abstract string ActuatedCode { get; }

    protected abstract string ReleasedCode { get; }

    public override void Evaluate(in TickContext ctx)
    {
        bool actuated = Actuated.Value;
        if (actuated != _wasActuated)
        {
            ctx.Log(Id, actuated ? ActuatedCode : ReleasedCode, actuated ? "Actuated by the operator." : "Released.");
        }

        _wasActuated = actuated;
        Ok.Value = _welded || (!_open && !actuated);
    }

    public void ApplyFault(string faultId, FaultArguments arguments)
    {
        switch (faultId)
        {
            case WiringOpen:
                _open = true;
                break;
            case ContactWelded:
                _welded = true;
                break;
        }
    }

    public void ClearFault(string faultId)
    {
        switch (faultId)
        {
            case WiringOpen:
                _open = false;
                break;
            case ContactWelded:
                _welded = false;
                break;
        }
    }
}
