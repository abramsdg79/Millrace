namespace Dse.Core.Flow;

/// <summary>
/// Ambient conditions a node hands to its transforms, taken from the node's
/// own signal inputs. Add a field when a transform needs one; nodes that do
/// not drive it pass the default.
/// </summary>
/// <param name="AmbientTemperature">°C.</param>
public readonly record struct TransformContext(double AmbientTemperature);
