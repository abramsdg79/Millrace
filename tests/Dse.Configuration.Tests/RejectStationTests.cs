using Dse.Core;
using Dse.Io;

namespace Dse.Configuration.Tests;

/// <summary>Spec 6b.1 criteria 3-5 in a plant file: a reject gate, a pyrometer aimed at it, and a coil that claims its Reject.</summary>
public class RejectStationTests
{
    private static string Plant => Corpus.Read("valid", "reject-station.json");

    [Fact]
    public void TheGateThePyrometerOnItAndTheCoilsClaimBind()
    {
        LoadResult result = Plants.Load(Plant);

        Assert.True(result.IsValid, result.ToText());
        Simulation simulation = result.Builder!.Build();
        TagDescriptor reject = simulation.IO.Directory.Find("GATE.Reject");
        Assert.Equal((TagKind.Bool, TagAccess.ReadOnly, "COIL01"), (reject.Kind, reject.Access, reject.ClaimedBy));
        Assert.Equal(TagAccess.ReadOnly, simulation.IO.Directory.Find("GATE.Occupied").Access);
        Assert.Equal(TagKind.Int64, simulation.IO.Directory.Find("GATE.Rejected").Kind);
        Assert.Equal(TagAccess.ReadOnly, simulation.IO.Directory.Find("COIL01.Energised").Access);

        simulation.RunFor(TimeSpan.FromSeconds(1));

        // The coil's first scan, at tick 0, writes the alarm's power-up state; it lands on tick 1.
        Assert.Equal(
            (1L, "Set to false by COIL01."),
            simulation.Events.Records.Where(r => r.Source == "GATE.Reject").Select(r => (r.Tick, r.Message)).Single());
        Assert.False(simulation.IO.ReadBool("COIL01.Energised"));
    }

    [Fact]
    public void ACoilWhoseOutputIsReadOnlyIsDse115AtTheOutput()
    {
        ConfigDiagnostic d = Plants.Only(Plant.Replace("\"output\": \"GATE.Reject\"", "\"output\": \"GATE.Occupied\"", StringComparison.Ordinal)
            .Replace("\"claims\": [ \"GATE.Reject\" ],", string.Empty, StringComparison.Ordinal));   // a coil may not claim what it does not command

        Assert.Equal(("DSE115", "$.controllers[1].parameters.output"), (d.Code, d.Path));
    }
}
