using Millrace.Io;
using Millrace.Realtime.Tests.Fakes;

namespace Millrace.Realtime.Tests;

public class SubscriptionFilterTests
{
    private static RealtimeHub Primed()
    {
        var hub = new RealtimeHub(Frames.Directory());
        hub.Publish(Frames.Full(0, 1.0, false, 0));
        hub.Pump();
        return hub;
    }

    private static SubscriptionOptions Lossless => new() { Policy = BackpressurePolicy.Lossless };

    private static List<FrameDelta> Drain(Subscription sub)
    {
        var deltas = new List<FrameDelta>();
        while (sub.TryRead(out FrameDelta delta))
        {
            deltas.Add(delta);
        }

        return deltas;
    }

    [Fact]
    public void PrefixesSelectWholeComponents()
    {
        using RealtimeHub hub = Primed();
        using Subscription sub = hub.Subscribe(Lossless with { Prefixes = ["A"] });

        hub.Publish(Frames.Frame(1, 2.0, true, 5, dirty: [Frames.Speed, Frames.Run, Frames.Count]));
        hub.Pump();

        FrameDelta delta = Assert.Single(Drain(sub));
        Assert.Equal(new[] { Frames.Speed, Frames.Run }, delta.Changes.Select(c => c.Index));
    }

    [Fact]
    public void APrefixMatchesWholeSegmentsOnly()
    {
        using RealtimeHub hub = Primed();
        using Subscription exact = hub.Subscribe(Lossless with { Prefixes = ["A.Run"] });
        using Subscription partial = hub.Subscribe(Lossless with { Prefixes = ["A.R"] });

        hub.Publish(Frames.Frame(1, 2.0, true, 5, dirty: [Frames.Speed, Frames.Run, Frames.Count]));
        hub.Pump();

        Assert.Equal(Frames.Run, Assert.Single(Drain(exact)).Changes.Single().Index);
        Assert.Empty(Drain(partial));
    }

    [Fact]
    public void FilteredOutTagsStillDeliverEvents()
    {
        using RealtimeHub hub = Primed();
        using Subscription sub = hub.Subscribe(Lossless with { Prefixes = ["B"] });

        hub.Publish(Frames.Frame(1, 2.0, false, 0, [Frames.Speed], Frames.Event(1, "A", "AT_SPEED")));
        hub.Pump();

        FrameDelta delta = Assert.Single(Drain(sub));
        Assert.Empty(delta.Changes);
        Assert.Equal("AT_SPEED", Assert.Single(delta.Events).Code);
    }

    [Fact]
    public void AnAbsoluteDeadbandComparesAgainstTheLastDeliveredValue()
    {
        using RealtimeHub hub = Primed();
        using Subscription sub = hub.Subscribe(Lossless with { Deadbands = new Dictionary<string, double> { ["A.Speed"] = 0.1 } });

        hub.Publish(Frames.Frame(1, 1.05, false, 0, dirty: [Frames.Speed]));
        hub.Publish(Frames.Frame(2, 1.11, false, 0, dirty: [Frames.Speed]));
        hub.Publish(Frames.Frame(3, 1.15, false, 0, dirty: [Frames.Speed]));
        hub.Publish(Frames.Frame(4, 1.30, false, 0, dirty: [Frames.Speed]));
        hub.Pump();

        Assert.Equal(new[] { (2L, 1.11), (4L, 1.30) }, Drain(sub).Select(d => (d.Tick, d.Changes.Single().Value.AsDouble)));
    }

    [Fact]
    public void AQualityChangeAlwaysPassesTheDeadband()
    {
        using RealtimeHub hub = Primed();
        using Subscription sub = hub.Subscribe(Lossless with { Deadbands = new Dictionary<string, double> { ["A.Speed"] = 1.0 } });

        TagValue[] bad = [TagValue.Double(1.0, TagQuality.Bad(QualityDetail.SensorFailure)), TagValue.Bool(false), TagValue.Int64(0)];
        var frame = new TickFrame(1, Frames.TimeOf(1), bad, DirtyMask.FromBits(new ulong[] { 1UL }, 3, 1), []);
        hub.Publish(frame);
        hub.Pump();

        FrameDelta delta = Assert.Single(Drain(sub));
        Assert.Equal(TagQuality.Bad(QualityDetail.SensorFailure), delta.Changes.Single().Value.Quality);
    }

    [Fact]
    public void PercentOfRangeDeadbandUsesTheDirectoryRange()
    {
        using RealtimeHub hub = Primed();
        using Subscription sub = hub.Subscribe(Lossless with { DeadbandPercentOfRange = 5.0 });   // A.Speed range 0..2 → 0.1

        hub.Publish(Frames.Frame(1, 1.09, false, 0, dirty: [Frames.Speed]));
        hub.Publish(Frames.Frame(2, 1.10, false, 0, dirty: [Frames.Speed]));
        hub.Pump();

        Assert.Equal(new[] { 2L }, Drain(sub).Select(d => d.Tick));
    }

    [Fact]
    public void AnExplicitDeadbandOverridesThePercent()
    {
        using RealtimeHub hub = Primed();
        using Subscription sub = hub.Subscribe(Lossless with
        {
            DeadbandPercentOfRange = 50.0,
            Deadbands = new Dictionary<string, double> { ["A.Speed"] = 0.01 },
        });

        hub.Publish(Frames.Frame(1, 1.02, false, 0, dirty: [Frames.Speed]));
        hub.Pump();

        Assert.Single(Drain(sub));
    }

    [Fact]
    public void DeadbandsAreValidatedAtSubscribeTime()
    {
        using RealtimeHub hub = Primed();

        Assert.Throws<ArgumentException>(() => hub.Subscribe(Lossless with { Deadbands = new Dictionary<string, double> { ["Nope"] = 0.1 } }));
        Assert.Throws<ArgumentException>(() => hub.Subscribe(Lossless with { Deadbands = new Dictionary<string, double> { ["A.Run"] = 0.1 } }));
        Assert.Throws<ArgumentOutOfRangeException>(() => hub.Subscribe(Lossless with { Deadbands = new Dictionary<string, double> { ["A.Speed"] = -0.1 } }));
        Assert.Throws<ArgumentOutOfRangeException>(() => hub.Subscribe(Lossless with { DeadbandPercentOfRange = -1.0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => hub.Subscribe(Lossless with { Decimation = TimeSpan.Zero }));
    }

    [Fact]
    public void DecimationEmitsAtMostOncePerIntervalOfSimTime()
    {
        using RealtimeHub hub = Primed();
        using Subscription sub = hub.Subscribe(Lossless with { Decimation = TimeSpan.FromMilliseconds(50) });

        for (int t = 1; t <= 12; t++)
        {
            hub.Publish(Frames.Frame(t, 1.0 + t, false, 0, dirty: [Frames.Speed]));
        }

        hub.Pump();

        List<FrameDelta> deltas = Drain(sub);
        Assert.Equal(new[] { 1L, 6L, 11L }, deltas.Select(d => d.Tick));
        Assert.Equal(new[] { 2.0, 7.0, 12.0 }, deltas.Select(d => d.Changes.Single().Value.AsDouble));
        Assert.Equal(0, sub.Queued);
        Assert.Empty(Drain(sub));
    }

    [Fact]
    public void DecimationKeepsEveryEventInTheWindow()
    {
        using RealtimeHub hub = Primed();
        using Subscription sub = hub.Subscribe(Lossless with { Decimation = TimeSpan.FromMilliseconds(50) });

        hub.Publish(Frames.Frame(1, 1.1, false, 0, dirty: [Frames.Speed]));
        hub.Publish(Frames.Frame(2, 1.2, false, 0, [Frames.Speed], Frames.Event(2, "A", "E2")));
        hub.Publish(Frames.Frame(4, 1.4, false, 0, [Frames.Speed], Frames.Event(4, "A", "E4")));
        hub.Publish(Frames.Frame(6, 1.6, true, 0, dirty: [Frames.Speed, Frames.Run]));
        hub.Pump();

        List<FrameDelta> deltas = Drain(sub);
        Assert.Equal(2, deltas.Count);
        Assert.Equal(new[] { "E2", "E4" }, deltas[1].Events.Select(e => e.Code));
        Assert.Equal(6L, deltas[1].Tick);
        Assert.Equal(2, deltas[1].Changes.Count);
    }

    [Fact]
    public void DecimationAndConflateCompose()
    {
        using RealtimeHub hub = Primed();
        using Subscription sub = hub.Subscribe(new SubscriptionOptions { Policy = BackpressurePolicy.Conflate, Decimation = TimeSpan.FromMilliseconds(50) });

        for (int t = 1; t <= 12; t++)
        {
            hub.Publish(Frames.Frame(t, 1.0 + t, false, 0, dirty: [Frames.Speed]));
        }

        hub.Pump();

        Assert.Equal(1, sub.Queued);
        FrameDelta delta = Assert.Single(Drain(sub));
        Assert.Equal(11L, delta.Tick);
        Assert.Equal(12.0, delta.Changes.Single().Value.AsDouble);
    }
}
