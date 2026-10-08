using Millrace.Io;
using Millrace.Realtime.Tests.Fakes;

namespace Millrace.Realtime.Tests;

public class CommandBusTests
{
    private sealed class Recorder : ICommandRecorder
    {
        public List<TagCommand> Commands { get; } = [];

        public void Record(TagCommand command) => Commands.Add(command);
    }

    private static ArrayDirectory Directory() => new(
        new TagDescriptor(0, "CV001.Speed", TagKind.Double, TagAccess.ReadOnly, "m/s", 0.0, 2.0, "Belt speed"),
        new TagDescriptor(1, "CV001.Start", TagKind.Bool, TagAccess.ReadWrite, string.Empty, double.NaN, double.NaN, "Start"),
        new TagDescriptor(2, "Feed.Rate", TagKind.Double, TagAccess.ReadWrite, "kg/s", 0.0, 20.0, "Feed rate"),
        new TagDescriptor(3, "Feed.Batches", TagKind.Int64, TagAccess.ReadWrite, "count", double.NaN, double.NaN, "Batches"),
        new TagDescriptor(4, "Feed.Bias", TagKind.Double, TagAccess.ReadWrite, "kg/s", double.NaN, double.NaN, "Unranged"));

    [Fact]
    public void AnAcceptedCommandReachesTheWriter()
    {
        var writer = new RecordingWriter(Directory());
        var bus = new CommandBus(writer);

        Assert.Equal(CommandOutcome.Accepted, bus.WriteBool("CV001.Start", true));
        Assert.Equal(CommandOutcome.Accepted, bus.WriteDouble("Feed.Rate", 12.5));
        Assert.Equal(CommandOutcome.Accepted, bus.WriteInt64("Feed.Batches", 3L));

        Assert.Equal(new[] { (1, TagValue.Bool(true)), (2, TagValue.Double(12.5)), (3, TagValue.Int64(3L)) }, writer.Writes);
        Assert.Equal((3L, 0L), (bus.Accepted, bus.Rejected));
    }

    [Theory]
    [InlineData("Nope", true, CommandOutcome.UnknownTag)]
    [InlineData("CV001.Speed", 1.0, CommandOutcome.ReadOnly)]
    [InlineData("CV001.Start", 1.0, CommandOutcome.KindMismatch)]
    [InlineData("Feed.Rate", 25.0, CommandOutcome.OutOfRange)]
    [InlineData("Feed.Rate", -0.5, CommandOutcome.OutOfRange)]
    [InlineData("Feed.Rate", double.NaN, CommandOutcome.OutOfRange)]
    public void RejectedCommandsNeverReachTheWriter(string tag, object raw, CommandOutcome expected)
    {
        var writer = new RecordingWriter(Directory());
        var bus = new CommandBus(writer);
        TagValue value = raw is bool b ? TagValue.Bool(b) : TagValue.Double((double)raw);

        Assert.Equal(expected, bus.Write(tag, value));

        Assert.Empty(writer.Writes);
        Assert.Equal((0L, 1L), (bus.Accepted, bus.Rejected));
    }

    [Fact]
    public void AnUnrangedDoubleAcceptsAnyFiniteValue()
    {
        var writer = new RecordingWriter(Directory());
        var bus = new CommandBus(writer);

        Assert.Equal(CommandOutcome.Accepted, bus.WriteDouble("Feed.Bias", -1e6));
        Assert.Equal(CommandOutcome.OutOfRange, bus.WriteDouble("Feed.Bias", double.PositiveInfinity));
    }

    [Fact]
    public void TheRecorderSeesEveryCommandWithItsOutcome()
    {
        var recorder = new Recorder();
        var bus = new CommandBus(new RecordingWriter(Directory()), recorder);

        bus.WriteBool("CV001.Start", true);
        bus.WriteDouble("CV001.Speed", 1.0);

        Assert.Equal(
            new[] { ("CV001.Start", CommandOutcome.Accepted), ("CV001.Speed", CommandOutcome.ReadOnly) },
            recorder.Commands.Select(c => (c.Tag, c.Outcome)));
    }
}
