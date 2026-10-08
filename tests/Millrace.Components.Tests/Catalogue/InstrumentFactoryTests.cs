using Millrace.Components.Instruments;
using Millrace.Components.Safety;
using Millrace.Core.Catalogue;
using Millrace.Core.Flow;

namespace Millrace.Components.Tests.Catalogue;

public class InstrumentFactoryTests
{
    [Fact]
    public void TheBeltScaleFactoryResolvesItsBeltAndReadsItsSpec()
    {
        var belt = new BulkBelt("CV", 10.0, 0.5, 2.0, 100.0);
        BindingContext context = new BindingContext(ComponentsFixtures.Catalogue).AddNode(belt);

        BeltScale scale = MechanicalFactoryTests.Build<BeltScale>(
            BeltScale.Descriptor,
            """{ "belt": "CV", "positionM": 4, "spec": { "unit": "t/h", "rangeLow": 0, "rangeHigh": 800, "noiseSigma": 0.5, "lagSeconds": 2 } }""",
            context);

        Assert.Equal(4.0, scale.PositionM);
        Assert.Equal(new InstrumentSpec("t/h", 0.0, 800.0, 0.5, 2.0), scale.Spec);
    }

    [Fact]
    public void AnInvertedRangeIsTheConstructorsToReject()
    {
        using var document = System.Text.Json.JsonDocument.Parse("""{ "spec": { "unit": "A", "rangeLow": 5, "rangeHigh": 1 } }""");
        var issues = new List<BindingIssue>();
        ParameterValues? values = ParameterBinder.Bind(
            CurrentSensor.Descriptor.Parameters, document.RootElement, "$", new BindingContext(ComponentsFixtures.Catalogue), true, issues);

        Assert.Empty(issues);
        Assert.Throws<ArgumentException>(() => CurrentSensor.Descriptor.Factory("X", values!));
    }

    [Fact]
    public void TheRelayFactoryMakesTheChannelsAsked()
    {
        SafetyRelay relay = MechanicalFactoryTests.Build<SafetyRelay>(SafetyRelay.Descriptor, """{ "channels": 4 }""");

        Assert.Contains(relay.Ports, p => p.Name == "Channel4");
        Assert.DoesNotContain(relay.Ports, p => p.Name == "Channel5");
    }
}
