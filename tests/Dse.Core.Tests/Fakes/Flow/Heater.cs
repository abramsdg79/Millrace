using Dse.Core.Flow;

namespace Dse.Core.Tests.Fakes.Flow;

/// <summary>Moves temperature toward ambient at a fixed fraction per second. Ignores state.</summary>
public sealed class Heater : IMaterialTransform
{
    private readonly double _ratePerSecond;

    public Heater(double ratePerSecond) => _ratePerSecond = ratePerSecond;

    public void Apply(ref MaterialProperties properties, Span<double> state, double dt, in TransformContext context) =>
        properties = properties with
        {
            Temperature = properties.Temperature
                + ((context.AmbientTemperature - properties.Temperature) * _ratePerSecond * dt),
        };
}
