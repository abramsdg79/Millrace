using Millrace.Configuration;

namespace Millrace.Scenarios;

/// <summary>Renders docs/scenario-diagnostics.md from <see cref="ScenarioDiagnostics.All"/>, so the page cannot drift from the codes.</summary>
public static class ScenarioDiagnosticsReference
{
    private const string Introduction =
        "<!-- Generated from ScenarioDiagnostics.All by ScenarioDiagnosticsReference.Render(). Do not edit by hand:\n" +
        "     run the Millrace.Scenarios tests with MILLRACE_UPDATE_GOLDEN=1, read the result, commit it. -->\n\n" +
        "`millrace run`, `ScenarioLoader.Parse` and `ScenarioRunner.Run` report every problem in a scenario file as a\n" +
        "diagnostic with four parts: a **code**, a **JSON path** into the file (`$.timeline[1].args.amount`), a\n" +
        "**message** saying what is wrong, and a **fix** saying what to do. It is the same `ConfigDiagnostic` a\n" +
        "plant file's problems arrive as, and a diagnostic without a fix cannot be constructed.\n\n" +
        "Checking happens in two passes. `ScenarioLoader.Parse` is structural and needs no plant: MR200 to\n" +
        "MR204, and MR203 for a time that is not on the step the scenario itself declared. `ScenarioRunner`\n" +
        "then loads the plant and binds every action against the built simulation: MR205, MR206, and MR203\n" +
        "against the step the plant actually runs at. **Every check happens before tick 0**, so a scenario that\n" +
        "is wrong never produces a partial event log.\n\n";

    private const string Trailer =
        "## MR100–MR112 — the plant's own diagnostics\n\n" +
        "A scenario names a plant, and that plant is loaded by the same loader `millrace validate` uses. When the\n" +
        "plant has errors of its own they follow the MR205 line unchanged, with their codes, their paths and\n" +
        "their fixes. See [configuration diagnostics](configuration-diagnostics.md).\n";

    /// <summary>The page, ending in a newline.</summary>
    public static string Render() =>
        DiagnosticsReference.Render("Scenario diagnostics", ScenarioDiagnostics.All, Introduction, Trailer);
}
