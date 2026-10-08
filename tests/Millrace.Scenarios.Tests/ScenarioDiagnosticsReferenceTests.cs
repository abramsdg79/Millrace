using Millrace.Tests.Shared;

namespace Millrace.Scenarios.Tests;

public class ScenarioDiagnosticsReferenceTests
{
    [Fact]
    public void TheCommittedReferencePageIsCurrent()
    {
        // Two levels up from this file is the repository root. Regenerate with MILLRACE_UPDATE_GOLDEN=1.
        Golden.Assert("../../docs/scenario-diagnostics.md", ScenarioDiagnosticsReference.Render());
    }

    [Fact]
    public void EveryCodeHasASection()
    {
        string page = ScenarioDiagnosticsReference.Render();

        Assert.All(ScenarioDiagnostics.All, d => Assert.Contains($"## {d.Code} — {d.Title}\n", page, StringComparison.Ordinal));
        Assert.StartsWith("# Scenario diagnostics\n", page, StringComparison.Ordinal);
        Assert.Contains("| MR206 | Action does not bind to the plant |\n", page, StringComparison.Ordinal);
        Assert.EndsWith("\n", page, StringComparison.Ordinal);
        Assert.DoesNotContain('\r', page);
    }
}
