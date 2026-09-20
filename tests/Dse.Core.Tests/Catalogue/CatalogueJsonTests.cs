using System.Text.Json;
using Dse.Core.Catalogue;
using Dse.Core.Faults;
using Dse.Core.Flow;
using Dse.Core.Graph;
using Dse.Io;

namespace Dse.Core.Tests.Catalogue;

public class CatalogueJsonTests
{
    private sealed class Module : ICatalogueModule
    {
        public string Name => "Test";

        public void Register(CatalogueBuilder builder)
        {
            var rating = new GroupDefinition("Rating", Param.Double("powerW", "Power.", "W", min: 0.0, exclusiveMin: true));
            builder.Add(new ComponentDescriptor("zeta", ComponentCategory.Signal, "Last.", (id, p) => new UnitDelay<bool>(id)));
            builder.Add(new ComponentDescriptor("alpha", ComponentCategory.Flow, "First.", (id, p) => new UnitDelay<bool>(id))
            {
                Parameters =
                [
                    Param.Group("rating", "Rating.", rating),
                    Param.Int("channels", "Channels.", "count", @default: 2, min: 1),
                    Param.Reference<IMaterialObservable>("belt", "Belt."),
                    Param.Double("capacityKg", "Omit for unlimited.", "kg", optional: true),
                ],
                Ports = [PortSpec.In<bool>("Channel{n}", required: true, repeat: new PortRepeat("channels")), PortSpec.Out<double>("Level", "fraction")],
                FlowPorts = [PortSpec.Inlet("In", PayloadKind.Bulk)],
                Faults = [new FaultDescriptor("jam", "Stops.", new FaultParameter("seconds", "s", 1.5, "How long."))],
                Tags = [new TagEntry("Level", TagKind.Double, TagAccess.ReadOnly, "fraction")],
                Telemetry = [new TelemetryKey("Held", "kg")],
                Provides = [typeof(IMaterialObservable)],
            });
            builder.Add(new ObjectDescriptor(ObjectSlots.Hold, "for-seconds", "Waits.", p => new object())
            {
                Parameters = [Param.Double("seconds", "How long.", "s", min: 0.0)],
            });
            builder.Add(new MaterialDescriptor(new MaterialType("ore", PayloadKind.Bulk, "wet"), new MaterialProperties(2000.0, 0.03, 15.0), "Ore."));
        }
    }

    private static readonly string Json = CatalogueJson.Export(new CatalogueBuilder().Add<Module>().Build());

    [Fact]
    public void IsValidJsonWithAVersionAndSortedComponents()
    {
        using JsonDocument document = JsonDocument.Parse(Json);
        JsonElement root = document.RootElement;

        Assert.Equal(1, root.GetProperty("formatVersion").GetInt32());
        Assert.Equal("Test", root.GetProperty("modules")[0].GetString());
        Assert.Equal(["alpha", "zeta"], root.GetProperty("components").EnumerateArray().Select(c => c.GetProperty("type").GetString()));
    }

    [Fact]
    public void WritesEveryAspectOfAComponent()
    {
        using JsonDocument document = JsonDocument.Parse(Json);
        JsonElement alpha = document.RootElement.GetProperty("components")[0];

        Assert.Equal("flow", alpha.GetProperty("category").GetString());
        Assert.Equal("Test", alpha.GetProperty("module").GetString());

        JsonElement rating = alpha.GetProperty("parameters")[0];
        Assert.Equal("group", rating.GetProperty("kind").GetString());
        Assert.Equal("Rating", rating.GetProperty("group").GetString());
        Assert.True(rating.GetProperty("required").GetBoolean());
        JsonElement power = rating.GetProperty("parameters")[0];
        Assert.Equal(0.0, power.GetProperty("minimum").GetDouble());
        Assert.True(power.GetProperty("exclusiveMinimum").GetBoolean());
        Assert.False(power.TryGetProperty("maximum", out _));

        JsonElement channels = alpha.GetProperty("parameters")[1];
        Assert.Equal(2, channels.GetProperty("default").GetInt32());
        Assert.False(channels.GetProperty("required").GetBoolean());

        Assert.Equal("IMaterialObservable", alpha.GetProperty("parameters")[2].GetProperty("capability").GetString());
        Assert.True(alpha.GetProperty("parameters")[3].GetProperty("optional").GetBoolean());

        JsonElement port = alpha.GetProperty("ports")[0];
        Assert.Equal("in", port.GetProperty("direction").GetString());
        Assert.Equal("bool", port.GetProperty("valueType").GetString());
        Assert.True(port.GetProperty("required").GetBoolean());
        Assert.Equal("channels", port.GetProperty("repeat").GetProperty("parameter").GetString());

        Assert.Equal("bulk", alpha.GetProperty("flowPorts")[0].GetProperty("payload").GetString());
        Assert.Equal(1.5, alpha.GetProperty("faults")[0].GetProperty("parameters")[0].GetProperty("default").GetDouble());
        Assert.Equal("readOnly", alpha.GetProperty("tags")[0].GetProperty("access").GetString());
        Assert.Equal("kg", alpha.GetProperty("telemetry")[0].GetProperty("unit").GetString());
        Assert.Equal("IMaterialObservable", alpha.GetProperty("provides")[0].GetString());
    }

    [Fact]
    public void WritesObjectsAndMaterials()
    {
        using JsonDocument document = JsonDocument.Parse(Json);
        JsonElement hold = document.RootElement.GetProperty("objects")[0];
        JsonElement ore = document.RootElement.GetProperty("materials")[0];

        Assert.Equal("hold", hold.GetProperty("slot").GetString());
        Assert.Equal("for-seconds", hold.GetProperty("type").GetString());
        Assert.Equal("bulk", ore.GetProperty("kind").GetString());
        Assert.Equal("wet", ore.GetProperty("states")[0].GetString());
        Assert.Equal(0.03, ore.GetProperty("properties").GetProperty("moisture").GetDouble());
    }

    [Fact]
    public void IsByteIdenticalAcrossRunsAndUsesUnixLineEndings()
    {
        string again = CatalogueJson.Export(new CatalogueBuilder().Add<Module>().Build());

        Assert.Equal(Json, again);
        Assert.DoesNotContain('\r', Json);
        Assert.EndsWith("}\n", Json, StringComparison.Ordinal);
        Assert.DoesNotContain("\\u", Json, StringComparison.Ordinal);
    }
}
