using Millrace.Core.Validation;
using Xunit;

namespace Millrace.Core.Tests;

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
            new ValidationError("MR002", "Input 'G.In' is required but unconnected.", ["G"]),
        ]);

        Assert.False(result.IsValid);
        Assert.Single(result.Errors);
    }

    [Fact]
    public void TextListsEveryErrorOnItsOwnLine()
    {
        var result = ValidationResult.From(
        [
            new ValidationError("MR001", "first", ["A"]),
            new ValidationError("MR002", "second", ["B"]),
        ]);

        string expected =
            "MR001: first" + Environment.NewLine +
            "MR002: second" + Environment.NewLine;

        Assert.Equal(expected, result.ToText());
    }

    [Fact]
    public void ExceptionCarriesTheResultAndSummarisesItInTheMessage()
    {
        var result = ValidationResult.From(
        [
            new ValidationError("MR003", "Algebraic loop: A -> B -> A.", ["A", "B"]),
        ]);

        var exception = new SimulationValidationException(result);

        Assert.Same(result, exception.Result);
        Assert.Contains("MR003", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ExceptionRejectsAValidResult()
    {
        Assert.Throws<ArgumentException>(
            () => new SimulationValidationException(ValidationResult.Ok()));
    }
}
