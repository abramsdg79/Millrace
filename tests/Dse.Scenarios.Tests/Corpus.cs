using Dse.Components;
using Dse.Core.Catalogue;

namespace Dse.Scenarios.Tests;

/// <summary>The scenario files under Scenarios/, copied beside the test assembly, and the plants they name.</summary>
public static class Corpus
{
    /// <summary>The shipped catalogue. A scenario needs one only to load its plant.</summary>
    public static ComponentCatalogue Catalogue { get; } = new CatalogueBuilder().Add<ComponentsModule>().Build();

    private static string Root => Path.Combine(AppContext.BaseDirectory, "Scenarios");

    /// <summary>The absolute path of a corpus file, which is what resolves its plant.</summary>
    public static string PathOf(string kind, string name) => Path.Combine(Root, kind, name);

    public static string Text(string kind, string name) => File.ReadAllText(PathOf(kind, name));

    /// <summary>A plant of the linked corpus, by file name.</summary>
    public static string PlantPath(string name) => Path.Combine(AppContext.BaseDirectory, "Plants", name);

    public static IEnumerable<object[]> Valid() => Names("valid");

    public static IEnumerable<object[]> Invalid() => Names("invalid");

    public static IEnumerable<object[]> Unrunnable() => Names("unrunnable");

    /// <summary>Parses scenario text and runs it against a plant of the linked corpus. The text must parse.</summary>
    public static ScenarioRunResult Run(string scenarioJson, string plantFile)
    {
        ScenarioParseResult parsed = ScenarioLoader.Parse(scenarioJson);
        if (parsed.Scenario is null)
        {
            throw new InvalidOperationException("The scenario under test does not parse:\n" + parsed.ToText());
        }

        return ScenarioRunner.Run(parsed.Scenario, File.ReadAllText(PlantPath(plantFile)), Catalogue);
    }

    /// <summary>Parses a corpus file and, if it parses, runs it against the plant it names.</summary>
    public static (ScenarioParseResult Parsed, ScenarioRunResult? Result) RunFile(string kind, string name)
    {
        ScenarioParseResult parsed = ScenarioLoader.Parse(Text(kind, name));
        if (parsed.Scenario is null)
        {
            return (parsed, null);
        }

        string plantJson = File.ReadAllText(parsed.Scenario.ResolvePlantPath(PathOf(kind, name)));
        return (parsed, ScenarioRunner.Run(parsed.Scenario, plantJson, Catalogue));
    }

    private static IEnumerable<object[]> Names(string kind) =>
        Directory.EnumerateFiles(Path.Combine(Root, kind), "*.json")
            .Select(Path.GetFileName)
            .Order(StringComparer.Ordinal)
            .Select(name => new object[] { name! });
}
