using Dse.Core.Flow;

namespace Dse.Components.Transforms;

/// <summary>
/// Accumulates the time material spends at or above a temperature into one
/// slot of its state array. Resolve the slot once with <see cref="For"/>;
/// material with no such slot (bulk, or a schema without it) is left alone.
/// </summary>
public sealed class ResidenceAccumulator : IMaterialTransform
{
    public ResidenceAccumulator(int stateIndex, double thresholdTemperature)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(stateIndex);
        StateIndex = stateIndex;
        ThresholdTemperature = thresholdTemperature;
    }

    public int StateIndex { get; }

    /// <summary>°C.</summary>
    public double ThresholdTemperature { get; }

    /// <summary>Resolves the slot by name; throws if the material declares no such state.</summary>
    public static ResidenceAccumulator For(MaterialType type, string stateName, double thresholdTemperature)
    {
        ArgumentNullException.ThrowIfNull(type);
        return new ResidenceAccumulator(type.StateIndexOf(stateName), thresholdTemperature);
    }

    public void Apply(ref MaterialProperties properties, Span<double> state, double dt, in TransformContext context)
    {
        if (StateIndex >= state.Length || properties.Temperature < ThresholdTemperature)
        {
            return;
        }

        state[StateIndex] += dt;
    }
}
