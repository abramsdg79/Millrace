using Dse.Core.Contexts;
using Dse.Core.Faults;
using Dse.Core.Graph;

namespace Dse.Components.Mechanical;

/// <summary>An idler pulley. What it contributes is drag, and a worn bearing adds to it.</summary>
public sealed class TailPulley : ComponentBase, IFaultTarget
{
    /// <summary>Extra drag at the bearing, N.</summary>
    public const string BearingFriction = "bearing-friction";

    private static readonly FaultDescriptor[] Faults =
    [
        new(BearingFriction, "Extra drag at the pulley bearing.",
            new FaultParameter("drag", "N", 0.0, "Added to the pulley's drag while active.")),
    ];

    private double _faultDrag;

    public TailPulley(string id, double bearingDragN)
        : base(id)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bearingDragN);
        BearingDragN = bearingDragN;
        Drag = AddOutput<double>("Drag");
    }

    /// <summary>N.</summary>
    public double BearingDragN { get; }

    /// <summary>N, the drag this pulley adds to the belt.</summary>
    public OutputPort<double> Drag { get; }

    public IReadOnlyList<FaultDescriptor> SupportedFaults => Faults;

    public override void Evaluate(in TickContext ctx) => Drag.Value = BearingDragN + _faultDrag;

    public void ApplyFault(string faultId, FaultArguments arguments) =>
        _faultDrag = Math.Max(0.0, arguments.Get("drag"));

    public void ClearFault(string faultId) => _faultDrag = 0.0;
}
