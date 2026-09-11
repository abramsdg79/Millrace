using Dse.Core;
using Dse.Core.Events;
using Dse.Core.Graph;
using Dse.Core.Time;
using Dse.Core.Tests.Fakes;
using Dse.Core.Validation;
using Xunit;

namespace Dse.Core.Tests;

public class SimulationTests
{
    private static SimulationOptions Options => new()
    {
        Seed = 492781UL,
        StartTime = new DateTimeOffset(2026, 1, 1, 6, 0, 0, TimeSpan.Zero),
        TimeStep = TimeSpan.FromMilliseconds(10),
    };

    [Fact]
    public void BuildFlattensCompositesAndOrdersComponents()
    {
        var builder = new SimulationBuilder(Options);
        builder.Add(new TwoStage("CV001", 2.0, 3.0));

        Simulation sim = builder.Build();

        Assert.Equal(
            new[] { "CV001.First", "CV001.Second" },
            sim.Components.Select(c => c.Id));
    }

    [Fact]
    public void TickEvaluatesTheWholeChainInOneTick()
    {
        var source = new ConstantSource("S", 21.0);
        var gain = new Gain("G", 2.0);
        var recorder = new Recorder("R");
        source.Out.ConnectTo(gain.In);
        gain.Out.ConnectTo(recorder.In);

        Simulation sim = new SimulationBuilder(Options)
            .Add(recorder).Add(gain).Add(source)
            .Build();

        sim.Tick();

        Assert.Equal(new[] { 42.0 }, recorder.Samples);
    }

    [Fact]
    public void TickAdvancesTheClockAfterEvaluation()
    {
        Simulation sim = new SimulationBuilder(Options).Add(new ConstantSource("S", 1.0)).Build();

        Assert.Equal(0, sim.Clock.TickCount);
        sim.Tick();
        Assert.Equal(1, sim.Clock.TickCount);
        Assert.Equal(Options.StartTime.AddMilliseconds(10), sim.Clock.Now);
    }

    [Fact]
    public void RunForExecutesTheExpectedNumberOfTicks()
    {
        var recorder = new Recorder("R");
        Simulation sim = new SimulationBuilder(Options).Add(recorder).Build();

        sim.RunFor(TimeSpan.FromSeconds(1));

        Assert.Equal(100, recorder.Samples.Count);
        Assert.Equal(100, sim.Clock.TickCount);
    }

    [Fact]
    public void ScheduledEventsFireOnTheirTickBeforeEvaluation()
    {
        var order = new List<string>();
        var probe = new CallbackProbe("P", order);
        Simulation sim = new SimulationBuilder(Options).Add(probe).Build();

        sim.ScheduleAt(TimeSpan.FromMilliseconds(20), new CallbackEvent(() => order.Add("event")));

        sim.RunFor(TimeSpan.FromMilliseconds(40));

        // Tick 2 is the 20 ms mark: the event must precede that tick's evaluation.
        int eventIndex = order.IndexOf("event");
        Assert.Equal("evaluate@2", order[eventIndex + 1]);
    }

    [Fact]
    public void ValidateReportsDuplicateComponentIds()
    {
        var builder = new SimulationBuilder(Options)
            .Add(new ConstantSource("S", 1.0))
            .Add(new ConstantSource("S", 2.0));

        ValidationResult result = builder.Validate();

        Assert.False(result.IsValid);
        Assert.Equal("DSE001", result.Errors[0].Code);
    }

    [Fact]
    public void ValidateReportsUnconnectedRequiredInputs()
    {
        var builder = new SimulationBuilder(Options).Add(new NeedsInput("N"));

        ValidationResult result = builder.Validate();

        Assert.Equal("DSE002", result.Errors[0].Code);
        Assert.Contains("N.In", result.Errors[0].Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateReportsAnAlgebraicLoopAndNamesTheFix()
    {
        var left = new Gain("L", 1.0);
        var right = new Gain("R", 1.0);
        left.Out.ConnectTo(right.In);
        right.Out.ConnectTo(left.In);

        ValidationResult result = new SimulationBuilder(Options).Add(left).Add(right).Validate();

        Assert.Equal("DSE003", result.Errors[0].Code);
        Assert.Contains("UnitDelay", result.Errors[0].Message, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildThrowsWhenValidationFails()
    {
        var builder = new SimulationBuilder(Options).Add(new NeedsInput("N"));

        SimulationValidationException error =
            Assert.Throws<SimulationValidationException>(() => builder.Build());
        Assert.Equal("DSE002", error.Result.Errors[0].Code);
    }

    [Fact]
    public void UnitDelayLagsExactlyOneTickInsideAFeedbackLoop()
    {
        var counter = new Counter("C");
        var delay = new UnitDelay<double>("D");
        var recorder = new Recorder("R");
        counter.Out.ConnectTo(delay.In);
        delay.Out.ConnectTo(counter.In);
        delay.Out.ConnectTo(recorder.In);

        // Registered so the resolver, not registration order, decides evaluation order.
        Simulation sim = new SimulationBuilder(Options)
            .Add(recorder).Add(counter).Add(delay)
            .Build();

        sim.Tick();
        sim.Tick();
        sim.Tick();
        sim.Tick();

        Assert.Equal(new[] { 0.0, 1.0, 2.0, 3.0 }, recorder.Samples);
    }

    [Fact]
    public void UnitDelayLagsExactlyOneTickOutsideALoop()
    {
        var source = new ConstantSource("S", 5.0);
        var delay = new UnitDelay<double>("D");
        var recorder = new Recorder("R");
        source.Out.ConnectTo(delay.In);
        delay.Out.ConnectTo(recorder.In);

        Simulation sim = new SimulationBuilder(Options)
            .Add(recorder).Add(delay).Add(source)
            .Build();

        sim.Tick();
        sim.Tick();
        sim.Tick();

        Assert.Equal(new[] { 0.0, 5.0, 5.0 }, recorder.Samples);
    }

    [Fact]
    public void ValidateReportsAnInputDrivenByAComponentNotInThePlant()
    {
        var orphan = new ConstantSource("Orphan", 1.0);
        var gain = new Gain("G", 2.0);
        orphan.Out.ConnectTo(gain.In);

        ValidationResult result = new SimulationBuilder(Options).Add(gain).Validate();

        Assert.Equal("DSE004", result.Errors[0].Code);
        Assert.Contains("Orphan", result.Errors[0].Message, StringComparison.Ordinal);
        Assert.Contains("G.In", result.Errors[0].Message, StringComparison.Ordinal);
    }

    [Fact]
    public void InitializeRunsOnceBeforeTheFirstTick()
    {
        var counter = new InitCounter("I");
        Simulation sim = new SimulationBuilder(Options).Add(counter).Build();

        sim.Tick();
        sim.Tick();

        Assert.Equal(1, counter.InitializeCount);
    }

    private sealed class NeedsInput : ComponentBase
    {
        public NeedsInput(string id)
            : base(id) => AddInput<double>("In", defaultValue: 0.0, required: true);

        public override void Evaluate(in Contexts.TickContext ctx)
        {
        }
    }

    private sealed class InitCounter : ComponentBase
    {
        public InitCounter(string id)
            : base(id)
        {
        }

        public int InitializeCount { get; private set; }

        public override void Initialize(in Contexts.InitContext ctx) => InitializeCount++;

        public override void Evaluate(in Contexts.TickContext ctx)
        {
        }
    }

    /// <summary>Increments its input by one. Used to close a feedback loop through a delay.</summary>
    private sealed class Counter : ComponentBase
    {
        public Counter(string id)
            : base(id)
        {
            In = AddInput<double>("In");
            Out = AddOutput<double>("Out");
        }

        public InputPort<double> In { get; }

        public OutputPort<double> Out { get; }

        public override void Evaluate(in Contexts.TickContext ctx) => Out.Value = In.Value + 1.0;
    }

    private sealed class CallbackProbe : ComponentBase
    {
        private readonly List<string> _order;

        public CallbackProbe(string id, List<string> order)
            : base(id) => _order = order;

        public override void Evaluate(in Contexts.TickContext ctx) =>
            _order.Add($"evaluate@{ctx.Tick}");
    }
}
