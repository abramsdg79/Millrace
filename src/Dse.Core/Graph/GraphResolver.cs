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
        var inDegree = new int[count];
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
                inDegree[consumer]++;
            }
        }

        // Ready set kept sorted by registration index so ordering is stable.
        var ready = new SortedSet<int>();
        for (int i = 0; i < count; i++)
        {
            if (inDegree[i] == 0)
            {
                ready.Add(i);
            }
        }

        var result = new ISimComponent[count];
        int placed = 0;
        while (ready.Count > 0)
        {
            int next = ready.Min;
            ready.Remove(next);
            result[placed++] = components[next];

            foreach (int dependent in dependents[next])
            {
                if (--inDegree[dependent] == 0)
                {
                    ready.Add(dependent);
                }
            }
        }

        if (placed == count)
        {
            ordered = result;
            cycle = [];
            return true;
        }

        ordered = [];
        cycle = FindCycle(components, dependents, inDegree);
        return false;
    }

    /// <summary>
    /// Walks the residual subgraph's predecessors — backward, from a stalled
    /// component toward whatever still owes it an input — to name one loop. A
    /// forward walk over dependents can dead-end on a residual component that
    /// merely consumes from a cycle without being part of it (an observer);
    /// every residual component is guaranteed a residual predecessor, so the
    /// backward walk cannot dead-end.
    /// </summary>
    private static List<string> FindCycle(
        IReadOnlyList<ISimComponent> components,
        List<int>[] dependents,
        int[] inDegree)
    {
        int start = -1;
        for (int i = 0; i < inDegree.Length; i++)
        {
            if (inDegree[i] > 0)
            {
                start = i;
                break;
            }
        }

        if (start < 0)
        {
            return [];
        }

        int count = inDegree.Length;
        var predecessors = new List<int>[count];
        for (int i = 0; i < count; i++)
        {
            predecessors[i] = [];
        }

        for (int producer = 0; producer < count; producer++)
        {
            if (inDegree[producer] <= 0)
            {
                continue;
            }

            foreach (int dependent in dependents[producer])
            {
                if (inDegree[dependent] > 0)
                {
                    predecessors[dependent].Add(producer);
                }
            }
        }

        var path = new List<int>();
        var onPath = new Dictionary<int, int>();
        int current = start;
        while (!onPath.ContainsKey(current))
        {
            onPath[current] = path.Count;
            path.Add(current);
            current = predecessors[current][0];
        }

        List<string> loop = path.Skip(onPath[current]).Select(i => components[i].Id).ToList();
        loop.Reverse();
        return loop;
    }
}
