using Dse.Core.Io;
using Dse.Core.Tests.Fakes;
using Dse.Core.Time;
using Dse.Io;
using Xunit;

namespace Dse.Core.Tests;

public class PlantTagsTests
{
    private static SimulationOptions Options => new()
    {
        Seed = 1UL,
        StartTime = new DateTimeOffset(2026, 1, 1, 6, 0, 0, TimeSpan.Zero),
        TimeStep = TimeSpan.FromMilliseconds(10),
    };

    [Fact]
    public void ThePlantTagsAreTheDirectoryBuildPublishes()
    {
        var u = new Thermostat("U");
        SimulationBuilder builder = new SimulationBuilder(Options)
            .Add(new Thermostat("T"))
            .Add(u)
            .Bind("U.SP", TagBinding.Write("x", u.Setpoint, "°C", 0.0, 50.0, "Renamed setpoint"));

        IReadOnlyList<TagDescriptor> tags = builder.PlantTags();
        Simulation sim = builder.Build();

        Assert.Equal(sim.IO.Directory.Tags, tags);
        Assert.Contains(tags, t => string.Equals(t.Name, "U.SP", StringComparison.Ordinal));
        Assert.DoesNotContain(tags, t => string.Equals(t.Name, "U.Setpoint", StringComparison.Ordinal));
    }

    [Fact]
    public void AWritableTagOnADrivenInputIsReadOnly()
    {
        var t = new Thermostat("T");
        var s = new Setpoint("S", 12.0);
        s.Out.ConnectTo(t.Setpoint);
        SimulationBuilder builder = new SimulationBuilder(Options).Add(s).Add(t);

        IReadOnlyList<TagDescriptor> tags = builder.PlantTags();

        Assert.Equal(TagAccess.ReadOnly, Assert.Single(tags, d => string.Equals(d.Name, "T.Setpoint", StringComparison.Ordinal)).Access);
        Assert.Equal(TagAccess.ReadWrite, Assert.Single(tags, d => string.Equals(d.Name, "T.Enable", StringComparison.Ordinal)).Access);
    }

    [Fact]
    public void ABlocksOwnedTagsAreNotPlantTagsAndTheBuilderStillBuilds()
    {
        SimulationBuilder builder = new SimulationBuilder(Options)
            .Add(new Thermostat("T"))
            .AddScanBlock(new EchoBlock("B", TimeSpan.FromMilliseconds(20)).Reads("T.Enable").Publishes("Q"));

        IReadOnlyList<TagDescriptor> tags = builder.PlantTags();
        Simulation sim = builder.Build();

        Assert.DoesNotContain(tags, t => string.Equals(t.Name, "B.Q", StringComparison.Ordinal));
        Assert.True(sim.IO.Directory.TryFind("B.Q", out _));
        Assert.Equal(tags.Count + 1, sim.IO.Directory.Count);
    }
}
