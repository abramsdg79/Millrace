using Dse.Core;
using Dse.Io;

namespace Dse.Configuration.Tests;

public class WireAndBuildTests
{
    [Fact]
    public void AValidPlantComesBackReadyToBuild()
    {
        LoadResult result = Plants.Load(Plants.Minimal);

        Assert.True(result.IsValid);
        Assert.NotNull(result.Builder);
        Assert.Equal(new PlantSummary(3, 0, 2, 0), result.Summary);
        Assert.Equal(["CHUTE", "FEED", "PILE"], result.Nodes.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(1UL, result.Options!.Seed);
        Assert.Equal(TimeSpan.FromMilliseconds(10), result.Options.TimeStep);
        Assert.Equal(new DateTimeOffset(2026, 1, 1, 6, 0, 0, TimeSpan.Zero), result.Options.StartTime);

        Simulation simulation = result.Builder.Build();
        simulation.RunFor(TimeSpan.FromSeconds(5));
        Assert.True(simulation.IO.ReadDouble("PILE.Received") > 0.0);
    }

    [Fact]
    public void LoadOptionsOverrideTheFilesDefaults()
    {
        LoadResult result = Plants.Load(Plants.Minimal, new LoadOptions { TimeStep = TimeSpan.FromMilliseconds(5), Seed = 9UL });

        Assert.Equal(TimeSpan.FromMilliseconds(5), result.Options!.TimeStep);
        Assert.Equal(9UL, result.Options.Seed);
        Assert.Equal(new DateTimeOffset(2026, 1, 1, 6, 0, 0, TimeSpan.Zero), result.Options.StartTime);
    }

    [Fact]
    public void PortNamesMatchIgnoringCase()
    {
        string json = Plants.Minimal.Replace("\"FEED.Out\"", "\"FEED.out\"", StringComparison.Ordinal);

        Assert.True(Plants.Load(json).IsValid);
    }

    [Fact]
    public void ComponentIdsDoNot()
    {
        ConfigDiagnostic d = Plants.Only(Plants.Minimal.Replace("\"FEED.Out\"", "\"feed.Out\"", StringComparison.Ordinal));

        Assert.Equal("DSE108", d.Code);
        Assert.Equal("$.flows[0].from", d.Path);
        Assert.Contains("'FEED' is closest", d.Fix, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnknownPortListsThePortsThereAre()
    {
        ConfigDiagnostic d = Plants.Only(Plants.Minimal.Replace("\"CHUTE.In\"", "\"CHUTE.Inn\"", StringComparison.Ordinal));

        Assert.Equal("DSE108", d.Code);
        Assert.Equal("$.flows[0].to", d.Path);
        Assert.Contains("Full, In, Level, Out", d.Fix, StringComparison.Ordinal);
        Assert.Contains("'In' is closest", d.Fix, StringComparison.Ordinal);
    }

    [Fact]
    public void AnAddressWithoutADotIsNotAnAddress()
    {
        ConfigDiagnostic d = Plants.Only(Plants.Minimal.Replace("\"PILE.In\"", "\"PILE\"", StringComparison.Ordinal));

        Assert.Equal("DSE108", d.Code);
        Assert.Contains("<component>.<port>", d.Fix, StringComparison.Ordinal);
    }

    [Fact]
    public void BothEndsOfABadLinkAreReported()
    {
        string json = Plants.Minimal.Replace(
            "{ \"from\": \"CHUTE.Out\", \"to\": \"PILE.In\" }", "{ \"from\": \"CHUT.Out\", \"to\": \"PILE.Inn\" }", StringComparison.Ordinal);

        LoadResult result = Plants.Load(json);

        Assert.Equal(["$.flows[1].from", "$.flows[1].to"], result.Diagnostics.Select(d => d.Path));
    }

    [Fact]
    public void ASignalLinkCannotJoinDifferentValueTypes()
    {
        string json = Plants.Minimal.Replace(
            "\"flows\": [", "\"signals\": [ { \"from\": \"CHUTE.Full\", \"to\": \"FEED.Rate\" } ],\n  \"flows\": [", StringComparison.Ordinal);

        ConfigDiagnostic d = Plants.Only(json);

        Assert.Equal("DSE109", d.Code);
        Assert.Equal("$.signals[0]", d.Path);
        Assert.Contains("Boolean", d.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AMaterialLinkUnderSignalsIsSentToFlows()
    {
        string json = Plants.Minimal
            .Replace("{ \"from\": \"FEED.Out\", \"to\": \"CHUTE.In\" },", string.Empty, StringComparison.Ordinal)
            .Replace("\"flows\": [", "\"signals\": [ { \"from\": \"FEED.Out\", \"to\": \"CHUTE.In\" } ],\n  \"flows\": [", StringComparison.Ordinal);

        ConfigDiagnostic d = Assert.Single(Plants.Load(json).Diagnostics, x => x.Code == "DSE109");

        Assert.Contains("\"flows\"", d.Fix, StringComparison.Ordinal);
    }

    [Fact]
    public void ASignalLinkDrivesAnInput()
    {
        string json = Plants.Minimal.Replace(
            "\"flows\": [", "\"signals\": [ { \"from\": \"PILE.Full\", \"to\": \"FEED.Enabled\" } ],\n  \"flows\": [", StringComparison.Ordinal);

        // Wired the wrong way round on purpose — a full pile *enables* the feed — so the effect is unmistakable.
        Simulation simulation = Plants.Load(json).Builder!.Build();
        simulation.RunFor(TimeSpan.FromSeconds(5));

        Assert.Equal(0.0, simulation.IO.ReadDouble("PILE.Received"));
    }

    [Fact]
    public void AnExplicitTagBindsAndCanBeWritten()
    {
        string json = Plants.Minimal.Replace(
            "\"flows\": [",
            "\"tags\": [ { \"name\": \"AREA1.FEED_SP\", \"port\": \"FEED.Rate\", \"access\": \"write\", \"unit\": \"kg/s\", \"rangeLow\": 0, \"rangeHigh\": 50, \"description\": \"Feed rate setpoint\" } ],\n  \"flows\": [",
            StringComparison.Ordinal);

        LoadResult result = Plants.Load(json);
        Simulation simulation = result.Builder!.Build();
        TagDescriptor tag = simulation.IO.Directory.Find("AREA1.FEED_SP");

        Assert.Equal(1, result.Summary!.ExplicitTags);
        Assert.Equal(TagAccess.ReadWrite, tag.Access);
        Assert.Equal("kg/s", tag.Unit);
        Assert.Equal(50.0, tag.RangeHigh);
        Assert.Equal("Feed rate setpoint", tag.Description);
    }

    [Theory]
    [InlineData("FEED.Out", "read")]
    [InlineData("FEED.HopperMass", "write")]
    public void ATagThatCannotBindIsDse112(string port, string access)
    {
        string json = Plants.Minimal.Replace(
            "\"flows\": [", $"\"tags\": [ {{ \"name\": \"T\", \"port\": \"{port}\", \"access\": \"{access}\" }} ],\n  \"flows\": [", StringComparison.Ordinal);

        ConfigDiagnostic d = Plants.Only(json);

        Assert.Equal("DSE112", d.Code);
        Assert.Equal("$.tags[0]", d.Path);
    }

    [Fact]
    public void ALeafInsideACompositeCanBeAddressed()
    {
        string json = Corpus.Read("valid", "conveyor-line.json").Replace(
            "\"flows\": [", "\"tags\": [ { \"name\": \"CV001.MOTOR_AMPS_TRUE\", \"port\": \"CV001.Motor.Current\", \"unit\": \"A\" } ],\n  \"flows\": [", StringComparison.Ordinal);

        Simulation simulation = Plants.Load(json).Builder!.Build();

        Assert.Equal(TagAccess.ReadOnly, simulation.IO.Directory.Find("CV001.MOTOR_AMPS_TRUE").Access);
    }

    [Fact]
    public void CoreValidationPassesThroughSplitIntoMessageAndFix()
    {
        // 100 m/s with 0.5 m cells is 1 m per 10 ms tick: two cells per tick, which the belt's CFL check refuses.
        string json = Corpus.Read("invalid", "DSE006-belt-too-fast-for-its-cells.json");

        ConfigDiagnostic d = Plants.Only(json);

        Assert.Equal("DSE006", d.Code);
        Assert.Equal("$.components[2]", d.Path);
        Assert.EndsWith(".", d.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(d.Fix, d.Message, StringComparison.Ordinal);
        Assert.False(string.IsNullOrWhiteSpace(d.Fix));
    }
}
