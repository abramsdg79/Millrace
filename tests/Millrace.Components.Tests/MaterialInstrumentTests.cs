using Millrace.Components.Flow;
using Millrace.Components.Instruments;
using Millrace.Components.Tests.Fakes;
using Millrace.Core;
using Millrace.Core.Flow;
using Millrace.Core.Time;
using Xunit;

namespace Millrace.Components.Tests;

public class MaterialInstrumentTests
{
    private static readonly MaterialType Ore = new("Ore", PayloadKind.Bulk);
    private static readonly MaterialType Billet = new("Billet", PayloadKind.Discrete);

    private static SimulationOptions Options() => new()
    {
        Seed = 1UL,
        StartTime = new DateTimeOffset(2026, 1, 1, 6, 0, 0, TimeSpan.Zero),
        TimeStep = TimeSpan.FromSeconds(0.5),
    };

    [Fact]
    public void TheBeltScaleReadsTonnesPerHourFromDensityAndSpeed()
    {
        var feed = new BulkSource("Feed", Ore, 2.0);
        var belt = new BulkBelt("CV", length: 5.0, cellSize: 0.5, maxSpeed: 1.0, maxLinearDensity: 10.0);
        var pile = new BulkSink("Pile");
        var speed = new Setpoint("SP", 1.0);
        var scale = new BeltScale("WT", belt, positionM: 2.5, new InstrumentSpec("t/h", 0.0, 100.0));
        feed.Out.ConnectTo(belt.In);
        belt.Out.ConnectTo(pile.In);
        speed.Out.ConnectTo(belt.Speed);
        speed.Out.ConnectTo(scale.Speed);
        Simulation sim = new SimulationBuilder(Options()).Add(pile).Add(belt).Add(feed).Add(speed).Add(scale).Build();

        sim.RunFor(TimeSpan.FromSeconds(15));

        // 1 kg per 0.5 m cell = 2 kg/m × 1 m/s × 3.6 = 7.2 t/h, the feed rate.
        Assert.Equal(7.2, scale.Value.Value, 6);
        Assert.Equal(7.2, sim.Telemetry.Read("WT.Truth"), 6);

        speed.Value = 0.0;
        sim.Tick();
        Assert.Equal(0.0, scale.Value.Value, 9);
    }

    [Fact]
    public void ThePyrometerReadsTheItemInFrontOfItOrTheBackground()
    {
        var source = new ItemSource("Billets", Billet, 20.0, 2.0, new MaterialProperties(7800.0, 0.0, 950.0));
        var belt = new DiscreteBelt("CV", length: 4.0, maxSpeed: 1.0);
        var sink = new ItemSink("Out");
        var speed = new Setpoint("SP", 1.0);
        var pyro = new Pyrometer("TT", belt, positionM: 2.0, windowM: 0.3, new InstrumentSpec("degC", 0.0, 1500.0));
        source.Out.ConnectTo(belt.In);
        belt.Out.ConnectTo(sink.In);
        speed.Out.ConnectTo(belt.Speed);
        Simulation sim = new SimulationBuilder(Options()).Add(sink).Add(belt).Add(source).Add(speed).Add(pyro).Build();

        var readings = new List<double>();
        for (int i = 0; i < 40; i++)
        {
            sim.Tick();
            readings.Add(pyro.Value.Value);
        }

        Assert.Contains(950.0, readings);
        Assert.Contains(20.0, readings);
        Assert.All(readings, r => Assert.True(r == 950.0 || r == 20.0));
    }

    [Fact]
    public void ThePartCounterCountsEachItemOnceAndCanBeBlinded()
    {
        var source = new ItemSource("Billets", Billet, 20.0, 1.0);
        var belt = new DiscreteBelt("CV", length: 4.0, maxSpeed: 1.0);
        var sink = new ItemSink("Out");
        var speed = new Setpoint("SP", 1.0);
        var counter = new PartCounter("PC", belt, positionM: 3.0, windowM: 0.3);
        source.Out.ConnectTo(belt.In);
        belt.Out.ConnectTo(sink.In);
        speed.Out.ConnectTo(belt.Speed);
        Simulation sim = new SimulationBuilder(Options()).Add(sink).Add(belt).Add(source).Add(speed).Add(counter).Build();

        sim.RunFor(TimeSpan.FromSeconds(20));
        long seen = counter.Count.Value;
        Assert.InRange(seen, 13L, 16L);
        Assert.Equal((double)seen, sim.Telemetry.Read("PC.Count"));

        sim.InjectFaultIn(TimeSpan.Zero, "PC", PartCounter.Blinded);
        sim.RunFor(TimeSpan.FromSeconds(5));
        Assert.Equal(seen, counter.Count.Value);
        Assert.False(counter.Present.Value);
    }

    [Fact]
    public void AStoppedItemInTheWindowIsCountedOnce()
    {
        var source = new ItemSource("Billets", Billet, 20.0, 1.0);
        var belt = new DiscreteBelt("CV", length: 4.0, maxSpeed: 1.0);
        var sink = new ItemSink("Out", capacity: 0);
        var speed = new Setpoint("SP", 1.0);
        var counter = new PartCounter("PC", belt, positionM: 4.0, windowM: 0.1);   // at the head, where items queue
        source.Out.ConnectTo(belt.In);
        belt.Out.ConnectTo(sink.In);
        speed.Out.ConnectTo(belt.Speed);
        Simulation sim = new SimulationBuilder(Options()).Add(sink).Add(belt).Add(source).Add(speed).Add(counter).Build();

        sim.RunFor(TimeSpan.FromSeconds(30));

        Assert.Equal(1L, counter.Count.Value);
        Assert.True(counter.Present.Value);
    }
}
