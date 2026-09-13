using Dse.Io;

namespace Dse.Io.Abstractions.Tests;

public class TagValueTests
{
    [Fact]
    public void BoolRoundTrips()
    {
        TagValue value = TagValue.Bool(true);

        Assert.Equal(TagKind.Bool, value.Kind);
        Assert.True(value.AsBool);
        Assert.Equal(TagQuality.Good, value.Quality);
    }

    [Fact]
    public void DoubleRoundTripsExactly()
    {
        TagValue value = TagValue.Double(12.3456789012345);

        Assert.Equal(TagKind.Double, value.Kind);
        Assert.Equal(12.3456789012345, value.AsDouble);
    }

    [Fact]
    public void Int64RoundTrips()
    {
        TagValue value = TagValue.Int64(long.MinValue + 7);

        Assert.Equal(TagKind.Int64, value.Kind);
        Assert.Equal(long.MinValue + 7, value.AsInt64);
    }

    [Fact]
    public void AccessorOfTheWrongKindThrows()
    {
        TagValue value = TagValue.Double(1.0);

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => value.AsBool);
        Assert.Contains("Double", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Bool", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void QualityIsPartOfEquality()
    {
        TagValue good = TagValue.Double(1.0);
        TagValue bad = TagValue.Double(1.0, TagQuality.Bad(QualityDetail.SensorFailure));

        Assert.NotEqual(good, bad);
        Assert.True(good.ValueEquals(bad));
        Assert.Equal(good, bad.WithQuality(TagQuality.Good));
    }

    [Fact]
    public void NaNEqualsNaNAndNegativeZeroDiffersFromZero()
    {
        Assert.Equal(TagValue.Double(double.NaN), TagValue.Double(double.NaN));
        Assert.NotEqual(TagValue.Double(-0.0), TagValue.Double(0.0));
    }

    [Fact]
    public void DefaultIsFalseBool()
    {
        TagValue value = default;

        Assert.Equal(TagKind.Bool, value.Kind);
        Assert.False(value.AsBool);
        Assert.True(value.Quality.IsGood);
    }

    [Fact]
    public void KindOfMapsTheThreeSupportedTypes()
    {
        Assert.Equal(TagKind.Bool, TagValue.KindOf<bool>());
        Assert.Equal(TagKind.Double, TagValue.KindOf<double>());
        Assert.Equal(TagKind.Int64, TagValue.KindOf<long>());
        Assert.Throws<NotSupportedException>(() => TagValue.KindOf<int>());
    }

    [Fact]
    public void ToStringIsInvariantAndShowsNonGoodQuality()
    {
        Assert.Equal("true", TagValue.Bool(true).ToString());
        Assert.Equal("2.5", TagValue.Double(2.5).ToString());
        Assert.Equal("42", TagValue.Int64(42).ToString());
        Assert.Equal("2.5 [Bad:SensorFailure]", TagValue.Double(2.5, TagQuality.Bad(QualityDetail.SensorFailure)).ToString());
    }
}
