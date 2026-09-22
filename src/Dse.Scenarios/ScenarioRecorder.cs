using Dse.Core;
using Dse.Core.Faults;
using Dse.Core.Time;
using Dse.Io;

namespace Dse.Scenarios;

/// <summary>
/// Accumulates every action that took effect, in landing order, and turns the
/// lot into a <see cref="Scenario"/>. Attach it with
/// <c>Simulation.AttachActionRecorder</c> and a live run — a command bus, an
/// operator, a test — becomes a file that replays to the same event log.
/// </summary>
/// <remarks>
/// Called on the tick thread only, during phase 1: not safe to call from another thread.
/// </remarks>
public sealed class ScenarioRecorder : IActionRecorder
{
    private readonly List<Recorded> _actions = [];

    /// <summary>Actions recorded so far.</summary>
    public int Count => _actions.Count;

    /// <inheritdoc/>
    public void Wrote(long tick, string tag, TagValue value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);
        _actions.Add(new Recorded(Kind.Write, tick, tag, string.Empty, From(value), []));
    }

    /// <inheritdoc/>
    public void Faulted(long tick, string componentId, string faultId, FaultArguments arguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(componentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(faultId);
        ArgumentNullException.ThrowIfNull(arguments);

        var copied = new FaultArgument[arguments.Count];
        for (int i = 0; i < copied.Length; i++)
        {
            copied[i] = arguments[i];
        }

        _actions.Add(new Recorded(Kind.Fault, tick, componentId, faultId, null, copied));
    }

    /// <inheritdoc/>
    public void Cleared(long tick, string componentId, string faultId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(componentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(faultId);
        _actions.Add(new Recorded(Kind.Clear, tick, componentId, faultId, null, []));
    }

    /// <summary>
    /// The recording as a scenario over <paramref name="plantPath"/>. The
    /// overrides are the run's actual options, so a replay cannot inherit a
    /// different default; each action's time is its tick times the step.
    /// </summary>
    public Scenario ToScenario(string plantPath, SimulationOptions options, TimeSpan duration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(plantPath);
        ArgumentNullException.ThrowIfNull(options);

        var timeline = new List<ScenarioAction>(_actions.Count);
        foreach (Recorded recorded in _actions)
        {
            TimeSpan at = TimeSpan.FromTicks(options.TimeStep.Ticks * recorded.Tick);
            timeline.Add(recorded.Action switch
            {
                Kind.Write => new WriteAction(at, recorded.Target, recorded.Value!),
                Kind.Fault => new FaultAction(at, recorded.Target, recorded.FaultId, recorded.Arguments),
                _ => new ClearAction(at, recorded.Target, recorded.FaultId),
            });
        }

        return new Scenario(plantPath, options.Seed, options.StartTime, options.TimeStep, duration, timeline);
    }

    private static ScenarioValue From(TagValue value) => value.Kind switch
    {
        TagKind.Bool => ScenarioValue.OfBool(value.AsBool),
        TagKind.Int64 => ScenarioValue.OfInteger(value.AsInt64),
        _ => ScenarioValue.OfNumber(value.AsDouble),
    };

    private enum Kind
    {
        Write,
        Fault,
        Clear,
    }

    private sealed record Recorded(
        Kind Action,
        long Tick,
        string Target,
        string FaultId,
        ScenarioValue? Value,
        IReadOnlyList<FaultArgument> Arguments);
}
