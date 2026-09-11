namespace Dse.Core.Validation;

public sealed class SimulationValidationException : Exception
{
    public SimulationValidationException(ValidationResult result)
        : base(BuildMessage(result))
    {
        Result = result;
    }

    public ValidationResult Result { get; }

    private static string BuildMessage(ValidationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.IsValid)
        {
            throw new ArgumentException(
                "A validation exception requires at least one error.", nameof(result));
        }

        return $"The plant failed validation:{Environment.NewLine}{result.ToText()}";
    }
}
