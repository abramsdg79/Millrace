using Millrace.Core.Flow;

namespace Millrace.Components.Flow;

/// <summary>One ingredient of a bulk recipe: the inlet it arrives on, what it is, how much.</summary>
/// <param name="InletName">The inlet's port name on the unit.</param>
/// <param name="Material">Must be a Bulk material.</param>
/// <param name="MassKg">kg per batch.</param>
public sealed record RecipeLine(string InletName, MaterialType Material, double MassKg);
