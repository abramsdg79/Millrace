using Millrace.Core.Time;
using Xunit;

namespace Millrace.Core.Tests;

public class SimulationClockTests
{
    private static readonly DateTimeOffset Start =
        new(2026, 1, 1, 6, 0, 0, TimeSpan.Zero);

    [Fact]
    public void StartsAtTickZero()
    {
        var clock = new SimulationClock(Start, TimeSpan.FromMilliseconds(10));

        Assert.Equal(0, clock.TickCount);
        Assert.Equal(Start, clock.Now);
        Assert.Equal(TimeSpan.Zero, clock.Elapsed);
    }

    [Fact]
    public void ExposesDeltaInSeconds()
    {
        var clock = new SimulationClock(Start, TimeSpan.FromMilliseconds(10));

        Assert.Equal(0.01, clock.DeltaSeconds, 12);
    }

    [Fact]
    public void OneHundredTicksOfTenMillisecondsIsExactlyOneSecond()
    {
        var clock = new SimulationClock(Start, TimeSpan.FromMilliseconds(10));

        for (int i = 0; i < 100; i++)
        {
            clock.Advance();
        }

        Assert.Equal(Start.AddSeconds(1), clock.Now);
    }

    [Fact]
    public void DoesNotDriftOverAMillionTicks()
    {
        var clock = new SimulationClock(Start, TimeSpan.FromMilliseconds(10));

        for (int i = 0; i < 1_000_000; i++)
        {
            clock.Advance();
        }

        // 1,000,000 x 10 ms = 10,000 s exactly, with no accumulated error.
        Assert.Equal(Start.AddSeconds(10_000), clock.Now);
        Assert.Equal(TimeSpan.FromSeconds(10_000), clock.Elapsed);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void RejectsNonPositiveTimeStep(int milliseconds)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new SimulationClock(Start, TimeSpan.FromMilliseconds(milliseconds)));
    }

    [Fact]
    public void OptionsDefaultToTenMillisecondStep()
    {
        var options = new SimulationOptions();

        Assert.Equal(TimeSpan.FromMilliseconds(10), options.TimeStep);
    }
}
