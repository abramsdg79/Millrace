using Dse.Core.Flow;

namespace Dse.Core.Catalogue;

/// <summary>
/// Factories for port descriptors. Not called <c>Ports</c>: every component inherits an
/// instance property of that name, which would shadow it inside a descriptor initialiser.
/// </summary>
public static class PortSpec
{
    public static PortDescriptor In<T>(string name, string unit = "", string description = "", bool required = false, PortRepeat? repeat = null)
        where T : unmanaged =>
        new(Valid(name), PortDirection.In, ValueTypeName(typeof(T)), unit, description, required, repeat);

    public static PortDescriptor Out<T>(string name, string unit = "", string description = "", PortRepeat? repeat = null)
        where T : unmanaged =>
        new(Valid(name), PortDirection.Out, ValueTypeName(typeof(T)), unit, description, false, repeat);

    public static FlowPortDescriptor Inlet(string name, PayloadKind payload, string description = "", PortRepeat? repeat = null) =>
        new(Valid(name), PortDirection.In, payload, description, repeat);

    public static FlowPortDescriptor Outlet(string name, PayloadKind payload, string description = "", PortRepeat? repeat = null) =>
        new(Valid(name), PortDirection.Out, payload, description, repeat);

    /// <summary><c>bool</c>, <c>double</c>, <c>int</c>, <c>long</c>, or <c>enum:TypeName</c>.</summary>
    public static string ValueTypeName(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (type == typeof(bool))
        {
            return "bool";
        }

        if (type == typeof(double))
        {
            return "double";
        }

        if (type == typeof(int))
        {
            return "int";
        }

        if (type == typeof(long))
        {
            return "long";
        }

        return type.IsEnum ? $"enum:{type.Name}" : type.Name;
    }

    private static string Valid(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return name;
    }
}
