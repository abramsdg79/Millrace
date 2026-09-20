using Dse.Core.Graph;
using Dse.Core.Io;
using Dse.Io;

namespace Dse.Core.Tests;

public class TagBindingForPortTests
{
    [Fact]
    public void BindsABoolOutputReadOnly()
    {
        var delay = new UnitDelay<bool>("D");

        TagBinding tag = TagBinding.ForPort("D.OUT", delay.Out, TagAccess.ReadOnly, description: "Held value");

        Assert.Equal(TagKind.Bool, tag.Kind);
        Assert.Equal(TagAccess.ReadOnly, tag.Access);
        Assert.Same(delay.Out, tag.Port);
        Assert.Equal("Held value", tag.Description);
    }

    [Fact]
    public void BindsADoubleInputWritableWithUnitAndRange()
    {
        var delay = new UnitDelay<double>("D");

        TagBinding tag = TagBinding.ForPort("D.SP", delay.In, TagAccess.ReadWrite, "m/s", 0.0, 5.0);

        Assert.Equal(TagKind.Double, tag.Kind);
        Assert.Equal(TagAccess.ReadWrite, tag.Access);
        Assert.Equal("m/s", tag.Unit);
        Assert.Equal(5.0, tag.RangeHigh);
    }

    [Fact]
    public void BindsAnInputReadOnlyToObserveACommand()
    {
        var delay = new UnitDelay<bool>("D");

        TagBinding tag = TagBinding.ForPort("D.CMD", delay.In, TagAccess.ReadOnly);

        Assert.Equal(TagAccess.ReadOnly, tag.Access);
    }

    [Fact]
    public void BindsLongAndIntOutputsAsInt64()
    {
        Assert.Equal(TagKind.Int64, TagBinding.ForPort("L", new UnitDelay<long>("L").Out, TagAccess.ReadOnly).Kind);
        Assert.Equal(TagKind.Int64, TagBinding.ForPort("I", new UnitDelay<int>("I").Out, TagAccess.ReadOnly).Kind);
    }

    [Fact]
    public void RefusesToWriteAnOutput()
    {
        var delay = new UnitDelay<bool>("D");

        var ex = Assert.Throws<ArgumentException>(
            () => TagBinding.ForPort("D.OUT", delay.Out, TagAccess.ReadWrite));

        Assert.Contains("is an output", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RefusesAPortTypeWithNoTagKindAndListsTheSupportedOnes()
    {
        var delay = new UnitDelay<float>("D");

        var ex = Assert.Throws<ArgumentException>(
            () => TagBinding.ForPort("D.OUT", delay.Out, TagAccess.ReadOnly));

        Assert.Contains("Single", ex.Message, StringComparison.Ordinal);
        Assert.Contains("bool, double, int, long", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void BindsAnIntInputReadOnly()
    {
        var delay = new UnitDelay<int>("I");

        TagBinding tag = TagBinding.ForPort("I.VAL", delay.In, TagAccess.ReadOnly);

        Assert.Equal(TagKind.Int64, tag.Kind);
        Assert.Equal(TagAccess.ReadOnly, tag.Access);
        Assert.Same(delay.In, tag.Port);
    }

    [Fact]
    public void RefusesToWriteAnIntInput()
    {
        var delay = new UnitDelay<int>("I");

        var ex = Assert.Throws<ArgumentException>(
            () => TagBinding.ForPort("I.VAL", delay.In, TagAccess.ReadWrite));

        Assert.Contains("is an int input", ex.Message, StringComparison.Ordinal);
    }
}
