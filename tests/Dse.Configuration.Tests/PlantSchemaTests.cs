using System.Text.Json;
using Dse.Configuration.Loading;
using Dse.Tests.Shared;

namespace Dse.Configuration.Tests;

public class PlantSchemaTests
{
    private static readonly string Text = PlantSchema.Generate(Plants.Catalogue);

    private static JsonElement Defs(JsonDocument document) => document.RootElement.GetProperty("$defs");

    [Fact]
    public void DeclaresItsDialectAndClosesTheRoot()
    {
        using JsonDocument document = JsonDocument.Parse(Text);
        JsonElement root = document.RootElement;

        Assert.Equal(PlantSchema.Dialect, root.GetProperty("$schema").GetString());
        Assert.False(root.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal("components", Assert.Single(root.GetProperty("required").EnumerateArray()).GetString());
        Assert.Equal(
            ["$schema", "defaults", "materials", "components", "signals", "flows", "tags"],
            root.GetProperty("properties").EnumerateObject().Select(p => p.Name));
    }

    [Fact]
    public void TheRootAndDefaultsKeySetsMatchTheirSchemas()
    {
        using JsonDocument document = JsonDocument.Parse(Text);
        JsonElement root = document.RootElement;

        Assert.Equal(PlantSchemas.TopLevelKeys, root.GetProperty("properties").EnumerateObject().Select(p => p.Name));
        Assert.Equal(
            PlantSchemas.DefaultsKeys,
            root.GetProperty("properties").GetProperty("defaults").GetProperty("properties").EnumerateObject().Select(p => p.Name));
    }

    [Fact]
    public void HasOneBranchPerComponentType()
    {
        using JsonDocument document = JsonDocument.Parse(Text);
        string[] branches = document.RootElement
            .GetProperty("properties").GetProperty("components").GetProperty("items").GetProperty("oneOf")
            .EnumerateArray().Select(b => b.GetProperty("$ref").GetString()!).ToArray();

        Assert.Equal(Plants.Catalogue.Components.Select(c => $"#/$defs/component.{c.Type}"), branches);
    }

    [Fact]
    public void AComponentBranchPinsItsTypeAndClosesItsParameters()
    {
        using JsonDocument document = JsonDocument.Parse(Text);
        JsonElement gearbox = Defs(document).GetProperty("component.gearbox");
        JsonElement parameters = gearbox.GetProperty("properties").GetProperty("parameters");

        Assert.Equal("gearbox", gearbox.GetProperty("properties").GetProperty("type").GetProperty("const").GetString());
        Assert.Equal(["id", "type", "parameters"], gearbox.GetProperty("required").EnumerateArray().Select(r => r.GetString()));
        Assert.False(gearbox.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal("ratio", Assert.Single(parameters.GetProperty("required").EnumerateArray()).GetString());
        Assert.False(parameters.GetProperty("additionalProperties").GetBoolean());

        JsonElement efficiency = parameters.GetProperty("properties").GetProperty("efficiency");
        Assert.Equal("number", efficiency.GetProperty("type").GetString());
        Assert.Equal(0.0, efficiency.GetProperty("exclusiveMinimum").GetDouble());
        Assert.Equal(1.0, efficiency.GetProperty("maximum").GetDouble());
        Assert.Equal(0.95, efficiency.GetProperty("default").GetDouble());
        Assert.False(efficiency.TryGetProperty("minimum", out _));
    }

    [Fact]
    public void AComponentWithNoRequiredParameterDoesNotRequireTheObject()
    {
        using JsonDocument document = JsonDocument.Parse(Text);
        JsonElement sink = Defs(document).GetProperty("component.bulk-sink");

        Assert.Equal(["id", "type"], sink.GetProperty("required").EnumerateArray().Select(r => r.GetString()));
    }

    [Fact]
    public void ASharedGroupIsDefinedOnceAndReferenced()
    {
        using JsonDocument document = JsonDocument.Parse(Text);
        JsonElement defs = Defs(document);

        string motorRef = defs.GetProperty("component.motor").GetProperty("properties").GetProperty("parameters")
            .GetProperty("properties").GetProperty("rating").GetProperty("$ref").GetString()!;
        string conveyorRef = defs.GetProperty("component.conveyor").GetProperty("properties").GetProperty("parameters")
            .GetProperty("properties").GetProperty("motor").GetProperty("$ref").GetString()!;

        Assert.Equal("#/$defs/group.MotorRating", motorRef);
        Assert.Equal(motorRef, conveyorRef);
        Assert.Equal(3, defs.GetProperty("group.MotorRating").GetProperty("required").GetArrayLength());
    }

    [Fact]
    public void ASlotIsAOneOfOverItsTypesAndATypePinsItsName()
    {
        using JsonDocument document = JsonDocument.Parse(Text);
        JsonElement defs = Defs(document);

        Assert.Equal(5, defs.GetProperty("object.hold").GetProperty("oneOf").GetArrayLength());
        Assert.Equal(3, defs.GetProperty("object.transform").GetProperty("oneOf").GetArrayLength());

        JsonElement all = defs.GetProperty("object.hold.all");
        Assert.Equal("all", all.GetProperty("properties").GetProperty("type").GetProperty("const").GetString());
        Assert.Equal(["type", "conditions"], all.GetProperty("required").EnumerateArray().Select(r => r.GetString()));
        JsonElement conditions = all.GetProperty("properties").GetProperty("conditions");
        Assert.Equal(1, conditions.GetProperty("minItems").GetInt32());
        Assert.Equal("#/$defs/object.hold", conditions.GetProperty("items").GetProperty("$ref").GetString());
    }

    [Fact]
    public void AReferenceIsAStringThatSaysTheLoaderChecksIt()
    {
        using JsonDocument document = JsonDocument.Parse(Text);
        JsonElement belt = Defs(document).GetProperty("component.belt-scale").GetProperty("properties").GetProperty("parameters")
            .GetProperty("properties").GetProperty("belt");

        Assert.Equal("string", belt.GetProperty("type").GetString());
        Assert.Contains("IMaterialObservable", belt.GetProperty("description").GetString(), StringComparison.Ordinal);
        Assert.Contains("loader", belt.GetProperty("description").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void DefsAreSortedAndTheTextIsDeterministic()
    {
        using JsonDocument document = JsonDocument.Parse(Text);
        string[] keys = Defs(document).EnumerateObject().Select(p => p.Name).ToArray();

        Assert.Equal(keys.Order(StringComparer.Ordinal), keys);
        Assert.Equal(Text, PlantSchema.Generate(Plants.Catalogue));
        Assert.DoesNotContain('\r', Text);
        Assert.EndsWith("}\n", Text, StringComparison.Ordinal);
    }

    [Fact]
    public void MatchesTheGoldenFile()
    {
        Golden.Assert("Golden/plant.schema.json", Text);
    }
}
