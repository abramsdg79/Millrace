namespace Millrace.Core.Flow;

/// <summary>
/// What an instrument sees when it looks at one point on a node. For bulk,
/// the mass and linear density of the cell there; for an item, its mass and
/// id with <see cref="LinearDensity"/> zero.
/// </summary>
/// <param name="Mass">kg.</param>
/// <param name="LinearDensity">kg/m; zero for discrete items.</param>
/// <param name="Properties">The material's intensive properties.</param>
/// <param name="ItemId">The item's id, or zero for bulk.</param>
public readonly record struct MaterialObservation(
    double Mass,
    double LinearDensity,
    MaterialProperties Properties,
    long ItemId);
