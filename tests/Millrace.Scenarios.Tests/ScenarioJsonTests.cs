using Millrace.Core.Faults;

namespace Millrace.Scenarios.Tests;

public class ScenarioJsonTests
{
    private static Scenario Example() => new(
        "conveyor-line.json",
        42UL,
        new DateTimeOffset(2026, 1, 1, 6, 0, 0, TimeSpan.Zero),
        TimeSpan.FromMilliseconds(10),
        TimeSpan.FromSeconds(120),
        [
            new WriteAction(TimeSpan.FromSeconds(5), "CV001.Start", ScenarioValue.OfBool(true)),
            new FaultAction(TimeSpan.FromSeconds(30), "CV001.Motor", "thermal-bias", [new FaultArgument("amount", 0.8)]),
            new ClearAction(TimeSpan.FromMilliseconds(60500), "CV001.Motor", "thermal-bias"),
        ]);

    [Fact]
    public void TheSpecsExampleRendersInTheSpecsOrder()
    {
        string json = ScenarioJson.Write(Example());

        Assert.Equal(
            """
            {
              "plant": "conveyor-line.json",
              "seed": 42,
              "startTime": "2026-01-01T06:00:00Z",
              "timeStepMs": 10,
              "duration": 120,
              "timeline": [
                {
                  "at": 5,
                  "write": "CV001.Start",
                  "value": true
                },
                {
                  "at": 30,
                  "fault": "CV001.Motor",
                  "id": "thermal-bias",
                  "args": {
                    "amount": 0.8
                  }
                },
                {
                  "at": 60.5,
                  "clear": "CV001.Motor",
                  "id": "thermal-bias"
                }
              ]
            }

            """.ReplaceLineEndings("\n"),
            json);
    }

    [Fact]
    public void AnOmittedOverrideIsAnOmittedKey()
    {
        string json = ScenarioJson.Write(new Scenario("p.json", null, null, null, TimeSpan.FromSeconds(1), []));

        Assert.Equal(
            """
            {
              "plant": "p.json",
              "duration": 1,
              "timeline": []
            }

            """.ReplaceLineEndings("\n"),
            json);
    }

    [Fact]
    public void WhatItWritesParsesBackToWhatItWrites()
    {
        string json = ScenarioJson.Write(Example());

        ScenarioParseResult parsed = ScenarioLoader.Parse(json);

        Assert.Empty(parsed.Diagnostics);
        Assert.Equal(json, ScenarioJson.Write(parsed.Scenario!));
    }

    [Fact]
    public void EveryFieldSurvivesTheRoundTrip()
    {
        Scenario original = Example();

        Scenario parsed = ScenarioLoader.Parse(ScenarioJson.Write(original)).Scenario!;

        Assert.Equal(
            (original.PlantPath, original.Seed, original.StartTime, original.TimeStep, original.Duration),
            (parsed.PlantPath, parsed.Seed, parsed.StartTime, parsed.TimeStep, parsed.Duration));
        Assert.Equal(original.Timeline[0], parsed.Timeline[0]);
        Assert.Equal(original.Timeline[2], parsed.Timeline[2]);
        var fault = Assert.IsType<FaultAction>(parsed.Timeline[1]);
        Assert.Equal((TimeSpan.FromSeconds(30), "CV001.Motor", "thermal-bias"), (fault.At, fault.ComponentId, fault.FaultId));
        Assert.Equal(new[] { new FaultArgument("amount", 0.8) }, fault.Arguments);
    }

    [Fact]
    public void AnOffsetStartTimeKeepsItsOffset()
    {
        var scenario = new Scenario(
            "p.json", null, new DateTimeOffset(2026, 3, 1, 8, 0, 0, TimeSpan.FromHours(2)), null, TimeSpan.FromSeconds(1), []);

        string json = ScenarioJson.Write(scenario);

        Assert.Contains("\"startTime\": \"2026-03-01T08:00:00+02:00\"", json, StringComparison.Ordinal);
        Assert.Equal(scenario.StartTime, ScenarioLoader.Parse(json).Scenario!.StartTime);
    }

    [Fact]
    public void AFractionOfASecondSurvives()
    {
        var scenario = new Scenario(
            "p.json", null, new DateTimeOffset(2026, 1, 1, 6, 0, 0, 250, TimeSpan.Zero), null, TimeSpan.FromSeconds(1), []);

        string json = ScenarioJson.Write(scenario);

        Assert.Contains("\"startTime\": \"2026-01-01T06:00:00.25Z\"", json, StringComparison.Ordinal);
        Assert.Equal(scenario.StartTime, ScenarioLoader.Parse(json).Scenario!.StartTime);
    }

    [Fact]
    public void AWholeDoubleIsWrittenAsAnIntegerLiteralAndComesBackAsOne()
    {
        var scenario = new Scenario("p.json", null, null, null, TimeSpan.FromSeconds(10),
            [new WriteAction(TimeSpan.FromSeconds(1), "T", ScenarioValue.OfNumber(5.0))]);

        string json = ScenarioJson.Write(scenario);

        Assert.Contains("\"value\": 5", json, StringComparison.Ordinal);
        var parsed = Assert.IsType<WriteAction>(ScenarioLoader.Parse(json).Scenario!.Timeline[0]);
        Assert.Equal(ScenarioValueKind.Integer, parsed.Value.Kind);
        Assert.Equal(json, ScenarioJson.Write(ScenarioLoader.Parse(json).Scenario!));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void ANonFiniteWriteCannotBeWritten(double value)
    {
        var scenario = new Scenario("p.json", null, null, null, TimeSpan.FromSeconds(10),
            [new WriteAction(TimeSpan.FromSeconds(1), "T", ScenarioValue.OfNumber(value))]);

        ArgumentException error = Assert.Throws<ArgumentException>(() => ScenarioJson.Write(scenario));
        Assert.Contains("JSON cannot represent", error.Message, StringComparison.Ordinal);
        Assert.Equal("scenario", error.ParamName);
    }

    [Fact]
    public void ANonFiniteFaultArgumentCannotBeWritten()
    {
        var scenario = new Scenario("p.json", null, null, null, TimeSpan.FromSeconds(10),
            [new FaultAction(TimeSpan.FromSeconds(1), "C", "f", [new FaultArgument("amount", double.NaN)])]);

        ArgumentException error = Assert.Throws<ArgumentException>(() => ScenarioJson.Write(scenario));
        Assert.Contains("amount=NaN", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheTextIsNormalised()
    {
        string json = ScenarioJson.Write(Example());

        Assert.DoesNotContain('\r', json);
        Assert.EndsWith("}\n", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\n\n", json, StringComparison.Ordinal);
    }

    [Fact]
    public void WritingIsDeterministic()
    {
        Assert.Equal(ScenarioJson.Write(Example()), ScenarioJson.Write(Example()));
    }

    [Fact]
    public void AFaultWithNoArgumentsOmitsArgs()
    {
        var scenario = new Scenario("p.json", null, null, null, TimeSpan.FromSeconds(10),
            [new FaultAction(TimeSpan.FromSeconds(1), "CV001.Motor", "thermal-bias", [])]);

        string json = ScenarioJson.Write(scenario);

        Assert.Contains("\"id\": \"thermal-bias\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"args\"", json, StringComparison.Ordinal);

        var fault = Assert.IsType<FaultAction>(ScenarioLoader.Parse(json).Scenario!.Timeline[0]);
        Assert.Empty(fault.Arguments);
        Assert.Equal(json, ScenarioJson.Write(ScenarioLoader.Parse(json).Scenario!));
    }

    [Theory]
    [MemberData(nameof(Corpus.Valid), MemberType = typeof(Corpus))]
    public void EveryValidCorpusFileRoundTripsThroughWrite(string name)
    {
        Scenario first = ScenarioLoader.Parse(Corpus.Text("valid", name)).Scenario!;
        string writtenOnce = ScenarioJson.Write(first);

        ScenarioParseResult reparsed = ScenarioLoader.Parse(writtenOnce);
        Assert.True(reparsed.IsValid, reparsed.ToText());
        string writtenTwice = ScenarioJson.Write(reparsed.Scenario!);

        Assert.Equal(writtenOnce, writtenTwice);
    }
}
