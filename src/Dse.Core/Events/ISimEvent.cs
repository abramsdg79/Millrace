namespace Dse.Core.Events;

/// <summary>Something that happens at a specific tick.</summary>
public interface ISimEvent
{
    void Apply();
}

/// <summary>An event that runs a delegate. Not serialisable; scenarios (plan 5) supply their own.</summary>
public sealed class CallbackEvent : ISimEvent
{
    private readonly Action _callback;

    public CallbackEvent(Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        _callback = callback;
    }

    public void Apply() => _callback();
}

/// <summary>An event with its ordering key. Sequence breaks ties within a tick.</summary>
public readonly record struct ScheduledEvent(long DueTick, long Sequence, ISimEvent Payload);
