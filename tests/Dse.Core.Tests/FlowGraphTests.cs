using System.Diagnostics.CodeAnalysis;
using Dse.Core.Flow;
using Dse.Core.Graph;
using Dse.Core.Tests.Fakes;
using Dse.Core.Tests.Fakes.Flow;
using Dse.Core.Validation;
using Xunit;

namespace Dse.Core.Tests;

public class FlowGraphTests
{
    private static readonly MaterialType Ore = new("Ore", PayloadKind.Bulk);
    private static readonly MaterialType Wheel = new("Wheel", PayloadKind.Discrete);

    private static IReadOnlySet<string> Ids(params IFlowNode[] nodes) =>
        nodes.Select(n => n.Id).ToHashSet(StringComparer.Ordinal);

    private static BulkFeeder FeederWith(double kilograms)
    {
        var feeder = new BulkFeeder("F", Ore, rateKgPerSecond: kilograms);
        feeder.Evaluate(TestContexts.Tick(0, dt: 1.0));
        return feeder;
    }

    [Fact]
    public void BulkMovesTheMinimumOfOfferAndAccept()
    {
        BulkFeeder feeder = FeederWith(10.0);
        var buffer = new BulkBuffer("B", capacityKg: 4.0);
        feeder.Out.ConnectTo(buffer.In);

        FlowGraph.Build([feeder, buffer]).Step(1.0);

        Assert.Equal(4.0, buffer.MassHeld, 9);
        Assert.Equal(6.0, feeder.MassHeld, 9);
    }

    [Fact]
    public void DownstreamNodesResolveFirstWhateverTheRegistrationOrder()
    {
        BulkFeeder feeder = FeederWith(10.0);
        var buffer = new BulkBuffer("B", 4.0);
        var sink = new BulkSink("S");
        feeder.Out.ConnectTo(buffer.In);
        buffer.Out.ConnectTo(sink.In);

        FlowGraph graph = FlowGraph.Build([sink, buffer, feeder]);

        Assert.Equal(new[] { "S", "B", "F" }, graph.Nodes.Select(n => n.Id));

        graph.Step(1.0);
        Assert.Equal(0.0, sink.TotalReceived);
        Assert.Equal(4.0, buffer.MassHeld, 9);

        // The buffer drains into the sink before the feeder refills it.
        graph.Step(1.0);
        Assert.Equal(4.0, sink.TotalReceived, 9);
        Assert.Equal(4.0, buffer.MassHeld, 9);
        Assert.Equal(2.0, feeder.MassHeld, 9);
    }

    [Fact]
    public void ItemsMoveWholeInOrderUntilTheConsumerIsFull()
    {
        var feeder = new ItemFeeder("F", Wheel, itemMassKg: 1.0, intervalSeconds: 1.0);
        feeder.Initialize(TestContexts.Init("F", dt: 1.0));
        for (int tick = 0; tick < 3; tick++)
        {
            feeder.Evaluate(TestContexts.Tick(tick, dt: 1.0));
        }

        var buffer = new ItemBuffer("B", capacity: 2);
        feeder.Out.ConnectTo(buffer.In);

        FlowGraph.Build([feeder, buffer]).Step(1.0);

        Assert.Equal(2, buffer.Count);
        Assert.Equal(1.0, feeder.MassHeld, 9);
        Assert.True(buffer.TryPeekItem(buffer.Out, out ItemInstance? head));
        Assert.Equal(1, head!.Id);
    }

    [Fact]
    public void AProducerThatWithdrawsTheWrongItemIsRejected()
    {
        var cheat = new CheatingProducer("C");
        var sink = new ItemSink("S");
        cheat.Out.ConnectTo(sink.In);

        InvalidOperationException error =
            Assert.Throws<InvalidOperationException>(() => FlowGraph.Build([cheat, sink]).Step(1.0));
        Assert.Contains("C", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EmptyGraphStepsWithoutError()
    {
        FlowGraph graph = FlowGraph.Build([]);

        graph.Step(1.0);

        Assert.Empty(graph.Nodes);
        Assert.Same(FlowGraph.Empty, graph);
    }

    [Fact]
    public void ValidateReportsRecirculation()
    {
        var a = new BulkBuffer("A", 1.0);
        var b = new BulkBuffer("B", 1.0);
        a.Out.ConnectTo(b.In);
        b.Out.ConnectTo(a.In);

        IReadOnlyList<ValidationError> errors = FlowGraph.Validate([a, b], Ids(a, b), 0.01);

        ValidationError error = Assert.Single(errors);
        Assert.Equal("DSE005", error.Code);
        Assert.Contains("A", error.ComponentIds);
        Assert.Contains("B", error.ComponentIds);
    }

    [Fact]
    public void ValidateReportsAnInletFedFromOutsideThePlant()
    {
        var feeder = new BulkFeeder("F", Ore, 1.0);
        var buffer = new BulkBuffer("B", 1.0);
        feeder.Out.ConnectTo(buffer.In);

        ValidationError error = Assert.Single(FlowGraph.Validate([buffer], Ids(buffer), 0.01));

        Assert.Equal("DSE007", error.Code);
        Assert.Contains("F.Out", error.Message, StringComparison.Ordinal);
        Assert.Contains("B.In", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateReportsAFlowPortOnAComponentThatIsNotAFlowNode()
    {
        var producer = new SignalOnlyProducer("P");
        var buffer = new BulkBuffer("B", 1.0);
        producer.Out.ConnectTo(buffer.In);
        IReadOnlySet<string> plantIds = new HashSet<string>(StringComparer.Ordinal) { "P", "B" };

        ValidationError error = Assert.Single(FlowGraph.Validate([buffer], plantIds, 0.01));

        Assert.Equal("DSE008", error.Code);
        Assert.Contains(nameof(IFlowNode), error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateReportsAConnectedPortWithoutItsContract()
    {
        var mute = new MuteNode("M");
        var buffer = new BulkBuffer("B", 1.0);
        mute.Out.ConnectTo(buffer.In);

        ValidationError error = Assert.Single(FlowGraph.Validate([mute, buffer], Ids(mute, buffer), 0.01));

        Assert.Equal("DSE008", error.Code);
        Assert.Contains(nameof(IBulkProducer), error.Message, StringComparison.Ordinal);
        Assert.Contains("M.Out", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateCollectsNodeSpecificErrors()
    {
        var picky = new PickyNode("P");

        ValidationError error = Assert.Single(FlowGraph.Validate([picky], Ids(picky), 0.01));

        Assert.Equal("DSE999", error.Code);
    }

    [Fact]
    public void ValidPlantHasNoErrors()
    {
        BulkFeeder feeder = FeederWith(1.0);
        var sink = new BulkSink("S");
        feeder.Out.ConnectTo(sink.In);

        Assert.Empty(FlowGraph.Validate([feeder, sink], Ids(feeder, sink), 0.01));
    }

    [Fact]
    public void BuildRefusesAnUnvalidatedLoop()
    {
        var a = new BulkBuffer("A", 1.0);
        var b = new BulkBuffer("B", 1.0);
        a.Out.ConnectTo(b.In);
        b.Out.ConnectTo(a.In);

        Assert.Throws<InvalidOperationException>(() => FlowGraph.Build([a, b]));
    }

    /// <summary>Shows one item but hands over another — the contract violation the engine must catch.</summary>
    private sealed class CheatingProducer : FlowComponentBase, IItemProducer
    {
        private readonly ItemInstance _shown;
        private readonly ItemInstance _given;

        public CheatingProducer(string id)
            : base(id)
        {
            _shown = new ItemInstance(1, Wheel, 1.0, default);
            _given = new ItemInstance(2, Wheel, 1.0, default);
            Out = AddOutlet("Out", PayloadKind.Discrete);
        }

        public FlowOutlet Out { get; }

        public override double MassHeld => 2.0;

        public bool TryPeekItem(FlowOutlet outlet, [NotNullWhen(true)] out ItemInstance? item)
        {
            item = _shown;
            return true;
        }

        public ItemInstance WithdrawItem(FlowOutlet outlet) => _given;
    }

    private sealed class SignalOnlyProducer : ComponentBase
    {
        public SignalOnlyProducer(string id)
            : base(id) => Out = AddPort(new FlowOutlet("Out", Id, PayloadKind.Bulk));

        public FlowOutlet Out { get; }

        public override void Evaluate(in Contexts.TickContext ctx)
        {
        }
    }

    private sealed class MuteNode : FlowComponentBase
    {
        public MuteNode(string id)
            : base(id) => Out = AddOutlet("Out", PayloadKind.Bulk);

        public FlowOutlet Out { get; }

        public override double MassHeld => 0.0;
    }

    private sealed class PickyNode : FlowComponentBase
    {
        public PickyNode(string id)
            : base(id)
        {
        }

        public override double MassHeld => 0.0;

        public override IEnumerable<ValidationError> ValidateFlow(double dt) =>
            [new ValidationError("DSE999", "Picky.", [Id])];
    }
}
