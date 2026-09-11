using Dse.Core.Contexts;
using Dse.Core.Graph;
using Dse.Core.Logging;
using Dse.Core.Tests.Fakes;
using Xunit;

namespace Dse.Core.Tests;

public class UnitDelayTests
{
    [Fact]
    public void DoesNotHaveDirectFeedthrough()
    {
        Assert.False(new UnitDelay<double>("D1").HasDirectFeedthrough);
    }

    [Fact]
    public void EmitsTheInitialValueOnTheFirstTick()
    {
        var delay = new UnitDelay<double>("D1", initialValue: 7.0);
        var source = new ConstantSource("S1", 100.0);
        source.Out.ConnectTo(delay.In);

        source.Evaluate(NewTickContext(0));
        delay.Evaluate(NewTickContext(0));

        Assert.Equal(7.0, delay.Out.Value);
    }

    [Fact]
    public void EmitsThePreviousTicksInputThereafter()
    {
        var delay = new UnitDelay<double>("D1");
        var source = new ConstantSource("S1", 5.0);
        source.Out.ConnectTo(delay.In);

        source.Evaluate(NewTickContext(0));
        delay.Evaluate(NewTickContext(0));
        delay.Latch();
        Assert.Equal(0.0, delay.Out.Value);

        source.Evaluate(NewTickContext(1));
        delay.Evaluate(NewTickContext(1));
        delay.Latch();
        Assert.Equal(5.0, delay.Out.Value);
    }

    [Fact]
    public void WorksForBooleans()
    {
        var delay = new UnitDelay<bool>("D1", initialValue: true);

        delay.Evaluate(NewTickContext(0));

        Assert.True(delay.Out.Value);
    }

    private static TickContext NewTickContext(long tick) =>
        new(tick, 0.01, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), new EventLog());
}
