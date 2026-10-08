using System.Runtime.CompilerServices;

namespace Millrace.Tests.Shared;

/// <summary>
/// Compares text with a committed file beside the calling test. Set the
/// environment variable MILLRACE_UPDATE_GOLDEN=1 to write the file instead (R42);
/// read what it wrote before committing it.
/// </summary>
internal static class Golden
{
    public static void Assert(string relativePath, string actual, [CallerFilePath] string callerFile = "")
    {
        string path = Path.Combine(Path.GetDirectoryName(callerFile)!, relativePath);
        string normalised = actual.ReplaceLineEndings("\n");

        if (Environment.GetEnvironmentVariable("MILLRACE_UPDATE_GOLDEN") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, normalised);
            return;
        }

        Xunit.Assert.True(File.Exists(path), $"Golden file '{path}' does not exist. Run the test once with MILLRACE_UPDATE_GOLDEN=1, read the file, commit it.");
        string expected = File.ReadAllText(path).ReplaceLineEndings("\n");
        if (!string.Equals(expected, normalised, StringComparison.Ordinal))
        {
            File.WriteAllText(path + ".actual", normalised);
            Xunit.Assert.Fail($"Output differs from golden file '{path}'. The new output is beside it as '.actual'; diff the two.");
        }
    }
}
