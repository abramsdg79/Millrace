using System.Text.Json;
using Dse.Core.Catalogue;
using Dse.Io;

namespace Dse.Control.Catalogue.Tests;

/// <summary>Binds parameters the way the loader's controller pass does: construct mode, against a tag table.</summary>
internal static class Bind
{
    public static readonly TimeSpan Period = TimeSpan.FromMilliseconds(100);

    private static readonly (string Name, TagKind Kind, TagAccess Access)[] Tags =
    [
        ("V1.Level", TagKind.Double, TagAccess.ReadOnly),
        ("V1.Fill", TagKind.Bool, TagAccess.ReadWrite),
        ("V1.Tripped", TagKind.Bool, TagAccess.ReadOnly),
        ("V1.Running", TagKind.Bool, TagAccess.ReadOnly),
    ];

    public static ParameterValues Values(IReadOnlyList<ParameterDescriptor> schema, string json)
    {
        (ParameterValues? values, List<BindingIssue> issues) = TryValues(schema, json);
        Assert.True(values is not null, string.Join("\n", issues.Select(i => $"{i.Path}: {i.Message}")));
        return values!;
    }

    public static (ParameterValues? Values, List<BindingIssue> Issues) TryValues(IReadOnlyList<ParameterDescriptor> schema, string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        var issues = new List<BindingIssue>();
        BindingContext context = new BindingContext(ControlFixtures.Catalogue).UseTags(Tags);
        ParameterValues? values = ParameterBinder.Bind(schema, document.RootElement, "$", context, construct: true, issues);
        return (values, issues);
    }
}
