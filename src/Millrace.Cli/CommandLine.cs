namespace Millrace.Cli;

/// <summary>What a command line came to: a command with its argument and options, a request for help, or a usage error.</summary>
internal sealed class ParsedCommandLine
{
    public CommandSpec? Command { get; init; }

    public bool HelpRequested { get; init; }

    public string? Argument { get; init; }

    public Dictionary<string, List<string>> Options { get; } = new(StringComparer.Ordinal);

    /// <summary>Non-null means a usage error; nothing else is meaningful.</summary>
    public string? Error { get; init; }

    public string? Single(OptionSpec option) => Options.TryGetValue(option.Name, out List<string>? values) ? values[0] : null;

    public IReadOnlyList<string> All(OptionSpec option) => Options.TryGetValue(option.Name, out List<string>? values) ? values : [];
}

internal static class CommandLine
{
    private static readonly string[] HelpWords = ["help", "--help", "-h"];

    public static ParsedCommandLine Parse(string[] args)
    {
        if (args.Length == 0 || (args.Length == 1 && HelpWords.Contains(args[0], StringComparer.Ordinal)))
        {
            return new ParsedCommandLine { HelpRequested = true };
        }

        CommandSpec? command = CommandTable.All
            .Where(c => args.Length >= c.Words.Length && c.Words.SequenceEqual(args.Take(c.Words.Length), StringComparer.Ordinal))
            .OrderByDescending(c => c.Words.Length)
            .FirstOrDefault();
        if (command is null)
        {
            return Fail($"'{string.Join(' ', args.TakeWhile(a => !a.StartsWith('-')))}' is not a millrace command.");
        }

        var parsed = new List<(string Name, string Value)>();
        var positionals = new List<string>();
        for (int i = command.Words.Length; i < args.Length; i++)
        {
            string arg = args[i];
            if (arg is "--help" or "-h")
            {
                return new ParsedCommandLine { Command = command, HelpRequested = true };
            }

            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                positionals.Add(arg);
                continue;
            }

            int equals = arg.IndexOf('=', StringComparison.Ordinal);
            string name = equals < 0 ? arg : arg[..equals];
            OptionSpec? option = command.Options.FirstOrDefault(o => string.Equals(o.Name, name, StringComparison.Ordinal));
            if (option is null)
            {
                return Fail($"'{name}' is not an option of `millrace {string.Join(' ', command.Words)}`. It accepts: {string.Join(", ", command.Options.Select(o => o.Name))}.", command);
            }

            string value;
            if (equals >= 0)
            {
                value = arg[(equals + 1)..];
            }
            else if (i + 1 < args.Length)
            {
                value = args[++i];
            }
            else
            {
                return Fail($"'{name}' needs a value: {name} <{option.ValueName}>.", command);
            }

            if (!option.Repeatable && parsed.Any(p => p.Name == name))
            {
                return Fail($"'{name}' was given twice.", command);
            }

            parsed.Add((name, value));
        }

        int expected = command.Argument is null ? 0 : 1;
        if (positionals.Count != expected)
        {
            return Fail(
                expected == 0
                    ? $"`millrace {string.Join(' ', command.Words)}` takes no argument, but got '{positionals[0]}'."
                    : positionals.Count == 0
                        ? $"`millrace {string.Join(' ', command.Words)}` needs a <{command.Argument}>."
                        : $"`millrace {string.Join(' ', command.Words)}` takes one <{command.Argument}>, but got {positionals.Count}.",
                command);
        }

        var result = new ParsedCommandLine { Command = command, Argument = positionals.FirstOrDefault() };
        foreach ((string name, string value) in parsed)
        {
            if (!result.Options.TryGetValue(name, out List<string>? values))
            {
                result.Options[name] = values = [];
            }

            values.Add(value);
        }

        return result;
    }

    private static ParsedCommandLine Fail(string problem, CommandSpec? command = null) => new()
    {
        Error = command is null
            ? $"{problem}\nRun `millrace help` to list the commands."
            : $"{problem}\nRun `millrace {string.Join(' ', command.Words)} --help` for its options.",
    };
}
