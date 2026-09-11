using Dse.Core.Contexts;
using Dse.Core.Flow;
using Dse.Core.Logging;
using Dse.Core.Randomness;
using Dse.Core.Telemetry;

namespace Dse.Core.Tests.Fakes;

/// <summary>Builds the contexts a component needs when a test drives it without a Simulation.</summary>
public static class TestContexts
{
    public static readonly DateTimeOffset Start = new(2026, 1, 1, 6, 0, 0, TimeSpan.Zero);

    public static InitContext Init(
        string componentId,
        double dt = 0.01,
        TelemetryRegistry? telemetry = null,
        ItemIdSequence? items = null) =>
        new(
            new DeterministicRandom(1UL),
            telemetry ?? new TelemetryRegistry(),
            items ?? new ItemIdSequence(),
            componentId,
            Start,
            dt);

    public static TickContext Tick(long tick, double dt = 0.01, EventLog? log = null) =>
        new(tick, dt, Start + TimeSpan.FromSeconds(tick * dt), log ?? new EventLog());
}
