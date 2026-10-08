using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace Millrace.Control.Tests;

public partial class DocumentationTests
{
    [GeneratedRegex(@"^\d{2}:\d{2}:\d{2}\.\d{3}  ")]
    private static partial Regex LogLine();

    [Fact]
    public void TheControlBlocksPageNamesEveryBlockEveryEventAndEveryDiagnostic()
    {
        string page = Page();

        foreach (string token in new[]
                 {
                     "Timer", "Permissive", "Interlock", "Alarm", "Sequencer",
                     "PERMISSIVE_LOST", "PERMISSIVE_OK",
                     "INTERLOCK_TRIP", "INTERLOCK_RESET", "RESET_REFUSED",
                     "Reset refused: PERM01.Ok is not normal.",
                     "ALARM_RAISED", "ALARM_CLEARED", "ALARM_ACKED",
                     "STEP_ENTERED", "SEQUENCE_COMPLETE", "SEQUENCE_FAULTED", "SEQUENCE_ABORTED",
                     "MR013", "MR014", "MR015",
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
                     "MR113", "MR114", "MR115", "Set to false by INT01.", "File order is scan order",
                     "ControlModule", "Millrace.Control.Catalogue",
                 })
        {
            Assert.Contains(token, page, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("cannot attach blocks", page, StringComparison.Ordinal);
    }

    [Fact]
    public void TheControlBlocksPageDescribesTheResetWritesAndTheRunPermit()
    {
        string page = Page();

        foreach (string token in new[]
                 {
                     "\"reset\"", "CV001.Permit", "run permit", "seal-in", "Set to true by INT01.", "start-while-tripped",
                 })
        {
            Assert.Contains(token, page, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("does not hold its output", page, StringComparison.Ordinal);
    }

    [Fact]
    public void TheControlBlocksPageDescribesClaimedTags()
    {
        string page = Page();

        foreach (string token in new[]
                 {
                     "### Claiming a tag", "\"claims\": [ \"CV001.Permit\" ]", "AddScanBlock(interlock, [\"CV001.Permit\"])",
                     "ClaimedBy", "claimed by INT01", "Tag 'CV001.Permit' is claimed by INT01; only that block writes it.",
                     "MR016", "duplicate-coil", "it may also list `claims` (*Claiming a tag*, below)",
                 })
        {
            Assert.Contains(token, page, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("The permit is an ordinary, writable tag", page, StringComparison.Ordinal);
    }

    [Fact]
    public void TheControlBlocksPageDescribesTheCoil()
    {
        string page = Page();

        foreach (string token in new[]
                 {
                     "## `Coil`", "new Coil(", "\"type\": \"coil\"", "\"claims\": [ \"GATE.Reject\" ]", "Energised",
                     "on its first scan and on every scan", "Set to true by COIL01.", "carries six of them", "registers the six below",
                 })
        {
            Assert.Contains(token, page, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("carries five of them", page, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryLogLineThePageQuotesIsAWholeLineOfTheWorkedExampleGolden()
    {
        HashSet<string> golden = [.. File.ReadAllText(Golden()).ReplaceLineEndings("\n").Split('\n')];
        string[] quoted = Page().Split('\n').Where(l => LogLine().IsMatch(l)).ToArray();

        Assert.Equal(3, quoted.Length);
        Assert.All(quoted, line => Assert.True(golden.Contains(line), $"The page quotes a line the golden does not log: '{line}'."));
    }

    /// <summary>Two levels up from this file is the repository root.</summary>
    private static string Page([CallerFilePath] string callerFile = "") =>
        File.ReadAllText(Path.Combine(Path.GetDirectoryName(callerFile)!, "..", "..", "docs", "control-blocks.md"));

    /// <summary>The worked example's golden, beside this file.</summary>
    private static string Golden([CallerFilePath] string callerFile = "") =>
        Path.Combine(Path.GetDirectoryName(callerFile)!, "Golden", "conveyor-control.log");
}
