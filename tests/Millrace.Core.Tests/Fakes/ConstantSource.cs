using Millrace.Core.Contexts;
using Millrace.Core.Graph;

namespace Millrace.Core.Tests.Fakes;

/// <summary>Emits a fixed value every tick.</summary>
public sealed class ConstantSource : ComponentBase
{
    private readonly double _value;

    public ConstantSource(string id, double value)
        : base(id)
    {
        _value = value;
        Out = AddOutput<double>("Out");
    }

    public OutputPort<double> Out { get; }

    public override void Evaluate(in TickContext ctx) => Out.Value = _value;
}
