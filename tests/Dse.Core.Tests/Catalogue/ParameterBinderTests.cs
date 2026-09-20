using System.Text.Json;
using Dse.Core.Catalogue;
using Dse.Core.Flow;
using Dse.Core.Graph;

namespace Dse.Core.Tests.Catalogue;

public class ParameterBinderTests
{
    private static readonly GroupDefinition Rating = new(
        "Rating",
        Param.Double("powerW", "Nameplate power.", "W", min: 0.0, exclusiveMin: true),
        Param.Double("droop", "Speed droop.", @default: 0.03, min: 0.0, max: 1.0));

    private static readonly GroupDefinition Line = new(
        "Line",
        Param.String("inlet", "Inlet name."),
        Param.Double("massKg", "Mass.", "kg", min: 0.0, exclusiveMin: true));

    private static readonly MaterialType Dough = new("dough", PayloadKind.Bulk, "proof", "bake");
    private static readonly MaterialType Loaf = new("loaf", PayloadKind.Discrete);

    private sealed record Timer(double Seconds);

    private sealed record StateFloor(int Index, double Value);

    private sealed record All(IReadOnlyList<object> Conditions);

    private static BindingContext Context()
    {
        ComponentCatalogue catalogue = new CatalogueBuilder()
            .Add(new ObjectDescriptor(ObjectSlots.Hold, "for-seconds", "Waits.", p => new Timer(p.Double("seconds")))
            {
                Parameters = [Param.Double("seconds", "How long.", "s", min: 0.0)],
            })
            .Add(new ObjectDescriptor(ObjectSlots.Hold, "state-at-least", "Waits for a state.", p => new StateFloor(p.StateIndex("state"), p.Double("value")))
            {
                Parameters =
                [
                    Param.Material("material", "Whose state."),
                    Param.MaterialState("state", "Which state.", "material"),
                    Param.Double("value", "Threshold."),
                ],
            })
            .Add(new ObjectDescriptor(ObjectSlots.Hold, "all", "Every condition.", p => new All(p.Objects<object>("conditions")))
            {
                Parameters = [Param.ObjectList("conditions", "The conditions.", ObjectSlots.Hold, minCount: 1)],
            })
            .Build();

        return new BindingContext(catalogue)
            .AddMaterial(new MaterialDescriptor(Dough, new MaterialProperties(1100.0, 0.4, 25.0), "Dough."))
            .AddMaterial(new MaterialDescriptor(Loaf, default, "A loaf."))
            .AddNode(new BulkBelt("BELT", 10.0, 0.5, 2.0, 100.0))
            .AddNode(new UnitDelay<bool>("DELAY"));
    }

    private static (ParameterValues? Values, List<BindingIssue> Issues) Bind(
        IReadOnlyList<ParameterDescriptor> schema, string json, bool construct = true)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        var issues = new List<BindingIssue>();
        ParameterValues? values = ParameterBinder.Bind(schema, document.RootElement, "$.p", Context(), construct, issues);
        return (values, issues);
    }

    [Fact]
    public void AppliesDefaultsAndReadsScalars()
    {
        ParameterDescriptor[] schema =
        [
            Param.Double("ratio", "Ratio."),
            Param.Double("efficiency", "Efficiency.", @default: 0.95),
            Param.Int("channels", "Channels.", @default: 2),
            Param.Bool("enabled", "Enabled.", @default: true),
            Param.String("unit", "Unit.", @default: "A"),
            Param.Enum("mode", "Mode.", ["fast", "slow"], @default: "slow"),
            Param.Double("capacityKg", "Omit for unlimited.", optional: true),
        ];

        var (values, issues) = Bind(schema, """{ "ratio": 20, "channels": 3 }""");

        Assert.Empty(issues);
        Assert.NotNull(values);
        Assert.Equal(20.0, values.Double("ratio"));
        Assert.Equal(0.95, values.Double("efficiency"));
        Assert.Equal(3, values.Int("channels"));
        Assert.True(values.Bool("enabled"));
        Assert.Equal("A", values.String("unit"));
        Assert.Equal("slow", values.String("mode"));
        Assert.False(values.Has("capacityKg"));
        Assert.Equal(double.PositiveInfinity, values.DoubleOr("capacityKg", double.PositiveInfinity));
    }

    [Fact]
    public void AnAbsentParametersObjectBindsAsEmpty()
    {
        var issues = new List<BindingIssue>();

        ParameterValues? values = ParameterBinder.Bind(
            [Param.Double("efficiency", "Efficiency.", @default: 0.95)], default, "$.p", Context(), true, issues);

        Assert.Empty(issues);
        Assert.Equal(0.95, values!.Double("efficiency"));
    }

    [Fact]
    public void CollectsEveryIssueAtOnce()
    {
        ParameterDescriptor[] schema =
        [
            Param.Double("ratio", "Ratio.", min: 1.0),
            Param.Int("channels", "Channels.", min: 1),
            Param.Bool("enabled", "Enabled."),
        ];

        var (values, issues) = Bind(schema, """{ "ratio": 0.5, "channels": 1.5, "enabeld": true }""");

        Assert.Null(values);
        Assert.Equal(4, issues.Count);
        Assert.Contains(issues, i => i.Kind == BindingIssueKind.UnknownKey && i.Path == "$.p.enabeld" && i.Fix.Contains("'enabled' is closest", StringComparison.Ordinal));
        Assert.Contains(issues, i => i.Kind == BindingIssueKind.BadParameter && i.Path == "$.p.ratio" && i.Message.Contains("0.5", StringComparison.Ordinal));
        Assert.Contains(issues, i => i.Kind == BindingIssueKind.BadParameter && i.Path == "$.p.channels" && i.Message.Contains("whole number", StringComparison.Ordinal));
        Assert.Contains(issues, i => i.Kind == BindingIssueKind.BadParameter && i.Path == "$.p.enabled" && i.Message.Contains("required", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("""{ "v": 0 }""", false)]
    [InlineData("""{ "v": 0.001 }""", true)]
    [InlineData("""{ "v": 1 }""", true)]
    [InlineData("""{ "v": 1.001 }""", false)]
    public void HonoursExclusiveAndInclusiveBounds(string json, bool ok)
    {
        var (_, issues) = Bind([Param.Double("v", "Yield.", min: 0.0, max: 1.0, exclusiveMin: true)], json);

        Assert.Equal(ok, issues.Count == 0);
    }

    [Fact]
    public void RejectsAWrongJsonTypeAndSaysWhatItWanted()
    {
        var (_, issues) = Bind([Param.Double("ratio", "Ratio.")], """{ "ratio": "20" }""");

        BindingIssue issue = Assert.Single(issues);
        Assert.Equal(BindingIssueKind.BadParameter, issue.Kind);
        Assert.Contains("a number", issue.Message, StringComparison.Ordinal);
        Assert.Contains("a string", issue.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsAnEnumValueItDoesNotAllow()
    {
        var (_, issues) = Bind([Param.Enum("mode", "Mode.", ["fast", "slow"])], """{ "mode": "fats" }""");

        BindingIssue issue = Assert.Single(issues);
        Assert.Contains("'fast' is closest", issue.Fix, StringComparison.Ordinal);
    }

    [Fact]
    public void BindsAGroupAndFillsAnOmittedOneWithDefaults()
    {
        var optionalGroup = new GroupDefinition("Opt", Param.Double("a", "A.", @default: 7.0));
        ParameterDescriptor[] schema = [Param.Group("motor", "Rating.", Rating), Param.Group("extra", "Extra.", optionalGroup)];

        var (values, issues) = Bind(schema, """{ "motor": { "powerW": 750 } }""");

        Assert.Empty(issues);
        Assert.Equal(750.0, values!.Group("motor").Double("powerW"));
        Assert.Equal(0.03, values.Group("motor").Double("droop"));
        Assert.Equal(7.0, values.Group("extra").Double("a"));
    }

    [Fact]
    public void PathsReachIntoGroupsAndLists()
    {
        ParameterDescriptor[] schema = [Param.Group("motor", "Rating.", Rating), Param.GroupList("recipe", "Recipe.", Line, minCount: 1)];

        var (_, issues) = Bind(schema, """{ "motor": { "powerW": 0 }, "recipe": [ { "inlet": "Flour", "massKg": 5 }, { "inlet": "Water" } ] }""");

        Assert.Equal(2, issues.Count);
        Assert.Contains(issues, i => i.Path == "$.p.motor.powerW");
        Assert.Contains(issues, i => i.Path == "$.p.recipe[1].massKg");
    }

    [Fact]
    public void BindsAStringListAndRejectsANonStringEntry()
    {
        var (values, issues) = Bind([Param.StringList("states", "States.")], """{ "states": [ "proof", "bake" ] }""");
        var (_, bad) = Bind([Param.StringList("states", "States.")], """{ "states": [ "proof", 2 ] }""");
        var (absent, _) = Bind([Param.StringList("states", "States.")], "{}");

        Assert.Empty(issues);
        Assert.Equal(["proof", "bake"], values!.Strings("states"));
        Assert.Equal("$.p.states[1]", Assert.Single(bad).Path);
        Assert.Empty(absent!.Strings("states"));
    }

    [Fact]
    public void AListShorterThanItsMinimumIsAnIssue()
    {
        var (_, issues) = Bind([Param.GroupList("recipe", "Recipe.", Line, minCount: 1)], """{ "recipe": [] }""");

        BindingIssue issue = Assert.Single(issues);
        Assert.Contains("at least 1", issue.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolvesAMaterialItsPropertiesAndAState()
    {
        ParameterDescriptor[] schema =
        [
            Param.Material("material", "What.", PayloadKind.Bulk),
            Param.MaterialState("state", "Which.", "material"),
            Param.Material("output", "Optional.", optional: true),
        ];

        var (values, issues) = Bind(schema, """{ "state": "bake", "material": "dough" }""");

        Assert.Empty(issues);
        Assert.Same(Dough, values!.Material("material"));
        Assert.Equal(0.4, values.MaterialProperties("material").Moisture);
        Assert.Equal(1, values.StateIndex("state"));
        Assert.Null(values.MaterialOrNull("output"));
    }

    [Fact]
    public void ReportsUnknownMaterialWrongKindAndUnknownState()
    {
        ParameterDescriptor[] schema =
        [
            Param.Material("a", "A."),
            Param.Material("b", "B.", PayloadKind.Discrete),
            Param.Material("c", "C."),
            Param.MaterialState("state", "Which.", "c"),
        ];

        var (_, issues) = Bind(schema, """{ "a": "duogh", "b": "dough", "c": "dough", "state": "cool" }""");

        Assert.Equal(3, issues.Count);
        Assert.Contains(issues, i => i.Kind == BindingIssueKind.UnknownMaterial && i.Fix.Contains("'dough' is closest", StringComparison.Ordinal));
        Assert.Contains(issues, i => i.Kind == BindingIssueKind.BadParameter && i.Path == "$.p.b" && i.Message.Contains("Bulk", StringComparison.Ordinal));
        Assert.Contains(issues, i => i.Kind == BindingIssueKind.UnknownState && i.Fix.Contains("bake, proof", StringComparison.Ordinal));
    }

    [Fact]
    public void AStateOfAnOmittedOptionalMaterialIsAnIssue()
    {
        ParameterDescriptor[] schema =
        [
            Param.Material("output", "O.", optional: true),
            Param.MaterialState("state", "S.", "output"),
        ];

        var (_, issues) = Bind(schema, """{ "state": "bake" }""");

        BindingIssue issue = Assert.Single(issues);
        Assert.Equal(BindingIssueKind.BadParameter, issue.Kind);
        Assert.Equal("$.p.state", issue.Path);
        Assert.Contains("Give 'output' as well", issue.Fix, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolvesAReferenceToItsCapability()
    {
        var (values, issues) = Bind([Param.Reference<IMaterialObservable>("belt", "Belt.")], """{ "belt": "BELT" }""");

        Assert.Empty(issues);
        Assert.IsType<BulkBelt>(values!.Reference<IMaterialObservable>("belt"));
    }

    [Fact]
    public void ReportsAMissingReferenceAndAMissingCapability()
    {
        ParameterDescriptor[] schema = [Param.Reference<IMaterialObservable>("a", "A."), Param.Reference<IMaterialObservable>("b", "B.")];

        var (_, issues) = Bind(schema, """{ "a": "BLET", "b": "DELAY" }""");

        Assert.Equal(2, issues.Count);
        Assert.Contains(issues, i => i.Kind == BindingIssueKind.MissingReference && i.Fix.Contains("'BELT' is closest", StringComparison.Ordinal));
        Assert.Contains(issues, i => i.Kind == BindingIssueKind.MissingCapability && i.Message.Contains("IMaterialObservable", StringComparison.Ordinal));
    }

    [Fact]
    public void InCheckModeAReferenceNeedsOnlyToBeAString()
    {
        var (values, issues) = Bind([Param.Reference<IMaterialObservable>("belt", "Belt.")], """{ "belt": "NOT-BUILT-YET" }""", construct: false);

        Assert.Empty(issues);
        Assert.NotNull(values);
    }

    [Fact]
    public void BuildsNestedObjectsRecursively()
    {
        const string Json = """
            { "hold": { "type": "all", "conditions": [
                { "type": "for-seconds", "seconds": 900 },
                { "type": "state-at-least", "material": "dough", "state": "bake", "value": 1 } ] } }
            """;

        var (values, issues) = Bind([Param.Object("hold", "Hold.", ObjectSlots.Hold)], Json);

        Assert.Empty(issues);
        All all = values!.Object<All>("hold");
        Assert.Equal(new Timer(900.0), all.Conditions[0]);
        Assert.Equal(new StateFloor(1, 1.0), all.Conditions[1]);
    }

    [Fact]
    public void ReportsAnUnknownObjectTypeAndAMissingTypeKey()
    {
        ParameterDescriptor[] schema = [Param.Object("a", "A.", ObjectSlots.Hold), Param.Object("b", "B.", ObjectSlots.Hold)];

        var (_, issues) = Bind(schema, """{ "a": { "type": "for-secnods", "seconds": 1 }, "b": { "seconds": 1 } }""");

        Assert.Equal(2, issues.Count);
        Assert.Contains(issues, i => i.Kind == BindingIssueKind.UnknownType && i.Path == "$.p.a.type" && i.Fix.Contains("'for-seconds' is closest", StringComparison.Ordinal));
        Assert.Contains(issues, i => i.Kind == BindingIssueKind.BadParameter && i.Path == "$.p.b" && i.Message.Contains("\"type\"", StringComparison.Ordinal));
    }

    [Fact]
    public void AnObjectFactoryThatThrowsBecomesARejectedIssue()
    {
        ComponentCatalogue catalogue = new CatalogueBuilder()
            .Add(new ObjectDescriptor(ObjectSlots.Hold, "fussy", "Rejects everything.", p => throw new ArgumentException("Seconds must be even."))
            {
                Parameters = [Param.Double("seconds", "How long.")],
            })
            .Build();
        using JsonDocument document = JsonDocument.Parse("""{ "hold": { "type": "fussy", "seconds": 3 } }""");
        var issues = new List<BindingIssue>();

        ParameterValues? values = ParameterBinder.Bind(
            [Param.Object("hold", "Hold.", ObjectSlots.Hold)], document.RootElement, "$.p", new BindingContext(catalogue), true, issues);

        Assert.Null(values);
        BindingIssue issue = Assert.Single(issues);
        Assert.Equal(BindingIssueKind.Rejected, issue.Kind);
        Assert.Equal("$.p.hold", issue.Path);
        Assert.Contains("Seconds must be even.", issue.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AskingForAnUndeclaredParameterIsAFactoryBug()
    {
        var (values, _) = Bind([Param.Double("ratio", "Ratio.")], """{ "ratio": 2 }""");

        var ex = Assert.Throws<KeyNotFoundException>(() => values!.Double("ration"));
        Assert.Contains("'ration'", ex.Message, StringComparison.Ordinal);
        Assert.Contains("ratio", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AddingAMaterialTwiceIsAnError()
    {
        BindingContext context = Context();

        var ex = Assert.Throws<InvalidOperationException>(
            () => context.AddMaterial(new MaterialDescriptor(new MaterialType("dough", PayloadKind.Bulk), default, "Again.")));
        Assert.Contains("'dough'", ex.Message, StringComparison.Ordinal);
    }
}
