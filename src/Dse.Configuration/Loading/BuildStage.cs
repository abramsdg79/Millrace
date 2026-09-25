using Dse.Core;
using Dse.Core.Io;
using Dse.Core.Time;
using Dse.Core.Validation;

namespace Dse.Configuration.Loading;

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
            (string message, string fix) = Split(error.Message);
            state.Error(error.Code, PathOf(state, error), message, fix);
        }

        state.Builder = builder;
        state.SimulationOptions = options;
    }

    /// <summary>Core messages read "symptom. fix." (R40).</summary>
    private static (string Message, string Fix) Split(string text)
    {
        int cut = text.IndexOf(". ", StringComparison.Ordinal);
        return cut < 0
            ? (text, "Correct the plant so that this check passes.")
            : (text[..(cut + 1)], text[(cut + 2)..]);
    }

    private static string PathOf(LoadState state, ValidationError error)
    {
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
            if (controller is not null)
            {
                // R92: a scan period off the step is the one block check with a key of its own.
                return string.Equals(error.Code, "DSE013", StringComparison.Ordinal) ? $"{controller.Path}.scanPeriodMs" : controller.Path;
            }
        }

        return "$";
    }
}
