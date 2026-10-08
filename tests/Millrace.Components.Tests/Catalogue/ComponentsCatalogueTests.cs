using System.Reflection;
using Millrace.Components.Flow;
using Millrace.Core.Flow;
using Millrace.Core.Graph;
using Millrace.Core.Testing;

namespace Millrace.Components.Tests.Catalogue;

public class ComponentsCatalogueTests
{
    private static readonly ConformanceReport Report =
        CatalogueConformance.Check(ComponentsFixtures.Catalogue, ComponentsFixtures.Create());

    [Fact]
    public void EveryDescriptorMatchesWhatItBuilds()
    {
        Assert.Empty(Report.Mismatches);
    }

    [Fact]
    public void EveryConcreteNodeTransformAndHoldHasADescriptor()
    {
        Assembly[] assemblies = [typeof(ComponentsModule).Assembly, typeof(UnitDelay<>).Assembly];
        List<Type> concrete = assemblies
            .SelectMany(a => a.GetTypes())
            .Where(t => t is { IsClass: true, IsAbstract: false })
            .Where(t => typeof(ISimNode).IsAssignableFrom(t)
                     || typeof(IMaterialTransform).IsAssignableFrom(t)
                     || typeof(IHoldCondition).IsAssignableFrom(t))
            .ToList();

        var built = new HashSet<Type>(Report.BuiltTypes.Select(Definition));
        List<string> missing = concrete
            .Where(t => !built.Contains(Definition(t)))
            .Select(t => t.FullName!)
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Empty(missing);
    }

    private static Type Definition(Type type) => type.IsGenericType ? type.GetGenericTypeDefinition() : type;
}
