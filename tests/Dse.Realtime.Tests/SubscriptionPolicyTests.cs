using Dse.Io;
using Dse.Realtime.Tests.Fakes;

namespace Dse.Realtime.Tests;

public class SubscriptionPolicyTests
{
    private static RealtimeHub Primed()
    {
        var hub = new RealtimeHub(Frames.Directory());
        hub.Publish(Frames.Full(0, 1.0, false, 0));
        hub.Pump();
        return hub;
    }

    [Fact]
    public void LosslessKeepsEveryDeltaInOrder()
    {
        using RealtimeHub hub = Primed();
        using Subscription sub = hub.Subscribe(new SubscriptionOptions { Policy = BackpressurePolicy.Lossless, Capacity = 10 });

        for (int t = 1; t <= 5; t++)
        {
            hub.Publish(Frames.Frame(t, 1.0 + t, false, 0, dirty: [Frames.Speed]));
        }

        hub.Pump();

        Assert.Equal(5, sub.Queued);
        for (int t = 1; t <= 5; t++)
        {
            Assert.True(sub.TryRead(out FrameDelta delta));
            Assert.Equal(1.0 + t, delta.Changes.Single().Value.AsDouble);
        }

        Assert.Equal(5L, sub.Delivered);
    }

    [Fact]
    public void LosslessFaultsAtCapacityInsteadOfDropping()
    {
        using RealtimeHub hub = Primed();
        using Subscription sub = hub.Subscribe(new SubscriptionOptions { Policy = BackpressurePolicy.Lossless, Capacity = 3 });

        for (int t = 1; t <= 4; t++)
        {
            hub.Publish(Frames.Frame(t, 1.0 + t, false, 0, dirty: [Frames.Speed]));
        }

        hub.Pump();

        Assert.True(sub.IsFaulted);
        Assert.Contains("3", sub.FaultReason, StringComparison.Ordinal);
        Assert.False(sub.TryRead(out _));
        Assert.Equal(0, sub.Queued);
    }

    [Fact]
    public void ConflateKeepsOnlyTheLatestValuePerTag()
    {
        using RealtimeHub hub = Primed();
        using Subscription sub = hub.Subscribe(new SubscriptionOptions { Policy = BackpressurePolicy.Conflate });

        hub.Publish(Frames.Frame(1, 2.0, false, 0, dirty: [Frames.Speed]));
        hub.Publish(Frames.Frame(2, 3.0, true, 0, dirty: [Frames.Speed, Frames.Run]));
        hub.Publish(Frames.Frame(3, 4.0, true, 0, dirty: [Frames.Speed]));
        hub.Pump();

        Assert.Equal(1, sub.Queued);
        Assert.True(sub.TryRead(out FrameDelta delta));
        Assert.Equal(3L, delta.Tick);
        Assert.Equal(Frames.TimeOf(3), delta.SimTime);
        Assert.Equal(4.0, delta.Changes.Single(c => c.Index == Frames.Speed).Value.AsDouble);
        Assert.True(delta.Changes.Single(c => c.Index == Frames.Run).Value.AsBool);
        Assert.Equal(2, delta.Changes.Count);
        Assert.False(sub.TryRead(out _));
        Assert.False(sub.IsFaulted);
    }

    [Fact]
    public void ConflateAccumulatesEventsUpToCapacityThenCountsDrops()
    {
        using RealtimeHub hub = Primed();
        using Subscription sub = hub.Subscribe(new SubscriptionOptions { Policy = BackpressurePolicy.Conflate, Capacity = 2 });

        hub.Publish(Frames.Frame(1, 1.0, false, 0, [], Frames.Event(1, "A", "E1")));
        hub.Publish(Frames.Frame(2, 1.0, false, 0, [], Frames.Event(2, "A", "E2"), Frames.Event(2, "A", "E3")));
        hub.Pump();

        Assert.True(sub.TryRead(out FrameDelta delta));
        Assert.Equal(new[] { "E1", "E2" }, delta.Events.Select(e => e.Code));
        Assert.Equal(1L, sub.DroppedEvents);
        Assert.False(sub.IsFaulted);
    }

    [Fact]
    public void AvailableIsSetWhileSomethingIsReadable()
    {
        using RealtimeHub hub = Primed();
        using Subscription sub = hub.Subscribe(new SubscriptionOptions { Policy = BackpressurePolicy.Lossless });

        Assert.False(sub.Available.WaitOne(0));
        hub.Publish(Frames.Frame(1, 2.0, false, 0, dirty: [Frames.Speed]));
        hub.Pump();
        Assert.True(sub.Available.WaitOne(0));

        Assert.True(sub.TryRead(out _));
        Assert.False(sub.Available.WaitOne(0));
    }

    [Fact]
    public void OffersAfterDisposeAreIgnored()
    {
        using RealtimeHub hub = Primed();
        Subscription sub = hub.Subscribe(new SubscriptionOptions { Policy = BackpressurePolicy.Lossless });
        sub.Dispose();

        hub.Publish(Frames.Frame(1, 2.0, false, 0, dirty: [Frames.Speed]));
        hub.Pump();

        Assert.False(sub.TryRead(out _));
        Assert.Equal(0, sub.Queued);
    }
}
