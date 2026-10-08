using Millrace.Io;

namespace Millrace.Control.Tests;

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

    /// <summary>The run-permit pattern: drop Start and Permit on trip, give Permit back on reset.</summary>
    private static Interlock MakeWithPermit() => new(
        "INT01",
        [new Condition("CV001.Tripped", false), new Condition("PERM01.Ok", true)],
        [new BlockWrite("CV001.Start", TagValue.Bool(false)), new BlockWrite("CV001.Permit", TagValue.Bool(false))],
        Period,
        [new BlockWrite("CV001.Permit", TagValue.Bool(true))]);

    private static Scan HealthyWithPermit()
    {
        var scan = new Scan(MakeWithPermit());
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
        Assert.Equal("INTERLOCK_TRIP,RESET_REFUSED", scan.Codes());
        Assert.Equal("Reset refused: PERM01.Ok is not normal.", scan.Events[^1].Message);
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

    [Fact]
    public void ATagInBothListsIsOneWritePinAfterTheTripPins()
    {
        Interlock interlock = MakeWithPermit();

        Assert.Equal(
            new[] { new TagRef("CV001.Start", TagKind.Bool), new TagRef("CV001.Permit", TagKind.Bool) },
            interlock.Writes);
    }

    [Fact]
    public void AResetOnlyTagGetsItsOwnPinAfterTheTripPins()
    {
        var interlock = new Interlock(
            "INT01",
            [new Condition("CV001.Tripped", false)],
            [new BlockWrite("CV001.Start", TagValue.Bool(false))],
            Period,
            [new BlockWrite("HORN.Silence", TagValue.Bool(true))]);

        Assert.Equal(["CV001.Start", "HORN.Silence"], interlock.Writes.Select(w => w.Name));
    }

    [Fact]
    public void WithoutResetWritesThePinsAreExactlyTheTripWrites()
    {
        var four = new Interlock("INT01", [new Condition("CV001.Tripped", false)], [new BlockWrite("CV001.Start", TagValue.Bool(false))], Period);
        var five = new Interlock("INT01", [new Condition("CV001.Tripped", false)], [new BlockWrite("CV001.Start", TagValue.Bool(false))], Period, []);

        Assert.Equal(four.Writes, five.Writes);
        Assert.Equal(new TagRef("CV001.Start", TagKind.Bool), Assert.Single(five.Writes));
    }

    [Fact]
    public void TheTripScanSendsTheTripWritesAndNoResetWrite()
    {
        Scan scan = HealthyWithPermit().Once();
        Assert.Empty(scan.LastWrites);

        scan.Set("CV001.Tripped", true).Once();

        Assert.Equal(
            new[] { ("CV001.Start", false), ("CV001.Permit", false) },
            scan.LastWrites.Select(w => (w.Key, w.Value.AsBool)));
    }

    [Fact]
    public void TheResetWritesGoOutOnTheAcceptedResetScanOnly()
    {
        Scan scan = HealthyWithPermit().Once();
        scan.Set("CV001.Tripped", true).Once();
        scan.Set("CV001.Tripped", false).Once();
        Assert.Empty(scan.LastWrites);

        scan.Command("Reset", true).Once();
        Assert.Equal("INTERLOCK_RESET", Assert.Single(scan.LastEvents).Code);
        Assert.Equal(new[] { ("CV001.Permit", true) }, scan.LastWrites.Select(w => (w.Key, w.Value.AsBool)));

        scan.Times(3);                                   // Reset held high: no second reset, no second write
        Assert.Empty(scan.LastWrites);
        Assert.Equal("INTERLOCK_TRIP,INTERLOCK_RESET", scan.Codes());
    }

    [Fact]
    public void ARefusedResetSendsNoResetWrite()
    {
        Scan scan = HealthyWithPermit().Once();
        scan.Set("PERM01.Ok", false).Once();

        scan.Command("Reset", true).Once();              // refused: PERM01.Ok still abnormal
        Assert.Empty(scan.LastWrites);
        scan.Command("Reset", false).Once();
        scan.Command("Reset", true).Once();              // a second edge, still refused
        Assert.Empty(scan.LastWrites);

        Assert.True(scan.Bool("Tripped"));
        Assert.Equal("INTERLOCK_TRIP,RESET_REFUSED,RESET_REFUSED", scan.Codes());
    }

    [Fact]
    public void TheConstructorRejectsTwoResetWritesToOneTag()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(() => new Interlock(
            "INT01",
            [new Condition("CV001.Tripped", false)],
            [],
            Period,
            [new BlockWrite("CV001.Permit", TagValue.Bool(true)), new BlockWrite("CV001.Permit", TagValue.Bool(false))]));

        Assert.Equal("resetWrites", error.ParamName);
        Assert.Contains("'CV001.Permit' is commanded twice on reset.", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheConstructorRejectsNullResetWrites()
    {
        ArgumentNullException error = Assert.Throws<ArgumentNullException>(() => new Interlock(
            "INT01",
            [new Condition("CV001.Tripped", false)],
            [],
            Period,
            null!));

        Assert.Equal("resetWrites", error.ParamName);
    }

    [Fact]
    public void TheConstructorRejectsAResetWriteToABlankTag()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(() => new Interlock(
            "INT01",
            [new Condition("CV001.Tripped", false)],
            [],
            Period,
            [new BlockWrite(" ", TagValue.Bool(true))]));

        Assert.Equal("resetWrites", error.ParamName);
    }

    [Fact]
    public void TheConstructorRejectsATagCommandedAsTwoKinds()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(() => new Interlock(
            "INT01",
            [new Condition("CV001.Tripped", false)],
            [new BlockWrite("V1.Setpoint", TagValue.Double(0.0))],
            Period,
            [new BlockWrite("V1.Setpoint", TagValue.Bool(true))]));

        Assert.Equal("resetWrites", error.ParamName);
        Assert.Contains("as a Double on trip and as a Bool on reset", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ARefusedResetNamesTheFirstAbnormalConditionInDeclaredOrderNotTheFirstOut()
    {
        Scan scan = Healthy().Once();
        scan.Set("PERM01.Ok", false).Once();                  // trips on condition 1
        scan.Set("CV001.Tripped", true).Once();               // condition 0 goes abnormal too

        scan.Command("Reset", true).Once();

        Assert.Equal(1L, scan.Int64("FirstOut"));
        BlockEvent refused = Assert.Single(scan.LastEvents);
        Assert.Equal("RESET_REFUSED", refused.Code);
        Assert.Equal("Reset refused: CV001.Tripped is not normal.", refused.Message);
        Assert.Empty(scan.LastWrites);
    }

    [Fact]
    public void AResetEdgeOnAnUntrippedInterlockLogsNothing()
    {
        Scan scan = Healthy().Once();

        scan.Command("Reset", true).Once();
        scan.Command("Reset", false).Once();
        scan.Command("Reset", true).Once();

        Assert.True(scan.Bool("Ok"));
        Assert.Empty(scan.Events);
        Assert.Empty(scan.LastWrites);
    }

    [Fact]
    public void AResetEdgeOnTheTripScanIsNotARefusal()
    {
        Scan scan = HealthyWithPermit().Once();

        scan.Set("CV001.Tripped", true).Command("Reset", true).Once();

        BlockEvent raised = Assert.Single(scan.LastEvents);
        Assert.Equal("INTERLOCK_TRIP", raised.Code);
        Assert.Equal(new[] { ("CV001.Start", false), ("CV001.Permit", false) }, scan.LastWrites.Select(w => (w.Key, w.Value.AsBool)));

        scan.Times(3);                                        // still held high, still abnormal: no edge, nothing more
        Assert.Equal("INTERLOCK_TRIP", scan.Codes());
    }

    [Fact]
    public void AResetRefusedWhileAbnormalIsAcceptedOnTheNextEdgeOnceNormal()
    {
        Scan scan = HealthyWithPermit().Once();
        scan.Set("CV001.Tripped", true).Once();

        scan.Command("Reset", true).Once();                  // refused
        scan.Set("CV001.Tripped", false).Command("Reset", false).Once();
        scan.Command("Reset", true).Once();                  // accepted

        Assert.False(scan.Bool("Tripped"));
        Assert.Equal("INTERLOCK_TRIP,RESET_REFUSED,INTERLOCK_RESET", scan.Codes());
        Assert.Equal(new[] { ("CV001.Permit", true) }, scan.LastWrites.Select(w => (w.Key, w.Value.AsBool)));
    }
}
