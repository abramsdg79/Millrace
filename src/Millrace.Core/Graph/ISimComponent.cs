using Millrace.Core.Contexts;

namespace Millrace.Core.Graph;

/// <summary>
/// An evaluatable leaf. Reads its inputs, updates its own state, writes its
/// outputs — and never touches another component. That restriction is what makes
/// topological ordering meaningful and components independently testable.
/// </summary>
public interface ISimComponent : ISimNode
{
    IReadOnlyList<Port> Ports { get; }

    /// <summary>
    /// True when outputs depend on inputs within the same tick. Components that
    /// return false (such as <see cref="UnitDelay{T}"/>) create no ordering edge
    /// and so may sit inside a feedback loop.
    /// </summary>
    bool HasDirectFeedthrough { get; }

    void Initialize(in InitContext ctx);

    void Evaluate(in TickContext ctx);

    /// <summary>
    /// Called once per tick after every component has evaluated. Components
    /// without direct feedthrough capture their inputs here, so the value they
    /// hold is always the one their producer wrote this tick, whatever the
    /// evaluation order.
    /// </summary>
    void Latch();
}
