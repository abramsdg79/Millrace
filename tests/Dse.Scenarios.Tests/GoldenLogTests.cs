namespace Dse.Scenarios.Tests;

public class GoldenLogTests
{
    private const string Six = "a\nb\nc\nd\ne\nf\n";

    [Fact]
    public void IdenticalLogsMatch()
    {
        LogComparison comparison = GoldenLog.Compare(Six, Six);

        Assert.True(comparison.Matched);
        Assert.Equal(0, comparison.FirstDifferentLine);
        Assert.Equal((6, 6), (comparison.ExpectedLines, comparison.ActualLines));
        Assert.Equal("The logs match (6 lines).\n", comparison.Report);
    }

    [Fact]
    public void CarriageReturnsAreNotADifference()
    {
        Assert.True(GoldenLog.Compare("a\r\nb\r\n", "a\nb\n").Matched);
    }

    [Theory]
    [InlineData("a\nb")]
    [InlineData("a\nb\n")]
    [InlineData("a\nb\n\n\n")]
    public void TrailingNewlinesAreNotADifference(string actual)
    {
        Assert.True(GoldenLog.Compare("a\nb\n", actual).Matched);
    }

    [Fact]
    public void TwoEmptyLogsMatch()
    {
        LogComparison comparison = GoldenLog.Compare(string.Empty, "\n");

        Assert.True(comparison.Matched);
        Assert.Equal((0, 0), (comparison.ExpectedLines, comparison.ActualLines));
    }

    [Fact]
    public void AnEmptyActualAgainstAFullExpectedDivergesAtLineOne()
    {
        LogComparison comparison = GoldenLog.Compare(Six, string.Empty);

        Assert.False(comparison.Matched);
        Assert.Equal(1, comparison.FirstDifferentLine);
        Assert.Equal((6, 0), (comparison.ExpectedLines, comparison.ActualLines));
        Assert.Contains("actual (0 lines):\n  (no lines)\n", comparison.Report, StringComparison.Ordinal);
    }

    [Fact]
    public void TheFirstDifferenceIsReportedWithContextFromBothTexts()
    {
        LogComparison comparison = GoldenLog.Compare(Six, "a\nb\nc\nD\ne\nf\n");

        Assert.False(comparison.Matched);
        Assert.Equal(4, comparison.FirstDifferentLine);
        Assert.Equal(
            """
            The logs differ at line 4.
            expected (6 lines):
                  1 | a
                  2 | b
                  3 | c
            >     4 | d
                  5 | e
                  6 | f
            actual (6 lines):
                  1 | a
                  2 | b
                  3 | c
            >     4 | D
                  5 | e
                  6 | f

            """.ReplaceLineEndings("\n"),
            comparison.Report);
    }

    [Fact]
    public void ContextIsAtMostThreeLinesEitherSide()
    {
        LogComparison comparison = GoldenLog.Compare(
            "1\n2\n3\n4\n5\n6\n7\n8\n9\n10\n11\n",
            "1\n2\n3\n4\n5\n6\nX\n8\n9\n10\n11\n");

        Assert.Equal(7, comparison.FirstDifferentLine);
        Assert.Contains("      4 | 4\n", comparison.Report, StringComparison.Ordinal);
        Assert.DoesNotContain("      3 | 3\n", comparison.Report, StringComparison.Ordinal);
        Assert.Contains("     10 | 10\n", comparison.Report, StringComparison.Ordinal);
        Assert.DoesNotContain("     11 | 11\n", comparison.Report, StringComparison.Ordinal);
    }

    [Fact]
    public void AShorterActualDivergesAfterItsLastLine()
    {
        LogComparison comparison = GoldenLog.Compare(Six, "a\nb\nc\n");

        Assert.False(comparison.Matched);
        Assert.Equal(4, comparison.FirstDifferentLine);
        Assert.Equal((6, 3), (comparison.ExpectedLines, comparison.ActualLines));
        Assert.Contains("actual (3 lines):", comparison.Report, StringComparison.Ordinal);
    }

    [Fact]
    public void ALongerActualDivergesAfterTheExpectedsLastLine()
    {
        LogComparison comparison = GoldenLog.Compare("a\nb\nc\n", Six);

        Assert.False(comparison.Matched);
        Assert.Equal(4, comparison.FirstDifferentLine);
        Assert.Equal((3, 6), (comparison.ExpectedLines, comparison.ActualLines));
        Assert.Contains(">     4 | d\n", comparison.Report, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("\n", "")]
    [InlineData("a", "a\n")]
    [InlineData("a\n", "a\n")]
    [InlineData("a\r\nb", "a\nb\n")]
    [InlineData("a\n\n\n", "a\n")]
    public void NormaliseGivesOneTrailingNewlineAndNoCarriageReturns(string text, string expected)
    {
        Assert.Equal(expected, GoldenLog.Normalise(text));
    }

    [Fact]
    public void TheReportAlwaysEndsWithOneNewline()
    {
        Assert.EndsWith("\n", GoldenLog.Compare(Six, Six).Report, StringComparison.Ordinal);
        Assert.EndsWith("\n", GoldenLog.Compare(Six, "x\n").Report, StringComparison.Ordinal);
    }
}
