using System.Runtime.CompilerServices;

namespace Dse.Control.Tests;

public class DocumentationTests
{
    [Fact]
    public void TheControlBlocksPageNamesEveryBlockEveryEventAndEveryDiagnostic()
    {
        string page = Page();

        foreach (string token in new[]
                 {
                     "Timer", "Permissive", "Interlock", "Alarm", "Sequencer",
                     "PERMISSIVE_LOST", "PERMISSIVE_OK",
                     "INTERLOCK_TRIP", "INTERLOCK_RESET",
                     "ALARM_RAISED", "ALARM_CLEARED", "ALARM_ACKED",
                     "STEP_ENTERED", "SEQUENCE_COMPLETE", "SEQUENCE_FAULTED", "SEQUENCE_ABORTED",
                     "DSE013", "DSE014", "DSE015",
                     "AddScanBlock", "IScanBlock", "ScanInputs", "ScanOutputs",
                 })
        {
            Assert.Contains(token, page, StringComparison.Ordinal);
        }

        Assert.DoesNotContain('\r', page);
    }

    /// <summary>Two levels up from this file is the repository root.</summary>
    private static string Page([CallerFilePath] string callerFile = "") =>
        File.ReadAllText(Path.Combine(Path.GetDirectoryName(callerFile)!, "..", "..", "docs", "control-blocks.md"));
}
