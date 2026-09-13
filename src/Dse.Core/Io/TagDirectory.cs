using System.Globalization;
using System.Text;
using Dse.Io;

namespace Dse.Core.Io;

/// <summary>
/// The plant's tag directory (spec 9.1): bindings sorted by ordinal name, each
/// given the index it keeps for the life of the simulation. Built once by the
/// builder; immutable.
/// </summary>
public sealed class TagDirectory : ITagDirectory
{
    private readonly TagDescriptor[] _descriptors;
    private readonly Dictionary<string, int> _indexByName = new(StringComparer.Ordinal);

    internal TagDirectory(IEnumerable<TagBinding> fullyNamed)
    {
        ArgumentNullException.ThrowIfNull(fullyNamed);

        Bindings = fullyNamed.OrderBy(b => b.Name, StringComparer.Ordinal).ToArray();
        _descriptors = new TagDescriptor[Bindings.Length];
        for (int i = 0; i < Bindings.Length; i++)
        {
            TagBinding b = Bindings[i];
            if (!_indexByName.TryAdd(b.Name, i))
            {
                throw new ArgumentException(
                    $"Tag '{b.Name}' is bound twice ('{Bindings[_indexByName[b.Name]].Port.QualifiedName}' and " +
                    $"'{b.Port.QualifiedName}'). Names must be unique.",
                    nameof(fullyNamed));
            }

            _descriptors[i] = new TagDescriptor(i, b.Name, b.Kind, b.Access, b.Unit, b.RangeLow, b.RangeHigh, b.Description);
        }
    }

    /// <summary>The bindings in index order.</summary>
    internal TagBinding[] Bindings { get; }

    /// <inheritdoc/>
    public int Count => _descriptors.Length;

    /// <inheritdoc/>
    public IReadOnlyList<TagDescriptor> Tags => _descriptors;

    /// <inheritdoc/>
    public TagDescriptor this[int index] => _descriptors[index];

    /// <inheritdoc/>
    public bool TryFind(string name, out TagDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (_indexByName.TryGetValue(name, out int index))
        {
            descriptor = _descriptors[index];
            return true;
        }

        descriptor = null!;
        return false;
    }

    /// <inheritdoc/>
    public TagDescriptor Find(string name)
    {
        if (TryFind(name, out TagDescriptor descriptor))
        {
            return descriptor;
        }

        throw new KeyNotFoundException(
            $"No tag '{name}'. The directory has {Count} tags; call ToText() to list them.");
    }

    /// <summary>One line per tag: name, kind, access, unit, range, description.</summary>
    public string ToText()
    {
        var builder = new StringBuilder();
        foreach (TagDescriptor tag in _descriptors)
        {
            builder.Append(tag.Name)
                   .Append("  ").Append(tag.Kind)
                   .Append("  ").Append(tag.Access);
            if (tag.Unit.Length > 0)
            {
                builder.Append("  ").Append(tag.Unit);
            }

            if (tag.HasRange)
            {
                builder.Append(string.Create(CultureInfo.InvariantCulture, $"  [{tag.RangeLow}, {tag.RangeHigh}]"));
            }

            if (tag.Description.Length > 0)
            {
                builder.Append("  ").Append(tag.Description);
            }

            builder.Append(Environment.NewLine);
        }

        return builder.ToString();
    }
}
