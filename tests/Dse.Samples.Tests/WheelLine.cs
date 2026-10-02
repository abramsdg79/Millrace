using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Dse.Configuration;
using Dse.Core;
using Dse.Core.Graph;
using Dse.Scenarios;

namespace Dse.Samples.Tests;

/// <summary>
/// The wheel-line sample's files: read from the copy in the test output, and
/// written — only when DSE_UPDATE_GOLDEN=1 — at their source under
/// <c>samples/wheel-line/</c>. The catalogue, the update switch and the way a
/// scenario action is scheduled are the mine-conveyor sample's, from
/// <see cref="Sample"/>.
/// </summary>
public static class WheelLine
{
    /// <summary>The six scenarios, in the order the README tells them.</summary>
    public static IReadOnlyList<string> Names { get; } =
    [
        "normal-run",
        "slow-press",
        "stuck-kicker",
        "press-jam",
        "zone-low",
        "pyro-fail-high",
    ];

    public static TheoryData<string> Scenarios => new(Names);

    public static string Root { get; } = Path.Combine(AppContext.BaseDirectory, "wheel-line");

    public static string Plant => Path.Combine(Root, "plant.json");

    public static string Readme => Path.Combine(Root, "README.md");

    /// <summary>The sample's folder in the repository, found from this source file.</summary>
    public static string SourceRoot { get; } = FindSourceRoot();

    public static string Scenario(string name) => Path.Combine(Root, "scenarios", name + ".json");

    public static string Golden(string name) => Path.Combine(Root, "expected", name + ".log");

    public static string SourceGolden(string name) => Path.Combine(SourceRoot, "expected", name + ".log");

    private static readonly ConcurrentDictionary<string, Lazy<ScenarioRunResult>> Runs = new(StringComparer.Ordinal);

    /// <summary>
    /// A scenario's run through the same runner <c>dse run</c> uses, once per test
    /// process: a run is deterministic, so every test that reads it shares it.
    /// </summary>
    public static ScenarioRunResult Run(string name) =>
        Runs.GetOrAdd(name, n => new Lazy<ScenarioRunResult>(() => RunOnce(n))).Value;

    /// <summary>
    /// Runs a scenario live, to its full duration, and calls
    /// <paramref name="afterEachTick"/> after every tick. The event log holds
    /// events only; a story told by item temperatures, ids or a belt's count needs
    /// this. Returns the simulation as the run left it.
    /// </summary>
    public static Simulation Watch(string name, Action<Simulation> afterEachTick)
    {
        ArgumentNullException.ThrowIfNull(afterEachTick);
        string path = Scenario(name);
        Scenario scenario = ScenarioLoader.Parse(File.ReadAllText(path)).Scenario
            ?? throw new InvalidOperationException($"'{name}' does not parse.");
        LoadResult load = PlantLoader.Load(File.ReadAllText(scenario.ResolvePlantPath(path)), Sample.Catalogue, scenario.ToLoadOptions());
        Simulation simulation = load.Builder?.Build() ?? throw new InvalidOperationException(load.ToText());
        foreach (ScenarioAction action in scenario.Timeline)
        {
            Sample.Schedule(simulation, action);
        }

        long ticks = scenario.Duration.Ticks / load.Options!.TimeStep.Ticks;
        for (long tick = 1; tick <= ticks; tick++)
        {
            simulation.Tick();
            afterEachTick(simulation);
        }

        return simulation;
    }

    /// <summary>The leaf with this id, as the type the test needs.</summary>
    public static T Component<T>(Simulation simulation, string id)
        where T : class, ISimComponent
    {
        ArgumentNullException.ThrowIfNull(simulation);
        return simulation.Components.OfType<T>().Single(c => string.Equals(c.Id, id, StringComparison.Ordinal));
    }

    private static ScenarioRunResult RunOnce(string name)
    {
        string path = Scenario(name);
        ScenarioParseResult parsed = ScenarioLoader.Parse(File.ReadAllText(path));
        Scenario scenario = parsed.Scenario ?? throw new InvalidOperationException($"'{name}' does not parse: {parsed.ToText()}");
        ScenarioRunResult result = ScenarioRunner.Run(scenario, File.ReadAllText(scenario.ResolvePlantPath(path)), Sample.Catalogue);
        return result.IsValid ? result : throw new InvalidOperationException($"'{name}' does not run: {result.ToText()}");
    }

    private static string FindSourceRoot([CallerFilePath] string thisFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", "..", "samples", "wheel-line"));
}
