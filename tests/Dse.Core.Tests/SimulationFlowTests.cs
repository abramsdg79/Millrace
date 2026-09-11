using Dse.Core;
using Dse.Core.Flow;
using Dse.Core.Graph;
using Dse.Core.Tests.Fakes;
using Dse.Core.Tests.Fakes.Flow;
using Dse.Core.Time;
using Dse.Core.Validation;
using Xunit;

namespace Dse.Core.Tests;

public class SimulationFlowTests
{
    private static readonly MaterialType Ore = new("Ore", PayloadKind.Bulk);

    private static SimulationOptions Options(bool checkConservation = true) => new()
    {
        Seed = 1UL,
        StartTime = new DateTimeOffset(2026, 1, 1, 6, 0, 0, TimeSpan.Zero),
        TimeStep = TimeSpan.FromMilliseconds(10),
        CheckConservation = checkConservation,
    };

    [Fact]
    public void FeederToSinkMovesTheFeedRateEveryTick()
    {
        var feeder = new BulkFeeder("F", Ore, rateKgPerSecond: 10.0);
        var sink = new BulkSink("S");
        feeder.Out.ConnectTo(sink.In);
        Simulation sim = new SimulationBuilder(Options()).Add(sink).Add(feeder).Build();

        sim.RunFor(TimeSpan.FromSeconds(1));

        Assert.Equal(100, sink.Received.Count);
        Assert.All(sink.Received, mass => Assert.Equal(0.1, mass, 9));
        Assert.Equal(10.0, sink.TotalReceived, 9);
        Assert.Equal(0.0, feeder.MassHeld, 9);
    }

    [Fact]
    public void FlowNodesInsideACompositeAreWiredThroughTheFlattenedLeaves()
    {
        var line = new FeedLine("Line", Ore);
        Simulation sim = new SimulationBuilder(Options()).Add(line).Build();

        sim.RunFor(TimeSpan.FromMilliseconds(50));

        Assert.Equal(new[] { "Line.Feeder", "Line.Sink" }, sim.Components.Select(c => c.Id));
        Assert.Equal(0.5, line.Sink.TotalReceived, 9);
    }

    [Fact]
    public void BuildRejectsRecirculation()
    {
        var a = new BulkBuffer("A", 1.0);
        var b = new BulkBuffer("B", 1.0);
        a.Out.ConnectTo(b.In);
        b.Out.ConnectTo(a.In);

        SimulationValidationException error = Assert.Throws<SimulationValidationException>(
            () => new SimulationBuilder(Options()).Add(a).Add(b).Build());

        Assert.Equal("DSE005", error.Result.Errors[0].Code);
    }

    [Fact]
    public void ValidateReportsAnInletFedByAComponentThatWasNotAdded()
    {
        var feeder = new BulkFeeder("F", Ore, 1.0);
        var sink = new BulkSink("S");
        feeder.Out.ConnectTo(sink.In);

        ValidationResult result = new SimulationBuilder(Options()).Add(sink).Validate();

        Assert.Equal("DSE007", result.Errors[0].Code);
        Assert.Contains("F.Out", result.Errors[0].Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateReportsAnOutletFeedingAComponentThatWasNotAdded()
    {
        var feeder = new BulkFeeder("F", Ore, 1.0);
        var sink = new BulkSink("S");
        feeder.Out.ConnectTo(sink.In);

        ValidationResult result = new SimulationBuilder(Options()).Add(feeder).Validate();

        Assert.Equal("DSE007", result.Errors[0].Code);
        Assert.Contains("S.In", result.Errors[0].Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheAuditRunsAfterEveryFlowPhase()
    {
        var feeder = new BulkFeeder("F", Ore, 10.0);
        var leaky = new LeakyBuffer("L", keepFraction: 0.5);
        feeder.Out.ConnectTo(leaky.In);
        Simulation sim = new SimulationBuilder(Options()).Add(feeder).Add(leaky).Build();

        MassConservationException error = Assert.Throws<MassConservationException>(sim.Tick);

        Assert.Equal(0, error.Tick);
        Assert.Equal(0.05, error.Balance.Drift, 9);
    }

    [Fact]
    public void TheAuditCanBeSwitchedOffPerRun()
    {
        var feeder = new BulkFeeder("F", Ore, 10.0);
        var leaky = new LeakyBuffer("L", 0.5);
        feeder.Out.ConnectTo(leaky.In);
        Simulation sim = new SimulationBuilder(Options(checkConservation: false)).Add(feeder).Add(leaky).Build();

        sim.RunFor(TimeSpan.FromMilliseconds(100));

        Assert.Equal(0.5, sim.MassBalance.Drift, 9);
    }

    [Fact]
    public void MassBalanceIsExposed()
    {
        var feeder = new BulkFeeder("F", Ore, 10.0);
        var buffer = new BulkBuffer("B", 0.25);
        feeder.Out.ConnectTo(buffer.In);
        Simulation sim = new SimulationBuilder(Options()).Add(feeder).Add(buffer).Build();

        sim.RunFor(TimeSpan.FromMilliseconds(100));

        MassBalance balance = sim.MassBalance;
        Assert.Equal(1.0, balance.Created, 9);
        Assert.Equal(0.0, balance.Destroyed, 9);
        Assert.Equal(1.0, balance.Held, 9);
        Assert.Equal(0.25, buffer.MassHeld, 9);
        Assert.Equal(0.75, feeder.MassHeld, 9);
    }

    [Fact]
    public void APlantWithoutFlowNodesHasAnEmptyBalance()
    {
        Simulation sim = new SimulationBuilder(Options()).Add(new ConstantSource("S", 1.0)).Build();

        sim.RunFor(TimeSpan.FromMilliseconds(30));

        Assert.Equal(default, sim.MassBalance);
    }

    private sealed class FeedLine : CompositeComponent
    {
        public FeedLine(string id, MaterialType type)
            : base(id)
        {
            BulkFeeder feeder = AddChild(new BulkFeeder("Feeder", type, 10.0));
            Sink = AddChild(new BulkSink("Sink"));
            feeder.Out.ConnectTo(Sink.In);
        }

        public BulkSink Sink { get; }
    }
}
