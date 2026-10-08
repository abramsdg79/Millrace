using Millrace.Core.Contexts;
using Millrace.Core.Graph;

namespace Millrace.Core.Tests.Fakes;

/// <summary>A signal source a test can change between ticks.</summary>
public sealed class Setpoint : ComponentBase
{
    public Setpoint(string id, double value = 0.0)
        : base(id)
    {
        Value = value;
        Out = AddOutput<double>("Out");
    }

    public double Value { get; set; }

    public OutputPort<double> Out { get; }

    public override void Evaluate(in TickContext ctx) => Out.Value = Value;
}
