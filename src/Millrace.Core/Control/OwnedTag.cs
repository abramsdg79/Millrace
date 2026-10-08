using Millrace.Core.Graph;
using Millrace.Core.Io;
using Millrace.Io;

namespace Millrace.Core.Control;

/// <summary>
/// A tag a scan block owns: a port nothing else in the plant touches, the
/// binding that publishes it, and — for an output — the way the host stores a
/// scan's value into that port. An output is an <see cref="OutputPort{T}"/> the
/// host writes and the image captures; a command is an
/// <see cref="InputPort{T}"/> a writable binding drives, which is what makes a
/// command an ordinary tag write (R66).
/// </summary>
internal sealed class OwnedTag
{
    private readonly Action<TagValue>? _store;

    private OwnedTag(string name, TagBinding binding, Action<TagValue>? store)
    {
        Name = name;
        Binding = binding;
        _store = store;
    }

    /// <summary>The full tag name: the block id, a dot, and the pin name.</summary>
    internal string Name { get; }

    /// <summary>The binding the directory publishes. Its Kind is what Task 4 checks a scanned value against.</summary>
    internal TagBinding Binding { get; }

    /// <summary>Stores a scanned value into the port. A command has nothing to store.</summary>
    internal void Store(TagValue value) => _store?.Invoke(value);

    /// <summary>A read-only owned tag over a fresh output port.</summary>
    internal static OwnedTag Output(string blockId, string fullName, TagSpec spec)
    {
        switch (spec.Kind)
        {
            case TagKind.Bool:
            {
                var port = new OutputPort<bool>(spec.Name, blockId);
                return new OwnedTag(
                    fullName,
                    TagBinding.Read(fullName, port, spec.Description),
                    value => port.Value = value.AsBool);
            }

            case TagKind.Double:
            {
                var port = new OutputPort<double>(spec.Name, blockId);
                return new OwnedTag(
                    fullName,
                    TagBinding.Read(fullName, port, spec.Unit, double.NaN, double.NaN, spec.Description),
                    value => port.Value = value.AsDouble);
            }

            default:
            {
                var port = new OutputPort<long>(spec.Name, blockId);
                return new OwnedTag(
                    fullName,
                    TagBinding.Read(fullName, port, Unit(spec), spec.Description),
                    value => port.Value = value.AsInt64);
            }
        }
    }

    /// <summary>A read-write owned tag over a fresh input port.</summary>
    internal static OwnedTag Command(string blockId, string fullName, TagSpec spec)
    {
        switch (spec.Kind)
        {
            case TagKind.Bool:
            {
                var port = new InputPort<bool>(spec.Name, blockId, false, isRequired: false);
                return new OwnedTag(fullName, TagBinding.Write(fullName, port, spec.Description), null);
            }

            case TagKind.Double:
            {
                var port = new InputPort<double>(spec.Name, blockId, 0.0, isRequired: false);
                return new OwnedTag(
                    fullName,
                    TagBinding.Write(fullName, port, spec.Unit, double.NaN, double.NaN, spec.Description),
                    null);
            }

            default:
            {
                var port = new InputPort<long>(spec.Name, blockId, 0L, isRequired: false);
                return new OwnedTag(fullName, TagBinding.Write(fullName, port, Unit(spec), spec.Description), null);
            }
        }
    }

    /// <summary>An integer tag with no declared unit publishes "count", as every other integer tag does.</summary>
    private static string Unit(TagSpec spec) => spec.Unit.Length == 0 ? "count" : spec.Unit;
}
