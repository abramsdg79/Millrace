using Dse.Components;
using Dse.Core.Catalogue;

namespace Dse.Configuration.Tests;

/// <summary>The catalogue and a smallest-useful plant that loader tests mutate with <c>Replace</c>.</summary>
internal static class Plants
{
    public static ComponentCatalogue Catalogue { get; } = new CatalogueBuilder().Add<ComponentsModule>().Build();

    /// <summary>Feed → chute → pile. Valid.</summary>
    public const string Minimal = """
        {
          "defaults": { "seed": 1, "timeStepMs": 10, "startTime": "2026-01-01T06:00:00Z" },
          "materials": [
            { "name": "ore", "kind": "bulk", "properties": { "density": 2000, "moisture": 0.03, "temperature": 15 } }
          ],
          "components": [
            { "id": "FEED", "type": "bulk-source", "parameters": { "material": "ore", "rateKgPerS": 20 } },
            { "id": "CHUTE", "type": "transfer-chute", "parameters": { "capacityKg": 200 } },
            { "id": "PILE", "type": "bulk-sink" }
          ],
          "flows": [
            { "from": "FEED.Out", "to": "CHUTE.In" },
            { "from": "CHUTE.Out", "to": "PILE.In" }
          ]
        }
        """;

    public static LoadResult Load(string json, LoadOptions? options = null) => PlantLoader.Load(json, Catalogue, options);

    public static ConfigDiagnostic Only(string json) => Assert.Single(Load(json).Diagnostics);
}
