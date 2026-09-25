using Dse.Core.Flow;
using Dse.Io;

namespace Dse.Core.Catalogue;

/// <summary>
/// The parsed, defaulted, range-checked, resolved parameters a factory reads.
/// Asking for a name the descriptor does not declare throws: that is a bug in
/// the factory, and the conformance test finds it.
/// </summary>
public sealed class ParameterValues
{
    private readonly Dictionary<string, object?> _values;
    private readonly string[] _declared;

    internal ParameterValues(Dictionary<string, object?> values, IEnumerable<string> declared)
    {
        _values = values;
        _declared = declared.ToArray();
    }

    /// <summary>True when the parameter was given or defaulted; false for an optional one left out.</summary>
    public bool Has(string name)
    {
        RequireDeclared(name);
        return _values.ContainsKey(name);
    }

    public double Double(string name) => Get<double>(name);

    public double DoubleOr(string name, double fallback) => Has(name) ? Get<double>(name) : fallback;

    public int Int(string name) => Get<int>(name);

    public int IntOr(string name, int fallback) => Has(name) ? Get<int>(name) : fallback;

    public bool Bool(string name) => Get<bool>(name);

    /// <summary>A <see cref="ParameterKind.String"/> or <see cref="ParameterKind.Enum"/> value.</summary>
    public string String(string name) => Get<string>(name);

    public IReadOnlyList<string> Strings(string name) => Get<IReadOnlyList<string>>(name);

    public ParameterValues Group(string name) => Get<ParameterValues>(name);

    public IReadOnlyList<ParameterValues> Groups(string name) => Get<IReadOnlyList<ParameterValues>>(name);

    /// <summary>The capability the referenced component supplied.</summary>
    public T Reference<T>(string name)
        where T : class => Get<T>(name);

    public MaterialType Material(string name) => Get<MaterialDescriptor>(name).Material;

    public MaterialType? MaterialOrNull(string name) => Has(name) ? Material(name) : null;

    /// <summary>The default properties the named material was defined with.</summary>
    public MaterialProperties MaterialProperties(string name) => Get<MaterialDescriptor>(name).Properties;

    /// <summary>The index of a <see cref="ParameterKind.MaterialState"/> in its material's state schema.</summary>
    public int StateIndex(string name) => Get<int>(name);

    /// <summary>A <see cref="ParameterKind.Tag"/>: the tag's full name.</summary>
    public string Tag(string name) => Get<string>(name);

    /// <summary>A <see cref="ParameterKind.Value"/>, of the kind of the tag its sibling names.</summary>
    public TagValue Value(string name) => Get<TagValue>(name);

    public T Object<T>(string name)
        where T : class => Get<T>(name);

    public IReadOnlyList<T> Objects<T>(string name)
        where T : class => Get<IReadOnlyList<object>>(name).Cast<T>().ToList();

    private T Get<T>(string name)
    {
        RequireDeclared(name);
        if (!_values.TryGetValue(name, out object? value))
        {
            throw new KeyNotFoundException(
                $"Parameter '{name}' is optional and was left out. Test for it with Has, or read it with an ...Or method.");
        }

        if (value is not T typed)
        {
            throw new InvalidCastException(
                $"Parameter '{name}' holds {value?.GetType().Name ?? "nothing"}, not {typeof(T).Name}. " +
                $"Read it with the accessor that matches its declared kind.");
        }

        return typed;
    }

    private void RequireDeclared(string name)
    {
        if (Array.IndexOf(_declared, name) < 0)
        {
            throw new KeyNotFoundException(
                $"The factory asked for parameter '{name}', which its descriptor does not declare. " +
                $"Declared: {string.Join(", ", _declared)}.");
        }
    }
}
