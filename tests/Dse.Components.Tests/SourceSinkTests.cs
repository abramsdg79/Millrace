using Dse.Components.Flow;
using Dse.Components.Tests.Fakes;
using Dse.Core;
using Dse.Core.Flow;
using Dse.Core.Time;
using Dse.Io;
using Xunit;

namespace Dse.Components.Tests;

public class SourceSinkTests
{
    private static readonly MaterialType Ore = new("Ore", PayloadKind.Bulk);
    private static readonly MaterialType Billet = new("Billet", PayloadKind.Discrete);

    private static SimulationOptions Options(double stepSeconds = 0.5) => new()
    {
        Seed = 1UL,
        StartTime = new DateTimeOffset(2026, 1, 1, 6, 0, 0, TimeSpan.Zero),
        TimeStep = TimeSpan.FromSeconds(stepSeconds),
    };

    [Fact]
    public void ABulkSourceFeedsASinkAtItsRateAndTheLedgerBalances()
    {
        var source = new BulkSource("Feed", Ore, rateKgPerSecond: 2.0, new MaterialProperties(1600.0, 0.05, 15.0));
        var sink = new BulkSink("Pile");
        source.Out.ConnectTo(sink.In);
        Simulation sim = new SimulationBuilder(Options()).Add(sink).Add(source).Build();

        sim.RunFor(TimeSpan.FromSeconds(5));   // 10 ticks; mass created on tick N moves on tick N+1

        // Outputs and telemetry are written in phase 2, so they show the state
        // after the *previous* tick's transport: one tick behind the ledger.
        Assert.Equal(9.0, sink.MassDestroyed, 9);
        Assert.Equal(8.0, sink.Received.Value, 9);
        Assert.Equal(1.0, source.MassHeld, 9);
        Assert.Equal(2.0, sink.Rate.Value, 9);
        Assert.Equal(15.0, sink.LastProperties.Temperature);
        Assert.Equal(10.0, sim.MassBalance.Created, 9);
        Assert.Equal(0.0, sim.MassBalance.Drift, 9);
        Assert.Equal(9.0, sim.Telemetry.Read("Feed.Sourced"), 9);
        Assert.Equal(8.0, sim.Telemetry.Read("Pile.Received"), 9);
    }

    [Fact]
    public void TheRateInputOverridesTheConfiguredRate()
    {
        var source = new BulkSource("Feed", Ore, rateKgPerSecond: 2.0);
        var sink = new BulkSink("Pile");
        var rate = new Setpoint("Rate", 4.0);
        source.Out.ConnectTo(sink.In);
        rate.Out.ConnectTo(source.Rate);
        Simulation sim = new SimulationBuilder(Options()).Add(sink).Add(source).Add(rate).Build();

        sim.RunFor(TimeSpan.FromSeconds(2));

        Assert.Equal(8.0, sim.MassBalance.Created, 9);
    }

    [Fact]
    public void AFullHopperStopsCreatingMass()
    {
        var source = new BulkSource("Feed", Ore, rateKgPerSecond: 2.0, hopperCapacityKg: 3.0);
        var sink = new BulkSink("Pile", capacityKg: 0.0);   // accepts nothing
        source.Out.ConnectTo(sink.In);
        Simulation sim = new SimulationBuilder(Options()).Add(sink).Add(source).Build();

        sim.RunFor(TimeSpan.FromSeconds(10));

        Assert.Equal(3.0, source.MassHeld, 9);
        Assert.Equal(3.0, source.HopperMass.Value, 9);
        Assert.Equal(3.0, sim.MassBalance.Created, 9);
        Assert.Equal(0.0, sim.MassBalance.Drift, 9);
    }

    [Fact]
    public void DisablingAndStarvingBothStopTheFeed()
    {
        var source = new BulkSource("Feed", Ore, rateKgPerSecond: 2.0);
        var sink = new BulkSink("Pile");
        var enabled = new Switch("Run", true);
        source.Out.ConnectTo(sink.In);
        enabled.Out.ConnectTo(source.Enabled);
        Simulation sim = new SimulationBuilder(Options()).Add(sink).Add(source).Add(enabled).Build();

        sim.RunFor(TimeSpan.FromSeconds(1));
        enabled.Value = false;
        sim.RunFor(TimeSpan.FromSeconds(1));
        Assert.Equal(2.0, sim.MassBalance.Created, 9);

        enabled.Value = true;
        sim.InjectFaultIn(TimeSpan.Zero, "Feed", BulkSource.Starve);
        sim.RunFor(TimeSpan.FromSeconds(1));
        Assert.Equal(2.0, sim.MassBalance.Created, 9);

        sim.ClearFaultIn(TimeSpan.Zero, "Feed", BulkSource.Starve);
        sim.RunFor(TimeSpan.FromSeconds(1));
        Assert.Equal(4.0, sim.MassBalance.Created, 9);
    }

    [Fact]
    public void ASourceBuiltDisabledCreatesNothingUntilItsEnabledTagIsWritten()
    {
        var source = new BulkSource("Feed", Ore, rateKgPerSecond: 2.0, enabled: false);
        var sink = new BulkSink("Pile");
        source.Out.ConnectTo(sink.In);
        Simulation sim = new SimulationBuilder(Options()).Add(sink).Add(source).Build();

        sim.RunFor(TimeSpan.FromSeconds(2));
        Assert.False(sim.IO.Read("Feed.Enabled").AsBool);
        Assert.Equal(0.0, sim.MassBalance.Created, 9);

        sim.WriteIn(TimeSpan.Zero, "Feed.Enabled", TagValue.Bool(true));
        sim.RunFor(TimeSpan.FromSeconds(1));
        Assert.Equal(2.0, sim.MassBalance.Created, 9);
    }

    [Fact]
    public void ASinkWithCapacityFillsOnceAndSaysSo()
    {
        var source = new BulkSource("Feed", Ore, rateKgPerSecond: 2.0);
        var sink = new BulkSink("Pile", capacityKg: 2.5);
        source.Out.ConnectTo(sink.In);
        Simulation sim = new SimulationBuilder(Options()).Add(sink).Add(source).Build();

        sim.RunFor(TimeSpan.FromSeconds(5));

        Assert.Equal(2.5, sink.Received.Value, 9);
        Assert.True(sink.Full.Value);
        var full = Assert.Single(sim.Events.Records, r => r.Code == "FULL");
        Assert.Equal("Pile", full.Source);
        Assert.Equal(0.0, sim.MassBalance.Drift, 9);
    }

    [Fact]
    public void AnItemSourceMintsOnAnIntervalWithSequentialIds()
    {
        var source = new ItemSource("Billets", Billet, itemMassKg: 20.0, intervalSeconds: 1.0, new MaterialProperties(7800.0, 0.0, 25.0));
        var sink = new ItemSink("Scrap");
        source.Out.ConnectTo(sink.In);
        Simulation sim = new SimulationBuilder(Options()).Add(sink).Add(source).Build();

        sim.RunFor(TimeSpan.FromSeconds(5.5));   // 11 ticks: items at ticks 1,3,5,7,9 (elapsed reaches 1.0); the tick-9 item moves on tick 10

        Assert.Equal(5L, sink.LastItem!.Id);
        Assert.Equal(100.0, sink.MassReceived, 9);
        Assert.Equal(4L, sink.Count.Value);   // published in phase 2 of tick 10, before the fifth deposit
        Assert.Equal(25.0, sink.LastItem.Properties.Temperature);
        Assert.Equal(0.0, sim.MassBalance.Drift, 9);
        Assert.Equal(5.0, sim.Telemetry.Read("Billets.Sourced"));
    }

    [Fact]
    public void AnItemSourceQueuesWhenBlockedAndStopsAtItsQueueCapacity()
    {
        var source = new ItemSource("Billets", Billet, 20.0, 0.5, queueCapacity: 3);
        var sink = new ItemSink("Scrap", capacity: 0);
        source.Out.ConnectTo(sink.In);
        Simulation sim = new SimulationBuilder(Options()).Add(sink).Add(source).Build();

        sim.RunFor(TimeSpan.FromSeconds(10));

        Assert.Equal(3, source.Queued.Value);
        Assert.Equal(60.0, source.MassHeld, 9);
        Assert.Equal(3L, sim.Items.Issued);
        Assert.Equal(0.0, sim.MassBalance.Drift, 9);
    }

    [Fact]
    public void AnItemSinkWithCapacityFillsOnce()
    {
        var source = new ItemSource("Billets", Billet, 20.0, 0.5);
        var sink = new ItemSink("Scrap", capacity: 2);
        source.Out.ConnectTo(sink.In);
        Simulation sim = new SimulationBuilder(Options()).Add(sink).Add(source).Build();

        sim.RunFor(TimeSpan.FromSeconds(5));

        Assert.Equal(2L, sink.Count.Value);
        Assert.True(sink.Full.Value);
        Assert.Single(sim.Events.Records, r => r.Code == "FULL");
    }

    [Fact]
    public void StarvingAnItemSourceStopsMinting()
    {
        var source = new ItemSource("Billets", Billet, 20.0, 0.5);
        var sink = new ItemSink("Scrap");
        source.Out.ConnectTo(sink.In);
        Simulation sim = new SimulationBuilder(Options()).Add(sink).Add(source).Build();

        sim.InjectFaultAt(TimeSpan.FromSeconds(2), "Billets", ItemSource.Starve);
        sim.RunFor(TimeSpan.FromSeconds(6));

        Assert.Equal(4L, sim.Items.Issued);
        Assert.Contains(sim.Events.Records, r => r.Code == "FAULT" && r.Source == "Billets");
    }
}
