using System.Text.Json;
using Dse.Configuration.Loading;
using Dse.Core.Catalogue;
using Dse.Core.Graph;

namespace Dse.Configuration;

/// <summary>
/// Loads a declarative plant. Stages run in order; a stage runs only if no
/// earlier one reported an error, and reports every error it can find.
/// </summary>
public static class PlantLoader
{
    private static readonly Action<LoadState>[] Stages =
    [
        StructureStage.Run,
    ];

    public static LoadResult Load(string json, ComponentCatalogue catalogue, LoadOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentNullException.ThrowIfNull(catalogue);

        var state = new LoadState(catalogue, options ?? new LoadOptions());
        using JsonDocument? document = ParseStage.Run(json, state);
        if (document is not null)
        {
            foreach (Action<LoadState> stage in Stages)
            {
                stage(state);
                if (state.HasErrors)
                {
                    break;
                }
            }
        }

        bool valid = !state.HasErrors && state.Builder is not null;
        return new LoadResult(
            state.Diagnostics,
            valid ? state.Builder : null,
            valid ? state.SimulationOptions : null,
            valid ? new PlantSummary(state.Components.Count, state.Signals.Count, state.Flows.Count, state.Tags.Count) : null,
            valid
                ? state.Components.ToDictionary(c => c.Id, c => c.Node!, StringComparer.Ordinal)
                : new Dictionary<string, ISimNode>(StringComparer.Ordinal));
    }
}
