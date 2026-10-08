using System.Text.Json.Nodes;

namespace Millrace.Cli.Tests;

public class ModbusMapCommandTests
{
    [Fact]
    public void TheTextMapListsEveryTagByAreaThenAddressWithBothAddressings()
    {
        CliRun run = Cli.Run("modbus-map", Cli.Plant("claimed-permit.json"));

        Assert.Equal(ExitCodes.Ok, run.ExitCode);
        Assert.Empty(run.Err);
        Assert.Equal(
            """
            area               address  offset  type     tag              access     unit      description
            coils                    1       0  Bool     FEED.Enabled     ReadWrite            Feeder enabled
            coils                    2       1  Bool     INT01.Reset      ReadWrite            Clears the latch on a rising edge when every condition is normal
            discrete inputs          1       0  Bool     CHUTE.Full       ReadOnly             At capacity
            discrete inputs          2       1  Bool     FEED.Permit      ReadOnly             Run permit; false stops the feeder (claimed by INT01)
            discrete inputs          3       2  Bool     INT01.Ok         ReadOnly             Not tripped
            discrete inputs          4       3  Bool     INT01.Tripped    ReadOnly             Latched by an abnormal condition
            discrete inputs          5       4  Bool     PILE.Full        ReadOnly             At capacity
            input registers          1       0  Float32  CHUTE.Level      ReadOnly   fraction  Held mass over capacity
            input registers          3       2  Float32  FEED.HopperMass  ReadOnly   kg        Mass in the hopper
            input registers          5       4  Int32    INT01.FirstOut   ReadOnly   count     Index of the condition that tripped, or -1
            input registers          7       6  Float32  PILE.Rate        ReadOnly   kg/s      Receiving rate
            input registers          9       8  Float32  PILE.Received    ReadOnly   kg        Cumulative mass received
            holding registers        1       0  Float32  FEED.Rate        ReadWrite  kg/s      Feed rate

            """,
            run.Out);
    }

    [Fact]
    public void TheCsvMapHasAHeaderAndOneRowPerTagQuotedWhereItMustBe()
    {
        CliRun run = Cli.Run("modbus-map", Cli.Plant("claimed-permit.json"), "--format", "csv");

        Assert.Equal(ExitCodes.Ok, run.ExitCode);
        string[] lines = run.Out.Split('\n');
        Assert.Equal(15, lines.Length);
        Assert.Equal("area,address,offset,type,tag,access,unit,description,claimedBy", lines[0]);
        Assert.Equal("discrete inputs,2,1,Bool,FEED.Permit,ReadOnly,,Run permit; false stops the feeder,INT01", lines[4]);
        Assert.Equal("input registers,5,4,Int32,INT01.FirstOut,ReadOnly,count,\"Index of the condition that tripped, or -1\",", lines[10]);
        Assert.Equal("holding registers,1,0,Float32,FEED.Rate,ReadWrite,kg/s,Feed rate,", lines[13]);
        Assert.Equal(string.Empty, lines[14]);
    }

    [Fact]
    public void TheFuxaMapIsADeviceTagsObjectInDirectoryOrderKeyedByStableIds()
    {
        CliRun run = Cli.Run("modbus-map", Cli.Plant("claimed-permit.json"), "--format", "fuxa");
        CliRun tags = Cli.Run("tags", Cli.Plant("claimed-permit.json"));

        Assert.Equal(ExitCodes.Ok, run.ExitCode);
        JsonObject map = JsonNode.Parse(run.Out)!.AsObject();
        Assert.Equal(
            tags.Out.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => "t_" + l.Split("  ")[0]),
            map.Select(p => p.Key));
        Assert.True(JsonNode.DeepEquals(
            JsonNode.Parse("""
                { "id": "t_INT01.FirstOut", "name": "INT01.FirstOut", "type": "Int32", "memaddress": "300000",
                  "address": "5", "description": "Index of the condition that tripped, or -1 (count)" }
                """),
            map["t_INT01.FirstOut"]));
        Assert.Equal("000000", (string?)map["t_FEED.Enabled"]!["memaddress"]);
        Assert.Equal("100000", (string?)map["t_FEED.Permit"]!["memaddress"]);
        Assert.Equal("400000", (string?)map["t_FEED.Rate"]!["memaddress"]);
        Assert.Equal("Float32", (string?)map["t_FEED.Rate"]!["type"]);
        Assert.Equal("Bool", (string?)map["t_FEED.Enabled"]!["type"]);
    }

    [Fact]
    public void OutWritesTheMapToAFile()
    {
        string path = Path.Combine(Path.GetTempPath(), $"millrace-map-{Guid.NewGuid():N}.csv");
        try
        {
            CliRun run = Cli.Run("modbus-map", Cli.Plant("minimal.json"), "--format", "csv", "--out", path);

            Assert.Equal(ExitCodes.Ok, run.ExitCode);
            Assert.Empty(run.Out);
            Assert.StartsWith("area,address,offset,", File.ReadAllText(path), StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("modbus-map a.json --format json")]
    [InlineData("modbus-map a.json --format xml")]
    [InlineData("modbus-map a.json --port 5020")]
    [InlineData("modbus-map")]
    public void MalformedMapInvocationsAreUsageErrors(string line)
    {
        CliRun run = Cli.Run(line.Split(' '));

        Assert.Equal(ExitCodes.Usage, run.ExitCode);
        Assert.Empty(run.Out);
        Assert.Contains("millrace modbus-map --help", run.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void AnInvalidPlantBehavesAsValidateDoes()
    {
        CliRun run = Cli.Run("modbus-map", Cli.Plant("broken.json"));

        Assert.Equal(ExitCodes.PlantInvalid, run.ExitCode);
        Assert.Empty(run.Out);
        Assert.Contains("MR102", run.Err, StringComparison.Ordinal);
    }
}
