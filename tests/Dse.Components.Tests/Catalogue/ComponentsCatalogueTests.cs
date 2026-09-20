using System.Reflection;
using Dse.Components.Conveyors;
using Dse.Components.Flow;
using Dse.Core.Flow;
using Dse.Core.Graph;
using Dse.Core.Testing;

namespace Dse.Components.Tests.Catalogue;

public class ComponentsCatalogueTests
{
    /// <summary>
    /// Types that have no descriptor YET. Each rollout task deletes the types it
    /// covers; Task 8 deletes this array. Never add to it.
    /// </summary>
    private static readonly Type[] Pending =
    [
        // Task 8 — conveyor
        typeof(Conveyor),
    ];

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
            .Where(t => !built.Contains(Definition(t)) && !Pending.Contains(t))
            .Select(t => t.FullName!)
            .Order(StringComparer.Ordinal)
            .ToList();
        List<string> stale = Pending.Where(t => built.Contains(Definition(t))).Select(t => t.Name).ToList();

        Assert.Empty(stale);
        Assert.Empty(missing);
    }

    private static Type Definition(Type type) => type.IsGenericType ? type.GetGenericTypeDefinition() : type;
}
