using Millrace.Io;

namespace Millrace.Control.Tests;

public class PermissiveTests
{
    private static readonly TimeSpan Period = TimeSpan.FromMilliseconds(100);

    /// <summary>Safety must be healthy (normal true) and the pile must not be full (normal false).</summary>
    private static Permissive Make() => new(
        "PERM01",
        [new Condition("CV001.SafetyOk", true), new Condition("Pile.Full", false)],
        Period);

    private static Scan Healthy()
    {
        var scan = new Scan(Make());
        scan.Set("CV001.SafetyOk", true).Set("Pile.Full", false);
        return scan;
    }

    [Fact]
    public void TheConstructorRejectsNoConditions()
    {
        Assert.Throws<ArgumentException>(() => new Permissive("PERM01", [], Period));
    }

    [Fact]
    public void TheConstructorRejectsABlankConditionTag()
    {
        Assert.Throws<ArgumentException>(
            () => new Permissive("PERM01", [new Condition(" ", true)], Period));
    }

    [Fact]
    public void ThePinsAreDeclaredInConditionOrder()
    {
        Permissive permissive = Make();

        Assert.Equal("PERM01", permissive.Id);
        Assert.Equal(2, permissive.Inputs.Count);
        Assert.Equal("CV001.SafetyOk", permissive.Inputs[0].Name);
        Assert.Equal("Pile.Full", permissive.Inputs[1].Name);
        Assert.All(permissive.Inputs, pin => Assert.Equal(TagKind.Bool, pin.Kind));
        Assert.Empty(permissive.Writes);
        Assert.Empty(permissive.Commands);
        Assert.Equal(2, permissive.Outputs.Count);
        Assert.Equal("Ok", permissive.Outputs[0].Name);
        Assert.Equal("FirstOut", permissive.Outputs[1].Name);
        Assert.Equal(TagKind.Int64, permissive.Outputs[1].Kind);
    }

    [Fact]
    public void OkIsTrueWhenEveryConditionIsNormal()
    {
        Scan scan = Healthy().Once();

        Assert.True(scan.Bool("Ok"));
        Assert.Equal(-1L, scan.Int64("FirstOut"));
        Assert.Empty(scan.Events);
    }

    [Fact]
    public void OkFallsAndFirstOutNamesTheFirstAbnormalCondition()
    {
        Scan scan = Healthy().Once();

        scan.Set("Pile.Full", true).Once();

        Assert.False(scan.Bool("Ok"));
        Assert.Equal(1L, scan.Int64("FirstOut"));
        BlockEvent raised = Assert.Single(scan.LastEvents);
        Assert.Equal("PERMISSIVE_LOST", raised.Code);
        Assert.Equal("Pile.Full dropped.", raised.Message);
    }

    [Fact]
    public void FirstOutKeepsTheFirstOfTwoSimultaneousLosses()
    {
        Scan scan = Healthy().Once();

        scan.Set("CV001.SafetyOk", false).Set("Pile.Full", true).Once();

        Assert.Equal(0L, scan.Int64("FirstOut"));
        Assert.Equal("CV001.SafetyOk dropped.", Assert.Single(scan.LastEvents).Message);
    }

    [Fact]
    public void OkReturnsAndFirstOutClearsWithoutAReset()
    {
        Scan scan = Healthy().Once();
        scan.Set("Pile.Full", true).Once();

        scan.Set("Pile.Full", false).Once();

        Assert.True(scan.Bool("Ok"));
        Assert.Equal(-1L, scan.Int64("FirstOut"));
        BlockEvent raised = Assert.Single(scan.LastEvents);
        Assert.Equal("PERMISSIVE_OK", raised.Code);
        Assert.Equal("All conditions normal.", raised.Message);
    }

    [Fact]
    public void AnEventIsRaisedOnEachChangeAndNotInBetween()
    {
        Scan scan = Healthy().Once();
        scan.Set("Pile.Full", true).Times(3);
        scan.Set("Pile.Full", false).Times(3);

        Assert.Equal("PERMISSIVE_LOST,PERMISSIVE_OK", scan.Codes());
    }
}
