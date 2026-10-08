using Millrace.Core.Contexts;
using Millrace.Core.Flow;
using Millrace.Core.Tests.Fakes;
using Millrace.Core.Tests.Fakes.Flow;
using Xunit;

namespace Millrace.Core.Tests;

public class FlowNodeTests
{
    private static readonly MaterialType Ore = new("Ore", PayloadKind.Bulk);
    private static readonly MaterialType Wheel = new("Wheel", PayloadKind.Discrete);

    [Fact]
    public void FlowComponentBaseDefaultsToNoCreationNoDestructionAndNoValidationErrors()
    {
        var buffer = new BulkBuffer("B", capacityKg: 10.0);

        Assert.Equal(0.0, buffer.MassCreated);
        Assert.Equal(0.0, buffer.MassDestroyed);
        Assert.Empty(buffer.ValidateFlow(0.01));
        buffer.Advance(TestContexts.Tick(0));
        buffer.Evaluate(TestContexts.Tick(0));
        Assert.Equal(0.0, buffer.MassHeld);
    }

    [Fact]
    public void FlowPortsAreRegisteredWithTheOwnerId()
    {
        var buffer = new BulkBuffer("B", 10.0);

        Assert.Equal(new[] { "In", "Out" }, buffer.Ports.Select(p => p.Name));
        Assert.All(buffer.Ports, p => Assert.Equal("B", p.OwnerId));
        Assert.Equal(PayloadKind.Bulk, buffer.In.Kind);
    }

    [Fact]
    public void BulkFeederCreatesRateTimesDtEachTickAndCountsIt()
    {
        var feeder = new BulkFeeder("F", Ore, rateKgPerSecond: 10.0);

        for (int tick = 0; tick < 5; tick++)
        {
            feeder.Evaluate(TestContexts.Tick(tick));
        }

        Assert.Equal(0.5, feeder.MassCreated, 9);
        Assert.Equal(0.5, feeder.MassHeld, 9);
        Assert.Equal(0.5, feeder.OfferMass(feeder.Out), 9);
    }

    [Fact]
    public void BulkFeederWithdrawHandsOverMassAndKeepsTheRest()
    {
        var feeder = new BulkFeeder("F", Ore, 100.0);
        feeder.Evaluate(TestContexts.Tick(0));

        BulkLot lot = feeder.Withdraw(feeder.Out, 0.25);

        Assert.Equal(0.25, lot.Mass, 9);
        Assert.Same(Ore, lot.Type);
        Assert.Equal(0.75, feeder.MassHeld, 9);
    }

    [Fact]
    public void BulkBufferAcceptsUpToItsCapacity()
    {
        var buffer = new BulkBuffer("B", 4.0);

        Assert.Equal(4.0, buffer.AcceptMass(buffer.In));
        buffer.Deposit(buffer.In, BulkLot.Of(Ore, 3.0, default));
        Assert.Equal(1.0, buffer.AcceptMass(buffer.In), 9);
        Assert.Equal(3.0, buffer.OfferMass(buffer.Out), 9);
    }

    [Fact]
    public void BulkSinkDestroysEverythingItReceives()
    {
        var sink = new BulkSink("S");

        sink.Deposit(sink.In, BulkLot.Of(Ore, 2.0, new MaterialProperties(1.0, 0.5, 9.0)));
        sink.Deposit(sink.In, BulkLot.Of(Ore, 3.0, default));

        Assert.Equal(double.PositiveInfinity, sink.AcceptMass(sink.In));
        Assert.Equal(5.0, sink.MassDestroyed, 9);
        Assert.Equal(new[] { 2.0, 3.0 }, sink.Received);
        Assert.Equal(0.0, sink.MassHeld);
    }

    [Fact]
    public void ItemFeederMintsIdsFromTheContextSequence()
    {
        var feeder = new ItemFeeder("F", Wheel, itemMassKg: 2.0, intervalSeconds: 0.02);
        feeder.Initialize(TestContexts.Init("F", dt: 0.01));

        for (int tick = 0; tick < 6; tick++)
        {
            feeder.Evaluate(TestContexts.Tick(tick));
        }

        Assert.True(feeder.TryPeekItem(feeder.Out, out ItemInstance? head));
        Assert.Equal(1, head!.Id);
        Assert.Same(head, feeder.WithdrawItem(feeder.Out));
        Assert.True(feeder.TryPeekItem(feeder.Out, out ItemInstance? next));
        Assert.Equal(2, next!.Id);
        Assert.Equal(6.0, feeder.MassCreated, 9);
        Assert.Equal(4.0, feeder.MassHeld, 9);
    }

    [Fact]
    public void ItemBufferAcceptsUpToItsCountAndKeepsOrder()
    {
        var buffer = new ItemBuffer("B", capacity: 2);
        var first = new ItemInstance(1, Wheel, 1.0, default);
        var second = new ItemInstance(2, Wheel, 1.0, default);
        var third = new ItemInstance(3, Wheel, 1.0, default);

        Assert.True(buffer.CanAcceptItem(buffer.In, first));
        buffer.DepositItem(buffer.In, first);
        buffer.DepositItem(buffer.In, second);

        Assert.False(buffer.CanAcceptItem(buffer.In, third));
        Assert.True(buffer.TryPeekItem(buffer.Out, out ItemInstance? head));
        Assert.Same(first, head);
        Assert.Equal(2.0, buffer.MassHeld, 9);
    }

    [Fact]
    public void ItemSinkDestroysItemMass()
    {
        var sink = new ItemSink("S");
        var item = new ItemInstance(1, Wheel, 2.5, default);

        Assert.True(sink.CanAcceptItem(sink.In, item));
        sink.DepositItem(sink.In, item);

        Assert.Equal(2.5, sink.MassDestroyed, 9);
        Assert.Same(item, Assert.Single(sink.Items));
    }
}
