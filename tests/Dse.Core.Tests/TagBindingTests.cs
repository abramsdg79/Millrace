using Dse.Core.Graph;
using Dse.Core.Io;
using Dse.Io;
using Xunit;

namespace Dse.Core.Tests;

public class TagBindingTests
{
    private enum Phase
    {
        Idle,
        Filling,
        Done,
    }

    [Fact]
    public void BoolReadCapturesThePortValue()
    {
        var port = new OutputPort<bool>("Ok", "S") { Value = true };
        TagBinding binding = TagBinding.Read("Ok", port, "Loop healthy");

        Assert.Equal(("Ok", TagKind.Bool, TagAccess.ReadOnly, string.Empty, "Loop healthy"),
            (binding.Name, binding.Kind, binding.Access, binding.Unit, binding.Description));
        Assert.True(double.IsNaN(binding.RangeLow));
        Assert.Equal(TagValue.Bool(true), binding.Capture());
    }

    [Fact]
    public void DoubleReadCarriesUnitRangeAndQuality()
    {
        var value = new OutputPort<double>("Value", "TT01") { Value = 42.5 };
        var health = new OutputPort<TagQuality>("Health", "TT01") { Value = TagQuality.Bad(QualityDetail.SensorFailure) };
        TagBinding binding = TagBinding.Read("Value", value, "°C", 0.0, 100.0, "Bearing temperature", health);

        Assert.Equal(("°C", 0.0, 100.0), (binding.Unit, binding.RangeLow, binding.RangeHigh));
        Assert.Equal(TagValue.Double(42.5, TagQuality.Bad(QualityDetail.SensorFailure)), binding.Capture());

        health.Value = TagQuality.Good;
        Assert.Equal(TagValue.Double(42.5), binding.Capture());
    }

    [Fact]
    public void DoubleReadWithoutQualitySourceIsAlwaysGood()
    {
        var value = new OutputPort<double>("Load", "B") { Value = 1.25 };

        Assert.Equal(TagValue.Double(1.25), TagBinding.Read("Load", value, "kg/m", 0.0, 50.0).Capture());
    }

    [Fact]
    public void IntAndLongReadsPublishAsInt64()
    {
        var count = new OutputPort<long>("Count", "PC") { Value = 17L };
        var queued = new OutputPort<int>("Queued", "IS") { Value = 3 };

        Assert.Equal(TagValue.Int64(17L), TagBinding.Read("Count", count).Capture());
        Assert.Equal(TagValue.Int64(3L), TagBinding.Read("Queued", queued).Capture());
        Assert.Equal("count", TagBinding.Read("Queued", queued).Unit);
    }

    [Fact]
    public void EnumReadPublishesAsInt64AndDocumentsTheMembers()
    {
        var phase = new OutputPort<Phase>("Phase", "PU") { Value = Phase.Done };
        TagBinding binding = TagBinding.ReadEnum("Phase", phase, "Unit phase");

        Assert.Equal(TagKind.Int64, binding.Kind);
        Assert.Equal(TagValue.Int64(2L), binding.Capture());
        Assert.Equal("Unit phase (0=Idle, 1=Filling, 2=Done)", binding.Description);
    }

    [Fact]
    public void EnumReadWithoutDescriptionListsMembersOnly()
    {
        var phase = new OutputPort<Phase>("Phase", "PU");

        Assert.Equal("0=Idle, 1=Filling, 2=Done", TagBinding.ReadEnum("Phase", phase).Description);
    }

    [Fact]
    public void WriteBindingDrivesTheInput()
    {
        var input = new InputPort<bool>("Command", "ST", defaultValue: false, isRequired: false);
        TagBinding binding = TagBinding.Write("Command", input, "Run command");

        Assert.Equal((TagKind.Bool, TagAccess.ReadWrite), (binding.Kind, binding.Access));
        binding.BindExternal();
        Assert.True(input.IsExternallyDriven);

        binding.Apply(TagValue.Bool(true));

        Assert.True(input.Value);
        Assert.Equal(TagValue.Bool(true), binding.Capture());
    }

    [Fact]
    public void DoubleWriteBindingCarriesRange()
    {
        var input = new InputPort<double>("Rate", "F", defaultValue: 5.0, isRequired: false);
        TagBinding binding = TagBinding.Write("Rate", input, "kg/s", 0.0, 20.0, "Feed rate");
        binding.BindExternal();

        binding.Apply(TagValue.Double(12.0));

        Assert.Equal(12.0, input.Value);
        Assert.Equal((0.0, 20.0), (binding.RangeLow, binding.RangeHigh));
    }

    [Fact]
    public void ApplyOnAReadOnlyBindingThrows()
    {
        var port = new OutputPort<bool>("Ok", "S");
        TagBinding binding = TagBinding.Read("Ok", port);

        Assert.Throws<InvalidOperationException>(() => binding.Apply(TagValue.Bool(true)));
    }

    [Fact]
    public void ApplyOfTheWrongKindThrows()
    {
        var input = new InputPort<bool>("Command", "ST", defaultValue: false, isRequired: false);
        TagBinding binding = TagBinding.Write("Command", input);
        binding.BindExternal();

        Assert.Throws<InvalidOperationException>(() => binding.Apply(TagValue.Double(1.0)));
    }

    [Fact]
    public void WithNameKeepsEverythingElse()
    {
        var port = new OutputPort<double>("Value", "S") { Value = 2.0 };
        TagBinding binding = TagBinding.Read("Value", port, "A", 0.0, 10.0, "Current");

        TagBinding renamed = binding.WithName("CV001.Current");

        Assert.Equal("CV001.Current", renamed.Name);
        Assert.Same(port, renamed.Port);
        Assert.Equal(("A", 0.0, 10.0, "Current"), (renamed.Unit, renamed.RangeLow, renamed.RangeHigh, renamed.Description));
        Assert.Equal(TagValue.Double(2.0), renamed.Capture());
    }

    [Fact]
    public void DoubleReadWithoutRangeHasNaNBounds()
    {
        var port = new OutputPort<double>("Received", "P") { Value = 1234.5 };
        TagBinding binding = TagBinding.Read("Received", port, "kg");

        Assert.True(double.IsNaN(binding.RangeLow));
        Assert.True(double.IsNaN(binding.RangeHigh));
        Assert.Equal(TagValue.Double(1234.5), binding.Capture());
    }

    [Fact]
    public void AsReadOnlyDropsTheWritePath()
    {
        var input = new InputPort<bool>("Command", "ST", defaultValue: false, isRequired: false);
        TagBinding readOnly = TagBinding.Write("Command", input, "Run command").AsReadOnly();

        Assert.Equal(TagAccess.ReadOnly, readOnly.Access);
        Assert.Equal("Run command", readOnly.Description);
        readOnly.BindExternal();
        Assert.False(input.IsExternallyDriven);
        Assert.Throws<InvalidOperationException>(() => readOnly.Apply(TagValue.Bool(true)));
        Assert.Equal(TagValue.Bool(false), readOnly.Capture());
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(".Speed")]
    [InlineData("Speed.")]
    [InlineData("Belt Speed")]
    public void RejectsMalformedNames(string name)
    {
        var port = new OutputPort<bool>("Ok", "S");

        Assert.Throws<ArgumentException>(() => TagBinding.Read(name, port));
    }

    [Fact]
    public void RejectsAnInvertedOrHalfRange()
    {
        var port = new OutputPort<double>("Value", "S");

        Assert.Throws<ArgumentException>(() => TagBinding.Read("Value", port, "A", 10.0, 0.0));
        Assert.Throws<ArgumentException>(() => TagBinding.Read("Value", port, "A", 0.0, double.NaN));
    }
}
