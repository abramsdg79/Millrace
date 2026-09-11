using Dse.Core.Contexts;
using Dse.Core.Graph;

namespace Dse.Core.Tests.Fakes;

/// <summary>Rectangular integration of its input over simulation time.</summary>
public sealed class Integrator : ComponentBase
{
    private double _accumulated;

    public Integrator(string id)
        : base(id)
    {
        In = AddInput<double>("In");
        Out = AddOutput<double>("Out");
    }

    public InputPort<double> In { get; }

    public OutputPort<double> Out { get; }

    public override void Evaluate(in TickContext ctx)
    {
        _accumulated += In.Value * ctx.Dt;
        Out.Value = _accumulated;
    }
}
