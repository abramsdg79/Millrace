using Dse.Core.Contexts;
using Dse.Core.Graph;
using Dse.Core.Io;
using Dse.Core.Logging;
using Dse.Core.Tests.Fakes;
using Dse.Core.Time;
using Dse.Core.Validation;
using Dse.Io;
using Xunit;

namespace Dse.Core.Tests;

/// <summary>
/// Spec 6d §2, the builder half: <c>AddScanBlock(block, claims)</c>, DSE016,
/// and a claimed tag seen end to end through a built <see cref="Simulation"/>.
/// </summary>
public class ClaimValidationTests
{
    private static SimulationOptions Options => new()
    {
        Seed = 1UL,
        StartTime = new DateTimeOffset(2026, 1, 1, 6, 0, 0, TimeSpan.Zero),
        TimeStep = TimeSpan.FromMilliseconds(10),
    };

    /// <summary>Two thermostats and a fuse: T.Enable is read and U.Enable commanded; a fuse's Ok drives U.Enable in the demoted-link test.</summary>
    private static SimulationBuilder Plant() =>
        new SimulationBuilder(Options).Add(new Thermostat("T")).Add(new Thermostat("U")).Add(new Fuse("F"));

    /// <summary>Reads T.Enable, commands U.Enable, owns the command Cmd; every other tick.</summary>
    private static EchoBlock Block(string id) =>
        new EchoBlock(id, TimeSpan.FromMilliseconds(20)).Reads("T.Enable").MayWrite("U.Enable").Accepts("Cmd");

    private static ValidationError OnlyDse016(SimulationBuilder builder)
    {
        ValidationResult result = builder.Validate();
        Assert.False(result.IsValid);
        return Assert.Single(result.Errors, e => e.Code == "DSE016");
    }

    /// <summary>A component with one required Bool input and a writable tag on it.</summary>
    private sealed class Needy : ComponentBase, ITagProvider
    {
        public Needy(string id)
            : base(id)
        {
            Go = AddInput<bool>("Go", required: true);
        }

        public InputPort<bool> Go { get; }

        public override void Evaluate(in TickContext ctx)
        {
        }

        public IEnumerable<TagBinding> DescribeTags() => [TagBinding.Write("Go", Go, "Run request")];
    }

    [Fact]
    public void AClaimOnATagTheBlockCommandsPublishesItReadOnlyNamingTheBlock()
    {
        Simulation sim = Plant().AddScanBlock(Block("A"), ["U.Enable"]).Build();

        TagDescriptor enable = sim.IO.Directory.Find("U.Enable");
        Assert.Equal((TagAccess.ReadOnly, "A"), (enable.Access, enable.ClaimedBy));
        Assert.Equal(TagAccess.ReadWrite, sim.IO.Directory.Find("U.Setpoint").Access);
        Assert.Single(sim.IO.Directory.Tags, t => t.ClaimedBy.Length > 0);
    }

    [Fact]
    public void WithoutClaimsNoTagIsClaimed()
    {
        Simulation sim = Plant().AddScanBlock(Block("A")).Build();

        Assert.All(sim.IO.Directory.Tags, t => Assert.Equal(string.Empty, t.ClaimedBy));
        Assert.Equal(TagAccess.ReadWrite, sim.IO.Directory.Find("U.Enable").Access);
    }

    [Fact]
    public void TheClaimantsWriteLandsAndLogsExactlyAsBefore()
    {
        EchoBlock block = Block("A");
        block.WriteOnce = true;
        Simulation sim = Plant().AddScanBlock(block, ["U.Enable"]).Build();

        sim.Tick();                                   // tick 0: the scan queues the write
        sim.Tick();                                   // tick 1: phase 1 applies it

        SimEventRecord record = Assert.Single(sim.Events.Records);
        Assert.Equal((1L, "U.Enable", "WRITE", "Set to true by A."), (record.Tick, record.Source, record.Code, record.Message));
        Assert.True(sim.IO.ReadBool("U.Enable"));
    }

    [Fact]
    public void AnExternalWriteAndAScheduledWriteToAClaimedTagAreRefusedAndNothingLands()
    {
        Simulation sim = Plant().AddScanBlock(Block("A"), ["U.Enable"]).Build();
        int index = sim.IO.Directory.Find("U.Enable").Index;
        const string Refusal = "Tag 'U.Enable' is claimed by A; only that block writes it.";

        Assert.Equal(Refusal, Assert.Throws<InvalidOperationException>(() => sim.IO.Write("U.Enable", TagValue.Bool(true))).Message);
        Assert.Equal(Refusal, Assert.Throws<InvalidOperationException>(() => sim.IO.Write(index, TagValue.Bool(true))).Message);
        Assert.Equal(Refusal, Assert.Throws<InvalidOperationException>(
            () => sim.WriteAt(TimeSpan.FromMilliseconds(30), "U.Enable", TagValue.Bool(true))).Message);
        Assert.Equal(Refusal, Assert.Throws<InvalidOperationException>(
            () => sim.WriteIn(TimeSpan.FromMilliseconds(30), "U.Enable", TagValue.Bool(true))).Message);
        Assert.Equal(0, sim.IO.PendingWrites);

        sim.RunFor(TimeSpan.FromMilliseconds(100));

        Assert.Empty(sim.Events.Records);
        Assert.False(sim.IO.ReadBool("U.Enable"));
    }

    [Fact]
    public void AClaimedRequiredInputStillCountsAsDriven()
    {
        SimulationBuilder builder = new SimulationBuilder(Options)
            .Add(new Needy("N"))
            .AddScanBlock(new EchoBlock("A", TimeSpan.FromMilliseconds(20)).MayWrite("N.Go"), ["N.Go"]);

        ValidationResult result = builder.Validate();

        Assert.True(result.IsValid, string.Join(" | ", result.Errors.Select(e => e.Message)));
        Assert.Equal("A", builder.Build().IO.Directory.Find("N.Go").ClaimedBy);
    }

    [Fact]
    public void AClaimNamingNoTagIsDse016()
    {
        ValidationError error = OnlyDse016(Plant().AddScanBlock(Block("A"), ["U.Enabel"]));

        Assert.Equal(
            "Block 'A' claims tag 'U.Enabel', which the plant does not have. Check the name against 'dse tags' — 'U.Enable' is " +
            "closest; a block claims a tag it commands.",
            error.Message);
        Assert.Equal(["A"], error.ComponentIds);
        Assert.Equal(("U.Enabel", 0), (error.Tag, error.ClaimIndex));
    }

    [Theory]
    [InlineData("u.enable")]
    [InlineData(" U.Enable")]
    [InlineData("")]
    public void AClaimIsMatchedOrdinallyAndExactly(string claim)
    {
        ValidationError error = OnlyDse016(Plant().AddScanBlock(Block("A"), [claim]));

        Assert.StartsWith($"Block 'A' claims tag '{claim}', which the plant does not have.", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("u.enable", "U.Enable")]
    [InlineData(" U.Enable", "U.Enable")]
    [InlineData("U.Setpont", "U.Setpoint")]
    public void AClaimNearATagIsHintedWithTheNearestName(string claim, string closest)
    {
        ValidationError error = OnlyDse016(Plant().AddScanBlock(Block("A"), [claim]));

        Assert.Equal(
            $"Block 'A' claims tag '{claim}', which the plant does not have. Check the name against 'dse tags' — '{closest}' is " +
            "closest; a block claims a tag it commands.",
            error.Message);
    }

    [Theory]
    [InlineData("Heater9.RunPermit")]
    [InlineData("")]
    public void AClaimNearNoTagKeepsThePlainFix(string claim)
    {
        ValidationError error = OnlyDse016(Plant().AddScanBlock(Block("A"), [claim]));

        Assert.Equal(
            $"Block 'A' claims tag '{claim}', which the plant does not have. Check the name against 'dse tags'; a block claims " +
            "a tag it commands.",
            error.Message);
    }

    [Fact]
    public void TheBlocksOwnWritesAreSearchedBeforeEveryOtherTag()
    {
        // 'T.Enable' is as near to 'V.Enable' as 'U.Enable' and sorts first; the block commands only U.Enable.
        ValidationError error = OnlyDse016(Plant().AddScanBlock(Block("A"), ["V.Enable"]));

        Assert.Contains("— 'U.Enable' is closest;", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AWriteThePlantDoesNotHaveIsNeverTheHint()
    {
        ValidationResult result = Plant().AddScanBlock(Block("A").MayWrite("U.Enabel"), ["U.Enabel"]).Validate();

        ValidationError claim = Assert.Single(result.Errors, e => e.Code == "DSE016");
        Assert.Contains("— 'U.Enable' is closest;", claim.Message, StringComparison.Ordinal);
        Assert.Single(result.Errors, e => e.Code == "DSE014");
    }

    [Fact]
    public void AClaimOnAReadOnlyTagIsDse016()
    {
        ValidationError error = OnlyDse016(Plant().AddScanBlock(Block("A").MayWrite("A.Q").Publishes("Q"), ["A.Q"]));

        Assert.Equal(
            "Block 'A' claims tag 'A.Q', which is read-only. Claim a read-write tag the block commands, not a measured value, " +
            "a block's output or an input a signal link drives.",
            error.Message);
    }

    [Fact]
    public void AClaimOnAWritableTagDemotedByASignalLinkIsDse016()
    {
        var fuse = new Fuse("F");
        var heater = new Thermostat("U");
        fuse.Ok.ConnectTo(heater.Enable);
        SimulationBuilder builder = new SimulationBuilder(Options)
            .Add(new Thermostat("T")).Add(heater).Add(fuse)
            .AddScanBlock(Block("A"), ["U.Enable"]);

        ValidationError error = OnlyDse016(builder);

        Assert.StartsWith("Block 'A' claims tag 'U.Enable', which is read-only.", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AClaimOnATagTheBlockDoesNotCommandIsDse016()
    {
        ValidationError error = OnlyDse016(Plant().AddScanBlock(Block("A"), ["U.Setpoint"]));

        Assert.Equal(
            "Block 'A' claims tag 'U.Setpoint', which it does not command. Add the tag to the block's writes, or remove the claim.",
            error.Message);
    }

    [Fact]
    public void ATagClaimedTwiceByOneBlockIsOneDse016()
    {
        ValidationError error = OnlyDse016(Plant().AddScanBlock(Block("A"), ["U.Enable", "U.Enable"]));

        Assert.Equal("Block 'A' claims tag 'U.Enable', which it already claims. Claim each tag once.", error.Message);
        Assert.Equal(("U.Enable", 1), (error.Tag, error.ClaimIndex));
    }

    [Fact]
    public void ARepeatedClaimOnAMissingTagIsReportedOnceAtEachPosition()
    {
        ValidationResult result = Plant().AddScanBlock(Block("A"), ["U.Enabel", "U.Enabel"]).Validate();

        Assert.Equal(
            new[]
            {
                ("Block 'A' claims tag 'U.Enabel', which the plant does not have.", 0),
                ("Block 'A' claims tag 'U.Enabel', which it already claims.", 1),
            },
            result.Errors.Where(e => e.Code == "DSE016").Select(e => (e.Message[..(e.Message.IndexOf(". ", StringComparison.Ordinal) + 1)], e.ClaimIndex)));
    }

    [Fact]
    public void ATagClaimedByTwoBlocksIsOneDse016OnTheSecond()
    {
        ValidationError error = OnlyDse016(Plant().AddScanBlock(Block("A"), ["U.Enable"]).AddScanBlock(Block("B"), ["U.Enable"]));

        Assert.Equal(
            "Block 'B' claims tag 'U.Enable', which 'A' already claims. A tag has one claimant; remove one of the claims.",
            error.Message);
        Assert.Equal(["B", "A"], error.ComponentIds);
        Assert.Equal(0, error.ClaimIndex);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AnotherBlockCommandingAClaimedTagIsDse016WhicheverWasAddedFirst(bool claimantFirst)
    {
        SimulationBuilder builder = Plant();
        if (claimantFirst)
        {
            builder.AddScanBlock(Block("A"), ["U.Enable"]).AddScanBlock(Block("B"));
        }
        else
        {
            builder.AddScanBlock(Block("B")).AddScanBlock(Block("A"), ["U.Enable"]);
        }

        ValidationError error = OnlyDse016(builder);

        Assert.Equal(
            "Block 'B' commands tag 'U.Enable', which 'A' claims. Only the claiming block writes a claimed tag; remove the " +
            "write from 'B', or this claim.",
            error.Message);
        Assert.Equal(["B", "A"], error.ComponentIds);
        Assert.Equal(("U.Enable", 0), (error.Tag, error.ClaimIndex));
    }

    [Fact]
    public void AnotherBlocksWritePointsAtTheClaimantsAcceptedClaimNotItsRepeat()
    {
        ValidationResult result = Plant()
            .AddScanBlock(Block("A"), ["U.Setpoint", "U.Enable", "U.Enable"]).AddScanBlock(Block("B")).Validate();

        Assert.Equal(
            new[] { ("A", 0), ("A", 2), ("B", 1) },
            result.Errors.Where(e => e.Code == "DSE016").Select(e => (e.ComponentIds[0], e.ClaimIndex)));
    }

    [Fact]
    public void ABlockMayClaimAnotherBlocksCommandAndThenOnlyItWritesIt()
    {
        EchoBlock resetter = new EchoBlock("R", TimeSpan.FromMilliseconds(20)).MayWrite("A.Cmd");
        resetter.WriteOnce = true;
        Simulation sim = Plant().AddScanBlock(Block("A")).AddScanBlock(resetter, ["A.Cmd"]).Build();

        Assert.Equal((TagAccess.ReadOnly, "R"), (sim.IO.Directory.Find("A.Cmd").Access, sim.IO.Directory.Find("A.Cmd").ClaimedBy));
        Assert.Throws<InvalidOperationException>(() => sim.IO.Write("A.Cmd", TagValue.Bool(true)));

        sim.RunFor(TimeSpan.FromMilliseconds(20));

        Assert.Contains(sim.Events.Records, r => r.Source == "A.Cmd" && r.Message == "Set to true by R.");
    }

    [Fact]
    public void ABlockMayClaimItsOwnCommandWhenItCommandsIt()
    {
        Simulation sim = Plant().AddScanBlock(Block("A").MayWrite("A.Cmd"), ["A.Cmd"]).Build();

        Assert.Equal("A", sim.IO.Directory.Find("A.Cmd").ClaimedBy);
        Assert.Throws<InvalidOperationException>(() => sim.IO.Write("A.Cmd", TagValue.Bool(true)));
    }

    [Fact]
    public void AClaimFollowsTheNameAnExplicitBindGaveThePort()
    {
        var heater = new Thermostat("U");
        SimulationBuilder builder = new SimulationBuilder(Options)
            .Add(new Thermostat("T")).Add(heater)
            .Bind("Heater.Run", TagBinding.Write("Run", heater.Enable, "Heater run"))
            .AddScanBlock(new EchoBlock("A", TimeSpan.FromMilliseconds(20)).MayWrite("Heater.Run"), ["Heater.Run"]);

        Simulation sim = builder.Build();

        Assert.Equal("A", sim.IO.Directory.Find("Heater.Run").ClaimedBy);
        Assert.False(sim.IO.Directory.TryFind("U.Enable", out _));
    }

    [Fact]
    public void ANullClaimIsRefusedWhenTheBlockIsAdded()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(
            () => Plant().AddScanBlock(Block("A"), ["U.Enable", null!]));

        Assert.Equal("claims", error.ParamName);
        Assert.StartsWith("Block 'A' has a null claim. Name each claimed tag.", error.Message, StringComparison.Ordinal);
    }
}
