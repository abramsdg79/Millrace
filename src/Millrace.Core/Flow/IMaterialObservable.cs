namespace Millrace.Core.Flow;

/// <summary>
/// A node an instrument can be mounted on. Safe to call during phase 2 only:
/// material changes exclusively in phase 3, so what an instrument reads in
/// its Evaluate is the same whatever the evaluation order. An instrument
/// holds a reference to the node and calls nothing else on it.
/// </summary>
public interface IMaterialObservable
{
    /// <summary>
    /// The material at <paramref name="position"/> metres from the node's
    /// inlet end, looking <paramref name="window"/> metres either side. Nodes
    /// without a length (a chute, a process unit) ignore both and report their
    /// contents. False when nothing is there.
    /// </summary>
    bool TryObserve(double position, double window, out MaterialObservation observation);
}
