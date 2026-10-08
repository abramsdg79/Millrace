using Millrace.Core.Catalogue;

namespace Millrace.Core.Tests.Catalogue;

public class SuggestTests
{
    [Fact]
    public void FindsATransposition()
    {
        Assert.Equal("Start", Suggest.Closest("Strat", ["Reset", "Start", "Speed"]));
    }

    [Fact]
    public void IgnoresCase()
    {
        Assert.Equal("CV001", Suggest.Closest("cv001", ["CV001", "CV002"]));
    }

    [Fact]
    public void GivesUpWhenNothingIsNear()
    {
        Assert.Null(Suggest.Closest("hopper", ["Start", "Reset"]));
    }

    [Fact]
    public void BreaksATieByOrdinalOrder()
    {
        Assert.Equal("CV001", Suggest.Closest("CV00", ["CV002", "CV001"]));
    }

    [Fact]
    public void ListsSortedAndTruncates()
    {
        Assert.Equal("a, b, c", Suggest.List(["c", "a", "b"]));
        Assert.Equal("a, b, … (4 in all)", Suggest.List(["d", "c", "a", "b"], max: 2));
    }

    [Fact]
    public void WritesAFixSentence()
    {
        Assert.Equal("Use one of CV001, CV002 — 'CV001' is closest.", Suggest.Fix("CV01", ["CV002", "CV001"], "components"));
        Assert.Equal("Use one of Reset, Start.", Suggest.Fix("hopper", ["Start", "Reset"], "ports"));
        Assert.Equal("There are no materials to choose from; define one first.", Suggest.Fix("ore", [], "materials"));
    }
}
