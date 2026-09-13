using Dse.Core.Contexts;
using Dse.Core.Graph;
using Dse.Core.Io;
using Dse.Core.Tests.Fakes;
using Dse.Core.Time;
using Dse.Core.Validation;
using Dse.Io;
using Xunit;

namespace Dse.Core.Tests;

public class IoIntegrationTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 6, 0, 0, TimeSpan.Zero);

    private static SimulationOptions Options => new()
    {
        Seed = 1UL,
        StartTime = Start,
        TimeStep = TimeSpan.FromMilliseconds(10),
    };

    private static (Simulation Sim, Thermostat T, FrameCollector Frames) Plant()
    {
        var t = new Thermostat("T");
        var frames = new FrameCollector();
        Simulation sim = new SimulationBuilder(Options).Add(t).Build();
        sim.AttachFrameSink(frames);
        return (sim, t, frames);
    }

    /// <summary>Declares a tag on a port it does not own.</summary>
    private sealed class Naughty : ComponentBase, ITagProvider
    {
        private readonly OutputPort<double> _foreign;

        public Naughty(string id, OutputPort<double> foreign)
            : base(id) => _foreign = foreign;

        public override void Evaluate(in TickContext ctx)
        {
        }

        public IEnumerable<TagBinding> DescribeTags() => [TagBinding.Read("Stolen", _foreign, "V", 0.0, 1.0)];
    }

    /// <summary>A required input whose only driver is a writable tag.</summary>
    private sealed class Demanding : ComponentBase, ITagProvider
    {
        public Demanding(string id)
            : base(id) => Command = AddInput<bool>("Command", required: true);

        public InputPort<bool> Command { get; }

        public override void Evaluate(in TickContext ctx)
        {
        }

        public IEnumerable<TagBinding> DescribeTags() => [TagBinding.Write("Command", Command, "Run command")];
    }

    [Fact]
    public void ARequiredInputDrivenOnlyByAWritableTagBuilds()
    {
        var d = new Demanding("D");
        ValidationResult validation = new SimulationBuilder(Options).Add(new Demanding("D")).Validate();
        Assert.Empty(validation.Errors);

        Simulation sim = new SimulationBuilder(Options).Add(d).Build();
        sim.IO.WriteBool("D.Command", true);
        sim.Tick();

        Assert.True(d.Command.Value);
    }

    [Fact]
    public void DirectoryListsDeclaredTagsWithFullNames()
    {
        (Simulation sim, _, _) = Plant();

        Assert.Equal(new[] { "T.Enable", "T.Output", "T.Setpoint" }, sim.IO.Directory.Tags.Select(t => t.Name));
        TagDescriptor output = sim.IO.Directory.Find("T.Output");
        Assert.Equal(("%", 0.0, 200.0, TagAccess.ReadOnly, "Heater output"), (output.Unit, output.RangeLow, output.RangeHigh, output.Access, output.Description));
        Assert.Equal(TagAccess.ReadWrite, sim.IO.Directory.Find("T.Setpoint").Access);
    }

    [Fact]
    public void CompositeAliasRenamesTheLeafTag()
    {
        var pair = new Pair("P1");
        Simulation sim = new SimulationBuilder(Options).Add(pair).Build();

        string[] names = sim.IO.Directory.Tags.Select(t => t.Name).ToArray();
        Assert.Equal(new[] { "P1.A.Enable", "P1.A.Output", "P1.B.Enable", "P1.B.Setpoint", "P1.Out", "P1.SP" }, names);
        Assert.Equal(TagAccess.ReadWrite, sim.IO.Directory.Find("P1.SP").Access);
    }

    [Fact]
    public void ExplicitBindAddsATruthTag()
    {
        var source = new ConstantSource("S", 3.0);
        var recorder = new Recorder("R");
        source.Out.ConnectTo(recorder.In);
        Simulation sim = new SimulationBuilder(Options)
            .Add(source).Add(recorder)
            .Bind("Truth.S", TagBinding.Read("Out", source.Out, "V", 0.0, 10.0, "Source truth"))
            .Build();

        sim.Tick();

        Assert.Equal(3.0, sim.IO.ReadDouble("Truth.S"));
        Assert.Equal("Truth.S", Assert.Single(sim.IO.Directory.Tags).Name);
    }

    [Fact]
    public void ExplicitBindReplacesADeclaredTag()
    {
        var t = new Thermostat("T");
        Simulation sim = new SimulationBuilder(Options)
            .Add(t)
            .Bind("Ctrl.SP", TagBinding.Write("x", t.Setpoint, "°C", 0.0, 50.0))
            .Build();

        Assert.Equal(new[] { "Ctrl.SP", "T.Enable", "T.Output" }, sim.IO.Directory.Tags.Select(x => x.Name));
    }

    [Fact]
    public void DuplicateTagNameIsDse009()
    {
        var s1 = new ConstantSource("S1", 1.0);
        var s2 = new ConstantSource("S2", 2.0);
        ValidationResult result = new SimulationBuilder(Options)
            .Add(s1).Add(s2)
            .Bind("Same", TagBinding.Read("Out", s1.Out, "V", 0.0, 1.0))
            .Bind("Same", TagBinding.Read("Out", s2.Out, "V", 0.0, 1.0))
            .Validate();

        ValidationError error = Assert.Single(result.Errors);
        Assert.Equal("DSE009", error.Code);
        Assert.Contains("S1.Out", error.Message, StringComparison.Ordinal);
        Assert.Contains("S2.Out", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DeclaredWritableTagOnADrivenInputBecomesReadOnly()
    {
        var t = new Thermostat("T");
        var source = new ConstantSource("S", 30.0);
        source.Out.ConnectTo(t.Setpoint);
        Simulation sim = new SimulationBuilder(Options).Add(t).Add(source).Build();

        sim.Tick();

        Assert.Equal(TagAccess.ReadOnly, sim.IO.Directory.Find("T.Setpoint").Access);
        Assert.Equal(30.0, sim.IO.ReadDouble("T.Setpoint"));
        Assert.Throws<InvalidOperationException>(() => sim.IO.WriteDouble("T.Setpoint", 1.0));
        Assert.False(t.Setpoint.IsExternallyDriven);
    }

    [Fact]
    public void ExplicitlyBoundWritableTagOnADrivenInputIsDse010()
    {
        var t = new Thermostat("T");
        var source = new ConstantSource("S", 30.0);
        source.Out.ConnectTo(t.Setpoint);

        ValidationResult result = new SimulationBuilder(Options)
            .Add(t).Add(source)
            .Bind("SP", TagBinding.Write("Setpoint", t.Setpoint, "°C", 0.0, 100.0))
            .Validate();

        ValidationError error = Assert.Single(result.Errors);
        Assert.Equal("DSE010", error.Code);
        Assert.Contains("T.Setpoint", error.Message, StringComparison.Ordinal);
        Assert.Contains("S.Out", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void BindOnAPortOutsideThePlantIsDse011()
    {
        var inside = new ConstantSource("In", 1.0);
        var outside = new ConstantSource("Out", 1.0);

        ValidationResult result = new SimulationBuilder(Options)
            .Add(inside)
            .Bind("X", TagBinding.Read("Out", outside.Out, "V", 0.0, 1.0))
            .Validate();

        Assert.Equal("DSE011", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public void DeclaringATagOnAnotherComponentsPortIsDse011()
    {
        var victim = new ConstantSource("V", 1.0);
        var naughty = new Naughty("N", victim.Out);

        ValidationResult result = new SimulationBuilder(Options).Add(victim).Add(naughty).Validate();

        ValidationError error = Assert.Single(result.Errors);
        Assert.Equal("DSE011", error.Code);
        Assert.Contains("N", error.ComponentIds);
    }

    [Fact]
    public void BeforeTheFirstTickTheImageIsPrimed()
    {
        (Simulation sim, _, _) = Plant();
        sim.Initialize();

        Assert.Equal(-1L, sim.IO.SnapshotTick);
        Assert.Equal(20.0, sim.IO.ReadDouble("T.Setpoint"));
        Assert.Equal(0.0, sim.IO.ReadDouble("T.Output"));
    }

    [Fact]
    public void WritesLandAtPhaseOneOfTheNextTick()
    {
        (Simulation sim, _, _) = Plant();
        sim.RunFor(TimeSpan.FromMilliseconds(30));

        sim.IO.WriteDouble("T.Setpoint", 10.0);
        sim.IO.WriteBool("T.Enable", true);
        Assert.Equal(0.0, sim.IO.ReadDouble("T.Output"));
        Assert.Equal(2, sim.IO.PendingWrites);

        sim.Tick();

        Assert.Equal(20.0, sim.IO.ReadDouble("T.Output"));
        Assert.Equal(3L, sim.IO.SnapshotTick);
        Assert.Equal(
            new[] { (3L, "T.Setpoint", "WRITE", "Set to 10."), (3L, "T.Enable", "WRITE", "Set to true.") },
            sim.Events.Records.Select(r => (r.Tick, r.Source, r.Code, r.Message)));
    }

    [Fact]
    public void FramesArriveOncePerTickWithDirtyMasksAndEvents()
    {
        (Simulation sim, _, FrameCollector frames) = Plant();
        sim.RunFor(TimeSpan.FromMilliseconds(20));

        Assert.Equal(2, frames.Frames.Count);
        Assert.Equal(0L, frames.Frames[0].Tick);
        Assert.Equal(3, frames.Frames[0].Dirty.Count);
        Assert.Equal(0, frames.Frames[1].Dirty.Count);
        Assert.Empty(frames.Frames[1].Events);

        sim.IO.WriteBool("T.Enable", true);
        sim.Tick();

        TickFrame frame = frames.Frames[2];
        Assert.Equal(2L, frame.Tick);
        Assert.Equal(Start + TimeSpan.FromMilliseconds(20), frame.SimTime);
        Assert.Equal(new[] { 0, 1 }, frame.Dirty.Indices());
        Assert.True(frame.Values.Span[0].AsBool);
        Assert.Equal(40.0, frame.Values.Span[1].AsDouble);
        DiscreteEvent written = Assert.Single(frame.Events);
        Assert.Equal(("T.Enable", "WRITE", 2L), (written.Source, written.Code, written.Tick));
    }

    [Fact]
    public void FrameValuesAreTheSnapshotTheReaderSees()
    {
        (Simulation sim, _, FrameCollector frames) = Plant();
        sim.IO.WriteBool("T.Enable", true);
        sim.Tick();

        TickFrame frame = frames.Frames[0];
        for (int i = 0; i < sim.IO.Directory.Count; i++)
        {
            Assert.Equal(frame.Values.Span[i], sim.IO.Read(i));
        }
    }

    [Fact]
    public void QualityFlowsFromTheSourcePort()
    {
        (Simulation sim, Thermostat t, FrameCollector frames) = Plant();
        t.Quality = TagQuality.Bad(QualityDetail.SensorFailure);

        sim.Tick();

        Assert.Equal(TagQuality.Bad(QualityDetail.SensorFailure), sim.IO.Read("T.Output").Quality);
        Assert.Equal(TagQuality.Bad(QualityDetail.SensorFailure), frames.Frames[0].Values.Span[1].Quality);
        Assert.True(sim.IO.Read("T.Setpoint").Quality.IsGood);
    }

    [Fact]
    public void HandlesReadWithoutLookups()
    {
        (Simulation sim, _, _) = Plant();
        TagHandle<double> output = sim.IO.Handle<double>("T.Output");
        TagHandle<bool> enable = sim.IO.Handle<bool>("T.Enable");

        sim.IO.WriteBool(enable, true);
        sim.Tick();

        Assert.Equal(40.0, sim.IO.ReadDouble(output));
        Assert.True(sim.IO.ReadBool(enable));
    }

    [Fact]
    public void TwoRunsWithTheSameWritesProduceIdenticalFrames()
    {
        static List<TickFrame> Run()
        {
            (Simulation sim, _, FrameCollector frames) = Plant();
            sim.RunFor(TimeSpan.FromMilliseconds(50));
            sim.IO.WriteBool("T.Enable", true);
            sim.IO.WriteDouble("T.Setpoint", 33.0);
            sim.RunFor(TimeSpan.FromMilliseconds(50));
            return frames.Frames;
        }

        List<TickFrame> a = Run();
        List<TickFrame> b = Run();

        Assert.Equal(a.Count, b.Count);
        for (int i = 0; i < a.Count; i++)
        {
            Assert.Equal(a[i].Tick, b[i].Tick);
            Assert.Equal(a[i].Values.ToArray(), b[i].Values.ToArray());
            Assert.Equal(a[i].Dirty.Indices(), b[i].Dirty.Indices());
            Assert.Equal(a[i].Events.Select(e => (e.Source, e.Code)), b[i].Events.Select(e => (e.Source, e.Code)));
        }
    }

    [Fact]
    public void WithoutASinkNoFrameIsBuilt()
    {
        var t = new Thermostat("T");
        Simulation sim = new SimulationBuilder(Options).Add(t).Build();

        Assert.Null(sim.FrameSink);
        sim.RunFor(TimeSpan.FromMilliseconds(30));

        Assert.Equal(2L, sim.IO.SnapshotTick);
    }

    [Fact]
    public void ASinkAttachedMidRunGetsFramesFromThenOn()
    {
        var t = new Thermostat("T");
        var frames = new FrameCollector();
        Simulation sim = new SimulationBuilder(Options).Add(t).Build();
        sim.RunFor(TimeSpan.FromMilliseconds(30));

        sim.AttachFrameSink(frames);
        sim.Tick();

        TickFrame frame = Assert.Single(frames.Frames);
        Assert.Equal(3L, frame.Tick);
        Assert.Equal(0, frame.Dirty.Count);
        Assert.Equal(3, frame.Values.Length);
    }

    [Fact]
    public void OnlyOneSinkMayBeAttached()
    {
        (Simulation sim, _, _) = Plant();

        Assert.Throws<InvalidOperationException>(() => sim.AttachFrameSink(new FrameCollector()));
    }

    [Fact]
    public void BindAfterBuildThrows()
    {
        var t = new Thermostat("T");
        var builder = new SimulationBuilder(Options).Add(t);
        builder.Build();

        Assert.Throws<InvalidOperationException>(() =>
            builder.Bind("X", TagBinding.Read("Output", t.Output, "%", 0.0, 1.0)));
    }
}
