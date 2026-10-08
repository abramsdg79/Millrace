using Millrace.Components;
using Millrace.Configuration;
using Millrace.Control.Catalogue;
using Millrace.Core;
using Millrace.Core.Catalogue;
using Millrace.Core.Faults;
using Millrace.Io;

namespace Millrace.Scenarios.Tests;

/// <summary>
/// R77, closed: a recording of a run with control blocks holds the external
/// actions only. Replaying them re-runs the blocks, which issue their own writes
/// again, so the replay's event log is the live run's byte for byte.
/// </summary>
public class ControlledRecordAndReplayTests
{
    private static readonly ComponentCatalogue Catalogue =
        new CatalogueBuilder().Add<ComponentsModule>().Add<ControlModule>().Build();

    private static (Simulation Live, ScenarioRecorder Recorder, LoadResult Load, string PlantJson) LiveRun()
    {
        string plantJson = File.ReadAllText(Corpus.PlantPath("conveyor-control.json"));
        LoadResult load = PlantLoader.Load(plantJson, Catalogue);
        Assert.True(load.IsValid, load.ToText());

        Simulation live = load.Builder!.Build();
        var recorder = new ScenarioRecorder();
        live.AttachActionRecorder(recorder);
        live.WriteAt(TimeSpan.FromSeconds(1), "SEQ01.Start", TagValue.Bool(true));
        live.WriteAt(TimeSpan.FromSeconds(2), "SEQ01.Start", TagValue.Bool(false));
        live.InjectFaultAt(
            TimeSpan.FromSeconds(40), "CV001.Motor", "thermal-bias", new FaultArguments(new FaultArgument("amount", 0.8)));
        live.RunFor(TimeSpan.FromSeconds(120));

        return (live, recorder, load, plantJson);
    }

    [Fact]
    public void ARecordingOfAControlledRunHoldsOnlyTheExternalActions()
    {
        (Simulation live, ScenarioRecorder recorder, LoadResult load, _) = LiveRun();

        Assert.Contains("CV001.Start  WRITE  Set to false by INT01.", live.Events.ToText(), StringComparison.Ordinal);
        Assert.Equal(3, recorder.Count);
        Assert.Equal(
            """
            {
              "plant": "conveyor-control.json",
              "seed": 1,
              "startTime": "2026-01-01T06:00:00Z",
              "timeStepMs": 10,
              "duration": 120,
              "timeline": [
                {
                  "at": 1,
                  "write": "SEQ01.Start",
                  "value": true
                },
                {
                  "at": 2,
                  "write": "SEQ01.Start",
                  "value": false
                },
                {
                  "at": 40,
                  "fault": "CV001.Motor",
                  "id": "thermal-bias",
                  "args": {
                    "amount": 0.8
                  }
                }
              ]
            }

            """.ReplaceLineEndings("\n"),
            ScenarioJson.Write(recorder.ToScenario("conveyor-control.json", load.Options!, TimeSpan.FromSeconds(120))));
    }

    [Fact]
    public void TheReplayOfAControlledRunIsByteIdenticalToIt()
    {
        (Simulation live, ScenarioRecorder recorder, LoadResult load, string plantJson) = LiveRun();

        Scenario recorded = recorder.ToScenario("conveyor-control.json", load.Options!, TimeSpan.FromSeconds(120));
        ScenarioParseResult parsed = ScenarioLoader.Parse(ScenarioJson.Write(recorded));
        Assert.Empty(parsed.Diagnostics);

        ScenarioRunResult replay = ScenarioRunner.Run(parsed.Scenario!, plantJson, Catalogue);

        Assert.True(replay.IsValid, replay.ToText());
        Assert.Equal(live.Events.ToText(), replay.Events!.ToText());
        Assert.Equal(12000L, replay.Summary!.Ticks);
        Assert.Equal(live.Events.Records.Count, replay.Summary.Events);
    }
}
