using Dse.Core.Contexts;

namespace Dse.Core.Graph;

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
    /// return false (such as <c>UnitDelay&lt;T&gt;</c>) create no ordering edge
    /// and so may sit inside a feedback loop.
    /// </summary>
    bool HasDirectFeedthrough { get; }

    void Initialize(in InitContext ctx);

    void Evaluate(in TickContext ctx);
}
