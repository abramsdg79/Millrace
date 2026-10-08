using Millrace.Io;

namespace Millrace.Io.Abstractions.Tests;

public class ScanBlockContractTests
{
    /// <summary>A block written against the abstractions alone: it echoes its input and counts its scans.</summary>
    private sealed class Echo : IScanBlock
    {
        public string Id => "ECHO";

        public TimeSpan ScanPeriod => TimeSpan.FromMilliseconds(100);

        public IReadOnlyList<TagRef> Inputs { get; } = [new TagRef("T.Enable", TagKind.Bool)];

        public IReadOnlyList<TagRef> Writes { get; } = [new TagRef("U.Enable", TagKind.Bool)];

        public IReadOnlyList<TagSpec> Outputs { get; } = [new TagSpec("Q", TagKind.Bool, "", "The echo")];

        public IReadOnlyList<TagSpec> Commands { get; } = [new TagSpec("Enable", TagKind.Bool, "", "Run the echo")];

        public int Scans { get; private set; }

        public void Scan(in ScanInputs inputs, ref ScanOutputs outputs)
        {
            Scans++;
            bool value = inputs.Input(0).AsBool && inputs.Command(0).AsBool;
            outputs.Set(0, TagValue.Bool(value));
            outputs.Write(0, TagValue.Bool(value));
            if (value)
            {
                outputs.Raise("ECHOED", "The input is high.");
            }
        }
    }

    private static ScanInputs Inputs(bool input, bool command) => new(
        new[] { TagValue.Bool(input) },
        new[] { TagValue.Bool(command) },
        tick: 7L,
        now: new DateTimeOffset(2026, 1, 1, 6, 0, 0, TimeSpan.Zero),
        deltaSeconds: 0.01,
        elapsed: 0.1);

    [Fact]
    public void ATagRefRejectsABlankName()
    {
        Assert.Throws<ArgumentException>(() => new TagRef("  ", TagKind.Bool));
    }

    [Fact]
    public void ATagSpecRejectsANameWithAnEmptySegment()
    {
        Assert.Throws<ArgumentException>(() => new TagSpec(".Active", TagKind.Bool));
        Assert.Throws<ArgumentException>(() => new TagSpec("Hi..Active", TagKind.Bool));
        Assert.Throws<ArgumentException>(() => new TagSpec("Hi Active", TagKind.Bool));
    }

    [Fact]
    public void ATagSpecDefaultsUnitAndDescriptionToEmpty()
    {
        var spec = new TagSpec("Q", TagKind.Bool);

        Assert.Equal("Q", spec.Name);
        Assert.Equal(TagKind.Bool, spec.Kind);
        Assert.Equal(string.Empty, spec.Unit);
        Assert.Equal(string.Empty, spec.Description);
    }

    [Fact]
    public void ABlockEventCarriesACodeAndAMessage()
    {
        var raised = new BlockEvent("ALARM_RAISED", "Hi: 82.3 above 80.");

        Assert.Equal("ALARM_RAISED", raised.Code);
        Assert.Equal("Hi: 82.3 above 80.", raised.Message);
    }

    [Fact]
    public void ScanInputsExposeValuesAndTiming()
    {
        ScanInputs inputs = Inputs(input: true, command: false);

        Assert.Equal(1, inputs.InputCount);
        Assert.Equal(1, inputs.CommandCount);
        Assert.True(inputs.Input(0).AsBool);
        Assert.False(inputs.Command(0).AsBool);
        Assert.Equal(7L, inputs.Tick);
        Assert.Equal(new DateTimeOffset(2026, 1, 1, 6, 0, 0, TimeSpan.Zero), inputs.Now);
        Assert.Equal(0.01, inputs.DeltaSeconds);
        Assert.Equal(0.1, inputs.Elapsed);
    }

    [Fact]
    public void ScanInputsRejectAnIndexOutsideThePins()
    {
        ScanInputs inputs = Inputs(input: true, command: true);

        Assert.Throws<ArgumentOutOfRangeException>(() => inputs.Input(1));
        Assert.Throws<ArgumentOutOfRangeException>(() => inputs.Input(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => inputs.Command(1));
    }

    [Fact]
    public void AnOutputThatWasNotSetIsNotReported()
    {
        var outputs = new ScanOutputs(2, 1);

        Assert.False(outputs.TryOutput(0, out _));
        Assert.False(outputs.TryWrite(0, out _));
        Assert.Empty(outputs.Events);
    }

    [Fact]
    public void SetAndWriteAreReadBackByIndex()
    {
        var outputs = new ScanOutputs(2, 1);

        outputs.Set(1, TagValue.Double(4.5));
        outputs.Write(0, TagValue.Bool(true));

        Assert.False(outputs.TryOutput(0, out _));
        Assert.True(outputs.TryOutput(1, out TagValue set));
        Assert.Equal(4.5, set.AsDouble);
        Assert.True(outputs.TryWrite(0, out TagValue written));
        Assert.True(written.AsBool);
    }

    [Fact]
    public void RaiseCollectsEventsInOrder()
    {
        var outputs = new ScanOutputs(1, 0);

        outputs.Raise("FIRST", "One.");
        outputs.Raise("SECOND", "Two.");

        Assert.Equal(2, outputs.Events.Count);
        Assert.Equal("FIRST", outputs.Events[0].Code);
        Assert.Equal("Two.", outputs.Events[1].Message);
    }

    [Fact]
    public void ResetClearsOutputsWritesAndEvents()
    {
        var outputs = new ScanOutputs(1, 1);
        outputs.Set(0, TagValue.Bool(true));
        outputs.Write(0, TagValue.Bool(true));
        outputs.Raise("CODE", "Message.");

        outputs.Reset();

        Assert.False(outputs.TryOutput(0, out _));
        Assert.False(outputs.TryWrite(0, out _));
        Assert.Empty(outputs.Events);
    }

    [Fact]
    public void ScanOutputsRejectAnIndexOutsideTheirBuffers()
    {
        var outputs = new ScanOutputs(1, 1);

        Assert.Throws<ArgumentOutOfRangeException>(() => outputs.Set(1, TagValue.Bool(true)));
        Assert.Throws<ArgumentOutOfRangeException>(() => outputs.Write(-1, TagValue.Bool(true)));
        Assert.Throws<ArgumentOutOfRangeException>(() => outputs.TryOutput(1, out _));
    }

    [Fact]
    public void RaiseRejectsABlankCodeOrMessage()
    {
        var outputs = new ScanOutputs(1, 0);

        Assert.Throws<ArgumentException>(() => outputs.Raise(" ", "Message."));
        Assert.Throws<ArgumentException>(() => outputs.Raise("CODE", " "));
    }

    [Fact]
    public void ACopyOfScanOutputsSharesItsBuffers()
    {
        var outputs = new ScanOutputs(1, 0);
        ScanOutputs copy = outputs;

        copy.Set(0, TagValue.Bool(true));
        copy.Raise("CODE", "Message.");

        Assert.True(outputs.TryOutput(0, out TagValue value));
        Assert.True(value.AsBool);
        Assert.Single(outputs.Events);
    }

    [Fact]
    public void ABlockNeedsNothingButTheseTypesToScan()
    {
        var block = new Echo();
        var outputs = new ScanOutputs(block.Outputs.Count, block.Writes.Count);

        ScanInputs low = Inputs(input: true, command: false);
        block.Scan(in low, ref outputs);
        Assert.True(outputs.TryOutput(0, out TagValue first));
        Assert.False(first.AsBool);
        Assert.Empty(outputs.Events);

        outputs.Reset();
        ScanInputs high = Inputs(input: true, command: true);
        block.Scan(in high, ref outputs);
        Assert.True(outputs.TryOutput(0, out TagValue second));
        Assert.True(second.AsBool);
        Assert.True(outputs.TryWrite(0, out TagValue commanded));
        Assert.True(commanded.AsBool);
        Assert.Equal("ECHOED", Assert.Single(outputs.Events).Code);
        Assert.Equal(2, block.Scans);
        Assert.Equal(TimeSpan.FromMilliseconds(100), block.ScanPeriod);
        Assert.Equal("ECHO", block.Id);
        Assert.Equal("Enable", Assert.Single(block.Commands).Name);
    }
}
