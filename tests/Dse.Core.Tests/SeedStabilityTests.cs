using Dse.Core;
using Dse.Core.Time;
using Dse.Core.Tests.Fakes;
using Xunit;

namespace Dse.Core.Tests;

public class SeedStabilityTests
{
    private static SimulationOptions Options(ulong seed = 492781UL) => new()
    {
        Seed = seed,
        StartTime = new DateTimeOffset(2026, 1, 1, 6, 0, 0, TimeSpan.Zero),
        TimeStep = TimeSpan.FromMilliseconds(10),
    };

    [Fact]
    public void AddingAComponentDoesNotShiftAnotherComponentsStream()
    {
        var smallX = new NoiseSource("X");
        Simulation small = new SimulationBuilder(Options()).Add(smallX).Build();
        small.RunFor(TimeSpan.FromMilliseconds(100));

        var largeX = new NoiseSource("X");
        Simulation large = new SimulationBuilder(Options())
            .Add(new NoiseSource("W"))
            .Add(largeX)
            .Add(new NoiseSource("Z"))
            .Build();
        large.RunFor(TimeSpan.FromMilliseconds(100));

        Assert.Equal(smallX.Samples, largeX.Samples);
    }

    [Fact]
    public void DifferentComponentsGetDifferentStreams()
    {
        var first = new NoiseSource("A");
        var second = new NoiseSource("B");
        Simulation sim = new SimulationBuilder(Options()).Add(first).Add(second).Build();

        sim.RunFor(TimeSpan.FromMilliseconds(100));

        Assert.NotEqual(first.Samples, second.Samples);
    }

    [Fact]
    public void ADifferentMasterSeedChangesEveryStream()
    {
        var first = new NoiseSource("A");
        new SimulationBuilder(Options(1UL)).Add(first).Build()
            .RunFor(TimeSpan.FromMilliseconds(100));

        var second = new NoiseSource("A");
        new SimulationBuilder(Options(2UL)).Add(second).Build()
            .RunFor(TimeSpan.FromMilliseconds(100));

        Assert.NotEqual(first.Samples, second.Samples);
    }

    [Fact]
    public void RenamingAComponentChangesItsStream()
    {
        var original = new NoiseSource("A");
        new SimulationBuilder(Options()).Add(original).Build()
            .RunFor(TimeSpan.FromMilliseconds(100));

        var renamed = new NoiseSource("A2");
        new SimulationBuilder(Options()).Add(renamed).Build()
            .RunFor(TimeSpan.FromMilliseconds(100));

        Assert.NotEqual(original.Samples, renamed.Samples);
    }
}
