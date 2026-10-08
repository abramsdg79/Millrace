using Millrace.Core.Flow;
using Millrace.Core.Randomness;
using Millrace.Core.Telemetry;

namespace Millrace.Core.Contexts;

/// <summary>What a component is given once, before the first tick.</summary>
public readonly struct InitContext
{
    private readonly TelemetryRegistry _telemetry;
    private readonly string _componentId;

    public InitContext(
        DeterministicRandom random,
        TelemetryRegistry telemetry,
        ItemIdSequence items,
        string componentId,
        DateTimeOffset startTime,
        double dt)
    {
        ArgumentNullException.ThrowIfNull(random);
        ArgumentNullException.ThrowIfNull(telemetry);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentException.ThrowIfNullOrWhiteSpace(componentId);

        Random = random;
        Items = items;
        StartTime = startTime;
        Dt = dt;
        _telemetry = telemetry;
        _componentId = componentId;
    }

    /// <summary>This component's own random stream, derived from the master seed and its id.</summary>
    public DeterministicRandom Random { get; }

    /// <summary>
    /// The simulation's item id counter. Components that create items keep a
    /// reference and call <see cref="ItemIdSequence.Next"/> per item; it is the
    /// only source of item ids, which is what makes them deterministic.
    /// </summary>
    public ItemIdSequence Items { get; }

    public DateTimeOffset StartTime { get; }

    public double Dt { get; }

    /// <summary>Registers a telemetry channel, prefixed with this component's id.</summary>
    public TelemetryHandle RegisterTelemetry(string name, string unit) =>
        _telemetry.Register($"{_componentId}.{name}", unit);
}
