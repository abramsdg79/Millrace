using Dse.Components.Mechanical;
using Dse.Core;
using Dse.Core.Time;
using Dse.Io;

namespace Dse.Realtime.Tests;

/// <summary>Spec 6d: a remote client writing a tag a block claims is refused like any read-only tag.</summary>
public class ClaimedTagCommandTests
{
    /// <summary>Commands K1.Permit and nothing else; never writes it, which is all this test needs.</summary>
    private sealed class PermitHolder : IScanBlock
    {
        public string Id => "INT01";

        public TimeSpan ScanPeriod => TimeSpan.FromMilliseconds(100);

        public IReadOnlyList<TagRef> Inputs { get; } = [];

        public IReadOnlyList<TagRef> Writes { get; } = [new TagRef("K1.Permit", TagKind.Bool)];

        public IReadOnlyList<TagSpec> Outputs { get; } = [];

        public IReadOnlyList<TagSpec> Commands { get; } = [];

        public void Scan(in ScanInputs inputs, ref ScanOutputs outputs)
        {
        }
    }

    [Fact]
    public void ACommandToAClaimedTagIsReadOnlyAndNeverQueued()
    {
        Simulation sim = new SimulationBuilder(new SimulationOptions
        {
            Seed = 1UL,
            StartTime = new DateTimeOffset(2026, 1, 1, 6, 0, 0, TimeSpan.Zero),
            TimeStep = TimeSpan.FromMilliseconds(10),
        }).Add(new MotorStarter("K1")).AddScanBlock(new PermitHolder(), ["K1.Permit"]).Build();
        var bus = new CommandBus(sim.IO);

        Assert.Equal(CommandOutcome.ReadOnly, bus.WriteBool("K1.Permit", true));
        Assert.Equal(CommandOutcome.Accepted, bus.WriteBool("K1.Command", true));

        Assert.Equal((1L, 1L), (bus.Accepted, bus.Rejected));
        Assert.Equal(1, sim.IO.PendingWrites);
        Assert.Equal("INT01", bus.Directory.Find("K1.Permit").ClaimedBy);
    }
}
