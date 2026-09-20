namespace Dse.Configuration;

/// <summary>
/// One thing wrong with a plant file: a code, where it is (a JSON path), what
/// is wrong, and what to do. The fix is not optional — an error that does not
/// name its fix cannot be constructed.
/// </summary>
public sealed record ConfigDiagnostic
{
    public ConfigDiagnostic(string code, DiagnosticSeverity severity, string path, string message, string fix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        ArgumentException.ThrowIfNullOrWhiteSpace(fix);
        Code = code;
        Severity = severity;
        Path = path;
        Message = message;
        Fix = fix;
    }

    public string Code { get; }

    public DiagnosticSeverity Severity { get; }

    /// <summary>A JSON path from the document root: <c>$.components[3].parameters.motor.ratedPowerW</c>.</summary>
    public string Path { get; }

    public string Message { get; }

    public string Fix { get; }

    /// <summary>Three lines, <c>\n</c>-separated, no trailing newline.</summary>
    public string ToText() => $"{Code} {Path}\n  {Message}\n  Fix: {Fix}";
}
