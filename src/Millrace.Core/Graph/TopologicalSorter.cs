namespace Millrace.Core.Graph;

/// <summary>
/// Kahn's algorithm over an adjacency list, ties broken by index so the order
/// is stable for a given registration order. Shared by the signal resolver and
/// the material-flow resolver; each maps indices to its own ids.
/// </summary>
internal static class TopologicalSorter
{
    /// <summary>
    /// <paramref name="dependents"/>[i] lists the indices that must come after i.
    /// On failure <paramref name="cycle"/> names one loop in edge order.
    /// </summary>
    public static bool TrySort(List<int>[] dependents, out int[] order, out List<int> cycle)
    {
        ArgumentNullException.ThrowIfNull(dependents);

        int count = dependents.Length;
        var inDegree = new int[count];
        for (int i = 0; i < count; i++)
        {
            foreach (int dependent in dependents[i])
            {
                inDegree[dependent]++;
            }
        }

        // Ready set kept sorted by index so ordering is stable.
        var ready = new SortedSet<int>();
        for (int i = 0; i < count; i++)
        {
            if (inDegree[i] == 0)
            {
                ready.Add(i);
            }
        }

        var result = new int[count];
        int placed = 0;
        while (ready.Count > 0)
        {
            int next = ready.Min;
            ready.Remove(next);
            result[placed++] = next;

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
            order = result;
            cycle = [];
            return true;
        }

        order = [];
        cycle = FindCycle(dependents, inDegree);
        return false;
    }

    /// <summary>
    /// Walks the residual subgraph's predecessors — backward, from a stalled
    /// node toward whatever still owes it an input — to name one loop. A
    /// forward walk over dependents can dead-end on a residual node that merely
    /// consumes from a cycle without being part of it (an observer); every
    /// residual node is guaranteed a residual predecessor, so the backward walk
    /// cannot dead-end.
    /// </summary>
    private static List<int> FindCycle(List<int>[] dependents, int[] inDegree)
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

        List<int> loop = path.Skip(onPath[current]).ToList();
        loop.Reverse();
        return loop;
    }
}
