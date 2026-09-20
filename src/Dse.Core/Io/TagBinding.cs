using System.Globalization;
using Dse.Core.Graph;
using Dse.Io;

namespace Dse.Core.Io;

/// <summary>
/// Maps a tag name to a port with the metadata the directory publishes
/// (spec 9.1). Immutable. Created through the static factories; the builder
/// qualifies the name and, for writable bindings, marks the input as
/// externally driven at <c>Build()</c>.
/// </summary>
public sealed class TagBinding
{
    private readonly Func<TagValue> _capture;
    private readonly Action<TagValue>? _apply;
    private readonly Action? _bindExternal;

    private TagBinding(
        Port port,
        string name,
        TagKind kind,
        TagAccess access,
        string unit,
        double rangeLow,
        double rangeHigh,
        string description,
        Func<TagValue> capture,
        Action<TagValue>? apply,
        Action? bindExternal)
    {
        Port = port;
        Name = name;
        Kind = kind;
        Access = access;
        Unit = unit;
        RangeLow = rangeLow;
        RangeHigh = rangeHigh;
        Description = description;
        _capture = capture;
        _apply = apply;
        _bindExternal = bindExternal;
    }

    /// <summary>The bound port.</summary>
    public Port Port { get; }

    /// <summary>Relative to the declaring component until the builder qualifies it; full thereafter.</summary>
    public string Name { get; }

    /// <summary>The published value kind.</summary>
    public TagKind Kind { get; }

    /// <summary>Whether the tag accepts writes.</summary>
    public TagAccess Access { get; }

    /// <summary>Engineering unit; empty for discrete tags.</summary>
    public string Unit { get; }

    /// <summary>Lower range bound or NaN.</summary>
    public double RangeLow { get; }

    /// <summary>Upper range bound or NaN.</summary>
    public double RangeHigh { get; }

    /// <summary>Human description, a sentence fragment.</summary>
    public string Description { get; }

    /// <summary>A read-only discrete tag.</summary>
    public static TagBinding Read(string name, OutputPort<bool> port, string description = "")
    {
        ArgumentNullException.ThrowIfNull(port);
        return new TagBinding(
            port, ValidName(name), TagKind.Bool, TagAccess.ReadOnly, string.Empty, double.NaN, double.NaN,
            description ?? string.Empty, () => TagValue.Bool(port.Value), null, null);
    }

    /// <summary>A read-only analog tag with unit, range and an optional quality source (R25).</summary>
    public static TagBinding Read(
        string name,
        OutputPort<double> port,
        string unit,
        double rangeLow = double.NaN,
        double rangeHigh = double.NaN,
        string description = "",
        OutputPort<TagQuality>? quality = null)
    {
        ArgumentNullException.ThrowIfNull(port);
        ArgumentNullException.ThrowIfNull(unit);
        ValidRange(rangeLow, rangeHigh);

        Func<TagValue> capture = quality is null
            ? () => TagValue.Double(port.Value)
            : () => TagValue.Double(port.Value, quality.Value);

        return new TagBinding(
            port, ValidName(name), TagKind.Double, TagAccess.ReadOnly, unit, rangeLow, rangeHigh,
            description ?? string.Empty, capture, null, null);
    }

    /// <summary>A read-only integer tag.</summary>
    public static TagBinding Read(string name, OutputPort<long> port, string unit = "count", string description = "")
    {
        ArgumentNullException.ThrowIfNull(port);
        ArgumentNullException.ThrowIfNull(unit);
        return new TagBinding(
            port, ValidName(name), TagKind.Int64, TagAccess.ReadOnly, unit, double.NaN, double.NaN,
            description ?? string.Empty, () => TagValue.Int64(port.Value), null, null);
    }

    /// <summary>A read-only integer tag over an <see cref="int"/> port; published as Int64.</summary>
    public static TagBinding Read(string name, OutputPort<int> port, string unit = "count", string description = "")
    {
        ArgumentNullException.ThrowIfNull(port);
        ArgumentNullException.ThrowIfNull(unit);
        return new TagBinding(
            port, ValidName(name), TagKind.Int64, TagAccess.ReadOnly, unit, double.NaN, double.NaN,
            description ?? string.Empty, () => TagValue.Int64(port.Value), null, null);
    }

    /// <summary>
    /// A read-only integer tag over an enum port. The members are appended to
    /// the description as <c>0=Idle, 1=Filling</c> so a consumer can decode it.
    /// </summary>
    public static TagBinding ReadEnum<TEnum>(string name, OutputPort<TEnum> port, string description = "")
        where TEnum : unmanaged, Enum
    {
        ArgumentNullException.ThrowIfNull(port);

        string members = string.Join(", ", Enum.GetValues<TEnum>().Select(v =>
            string.Create(CultureInfo.InvariantCulture, $"{EnumBits<TEnum>.ToInt64(v)}={v}")));
        string full = string.IsNullOrEmpty(description) ? members : $"{description} ({members})";

        return new TagBinding(
            port, ValidName(name), TagKind.Int64, TagAccess.ReadOnly, string.Empty, double.NaN, double.NaN,
            full, () => TagValue.Int64(EnumBits<TEnum>.ToInt64(port.Value)), null, null);
    }

    /// <summary>A writable discrete tag driving an input (R23).</summary>
    public static TagBinding Write(string name, InputPort<bool> port, string description = "")
    {
        ArgumentNullException.ThrowIfNull(port);
        return new TagBinding(
            port, ValidName(name), TagKind.Bool, TagAccess.ReadWrite, string.Empty, double.NaN, double.NaN,
            description ?? string.Empty,
            () => TagValue.Bool(port.Value),
            v => port.SetExternal(v.AsBool),
            port.DriveExternally);
    }

    /// <summary>A writable analog tag driving an input, with unit and range.</summary>
    public static TagBinding Write(
        string name,
        InputPort<double> port,
        string unit,
        double rangeLow = double.NaN,
        double rangeHigh = double.NaN,
        string description = "")
    {
        ArgumentNullException.ThrowIfNull(port);
        ArgumentNullException.ThrowIfNull(unit);
        ValidRange(rangeLow, rangeHigh);
        return new TagBinding(
            port, ValidName(name), TagKind.Double, TagAccess.ReadWrite, unit, rangeLow, rangeHigh,
            description ?? string.Empty,
            () => TagValue.Double(port.Value),
            v => port.SetExternal(v.AsDouble),
            port.DriveExternally);
    }

    /// <summary>A writable integer tag driving an input.</summary>
    public static TagBinding Write(string name, InputPort<long> port, string unit = "count", string description = "")
    {
        ArgumentNullException.ThrowIfNull(port);
        ArgumentNullException.ThrowIfNull(unit);
        return new TagBinding(
            port, ValidName(name), TagKind.Int64, TagAccess.ReadWrite, unit, double.NaN, double.NaN,
            description ?? string.Empty,
            () => TagValue.Int64(port.Value),
            v => port.SetExternal(v.AsInt64),
            port.DriveExternally);
    }

    /// <summary>Reads the port (and quality source) into a value. Phase 4 only.</summary>
    internal TagValue Capture() => _capture();

    /// <summary>Applies an external write to the input. Phase 1 only.</summary>
    internal void Apply(TagValue value)
    {
        if (_apply is null)
        {
            throw new InvalidOperationException($"Tag '{Name}' is read-only.");
        }

        if (value.Kind != Kind)
        {
            throw new InvalidOperationException(
                $"Tag '{Name}' is a {Kind} tag; cannot apply a {value.Kind} value.");
        }

        _apply(value);
    }

    /// <summary>Marks the input externally driven. Called by the builder for writable bindings.</summary>
    internal void BindExternal() => _bindExternal?.Invoke();

    /// <summary>
    /// Binds a port whose value type is not known at compile time — the way a
    /// configuration file binds tags. Supports bool, double, int and long ports.
    /// An input may be bound <see cref="TagAccess.ReadOnly"/> to observe a command.
    /// </summary>
    public static TagBinding ForPort(
        string name,
        Port port,
        TagAccess access,
        string unit = "",
        double rangeLow = double.NaN,
        double rangeHigh = double.NaN,
        string description = "")
    {
        ArgumentNullException.ThrowIfNull(port);
        ArgumentNullException.ThrowIfNull(unit);
        description ??= string.Empty;
        bool writable = access == TagAccess.ReadWrite;

        switch (port)
        {
            case OutputPort<bool> o when !writable:
                return Read(name, o, description);
            case OutputPort<double> o when !writable:
                return Read(name, o, unit, rangeLow, rangeHigh, description);
            case OutputPort<long> o when !writable:
                return Read(name, o, unit.Length == 0 ? "count" : unit, description);
            case OutputPort<int> o when !writable:
                return Read(name, o, unit.Length == 0 ? "count" : unit, description);
            case InputPort<bool> i:
                return writable ? Write(name, i, description) : Write(name, i, description).AsReadOnly();
            case InputPort<double> i:
                return writable
                    ? Write(name, i, unit, rangeLow, rangeHigh, description)
                    : Write(name, i, unit, rangeLow, rangeHigh, description).AsReadOnly();
            case InputPort<long> i:
                return writable
                    ? Write(name, i, unit.Length == 0 ? "count" : unit, description)
                    : Write(name, i, unit.Length == 0 ? "count" : unit, description).AsReadOnly();
            case InputPort<int> i when writable:
                throw new ArgumentException(
                    $"Port '{port.QualifiedName}' is an int input; a tag can observe it but not write it, " +
                    $"because tags carry 64-bit integers. Bind it read-only, or declare the input as long.",
                    nameof(access));
            case InputPort<int> i:
                return new TagBinding(
                    i, ValidName(name), TagKind.Int64, TagAccess.ReadOnly, unit.Length == 0 ? "count" : unit,
                    double.NaN, double.NaN, description, () => TagValue.Int64(i.Value), null, null);
        }

        if (writable && IsOutput(port))
        {
            throw new ArgumentException(
                $"Port '{port.QualifiedName}' is an output; a tag can read it but not write it. " +
                $"Bind it read-only, or bind the input it drives.",
                nameof(access));
        }

        throw new ArgumentException(
            $"Port '{port.QualifiedName}' carries {port.ValueType?.Name ?? "material"}, which has no tag kind. " +
            $"A tag binds a bool, double, int, long port (an int input is read-only).",
            nameof(port));
    }

    private static bool IsOutput(Port port) => port.ValueType is not null && !port.IsInput;

    /// <summary>The same binding under a different (usually fully qualified) name.</summary>
    internal TagBinding WithName(string name) => new(
        Port, ValidName(name), Kind, Access, Unit, RangeLow, RangeHigh, Description, _capture, _apply, _bindExternal);

    /// <summary>The same binding with the write path removed (R23: the input is wired, so the tag only observes).</summary>
    internal TagBinding AsReadOnly() => new(
        Port, Name, Kind, TagAccess.ReadOnly, Unit, RangeLow, RangeHigh, Description, _capture, null, null);

    private static string ValidName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name.StartsWith('.') || name.EndsWith('.') || name.Contains("..", StringComparison.Ordinal))
        {
            throw new ArgumentException($"Tag name '{name}' has an empty segment.", nameof(name));
        }

        foreach (char c in name)
        {
            if (char.IsWhiteSpace(c))
            {
                throw new ArgumentException($"Tag name '{name}' contains whitespace.", nameof(name));
            }
        }

        return name;
    }

    private static void ValidRange(double low, double high)
    {
        if (double.IsNaN(low) && double.IsNaN(high))
        {
            return;
        }

        if (!double.IsFinite(low) || !double.IsFinite(high) || high <= low)
        {
            throw new ArgumentException(
                string.Create(CultureInfo.InvariantCulture,
                    $"Range [{low}, {high}] must be both NaN (no range) or finite and ascending."));
        }
    }
}
