using Millrace.Configuration;

namespace Millrace.Scenarios;

/// <summary>
/// A run, as a file describes it: which plant, how the engine is set up, how
/// long, and what happens. Immutable and pure — resolving the plant path is the
/// only thing here that knows the file system exists, and it only does string
/// arithmetic.
/// </summary>
/// <param name="PlantPath">The plant file, relative to the scenario file that named it, or absolute.</param>
/// <param name="Seed">Overrides the plant's <c>defaults.seed</c>.</param>
/// <param name="StartTime">Overrides the plant's <c>defaults.startTime</c>.</param>
/// <param name="TimeStep">Overrides the plant's <c>defaults.timeStepMs</c>.</param>
/// <param name="Duration">How long to run, from the start. A whole number of ticks.</param>
/// <param name="Timeline">The actions, in file order. Actions on one tick fire in this order.</param>
public sealed record Scenario(
    string PlantPath,
    ulong? Seed,
    DateTimeOffset? StartTime,
    TimeSpan? TimeStep,
    TimeSpan Duration,
    IReadOnlyList<ScenarioAction> Timeline)
{
    /// <summary>
    /// The plant's absolute path. A scenario is a sibling of its plant, so a
    /// directory that moves keeps working.
    /// </summary>
    public string ResolvePlantPath(string scenarioFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scenarioFilePath);
        string directory = Path.GetDirectoryName(Path.GetFullPath(scenarioFilePath)) ?? string.Empty;
        return Path.GetFullPath(Path.Combine(directory, PlantPath));
    }

    /// <summary>The overrides, for <c>PlantLoader.Load</c>. Absent here means the plant decides.</summary>
    public LoadOptions ToLoadOptions() => new()
    {
        Seed = Seed,
        StartTime = StartTime,
        TimeStep = TimeStep,
    };
}
