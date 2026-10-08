namespace Millrace.Configuration;

/// <summary>What a diagnostic code means, for the reference page and <c>--help</c>.</summary>
public sealed record DiagnosticInfo(string Code, string Title, string Explanation);
