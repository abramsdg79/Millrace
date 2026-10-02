using Dse.Core.Catalogue;
using Dse.Io;

namespace Dse.Control.Catalogue.Tests;

public class BlockFactoryTests
{
    [Theory]
    [InlineData("on-delay", TimerMode.OnDelay)]
    [InlineData("off-delay", TimerMode.OffDelay)]
    [InlineData("pulse", TimerMode.Pulse)]
    public void ATimerMapsEveryMode(string mode, TimerMode expected)
    {
        ParameterValues values = Bind.Values(
            TimerCatalogue.Descriptor.Parameters, $$"""{ "mode": "{{mode}}", "input": "V1.Running", "presetS": 1.5 }""");

        var timer = (Timer)TimerCatalogue.Descriptor.Factory("TMR01", Bind.Period, values);

        Assert.Equal(expected, timer.Mode);
        Assert.Equal(TimeSpan.FromSeconds(1.5), timer.Preset);
        Assert.Equal(Bind.Period, timer.ScanPeriod);
        Assert.Equal(new TagRef("V1.Running", TagKind.Bool), Assert.Single(timer.Inputs));
    }

    [Fact]
    public void APermissiveReadsItsConditionsInOrder()
    {
        ParameterValues values = Bind.Values(
            PermissiveCatalogue.Descriptor.Parameters,
            """{ "conditions": [ { "tag": "V1.Tripped", "normal": false }, { "tag": "V1.Running", "normal": true } ] }""");

        IScanBlock block = PermissiveCatalogue.Descriptor.Factory("PERM01", Bind.Period, values);

        Assert.Equal(["V1.Tripped", "V1.Running"], block.Inputs.Select(i => i.Name));
        Assert.Empty(block.Writes);
    }

    [Fact]
    public void AnInterlockCommandsItsTripWrites()
    {
        ParameterValues values = Bind.Values(
            InterlockCatalogue.Descriptor.Parameters,
            """{ "conditions": [ { "tag": "V1.Tripped", "normal": false } ], "trip": [ { "tag": "V1.Fill", "value": false } ] }""");

        IScanBlock block = InterlockCatalogue.Descriptor.Factory("INT01", Bind.Period, values);

        Assert.Equal(new TagRef("V1.Tripped", TagKind.Bool), Assert.Single(block.Inputs));
        Assert.Equal(new TagRef("V1.Fill", TagKind.Bool), Assert.Single(block.Writes));
    }

    [Fact]
    public void AnInterlocksTripWritesDefaultToNone()
    {
        ParameterValues values = Bind.Values(
            InterlockCatalogue.Descriptor.Parameters, """{ "conditions": [ { "tag": "V1.Tripped", "normal": false } ] }""");

        IScanBlock block = InterlockCatalogue.Descriptor.Factory("INT01", Bind.Period, values);

        Assert.Empty(block.Writes);
    }

    [Fact]
    public void AnInterlocksResetWritesBindAndShareAPinWithItsTripWrites()
    {
        ParameterValues values = Bind.Values(
            InterlockCatalogue.Descriptor.Parameters,
            """
            { "conditions": [ { "tag": "V1.Tripped", "normal": false } ],
              "trip": [ { "tag": "V1.Fill", "value": false } ],
              "reset": [ { "tag": "V1.Fill", "value": true } ] }
            """);

        IScanBlock block = InterlockCatalogue.Descriptor.Factory("INT01", Bind.Period, values);

        Assert.Equal(new TagRef("V1.Fill", TagKind.Bool), Assert.Single(block.Writes));
    }

    [Fact]
    public void AnAlarmOwnsAPairPerConfiguredLimitInLimitOrder()
    {
        ParameterValues values = Bind.Values(
            AlarmCatalogue.Descriptor.Parameters,
            """{ "input": "V1.Level", "limits": [ { "kind": "hi-hi", "value": 95 }, { "kind": "lo", "value": 20 } ] }""");

        Assert.Equal(
            new[]
            {
                ("CUR01.Lo.Active", TagAccess.ReadOnly),
                ("CUR01.Lo.Acked", TagAccess.ReadOnly),
                ("CUR01.HiHi.Active", TagAccess.ReadOnly),
                ("CUR01.HiHi.Acked", TagAccess.ReadOnly),
                ("CUR01.Ack", TagAccess.ReadWrite),
            },
            AlarmCatalogue.Descriptor.OwnedTags("CUR01", values).Select(t => (t.Spec.Name, t.Access)));
    }

    [Fact]
    public void ASequencerReadsItsTransitionsAndCommandsItsStepAndAbortWrites()
    {
        ParameterValues values = Bind.Values(
            SequencerCatalogue.Descriptor.Parameters,
            """
            { "steps": [
                { "name": "Fill", "writes": [ { "tag": "V1.Fill", "value": true } ],
                  "transition": { "type": "when", "tag": "V1.Level", "op": ">=", "value": 80 }, "timeoutS": 30 },
                { "name": "Settle", "transition": { "type": "after", "delayS": 5 } } ],
              "abort": [ { "tag": "V1.Fill", "value": false } ] }
            """);

        IScanBlock block = SequencerCatalogue.Descriptor.Factory("SEQ01", Bind.Period, values);

        Assert.Equal(new TagRef("V1.Level", TagKind.Double), Assert.Single(block.Inputs));
        Assert.Equal(new TagRef("V1.Fill", TagKind.Bool), Assert.Single(block.Writes));
        Assert.Equal(Bind.Period, block.ScanPeriod);
    }

    [Fact]
    public void ADurationLongerThanAYearIsOutOfRange()
    {
        (ParameterValues? values, List<BindingIssue> issues) = Bind.TryValues(
            TimerCatalogue.Descriptor.Parameters, """{ "mode": "pulse", "input": "V1.Running", "presetS": 1e30 }""");

        Assert.Null(values);
        BindingIssue issue = Assert.Single(issues);
        Assert.Equal(BindingIssueKind.BadParameter, issue.Kind);
        Assert.Equal("$.presetS", issue.Path);
        Assert.Contains("[0, 31536000]", issue.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ACoilReadsItsConditionAndCommandsItsOutput()
    {
        ParameterValues values = Bind.Values(
            CoilCatalogue.Descriptor.Parameters, """{ "condition": { "tag": "V1.Tripped", "normal": false }, "output": "V1.Fill" }""");

        IScanBlock block = CoilCatalogue.Descriptor.Factory("COIL01", Bind.Period, values);

        Assert.IsType<Coil>(block);
        Assert.Equal(new TagRef("V1.Tripped", TagKind.Bool), Assert.Single(block.Inputs));
        Assert.Equal(new TagRef("V1.Fill", TagKind.Bool), Assert.Single(block.Writes));
        Assert.Equal(Bind.Period, block.ScanPeriod);
        Assert.Equal(
            (new TagSpec("COIL01.Energised", TagKind.Bool, "", "The condition is at its normal value; the output is driven true"), TagAccess.ReadOnly),
            Assert.Single(CoilCatalogue.Descriptor.OwnedTags("COIL01", values)));
    }

    [Theory]
    [InlineData("""{ "condition": { "tag": "V1.Tripped", "normal": false }, "output": "V1.Running" }""", "$.output")]
    [InlineData("""{ "condition": { "tag": "V1.Level", "normal": false }, "output": "V1.Fill" }""", "$.condition.tag")]
    [InlineData("""{ "condition": { "tag": "V1.Tripped" }, "output": "V1.Fill" }""", "$.condition.normal")]
    public void ACoilRefusesAReadOnlyOutputANonBoolConditionAndAMissingNormal(string json, string path)
    {
        (ParameterValues? values, List<BindingIssue> issues) = Bind.TryValues(CoilCatalogue.Descriptor.Parameters, json);

        Assert.Null(values);
        Assert.Equal(path, Assert.Single(issues).Path);
    }
}
