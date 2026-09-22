using Dse.Io;

namespace Dse.Control.Tests;

public class InterlockTests
{
    private static readonly TimeSpan Period = TimeSpan.FromMilliseconds(100);

    /// <summary>Trips on an overload (normal false) or on the permissive dropping (normal true).</summary>
    private static Interlock Make() => new(
        "INT01",
        [new Condition("CV001.Tripped", false), new Condition("PERM01.Ok", true)],
        [new BlockWrite("CV001.Start", TagValue.Bool(false))],
        Period);

    private static Scan Healthy()
    {
        var scan = new Scan(Make());
        scan.Set("CV001.Tripped", false).Set("PERM01.Ok", true);
        return scan;
    }

    [Fact]
    public void TheConstructorRejectsNoConditions()
    {
        Assert.Throws<ArgumentException>(() => new Interlock("INT01", [], [], Period));
    }

    [Fact]
    public void TheConstructorRejectsAWriteToABlankTag()
    {
        Assert.Throws<ArgumentException>(() => new Interlock(
            "INT01",
            [new Condition("CV001.Tripped", false)],
            [new BlockWrite(" ", TagValue.Bool(false))],
            Period));
    }

    [Fact]
    public void TheConstructorRejectsTwoWritesToOneTag()
    {
        Assert.Throws<ArgumentException>(() => new Interlock(
            "INT01",
            [new Condition("CV001.Tripped", false)],
            [new BlockWrite("CV001.Start", TagValue.Bool(false)), new BlockWrite("CV001.Start", TagValue.Bool(true))],
            Period));
    }

    [Fact]
    public void ThePinsIncludeTheResetCommandAndTheTripWrites()
    {
        Interlock interlock = Make();

        Assert.Equal(2, interlock.Inputs.Count);
        Assert.Equal("CV001.Start", Assert.Single(interlock.Writes).Name);
        Assert.Equal(TagKind.Bool, interlock.Writes[0].Kind);
        Assert.Equal("Reset", Assert.Single(interlock.Commands).Name);
        Assert.Equal(3, interlock.Outputs.Count);
        Assert.Equal("Ok", interlock.Outputs[0].Name);
        Assert.Equal("Tripped", interlock.Outputs[1].Name);
        Assert.Equal("FirstOut", interlock.Outputs[2].Name);
    }

    [Fact]
    public void TrippedLatchesAndOkFallsOnTheFirstAbnormalCondition()
    {
        Scan scan = Healthy().Once();
        Assert.True(scan.Bool("Ok"));
        Assert.False(scan.Bool("Tripped"));

        scan.Set("CV001.Tripped", true).Once();

        Assert.False(scan.Bool("Ok"));
        Assert.True(scan.Bool("Tripped"));
        Assert.Equal(0L, scan.Int64("FirstOut"));
        BlockEvent raised = Assert.Single(scan.LastEvents);
        Assert.Equal("INTERLOCK_TRIP", raised.Code);
        Assert.Equal("CV001.Tripped abnormal.", raised.Message);
    }

    [Fact]
    public void TheTripWritesGoOutOnTheTripScanOnly()
    {
        Scan scan = Healthy().Once();
        Assert.False(scan.TryWrite("CV001.Start", out _));

        scan.Set("CV001.Tripped", true).Once();
        Assert.True(scan.TryWrite("CV001.Start", out TagValue value));
        Assert.False(value.AsBool);

        scan.Once();
        Assert.False(scan.TryWrite("CV001.Start", out _));
    }

    [Fact]
    public void TheLatchHoldsAfterTheConditionReturns()
    {
        Scan scan = Healthy().Once();
        scan.Set("CV001.Tripped", true).Once();

        scan.Set("CV001.Tripped", false).Times(3);

        Assert.True(scan.Bool("Tripped"));
        Assert.Equal(0L, scan.Int64("FirstOut"));
    }

    [Fact]
    public void AResetRisingEdgeWithEveryConditionNormalClearsTheLatch()
    {
        Scan scan = Healthy().Once();
        scan.Set("CV001.Tripped", true).Once();
        scan.Set("CV001.Tripped", false).Once();

        scan.Command("Reset", true).Once();

        Assert.True(scan.Bool("Ok"));
        Assert.False(scan.Bool("Tripped"));
        Assert.Equal(-1L, scan.Int64("FirstOut"));
        BlockEvent raised = Assert.Single(scan.LastEvents);
        Assert.Equal("INTERLOCK_RESET", raised.Code);
        Assert.Equal("Reset with all conditions normal.", raised.Message);
    }

    [Fact]
    public void AResetWhileAConditionIsStillAbnormalIsRefused()
    {
        Scan scan = Healthy().Once();
        scan.Set("PERM01.Ok", false).Once();

        scan.Command("Reset", true).Times(3);

        Assert.True(scan.Bool("Tripped"));
        Assert.Equal(1L, scan.Int64("FirstOut"));
        Assert.Equal("INTERLOCK_TRIP", scan.Codes());
    }

    [Fact]
    public void AHeldHighResetDoesNotClearASecondTrip()
    {
        Scan scan = Healthy().Once();
        scan.Set("CV001.Tripped", true).Once();
        scan.Set("CV001.Tripped", false).Once();
        scan.Command("Reset", true).Once();
        Assert.False(scan.Bool("Tripped"));

        scan.Set("CV001.Tripped", true).Once();        // Reset is still high
        scan.Set("CV001.Tripped", false).Times(3);

        Assert.True(scan.Bool("Tripped"));
        Assert.Equal("INTERLOCK_TRIP,INTERLOCK_RESET,INTERLOCK_TRIP", scan.Codes());
    }

    [Fact]
    public void AResetHeldThroughAnAbnormalConditionDoesNotClearWhenItReturnsNormal()
    {
        Scan scan = Healthy().Once();
        scan.Set("CV001.Tripped", true).Once();               // trips

        scan.Command("Reset", true).Once();                   // rising edge consumed while still abnormal
        Assert.True(scan.Bool("Tripped"));

        scan.Set("CV001.Tripped", false).Times(3);            // condition returns normal, Reset still held high

        Assert.True(scan.Bool("Tripped"));                    // no fresh rising edge, so it stays tripped
    }

    [Fact]
    public void TheSecondConditionNamesItselfWhenItTripsFirst()
    {
        Scan scan = Healthy().Once();

        scan.Set("PERM01.Ok", false).Once();

        Assert.Equal(1L, scan.Int64("FirstOut"));
        Assert.Equal("PERM01.Ok abnormal.", Assert.Single(scan.LastEvents).Message);
    }
}
