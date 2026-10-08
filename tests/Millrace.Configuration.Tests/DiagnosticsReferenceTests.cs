using Millrace.Tests.Shared;

namespace Millrace.Configuration.Tests;

public class DiagnosticsReferenceTests
{
    [Fact]
    public void TheCommittedReferencePageIsCurrent()
    {
        // Two levels up from this file is the repository root. Regenerate with MILLRACE_UPDATE_GOLDEN=1.
        Golden.Assert("../../docs/configuration-diagnostics.md", DiagnosticsReference.Render());
    }

    [Fact]
    public void EveryCodeHasASection()
    {
        string page = DiagnosticsReference.Render();

        Assert.All(ConfigDiagnostics.All, d => Assert.Contains($"## {d.Code} — {d.Title}\n", page, StringComparison.Ordinal));
        Assert.StartsWith("# Configuration diagnostics\n", page, StringComparison.Ordinal);
        Assert.EndsWith("\n", page, StringComparison.Ordinal);
        Assert.DoesNotContain('\r', page);
    }

    [Fact]
    public void APageMayBeRenderedFromNothingButATitleAndCodes()
    {
        string page = DiagnosticsReference.Render(
            "Example diagnostics",
            [new DiagnosticInfo("MR900", "Something is wrong", "It went wrong.")]);

        Assert.Equal(
            "# Example diagnostics\n\n"
            + "| Code | Meaning |\n|---|---|\n| MR900 | Something is wrong |\n\n"
            + "## MR900 — Something is wrong\n\nIt went wrong.\n\n",
            page);
    }

    [Fact]
    public void ThePlantValidationTrailerCoversTheBlockCodes()
    {
        string page = DiagnosticsReference.Render();

        Assert.Contains("## MR001–MR016 — plant validation\n", page, StringComparison.Ordinal);
        Assert.Contains("MR013", page, StringComparison.Ordinal);
        Assert.Contains("MR014", page, StringComparison.Ordinal);
        Assert.Contains("MR015", page, StringComparison.Ordinal);
        Assert.Contains("MR016", page, StringComparison.Ordinal);
        Assert.Contains("the `claims` entry it is about", page, StringComparison.Ordinal);

        // Plan 5a ruled there is no MR012, and the page must never print it as
        // though it were a code. The trailer says "The numbering skips 012."
        Assert.DoesNotContain("MR012", page, StringComparison.Ordinal);
    }
}
