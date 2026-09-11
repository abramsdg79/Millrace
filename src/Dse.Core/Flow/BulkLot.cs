namespace Dse.Core.Flow;

/// <summary>
/// A parcel of bulk material: what a cell holds and what moves across a link.
/// <see cref="Empty"/> has no type and adopts whatever is merged into it.
/// </summary>
public readonly record struct BulkLot(MaterialType? Type, double Mass, MaterialProperties Properties)
{
    public static BulkLot Empty => default;

    public bool IsEmpty => Mass <= 0.0;

    public static BulkLot Of(MaterialType type, double mass, MaterialProperties properties)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (type.Kind != PayloadKind.Bulk)
        {
            throw new ArgumentException(
                $"Material '{type}' is {type.Kind}; a bulk lot needs a Bulk material type.",
                nameof(type));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(mass);
        return new BulkLot(type, mass, properties);
    }

    /// <summary>Combines two lots of the same material, blending properties by mass.</summary>
    public BulkLot Merge(in BulkLot other)
    {
        if (other.IsEmpty)
        {
            return this;
        }

        if (IsEmpty)
        {
            return other;
        }

        if (!ReferenceEquals(Type, other.Type))
        {
            throw new InvalidOperationException(
                $"Cannot merge '{other.Type}' into a lot of '{Type}'. A cell holds one material " +
                $"type; change the type with an explicit process component before mixing.");
        }

        return new BulkLot(
            Type,
            Mass + other.Mass,
            MaterialProperties.Blend(Properties, Mass, other.Properties, other.Mass));
    }

    /// <summary>
    /// Splits off up to <paramref name="mass"/> kilograms. The taken lot and the
    /// remainder share this lot's properties.
    /// </summary>
    public BulkLot Take(double mass, out BulkLot remaining)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(mass);

        if (mass <= 0.0 || IsEmpty)
        {
            remaining = this;
            return Empty;
        }

        if (mass >= Mass)
        {
            remaining = Empty;
            return this;
        }

        remaining = this with { Mass = Mass - mass };
        return this with { Mass = mass };
    }
}
