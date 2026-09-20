using Dse.Core.Catalogue;
using Dse.Core.Contexts;
using Dse.Core.Graph;

namespace Dse.Components.Mechanical;

/// <summary>
/// The force it takes to move a loaded belt: rolling friction on the belt's
/// own mass plus the load, plus whatever drag the idlers add. This is the
/// link from "material accumulated" to "motor works harder".
/// </summary>
public sealed class BeltFriction : ComponentBase
{
    private const double Gravity = 9.80665;

    public static ComponentDescriptor Descriptor { get; } = new(
        "belt-friction",
        ComponentCategory.Mechanical,
        "Rolling resistance of a loaded belt: force from carried mass, belt mass and added drag.",
        (id, p) => new BeltFriction(id, p.Double("emptyBeltMassKg"), p.Double("frictionCoefficient")))
    {
        Parameters =
        [
            Param.Double("emptyBeltMassKg", "Mass of the moving belt and idlers with no load.", "kg", min: 0.0),
            Param.Double("frictionCoefficient", "Rolling resistance coefficient.", @default: 0.03, min: 0.0),
        ],
        Ports =
        [
            PortSpec.In<double>("Load", "kg", "Mass on the belt."),
            PortSpec.In<double>("Drag", "N", "Additional drag, such as the tail pulley's."),
            PortSpec.Out<double>("Force", "N"),
        ],
    };

    public BeltFriction(string id, double emptyBeltMassKg, double frictionCoefficient = 0.03)
        : base(id)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(emptyBeltMassKg);
        ArgumentOutOfRangeException.ThrowIfNegative(frictionCoefficient);
        EmptyBeltMassKg = emptyBeltMassKg;
        FrictionCoefficient = frictionCoefficient;

        Load = AddInput<double>("Load");
        Drag = AddInput<double>("Drag");
        Force = AddOutput<double>("Force");
    }

    /// <summary>kg, belt and moving parts with no material on it.</summary>
    public double EmptyBeltMassKg { get; }

    /// <summary>Rolling friction coefficient, dimensionless.</summary>
    public double FrictionCoefficient { get; }

    /// <summary>kg of material on the belt.</summary>
    public InputPort<double> Load { get; }

    /// <summary>N of extra drag, from idlers. Unconnected reads zero.</summary>
    public InputPort<double> Drag { get; }

    /// <summary>N.</summary>
    public OutputPort<double> Force { get; }

    public override void Evaluate(in TickContext ctx) =>
        Force.Value = (FrictionCoefficient * Gravity * (EmptyBeltMassKg + Math.Max(0.0, Load.Value))) + Math.Max(0.0, Drag.Value);
}
