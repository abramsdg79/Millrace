using Millrace.Core.Contexts;
using Millrace.Core.Graph;
using Millrace.Core.Io;

namespace Millrace.Control.Tests.Fakes;

/// <summary>
/// A tank that fills while it is commanded to and is not tripped. Small enough
/// to reason about tick by tick, and rich enough for every block: a Double to
/// alarm on, two Bools to interlock and time on, and two commands to write.
/// </summary>
public sealed class Vessel : ComponentBase, ITagProvider
{
    private readonly double _ratePerSecond;

    public Vessel(string id, double ratePerSecond)
        : base(id)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(ratePerSecond);

        _ratePerSecond = ratePerSecond;
        Fill = AddInput<bool>("Fill");
        Trip = AddInput<bool>("Trip");
        Level = AddOutput<double>("Level");
        Running = AddOutput<bool>("Running");
        Tripped = AddOutput<bool>("Tripped");
    }

    public InputPort<bool> Fill { get; }

    public InputPort<bool> Trip { get; }

    public OutputPort<double> Level { get; }

    public OutputPort<bool> Running { get; }

    public OutputPort<bool> Tripped { get; }

    public override void Evaluate(in TickContext ctx)
    {
        bool running = Fill.Value && !Trip.Value;
        Level.Value = Math.Clamp(Level.Value + (running ? _ratePerSecond * ctx.Dt : 0.0), 0.0, 100.0);
        Running.Value = running;
        Tripped.Value = Trip.Value;
    }

    public IEnumerable<TagBinding> DescribeTags() =>
    [
        TagBinding.Write("Fill", Fill, "Fill command"),
        TagBinding.Write("Trip", Trip, "Trip command"),
        TagBinding.Read("Level", Level, "%", 0.0, 100.0, "Vessel level"),
        TagBinding.Read("Running", Running, "Filling"),
        TagBinding.Read("Tripped", Tripped, "Tripped"),
    ];
}
