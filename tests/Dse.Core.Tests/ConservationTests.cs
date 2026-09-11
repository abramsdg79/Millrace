using System.Globalization;
using Dse.Core.Flow;
using Dse.Core.Tests.Fakes;
using Dse.Core.Tests.Fakes.Flow;
using Dse.Core.Time;
using Xunit;

namespace Dse.Core.Tests;

public class ConservationTests
{
    private static readonly MaterialType Ore = new("Ore", PayloadKind.Bulk);

    private static BulkFeeder FeederWith(double kilograms)
    {
        var feeder = new BulkFeeder("F", Ore, rateKgPerSecond: kilograms);
        feeder.Evaluate(TestContexts.Tick(0, dt: 1.0));
        return feeder;
    }

    [Fact]
    public void BalanceSumsCreatedDestroyedAndHeld()
    {
        BulkFeeder feeder = FeederWith(10.0);
        var buffer = new BulkBuffer("B", 4.0);
        var sink = new BulkSink("S");
        feeder.Out.ConnectTo(buffer.In);
        buffer.Out.ConnectTo(sink.In);
        FlowGraph graph = FlowGraph.Build([feeder, buffer, sink]);

        graph.Step(1.0);
        Assert.Equal(new MassBalance(10.0, 0.0, 10.0), graph.Balance());

        graph.Step(1.0);
        MassBalance balance = graph.Balance();
        Assert.Equal(10.0, balance.Created, 9);
        Assert.Equal(4.0, balance.Destroyed, 9);
        Assert.Equal(6.0, balance.Held, 9);
        Assert.Equal(0.0, balance.Drift, 9);
    }

    [Fact]
    public void AConservingGraphPassesTheAudit()
    {
        BulkFeeder feeder = FeederWith(10.0);
        var sink = new BulkSink("S");
        feeder.Out.ConnectTo(sink.In);
        FlowGraph graph = FlowGraph.Build([feeder, sink]);

        graph.Step(1.0);
        graph.AssertConserved(tick: 0, relativeTolerance: 1e-9);
    }

    [Fact]
    public void ALeakingNodeTripsTheAudit()
    {
        BulkFeeder feeder = FeederWith(10.0);
        var leaky = new LeakyBuffer("L", keepFraction: 0.5);
        feeder.Out.ConnectTo(leaky.In);
        FlowGraph graph = FlowGraph.Build([feeder, leaky]);

        graph.Step(1.0);
        MassConservationException error = Assert.Throws<MassConservationException>(
            () => graph.AssertConserved(tick: 3, relativeTolerance: 1e-9));

        Assert.Equal(3, error.Tick);
        Assert.Equal(5.0, error.Balance.Drift, 9);
        Assert.Contains("tick 3", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ToleranceScalesWithTheMassSourced()
    {
        BulkFeeder feeder = FeederWith(1_000_000.0);
        var leaky = new LeakyBuffer("L", keepFraction: 1.0 - 1e-6);
        feeder.Out.ConnectTo(leaky.In);
        FlowGraph graph = FlowGraph.Build([feeder, leaky]);
        graph.Step(1.0);

        // Drift is about 1 kg on 1,000,000 kg sourced.
        Assert.Throws<MassConservationException>(() => graph.AssertConserved(0, 1e-9));
        graph.AssertConserved(0, 1e-5);
    }

    [Fact]
    public void ExceptionMessageIsCultureInvariant()
    {
        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            var error = new MassConservationException(1, new MassBalance(2.5, 0.0, 0.0));

            Assert.Contains("2.5", error.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("2,5", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void OptionsDefaultToCheckingWithATightTolerance()
    {
        var options = new SimulationOptions();

        Assert.True(options.CheckConservation);
        Assert.Equal(1e-9, options.ConservationTolerance);
    }
}
