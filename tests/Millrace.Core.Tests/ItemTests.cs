using Millrace.Core.Contexts;
using Millrace.Core.Flow;
using Millrace.Core.Tests.Fakes;
using Xunit;

namespace Millrace.Core.Tests;

public class ItemTests
{
    private static readonly MaterialType Billet =
        new("Billet", PayloadKind.Discrete, "CoreTemperature", "TimeAbove1150");

    private static readonly MaterialType Wheel = new("Wheel", PayloadKind.Discrete);

    private static readonly MaterialType Ore = new("Ore", PayloadKind.Bulk);

    [Fact]
    public void SequenceStartsAtOneAndIsMonotonic()
    {
        var sequence = new ItemIdSequence();

        Assert.Equal(0, sequence.Issued);
        Assert.Equal(1, sequence.Next());
        Assert.Equal(2, sequence.Next());
        Assert.Equal(2, sequence.Issued);
    }

    [Fact]
    public void ItemStateIsSizedBySchemaAndStartsAtZero()
    {
        var item = new ItemInstance(7, Billet, 12.5, new MaterialProperties(7800.0, 0.0, 25.0));

        Assert.Equal(7, item.Id);
        Assert.Same(Billet, item.Type);
        Assert.Equal(12.5, item.Mass);
        Assert.Equal(new[] { 0.0, 0.0 }, item.State);
    }

    [Fact]
    public void ChangeTypeResetsStateToTheNewSchema()
    {
        var item = new ItemInstance(1, Billet, 12.5, default);
        item.State[1] = 42.0;

        item.ChangeType(Wheel);

        Assert.Same(Wheel, item.Type);
        Assert.Empty(item.State);
    }

    [Fact]
    public void ItemsRejectBulkTypes()
    {
        Assert.Throws<ArgumentException>(() => new ItemInstance(1, Ore, 1.0, default));
        Assert.Throws<ArgumentException>(() => new ItemInstance(1, Wheel, 1.0, default).ChangeType(Ore));
    }

    [Fact]
    public void InitContextHandsEveryComponentTheSameSequence()
    {
        var shared = new ItemIdSequence();
        InitContext first = TestContexts.Init("A", items: shared);
        InitContext second = TestContexts.Init("B", items: shared);

        Assert.Equal(1, first.Items.Next());
        Assert.Equal(2, second.Items.Next());
    }
}
