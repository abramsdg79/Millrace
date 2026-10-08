using Millrace.Io;

namespace Millrace.Io.Abstractions.Tests;

public class TagQualityTests
{
    [Fact]
    public void DefaultIsGoodWithNoDetail()
    {
        TagQuality quality = default;

        Assert.Equal(TagQuality.Good, quality);
        Assert.True(quality.IsGood);
        Assert.Equal(Quality.Good, quality.Code);
        Assert.Equal(QualityDetail.None, quality.Detail);
    }

    [Fact]
    public void BadAndUncertainCarryTheirDetail()
    {
        TagQuality bad = TagQuality.Bad(QualityDetail.SensorFailure);
        TagQuality uncertain = TagQuality.Uncertain(QualityDetail.OutOfRange);

        Assert.False(bad.IsGood);
        Assert.Equal((Quality.Bad, QualityDetail.SensorFailure), (bad.Code, bad.Detail));
        Assert.Equal((Quality.Uncertain, QualityDetail.OutOfRange), (uncertain.Code, uncertain.Detail));
    }

    [Fact]
    public void ToStringNamesCodeAndDetail()
    {
        Assert.Equal("Good", TagQuality.Good.ToString());
        Assert.Equal("Bad:SensorFailure", TagQuality.Bad(QualityDetail.SensorFailure).ToString());
    }
}
