using Dse.Core.Validation;
using Xunit;

namespace Dse.Core.Tests;

public class ValidationTests
{
    [Fact]
    public void EmptyResultIsValid()
    {
        Assert.True(ValidationResult.Ok().IsValid);
        Assert.Empty(ValidationResult.Ok().Errors);
    }

    [Fact]
    public void ResultWithErrorsIsInvalid()
    {
        var result = ValidationResult.From(
        [
            new ValidationError("DSE002", "Input 'G.In' is required but unconnected.", ["G"]),
        ]);

        Assert.False(result.IsValid);
        Assert.Single(result.Errors);
    }

    [Fact]
    public void TextListsEveryErrorOnItsOwnLine()
    {
        var result = ValidationResult.From(
        [
            new ValidationError("DSE001", "first", ["A"]),
            new ValidationError("DSE002", "second", ["B"]),
        ]);

        string expected =
            "DSE001: first" + Environment.NewLine +
            "DSE002: second" + Environment.NewLine;

        Assert.Equal(expected, result.ToText());
    }

    [Fact]
    public void ExceptionCarriesTheResultAndSummarisesItInTheMessage()
    {
        var result = ValidationResult.From(
        [
            new ValidationError("DSE003", "Algebraic loop: A -> B -> A.", ["A", "B"]),
        ]);

        var exception = new SimulationValidationException(result);

        Assert.Same(result, exception.Result);
        Assert.Contains("DSE003", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ExceptionRejectsAValidResult()
    {
        Assert.Throws<ArgumentException>(
            () => new SimulationValidationException(ValidationResult.Ok()));
    }
}
