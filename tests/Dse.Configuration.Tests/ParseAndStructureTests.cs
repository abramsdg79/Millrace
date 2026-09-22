namespace Dse.Configuration.Tests;

public class ParseAndStructureTests
{
    [Fact]
    public void TheMinimalPlantHasNoDiagnostics()
    {
        LoadResult result = Plants.Load(Plants.Minimal);

        Assert.Empty(result.Diagnostics);
        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("2026-01-01T06:00:00Z")]
    [InlineData("2026-01-01T06:00:00+09:00")]
    [InlineData("2026-01-01T06:00:00.1234567Z")]
    public void StartTimeWithAnOffsetLoads(string startTime)
    {
        string json = Plants.Minimal.Replace("\"2026-01-01T06:00:00Z\"", $"\"{startTime}\"", StringComparison.Ordinal);

        Assert.Empty(Plants.Load(json).Diagnostics);
    }

    [Fact]
    public void CommentsAndTrailingCommasAreAllowed()
    {
        string json = Plants.Minimal
            .Replace("\"components\": [", "// the line\n  \"components\": [", StringComparison.Ordinal)
            .Replace("{ \"id\": \"PILE\", \"type\": \"bulk-sink\" }", "{ \"id\": \"PILE\", \"type\": \"bulk-sink\" },", StringComparison.Ordinal);

        Assert.Empty(Plants.Load(json).Diagnostics);
    }

    [Fact]
    public void ASyntaxErrorGivesLineAndColumn()
    {
        ConfigDiagnostic d = Plants.Only("{\n  \"components\": [ { \"id\": }\n}");

        Assert.Equal("DSE100", d.Code);
        Assert.Equal("$", d.Path);
        Assert.Contains("line 2", d.Message, StringComparison.Ordinal);
        Assert.Contains("column", d.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheRootMustBeAnObject()
    {
        ConfigDiagnostic d = Plants.Only("[]");

        Assert.Equal("DSE103", d.Code);
        Assert.Equal("$", d.Path);
    }

    [Fact]
    public void AnUnknownTopLevelKeySuggestsTheNearest()
    {
        ConfigDiagnostic d = Plants.Only(Plants.Minimal.Replace("\"flows\":", "\"flow\":", StringComparison.Ordinal));

        Assert.Equal("DSE101", d.Code);
        Assert.Equal("$.flow", d.Path);
        Assert.Contains("'flows' is closest", d.Fix, StringComparison.Ordinal);
    }

    [Fact]
    public void ASchemaKeyIsAllowed()
    {
        string json = Plants.Minimal.Replace("\"defaults\":", "\"$schema\": \"./dse-plant.schema.json\",\n  \"defaults\":", StringComparison.Ordinal);

        Assert.Empty(Plants.Load(json).Diagnostics);
    }

    [Fact]
    public void ComponentsAreRequired()
    {
        ConfigDiagnostic d = Plants.Only("{}");

        Assert.Equal("DSE103", d.Code);
        Assert.Equal("$.components", d.Path);
    }

    [Theory]
    [InlineData("\"seed\": 1", "\"seed\": -1", "$.defaults.seed")]
    [InlineData("\"seed\": 1", "\"seed\": 1.5", "$.defaults.seed")]
    [InlineData("\"timeStepMs\": 10", "\"timeStepMs\": 0", "$.defaults.timeStepMs")]
    [InlineData("\"timeStepMs\": 10", "\"timeStepMs\": \"10\"", "$.defaults.timeStepMs")]
    [InlineData("\"timeStepMs\": 10", "\"timeStepMs\": 1e30", "$.defaults.timeStepMs")]
    [InlineData("\"timeStepMs\": 10", "\"timeStepMs\": 0.00001", "$.defaults.timeStepMs")]
    [InlineData("\"startTime\": \"2026-01-01T06:00:00Z\"", "\"startTime\": \"yesterday\"", "$.defaults.startTime")]
    [InlineData("\"startTime\": \"2026-01-01T06:00:00Z\"", "\"startTime\": \"2026-01-01T06:00:00\"", "$.defaults.startTime")]
    public void BadDefaultsAreParameterErrors(string from, string to, string path)
    {
        ConfigDiagnostic d = Plants.Only(Plants.Minimal.Replace(from, to, StringComparison.Ordinal));

        Assert.Equal("DSE103", d.Code);
        Assert.Equal(path, d.Path);
    }

    [Fact]
    public void ASubTickTimeStepIsReportedNotCrashed()
    {
        ConfigDiagnostic d = Plants.Only(
            Plants.Minimal.Replace("\"timeStepMs\": 10", "\"timeStepMs\": 0.00001", StringComparison.Ordinal));

        Assert.Equal("DSE103", d.Code);
        Assert.Equal("$.defaults.timeStepMs", d.Path);
        Assert.Equal("\"timeStepMs\" must be at least one tick (0.0001 ms).", d.Message);
        Assert.Equal("Use the simulation step in milliseconds, such as 10.", d.Fix);
    }

    [Fact]
    public void ATimeStepLongerThanADayIsReportedNotCrashed()
    {
        ConfigDiagnostic d = Plants.Only(
            Plants.Minimal.Replace("\"timeStepMs\": 10", "\"timeStepMs\": 1e30", StringComparison.Ordinal));

        Assert.Equal("DSE103", d.Code);
        Assert.Equal("$.defaults.timeStepMs", d.Path);
        Assert.Equal("\"timeStepMs\" is 1E+30 ms, which is longer than a day.", d.Message);
        Assert.Equal("Use the simulation step in milliseconds, such as 10.", d.Fix);
    }

    [Fact]
    public void AnUnknownDefaultsKeyIsReported()
    {
        ConfigDiagnostic d = Plants.Only(Plants.Minimal.Replace("\"seed\": 1", "\"sead\": 1", StringComparison.Ordinal));

        Assert.Equal("DSE101", d.Code);
        Assert.Equal("$.defaults.sead", d.Path);
    }

    [Fact]
    public void AnUnknownComponentTypeSuggestsTheNearest()
    {
        ConfigDiagnostic d = Plants.Only(Plants.Minimal.Replace("\"bulk-sink\"", "\"bulk-snik\"", StringComparison.Ordinal));

        Assert.Equal("DSE102", d.Code);
        Assert.Equal("$.components[2].type", d.Path);
        Assert.Contains("'bulk-sink' is closest", d.Fix, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnknownTypeMentionsAssemblies()
    {
        ConfigDiagnostic d = Plants.Only(Plants.Minimal.Replace("\"bulk-sink\"", "\"furnace\"", StringComparison.Ordinal));

        Assert.Contains("--assembly", d.Fix, StringComparison.Ordinal);
    }

    [Fact]
    public void AParameterErrorCarriesTheFullPath()
    {
        ConfigDiagnostic d = Plants.Only(Plants.Minimal.Replace("\"capacityKg\": 200", "\"capacityKg\": -1", StringComparison.Ordinal));

        Assert.Equal("DSE103", d.Code);
        Assert.Equal("$.components[1].parameters.capacityKg", d.Path);
    }

    [Fact]
    public void AMissingParametersObjectReportsTheRequiredParameters()
    {
        ConfigDiagnostic d = Plants.Only(Plants.Minimal.Replace(", \"parameters\": { \"capacityKg\": 200 }", string.Empty, StringComparison.Ordinal));

        Assert.Equal("DSE103", d.Code);
        Assert.Equal("$.components[1].parameters.capacityKg", d.Path);
    }

    [Fact]
    public void AnUnknownMaterialIsAnUnknownType()
    {
        ConfigDiagnostic d = Plants.Only(Plants.Minimal.Replace("\"material\": \"ore\"", "\"material\": \"oar\"", StringComparison.Ordinal));

        Assert.Equal("DSE102", d.Code);
        Assert.Equal("$.components[0].parameters.material", d.Path);
        Assert.Contains("'ore' is closest", d.Fix, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("\"id\": \"CHUTE\"", "\"id\": \"FEED\"", "DSE107", "$.components[1].id")]
    [InlineData("\"id\": \"CHUTE\"", "\"id\": \"CH.UTE\"", "DSE103", "$.components[1].id")]
    [InlineData("\"id\": \"CHUTE\"", "\"id\": \"CH UTE\"", "DSE103", "$.components[1].id")]
    [InlineData("\"id\": \"CHUTE\"", "\"id\": 7", "DSE103", "$.components[1].id")]
    [InlineData("\"id\": \"CHUTE\", ", "", "DSE103", "$.components[1].id")]
    [InlineData("\"id\": \"CHUTE\"", "\"id\": \"CHUTE\", \"colour\": \"red\"", "DSE101", "$.components[1].colour")]
    public void ComponentEnvelopeErrors(string from, string to, string code, string path)
    {
        // The flows still name CHUTE, but the wire stage never runs once structure has failed.
        ConfigDiagnostic d = Plants.Only(Plants.Minimal.Replace(from, to, StringComparison.Ordinal));

        Assert.Equal(code, d.Code);
        Assert.Equal(path, d.Path);
    }

    [Fact]
    public void AMaterialDefinedTwiceIsADuplicate()
    {
        string json = Plants.Minimal.Replace(
            "\"materials\": [", "\"materials\": [\n    { \"name\": \"ore\", \"kind\": \"bulk\" },", StringComparison.Ordinal);

        ConfigDiagnostic d = Plants.Only(json);

        Assert.Equal("DSE107", d.Code);
        Assert.Equal("$.materials[1].name", d.Path);
    }

    [Fact]
    public void AMaterialMayDeclareStatesAndOmitProperties()
    {
        string json = Plants.Minimal.Replace(
            "\"materials\": [", "\"materials\": [\n    { \"name\": \"dough\", \"kind\": \"bulk\", \"states\": [ \"proof\", \"bake\" ] },", StringComparison.Ordinal);

        Assert.Empty(Plants.Load(json).Diagnostics);
    }

    [Fact]
    public void ARepeatedStateNameIsAParameterError()
    {
        string json = Plants.Minimal.Replace(
            "\"materials\": [", "\"materials\": [\n    { \"name\": \"dough\", \"kind\": \"bulk\", \"states\": [ \"proof\", \"proof\" ] },", StringComparison.Ordinal);

        ConfigDiagnostic d = Plants.Only(json);

        Assert.Equal("DSE103", d.Code);
        Assert.Equal("$.materials[0].states", d.Path);
    }

    [Fact]
    public void ALinkNeedsFromAndTo()
    {
        ConfigDiagnostic d = Plants.Only(Plants.Minimal.Replace("{ \"from\": \"CHUTE.Out\", \"to\": \"PILE.In\" }", "{ \"from\": \"CHUTE.Out\" }", StringComparison.Ordinal));

        Assert.Equal("DSE103", d.Code);
        Assert.Equal("$.flows[1].to", d.Path);
    }

    [Fact]
    public void ATagAccessMustBeReadOrWrite()
    {
        string json = Plants.Minimal.Replace(
            "\"flows\": [", "\"tags\": [ { \"name\": \"FEED.RUN\", \"port\": \"FEED.Enabled\", \"access\": \"rw\" } ],\n  \"flows\": [", StringComparison.Ordinal);

        ConfigDiagnostic d = Plants.Only(json);

        Assert.Equal("DSE103", d.Code);
        Assert.Equal("$.tags[0].access", d.Path);
        Assert.Contains("read, write", d.Fix, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryStructuralErrorIsReportedTogether()
    {
        string json = Plants.Minimal
            .Replace("\"bulk-sink\"", "\"bulk-snik\"", StringComparison.Ordinal)
            .Replace("\"capacityKg\": 200", "\"capacityKg\": -1", StringComparison.Ordinal)
            .Replace("\"rateKgPerS\": 20", "\"rateKgPerSec\": 20", StringComparison.Ordinal);

        LoadResult result = Plants.Load(json);

        Assert.False(result.IsValid);
        Assert.Null(result.Builder);
        Assert.Equal(
            ["DSE101", "DSE102", "DSE103", "DSE103"],
            result.Diagnostics.Select(d => d.Code).Order(StringComparer.Ordinal));
    }
}
