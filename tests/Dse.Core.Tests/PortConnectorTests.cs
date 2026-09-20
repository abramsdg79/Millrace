using Dse.Core.Flow;
using Dse.Core.Graph;

namespace Dse.Core.Tests;

public class PortConnectorTests
{
    [Fact]
    public void ConnectsAnOutputToAnInputOfTheSameType()
    {
        var a = new UnitDelay<bool>("A");
        var b = new UnitDelay<bool>("B");

        bool ok = PortConnector.TryConnect(a.Out, b.In, out string problem);

        Assert.True(ok, problem);
        Assert.Empty(problem);
        a.Out.Value = true;
        Assert.True(b.In.Value);
    }

    [Fact]
    public void RefusesMismatchedValueTypesAndNamesBoth()
    {
        var a = new UnitDelay<bool>("A");
        var b = new UnitDelay<double>("B");

        bool ok = PortConnector.TryConnect(a.Out, b.In, out string problem);

        Assert.False(ok);
        Assert.Contains("A.Out", problem, StringComparison.Ordinal);
        Assert.Contains("B.In", problem, StringComparison.Ordinal);
        Assert.Contains("Boolean", problem, StringComparison.Ordinal);
        Assert.Contains("Double", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void RefusesAnInputAsTheSource()
    {
        var a = new UnitDelay<bool>("A");
        var b = new UnitDelay<bool>("B");

        bool ok = PortConnector.TryConnect(a.In, b.In, out string problem);

        Assert.False(ok);
        Assert.Contains("'A.In' is an input", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void RefusesAnOutputAsTheTarget()
    {
        var a = new UnitDelay<bool>("A");
        var b = new UnitDelay<bool>("B");

        bool ok = PortConnector.TryConnect(a.Out, b.Out, out string problem);

        Assert.False(ok);
        Assert.Contains("'B.Out' is an output", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void ReportsASecondDriverInsteadOfThrowing()
    {
        var a = new UnitDelay<bool>("A");
        var b = new UnitDelay<bool>("B");
        var c = new UnitDelay<bool>("C");
        Assert.True(PortConnector.TryConnect(a.Out, c.In, out _));

        bool ok = PortConnector.TryConnect(b.Out, c.In, out string problem);

        Assert.False(ok);
        Assert.Contains("already driven by 'A.Out'", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void ConnectsAnOutletToAnInletOfTheSameKind()
    {
        var outlet = new FlowOutlet("Out", "Up", PayloadKind.Bulk);
        var inlet = new FlowInlet("In", "Down", PayloadKind.Bulk);

        bool ok = PortConnector.TryConnect(outlet, inlet, out string problem);

        Assert.True(ok, problem);
        Assert.True(outlet.IsConnected);
        Assert.True(inlet.IsConnected);
    }

    [Fact]
    public void ReportsAPayloadKindMismatch()
    {
        var outlet = new FlowOutlet("Out", "Up", PayloadKind.Bulk);
        var inlet = new FlowInlet("In", "Down", PayloadKind.Discrete);

        bool ok = PortConnector.TryConnect(outlet, inlet, out string problem);

        Assert.False(ok);
        Assert.Contains("Bulk outlet", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void RefusesASignalPortOnAFlowLink()
    {
        var outlet = new FlowOutlet("Out", "Up", PayloadKind.Bulk);
        var b = new UnitDelay<bool>("B");

        bool ok = PortConnector.TryConnect(outlet, b.In, out string problem);

        Assert.False(ok);
        Assert.Contains("material", problem, StringComparison.Ordinal);
    }

    private sealed class Pair : CompositeComponent
    {
        public Pair(string id)
            : base(id)
        {
            First = AddChild(new UnitDelay<bool>("First"));
            Second = AddChild(new UnitDelay<bool>("Second"));
            First.Out.ConnectTo(Second.In);
            Expose("Zed", Second.Out);
            Expose("Alpha", First.In);
        }

        public UnitDelay<bool> First { get; }

        public UnitDelay<bool> Second { get; }
    }

    [Fact]
    public void ACompositeListsItsAliasesSortedAndItsLeavesInChildOrder()
    {
        var pair = new Pair("P");

        Assert.Equal(["Alpha", "Zed"], pair.ExposedPorts.Select(e => e.Key));
        Assert.Same(pair.First.In, pair.ExposedPorts[0].Value);
        Assert.Equal(["P.First", "P.Second"], pair.LeafComponents.Select(l => l.Id));
    }
}
