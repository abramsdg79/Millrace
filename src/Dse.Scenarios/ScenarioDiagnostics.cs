using System.Globalization;
using Dse.Configuration;

namespace Dse.Scenarios;

/// <summary>
/// Every scenario diagnostic code. The reference page in docs/ is generated
/// from <see cref="All"/>, so the page cannot drift from the codes.
/// </summary>
public static class ScenarioDiagnostics
{
    /// <summary>The scenario file could not be parsed.</summary>
    public const string Syntax = "DSE200";

    /// <summary>A key the format does not define.</summary>
    public const string UnknownKey = "DSE201";

    /// <summary>A value is missing, of the wrong type, or out of range.</summary>
    public const string BadValue = "DSE202";

    /// <summary>A time is not on a tick, or an action is not before the end of the run.</summary>
    public const string BadTime = "DSE203";

    /// <summary>An action does not have exactly one shape with its required companions.</summary>
    public const string BadAction = "DSE204";

    /// <summary>The plant the scenario names has errors of its own.</summary>
    public const string PlantInvalid = "DSE205";

    /// <summary>An action names something the plant does not have.</summary>
    public const string DoesNotBind = "DSE206";

    /// <summary>The codes, in order, with what each means.</summary>
    public static IReadOnlyList<DiagnosticInfo> All { get; } =
    [
        new(Syntax, "Scenario is not valid JSON",
            "The text could not be parsed. Comments and trailing commas are allowed; everything else must be strict JSON. The message gives the line and column."),
        new(UnknownKey, "Unknown key",
            "An object has a key the format does not define. Keys match exactly, including case. Nothing is ignored silently, so a misspelt key cannot quietly do nothing."),
        new(BadValue, "Value missing, of the wrong type, or out of range",
            "A required value is absent, has the wrong JSON type, or is outside its range: no \"plant\", no \"duration\", a duration of zero or less, a negative seed, a start time without an offset, a fault argument that is not a number, or a write value that is neither a JSON boolean nor a number."),
        new(BadTime, "Time is not on a tick, or not before the end",
            "A time is not a whole number of time steps, or an action is scheduled at or after the end of the run. A run of 120 s at 10 ms runs ticks 0 to 11999, so an action at 120 s would never fire."),
        new(BadAction, "Action is malformed",
            "An action does not have exactly one of \"write\", \"fault\" and \"clear\", or lacks a companion that shape requires, or carries one that belongs to another shape."),
        new(PlantInvalid, "Plant file is invalid",
            "The plant the scenario names has errors of its own. This diagnostic names the plant; the plant's own diagnostics follow it unchanged, with their codes, their paths and their fixes. A plant file that cannot be read at all is the command line's exit code 3, not a diagnostic."),
        new(DoesNotBind, "Action does not bind to the plant",
            "An action names a tag, a component, a fault or a fault argument the plant does not have, or writes a tag that is read-only or of another kind. Every action is bound before tick 0, so a bad scenario never produces a partial log."),
    ];

    internal static ConfigDiagnostic Error(string code, string path, string message, string fix) =>
        new(code, DiagnosticSeverity.Error, path, message, fix);

    /// <summary>A time in whole seconds and fractions, for a message. Exact: the tick count divided by a constant.</summary>
    internal static double Seconds(TimeSpan time) => time.Ticks / (double)TimeSpan.TicksPerSecond;

    /// <summary><paramref name="what"/> is a capitalised noun phrase: "The duration", "The action time".</summary>
    internal static ConfigDiagnostic OffTick(string path, string what, TimeSpan time, TimeSpan step) => Error(
        BadTime,
        path,
        string.Create(CultureInfo.InvariantCulture, $"{what} {Seconds(time)} s is not a whole number of {step.TotalMilliseconds} ms steps."),
        "Move it to a multiple of the time step, or change the step.");

    internal static ConfigDiagnostic NotBeforeTheEnd(string path, TimeSpan at, TimeSpan duration) => Error(
        BadTime,
        path,
        string.Create(CultureInfo.InvariantCulture, $"An action at {Seconds(at)} s is not before the end of the run at {Seconds(duration)} s."),
        "Move the action earlier, or extend \"duration\"; the last tick of the run starts one step before the end.");
}
