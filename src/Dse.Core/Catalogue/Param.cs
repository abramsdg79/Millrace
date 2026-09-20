using Dse.Core.Flow;

namespace Dse.Core.Catalogue;

/// <summary>The only way to make a <see cref="ParameterDescriptor"/>.</summary>
public static class Param
{
    public static ParameterDescriptor Double(
        string name,
        string description,
        string unit = "",
        double? @default = null,
        double? min = null,
        double? max = null,
        bool exclusiveMin = false,
        bool exclusiveMax = false,
        bool optional = false)
    {
        if (@default is { } value && !double.IsFinite(value))
        {
            throw new ArgumentException(
                $"Parameter '{name}' has a non-finite default. JSON cannot carry one; make the parameter optional instead.",
                nameof(@default));
        }

        return new ParameterDescriptor(name, ParameterKind.Double, description)
        {
            Unit = unit,
            Default = @default,
            Minimum = min,
            Maximum = max,
            ExclusiveMinimum = exclusiveMin,
            ExclusiveMaximum = exclusiveMax,
            IsOptional = optional,
        };
    }

    public static ParameterDescriptor Int(
        string name,
        string description,
        string unit = "",
        long? @default = null,
        double? min = null,
        double? max = null,
        bool optional = false) =>
        new(name, ParameterKind.Int, description)
        {
            Unit = unit,
            Default = @default,
            Minimum = min,
            Maximum = max,
            IsOptional = optional,
        };

    public static ParameterDescriptor Bool(string name, string description, bool? @default = null) =>
        new(name, ParameterKind.Bool, description) { Default = @default };

    public static ParameterDescriptor String(string name, string description, string? @default = null) =>
        new(name, ParameterKind.String, description) { Default = @default };

    public static ParameterDescriptor StringList(string name, string description, int minCount = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(minCount);
        return new ParameterDescriptor(name, ParameterKind.StringList, description) { MinCount = minCount };
    }

    public static ParameterDescriptor Enum(string name, string description, IReadOnlyList<string> values, string? @default = null)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count == 0)
        {
            throw new ArgumentException($"Enum parameter '{name}' needs at least one allowed value.", nameof(values));
        }

        if (@default is not null && !values.Contains(@default, StringComparer.Ordinal))
        {
            throw new ArgumentException($"Enum parameter '{name}' defaults to '{@default}', which it does not allow.", nameof(@default));
        }

        return new ParameterDescriptor(name, ParameterKind.Enum, description)
        {
            AllowedValues = values.ToArray(),
            Default = @default,
        };
    }

    public static ParameterDescriptor Group(string name, string description, GroupDefinition group)
    {
        ArgumentNullException.ThrowIfNull(group);
        return new ParameterDescriptor(name, ParameterKind.Group, description)
        {
            GroupName = group.Name,
            Children = group.Parameters,
        };
    }

    public static ParameterDescriptor GroupList(string name, string description, GroupDefinition group, int minCount = 0)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentOutOfRangeException.ThrowIfNegative(minCount);
        return new ParameterDescriptor(name, ParameterKind.GroupList, description)
        {
            GroupName = group.Name,
            Children = group.Parameters,
            MinCount = minCount,
        };
    }

    public static ParameterDescriptor Reference<TCapability>(string name, string description, bool optional = false)
        where TCapability : class =>
        new(name, ParameterKind.Reference, description)
        {
            Capability = typeof(TCapability),
            IsOptional = optional,
        };

    public static ParameterDescriptor Material(string name, string description, PayloadKind? kind = null, bool optional = false) =>
        new(name, ParameterKind.Material, description)
        {
            Payload = kind,
            IsOptional = optional,
        };

    public static ParameterDescriptor MaterialState(string name, string description, string materialParameter)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(materialParameter);
        return new ParameterDescriptor(name, ParameterKind.MaterialState, description)
        {
            MaterialParameter = materialParameter,
        };
    }

    public static ParameterDescriptor Object(string name, string description, string slot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slot);
        return new ParameterDescriptor(name, ParameterKind.Object, description) { Slot = slot };
    }

    public static ParameterDescriptor ObjectList(string name, string description, string slot, int minCount = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slot);
        ArgumentOutOfRangeException.ThrowIfNegative(minCount);
        return new ParameterDescriptor(name, ParameterKind.ObjectList, description)
        {
            Slot = slot,
            MinCount = minCount,
        };
    }
}
