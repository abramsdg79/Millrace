namespace Dse.Core.Flow;

/// <summary>
/// One discrete piece of material. Mutable, because transforms change its
/// properties and state in place as it moves; identity is the id.
/// </summary>
public sealed class ItemInstance
{
    public ItemInstance(long id, MaterialType type, double mass, MaterialProperties properties)
    {
        ArgumentNullException.ThrowIfNull(type);
        RequireDiscrete(type);
        ArgumentOutOfRangeException.ThrowIfNegative(mass);

        Id = id;
        Type = type;
        Mass = mass;
        Properties = properties;
        State = type.NewState();
    }

    public long Id { get; }

    public MaterialType Type { get; private set; }

    public double Mass { get; set; }

    public MaterialProperties Properties { get; set; }

    /// <summary>Accumulated state, indexed by <see cref="MaterialType.StateIndexOf"/> on <see cref="Type"/>.</summary>
    public double[] State { get; private set; }

    /// <summary>
    /// Re-types the item, as a former or a process unit does on discharge. The
    /// accumulated state belonged to the old type and is reset.
    /// </summary>
    public void ChangeType(MaterialType newType)
    {
        ArgumentNullException.ThrowIfNull(newType);
        RequireDiscrete(newType);

        Type = newType;
        State = newType.NewState();
    }

    public override string ToString() => $"{Type}#{Id}";

    private static void RequireDiscrete(MaterialType type)
    {
        if (type.Kind != PayloadKind.Discrete)
        {
            throw new ArgumentException(
                $"Material '{type}' is {type.Kind}; an item needs a Discrete material type.",
                nameof(type));
        }
    }
}
