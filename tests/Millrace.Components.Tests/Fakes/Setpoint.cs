using Millrace.Core.Contexts;
using Millrace.Core.Graph;

namespace Millrace.Components.Tests.Fakes;

/// <summary>An analog signal a test can change between ticks.</summary>
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
