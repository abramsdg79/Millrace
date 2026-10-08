using Millrace.Core.Contexts;
using Millrace.Core.Graph;
using Millrace.Core.Randomness;

namespace Millrace.Core.Tests.Fakes;

/// <summary>Emits one draw from its own random stream per tick.</summary>
public sealed class NoiseSource : ComponentBase
{
    private readonly List<double> _samples = [];
    private DeterministicRandom? _random;

    public NoiseSource(string id)
        : base(id) => Out = AddOutput<double>("Out");

    public OutputPort<double> Out { get; }

    public IReadOnlyList<double> Samples => _samples;

    public override void Initialize(in InitContext ctx) => _random = ctx.Random;

    public override void Evaluate(in TickContext ctx)
    {
        double value = _random!.NextDouble();
        Out.Value = value;
        _samples.Add(value);
    }
}
