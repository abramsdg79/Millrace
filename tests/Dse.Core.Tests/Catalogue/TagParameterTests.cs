using System.Text.Json;
using Dse.Core.Catalogue;
using Dse.Core.Graph;
using Dse.Io;

namespace Dse.Core.Tests.Catalogue;

public class TagParameterTests
{
    private static readonly ParameterDescriptor[] Write =
    [
        Param.Tag("tag", "The tag commanded.", writes: true),
        Param.Value("value", "What it is set to.", "tag"),
    ];

    private static readonly ParameterDescriptor[] Compare =
    [
        Param.Tag("tag", "The tag compared."),
        Param.Value("value", "What it is compared with.", "tag"),
    ];

    private static readonly (string Name, TagKind Kind, TagAccess Access)[] Tags =
    [
        ("CV001.Start", TagKind.Bool, TagAccess.ReadWrite),
        ("CV001.Speed", TagKind.Double, TagAccess.ReadOnly),
        ("Feed.Rate", TagKind.Double, TagAccess.ReadWrite),
        ("Batch.Count", TagKind.Int64, TagAccess.ReadWrite),
        ("PERM01.FirstOut", TagKind.Int64, TagAccess.ReadOnly),
    ];

    private static (ParameterValues? Values, List<BindingIssue> Issues) Bind(
        IReadOnlyList<ParameterDescriptor> schema, string json, bool resolve = true, bool construct = true)
    {
        var context = new BindingContext(new CatalogueBuilder().Build());
        if (resolve)
        {
            context.UseTags(Tags);
        }

        using JsonDocument document = JsonDocument.Parse(json);
        var issues = new List<BindingIssue>();
        ParameterValues? values = ParameterBinder.Bind(schema, document.RootElement, "$.p", context, construct, issues);
        return (values, issues);
    }

    [Fact]
    public void ATagAndAValueAreRequiredAndTheValueNamesItsTag()
    {
        ParameterDescriptor tag = Param.Tag("input", "The tag watched.", TagKind.Double);
        ParameterDescriptor value = Param.Value("value", "The value.", "input");

        Assert.True(tag.IsRequired);
        Assert.True(value.IsRequired);
        Assert.Equal(ParameterKind.Tag, tag.Kind);
        Assert.Equal(TagKind.Double, tag.RequiredKind);
        Assert.False(tag.IsWriteTarget);
        Assert.Equal(ParameterKind.Value, value.Kind);
        Assert.Equal("input", value.TagParameter);
    }

    [Theory]
    [InlineData("true", "CV001.Start", "Bool true")]
    [InlineData("true", "Batch.Count", "WrongTagKind")]
    [InlineData("true", "Feed.Rate", "WrongTagKind")]
    [InlineData("3", "CV001.Start", "WrongTagKind")]
    [InlineData("3", "Batch.Count", "Int64 3")]
    [InlineData("3", "Feed.Rate", "Double 3")]
    [InlineData("3.0", "Batch.Count", "Int64 3")]
    [InlineData("3e0", "Batch.Count", "Int64 3")]
    [InlineData("1.5", "CV001.Start", "WrongTagKind")]
    [InlineData("1.5", "Batch.Count", "WrongTagKind")]
    [InlineData("1.5", "Feed.Rate", "Double 1.5")]
    [InlineData("1e20", "Batch.Count", "WrongTagKind")]
    public void AValueConvertsToItsTagsKind(string value, string tag, string expected)
    {
        (ParameterValues? values, List<BindingIssue> issues) = Bind(Write, $$"""{ "tag": "{{tag}}", "value": {{value}} }""");

        string actual = values is null
            ? Assert.Single(issues).Kind.ToString()
            : $"{values.Value("value").Kind} {values.Value("value")}";
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void AnUnknownTagSuggestsTheNearestName()
    {
        (ParameterValues? values, List<BindingIssue> issues) = Bind(Write, """{ "tag": "CV001.Strat", "value": true }""");

        Assert.Null(values);
        BindingIssue issue = Assert.Single(issues);           // the value is not reported a second time
        Assert.Equal(BindingIssueKind.UnknownTag, issue.Kind);
        Assert.Equal("$.p.tag", issue.Path);
        Assert.Equal("'CV001.Strat' is not a tag in this plant.", issue.Message);
        Assert.Equal("Use a tag the plant has — 'CV001.Start' is closest.", issue.Fix);
    }

    [Fact]
    public void ATagOfTheWrongKindIsAnIssueAtTheTag()
    {
        ParameterDescriptor[] schema = [Param.Tag("input", "A Bool tag.", TagKind.Bool)];

        (ParameterValues? values, List<BindingIssue> issues) = Bind(schema, """{ "input": "PERM01.FirstOut" }""");

        Assert.Null(values);
        BindingIssue issue = Assert.Single(issues);
        Assert.Equal(BindingIssueKind.WrongTagKind, issue.Kind);
        Assert.Equal("$.p.input", issue.Path);
        Assert.Equal("'PERM01.FirstOut' is an Int64 tag, but 'input' needs a Bool tag.", issue.Message);
    }

    [Fact]
    public void ACommandedTagMustBeReadWrite()
    {
        (ParameterValues? values, List<BindingIssue> issues) = Bind(Write, """{ "tag": "CV001.Speed", "value": 1.0 }""");

        Assert.Null(values);
        BindingIssue issue = Assert.Single(issues);
        Assert.Equal(BindingIssueKind.ReadOnlyTag, issue.Kind);
        Assert.Equal("$.p.tag", issue.Path);
        Assert.Equal("'CV001.Speed' is read-only, so a block cannot command it.", issue.Message);
    }

    [Fact]
    public void AReadOnlyTagMayBeCompared()
    {
        (ParameterValues? values, List<BindingIssue> issues) = Bind(Compare, """{ "tag": "PERM01.FirstOut", "value": 2 }""");

        Assert.Empty(issues);
        Assert.Equal("PERM01.FirstOut", values!.Tag("tag"));
        Assert.Equal(TagValue.Int64(2), values.Value("value"));
    }

    [Fact]
    public void AValueMustBeABooleanOrANumber()
    {
        (_, List<BindingIssue> issues) = Bind(Write, """{ "tag": "CV001.Start", "value": "on" }""");

        BindingIssue issue = Assert.Single(issues);
        Assert.Equal(BindingIssueKind.BadParameter, issue.Kind);
        Assert.Equal("$.p.value", issue.Path);
        Assert.Equal("'value' must be true, false or a number, but it is a string.", issue.Message);
    }

    [Fact]
    public void WithoutATagTableATagIsANameAndAValueIsProvisional()
    {
        (ParameterValues? values, List<BindingIssue> issues) = Bind(Write, """{ "tag": "Nowhere.Tag", "value": 3 }""", resolve: false);

        Assert.Empty(issues);
        Assert.Equal("Nowhere.Tag", values!.Tag("tag"));
        Assert.Equal(TagValue.Double(3.0), values.Value("value"));
    }

    [Fact]
    public void InCheckModeTagsAreNotResolvedEvenWithATable()
    {
        (ParameterValues? values, List<BindingIssue> issues) = Bind(Write, """{ "tag": "Nowhere.Tag", "value": true }""", construct: false);

        Assert.Empty(issues);
        Assert.Equal(TagValue.Bool(true), values!.Value("value"));
    }

    [Fact]
    public void AValueMustNameASiblingTagParameter()
    {
        var descriptor = new ObjectDescriptor("probe", "set", "Sets a tag.", p => new object())
        {
            Parameters = [Param.Tag("target", "The tag."), Param.Value("value", "The value.", "tag")],
        };

        var ex = Assert.Throws<ArgumentException>(() => new CatalogueBuilder().Add(descriptor));

        Assert.Contains("'tag'", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Declared tag parameters: target.", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AComponentMayNotDeclareATagParameter()
    {
        var descriptor = new ComponentDescriptor("tagged", ComponentCategory.Signal, "Names a tag.", (id, p) => new UnitDelay<bool>(id))
        {
            Parameters = [Param.Tag("input", "A tag.")],
        };

        var ex = Assert.Throws<ArgumentException>(() => new CatalogueBuilder().Add(descriptor));

        Assert.Contains("only a block or an object may declare", ex.Message, StringComparison.Ordinal);
    }
}
