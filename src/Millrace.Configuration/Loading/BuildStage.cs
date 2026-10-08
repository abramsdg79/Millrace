using System.Globalization;
using Millrace.Core;
using Millrace.Core.Io;
using Millrace.Core.Time;
using Millrace.Core.Validation;

namespace Millrace.Configuration.Loading;

/// <summary>Stage 6: hand the plant to Core; if it is valid, resolve and add the controllers (R80) and hand it over again.</summary>
internal static class BuildStage
{
    public static void Run(LoadState state)
    {
        var fallback = new SimulationOptions();
        var options = new SimulationOptions
        {
            Seed = state.Options.Seed ?? state.Defaults.Seed ?? fallback.Seed,
            TimeStep = state.Options.TimeStep ?? state.Defaults.TimeStep ?? fallback.TimeStep,
            StartTime = state.Options.StartTime ?? state.Defaults.StartTime ?? fallback.StartTime,
        };

        var builder = new SimulationBuilder(options);
        foreach (ComponentEntry entry in state.Components)
        {
            // File order, not build order: the order a plant is added in is part of what makes a run reproducible.
            builder.Add(entry.Node!);
        }

        foreach ((string name, TagBinding binding) in state.Bindings)
        {
            builder.Bind(name, binding);
        }

        IReadOnlyList<ValidationError> errors = builder.Validate().Errors;
        if (errors.Count == 0 && state.Controllers.Count > 0)
        {
            if (!ControllerPass.Run(state, builder))
            {
                return;
            }

            errors = builder.Validate().Errors;
        }

        foreach (ValidationError error in errors)
        {
            (string message, string fix) = Split(error);
            state.Error(error.Code, PathOf(state, error), message, fix);
        }

        state.Builder = builder;
        state.SimulationOptions = options;
    }

    /// <summary>
    /// Core messages read "symptom. fix." (R40). A MR016 quotes its claim's tag
    /// in the symptom, and a claim is any string (R137), so the search for the
    /// cut starts after the quoted tag.
    /// </summary>
    private static (string Message, string Fix) Split(ValidationError error)
    {
        string text = error.Message;
        int from = 0;
        if (error.Tag.Length > 0)
        {
            int quoted = text.IndexOf($"'{error.Tag}'", StringComparison.Ordinal);
            from = quoted < 0 ? 0 : quoted + error.Tag.Length + 2;
        }

        int cut = text.IndexOf(". ", from, StringComparison.Ordinal);
        return cut < 0
            ? (text, "Correct the plant so that this check passes.")
            : (text[..(cut + 1)], text[(cut + 2)..]);
    }

    private static string PathOf(LoadState state, ValidationError error)
    {
        string? fallback = null;
        foreach (string id in error.ComponentIds)
        {
            int dot = id.IndexOf('.', StringComparison.Ordinal);
            string top = dot < 0 ? id : id[..dot];
            ComponentEntry? entry = state.Components.FirstOrDefault(c => string.Equals(c.Id, top, StringComparison.Ordinal));
            if (entry is not null)
            {
                return entry.Path;
            }

            ControllerEntry? controller = state.Controllers.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.Ordinal));
            if (controller is null)
            {
                continue;
            }

            // R92: a scan period off the step is a block check with a key of its own.
            if (string.Equals(error.Code, "MR013", StringComparison.Ordinal))
            {
                return $"{controller.Path}.scanPeriodMs";
            }

            // R133: a claim's diagnostic lands on the claim — the entry of the controller whose list holds the tag at ClaimIndex.
            if (string.Equals(error.Code, "MR016", StringComparison.Ordinal))
            {
                int claim = error.ClaimIndex;
                if (claim >= 0 && claim < controller.Claims.Count && string.Equals(controller.Claims[claim], error.Tag, StringComparison.Ordinal))
                {
                    return string.Create(CultureInfo.InvariantCulture, $"{controller.Path}.claims[{claim}]");
                }

                fallback ??= controller.Path;
                continue;
            }

            return controller.Path;
        }

        return fallback ?? "$";
    }
}
