using Millrace.Io;
using Millrace.Io.Abstractions.Tests.Fakes;

namespace Millrace.Io.Abstractions.Tests;

public class TagAccessExtensionsTests
{
    private static ArrayTagReader Reader() => new(
        new TagDescriptor(0, "CV001.Speed", TagKind.Double, TagAccess.ReadOnly, "m/s", 0.0, 3.0, "Belt speed"),
        new TagDescriptor(1, "CV001.Start", TagKind.Bool, TagAccess.ReadWrite, string.Empty, double.NaN, double.NaN, "Start command"),
        new TagDescriptor(2, "Pile.Count", TagKind.Int64, TagAccess.ReadOnly, "count", double.NaN, double.NaN, "Items received"));

    [Fact]
    public void TypedReadsByNameUnwrapTheValue()
    {
        ArrayTagReader reader = Reader();
        reader.Values[0] = TagValue.Double(1.5);
        reader.Values[1] = TagValue.Bool(true);
        reader.Values[2] = TagValue.Int64(9);

        Assert.Equal(1.5, reader.ReadDouble("CV001.Speed"));
        Assert.True(reader.ReadBool("CV001.Start"));
        Assert.Equal(9L, reader.ReadInt64("Pile.Count"));
    }

    [Fact]
    public void TypedReadsByHandleUnwrapTheValue()
    {
        ArrayTagReader reader = Reader();
        reader.Values[0] = TagValue.Double(2.5);
        TagHandle<double> speed = reader.Handle<double>("CV001.Speed");

        Assert.Equal(0, speed.Index);
        Assert.Equal("CV001.Speed", speed.Name);
        Assert.Equal(2.5, reader.ReadDouble(speed));
    }

    [Fact]
    public void TypedWritesWrapTheValue()
    {
        ArrayTagReader writer = Reader();

        writer.WriteBool("CV001.Start", true);
        writer.WriteDouble(writer.Handle<double>("CV001.Speed"), 0.5);
        writer.WriteInt64(2, 4L);

        Assert.Equal(
            new[] { (1, TagValue.Bool(true)), (0, TagValue.Double(0.5)), (2, TagValue.Int64(4L)) },
            writer.Writes);
    }

    [Fact]
    public void DescriptorHasRangeOnlyWhenBothBoundsAreNumbers()
    {
        ArrayTagReader reader = Reader();

        Assert.True(reader.Directory[0].HasRange);
        Assert.False(reader.Directory[1].HasRange);
    }

    [Fact]
    public void TickFrameHoldsWhatItWasGiven()
    {
        var values = new[] { TagValue.Double(1.0), TagValue.Bool(true) };
        var events = new[] { new DiscreteEvent(5, DateTimeOffset.UnixEpoch, "CV001", "AT_SPEED", "At speed.") };
        var frame = new TickFrame(5, DateTimeOffset.UnixEpoch, values, DirtyMask.All(2), events);

        Assert.Equal(5, frame.Tick);
        Assert.Equal(2, frame.Values.Length);
        Assert.Equal(TagValue.Bool(true), frame.Values.Span[1]);
        Assert.Equal(2, frame.Dirty.Count);
        Assert.Single(frame.Events);
    }

    [Fact]
    public void TickFrameRejectsMismatchedMaskLength()
    {
        var values = new[] { TagValue.Double(1.0) };

        Assert.Throws<ArgumentException>(() =>
            new TickFrame(0, DateTimeOffset.UnixEpoch, values, DirtyMask.All(2), []));
    }
}
