using Dse.Core;
using Dse.Core.Events;
using Dse.Core.Graph;
using Dse.Core.Time;
using Dse.Core.Tests.Fakes;
using Xunit;

namespace Dse.Core.Tests;

public class DeterminismTests
{
    private sealed record Run(
        Simulation Simulation,
        Recorder Recorder,
        NoiseSource Noise);

    private static SimulationOptions Options(ulong seed) => new()
    {
        Seed = seed,
        StartTime = new DateTimeOffset(2026, 1, 1, 6, 32, 11, TimeSpan.Zero),
        TimeStep = TimeSpan.FromMilliseconds(10),
    };

    /// <summary>
    /// Noise feeds a two-stage composite; the composite's output is integrated
    /// through a delayed feedback loop; a tripper logs an event when the integral
    /// passes a threshold. Exercises composites, loops, randomness and logging.
    /// </summary>
    private static Run BuildPlant(ulong seed)
    {
        var noise = new NoiseSource("Noise");
        var stage = new TwoStage("CV001", firstFactor: 0.5, secondFactor: 0.5);
        var integrator = new Integrator("Integrator");
        var delay = new UnitDelay<double>("Delay");
        var damping = new Gain("Damping", -0.05);
        var sum = new Sum("Sum");
        var recorder = new Recorder("Recorder");
        var tripper = new Tripper("Tripper", threshold: 0.20);

        noise.Out.ConnectTo(stage.Input<double>("In"));
        stage.Output<double>("Out").ConnectTo(sum.A);
        delay.Out.ConnectTo(damping.In);
        damping.Out.ConnectTo(sum.B);
        sum.Out.ConnectTo(integrator.In);
        integrator.Out.ConnectTo(delay.In);
        integrator.Out.ConnectTo(recorder.In);
        integrator.Out.ConnectTo(tripper.In);

        Simulation sim = new SimulationBuilder(Options(seed))
            .Add(noise).Add(stage).Add(sum).Add(integrator)
            .Add(delay).Add(damping).Add(recorder).Add(tripper)
            .Build();

        return new Run(sim, recorder, noise);
    }

    [Fact]
    public void ThePlantValidatesDespiteTheFeedbackLoop()
    {
        Run run = BuildPlant(492781UL);

        Assert.Equal(9, run.Simulation.Components.Count);
    }

    [Fact]
    public void TwoRunsWithTheSameSeedAreIndistinguishable()
    {
        Run first = BuildPlant(492781UL);
        Run second = BuildPlant(492781UL);

        first.Simulation.RunFor(TimeSpan.FromMinutes(1));
        second.Simulation.RunFor(TimeSpan.FromMinutes(1));

        Assert.Equal(first.Recorder.Samples, second.Recorder.Samples);
        Assert.Equal(first.Noise.Samples, second.Noise.Samples);
        Assert.Equal(first.Simulation.Events.ToText(), second.Simulation.Events.ToText());
        Assert.Equal(
            first.Simulation.Telemetry.Read("CV001.Second.Out"),
            second.Simulation.Telemetry.Read("CV001.Second.Out"));
    }

    [Fact]
    public void ADifferentSeedProducesADifferentRun()
    {
        Run first = BuildPlant(492781UL);
        Run second = BuildPlant(492782UL);

        first.Simulation.RunFor(TimeSpan.FromMinutes(1));
        second.Simulation.RunFor(TimeSpan.FromMinutes(1));

        Assert.NotEqual(first.Recorder.Samples, second.Recorder.Samples);
    }

    [Fact]
    public void TheRunProducesLoggedEvents()
    {
        Run run = BuildPlant(492781UL);

        run.Simulation.RunFor(TimeSpan.FromMinutes(1));

        Assert.Contains(run.Simulation.Events.Records, r => r.Code == "TRIP");
    }

    [Fact]
    public void InjectedEventsAreReproducible()
    {
        Run first = BuildPlant(492781UL);
        Run second = BuildPlant(492781UL);

        foreach (Run run in new[] { first, second })
        {
            Simulation sim = run.Simulation;
            sim.ScheduleAt(
                TimeSpan.FromSeconds(30),
                new CallbackEvent(() => sim.Events.Record(
                    sim.Clock.TickCount, sim.Clock.Now, "TEST", "INJECTED", "fault injected")));
        }

        first.Simulation.RunFor(TimeSpan.FromMinutes(1));
        second.Simulation.RunFor(TimeSpan.FromMinutes(1));

        Assert.Equal(first.Simulation.Events.ToText(), second.Simulation.Events.ToText());
        Assert.Contains(first.Simulation.Events.Records, r => r.Code == "INJECTED");
    }

    /// <summary>Adds two inputs. Needed to close the feedback loop.</summary>
    private sealed class Sum : ComponentBase
    {
        public Sum(string id)
            : base(id)
        {
            A = AddInput<double>("A");
            B = AddInput<double>("B");
            Out = AddOutput<double>("Out");
        }

        public InputPort<double> A { get; }

        public InputPort<double> B { get; }

        public OutputPort<double> Out { get; }

        public override void Evaluate(in Contexts.TickContext ctx) =>
            Out.Value = A.Value + B.Value;
    }
}
