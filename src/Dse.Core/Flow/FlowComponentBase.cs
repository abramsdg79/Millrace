using Dse.Core.Contexts;
using Dse.Core.Graph;
using Dse.Core.Validation;

namespace Dse.Core.Flow;

/// <summary>
/// Base for components that hold material. Most flow nodes neither create nor
/// destroy mass, need no validation of their own, and have nothing to do in the
/// signal phase, so those default to nothing; only the inventory is mandatory.
/// </summary>
public abstract class FlowComponentBase : ComponentBase, IFlowNode
{
    protected FlowComponentBase(string id)
        : base(id)
    {
    }

    public abstract double MassHeld { get; }

    public virtual double MassCreated => 0.0;

    public virtual double MassDestroyed => 0.0;

    public override void Evaluate(in TickContext ctx)
    {
    }

    public virtual void Advance(double dt)
    {
    }

    public virtual IEnumerable<ValidationError> ValidateFlow(double dt) => [];

    protected FlowInlet AddInlet(string name, PayloadKind kind) =>
        AddPort(new FlowInlet(name, Id, kind));

    protected FlowOutlet AddOutlet(string name, PayloadKind kind) =>
        AddPort(new FlowOutlet(name, Id, kind));
}
