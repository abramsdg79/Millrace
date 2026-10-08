using Millrace.Io;

namespace Millrace.Realtime;

/// <summary>One inbound write and what became of it.</summary>
public sealed record TagCommand(string Tag, TagValue Value, CommandOutcome Outcome);
