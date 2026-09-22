namespace Dse.Cli;

/// <summary>An option a command accepts. Every option takes a value.</summary>
internal sealed record OptionSpec(string Name, string ValueName, string Help, bool Repeatable = false);

/// <summary>One command: how it is invoked, what it accepts, what runs it.</summary>
internal sealed record CommandSpec(
    string[] Words,
    string? Argument,
    string Summary,
    OptionSpec[] Options,
    Func<CliContext, int> Run)
{
    public string Invocation => string.Join(' ', Words) + (Argument is null ? string.Empty : $" <{Argument}>");
}

/// <summary>The single source of commands, options and help text.</summary>
internal static class CommandTable
{
    public static readonly OptionSpec Out = new("--out", "file", "Write the output to this file instead of standard output.");
    public static readonly OptionSpec Format = new("--format", "text|json", "How to print results. Default: text.");
    public static readonly OptionSpec TimeStep = new("--time-step", "ms", "Simulation step in milliseconds, overriding the plant's defaults.");
    public static readonly OptionSpec Assembly = new(
        "--assembly", "path", "Load catalogue modules from this assembly, in addition to the shipped components.", Repeatable: true);

    public static IReadOnlyList<CommandSpec> All { get; } =
    [
        new(["catalog", "export"], null, "Print every component, transform, hold and material type as JSON.", [Out, Assembly], Commands.CatalogExport.Run),
        new(["schema", "export"], null, "Print the JSON Schema for plant files, generated from the catalogue.", [Out, Assembly], Commands.SchemaExport.Run),
        new(["validate"], "plant.json", "Load a plant and report every error, each with its fix.", [Format, TimeStep, Assembly], Commands.Validate.Run),
        new(["tags"], "plant.json", "Load and build a plant, then list its tags: name, kind, access, unit, range.", [Format, TimeStep, Assembly], Commands.Tags.Run),
    ];

    public static string GeneralHelp()
    {
        var lines = new List<string> { "dse — deterministic industrial process simulation engine", string.Empty, "Commands:" };
        int width = All.Max(c => c.Invocation.Length);
        foreach (CommandSpec command in All)
        {
            lines.Add($"  {command.Invocation.PadRight(width)}  {command.Summary}");
        }

        lines.Add(string.Empty);
        lines.Add("Run `dse <command> --help` for a command's options.");
        lines.Add(string.Empty);
        lines.Add("Exit codes: 0 success; 1 the plant has errors; 2 usage error; 3 a file or assembly could not be read.");
        return string.Join('\n', lines) + "\n";
    }

    public static string HelpFor(CommandSpec command)
    {
        var lines = new List<string> { $"dse {command.Invocation} [options]", string.Empty, command.Summary, string.Empty, "Options:" };
        int width = command.Options.Max(o => o.Name.Length + o.ValueName.Length + 3);
        foreach (OptionSpec option in command.Options)
        {
            lines.Add($"  {$"{option.Name} <{option.ValueName}>".PadRight(width)}  {option.Help}{(option.Repeatable ? " May be repeated." : string.Empty)}");
        }

        return string.Join('\n', lines) + "\n";
    }
}
