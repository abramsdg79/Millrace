using Millrace.Core.Contexts;
using Millrace.Core.Flow;
using Millrace.Core.Graph;
using Xunit;

namespace Millrace.Core.Tests;

public class FlowPortTests
{
    [Fact]
    public void ConnectLinksBothEnds()
    {
        var outlet = new FlowOutlet("Out", "A", PayloadKind.Bulk);
        var inlet = new FlowInlet("In", "B", PayloadKind.Bulk);

        outlet.ConnectTo(inlet);

        Assert.True(outlet.IsConnected);
        Assert.True(inlet.IsConnected);
        Assert.Same(inlet, outlet.Target);
        Assert.Same(outlet, inlet.Source);
    }

    [Fact]
    public void KindMismatchThrowsNamingBothPorts()
    {
        var outlet = new FlowOutlet("Out", "A", PayloadKind.Bulk);
        var inlet = new FlowInlet("In", "B", PayloadKind.Discrete);

        InvalidOperationException error =
            Assert.Throws<InvalidOperationException>(() => outlet.ConnectTo(inlet));
        Assert.Contains("A.Out", error.Message, StringComparison.Ordinal);
        Assert.Contains("B.In", error.Message, StringComparison.Ordinal);
        Assert.False(outlet.IsConnected);
        Assert.False(inlet.IsConnected);
    }

    [Fact]
    public void AnOutletFeedsExactlyOneInlet()
    {
        var outlet = new FlowOutlet("Out", "A", PayloadKind.Bulk);
        outlet.ConnectTo(new FlowInlet("In", "B", PayloadKind.Bulk));

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => outlet.ConnectTo(new FlowInlet("In", "C", PayloadKind.Bulk)));
        Assert.Contains("A.Out", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnInletHasExactlyOneSource()
    {
        var inlet = new FlowInlet("In", "C", PayloadKind.Bulk);
        new FlowOutlet("Out", "A", PayloadKind.Bulk).ConnectTo(inlet);

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => new FlowOutlet("Out", "B", PayloadKind.Bulk).ConnectTo(inlet));
        Assert.Contains("C.In", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnOutletCannotFeedItsOwnComponent()
    {
        var node = new BareNode("A");

        InvalidOperationException error =
            Assert.Throws<InvalidOperationException>(() => node.Out.ConnectTo(node.In));
        Assert.Contains("A.Out", error.Message, StringComparison.Ordinal);
        Assert.Contains("A.In", error.Message, StringComparison.Ordinal);
        Assert.False(node.Out.IsConnected);
        Assert.False(node.In.IsConnected);
    }

    [Fact]
    public void FlowLinksDoNotOrderSignalEvaluation()
    {
        var producer = new BareNode("P");
        var consumer = new BareNode("C");
        producer.Out.ConnectTo(consumer.In);

        // Registered consumer-first; a signal edge would reorder them.
        Assert.True(GraphResolver.TryResolve([consumer, producer], out ISimComponent[] ordered, out _));

        Assert.Equal(new[] { "C", "P" }, ordered.Select(c => c.Id));
        Assert.Null(consumer.In.SourcePort);
        Assert.False(consumer.In.IsMissingRequiredConnection);
    }

    [Fact]
    public void FlowPortsAreQualifiedAndExposedByComposites()
    {
        var composite = new Wrapper("CV001");

        Assert.Equal("CV001.Node.In", composite.Inlet("Feed").QualifiedName);
        Assert.Equal("CV001.Node.Out", composite.Outlet("Discharge").QualifiedName);
        Assert.Throws<InvalidCastException>(() => composite.Input<double>("Feed"));
    }

    private sealed class BareNode : ComponentBase
    {
        public BareNode(string id)
            : base(id)
        {
            In = AddPort(new FlowInlet("In", Id, PayloadKind.Bulk));
            Out = AddPort(new FlowOutlet("Out", Id, PayloadKind.Bulk));
        }

        public FlowInlet In { get; }

        public FlowOutlet Out { get; }

        public override void Evaluate(in TickContext ctx)
        {
        }
    }

    private sealed class Wrapper : CompositeComponent
    {
        public Wrapper(string id)
            : base(id)
        {
            BareNode node = AddChild(new BareNode("Node"));
            Expose("Feed", node.In);
            Expose("Discharge", node.Out);
        }
    }
}
