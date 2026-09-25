using Dse.Core.Logging;

namespace Dse.Samples.Tests;

public class CausalChainTests
{
    private static readonly DateTimeOffset Start = new(2026, 3, 2, 6, 0, 0, TimeSpan.FromHours(8));

    private static SimEventRecord At(long tick, string source, string code, string message) =>
        new(tick, Start.AddMilliseconds(tick * 10), source, code, message);

    private static readonly SimEventRecord[] Log =
    [
        At(0, "PERM_CV001", "PERMISSIVE_LOST", "CV001.SafetyOk dropped."),
        At(100, "CV001.Starter", "CONTACTOR_CLOSED", "Motor energised."),
        At(200, "INT_CV001", "INTERLOCK_TRIP", "CV002.Stopped abnormal."),
        At(201, "CV001.Start", "WRITE", "Set to false by INT_CV001."),
        At(300, "CV001.Starter", "CONTACTOR_OPENED", "Motor de-energised."),
    ];

    [Fact]
    public void AChainInLogOrderHoldsWithOtherEventsBetween()
    {
        EventPattern[] chain =
        [
            new("CV001.Starter", "CONTACTOR_CLOSED"),
            new("CV001.Start", "WRITE", "by INT_CV001."),
            new("CV001.Starter", "CONTACTOR_OPENED"),
        ];

        Assert.Null(CausalChain.FindChain(Log, chain));
    }

    [Fact]
    public void AChainOutOfOrderNamesTheFirstPatternNotFoundAndTheLastMatched()
    {
        EventPattern[] chain =
        [
            new("INT_CV001", "INTERLOCK_TRIP"),
            new("CV001.Starter", "CONTACTOR_CLOSED"),
        ];

        Assert.Equal(
            "The chain breaks at CV001.Starter CONTACTOR_CLOSED: no such event after INT_CV001 INTERLOCK_TRIP, " +
            "matched by '06:00:02.000  INT_CV001  INTERLOCK_TRIP  CV002.Stopped abnormal.'.",
            CausalChain.FindChain(Log, chain));
    }

    [Fact]
    public void AChainWhoseFirstPatternIsMissingSaysSoFromTheStart()
    {
        Assert.Equal(
            "The chain breaks at CV002.Starter CONTACTOR_OPENED: no such event after the start of the log.",
            CausalChain.FindChain(Log, [new EventPattern("CV002.Starter", "CONTACTOR_OPENED")]));
    }

    [Fact]
    public void OneEventCannotSatisfyTwoPatterns()
    {
        EventPattern twice = new("CV001.Starter", "CONTACTOR_OPENED");

        Assert.NotNull(CausalChain.FindChain(Log, [twice, twice]));
    }

    [Theory]
    [InlineData("CV001", "CONTACTOR_CLOSED", "")]                      // a source is exact, not a prefix
    [InlineData("CV001.Starter", "CONTACTOR", "")]                     // so is a code
    [InlineData("CV001.Starter", "CONTACTOR_CLOSED", "de-energised")]  // the fragment must be in the message
    public void APatternMatchesExactSourceExactCodeAndAMessageFragment(string source, string code, string fragment)
    {
        Assert.NotNull(CausalChain.FindChain(Log, [new EventPattern(source, code, fragment)]));
    }

    [Fact]
    public void ANullSourceMatchesAnySource()
    {
        Assert.Null(CausalChain.FindChain(Log, [new EventPattern(null, "INTERLOCK_TRIP", "CV002.Stopped")]));
    }

    [Fact]
    public void AnAbsenceHoldsWhenTheForbiddenEventIsOutsideItsWindow()
    {
        var absence = new Absence(
            new EventPattern("CV001.Starter", "CONTACTOR_CLOSED"),
            new EventPattern("CV001.Starter", "CONTACTOR_OPENED"),
            new EventPattern("CV001.Start", "WRITE"));

        Assert.Null(CausalChain.FindAbsence(Log, absence));
    }

    [Fact]
    public void AnAbsenceThatFailsQuotesTheOffendingLine()
    {
        var absence = new Absence(new EventPattern("CV001.Starter", "CONTACTOR_CLOSED"), new EventPattern(null, "INTERLOCK_TRIP"));

        Assert.Equal(
            "any source INTERLOCK_TRIP must not occur here, but '06:00:02.000  INT_CV001  INTERLOCK_TRIP  CV002.Stopped abnormal.' does.",
            CausalChain.FindAbsence(Log, absence));
    }

    [Fact]
    public void AnAbsenceWhoseBoundsNeverOccurFailsRatherThanPassingEmpty()
    {
        EventPattern forbidden = new(null, "INTERLOCK_TRIP");

        Assert.Equal(
            "The absence of any source INTERLOCK_TRIP starts at SEQ_STOP SEQUENCE_COMPLETE, which never occurs.",
            CausalChain.FindAbsence(Log, new Absence(new EventPattern("SEQ_STOP", "SEQUENCE_COMPLETE"), forbidden)));
        Assert.Equal(
            "The absence of any source INTERLOCK_TRIP ends at SEQ_STOP SEQUENCE_COMPLETE, which never occurs after its start.",
            CausalChain.FindAbsence(Log, new Absence(null, forbidden, new EventPattern("SEQ_STOP", "SEQUENCE_COMPLETE"))));
    }

    [Fact]
    public void AnEmptyLogFailsRatherThanPassingVacuously()
    {
        SimEventRecord[] empty = [];

        Assert.Equal(
            "The chain breaks at CV001.Starter CONTACTOR_CLOSED: no such event after the start of the log.",
            CausalChain.FindChain(empty, [new EventPattern("CV001.Starter", "CONTACTOR_CLOSED")]));

        Assert.Equal(
            "The absence of any source INTERLOCK_TRIP starts at SEQ_STOP SEQUENCE_COMPLETE, which never occurs.",
            CausalChain.FindAbsence(
                empty,
                new Absence(new EventPattern("SEQ_STOP", "SEQUENCE_COMPLETE"), new EventPattern(null, "INTERLOCK_TRIP"))));
    }
}
