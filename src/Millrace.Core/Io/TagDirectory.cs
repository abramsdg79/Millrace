using System.Globalization;
using System.Text;
using Millrace.Io;

namespace Millrace.Core.Io;

/// <summary>
/// The plant's tag directory (spec 9.1): bindings sorted by ordinal name, each
/// given the index it keeps for the life of the simulation. Built once by the
/// builder; immutable.
/// </summary>
public sealed class TagDirectory : ITagDirectory
{
    private readonly TagDescriptor[] _descriptors;
    private readonly string?[] _claimants;
    private readonly Dictionary<string, int> _indexByName = new(StringComparer.Ordinal);

    /// <summary>
    /// Sorts and indexes the bindings. <paramref name="claimants"/> maps a tag
    /// name to the block that claims it; a claimed tag is published
    /// <see cref="TagAccess.ReadOnly"/> with <see cref="TagDescriptor.ClaimedBy"/>
    /// set, while its binding stays writable for the claimant.
    /// </summary>
    internal TagDirectory(IEnumerable<TagBinding> fullyNamed, IReadOnlyDictionary<string, string>? claimants = null)
    {
        ArgumentNullException.ThrowIfNull(fullyNamed);

        Bindings = fullyNamed.OrderBy(b => b.Name, StringComparer.Ordinal).ToArray();
        _descriptors = new TagDescriptor[Bindings.Length];
        _claimants = new string?[Bindings.Length];
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

            string? claimant = claimants is not null && claimants.TryGetValue(b.Name, out string? id) ? id : null;
            _claimants[i] = claimant;
            _descriptors[i] = new TagDescriptor(
                i, b.Name, b.Kind, claimant is null ? b.Access : TagAccess.ReadOnly, b.Unit, b.RangeLow, b.RangeHigh, b.Description)
            {
                ClaimedBy = claimant ?? string.Empty,
            };
        }
    }

    /// <summary>The bindings in index order.</summary>
    internal TagBinding[] Bindings { get; }

    /// <summary>The block that claims the tag at <paramref name="index"/>, or null when none does.</summary>
    internal string? ClaimantOf(int index) => _claimants[index];

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

    /// <summary>One line per tag: name, kind, access, unit, range, description and, for a claimed tag, its claimant.</summary>
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

            if (tag.ClaimedBy.Length > 0)
            {
                builder.Append("  claimed by ").Append(tag.ClaimedBy);
            }

            builder.Append(Environment.NewLine);
        }

        return builder.ToString();
    }
}
