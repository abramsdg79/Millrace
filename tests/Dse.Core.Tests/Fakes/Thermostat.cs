using Dse.Core.Contexts;
using Dse.Core.Graph;
using Dse.Core.Io;
using Dse.Io;

namespace Dse.Core.Tests.Fakes;

/// <summary>
/// Two writable inputs, one analog output with a quality source. Output is
/// twice the setpoint while enabled, else zero.
/// </summary>
public sealed class Thermostat : ComponentBase, ITagProvider
{
    public Thermostat(string id)
        : base(id)
    {
        Setpoint = AddInput<double>("Setpoint", defaultValue: 20.0);
        Enable = AddInput<bool>("Enable");
        Output = AddOutput<double>("Output");
        Health = AddOutput<TagQuality>("Health");
    }

    public InputPort<double> Setpoint { get; }

    public InputPort<bool> Enable { get; }

    public OutputPort<double> Output { get; }

    public OutputPort<TagQuality> Health { get; }

    /// <summary>What <see cref="Health"/> publishes next tick.</summary>
    public TagQuality Quality { get; set; } = TagQuality.Good;

    public override void Evaluate(in TickContext ctx)
    {
        Output.Value = Enable.Value ? Setpoint.Value * 2.0 : 0.0;
        Health.Value = Quality;
    }

    public IEnumerable<TagBinding> DescribeTags() =>
    [
        TagBinding.Write("Setpoint", Setpoint, "°C", 0.0, 100.0, "Temperature setpoint"),
        TagBinding.Write("Enable", Enable, "Heater enable"),
        TagBinding.Read("Output", Output, "%", 0.0, 200.0, "Heater output", Health),
    ];
}
