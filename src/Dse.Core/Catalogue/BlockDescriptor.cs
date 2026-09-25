using Dse.Io;

namespace Dse.Core.Catalogue;

/// <summary>
/// Everything a tool or an agent needs to know about a control block type: its
/// parameters, the tags an instance will own, and the factory that builds one.
/// Hand-written beside the module that registers it; <c>CatalogueConformance</c>
/// keeps it honest.
/// </summary>
public sealed class BlockDescriptor
{
    public BlockDescriptor(
        string type,
        string description,
        Func<string, ParameterValues, IReadOnlyList<(TagSpec Spec, TagAccess Access)>> ownedTags,
        Func<string, TimeSpan, ParameterValues, IScanBlock> factory)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ArgumentNullException.ThrowIfNull(ownedTags);
        ArgumentNullException.ThrowIfNull(factory);
        Type = type;
        Description = description;
        OwnedTags = ownedTags;
        Factory = factory;
    }

    /// <summary>Kebab-case, unique among components and blocks: <c>interlock</c>.</summary>
    public string Type { get; }

    public string Description { get; }

    /// <summary>
    /// The tags a block of this type with this id and these parameters will own,
    /// by full name (<c>INT01.Ok</c>): outputs <see cref="TagAccess.ReadOnly"/>,
    /// commands <see cref="TagAccess.ReadWrite"/>. A function of the parameters
    /// alone, called before any block exists, with the parameters bound for
    /// checking: scalars, enums, strings, groups and tag names are final, but
    /// values are provisional and object parameters are not yet built, so it must
    /// read neither.
    /// </summary>
    public Func<string, ParameterValues, IReadOnlyList<(TagSpec Spec, TagAccess Access)>> OwnedTags { get; }

    /// <summary>Builds a block from its id, its scan period and its resolved parameters.</summary>
    public Func<string, TimeSpan, ParameterValues, IScanBlock> Factory { get; }

    public IReadOnlyList<ParameterDescriptor> Parameters { get; init; } = [];
}
