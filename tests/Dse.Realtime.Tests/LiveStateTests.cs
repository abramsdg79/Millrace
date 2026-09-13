using Dse.Io;
using Dse.Realtime.Tests.Fakes;

namespace Dse.Realtime.Tests;

public class LiveStateTests
{
    [Fact]
    public void StartsEmptyAtTickMinusOne()
    {
        var state = new LiveState(Frames.Directory());

        Assert.Equal(-1L, state.Tick);
        Assert.Equal(0L, state.FramesApplied);
        Assert.Equal(TagValue.Double(0.0), state[Frames.Speed].Value);
        Assert.Equal(TagValue.Bool(false), state[Frames.Run].Value);
        Assert.Equal(TagValue.Int64(0L), state[Frames.Count].Value);
        Assert.Equal(-1L, state[Frames.Speed].LastChangeTick);
    }

    [Fact]
    public void TheFirstFrameIsAppliedInFullWhateverItsMask()
    {
        var state = new LiveState(Frames.Directory());

        state.Apply(Frames.Frame(7, 1.5, true, 3, dirty: []));

        Assert.Equal(7L, state.Tick);
        Assert.Equal(Frames.TimeOf(7), state.SimTime);
        Assert.Equal((1.5, true, 3L), (state[0].Value.AsDouble, state[1].Value.AsBool, state[2].Value.AsInt64));
        Assert.All(new[] { state[0], state[1], state[2] }, t => Assert.Equal(7L, t.LastChangeTick));
    }

    [Fact]
    public void LaterFramesUpdateOnlyDirtyTagsAndTheirChangeTick()
    {
        var state = new LiveState(Frames.Directory());
        state.Apply(Frames.Full(0, 1.0, false, 0));

        state.Apply(Frames.Frame(1, 1.2, false, 0, dirty: [Frames.Speed]));
        state.Apply(Frames.Frame(2, 1.2, true, 0, dirty: [Frames.Run]));

        Assert.Equal(2L, state.Tick);
        Assert.Equal((1.2, 1L), (state[Frames.Speed].Value.AsDouble, state[Frames.Speed].LastChangeTick));
        Assert.Equal((true, 2L), (state[Frames.Run].Value.AsBool, state[Frames.Run].LastChangeTick));
        Assert.Equal((0L, 0L), (state[Frames.Count].Value.AsInt64, state[Frames.Count].LastChangeTick));
        Assert.Equal(Frames.TimeOf(2), state[Frames.Run].LastChangeTime);
        Assert.Equal(3L, state.FramesApplied);
    }

    [Fact]
    public void GetByNameResolvesThroughTheDirectory()
    {
        var state = new LiveState(Frames.Directory());
        state.Apply(Frames.Full(0, 0.5, true, 9));

        Assert.Equal(9L, state.Get("B.Count").Value.AsInt64);
        Assert.Throws<KeyNotFoundException>(() => state.Get("Nope"));
    }

    [Fact]
    public void RecentEventsKeepTheNewestN()
    {
        var state = new LiveState(Frames.Directory(), recentEventCapacity: 3);
        state.Apply(Frames.Full(0, 0, false, 0, Frames.Event(0, "A", "E0"), Frames.Event(0, "A", "E1")));
        state.Apply(Frames.Quiet(1, 0, false, 0));
        state.Apply(Frames.Frame(2, 0, false, 0, [], Frames.Event(2, "A", "E2"), Frames.Event(2, "A", "E3")));

        StateSnapshot snapshot = state.Snapshot();

        Assert.Equal(new[] { "E1", "E2", "E3" }, snapshot.RecentEvents.Select(e => e.Code));
    }

    [Fact]
    public void SnapshotIsAnIndependentCopy()
    {
        var state = new LiveState(Frames.Directory());
        state.Apply(Frames.Full(0, 1.0, false, 0));

        StateSnapshot snapshot = state.Snapshot();
        state.Apply(Frames.Frame(1, 2.0, false, 0, dirty: [Frames.Speed]));

        Assert.Equal(0L, snapshot.Tick);
        Assert.Equal(1.0, snapshot.Tags.Span[Frames.Speed].Value.AsDouble);
        Assert.Equal(2.0, state[Frames.Speed].Value.AsDouble);
        Assert.Equal(3, snapshot.Tags.Length);
    }

    [Fact]
    public void RejectsAFrameOfTheWrongWidth()
    {
        var state = new LiveState(Frames.Directory());
        var narrow = new TickFrame(0, Frames.Start, new[] { TagValue.Bool(true) }, DirtyMask.All(1), []);

        Assert.Throws<ArgumentException>(() => state.Apply(narrow));
    }
}
