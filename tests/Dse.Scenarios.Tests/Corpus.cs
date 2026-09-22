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

    private static IEnumerable<object[]> Names(string kind) =>
        Directory.EnumerateFiles(Path.Combine(Root, kind), "*.json")
            .Select(Path.GetFileName)
            .Order(StringComparer.Ordinal)
            .Select(name => new object[] { name! });
}
