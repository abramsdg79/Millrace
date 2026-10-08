using Millrace.Core.Contexts;
using Millrace.Core.Graph;

namespace Millrace.Core.Tests.Fakes;

/// <summary>
/// A bidirectional element: a value passes forward live, and a value coming
/// back is read one tick late. Two of these in a row would be an algebraic
/// loop if the back path were live.
/// </summary>
public sealed class Reflector : ComponentBase
{
    private readonly double _gain;

    public Reflector(string id, double gain)
        : base(id)
    {
        _gain = gain;
        Forward = AddInput<double>("Forward");
        Back = AddInput<double>("Back", latched: true);
        ForwardOut = AddOutput<double>("ForwardOut");
        BackOut = AddOutput<double>("BackOut");
    }

    public InputPort<double> Forward { get; }

    public InputPort<double> Back { get; }

    public OutputPort<double> ForwardOut { get; }

    public OutputPort<double> BackOut { get; }

    public override void Evaluate(in TickContext ctx)
    {
        ForwardOut.Value = Forward.Value * _gain;
        BackOut.Value = Back.Value / _gain;
    }
}
