using Dse.Core.Graph;

namespace Dse.Core.Tests.Fakes;

/// <summary>Two thermostats; exposes A's setpoint as SP and B's output as Out.</summary>
public sealed class Pair : CompositeComponent
{
    public Pair(string id)
        : base(id)
    {
        A = AddChild(new Thermostat("A"));
        B = AddChild(new Thermostat("B"));
        Expose("SP", A.Setpoint);
        Expose("Out", B.Output);
    }

    public Thermostat A { get; }

    public Thermostat B { get; }
}
