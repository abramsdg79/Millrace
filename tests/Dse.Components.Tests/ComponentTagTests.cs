using Dse.Components.Flow;
using Dse.Components.Mechanical;
using Dse.Components.Safety;
using Dse.Core.Flow;
using Dse.Core.Io;
using Dse.Io;
using Xunit;

namespace Dse.Components.Tests;

public class ComponentTagTests
{
    private static readonly MaterialType Ore = new("Ore", PayloadKind.Bulk);

    private static IEnumerable<(string Name, TagKind Kind, TagAccess Access)> Shape(ITagProvider provider) =>
        provider.DescribeTags().Select(t => (t.Name, t.Kind, t.Access));

    [Fact]
    public void StarterPublishesCommandsAndStates()
    {
        Assert.Equal(
            new[]
            {
                ("Command", TagKind.Bool, TagAccess.ReadWrite),
                ("Reset", TagKind.Bool, TagAccess.ReadWrite),
                ("Contactor", TagKind.Bool, TagAccess.ReadOnly),
                ("Tripped", TagKind.Bool, TagAccess.ReadOnly),
            },
            Shape(new MotorStarter("ST")));
    }

    [Fact]
    public void SafetyDevicesPublishActuationAndLoopState()
    {
        (string, TagKind, TagAccess)[] expected =
        [
            ("Actuated", TagKind.Bool, TagAccess.ReadWrite),
            ("Ok", TagKind.Bool, TagAccess.ReadOnly),
        ];

        Assert.Equal(expected, Shape(new EStop("ES")));
        Assert.Equal(expected, Shape(new PullKey("PK")));
        Assert.Equal(
            new[] { ("Reset", TagKind.Bool, TagAccess.ReadWrite), ("Ok", TagKind.Bool, TagAccess.ReadOnly) },
            Shape(new SafetyRelay("SR", 2)));
    }

    [Fact]
    public void BulkSourceAndSinkPublishFeedAndTotals()
    {
        var source = new BulkSource("Feed", Ore, 20.0, hopperCapacityKg: 500.0);
        TagBinding hopper = source.DescribeTags().Single(t => t.Name == "HopperMass");

        Assert.Equal(
            new[]
            {
                ("Enabled", TagKind.Bool, TagAccess.ReadWrite),
                ("Rate", TagKind.Double, TagAccess.ReadWrite),
                ("HopperMass", TagKind.Double, TagAccess.ReadOnly),
            },
            Shape(source));
        Assert.Equal(("kg", 0.0, 500.0), (hopper.Unit, hopper.RangeLow, hopper.RangeHigh));
        Assert.True(double.IsNaN(new BulkSource("F2", Ore, 1.0).DescribeTags().Single(t => t.Name == "HopperMass").RangeHigh));

        Assert.Equal(
            new[]
            {
                ("Received", TagKind.Double, TagAccess.ReadOnly),
                ("Rate", TagKind.Double, TagAccess.ReadOnly),
                ("Full", TagKind.Bool, TagAccess.ReadOnly),
            },
            Shape(new BulkSink("Pile")));
    }

    [Fact]
    public void ItemSourceAndSinkPublishEnableAndCounts()
    {
        var items = new MaterialType("Wheel", PayloadKind.Discrete);

        Assert.Equal(
            new[] { ("Enabled", TagKind.Bool, TagAccess.ReadWrite), ("Queued", TagKind.Int64, TagAccess.ReadOnly) },
            Shape(new ItemSource("IS", items, itemMassKg: 5.0, intervalSeconds: 1.0)));
        Assert.Equal(
            new[] { ("Count", TagKind.Int64, TagAccess.ReadOnly), ("Full", TagKind.Bool, TagAccess.ReadOnly) },
            Shape(new ItemSink("Bin")));
    }

    [Fact]
    public void ChutePublishesLevelAsAFraction()
    {
        TagBinding level = new TransferChute("Chute", 200.0).DescribeTags().Single(t => t.Name == "Level");

        Assert.Equal(("fraction", 0.0, 1.0, TagKind.Double), (level.Unit, level.RangeLow, level.RangeHigh, level.Kind));
        Assert.Contains(new TransferChute("C2", 1.0).DescribeTags(), t => t.Name == "Full" && t.Kind == TagKind.Bool);
    }
}
