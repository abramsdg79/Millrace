using Millrace.Core.Flow;
using Xunit;

namespace Millrace.Core.Tests;

public class BulkLotTests
{
    private static readonly MaterialType Ore = new("Ore", PayloadKind.Bulk);
    private static readonly MaterialType Coal = new("Coal", PayloadKind.Bulk);
    private static readonly MaterialType Billet = new("Billet", PayloadKind.Discrete);
    private static readonly MaterialProperties Wet = new(1600.0, 0.20, 15.0);
    private static readonly MaterialProperties Dry = new(1600.0, 0.00, 35.0);

    [Fact]
    public void MergeAddsMassAndBlendsProperties()
    {
        BulkLot merged = BulkLot.Of(Ore, 1.0, Wet).Merge(BulkLot.Of(Ore, 3.0, Dry));

        Assert.Same(Ore, merged.Type);
        Assert.Equal(4.0, merged.Mass, 9);
        Assert.Equal(0.05, merged.Properties.Moisture, 9);
        Assert.Equal(30.0, merged.Properties.Temperature, 9);
    }

    [Fact]
    public void MergingIntoEmptyAdoptsTheIncomingLot()
    {
        BulkLot incoming = BulkLot.Of(Ore, 2.0, Wet);

        Assert.Equal(incoming, BulkLot.Empty.Merge(incoming));
    }

    [Fact]
    public void MergingEmptyIsANoOp()
    {
        BulkLot lot = BulkLot.Of(Ore, 2.0, Wet);

        Assert.Equal(lot, lot.Merge(BulkLot.Empty));
    }

    [Fact]
    public void MergingDifferentTypesThrows()
    {
        BulkLot ore = BulkLot.Of(Ore, 1.0, Wet);

        InvalidOperationException error =
            Assert.Throws<InvalidOperationException>(() => ore.Merge(BulkLot.Of(Coal, 1.0, Dry)));
        Assert.Contains("Ore", error.Message, StringComparison.Ordinal);
        Assert.Contains("Coal", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TakeSplitsTheMassAndKeepsTheProperties()
    {
        BulkLot taken = BulkLot.Of(Ore, 5.0, Wet).Take(2.0, out BulkLot remaining);

        Assert.Equal(2.0, taken.Mass, 9);
        Assert.Equal(3.0, remaining.Mass, 9);
        Assert.Equal(Wet, taken.Properties);
        Assert.Equal(Wet, remaining.Properties);
        Assert.Same(Ore, remaining.Type);
    }

    [Fact]
    public void TakingMoreThanHeldTakesEverything()
    {
        BulkLot lot = BulkLot.Of(Ore, 5.0, Wet);

        BulkLot taken = lot.Take(9.0, out BulkLot remaining);

        Assert.Equal(lot, taken);
        Assert.True(remaining.IsEmpty);
    }

    [Fact]
    public void TakingZeroReturnsEmptyAndLeavesTheLot()
    {
        BulkLot lot = BulkLot.Of(Ore, 5.0, Wet);

        BulkLot taken = lot.Take(0.0, out BulkLot remaining);

        Assert.True(taken.IsEmpty);
        Assert.Equal(lot, remaining);
    }

    [Fact]
    public void OfRejectsDiscreteTypesAndNegativeMass()
    {
        Assert.Throws<ArgumentException>(() => BulkLot.Of(Billet, 1.0, Wet));
        Assert.Throws<ArgumentOutOfRangeException>(() => BulkLot.Of(Ore, -1.0, Wet));
    }

    [Fact]
    public void EmptyHasNoTypeAndNoMass()
    {
        Assert.True(BulkLot.Empty.IsEmpty);
        Assert.Null(BulkLot.Empty.Type);
        Assert.Equal(0.0, BulkLot.Empty.Mass);
    }
}
