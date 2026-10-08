using Millrace.Core.Catalogue;
using Millrace.Core.Contexts;
using Millrace.Core.Graph;

namespace Millrace.Components.Mechanical;

/// <summary>
/// A fixed-ratio reducer. Speed passes forward live; the torque the load
/// reflects back is read one tick late (latched), which is what keeps a
/// motor–gearbox–pulley chain free of algebraic loops.
/// </summary>
public sealed class Gearbox : ComponentBase
{
    public static ComponentDescriptor Descriptor { get; } = new(
        "gearbox",
        ComponentCategory.Mechanical,
        "A fixed-ratio reducer: divides speed, multiplies torque demand back towards the motor, loses a fixed efficiency.",
        (id, p) => new Gearbox(id, p.Double("ratio"), p.Double("efficiency")))
    {
        Parameters =
        [
            Param.Double("ratio", "Input speed over output speed.", min: 0.0, exclusiveMin: true),
            Param.Double("efficiency", "Fraction of power transmitted.", @default: 0.95, min: 0.0, max: 1.0, exclusiveMin: true),
        ],
        Ports =
        [
            PortSpec.In<double>("InputSpeed", "rad/s"),
            PortSpec.In<double>("OutputTorqueDemand", "N·m", "Latched."),
            PortSpec.Out<double>("OutputSpeed", "rad/s"),
            PortSpec.Out<double>("InputTorqueDemand", "N·m"),
        ],
    };

    public Gearbox(string id, double ratio, double efficiency = 0.95)
        : base(id)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(ratio);
        if (efficiency <= 0.0 || efficiency > 1.0)
        {
            throw new ArgumentOutOfRangeException(nameof(efficiency), efficiency, "Efficiency must be in (0, 1].");
        }

        Ratio = ratio;
        Efficiency = efficiency;
        InputSpeed = AddInput<double>("InputSpeed");
        OutputTorqueDemand = AddInput<double>("OutputTorqueDemand", latched: true);
        OutputSpeed = AddOutput<double>("OutputSpeed");
        InputTorqueDemand = AddOutput<double>("InputTorqueDemand");
    }

    /// <summary>Input turns per output turn.</summary>
    public double Ratio { get; }

    public double Efficiency { get; }

    /// <summary>rad/s at the input shaft.</summary>
    public InputPort<double> InputSpeed { get; }

    /// <summary>N·m demanded at the output shaft. Latched.</summary>
    public InputPort<double> OutputTorqueDemand { get; }

    /// <summary>rad/s at the output shaft.</summary>
    public OutputPort<double> OutputSpeed { get; }

    /// <summary>N·m reflected to the input shaft.</summary>
    public OutputPort<double> InputTorqueDemand { get; }

    public override void Evaluate(in TickContext ctx)
    {
        OutputSpeed.Value = InputSpeed.Value / Ratio;
        InputTorqueDemand.Value = OutputTorqueDemand.Value / (Ratio * Efficiency);
    }
}
