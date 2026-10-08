using System.Globalization;
using Millrace.Core.Contexts;
using Millrace.Core.Graph;

namespace Millrace.Core.Tests.Fakes;

/// <summary>Logs a one-shot event the first time its input exceeds a threshold.</summary>
public sealed class Tripper : ComponentBase
{
    private readonly double _threshold;
    private bool _tripped;

    public Tripper(string id, double threshold)
        : base(id)
    {
        _threshold = threshold;
        In = AddInput<double>("In", defaultValue: 0.0, required: true);
        Tripped = AddOutput<bool>("Tripped");
    }

    public InputPort<double> In { get; }

    public OutputPort<bool> Tripped { get; }

    public override void Evaluate(in TickContext ctx)
    {
        if (!_tripped && In.Value > _threshold)
        {
            _tripped = true;
            ctx.Log(Id, "TRIP", $"Input exceeded {_threshold.ToString(CultureInfo.InvariantCulture)}.");
        }

        Tripped.Value = _tripped;
    }
}
