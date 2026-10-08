using Millrace.Core.Contexts;
using Millrace.Core.Graph;
using Millrace.Core.Validation;

namespace Millrace.Core.Flow;

/// <summary>
/// A component that holds material. It reports its inventory so the engine can
/// audit conservation every tick, and it advances its own contents once its
/// outgoing links have been resolved.
/// </summary>
public interface IFlowNode : ISimComponent
{
    /// <summary>Kilograms currently resident in this node.</summary>
    double MassHeld { get; }

    /// <summary>Cumulative kilograms this node has injected from outside the plant (sources).</summary>
    double MassCreated { get; }

    /// <summary>Cumulative kilograms this node has removed from the plant (sinks, declared losses).</summary>
    double MassDestroyed { get; }

    /// <summary>
    /// Internal motion for one time step: cells shift, items travel, transforms
    /// run. Called after this node's outgoing links have transferred and after
    /// every downstream node has already advanced. The context is how a node
    /// logs an event or reads simulation time during this phase, since phase 3
    /// runs after the signal phase has already latched.
    /// </summary>
    void Advance(in TickContext ctx);

    /// <summary>Flow-specific build-time checks, such as the CFL condition. Empty when valid.</summary>
    IEnumerable<ValidationError> ValidateFlow(double dt);
}
