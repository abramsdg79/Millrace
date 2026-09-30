using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Dse.Core;
using Dse.Core.Catalogue;
using Dse.Io;

namespace Dse.Configuration.Loading;

/// <summary>
/// The build stage's middle step (R80), run once the plant alone has validated: the controllers. Every tag a controller
/// may name is known before any block exists — the plant's own, from the
/// builder, and every controller's owned tags, from its descriptor — so every
/// tag and value in every controller is resolved, and every error reported,
/// before the first factory runs.
/// </summary>
internal static class ControllerPass
{
    /// <summary>Adds every controller's block to <paramref name="builder"/> in file order; false after reporting why not.</summary>
    public static bool Run(LoadState state, SimulationBuilder builder)
    {
        if (state.Controllers.Count == 0)
        {
            return true;
        }

        // Plant tags first: on a name clash the plant's entry wins and Validate() reports DSE015 (R85).
        var tags = new List<(string Name, TagKind Kind, TagAccess Access)>();
        foreach (TagDescriptor tag in builder.PlantTags())
        {
            tags.Add((tag.Name, tag.Kind, tag.Access));
        }

        var declared = new Dictionary<ControllerEntry, IReadOnlyList<(TagSpec Spec, TagAccess Access)>>(ReferenceEqualityComparer.Instance);
        foreach (ControllerEntry entry in state.Controllers)
        {
            IReadOnlyList<(TagSpec Spec, TagAccess Access)> owned = OwnedTags(state, entry);
            declared[entry] = owned;
            foreach ((TagSpec spec, TagAccess access) in owned)
            {
                tags.Add((spec.Name, spec.Kind, access));
            }
        }

        if (state.HasErrors)
        {
            return false;
        }

        state.Context.UseTags(tags);

        var bound = new List<(ControllerEntry Entry, ParameterValues Values)>(state.Controllers.Count);
        foreach (ControllerEntry entry in state.Controllers)
        {
            var issues = new List<BindingIssue>();
            ParameterValues? values = ParameterBinder.Bind(
                entry.Descriptor.Parameters, entry.Parameters, entry.ParametersPath, state.Context, construct: true, issues);
            state.AddIssues(issues);
            if (values is not null)
            {
                bound.Add((entry, values));
            }
        }

        if (state.HasErrors)
        {
            return false;
        }

        var blocks = new List<(ControllerEntry Entry, IScanBlock Block)>(bound.Count);
        foreach ((ControllerEntry entry, ParameterValues values) in bound)
        {
            if (TryBuild(state, entry, values, out IScanBlock? block) && PinsMatch(state, entry, block, declared[entry]))
            {
                blocks.Add((entry, block));
            }
        }

        if (state.HasErrors)
        {
            return false;
        }

        // File order is scan order, and the later block wins a same-tick write. Claims are Core's to check (DSE016).
        foreach ((ControllerEntry entry, IScanBlock block) in blocks)
        {
            builder.AddScanBlock(block, entry.Claims);
        }

        return true;
    }

    private static IReadOnlyList<(TagSpec Spec, TagAccess Access)> OwnedTags(LoadState state, ControllerEntry entry)
    {
        try
        {
            IReadOnlyList<(TagSpec Spec, TagAccess Access)>? owned = entry.Descriptor.OwnedTags(entry.Id, entry.CheckedValues);
            if (owned is null || owned.Any(t => t.Spec is null))
            {
                state.Error(
                    ConfigDiagnostics.Rejected,
                    entry.Path,
                    $"The owned-tag function of type '{entry.Descriptor.Type}' returned null or a null tag.",
                    ModuleDefect(state, entry));
                return [];
            }

            return owned;
        }
#pragma warning disable CA1031 // A defective third-party descriptor must become a diagnostic, not a crash.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            state.Error(
                ConfigDiagnostics.Rejected,
                entry.Path,
                $"The owned-tag function of type '{entry.Descriptor.Type}' failed with {ex.GetType().Name}: {InstantiateStage.AsSentence(ex.Message)}",
                ModuleDefect(state, entry));
            return [];
        }
    }

    private static bool TryBuild(LoadState state, ControllerEntry entry, ParameterValues values, [NotNullWhen(true)] out IScanBlock? block)
    {
        try
        {
            IScanBlock? made = entry.Descriptor.Factory(entry.Id, entry.ScanPeriod, values);
            if (made is null)
            {
                state.Error(
                    ConfigDiagnostics.Rejected,
                    entry.Path,
                    $"The factory for type '{entry.Descriptor.Type}' returned null.",
                    ModuleDefect(state, entry));
                block = null;
                return false;
            }

            // An id the factory was not given would publish tags nothing resolved against, and one that
            // breaks the name rules would throw out of AddScanBlock; a period it was not given breaks DSE013's path.
            if (!string.Equals(made.Id, entry.Id, StringComparison.Ordinal) || made.ScanPeriod != entry.ScanPeriod)
            {
                state.Error(
                    ConfigDiagnostics.Rejected,
                    entry.Path,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"The factory for type '{entry.Descriptor.Type}' built block '{made.Id}' scanning every {made.ScanPeriod.TotalMilliseconds} ms, but was given '{entry.Id}' and {entry.ScanPeriod.TotalMilliseconds} ms."),
                    ModuleDefect(state, entry));
                block = null;
                return false;
            }

            block = made;
            return true;
        }
        catch (ArgumentException ex)
        {
            state.Error(
                ConfigDiagnostics.Rejected,
                entry.Path,
                $"'{entry.Id}' ({entry.Descriptor.Type}) rejected its parameters: {InstantiateStage.AsSentence(ex.Message)}",
                "Change the parameter the message names; each value is valid alone, the combination is not.");
        }
#pragma warning disable CA1031 // A defective third-party factory must become a diagnostic, not a crash.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            state.Error(
                ConfigDiagnostics.Rejected,
                entry.Path,
                $"The factory for type '{entry.Descriptor.Type}' failed with {ex.GetType().Name}: {InstantiateStage.AsSentence(ex.Message)}",
                ModuleDefect(state, entry));
        }

        block = null;
        return false;
    }

    /// <summary>
    /// Reads the block's four pin lists once and checks its owned tags against what its descriptor declared, which
    /// every other controller was resolved against; false after reporting a defect in the module (R100).
    /// </summary>
    private static bool PinsMatch(
        LoadState state, ControllerEntry entry, IScanBlock block, IReadOnlyList<(TagSpec Spec, TagAccess Access)> declared)
    {
        string pin = nameof(IScanBlock.Inputs);
        IReadOnlyList<TagSpec> outputs;
        IReadOnlyList<TagSpec> commands;
        try
        {
            if (!NoNulls(block.Inputs))
            {
                return NullPin(state, entry, pin);
            }

            pin = nameof(IScanBlock.Writes);
            if (!NoNulls(block.Writes))
            {
                return NullPin(state, entry, pin);
            }

            pin = nameof(IScanBlock.Outputs);
            outputs = block.Outputs;
            if (!NoNulls(outputs))
            {
                return NullPin(state, entry, pin);
            }

            pin = nameof(IScanBlock.Commands);
            commands = block.Commands;
            if (!NoNulls(commands))
            {
                return NullPin(state, entry, pin);
            }
        }
#pragma warning disable CA1031 // A defective third-party block must become a diagnostic, not a crash.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            state.Error(
                ConfigDiagnostics.Rejected,
                entry.Path,
                $"Reading the {pin} of the block built for type '{entry.Descriptor.Type}' failed with {ex.GetType().Name}: {InstantiateStage.AsSentence(ex.Message)}",
                ModuleDefect(state, entry));
            return false;
        }

        List<string> owns = outputs.Select(s => Owned($"{entry.Id}.{s.Name}", s.Kind, TagAccess.ReadOnly))
            .Concat(commands.Select(s => Owned($"{entry.Id}.{s.Name}", s.Kind, TagAccess.ReadWrite)))
            .Order(StringComparer.Ordinal)
            .ToList();
        List<string> declares = declared.Select(t => Owned(t.Spec.Name, t.Spec.Kind, t.Access)).Order(StringComparer.Ordinal).ToList();
        if (owns.SequenceEqual(declares, StringComparer.Ordinal))
        {
            return true;
        }

        state.Error(
            ConfigDiagnostics.Rejected,
            entry.Path,
            $"The block built for type '{entry.Descriptor.Type}' owns {List(owns)}, but its descriptor declares {List(declares)}.",
            ModuleDefect(state, entry));
        return false;

        static string Owned(string name, TagKind kind, TagAccess access) => $"'{name}' ({kind} {access})";

        static string List(List<string> tags) => tags.Count == 0 ? "no tags" : string.Join(", ", tags);
    }

    private static bool NoNulls<T>(IReadOnlyList<T>? pins)
        where T : class =>
        pins is not null && pins.All(p => p is not null);

    private static bool NullPin(LoadState state, ControllerEntry entry, string pin)
    {
        state.Error(
            ConfigDiagnostics.Rejected,
            entry.Path,
            $"The block built for type '{entry.Descriptor.Type}' has a null {pin} list or a null entry in it.",
            ModuleDefect(state, entry));
        return false;
    }

    private static string ModuleDefect(LoadState state, ControllerEntry entry) =>
        $"Report this to the author of module '{state.Catalogue.ModuleOf(entry.Descriptor)}' together with this plant file; " +
        "it is a defect in the module, not in the plant. The module's conformance test should have caught it.";
}
