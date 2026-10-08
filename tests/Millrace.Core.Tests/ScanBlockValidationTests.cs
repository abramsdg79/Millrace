using Millrace.Core.Io;
using Millrace.Core.Tests.Fakes;
using Millrace.Core.Time;
using Millrace.Core.Validation;
using Millrace.Io;
using Xunit;

namespace Millrace.Core.Tests;

public class ScanBlockValidationTests
{
    private static SimulationOptions Options => new()
    {
        Seed = 1UL,
        StartTime = new DateTimeOffset(2026, 1, 1, 6, 0, 0, TimeSpan.Zero),
        TimeStep = TimeSpan.FromMilliseconds(10),
    };

    private static SimulationBuilder Plant() => new SimulationBuilder(Options).Add(new Thermostat("T")).Add(new Fuse("F"));

    /// <summary>A block with one Bool input, one Bool output and one Bool command, scanning every other tick.</summary>
    private static EchoBlock Block(string id) =>
        new EchoBlock(id, TimeSpan.FromMilliseconds(20))
            .Reads("T.Enable")
            .Publishes("Q", TagKind.Bool, "", "The echo")
            .Accepts("Cmd", TagKind.Bool, "", "A command");

    private static ValidationError Only(SimulationBuilder builder)
    {
        ValidationResult result = builder.Validate();
        Assert.False(result.IsValid);
        return Assert.Single(result.Errors);
    }

    [Fact]
    public void APlantWithNoBlocksHasNoBlocksAndNoExtraTags()
    {
        Simulation sim = Plant().Build();

        Assert.Equal(0, sim.ScanBlockCount);
        Assert.Equal(3, sim.IO.Directory.Count);
    }

    [Fact]
    public void OwnedTagsJoinTheDirectoryWithTheirKindUnitAndDescription()
    {
        Simulation sim = Plant()
            .AddScanBlock(new EchoBlock("B", TimeSpan.FromMilliseconds(20))
                .Reads("T.Enable")
                .Publishes("Q", TagKind.Bool, "", "The echo")
                .Publishes("Count", TagKind.Int64, "", "Scans so far")
                .Publishes("Age", TagKind.Double, "s", "Seconds since the last scan")
                .Accepts("Cmd", TagKind.Bool, "", "A command"))
            .Build();

        Assert.Equal(1, sim.ScanBlockCount);
        Assert.Equal(7, sim.IO.Directory.Count);

        TagDescriptor q = sim.IO.Directory.Find("B.Q");
        Assert.Equal(TagKind.Bool, q.Kind);
        Assert.Equal(TagAccess.ReadOnly, q.Access);
        Assert.Equal("The echo", q.Description);

        TagDescriptor count = sim.IO.Directory.Find("B.Count");
        Assert.Equal(TagKind.Int64, count.Kind);
        Assert.Equal("count", count.Unit);

        TagDescriptor age = sim.IO.Directory.Find("B.Age");
        Assert.Equal(TagKind.Double, age.Kind);
        Assert.Equal("s", age.Unit);

        TagDescriptor command = sim.IO.Directory.Find("B.Cmd");
        Assert.Equal(TagAccess.ReadWrite, command.Access);
    }

    [Fact]
    public void ACommandIsWritableAndAnOutputIsNot()
    {
        Simulation sim = Plant().AddScanBlock(Block("B")).Build();

        sim.IO.Write("B.Cmd", TagValue.Bool(true));
        Assert.Equal(1, sim.IO.PendingWrites);

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => sim.IO.Write("B.Q", TagValue.Bool(true)));
        Assert.Contains("read-only", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AZeroScanPeriodIsMr013()
    {
        SimulationBuilder builder = Plant().AddScanBlock(
            new EchoBlock("B", TimeSpan.Zero).Reads("T.Enable"));

        ValidationError error = Only(builder);
        Assert.Equal("MR013", error.Code);
        Assert.Equal(
            "Block 'B' has a scan period of 0 ms. A scan period must be positive and a whole number of 10 ms steps.",
            error.Message);
    }

    [Fact]
    public void AScanPeriodThatIsNotAMultipleOfTheStepIsMr013()
    {
        SimulationBuilder builder = Plant().AddScanBlock(
            new EchoBlock("B", TimeSpan.FromMilliseconds(15)).Reads("T.Enable"));

        ValidationError error = Only(builder);
        Assert.Equal("MR013", error.Code);
        Assert.Equal(
            "Block 'B' scans every 15 ms, which is not a whole number of 10 ms steps. Use a period that is a multiple of the time step.",
            error.Message);
    }

    [Fact]
    public void AZeroTimeStepWithABlockAttachedDoesNotThrow()
    {
        var options = new SimulationOptions
        {
            Seed = 1UL,
            StartTime = new DateTimeOffset(2026, 1, 1, 6, 0, 0, TimeSpan.Zero),
            TimeStep = TimeSpan.Zero,
        };
        SimulationBuilder builder = new SimulationBuilder(options)
            .Add(new Thermostat("T"))
            .AddScanBlock(new EchoBlock("B", TimeSpan.FromMilliseconds(20)).Reads("T.Enable"));

        // The guard just stops the crash; nothing else in this minimal plant
        // (no flow nodes, so FlowGraph.Validate never sees dt) flags a zero
        // time step as invalid, so Validate() returns cleanly, valid.
        ValidationResult result = builder.Validate();

        Assert.True(result.IsValid);
    }

    [Fact]
    public void AnUnknownInputTagIsMr014()
    {
        SimulationBuilder builder = Plant().AddScanBlock(
            new EchoBlock("B", TimeSpan.FromMilliseconds(20)).Reads("T.Nope"));

        ValidationError error = Only(builder);
        Assert.Equal("MR014", error.Code);
        Assert.Equal(
            "Block 'B' reads tag 'T.Nope', which the plant does not have. Check the name against 'millrace tags', or bind the port it should read.",
            error.Message);
    }

    [Fact]
    public void AnInputOfTheWrongKindIsMr014()
    {
        SimulationBuilder builder = Plant().AddScanBlock(
            new EchoBlock("B", TimeSpan.FromMilliseconds(20)).Reads("T.Output"));

        ValidationError error = Only(builder);
        Assert.Equal("MR014", error.Code);
        Assert.Equal(
            "Block 'B' reads tag 'T.Output' as a Bool, but the plant publishes a Double. Declare the pin with the kind the tag has.",
            error.Message);
    }

    [Fact]
    public void AnUnknownWriteTagIsMr014()
    {
        SimulationBuilder builder = Plant().AddScanBlock(
            new EchoBlock("B", TimeSpan.FromMilliseconds(20)).Reads("T.Enable").MayWrite("T.Nope"));

        ValidationError error = Only(builder);
        Assert.Equal("MR014", error.Code);
        Assert.Equal(
            "Block 'B' commands tag 'T.Nope', which the plant does not have. Check the name against 'millrace tags', or bind the port it should command.",
            error.Message);
    }

    [Fact]
    public void AWriteToAReadOnlyTagIsMr014()
    {
        SimulationBuilder builder = Plant().AddScanBlock(
            new EchoBlock("B", TimeSpan.FromMilliseconds(20)).Reads("T.Enable").MayWrite("T.Output", TagKind.Double));

        ValidationError error = Only(builder);
        Assert.Equal("MR014", error.Code);
        Assert.Equal(
            "Block 'B' commands tag 'T.Output', which is read-only. Command a read-write tag, or bind that port as a writable tag.",
            error.Message);
    }

    [Fact]
    public void AWriteOfTheWrongKindIsMr014()
    {
        SimulationBuilder builder = Plant().AddScanBlock(
            new EchoBlock("B", TimeSpan.FromMilliseconds(20)).Reads("T.Enable").MayWrite("T.Setpoint"));

        ValidationError error = Only(builder);
        Assert.Equal("MR014", error.Code);
        Assert.Equal(
            "Block 'B' commands tag 'T.Setpoint' as a Bool, but the plant publishes a Double. Declare the pin with the kind the tag has.",
            error.Message);
    }

    [Fact]
    public void ABlockIdThatIsAlreadyAComponentIdIsMr015()
    {
        SimulationBuilder builder = Plant().AddScanBlock(
            new EchoBlock("F", TimeSpan.FromMilliseconds(20)).Reads("T.Enable").Publishes("Q"));

        ValidationError error = Only(builder);
        Assert.Equal("MR015", error.Code);
        Assert.Equal(
            "Block id 'F' is already a component id. Ids must be unique across components and blocks; rename one of them.",
            error.Message);
    }

    [Fact]
    public void TwoBlocksWithTheSameIdIsMr015()
    {
        SimulationBuilder builder = Plant()
            .AddScanBlock(new EchoBlock("B", TimeSpan.FromMilliseconds(20)).Reads("T.Enable").Publishes("Q"))
            .AddScanBlock(new EchoBlock("B", TimeSpan.FromMilliseconds(20)).Reads("T.Enable").Publishes("R"));

        ValidationError error = Only(builder);
        Assert.Equal("MR015", error.Code);
        Assert.Equal(
            "Duplicate block id 'B'. Ids must be unique across components and blocks; rename one of them.",
            error.Message);
    }

    [Fact]
    public void AnOwnedTagThatCollidesWithAPlantTagIsMr015()
    {
        var thermostat = new Thermostat("T");
        SimulationBuilder builder = new SimulationBuilder(Options)
            .Add(thermostat)
            .Bind("B.Q", TagBinding.Read("Q", thermostat.Output, "%", 0.0, 200.0, "Bound out of the way"))
            .AddScanBlock(new EchoBlock("B", TimeSpan.FromMilliseconds(20)).Reads("T.Enable").Publishes("Q"));

        ValidationError error = Only(builder);
        Assert.Equal("MR015", error.Code);
        Assert.Equal(
            "Block 'B' owns tag 'B.Q', which the plant already has. Rename the block or the pin; a block's tag is its id followed by the pin name.",
            error.Message);
    }

    [Fact]
    public void ABlockThatDeclaresOnePinNameTwiceIsMr015()
    {
        SimulationBuilder builder = Plant().AddScanBlock(
            new EchoBlock("B", TimeSpan.FromMilliseconds(20)).Reads("T.Enable").Publishes("Q").Accepts("Q"));

        ValidationError error = Only(builder);
        Assert.Equal("MR015", error.Code);
        Assert.Equal(
            "Block 'B' declares tag 'B.Q' twice. Give each output and command its own name.",
            error.Message);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ABlockMayReadAnotherBlocksOutputWhicheverOrderTheyWereAdded(bool readerFirst)
    {
        var reader = new EchoBlock("A", TimeSpan.FromMilliseconds(20)).Reads("B.Q").Publishes("Q");
        var writer = new EchoBlock("B", TimeSpan.FromMilliseconds(20)).Reads("T.Enable").Publishes("Q");

        SimulationBuilder builder = Plant();
        if (readerFirst)
        {
            builder.AddScanBlock(reader).AddScanBlock(writer);
        }
        else
        {
            builder.AddScanBlock(writer).AddScanBlock(reader);
        }

        Assert.True(builder.Validate().IsValid);
    }

    [Fact]
    public void AddScanBlockAfterBuildThrows()
    {
        SimulationBuilder builder = Plant();
        builder.Build();

        Assert.Throws<InvalidOperationException>(() => builder.AddScanBlock(Block("B")));
    }

    [Fact]
    public void AddScanBlockRejectsNullAndABlankId()
    {
        Assert.Throws<ArgumentNullException>(() => Plant().AddScanBlock(null!));
        Assert.Throws<ArgumentException>(
            () => Plant().AddScanBlock(new EchoBlock(" ", TimeSpan.FromMilliseconds(20))));
    }

    [Fact]
    public void AddScanBlockRejectsAnIdThatIsNotAValidTagNameSegment()
    {
        // Reviewer note (Task 2, carried into Task 3's dispatch): a block id
        // prefixes every owned tag name, so it must satisfy the same rules a
        // tag name does. AddScanBlock calls Millrace.Io.TagNameRules.Check(block.Id,
        // nameof(block)) directly; otherwise a malformed id would crash deep
        // inside Validate (TagBinding.ValidName) instead of failing where the
        // block was added, and the exception should name the public
        // parameter ('block'), not a private local.
        ArgumentException whitespace = Assert.Throws<ArgumentException>(
            () => Plant().AddScanBlock(new EchoBlock("A B", TimeSpan.FromMilliseconds(20))));
        Assert.Equal("block", whitespace.ParamName);

        ArgumentException leadingDot = Assert.Throws<ArgumentException>(
            () => Plant().AddScanBlock(new EchoBlock(".A", TimeSpan.FromMilliseconds(20))));
        Assert.Equal("block", leadingDot.ParamName);

        ArgumentException trailingDot = Assert.Throws<ArgumentException>(
            () => Plant().AddScanBlock(new EchoBlock("A.", TimeSpan.FromMilliseconds(20))));
        Assert.Equal("block", trailingDot.ParamName);
    }

    [Fact]
    public void BuildThrowsWhenABlockIsInvalid()
    {
        SimulationBuilder builder = Plant().AddScanBlock(
            new EchoBlock("B", TimeSpan.FromMilliseconds(20)).Reads("T.Nope"));

        SimulationValidationException error = Assert.Throws<SimulationValidationException>(() => builder.Build());
        Assert.Equal("MR014", Assert.Single(error.Result.Errors).Code);
    }
}
