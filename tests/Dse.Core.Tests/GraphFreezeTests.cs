using Dse.Core.Graph;
using Dse.Core.Tests.Fakes;
using Dse.Core.Time;
using Xunit;

namespace Dse.Core.Tests;

public class GraphFreezeTests
{
    private static SimulationOptions Options => new() { Seed = 1UL };

    [Fact]
    public void PortsAreFrozenByBuild()
    {
        var source = new ConstantSource("S", 1.0);
        var recorder = new Recorder("R");
        source.Out.ConnectTo(recorder.In);
        var builder = new SimulationBuilder(Options).Add(source).Add(recorder);

        Assert.False(recorder.In.IsFrozen);
        builder.Build();

        Assert.True(recorder.In.IsFrozen);
        Assert.True(source.Out.IsFrozen);
    }

    [Fact]
    public void WiringAfterBuildThrows()
    {
        var source = new ConstantSource("S", 1.0);
        var recorder = new Recorder("R");
        var spare = new Recorder("Spare");
        source.Out.ConnectTo(recorder.In);
        new SimulationBuilder(Options).Add(source).Add(recorder).Add(spare).Build();

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => source.Out.ConnectTo(spare.In));
        Assert.Contains("built", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AddingAfterBuildThrows()
    {
        var builder = new SimulationBuilder(Options).Add(new ConstantSource("S", 1.0));
        builder.Build();

        Assert.Throws<InvalidOperationException>(() => builder.Add(new ConstantSource("T", 2.0)));
    }

    [Fact]
    public void FreezeIsIdempotentAndBuildCanBeCalledOnce()
    {
        var builder = new SimulationBuilder(Options).Add(new ConstantSource("S", 1.0));
        builder.Build();

        Assert.Throws<InvalidOperationException>(() => builder.Build());
    }
}
