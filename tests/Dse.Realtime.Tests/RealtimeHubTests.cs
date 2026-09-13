using Dse.Io;
using Dse.Realtime.Tests.Fakes;

namespace Dse.Realtime.Tests;

public class RealtimeHubTests
{
    private static SubscriptionOptions Lossless => new() { Policy = BackpressurePolicy.Lossless };

    [Fact]
    public void PublishQueuesAndPumpApplies()
    {
        using var hub = new RealtimeHub(Frames.Directory());

        hub.Publish(Frames.Full(0, 1.0, true, 1));
        hub.Publish(Frames.Frame(1, 1.5, true, 1, dirty: [Frames.Speed]));

        Assert.Equal(2, hub.Pending);
        Assert.Equal(-1L, hub.State.Tick);
        Assert.Equal(2, hub.Pump());
        Assert.Equal(0, hub.Pending);
        Assert.Equal(1L, hub.State.Tick);
        Assert.Equal(1.5, hub.State[Frames.Speed].Value.AsDouble);
        Assert.Equal(2L, hub.PublishedFrames);
        Assert.Equal(0, hub.Pump());
    }

    [Fact]
    public void RingOverflowDropsTheNewestFrameAndCounts()
    {
        using var hub = new RealtimeHub(Frames.Directory(), ringCapacity: 2);

        hub.Publish(Frames.Full(0, 1.0, false, 0));
        hub.Publish(Frames.Full(1, 2.0, false, 0));
        hub.Publish(Frames.Full(2, 3.0, false, 0));

        Assert.Equal(2L, hub.PublishedFrames);
        Assert.Equal(1L, hub.DroppedFrames);
        hub.Pump();
        Assert.Equal(1L, hub.State.Tick);
        Assert.Equal(2.0, hub.State[Frames.Speed].Value.AsDouble);
    }

    [Fact]
    public void PublishNeverBlocksWhenTheRingIsFull()
    {
        using var hub = new RealtimeHub(Frames.Directory(), ringCapacity: 4);

        for (int i = 0; i < 10_000; i++)
        {
            hub.Publish(Frames.Full(i, i, false, 0));
        }

        Assert.Equal(4L, hub.PublishedFrames);
        Assert.Equal(9_996L, hub.DroppedFrames);
    }

    [Fact]
    public void SubscribeCapturesTheSnapshotAndDeliversOnlyLaterFrames()
    {
        using var hub = new RealtimeHub(Frames.Directory());
        hub.Publish(Frames.Full(0, 1.0, false, 0));
        hub.Publish(Frames.Frame(1, 1.1, false, 0, dirty: [Frames.Speed]));
        hub.Pump();

        using Subscription sub = hub.Subscribe(Lossless);

        Assert.Equal(1L, sub.Initial.Tick);
        Assert.Equal(1.1, sub.Initial.Tags.Span[Frames.Speed].Value.AsDouble);
        Assert.False(sub.TryRead(out _));

        hub.Publish(Frames.Frame(2, 1.2, false, 0, dirty: [Frames.Speed]));
        hub.Publish(Frames.Frame(3, 1.2, true, 0, dirty: [Frames.Run]));
        hub.Pump();

        Assert.True(sub.TryRead(out FrameDelta first));
        Assert.True(sub.TryRead(out FrameDelta second));
        Assert.False(sub.TryRead(out _));
        Assert.Equal((2L, Frames.Speed, 1.2), (first.Tick, first.Changes[0].Index, first.Changes[0].Value.AsDouble));
        Assert.Equal((3L, Frames.Run, true), (second.Tick, second.Changes[0].Index, second.Changes[0].Value.AsBool));
    }

    [Fact]
    public void FramesAlreadyInTheRingAtSubscribeTimeAreDelivered()
    {
        using var hub = new RealtimeHub(Frames.Directory());
        hub.Publish(Frames.Full(0, 1.0, false, 0));
        hub.Publish(Frames.Frame(1, 1.1, false, 0, dirty: [Frames.Speed]));

        using Subscription sub = hub.Subscribe(Lossless);
        Assert.Equal(-1L, sub.Initial.Tick);

        hub.Pump();

        Assert.True(sub.TryRead(out FrameDelta first));
        Assert.Equal(0L, first.Tick);
        Assert.Single(first.Changes);
        Assert.True(sub.TryRead(out FrameDelta second));
        Assert.Equal(1L, second.Tick);
    }

    [Fact]
    public void AFrameWithNoChangesAndNoEventsProducesNoDelta()
    {
        using var hub = new RealtimeHub(Frames.Directory());
        hub.Publish(Frames.Full(0, 1.0, false, 0));
        hub.Pump();
        using Subscription sub = hub.Subscribe(Lossless);

        hub.Publish(Frames.Quiet(1, 1.0, false, 0));
        hub.Publish(Frames.Frame(2, 1.0, false, 0, [], Frames.Event(2, "A", "PING")));
        hub.Pump();

        Assert.True(sub.TryRead(out FrameDelta delta));
        Assert.Equal(2L, delta.Tick);
        Assert.Empty(delta.Changes);
        Assert.Equal("PING", Assert.Single(delta.Events).Code);
        Assert.False(sub.TryRead(out _));
    }

    [Fact]
    public void ADirtyBitWithAnUnchangedValueIsNotAChangeForASubscriber()
    {
        using var hub = new RealtimeHub(Frames.Directory());
        hub.Publish(Frames.Full(0, 1.0, false, 0));
        hub.Pump();
        using Subscription sub = hub.Subscribe(Lossless);

        hub.Publish(Frames.Frame(1, 1.0, false, 0, dirty: [Frames.Speed]));
        hub.Pump();

        Assert.False(sub.TryRead(out _));
    }

    [Fact]
    public void LosslessSubscriptionFaultsOnARingGap()
    {
        using var hub = new RealtimeHub(Frames.Directory(), ringCapacity: 1);
        using Subscription lossless = hub.Subscribe(Lossless);
        using Subscription conflate = hub.Subscribe(new SubscriptionOptions { Policy = BackpressurePolicy.Conflate });

        hub.Publish(Frames.Full(0, 1.0, false, 0));
        hub.Publish(Frames.Full(1, 2.0, false, 0));
        hub.Pump();

        Assert.True(lossless.IsFaulted);
        Assert.Contains("ring overflow", lossless.FaultReason, StringComparison.Ordinal);
        Assert.False(lossless.TryRead(out _));
        Assert.True(lossless.Available.WaitOne(0));

        Assert.False(conflate.IsFaulted);
        Assert.True(conflate.TryRead(out FrameDelta delta));
        Assert.Equal(1.0, delta.Changes.Single(c => c.Index == Frames.Speed).Value.AsDouble);
    }

    [Fact]
    public void DisposeUnsubscribes()
    {
        using var hub = new RealtimeHub(Frames.Directory());
        Subscription sub = hub.Subscribe(Lossless);
        Assert.Equal(1, hub.SubscriberCount);

        sub.Dispose();

        Assert.Equal(0, hub.SubscriberCount);
        hub.Publish(Frames.Full(0, 1.0, false, 0));
        Assert.Equal(1, hub.Pump());
    }

    [Fact]
    public void FramesAvailableIsSignalledByPublish()
    {
        using var hub = new RealtimeHub(Frames.Directory());

        Assert.False(hub.FramesAvailable.WaitOne(0));
        hub.Publish(Frames.Full(0, 1.0, false, 0));
        Assert.True(hub.FramesAvailable.WaitOne(0));
    }

    [Fact]
    public void ProducerAndPumpOnDifferentThreadsLoseNothingWhenTheRingIsLargeEnough()
    {
        using var hub = new RealtimeHub(Frames.Directory(), ringCapacity: 1 << 16);
        using Subscription sub = hub.Subscribe(Lossless with { Capacity = 100_000 });
        const int frames = 50_000;
        int pumped = 0;

        var producer = new Thread(() =>
        {
            for (int i = 0; i < frames; i++)
            {
                hub.Publish(Frames.Frame(i, i + 1.0, false, 0, dirty: [Frames.Speed]));
            }
        });
        producer.Start();
        while (producer.IsAlive || hub.Pending > 0)
        {
            pumped += hub.Pump();
        }

        Assert.Equal(frames, pumped);
        Assert.Equal(0L, hub.DroppedFrames);
        long expected = 0;
        while (sub.TryRead(out FrameDelta delta))
        {
            Assert.Equal(expected++, delta.Tick);
        }

        Assert.Equal(frames, expected);
    }
}
