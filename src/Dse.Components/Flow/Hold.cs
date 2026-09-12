using Dse.Core.Flow;

namespace Dse.Components.Flow;

/// <summary>The shipped hold conditions.</summary>
public static class Hold
{
    public static IHoldCondition ForSeconds(double seconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(seconds);
        return new Timed(seconds);
    }

    public static IHoldCondition TemperatureAtLeast(double celsius) => new TemperatureFloor(celsius);

    public static IHoldCondition TemperatureAtMost(double celsius) => new TemperatureCeiling(celsius);

    /// <summary>A state slot at or above a value. Unsatisfiable for material without that slot.</summary>
    public static IHoldCondition StateAtLeast(int stateIndex, double value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(stateIndex);
        return new StateFloor(stateIndex, value);
    }

    public static IHoldCondition StateAtLeast(MaterialType type, string stateName, double value)
    {
        ArgumentNullException.ThrowIfNull(type);
        return StateAtLeast(type.StateIndexOf(stateName), value);
    }

    public static IHoldCondition All(params IHoldCondition[] conditions)
    {
        ArgumentNullException.ThrowIfNull(conditions);
        ArgumentOutOfRangeException.ThrowIfZero(conditions.Length);
        return new Conjunction(conditions.ToArray());
    }

    /// <summary>The seconds a timed hold waits for, or null. Used by units to report progress.</summary>
    internal static double? TimedSeconds(IHoldCondition condition) => condition switch
    {
        Timed timed => timed.Seconds,
        Conjunction all => all.Conditions.Select(TimedSeconds).FirstOrDefault(s => s is not null),
        _ => null,
    };

    private sealed class Timed(double seconds) : IHoldCondition
    {
        public double Seconds { get; } = seconds;

        public bool IsSatisfied(double elapsedSeconds, in MaterialProperties properties, ReadOnlySpan<double> state) =>
            elapsedSeconds >= Seconds - 1e-12;
    }

    private sealed class TemperatureFloor(double celsius) : IHoldCondition
    {
        public bool IsSatisfied(double elapsedSeconds, in MaterialProperties properties, ReadOnlySpan<double> state) =>
            properties.Temperature >= celsius;
    }

    private sealed class TemperatureCeiling(double celsius) : IHoldCondition
    {
        public bool IsSatisfied(double elapsedSeconds, in MaterialProperties properties, ReadOnlySpan<double> state) =>
            properties.Temperature <= celsius;
    }

    private sealed class StateFloor(int index, double value) : IHoldCondition
    {
        public bool IsSatisfied(double elapsedSeconds, in MaterialProperties properties, ReadOnlySpan<double> state) =>
            index < state.Length && state[index] >= value;
    }

    private sealed class Conjunction(IHoldCondition[] conditions) : IHoldCondition
    {
        public IHoldCondition[] Conditions { get; } = conditions;

        public bool IsSatisfied(double elapsedSeconds, in MaterialProperties properties, ReadOnlySpan<double> state)
        {
            for (int i = 0; i < Conditions.Length; i++)
            {
                if (!Conditions[i].IsSatisfied(elapsedSeconds, in properties, state))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
