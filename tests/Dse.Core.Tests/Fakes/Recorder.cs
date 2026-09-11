using Dse.Core.Contexts;
using Dse.Core.Graph;

namespace Dse.Core.Tests.Fakes;

/// <summary>Captures every value it sees, so a test can compare whole runs.</summary>
public sealed class Recorder : ComponentBase
{
    private readonly List<double> _samples = [];

    public Recorder(string id)
        : base(id) => In = AddInput<double>("In");

    public InputPort<double> In { get; }

    public IReadOnlyList<double> Samples => _samples;

    public override void Evaluate(in TickContext ctx) => _samples.Add(In.Value);
}
