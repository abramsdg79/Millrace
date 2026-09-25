using System.Text;

namespace Dse.Configuration;

/// <summary>
/// Renders a diagnostics reference page from a code table, so a page cannot
/// drift from the codes. <see cref="Render()"/> is
/// docs/configuration-diagnostics.md; the parametric overload is how another
/// family of codes — scenarios, say — renders its own page without this
/// assembly having to know about it.
/// </summary>
public static class DiagnosticsReference
{
    private const string ConfigurationIntroduction =
        "<!-- Generated from ConfigDiagnostics.All by DiagnosticsReference.Render(). Do not edit by hand:\n" +
        "     run the Dse.Configuration tests with DSE_UPDATE_GOLDEN=1, read the result, commit it. -->\n\n" +
        "`dse validate` and `PlantLoader.Load` report every problem in a plant file as a diagnostic with four\n" +
        "parts: a **code**, a **JSON path** into the file (`$.components[3].parameters.motor.ratedPowerW`), a\n" +
        "**message** saying what is wrong, and a **fix** saying what to do. A diagnostic without a fix cannot be\n" +
        "constructed.\n\n" +
        "The loader works in stages — parse, structure, references, instantiate, wire, build — and stops at the\n" +
        "end of the first stage that found an error, having reported *every* error that stage could find. Fixing\n" +
        "what is reported may therefore reveal errors from a later stage.\n\n" +
        "A plant's `controllers` are resolved in the build stage, once the plant itself has validated: every tag\n" +
        "they name and every value they give is checked against the plant's tags and the blocks' own\n" +
        "(DSE113–DSE115) before any block is built.\n\n";

    private const string ConfigurationTrailer =
        "## DSE001–DSE015 — plant validation\n\n" +
        "Codes below DSE100 come from `SimulationBuilder.Validate()` and mean the same for a plant built in code:\n" +
        "duplicate ids, unconnected required inputs, algebraic loops, belts too fast for their cells, incompatible\n" +
        "flow links, tag conflicts — and, for a control block declared under `controllers` or attached with\n" +
        "`AddScanBlock`, a scan period that is not a positive whole number of time steps (DSE013), a pin naming a\n" +
        "tag the plant does not have, publishes with another kind or will not accept a command (DSE014), and a\n" +
        "block id or owned tag name that collides with something the plant already has (DSE015). The numbering\n" +
        "skips 012. The loader passes them through with the path of the first component involved, or of the\n" +
        "controller (for DSE013, its `scanPeriodMs`); their message is split at its first sentence into message\n" +
        "and fix. A plant file's controllers are resolved before the blocks are validated, so DSE113–DSE115\n" +
        "report what DSE014 would. See `docs/architecture.md` and `docs/control-blocks.md`.\n";

    /// <summary>docs/configuration-diagnostics.md, unchanged.</summary>
    public static string Render() =>
        Render("Configuration diagnostics", ConfigDiagnostics.All, ConfigurationIntroduction, ConfigurationTrailer);

    /// <summary>
    /// A title, an optional introduction, the table of codes, a section per
    /// code, and an optional trailer. Every part ends in a blank line, so the
    /// page is valid Markdown whichever parts are given.
    /// </summary>
    public static string Render(
        string title,
        IReadOnlyList<DiagnosticInfo> codes,
        string? introduction = null,
        string? trailer = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(codes);

        var page = new StringBuilder();
        page.Append("# ").Append(title).Append("\n\n");
        if (!string.IsNullOrEmpty(introduction))
        {
            page.Append(introduction);
        }

        page.Append("| Code | Meaning |\n|---|---|\n");
        foreach (DiagnosticInfo info in codes)
        {
            page.Append("| ").Append(info.Code).Append(" | ").Append(info.Title).Append(" |\n");
        }

        page.Append('\n');
        foreach (DiagnosticInfo info in codes)
        {
            page.Append("## ").Append(info.Code).Append(" — ").Append(info.Title).Append("\n\n");
            page.Append(info.Explanation).Append("\n\n");
        }

        if (!string.IsNullOrEmpty(trailer))
        {
            page.Append(trailer);
        }

        return page.ToString();
    }
}
