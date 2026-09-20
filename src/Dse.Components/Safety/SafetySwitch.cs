using Dse.Core.Catalogue;
using Dse.Core.Contexts;
using Dse.Core.Faults;
using Dse.Core.Graph;
using Dse.Core.Io;
using Dse.Io;

namespace Dse.Components.Safety;

/// <summary>
/// A normally-closed safety contact: <see cref="Ok"/> is true until someone
/// actuates it. Wiring that opens fails safe; a contact that welds fails
/// dangerous. Both are real, and both are faults here.
/// </summary>
public abstract class SafetySwitch : ComponentBase, IFaultTarget, ITagProvider
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

    /// <summary>The descriptor of a concrete switch; all of them share ports, faults and tags.</summary>
    protected static ComponentDescriptor Describe(string type, string description, Func<string, ISimNode> create) =>
        new(type, ComponentCategory.Safety, description, (id, p) => create(id))
        {
            Ports =
            [
                PortSpec.In<bool>("Actuated", description: "The operator has pressed or pulled it."),
                PortSpec.Out<bool>("Ok", description: "The safety loop through this switch is healthy."),
            ],
            Faults = Faults,
            Tags =
            [
                new TagEntry("Actuated", TagKind.Bool, TagAccess.ReadWrite),
                new TagEntry("Ok", TagKind.Bool, TagAccess.ReadOnly),
            ],
        };

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

    public IEnumerable<TagBinding> DescribeTags() =>
    [
        TagBinding.Write("Actuated", Actuated, "Actuated by the operator"),
        TagBinding.Read("Ok", Ok, "Safety loop healthy"),
    ];

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
