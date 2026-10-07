using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Dse.Configuration;
using Dse.Core;
using Dse.Scenarios;

namespace Dse.Samples.Tests;

/// <summary>
/// One sample folder, the loader both sample helpers share (plan 7, R190): its
/// files, read from the copy in the test output and written — only when
/// DSE_UPDATE_GOLDEN=1 — at their source under <c>samples/&lt;folder&gt;/</c>;
/// its scenarios' runs, cached; and a scenario loaded live with its timeline
/// scheduled. <see cref="Sample"/> holds the mine conveyors' and
/// <see cref="WheelLine"/> the wheel line's.
/// </summary>
public sealed class SampleFolder
{
    private readonly ConcurrentDictionary<string, Lazy<ScenarioRunResult>> _runs = new(StringComparer.Ordinal);

    /// <summary>The sample under <c>samples/<paramref name="folder"/>/</c>, copied to <c><paramref name="folder"/>/</c> in the test output.</summary>
    public SampleFolder(string folder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        Folder = folder;
        Root = Path.Combine(AppContext.BaseDirectory, folder);
        SourceRoot = FindSourceRoot(folder);
    }

    /// <summary>The folder's name, the same under <c>samples/</c> and in the test output.</summary>
    public string Folder { get; }

    /// <summary>The copy in the test output, which the tests read.</summary>
    public string Root { get; }

    public string Plant => Path.Combine(Root, "plant.json");

    public string Readme => Path.Combine(Root, "README.md");

    /// <summary>The sample's folder in the repository, found from this source file.</summary>
    public string SourceRoot { get; }

    public string Scenario(string name) => Path.Combine(Root, "scenarios", name + ".json");

    public string Golden(string name) => Path.Combine(Root, "expected", name + ".log");

    public string SourceGolden(string name) => Path.Combine(SourceRoot, "expected", name + ".log");

    /// <summary>
    /// A scenario's run through the same runner <c>dse run</c> uses, once per test
    /// process: a run is deterministic, so every test that reads it shares it.
    /// </summary>
    public ScenarioRunResult Run(string name) =>
        _runs.GetOrAdd(name, n => new Lazy<ScenarioRunResult>(() => RunOnce(n))).Value;

    /// <summary>
    /// A scenario loaded live: its plant built with the scenario's load options and
    /// every timeline action scheduled through <see cref="Sample.Schedule"/>, not yet
    /// ticked. The caller ticks it <see cref="LiveScenario.Ticks"/> times to run it
    /// to its full duration.
    /// </summary>
    public LiveScenario Load(string name)
    {
        string path = Scenario(name);
        ScenarioParseResult parsed = ScenarioLoader.Parse(File.ReadAllText(path));
        Scenario scenario = parsed.Scenario ?? throw new InvalidOperationException($"'{name}' does not parse: {parsed.ToText()}");
        LoadResult load = PlantLoader.Load(File.ReadAllText(scenario.ResolvePlantPath(path)), Sample.Catalogue, scenario.ToLoadOptions());
        Simulation simulation = load.Builder?.Build() ?? throw new InvalidOperationException(load.ToText());
        foreach (ScenarioAction action in scenario.Timeline)
        {
            Sample.Schedule(simulation, action);
        }

        TimeSpan step = load.Options!.TimeStep;
        return new LiveScenario(simulation, step, scenario.Duration.Ticks / step.Ticks);
    }

    private ScenarioRunResult RunOnce(string name)
    {
        string path = Scenario(name);
        ScenarioParseResult parsed = ScenarioLoader.Parse(File.ReadAllText(path));
        Scenario scenario = parsed.Scenario ?? throw new InvalidOperationException($"'{name}' does not parse: {parsed.ToText()}");
        ScenarioRunResult result = ScenarioRunner.Run(scenario, File.ReadAllText(scenario.ResolvePlantPath(path)), Sample.Catalogue);
        return result.IsValid ? result : throw new InvalidOperationException($"'{name}' does not run: {result.ToText()}");
    }

    private static string FindSourceRoot(string folder, [CallerFilePath] string thisFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", "..", "samples", folder));
}

/// <summary>A scenario's simulation, built and scheduled, with its step and the number of ticks its duration takes.</summary>
public sealed record LiveScenario(Simulation Simulation, TimeSpan Step, long Ticks);
