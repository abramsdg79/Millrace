using System.Globalization;
using Dse.Components;
using Dse.Control.Catalogue;
using Dse.Core.Catalogue;

namespace Dse.Cli;

/// <summary>The whole CLI as a function of its arguments and two streams, so tests run it in-process.</summary>
public static class CliApp
{
    public static int Run(string[] args, TextWriter stdout, TextWriter stderr)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(stdout);
        ArgumentNullException.ThrowIfNull(stderr);

        ParsedCommandLine parsed = CommandLine.Parse(args);
        if (parsed.Error is not null)
        {
            stderr.Write(parsed.Error + "\n");
            return ExitCodes.Usage;
        }

        if (parsed.HelpRequested)
        {
            stdout.Write(parsed.Command is null ? CommandTable.GeneralHelp() : CommandTable.HelpFor(parsed.Command));
            return ExitCodes.Ok;
        }

        if (OptionValueProblem(parsed) is { } problem)
        {
            stderr.Write($"{problem}\nRun `dse {string.Join(' ', parsed.Command!.Words)} --help` for its options.\n");
            return ExitCodes.Usage;
        }

        CatalogueBuilder builder = new CatalogueBuilder().Add<ComponentsModule>().Add<ControlModule>();
        if (!ModuleLoader.TryLoad(parsed.All(CommandTable.Assembly), builder, out string loadProblem))
        {
            stderr.Write(loadProblem + "\n");
            return ExitCodes.Unreadable;
        }

        return parsed.Command!.Run(new CliContext(parsed, builder.Build(), stdout, stderr));
    }

    private static string? OptionValueProblem(ParsedCommandLine parsed)
    {
        if (parsed.Single(CommandTable.Format) is { } format && format is not ("text" or "json"))
        {
            return $"'--format {format}' is not a format. Use text or json.";
        }

        if (parsed.Single(CommandTable.TimeStep) is { } step
            && !(double.TryParse(step, NumberStyles.Float, CultureInfo.InvariantCulture, out double ms) && double.IsFinite(ms) && ms > 0.0))
        {
            return $"'--time-step {step}' is not a step. Give milliseconds greater than zero, such as 10 or 0.5.";
        }

        return null;
    }
}
