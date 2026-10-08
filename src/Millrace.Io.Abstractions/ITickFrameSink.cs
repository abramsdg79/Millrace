namespace Millrace.Io;

/// <summary>
/// Where the simulation hands each frame at phase 5. Called once per tick on
/// the simulation thread. <b>Must not block and must not throw</b>: the
/// implementation's whole obligation is one enqueue, and it drops rather than
/// waits.
/// </summary>
public interface ITickFrameSink
{
    /// <summary>Accepts a frame without blocking.</summary>
    void Publish(TickFrame frame);
}
