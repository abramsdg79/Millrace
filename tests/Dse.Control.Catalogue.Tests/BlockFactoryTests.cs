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
}
