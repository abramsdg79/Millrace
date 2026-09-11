using Dse.Core.Graph;
using Dse.Core.Tests.Fakes;
using Xunit;

namespace Dse.Core.Tests;

public class GraphResolverTests
{
    [Fact]
    public void OrdersProducersBeforeConsumers()
    {
        var source = new ConstantSource("S", 1.0);
        var gain = new Gain("G", 2.0);
        var recorder = new Recorder("R");
        source.Out.ConnectTo(gain.In);
        gain.Out.ConnectTo(recorder.In);

        // Deliberately registered in reverse dependency order.
        bool resolved = GraphResolver.TryResolve(
            [recorder, gain, source], out ISimComponent[] ordered, out _);

        Assert.True(resolved);
        Assert.Equal(new[] { "S", "G", "R" }, ordered.Select(c => c.Id));
    }

    [Fact]
    public void IndependentComponentsKeepRegistrationOrder()
    {
        var first = new ConstantSource("A", 1.0);
        var second = new ConstantSource("B", 2.0);

        Assert.True(GraphResolver.TryResolve([first, second], out ISimComponent[] ordered, out _));

        Assert.Equal(new[] { "A", "B" }, ordered.Select(c => c.Id));
    }

    [Fact]
    public void ReportsACycleWhenNothingBreaksTheLoop()
    {
        var left = new Gain("L", 1.0);
        var right = new Gain("R", 1.0);
        left.Out.ConnectTo(right.In);
        right.Out.ConnectTo(left.In);

        bool resolved = GraphResolver.TryResolve(
            [left, right], out _, out IReadOnlyList<string> cycle);

        Assert.False(resolved);
        Assert.Contains("L", cycle);
        Assert.Contains("R", cycle);
    }

    [Fact]
    public void ReportsOnlyTheComponentsInTheCycleWhenAnObserverHangsOffIt()
    {
        var x = new Recorder("X");
        var b = new Gain("B", 1.0);
        var c = new Gain("C", 1.0);
        b.Out.ConnectTo(x.In);
        b.Out.ConnectTo(c.In);
        c.Out.ConnectTo(b.In);

        bool resolved = GraphResolver.TryResolve(
            [x, b, c], out _, out IReadOnlyList<string> cycle);

        Assert.False(resolved);
        Assert.Contains("B", cycle);
        Assert.Contains("C", cycle);
        Assert.DoesNotContain("X", cycle);
        Assert.Equal(2, cycle.Count);
    }

    [Fact]
    public void AUnitDelayBreaksTheLoop()
    {
        var gain = new Gain("G", 0.5);
        var delay = new UnitDelay<double>("D");
        gain.Out.ConnectTo(delay.In);
        delay.Out.ConnectTo(gain.In);

        bool resolved = GraphResolver.TryResolve(
            [gain, delay], out ISimComponent[] ordered, out _);

        Assert.True(resolved);
        Assert.Equal(new[] { "D", "G" }, ordered.Select(c => c.Id));
    }

    [Fact]
    public void ResolvesADiamond()
    {
        var source = new ConstantSource("S", 1.0);
        var left = new Gain("L", 2.0);
        var right = new Gain("R", 3.0);
        var sink = new Recorder("K");
        source.Out.ConnectTo(left.In);
        source.Out.ConnectTo(right.In);
        left.Out.ConnectTo(sink.In);

        Assert.True(GraphResolver.TryResolve(
            [sink, left, right, source], out ISimComponent[] ordered, out _));

        List<string> ids = ordered.Select(c => c.Id).ToList();
        Assert.True(ids.IndexOf("S") < ids.IndexOf("L"));
        Assert.True(ids.IndexOf("S") < ids.IndexOf("R"));
        Assert.True(ids.IndexOf("L") < ids.IndexOf("K"));
    }
}
