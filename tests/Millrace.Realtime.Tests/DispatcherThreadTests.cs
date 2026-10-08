using System.Diagnostics;
using Millrace.Realtime.Tests.Fakes;

namespace Millrace.Realtime.Tests;

public class DispatcherThreadTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private static void WaitUntil(Func<bool> condition)
    {
        Stopwatch clock = Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(clock.Elapsed < Patience, "Timed out waiting for the dispatcher.");
            Thread.Sleep(1);
        }
    }

    [Fact]
    public void PumpsFramesInTheBackground()
    {
        using var hub = new RealtimeHub(Frames.Directory(), ringCapacity: 1 << 14);
        using Subscription sub = hub.Subscribe(new SubscriptionOptions { Policy = BackpressurePolicy.Lossless, Capacity = 10_000 });
        using var dispatcher = new DispatcherThread(hub);
        dispatcher.Start();

        for (int t = 0; t < 1_000; t++)
        {
            hub.Publish(Frames.Frame(t, t + 1.0, false, 0, dirty: [Frames.Speed]));
        }

        WaitUntil(() => hub.State.Tick == 999);

        Assert.True(dispatcher.IsRunning);
        Assert.Equal(0L, hub.DroppedFrames);
        WaitUntil(() => sub.Queued == 1_000);
        long expected = 0;
        while (sub.TryRead(out FrameDelta delta))
        {
            Assert.Equal(expected++, delta.Tick);
        }

        Assert.Equal(1_000L, expected);
        Assert.Null(dispatcher.Failure);
    }

    [Fact]
    public void DisposeStopsPromptlyWhenIdle()
    {
        using var hub = new RealtimeHub(Frames.Directory());
        var dispatcher = new DispatcherThread(hub, idleWait: TimeSpan.FromMilliseconds(20));
        dispatcher.Start();
        WaitUntil(() => dispatcher.IsRunning);

        Stopwatch clock = Stopwatch.StartNew();
        dispatcher.Dispose();

        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(2));
        Assert.False(dispatcher.IsRunning);
    }

    [Fact]
    public void DisposeDrainsWhatWasPublishedBeforeTheStop()
    {
        using var hub = new RealtimeHub(Frames.Directory());
        var dispatcher = new DispatcherThread(hub, idleWait: TimeSpan.FromSeconds(5));
        dispatcher.Start();
        WaitUntil(() => dispatcher.IsRunning);

        hub.Publish(Frames.Full(0, 1.0, false, 0));
        hub.Publish(Frames.Full(1, 2.0, false, 0));
        dispatcher.Dispose();

        Assert.Equal(1L, hub.State.Tick);
        Assert.Equal(2L, dispatcher.PumpedFrames);
    }

    [Fact]
    public void StartTwiceThrows()
    {
        using var hub = new RealtimeHub(Frames.Directory());
        using var dispatcher = new DispatcherThread(hub);
        dispatcher.Start();

        Assert.Throws<InvalidOperationException>(dispatcher.Start);
    }
}
