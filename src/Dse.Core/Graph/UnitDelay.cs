using Dse.Core.Contexts;

namespace Dse.Core.Graph;

/// <summary>
/// Emits the previous tick's input. Because it declares no direct feedthrough it
/// creates no ordering edge, which is how a genuine feedback loop — belt load
/// raising torque demand, lowering speed, changing belt load — is made solvable.
/// </summary>
public sealed class UnitDelay<T> : ComponentBase
    where T : unmanaged
{
    private T _held;

    public UnitDelay(string id, T initialValue = default)
        : base(id)
    {
        _held = initialValue;
        In = AddInput<T>("In");
        Out = AddOutput<T>("Out");
    }

    public InputPort<T> In { get; }

    public OutputPort<T> Out { get; }

    public override bool HasDirectFeedthrough => false;

    public override void Evaluate(in TickContext ctx)
    {
        Out.Value = _held;
        _held = In.Value;
    }
}
