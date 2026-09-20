using Dse.Core.Flow;

namespace Dse.Core.Catalogue;

/// <summary>A material a plant can name, with the properties a source gives it by default.</summary>
public sealed record MaterialDescriptor(MaterialType Material, MaterialProperties Properties, string Description);
