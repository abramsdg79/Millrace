namespace Dse.Core.Flow;

/// <summary>
/// The intensive properties every parcel of material carries. A fixed struct
/// rather than a property bag: a dictionary per cell would put allocation and
/// non-deterministic iteration into the hottest loop in the engine. Adding a
/// field is a source change; it blends by the same rule.
/// </summary>
/// <param name="Density">kg/m³.</param>
/// <param name="Moisture">Mass fraction, 0..1.</param>
/// <param name="Temperature">°C.</param>
public readonly record struct MaterialProperties(double Density, double Moisture, double Temperature)
{
    /// <summary>
    /// Mass-weighted average of two streams. A zero-mass side contributes
    /// nothing; two zero masses return <paramref name="a"/> unchanged.
    /// </summary>
    public static MaterialProperties Blend(
        in MaterialProperties a,
        double massA,
        in MaterialProperties b,
        double massB)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(massA);
        ArgumentOutOfRangeException.ThrowIfNegative(massB);

        double total = massA + massB;
        if (total <= 0.0)
        {
            return a;
        }

        double weightA = massA / total;
        double weightB = massB / total;
        return new MaterialProperties(
            (a.Density * weightA) + (b.Density * weightB),
            (a.Moisture * weightA) + (b.Moisture * weightB),
            (a.Temperature * weightA) + (b.Temperature * weightB));
    }
}
