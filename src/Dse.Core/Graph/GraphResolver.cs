namespace Dse.Core.Graph;

/// <summary>
/// Turns a wired component set into a fixed evaluation order. That order is the
/// determinism guarantee, so it is computed once, at build time, and the graph
/// is immutable afterwards.
/// </summary>
public static class GraphResolver
{
    /// <summary>
    /// Topologically sorts <paramref name="components"/>. On failure
    /// <paramref name="cycle"/> names the components in one unresolvable loop.
    /// </summary>
    public static bool TryResolve(
        IReadOnlyList<ISimComponent> components,
        out ISimComponent[] ordered,
        out IReadOnlyList<string> cycle)
    {
        ArgumentNullException.ThrowIfNull(components);

        int count = components.Count;
        var indexById = new Dictionary<string, int>(count, StringComparer.Ordinal);
        for (int i = 0; i < count; i++)
        {
            indexById[components[i].Id] = i;
        }

        // dependents[i] = indices of components that must run after i.
        var dependents = new List<int>[count];
        for (int i = 0; i < count; i++)
        {
            dependents[i] = [];
        }

        for (int consumer = 0; consumer < count; consumer++)
        {
            ISimComponent component = components[consumer];
            if (!component.HasDirectFeedthrough)
            {
                continue;
            }

            foreach (Port port in component.Ports)
            {
                Port? source = port.SourcePort;
                if (source is null || !indexById.TryGetValue(source.OwnerId, out int producer))
                {
                    continue;
                }

                if (producer == consumer)
                {
                    continue;
                }

                dependents[producer].Add(consumer);
            }
        }

        if (!TopologicalSorter.TrySort(dependents, out int[] order, out List<int> loop))
        {
            ordered = [];
            cycle = loop.Select(i => components[i].Id).ToList();
            return false;
        }

        ordered = new ISimComponent[count];
        for (int i = 0; i < count; i++)
        {
            ordered[i] = components[order[i]];
        }

        cycle = [];
        return true;
    }
}
