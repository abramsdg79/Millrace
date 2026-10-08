using Millrace.Core.Catalogue;
using Millrace.Io;

namespace Millrace.Control.Catalogue.Tests;

public class TransitionTests
{
    [Theory]
    [InlineData("==", PredicateOperator.Equal)]
    [InlineData("!=", PredicateOperator.NotEqual)]
    [InlineData("<", PredicateOperator.Less)]
    [InlineData("<=", PredicateOperator.LessOrEqual)]
    [InlineData(">", PredicateOperator.Greater)]
    [InlineData(">=", PredicateOperator.GreaterOrEqual)]
    public void AWhenTransitionMapsEveryOperator(string op, PredicateOperator expected)
    {
        ParameterValues values = Bind.Values(
            TransitionCatalogue.When.Parameters, $$"""{ "tag": "V1.Level", "op": "{{op}}", "value": 80 }""");

        var transition = (StepTransition)TransitionCatalogue.When.Factory(values);

        Assert.False(transition.IsTimed);
        Assert.Equal("V1.Level", transition.Tag);
        Assert.Equal(expected, transition.Operator);
        Assert.Equal(TagValue.Double(80.0), transition.Value);
    }

    [Fact]
    public void AnAfterTransitionRunsTheStepClock()
    {
        var transition = (StepTransition)TransitionCatalogue.After.Factory(
            Bind.Values(TransitionCatalogue.After.Parameters, """{ "delayS": 2.5 }"""));

        Assert.True(transition.IsTimed);
        Assert.Equal(TimeSpan.FromSeconds(2.5), transition.Delay);
    }

    [Fact]
    public void ABoolTagCannotBeOrdered()
    {
        ParameterValues values = Bind.Values(
            TransitionCatalogue.When.Parameters, """{ "tag": "V1.Fill", "op": "<", "value": true }""");

        var ex = Assert.Throws<ArgumentException>(() => TransitionCatalogue.When.Factory(values));

        Assert.Contains("A Bool tag cannot be compared with Less", ex.Message, StringComparison.Ordinal);
    }
}
