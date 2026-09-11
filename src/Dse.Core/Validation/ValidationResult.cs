using System.Text;

namespace Dse.Core.Validation;

public sealed class ValidationResult
{
    private static readonly ValidationResult Valid = new([]);

    private ValidationResult(IReadOnlyList<ValidationError> errors) => Errors = errors;

    public IReadOnlyList<ValidationError> Errors { get; }

    public bool IsValid => Errors.Count == 0;

    public static ValidationResult Ok() => Valid;

    public static ValidationResult From(IEnumerable<ValidationError> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);

        List<ValidationError> list = errors.ToList();
        return list.Count == 0 ? Valid : new ValidationResult(list);
    }

    public string ToText()
    {
        var builder = new StringBuilder();
        foreach (ValidationError error in Errors)
        {
            builder.Append(error.Code).Append(": ").Append(error.Message)
                   .Append(Environment.NewLine);
        }

        return builder.ToString();
    }
}
