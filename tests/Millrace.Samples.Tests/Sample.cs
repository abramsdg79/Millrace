using Millrace.Components;
using Millrace.Control.Catalogue;
using Millrace.Core;
using Millrace.Core.Catalogue;
using Millrace.Core.Faults;
using Millrace.Io;
using Millrace.Scenarios;

namespace Millrace.Samples.Tests;

/// <summary>
/// The mine-conveyor sample's files: read from the copy in the test output, and
/// written — only when MILLRACE_UPDATE_GOLDEN=1 — at their source under
/// <c>samples/mine-conveyors/</c>. Paths, cached runs and live loads come from
/// the shared <see cref="SampleFolder"/>; the catalogue, the update switch and
/// the way a scenario action is scheduled are here, and both samples use them.
/// </summary>
public static class Sample
{
    private static readonly SampleFolder Folder = new("mine-conveyors");

    /// <summary>The nine scenarios, in the order the README tells them.</summary>
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
        "start-while-tripped",
    ];

    public static TheoryData<string> Scenarios => new(Names);

    /// <summary>The CLI's default catalogue: the components and the control blocks.</summary>
    public static ComponentCatalogue Catalogue { get; } =
        new CatalogueBuilder().Add<ComponentsModule>().Add<ControlModule>().Build();

    public static string Root => Folder.Root;

    public static string Plant => Folder.Plant;

    public static string Readme => Folder.Readme;

    /// <summary>The sample's folder in the repository, found from the loader's source file.</summary>
    public static string SourceRoot => Folder.SourceRoot;

    public static bool Updating => Environment.GetEnvironmentVariable("MILLRACE_UPDATE_GOLDEN") == "1";

    public static string Scenario(string name) => Folder.Scenario(name);

    public static string Golden(string name) => Folder.Golden(name);

    public static string SourceGolden(string name) => Folder.SourceGolden(name);

    /// <summary>
    /// A scenario's run through the same runner <c>millrace run</c> uses, once per test
    /// process: a run is deterministic, so every test that reads it shares it.
    /// </summary>
    public static ScenarioRunResult Run(string name) => Folder.Run(name);

    /// <summary>
    /// Runs a scenario live and samples the named tags every <paramref name="every"/>,
    /// from the first sample to the end of the run. The event log holds events
    /// only; a story told by a measured value needs this.
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<TagSample>> Trace(string name, IReadOnlyList<string> tags, TimeSpan every)
    {
        ArgumentNullException.ThrowIfNull(tags);
        LiveScenario live = Folder.Load(name);
        TimeSpan step = live.Step;
        if (every < step || every.Ticks % step.Ticks != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(every), every, $"Sample every whole number of {step.TotalMilliseconds} ms steps, at least one.");
        }

        long stride = every.Ticks / step.Ticks;
        var traces = tags.ToDictionary(t => t, _ => new List<TagSample>(), StringComparer.Ordinal);
        for (long tick = 1; tick <= live.Ticks; tick++)
        {
            live.Simulation.Tick();
            if (tick % stride == 0)
            {
                TimeSpan time = TimeSpan.FromTicks(step.Ticks * tick);
                foreach (string tag in tags)
                {
                    traces[tag].Add(new TagSample(time, live.Simulation.IO.Read(tag).AsDouble));
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
}
