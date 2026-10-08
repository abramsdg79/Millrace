namespace Millrace.Core.Logging;

/// <summary>One discrete thing that happened, at one tick.</summary>
public sealed record SimEventRecord(
    long Tick,
    DateTimeOffset SimTime,
    string Source,
    string Code,
    string Message);
