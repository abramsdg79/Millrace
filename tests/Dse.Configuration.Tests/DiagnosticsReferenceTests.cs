using Dse.Tests.Shared;

namespace Dse.Configuration.Tests;

public class DiagnosticsReferenceTests
{
    [Fact]
    public void TheCommittedReferencePageIsCurrent()
    {
        // Two levels up from this file is the repository root. Regenerate with DSE_UPDATE_GOLDEN=1.
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
            [new DiagnosticInfo("DSE900", "Something is wrong", "It went wrong.")]);

        Assert.Equal(
            "# Example diagnostics\n\n"
            + "| Code | Meaning |\n|---|---|\n| DSE900 | Something is wrong |\n\n"
            + "## DSE900 — Something is wrong\n\nIt went wrong.\n\n",
            page);
    }
}
