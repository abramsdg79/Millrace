using Dse.Core.Contexts;
using Dse.Core.Graph;

namespace Dse.Components.Mechanical;

/// <summary>
/// A fixed-ratio reducer. Speed passes forward live; the torque the load
/// reflects back is read one tick late (latched), which is what keeps a
/// motor–gearbox–pulley chain free of algebraic loops.
/// </summary>
public sealed class Gearbox : ComponentBase
{
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
