using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Dse.Cli;

namespace Dse.Samples.Tests;

/// <summary>
/// The v1.0.0 release (plan 7): the version the build stamps, the changelog's
/// one entry and the root README's "Getting started" agree, every link the
/// changelog makes resolves, and every command the README tells a newcomer to
/// run does what it says.
/// </summary>
public partial class ReleaseTests
{
    [GeneratedRegex(@"^## 1\.0\.0 — \d{4}-\d{2}-\d{2}$", RegexOptions.Multiline)]
    private static partial Regex ReleaseHeading();

    [GeneratedRegex(@"\]\((?!https?://)([^)#]+)\)")]
    private static partial Regex RelativeLink();

    [GeneratedRegex(@"-- run (samples/[a-z-]+/scenarios/[a-z-]+\.json) --expect (samples/[a-z-]+/expected/[a-z-]+\.log)\n")]
    private static partial Regex ExpectCommand();

    [Fact]
    public void TheVersionTheChangelogAndTheReadmeAgree()
    {
        string props = RepositoryFile("Directory.Build.props");
        string changelog = RepositoryFile("CHANGELOG.md");
        string readme = RepositoryFile("README.md");

        Assert.Contains("    <Version>1.0.0</Version>\n", props, StringComparison.Ordinal);
        Assert.All(
            Directory.EnumerateFiles(Path.Combine(RepositoryRoot(), "src"), "*.csproj", SearchOption.AllDirectories),
            path => Assert.False(File.ReadAllText(path).Contains("<Version>", StringComparison.Ordinal), $"{path} sets its own <Version>, overriding Directory.Build.props."));

        Assert.StartsWith("# Changelog\n", changelog, StringComparison.Ordinal);
        Assert.Single(ReleaseHeading().Matches(changelog));
        string[] headings = changelog.Split('\n').Where(l => l.StartsWith("## ", StringComparison.Ordinal)).ToArray();
        string[] expected = headings.Contains("## Unreleased") ? ["## Unreleased", headings[^1]] : [headings[^1]];
        Assert.Equal(expected, headings);
        Assert.Matches(ReleaseHeading(), headings[^1]);
        string[] areas = changelog.Split('\n').Where(l => l.StartsWith("### ", StringComparison.Ordinal)).Select(l => l[4..].Split(' ')[0]).ToArray();
        Assert.All(["Core", "Components", "Io", "Realtime", "Control", "Scenarios", "Configuration", "Cli", "Samples"], area => Assert.Contains(area, areas));

        string[] links = RelativeLink().Matches(changelog).Select(m => m.Groups[1].Value).ToArray();
        Assert.All(links, link => Assert.True(File.Exists(Path.Combine(RepositoryRoot(), link)), $"The changelog links '{link}', which does not exist."));
        foreach (string folder in new[] { "specs", "plans" })
        {
            IEnumerable<string> v1 = Directory.GetFiles(Path.Combine(RepositoryRoot(), "docs", "superpowers", folder), "*.md")
                .Select(Path.GetFileName)
                .Where(name => string.CompareOrdinal(name, "2026-10-08") < 0)
                .Select(name => $"docs/superpowers/{folder}/{name}");
            Assert.All(v1, path => Assert.Contains(path, links));
        }

        Assert.Contains("## Getting started\n", readme, StringComparison.Ordinal);
        Assert.Contains("[.NET 10 SDK]", readme, StringComparison.Ordinal);
        Assert.Contains("```bash\ndotnet build Dse.sln\ndotnet test Dse.sln\n```\n", readme, StringComparison.Ordinal);
        Assert.Contains("[changelog](CHANGELOG.md)", readme, StringComparison.Ordinal);
        Assert.Contains("**Version 1.0.0.**", readme, StringComparison.Ordinal);
        Assert.DoesNotContain("Under construction", readme, StringComparison.Ordinal);
        Assert.DoesNotContain("## Build and test", readme, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryRunTheReadmeChecksAgainstAGoldenMatchesIt()
    {
        string readme = RepositoryFile("README.md");
        string gettingStarted = readme[readme.IndexOf("## Getting started\n", StringComparison.Ordinal)..readme.IndexOf("## Command line\n", StringComparison.Ordinal)];
        Match[] commands = ExpectCommand().Matches(gettingStarted).ToArray();

        Assert.Equal(["mine-conveyors", "wheel-line"], commands.Select(c => c.Groups[1].Value.Split('/')[1]));
        Assert.All(commands, command =>
        {
            string golden = command.Groups[2].Value;
            CliRun run = Cli.Run("run", Path.Combine(RepositoryRoot(), command.Groups[1].Value), "--expect", Path.Combine(RepositoryRoot(), golden));

            Assert.Equal(ExitCodes.Ok, run.ExitCode);
            Assert.StartsWith($"Matched {Path.Combine(RepositoryRoot(), golden)} (", run.Out, StringComparison.Ordinal);
        });
    }

    private static string RepositoryFile(string name) =>
        File.ReadAllText(Path.Combine(RepositoryRoot(), name)).ReplaceLineEndings("\n");

    /// <summary>Two levels up from this file is the repository root.</summary>
    private static string RepositoryRoot([CallerFilePath] string thisFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", ".."));
}
