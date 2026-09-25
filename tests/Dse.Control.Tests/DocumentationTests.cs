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

    [Fact]
    public void TheControlBlocksPageDescribesThePlantFile()
    {
        string page = Page();

        foreach (string token in new[]
                 {
                     "\"controllers\"", "scanPeriodMs", "presetS", "timeoutS", "delayS", "onDelayS",
                     "DSE113", "DSE114", "DSE115", "Set to false by INT01.", "File order is scan order",
                     "ControlModule", "Dse.Control.Catalogue",
                 })
        {
            Assert.Contains(token, page, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("cannot attach blocks", page, StringComparison.Ordinal);
    }

    /// <summary>Two levels up from this file is the repository root.</summary>
    private static string Page([CallerFilePath] string callerFile = "") =>
        File.ReadAllText(Path.Combine(Path.GetDirectoryName(callerFile)!, "..", "..", "docs", "control-blocks.md"));
}
