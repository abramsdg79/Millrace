using Dse.Core.Events;
using Xunit;

namespace Dse.Core.Tests;

public class EventQueueTests
{
    private static CallbackEvent Named(List<string> sink, string name) =>
        new(() => sink.Add(name));

    [Fact]
    public void DequeuesInDueTickOrderRegardlessOfScheduleOrder()
    {
        var fired = new List<string>();
        var queue = new EventQueue();

        queue.Schedule(30, Named(fired, "third"));
        queue.Schedule(10, Named(fired, "first"));
        queue.Schedule(20, Named(fired, "second"));

        DrainThrough(queue, 30);

        Assert.Equal(new[] { "first", "second", "third" }, fired);
    }

    [Fact]
    public void EventsOnTheSameTickFireInScheduleOrder()
    {
        var fired = new List<string>();
        var queue = new EventQueue();

        queue.Schedule(5, Named(fired, "a"));
        queue.Schedule(5, Named(fired, "b"));
        queue.Schedule(5, Named(fired, "c"));

        DrainThrough(queue, 5);

        Assert.Equal(new[] { "a", "b", "c" }, fired);
    }

    [Fact]
    public void OverdueEventsFireOnTheNextDrain()
    {
        var fired = new List<string>();
        var queue = new EventQueue();
        queue.Schedule(3, Named(fired, "missed"));

        // Nothing drained at tick 3; drain at tick 9 must still deliver it.
        DrainThrough(queue, 9);

        Assert.Equal(new[] { "missed" }, fired);
    }

    [Fact]
    public void DoesNotDequeueEventsThatAreNotYetDue()
    {
        var queue = new EventQueue();
        queue.Schedule(100, new CallbackEvent(() => { }));

        Assert.False(queue.TryDequeueDue(99, out _));
        Assert.Equal(1, queue.Count);
    }

    [Fact]
    public void SequenceNumbersAreMonotonic()
    {
        var queue = new EventQueue();

        long first = queue.Schedule(1, new CallbackEvent(() => { }));
        long second = queue.Schedule(1, new CallbackEvent(() => { }));

        Assert.True(second > first);
    }

    [Fact]
    public void RejectsNegativeDueTick()
    {
        var queue = new EventQueue();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => queue.Schedule(-1, new CallbackEvent(() => { })));
    }

    private static void DrainThrough(EventQueue queue, long tick)
    {
        while (queue.TryDequeueDue(tick, out ScheduledEvent scheduled))
        {
            scheduled.Payload.Apply();
        }
    }
}
