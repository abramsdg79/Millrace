using Dse.Core.Catalogue;
using Dse.Core.Flow;
using Dse.Core.Graph;

namespace Dse.Core.Tests.Catalogue;

public class CatalogueModelTests
{
    private static ComponentDescriptor Delay(string type) =>
        new(type, ComponentCategory.Signal, "Holds a value for one tick.", (id, p) => new UnitDelay<bool>(id))
        {
            Ports = [PortSpec.In<bool>("In"), PortSpec.Out<bool>("Out")],
        };

    private sealed class ModuleA : ICatalogueModule
    {
        public string Name => "A";

        public void Register(CatalogueBuilder builder) => builder.Add(Delay("delay"));
    }

    private sealed class ModuleB : ICatalogueModule
    {
        public string Name => "B";

        public void Register(CatalogueBuilder builder) => builder.Add(Delay("delay"));
    }

    [Fact]
    public void AScalarWithoutADefaultIsRequiredUnlessOptional()
    {
        Assert.True(Param.Double("ratio", "Reduction ratio.").IsRequired);
        Assert.False(Param.Double("efficiency", "Efficiency.", @default: 0.95).IsRequired);
        Assert.False(Param.Double("capacityKg", "Omit for unlimited.", optional: true).IsRequired);
    }

    [Fact]
    public void AGroupIsRequiredOnlyWhenOneOfItsChildrenIs()
    {
        var allDefaulted = new GroupDefinition("G1", Param.Double("a", "A.", @default: 1.0));
        var oneRequired = new GroupDefinition("G2", Param.Double("a", "A."), Param.Double("b", "B.", @default: 1.0));

        Assert.False(Param.Group("g", "G.", allDefaulted).IsRequired);
        Assert.True(Param.Group("g", "G.", oneRequired).IsRequired);
    }

    [Fact]
    public void AListIsRequiredWhenItsMinimumCountIsPositive()
    {
        var line = new GroupDefinition("Line", Param.String("inlet", "Inlet name."));

        Assert.True(Param.GroupList("recipe", "Recipe.", line, minCount: 1).IsRequired);
        Assert.False(Param.ObjectList("transforms", "Transforms.", ObjectSlots.Transform).IsRequired);
        Assert.False(Param.StringList("states", "States.").IsRequired);
    }

    [Fact]
    public void AReferenceRecordsItsCapability()
    {
        ParameterDescriptor belt = Param.Reference<IMaterialObservable>("belt", "The belt weighed.");

        Assert.Equal(ParameterKind.Reference, belt.Kind);
        Assert.Equal(typeof(IMaterialObservable), belt.Capability);
        Assert.True(belt.IsRequired);
    }

    [Theory]
    [InlineData("RatedPower")]
    [InlineData("rated-power")]
    [InlineData("rated power")]
    [InlineData("")]
    public void AParameterNameMustBeCamelCase(string name)
    {
        Assert.Throws<ArgumentException>(() => Param.Double(name, "X."));
    }

    [Fact]
    public void PortValueTypesHaveStableNames()
    {
        Assert.Equal("bool", PortSpec.ValueTypeName(typeof(bool)));
        Assert.Equal("double", PortSpec.ValueTypeName(typeof(double)));
        Assert.Equal("int", PortSpec.ValueTypeName(typeof(int)));
        Assert.Equal("long", PortSpec.ValueTypeName(typeof(long)));
        Assert.Equal("enum:PayloadKind", PortSpec.ValueTypeName(typeof(PayloadKind)));
    }

    [Theory]
    [InlineData("Delay")]
    [InlineData("unit_delay")]
    [InlineData("-delay")]
    [InlineData("delay-")]
    public void ATypeNameMustBeKebabCase(string type)
    {
        var ex = Assert.Throws<ArgumentException>(() => new CatalogueBuilder().Add(Delay(type)));
        Assert.Contains("kebab-case", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PortNamesMustBeUniqueIgnoringCase()
    {
        var descriptor = new ComponentDescriptor(
            "clash", ComponentCategory.Signal, "Two ports, one name.", (id, p) => new UnitDelay<bool>(id))
        {
            Ports = [PortSpec.In<bool>("Run"), PortSpec.Out<bool>("RUN")],
        };

        var ex = Assert.Throws<ArgumentException>(() => new CatalogueBuilder().Add(descriptor));
        Assert.Contains("'Run' and 'RUN'", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ADuplicateTypeNamesBothModules()
    {
        CatalogueBuilder builder = new CatalogueBuilder().Add<ModuleA>();

        var ex = Assert.Throws<InvalidOperationException>(() => builder.Add<ModuleB>());

        Assert.Contains("'delay'", ex.Message, StringComparison.Ordinal);
        Assert.Contains("module 'A'", ex.Message, StringComparison.Ordinal);
        Assert.Contains("module 'B'", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheSameObjectTypeMayExistInTwoSlots()
    {
        ComponentCatalogue catalogue = new CatalogueBuilder()
            .Add(new ObjectDescriptor(ObjectSlots.Transform, "none", "Does nothing.", p => new object()))
            .Add(new ObjectDescriptor(ObjectSlots.Hold, "none", "Holds nothing.", p => new object()))
            .Build();

        Assert.True(catalogue.TryGetObject(ObjectSlots.Transform, "none", out _));
        Assert.True(catalogue.TryGetObject(ObjectSlots.Hold, "none", out _));
        Assert.Equal(["hold", "transform"], catalogue.Slots);
    }

    [Fact]
    public void EntriesComeBackSortedByTypeName()
    {
        ComponentCatalogue catalogue = new CatalogueBuilder()
            .Add(Delay("zeta"))
            .Add(Delay("alpha"))
            .Add(new MaterialDescriptor(new MaterialType("ore", PayloadKind.Bulk), default, "Run-of-mine ore."))
            .Build();

        Assert.Equal(["alpha", "zeta"], catalogue.Components.Select(c => c.Type));
        Assert.True(catalogue.TryGetComponent("alpha", out ComponentDescriptor? alpha));
        Assert.Equal("(direct)", catalogue.ModuleOf(alpha!));
        Assert.True(catalogue.TryGetMaterial("ore", out _));
        Assert.False(catalogue.TryGetComponent("Alpha", out _));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void AMaterialStateMustNameASiblingMaterial(int scenario)
    {
        ComponentDescriptor descriptor = scenario switch
        {
            // (a) MaterialParameter names nothing declared beside it.
            1 => new ComponentDescriptor("names-nothing", ComponentCategory.Signal, "X.", (id, p) => new UnitDelay<bool>(id))
            {
                Parameters = [Param.MaterialState("state", "S.", "material")],
            },
            // (b) The sibling it names is not a Material parameter.
            2 => new ComponentDescriptor("names-a-double", ComponentCategory.Signal, "X.", (id, p) => new UnitDelay<bool>(id))
            {
                Parameters = [Param.Double("material", "Not a material."), Param.MaterialState("state", "S.", "material")],
            },
            // (c) The state is inside a group and the material is outside it.
            _ => new ComponentDescriptor("state-outside-group", ComponentCategory.Signal, "X.", (id, p) => new UnitDelay<bool>(id))
            {
                Parameters =
                [
                    Param.Material("material", "M."),
                    Param.Group("g", "G.", new GroupDefinition("G", Param.MaterialState("state", "S.", "material"))),
                ],
            },
        };

        var ex = Assert.Throws<ArgumentException>(() => new CatalogueBuilder().Add(descriptor));
        Assert.Contains("no material parameter of that name", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ACorrectMaterialStatePairIsAccepted()
    {
        ComponentCatalogue catalogue = new CatalogueBuilder()
            .Add(new ObjectDescriptor(ObjectSlots.Hold, "state-at-least", "Waits.", p => new object())
            {
                Parameters = [Param.Material("material", "M."), Param.MaterialState("state", "S.", "material")],
            })
            .Build();

        Assert.True(catalogue.TryGetObject(ObjectSlots.Hold, "state-at-least", out _));
    }

    [Fact]
    public void ARepeatByCountExpandsFromOne()
    {
        var repeat = new PortRepeat("channels");

        Assert.Equal(["Channel1", "Channel2", "Channel3"], repeat.Expand("Channel{n}", count: 3, names: []));
    }

    [Fact]
    public void ARepeatByNameExpandsFromTheNames()
    {
        var repeat = new PortRepeat("recipe", "inlet");

        Assert.Equal(["Flour", "Water"], repeat.Expand("{n}", count: 0, names: ["Flour", "Water"]));
    }
}
