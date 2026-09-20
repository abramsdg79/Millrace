using Dse.Core.Catalogue;
using Dse.Core.Graph;

namespace Dse.Configuration.Loading;

/// <summary>Stage 4: build every component, referents first. A failure skips its dependents without further noise.</summary>
internal static class InstantiateStage
{
    public static void Run(LoadState state)
    {
        var failed = new HashSet<string>(StringComparer.Ordinal);
        foreach (ComponentEntry entry in state.BuildOrder)
        {
            if (entry.ReferencedIds.Any(failed.Contains) || !TryBuild(state, entry))
            {
                failed.Add(entry.Id);
            }
        }
    }

    private static bool TryBuild(LoadState state, ComponentEntry entry)
    {
        var issues = new List<BindingIssue>();
        ParameterValues? values = ParameterBinder.Bind(
            entry.Descriptor.Parameters, entry.Parameters, entry.ParametersPath, state.Context, construct: true, issues);
        state.AddIssues(issues);
        if (values is null)
        {
            return false;
        }

        ISimNode node;
        try
        {
            node = entry.Descriptor.Factory(entry.Id, values);
        }
        catch (ArgumentException ex)
        {
            state.Error(
                ConfigDiagnostics.Rejected,
                entry.Path,
                $"'{entry.Id}' ({entry.Descriptor.Type}) rejected its parameters: {WithoutParameterSuffix(ex.Message)}",
                "Change the parameter the message names; each value is valid alone, the combination is not.");
            return false;
        }
#pragma warning disable CA1031 // A defective third-party factory must become a diagnostic, not a crash.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            state.Error(
                ConfigDiagnostics.Rejected,
                entry.Path,
                $"The factory for type '{entry.Descriptor.Type}' failed with {ex.GetType().Name}: {ex.Message}",
                $"This is a defect in module '{state.Catalogue.ModuleOf(entry.Descriptor)}', not in the plant file; report it with this file. " +
                "The module's conformance test should have caught it.");
            return false;
        }

        entry.Node = node;
        state.Context.AddNode(node);
        return true;
    }

    // ArgumentException appends " (Parameter 'x')" naming a C# parameter, which means nothing to a plant author.
    private static string WithoutParameterSuffix(string message)
    {
        int cut = message.IndexOf(" (Parameter '", StringComparison.Ordinal);
        string text = cut < 0 ? message : message[..cut];
        return text.EndsWith('.') ? text : text + ".";
    }
}
