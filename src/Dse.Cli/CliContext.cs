using System.Globalization;
using Dse.Core.Catalogue;

namespace Dse.Cli;

/// <summary>What a command runs with.</summary>
internal sealed class CliContext(
    ParsedCommandLine commandLine, ComponentCatalogue catalogue, TextWriter stdout, TextWriter stderr, CancellationToken cancellation = default)
{
    public ParsedCommandLine CommandLine { get; } = commandLine;

    public ComponentCatalogue Catalogue { get; } = catalogue;

    public TextWriter Out { get; } = stdout;

    public TextWriter Err { get; } = stderr;

    /// <summary>Cancelled when the caller wants a long-running command to stop, as Ctrl+C does.</summary>
    public CancellationToken Cancellation { get; } = cancellation;

    public bool Json => CommandLine.Single(CommandTable.Format) == "json";

    public TimeSpan? TimeStep =>
        CommandLine.Single(CommandTable.TimeStep) is { } text
            ? TimeSpan.FromMilliseconds(double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture))
            : null;

    /// <summary>Writes a payload to <c>--out</c> or to standard output. Returns an exit code.</summary>
    public int Emit(string payload)
    {
        string? path = CommandLine.Single(CommandTable.Out);
        if (path is null)
        {
            Out.Write(payload);
            return ExitCodes.Ok;
        }

        try
        {
            File.WriteAllText(path, payload);
            return ExitCodes.Ok;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            Err.Write($"Cannot write '{path}': {ex.Message}\n");
            return ExitCodes.Unreadable;
        }
    }
}
