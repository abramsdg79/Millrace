using Dse.Core;
using Dse.Io;

namespace Dse.Configuration.Tests;

/// <summary>
/// Spec 6d §3: a controller entry's <c>claims</c> array is read in the
/// structure stage, handed to <c>AddScanBlock</c>, and every DSE016 Core
/// reports lands on the claim it is about (R133).
/// </summary>
public class ClaimTests
{
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

    /// <summary>An interlock that commands FEED.Enabled and FEED.Permit; <c>CLAIMS</c> is replaced by its claims entry.</summary>
    private const string Int01 =
        """{ "id": "INT01", "type": "interlock", "scanPeriodMs": 100, CLAIMS"parameters": { "conditions": [ { "tag": "CHUTE.Full", "normal": false } ], "trip": [ { "tag": "FEED.Enabled", "value": false }, { "tag": "FEED.Permit", "value": false } ] } }""";

    /// <summary>A second interlock that commands FEED.Enabled only.</summary>
    private const string Int02 =
        """{ "id": "INT02", "type": "interlock", "scanPeriodMs": 100, CLAIMS"parameters": { "conditions": [ { "tag": "PILE.Full", "normal": false } ], "trip": [ { "tag": "FEED.Enabled", "value": false } ] } }""";

    private static string Claiming(string entry, string claims) =>
        entry.Replace("CLAIMS", claims.Length == 0 ? string.Empty : $"\"claims\": {claims}, ", StringComparison.Ordinal);

    private static string Plant(params string[] controllers) =>
        Base.Replace("CONTROLLERS", string.Join(",\n    ", controllers), StringComparison.Ordinal);

    [Fact]
    public void AClaimIsPassedToTheBuilderAndTheTagIsPublishedReadOnly()
    {
        LoadResult result = Plants.Load(Plant(Claiming(Int01, """[ "FEED.Permit" ]""")));

        Assert.True(result.IsValid, result.ToText());
        Simulation simulation = result.Builder!.Build();
        TagDescriptor permit = simulation.IO.Directory.Find("FEED.Permit");
        Assert.Equal((TagAccess.ReadOnly, "INT01"), (permit.Access, permit.ClaimedBy));
        Assert.Equal(string.Empty, simulation.IO.Directory.Find("FEED.Enabled").ClaimedBy);
    }

    [Fact]
    public void AnEntryWithoutClaimsAndOneWithAnEmptyListClaimNothing()
    {
        LoadResult result = Plants.Load(Plant(Claiming(Int01, string.Empty), Claiming(Int02, "[]")));

        Assert.True(result.IsValid, result.ToText());
        Assert.All(result.Builder!.Build().IO.Directory.Tags, t => Assert.Equal(string.Empty, t.ClaimedBy));
    }

    [Theory]
    [InlineData("""[ "FEED.Permt" ]""", "$.controllers[0].claims[0]", "Block 'INT01' claims tag 'FEED.Permt', which the plant does not have.")]
    [InlineData("""[ "FEED.Permit", "CHUTE.Level" ]""", "$.controllers[0].claims[1]", "Block 'INT01' claims tag 'CHUTE.Level', which is read-only.")]
    [InlineData("""[ "FEED.Rate" ]""", "$.controllers[0].claims[0]", "Block 'INT01' claims tag 'FEED.Rate', which it does not command.")]
    [InlineData("""[ "FEED.Permit", "FEED.Permit" ]""", "$.controllers[0].claims[1]", "Block 'INT01' claims tag 'FEED.Permit', which it already claims.")]
    [InlineData("""[ "FEED. Permit" ]""", "$.controllers[0].claims[0]", "Block 'INT01' claims tag 'FEED. Permit', which the plant does not have.")]
    public void AClaimCoreRefusesIsDse016AtTheClaim(string claims, string path, string message)
    {
        ConfigDiagnostic d = Plants.Only(Plant(Claiming(Int01, claims)));

        Assert.Equal(("DSE016", path, message), (d.Code, d.Path, d.Message));
    }

    [Fact]
    public void TheMessageIsSplitIntoSymptomAndFix()
    {
        ConfigDiagnostic d = Plants.Only(Plant(Claiming(Int01, """[ "FEED.Permt" ]""")));
        ConfigDiagnostic spaced = Plants.Only(Plant(Claiming(Int01, """[ "FEED. Permit" ]""")));

        Assert.Equal("Check the name against 'dse tags' — 'FEED.Permit' is closest; a block claims a tag it commands.", d.Fix);
        Assert.Equal(d.Fix, spaced.Fix);
        Assert.Equal("Block 'INT01' claims tag 'FEED. Permit', which the plant does not have.", spaced.Message);
    }

    [Fact]
    public void AClaimNearNoTagKeepsThePlainFix()
    {
        ConfigDiagnostic d = Plants.Only(Plant(Claiming(Int01, """[ "Silo9.RunPermit" ]""")));

        Assert.Equal(("DSE016", "$.controllers[0].claims[0]"), (d.Code, d.Path));
        Assert.Equal("Block 'INT01' claims tag 'Silo9.RunPermit', which the plant does not have.", d.Message);
        Assert.Equal("Check the name against 'dse tags'; a block claims a tag it commands.", d.Fix);
    }

    [Fact]
    public void ATagClaimedByTwoControllersIsDse016AtTheSecondClaim()
    {
        ConfigDiagnostic d = Plants.Only(Plant(Claiming(Int01, """[ "FEED.Enabled" ]"""), Claiming(Int02, """[ "FEED.Enabled" ]""")));

        Assert.Equal(("DSE016", "$.controllers[1].claims[0]"), (d.Code, d.Path));
        Assert.Equal("Block 'INT02' claims tag 'FEED.Enabled', which 'INT01' already claims.", d.Message);
    }

    [Fact]
    public void AnotherControllerCommandingAClaimedTagIsDse016AtTheClaim()
    {
        ConfigDiagnostic d = Plants.Only(Plant(Claiming(Int02, string.Empty), Claiming(Int01, """[ "FEED.Permit", "FEED.Enabled" ]""")));

        Assert.Equal(("DSE016", "$.controllers[1].claims[1]"), (d.Code, d.Path));
        Assert.Equal("Block 'INT02' commands tag 'FEED.Enabled', which 'INT01' claims.", d.Message);
        Assert.Equal("Only the claiming block writes a claimed tag; remove the write from 'INT02', or this claim.", d.Fix);
    }

    [Fact]
    public void ARepeatedClaimAndAnotherWriterAreEachReportedAtTheirOwnClaim()
    {
        LoadResult result = Plants.Load(Plant(Claiming(Int02, string.Empty), Claiming(Int01, """[ "FEED.Enabled", "FEED.Enabled" ]""")));

        Assert.Equal(
            new[]
            {
                ("$.controllers[1].claims[1]", "Block 'INT01' claims tag 'FEED.Enabled', which it already claims."),
                ("$.controllers[1].claims[0]", "Block 'INT02' commands tag 'FEED.Enabled', which 'INT01' claims."),
            },
            result.Diagnostics.Select(d => (d.Path, d.Message)));
    }

    [Theory]
    [InlineData("\"FEED.Permit\"", "$.controllers[0].claims", "\"claims\" must be an array of tag names.")]
    [InlineData("[ \"FEED.Permit\", 7 ]", "$.controllers[0].claims[1]", "A claim is a tag's full name, as a string.")]
    public void AClaimsListThatIsNotAnArrayOfStringsIsDse103(string claims, string path, string message)
    {
        ConfigDiagnostic d = Plants.Only(Plant(Claiming(Int01, claims)));

        Assert.Equal(("DSE103", path, message), (d.Code, d.Path, d.Message));
    }
}
