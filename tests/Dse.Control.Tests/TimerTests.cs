using Dse.Io;

namespace Dse.Control.Tests;

public class TimerTests
{
    private static readonly TimeSpan Period = TimeSpan.FromMilliseconds(100);

    private static Timer Make(TimerMode mode, double presetSeconds = 0.3) =>
        new("TMR01", mode, "T.Enable", TimeSpan.FromSeconds(presetSeconds), Period);

    [Fact]
    public void TheConstructorRejectsABlankId()
    {
        Assert.Throws<ArgumentException>(
            () => new Timer(" ", TimerMode.OnDelay, "T.Enable", TimeSpan.FromSeconds(1), Period));
    }

    [Fact]
    public void TheConstructorRejectsABlankInput()
    {
        Assert.Throws<ArgumentException>(
            () => new Timer("TMR01", TimerMode.OnDelay, " ", TimeSpan.FromSeconds(1), Period));
    }

    [Fact]
    public void TheConstructorRejectsANegativePreset()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new Timer("TMR01", TimerMode.OnDelay, "T.Enable", TimeSpan.FromSeconds(-1), Period));
    }

    [Fact]
    public void TheConstructorRejectsANonPositiveScanPeriod()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new Timer("TMR01", TimerMode.OnDelay, "T.Enable", TimeSpan.FromSeconds(1), TimeSpan.Zero));
    }

    [Fact]
    public void ThePinsAreDeclaredInOrder()
    {
        Timer timer = Make(TimerMode.OnDelay);

        Assert.Equal("TMR01", timer.Id);
        Assert.Equal(Period, timer.ScanPeriod);
        Assert.Equal(TimerMode.OnDelay, timer.Mode);
        Assert.Equal(TimeSpan.FromSeconds(0.3), timer.Preset);
        Assert.Equal("T.Enable", Assert.Single(timer.Inputs).Name);
        Assert.Equal(TagKind.Bool, timer.Inputs[0].Kind);
        Assert.Empty(timer.Writes);
        Assert.Empty(timer.Commands);
        Assert.Equal(2, timer.Outputs.Count);
        Assert.Equal("Q", timer.Outputs[0].Name);
        Assert.Equal(TagKind.Bool, timer.Outputs[0].Kind);
        Assert.Equal("ET", timer.Outputs[1].Name);
        Assert.Equal(TagKind.Double, timer.Outputs[1].Kind);
        Assert.Equal("s", timer.Outputs[1].Unit);
    }

    [Fact]
    public void OnDelayHoldsQLowUntilThePresetHasElapsed()
    {
        var scan = new Scan(Make(TimerMode.OnDelay));

        scan.Set("T.Enable", true).Once();
        Assert.False(scan.Bool("Q"));
        Assert.Equal(0.0, scan.Double("ET"));

        scan.Times(2);                                 // 0.1 s, then 0.2 s
        Assert.False(scan.Bool("Q"));
        Assert.Equal(0.2, scan.Double("ET"), 12);

        scan.Once();                                   // 0.3 s
        Assert.True(scan.Bool("Q"));
        Assert.Equal(0.3, scan.Double("ET"), 12);
    }

    [Fact]
    public void OnDelayResetsWhenTheInputFalls()
    {
        // Only the harness's very first scan carries Elapsed 0; every scan after
        // it carries 0.1, so after the reset the count is 0.1, 0.2, 0.3 and the
        // third re-enabled scan — not the fourth — reaches the preset again.
        var scan = new Scan(Make(TimerMode.OnDelay));
        scan.Set("T.Enable", true).Times(4);           // 0, 0.1, 0.2, 0.3
        Assert.True(scan.Bool("Q"));

        scan.Set("T.Enable", false).Once();
        Assert.False(scan.Bool("Q"));
        Assert.Equal(0.0, scan.Double("ET"));

        scan.Set("T.Enable", true).Times(2);           // 0.1, 0.2
        Assert.False(scan.Bool("Q"));
        Assert.Equal(0.2, scan.Double("ET"), 12);

        scan.Once();                                   // 0.3
        Assert.True(scan.Bool("Q"));
    }

    [Fact]
    public void OnDelayWithAZeroPresetIsImmediate()
    {
        var scan = new Scan(Make(TimerMode.OnDelay, presetSeconds: 0.0));

        scan.Set("T.Enable", true).Once();

        Assert.True(scan.Bool("Q"));
        Assert.Equal(0.0, scan.Double("ET"));
    }

    [Fact]
    public void OffDelayHoldsQHighForThePresetAfterTheInputFalls()
    {
        var scan = new Scan(Make(TimerMode.OffDelay));
        scan.Set("T.Enable", true).Times(2);
        Assert.True(scan.Bool("Q"));

        scan.Set("T.Enable", false).Times(2);          // 0.1 s, 0.2 s
        Assert.True(scan.Bool("Q"));

        scan.Once();                                   // 0.3 s
        Assert.False(scan.Bool("Q"));
        Assert.Equal(0.3, scan.Double("ET"), 12);
    }

    [Fact]
    public void OffDelayRestartsWhenTheInputReturns()
    {
        var scan = new Scan(Make(TimerMode.OffDelay));
        scan.Set("T.Enable", true).Once();
        scan.Set("T.Enable", false).Times(2);
        Assert.True(scan.Bool("Q"));

        scan.Set("T.Enable", true).Once();
        Assert.True(scan.Bool("Q"));
        Assert.Equal(0.0, scan.Double("ET"));

        scan.Set("T.Enable", false).Times(2);
        Assert.True(scan.Bool("Q"));
        scan.Once();
        Assert.False(scan.Bool("Q"));
    }

    [Fact]
    public void PulseGivesAPresetLongPulseOnARisingEdge()
    {
        var scan = new Scan(Make(TimerMode.Pulse));

        scan.Set("T.Enable", true).Once();
        Assert.True(scan.Bool("Q"));
        Assert.Equal(0.0, scan.Double("ET"));

        scan.Times(2);                                 // 0.1 s, 0.2 s
        Assert.True(scan.Bool("Q"));

        scan.Once();                                   // 0.3 s
        Assert.False(scan.Bool("Q"));
        Assert.Equal(0.3, scan.Double("ET"), 12);
    }

    [Fact]
    public void PulseIsNotRetriggeredWhileItRuns()
    {
        var scan = new Scan(Make(TimerMode.Pulse));
        scan.Set("T.Enable", true).Once();

        scan.Set("T.Enable", false).Once();
        scan.Set("T.Enable", true).Once();             // a second rising edge, mid-pulse
        Assert.True(scan.Bool("Q"));
        Assert.Equal(0.2, scan.Double("ET"), 12);

        scan.Once();
        Assert.False(scan.Bool("Q"));                  // the pulse still ends at 0.3 s
    }

    [Fact]
    public void ElapsedTimeIsQuantisedToTheScanPeriod()
    {
        var scan = new Scan(Make(TimerMode.OnDelay, presetSeconds: 1.0));

        scan.Set("T.Enable", true).Times(3);

        Assert.Equal(0.2, scan.Double("ET"), 12);      // never 0.25 or 0.17: two periods
    }

    [Fact]
    public void ElapsedTimeStopsAtThePreset()
    {
        var scan = new Scan(Make(TimerMode.OnDelay));

        scan.Set("T.Enable", true).Times(10);

        Assert.True(scan.Bool("Q"));
        Assert.Equal(0.3, scan.Double("ET"), 12);
    }

    [Fact]
    public void ATimerRaisesNoEvents()
    {
        var scan = new Scan(Make(TimerMode.Pulse));

        scan.Set("T.Enable", true).Times(5);
        scan.Set("T.Enable", false).Times(5);

        Assert.Empty(scan.Events);
    }
}
