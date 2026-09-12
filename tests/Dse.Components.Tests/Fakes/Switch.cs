using Dse.Core.Contexts;
using Dse.Core.Graph;

namespace Dse.Components.Tests.Fakes;

/// <summary>A discrete signal a test can flip between ticks.</summary>
public sealed class Switch : ComponentBase
{
    public Switch(string id, bool value = false)
        : base(id)
    {
        Value = value;
        Out = AddOutput<bool>("Out");
    }

    public bool Value { get; set; }

    public OutputPort<bool> Out { get; }

    public override void Evaluate(in TickContext ctx) => Out.Value = Value;
}
