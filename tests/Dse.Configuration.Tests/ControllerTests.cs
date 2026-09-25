using Dse.Core;
using Dse.Io;

namespace Dse.Configuration.Tests;

public class ControllerTests
{
    /// <summary>The minimal plant; <c>CONTROLLERS</c> is replaced by the entries under test.</summary>
    private const string Base = """
        {
          "defaults": { "seed": 1, "timeStepMs": 10, "startTime": "2026-01-01T06:00:00Z" },
          "materials": [ { "name": "ore", "kind": "bulk" } ],
          "components": [
            { "id": "FEED", "type": "bulk-source", "parameters": { "material": "ore", "rateKgPerS": 20 } },
            { "id": "CHUTE", "type": "transfer-chute", "parameters": { "capacityKg": 200 } },
            { "id": "PILE", "type": "bulk-sink" }
          ],
          "flows": [ { "from": "FEED.Out", "to": "CHUTE.In" }, { "from": "CHUTE.Out", "to": "PILE.In" } ],
          "controllers": [
            CONTROLLERS
          ]
        }
        """;

    private const string Perm01 =
        """{ "id": "PERM01", "type": "permissive", "scanPeriodMs": 100, "parameters": { "conditions": [ { "tag": "CHUTE.Full", "normal": false } ] } }""";

    private const string Int01 =
        """{ "id": "INT01", "type": "interlock", "scanPeriodMs": 100, "parameters": { "conditions": [ { "tag": "PERM01.Ok", "normal": true } ], "trip": [ { "tag": "FEED.Enabled", "value": false } ] } }""";

    private static string Plant(params string[] controllers) =>
        Base.Replace("CONTROLLERS", string.Join(",\n    ", controllers), StringComparison.Ordinal);

    [Fact]
    public void AControlledPlantLoadsBuildsAndCountsItsControllers()
    {
        LoadResult result = Plants.Load(Plant(Perm01, Int01));

        Assert.True(result.IsValid, result.ToText());
        Assert.Equal(new PlantSummary(3, 0, 2, 0, 2), result.Summary);

        Simulation simulation = result.Builder!.Build();
        Assert.Equal(2, simulation.ScanBlockCount);
        Assert.True(simulation.IO.Directory.TryFind("INT01.Reset", out TagDescriptor reset));
        Assert.Equal(TagAccess.ReadWrite, reset.Access);
        simulation.RunFor(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void ABlockMayNameALaterBlocksTag()
    {
        LoadResult result = Plants.Load(Plant(Int01, Perm01));

        Assert.True(result.IsValid, result.ToText());
        Assert.Equal(2, result.Builder!.Build().ScanBlockCount);
    }

    [Fact]
    public void AnUnknownTagSuggestsTheNearestAndIsReportedOnce()
    {
        ConfigDiagnostic d = Plants.Only(Plant(Perm01, Int01.Replace("FEED.Enabled", "FEED.Enabeld", StringComparison.Ordinal)));

        Assert.Equal("DSE113", d.Code);
        Assert.Equal("$.controllers[1].parameters.trip[0].tag", d.Path);
        Assert.Equal("'FEED.Enabeld' is not a tag in this plant.", d.Message);
        Assert.Equal("Use a tag the plant has — 'FEED.Enabled' is closest.", d.Fix);
    }

    [Fact]
    public void EveryTagErrorIsReportedBeforeAnyBlockIsBuilt()
    {
        string json = Plant(
            Perm01.Replace("CHUTE.Full", "CHUTE.Level", StringComparison.Ordinal),
            Int01.Replace("PERM01.Ok", "PERM01.Okay", StringComparison.Ordinal).Replace("FEED.Enabled", "CHUTE.Full", StringComparison.Ordinal));

        Assert.Equal(
            new[]
            {
                ("DSE114", "$.controllers[0].parameters.conditions[0].tag"),
                ("DSE113", "$.controllers[1].parameters.conditions[0].tag"),
                ("DSE115", "$.controllers[1].parameters.trip[0].tag"),
            },
            Plants.Load(json).Diagnostics.Select(d => (d.Code, d.Path)));
    }

    [Fact]
    public void AWriteToAnInputASignalLinkDrivesIsDse115()
    {
        string json = Plant(Perm01, Int01).Replace(
            "\"flows\":", "\"signals\": [ { \"from\": \"CHUTE.Full\", \"to\": \"FEED.Enabled\" } ],\n  \"flows\":", StringComparison.Ordinal);

        ConfigDiagnostic d = Plants.Only(json);

        Assert.Equal("DSE115", d.Code);
        Assert.Equal("$.controllers[1].parameters.trip[0].tag", d.Path);
        Assert.Equal("'FEED.Enabled' is read-only, so a block cannot command it.", d.Message);
    }

    [Fact]
    public void AWriteToAnotherBlocksOutputIsDse115()
    {
        ConfigDiagnostic d = Plants.Only(Plant(Perm01, Int01.Replace("\"tag\": \"FEED.Enabled\"", "\"tag\": \"PERM01.Ok\"", StringComparison.Ordinal)));

        Assert.Equal("DSE115", d.Code);
        Assert.Equal("$.controllers[1].parameters.trip[0].tag", d.Path);
    }

    [Fact]
    public void AScanPeriodOffTheStepIsDse013AtTheScanPeriod()
    {
        ConfigDiagnostic d = Plants.Only(Plant(Perm01.Replace("\"scanPeriodMs\": 100", "\"scanPeriodMs\": 15", StringComparison.Ordinal), Int01));

        Assert.Equal("DSE013", d.Code);
        Assert.Equal("$.controllers[0].scanPeriodMs", d.Path);
        Assert.Equal("Block 'PERM01' scans every 15 ms, which is not a whole number of 10 ms steps.", d.Message);
        Assert.Equal("Use a period that is a multiple of the time step.", d.Fix);
    }

    [Theory]
    [InlineData("\"scanPeriodMs\": 0, ", "\"scanPeriodMs\" must be a number greater than zero.")]
    [InlineData("\"scanPeriodMs\": -5, ", "\"scanPeriodMs\" must be a number greater than zero.")]
    [InlineData("\"scanPeriodMs\": \"100\", ", "\"scanPeriodMs\" must be a number greater than zero.")]
    [InlineData("\"scanPeriodMs\": 1e30, ", "\"scanPeriodMs\" is 1E+30 ms, which is longer than a day.")]
    [InlineData("\"scanPeriodMs\": 0.00001, ", "\"scanPeriodMs\" must be at least one tick (0.0001 ms).")]
    [InlineData("", "A controller needs \"scanPeriodMs\"; there is no default.")]
    public void AScanPeriodIsAPositiveNumberOfAtMostADay(string replacement, string message)
    {
        ConfigDiagnostic d = Plants.Only(Plant(Perm01.Replace("\"scanPeriodMs\": 100, ", replacement, StringComparison.Ordinal)));

        Assert.Equal("DSE103", d.Code);
        Assert.Equal("$.controllers[0].scanPeriodMs", d.Path);
        Assert.Equal(message, d.Message);
    }

    [Theory]
    [InlineData("INT.01")]
    [InlineData("INT 01")]
    [InlineData("")]
    public void AControllerIdFollowsTheComponentIdRules(string id)
    {
        ConfigDiagnostic d = Plants.Only(Plant(Perm01, Int01.Replace("\"id\": \"INT01\"", $"\"id\": \"{id}\"", StringComparison.Ordinal)));

        Assert.Equal("DSE103", d.Code);
        Assert.Equal("$.controllers[1].id", d.Path);
    }

    [Theory]
    [InlineData("PERM01", "CHUTE", "$.controllers[0].id", "A component is already called 'CHUTE'.")]
    [InlineData("INT01", "PERM01", "$.controllers[1].id", "Another controller is already called 'PERM01'.")]
    public void AControllerIdIsUniqueAcrossComponentsAndControllers(string from, string to, string path, string message)
    {
        string json = Plant(Perm01, Int01).Replace($"\"id\": \"{from}\"", $"\"id\": \"{to}\"", StringComparison.Ordinal);

        ConfigDiagnostic d = Plants.Only(json);

        Assert.Equal("DSE107", d.Code);
        Assert.Equal(path, d.Path);
        Assert.Equal(message, d.Message);
    }

    [Fact]
    public void AnUnknownBlockTypeSuggestsTheNearest()
    {
        ConfigDiagnostic d = Plants.Only(Plant(Perm01.Replace("\"permissive\"", "\"permisive\"", StringComparison.Ordinal)));

        Assert.Equal("DSE102", d.Code);
        Assert.Equal("$.controllers[0].type", d.Path);
        Assert.Equal("'permisive' is not a block type in this catalogue.", d.Message);
        Assert.Contains("'permissive' is closest", d.Fix, StringComparison.Ordinal);
    }

    [Fact]
    public void AControllersSectionMustBeAnArray()
    {
        ConfigDiagnostic d = Plants.Only(Plants.Minimal.Replace("\"flows\":", "\"controllers\": { },\n  \"flows\":", StringComparison.Ordinal));

        Assert.Equal("DSE103", d.Code);
        Assert.Equal("$.controllers", d.Path);
        Assert.Equal("\"controllers\" must be an array.", d.Message);
    }

    [Fact]
    public void AnOwnedTagThatABindAlreadyNamedIsDse015AtTheController()
    {
        string json = Plant(Perm01, Int01).Replace(
            "\"flows\":", "\"tags\": [ { \"name\": \"PERM01.Ok\", \"port\": \"PILE.Full\" } ],\n  \"flows\":", StringComparison.Ordinal);

        ConfigDiagnostic d = Plants.Only(json);

        Assert.Equal("DSE015", d.Code);
        Assert.Equal("$.controllers[0]", d.Path);
        Assert.Contains("'PERM01.Ok'", d.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ABlockConstructorsRefusalIsDse111AtTheController()
    {
        const string LevelAlarm =
            """{ "id": "LVL01", "type": "alarm", "scanPeriodMs": 100, "parameters": { "input": "CHUTE.Level", "limits": [ { "kind": "hi", "value": 0.9 }, { "kind": "hi-hi", "value": 0.5 } ] } }""";

        ConfigDiagnostic d = Plants.Only(Plant(LevelAlarm));

        Assert.Equal("DSE111", d.Code);
        Assert.Equal("$.controllers[0]", d.Path);
        Assert.Equal(
            "'LVL01' (alarm) rejected its parameters: Limits must ascend LoLo < Lo < Hi < HiHi, but Hi is 0.9 and HiHi is 0.5.",
            d.Message);
    }

    [Theory]
    [InlineData("broken-block", "The factory for type 'broken-block' failed with InvalidOperationException: bad wiring.")]
    [InlineData("tagless", "The owned-tag function of type 'tagless' failed with InvalidOperationException: no tags today.")]
    [InlineData("null-tags", "The owned-tag function of type 'null-tags' returned null or a null tag.")]
    [InlineData("null-block", "The factory for type 'null-block' returned null.")]
    [InlineData("wrong-id", "The factory for type 'wrong-id' built block 'OTHER' scanning every 100 ms, but was given 'B1' and 100 ms.")]
    [InlineData("throwing-outputs", "Reading the Outputs of the block built for type 'throwing-outputs' failed with InvalidOperationException: no outputs today.")]
    [InlineData("null-inputs", "The block built for type 'null-inputs' has a null Inputs list or a null entry in it.")]
    [InlineData("null-command", "The block built for type 'null-command' has a null Commands list or a null entry in it.")]
    [InlineData("misdeclared", "The block built for type 'misdeclared' owns 'B1.Ok' (Bool ReadOnly), but its descriptor declares 'B1.Ok' (Bool ReadWrite).")]
    public void AModuleDefectIsDse111NamingTheModule(string type, string message)
    {
        string json = $$"""{ "components": [ { "id": "PILE", "type": "bulk-sink" } ], "controllers": [ { "id": "B1", "type": "{{type}}", "scanPeriodMs": 100 } ] }""";

        ConfigDiagnostic d = TestPlants.Only(json);

        Assert.Equal("DSE111", d.Code);
        Assert.Equal("$.controllers[0]", d.Path);
        Assert.Equal(message, d.Message);
        Assert.Contains("module 'Test'", d.Fix, StringComparison.Ordinal);
    }

    [Fact]
    public void ATransitionThatRejectsItsParametersIsASentenceWithoutTheParameterSuffix()
    {
        const string Seq01 =
            """{ "id": "SEQ01", "type": "sequencer", "scanPeriodMs": 100, "parameters": { "steps": [ { "name": "Fill the chute", "transition": { "type": "when", "tag": "CHUTE.Full", "op": "<", "value": true } } ] } }""";

        ConfigDiagnostic d = Plants.Only(Plant(Seq01));

        Assert.Equal("DSE111", d.Code);
        Assert.EndsWith("Use Equal or NotEqual.", d.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("(Parameter", d.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void APlantTagConflictIsReportedAsItselfNotAsAnUnknownTag()
    {
        // FEED.Go binds FEED.Enabled for writing, but a signal link already drives that input: DSE010.
        // PlantTags() leaves the rejected tag out, so resolving INT01 first would have said DSE113 (R80).
        string json = Plant(Int01.Replace("FEED.Enabled", "FEED.Go", StringComparison.Ordinal).Replace("PERM01.Ok\", \"normal\": true", "CHUTE.Full\", \"normal\": false", StringComparison.Ordinal))
            .Replace(
                "\"flows\":",
                "\"signals\": [ { \"from\": \"CHUTE.Full\", \"to\": \"FEED.Enabled\" } ],\n" +
                "  \"tags\": [ { \"name\": \"FEED.Go\", \"port\": \"FEED.Enabled\", \"access\": \"write\" } ],\n  \"flows\":",
                StringComparison.Ordinal);

        ConfigDiagnostic d = Plants.Only(json);

        Assert.Equal("DSE010", d.Code);
        Assert.Equal("$.components[0]", d.Path);
        Assert.Contains("'FEED.Go'", d.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEmptyControllersSectionIsValid()
    {
        LoadResult result = Plants.Load(Plants.Minimal.Replace("\"flows\":", "\"controllers\": [],\n  \"flows\":", StringComparison.Ordinal));

        Assert.True(result.IsValid, result.ToText());
        Assert.Equal(0, result.Summary!.Controllers);
        Assert.Equal(0, result.Builder!.Build().ScanBlockCount);
    }

    [Fact]
    public void ADurationLongerThanAYearIsDse103AtTheParameter()
    {
        const string SlowTimer =
            """{ "id": "TMR01", "type": "timer", "scanPeriodMs": 100, "parameters": { "mode": "on-delay", "input": "CHUTE.Full", "presetS": 1e30 } }""";

        ConfigDiagnostic d = Plants.Only(Plant(SlowTimer));

        Assert.Equal("DSE103", d.Code);
        Assert.Equal("$.controllers[0].parameters.presetS", d.Path);
    }
}
