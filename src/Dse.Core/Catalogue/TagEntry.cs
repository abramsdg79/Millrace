using Dse.Io;

namespace Dse.Core.Catalogue;

/// <summary>
/// A tag a type declares, named relative to the component (for a composite, the
/// alias or <c>Leaf.Tag</c>). Units that depend on a parameter are left empty.
/// </summary>
public sealed record TagEntry(string Name, TagKind Kind, TagAccess Access, string Unit = "", PortRepeat? Repeat = null);
