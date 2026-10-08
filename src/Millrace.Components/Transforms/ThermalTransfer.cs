using Millrace.Core.Flow;

namespace Millrace.Components.Transforms;

/// <summary>
/// Lumped-capacitance heat transfer toward the ambient temperature with one
/// time constant. Zero snaps the material to ambient. This is the transform
/// that turns a belt into a band oven or a cooling conveyor.
/// </summary>
public sealed class ThermalTransfer : IMaterialTransform
{
    public ThermalTransfer(double timeConstantSeconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(timeConstantSeconds);
        TimeConstantSeconds = timeConstantSeconds;
    }

    /// <summary>s.</summary>
    public double TimeConstantSeconds { get; }

    public void Apply(ref MaterialProperties properties, Span<double> state, double dt, in TransformContext context)
    {
        double fraction = TimeConstantSeconds <= 0.0 ? 1.0 : Math.Min(1.0, dt / TimeConstantSeconds);
        properties = properties with
        {
            Temperature = properties.Temperature + ((context.AmbientTemperature - properties.Temperature) * fraction),
        };
    }
}
