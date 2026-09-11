using Dse.Core.Graph;
using Dse.Core.Tests.Fakes;
using Xunit;

namespace Dse.Core.Tests;

public class CompositeComponentTests
{
    [Fact]
    public void ChildIdsAreQualifiedWithTheCompositeId()
    {
        var stage = new TwoStage("CV001", firstFactor: 2.0, secondFactor: 3.0);

        Assert.Equal(
            new[] { "CV001.First", "CV001.Second" },
            stage.Leaves().Select(c => c.Id));
    }

    [Fact]
    public void QualificationReachesPortOwnerIds()
    {
        var stage = new TwoStage("CV001", 2.0, 3.0);

        ISimComponent first = stage.Leaves().First();
        Assert.All(first.Ports, p => Assert.Equal("CV001.First", p.OwnerId));
    }

    [Fact]
    public void NestingQualifiesEveryDescendant()
    {
        var outer = new NestingComposite("Plant", new TwoStage("CV001", 2.0, 3.0));

        Assert.Equal(
            new[] { "Plant.CV001.First", "Plant.CV001.Second" },
            outer.Leaves().Select(c => c.Id));
    }

    [Fact]
    public void ExposedAliasesResolveToTheUnderlyingChildPorts()
    {
        var stage = new TwoStage("CV001", 2.0, 3.0);

        InputPort<double> input = stage.Input<double>("In");
        OutputPort<double> output = stage.Output<double>("Out");

        Assert.Equal("CV001.First.In", input.QualifiedName);
        Assert.Equal("CV001.Second.Out", output.QualifiedName);
    }

    [Fact]
    public void UnknownAliasThrowsWithTheAvailableNames()
    {
        var stage = new TwoStage("CV001", 2.0, 3.0);

        KeyNotFoundException error =
            Assert.Throws<KeyNotFoundException>(() => stage.Input<double>("Nope"));
        Assert.Contains("In", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AliasOfTheWrongTypeThrows()
    {
        var stage = new TwoStage("CV001", 2.0, 3.0);

        Assert.Throws<InvalidCastException>(() => stage.Input<bool>("In"));
    }

    private sealed class NestingComposite : CompositeComponent
    {
        public NestingComposite(string id, TwoStage inner)
            : base(id) => AddChild(inner);
    }
}
