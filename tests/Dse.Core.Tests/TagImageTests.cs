using Dse.Core.Graph;
using Dse.Core.Io;
using Dse.Core.Logging;
using Dse.Core.Tests.Fakes;
using Dse.Io;
using Xunit;

namespace Dse.Core.Tests;

public class TagImageTests
{
    private sealed class Ports
    {
        public OutputPort<double> Speed { get; } = new("Speed", "CV001") { Value = 1.0 };

        public OutputPort<TagQuality> SpeedHealth { get; } = new("Health", "CV001");

        public OutputPort<bool> Running { get; } = new("Running", "CV001");

        public InputPort<bool> Start { get; } = new("Start", "CV001", defaultValue: false, isRequired: false);

        public InputPort<double> Rate { get; } = new("Rate", "Feed", defaultValue: 5.0, isRequired: false);

        public TagDirectory Directory() => new(
        [
            TagBinding.Read("CV001.Speed", Speed, "m/s", 0.0, 3.0, "Belt speed", SpeedHealth),
            TagBinding.Read("CV001.Running", Running, "Contactor closed"),
            TagBinding.Write("CV001.Start", Start, "Start command"),
            TagBinding.Write("Feed.Rate", Rate, "kg/s", 0.0, 20.0, "Feed rate"),
        ]);

        public TagImage Image()
        {
            TagDirectory directory = Directory();
            foreach (TagBinding binding in directory.Bindings)
            {
                binding.BindExternal();
            }

            var image = new TagImage(directory);
            image.Prime();
            return image;
        }
    }

    [Fact]
    public void DirectoryIsSortedByOrdinalNameWithIndices()
    {
        TagDirectory directory = new Ports().Directory();

        Assert.Equal(4, directory.Count);
        Assert.Equal(
            new[] { "CV001.Running", "CV001.Speed", "CV001.Start", "Feed.Rate" },
            directory.Tags.Select(t => t.Name));
        Assert.Equal(Enumerable.Range(0, 4), directory.Tags.Select(t => t.Index));
        Assert.Same(directory.Tags[1], directory[1]);
        Assert.True(directory.TryFind("Feed.Rate", out TagDescriptor rate));
        Assert.Equal((TagKind.Double, TagAccess.ReadWrite, "kg/s", 0.0, 20.0), (rate.Kind, rate.Access, rate.Unit, rate.RangeLow, rate.RangeHigh));
        Assert.False(directory.TryFind("Nope", out _));
        Assert.Throws<KeyNotFoundException>(() => directory.Find("Nope"));
    }

    [Fact]
    public void DirectoryRejectsDuplicateNames()
    {
        var a = new OutputPort<bool>("A", "X");
        var b = new OutputPort<bool>("B", "X");

        Assert.Throws<ArgumentException>(() => new TagDirectory([TagBinding.Read("X.Same", a), TagBinding.Read("X.Same", b)]));
    }

    [Fact]
    public void ToTextListsEveryTagOnItsOwnLine()
    {
        string text = new Ports().Directory().ToText();

        string[] lines = text.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(4, lines.Length);
        Assert.Contains("CV001.Speed", lines[1], StringComparison.Ordinal);
        Assert.Contains("m/s", lines[1], StringComparison.Ordinal);
        Assert.Contains("[0, 3]", lines[1], StringComparison.Ordinal);
        Assert.Contains("ReadWrite", lines[2], StringComparison.Ordinal);
        Assert.Contains("Belt speed", lines[1], StringComparison.Ordinal);
    }

    [Fact]
    public void PrimedImageReadsCurrentValuesWithSnapshotTickMinusOne()
    {
        var ports = new Ports();
        TagImage image = ports.Image();

        Assert.Equal(-1L, image.SnapshotTick);
        Assert.Equal(1.0, image.ReadDouble("CV001.Speed"));
        Assert.False(image.ReadBool("CV001.Start"));
        Assert.Equal(5.0, image.ReadDouble("Feed.Rate"));
    }

    [Fact]
    public void ReadsSeeThePublishedSnapshotNotTheLivePort()
    {
        var ports = new Ports();
        TagImage image = ports.Image();
        ports.Speed.Value = 2.0;

        Assert.Equal(1.0, image.ReadDouble("CV001.Speed"));
        image.Publish(0);
        Assert.Equal(2.0, image.ReadDouble("CV001.Speed"));
        Assert.Equal(0L, image.SnapshotTick);

        ports.Speed.Value = 3.0;
        Assert.Equal(2.0, image.ReadDouble("CV001.Speed"));
    }

    [Fact]
    public void FirstPublishIsAllDirtyThenOnlyChanges()
    {
        var ports = new Ports();
        TagImage image = ports.Image();

        (TagValue[] first, DirtyMask firstMask) = image.Publish(0);
        Assert.Equal(4, firstMask.Count);
        Assert.Equal(4, first.Length);

        ports.Running.Value = true;
        (TagValue[] second, DirtyMask secondMask) = image.Publish(1);
        Assert.Equal(new[] { 0 }, secondMask.Indices());
        Assert.True(second[0].AsBool);
        Assert.NotSame(first, second);

        (_, DirtyMask third) = image.Publish(2);
        Assert.Equal(0, third.Count);
    }

    [Fact]
    public void QualityChangeAloneIsDirty()
    {
        var ports = new Ports();
        TagImage image = ports.Image();
        image.Publish(0);

        ports.SpeedHealth.Value = TagQuality.Bad(QualityDetail.SensorFailure);
        (TagValue[] values, DirtyMask mask) = image.Publish(1);

        Assert.Equal(new[] { 1 }, mask.Indices());
        Assert.Equal(TagQuality.Bad(QualityDetail.SensorFailure), values[1].Quality);
        Assert.Equal(TagQuality.Bad(QualityDetail.SensorFailure), image.Read("CV001.Speed").Quality);
    }

    [Fact]
    public void HandlesResolveOnceAndCheckTheKind()
    {
        var ports = new Ports();
        TagImage image = ports.Image();

        TagHandle<double> speed = image.Handle<double>("CV001.Speed");
        Assert.Equal(1, speed.Index);
        Assert.Equal(1.0, image.ReadDouble(speed));

        Assert.Throws<InvalidOperationException>(() => image.Handle<bool>("CV001.Speed"));
        Assert.Throws<KeyNotFoundException>(() => image.Handle<double>("CV001.Nope"));
        Assert.Throws<NotSupportedException>(() => image.Handle<int>("CV001.Speed"));
    }

    [Fact]
    public void WritesAreValidatedBeforeQueueing()
    {
        var ports = new Ports();
        TagImage image = ports.Image();

        Assert.Throws<InvalidOperationException>(() => image.WriteDouble("CV001.Speed", 1.0));
        Assert.Throws<InvalidOperationException>(() => image.WriteDouble("CV001.Start", 1.0));
        Assert.Throws<KeyNotFoundException>(() => image.WriteBool("CV001.Nope", true));
        Assert.Throws<ArgumentOutOfRangeException>(() => image.Write(9, TagValue.Bool(true)));
        Assert.Equal(0, image.PendingWrites);
    }

    [Fact]
    public void PendingWritesLandInOrderAndAreLogged()
    {
        var ports = new Ports();
        TagImage image = ports.Image();
        var log = new EventLog();

        image.WriteBool("CV001.Start", true);
        image.WriteDouble("Feed.Rate", 7.5);
        image.WriteDouble("Feed.Rate", 8.0);
        Assert.Equal(3, image.PendingWrites);
        Assert.False(ports.Start.Value);

        int applied = image.ApplyPendingWrites(TestContexts.Tick(4, log: log));

        Assert.Equal(3, applied);
        Assert.Equal(0, image.PendingWrites);
        Assert.True(ports.Start.Value);
        Assert.Equal(8.0, ports.Rate.Value);
        Assert.Equal(
            new[] { ("CV001.Start", "WRITE", "Set to true."), ("Feed.Rate", "WRITE", "Set to 7.5."), ("Feed.Rate", "WRITE", "Set to 8.") },
            log.Records.Select(r => (r.Source, r.Code, r.Message)));
        Assert.All(log.Records, r => Assert.Equal(4L, r.Tick));
    }

    [Fact]
    public void WrittenValueShowsInTheImageAfterPublish()
    {
        var ports = new Ports();
        TagImage image = ports.Image();

        image.WriteBool("CV001.Start", true);
        image.ApplyPendingWrites(TestContexts.Tick(0));
        Assert.False(image.ReadBool("CV001.Start"));

        image.Publish(0);
        Assert.True(image.ReadBool("CV001.Start"));
    }

    [Fact]
    public void ReadsFromAnotherThreadSeeWholeSnapshots()
    {
        var ports = new Ports();
        TagImage image = ports.Image();
        using var stop = new CancellationTokenSource();
        int torn = 0;

        var reader = new Thread(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                ReadOnlySpan<TagValue> snapshot = image.Snapshot().Span;
                TagValue speed = snapshot[1];
                TagValue running = snapshot[0];
                if (speed.AsDouble >= 100.0 != running.AsBool)
                {
                    Interlocked.Increment(ref torn);
                }
            }
        });
        reader.Start();

        for (int tick = 0; tick < 20_000; tick++)
        {
            bool high = tick % 2 == 1;
            ports.Speed.Value = high ? 100.0 : 1.0;
            ports.Running.Value = high;
            image.Publish(tick);
        }

        stop.Cancel();
        reader.Join();

        Assert.Equal(0, torn);
    }

    [Fact]
    public void SnapshotIsTheCurrentPublishedImage()
    {
        var ports = new Ports();
        TagImage image = ports.Image();
        ports.Running.Value = true;
        image.Publish(0);

        ReadOnlyMemory<TagValue> snapshot = image.Snapshot();

        Assert.Equal(4, snapshot.Length);
        Assert.True(snapshot.Span[0].AsBool);
        Assert.Equal(image.Read(1), snapshot.Span[1]);
    }
}
