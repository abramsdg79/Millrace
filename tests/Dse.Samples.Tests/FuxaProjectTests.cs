using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using Dse.Cli;

namespace Dse.Samples.Tests;

/// <summary>
/// The committed FUXA project (plan 8 criterion 5, "in sync"): its device is
/// the DSE server, its tags are exactly <c>dse modbus-map … --format fuxa</c>,
/// and everything its views, alarms, charts and script reference is one of
/// them — and writable where the HMI writes it.
/// </summary>
public class FuxaProjectTests
{
    private static JsonObject Project() =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(RepositoryRoot(), "hmi", "fuxa", "mine-conveyors.fuxap.json")))!.AsObject();

    private static JsonObject Tags(JsonObject project) => project["devices"]!["dse"]!["tags"]!.AsObject();

    [Fact]
    public void TheDeviceTagsAreExactlyTheModbusMapOfTheMinePlant()
    {
        CliRun map = Cli.Run("modbus-map", Path.Combine(RepositoryRoot(), "samples", "mine-conveyors", "plant.json"), "--format", "fuxa");

        Assert.Equal(ExitCodes.Ok, map.ExitCode);
        JsonObject expected = JsonNode.Parse(map.Out)!.AsObject();
        JsonObject actual = Tags(Project());
        Assert.Equal(expected.Select(p => p.Key), actual.Select(p => p.Key));
        Assert.All(expected, p => Assert.True(JsonNode.DeepEquals(p.Value, actual[p.Key]), $"Tag '{p.Key}' differs from the map: {actual[p.Key]?.ToJsonString()}"));
    }

    [Fact]
    public void TheDeviceIsTheDseServerPolledEvery200Milliseconds()
    {
        JsonNode device = Project()["devices"]!["dse"]!;

        Assert.Equal("ModbusTCP", (string?)device["type"]);
        Assert.True((bool)device["enabled"]!);
        Assert.Equal(200, (int)device["polling"]!);
        Assert.Equal("dse:5020", (string?)device["property"]!["address"]);
        Assert.Equal("1", (string?)device["property"]!["slaveid"]);
        Assert.Equal("TcpPort", (string?)device["property"]!["connectionOption"]);
    }

    [Fact]
    public void EveryTagTheHmiReferencesIsADeviceTag()
    {
        JsonObject project = Project();
        JsonObject tags = Tags(project);
        var referenced = new List<string>();
        foreach (string section in new[] { "hmi", "alarms", "charts", "scripts" })
        {
            Collect(project[section], referenced);
        }

        Assert.NotEmpty(referenced);
        Assert.All(referenced, id => Assert.True(tags.ContainsKey(id), $"The HMI references '{id}', which the device does not have."));
    }

    [Fact]
    public void EveryTagTheHmiWritesIsACoil()
    {
        JsonObject project = Project();
        JsonObject tags = Tags(project);
        var written = new List<string>();
        foreach (JsonNode? item in project["hmi"]!["views"]!.AsArray().SelectMany(v => v!["items"]!.AsObject().Select(p => p.Value)))
        {
            foreach (JsonNode? ev in item!["property"]?["events"]?.AsArray() ?? [])
            {
                Collect(ev!["actoptions"], written);
            }
        }

        Assert.Equal(17, written.Count);
        Assert.All(written, id => Assert.Equal("000000", (string?)tags[id]!["memaddress"]));
    }

    [Fact]
    public void TheProjectHasTheThreeViewsAndEachItemIsAnElementOfItsView()
    {
        JsonArray views = Project()["hmi"]!["views"]!.AsArray();

        Assert.Equal(["Overview", "Alarms", "Trends"], views.Select(v => (string?)v!["name"]));
        Assert.All(views, view =>
        {
            string svg = (string)view!["svgcontent"]!;
            Assert.All(view["items"]!.AsObject(), item => Assert.Contains($"id=\"{item.Key}\"", svg, StringComparison.Ordinal));
        });
    }

    [Fact]
    public void TheReadmesRegisterMapIsTheOneModbusMapPrints()
    {
        string readme = File.ReadAllText(Path.Combine(RepositoryRoot(), "hmi", "fuxa", "README.md")).ReplaceLineEndings("\n");
        int start = readme.IndexOf("```text\n", readme.IndexOf("## Register map\n", StringComparison.Ordinal), StringComparison.Ordinal) + "```text\n".Length;
        string quoted = readme[start..readme.IndexOf("```\n", start, StringComparison.Ordinal)];

        CliRun map = Cli.Run("modbus-map", Path.Combine(RepositoryRoot(), "samples", "mine-conveyors", "plant.json"));

        Assert.Equal(map.Out, quoted);
    }

    [Fact]
    public void TheRootReadmeTheArchitectureAndTheScenariosPageNameServeAndTheHmi()
    {
        string readme = File.ReadAllText(Path.Combine(RepositoryRoot(), "README.md")).ReplaceLineEndings("\n");
        string architecture = File.ReadAllText(Path.Combine(RepositoryRoot(), "docs", "architecture.md")).ReplaceLineEndings("\n");
        string scenarios = File.ReadAllText(Path.Combine(RepositoryRoot(), "docs", "scenarios.md")).ReplaceLineEndings("\n");

        Assert.Contains("-- serve samples/mine-conveyors/plant.json", readme, StringComparison.Ordinal);
        Assert.Contains("-- modbus-map plant.json", readme, StringComparison.Ordinal);
        Assert.Contains("[FUXA HMI](hmi/fuxa/README.md)", readme, StringComparison.Ordinal);
        Assert.Contains("\n## Modbus TCP\n", architecture, StringComparison.Ordinal);
        Assert.Contains("`Dse.Modbus`", architecture, StringComparison.Ordinal);
        Assert.Contains("dse serve plant.json --scenario scenario.json", scenarios, StringComparison.Ordinal);
    }

    /// <summary>Every <c>t_…</c> tag id in a subtree: a string value that is one, or a comma-separated list of them.</summary>
    private static void Collect(JsonNode? node, List<string> ids)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (KeyValuePair<string, JsonNode?> property in obj)
                {
                    Collect(property.Value, ids);
                }

                break;
            case JsonArray array:
                foreach (JsonNode? element in array)
                {
                    Collect(element, ids);
                }

                break;
            case JsonValue value when value.TryGetValue(out string? text) && text.StartsWith("t_", StringComparison.Ordinal):
                ids.AddRange(text.Split(','));
                break;
        }
    }

    /// <summary>Two levels up from this file is the repository root.</summary>
    private static string RepositoryRoot([CallerFilePath] string thisFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", ".."));
}
