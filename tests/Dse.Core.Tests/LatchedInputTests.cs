using Dse.Core;
using Dse.Core.Graph;
using Dse.Core.Tests.Fakes;
using Dse.Core.Time;
using Dse.Core.Validation;
using Xunit;

namespace Dse.Core.Tests;

public class LatchedInputTests
{
    private static SimulationOptions Options() => new()
    {
        Seed = 1UL,
        StartTime = new DateTimeOffset(2026, 1, 1, 6, 0, 0, TimeSpan.Zero),
        TimeStep = TimeSpan.FromMilliseconds(10),
    };

    [Fact]
    public void ALatchedInputReadsItsDefaultUntilTheFirstLatch()
    {
        var reflector = new Reflector("R", 2.0);
        var driver = new OutputPort<double>("Out", "SP");
        driver.ConnectTo(reflector.Back);
        driver.Value = 8.0;

        reflector.Evaluate(TestContexts.Tick(0));
        Assert.Equal(0.0, reflector.BackOut.Value);

        reflector.Latch();
        reflector.Evaluate(TestContexts.Tick(1));
        Assert.Equal(4.0, reflector.BackOut.Value);
    }

    [Fact]
    public void ALatchedInputLagsItsProducerByExactlyOneTick()
    {
        var reflector = new Reflector("R", 2.0);
        var driver = new OutputPort<double>("Out", "SP");
        driver.ConnectTo(reflector.Back);

        var seen = new List<double>();
        for (int tick = 0; tick < 4; tick++)
        {
            driver.Value = tick + 1;
            reflector.Evaluate(TestContexts.Tick(tick));
            seen.Add(reflector.Back.Value);
            reflector.Latch();
        }

        Assert.Equal([0.0, 1.0, 2.0, 3.0], seen);
    }

    [Fact]
    public void ALatchedInputCreatesNoOrderingEdge()
    {
        var a = new Reflector("A", 2.0);
        var b = new Reflector("B", 3.0);
        a.ForwardOut.ConnectTo(b.Forward);   // live: A before B
        b.BackOut.ConnectTo(a.Back);         // latched: no edge B -> A

        ValidationResult result = new SimulationBuilder(Options()).Add(b).Add(a).Validate();

        Assert.True(result.IsValid, result.ToText());
        Simulation sim = new SimulationBuilder(Options()).Add(b).Add(a).Build();
        Assert.Equal(["A", "B"], sim.Components.Select(c => c.Id));
    }

    [Fact]
    public void ALiveBackPathIsStillAnAlgebraicLoop()
    {
        var a = new Reflector("A", 2.0);
        var b = new Reflector("B", 3.0);
        a.ForwardOut.ConnectTo(b.Forward);
        b.BackOut.ConnectTo(a.Forward);      // live both ways

        ValidationResult result = new SimulationBuilder(Options()).Add(a).Add(b).Validate();

        Assert.Contains(result.Errors, e => e.Code == "DSE003");
    }

    [Fact]
    public void AChainOfReflectorsSettlesInASimulation()
    {
        var source = new ConstantSource("S", 1.0);
        var a = new Reflector("A", 2.0);
        var b = new Reflector("B", 3.0);
        var load = new Gain("L", 5.0);
        source.Out.ConnectTo(a.Forward);
        a.ForwardOut.ConnectTo(b.Forward);
        b.ForwardOut.ConnectTo(load.In);
        load.Out.ConnectTo(b.Back);
        b.BackOut.ConnectTo(a.Back);

        Simulation sim = new SimulationBuilder(Options())
            .Add(load).Add(b).Add(a).Add(source).Build();
        sim.RunFor(TimeSpan.FromMilliseconds(50));

        // Forward: 1 -> 2 -> 6 -> load 30. Back: 30 / 3 = 10 (one tick late), 10 / 2 = 5 (two ticks late).
        Assert.Equal(30.0, load.Out.Value);
        Assert.Equal(10.0, b.BackOut.Value);
        Assert.Equal(5.0, a.BackOut.Value);
    }

    [Fact]
    public void ALatchedInputFromOutsideThePlantIsStillRejected()
    {
        var a = new Reflector("A", 2.0);
        var stray = new Reflector("Stray", 1.0);
        stray.BackOut.ConnectTo(a.Back);

        ValidationResult result = new SimulationBuilder(Options()).Add(a).Validate();

        Assert.Contains(result.Errors, e => e.Code == "DSE004");
    }

    [Fact]
    public void UnitDelayStillLagsByOneTick()
    {
        var source = new ConstantSource("S", 7.0);
        var delay = new UnitDelay<double>("D");
        var recorder = new Recorder("R");
        source.Out.ConnectTo(delay.In);
        delay.Out.ConnectTo(recorder.In);

        Simulation sim = new SimulationBuilder(Options()).Add(recorder).Add(delay).Add(source).Build();
        sim.RunFor(TimeSpan.FromMilliseconds(30));

        Assert.Equal([0.0, 7.0, 7.0], recorder.Samples);
    }
}
