using Millrace.Cli;

namespace Millrace.Samples.Tests;

/// <summary>What one in-process run of the CLI printed, and how it exited.</summary>
internal sealed record CliRun(int ExitCode, string Out, string Err);

/// <summary>Runs the CLI in-process, as <c>Millrace.Cli.Tests</c> does, and captures both streams.</summary>
internal static class Cli
{
    public static CliRun Run(params string[] args)
    {
        using var stdout = new StringWriter { NewLine = "\n" };
        using var stderr = new StringWriter { NewLine = "\n" };
        int exit = CliApp.Run(args, stdout, stderr);
        return new CliRun(exit, stdout.ToString(), stderr.ToString());
    }
}
