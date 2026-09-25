using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Dse.Components;
using Dse.Control.Catalogue;
using Dse.Configuration;
using Dse.Core;
using Dse.Core.Catalogue;
using Dse.Core.Faults;
using Dse.Io;
using Dse.Scenarios;

namespace Dse.Samples.Tests;

/// <summary>
/// The mine-conveyor sample's files: read from the copy in the test output, and
/// written — only when DSE_UPDATE_GOLDEN=1 — at their source under
/// <c>samples/mine-conveyors/</c>.
/// </summary>
public static class Sample
{
    /// <summary>The eight scenarios, in the order the README tells them.</summary>
    public static IReadOnlyList<string> Names { get; } =
    [
        "normal-start-stop",
        "pull-key",
        "e-stop",
        "overload",
        "chute-blockage",
        "failed-zero-speed",
        "welded-contactor",
        "feed-starve",
    ];

    public static TheoryData<string> Scenarios => new(Names);

    /// <summary>The CLI's default catalogue: the components and the control blocks.</summary>
    public static ComponentCatalogue Catalogue { get; } =
        new CatalogueBuilder().Add<ComponentsModule>().Add<ControlModule>().Build();

    public static string Root { get; } = Path.Combine(AppContext.BaseDirectory, "mine-conveyors");

    public static string Plant => Path.Combine(Root, "plant.json");

    public static string Readme => Path.Combine(Root, "README.md");

    /// <summary>The sample's folder in the repository, found from this source file.</summary>
    public static string SourceRoot { get; } = FindSourceRoot();

    public static bool Updating => Environment.GetEnvironmentVariable("DSE_UPDATE_GOLDEN") == "1";

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

    private static ScenarioRunResult RunOnce(string name)
    {
        string path = Scenario(name);
        ScenarioParseResult parsed = ScenarioLoader.Parse(File.ReadAllText(path));
        Scenario scenario = parsed.Scenario ?? throw new InvalidOperationException($"'{name}' does not parse: {parsed.ToText()}");
        ScenarioRunResult result = ScenarioRunner.Run(scenario, File.ReadAllText(scenario.ResolvePlantPath(path)), Catalogue);
        return result.IsValid ? result : throw new InvalidOperationException($"'{name}' does not run: {result.ToText()}");
    }

    /// <summary>
    /// Runs a scenario live and samples the named tags every <paramref name="every"/>,
    /// from the first sample to the end of the run. The event log holds events
    /// only; a story told by a measured value needs this.
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<TagSample>> Trace(string name, IReadOnlyList<string> tags, TimeSpan every)
    {
        ArgumentNullException.ThrowIfNull(tags);
        string path = Scenario(name);
        Scenario scenario = ScenarioLoader.Parse(File.ReadAllText(path)).Scenario
            ?? throw new InvalidOperationException($"'{name}' does not parse.");
        LoadResult load = PlantLoader.Load(File.ReadAllText(scenario.ResolvePlantPath(path)), Catalogue, scenario.ToLoadOptions());
        Simulation simulation = load.Builder?.Build() ?? throw new InvalidOperationException(load.ToText());
        foreach (ScenarioAction action in scenario.Timeline)
        {
            Schedule(simulation, action);
        }

        TimeSpan step = load.Options!.TimeStep;
        long ticks = scenario.Duration.Ticks / step.Ticks;
        if (every < step || every.Ticks % step.Ticks != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(every), every, $"Sample every whole number of {step.TotalMilliseconds} ms steps, at least one.");
        }

        long stride = every.Ticks / step.Ticks;
        var traces = tags.ToDictionary(t => t, _ => new List<TagSample>(), StringComparer.Ordinal);
        for (long tick = 1; tick <= ticks; tick++)
        {
            simulation.Tick();
            if (tick % stride == 0)
            {
                TimeSpan time = TimeSpan.FromTicks(step.Ticks * tick);
                foreach (string tag in tags)
                {
                    traces[tag].Add(new TagSample(time, simulation.IO.Read(tag).AsDouble));
                }
            }
        }

        return traces.ToDictionary(p => p.Key, p => (IReadOnlyList<TagSample>)p.Value, StringComparer.Ordinal);
    }

    /// <summary>Schedules one scenario action on a live simulation, the way <c>ScenarioRunner</c> does, so a recorder sees it.</summary>
    public static void Schedule(Simulation simulation, ScenarioAction action)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        switch (action)
        {
            case WriteAction write:
                TagKind kind = simulation.IO.Directory.Find(write.Tag).Kind;
                TagValue value = write.Value.ToTagValue(kind)
                    ?? throw new InvalidOperationException($"'{write.Tag}' cannot take {write.Value}.");
                simulation.WriteAt(write.At, write.Tag, value);
                break;
            case FaultAction fault:
                simulation.InjectFaultAt(fault.At, fault.ComponentId, fault.FaultId, new FaultArguments(fault.Arguments.ToArray()));
                break;
            case ClearAction clear:
                simulation.ClearFaultAt(clear.At, clear.ComponentId, clear.FaultId);
                break;
            default:
                throw new InvalidOperationException($"'{action?.GetType().Name}' is not a scenario action.");
        }
    }

    private static string FindSourceRoot([CallerFilePath] string thisFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", "..", "samples", "mine-conveyors"));
}
