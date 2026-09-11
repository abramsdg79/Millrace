using Dse.Core.Contexts;
using Dse.Core.Graph;
using Dse.Core.Telemetry;

namespace Dse.Core.Tests.Fakes;

/// <summary>Multiplies its input by a constant and publishes the result as telemetry.</summary>
public sealed class Gain : ComponentBase
{
    private readonly double _factor;
    private TelemetryHandle _outTelemetry;

    public Gain(string id, double factor)
        : base(id)
    {
        _factor = factor;
        In = AddInput<double>("In");
        Out = AddOutput<double>("Out");
    }

    public InputPort<double> In { get; }

    public OutputPort<double> Out { get; }

    public override void Initialize(in InitContext ctx) =>
        _outTelemetry = ctx.RegisterTelemetry("Out", "1");

    public override void Evaluate(in TickContext ctx)
    {
        Out.Value = In.Value * _factor;
        _outTelemetry.Write(Out.Value);
    }
}
