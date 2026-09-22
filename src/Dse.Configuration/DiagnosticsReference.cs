using System.Text;

namespace Dse.Configuration;

/// <summary>Renders docs/configuration-diagnostics.md from the code table, so the page cannot drift from the codes.</summary>
public static class DiagnosticsReference
{
    public static string Render()
    {
        var page = new StringBuilder();
        page.Append("# Configuration diagnostics\n\n");
        page.Append("<!-- Generated from ConfigDiagnostics.All by DiagnosticsReference.Render(). Do not edit by hand:\n");
        page.Append("     run the Dse.Configuration tests with DSE_UPDATE_GOLDEN=1, read the result, commit it. -->\n\n");
        page.Append("`dse validate` and `PlantLoader.Load` report every problem in a plant file as a diagnostic with four\n");
        page.Append("parts: a **code**, a **JSON path** into the file (`$.components[3].parameters.motor.ratedPowerW`), a\n");
        page.Append("**message** saying what is wrong, and a **fix** saying what to do. A diagnostic without a fix cannot be\n");
        page.Append("constructed.\n\n");
        page.Append("The loader works in stages — parse, structure, references, instantiate, wire, build — and stops at the\n");
        page.Append("end of the first stage that found an error, having reported *every* error that stage could find. Fixing\n");
        page.Append("what is reported may therefore reveal errors from a later stage.\n\n");
        page.Append("| Code | Meaning |\n|---|---|\n");
        foreach (DiagnosticInfo info in ConfigDiagnostics.All)
        {
            page.Append("| ").Append(info.Code).Append(" | ").Append(info.Title).Append(" |\n");
        }

        page.Append('\n');
        foreach (DiagnosticInfo info in ConfigDiagnostics.All)
        {
            page.Append("## ").Append(info.Code).Append(" — ").Append(info.Title).Append("\n\n");
            page.Append(info.Explanation).Append("\n\n");
        }

        page.Append("## DSE001–DSE011 — plant validation\n\n");
        page.Append("Codes below DSE100 come from `SimulationBuilder.Validate()` and mean the same for a plant built in code:\n");
        page.Append("duplicate ids, unconnected required inputs, algebraic loops, belts too fast for their cells, incompatible\n");
        page.Append("flow links, tag conflicts. The loader passes them through with the path of the first component involved;\n");
        page.Append("their message is split at its first sentence into message and fix. See `docs/architecture.md`.\n");
        return page.ToString();
    }
}
