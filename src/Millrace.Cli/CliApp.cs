using System.Globalization;
using System.Net;
using Millrace.Components;
using Millrace.Control.Catalogue;
using Millrace.Core.Catalogue;

namespace Millrace.Cli;

/// <summary>The whole CLI as a function of its arguments and two streams, so tests run it in-process.</summary>
public static class CliApp
{
    public static int Run(string[] args, TextWriter stdout, TextWriter stderr) =>
        Run(args, stdout, stderr, CancellationToken.None);

    /// <summary>
    /// As <see cref="Run(string[], TextWriter, TextWriter)"/>; <paramref name="cancellation"/>
    /// stops a long-running command (<c>millrace serve</c>) as Ctrl+C does, so a test can.
    /// </summary>
    public static int Run(string[] args, TextWriter stdout, TextWriter stderr, CancellationToken cancellation)
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
            stderr.Write($"{problem}\nRun `millrace {string.Join(' ', parsed.Command!.Words)} --help` for its options.\n");
            return ExitCodes.Usage;
        }

        CatalogueBuilder builder = new CatalogueBuilder().Add<ComponentsModule>().Add<ControlModule>();
        if (!ModuleLoader.TryLoad(parsed.All(CommandTable.Assembly), builder, out string loadProblem))
        {
            stderr.Write(loadProblem + "\n");
            return ExitCodes.Unreadable;
        }

        return parsed.Command!.Run(new CliContext(parsed, builder.Build(), stdout, stderr, cancellation));
    }

    private static string? OptionValueProblem(ParsedCommandLine parsed)
    {
        CommandSpec command = parsed.Command!;
        if (command.Options.Contains(CommandTable.Format) && parsed.Single(CommandTable.Format) is { } format && format is not ("text" or "json"))
        {
            return $"'--format {format}' is not a format. Use text or json.";
        }

        if (command.Options.Contains(CommandTable.MapFormat) && parsed.Single(CommandTable.MapFormat) is { } mapFormat && mapFormat is not ("text" or "csv" or "fuxa"))
        {
            return $"'--format {mapFormat}' is not a map format. Use text, csv or fuxa.";
        }

        if (parsed.Single(CommandTable.Port) is { } port
            && !(int.TryParse(port, NumberStyles.None, CultureInfo.InvariantCulture, out int number) && number <= 65535))
        {
            return $"'--port {port}' is not a port. Give a whole number from 0 to 65535, such as 5020.";
        }

        if (parsed.Single(CommandTable.Speed) is { } speed
            && !(double.TryParse(speed, NumberStyles.Float, CultureInfo.InvariantCulture, out double factor) && double.IsFinite(factor) && factor >= 0.001))
        {
            return $"'--speed {speed}' is not a speed. Give a factor of at least 0.001, such as 10 or 0.5.";
        }

        if (parsed.Single(CommandTable.Bind) is { } bind && !IPAddress.TryParse(bind, out _))
        {
            return $"'--bind {bind}' is not an IP address. Give an address such as 127.0.0.1 or 0.0.0.0.";
        }

        if (parsed.Single(CommandTable.TimeStep) is { } step
            && !(double.TryParse(step, NumberStyles.Float, CultureInfo.InvariantCulture, out double ms) && double.IsFinite(ms) && ms > 0.0))
        {
            return $"'--time-step {step}' is not a step. Give milliseconds greater than zero, such as 10 or 0.5.";
        }

        return null;
    }
}
