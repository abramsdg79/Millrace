namespace Millrace.Io;

/// <summary>
/// One thing a scan block wants in the event log. The host records it with the
/// block's id as the source and the tick the scan ran on. The message is a
/// sentence ending in a full stop.
/// </summary>
/// <param name="Code">A short upper-case code, such as <c>INTERLOCK_TRIP</c>.</param>
/// <param name="Message">A sentence ending in a full stop.</param>
public readonly record struct BlockEvent(string Code, string Message);
