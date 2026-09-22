using Dse.Io;

namespace Dse.Control.Tests;

public class SequencerTests
{
    private static readonly TimeSpan Period = TimeSpan.FromMilliseconds(100);

    private static BlockWrite Start(bool value) => new("CV001.Start", TagValue.Bool(value));

    /// <summary>An abort write on a tag no step writes, so the abort writes reach <c>Writes</c> on their own.</summary>
    private static BlockWrite Feed(bool value) => new("FD001.Enabled", TagValue.Bool(value));

    /// <summary>Start the belt, wait for speed, then stop it and wait for the belt to rest.</summary>
    private static Sequencer Make(TimeSpan? firstTimeout = null) => new(
        "SEQ01",
        [
            new SequenceStep(
                "Start the belt",
                [Start(true)],
                StepTransition.When("CV001.Speed", PredicateOperator.GreaterOrEqual, TagValue.Double(1.0)),
                firstTimeout),
            new SequenceStep("Run", [], StepTransition.After(TimeSpan.FromSeconds(0.3))),
            new SequenceStep(
                "Stop the belt",
                [Start(false)],
                StepTransition.When("CV001.Stopped", PredicateOperator.Equal, TagValue.Bool(true))),
        ],
        Period,
        [Start(false), Feed(false)]);

    private static Scan Idle(TimeSpan? firstTimeout = null)
    {
        var scan = new Scan(Make(firstTimeout));
        scan.Set("CV001.Speed", 0.0).Set("CV001.Stopped", false);
        return scan;
    }

    /// <summary>Pulses a command: one scan high, one scan low.</summary>
    private static Scan Pulse(Scan scan, string command)
    {
        scan.Command(command, true).Once();
        scan.Command(command, false);
        return scan;
    }

    [Fact]
    public void TheConstructorRejectsNoSteps()
    {
        Assert.Throws<ArgumentException>(() => new Sequencer("SEQ01", [], Period));
    }

    [Fact]
    public void TheConstructorRejectsAStepNameEndingInAFullStop()
    {
        Assert.Throws<ArgumentException>(() => new Sequencer(
            "SEQ01",
            [new SequenceStep("Start the belt.", [], StepTransition.After(TimeSpan.FromSeconds(1)))],
            Period));
    }

    [Fact]
    public void TheConstructorRejectsAnOrderingOperatorOnABoolPredicate()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(() => new Sequencer(
            "SEQ01",
            [new SequenceStep(
                "Wait",
                [],
                StepTransition.When("CV001.Stopped", PredicateOperator.Greater, TagValue.Bool(true)))],
            Period));
        Assert.Contains("Bool", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheConstructorRejectsTwoWritesToOneTagOfDifferentKinds()
    {
        Assert.Throws<ArgumentException>(() => new Sequencer(
            "SEQ01",
            [
                new SequenceStep("One", [Start(true)], StepTransition.After(TimeSpan.FromSeconds(1))),
                new SequenceStep(
                    "Two",
                    [new BlockWrite("CV001.Start", TagValue.Double(1.0))],
                    StepTransition.After(TimeSpan.FromSeconds(1))),
            ],
            Period));
    }

    [Fact]
    public void TheConstructorRejectsDuplicateTagsWithinOneStepsEntryWrites()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(() => new SequenceStep(
            "One",
            [Start(true), Start(false)],
            StepTransition.After(TimeSpan.FromSeconds(1))));
        Assert.Contains("CV001.Start", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheConstructorRejectsANonPositiveTimeout()
    {
        Assert.Throws<ArgumentException>(() => new Sequencer(
            "SEQ01",
            [new SequenceStep("One", [], StepTransition.After(TimeSpan.FromSeconds(1)), TimeSpan.Zero)],
            Period));
    }

    [Fact]
    public void ThePinsAreThePredicateTagsTheWriteTagsAndTheFiveCommands()
    {
        Sequencer sequencer = Make();

        Assert.Equal(new[] { "CV001.Speed", "CV001.Stopped" }, sequencer.Inputs.Select(p => p.Name).ToArray());
        Assert.Equal(TagKind.Double, sequencer.Inputs[0].Kind);
        Assert.Equal(TagKind.Bool, sequencer.Inputs[1].Kind);
        Assert.Equal(
            new[] { "CV001.Start", "FD001.Enabled" },
            sequencer.Writes.Select(p => p.Name).ToArray());
        Assert.Equal(
            new[] { "Start", "Hold", "Resume", "Abort", "Reset" },
            sequencer.Commands.Select(p => p.Name).ToArray());
        Assert.Equal(
            new[] { "Step", "Running", "Held", "Complete", "Faulted", "StepTime" },
            sequencer.Outputs.Select(p => p.Name).ToArray());
        Assert.Equal(TagKind.Int64, sequencer.Outputs[0].Kind);
        Assert.Equal(TagKind.Double, sequencer.Outputs[5].Kind);
        Assert.Equal("s", sequencer.Outputs[5].Unit);
    }

    [Fact]
    public void AStartRisingEdgeFromIdleEntersStepOne()
    {
        Scan scan = Idle().Once();
        Assert.Equal(0L, scan.Int64("Step"));
        Assert.False(scan.Bool("Running"));

        Pulse(scan, "Start");

        Assert.Equal(1L, scan.Int64("Step"));
        Assert.True(scan.Bool("Running"));
        Assert.True(scan.TryWrite("CV001.Start", out TagValue value));
        Assert.True(value.AsBool);
        BlockEvent entered = Assert.Single(scan.LastEvents);
        Assert.Equal("STEP_ENTERED", entered.Code);
        Assert.Equal("1: Start the belt.", entered.Message);
    }

    [Fact]
    public void AHeldHighStartDoesNotRetrigger()
    {
        Scan scan = Idle().Once();
        scan.Command("Start", true).Once();
        scan.Set("CV001.Speed", 2.0).Times(3);         // step 1 satisfied, then step 2 runs

        Assert.Equal(2L, scan.Int64("Step"));
        Assert.Equal("STEP_ENTERED,STEP_ENTERED", scan.Codes());
    }

    [Fact]
    public void EntryWritesGoOutOnTheScanThatEntersTheStepAndNoOther()
    {
        Scan scan = Idle().Once();
        Pulse(scan, "Start");
        Assert.True(scan.TryWrite("CV001.Start", out _));

        scan.Once();
        Assert.False(scan.TryWrite("CV001.Start", out _));
    }

    [Fact]
    public void TheSequenceAdvancesWhenThePredicateIsSatisfied()
    {
        Scan scan = Idle().Once();
        Pulse(scan, "Start");

        scan.Once();                                   // step 1's transition is not satisfied
        Assert.Equal(1L, scan.Int64("Step"));

        scan.Set("CV001.Speed", 1.0).Once();
        Assert.Equal(2L, scan.Int64("Step"));
        Assert.Equal("2: Run.", scan.LastEvents[0].Message);
    }

    [Theory]
    [InlineData(PredicateOperator.Equal, 1.0, true)]
    [InlineData(PredicateOperator.NotEqual, 1.0, false)]
    [InlineData(PredicateOperator.Less, 0.5, true)]
    [InlineData(PredicateOperator.LessOrEqual, 1.0, true)]
    [InlineData(PredicateOperator.Greater, 1.5, true)]
    [InlineData(PredicateOperator.GreaterOrEqual, 1.0, true)]
    public void EveryComparisonOperatorIsHonoured(PredicateOperator op, double actual, bool advances)
    {
        var sequencer = new Sequencer(
            "SEQ01",
            [
                new SequenceStep("Wait", [], StepTransition.When("CV001.Speed", op, TagValue.Double(1.0))),
                new SequenceStep("Done", [], StepTransition.After(TimeSpan.FromSeconds(10))),
            ],
            Period);

        var scan = new Scan(sequencer);
        scan.Set("CV001.Speed", 0.0).Once();
        Pulse(scan, "Start");
        scan.Set("CV001.Speed", actual).Once();

        Assert.Equal(advances ? 2L : 1L, scan.Int64("Step"));
    }

    [Fact]
    public void AnAfterTransitionAdvancesWhenTheStepClockReachesIt()
    {
        Scan scan = Idle().Once();
        Pulse(scan, "Start");
        scan.Set("CV001.Speed", 2.0).Once();           // into step 2, After(0.3)

        Assert.Equal(2L, scan.Int64("Step"));
        scan.Times(2);                                 // 0.1 s, 0.2 s
        Assert.Equal(2L, scan.Int64("Step"));

        scan.Once();                                   // 0.3 s
        Assert.Equal(3L, scan.Int64("Step"));
    }

    [Fact]
    public void HoldFreezesTheStepClockAndResumeContinues()
    {
        Scan scan = Idle().Once();
        Pulse(scan, "Start");
        scan.Set("CV001.Speed", 2.0).Once();           // into step 2
        scan.Once();                                   // 0.1 s

        Pulse(scan, "Hold");
        Assert.True(scan.Bool("Held"));
        scan.Times(5);
        Assert.Equal(2L, scan.Int64("Step"));
        Assert.Equal(0.1, scan.Double("StepTime"), 12);

        Pulse(scan, "Resume");
        Assert.False(scan.Bool("Held"));
        scan.Times(2);                                 // 0.2 s, 0.3 s
        Assert.Equal(3L, scan.Int64("Step"));
    }

    [Fact]
    public void AbortReturnsToIdleAndIssuesTheAbortWrites()
    {
        Scan scan = Idle().Once();
        Pulse(scan, "Start");
        scan.Set("CV001.Speed", 2.0).Once();           // into step 2

        scan.Command("Abort", true).Once();

        Assert.Equal(0L, scan.Int64("Step"));
        Assert.False(scan.Bool("Running"));
        Assert.True(scan.TryWrite("CV001.Start", out TagValue value));
        Assert.False(value.AsBool);
        Assert.True(scan.TryWrite("FD001.Enabled", out TagValue feed));
        Assert.False(feed.AsBool);
        BlockEvent aborted = Assert.Single(scan.LastEvents);
        Assert.Equal("SEQUENCE_ABORTED", aborted.Code);
        Assert.Equal("Aborted at step 2.", aborted.Message);
    }

    [Fact]
    public void ATimeoutFaultsTheStepAndStopsTheSequence()
    {
        Scan scan = Idle(firstTimeout: TimeSpan.FromSeconds(0.2)).Once();
        Pulse(scan, "Start");

        scan.Times(2);                                 // 0.1 s, then 0.2 s

        Assert.True(scan.Bool("Faulted"));
        Assert.False(scan.Bool("Running"));
        Assert.Equal(1L, scan.Int64("Step"));
        BlockEvent faulted = Assert.Single(scan.LastEvents);
        Assert.Equal("SEQUENCE_FAULTED", faulted.Code);
        Assert.Equal("Step 1 timed out after 0.2 s.", faulted.Message);
    }

    [Fact]
    public void ATimeoutIssuesTheAbortWritesOnTheFaultScanAndNoOther()
    {
        Scan scan = Idle(firstTimeout: TimeSpan.FromSeconds(0.2)).Once();
        Pulse(scan, "Start");

        scan.Times(2);                                 // 0.1 s, then 0.2 s: step 1 times out

        Assert.True(scan.Bool("Faulted"));
        Assert.True(scan.TryWrite("CV001.Start", out TagValue start));
        Assert.False(start.AsBool);                    // the belt is stopped, not left running
        Assert.True(scan.TryWrite("FD001.Enabled", out TagValue feed));
        Assert.False(feed.AsBool);

        scan.Once();
        Assert.Empty(scan.LastWrites);
    }

    [Fact]
    public void StartIsIgnoredWhileFaulted()
    {
        Scan scan = Idle(firstTimeout: TimeSpan.FromSeconds(0.2)).Once();
        Pulse(scan, "Start");
        scan.Times(2);
        Assert.True(scan.Bool("Faulted"));

        Pulse(scan, "Start");

        Assert.True(scan.Bool("Faulted"));
        Assert.False(scan.Bool("Running"));
    }

    [Fact]
    public void ResetFromFaultedReturnsToIdle()
    {
        Scan scan = Idle(firstTimeout: TimeSpan.FromSeconds(0.2)).Once();
        Pulse(scan, "Start");
        scan.Times(2);

        Pulse(scan, "Reset");

        Assert.False(scan.Bool("Faulted"));
        Assert.Equal(0L, scan.Int64("Step"));
        Assert.Equal(0.0, scan.Double("StepTime"));
    }

    [Fact]
    public void TheLastStepCompletesTheSequence()
    {
        Scan scan = Idle().Once();
        Pulse(scan, "Start");
        scan.Set("CV001.Speed", 2.0).Once();           // into step 2
        scan.Times(3);                                 // After(0.3) elapses, into step 3
        Assert.Equal(3L, scan.Int64("Step"));

        scan.Set("CV001.Stopped", true).Once();

        Assert.True(scan.Bool("Complete"));
        Assert.False(scan.Bool("Running"));
        Assert.Equal(0L, scan.Int64("Step"));
        BlockEvent complete = Assert.Single(scan.LastEvents);
        Assert.Equal("SEQUENCE_COMPLETE", complete.Code);
        Assert.Equal("Finished after 3 steps.", complete.Message);
    }

    [Fact]
    public void AOneStepSequenceCompletesWithASingularMessage()
    {
        var scan = new Scan(new Sequencer(
            "SEQ01",
            [new SequenceStep("Run", [], StepTransition.After(TimeSpan.FromSeconds(0.1)))],
            Period));
        scan.Once();
        Pulse(scan, "Start");

        scan.Once();                                   // 0.1 s: the only step ends

        Assert.True(scan.Bool("Complete"));
        BlockEvent complete = Assert.Single(scan.LastEvents);
        Assert.Equal("SEQUENCE_COMPLETE", complete.Code);
        Assert.Equal("Finished after 1 step.", complete.Message);
    }

    [Fact]
    public void ResetFromCompleteReturnsToIdleAndStartRunsItAgain()
    {
        Scan scan = Idle().Once();
        Pulse(scan, "Start");
        scan.Set("CV001.Speed", 2.0).Once();
        scan.Times(3);
        scan.Set("CV001.Stopped", true).Once();
        Assert.True(scan.Bool("Complete"));

        Pulse(scan, "Reset");
        Assert.False(scan.Bool("Complete"));

        scan.Set("CV001.Speed", 0.0).Set("CV001.Stopped", false);
        Pulse(scan, "Start");
        Assert.Equal(1L, scan.Int64("Step"));
    }

    [Fact]
    public void StepTimeReportsTheTimeInTheCurrentStep()
    {
        Scan scan = Idle().Once();
        Pulse(scan, "Start");
        Assert.Equal(0.0, scan.Double("StepTime"));

        scan.Times(2);
        Assert.Equal(0.2, scan.Double("StepTime"), 12);

        scan.Set("CV001.Speed", 2.0).Once();           // into step 2
        Assert.Equal(0.0, scan.Double("StepTime"));
    }

    [Fact]
    public void AbortIsIgnoredWhenTheSequenceIsIdle()
    {
        Scan scan = Idle().Once();

        scan.Command("Abort", true).Once();

        Assert.Equal(0L, scan.Int64("Step"));
        Assert.Empty(scan.Events);
    }

    [Fact]
    public void ResumeWithoutAHoldDoesNothing()
    {
        Scan scan = Idle().Once();
        Pulse(scan, "Start");

        Pulse(scan, "Resume");

        Assert.False(scan.Bool("Held"));
        Assert.Equal(1L, scan.Int64("Step"));
        Assert.Equal("STEP_ENTERED", scan.Codes());
    }
}
