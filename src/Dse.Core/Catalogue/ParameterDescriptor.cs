using Dse.Core.Flow;

namespace Dse.Core.Catalogue;

/// <summary>One parameter of a component, object or group. Built with <see cref="Param"/>.</summary>
public sealed class ParameterDescriptor
{
    internal ParameterDescriptor(string name, ParameterKind kind, string description)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        if (!IsCamelCase(name))
        {
            throw new ArgumentException(
                $"Parameter name '{name}' must be camelCase: a lowercase letter, then letters and digits.",
                nameof(name));
        }

        Name = name;
        Kind = kind;
        Description = description;
    }

    public string Name { get; }

    public ParameterKind Kind { get; }

    public string Description { get; }

    public string Unit { get; init; } = string.Empty;

    /// <summary>A <see cref="double"/>, <see cref="long"/>, <see cref="bool"/> or <see cref="string"/>; null when there is none.</summary>
    public object? Default { get; init; }

    /// <summary>May be omitted although it has no default; the factory decides what absence means.</summary>
    public bool IsOptional { get; init; }

    public double? Minimum { get; init; }

    public double? Maximum { get; init; }

    public bool ExclusiveMinimum { get; init; }

    public bool ExclusiveMaximum { get; init; }

    public IReadOnlyList<string> AllowedValues { get; init; } = [];

    /// <summary>For <see cref="ParameterKind.Group"/> and <see cref="ParameterKind.GroupList"/>: the shared definition's name.</summary>
    public string GroupName { get; init; } = string.Empty;

    public IReadOnlyList<ParameterDescriptor> Children { get; init; } = [];

    /// <summary>For <see cref="ParameterKind.Reference"/>: what the referenced component must supply.</summary>
    public Type? Capability { get; init; }

    /// <summary>For <see cref="ParameterKind.Material"/>: the payload kind required, or null for either.</summary>
    public PayloadKind? Payload { get; init; }

    /// <summary>For <see cref="ParameterKind.MaterialState"/>: the sibling material parameter it indexes.</summary>
    public string MaterialParameter { get; init; } = string.Empty;

    /// <summary>For <see cref="ParameterKind.Object"/> and <see cref="ParameterKind.ObjectList"/>.</summary>
    public string Slot { get; init; } = string.Empty;

    /// <summary>For the three list kinds.</summary>
    public int MinCount { get; init; }

    /// <summary>True when a plant file must supply it.</summary>
    public bool IsRequired => Kind switch
    {
        ParameterKind.Group => Children.Any(child => child.IsRequired),
        ParameterKind.GroupList or ParameterKind.ObjectList or ParameterKind.StringList => MinCount > 0,
        ParameterKind.Reference or ParameterKind.Material or ParameterKind.MaterialState or ParameterKind.Object => !IsOptional,
        _ => Default is null && !IsOptional,
    };

    private static bool IsCamelCase(string name)
    {
        if (name.Length == 0 || !char.IsAsciiLetterLower(name[0]))
        {
            return false;
        }

        foreach (char c in name)
        {
            if (!char.IsAsciiLetterOrDigit(c))
            {
                return false;
            }
        }

        return true;
    }
}
