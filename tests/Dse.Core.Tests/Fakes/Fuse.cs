using Dse.Core.Contexts;
using Dse.Core.Faults;
using Dse.Core.Graph;

namespace Dse.Core.Tests.Fakes;

/// <summary>Conducts until blown. The one fault has one parameter so resolution can be tested.</summary>
public sealed class Fuse : ComponentBase, IFaultTarget
{
    public const string Blow = "blow";

    private static readonly FaultDescriptor[] Faults =
    [
        new(Blow, "Opens the fuse.", new FaultParameter("resistance", "ohm", 1.0e6, "Resistance once open.")),
    ];

    private bool _blown;

    public Fuse(string id)
        : base(id)
    {
        Ok = AddOutput<bool>("Ok");
        Resistance = AddOutput<double>("Resistance");
    }

    public OutputPort<bool> Ok { get; }

    public OutputPort<double> Resistance { get; }

    public IReadOnlyList<FaultDescriptor> SupportedFaults => Faults;

    public FaultArguments? LastArguments { get; private set; }

    public void ApplyFault(string faultId, FaultArguments arguments)
    {
        _blown = true;
        LastArguments = arguments;
        Resistance.Value = arguments.Get("resistance");
    }

    public void ClearFault(string faultId)
    {
        _blown = false;
        Resistance.Value = 0.0;
    }

    public override void Evaluate(in TickContext ctx) => Ok.Value = !_blown;
}
