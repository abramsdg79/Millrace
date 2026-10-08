namespace Millrace.Configuration.Tests;

/// <summary>The plant files under Plants/, copied beside the test assembly. Public because xUnit's MemberData reads it.</summary>
public static class Corpus
{
    private static string Root => Path.Combine(AppContext.BaseDirectory, "Plants");

    public static IEnumerable<object[]> Valid() => Names("valid");

    public static IEnumerable<object[]> Invalid() => Names("invalid");

    public static string Read(string kind, string name) => File.ReadAllText(Path.Combine(Root, kind, name));

    private static IEnumerable<object[]> Names(string kind) =>
        Directory.EnumerateFiles(Path.Combine(Root, kind), "*.json")
            .Select(Path.GetFileName)
            .Order(StringComparer.Ordinal)
            .Select(name => new object[] { name! });
}

public class CorpusTests
{
    [Theory]
    [MemberData(nameof(Corpus.Valid), MemberType = typeof(Corpus))]
    public void EveryValidPlantLoadsCleanAndBuilds(string name)
    {
        LoadResult result = Plants.Load(Corpus.Read("valid", name));

        Assert.True(result.IsValid, result.ToText());
        Assert.Empty(result.Diagnostics);
        result.Builder!.Build().RunFor(TimeSpan.FromSeconds(2));
    }

    [Theory]
    [MemberData(nameof(Corpus.Invalid), MemberType = typeof(Corpus))]
    public void EveryInvalidPlantYieldsExactlyTheCodeInItsName(string name)
    {
        string expected = name[..name.IndexOf('-', StringComparison.Ordinal)];

        LoadResult result = Plants.Load(Corpus.Read("invalid", name));

        Assert.False(result.IsValid);
        Assert.NotEmpty(result.Diagnostics);
        Assert.All(result.Diagnostics, d => Assert.Equal(expected, d.Code));
        Assert.Null(result.Builder);
    }

    [Fact]
    public void EveryConfigurationCodeHasAnInvalidPlant()
    {
        var covered = Corpus.Invalid().Select(row => ((string)row[0])[..5]).ToHashSet(StringComparer.Ordinal);

        Assert.All(ConfigDiagnostics.All, d => Assert.Contains(d.Code, covered));
    }
}
