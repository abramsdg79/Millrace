using Dse.Io;

namespace Dse.Control.Tests;

public class AlarmTests
{
    private static readonly TimeSpan Period = TimeSpan.FromMilliseconds(100);

    private static AlarmLimit Limit(AlarmLimitKind kind, double value, double deadband = 5.0, double onDelaySeconds = 0.0) =>
        new(kind, value, deadband, TimeSpan.FromSeconds(onDelaySeconds));

    private static Alarm Make(params AlarmLimit[] limits) =>
        new("CUR01", "CV001.Current", limits, Period);

    /// <summary>Hi at 80 and HiHi at 90, both with a deadband of 5 and no on-delay.</summary>
    private static Scan Running()
    {
        var scan = new Scan(Make(Limit(AlarmLimitKind.Hi, 80.0), Limit(AlarmLimitKind.HiHi, 90.0)));
        scan.Set("CV001.Current", 40.0);
        return scan;
    }

    [Fact]
    public void TheConstructorRejectsNoLimits()
    {
        Assert.Throws<ArgumentException>(() => Make());
    }

    [Fact]
    public void TheConstructorRejectsABlankInput()
    {
        Assert.Throws<ArgumentException>(
            () => new Alarm("CUR01", " ", [Limit(AlarmLimitKind.Hi, 80.0)], Period));
    }

    [Fact]
    public void TheConstructorRejectsTwoLimitsOfTheSameKind()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(
            () => Make(Limit(AlarmLimitKind.Hi, 80.0), Limit(AlarmLimitKind.Hi, 85.0)));
        Assert.Contains("twice", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheConstructorRejectsLimitsOutOfOrder()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(
            () => Make(Limit(AlarmLimitKind.Hi, 95.0), Limit(AlarmLimitKind.HiHi, 90.0)));
        Assert.Contains("LoLo < Lo < Hi < HiHi", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheConstructorRejectsANegativeDeadband()
    {
        Assert.Throws<ArgumentException>(
            () => Make(new AlarmLimit(AlarmLimitKind.Hi, 80.0, -1.0, TimeSpan.Zero)));
    }

    [Fact]
    public void TheConstructorRejectsANegativeOnDelay()
    {
        Assert.Throws<ArgumentException>(
            () => Make(new AlarmLimit(AlarmLimitKind.Hi, 80.0, 5.0, TimeSpan.FromSeconds(-1))));
    }

    [Fact]
    public void ThePinsAreTwoPerLimitInAscendingOrder()
    {
        Alarm alarm = Make(
            Limit(AlarmLimitKind.HiHi, 90.0),
            Limit(AlarmLimitKind.LoLo, 10.0, deadband: 2.0),
            Limit(AlarmLimitKind.Hi, 80.0),
            Limit(AlarmLimitKind.Lo, 20.0, deadband: 2.0));

        Assert.Equal("CV001.Current", Assert.Single(alarm.Inputs).Name);
        Assert.Equal(TagKind.Double, alarm.Inputs[0].Kind);
        Assert.Empty(alarm.Writes);
        Assert.Equal("Ack", Assert.Single(alarm.Commands).Name);
        Assert.Equal(
            new[] { "LoLo.Active", "LoLo.Acked", "Lo.Active", "Lo.Acked", "Hi.Active", "Hi.Acked", "HiHi.Active", "HiHi.Acked" },
            alarm.Outputs.Select(o => o.Name).ToArray());
        Assert.All(alarm.Outputs, spec => Assert.Equal(TagKind.Bool, spec.Kind));
    }

    [Fact]
    public void ALimitStartsNormalAndAcknowledged()
    {
        Scan scan = Running().Once();

        Assert.False(scan.Bool("Hi.Active"));
        Assert.True(scan.Bool("Hi.Acked"));
        Assert.Empty(scan.Events);
    }

    [Fact]
    public void AHiLimitRaisesWhenTheValueCrosses()
    {
        Scan scan = Running().Once();

        scan.Set("CV001.Current", 82.3).Once();

        Assert.True(scan.Bool("Hi.Active"));
        Assert.False(scan.Bool("Hi.Acked"));
        BlockEvent raised = Assert.Single(scan.LastEvents);
        Assert.Equal("ALARM_RAISED", raised.Code);
        Assert.Equal("Hi: 82.3 above 80.", raised.Message);
    }

    [Fact]
    public void TheOnDelayMustElapseBeforeTheAlarmRaises()
    {
        var scan = new Scan(Make(Limit(AlarmLimitKind.Hi, 80.0, onDelaySeconds: 0.2)));
        scan.Set("CV001.Current", 40.0).Once();

        scan.Set("CV001.Current", 82.3).Once();        // detected, delay 0.0
        Assert.False(scan.Bool("Hi.Active"));

        scan.Once();                                   // delay 0.1
        Assert.False(scan.Bool("Hi.Active"));

        scan.Once();                                   // delay 0.2
        Assert.True(scan.Bool("Hi.Active"));
        Assert.Equal("ALARM_RAISED", Assert.Single(scan.LastEvents).Code);
    }

    [Fact]
    public void AnOnDelayOfZeroRaisesOnTheFirstScanAcross()
    {
        var scan = new Scan(Make(Limit(AlarmLimitKind.Hi, 80.0)));

        scan.Set("CV001.Current", 82.3).Once();

        Assert.True(scan.Bool("Hi.Active"));
    }

    [Fact]
    public void ABriefExcursionShorterThanTheOnDelayDoesNotRaise()
    {
        var scan = new Scan(Make(Limit(AlarmLimitKind.Hi, 80.0, onDelaySeconds: 0.3)));
        scan.Set("CV001.Current", 40.0).Once();

        scan.Set("CV001.Current", 82.3).Times(2);
        scan.Set("CV001.Current", 40.0).Once();
        scan.Set("CV001.Current", 82.3).Times(2);

        Assert.False(scan.Bool("Hi.Active"));
        Assert.Empty(scan.Events);
    }

    [Fact]
    public void TheDeadbandKeepsTheAlarmActiveUntilTheValueRecrosses()
    {
        Scan scan = Running().Once();
        scan.Set("CV001.Current", 82.3).Once();

        scan.Set("CV001.Current", 76.0).Once();        // inside the deadband
        Assert.True(scan.Bool("Hi.Active"));
        Assert.Empty(scan.LastEvents);

        scan.Set("CV001.Current", 71.5).Once();        // below 80 - 5
        Assert.False(scan.Bool("Hi.Active"));
        BlockEvent cleared = Assert.Single(scan.LastEvents);
        Assert.Equal("ALARM_CLEARED", cleared.Code);
        Assert.Equal("Hi: 71.5 back within limits.", cleared.Message);
    }

    [Fact]
    public void AValueExactlyAtTheHiLimitDoesNotRaise()
    {
        Scan scan = Running().Once();

        scan.Set("CV001.Current", 80.0).Once();        // exactly the Hi limit: > is strict

        Assert.False(scan.Bool("Hi.Active"));
        Assert.Empty(scan.Events);
    }

    [Fact]
    public void AValueExactlyAtTheHiLimitMinusTheDeadbandClears()
    {
        Scan scan = Running().Once();
        scan.Set("CV001.Current", 82.3).Once();

        scan.Set("CV001.Current", 75.0).Once();         // exactly 80 - 5: <= is inclusive

        Assert.False(scan.Bool("Hi.Active"));
        Assert.Equal("ALARM_CLEARED", Assert.Single(scan.LastEvents).Code);
    }

    [Fact]
    public void ALoLimitAtExactlyItsValueDoesNotRaise()
    {
        var scan = new Scan(Make(Limit(AlarmLimitKind.Lo, 20.0, deadband: 2.0)));
        scan.Set("CV001.Current", 40.0).Once();

        scan.Set("CV001.Current", 20.0).Once();          // exactly the Lo limit: < is strict

        Assert.False(scan.Bool("Lo.Active"));
        Assert.Empty(scan.Events);
    }

    [Fact]
    public void ALoLimitAtExactlyTheLimitPlusTheDeadbandClears()
    {
        var scan = new Scan(Make(Limit(AlarmLimitKind.Lo, 20.0, deadband: 2.0)));
        scan.Set("CV001.Current", 40.0).Once();
        scan.Set("CV001.Current", 18.5).Once();

        scan.Set("CV001.Current", 22.0).Once();          // exactly 20 + 2: >= is inclusive

        Assert.False(scan.Bool("Lo.Active"));
        Assert.Equal("ALARM_CLEARED", Assert.Single(scan.LastEvents).Code);
    }

    [Fact]
    public void ALoLimitRaisesBelowAndClearsAbove()
    {
        var scan = new Scan(Make(Limit(AlarmLimitKind.Lo, 20.0, deadband: 2.0)));
        scan.Set("CV001.Current", 40.0).Once();

        scan.Set("CV001.Current", 18.5).Once();
        Assert.True(scan.Bool("Lo.Active"));
        Assert.Equal("Lo: 18.5 below 20.", Assert.Single(scan.LastEvents).Message);

        scan.Set("CV001.Current", 21.0).Once();        // inside the deadband
        Assert.True(scan.Bool("Lo.Active"));

        scan.Set("CV001.Current", 22.0).Once();        // at 20 + 2
        Assert.False(scan.Bool("Lo.Active"));
    }

    [Fact]
    public void ALoLoLimitRaisesClearsAndIsAcknowledged()
    {
        var scan = new Scan(Make(Limit(AlarmLimitKind.LoLo, 10.0, deadband: 2.0, onDelaySeconds: 0.2)));
        scan.Set("CV001.Current", 40.0).Once();
        Assert.False(scan.Bool("LoLo.Active"));
        Assert.True(scan.Bool("LoLo.Acked"));

        scan.Set("CV001.Current", 8.5).Once();         // detected, delay 0.0
        Assert.False(scan.Bool("LoLo.Active"));

        scan.Times(2);                                 // 0.1, then 0.2
        Assert.True(scan.Bool("LoLo.Active"));
        Assert.False(scan.Bool("LoLo.Acked"));
        BlockEvent raised = Assert.Single(scan.LastEvents);
        Assert.Equal("ALARM_RAISED", raised.Code);
        Assert.Equal("LoLo: 8.5 below 10.", raised.Message);

        scan.Command("Ack", true).Once();
        Assert.True(scan.Bool("LoLo.Acked"));
        Assert.Equal("LoLo acknowledged.", Assert.Single(scan.LastEvents).Message);

        scan.Set("CV001.Current", 11.0).Once();        // inside the deadband
        Assert.True(scan.Bool("LoLo.Active"));

        scan.Set("CV001.Current", 12.5).Once();        // above 10 + 2
        Assert.False(scan.Bool("LoLo.Active"));
        Assert.Equal("LoLo: 12.5 back within limits.", Assert.Single(scan.LastEvents).Message);
    }

    [Fact]
    public void EveryLimitIsIndependent()
    {
        Scan scan = Running().Once();

        scan.Set("CV001.Current", 85.0).Once();
        Assert.True(scan.Bool("Hi.Active"));
        Assert.False(scan.Bool("HiHi.Active"));

        scan.Set("CV001.Current", 95.0).Once();
        Assert.True(scan.Bool("Hi.Active"));
        Assert.True(scan.Bool("HiHi.Active"));

        scan.Set("CV001.Current", 84.0).Once();        // below 90 - 5, still above 80
        Assert.True(scan.Bool("Hi.Active"));
        Assert.False(scan.Bool("HiHi.Active"));
    }

    [Fact]
    public void AnAckRisingEdgeAcknowledgesEveryUnacknowledgedLimit()
    {
        Scan scan = Running().Once();
        scan.Set("CV001.Current", 95.0).Once();
        Assert.False(scan.Bool("Hi.Acked"));
        Assert.False(scan.Bool("HiHi.Acked"));

        scan.Command("Ack", true).Once();

        Assert.True(scan.Bool("Hi.Active"));
        Assert.True(scan.Bool("Hi.Acked"));
        Assert.True(scan.Bool("HiHi.Acked"));
        Assert.Equal(2, scan.LastEvents.Count);
        Assert.Equal("ALARM_ACKED", scan.LastEvents[0].Code);
        Assert.Equal("Hi acknowledged.", scan.LastEvents[0].Message);
        Assert.Equal("HiHi acknowledged.", scan.LastEvents[1].Message);
    }

    [Fact]
    public void AHeldHighAckDoesNotAcknowledgeTheNextAlarm()
    {
        Scan scan = Running().Once();
        scan.Set("CV001.Current", 82.3).Once();
        scan.Command("Ack", true).Once();
        Assert.True(scan.Bool("Hi.Acked"));

        scan.Set("CV001.Current", 71.5).Once();        // clears
        scan.Set("CV001.Current", 82.3).Once();        // raises again, Ack still high

        Assert.True(scan.Bool("Hi.Active"));
        Assert.False(scan.Bool("Hi.Acked"));
    }

    [Fact]
    public void ReturnToNormalWhileUnacknowledgedLeavesBothFalse()
    {
        Scan scan = Running().Once();
        scan.Set("CV001.Current", 82.3).Once();

        scan.Set("CV001.Current", 71.5).Once();

        Assert.False(scan.Bool("Hi.Active"));
        Assert.False(scan.Bool("Hi.Acked"));
    }

    [Fact]
    public void AnAckAfterReturnToNormalClearsTheOutstandingState()
    {
        Scan scan = Running().Once();
        scan.Set("CV001.Current", 82.3).Once();
        scan.Set("CV001.Current", 71.5).Once();

        scan.Command("Ack", true).Once();

        Assert.False(scan.Bool("Hi.Active"));
        Assert.True(scan.Bool("Hi.Acked"));
        Assert.Equal("ALARM_ACKED", Assert.Single(scan.LastEvents).Code);
    }

    [Fact]
    public void TheThreeEventsArriveInOrderOverOneExcursion()
    {
        Scan scan = Running().Once();
        scan.Set("CV001.Current", 82.3).Once();
        scan.Command("Ack", true).Once();
        scan.Command("Ack", false).Once();
        scan.Set("CV001.Current", 71.5).Once();

        Assert.Equal("ALARM_RAISED,ALARM_ACKED,ALARM_CLEARED", scan.Codes());
    }

    [Theory]
    [InlineData(AlarmLimitKind.Hi, 7.5, 7.5004, "Hi: 7.51 above 7.5.")]
    [InlineData(AlarmLimitKind.HiHi, 8.6, 8.600000000000001, "HiHi: 8.61 above 8.6.")]
    [InlineData(AlarmLimitKind.Lo, 2.0, 1.9996, "Lo: 1.9 below 2.")]
    [InlineData(AlarmLimitKind.LoLo, 2.0, 1.9999999999999998, "LoLo: 1.9 below 2.")]
    [InlineData(AlarmLimitKind.Hi, 7.5, 7.71, "Hi: 7.71 above 7.5.")]
    [InlineData(AlarmLimitKind.Hi, 0.5, 1.1, "Hi: 1.10 above 0.5.")]
    [InlineData(AlarmLimitKind.Lo, 4.4, 4.35, "Lo: 4.35 below 4.4.")]
    [InlineData(AlarmLimitKind.Hi, 100.0, 100.05, "Hi: 100.1 above 100.")]
    [InlineData(AlarmLimitKind.Hi, 0.125, 0.12500001, "Hi: 0.1251 above 0.125.")]
    [InlineData(AlarmLimitKind.Hi, 0.1234567, 0.12345671, "Hi: 0.123457 above 0.1234567.")]
    [InlineData(AlarmLimitKind.Hi, 1e-5, 1.23e-5, "Hi: 0.000013 above 1E-05.")]
    [InlineData(AlarmLimitKind.Hi, -3.0, -2.96, "Hi: -2.9 above -3.")]
    [InlineData(AlarmLimitKind.Lo, -3.0, -3.04, "Lo: -3.1 below -3.")]
    [InlineData(AlarmLimitKind.Hi, -1.0, -0.01, "Hi: 0.0 above -1.")]
    public void ARaisePrintsOneMoreDecimalThanTheLimitRoundedAwayFromIt(AlarmLimitKind kind, double limit, double value, string message)
    {
        var scan = new Scan(Make(Limit(kind, limit)));

        scan.Set("CV001.Current", value).Once();

        BlockEvent raised = Assert.Single(scan.LastEvents);
        Assert.Equal(("ALARM_RAISED", message), (raised.Code, raised.Message));
    }

    [Theory]
    [InlineData(7.5, 1.0, 6.4951, "Hi: 6.50 back within limits.")]
    [InlineData(1.5, 1.0, 0.125, "Hi: 0.12 back within limits.")]
    [InlineData(1.5, 1.0, 0.375, "Hi: 0.38 back within limits.")]
    [InlineData(0.5, 0.5, -0.001, "Hi: 0.00 back within limits.")]
    public void AClearPrintsTheValueRoundedToNearest(double limit, double deadband, double value, string message)
    {
        var scan = new Scan(Make(Limit(AlarmLimitKind.Hi, limit, deadband)));
        scan.Set("CV001.Current", limit + 1.0).Once();

        scan.Set("CV001.Current", value).Once();

        BlockEvent cleared = Assert.Single(scan.LastEvents);
        Assert.Equal(("ALARM_CLEARED", message), (cleared.Code, cleared.Message));
    }
}
