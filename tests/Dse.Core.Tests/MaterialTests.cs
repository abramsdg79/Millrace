using Dse.Core.Flow;
using Xunit;

namespace Dse.Core.Tests;

public class MaterialTests
{
    private static readonly MaterialProperties A = new(Density: 1000.0, Moisture: 0.10, Temperature: 20.0);
    private static readonly MaterialProperties B = new(Density: 2000.0, Moisture: 0.30, Temperature: 60.0);

    [Fact]
    public void BlendIsMassWeighted()
    {
        MaterialProperties blended = MaterialProperties.Blend(A, 1.0, B, 3.0);

        Assert.Equal(1750.0, blended.Density, 9);
        Assert.Equal(0.25, blended.Moisture, 9);
        Assert.Equal(50.0, blended.Temperature, 9);
    }

    [Fact]
    public void BlendingWithZeroMassReturnsTheOtherSideExactly()
    {
        Assert.Equal(B, MaterialProperties.Blend(A, 0.0, B, 5.0));
        Assert.Equal(A, MaterialProperties.Blend(A, 5.0, B, 0.0));
    }

    [Fact]
    public void BlendingTwoEmptyMassesReturnsTheFirst()
    {
        Assert.Equal(A, MaterialProperties.Blend(A, 0.0, B, 0.0));
    }

    [Fact]
    public void BlendRejectsNegativeMass()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MaterialProperties.Blend(A, -1.0, B, 1.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => MaterialProperties.Blend(A, 1.0, B, -1.0));
    }

    [Fact]
    public void StateIndexResolvesByName()
    {
        var billet = new MaterialType("Billet", PayloadKind.Discrete, "CoreTemperature", "TimeAbove1150");

        Assert.Equal(0, billet.StateIndexOf("CoreTemperature"));
        Assert.Equal(1, billet.StateIndexOf("TimeAbove1150"));
        Assert.Equal(new[] { "CoreTemperature", "TimeAbove1150" }, billet.StateSchema);
    }

    [Fact]
    public void UnknownStateNamesTheDeclaredOnes()
    {
        var billet = new MaterialType("Billet", PayloadKind.Discrete, "CoreTemperature");

        KeyNotFoundException error =
            Assert.Throws<KeyNotFoundException>(() => billet.StateIndexOf("Nope"));
        Assert.Contains("CoreTemperature", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DuplicateStateNamesAreRejected()
    {
        Assert.Throws<ArgumentException>(
            () => new MaterialType("Billet", PayloadKind.Discrete, "T", "T"));
    }

    [Fact]
    public void NewStateIsSizedBySchema()
    {
        var billet = new MaterialType("Billet", PayloadKind.Discrete, "A", "B");
        var ore = new MaterialType("Ore", PayloadKind.Bulk);

        Assert.Equal(2, billet.NewState().Length);
        Assert.Empty(ore.NewState());
    }

    [Fact]
    public void BlankNameIsRejected()
    {
        Assert.Throws<ArgumentException>(() => new MaterialType(" ", PayloadKind.Bulk));
    }
}
