namespace Millrace.Realtime;

/// <summary>
/// Receives every command the bus handles, accepted or rejected. Plan 5's
/// scenario recorder attaches here. Called on the writer's thread; must be
/// thread-safe if the bus is shared.
/// </summary>
public interface ICommandRecorder
{
    /// <summary>Records one command.</summary>
    void Record(TagCommand command);
}
