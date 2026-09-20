using System.Globalization;
using System.Text.Json;
using Dse.Core.Catalogue;

namespace Dse.Configuration.Loading;

/// <summary>Stage 3: every reference names a real component of a suitable type, and there is an order to build them in.</summary>
internal static class ReferenceStage
{
    public static void Run(LoadState state)
    {
        var byId = state.Components.ToDictionary(c => c.Id, StringComparer.Ordinal);
        foreach (ComponentEntry entry in state.Components)
        {
            foreach ((string path, string id, ParameterDescriptor parameter) in References(entry.Descriptor.Parameters, entry.Parameters, entry.ParametersPath))
            {
                if (!byId.TryGetValue(id, out ComponentEntry? target))
                {
                    state.Error(ConfigDiagnostics.MissingReference, path, $"'{id}' is not a component in this plant.", Suggest.Fix(id, byId.Keys, "components"));
                }
                else if (ReferenceEquals(target, entry))
                {
                    state.Error(ConfigDiagnostics.ReferenceCycle, path, $"'{id}' references itself.", "Reference a different component.");
                }
                else if (!Supplies(target.Descriptor, parameter.Capability!))
                {
                    List<string> able = state.Components.Where(c => Supplies(c.Descriptor, parameter.Capability!)).Select(c => c.Id).ToList();
                    state.Error(
                        ConfigDiagnostics.MissingCapability,
                        path,
                        $"'{id}' is a {target.Descriptor.Type}, which cannot supply {parameter.Capability!.Name}; '{parameter.Name}' needs one.",
                        able.Count == 0
                            ? $"No component in this plant supplies {parameter.Capability.Name}; add one that does, then reference it."
                            : $"Reference one of {Suggest.List(able)}.");
                }
                else
                {
                    entry.ReferencedIds.Add(id);
                }
            }
        }

        if (!state.HasErrors)
        {
            Order(state, byId);
        }
    }

    /// <summary>Every reference in a parameters object, through groups and group lists, with its JSON path.</summary>
    private static IEnumerable<(string Path, string Id, ParameterDescriptor Parameter)> References(
        IReadOnlyList<ParameterDescriptor> schema, JsonElement json, string path)
    {
        if (json.ValueKind != JsonValueKind.Object)
        {
            yield break;
        }

        foreach (ParameterDescriptor parameter in schema)
        {
            if (!json.TryGetProperty(parameter.Name, out JsonElement value))
            {
                continue;
            }

            string childPath = $"{path}.{parameter.Name}";
            switch (parameter.Kind)
            {
                case ParameterKind.Reference when value.ValueKind == JsonValueKind.String:
                    yield return (childPath, value.GetString()!, parameter);
                    break;

                case ParameterKind.Group:
                    foreach (var found in References(parameter.Children, value, childPath))
                    {
                        yield return found;
                    }

                    break;

                case ParameterKind.GroupList when value.ValueKind == JsonValueKind.Array:
                    int index = 0;
                    foreach (JsonElement item in value.EnumerateArray())
                    {
                        foreach (var found in References(parameter.Children, item, string.Create(CultureInfo.InvariantCulture, $"{childPath}[{index}]")))
                        {
                            yield return found;
                        }

                        index++;
                    }

                    break;
            }
        }
    }

    private static bool Supplies(ComponentDescriptor descriptor, Type capability) =>
        descriptor.Provides.Any(capability.IsAssignableFrom);

    /// <summary>File order, except that a component waits for what it references. Deterministic.</summary>
    private static void Order(LoadState state, Dictionary<string, ComponentEntry> byId)
    {
        var placed = new HashSet<string>(StringComparer.Ordinal);
        var order = new List<ComponentEntry>(state.Components.Count);
        while (order.Count < state.Components.Count)
        {
            ComponentEntry? next = state.Components.FirstOrDefault(c => !placed.Contains(c.Id) && c.ReferencedIds.All(placed.Contains));
            if (next is null)
            {
                ReportCycle(state, byId, placed);
                return;
            }

            placed.Add(next.Id);
            order.Add(next);
        }

        state.BuildOrder = order;
    }

    private static void ReportCycle(LoadState state, Dictionary<string, ComponentEntry> byId, HashSet<string> placed)
    {
        // Every unplaced component waits on an unplaced one, so walking "first unplaced referent" must revisit something.
        ComponentEntry start = state.Components.First(c => !placed.Contains(c.Id));
        var walk = new List<ComponentEntry> { start };
        ComponentEntry current = start;
        while (true)
        {
            current = byId[current.ReferencedIds.First(id => !placed.Contains(id))];
            int seenAt = walk.IndexOf(current);
            if (seenAt >= 0)
            {
                List<ComponentEntry> cycle = walk.GetRange(seenAt, walk.Count - seenAt);
                string path = string.Join(" -> ", cycle.Select(c => c.Id).Append(cycle[0].Id));
                state.Error(
                    ConfigDiagnostics.ReferenceCycle,
                    cycle[0].ParametersPath,
                    $"These components reference each other in a loop: {path}.",
                    "Remove one of the references; a component cannot be built before the thing it references.");
                return;
            }

            walk.Add(current);
        }
    }
}
