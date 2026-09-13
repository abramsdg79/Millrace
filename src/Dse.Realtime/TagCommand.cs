using Dse.Io;

namespace Dse.Realtime;

/// <summary>One inbound write and what became of it.</summary>
public sealed record TagCommand(string Tag, TagValue Value, CommandOutcome Outcome);
