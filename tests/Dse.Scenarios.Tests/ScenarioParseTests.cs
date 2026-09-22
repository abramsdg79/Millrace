using Dse.Configuration;
using Dse.Core.Faults;

namespace Dse.Scenarios.Tests;

public class ScenarioParseTests
{
    /// <summary>A scenario with one action, built from the pieces a test wants to vary.</summary>
    private static string Json(string body) => $"{{ \"plant\": \"p.json\", \"duration\": 10, {body} }}";

    private static ConfigDiagnostic Only(string json) => Assert.Single(ScenarioLoader.Parse(json).Diagnostics);

    [Fact]
    public void TheSpecsExampleParses()
    {
        ScenarioParseResult result = ScenarioLoader.Parse("""
            {
              "plant": "conveyor-line.json",
              "seed": 42,
              "startTime": "2026-01-01T06:00:00Z",
              "timeStepMs": 10,
              "duration": 120,
              "timeline": [
                { "at": 5,    "write": "CV001.Start", "value": true },
                { "at": 30,   "fault": "CV001.Motor", "id": "thermal-bias", "args": { "amount": 0.8 } },
                { "at": 60.5, "clear": "CV001.Motor", "id": "thermal-bias" }
              ]
            }
            """);

        Assert.Empty(result.Diagnostics);
        Assert.True(result.IsValid);
        Scenario scenario = result.Scenario!;
        Assert.Equal("conveyor-line.json", scenario.PlantPath);
        Assert.Equal(42UL, scenario.Seed);
        Assert.Equal(new DateTimeOffset(2026, 1, 1, 6, 0, 0, TimeSpan.Zero), scenario.StartTime);
        Assert.Equal(TimeSpan.FromMilliseconds(10), scenario.TimeStep);
        Assert.Equal(TimeSpan.FromSeconds(120), scenario.Duration);

        var write = Assert.IsType<WriteAction>(scenario.Timeline[0]);
        Assert.Equal((TimeSpan.FromSeconds(5), "CV001.Start"), (write.At, write.Tag));
        Assert.Equal(ScenarioValue.OfBool(true), write.Value);

        var fault = Assert.IsType<FaultAction>(scenario.Timeline[1]);
        Assert.Equal((TimeSpan.FromSeconds(30), "CV001.Motor", "thermal-bias"), (fault.At, fault.ComponentId, fault.FaultId));
        Assert.Equal(new[] { new FaultArgument("amount", 0.8) }, fault.Arguments);

        var clear = Assert.IsType<ClearAction>(scenario.Timeline[2]);
        Assert.Equal((TimeSpan.FromMilliseconds(60500), "CV001.Motor", "thermal-bias"), (clear.At, clear.ComponentId, clear.FaultId));
    }

    [Fact]
    public void TheSmallestScenarioIsAPlantAndADuration()
    {
        ScenarioParseResult result = ScenarioLoader.Parse("""{ "plant": "p.json", "duration": 1 }""");

        Assert.Empty(result.Diagnostics);
        Scenario scenario = result.Scenario!;
        Assert.Null(scenario.Seed);
        Assert.Null(scenario.StartTime);
        Assert.Null(scenario.TimeStep);
        Assert.Empty(scenario.Timeline);
    }

    [Fact]
    public void BrokenJsonIsDse200WithAPosition()
    {
        ConfigDiagnostic diagnostic = Only("""{ "plant": "p.json", "duration": """);

        Assert.Equal(("DSE200", "$"), (diagnostic.Code, diagnostic.Path));
        Assert.StartsWith("The scenario is not valid JSON at line ", diagnostic.Message, StringComparison.Ordinal);
        Assert.StartsWith("Correct the JSON at that position.", diagnostic.Fix, StringComparison.Ordinal);
    }

    [Fact]
    public void ANonObjectIsDse202()
    {
        ConfigDiagnostic diagnostic = Only("[ ]");

        Assert.Equal(("DSE202", "$", "A scenario file is a JSON object."), (diagnostic.Code, diagnostic.Path, diagnostic.Message));
    }

    [Fact]
    public void AnUnknownTopLevelKeyIsDse201AndNamesTheNearest()
    {
        ConfigDiagnostic diagnostic = Only("""{ "plant": "p.json", "duration": 10, "seedd": 3 }""");

        Assert.Equal(("DSE201", "$.seedd"), (diagnostic.Code, diagnostic.Path));
        Assert.Equal("'seedd' is not a key a scenario has.", diagnostic.Message);
        Assert.Contains("'seed' is closest.", diagnostic.Fix, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnknownActionKeyIsDse201()
    {
        ConfigDiagnostic diagnostic = Only(Json("""
            "timeline": [ { "at": 1, "write": "T", "value": true, "vaue": 2 } ]
            """));

        Assert.Equal(("DSE201", "$.timeline[0].vaue"), (diagnostic.Code, diagnostic.Path));
        Assert.Contains("'value' is closest.", diagnostic.Fix, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{ "duration": 10 }""", "$.plant", "A scenario needs a \"plant\": the path of the plant file, relative to this scenario.")]
    [InlineData("""{ "plant": 3, "duration": 10 }""", "$.plant", "A scenario needs a \"plant\": the path of the plant file, relative to this scenario.")]
    [InlineData("""{ "plant": "p.json" }""", "$.duration", "A scenario needs a \"duration\": how many seconds to run.")]
    [InlineData("""{ "plant": "p.json", "duration": 0 }""", "$.duration", "\"duration\" must be a number of seconds greater than zero and at most 1000000000.")]
    [InlineData("""{ "plant": "p.json", "duration": -1 }""", "$.duration", "\"duration\" must be a number of seconds greater than zero and at most 1000000000.")]
    [InlineData("""{ "plant": "p.json", "duration": 10, "seed": -1 }""", "$.seed", "\"seed\" must be a whole number, zero or greater.")]
    [InlineData("""{ "plant": "p.json", "duration": 10, "timeStepMs": 0 }""", "$.timeStepMs", "\"timeStepMs\" must be a number greater than zero.")]
    [InlineData("""{ "plant": "p.json", "duration": 10, "startTime": "2026-01-01T06:00:00" }""", "$.startTime", "\"startTime\" must be an ISO 8601 date and time with an offset.")]
    [InlineData("""{ "plant": "p.json", "duration": 10, "timeline": 3 }""", "$.timeline", "\"timeline\" must be an array of actions.")]
    public void ABadTopLevelValueIsDse202(string json, string path, string message)
    {
        ConfigDiagnostic diagnostic = Only(json);

        Assert.Equal(("DSE202", path, message), (diagnostic.Code, diagnostic.Path, diagnostic.Message));
        Assert.EndsWith(".", diagnostic.Fix, StringComparison.Ordinal);
    }

    [Fact]
    public void AnOffsetStartTimeIsKept()
    {
        ScenarioParseResult result = ScenarioLoader.Parse(
            """{ "plant": "p.json", "duration": 10, "startTime": "2026-03-01T08:00:00+02:00" }""");

        Assert.Empty(result.Diagnostics);
        Assert.Equal(new DateTimeOffset(2026, 3, 1, 8, 0, 0, TimeSpan.FromHours(2)), result.Scenario!.StartTime);
    }

    [Theory]
    [InlineData("""{ "at": 1 }""", "$.timeline[0]", "An action needs exactly one of \"write\", \"fault\" or \"clear\"; this one has none.")]
    [InlineData("""{ "at": 1, "write": "T", "value": true, "clear": "C", "id": "f" }""", "$.timeline[0]", "An action needs exactly one of \"write\", \"fault\" or \"clear\"; this one has 2.")]
    [InlineData("""{ "at": 1, "write": "T" }""", "$.timeline[0]", "A write action needs a \"value\".")]
    [InlineData("""{ "at": 1, "fault": "C" }""", "$.timeline[0]", "A fault action needs an \"id\" naming the fault.")]
    [InlineData("""{ "at": 1, "clear": "C" }""", "$.timeline[0]", "A clear action needs an \"id\" naming the fault.")]
    [InlineData("""{ "at": 1, "clear": "C", "id": "f", "args": { "x": 1 } }""", "$.timeline[0].args", "A clear action takes no \"args\".")]
    [InlineData("""{ "at": 1, "write": "T", "value": true, "id": "f" }""", "$.timeline[0].id", "\"id\" belongs to a fault or a clear, not to a write.")]
    [InlineData("""{ "at": 1, "write": "T", "value": true, "args": { "x": 1 } }""", "$.timeline[0].args", "\"args\" belongs to a fault, not to a write.")]
    [InlineData("""3""", "$.timeline[0]", "A timeline entry is an object.")]
    public void AMalformedActionIsDse204(string action, string path, string message)
    {
        ConfigDiagnostic diagnostic = Only(Json($"\"timeline\": [ {action} ]"));

        Assert.Equal(("DSE204", path, message), (diagnostic.Code, diagnostic.Path, diagnostic.Message));
        Assert.EndsWith(".", diagnostic.Fix, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{ "write": "T", "value": true }""", "$.timeline[0].at", "An action needs an \"at\": its time in seconds from the start of the run.")]
    [InlineData("""{ "at": "soon", "write": "T", "value": true }""", "$.timeline[0].at", "\"at\" must be a number of seconds, zero or greater and at most 1000000000.")]
    [InlineData("""{ "at": -1, "write": "T", "value": true }""", "$.timeline[0].at", "\"at\" must be a number of seconds, zero or greater and at most 1000000000.")]
    [InlineData("""{ "at": 1, "write": "", "value": true }""", "$.timeline[0].write", "\"write\" must be the name of a tag.")]
    [InlineData("""{ "at": 1, "write": "T", "value": "on" }""", "$.timeline[0].value", "\"value\" must be true, false or a number.")]
    [InlineData("""{ "at": 1, "fault": 3, "id": "f" }""", "$.timeline[0].fault", "\"fault\" must be the name of a component.")]
    [InlineData("""{ "at": 1, "clear": 3, "id": "f" }""", "$.timeline[0].clear", "\"clear\" must be the name of a component.")]
    [InlineData("""{ "at": 1, "fault": "C", "id": 3 }""", "$.timeline[0].id", "\"id\" must be the name of a fault.")]
    [InlineData("""{ "at": 1, "fault": "C", "id": "f", "args": 3 }""", "$.timeline[0].args", "\"args\" must be an object of numbers.")]
    [InlineData("""{ "at": 1, "fault": "C", "id": "f", "args": { "amount": "lots" } }""", "$.timeline[0].args.amount", "Fault argument 'amount' must be a number.")]
    public void ABadActionValueIsDse202(string action, string path, string message)
    {
        ConfigDiagnostic diagnostic = Only(Json($"\"timeline\": [ {action} ]"));

        Assert.Equal(("DSE202", path, message), (diagnostic.Code, diagnostic.Path, diagnostic.Message));
        Assert.EndsWith(".", diagnostic.Fix, StringComparison.Ordinal);
    }

    [Fact]
    public void AnActionAtTheEndOfTheRunIsDse203()
    {
        ConfigDiagnostic diagnostic = Only(Json("""
            "timeline": [ { "at": 10, "write": "T", "value": true } ]
            """));

        Assert.Equal(("DSE203", "$.timeline[0].at"), (diagnostic.Code, diagnostic.Path));
        Assert.Equal("An action at 10 s is not before the end of the run at 10 s.", diagnostic.Message);
        Assert.StartsWith("Move the action earlier", diagnostic.Fix, StringComparison.Ordinal);
    }

    [Fact]
    public void ADeclaredStepMakesAnOffTickActionDse203()
    {
        ConfigDiagnostic diagnostic = Only("""
            {
              "plant": "p.json", "timeStepMs": 10, "duration": 10,
              "timeline": [ { "at": 5.005, "write": "T", "value": true } ]
            }
            """);

        Assert.Equal(("DSE203", "$.timeline[0].at"), (diagnostic.Code, diagnostic.Path));
        Assert.Equal("The action time 5.005 s is not a whole number of 10 ms steps.", diagnostic.Message);
    }

    [Fact]
    public void ADeclaredStepMakesAnOffTickDurationDse203()
    {
        ConfigDiagnostic diagnostic = Only("""{ "plant": "p.json", "timeStepMs": 100, "duration": 10.55 }""");

        Assert.Equal(("DSE203", "$.duration"), (diagnostic.Code, diagnostic.Path));
        Assert.Equal("The duration 10.55 s is not a whole number of 100 ms steps.", diagnostic.Message);
    }

    [Fact]
    public void WithoutADeclaredStepTickAlignmentWaitsForThePlant()
    {
        ScenarioParseResult result = ScenarioLoader.Parse(Json("""
            "timeline": [ { "at": 5.005, "write": "T", "value": true } ]
            """));

        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void EveryProblemIsReportedNotJustTheFirst()
    {
        ScenarioParseResult result = ScenarioLoader.Parse("""
            {
              "duration": 0,
              "timeline": [
                { "at": 1 },
                { "at": 2, "write": "T", "value": "on" }
              ]
            }
            """);

        Assert.Equal(
            new[] { "DSE202", "DSE202", "DSE204", "DSE202" },
            result.Diagnostics.Select(d => d.Code));
        Assert.Equal(
            new[] { "$.plant", "$.duration", "$.timeline[0]", "$.timeline[1].value" },
            result.Diagnostics.Select(d => d.Path));
        Assert.Null(result.Scenario);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void NumbersKeepTheShapeTheFileWroteThem()
    {
        ScenarioParseResult result = ScenarioLoader.Parse(Json("""
            "timeline": [
              { "at": 1, "write": "A", "value": 7 },
              { "at": 2, "write": "B", "value": 7.0 },
              { "at": 3, "write": "C", "value": 7.5 }
            ]
            """));

        Assert.Empty(result.Diagnostics);
        Assert.Equal(
            new[] { ScenarioValueKind.Integer, ScenarioValueKind.Number, ScenarioValueKind.Number },
            result.Scenario!.Timeline.Cast<WriteAction>().Select(w => w.Value.Kind));
    }

    [Fact]
    public void CommentsAndTrailingCommasAreAllowed()
    {
        ScenarioParseResult result = ScenarioLoader.Parse("""
            {
              // the smallest useful scenario
              "plant": "p.json",
              "duration": 1,
            }
            """);

        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void TheTextOfAResultIsTheDiagnosticsBlankLineSeparated()
    {
        ScenarioParseResult result = ScenarioLoader.Parse("""{ "plant": "p.json" }""");

        Assert.Equal(Assert.Single(result.Diagnostics).ToText() + "\n", result.ToText());
        Assert.Equal(string.Empty, ScenarioLoader.Parse("""{ "plant": "p.json", "duration": 1 }""").ToText());
    }
}
