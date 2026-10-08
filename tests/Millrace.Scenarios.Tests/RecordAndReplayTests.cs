using Millrace.Configuration;
using Millrace.Core;
using Millrace.Core.Faults;
using Millrace.Realtime;

namespace Millrace.Scenarios.Tests;

public class RecordAndReplayTests
{
    /// <summary>
    /// A live run driven the way a real one is driven — a command bus for the
    /// write, the fault API for the rest — recorded, written, parsed and
    /// replayed. The two logs must be byte-identical, which is only true
    /// because a scheduled write lands on the tick it names.
    /// </summary>
    private static (Simulation Live, ScenarioRecorder Recorder, LoadResult Load, string PlantJson) LiveRun()
    {
        string plantJson = File.ReadAllText(Corpus.PlantPath("conveyor-line.json"));
        LoadResult load = PlantLoader.Load(plantJson, Corpus.Catalogue);
        Assert.True(load.IsValid, load.ToText());

        Simulation live = load.Builder!.Build();
        var recorder = new ScenarioRecorder();
        live.AttachActionRecorder(recorder);
        var bus = new CommandBus(live.IO);

        live.RunFor(TimeSpan.FromSeconds(5));
        Assert.Equal(CommandOutcome.Accepted, bus.WriteBool("CV001.Start", true));
        live.InjectFaultAt(
            TimeSpan.FromSeconds(30), "CV001.Motor", "thermal-bias", new FaultArguments(new FaultArgument("amount", 0.8)));
        live.ClearFaultAt(TimeSpan.FromMilliseconds(60500), "CV001.Motor", "thermal-bias");
        live.RunFor(TimeSpan.FromSeconds(115));

        return (live, recorder, load, plantJson);
    }

    [Fact]
    public void TheRecordingIsTheScenarioFileAPersonWouldHaveWritten()
    {
        (_, ScenarioRecorder recorder, LoadResult load, _) = LiveRun();

        string json = ScenarioJson.Write(recorder.ToScenario("conveyor-line.json", load.Options!, TimeSpan.FromSeconds(120)));

        Assert.Equal(
            """
            {
              "plant": "conveyor-line.json",
              "seed": 1,
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
    public void TheReplayOfARecordedRunIsByteIdenticalToIt()
    {
        (Simulation live, ScenarioRecorder recorder, LoadResult load, string plantJson) = LiveRun();

        Scenario recorded = recorder.ToScenario("conveyor-line.json", load.Options!, TimeSpan.FromSeconds(120));
        ScenarioParseResult parsed = ScenarioLoader.Parse(ScenarioJson.Write(recorded));
        Assert.Empty(parsed.Diagnostics);

        ScenarioRunResult replay = ScenarioRunner.Run(parsed.Scenario!, plantJson, Corpus.Catalogue);

        Assert.True(replay.IsValid, replay.ToText());
        Assert.Equal(live.Events.ToText(), replay.Events!.ToText());
        Assert.Equal(12000L, replay.Summary!.Ticks);
        Assert.Equal(live.Events.Records.Count, replay.Summary.Events);
        Assert.NotEmpty(live.Events.Records);
    }
}
