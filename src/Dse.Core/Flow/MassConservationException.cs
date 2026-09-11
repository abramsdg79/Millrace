using System.Globalization;

namespace Dse.Core.Flow;

/// <summary>Raised by the per-tick audit when mass appeared or vanished outside the transport protocol.</summary>
public sealed class MassConservationException : Exception
{
    public MassConservationException(long tick, MassBalance balance)
        : base(BuildMessage(tick, balance))
    {
        Tick = tick;
        Balance = balance;
    }

    public long Tick { get; }

    public MassBalance Balance { get; }

    private static string BuildMessage(long tick, MassBalance balance) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"Mass is not conserved at tick {tick}: sourced {balance.Created} kg - sunk " +
            $"{balance.Destroyed} kg - held {balance.Held} kg = {balance.Drift} kg. A node is " +
            $"creating or losing mass outside the transfer protocol. Report injected mass in " +
            $"MassCreated and removed mass (sinks, declared losses) in MassDestroyed.");
}
