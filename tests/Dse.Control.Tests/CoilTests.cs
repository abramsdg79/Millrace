using Dse.Control.Tests.Fakes;
using Dse.Core;
using Dse.Core.Time;
using Dse.Io;

namespace Dse.Control.Tests;

public class CoilTests
{
    private static readonly TimeSpan Period = TimeSpan.FromMilliseconds(100);

    /// <summary>Drives V1.Fill true while V1.Tripped is false.</summary>
    private static Coil Make(bool normal = false) => new("COIL01", new Condition("V1.Tripped", normal), "V1.Fill", Period);

    private static string? Written(Scan scan) =>
        scan.TryWrite("V1.Fill", out TagValue value) ? value.ToString() : null;

    [Fact]
    public void ThePinsAreTheConditionTheOutputAndEnergised()
    {
        Coil coil = Make();

        Assert.Equal(new TagRef("V1.Tripped", TagKind.Bool), Assert.Single(coil.Inputs));
        Assert.Equal(new TagRef("V1.Fill", TagKind.Bool), Assert.Single(coil.Writes));
        TagSpec energised = Assert.Single(coil.Outputs);
        Assert.Equal(("Energised", TagKind.Bool), (energised.Name, energised.Kind));
        Assert.Empty(coil.Commands);
    }

    [Fact]
    public void TheConstructorRejectsBlankNamesAndANonPositivePeriod()
    {
        Assert.Throws<ArgumentException>(() => new Coil(" ", new Condition("V1.Tripped", false), "V1.Fill", Period));
        Assert.Throws<ArgumentException>(() => new Coil("COIL01", new Condition(" ", false), "V1.Fill", Period));
        Assert.Throws<ArgumentException>(() => new Coil("COIL01", new Condition("V1.Tripped", false), " ", Period));
        Assert.Throws<ArgumentNullException>(() => new Coil("COIL01", null!, "V1.Fill", Period));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Coil("COIL01", new Condition("V1.Tripped", false), "V1.Fill", TimeSpan.Zero));
    }

    [Fact]
    public void TheFirstScanWritesTheOutputEvenWhenItIsFalse()
    {
        var scan = new Scan(Make());
        scan.Set("V1.Tripped", true).Once();

        Assert.Equal("false", Written(scan));
        Assert.False(scan.Bool("Energised"));
    }

    [Fact]
    public void ItEnergisesAndDeEnergisesWithItsConditionAndWritesOnlyOnTransitions()
    {
        var scan = new Scan(Make());
        var writes = new List<string?>();

        foreach (bool tripped in new[] { false, false, false, true, true, false, false })
        {
            scan.Set("V1.Tripped", tripped).Once();
            writes.Add(Written(scan));
        }

        Assert.Equal(["true", null, null, "false", null, "true", null], writes);
        Assert.True(scan.Bool("Energised"));
        Assert.Empty(scan.Events);
    }

    [Fact]
    public void NormalTrueInvertsTheCoil()
    {
        var scan = new Scan(Make(normal: true));

        scan.Set("V1.Tripped", false).Once();
        Assert.Equal("false", Written(scan));
        Assert.False(scan.Bool("Energised"));

        scan.Set("V1.Tripped", true).Once();
        Assert.Equal("true", Written(scan));
        Assert.True(scan.Bool("Energised"));
    }

    [Theory]
    [InlineData(Quality.Uncertain)]
    [InlineData(Quality.Bad)]
    public void ItReadsTheConditionsValueWhateverItsQualityAsTheInterlockDoes(Quality quality)
    {
        var coil = new Scan(Make(normal: true));
        var interlock = new Scan(new Interlock("INT01", [new Condition("V1.Tripped", true)], [], Period));
        TagValue value = TagValue.Bool(true, new TagQuality(quality, QualityDetail.SensorFailure));

        coil.Set("V1.Tripped", value).Once();
        interlock.Set("V1.Tripped", value).Once();

        Assert.True(coil.Bool("Energised"));
        Assert.Equal("true", Written(coil));
        Assert.False(interlock.Bool("Tripped"));
    }

    [Fact]
    public void OverAPlantItDrivesItsClaimedOutputAndLogsEachWriteWithItsId()
    {
        var options = new SimulationOptions
        {
            Seed = 1UL,
            StartTime = new DateTimeOffset(2026, 1, 1, 6, 0, 0, TimeSpan.Zero),
            TimeStep = TimeSpan.FromMilliseconds(10),
        };
        Simulation sim = new SimulationBuilder(options)
            .Add(new Vessel("V1", 10.0))
            .AddScanBlock(Make(), ["V1.Fill"])
            .Build();
        sim.WriteAt(TimeSpan.FromMilliseconds(500), "V1.Trip", TagValue.Bool(true));
        sim.WriteAt(TimeSpan.FromMilliseconds(800), "V1.Trip", TagValue.Bool(false));

        sim.RunFor(TimeSpan.FromSeconds(1.2));

        // Scans at ticks 0, 10, 20, …: tick 0 energises (lands tick 1); Tripped is
        // published at the end of tick 50, seen by the scan at tick 60 (lands 61);
        // cleared at the end of tick 80, seen at tick 90 (lands 91).
        Assert.Equal(
            [(1L, "Set to true by COIL01."), (61L, "Set to false by COIL01."), (91L, "Set to true by COIL01.")],
            sim.Events.Records.Where(r => r.Source == "V1.Fill").Select(r => (r.Tick, r.Message)));
        Assert.True(sim.IO.ReadBool("V1.Fill"));
        Assert.True(sim.IO.ReadBool("COIL01.Energised"));
        TagDescriptor fill = sim.IO.Directory.Find("V1.Fill");
        Assert.Equal((TagAccess.ReadOnly, "COIL01"), (fill.Access, fill.ClaimedBy));
        Assert.Throws<InvalidOperationException>(() => sim.WriteIn(TimeSpan.Zero, "V1.Fill", TagValue.Bool(false)));
    }
}
