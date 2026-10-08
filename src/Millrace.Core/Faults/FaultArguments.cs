using System.Globalization;
using System.Text;

namespace Millrace.Core.Faults;

/// <summary>
/// The arguments a fault is injected with. Small and ordered — a fault has a
/// handful of parameters — so lookup is a linear scan and iteration is stable.
/// </summary>
public sealed class FaultArguments
{
    private readonly FaultArgument[] _arguments;

    public static FaultArguments None { get; } = new();

    public FaultArguments(params FaultArgument[] arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        for (int i = 0; i < arguments.Length; i++)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(arguments[i].Name, nameof(arguments));
            for (int j = 0; j < i; j++)
            {
                if (string.Equals(arguments[i].Name, arguments[j].Name, StringComparison.Ordinal))
                {
                    throw new ArgumentException(
                        $"Argument '{arguments[i].Name}' is given twice.", nameof(arguments));
                }
            }
        }

        _arguments = arguments.ToArray();
    }

    public int Count => _arguments.Length;

    public FaultArgument this[int index] => _arguments[index];

    public bool TryGet(string name, out double value)
    {
        for (int i = 0; i < _arguments.Length; i++)
        {
            if (string.Equals(_arguments[i].Name, name, StringComparison.Ordinal))
            {
                value = _arguments[i].Value;
                return true;
            }
        }

        value = 0.0;
        return false;
    }

    public double Get(string name)
    {
        if (TryGet(name, out double value))
        {
            return value;
        }

        throw new KeyNotFoundException(
            $"No fault argument named '{name}'. Present: {string.Join(", ", _arguments.Select(a => a.Name))}.");
    }

    /// <summary><c>name=value, name=value</c>, invariant. Empty for no arguments.</summary>
    public override string ToString()
    {
        var builder = new StringBuilder();
        for (int i = 0; i < _arguments.Length; i++)
        {
            if (i > 0)
            {
                builder.Append(", ");
            }

            builder.Append(_arguments[i].Name)
                   .Append('=')
                   .Append(_arguments[i].Value.ToString(CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }
}
