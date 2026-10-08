namespace Millrace.Core.Flow;

/// <summary>
/// A kind of material — ore, dough, a billet. The state schema names the
/// per-parcel accumulated values (time above a temperature, say) that
/// transforms may write. An empty schema costs nothing.
/// </summary>
public sealed class MaterialType
{
    private readonly string[] _stateSchema;

    public MaterialType(string name, PayloadKind kind, params string[] stateSchema)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(stateSchema);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string state in stateSchema)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(state, nameof(stateSchema));
            if (!seen.Add(state))
            {
                throw new ArgumentException(
                    $"State '{state}' is declared twice on material '{name}'. State names must be unique.",
                    nameof(stateSchema));
            }
        }

        Name = name;
        Kind = kind;
        _stateSchema = stateSchema.ToArray();
    }

    public string Name { get; }

    public PayloadKind Kind { get; }

    /// <summary>Ordered names of the accumulated-state slots.</summary>
    public IReadOnlyList<string> StateSchema => _stateSchema;

    /// <summary>
    /// Index of a named state slot. Resolve it once in Initialize and index the
    /// array by integer per tick; never look up by name in Evaluate.
    /// </summary>
    public int StateIndexOf(string stateName)
    {
        int index = Array.IndexOf(_stateSchema, stateName);
        if (index < 0)
        {
            throw new KeyNotFoundException(
                $"Material '{Name}' declares no state named '{stateName}'. " +
                $"Declared: {string.Join(", ", _stateSchema)}.");
        }

        return index;
    }

    /// <summary>A zeroed state array sized by the schema.</summary>
    public double[] NewState() =>
        _stateSchema.Length == 0 ? Array.Empty<double>() : new double[_stateSchema.Length];

    public override string ToString() => Name;
}
