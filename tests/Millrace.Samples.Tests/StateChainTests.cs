namespace Millrace.Samples.Tests;

public class StateChainTests
{
    private static List<TagSample> Series(params (double Seconds, double Value)[] points) =>
        points.Select(p => new TagSample(TimeSpan.FromSeconds(p.Seconds), p.Value)).ToList();

    private static readonly TimeSpan Start = TimeSpan.FromSeconds(10);

    [Fact]
    public void ASeriesSettlesAtTheStartOfItsLastQuietRun()
    {
        List<TagSample> series = Series((9, 0), (10, 300), (11, 2), (12, 40), (13, 3), (14, 1), (15, 0));

        Assert.Equal(TimeSpan.FromSeconds(13), StateChain.SettlesAtOrBelow(series, 5, Start));
    }

    [Fact]
    public void ASeriesThatEndsAboveTheCeilingNeverSettles()
    {
        Assert.Null(StateChain.SettlesAtOrBelow(Series((10, 300), (11, 0), (12, 6)), 5, Start));
    }

    [Fact]
    public void TagsThatFallOneAfterAnotherHoldInThatOrder()
    {
        var traces = new Dictionary<string, IReadOnlyList<TagSample>>(StringComparer.Ordinal)
        {
            ["A"] = Series((10, 300), (11, 0), (12, 0), (13, 0)),
            ["B"] = Series((10, 300), (11, 300), (12, 0), (13, 0)),
        };

        Assert.Null(StateChain.FindFallInOrder(traces, ["A", "B"], Start, 250, 5));
    }

    [Fact]
    public void ATagThatFallsNoLaterThanTheOneBeforeItIsNamed()
    {
        var traces = new Dictionary<string, IReadOnlyList<TagSample>>(StringComparer.Ordinal)
        {
            ["A"] = Series((10, 300), (11, 300), (12, 0), (13, 0)),
            ["B"] = Series((10, 300), (11, 0), (12, 0), (13, 0)),
        };

        Assert.Equal(
            "B settles at or below 5 at 11 s, not after A at 12 s.",
            StateChain.FindFallInOrder(traces, ["A", "B"], Start, 250, 5));
    }

    [Fact]
    public void ATagThatWasNotRunningAtTheStartProvesNothing()
    {
        var traces = new Dictionary<string, IReadOnlyList<TagSample>>(StringComparer.Ordinal)
        {
            ["A"] = Series((10, 0), (11, 0)),
        };

        Assert.Equal(
            "A was not above 250 at 10 s, so its fall proves nothing.",
            StateChain.FindFallInOrder(traces, ["A"], Start, 250, 5));
    }

    [Fact]
    public void AZeroLengthSeriesFailsRatherThanPassingVacuously()
    {
        List<TagSample> empty = [];

        Assert.Null(StateChain.SettlesAtOrBelow(empty, 5, Start));

        var traces = new Dictionary<string, IReadOnlyList<TagSample>>(StringComparer.Ordinal)
        {
            ["A"] = empty,
        };

        Assert.Equal(
            "A was not above 250 at 10 s, so its fall proves nothing.",
            StateChain.FindFallInOrder(traces, ["A"], Start, 250, 5));
    }
}
