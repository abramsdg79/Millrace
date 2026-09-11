using Dse.Core.Contexts;
using Dse.Core.Graph;
using Dse.Core.Logging;
using Dse.Core.Randomness;
using Dse.Core.Telemetry;
using Dse.Core.Tests.Fakes;
using Xunit;

namespace Dse.Core.Tests;

public class ComponentTests
{
    private static readonly DateTimeOffset Start =
        new(2026, 1, 1, 6, 0, 0, TimeSpan.Zero);

    [Fact]
    public void PortsAreRegisteredInDeclarationOrderWithTheOwnerId()
    {
        var gain = new Gain("G1", factor: 2.0);

        Assert.Equal(new[] { "In", "Out" }, gain.Ports.Select(p => p.Name));
        Assert.All(gain.Ports, p => Assert.Equal("G1", p.OwnerId));
    }

    [Fact]
    public void ComponentsDefaultToDirectFeedthrough()
    {
        Assert.True(new Gain("G1", 2.0).HasDirectFeedthrough);
    }

    [Fact]
    public void EvaluateReadsInputsAndWritesOutputs()
    {
        var source = new ConstantSource("S1", 21.0);
        var gain = new Gain("G1", factor: 2.0);
        source.Out.ConnectTo(gain.In);

        TickContext tick = NewTickContext();
        source.Evaluate(tick);
        gain.Evaluate(tick);

        Assert.Equal(42.0, gain.Out.Value);
    }

    [Fact]
    public void IntegratorAccumulatesUsingTheTimeStep()
    {
        var source = new ConstantSource("S1", 2.0);
        var integrator = new Integrator("I1");
        source.Out.ConnectTo(integrator.In);

        for (long tick = 0; tick < 100; tick++)
        {
            TickContext context = NewTickContext(tick);
            source.Evaluate(context);
            integrator.Evaluate(context);
        }

        // 2.0 units/second integrated for 100 x 10 ms = 1 s.
        Assert.Equal(2.0, integrator.Out.Value, 9);
    }

    [Fact]
    public void RegisterTelemetryPrefixesTheComponentId()
    {
        var registry = new TelemetryRegistry();
        var gain = new Gain("CV001.Gain", factor: 3.0);

        gain.Initialize(NewInitContext(gain.Id, registry));

        Assert.Equal(new[] { "CV001.Gain.Out" }, registry.Channels.Select(c => c.Key));
    }

    [Fact]
    public void LoggingThroughTheTickContextLandsInTheEventLog()
    {
        var log = new EventLog();
        var context = new TickContext(5, 0.01, Start.AddSeconds(0.05), log);

        context.Log("G1", "NOTE", "hello");

        Assert.Single(log.Records);
        Assert.Equal("G1", log.Records[0].Source);
        Assert.Equal(5, log.Records[0].Tick);
    }

    [Fact]
    public void BlankComponentIdIsRejected()
    {
        Assert.Throws<ArgumentException>(() => new Gain(" ", 1.0));
    }

    private static TickContext NewTickContext(long tick = 0) =>
        new(tick, 0.01, Start + TimeSpan.FromMilliseconds(10 * tick), new EventLog());

    private static InitContext NewInitContext(string componentId, TelemetryRegistry registry) =>
        new(new DeterministicRandom(1UL), registry, componentId, Start, 0.01);
}
