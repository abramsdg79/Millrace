using Dse.Core;
using Dse.Core.Graph;
using Dse.Scenarios;

namespace Dse.Samples.Tests;

/// <summary>
/// The wheel-line sample's files: read from the copy in the test output, and
/// written — only when DSE_UPDATE_GOLDEN=1 — at their source under
/// <c>samples/wheel-line/</c>. Paths, cached runs and live loads come from the
/// shared <see cref="SampleFolder"/>; the catalogue, the update switch and the
/// way a scenario action is scheduled are the mine-conveyor sample's, from
/// <see cref="Sample"/>.
/// </summary>
public static class WheelLine
{
    private static readonly SampleFolder Folder = new("wheel-line");

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

    public static string Root => Folder.Root;

    public static string Plant => Folder.Plant;

    public static string Readme => Folder.Readme;

    /// <summary>The sample's folder in the repository, found from the loader's source file.</summary>
    public static string SourceRoot => Folder.SourceRoot;

    public static string Scenario(string name) => Folder.Scenario(name);

    public static string Golden(string name) => Folder.Golden(name);

    public static string SourceGolden(string name) => Folder.SourceGolden(name);

    /// <summary>
    /// A scenario's run through the same runner <c>dse run</c> uses, once per test
    /// process: a run is deterministic, so every test that reads it shares it.
    /// </summary>
    public static ScenarioRunResult Run(string name) => Folder.Run(name);

    /// <summary>
    /// Runs a scenario live, to its full duration, and calls
    /// <paramref name="afterEachTick"/> after every tick. The event log holds
    /// events only; a story told by item temperatures, ids or a belt's count needs
    /// this. Returns the simulation as the run left it.
    /// </summary>
    public static Simulation Watch(string name, Action<Simulation> afterEachTick)
    {
        ArgumentNullException.ThrowIfNull(afterEachTick);
        LiveScenario live = Folder.Load(name);
        for (long tick = 1; tick <= live.Ticks; tick++)
        {
            live.Simulation.Tick();
            afterEachTick(live.Simulation);
        }

        return live.Simulation;
    }

    /// <summary>The leaf with this id, as the type the test needs.</summary>
    public static T Component<T>(Simulation simulation, string id)
        where T : class, ISimComponent
    {
        ArgumentNullException.ThrowIfNull(simulation);
        return simulation.Components.OfType<T>().Single(c => string.Equals(c.Id, id, StringComparison.Ordinal));
    }
}
