using Dse.Core.Logging;

namespace Dse.Core.Contexts;

/// <summary>What a component is given on every tick.</summary>
public readonly struct TickContext
{
    private readonly EventLog _log;

    public TickContext(long tick, double dt, DateTimeOffset simTime, EventLog log)
    {
        ArgumentNullException.ThrowIfNull(log);

        Tick = tick;
        Dt = dt;
        SimTime = simTime;
        _log = log;
    }

    public long Tick { get; }

    /// <summary>The time step in seconds.</summary>
    public double Dt { get; }

    public DateTimeOffset SimTime { get; }

    /// <summary>Records a discrete event. Pass the component's own id as the source.</summary>
    public void Log(string source, string code, string message) =>
        _log.Record(Tick, SimTime, source, code, message);
}
