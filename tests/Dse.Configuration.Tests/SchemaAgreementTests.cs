using System.Text.Json;
using Json.Schema;

namespace Dse.Configuration.Tests;

/// <summary>
/// The schema and the loader are two validators. For structure they must agree;
/// for meaning only the loader can speak. Both halves are asserted, so the
/// boundary between them is documented by a test rather than by hope.
/// </summary>
public class SchemaAgreementTests
{
    /// <summary>What an off-the-shelf validator can see: unknown keys, unknown component types, bad parameters.</summary>
    private static readonly string[] Structural = ["DSE101", "DSE102", "DSE103"];

    private static readonly JsonSchema Schema = JsonSchema.FromText(PlantSchema.Generate(Plants.Catalogue));

    // JsonSchema.Net 8.x's Evaluate takes a JsonElement rather than a JsonNode; adapted per the brief's fallback.
    private static bool Accepts(string plantJson) => Schema.Evaluate(JsonDocument.Parse(plantJson).RootElement).IsValid;

    [Fact]
    public void TheSchemaIsItselfValidDraft202012()
    {
        JsonElement schemaAsData = JsonDocument.Parse(PlantSchema.Generate(Plants.Catalogue)).RootElement;

        Assert.True(MetaSchemas.Draft202012.Evaluate(schemaAsData).IsValid);
    }

    [Theory]
    [MemberData(nameof(Corpus.Valid), MemberType = typeof(Corpus))]
    public void TheSchemaAcceptsEveryPlantTheLoaderAccepts(string name)
    {
        Assert.True(Accepts(Corpus.Read("valid", name)), $"The schema rejected valid plant '{name}'.");
    }

    [Theory]
    [MemberData(nameof(Corpus.Invalid), MemberType = typeof(Corpus))]
    public void TheSchemaRejectsStructuralErrorsAndOnlyThose(string name)
    {
        string code = name[..6];
        if (code == "DSE100")
        {
            return; // Not JSON at all; there is nothing to hand a schema validator.
        }

        bool accepted = Accepts(Corpus.Read("invalid", name));

        if (Structural.Contains(code))
        {
            Assert.False(accepted, $"'{name}' has a structural error the schema should catch, but the schema accepted it.");
        }
        else
        {
            Assert.True(accepted, $"'{name}' has a semantic error only the loader can see, but the schema rejected it — it is stricter than the loader somewhere.");
        }
    }

    [Theory]
    [InlineData("\"ratio\": 20", "\"ratio\": \"20\"")]
    [InlineData("\"ratio\": 20", "\"ratio\": 0")]
    [InlineData("\"ratio\": 20", "\"ratoi\": 20")]
    [InlineData("\"type\": \"gearbox\"", "\"type\": \"gearbocks\"")]
    [InlineData("\"id\": \"GB\"", "\"id\": \"G B\"")]
    [InlineData("\"id\": \"GB\"", "\"id\": \"G.B\"")]
    [InlineData("\"id\": \"GB\", ", "")]
    public void BothValidatorsRejectTheSameStructuralMistakes(string from, string to)
    {
        const string Good = """{ "components": [ { "id": "GB", "type": "gearbox", "parameters": { "ratio": 20 } } ] }""";
        string bad = Good.Replace(from, to, StringComparison.Ordinal);

        Assert.True(Accepts(Good));
        Assert.True(Plants.Load(Good).IsValid);
        Assert.False(Accepts(bad));
        Assert.False(Plants.Load(bad).IsValid);
    }
}
