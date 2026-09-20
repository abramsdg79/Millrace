using Dse.Core.Catalogue;
using Dse.Core.Contexts;
using Dse.Core.Faults;
using Dse.Core.Graph;

namespace Dse.Components.Mechanical;

/// <summary>
/// The pulley the drive turns: shaft speed becomes belt speed, and the force
/// the belt resists with becomes torque at the shaft. Bearing drag and belt
/// slip are its own faults, because they are genuinely its own.
/// </summary>
public sealed class DrivePulley : ComponentBase, IFaultTarget
{
    /// <summary>Extra drag at the bearing, N.</summary>
    public const string BearingFriction = "bearing-friction";

    /// <summary>The belt slips on the pulley: belt speed falls short of the surface speed.</summary>
    public const string BeltSlip = "belt-slip";

    private static readonly FaultDescriptor[] Faults =
    [
        new(BearingFriction, "Extra drag at the pulley bearing.",
            new FaultParameter("drag", "N", 0.0, "Added to the belt force while active.")),
        new(BeltSlip, "The belt slips on the pulley; belt speed is reduced, torque is not.",
            new FaultParameter("fraction", "", 0.1, "Fraction of surface speed lost, 0..1.")),
    ];

    public static ComponentDescriptor Descriptor { get; } = new(
        "drive-pulley",
        ComponentCategory.Mechanical,
        "Turns shaft speed into belt speed and belt force into a torque demand.",
        (id, p) => new DrivePulley(id, p.Double("diameterM"), p.Double("bearingDragN")))
    {
        Parameters =
        [
            Param.Double("diameterM", "Pulley diameter.", "m", min: 0.0, exclusiveMin: true),
            Param.Double("bearingDragN", "Constant drag from the bearings.", "N", @default: 0.0, min: 0.0),
        ],
        Ports =
        [
            PortSpec.In<double>("ShaftSpeed", "rad/s"),
            PortSpec.In<double>("BeltForce", "N"),
            PortSpec.Out<double>("BeltSpeed", "m/s"),
            PortSpec.Out<double>("TorqueDemand", "N·m"),
        ],
        Faults = Faults,
    };

    private double _faultDrag;
    private double _slip;

    public DrivePulley(string id, double diameterM, double bearingDragN = 0.0)
        : base(id)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(diameterM);
        ArgumentOutOfRangeException.ThrowIfNegative(bearingDragN);
        DiameterM = diameterM;
        BearingDragN = bearingDragN;

        ShaftSpeed = AddInput<double>("ShaftSpeed");
        BeltForce = AddInput<double>("BeltForce");
        BeltSpeed = AddOutput<double>("BeltSpeed");
        TorqueDemand = AddOutput<double>("TorqueDemand");
    }

    /// <summary>m.</summary>
    public double DiameterM { get; }

    /// <summary>N.</summary>
    public double BearingDragN { get; }

    /// <summary>rad/s.</summary>
    public InputPort<double> ShaftSpeed { get; }

    /// <summary>N, the belt's total resistance.</summary>
    public InputPort<double> BeltForce { get; }

    /// <summary>m/s.</summary>
    public OutputPort<double> BeltSpeed { get; }

    /// <summary>N·m at the shaft.</summary>
    public OutputPort<double> TorqueDemand { get; }

    public IReadOnlyList<FaultDescriptor> SupportedFaults => Faults;

    public override void Evaluate(in TickContext ctx)
    {
        double radius = DiameterM / 2.0;
        BeltSpeed.Value = ShaftSpeed.Value * radius * (1.0 - _slip);
        TorqueDemand.Value = (Math.Max(0.0, BeltForce.Value) + BearingDragN + _faultDrag) * radius;
    }

    public void ApplyFault(string faultId, FaultArguments arguments)
    {
        switch (faultId)
        {
            case BearingFriction:
                _faultDrag = Math.Max(0.0, arguments.Get("drag"));
                break;
            case BeltSlip:
                _slip = Math.Clamp(arguments.Get("fraction"), 0.0, 1.0);
                break;
        }
    }

    public void ClearFault(string faultId)
    {
        switch (faultId)
        {
            case BearingFriction:
                _faultDrag = 0.0;
                break;
            case BeltSlip:
                _slip = 0.0;
                break;
        }
    }
}
