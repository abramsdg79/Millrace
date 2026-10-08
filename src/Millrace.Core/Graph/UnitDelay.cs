using Millrace.Core.Contexts;

namespace Millrace.Core.Graph;

/// <summary>
/// Emits the value latched at the end of the previous tick. Because it declares
/// no direct feedthrough it creates no ordering edge, which is how a genuine
/// feedback loop — belt load raising torque demand, lowering speed, changing
/// belt load — is made solvable. Latching happens in <see cref="OnLatch"/>, after
/// every component has evaluated, so the lag is exactly one tick regardless of
/// where the resolver places this component relative to its producer.
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
    }

    protected override void OnLatch() => _held = In.Value;
}
