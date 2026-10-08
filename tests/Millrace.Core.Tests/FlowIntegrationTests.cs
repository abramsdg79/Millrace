using Millrace.Core;
using Millrace.Core.Flow;
using Millrace.Core.Tests.Fakes;
using Millrace.Core.Tests.Fakes.Flow;
using Millrace.Core.Time;
using Xunit;

namespace Millrace.Core.Tests;

public class FlowIntegrationTests
{
    private static readonly MaterialType Ore = new("Ore", PayloadKind.Bulk);

    private sealed record Plant(
        Simulation Sim,
        BulkFeeder Feeder,
        BulkBelt Cv1,
        BulkBuffer Chute,
        BulkBelt Cv2,
        BulkSink Sink,
        Setpoint Speed1,
        Setpoint Speed2);

    private static SimulationOptions Options(ulong seed) => new()
    {
        Seed = seed,
        StartTime = new DateTimeOffset(2026, 1, 1, 6, 0, 0, TimeSpan.Zero),
        TimeStep = TimeSpan.FromMilliseconds(500),
    };

    /// <summary>
    /// Feeder → CV001 (5 m, ten 0.5 m cells) → chute (3 kg) → CV002 (2.5 m, five
    /// cells) → stockpile. At 1 m/s and dt = 0.5 s each cell shifts exactly one
    /// place per tick, so travel times are exact. Feed 2 kg/s = 1 kg per tick.
    /// </summary>
    private static Plant Build(ulong seed = 1UL, double feedRate = 2.0)
    {
        var feeder = new BulkFeeder("Feeder", Ore, feedRate, new MaterialProperties(1600.0, 0.08, 15.0));
        var cv1 = new BulkBelt("CV001", length: 5.0, cellSize: 0.5, maxSpeed: 1.0, maxLinearDensity: 4.0);
        var chute = new BulkBuffer("Chute", capacityKg: 3.0);
        var cv2 = new BulkBelt("CV002", length: 2.5, cellSize: 0.5, maxSpeed: 1.0, maxLinearDensity: 4.0);
        var sink = new BulkSink("Stockpile");
        var speed1 = new Setpoint("SP1", 1.0);
        var speed2 = new Setpoint("SP2", 1.0);

        feeder.Out.ConnectTo(cv1.In);
        cv1.Out.ConnectTo(chute.In);
        chute.Out.ConnectTo(cv2.In);
        cv2.Out.ConnectTo(sink.In);
        speed1.Out.ConnectTo(cv1.Speed);
        speed2.Out.ConnectTo(cv2.Speed);

        Simulation sim = new SimulationBuilder(Options(seed))
            .Add(sink).Add(cv2).Add(chute).Add(cv1).Add(feeder).Add(speed1).Add(speed2)
            .Build();

        return new Plant(sim, feeder, cv1, chute, cv2, sink, speed1, speed2);
    }

    private static void Run(Plant plant, int ticks)
    {
        for (int i = 0; i < ticks; i++)
        {
            plant.Sim.Tick();
        }
    }

    [Fact]
    public void SteadyStateThroughputEqualsTheFeedRate()
    {
        Plant plant = Build();

        Run(plant, 60);

        IEnumerable<double> lastTen = plant.Sink.Received.TakeLast(10);
        Assert.All(lastTen, mass => Assert.Equal(1.0, mass, 9));
        Assert.Equal(10.0, plant.Cv1.Load.Value, 9);
        Assert.Equal(0.0, plant.Sim.MassBalance.Drift, 9);
    }

    [Fact]
    public void ASurgeArrivesAfterLengthOverSpeedPlusOneHandOff()
    {
        Plant plant = Build();
        plant.Sim.Tick();                     // tick 0: 1 kg created and placed on CV001
        plant.Feeder.RateKgPerSecond = 0.0;

        int arrival = -1;
        for (int tick = 1; tick < 40; tick++)
        {
            plant.Sim.Tick();
            if (plant.Sink.TotalReceived > 0.0)
            {
                arrival = tick;
                break;
            }
        }

        // 10 ticks along CV001 (5 m at 1 m/s), one tick in the chute, 5 ticks along CV002.
        Assert.Equal(16, arrival);
        Assert.Equal(1.0, plant.Sink.TotalReceived, 9);
    }

    [Fact]
    public void StoppingTheDownstreamBeltBacksUpTheChainAndRestartingDrainsIt()
    {
        Plant plant = Build();
        Run(plant, 40);
        double steadyLoad = plant.Cv1.Load.Value;

        plant.Speed2.Value = 0.0;
        Run(plant, 5);
        int receivedAtStop = plant.Sink.Received.Count;
        Run(plant, 25);

        Assert.Equal(receivedAtStop, plant.Sink.Received.Count);
        Assert.Equal(3.0, plant.Chute.MassHeld, 9);
        Assert.Equal(20.0, plant.Cv1.Load.Value, 9);
        Assert.Equal(4.0, plant.Cv1.PeakLinearDensity.Value, 9);
        Assert.True(plant.Cv1.Load.Value > steadyLoad);
        Assert.True(plant.Feeder.MassHeld > 0.0);
        Assert.Equal(0.0, plant.Sim.MassBalance.Drift, 9);

        plant.Speed2.Value = 1.0;
        Run(plant, 80);   // the backlog (hopper, belt and chute) flushes at +1 kg/tick

        Assert.True(plant.Chute.MassHeld < 3.0);
        Assert.All(plant.Sink.Received.TakeLast(5), mass => Assert.Equal(1.0, mass, 9));
        Assert.Equal(0.0, plant.Sim.MassBalance.Drift, 9);
    }

    [Fact]
    public void TwoRunsAreIndistinguishable()
    {
        Plant first = Build();
        Plant second = Build();

        foreach (Plant plant in new[] { first, second })
        {
            Run(plant, 40);
            plant.Speed2.Value = 0.0;
            Run(plant, 30);
            plant.Speed2.Value = 1.0;
            Run(plant, 80);
        }

        Assert.Equal(first.Sink.Received, second.Sink.Received);
        Assert.Equal(first.Sim.Telemetry.Read("CV001.Load"), second.Sim.Telemetry.Read("CV001.Load"));
        Assert.Equal(first.Sim.MassBalance, second.Sim.MassBalance);
    }

    [Fact]
    public void BeltLoadIsPublishedAsTelemetry()
    {
        Plant plant = Build();

        Run(plant, 20);

        Assert.Equal(plant.Cv1.Load.Value, plant.Sim.Telemetry.Read("CV001.Load"));
        Assert.Equal(plant.Cv2.Load.Value, plant.Sim.Telemetry.Read("CV002.Load"));
    }

    [Fact]
    public void PropertiesTravelWithTheMaterial()
    {
        Plant plant = Build();

        Run(plant, 30);

        Assert.Equal(0.08, plant.Sink.LastProperties.Moisture, 9);
        Assert.Equal(15.0, plant.Sink.LastProperties.Temperature, 9);
    }
}
