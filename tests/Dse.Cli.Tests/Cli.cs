namespace Dse.Cli.Tests;

/// <summary>Runs the CLI in-process and captures both streams.</summary>
internal sealed record CliRun(int ExitCode, string Out, string Err);

internal static class Cli
{
    public static string Plant(string name) => Path.Combine(AppContext.BaseDirectory, "Plants", name);

    public static string Scenario(string name) => Path.Combine(AppContext.BaseDirectory, "Scenarios", name);

    public static string Golden(string name) => Path.Combine(AppContext.BaseDirectory, "Golden", name);

    /// <summary>tests/&lt;project&gt;/bin/&lt;configuration&gt;/&lt;tfm&gt;/&lt;project&gt;.dll, found from this assembly's own output directory.</summary>
    public static string Built(string project)
    {
        var output = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        string tfm = output.Name;
        string configuration = output.Parent!.Name;
        string tests = output.Parent.Parent!.Parent!.Parent!.FullName;
        return Path.Combine(tests, project, "bin", configuration, tfm, project + ".dll");
    }

    public static CliRun Run(params string[] args)
    {
        using var stdout = new StringWriter { NewLine = "\n" };
        using var stderr = new StringWriter { NewLine = "\n" };
        int exit = CliApp.Run(args, stdout, stderr);
        return new CliRun(exit, stdout.ToString(), stderr.ToString());
    }
}
