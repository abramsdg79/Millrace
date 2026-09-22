namespace Dse.Cli.Tests;

/// <summary>Runs the CLI in-process and captures both streams.</summary>
internal sealed record CliRun(int ExitCode, string Out, string Err);

internal static class Cli
{
    public static string Plant(string name) => Path.Combine(AppContext.BaseDirectory, "Plants", name);

    public static CliRun Run(params string[] args)
    {
        using var stdout = new StringWriter { NewLine = "\n" };
        using var stderr = new StringWriter { NewLine = "\n" };
        int exit = CliApp.Run(args, stdout, stderr);
        return new CliRun(exit, stdout.ToString(), stderr.ToString());
    }
}
