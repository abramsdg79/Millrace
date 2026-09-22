using Dse.Core.Catalogue;
using Dse.Core.Contexts;
using Dse.Core.Graph;
using Dse.Core.Io;
using Dse.Io;

namespace Dse.Cli.Tests.SampleModule;

/// <summary>
/// A switch with hysteresis: on above one level, off below a lower one, holding
/// in between. The worked example of docs/authoring-a-component.md — every
/// section of that recipe points at a part of this file.
/// </summary>
public sealed class HysteresisSwitch : ComponentBase, ITagProvider
{
    // 1. The descriptor sits beside the constructor it must match. The catalogue
    //    conformance test builds one of these from it and compares the two.
    public static ComponentDescriptor Descriptor { get; } = new(
        "hysteresis-switch",
        ComponentCategory.Signal,
        "Switches on above one level and off below a lower one, holding its state in between.",
        (id, p) => new HysteresisSwitch(id, p.Double("onAbove"), p.Double("offBelow")))
    {
        Parameters =
        [
            Param.Double("onAbove", "The output turns on when the value rises above this."),
            Param.Double("offBelow", "The output turns off when the value falls below this. Must be below onAbove."),
        ],
        Ports =
        [
            PortSpec.In<double>("Value", description: "The value watched."),
            PortSpec.Out<bool>("On"),
        ],
        Tags = [new TagEntry("On", TagKind.Bool, TagAccess.ReadOnly)],
    };

    private readonly double _onAbove;
    private readonly double _offBelow;

    // 2. All state is instance state. No statics, no clock, no System.Random:
    //    two simulations in one process must not see each other.
    private bool _on;

    public HysteresisSwitch(string id, double onAbove, double offBelow)
        : base(id)
    {
        // 3. A rule across two parameters cannot be a parameter range, so the
        //    constructor enforces it. A plant file that breaks it gets DSE111
        //    carrying this message — so the message must say what to change.
        if (offBelow >= onAbove)
        {
            throw new ArgumentException("offBelow must be below onAbove; otherwise the switch has no band to hold in.", nameof(offBelow));
        }

        _onAbove = onAbove;
        _offBelow = offBelow;

        // 4. Ports are declared once, in the constructor. The graph is immutable after Build().
        Value = AddInput<double>("Value");
        On = AddOutput<bool>("On");
    }

    public InputPort<double> Value { get; }

    public OutputPort<bool> On { get; }

    // 5. Evaluate reads inputs and writes outputs for one tick. It never reaches
    //    for another component: everything it knows arrives on a port.
    public override void Evaluate(in TickContext ctx)
    {
        if (Value.Value > _onAbove)
        {
            _on = true;
        }
        else if (Value.Value < _offBelow)
        {
            _on = false;
        }

        On.Value = _on;
    }

    // 6. Tags are what a plant would really have on the wire. Names are relative; the builder prefixes the id.
    public IEnumerable<TagBinding> DescribeTags() => [TagBinding.Read("On", On, "Switch state")];
}
