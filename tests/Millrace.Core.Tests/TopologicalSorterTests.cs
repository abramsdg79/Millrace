using Millrace.Core.Graph;
using Xunit;

namespace Millrace.Core.Tests;

public class TopologicalSorterTests
{
    private static List<int>[] Graph(int count, params (int From, int To)[] edges)
    {
        var dependents = new List<int>[count];
        for (int i = 0; i < count; i++)
        {
            dependents[i] = [];
        }

        foreach ((int from, int to) in edges)
        {
            dependents[from].Add(to);
        }

        return dependents;
    }

    [Fact]
    public void PlacesProducersBeforeConsumers()
    {
        Assert.True(TopologicalSorter.TrySort(Graph(3, (2, 1), (1, 0)), out int[] order, out _));

        Assert.Equal(new[] { 2, 1, 0 }, order);
    }

    [Fact]
    public void TiesBreakByIndex()
    {
        Assert.True(TopologicalSorter.TrySort(Graph(3), out int[] independent, out _));
        Assert.True(TopologicalSorter.TrySort(Graph(3, (2, 0)), out int[] oneEdge, out _));

        Assert.Equal(new[] { 0, 1, 2 }, independent);
        Assert.Equal(new[] { 1, 2, 0 }, oneEdge);
    }

    [Fact]
    public void NamesTheCycleInEdgeOrder()
    {
        List<int>[] dependents = Graph(3, (0, 1), (1, 2), (2, 0));

        Assert.False(TopologicalSorter.TrySort(dependents, out int[] order, out List<int> cycle));

        Assert.Empty(order);
        Assert.Equal(3, cycle.Count);
        for (int i = 0; i < cycle.Count; i++)
        {
            int next = cycle[(i + 1) % cycle.Count];
            Assert.Contains(next, dependents[cycle[i]]);
        }
    }

    [Fact]
    public void AnObserverOfACycleIsNotReported()
    {
        // 0 consumes from 1; 1 and 2 feed each other.
        Assert.False(TopologicalSorter.TrySort(Graph(3, (1, 0), (1, 2), (2, 1)), out _, out List<int> cycle));

        Assert.Equal(2, cycle.Count);
        Assert.Contains(1, cycle);
        Assert.Contains(2, cycle);
        Assert.DoesNotContain(0, cycle);
    }
}
