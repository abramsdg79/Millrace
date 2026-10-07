using Dse.Core;
using Dse.Io;

namespace Dse.Scenarios.Tests;

/// <summary><c>ScenarioRunner.Bind</c> (plan 8): the run without the running, for a host that ticks it itself.</summary>
public class ScenarioBindingTests
{
    private static ScenarioBinding Bind(string plant, string body)
    {
        ScenarioParseResult parsed = ScenarioLoader.Parse($$"""{ "plant": "{{plant}}", {{body}} }""");
        return ScenarioRunner.Bind(parsed.Scenario!, File.ReadAllText(Corpus.PlantPath(plant)), Corpus.Catalogue);
    }

    [Fact]
    public void ABoundScenarioIsATickZeroSimulationWithItsTimelineScheduled()
    {
        ScenarioBinding binding = Bind("minimal.json", """
            "duration": 30,
            "timeline": [ { "at": 10, "write": "FEED.Rate", "value": 5 } ]
            """);

        Simulation simulation = Assert.IsType<Simulation>(binding.Simulation);
        Assert.True(binding.IsValid);
        Assert.Empty(binding.Diagnostics);
        Assert.Equal(0L, simulation.Clock.TickCount);

        simulation.RunFor(TimeSpan.FromSeconds(11));
        Assert.Equal(TagValue.Double(5.0), simulation.IO.Read("FEED.Rate"));
        Assert.Contains(simulation.Events.Records, r => r.Code == "WRITE" && r.Message == "Set to 5.");
    }

    [Fact]
    public void ABoundScenarioRunsOnPastItsDuration()
    {
        Simulation simulation = Bind("minimal.json", "\"duration\": 2").Simulation!;

        simulation.RunFor(TimeSpan.FromSeconds(5));

        Assert.Equal(500L, simulation.Clock.TickCount);
    }

    [Fact]
    public void AScenarioThatDoesNotBindGivesNoSimulationAndRunsDiagnostics()
    {
        const string Body = """
            "duration": 30,
            "timeline": [ { "at": 10, "write": "FEED.Nope", "value": 5 } ]
            """;

        ScenarioBinding binding = Bind("minimal.json", Body);
        ScenarioRunResult run = Corpus.Run($$"""{ "plant": "minimal.json", {{Body}} }""", "minimal.json");

        Assert.Null(binding.Simulation);
        Assert.False(binding.IsValid);
        Assert.Equal(run.ToText(), binding.ToText());
    }
}
