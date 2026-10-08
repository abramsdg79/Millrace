using System.Diagnostics;
using Millrace.Core;
using Millrace.Core.Time;
using Millrace.Core.Tests.Fakes;
using Xunit;

namespace Millrace.Core.Tests;

public class SimulationRunnerTests
{
    private static SimulationOptions Options => new()
    {
        Seed = 1UL,
        StartTime = new DateTimeOffset(2026, 1, 1, 6, 0, 0, TimeSpan.Zero),
        TimeStep = TimeSpan.FromMilliseconds(10),
    };

    private static (Simulation Sim, Recorder Recorder) NewPlant()
    {
        var source = new ConstantSource("S", 3.0);
        var recorder = new Recorder("R");
        source.Out.ConnectTo(recorder.In);

        Simulation sim = new SimulationBuilder(Options).Add(source).Add(recorder).Build();
        return (sim, recorder);
    }

    [Fact]
    public void AsFastAsPossibleRunsEveryTick()
    {
        (Simulation sim, Recorder recorder) = NewPlant();

        new SimulationRunner(sim).RunFor(TimeSpan.FromSeconds(1));

        Assert.Equal(100, recorder.Samples.Count);
    }

    [Fact]
    public void StepAdvancesExactlyTheRequestedTicks()
    {
        (Simulation sim, Recorder recorder) = NewPlant();
        var runner = new SimulationRunner(sim);

        runner.Step();
        runner.Step(4);

        Assert.Equal(5, recorder.Samples.Count);
        Assert.Equal(5, sim.Clock.TickCount);
    }

    [Fact]
    public void ExecutionModeDoesNotChangeResults()
    {
        (Simulation fast, Recorder fastRecorder) = NewPlant();
        (Simulation scaled, Recorder scaledRecorder) = NewPlant();

        new SimulationRunner(fast).RunFor(TimeSpan.FromSeconds(1));
        new SimulationRunner(scaled, ExecutionMode.Scaled, speedFactor: 1000.0)
            .RunFor(TimeSpan.FromSeconds(1));

        Assert.Equal(fastRecorder.Samples, scaledRecorder.Samples);
        Assert.Equal(fast.Events.ToText(), scaled.Events.ToText());
    }

    [Fact]
    public void ScaledModeTakesRoughlyTheExpectedWallClockTime()
    {
        (Simulation sim, _) = NewPlant();
        var stopwatch = Stopwatch.StartNew();

        // 2 simulated seconds at 20x is about 100 ms of wall clock.
        new SimulationRunner(sim, ExecutionMode.Scaled, speedFactor: 20.0)
            .RunFor(TimeSpan.FromSeconds(2));

        stopwatch.Stop();
        Assert.InRange(stopwatch.Elapsed.TotalMilliseconds, 50.0, 600.0);
    }

    [Fact]
    public void CancellationStopsTheRunEarly()
    {
        (Simulation sim, Recorder recorder) = NewPlant();
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(50));

        new SimulationRunner(sim, ExecutionMode.Scaled, speedFactor: 1.0)
            .RunFor(TimeSpan.FromSeconds(30), cts.Token);

        Assert.True(recorder.Samples.Count < 3000);
    }

    [Fact]
    public void RejectsANonPositiveSpeedFactor()
    {
        (Simulation sim, _) = NewPlant();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => new SimulationRunner(sim, ExecutionMode.Scaled, speedFactor: 0.0));
    }
}
