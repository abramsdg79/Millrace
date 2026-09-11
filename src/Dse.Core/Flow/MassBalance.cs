namespace Dse.Core.Flow;

/// <summary>The plant-wide mass ledger at one instant, in kilograms.</summary>
/// <param name="Created">Cumulative mass injected by sources.</param>
/// <param name="Destroyed">Cumulative mass removed by sinks and declared losses.</param>
/// <param name="Held">Mass currently resident in nodes.</param>
public readonly record struct MassBalance(double Created, double Destroyed, double Held)
{
    /// <summary>Sourced − sunk − held. Zero when every transfer went through the protocol.</summary>
    public double Drift => Created - Destroyed - Held;
}
