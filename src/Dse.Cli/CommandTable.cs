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
        "--assembly", "path", "Load catalogue modules from this assembly, in addition to the shipped components and control blocks.", Repeatable: true);

    public static readonly OptionSpec Expect = new(
        "--expect", "golden.log", "Compare the event log with this file; exit 4 if they differ.");

    public static readonly OptionSpec Scenario = new(
        "--scenario", "scenario.json", "Schedule this scenario's timeline and use its seed, start time and time step. It must name the same plant. serve runs until stopped; the scenario's duration is ignored.");

    public static readonly OptionSpec Bind = new(
        "--bind", "address", "Address to listen on; default 127.0.0.1 (this machine only). Use 0.0.0.0 to listen on every interface — Modbus has no authentication.");

    public static readonly OptionSpec Port = new("--port", "n", "Serve Modbus TCP on this port; 0 lets the system choose a free one. Default: 5020.");

    public static readonly OptionSpec Speed = new("--speed", "x", "Run this many times faster than real time, such as 10 or 0.5. Default: 1.");

    public static readonly OptionSpec MapFormat = new(
        "--format", "text|csv|fuxa", "How to print the map: a table, CSV, or the tags object of a FUXA Modbus device. Default: text.");

    public static IReadOnlyList<CommandSpec> All { get; } =
    [
        new(["catalog", "export"], null, "Print every component, block, transform, transition, hold and material type as JSON.", [Out, Assembly], Commands.CatalogExport.Run),
        new(["schema", "export"], null, "Print the JSON Schema for plant files, generated from the catalogue.", [Out, Assembly], Commands.SchemaExport.Run),
        new(["validate"], "plant.json", "Load a plant and report every error, each with its fix.", [Format, TimeStep, Assembly], Commands.Validate.Run),
        new(["tags"], "plant.json", "Load and build a plant, then list its tags: name, kind, access, unit, range, description and claimant.", [Format, TimeStep, Assembly], Commands.Tags.Run),
        new(["run"], "scenario.json", "Run a scenario against its plant and print the event log.", [Expect, Out, Format, Assembly], Commands.RunScenario.Run),
        new(["serve"], "plant.json", "Run a plant in real time and serve its tags over Modbus TCP until Ctrl+C.", [Scenario, Port, Bind, Speed, Assembly], Commands.Serve.Run),
        new(["modbus-map"], "plant.json", "Print the plant's Modbus register map: area, address, type and tag of every tag.", [MapFormat, Out, Assembly], Commands.ModbusMap.Run),
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
        lines.Add("Exit codes: 0 success; 1 the plant or scenario has errors; 2 usage error; " +
                  "3 a file or assembly could not be read, or the port could not be opened; 4 the event log differs from --expect.");
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
