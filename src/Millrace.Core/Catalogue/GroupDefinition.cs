namespace Millrace.Core.Catalogue;

/// <summary>
/// A named, reusable set of parameters — one per nested record type. Declared
/// once and shared, so the schema defines it once under <c>$defs</c>.
/// </summary>
public sealed class GroupDefinition
{
    public GroupDefinition(string name, params ParameterDescriptor[] parameters)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(parameters);
        Name = name;
        Parameters = parameters.ToArray();
    }

    /// <summary>PascalCase, unique across a catalogue: <c>MotorRating</c>.</summary>
    public string Name { get; }

    public IReadOnlyList<ParameterDescriptor> Parameters { get; }
}
