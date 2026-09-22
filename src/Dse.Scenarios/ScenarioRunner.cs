using System.Globalization;
using Dse.Configuration;
using Dse.Core;
using Dse.Core.Catalogue;
using Dse.Core.Faults;
using Dse.Io;

namespace Dse.Scenarios;

/// <summary>
/// Runs a scenario against a plant. Pure given the two texts — the caller reads
/// the files — so a test runs a whole scenario from strings. Nothing ticks
/// until every action has bound.
/// </summary>
public static class ScenarioRunner
{
    private const string BindFix =
        "Use a component, a fault and arguments the plant declares; `dse catalog export` lists every component's faults.";

    /// <summary>Loads the plant, binds every action, and runs only if nothing is wrong.</summary>
    public static ScenarioRunResult Run(Scenario scenario, string plantJson, ComponentCatalogue catalogue)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        ArgumentNullException.ThrowIfNull(plantJson);
        ArgumentNullException.ThrowIfNull(catalogue);

        LoadResult load = PlantLoader.Load(plantJson, catalogue, scenario.ToLoadOptions());
        if (!load.IsValid)
        {
            return new ScenarioRunResult(PlantDiagnostics(scenario, load), null, null);
        }

        Simulation simulation = load.Builder!.Build();
        TimeSpan step = load.Options!.TimeStep;
        var diagnostics = new List<ConfigDiagnostic>();

        if (scenario.Duration.Ticks % step.Ticks != 0L)
        {
            diagnostics.Add(ScenarioDiagnostics.OffTick("$.duration", "The duration", scenario.Duration, step));
        }

        for (int i = 0; i < scenario.Timeline.Count; i++)
        {
            Schedule(simulation, scenario, i, step, diagnostics);
        }

        if (diagnostics.Count > 0)
        {
            return new ScenarioRunResult(diagnostics, null, null);
        }

        simulation.RunFor(scenario.Duration);
        return new ScenarioRunResult(
            [],
            simulation.Events,
            new RunSummary(simulation.Clock.TickCount, simulation.Events.Records.Count, scenario.Timeline.Count));
    }

    /// <summary>One line naming the plant, then the plant's own diagnostics unchanged: their codes, their paths, their fixes.</summary>
    private static List<ConfigDiagnostic> PlantDiagnostics(Scenario scenario, LoadResult load)
    {
        int errors = load.Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error);
        var diagnostics = new List<ConfigDiagnostic>(load.Diagnostics.Count + 1)
        {
            ScenarioDiagnostics.Error(
                ScenarioDiagnostics.PlantInvalid,
                "$.plant",
                string.Create(CultureInfo.InvariantCulture,
                    $"The plant '{scenario.PlantPath}' has {errors} error{(errors == 1 ? string.Empty : "s")} of its own; they follow."),
                "Fix the plant file and run the scenario again; `dse validate` reports exactly these errors."),
        };

        diagnostics.AddRange(load.Diagnostics);
        return diagnostics;
    }

    private static void Schedule(
        Simulation simulation, Scenario scenario, int index, TimeSpan step, List<ConfigDiagnostic> diagnostics)
    {
        ScenarioAction action = scenario.Timeline[index];
        string path = string.Create(CultureInfo.InvariantCulture, $"$.timeline[{index}]");

        // "at >= 0" is DSE202 wherever it is checked (spec 3): the parser rejects
        // a negative number as an out-of-range value, and so does this, for a
        // Scenario built in code rather than parsed.
        if (action.At < TimeSpan.Zero)
        {
            diagnostics.Add(ScenarioDiagnostics.Error(
                ScenarioDiagnostics.BadValue,
                $"{path}.at",
                string.Create(CultureInfo.InvariantCulture,
                    $"An action at {ScenarioDiagnostics.Seconds(action.At)} s is before the start of the run."),
                "Move the action to zero or later; \"at\" is seconds from the start of the run."));
            return;
        }

        if (action.At.Ticks % step.Ticks != 0L)
        {
            diagnostics.Add(ScenarioDiagnostics.OffTick($"{path}.at", "The action time", action.At, step));
        }

        if (action.At >= scenario.Duration)
        {
            diagnostics.Add(ScenarioDiagnostics.NotBeforeTheEnd($"{path}.at", action.At, scenario.Duration));
        }

        switch (action)
        {
            case WriteAction write:
                Bind(simulation, write, path, diagnostics);
                break;

            case FaultAction fault:
                Attempt(diagnostics, path, "fault", () => simulation.InjectFaultAt(
                    fault.At, fault.ComponentId, fault.FaultId, new FaultArguments(fault.Arguments.ToArray())));
                break;

            case ClearAction clear:
                Attempt(diagnostics, path, "clear", () => simulation.ClearFaultAt(clear.At, clear.ComponentId, clear.FaultId));
                break;

            default:
                diagnostics.Add(ScenarioDiagnostics.Error(
                    ScenarioDiagnostics.DoesNotBind,
                    path,
                    $"'{action.GetType().Name}' is not an action this runner knows.",
                    "Use a write, a fault or a clear action."));
                break;
        }
    }

    /// <summary>
    /// A write is checked against the directory here (R57), so the message can
    /// name the tag, its kind and the offending value; <see cref="Attempt"/>
    /// still wraps the call, so an engine exception cannot escape as a crash.
    /// </summary>
    private static void Bind(Simulation simulation, WriteAction write, string path, List<ConfigDiagnostic> diagnostics)
    {
        if (!simulation.IO.Directory.TryFind(write.Tag, out TagDescriptor tag))
        {
            diagnostics.Add(ScenarioDiagnostics.Error(
                ScenarioDiagnostics.DoesNotBind,
                $"{path}.write",
                $"There is no tag '{write.Tag}' in this plant.",
                Suggest.Fix(write.Tag, simulation.IO.Directory.Tags.Select(t => t.Name), "tags")));
            return;
        }

        if (tag.Access != TagAccess.ReadWrite)
        {
            diagnostics.Add(ScenarioDiagnostics.Error(
                ScenarioDiagnostics.DoesNotBind,
                $"{path}.write",
                $"Tag '{tag.Name}' is read-only; a scenario cannot write it.",
                "Write a tag whose access is ReadWrite; `dse tags <plant>` shows each tag's access."));
            return;
        }

        if (write.Value.ToTagValue(tag.Kind) is not { } value)
        {
            diagnostics.Add(ScenarioDiagnostics.Error(
                ScenarioDiagnostics.DoesNotBind,
                $"{path}.value",
                string.Create(CultureInfo.InvariantCulture, $"Tag '{tag.Name}' is a {tag.Kind} tag; {write.Value} is not a {tag.Kind} value."),
                tag.Kind switch
                {
                    TagKind.Bool => "Write true or false.",
                    TagKind.Double => "Write a number, such as 1.5.",
                    _ => "Write a whole number, such as 3.",
                }));
            return;
        }

        Attempt(diagnostics, path, "write", () => simulation.WriteAt(write.At, write.Tag, value));
    }

    /// <summary>
    /// Schedules one action, turning the engine's own "no such thing" into
    /// DSE206 at the JSON path of the part that was wrong (R58). The engine's
    /// messages already name what exists, so they are the message.
    /// </summary>
    private static void Attempt(List<ConfigDiagnostic> diagnostics, string path, string key, Action schedule)
    {
        try
        {
            schedule();
        }
        catch (Exception ex) when (ex is KeyNotFoundException or ArgumentException or InvalidOperationException)
        {
            string where = ex is ArgumentException argument
                ? argument.ParamName switch
                {
                    "faultId" => $"{path}.id",
                    "given" => $"{path}.args",
                    _ => $"{path}.{key}",
                }
                : $"{path}.{key}";

            diagnostics.Add(ScenarioDiagnostics.Error(ScenarioDiagnostics.DoesNotBind, where, Sentence(ex.Message), BindFix));
        }
    }

    /// <summary>The exception's own words, without the " (Parameter 'x')" the base class appends, ending in a full stop.</summary>
    private static string Sentence(string message)
    {
        int cut = message.IndexOf(" (Parameter '", StringComparison.Ordinal);
        string text = (cut < 0 ? message : message[..cut]).TrimEnd();
        return text.EndsWith('.') ? text : text + ".";
    }
}
